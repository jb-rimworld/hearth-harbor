// SPDX-License-Identifier: MIT
using LudeonTK;
using RimWorld;
using Verse;

namespace Hearth.ShopsFood
{
    /// <summary>Developer-mode helpers (Debug actions → Hearth &amp; Harbor).</summary>
    public static class VenueDebugActions
    {
        [DebugAction("Hearth & Harbor", "Hold market day now", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void HoldMarket()
        {
            var res = VenuesGame.Instance?.ForceMarket(Find.CurrentMap);
            Messages.Message(res != null && res.Any ? $"Market day: {res.Caravans} caravans, {res.VisitorGroups} visitor groups." : "Market day could not start (no friendly factions?).", MessageTypeDefOf.NeutralEvent, false);
        }

        [DebugAction("Hearth & Harbor", "Restock + report venues", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void Report()
        {
            var map = Find.CurrentMap;
            foreach (var v in Venues.All(map))
                Log.Message($"[HH] {v.DisplayNameOf()}: gate {VenueApi.Gate(v)}, stock {VenueSales.Stock(v, true).Count} stacks, price x{VenueApi.PriceMultiplier(v):0.00}, staff {VenueApi.Staffer(v)?.LabelShort ?? "-"}");
        }
    }
}
