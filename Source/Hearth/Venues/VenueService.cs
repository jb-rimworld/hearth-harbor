// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Hearth.ShopsFood
{
    /// <summary>
    /// Serving (F14, 1.1.0): the Social skill of whoever is on duty drives the service — friendlier guests, bigger purchases, tips and
    /// quicker sales — and a shift leaves a mood: busy shifts please, standing around for hours with nothing to do bores.
    /// Shift records are runtime only (a reload starts a new shift); the thoughts themselves are ordinary saved memories.
    /// Other modules report a guest they served with <see cref="NoteServed"/> (and use <see cref="Quality"/> for their own effects).
    /// </summary>
    public static class VenueService
    {
        // ------------------------------------------------------------------ service quality

        /// <summary>0..1.1: how good the service of this pawn is. Social 0 → 0.25, Social 20 → 1.0, plus a little for a passion.
        /// The setting "Weight of the Social skill" pulls it towards 0.6 (weight 0) or stretches it (weight 2).</summary>
        public static float Quality(Pawn staffer)
        {
            if (staffer == null) return 0f;
            var sk = staffer.skills?.GetSkill(SkillDefOf.Social);
            if (sk == null || sk.TotallyDisabled) return 0.3f;
            float raw = 0.25f + 0.75f * Mathf.Clamp01(sk.Level / 20f);
            if (sk.passion == Passion.Minor) raw += 0.05f;
            else if (sk.passion == Passion.Major) raw += 0.10f;
            float w = Mathf.Max(0f, VS.F(VS.ServiceSkill));
            return Mathf.Clamp(0.6f + (raw - 0.6f) * w, 0.05f, 1.1f);
        }

        /// <summary>Satisfaction a served guest gets (points/100): 0.4 .. 2 points.</summary>
        public static float SatisfactionBonus(float q) => 0.004f + 0.016f * Mathf.Clamp01(q);
        /// <summary>Factor on how much a guest buys when somebody serves: 0.9 (poor) .. 1.12 (excellent).</summary>
        public static float SpendFactor(float q) => 0.9f + 0.2f * Mathf.Clamp(q, 0f, 1.1f);
        /// <summary>Factor on the browsing time when somebody serves: a skilled tender closes the sale faster.</summary>
        public static float BrowseFactor(float q) => Mathf.Clamp(1.15f - 0.35f * q, 0.7f, 1.15f);

        /// <summary>Does the guest tip, and how much? Chance and size grow with the quality; at most venues.tipShare of the price.</summary>
        public static Money RollTip(float q, Money price, RngStream rng)
        {
            float share = VS.F(VS.TipShare);
            if (share <= 0f || !price.IsPositive || q <= 0.2f) return Money.Zero;
            if (!rng.Chance(Mathf.Clamp01((q - 0.2f) * 0.75f))) return Money.Zero;
            double tip = price.Silver * share * Mathf.Clamp01(q) * rng.Range(0.4, 1.0);
            tip = Math.Round(tip, 2);
            return tip < 0.05 ? Money.Zero : Money.FromSilver(tip);
        }

        /// <summary>
        /// After a served sale: the guest may add a tip (<see cref="RollTip"/>), charged like the sale and booked under the same category.
        /// Shops, taverns and restaurants share this. Returns the tip (zero when none).
        /// </summary>
        /// <summary>Tests only: the tip TryTip charges instead of rolling one (null = roll).</summary>
        internal static Money? ForceTipForTests;

        public static Money TryTip(Pawn staffer, Pawn guest, Venue venue, Money price, string incomeCat, float q)
        {
            if (staffer == null || guest == null || venue == null) return Money.Zero;
            var tip = ForceTipForTests ?? RollTip(q, price, JRand.Stream("venues.tip"));
            if (!tip.IsPositive || tip > (GuestApi.Info(guest)?.Budget ?? Money.Zero)) return Money.Zero;
            string memo = venue.DisplayNameOf() + ": " + "HH.Serve.TipMemo".TranslateOr("tip for {0}").Replace("{0}", staffer.LabelShort);
            bool paid;
            Hearth.Life.GuestLifeFx.TipCharging = guest;    // F31: the tip shows its own coin flip, not the pay cue
            try { paid = GuestApi.ChargeGuest(guest, tip, incomeCat, memo); }
            finally { Hearth.Life.GuestLifeFx.TipCharging = null; }
            if (!paid) return Money.Zero;
            Hearth.Life.GuestLifeFx.NoteTip(guest, staffer);
            (venue.Comp as CompVenueSignHH)?.RecordTip(tip);
            if (staffer.Spawned)
                MoteMaker.ThrowText(staffer.DrawPos + new Vector3(0f, 0f, 0.35f), staffer.Map, "HH.Serve.TipMote".TranslateOr("tip +{0}").Replace("{0}", tip.ToStringShort()), new Color(0.55f, 0.9f, 0.55f), 2.5f);
            return tip;
        }

        /// <summary>"Anna (Social 12) · 5 served this shift".</summary>
        public static string TenderLine(Pawn p)
        {
            if (p == null) return "";
            int social = p.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0;
            string s = p.LabelShort + " (" + SkillDefOf.Social.LabelCap + " " + social + ")";
            if (shifts.TryGetValue(p.thingIDNumber, out var sh))
                s += " · " + "HH.Serve.ServedShift".TranslateOr("{0} served this shift").Replace("{0}", sh.Served.ToString());
            return s;
        }

        /// <summary>
        /// One paid sale with somebody on duty (shop, tavern, restaurant): the tender learns Social, the guest enjoys friendly service,
        /// the shift counts one more guest. Returns the service quality used.
        /// </summary>
        public static float OnServedSale(Pawn staffer, Pawn guest)
        {
            if (staffer == null) return 0f;
            float q = Quality(staffer);
            staffer.skills?.Learn(SkillDefOf.Social, 25f, false);
            if (guest != null) GuestApi.AddSatisfaction(guest, SatisfactionBonus(q), "HH.Venue.GoodService".TranslateOr("Friendly service"));
            NoteServed(staffer);
            return q;
        }

        // ------------------------------------------------------------------ shifts

        sealed class Shift
        {
            public Pawn Pawn; public Thing Sign;
            public int Start, LastTend, LastServed, Served, IdleStage;
        }

        static readonly Dictionary<int, Shift> shifts = new Dictionary<int, Shift>();
        /// <summary>A shift ends when the pawn has not tended for this long (the tend job restarts every so often to check needs).</summary>
        public const int ShiftGapTicks = 900;
        /// <summary>Shorter shifts leave no busy/idle mood.</summary>
        public const int MinShiftTicks = GenDate.TicksPerHour;

        static VenueService() { HearthGame.OnGameInit += () => shifts.Clear(); }

        public static int ShiftCount => shifts.Count;
        public static int ServedThisShift(Pawn p) => p != null && shifts.TryGetValue(p.thingIDNumber, out var s) ? s.Served : 0;

        /// <summary>The tend job starts (or restarts) at a sign: continue the running shift there or start a new one.</summary>
        public static void BeginShift(Pawn p, Thing sign)
        {
            if (p == null || sign == null) return;
            int now = Find.TickManager.TicksGame;
            if (shifts.TryGetValue(p.thingIDNumber, out var s))
            {
                if (s.Sign == sign && now - s.LastTend <= ShiftGapTicks) { s.LastTend = now; return; }
                Finish(s, now);
            }
            shifts[p.thingIDNumber] = new Shift { Pawn = p, Sign = sign, Start = now, LastTend = now, LastServed = now };
        }

        /// <summary>Called while tending (every 250 ticks): keeps the shift alive and gives the idle thought after long empty stretches.</summary>
        public static void TendTick(Pawn p)
        {
            if (p == null || !shifts.TryGetValue(p.thingIDNumber, out var s)) return;
            int now = Find.TickManager.TicksGame;
            s.LastTend = now;
            if (!VS.B(VS.ShiftThoughts)) return;
            int idleTicks = Mathf.Max(250, Mathf.RoundToInt(VS.F(VS.IdleHours) * GenDate.TicksPerHour));
            int since = now - s.LastServed;
            int due = (s.IdleStage + 1) * idleTicks;
            if (since < due) return;
            GainIdle(p, s.IdleStage);
            s.IdleStage++;
        }

        /// <summary>A guest was served by this pawn (sale, round, treatment). Counts for the busy mood and ends a bored stretch.</summary>
        public static void NoteServed(Pawn staffer, int weight = 1)
        {
            if (staffer == null) return;
            int now = Find.TickManager.TicksGame;
            if (!shifts.TryGetValue(staffer.thingIDNumber, out var s))
            {
                // served without a tend job (a croupier, a stylist): a short "shift" around the service
                s = new Shift { Pawn = staffer, Sign = null, Start = now, LastTend = now, LastServed = now };
                shifts[staffer.thingIDNumber] = s;
            }
            s.Served += Math.Max(1, weight);
            s.LastServed = now; s.LastTend = Math.Max(s.LastTend, now);
            s.IdleStage = 0;
        }

        /// <summary>Ends shifts nobody tends any more (called every 250 ticks per map; cheap).</summary>
        public static void Sweep(Map map)
        {
            if (shifts.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            List<int> done = null;
            foreach (var kv in shifts)
            {
                var s = kv.Value;
                if (s.Pawn == null || s.Pawn.Destroyed) { (done ?? (done = new List<int>())).Add(kv.Key); continue; }
                if (s.Pawn.MapHeld != map && s.Pawn.MapHeld != null) continue;
                if (now - s.LastTend > ShiftGapTicks) { Finish(s, now); (done ?? (done = new List<int>())).Add(kv.Key); }
            }
            if (done != null) foreach (var k in done) shifts.Remove(k);
        }

        /// <summary>Ends a pawn's shift now (tests, a drafted tender).</summary>
        public static void EndShift(Pawn p)
        {
            if (p == null || !shifts.TryGetValue(p.thingIDNumber, out var s)) return;
            Finish(s, Find.TickManager.TicksGame);
            shifts.Remove(p.thingIDNumber);
        }

#if HH_TESTS
        /// <summary>Tests: moves the start (and last service) of a pawn's running shift back in time.</summary>
        internal static void BackdateShift(Pawn p, int ticks, bool alsoLastServed = true)
        {
            if (p == null || !shifts.TryGetValue(p.thingIDNumber, out var s)) return;
            s.Start -= ticks;
            if (alsoLastServed) s.LastServed -= ticks;
        }
#endif

        /// <summary>Busy stage for a rate of guests per hour: -1 none, 0 decent, 1 busy, 2 rushed.</summary>
        public static int BusyStage(float perHour, float busyAt)
        {
            if (busyAt <= 0f || perHour < busyAt) return -1;
            if (perHour >= busyAt * 3f) return 2;
            if (perHour >= busyAt * 2f) return 1;
            return 0;
        }

        static void Finish(Shift s, int now)
        {
            if (s.Pawn == null || !VS.B(VS.ShiftThoughts) || !Modules.On(ModuleId.ShopsFood)) return;
            int end = Math.Max(s.LastTend, s.LastServed);
            int len = end - s.Start;
            if (len < MinShiftTicks || s.Served <= 0) return;
            float perHour = s.Served / (len / (float)GenDate.TicksPerHour);
            int stage = BusyStage(perHour, VS.F(VS.BusyPerHour));
            if (stage < 0) return;
            var passion = s.Pawn.skills?.GetSkill(SkillDefOf.Social)?.passion ?? Passion.None;
            if (passion == Passion.Major) stage++;
            GainBusy(s.Pawn, Math.Min(stage, 2));
        }

        static void GainBusy(Pawn p, int stage)
        {
            var mem = p.needs?.mood?.thoughts?.memories;
            var def = HHVenueDefOf.HH_ServedBusy;
            if (mem == null || def == null) return;
            stage = Mathf.Clamp(stage, 0, def.stages.Count - 1);
            mem.RemoveMemoriesOfDef(def);
            mem.RemoveMemoriesOfDef(HHVenueDefOf.HH_ServedIdle);   // a busy shift ends the boredom
            mem.TryGainMemory(ThoughtMaker.MakeThought(def, stage));
        }

        static void GainIdle(Pawn p, int stage)
        {
            var mem = p.needs?.mood?.thoughts?.memories;
            var def = HHVenueDefOf.HH_ServedIdle;
            if (mem == null || def == null) return;
            // passionate talkers are bored sooner and more; a lazy pawn minds a quiet shift less
            var sk = p.skills?.GetSkill(SkillDefOf.Social);
            if (sk != null && sk.passion == Passion.Major) stage++;
            stage = Mathf.Clamp(stage, 0, def.stages.Count - 1);
            mem.RemoveMemoriesOfDef(def);
            mem.TryGainMemory(ThoughtMaker.MakeThought(def, stage));
            if (p.Spawned) MoteMaker.ThrowText(p.DrawPos + new Vector3(0f, 0f, 0.5f), p.Map, "HH.Serve.IdleMote".TranslateOr("*yawn*"), new Color(0.75f, 0.75f, 0.85f), 2.5f);
        }
    }
}
