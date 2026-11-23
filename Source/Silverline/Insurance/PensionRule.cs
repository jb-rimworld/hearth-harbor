// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Hearth;
using Hearth.Insurance;
using RimWorld;
using UnityEngine;
using Verse;

namespace Silverline
{
    /// <summary>
    /// The pension rule (1.1.0, F26): whoever draws a daily pension (Golden Years Plan at Good Enough or Premium, age 60 or more) may have at most
    /// <c>insurance.pensionMaxWorkHours</c> hours per day set to Work in the timetable. Anything, Joy, Sleep and Meditate count as free time.
    /// A day over the limit gives a strike (checked once per day, at most one strike every two days); the 2nd outstanding strike forfeits the pension of the
    /// running period, the 3rd ends the entitlement. Strikes decay by one per quadrum. Read only: the player stays in control of the timetable.
    /// </summary>
    public static class PensionRule
    {
        public const int StrikeCooldownDays = 2;

        /// <summary>A contract the rule applies to (before the age check): annuity tier of a retirement policy.</summary>
        public static bool IsPension(PolicyContract c)
            => c != null && c.Active && c.Def != null && c.Def.dailyAnnuity > 0 && c.Def.HasTrigger(ClaimTrigger.Retirement) && c.holder != null;

        /// <summary>The holder draws (or will draw) the daily annuity: Good Enough or Premium, age reached, waiting period over.</summary>
        public static bool Drawing(PolicyContract c, int now)
            => IsPension(c) && c.tier != CoverTier.BareMinimum && !c.pensionRevoked && !c.holder.Dead && c.holder.ageTracker.AgeBiologicalYears >= 60 && !c.InWaitingPeriod(now);

        public static bool Enabled => InsSettings.PensionMode != PensionRuleMode.Off;

        /// <summary>Hours per day set to Work in the pawn's timetable (0 for pawns without a timetable).</summary>
        public static int WorkHours(Pawn p)
        {
            var tt = p?.timetable; if (tt == null) return 0;
            int n = 0;
            for (int h = 0; h < 24; h++) if (tt.GetAssignment(h) == TimeAssignmentDefOf.Work) n++;
            return n;
        }

        /// <summary>Hours set to Anything: free time in the sense of the rule, although a pawn may work in them if nothing else is wanted.</summary>
        public static int AnythingHours(Pawn p)
        {
            var tt = p?.timetable; if (tt == null) return 0;
            int n = 0;
            for (int h = 0; h < 24; h++) if (tt.GetAssignment(h) == TimeAssignmentDefOf.Anything) n++;
            return n;
        }

        /// <summary>Turns the surplus Work hours (the later ones) into Anything so that at most <paramref name="max"/> remain. Returns how many hours were changed.</summary>
        public static int FixSchedule(Pawn p, int max)
        {
            var tt = p?.timetable; if (tt == null) return 0;
            int kept = 0, changed = 0;
            for (int h = 0; h < 24; h++)
            {
                if (tt.GetAssignment(h) != TimeAssignmentDefOf.Work) continue;
                if (kept < max) { kept++; continue; }
                tt.SetAssignment(h, TimeAssignmentDefOf.Anything); changed++;
            }
            if (changed > 0) InsuranceComponent.Instance?.TouchStatus();
            return changed;
        }

        /// <summary>True when the schedule breaks the rule right now (used by the alert and the status chips).</summary>
        public static bool AtRisk(PolicyContract c, int now) => Enabled && Drawing(c, now) && WorkHours(c.holder) > InsSettings.PensionHours;

        /// <summary>Pensioners whose schedule breaks the rule right now.</summary>
        public static List<Pawn> PensionersAtRisk()
        {
            var res = new List<Pawn>();
            var comp = InsuranceComponent.Instance; if (comp == null || !Enabled) return res;
            int now = Ins.Now;
            foreach (var c in comp.contracts) if (AtRisk(c, now) && c.holder.Spawned && !res.Contains(c.holder)) res.Add(c.holder);
            return res;
        }
    }

    /// <summary>The alert in the vanilla alert column (found by the game by reflection).</summary>
    public class Alert_PensionSchedule : Alert
    {
        List<Pawn> cache = new List<Pawn>(); int cacheTick = -9999;

        public Alert_PensionSchedule()
        {
            defaultPriority = AlertPriority.Medium;
            defaultLabel = "Pension at risk";   // the translated label comes from GetLabel
        }

        List<Pawn> Pensioners
        {
            get
            {
                int now = Find.TickManager.TicksGame;
                if (now - cacheTick >= 250 || now < cacheTick)
                {
                    cacheTick = now;
                    cache = Modules.On(ModuleId.Bank) && (InsuranceComponent.Instance?.Started ?? false) ? PensionRule.PensionersAtRisk() : new List<Pawn>();
                }
                return cache;
            }
        }

        public override string GetLabel() => Ins.T("Pension.Alert.Label");

        public override TaggedString GetExplanation()
            => Guard.Run(ModuleId.Bank, "Insurance.PensionAlertText", () => (TaggedString)Ins.T("Pension.Alert.Body", InsSettings.PensionHours, string.Join(", ", Pensioners.Select(p => p.LabelShortCap + " (" + PensionRule.WorkHours(p) + " h)"))), (TaggedString)"");

        public override AlertReport GetReport()
        {
            if (!Modules.On(ModuleId.Bank)) return AlertReport.Inactive;
            return Guard.Run(ModuleId.Bank, "Insurance.PensionAlert", () => { var l = Pensioners; return l.Count == 0 ? AlertReport.Inactive : AlertReport.CulpritsAre(l); }, AlertReport.Inactive);
        }
    }

    public sealed partial class InsuranceComponent
    {
        public void TouchStatus() { statusVersion++; }

        public bool PensionSuspended(PolicyContract c, int now) => c.pensionRevoked || c.pensionSuspendedUntil > now;

        /// <summary>Once per game day: pension notice before the 60th birthday, the schedule check, strikes and their consequences.</summary>
        void PensionDailyChecks(int now)
        {
            if (!Modules.On(ModuleId.Bank)) return;
            var mode = InsSettings.PensionMode;
            int day = now / ClaimEngine.Day, qi = (int)InsuranceTime.QuadrumIndex();
            foreach (var c in contracts.Where(PensionRule.IsPension).ToList())
            {
                if (c.holder.Dead) continue;
                float age = c.holder.ageTracker.AgeBiologicalYearsFloat;
                // seven days before the 60th birthday (a year has 60 days): what the pension rule means
                if (!c.pensionNoticed && mode != PensionRuleMode.Off && c.tier != CoverTier.BareMinimum && age < 60f && age >= 60f - 7f / 60f)
                {
                    c.pensionNoticed = true;
                    ClaimLetters.Notice(Ins.T("Pension.Notice.Title", c.holder.LabelShortCap), Ins.T("Pension.Notice.Body", c.holder.LabelShortCap, InsSettings.PensionHours), false, false,
                        c.holder.Spawned ? new LookTargets(c.holder) : null);
                }
                if (mode == PensionRuleMode.Off || !PensionRule.Drawing(c, now)) continue;
                if (c.pensionCheckDay == day) continue;
                c.pensionCheckDay = day;
                int hours = PensionRule.WorkHours(c.holder);
                c.pensionWorkHours = hours;
                if (hours <= InsSettings.PensionHours) continue;
                if (c.pensionLastStrikeDay >= 0 && day - c.pensionLastStrikeDay < PensionRule.StrikeCooldownDays) continue;   // time to react to the last letter
                c.pensionLastStrikeDay = day;
                var look = c.holder.Spawned ? new LookTargets(c.holder) : null;
                string who = c.holder.LabelShortCap;
                if (mode == PensionRuleMode.WarnOnly)
                {
                    if (c.pensionWarnQuadrum == qi) continue;   // one warning per quadrum, not every second day
                    c.pensionWarnQuadrum = qi;
                    ClaimLetters.Notice(Ins.T("Pension.Warn.Title", who), Ins.T("Pension.Warn.Body", who, hours, InsSettings.PensionHours), false, true, look);
                    continue;
                }
                bool legal = HasActive("SL_LegalShield") && c.pensionLegalQuadrum != qi;
                if (legal && c.pensionStrikes == 0)
                {
                    c.pensionLegalQuadrum = qi;
                    ClaimLetters.Notice(Ins.T("Pension.Legal.Title", who), Ins.T("Pension.Legal.Body", who, hours, InsSettings.PensionHours), false, true, look);
                    statusVersion++;
                    continue;
                }
                c.pensionStrikes++; statusVersion++;
                Log(Ins.T("Log.PensionStrike", who, c.pensionStrikes));
                InsSounds.Play("SL_PensionGong");
                if (c.pensionStrikes == 1)
                    ClaimLetters.Notice(Ins.T("Pension.Strike1.Title", who), Ins.T("Pension.Strike1.Body", who, hours, InsSettings.PensionHours), false, true, look);
                else if (c.pensionStrikes == 2)
                {
                    double lost = c.accrued; c.accrued = 0;
                    int from = c.accruedSince >= 0 ? c.accruedSince : now;
                    c.pensionSuspendedUntil = Math.Max(now, from) + InsSettings.Billing * ClaimEngine.Day; c.accruedSince = -1;
                    ClaimLetters.Notice(Ins.T("Pension.Strike2.Title", who), Ins.T("Pension.Strike2.Body", who, hours, InsSettings.PensionHours, Ins.Silver(lost)), false, true, look);
                }
                else
                {
                    c.pensionRevoked = true;
                    ClaimLetters.Notice(Ins.T("Pension.Strike3.Title", who), Ins.T("Pension.Strike3.Body", who, hours, InsSettings.PensionHours), false, true, look);
                    End(c, Ins.T("End.Pension"));
                }
            }
        }
    }
}
