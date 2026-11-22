// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Hearth.ShopsFood
{
    public sealed class MarketResult
    {
        public int Caravans, VisitorGroups;
        public readonly List<Faction> Factions = new List<Faction>();
        public readonly List<Pawn> Pawns = new List<Pawn>();
        public bool Any => Caravans + VisitorGroups > 0;
    }

    /// <summary>The big market (6.10, N48): friendly factions bring several trader caravans and day visitors at once.</summary>
    public static class VenueMarket
    {
        public static bool CanHaveMarket(Map map)
        {
            if (map == null || !map.IsPlayerHome) return false;
            foreach (var v in Venues.All(map)) if (VenueApi.SellsOf(v) == SellKind.Goods) return true;
            return false;
        }

        static List<Faction> Sources(IncidentDef def, Map map)
        {
            var worker = def.Worker as IncidentWorker_NeutralGroup;
            var parms = new IncidentParms { target = map, forced = true };
            var list = new List<Faction>();
            foreach (var f in Find.FactionManager.AllFactionsVisible)
            {
                if (f.IsPlayer || f.defeated || f.HostileTo(Faction.OfPlayer)) continue;
                bool ok;
                try { ok = worker != null && worker.FactionCanBeGroupSource(f, parms); } catch { ok = false; }
                if (ok) list.Add(f);
            }
            return list;
        }

        /// <summary>Hold a market day now: <paramref name="caravans"/> trader caravans and <paramref name="visitors"/> visitor groups at once.</summary>
        public static MarketResult Hold(Map map, int caravans, int visitors)
        {
            var res = new MarketResult();
            if (map == null) return res;
            var traderFactions = Sources(IncidentDefOf.TraderCaravanArrival, map);
            traderFactions.Shuffle();
            for (int i = 0; i < caravans && traderFactions.Count > 0; i++)
            {
                var f = traderFactions[i % traderFactions.Count];
                var parms = new IncidentParms { target = map, faction = f, forced = true, sendLetter = false, storeGeneratedNeutralPawns = new List<Pawn>() };
                bool ok = Guard.Run(ModuleId.ShopsFood, "Market.Caravan", () => IncidentDefOf.TraderCaravanArrival.Worker.TryExecute(parms), false);
                if (!ok) continue;
                res.Caravans++;
                if (!res.Factions.Contains(f)) res.Factions.Add(f);
                res.Pawns.AddRange(parms.storeGeneratedNeutralPawns);
            }
            var visitorFactions = Sources(IncidentDefOf.VisitorGroup, map);
            visitorFactions.Shuffle();
            for (int i = 0; i < visitors && visitorFactions.Count > 0; i++)
            {
                var f = visitorFactions[i % visitorFactions.Count];
                var parms = new IncidentParms { target = map, faction = f, forced = true, sendLetter = false, storeGeneratedNeutralPawns = new List<Pawn>() };
                bool ok = Guard.Run(ModuleId.ShopsFood, "Market.Visitors", () => IncidentDefOf.VisitorGroup.Worker.TryExecute(parms), false);
                if (!ok) continue;
                res.VisitorGroups++;
                if (!res.Factions.Contains(f)) res.Factions.Add(f);
                res.Pawns.AddRange(parms.storeGeneratedNeutralPawns);
            }
            if (res.Any) SendLetter(map, res);
            HLog.Info("Venues", $"Market day: {res.Caravans} caravans, {res.VisitorGroups} visitor groups, factions {string.Join(", ", res.Factions.Select(f => f.Name))}");
            return res;
        }

        static void SendLetter(Map map, MarketResult res)
        {
            string names = string.Join(", ", res.Factions.Select(f => f.Name));
            string text = "HH.Venue.MarketText".TranslateOr("Word has spread: it is market day. {0} trader caravans and {1} groups of visitors from {2} are on their way to your colony. Traders will trade as usual at their camps; visitors will browse your shops and taverns if they are open.\n\nKeep your shelves stocked and your counters staffed.")
                .Replace("{0}", res.Caravans.ToString()).Replace("{1}", res.VisitorGroups.ToString()).Replace("{2}", names);
            var target = res.Pawns.FirstOrDefault(p => p != null && p.Spawned);
            Find.LetterStack.ReceiveLetter("HH.Venue.MarketLabel".TranslateOr("Market day"), text, LetterDefOf.PositiveEvent, target != null ? new LookTargets(target) : LookTargets.Invalid);
        }
    }

    /// <summary>Schedules the big market (saved) and runs the daily-ish housekeeping of the module that must work even with the module paused.</summary>
    public sealed class VenuesGame : GameComponent
    {
        public static VenuesGame Instance;
        int nextMarketTick = -1, lastMarketTick = -999999, marketsHeld;
        public int NextMarketTick => nextMarketTick;
        public int MarketsHeld => marketsHeld;

        public VenuesGame(Game game) { Instance = this; }

        public override void StartedNewGame() { Instance = this; VenueState.Clear(); VanillaHiding.Refresh(); }
        public override void LoadedGame() { Instance = this; VenueState.Clear(); VanillaHiding.Refresh(); }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref nextMarketTick, "nextMarketTick", -1);
            Scribe_Values.Look(ref lastMarketTick, "lastMarketTick", -999999);
            Scribe_Values.Look(ref marketsHeld, "marketsHeld");
        }

        public void Schedule(int now)
        {
            float days = Math.Max(1, VS.I(VS.MarketDays));
            nextMarketTick = now + (int)(days * GenDate.TicksPerDay * Rand.Range(0.8f, 1.2f));
        }

        public override void GameComponentTick()
        {
            int t = Find.TickManager.TicksGame;
            if (t % 2500 == 77) Guard.Run(ModuleId.ShopsFood, "VanillaHiding.Refresh", VanillaHiding.Refresh);
            if (t % 2500 == 1500 && Modules.On(ModuleId.ShopsFood)) Guard.Run(ModuleId.ShopsFood, "Market.Check", () => CheckMarket(t));
        }

        void CheckMarket(int now)
        {
            if (!VS.B(VS.MarketEnabled)) return;
            if (nextMarketTick < 0) { Schedule(now); return; }
            if (now < nextMarketTick) return;
            var map = Find.AnyPlayerHomeMap;
            if (map == null) return;
            if (!VenueMarket.CanHaveMarket(map) || map.dangerWatcher.DangerRating == StoryDanger.High || map.mapPawns.FreeColonistsSpawnedCount == 0)
            {
                nextMarketTick = now + GenDate.TicksPerDay / 2; // try again later today
                return;
            }
            var res = VenueMarket.Hold(map, VS.I(VS.MarketCaravans), VS.I(VS.MarketVisitors));
            if (res.Any) { marketsHeld++; lastMarketTick = now; Schedule(now); }
            else nextMarketTick = now + GenDate.TicksPerDay / 2;
        }

        /// <summary>Debug/test: hold a market now and restart the interval.</summary>
        public MarketResult ForceMarket(Map map)
        {
            var res = VenueMarket.Hold(map, VS.I(VS.MarketCaravans), VS.I(VS.MarketVisitors));
            if (res.Any) { marketsHeld++; lastMarketTick = Find.TickManager.TicksGame; Schedule(lastMarketTick); }
            return res;
        }
    }
}
