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

        public static bool Possible(Map map) => map != null && map.IsPlayerHome && Underworld.InUse(map) && Dock(map) != null;

        /// <summary>A crate offer as a saved recipe (1.2.0): the goods are made only when the player buys. Not yet added to the open offers.</summary>
        public static UwOffer MakeOffer(Map map = null)
        {
            var o = new UwOffer { kind = UwOfferKind.Smuggler, mapId = map?.uniqueID ?? -1 }; double mv = 0; double max = HSettings.Get("underworld.smuggler.maxValue");
            for (int i = 0; i < 3 && mv < max * 0.5; i++)
            {
                var t = UwPay.MakeGoods(20, (float)Math.Max(30, max - mv), d => d.IsDrug || d.IsWeapon || d.IsMedicine);
                if (t == null) break;
                o.goods.Add(UwGoodsLine.Of(t)); mv += t.MarketValue * t.stackCount;
                t.Destroy();
            }
            o.marketValue = Money.FromSilver(mv);
            o.price = o.marketValue * HSettings.Get("underworld.smuggler.priceFactor");
            return o;
        }

        /// <summary>A boat lands: the offer is saved and announced by a standard letter; the player answers with the gizmos on the dock. No dialog.</summary>
        public static bool Fire(Map map, bool force = false)
        {
            if (!force && !Possible(map)) return false;
            if (Dock(map) == null || Offers.Any(UwOfferKind.Smuggler, map)) return false;   // one boat at a time
            var offer = MakeOffer(map); if (offer.goods.Count == 0) return false;
            Offers.Add(offer);
            Offers.Announce(offer);
            return true;
        }

        public static bool Buy(Map map, UwOffer offer) => Buy(map, offer, out _);

        /// <summary>Pays from the till and drops the crate at the dock. Whispers +1; may attract an inspection.
        /// On failure <paramref name="why"/> is the translated reason: Offer.Gone (no dock, nothing of the crate can be made) or what the colony is short of (UwPay.ShortOf).</summary>
        public static bool Buy(Map map, UwOffer offer, out string why)
        {
            why = UwPay.T("Offer.Gone");
            var dock = Dock(map); if (dock == null || offer == null) return false;
            if (UwPay.ColonyFunds(map) < offer.price) { why = UwPay.ShortOf(map, offer.price); return false; }
            var things = new List<Thing>();
            foreach (var g in offer.goods) { var t = g.Make(); if (t != null) things.Add(t); }
            if (things.Count == 0) return false;
            if (!UwPay.PayFromColony(map, offer.price, "world", "Smuggler: contraband crate")) { foreach (var t in things) t.Destroy(); why = UwPay.ShortOf(map, offer.price); return false; }
            why = null;
            DropPodUtility.DropThingsNear(dock.Position, map, things, forbid: false);
            Underworld.Comp.Smuggles++; UwActivity.Note(map); Underworld.Comp.NoteEvent(FacilityStatus.DockId);
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

    public class CompProperties_SmugglerDock : CompProperties { public CompProperties_SmugglerDock() { compClass = typeof(CompSmugglerDock); } }

    /// <summary>The dock's inspect line (next landing or the waiting boat) and the buy / send-away buttons of an open offer.</summary>
    public class CompSmugglerDock : ThingComp
    {
        public override string CompInspectStringExtra() => parent.Spawned ? FacilityStatus.DockLine(parent.Map) : null;

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (parent.Faction != Faction.OfPlayer || !parent.Spawned || !Modules.On(ModuleId.Underworld)) yield break;
            var o = Offers.OpenOf(UwOfferKind.Smuggler, parent.Map); if (o == null) yield break;
            foreach (var g in Offers.Gizmos(o)) yield return g;
        }
    }

    public class IncidentWorker_SmugglerLanding : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms) => parms.target is Map m && Smuggler.Possible(m);
        protected override bool TryExecuteWorker(IncidentParms parms) => parms.target is Map m && Smuggler.Fire(m);
    }
}
