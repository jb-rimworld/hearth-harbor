// SPDX-License-Identifier: MIT
// F06 + F16 (1.1.0): the night bell is a real call. A guest who rings it makes a sound and a swinging bell appears; colonists who work in the
// Serving work type, are within hearing range (scaled by their Hearing capacity, walls and closed doors muffle it) and are not busy with something vital
// hurry to the venue. A sleeping volunteer can be woken (once per half a day, with a small mood cost), so the guest is not left waiting.
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace Hearth.ShopsFood
{
    public enum BellWakes { Off = 0, AwakeOnly = 1, WakeVolunteers = 2 }
    public enum BellWho { ServingOnly = 0, AnyColonist = 1 }

    [DefOf]
    public static class HHBellDefOf
    {
        public static SoundDef HH_NightBell;
        public static SoundDef HH_BellAnswered;
        public static ThingDef HH_Mote_Bell;
        public static ThingDef HH_Mote_RingArc;
        public static ThoughtDef HH_WokenByBell;
        static HHBellDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(HHBellDefOf)); }
    }

    /// <summary>A bell mote: it hangs above the guest (or the sign) and swings like a hand bell that has just been struck.</summary>
    public class MoteBell : Mote
    {
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            float t = AgeSecs;
            exactRotation = 26f * Mathf.Sin(t * 21f) * Mathf.Exp(-t * 1.5f);
            base.DrawAt(drawLoc, flip);
        }
    }

    public static class NightBellSettings
    {
        public const string Wakes = "venues.bellWakes";
        public const string Who = "venues.bellWho";
        public const string Range = "venues.bellRange";
        public const string Responders = "venues.bellResponders";
        public const string WakeCooldownHours = "venues.bellWakeCooldownHours";
        public const string Hurry = "venues.bellHurry";

        public static void Register()
        {
            const ModuleId M = ModuleId.ShopsFood;
            HSettings.Enum(M, "Staffing", Wakes, BellWakes.WakeVolunteers, "Night bell: who is called",
                "Off: the bell only makes a sound, colonists notice it when their work lets them. Awake only: awake colonists within hearing range hurry to the venue. Wake volunteers: also colonists who sleep and have the Serving work set to priority 1 or 2 (or with simple work settings), at most once per half a day each, with a small mood cost.");
            HSettings.Enum(M, "Staffing", Who, BellWho.ServingOnly, "Night bell: who may answer",
                "Serving only: colonists with the Serving work type switched on. Any colonist: every colonist who is able to serve.");
            HSettings.Int(M, "Staffing", Range, 28, 10, 60, "Night bell hearing range (cells)",
                "How far the bell carries for someone with normal hearing. Walls and closed doors in between shorten it; poor hearing shortens it a lot, deaf colonists never hear it.", hardMin: 4, hardMax: 200);
            HSettings.Int(M, "Staffing", Responders, 1, 1, 3, "Night bell: colonists per ring",
                "How many colonists at most come running when the bell rings.", hardMin: 1, hardMax: 10);
            HSettings.Int(M, "Staffing", WakeCooldownHours, 12, 1, 48, "Night bell: sleep protection (hours)",
                "A colonist who was woken by the bell is not woken again for this many hours.", hardMin: 0, hardMax: 240);
            HSettings.Bool(M, "Staffing", Hurry, true, "Night bell: colonists hurry",
                "Colonists who answer the bell jog to the venue instead of walking.");
        }
    }

    /// <summary>Per-map, unsaved state of rung bells: when each sign last rang, who was called, when the sign last pulsed.</summary>
    public sealed class MapComponent_HH_NightBell : MapComponent
    {
        public sealed class RingState { public Thing Sign; public Pawn LastGuest; public int LastRing = -99999, LastAlert = -99999, LastSound = -99999, LastPulse, LastMessage = -99999, LastReopen = -99999; public bool AnyResponder; }
        public sealed class Answering { public Pawn Pawn; public Thing Sign; public int Since; }

        public readonly Dictionary<int, RingState> Rings = new Dictionary<int, RingState>();
        public readonly List<Answering> Pending = new List<Answering>();

        public MapComponent_HH_NightBell(Map map) : base(map) { }

        public RingState StateOf(Thing sign)
        {
            if (!Rings.TryGetValue(sign.thingIDNumber, out var s)) Rings[sign.thingIDNumber] = s = new RingState { Sign = sign };
            return s;
        }

        public override void MapComponentTick()
        {
            if (Rings.Count == 0 && Pending.Count == 0) return;
            int t = Find.TickManager.TicksGame;
            if (t % 30 != 11) return;
            Guard.Run(ModuleId.ShopsFood, "NightBell.Tick", () => TickInner(t));
        }

        void TickInner(int t)
        {
            // somebody arrived at the post: a small ding, the call is answered
            for (int i = Pending.Count - 1; i >= 0; i--)
            {
                var a = Pending[i];
                bool signGone = a.Sign == null || a.Sign.Destroyed || !a.Sign.Spawned;
                bool pawnGone = a.Pawn == null || a.Pawn.Dead || a.Pawn.Destroyed || !a.Pawn.Spawned;
                if (!signGone && !pawnGone && VenueState.StaffOn(a.Sign) == a.Pawn)
                {
                    NightBell.PlayAnswered(a.Pawn, a.Sign);
                    Pending.RemoveAt(i);
                    continue;
                }
                // still on the way (the venue job for this sign is running)? After PendingTimeout only while they are still walking:
                // a colonist who is on the job but has stood still that long is stuck and is called off, so the next one is not sent alongside
                bool onTheWay = !pawnGone && a.Pawn.CurJob != null && a.Pawn.CurJob.def == HHVenueDefOf.HH_TendVenue && a.Pawn.CurJob.targetA.Thing == a.Sign;
                if (!signGone && onTheWay && (t - a.Since <= NightBell.PendingTimeout || a.Pawn.pather?.Moving == true)) continue;
                Pending.RemoveAt(i);
                if (onTheWay)
                {
                    try { a.Pawn.jobs.EndCurrentJob(JobCondition.Incompletable); }
                    catch (Exception e) { HLog.Once("nightbell.stuck", LogLevel.Warn, "Venues", "Night bell: could not call off a stuck colonist: " + e.Message); }
                }
                // the sent colonist's job failed (interrupted, unreachable, ended, the colonist died): the call is open again at once
                else if (!pawnGone) NoteDropped(a.Pawn, t);
                if (!signGone) Reopen(a.Sign, a.Pawn, t);
            }
            // a ringing, unstaffed venue pulses a little bell above its sign so the player sees which one calls
            foreach (var kv in Rings.ToList())
            {
                var rs = kv.Value;
                if (rs.Sign == null || rs.Sign.Destroyed || !rs.Sign.Spawned || !VenueState.BellActive(rs.Sign)) { if (t - rs.LastRing > 15000) Rings.Remove(kv.Key); continue; }
                if (VenueState.StaffOn(rs.Sign) != null) continue;
                if (t - rs.LastPulse >= 90) { rs.LastPulse = t; NightBell.PulseAbove(rs.Sign); }
            }
        }

        /// <summary>Colonists who dropped a bell call: tick of the last drop and how many in a row (unsaved).</summary>
        readonly Dictionary<int, (int tick, int count)> dropped = new Dictionary<int, (int, int)>();

        void NoteDropped(Pawn p, int t)
        {
            dropped.TryGetValue(p.thingIDNumber, out var d);
            int count = t - d.tick <= NightBell.DropMemory ? d.count + 1 : 1;
            dropped[p.thingIDNumber] = (t, count);
            if (dropped.Count > 64) dropped.Clear();
        }

        /// <summary>
        /// A colonist who dropped this call twice in a short time is left out for <see cref="NightBell.DropCooldown"/> ticks (the first drop is
        /// forgiven at once, so a lone colonist is called again right away; somebody who keeps dropping it is not pulled back every few seconds).
        /// </summary>
        public bool Resting(Pawn p, int t)
            => p != null && dropped.TryGetValue(p.thingIDNumber, out var d) && d.count >= 2 && t - d.tick < NightBell.DropCooldown;

        /// <summary>
        /// A colonist who was sent did not arrive. The call is opened again: the next ring alerts at once, and while the guest who rang is still
        /// waiting the bell calls again right now (at most every <see cref="NightBell.ReopenGap"/> ticks per sign, so a colonist who keeps
        /// dropping the job is not pulled back every half second).
        /// </summary>
        void Reopen(Thing sign, Pawn failed, int t)
        {
            if (Pending.Any(x => x.Sign == sign)) return;                         // somebody else is still on the way
            var rs = StateOf(sign);
            rs.AnyResponder = false;                                               // the next ring may call again after RetryGap instead of AlertGap
            NightBell.Reopened++;
            HLog.Debug("Venues", "Night bell: " + (failed?.LabelShort ?? "the colonist") + " did not arrive, the call is open again");
            if (!VenueState.BellActive(sign) || VenueState.StaffOn(sign) != null || !NightBell.IsWaiting(rs.LastGuest, sign)) return;
            if (t - rs.LastReopen < NightBell.ReopenGap) return;
            rs.LastReopen = t;
            var v = VenueApi.VenueOfSign(map, sign);
            if (v == null) return;
            rs.LastAlert = t;
            rs.AnyResponder = NightBell.Alert(v, rs.LastGuest) > 0;
        }
    }

    /// <summary>Keeps who was woken by the bell and when, so nobody is woken every night (saved).</summary>
    public sealed class GameComponent_HH_NightBell : GameComponent
    {
        Dictionary<int, int> lastWake = new Dictionary<int, int>();
        List<int> kk, vv;
        public static GameComponent_HH_NightBell Instance => Current.Game?.GetComponent<GameComponent_HH_NightBell>();

        public GameComponent_HH_NightBell(Game game) { }

        public bool CanWake(Pawn p)
        {
            int hours = HSettings.GetInt(NightBellSettings.WakeCooldownHours);
            return !lastWake.TryGetValue(p.thingIDNumber, out int t) || Find.TickManager.TicksGame - t >= hours * GenDate.TicksPerHour;
        }
        public void RecordWake(Pawn p) { lastWake[p.thingIDNumber] = Find.TickManager.TicksGame; }

        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                int now = Find.TickManager.TicksGame;
                foreach (var k in lastWake.Where(kv => now - kv.Value > 2 * GenDate.TicksPerDay).Select(kv => kv.Key).ToList()) lastWake.Remove(k);
            }
            Scribe_Collections.Look(ref lastWake, "lastWake", LookMode.Value, LookMode.Value, ref kk, ref vv);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && lastWake == null) lastWake = new Dictionary<int, int>();
        }
    }

    /// <summary>The call itself: sound, animation and who answers.</summary>
    public static class NightBell
    {
        /// <summary>A ring within this many ticks of the last call is only a refresh (the alert is not repeated).</summary>
        public const int AlertGap = 2000;
        /// <summary>When nobody heard the last call, a new ring may try again after this many ticks.</summary>
        public const int RetryGap = 600;
        /// <summary>A waiting guest rings again this often while nobody is on the way (F06 acceptance: one failed roll must not leave them alone).</summary>
        public const int ReRingTicks = 600;
        /// <summary>A sent colonist who has not arrived after this many ticks no longer counts as "on the way".</summary>
        public const int PendingTimeout = 4000;
        /// <summary>A call whose colonist dropped the job is re-sent at most this often per sign.</summary>
        public const int ReopenGap = 300;
        /// <summary>Two drops of a bell call within this many ticks …</summary>
        public const int DropMemory = 2500;
        /// <summary>… leave that colonist out of the next calls for this many ticks.</summary>
        public const int DropCooldown = 1200;
        /// <summary>Awake colonists within hearing range always hear the bell when their Hearing is above this (below: a chance, like sleepers).</summary>
        public const float SureHearing = 0.3f;

        /// <summary>Tests: how often a call was opened again because the sent colonist did not arrive.</summary>
        internal static int Reopened;

        /// <summary>Tests only: forces the hearing roll (true = hears, false = does not), null = roll normally.</summary>
        internal static bool? ForceHear;
        /// <summary>Tests only: replaces the Hearing capacity of a colonist.</summary>
        internal static Func<Pawn, float> HearingOverride;

        public sealed class Responder { public Pawn Pawn; public bool Sleeping; public float Chance, Score, Distance; }

        // ------------------------------------------------------------------ the ring
        /// <summary>A guest rings the bell of this venue (called by the waiting job after the venue's bell state was set).</summary>
        public static void Ring(Venue v, Pawn guest)
        {
            Guard.Run(ModuleId.ShopsFood, "NightBell.Ring", () =>
            {
                if (!Modules.On(ModuleId.ShopsFood) || v?.Sign == null || !v.Sign.Spawned) return;
                var map = v.Map;
                var comp = map.GetComponent<MapComponent_HH_NightBell>(); if (comp == null) return;
                int now = Find.TickManager.TicksGame;
                var rs = comp.StateOf(v.Sign);
                rs.LastRing = now;
                if (guest != null) rs.LastGuest = guest;
                if (guest != null) MoteMaker.ThrowText(guest.DrawPos, map, "HH.Venue.BellRung".TranslateOr("*ding*"), 3f);
                if (now - rs.LastSound > 240) { rs.LastSound = now; PlayRing(v, guest); }
                int gap = rs.AnyResponder ? AlertGap : RetryGap;
                if (now - rs.LastAlert < gap) return;
                rs.LastAlert = now;
                int n = Alert(v, guest);
                rs.AnyResponder = n > 0;
            });
        }

        /// <summary>Is a colonist on the way to (or already at) this sign: sent by the bell, or by their own Serving work?</summary>
        public static bool SomeoneComing(Thing sign)
        {
            var map = sign?.Map;
            if (map == null) return false;
            if (VenueState.StaffOn(sign) != null) return true;
            var cols = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < cols.Count; i++)
            {
                var j = cols[i].CurJob;
                if (j != null && j.def == HHVenueDefOf.HH_TendVenue && j.targetA.Thing == sign) return true;
            }
            return false;
        }

        /// <summary>Tick of the last ring at this sign (very long ago when it never rang).</summary>
        public static int LastRingOf(Thing sign)
        {
            var comp = sign?.Map?.GetComponent<MapComponent_HH_NightBell>();
            return comp != null && comp.Rings.TryGetValue(sign.thingIDNumber, out var rs) ? rs.LastRing : -99999;
        }

        /// <summary>Is this guest still waiting at the sign after ringing its bell?</summary>
        public static bool IsWaiting(Pawn guest, Thing sign)
            => guest != null && guest.Spawned && !guest.Dead && guest.CurJob != null && guest.CurJob.def == HHVenueDefOf.HH_WaitAtVenue && guest.CurJob.count == 1 && guest.CurJob.targetA.Thing == sign;

        static void PlayRing(Venue v, Pawn guest)
        {
            var map = v.Map;
            try { HHBellDefOf.HH_NightBell?.PlayOneShot(new TargetInfo(v.Sign.Position, map)); }
            catch (Exception e) { HLog.Once("nightbell.sound", LogLevel.Warn, "Venues", "Night bell sound failed: " + e.Message); }
            Vector3 above = (guest != null ? guest.DrawPos : v.Sign.DrawPos) + new Vector3(0f, 0f, 1.05f);
            try
            {
                if (HHBellDefOf.HH_Mote_Bell != null) MoteMaker.MakeStaticMote(above, map, HHBellDefOf.HH_Mote_Bell, 1f);
                if (HHBellDefOf.HH_Mote_RingArc != null)
                {
                    MoteMaker.MakeStaticMote(above + new Vector3(0f, 0f, 0.1f), map, HHBellDefOf.HH_Mote_RingArc, 0.7f);
                    MoteMaker.MakeStaticMote(above + new Vector3(0f, 0f, 0.1f), map, HHBellDefOf.HH_Mote_RingArc, 1.2f);
                }
            }
            catch (Exception e) { HLog.Once("nightbell.mote", LogLevel.Warn, "Venues", "Night bell mote failed: " + e.Message); }
        }

        internal static void PulseAbove(Thing sign)
        {
            if (HHBellDefOf.HH_Mote_Bell == null || sign?.Map == null) return;
            MoteMaker.MakeStaticMote(sign.DrawPos + new Vector3(0f, 0f, 0.85f), sign.Map, HHBellDefOf.HH_Mote_Bell, 0.55f);
        }

        internal static void PlayAnswered(Pawn p, Thing sign)
        {
            try { HHBellDefOf.HH_BellAnswered?.PlayOneShot(new TargetInfo(sign.Position, sign.Map)); } catch (Exception) { }
            if (p != null && p.Spawned) MoteMaker.ThrowText(p.DrawPos, p.Map, "HH.Venue.BellAnswered".TranslateOr("On my way!"), 2f);
        }

        /// <summary>One line for the sign's inspect text while its bell rings: when it was rung and who is on the way.</summary>
        public static string StatusLine(Thing sign)
        {
            var comp = sign?.Map?.GetComponent<MapComponent_HH_NightBell>();
            var staffer = VenueState.StaffOn(sign);
            if (staffer != null) return "HH.Venue.BellAnsweredBy".TranslateOr("Night bell: answered by {0}").Replace("{0}", staffer.LabelShort);
            string ago = "";
            if (comp != null && comp.Rings.TryGetValue(sign.thingIDNumber, out var rs) && rs.LastRing > 0) ago = " · " + "HH.Venue.BellRungAgo".TranslateOr("rung {0} ago").Replace("{0}", (Find.TickManager.TicksGame - rs.LastRing).ToStringTicksToPeriod());
            var coming = comp?.Pending.Where(a => a.Sign == sign && a.Pawn != null && a.Pawn.Spawned && !a.Pawn.Dead).Select(a => a.Pawn.LabelShort).ToList();
            if (coming != null && coming.Count > 0) return "HH.Venue.BellOnTheWay".TranslateOr("Night bell: {0} is on the way").Replace("{0}", string.Join(", ", coming)) + ago;
            return "HH.Venue.BellNobody".TranslateOr("Night bell: ringing, nobody has come yet") + ago;
        }

        // ------------------------------------------------------------------ who answers
        /// <summary>
        /// Calls colonists to the venue. Returns how many were sent. Candidates: colonists able to serve (Serving work on, or anyone by setting), not busy
        /// with something vital, within hearing range. Awake ones with normal hearing hear it for sure; the others roll "hears it" (asleep: the more rested the lighter the sleep), the best at most
        /// <c>bellResponders</c> go; sleepers only when the setting allows and they are volunteers with no recent wake-up.
        /// </summary>
        public static int Alert(Venue v, Pawn guest)
        {
            var wakes = HSettings.GetEnum<BellWakes>(NightBellSettings.Wakes);
            if (wakes == BellWakes.Off) return 0;
            if (VenueApi.IsStaffed(v)) return 0;
            var map = v.Map;
            var sign = v.Sign;
            // somebody already on the way?
            var comp = map.GetComponent<MapComponent_HH_NightBell>();
            if (comp != null && comp.Pending.Any(a => a.Sign == sign && a.Pawn != null && !a.Pawn.Dead && a.Pawn.Spawned && a.Pawn.CurJob?.def == HHVenueDefOf.HH_TendVenue && a.Pawn.CurJob.targetA.Thing == sign)) return 0;
            if (SomeoneComing(sign)) return 0;                                     // a colonist is walking there anyway (own Serving work, or a call that outlived its entry)
            var list = Candidates(v, wakes);
            int max = Math.Max(1, HSettings.GetInt(NightBellSettings.Responders));
            var sent = new List<Pawn>();
            foreach (var c in list.OrderByDescending(x => x.Score))
            {
                if (sent.Count >= max) break;
                bool hears = ForceHear ?? JRand.Stream("venues.bell").Chance(c.Chance);
                if (!hears) continue;
                if (Send(c, v)) sent.Add(c.Pawn);
            }
            if (sent.Count > 0)
                HLog.Info("Venues", $"Night bell at {v.DisplayNameOf()}: {string.Join(", ", sent.Select(p => p.LabelShort))} answering ({list.Count} could have heard it)");
            else
            {
                HLog.Debug("Venues", $"Night bell at {v.DisplayNameOf()}: nobody answers ({list.Count} candidates)");
                var rs = comp?.StateOf(sign);
                int now = Find.TickManager.TicksGame;
                if (rs != null && now - rs.LastMessage > 15000 && map.IsPlayerHome)
                {
                    rs.LastMessage = now;
                    Messages.Message("HH.Venue.BellUnanswered".TranslateOr("The night bell at {0} is ringing, but nobody is coming. Colonists with the Serving work type need to be awake and within earshot.").Replace("{0}", v.DisplayNameOf()),
                        new LookTargets(sign), MessageTypeDefOf.CautionInput, false);
                }
            }
            return sent.Count;
        }

        /// <summary>Everyone who could answer, with their chance to hear the bell (public so tests and the sign's tooltip can look).</summary>
        public static List<Responder> Candidates(Venue v, BellWakes wakes)
        {
            var res = new List<Responder>();
            var map = v.Map; var bell = v.Sign.Position;
            var serving = HHVenueDefOf.HH_Serving;
            var who = HSettings.GetEnum<BellWho>(NightBellSettings.Who);
            float range = HSettings.GetInt(NightBellSettings.Range);
            var comp = map.GetComponent<MapComponent_HH_NightBell>();
            int now = Find.TickManager.TicksGame;
            foreach (var p in map.mapPawns.FreeColonistsSpawned)
            {
                if (p.workSettings == null || p.WorkTypeIsDisabled(serving)) continue;
                bool active = p.workSettings.WorkIsActive(serving);
                if (who == BellWho.ServingOnly && !active) continue;
                if (!VenueApi.PostCell(v).IsValid || p.Faction != Faction.OfPlayer) continue;
                bool awake = p.Awake();
                if (!awake && (wakes != BellWakes.WakeVolunteers || !Volunteer(p, active))) continue;
                if (!Available(p, awake)) continue;
                if (comp != null && comp.Resting(p, now)) continue;                // dropped this call twice just now
                float hearing = HearingOverride?.Invoke(p) ?? (p.health?.capacities?.GetLevel(PawnCapacityDefOf.Hearing) ?? 1f);
                if (hearing <= 0.05f) continue;                                    // deaf
                float dist = EffectiveDistance(map, p.Position, bell);
                float r = range * Mathf.Clamp(hearing, 0f, 1.5f);
                if (dist >= r) continue;
                float chance = Mathf.Clamp01(hearing * (1f - dist / r));
                // F16 acceptance: an awake colonist within earshot (r already includes their Hearing) always hears it; only poor hearing or sleep leaves it to chance
                if (awake) chance = hearing > SureHearing ? 1f : Mathf.Min(1f, chance * 1.5f);
                else
                {
                    float rest = p.needs?.rest?.CurLevel ?? 0.5f;
                    chance *= 0.35f + 0.65f * rest;                                // a sleeper who has just dropped off is deep asleep
                    if (HasTrait(p, "DeepSleeper")) chance *= 0.5f;
                }
                float social = p.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 5;
                res.Add(new Responder { Pawn = p, Sleeping = !awake, Chance = chance, Distance = dist, Score = chance + social / 40f + (awake ? 0.3f : 0f) });
            }
            return res;
        }

        static bool HasTrait(Pawn p, string defName)
        {
            var ts = p.story?.traits?.allTraits;
            if (ts != null) for (int i = 0; i < ts.Count; i++) if (ts[i].def.defName == defName) return true;
            return false;
        }

        /// <summary>A sleeper who may be woken: Serving at priority 1 or 2 (or simple work settings), and not woken within the cooldown.</summary>
        static bool Volunteer(Pawn p, bool servingActive)
        {
            if (!servingActive) return false;                                       // sleepers are only woken when they actually serve
            if (Find.PlaySettings.useWorkPriorities && p.workSettings.GetPriority(HHVenueDefOf.HH_Serving) > 2) return false;
            var gc = GameComponent_HH_NightBell.Instance;
            return gc == null || gc.CanWake(p);
        }

        /// <summary>Not drafted, hurt, hungry or exhausted, in a mental break, down, in danger or busy with a rescue, an operation or a fire.</summary>
        static bool Available(Pawn p, bool awake)
        {
            if (p.Dead || p.Downed || p.Drafted || p.InMentalState || p.IsPrisoner || p.IsBurning()) return false;
            if (!p.health.capacities.CapableOf(PawnCapacityDefOf.Moving)) return false;
            if (p.health.hediffSet.BleedRateTotal > 0.1f) return false;
            if (p.needs?.food != null && p.needs.food.CurLevel < 0.08f) return false;
            if (p.needs?.rest != null && p.needs.rest.CurLevel < 0.12f) return false;
            if (p.Map?.dangerWatcher != null && p.Map.dangerWatcher.DangerRating >= StoryDanger.High) return false;
            if (!awake) return !HealthAIUtility.ShouldSeekMedicalRest(p);
            var job = p.CurJob;
            if (job == null) return true;
            var d = job.def;
            if (d == HHVenueDefOf.HH_TendVenue) return false;                      // already tending a venue
            if (d == JobDefOf.TendPatient || d == JobDefOf.Rescue || d == JobDefOf.BeatFire || d == JobDefOf.Capture || d == JobDefOf.Arrest
                || d == JobDefOf.FeedPatient || d == JobDefOf.ExtinguishSelf || d == JobDefOf.Vomit) return false;
            if (job.playerForced && d != JobDefOf.Wait && d != JobDefOf.Wait_Wander) return false;
            if (!d.casualInterruptible && d.joyKind == null && d != JobDefOf.Wait && d != JobDefOf.Wait_Wander) return false;
            if (d == JobDefOf.LayDown) return !HealthAIUtility.ShouldSeekMedicalRest(p);
            return true;
        }

        /// <summary>Distance as sound travels it: straight line, every closed door and every wall cell in between muffles it.</summary>
        public static float EffectiveDistance(Map map, IntVec3 from, IntVec3 to)
        {
            float d = (from - to).LengthHorizontal;
            if (d > 80f) return d;
            bool inWall = false; int doors = 0, walls = 0;
            foreach (var c in GenSight.PointsOnLineOfSight(from, to))
            {
                if (!c.InBounds(map) || c == from || c == to) continue;
                var door = c.GetDoor(map);
                if (door != null) { if (!door.Open) doors++; inWall = false; continue; }
                bool wall = c.Filled(map);
                if (wall && !inWall) walls++;
                inWall = wall;
            }
            return d + 6f * doors + 3f * walls;
        }

        static bool Send(Responder c, Venue v)
        {
            var p = c.Pawn; var sign = v.Sign;
            try
            {
                var post = VenueApi.PostCell(v);
                if (!p.CanReach(post, PathEndMode.OnCell, Danger.None) || !p.CanReserve(sign, 1, -1, null, false)) return false;
                bool busy = !c.Sleeping && p.CurJob != null && p.CurJob.def != JobDefOf.Wait_Wander && p.CurJob.def != JobDefOf.Wait && p.CurJob.def.joyKind == null;
                if (c.Sleeping)
                {
                    p.jobs.EndCurrentJob(JobCondition.InterruptForced, false, false);
                }
                var job = JobMaker.MakeJob(HHVenueDefOf.HH_TendVenue, sign, post);
                job.expiryInterval = 1500;
                job.checkOverrideOnExpire = true;
                if (HSettings.GetBool(NightBellSettings.Hurry)) job.locomotionUrgency = LocomotionUrgency.Jog;
                p.jobs.StartJob(job, JobCondition.InterruptForced, null, resumeCurJobAfterwards: busy);
                if (p.CurJob != job) return false;
                if (c.Sleeping)
                {
                    GameComponent_HH_NightBell.Instance?.RecordWake(p);       // only a real wake-up costs the pawn its sleep protection and mood
                    GiveWokenThought(p);
                }
                var comp = v.Map.GetComponent<MapComponent_HH_NightBell>();
                comp?.Pending.Add(new MapComponent_HH_NightBell.Answering { Pawn = p, Sign = sign, Since = Find.TickManager.TicksGame });
                try { MoteMaker.MakeThoughtBubble(p, "UI/HH/Venues/HeardBell", false); } catch (Exception) { }
                return true;
            }
            catch (Exception e) { HLog.Error("Venues", "Night bell: could not send " + p.LabelShort, e); return false; }
        }

        static void GiveWokenThought(Pawn p)
        {
            try
            {
                if (HHBellDefOf.HH_WokenByBell == null) return;
                bool easygoing = p.story?.traits?.HasTrait(TraitDefOf.Kind) == true || (p.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0) >= 12;
                var th = (Thought_Memory)ThoughtMaker.MakeThought(HHBellDefOf.HH_WokenByBell, easygoing ? 1 : 0);
                p.needs?.mood?.thoughts?.memories?.TryGainMemory(th);
            }
            catch (Exception e) { HLog.Once("nightbell.thought", LogLevel.Warn, "Venues", "Woken-by-bell thought failed: " + e.Message); }
        }
    }

    /// <summary>The venue sign's inspect text names the ringing bell (a postfix, so the sign's own code stays untouched).</summary>
    [HarmonyPatch(typeof(CompVenueSignHH), nameof(CompVenueSignHH.CompInspectStringExtra))]
    static class Patch_VenueSign_BellLine
    {
        static void Postfix(CompVenueSignHH __instance, ref string __result)
        {
            if (!Modules.On(ModuleId.ShopsFood) || __instance?.parent == null || !__instance.parent.Spawned || !VenueState.BellActive(__instance.parent)) return;
            try { __result = (__result.NullOrEmpty() ? "" : __result + "\n") + NightBell.StatusLine(__instance.parent); }
            catch (Exception e) { HLog.Once("nightbell.inspect", LogLevel.Warn, "Venues", "Night bell inspect line failed: " + e.Message); }
        }
    }
}
