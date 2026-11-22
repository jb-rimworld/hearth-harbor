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
    /// <summary>
    /// System B (KONZEPT 8.6): signals from the game nudge prices. The player moves timing and selection, never the long-run mean:
    /// the coupling is capped at +-0.5 sigma per day (MarketMath.CapSignal) and signals decay (x0.8 per day).
    /// </summary>
    public sealed partial class FinanceGame
    {
        // snapshots of yesterday (saved so that a reload does not create phantom signals)
        List<int> sigFaction = new List<int>(); List<int> sigGoodwill = new List<int>(); List<int> sigSettlements = new List<int>();
        float sigColonyWealth; int sigRaids = -1;
        public const float KappaGoodwill = 0.0015f, KappaSettlement = 0.012f, KappaWealth = 0.06f, KappaRaid = 0.006f, SignalDecay = 0.8f;

        void ExposeSignals()
        {
            Scribe_Collections.Look(ref sigFaction, "sigFaction", LookMode.Value);
            Scribe_Collections.Look(ref sigGoodwill, "sigGoodwill", LookMode.Value);
            Scribe_Collections.Look(ref sigSettlements, "sigSettlements", LookMode.Value);
            Scribe_Values.Look(ref sigColonyWealth, "sigWealth"); Scribe_Values.Look(ref sigRaids, "sigRaids", -1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                sigFaction = sigFaction ?? new List<int>(); sigGoodwill = sigGoodwill ?? new List<int>(); sigSettlements = sigSettlements ?? new List<int>();
                int n = sigFaction.Count; while (sigGoodwill.Count < n) sigGoodwill.Add(0); while (sigSettlements.Count < n) sigSettlements.Add(0);
            }
        }

        int SettlementsOf(Faction f)
        {
            int n = 0; var ws = Find.WorldObjects?.Settlements; if (ws == null) return 0;
            for (int i = 0; i < ws.Count; i++) if (ws[i].Faction == f) n++;
            return n;
        }

        bool FactionAtWar(Faction f)
        {
            foreach (var o in Find.FactionManager.AllFactionsListForReading)
                if (o != f && !o.IsPlayer && !o.Hidden && f.RelationKindWith(o) == FactionRelationKind.Hostile && o.def.humanlikeFaction) return true;
            return false;
        }

        /// <summary>Daily: read the game state, turn changes into signal impulses, decay old ones, refresh faction-linked hazards.</summary>
        public void UpdateSignals(int tick)
        {
            float wealth = 0; int raids = Find.StoryWatcher?.statsRecord?.numRaidsEnemy ?? 0;
            foreach (var m in Find.Maps) if (m.IsPlayerHome) wealth += m.wealthWatcher.WealthTotal;
            float dWealth = sigColonyWealth > 1 ? (wealth - sigColonyWealth) / sigColonyWealth : 0;
            int dRaids = sigRaids >= 0 ? Math.Max(0, raids - sigRaids) : 0;
            var rng = JRand.Sealed("finance.news");

            foreach (var a in assets)
            {
                a.signal *= SignalDecay;
                if (a.def == null || a.delisted) continue;
                var f = FactionFor(a.def);
                if (f != null)
                {
                    int i = sigFaction.IndexOf(f.loadID);
                    int gw = f.PlayerGoodwill, set = SettlementsOf(f);
                    if (i >= 0)
                    {
                        int dg = gw - sigGoodwill[i], ds = set - sigSettlements[i];
                        a.signal += KappaGoodwill * dg + KappaSettlement * ds;
                        if (Math.Abs(dg) >= 8) Push(MarketEventKind.Signal, FT.Pick(dg > 0 ? "HH.Finance.News.Gear.Warm" : "HH.Finance.News.Gear.Cold", rng, a.Label, f.Name), true, a.id, dg > 0 ? 0.1f : -0.1f, tick, false, null);
                        else if (Math.Abs(ds) >= 1) Push(MarketEventKind.Signal, FT.Pick(ds > 0 ? "HH.Finance.News.Gear.Settle" : "HH.Finance.News.Gear.Fall", rng, a.Label, f.Name), false, a.id, ds > 0 ? 0.1f : -0.1f, tick, false, null);
                    }
                    // faction state shifts defaults and war-bond outcomes (hazard multiplier and victory chance), see 8.5
                    bool hostile = f.HostileTo(Faction.OfPlayer), war = FactionAtWar(f);
                    a.dynHaz = Math.Max(0.2, 1.0 + (hostile ? 0.8 : 0) + (war ? 0.6 : 0) - (gw > 40 ? 0.3 : 0));
                    a.dynPUp = a.def.kind == AssetKind.WarBond ? Math.Max(-0.2, Math.Min(0.2, 0.1 * (set - 4) / 4.0)) : 0;
                }
                if (a.def.colonySignals)
                {
                    a.signal += KappaWealth * dWealth + KappaRaid * dRaids;
                    if (dRaids > 0) Push(MarketEventKind.Signal, FT.Pick("HH.Finance.News.Gear.Raid", rng, a.Label), true, a.id, 0.1f, tick, false, null);
                }
                a.signal = (float)DetMath.Clamp(a.signal, -0.2, 0.2);
            }

            // new snapshot
            sigFaction.Clear(); sigGoodwill.Clear(); sigSettlements.Clear();
            foreach (var f in Find.FactionManager.AllFactionsListForReading)
            {
                if (f.IsPlayer || f.Hidden) continue;
                sigFaction.Add(f.loadID); sigGoodwill.Add(f.PlayerGoodwill); sigSettlements.Add(SettlementsOf(f));
            }
            sigColonyWealth = wealth; sigRaids = raids;
        }

        /// <summary>Test hook: inject a signal impulse into an asset as if the game had produced it.</summary>
        public void InjectSignal(string assetId, float impulse) { var a = Asset(assetId); if (a != null) a.signal = (float)DetMath.Clamp(a.signal + impulse, -0.2, 0.2); }
    }
}
