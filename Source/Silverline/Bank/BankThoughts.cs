// SPDX-License-Identifier: MIT
using System.Linq;
using Hearth;
using RimWorld;
using Verse;

namespace Silverline
{
    /// <summary>"We have savings" / "We're broke": a small colony-wide thought from the net worth tier (N22). Greedy colonists feel it twice as much.</summary>
    public class ThoughtWorker_Finances : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (!Modules.On(ModuleId.Bank) || !HSettings.GetBool("bank.moodOn")) return ThoughtState.Inactive;
            if (p.Faction != Faction.OfPlayer) return ThoughtState.Inactive;
            var c = BankGameComp.Current;
            if (c == null || !c.HasAccount) return ThoughtState.Inactive;
            int tier = c.ShownTier;
            if (tier < 0 || tier >= FinanceMath.TierMood.Length || FinanceMath.TierMood[tier] == 0) return ThoughtState.Inactive;
            return ThoughtState.ActiveAtStage(tier);
        }
    }

    /// <summary>-2 mood while the "friendly visit" bowler hats are standing nearby (stage 4).</summary>
    public class ThoughtWorker_CollectorsNearby : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (!Modules.On(ModuleId.Bank) || !p.Spawned) return ThoughtState.Inactive;
            var c = BankGameComp.Current;
            if (c == null || c.Collectors.Count == 0) return ThoughtState.Inactive;
            foreach (var h in c.Collectors)
                if (h != null && h.Spawned && h.Map == p.Map && h.Position.InHorDistOf(p.Position, 14f)) return ThoughtState.ActiveAtStage(0);
            return ThoughtState.Inactive;
        }
    }
}
