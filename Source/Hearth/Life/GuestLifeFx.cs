// SPDX-License-Identifier: MIT
// Guest life on the map (1.1.0, F31 "Verhalten je Situation"): small cues for the moments of a stay. A friendly chime when a group arrives
// (on foot at the map edge or out of a drop pod), the desk bell, a pen scratch and a key at the check-in, coins that rise towards the till when a
// guest pays, a tiny coin flip for a tip, a wave and a soft chime when guests leave, their review as 1-5 stars above the head, and a small heart
// when a couple lies down in a shared double bed.
// Everything here is transient display: nothing is saved, nothing touches money or results. One global setting (core.lifeFx) switches it all;
// every cue has its own pause per pawn and there is a cap per map and second, so a busy inn never turns into a firework.
using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Hearth.Life
{
    [DefOf]
    public static class LifeFxDefOf
    {
        public static FleckDef HH_Fleck_Life_Suitcase, HH_Fleck_Life_Key, HH_Fleck_Life_Coins, HH_Fleck_Life_TipCoin, HH_Fleck_Life_Wave,
            HH_Fleck_Life_Stars1, HH_Fleck_Life_Stars2, HH_Fleck_Life_Stars3, HH_Fleck_Life_Stars4, HH_Fleck_Life_Stars5, HH_Fleck_Life_Puff,
            HH_Fleck_Life_Sparkle, HH_Fleck_Life_Heart;
        public static SoundDef HH_Life_Arrival, HH_Life_CheckIn, HH_Life_Pay, HH_Life_Tip, HH_Life_Farewell, HH_Life_FiveStars;
        static LifeFxDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(LifeFxDefOf)); }
    }

    public enum LifeCue { Arrival = 0, CheckIn = 1, Pay = 2, Tip = 3, Farewell = 4, Review = 5, Heart = 6 }

    public static class GuestLifeFx
    {
        public const string SettingKey = "core.lifeFx";
        /// <summary>Pause per pawn (ticks) before the same cue may show again for them.</summary>
        static readonly int[] Gap = { 0, 2500, 900, 600, 5000, 5000, 20000 };
        /// <summary>At most this many cues per map and second (60 ticks).</summary>
        public const int MapCuesPerSecond = 5;
        /// <summary>The review stars appear this many ticks after the guest waved goodbye.</summary>
        public const int ReviewDelay = 110;
        /// <summary>The reviews of one group follow each other at this distance (ticks).</summary>
        public const int ReviewStagger = 25;
        /// <summary>A wave or review that met a busy second on screen is tried again this often, after this many ticks.</summary>
        const int BusyRetries = 3, BusyRetryTicks = 20;

        static readonly Dictionary<long, int> last = new Dictionary<long, int>();                 // pawn id * 8 + cue → tick
        static readonly Dictionary<int, (int tick, int count)> perMap = new Dictionary<int, (int, int)>();
        static readonly HashSet<int> groupsGreeted = new HashSet<int>(), groupsWaved = new HashSet<int>();
        static readonly List<(int groupId, List<Pawn> pawns, int since)> arrivals = new List<(int, List<Pawn>, int)>();
        static readonly List<(int tick, Action act)> queue = new List<(int, Action)>();
        static readonly List<long> pruneKeys = new List<long>();
        static bool subscribed;

        /// <summary>Tests: how often each cue really fired (index = <see cref="LifeCue"/>), and how many sounds were started.</summary>
        public static readonly int[] Fired = new int[7];
        public static int Sounds;
        /// <summary>Tests: the stars of the last review cue.</summary>
        public static int LastStars;
        /// <summary>Tests: set while a tip is charged, so the payment of the tip shows the tip's coin flip instead of the pay cue.</summary>
        internal static Pawn TipCharging;
        /// <summary>Tests only: treat every cue as on screen (true) or off screen (false); null = the camera decides.</summary>
        internal static bool? ForceVisible;

        static GuestLifeFx() { HearthGame.OnGameInit += Clear; }

        public static void Clear()
        {
            last.Clear(); perMap.Clear(); groupsGreeted.Clear(); groupsWaved.Clear(); arrivals.Clear(); queue.Clear();
            for (int i = 0; i < Fired.Length; i++) Fired[i] = 0;
            Sounds = 0; LastStars = 0; TipCharging = null; ForceVisible = null;
        }

        public static void RegisterSettings()
        {
            var s = HSettings.Bool(ModuleId.Core, "Look and sound", SettingKey, true, "Guest life on the map",
                "Small signs and sounds for the moments of a stay: a chime when guests arrive, the desk bell at check-in, coins when they pay or tip, a wave and their review in stars when they leave, a heart for couples sharing a bed.");
            if (s != null) s.Global = true;
        }

        /// <summary>Hooks the payment event once (from the Core content module when the defs are loaded).</summary>
        public static void Init()
        {
            if (subscribed) return;
            subscribed = true;
            Payments.Paid += OnPaid;
        }

        public static bool On
        {
            get { try { return HSettings.GetBool(SettingKey); } catch (Exception) { return true; } }
        }

        // ------------------------------------------------------------------ the moments of a stay
        /// <summary>A guest group was sent to the map (on foot or in drop pods): the chime plays when the first of them is on the map.</summary>
        public static void NoteArrival(int groupId, List<Pawn> pawns)
        {
            if (!On || pawns == null || pawns.Count == 0 || groupsGreeted.Contains(groupId)) return;
            if (arrivals.Count > 32) arrivals.RemoveAt(0);
            arrivals.Add((groupId, new List<Pawn>(pawns), Find.TickManager.TicksGame));
        }

        /// <summary>Check-in at a front desk: the desk bell and a pen scratch at the desk, a small key above the guest.</summary>
        public static void NoteCheckIn(Pawn guest, Thing desk)
        {
            if (!Allowed(guest, LifeCue.CheckIn, out bool vis)) return;
            var map = guest.Map;
            Fleck(guest.DrawPos + new Vector3(0.15f, 0f, 0.55f), map, LifeFxDefOf.HH_Fleck_Life_Key, 0.55f, vis, 0f, 0.35f);
            if (vis) Play(LifeFxDefOf.HH_Life_CheckIn, desk != null && desk.Spawned ? desk : guest);
            Count(LifeCue.CheckIn);
        }

        /// <summary>A guest paid the colony's till (any bill: a night, a meal, a ride …): a few coins rise from the guest towards the till.</summary>
        static void OnPaid(Pawn payer, Money amount, string toAccount, string memo, PaymentMethod method)
        {
            if (payer == null || !payer.Spawned || amount.Micro <= 0) return;
            if (payer == TipCharging) return;                                      // the tip shows its own coin flip
            var map = payer.Map;
            if (toAccount != Payments.ColonyTill(map) || !GuestApi.IsGuest(payer)) return;
            NotePay(payer);
        }

        public static void NotePay(Pawn guest)
        {
            if (!Allowed(guest, LifeCue.Pay, out bool vis)) return;
            var map = guest.Map;
            float angle = 0f;
            if (vis)
            {
                var till = Tills.TillSpot(map);
                var d = (till - guest.Position).ToVector3();
                angle = d.sqrMagnitude < 0.5f ? 0f : d.AngleFlat();
            }
            // up and a little towards the till: the coins leave the hand and travel half a cell, then fade
            Fleck(guest.DrawPos + new Vector3(0f, 0f, 0.35f), map, LifeFxDefOf.HH_Fleck_Life_Coins, 0.6f, vis, angle, 0.55f);
            if (vis) Play(LifeFxDefOf.HH_Life_Pay, guest, 0.8f);
            Count(LifeCue.Pay);
        }

        /// <summary>A guest tipped whoever served them: one coin flipped over to the server with a tiny ring.</summary>
        public static void NoteTip(Pawn guest, Pawn server)
        {
            if (!Allowed(guest, LifeCue.Tip, out bool vis)) return;
            var map = guest.Map;
            float angle = 0f;
            if (server != null && server.Spawned && server.Map == map)
            {
                var d = (server.Position - guest.Position).ToVector3();
                if (d.sqrMagnitude > 0.5f) angle = d.AngleFlat();
            }
            Fleck(guest.DrawPos + new Vector3(0f, 0f, 0.45f), map, LifeFxDefOf.HH_Fleck_Life_TipCoin, 0.4f, vis, angle, 0.7f);
            if (vis) Play(LifeFxDefOf.HH_Life_Tip, guest, 0.7f);
            Count(LifeCue.Tip);
        }

        /// <summary>
        /// The group leaves after its stay: each guest waves (a small hand), the group gets one soft farewell chime, and a moment later every guest's
        /// review shows above the head (1-5 stars; 5 stars sparkle with a sound, 1-2 stars only get a dry puff).
        /// </summary>
        public static void NoteDeparture(int groupId, List<(Pawn pawn, int stars, bool wave)> guests)
        {
            if (!On || guests == null || guests.Count == 0) return;
            bool chime = !groupsWaved.Contains(groupId);
            if (groupsWaved.Count > 256) groupsWaved.Clear();
            groupsWaved.Add(groupId);
            int n = 0;
            foreach (var g in guests)
            {
                var p = g.pawn;
                if (p == null || !p.Spawned) continue;
                if (g.wave) { if (Wave(p, chime, 0)) chime = false; }
                int stars = Mathf.Clamp(g.stars, 1, 5);
                // the stars come one after another (a short stagger per guest), so a large group does not spend the map's cue budget in one tick
                if (g.stars > 0) { int k = n++; Later(ReviewDelay + k * ReviewStagger, () => ShowReview(p, stars, 0)); }
            }
        }

        /// <summary>One wave (and, for the first visible guest of the group, the farewell chime). True when the chime was played.</summary>
        static bool Wave(Pawn p, bool chime, int tries)
        {
            var gate = Gate(p, LifeCue.Farewell, out bool vis);
            if (gate == GateResult.Busy) { if (tries < BusyRetries) Later(BusyRetryTicks, () => Wave(p, false, tries + 1)); return false; }   // a late wave never chimes (the next guest may have)
            if (gate != GateResult.Ok) return false;
            Fleck(p.DrawPos + new Vector3(0.2f, 0f, 0.6f), p.Map, LifeFxDefOf.HH_Fleck_Life_Wave, 0.6f, vis, 0f, 0.2f);
            bool played = false;
            if (vis && chime) { Play(LifeFxDefOf.HH_Life_Farewell, p, 0.85f); played = true; }
            Count(LifeCue.Farewell);
            return played;
        }

        static void ShowReview(Pawn p, int stars, int tries)
        {
            var gate = Gate(p, LifeCue.Review, out bool vis);
            if (gate == GateResult.Busy) { if (tries < BusyRetries) Later(BusyRetryTicks, () => ShowReview(p, stars, tries + 1)); return; }
            if (gate != GateResult.Ok) return;
            var map = p.Map;
            FleckDef def = stars == 1 ? LifeFxDefOf.HH_Fleck_Life_Stars1 : stars == 2 ? LifeFxDefOf.HH_Fleck_Life_Stars2 : stars == 3 ? LifeFxDefOf.HH_Fleck_Life_Stars3
                         : stars == 4 ? LifeFxDefOf.HH_Fleck_Life_Stars4 : LifeFxDefOf.HH_Fleck_Life_Stars5;
            var at = p.DrawPos + new Vector3(0f, 0f, 0.95f);
            Fleck(at, map, def, 1f, vis, 0f, 0.12f);
            if (stars >= 5)
            {
                for (int i = 0; i < 3; i++) Fleck(at + new Vector3(Rand.Range(-0.5f, 0.5f), 0f, Rand.Range(-0.1f, 0.2f)), map, LifeFxDefOf.HH_Fleck_Life_Sparkle, Rand.Range(0.35f, 0.55f), vis, Rand.Range(-40f, 40f), Rand.Range(0.2f, 0.5f));
                if (vis) Play(LifeFxDefOf.HH_Life_FiveStars, p, 0.8f);
            }
            else if (stars <= 2) Fleck(at + new Vector3(0.45f, 0f, -0.1f), map, LifeFxDefOf.HH_Fleck_Life_Puff, 0.55f, vis, 90f, 0.25f);   // a dry little puff, no sound
            LastStars = stars;
            Count(LifeCue.Review);
        }

        /// <summary>A couple lay down in their shared double bed: one small heart between them (once a night).</summary>
        public static void NoteHeart(Pawn a, Pawn b)
        {
            if (a == null || b == null || !a.Spawned || !b.Spawned || a.Map != b.Map) return;
            if (!Allowed(a, LifeCue.Heart, out bool vis)) return;
            Stamp(b, LifeCue.Heart);                                               // one heart per couple, not one per partner
            var map = a.Map;
            Fleck((a.DrawPos + b.DrawPos) * 0.5f + new Vector3(0f, 0f, 0.5f), map, LifeFxDefOf.HH_Fleck_Life_Heart, 0.55f, vis, 0f, 0.18f);
            Count(LifeCue.Heart);
        }

        // ------------------------------------------------------------------ per tick (from the Core content module)
        public static void Tick(int now)
        {
            if (queue.Count > 0)
                for (int i = queue.Count - 1; i >= 0; i--)
                {
                    if (queue[i].tick > now) continue;
                    var a = queue[i].act; queue.RemoveAt(i);
                    try { a(); } catch (Exception e) { HLog.Once("life.fx.queue", LogLevel.Warn, "Core", "Guest life cue failed: " + e.Message); }
                }
            if (arrivals.Count > 0 && now % 20 == 7) TickArrivals(now);
            if (now % 2500 == 1291) Prune(now);
        }

        static void TickArrivals(int now)
        {
            for (int i = arrivals.Count - 1; i >= 0; i--)
            {
                var (gid, pawns, since) = arrivals[i];
                Pawn first = null; bool anyLeft = false;
                for (int k = 0; k < pawns.Count; k++)
                {
                    var q = pawns[k];
                    if (q == null || q.Dead || q.Destroyed || (!q.Spawned && (q.ParentHolder == null || Find.WorldPawns.Contains(q)))) continue;   // gone (or back among the world pawns); a pawn in a drop pod is still coming
                    anyLeft = true;
                    if (q.Spawned) { first = q; break; }
                }
                if (first == null) { if (!anyLeft || now - since > 6000) arrivals.RemoveAt(i); continue; }   // still in the drop pods (or gone)
                if (!On || groupsGreeted.Contains(gid)) { arrivals.RemoveAt(i); continue; }
                bool vis = Visible(first.Position, first.Map);
                if (vis && !MapAllows(first.Map)) { if (now - since > 6000) arrivals.RemoveAt(i); continue; }   // the map is busy this second: next pass
                arrivals.RemoveAt(i);
                if (groupsGreeted.Count > 256) groupsGreeted.Clear();
                groupsGreeted.Add(gid);
                Fleck(first.DrawPos + new Vector3(0f, 0f, 0.6f), first.Map, LifeFxDefOf.HH_Fleck_Life_Suitcase, 0.6f, vis, 0f, 0.25f);
                if (vis) Play(LifeFxDefOf.HH_Life_Arrival, first, 0.85f);
                Count(LifeCue.Arrival);
            }
        }

        static void Prune(int now)
        {
            pruneKeys.Clear();
            foreach (var kv in last) if (now - kv.Value > 30000) pruneKeys.Add(kv.Key);
            for (int i = 0; i < pruneKeys.Count; i++) last.Remove(pruneKeys[i]);
            if (perMap.Count > 16) perMap.Clear();
        }

        /// <summary>Tests: number of tracked entries (pauses, pending arrivals, queued reviews).</summary>
        public static (int pauses, int arrivals, int queued) TrackedCounts => (last.Count, arrivals.Count, queue.Count);

        /// <summary>Tests: forget the pauses of one pawn (so one suite can trigger the same cue twice on purpose).</summary>
        internal static void ResetPauses(Pawn p)
        {
            if (p == null) return;
            for (int c = 0; c < Fired.Length; c++) last.Remove(Key(p, (LifeCue)c));
        }

        // ------------------------------------------------------------------ helpers
        static long Key(Pawn p, LifeCue c) => (long)p.thingIDNumber * 8 + (int)c;

        static void Stamp(Pawn p, LifeCue c) => last[Key(p, c)] = Find.TickManager.TicksGame;

        enum GateResult { Ok, No, Busy }

        /// <summary>
        /// The setting is on, the pawn is on a map and its pause for this cue is over; a cue the player can see must also fit into the map's budget
        /// for this second (cues off-screen draw nothing and do not spend it). <paramref name="vis"/>: the cue is on screen.
        /// </summary>
        static GateResult Gate(Pawn p, LifeCue c, out bool vis)
        {
            vis = false;
            if (!On || p == null || !p.Spawned || p.Map == null) return GateResult.No;
            int now = Find.TickManager.TicksGame;
            if (last.TryGetValue(Key(p, c), out int t) && now - t < Gap[(int)c]) return GateResult.No;
            vis = Visible(p.Position, p.Map);
            if (vis && !MapAllows(p.Map)) return GateResult.Busy;
            Stamp(p, c);
            return GateResult.Ok;
        }

        static bool Allowed(Pawn p, LifeCue c, out bool vis) => Gate(p, c, out vis) == GateResult.Ok;

        static bool MapAllows(Map map)
        {
            int now = Find.TickManager.TicksGame, sec = now / 60;
            perMap.TryGetValue(map.uniqueID, out var e);
            if (e.tick != sec) e = (sec, 0);
            if (e.count >= MapCuesPerSecond) return false;
            perMap[map.uniqueID] = (sec, e.count + 1);
            return true;
        }

        static void Count(LifeCue c) => Fired[(int)c]++;

        static void Later(int ticks, Action a) { if (a == null) return; if (queue.Count > 128) queue.RemoveAt(0); queue.Add((Find.TickManager.TicksGame + Math.Max(0, ticks), a)); }

        /// <summary>Only what the player can see gets drawn and heard (the current map and the camera view).</summary>
        public static bool Visible(IntVec3 c, Map map)
        {
            if (ForceVisible.HasValue) return ForceVisible.Value && map != null;
            if (map == null || map != Find.CurrentMap) return false;
            try { return Find.CameraDriver.CurrentViewRect.ExpandedBy(3).Contains(c); } catch (Exception) { return false; }
        }

        static void Fleck(Vector3 loc, Map map, FleckDef def, float scale, bool visible, float angle, float speed)
        {
            if (def == null || map == null || !visible) return;
            try
            {
                var d = FleckMaker.GetDataStatic(loc, map, def, scale);
                d.velocityAngle = angle; d.velocitySpeed = speed;
                map.flecks.CreateFleck(d);
            }
            catch (Exception e) { HLog.Once("life.fx.fleck", LogLevel.Warn, "Core", "Guest life fleck failed: " + e.Message); }
        }

        static void Play(SoundDef def, Thing at, float volume = 1f)
        {
            if (def == null || at == null || !at.Spawned) return;
            Sounds++;
            try { var info = SoundInfo.InMap(new TargetInfo(at.Position, at.Map)); info.volumeFactor = volume; def.PlayOneShot(info); }
            catch (Exception e) { HLog.Once("life.fx.sound." + def.defName, LogLevel.Warn, "Core", "Guest life sound failed: " + e.Message); }
        }

        /// <summary>1-5 stars from the outcome of a stay (the same scale as the departure report's header).</summary>
        public static int StarsFor(string outcome)
        {
            switch (outcome)
            {
                case "great": return 5;
                case "good": return 4;
                case "fine": return 3;
                case "poor": return 2;
                case "bad": return 1;
                default: return 0;
            }
        }
    }
}
