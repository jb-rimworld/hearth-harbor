// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Hearth.Wellness
{
    public class CompProperties_WellnessFacility : CompProperties
    {
        public WellnessKind kind;
        /// <summary>How many pawns may use it at the same time (informational; the job reservation limit is the real cap).</summary>
        public int maxUsers = 1;
        public CompProperties_WellnessFacility() { compClass = typeof(CompWellnessFacility); }
    }

    /// <summary>
    /// Marks a building as part of the spa (hot tub, sauna stove, massage table) and answers "can it be used right now?".
    /// Also holds the two switches of the owner (colonists / guests may use) and the transient session state of a massage table.
    /// </summary>
    public class CompWellnessFacility : ThingComp
    {
        public bool colonistsMayUse = true, guestsMayUse = true;

        // Massage table session (not saved: after a load the waiting simply restarts, see JobDriver_ReceiveMassage)
        public Pawn recipient, masseur;
        public bool recipientReady, sessionActive, sessionDone, sessionDenied;
        public int sessionTicks;

        public CompProperties_WellnessFacility Props => (CompProperties_WellnessFacility)props;
        public WellnessKind Kind => Props.kind;

        public bool AllowsUser(Pawn p)
        {
            if (p == null) return false;
            return GuestApi.IsGuest(p) ? guestsMayUse : colonistsMayUse;
        }

        public void ResetSession()
        {
            recipient = null; masseur = null; recipientReady = sessionActive = sessionDone = sessionDenied = false; sessionTicks = 0;
        }

        /// <summary>Physical readiness. <paramref name="slack"/> = already in use: the sauna may cool a few degrees before it throws people out.</summary>
        public bool UsableNow(out string reason, bool slack = false)
        {
            reason = null;
            var t = parent;
            if (!t.Spawned) { reason = "not on the map"; return false; }
            if (t.IsBrokenDown()) { reason = "broken down"; return false; }
            var power = t.TryGetComp<CompPowerTrader>();
            if (power != null && !power.PowerOn) { reason = "no power"; return false; }
            if (Kind != WellnessKind.Massage && !FlickUtility.WantsToBeOn(t)) { reason = "switched off"; return false; }
            if (Kind == WellnessKind.Sauna)
            {
                var fuel = t.TryGetComp<CompRefuelable>();
                if (fuel != null && !fuel.HasFuel) { reason = "no fuel"; return false; }
                if (!SaunaUtil.IsHotEnough(t, slack ? 5f : 0f, out float temp)) { reason = "too cold (" + temp.ToString("0") + " °C)"; return false; }
            }
            return true;
        }

        public override string CompInspectStringExtra()
        {
            if (!parent.Spawned) return null;
            string state;
            switch (Kind)
            {
                case WellnessKind.Sauna:
                    {
                        SaunaUtil.IsHotEnough(parent, 0f, out float temp);
                        bool ok = UsableNow(out string why);
                        state = ok ? "HH.Wellness.Inspect.SaunaReady".Translate(temp.ToString("0")).ToString()
                                   : "HH.Wellness.Inspect.SaunaNotReady".Translate(temp.ToString("0"), HSettings.Get(WellnessSettings.SaunaMinTemp).ToString("0"), why).ToString();
                        break;
                    }
                case WellnessKind.Massage:
                    state = recipient != null ? (sessionActive ? "HH.Wellness.Inspect.MassageRunning".Translate().ToString() : "HH.Wellness.Inspect.MassageWaiting".Translate().ToString())
                                              : "HH.Wellness.Inspect.MassageFree".Translate().ToString();
                    break;
                default:
                    state = UsableNow(out string w2) ? "HH.Wellness.Inspect.TubReady".Translate().ToString() : "HH.Wellness.Inspect.TubNotReady".Translate(w2).ToString();
                    break;
            }
            string who = "HH.Wellness.Inspect.Users".Translate(colonistsMayUse ? "HH.Wellness.Yes".Translate() : "HH.Wellness.No".Translate(),
                guestsMayUse ? "HH.Wellness.Yes".Translate() : "HH.Wellness.No".Translate()).ToString();
            return state + "\n" + who;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (parent.Faction != Faction.OfPlayer) yield break;
            yield return new Command_Toggle
            {
                defaultLabel = "HH.Wellness.Gizmo.Colonists".Translate(),
                defaultDesc = "HH.Wellness.Gizmo.ColonistsDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/HH/Wellness/ColonistsMayUse", false) ?? BaseContent.BadTex,
                isActive = () => colonistsMayUse,
                toggleAction = () => colonistsMayUse = !colonistsMayUse,
            };
            yield return new Command_Toggle
            {
                defaultLabel = "HH.Wellness.Gizmo.Guests".Translate(),
                defaultDesc = "HH.Wellness.Gizmo.GuestsDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/HH/Wellness/GuestsMayUse", false) ?? BaseContent.BadTex,
                isActive = () => guestsMayUse,
                toggleAction = () => guestsMayUse = !guestsMayUse,
            };
        }

        public override void PostExposeData()
        {
            Scribe_Values.Look(ref colonistsMayUse, "colonistsMayUse", true);
            Scribe_Values.Look(ref guestsMayUse, "guestsMayUse", true);
        }

        public override void PostDeSpawn(Map map
#if RW16PLUS
            , DestroyMode mode = DestroyMode.Vanish
#endif
            )
        {
            base.PostDeSpawn(map
#if RW16PLUS
                , mode
#endif
                );
            ResetSession();
        }
    }

    /// <summary>The sauna rule (KONZEPT 6.7): a sauna is a closed room whose stove keeps it at or above the minimum temperature (default 45 °C).</summary>
    public static class SaunaUtil
    {
        public static float MinTemp => HSettings.Get(WellnessSettings.SaunaMinTemp);

        public static Room RoomOf(Thing stove)
        {
            if (stove == null || !stove.Spawned) return null;
            var room = stove.GetRoom();
            if (room == null || room.PsychologicallyOutdoors || room.UsesOutdoorTemperature) return null;
            return room;
        }

        public static bool IsHotEnough(Thing stove, float slack, out float temp)
        {
            temp = 0f;
            var room = RoomOf(stove);
            if (room == null) { if (stove != null && stove.Spawned) temp = stove.AmbientTemperature; return false; }
            temp = room.Temperature;
            return temp >= MinTemp - slack;
        }

        /// <summary>Number of working saunas (stove with fuel in a room at temperature) on a map.</summary>
        public static int WorkingSaunas(Map map)
        {
            int n = 0;
            if (map == null || WellnessDefOf.HH_Sauna == null) return 0;
            foreach (var t in map.listerThings.ThingsOfDef(WellnessDefOf.HH_Sauna))
            {
                var c = t.TryGetComp<CompWellnessFacility>();
                if (c != null && c.UsableNow(out _)) n++;
            }
            return n;
        }

        /// <summary>A free cell in the stove's room, preferring seats, close to the stove. Null if none.</summary>
        public static IntVec3? FindSpot(Pawn pawn, Thing stove)
        {
            var room = RoomOf(stove);
            if (room == null) return null;
            var map = stove.Map;
            IntVec3? best = null; float bestScore = float.MaxValue;
            foreach (var c in GenRadial.RadialCellsAround(stove.Position, 5.9f, false))
            {
                if (!c.InBounds(map) || c.GetRoom(map) != room || !c.Standable(map)) continue;
                var edifice = c.GetEdifice(map);
                if (edifice != null && edifice.def.passability == Traversability.Impassable) continue;
                if (c.IsForbidden(pawn) || !pawn.CanReserve(c)) continue;
                float score = c.DistanceTo(stove.Position);
                if (edifice != null && edifice.def.building != null && edifice.def.building.isSittable) score -= 3f;
                if (score >= bestScore) continue;
                if (!pawn.CanReach(c, PathEndMode.OnCell, Danger.None)) continue;
                best = c; bestScore = score;
            }
            return best;
        }
    }

    /// <summary>What happens to a pawn when a session ends well (shared by tub, sauna and massage).</summary>
    public static class WellnessEffects
    {
        public static void GainJoy(Pawn p, int delta, Thing building, JoyKindDef kind)
        {
            var need = p.needs?.joy;
            if (need == null || p.CurJob == null) return;
            float f = HSettings.Get(WellnessSettings.JoyFactor) * (building != null ? building.GetStatValue(StatDefOf.JoyGainFactor) : 1f);
            need.GainJoy(f * p.CurJob.def.joyGainRate * 0.36f / 2500f * delta, kind);
        }

        /// <summary>Mood, rest, soothing, guest satisfaction and bookkeeping. <paramref name="quality"/> 0.7–1.3 (masseur skill).</summary>
        public static void Complete(Pawn p, WellnessKind kind, Thing facility, float quality = 1f)
        {
            if (p == null || p.Dead) return;
            // mood
            var memories = p.needs?.mood?.thoughts?.memories;
            if (memories != null && WellnessDefOf.HH_Relaxed != null)
                memories.TryGainMemory(ThoughtMaker.MakeThought(WellnessDefOf.HH_Relaxed, (int)kind));
            // rest
            float rest = HSettings.Get(WellnessSettings.RestBonus) * (kind == WellnessKind.Massage ? 1.4f : 1f) * quality;
            if (rest > 0f && p.needs?.rest != null) p.needs.rest.CurLevel = Mathf.Min(1f, p.needs.rest.CurLevel + rest);
            // soothed (pain -15 % for about a day)
            if (HSettings.GetBool(WellnessSettings.Soothe) && WellnessDefOf.HH_Soothed != null && p.health?.hediffSet != null)
            {
                var h = p.health.hediffSet.GetFirstHediffOfDef(WellnessDefOf.HH_Soothed);
                if (h == null) p.health.AddHediff(WellnessDefOf.HH_Soothed);
                else { var dis = h.TryGetComp<HediffComp_Disappears>(); if (dis != null) dis.ticksToDisappear = Math.Max(dis.ticksToDisappear, 30000); }
            }
            if (p.needs?.joy != null) JoyUtility.TryGainRecRoomThought(p);
            // guests
            if (GuestApi.IsGuest(p))
                GuestApi.AddSatisfaction(p, HSettings.Get(WellnessSettings.Satisfaction) * quality, "Spa: " + kind);
            WellnessState.Current?.RecordSession(p, kind);
            HLog.Debug("Wellness", $"{p.LabelShort} finished {kind} (quality {quality:0.00})");
        }
    }

    /// <summary>Prices, day passes and the actual charging of guests.</summary>
    public static class WellnessBilling
    {
        public static bool IsSpaVenueType(VenueTypeDef t) => t != null && (t.defName == "HH_Venue_Spa" || t.module == ModuleId.Wellness);

        public static Venue VenueOf(Thing facility)
        {
            if (facility == null || !facility.Spawned) return null;
            return Venues.At(facility.Position, facility.Map);
        }

        /// <summary>Is a colonist with the Serving work type standing in the venue's room (staffed = full price)?</summary>
        public static bool IsStaffed(Thing facility)
        {
            var room = facility.GetRoom(); if (room == null || WellnessDefOf.HH_Serving == null) return false;
            foreach (var c in facility.Map.mapPawns.FreeColonistsSpawned)
            {
                if (c.Downed || !c.Awake() || c.WorkTypeIsDisabled(WellnessDefOf.HH_Serving)) continue;
                if (c.workSettings == null || !c.workSettings.WorkIsActive(WellnessDefOf.HH_Serving)) continue;
                if (c.GetRoom() == room) return true;
            }
            return false;
        }

        static double VenueFactor(Thing facility)
        {
            double f = 1.0; var v = VenueOf(facility);
            if (v != null)
            {
                f *= v.Type != null ? Math.Max(0, v.Type.priceFactor) : 1.0;
                f *= v.Comp != null ? Math.Max(0, v.Comp.priceFactor) : 1.0;
            }
            return f;
        }

        public static Money Price(Thing facility, WellnessKind kind)
        {
            double f = VenueFactor(facility);
            if (kind != WellnessKind.Massage && !IsStaffed(facility)) f *= 1.0 - HSettings.Get(WellnessSettings.SelfService);
            return WellnessSettings.PriceOf(kind) * f;
        }

        public static Money PassPrice(Thing facility) => HSettings.GetSilver(WellnessSettings.PriceDayPass) * VenueFactor(facility);

        /// <summary>
        /// Charge a guest for one session. Free with a valid day pass; wealthier guests buy a pass instead (hot tub and sauna only).
        /// False = the guest could not or would not pay (nothing taken).
        /// </summary>
        public static bool ChargeSession(Pawn guest, Thing facility, WellnessKind kind)
        {
            var state = WellnessState.Current;
            var venue = VenueOf(facility);
            int venueId = venue?.Sign?.thingIDNumber ?? 0;
            if (kind != WellnessKind.Massage && state != null && state.HasPass(guest, venueId)) return true;
            var price = Price(facility, kind);
            if (kind != WellnessKind.Massage && state != null && HSettings.GetBool(WellnessSettings.DayPass))
            {
                var pass = PassPrice(facility);
                var info = GuestApi.Info(guest);
                if (info != null && pass > price && pass < price * 3.0 && info.Wealth >= 40 && info.Budget >= pass * 1.5)
                {
                    if (GuestApi.ChargeGuest(guest, pass, "spa", "Spa day pass"))
                    {
                        state.GrantPass(guest, venueId, Math.Max(1, (int)Math.Round(HSettings.Get(WellnessSettings.DayPassHours))));
                        state.revenueMicro += pass.Micro;
                        return true;
                    }
                }
            }
            if (GuestApi.ChargeGuest(guest, price, "spa", "Spa: " + kind))
            {
                if (state != null) state.revenueMicro += price.Micro;
                return true;
            }
            return false;
        }
    }

    /// <summary>Soft RimWard bridge (KONZEPT 13.1/13.2, F2): walking patients in a colony medical bed may take recreation in the spa. No RimWard types are referenced.</summary>
    public static class RehabBridge
    {
        public const string RimWardPackageId = "juliusberger.rimward";
        /// <summary>Tests flip this to exercise the rule without RimWard loaded.</summary>
        public static bool ForceActive;
        public static bool RimWardActive => ForceActive || ModsConfig.IsActive(RimWardPackageId);

        static System.Reflection.MethodInfo walkingPatient;
        static bool walkingResolved;
        /// <summary>RimWard 1.7+ says this pawn is one of its patients who is up and about (RimWard.PatientApi.IsWalkingPatient). Reflection only; false without RimWard.</summary>
        public static bool IsRimWardWalkingPatient(Pawn p)
        {
            if (p == null || !ModsConfig.IsActive(RimWardPackageId)) return false;
            if (!walkingResolved)
            {
                walkingResolved = true;
                try { walkingPatient = GenTypes.GetTypeInAnyAssembly("RimWard.PatientApi")?.GetMethod("IsWalkingPatient", new[] { typeof(Pawn) }); }
                catch (Exception e) { HLog.Warn("Wellness", "RimWard.PatientApi not usable: " + e.Message); }
                if (walkingPatient != null) HLog.Info("Wellness", "RimWard.PatientApi found: real RimWard patients may use the spa");
            }
            if (walkingPatient == null) return false;
            try { return (bool)walkingPatient.Invoke(null, new object[] { p }); } catch (Exception) { return false; }
        }

        /// <summary>Is the pawn a patient in a colony medical bed (as opposed to a healthy colonist)?</summary>
        public static bool IsBedPatient(Pawn p)
        {
            var bed = p.CurrentBed();
            return bed != null && bed.Medical && bed.Faction == Faction.OfPlayer;
        }

        public static bool MayUse(Pawn p, WellnessKind kind, out string reason)
        {
            reason = null;
            if (!HSettings.GetBool(WellnessSettings.RehabJoy)) { reason = "rehab recreation switched off"; return false; }
            if (!RimWardActive) { reason = "RimWard not active"; return false; }
            bool rwPatient = IsRimWardWalkingPatient(p); // a real RimWard patient is a guest of the colony, not a colonist, and is up and about
            if (!rwPatient && (p.Faction != Faction.OfPlayer || !p.IsColonist)) { reason = "not a colonist"; return false; }
            if (!rwPatient && !IsBedPatient(p)) { reason = "not in a colony medical bed"; return false; }
            if (p.Downed || !p.Awake()) { reason = "asleep or downed"; return false; }
            if (!p.health.capacities.CapableOf(PawnCapacityDefOf.Moving)) { reason = "cannot walk"; return false; }
            if (HealthAIUtility.ShouldBeTendedNowByPlayerUrgent(p)) { reason = "needs treatment now"; return false; }
            if (p.health.hediffSet.BleedRateTotal > 0.01f) { reason = "bleeding"; return false; }
            if (kind == WellnessKind.Sauna)
            {
                // no sauna after fresh surgery or while fighting an infection (KONZEPT 13.1)
                if (p.health.hediffSet.hediffs.Any(h => h.def.defName.StartsWith("Surgical", StringComparison.Ordinal)))
                { reason = "fresh surgery"; return false; }
                if (p.health.hediffSet.HasImmunizableNotImmuneHediff()) { reason = "fighting an illness"; return false; }
            }
            return true;
        }
    }
}
