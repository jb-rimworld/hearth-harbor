// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Hearth.Underworld
{
    public class CompProperties_OrganClinic : CompProperties { public CompProperties_OrganClinic() { compClass = typeof(CompOrganClinic); } }

    /// <summary>
    /// The organ clinic counter (6.11, Dark and Grim): a storage counter that only takes harvested organs. At night "anonymous donors" collect them and pay into the till.
    /// Grim adds orders: one specific organ within a deadline for a premium. Text and ordinary vanilla items only.
    /// </summary>
    public class CompOrganClinic : ThingComp
    {
        public IEnumerable<Thing> Organs()
        {
            if (!parent.Spawned) yield break;
            foreach (var c in parent.OccupiedRect()) foreach (var t in c.GetThingList(parent.Map).ToList()) if (Organs_.IsOrgan(t)) yield return t;
        }

        public override string CompInspectStringExtra()
            => UwPay.T("Organ.Inspect", Organs().Sum(t => t.stackCount), Underworld.Comp?.Orders.Count(o => !o.done) ?? 0);

        public override void CompTickRare()
        {
            if (!parent.Spawned || parent.Faction != Faction.OfPlayer || !Underworld.Allows(UwFeature.OrganClinic)) return;
            if (!Organs_.Night(parent.Map)) return;
            Organs_.Collect(this);
        }
    }

    public static class Organs_
    {
        public static bool IsOrgan(Thing t) => t != null && t.def.thingCategories != null && t.def.thingCategories.Contains(UwDefs.CatOrgans) && t.def.category == ThingCategory.Item;
        public static bool Night(Map map) { int h = GenLocalDate.HourOfDay(map); return h >= 23 || h < 4; }

        /// <summary>Price for one organ item: market value × factor; a matching open Grim order pays the premium.</summary>
        public static Money Price(Thing t, out OrganOrder order)
        {
            order = null;
            double v = t.MarketValue * t.stackCount * HSettings.Get("underworld.organ.priceFactor");
            if (Underworld.Allows(UwFeature.OrganOrders))
            {
                int now = Find.TickManager.TicksGame;
                order = Underworld.Comp.Orders.FirstOrDefault(o => !o.done && o.organ == t.def && o.deadlineTick > now);
                if (order != null) v *= order.premium;
            }
            return Money.FromSilver(v);
        }

        /// <summary>Buys every organ in the counter. Paid out of the world into the till (anonymous donors). Returns the number of organs sold.</summary>
        public static int Collect(CompOrganClinic clinic)
        {
            var map = clinic.parent.Map; int sold = 0;
            foreach (var t in clinic.Organs().ToList())
            {
                var price = Price(t, out var order);
                if (price.Micro <= 0) continue;
                int count = t.stackCount;
                Payments.CreditColony(map, price, "Organ clinic: anonymous donor");
                if (order != null) { order.done = true; Messages.Message(UwPay.T("Organ.OrderDone", t.LabelNoCount), clinic.parent, MessageTypeDefOf.PositiveEvent, false); }
                t.Destroy();
                sold += count;
                Underworld.Comp.OrgansSold += count; Underworld.Comp.NoteEvent(FacilityStatus.ClinicId);
                Underworld.AddWhispers(HSettings.Get("underworld.whispers.organ") * count, "organ sale");
                UwActivity.Note(map);
                Outrage(map);
            }
            if (sold > 0) Messages.Message(UwPay.T("Organ.Sold", sold), clinic.parent, MessageTypeDefOf.NeutralEvent, false);
            return sold;
        }

        static readonly string[] Hostile = { "OrganUse_Abhorrent", "OrganUse_HorribleNoSell", "OrganUse_HorribleSellOK" };
        static void Outrage(Map map)
        {
            if (UwDefs.ThoughtOrganOutrage == null || !ModsConfig.IdeologyActive) return;
            foreach (var c in map.mapPawns.FreeColonistsSpawned)
                if (c.Ideo != null && c.Ideo.PreceptsListForReading.Any(p => Hostile.Contains(p.def.defName))) c.needs?.mood?.thoughts?.memories?.TryGainMemory(UwDefs.ThoughtOrganOutrage);
        }

        static readonly string[] OrderDefs = { "Kidney", "Liver", "Lung", "Heart", "Stomach" };

        /// <summary>Grim: a new order every ~7 days while the book has room; expired orders cost a little reputation.</summary>
        public static void TickOrders(int now)
        {
            var comp = Underworld.Comp; if (comp == null) return;
            if (!Underworld.Allows(UwFeature.OrganOrders)) return;
            if (!HomeHasClinic()) return;
            foreach (var o in comp.Orders.ToList())
            {
                if (!o.done && o.deadlineTick <= now)
                {
                    o.done = true;
                    Underworld.AddWhispers(-1f, "organ order missed");
                    Messages.Message(UwPay.T("Organ.OrderMissed", o.organ.label), MessageTypeDefOf.NegativeEvent, false);
                }
            }
            comp.Orders.RemoveAll(o => o.done && o.deadlineTick < now - 60000);
            if (comp.NextOrderTick < 0) comp.NextOrderTick = now + 3 * 60000;
            if (now >= comp.NextOrderTick && comp.Orders.Count(o => !o.done) < 2)
            {
                comp.NextOrderTick = now + (int)(60000 * Rand.Range(5f, 9f));
                var def = DefDatabase<ThingDef>.GetNamedSilentFail(OrderDefs.RandomElement()); if (def == null) return;
                comp.Orders.Add(new OrganOrder { organ = def, deadlineTick = now + HSettings.GetInt("underworld.organ.orderDays") * 60000, premium = HSettings.Get("underworld.organ.orderPremium") });
                Find.LetterStack.ReceiveLetter(UwPay.T("Organ.OrderTitle"), UwPay.T("Organ.OrderText", def.label, HSettings.GetInt("underworld.organ.orderDays")), LetterDefOf.NeutralEvent);
            }
        }

        static bool HomeHasClinic() => UwDefs.OrganClinic != null && Find.Maps.Any(m => m.IsPlayerHome && m.listerThings.ThingsOfDef(UwDefs.OrganClinic).Count > 0);
    }
}
