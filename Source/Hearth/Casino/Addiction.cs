// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Hearth.CasinoMath;

namespace Hearth.Casino
{
    /// <summary>Gambling addiction of colonists, a full citizen of vanilla's addiction system (KONZEPT 6.12).</summary>
    public class Hediff_GamblingAddiction : Hediff_Addiction
    {
        public float depth;
        public int lastSessionTick = int.MinValue;
        public int sessions;
        int lastUpdateTick = -1;

        public int Stage => GamblingMath.StageFromDepth(depth);
        public bool InWithdrawal => Need != null && Need.CurCategory == DrugDesireCategory.Withdrawal;
        public double AbstinenceDays => lastSessionTick == int.MinValue ? 0 : (Find.TickManager.TicksGame - (double)lastSessionTick) / 60000.0;
        public double DaysNeeded => GamblingMath.AbstinenceDaysNeeded(Stage, CS.F(CS.AbstinenceDays));

        public override int CurStageIndex => Math.Min(def.stages.Count - 1, InWithdrawal ? Stage : 0);

        public override string LabelInBrackets => "HH.Casino.AddictionBrackets".Translate(Stage, AbstinenceDays.ToString("0.#"), DaysNeeded.ToString("0")).ToString();

        public override string TipStringExtra
        {
            get
            {
                var sb = new System.Text.StringBuilder(base.TipStringExtra);
                if (sb.Length > 0) sb.Append("\n");
                sb.Append("HH.Casino.AddictionTip".Translate(Stage, AbstinenceDays.ToString("0.#"), DaysNeeded.ToString("0"), (GamblingMath.NeedFallPerDay(Stage) / GamblingMath.SessionFill).ToString("0.#")));
                string hint = CasinoUI.HelpHintText();
                if (!hint.NullOrEmpty()) sb.Append("\n\n").Append(hint);
                return sb.ToString();
            }
        }

        /// <summary>A session: the need is filled, the addiction digs in, the abstinence counter restarts.</summary>
        public void Notify_Session(int now)
        {
            depth = Mathf.Min(1f, depth + (float)GamblingMath.DepthPerSession);
            lastSessionTick = now; sessions++;
            var n = Need; if (n != null) n.CurLevel = Mathf.Min(1f, n.CurLevel + (float)GamblingMath.SessionFill);
            Severity = 1f;
            pawn.health.Notify_HediffChanged(this);
        }

        public override void PostAdd(DamageInfo? dinfo)
        {
            base.PostAdd(dinfo);
            if (lastSessionTick == int.MinValue) lastSessionTick = Find.TickManager.TicksGame;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref depth, "depth"); Scribe_Values.Look(ref lastSessionTick, "lastSession", int.MinValue);
            Scribe_Values.Look(ref sessions, "sessions"); Scribe_Values.Look(ref lastUpdateTick, "lastUpdate", -1);
        }

#if RW17
        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (pawn.IsHashIntervalTick(150, delta)) SlowTick();
        }
#else
        public override void Tick()
        {
            base.Tick();
            if (pawn.IsHashIntervalTick(150)) SlowTick();
        }
#endif

        void SlowTick()
        {
            if (!Modules.On(ModuleId.Casino)) return;
            Guard.Run(ModuleId.Casino, "Gambling.SlowTick", () =>
            {
                int now = Find.TickManager.TicksGame;
                int elapsed = lastUpdateTick < 0 ? 150 : Math.Min(now - lastUpdateTick, 2500);
                lastUpdateTick = now;
                // abstinence: no session since lastSessionTick
                depth = Mathf.Max(0f, depth - (float)(GamblingMath.DepthRecoveryPerDay * elapsed / 60000.0));
                double need = DaysNeeded, abst = AbstinenceDays;
                Severity = Mathf.Clamp((float)(1.0 - abst / Math.Max(0.01, need)), 0.001f, 1f);
                if (abst >= need) { Gambling.Cure(this); return; }
                // gambling spree in deep withdrawal
                if (InWithdrawal && !pawn.InMentalState && !pawn.Downed && pawn.Spawned && Gambling.IsColonist(pawn))
                {
                    double mtb = (HearthProfile.Current?.Tier ?? 3f) >= 2.5f ? GamblingMath.SpreeMtbDays(Stage) : 0;   // Bed & Breakfast and easier: no withdrawal breakdowns
                    if (mtb > 0 && Rand.MTBEventOccurs((float)mtb, 60000f, 150f))
                        pawn.mindState.mentalStateHandler.TryStartMentalState(CasinoDefOf.HH_GamblingSpree, "HH.Casino.SpreeReason".Translate().ToString(), forced: false);
                }
            });
        }
    }

    /// <summary>The need of the addict. It falls faster the deeper the addiction (0.5 · 1.5^(stage−1) per day).</summary>
    public class Need_Gambling : Need_Chemical
    {
        public Need_Gambling(Pawn pawn) : base(pawn) { }
        public override void NeedInterval()
        {
            if (IsFrozen) return;
            var h = AddictionHediff as Hediff_GamblingAddiction;
            int stage = h?.Stage ?? 1;
            CurLevel -= (float)(GamblingMath.NeedFallPerDay(stage) / 60000.0 * 150.0);
        }
    }

    /// <summary>Withdrawal mood: −10 / −15 / −22 / −30 / −40 by addiction stage, only while the need is empty.</summary>
    public class ThoughtWorker_GamblingWithdrawal : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            var h = p.health?.hediffSet?.GetFirstHediffOfDef(CasinoDefOf.HH_GamblingAddiction) as Hediff_GamblingAddiction;
            if (h == null || !h.InWithdrawal) return ThoughtState.Inactive;
            return ThoughtState.ActiveAtStage(Math.Max(0, Math.Min(def.stages.Count - 1, h.Stage - 1)));
        }
    }

    /// <summary>Think node of the "gambling spree" mental state: go to the nearest device and play, ignoring the drug policy.</summary>
    public class JobGiver_GamblingSpree : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!Modules.On(ModuleId.Casino) || pawn.Map == null || !Hearth.Dlc.LuckApi.WillGamble(pawn)) return null;
            var comp = CasinoJobs.FindDeviceFor(pawn, guest: false, ignoreJoyRules: true);
            return comp == null ? null : CasinoJobs.MakeJob(pawn, comp);
        }
    }

    /// <summary>Addicts whose need is nearly empty (stage 3 and up) leave their work to gamble if the policy allows it.</summary>
    public class JobGiver_SatisfyGamblingNeed : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!Modules.On(ModuleId.Casino) || pawn.Map == null || !pawn.Spawned) return null;
            var h = Gambling.AddictionOf(pawn); if (h == null) return null;
            var need = h.Need; if (need == null) return null;
            if (h.Stage < 3 || need.CurLevel > 0.2f) return null;           // mild addicts only play in their free time (joy giver)
            if (!Gambling.IsColonist(pawn) || pawn.Downed || pawn.Drafted || !Hearth.Dlc.LuckApi.WillGamble(pawn)) return null;
            if (!Gambling.PolicyAllowsAddiction(pawn)) return null;
            var comp = CasinoJobs.FindDeviceFor(pawn, guest: false, ignoreJoyRules: true);
            return comp == null ? null : CasinoJobs.MakeJob(pawn, comp);
        }
    }

    public static class Gambling
    {
        public static bool IsColonist(Pawn p) => p != null && p.Faction == Faction.OfPlayer && p.RaceProps.Humanlike && !p.IsSlave && !p.IsPrisoner;
        public static bool IsAdult(Pawn p) => p.DevelopmentalStage.Adult();
        public static Hediff_GamblingAddiction AddictionOf(Pawn p) => p?.health?.hediffSet?.GetFirstHediffOfDef(CasinoDefOf.HH_GamblingAddiction) as Hediff_GamblingAddiction;

        /// <summary>Trait, gene and mood factors of the addiction chance (KONZEPT 6.12 "F").</summary>
        public static double Factor(Pawn p)
        {
            double f = 1.0; var t = p.story?.traits;
            if (t != null)
            {
                var dd = DefDatabase<TraitDef>.GetNamedSilentFail("DrugDesire");
                if (dd != null && t.HasTrait(dd)) { int deg = t.DegreeOfTrait(dd); if (deg >= 2) f *= 1.5; else if (deg == 1) f *= 1.25; }
                var asc = DefDatabase<TraitDef>.GetNamedSilentFail("Ascetic"); if (asc != null && t.HasTrait(asc)) f *= 0.5;
            }
            // our traits and genes act through the HH_GamblingAddiction stat (gambler x1.5, compulsive x2, high roller x1.3, addiction-proof x0.2, averse 0)
            f *= Hearth.Dlc.LuckApi.AddictionFactor(p);
            if (p.needs?.mood != null && p.needs.mood.CurLevelPercentage < 0.3f) f *= 1.3;
            return f;
        }

        public static double P0 => HSettings.Get(CS.AddictionP0);
        public static double ChanceFor(Pawn p, int sessionsInWindow) => GamblingMath.AddictionChance(sessionsInWindow, P0, Factor(p), HSettings.Get(CS.AddictionGrowth), HSettings.GetInt(CS.AddictionFree));

        /// <summary>Counts a finished colonist session: thoughts, the 3-day counter, and the addiction roll.</summary>
        public static void OnColonistSessionEnd(GamblerSession s, RngStream rng = null)
        {
            var pawn = s.Pawn; var comp = CasinoComponent.Get();
            if (pawn == null || pawn.Dead || comp == null || s.Rounds < 5) return;
            int now = Find.TickManager.TicksGame;
            if (pawn.needs?.mood != null)
            {
                if (s.Rounds >= 10 && s.Net.Silver >= 0.2 * s.BuyIn.Silver) pawn.needs.mood.thoughts.memories.TryGainMemory(CasinoDefOf.HH_WonAtTheTables);
                else if (s.Rounds >= 10 && s.Purse.Silver < CS.F(CS.MinBet)) pawn.needs.mood.thoughts.memories.TryGainMemory(CasinoDefOf.HH_LostAtTheTables);
            }
            RegisterSession(pawn, now, rng);
        }

        /// <summary>Registers one session for the pawn at <paramref name="now"/> and rolls for addiction (public so tests can drive it with a seeded stream).</summary>
        public static bool RegisterSession(Pawn pawn, int now, RngStream rng = null)
        {
            var comp = CasinoComponent.Get(); if (comp == null || !IsAdult(pawn)) return false;
            rng = rng ?? JRand.Stream("casino.addiction");
            var rec = comp.RecordOf(pawn, true);
            rec.SessionTicks.Add(now); rec.TotalSessions++; rec.LastSessionTick = now;
            int n3 = rec.CountInWindow(now);
            var add = AddictionOf(pawn);
            if (add != null) { add.Notify_Session(now); return false; }
            if (P0 <= 0) return false;
            if (rec.CravingUntil > now)
            {
                // craving memory after a cure: a session brings the addiction back one stage lower (p 0.5)
                if (rng.Chance(GamblingMath.RelapseChance)) { Addict(pawn, Math.Max(1, rec.CuredStage - 1), true); rec.CravingUntil = -1; return true; }
                return false;
            }
            if (rng.Chance(ChanceFor(pawn, n3))) { Addict(pawn, 1, false); return true; }
            return false;
        }

        public static Hediff_GamblingAddiction Addict(Pawn pawn, int stage, bool relapse)
        {
            if (AddictionOf(pawn) != null) return AddictionOf(pawn);
            var h = (Hediff_GamblingAddiction)HediffMaker.MakeHediff(CasinoDefOf.HH_GamblingAddiction, pawn);
            h.depth = (stage - 1) * (float)GamblingMath.DepthPerStage + 0.02f;
            h.lastSessionTick = Find.TickManager.TicksGame;
            h.Severity = 1f;
            pawn.health.AddHediff(h);
            var need = h.Need; if (need != null) need.CurLevel = 0.7f;
            if (PawnUtility.ShouldSendNotificationAbout(pawn))
            {
                string hint = CasinoUI.HelpHintText();
                Find.LetterStack.ReceiveLetter(
                    (relapse ? "HH.Casino.RelapseTitle" : "HH.Casino.AddictedTitle").Translate(pawn.LabelShortCap),
                    (relapse ? "HH.Casino.RelapseText" : "HH.Casino.AddictedText").Translate(pawn.LabelShortCap, pawn.Named("PAWN")) + (hint.NullOrEmpty() ? "" : "\n\n" + hint),
                    LetterDefOf.NegativeEvent, pawn);
            }
            return h;
        }

        public static void Cure(Hediff_GamblingAddiction h)
        {
            var pawn = h.pawn; var comp = CasinoComponent.Get(); int stage = h.Stage;
            pawn.health.RemoveHediff(h);
            var rec = comp?.RecordOf(pawn, true);
            if (rec != null) { rec.CravingUntil = Find.TickManager.TicksGame + GamblingMath.CravingDays * 60000; rec.CuredStage = stage; rec.EverAddicted = true; }
            if (PawnUtility.ShouldSendNotificationAbout(pawn))
                Messages.Message("HH.Casino.CuredMessage".Translate(pawn.LabelShortCap, GamblingMath.CravingDays), pawn, MessageTypeDefOf.PositiveEvent);
        }

        // Drug policy ------------------------------------------------------------------------------------------------------
        public static DrugPolicyEntry EntryOf(DrugPolicy policy)
        {
            if (policy == null) return null;
            for (int i = 0; i < policy.Count; i++) if (policy[i].drug == CasinoDefOf.HH_GamblingSession) return policy[i];
            return null;
        }

        /// <summary>The default ("It just works") settings of the virtual drug "gambling": allowed for joy, at most every 1.5 days.</summary>
        public static int InitPolicies()
        {
            var comp = CasinoComponent.Get(); var db = Current.Game?.drugPolicyDatabase; if (comp == null || db == null) return 0;
            int n = 0;
            foreach (var pol in db.AllPolicies)
            {
                if (comp.InitializedPolicies.Contains(pol.id)) continue;
                var e = EntryOf(pol); if (e == null) continue;
                e.allowedForJoy = true; e.allowedForAddiction = true; e.allowScheduled = false; e.daysFrequency = 1.5f;
                comp.InitializedPolicies.Add(pol.id); n++;
            }
            return n;
        }

        public static bool PolicyAllowsAddiction(Pawn p)
        {
            var e = EntryOf(p.drugs?.CurrentPolicy);
            return e == null || e.allowedForAddiction;
        }

        /// <summary>May this pawn gamble for fun right now? Reads the "Gambling" line of the drug policy (allowed for joy, frequency, mood and joy limits).</summary>
        public static bool PolicyAllowsJoy(Pawn p, out string why)
        {
            why = null;
            var e = EntryOf(p.drugs?.CurrentPolicy);
            if (e == null) { why = "no policy line"; return false; }
            if (!e.allowedForJoy) { why = "policy forbids it"; return false; }
            if (e.onlyIfMoodBelow < 1f && p.needs?.mood != null && p.needs.mood.CurLevelPercentage >= e.onlyIfMoodBelow) { why = "mood too good"; return false; }
            if (e.onlyIfJoyBelow < 1f && p.needs?.joy != null && p.needs.joy.CurLevelPercentage >= e.onlyIfJoyBelow) { why = "joy too high"; return false; }
            var rec = CasinoComponent.Get()?.RecordOf(p, false);
            if (rec != null)
            {
                int now = Find.TickManager.TicksGame;
                if (e.daysFrequency < 1f)
                {
                    int perDay = Mathf.Max(1, Mathf.RoundToInt(1f / e.daysFrequency));
                    int today = rec.SessionTicks.Count(t => now - t < 60000);
                    if (today >= perDay) { why = "daily limit"; return false; }
                }
                else if (now - rec.LastSessionTick < e.daysFrequency * 60000f) { why = "too soon after the last session"; return false; }
            }
            return true;
        }

        /// <summary>"2 of 3 sessions in 3 days – the next one risks addiction" (KONZEPT 6.12), or null.</summary>
        public static string CounterText(Pawn p)
        {
            var comp = CasinoComponent.Get(); if (comp == null) return null;
            var rec = comp.RecordOf(p, false); int n = rec?.CountInWindow(Find.TickManager.TicksGame) ?? 0;
            if (n == 0 && AddictionOf(p) == null) return null;
            int free = HSettings.GetInt(CS.AddictionFree);
            if (AddictionOf(p) != null) return "HH.Casino.CounterAddicted".Translate(n).ToString();
            if (n >= free) return "HH.Casino.CounterRisk".Translate(n, ChanceFor(p, n + 1).ToString("P0")).ToString();
            if (n == free - 1) return "HH.Casino.CounterNext".Translate(n, free).ToString();
            return "HH.Casino.Counter".Translate(n, free).ToString();
        }
    }
}
