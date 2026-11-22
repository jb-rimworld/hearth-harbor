// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Hearth;
using Hearth.Markets;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Silverline
{
    public enum ProjectKind { Palace, TradeRoad, GreatHall, Observatory, Harbour }

    /// <summary>A project announced by a faction (KONZEPT 8.7). Longs invest in it, Project Shorts bet against it.</summary>
    public sealed class FactionProject : IExposable
    {
        public int id, factionId, techLevel; public string factionDefName, factionName; public ProjectKind kind;
        public int startTick, endTick, resolvedTick = -1, lastStrikeTick = -1, strikes;
        public double volume, pOpen, pNeutral, playerHelp;
        public bool resolved, success, neutralOutcome, capped;
        public double Days => (endTick - startTick) / 60000.0;
        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id"); Scribe_Values.Look(ref factionId, "fid"); Scribe_Values.Look(ref techLevel, "tech");
            Scribe_Values.Look(ref factionDefName, "fdef"); Scribe_Values.Look(ref factionName, "fname"); Scribe_Values.Look(ref kind, "kind");
            Scribe_Values.Look(ref startTick, "start"); Scribe_Values.Look(ref endTick, "end"); Scribe_Values.Look(ref resolvedTick, "rt", -1);
            Scribe_Values.Look(ref lastStrikeTick, "strikeTick", -1); Scribe_Values.Look(ref strikes, "strikes");
            Scribe_Values.Look(ref volume, "vol"); Scribe_Values.Look(ref pOpen, "pOpen"); Scribe_Values.Look(ref pNeutral, "pNeutral"); Scribe_Values.Look(ref playerHelp, "help");
            Scribe_Values.Look(ref resolved, "resolved"); Scribe_Values.Look(ref success, "success"); Scribe_Values.Look(ref neutralOutcome, "neutral"); Scribe_Values.Look(ref capped, "capped");
        }
    }

    public sealed class ProjectBet : IExposable
    {
        public int id, projectId, tick; public bool isShort, settled; public long stake; public double quote;
        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id"); Scribe_Values.Look(ref projectId, "p"); Scribe_Values.Look(ref tick, "t"); Scribe_Values.Look(ref isShort, "short");
            Scribe_Values.Look(ref settled, "settled"); Scribe_Values.Look(ref stake, "stake"); Scribe_Values.Look(ref quote, "quote");
        }
    }

    public sealed partial class FinanceGame
    {
        public List<FactionProject> projects = new List<FactionProject>();
        public List<ProjectBet> bets = new List<ProjectBet>();
        List<int> nextProjFaction = new List<int>(), nextProjTick = new List<int>();
        int projectCounter, betCounter;

        void ExposeProjects()
        {
            Scribe_Collections.Look(ref projects, "projects", LookMode.Deep);
            Scribe_Collections.Look(ref bets, "bets", LookMode.Deep);
            Scribe_Collections.Look(ref nextProjFaction, "nextProjFaction", LookMode.Value);
            Scribe_Collections.Look(ref nextProjTick, "nextProjTick", LookMode.Value);
            Scribe_Values.Look(ref projectCounter, "projectCounter"); Scribe_Values.Look(ref betCounter, "betCounter");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                projects = projects ?? new List<FactionProject>(); bets = bets ?? new List<ProjectBet>();
                nextProjFaction = nextProjFaction ?? new List<int>(); nextProjTick = nextProjTick ?? new List<int>();
                while (nextProjTick.Count < nextProjFaction.Count) nextProjTick.Add(0);
            }
        }

        public FactionProject Project(int id) => projects.FirstOrDefault(p => p.id == id);
        public Money ProjectsStaked() { long s = 0; foreach (var b in bets) if (!b.settled) s += b.stake; return new Money(s); }
        public IEnumerable<FactionProject> ActiveProjects() => projects.Where(p => !p.resolved);
        public Faction FactionOf(FactionProject p) => Find.FactionManager?.AllFactionsListForReading.FirstOrDefault(f => f.loadID == p.factionId);

        public string ProjectTitle(FactionProject p) => FT.T("HH.Finance.Project.Title." + p.kind, p.factionName);

        // ---- probability ----------------------------------------------------------------------------------------------
        public double ProjectP(FactionProject p)
        {
            if (p.resolved) return p.success ? 1 : 0;
            var f = FactionOf(p);
            double gw = f != null ? f.PlayerGoodwill : 0; bool war = f != null && FactionAtWar(f);
            return ProjectMath.SuccessProbability(gw, p.techLevel, war, p.playerHelp, p.strikes);
        }

        public double LongQuote(FactionProject p, int tick) => ProjectMath.LongQuote(FinanceSettings.M, Math.Max(0.1, (p.endTick - tick) / (double)Day), ProjectP(p));
        public double ShortQuote(FactionProject p, Money stake) => ProjectMath.ShortQuote(ProjectP(p), stake.Silver, ProjectMath.ERef(p.volume / 0.02));

        // ---- limits -------------------------------------------------------------------------------------------------------
        public Money ProjectStakeOn(int projectId) { long s = 0; foreach (var b in bets) if (!b.settled && b.projectId == projectId) s += b.stake; return new Money(s); }
        public Money ProjectMaxStake(FactionProject p)
        {
            double nw = NetWorth().Silver;
            double m = ProjectMath.MaxStake(nw, ProjectStakeOn(p.id).Silver, ProjectsStaked().Silver, FinanceSettings.ProjectLimits());
            return Money.FromSilver(Math.Max(0, Math.Min(m, FinanceAccounts.Cash.Silver)));
        }

        public string ProjectBlock(FactionProject p, bool isShort, int tick)
        {
            if (!HSettings.GetBool("finance.projectsOn")) return FT.T("HH.Finance.Project.Off");
            if (p.resolved) return FT.T("HH.Finance.Project.Over");
            if (p.endTick - tick < ProjectMath.ShortCutoffDays * Day) return FT.T("HH.Finance.Project.Cutoff", ProjectMath.ShortCutoffDays.ToString());
            if (bets.Any(b => !b.settled && b.projectId == p.id && b.isShort != isShort)) return FT.T("HH.Finance.Project.NoBoth");
            if (isShort && p.lastStrikeTick >= 0 && tick - p.lastStrikeTick < ProjectMath.SmokeLockDays * Day) return FT.T("HH.Finance.Project.Smoke");
            return null;
        }

        public TradeResult BetOnProject(int projectId, bool isShort, Money stake)
        {
            var p = Project(projectId); int t = Tick;
            if (!FinanceAccounts.Available) return TradeResult.Fail("HH.Finance.Trade.NoBank");
            if (p == null) return TradeResult.Fail("HH.Finance.Project.Over");
            string block = ProjectBlock(p, isShort, t); if (block != null) return new TradeResult { Message = block };
            if (stake.Micro < MinTradeMicro) return TradeResult.Fail("HH.Finance.Trade.TooSmall");
            if (FinanceAccounts.Cash < stake) return TradeResult.Fail("HH.Finance.Trade.NoCash");
            var max = ProjectMaxStake(p);
            if (stake > max) return TradeResult.Fail("HH.Finance.Project.Limit", FT.Silver(max));
            double q = isShort ? ShortQuote(p, stake) : LongQuote(p, t);
            if (!FinanceAccounts.Post(FinanceAccounts.Colony, FinanceAccounts.Projects, stake, "Project " + (isShort ? "Short: " : "Long: ") + ProjectTitle(p))) return TradeResult.Fail("HH.Finance.Trade.NoCash");
            bets.Add(new ProjectBet { id = ++betCounter, projectId = p.id, isShort = isShort, stake = stake.Micro, quote = q, tick = t });
            tradeCountDay++;
            return new TradeResult { Ok = true, Money = stake, Message = FT.T(isShort ? "HH.Finance.Project.Shorted" : "HH.Finance.Project.Longed", FT.Silver(stake), q.ToString("0.00")) };
        }

        // ---- sabotage and help (API for the world-map action; the site itself is a later step) ---------------------------------------
        public void ProjectHelp(int projectId, double amount = 1.0) { var p = Project(projectId); if (p != null && !p.resolved) p.playerHelp = Math.Min(1.5, p.playerHelp + amount); }

        /// <summary>One sabotage strike on a project: lowers P, locks new Shorts for 10 days and may be discovered (consequences by difficulty tier).</summary>
        public bool ProjectSabotageStrike(int projectId, bool guardsEscaped)
        {
            var p = Project(projectId); if (p == null || p.resolved) return false;
            int t = Tick;
            double chance = ProjectMath.DetectionChance(guardsEscaped, p.strikes);
            bool detected = JRand.Sealed("finance.sabotage").NextDouble() < chance;
            p.strikes++; p.lastStrikeTick = t;
            Push(MarketEventKind.Signal, FT.T("HH.Finance.News.Smoke", ProjectTitle(p)), true, null, -0.1f, t, true, null);
            if (detected)
            {
                int tier = (int)Math.Round(HearthProfile.Current?.Tier ?? 3f);
                int[] penalty = { 0, 0, -2, -5, -15, -40, -60 };
                var f = FactionOf(p);
                if (f != null && penalty[Math.Max(0, Math.Min(6, tier))] != 0)
                    f.TryAffectGoodwillWith(Faction.OfPlayer, penalty[Math.Max(0, Math.Min(6, tier))], false, false);
                Push(MarketEventKind.Signal, FT.T("HH.Finance.News.Caught", ProjectTitle(p)), true, null, -0.1f, t, true, null);
            }
            return detected;
        }

        // ---- generation and settlement ---------------------------------------------------------------------------------------
        static bool Eligible(Faction f) => f != null && !f.IsPlayer && !f.Hidden && !f.defeated && f.def.humanlikeFaction && !f.def.permanentEnemy;

        static ProjectKind KindFor(Faction f, RngStream r)
        {
            if (f.def.defName == "Empire") return r.NextDouble() < 0.5 ? ProjectKind.Palace : ProjectKind.Observatory;
            if (f.def.techLevel <= TechLevel.Neolithic) return ProjectKind.GreatHall;
            if (f.def.techLevel >= TechLevel.Spacer) return r.NextDouble() < 0.5 ? ProjectKind.Observatory : ProjectKind.Harbour;
            return r.NextDouble() < 0.6 ? ProjectKind.TradeRoad : ProjectKind.Harbour;
        }

        public double FactionWealth(Faction f) => SettlementsOf(f) * 15000.0 * Math.Max(0.4, LotteryMath.TechFactor((int)f.def.techLevel));

        int NextProjectTick(int factionId) { int i = nextProjFaction.IndexOf(factionId); return i >= 0 ? nextProjTick[i] : -1; }
        void SetNextProjectTick(int factionId, int t) { int i = nextProjFaction.IndexOf(factionId); if (i < 0) { nextProjFaction.Add(factionId); nextProjTick.Add(t); } else nextProjTick[i] = t; }

        public FactionProject StartProject(Faction f, int tick, double? days = null)
        {
            var rng = JRand.Sealed("finance.project");
            int set = SettlementsOf(f); if (set <= 0) return null;
            double dur = days ?? Math.Round(10 + 20 * rng.NextDouble());
            var p = new FactionProject
            {
                id = ++projectCounter, factionId = f.loadID, factionDefName = f.def.defName, factionName = f.Name, techLevel = (int)f.def.techLevel,
                kind = KindFor(f, rng), startTick = tick, endTick = tick + (int)(dur * Day), volume = ProjectMath.Volume(FactionWealth(f)),
            };
            p.pOpen = p.pNeutral = ProjectMath.SuccessProbability(f.PlayerGoodwill, p.techLevel, FactionAtWar(f), 0, 0);
            projects.Add(p);
            Push(MarketEventKind.ProjectEnd, FT.T("HH.Finance.News.ProjectNew", ProjectTitle(p), ((int)dur).ToString()), false, null, 0, tick, false, null);
            SetNextProjectTick(f.loadID, Math.Max(p.endTick, tick + (int)((20 + 20 * rng.NextDouble()) * Day)));
            return p;
        }

        void ProjectsDay(int tick)
        {
            if (!HSettings.GetBool("finance.projectsOn") || Find.FactionManager == null) return;
            foreach (var f in Find.FactionManager.AllFactionsListForReading)
            {
                if (!Eligible(f)) continue;
                if (projects.Any(p => !p.resolved && p.factionId == f.loadID)) continue;
                int next = NextProjectTick(f.loadID);
                if (next < 0) { SetNextProjectTick(f.loadID, tick + (int)(JRand.Sealed("finance.project").Range(0, 6) * Day)); continue; }
                if (tick >= next) StartProject(f, tick);
            }
            projects.RemoveAll(p => p.resolved && tick - p.resolvedTick > 5 * Day && !bets.Any(b => b.projectId == p.id && !b.settled) && !contracts.Any(c => c.projectId == p.id && !c.resolved));
            bets.RemoveAll(b => b.settled && tick - b.tick > 30 * Day);
        }

        /// <summary>Hourly: settle projects whose time is up. Public for tests.</summary>
        public void ProjectsHour(int tick)
        {
            for (int i = 0; i < projects.Count; i++) { var p = projects[i]; if (!p.resolved && tick >= p.endTick) ResolveProject(p, tick); }
        }

        public void ResolveProject(FactionProject p, int tick)
        {
            if (p.resolved) return;
            var rng = JRand.Sealed("finance.project");
            double pf = ProjectP(p);
            p.success = rng.NextDouble() < pf;
            p.neutralOutcome = JRand.Sealed("finance.omenproject").NextDouble() < p.pNeutral;     // Omen settles on a shadow roll without the player's doing
            p.resolved = true; p.resolvedTick = tick;
            var lim = FinanceSettings.ProjectLimits();
            long netIn = 0, netOut = 0;
            foreach (var b in bets.Where(x => !x.settled && x.projectId == p.id).ToList())
            {
                bool win = b.isShort ? !p.success : p.success;
                double payout = win ? b.stake / (double)Money.PerSilver * b.quote : 0;
                double stakeS = b.stake / (double)Money.PerSilver;
                double paid = payout;
                if (win)
                {
                    paid = ProjectMath.CapPayout(stakeS, payout, yearGains / (double)Money.PerSilver, Math.Max(1, yearStartNetWorth) / (double)Money.PerSilver, lim, out bool capped);
                    if (capped) { p.capped = true; Push(MarketEventKind.Signal, FT.T("HH.Finance.Project.Noticed"), false, null, 0, tick, true, null); }
                }
                var m = Money.FromSilver(paid);
                if (m.Micro > 0) FinanceAccounts.Post(FinanceAccounts.Projects, FinanceAccounts.Colony, m, "Project settled: " + ProjectTitle(p), true);
                yearGains += m.Micro - b.stake; netIn += b.stake; netOut += m.Micro;
                b.settled = true;
            }
            if (p.success && p.playerHelp > 0.5) { var f = FactionOf(p); f?.TryAffectGoodwillWith(Faction.OfPlayer, 10, false, false); }
            Push(MarketEventKind.ProjectEnd, FT.T(p.success ? "HH.Finance.News.ProjectDone" : "HH.Finance.News.ProjectFailed", ProjectTitle(p)), false, null, p.success ? 0.1f : -0.1f, tick, netIn > 0, null);
            AddEvent(MarketEventKind.ProjectEnd, null, tick);
        }
    }
}
