// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Hearth.Underworld
{
    /// <summary>The smuggler's dock (6.11, light): at night a crate lands, no questions asked. You buy it cheap or send them away; sometimes someone notices the lights.</summary>
    public static class Smuggler
    {
        public static Thing Dock(Map map)
        {
            if (map == null || UwDefs.SmugglerDock == null) return null;
            foreach (var t in map.listerThings.ThingsOfDef(UwDefs.SmugglerDock)) if (t.Faction == Faction.OfPlayer && t.Spawned) return t;
            return null;
        }

        public static bool Possible(Map map) => Underworld.Unlocked && map != null && map.IsPlayerHome && Dock(map) != null;

        public sealed class Offer { public List<Thing> Goods = new List<Thing>(); public Money Price; public Money MarketValue; }

        public static Offer MakeOffer()
        {
            var o = new Offer(); double mv = 0; double max = HSettings.Get("underworld.smuggler.maxValue");
            for (int i = 0; i < 3 && mv < max * 0.5; i++)
            {
                var t = UwPay.MakeGoods(20, (float)Math.Max(30, max - mv), d => d.IsDrug || d.IsWeapon || d.IsMedicine);
                if (t == null) break;
                o.Goods.Add(t); mv += t.MarketValue * t.stackCount;
            }
            o.MarketValue = Money.FromSilver(mv);
            o.Price = o.MarketValue * HSettings.Get("underworld.smuggler.priceFactor");
            return o;
        }

        public static bool Fire(Map map, bool force = false)
        {
            if (!force && !Possible(map)) return false;
            var offer = MakeOffer(); if (offer.Goods.Count == 0) return false;
            var dock = Dock(map);
            var node = new DiaNode(UwPay.T("Smuggler.Text", offer.Goods.Count, offer.MarketValue.ToStringShort(), offer.Price.ToStringShort()));
            var buy = new DiaOption(UwPay.T("Smuggler.Buy", offer.Price.ToStringShort())) { action = () => Buy(map, offer), resolveTree = true };
            if (Ledger.Current.Balance(Underworld.Till(map)) < offer.Price) buy.Disable(UwPay.T("Smuggler.NoCash"));
            node.options.Add(buy);
            node.options.Add(new DiaOption(UwPay.T("Smuggler.Decline")) { action = () => { foreach (var t in offer.Goods) t.Destroy(); }, resolveTree = true });
            Find.WindowStack.Add(new Dialog_NodeTree(node, true, false, UwPay.T("Smuggler.Title")));
            return true;
        }

        /// <summary>Pays from the till and drops the crate at the dock. Whispers +1; may attract an inspection.</summary>
        public static bool Buy(Map map, Offer offer)
        {
            var dock = Dock(map); if (dock == null) return false;
            if (!Ledger.Current.Post(Underworld.Till(map), "world", offer.Price, "Smuggler: contraband crate")) return false;
            DropPodUtility.DropThingsNear(dock.Position, map, offer.Goods, forbid: false);
            Underworld.Comp.Smuggles++; UwActivity.Note(map);
            Underworld.AddWhispers(1f, "smuggler's crate");
            Inspection(map);
            return true;
        }

        public static void Inspection(Map map, bool force = false)
        {
            var empire = Find.FactionManager.AllFactionsVisible.FirstOrDefault(f => f.def.defName == "Empire" && !f.defeated);
            if (empire == null) return;
            double p = HSettings.Get("underworld.smuggler.inspectChance") * (0.5 + Underworld.Whispers / 50.0);
            if (!force && !JRand.Stream("underworld.inspect").Chance(p)) return;
            Underworld.AffectGoodwill(empire, -8, "dock inspection");
            Find.LetterStack.ReceiveLetter(UwPay.T("Smuggler.InspectTitle"), UwPay.T("Smuggler.InspectText"), LetterDefOf.NegativeEvent);
        }

        public static bool Night(Map map) { int h = GenLocalDate.HourOfDay(map); return h >= 22 || h < 4; }
    }

    public class IncidentWorker_SmugglerLanding : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms) => parms.target is Map m && Smuggler.Possible(m);
        protected override bool TryExecuteWorker(IncidentParms parms) => parms.target is Map m && Smuggler.Fire(m);
    }
}
