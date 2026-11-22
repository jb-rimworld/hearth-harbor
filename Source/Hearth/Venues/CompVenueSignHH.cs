// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Hearth.ShopsFood
{
    [StaticConstructorOnStartup]
    static class VenueTex
    {
        public static readonly Texture2D Hours = ContentFinder<Texture2D>.Get("UI/HH/Venues/Cmd_Hours", false) ?? BaseContent.BadTex;
        public static readonly Texture2D Price = ContentFinder<Texture2D>.Get("UI/HH/Venues/Cmd_Price", false) ?? BaseContent.BadTex;
        public static readonly Texture2D TakeGoods = ContentFinder<Texture2D>.Get("UI/HH/Venues/Cmd_TakeGoods", false) ?? BaseContent.BadTex;
        public static readonly Texture2D Rename = ContentFinder<Texture2D>.Get("UI/HH/Venues/Cmd_Rename", false) ?? BaseContent.BadTex;
    }

    public class CompProperties_VenueSignHH : CompProperties_VenueSign
    {
        public CompProperties_VenueSignHH() { compClass = typeof(CompVenueSignHH); }
    }

    /// <summary>
    /// The Venue Sign of Hearth &amp; Harbor: the contract's data (type, hours, price, N45 toggle) plus the controls,
    /// the readiness checklist, sales statistics and the room highlight (6.1).
    /// </summary>
    public class CompVenueSignHH : CompVenueSign
    {
        public int salesCount, itemsSold;
        long incomeMicro;
        int dayStamp = -1, daySales;
        long dayIncomeMicro;

        public Money Income => new Money(incomeMicro);
        public Money IncomeToday { get { RollDay(); return new Money(dayIncomeMicro); } }
        public int SalesToday { get { RollDay(); return daySales; } }

        void RollDay()
        {
            if (Find.TickManager == null) return;
            int day = Find.TickManager.TicksGame / GenDate.TicksPerDay;
            if (day != dayStamp) { dayStamp = day; daySales = 0; dayIncomeMicro = 0; }
        }

        public void RecordSale(Money price, int count)
        {
            RollDay();
            salesCount++; itemsSold += count; daySales++;
            incomeMicro += price.Micro; dayIncomeMicro += price.Micro;
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            if (!respawningAfterLoad && type == null) type = HHVenueDefOf.HH_Venue_Shop;
            base.PostSpawnSetup(respawningAfterLoad);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref salesCount, "salesCount");
            Scribe_Values.Look(ref itemsSold, "itemsSold");
            Scribe_Values.Look(ref incomeMicro, "incomeMicro");
            Scribe_Values.Look(ref dayStamp, "dayStamp", -1);
            Scribe_Values.Look(ref daySales, "daySales");
            Scribe_Values.Look(ref dayIncomeMicro, "dayIncomeMicro");
        }

        Venue Venue => parent.Spawned ? VenueApi.VenueOfSign(parent.Map, parent) : null;

        // ------------------------------------------------------------------ type menu

        public IEnumerable<VenueTypeDef> SelectableTypes()
        {
            foreach (var t in DefDatabase<VenueTypeDef>.AllDefs.OrderBy(d => d.defName))
            {
                if (Props.allowedTypes.Count > 0 && !Props.allowedTypes.Contains(t)) continue;
                if (!Modules.On(t.module)) continue;
                if (t.researchPrerequisite != null && !t.researchPrerequisite.IsFinished) continue;
                yield return t;
            }
        }

        public void SetType(VenueTypeDef newType)
        {
            if (newType == null || newType == type) return;
            bool hoursWereDefault = type != null && !alwaysOpen && openHour == type.defaultOpenHour && closeHour == type.defaultCloseHour;
            type = newType;
            if (hoursWereDefault || openHour < 0) { openHour = newType.defaultOpenHour; closeHour = newType.defaultCloseHour; }
            if (parent.Spawned) Venues.MarkDirty(parent.Map);
            VenueSales.ClearCaches();
        }

        void OpenTypeMenu()
        {
            var opts = new List<FloatMenuOption>();
            foreach (var t in SelectableTypes())
            {
                var tt = t;
                string label = t.LabelCap + (t == type ? "  (" + "HH.Venue.Current".TranslateOr("current") + ")" : "");
                opts.Add(new FloatMenuOption(label, () => SetType(tt), t.icon ?? BaseContent.BadTex, Color.white));
            }
            if (opts.Count == 0) opts.Add(new FloatMenuOption("HH.Venue.NoTypes".TranslateOr("No kind of business available"), null));
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        // ------------------------------------------------------------------ gizmos

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (var g in base.CompGetGizmosExtra()) yield return g;
            if (parent.Faction != Faction.OfPlayer) yield break;

            yield return new Command_Action
            {
                defaultLabel = type != null ? type.LabelCap.ToString() : "HH.Venue.ChooseType".TranslateOr("Kind of business"),
                defaultDesc = "HH.Venue.ChooseTypeDesc".TranslateOr("Choose what kind of business the room of this sign is: tavern, shop, restaurant, casino, spa or lounge."),
                icon = type?.icon ?? BaseContent.BadTex,
                action = OpenTypeMenu,
                hotKey = null,
            };
            yield return new Command_Action
            {
                defaultLabel = "HH.Venue.Hours".TranslateOr("Opening hours"),
                defaultDesc = "HH.Venue.HoursDesc".TranslateOr("Set when the business is open. Presets for day, evening, night and around the clock."),
                icon = VenueTex.Hours,
                action = () => Find.WindowStack.Add(new Dialog_VenueHours(this)),
            };
            yield return new Command_Action
            {
                defaultLabel = "HH.Venue.Price".TranslateOr("Prices") + ": " + (priceFactor * 100f).ToString("0") + " %",
                defaultDesc = "HH.Venue.PriceDesc".TranslateOr("Price factor on everything this business sells. Guests are put off by prices that are too high."),
                icon = VenueTex.Price,
                action = () => Find.WindowStack.Add(new Dialog_VenuePrice(this)),
            };
            if (VenueApi.SaleOf(type)?.sells is SellKind k && k != SellKind.None)
            {
                yield return new Command_Toggle
                {
                    defaultLabel = "HH.Venue.ColonyMayTake".TranslateOr("Colony may take goods"),
                    defaultDesc = "HH.Venue.ColonyMayTakeDesc".TranslateOr("Off: goods for sale in this room are forbidden for your colonists, so nobody carries your stock away. On: colonists treat the goods like any other stored item."),
                    icon = VenueTex.TakeGoods,
                    isActive = () => colonyMayTakeGoods,
                    toggleAction = () => { colonyMayTakeGoods = !colonyMayTakeGoods; VenueSales.ClearCaches(); },
                };
            }
            yield return new Command_Action
            {
                defaultLabel = "HH.Venue.Rename".TranslateOr("Rename"),
                defaultDesc = "HH.Venue.RenameDesc".TranslateOr("Give this business a name."),
                icon = VenueTex.Rename,
                action = () => Find.WindowStack.Add(new Dialog_RenameVenue(this)),
            };
        }

        // ------------------------------------------------------------------ inspect string, overlay

        public override string CompInspectStringExtra()
        {
            if (type == null) return null;
            var sb = new StringBuilder();
            string hours = alwaysOpen || openHour == closeHour ? "HH.Venue.AlwaysOpen".TranslateOr("open around the clock") : $"{openHour:00}:00–{closeHour:00}:00";
            var v = Venue;
            if (v == null)
            {
                sb.Append(type.LabelCap).Append(" · ").Append(hours);
                sb.Append("\n").Append("HH.Venue.NoRoom".TranslateOr("Not a business yet: the sign needs an enclosed, roofed room."));
                return sb.ToString();
            }
            var gate = VenueApi.Gate(v);
            string state;
            switch (gate)
            {
                case VenueGate.Open: state = "HH.Venue.Open".TranslateOr("open"); break;
                case VenueGate.WaitingForStaff: state = "HH.Venue.NoStaffState".TranslateOr("open, but nobody on duty"); break;
                default: state = "HH.Venue.Closed".TranslateOr("closed"); break;
            }
            sb.Append(type.LabelCap).Append(" · ").Append(hours).Append(" · ").Append(state);
            var staffer = VenueApi.Staffer(v);
            if (VenueApi.NeedsStaff(v))
                sb.Append("\n").Append("HH.Venue.Staff".TranslateOr("On duty")).Append(": ").Append(staffer != null ? staffer.LabelShort : "HH.Venue.Nobody".TranslateOr("nobody"));
            else if (staffer != null)
                sb.Append("\n").Append("HH.Venue.Staff".TranslateOr("On duty")).Append(": ").Append(staffer.LabelShort);
            float mult = VenueApi.PriceMultiplier(v);
            sb.Append("\n").Append("HH.Venue.PriceLine".TranslateOr("Prices")).Append(": ").Append((mult * 100f).ToString("0")).Append(" % (")
              .Append(("HH.Venue.Tier." + VenueApi.PriceTierLabel(priceFactor)).TranslateOr(VenueApi.PriceTierLabel(priceFactor))).Append(")");
            var sells = VenueApi.SellsOf(v);
            if (sells != SellKind.None)
            {
                int n = VenueSales.Stock(v).Count;
                sb.Append("\n").Append("HH.Venue.ForSale".TranslateOr("For sale")).Append(": ").Append(n).Append(" ").Append("HH.Venue.StacksWord".TranslateOr("stacks"))
                  .Append(" · ").Append(colonyMayTakeGoods ? "HH.Venue.ColonyMayTakeShort".TranslateOr("colony may take goods") : "HH.Venue.CustomersOnly".TranslateOr("customers only"));
                sb.Append("\n").Append("HH.Venue.Sales".TranslateOr("Sales today")).Append(": ").Append(SalesToday).Append(" (").Append(IncomeToday.ToStringShort()).Append(" ")
                  .Append("HH.Venue.Silver".TranslateOr("silver")).Append(") · ").Append("HH.Venue.SalesTotal".TranslateOr("total")).Append(": ").Append(salesCount);
            }
            foreach (var line in Checklist(v)) sb.Append("\n").Append(line);
            return sb.ToString();
        }

        /// <summary>Readiness checklist (B1): what works, what is missing and what to do about it.</summary>
        public IEnumerable<string> Checklist(Venue v)
        {
            string ok = "✔ ", no = "✘ ";
            var sells = VenueApi.SellsOf(v);
            if (sells == SellKind.Goods)
            {
                bool has = VenueSales.Stock(v).Count > 0;
                yield return has ? ok + "HH.Venue.CheckStock".TranslateOr("Goods on shelves") : no + "HH.Venue.CheckNoStock".TranslateOr("No goods for sale → put a shelf or stockpile with goods in the room");
            }
            else if (sells == SellKind.Drinks)
            {
                bool has = VenueSales.Stock(v).Count > 0;
                yield return has ? ok + "HH.Venue.CheckDrinks".TranslateOr("Drinks in stock") : no + "HH.Venue.CheckNoDrinks".TranslateOr("No drinks in stock → store beer or other drinks in the room");
            }
            if (VenueApi.NeedsStaff(v))
            {
                bool counter = VenueApi.CounterOf(v) != null;
                yield return counter ? ok + "HH.Venue.CheckCounter".TranslateOr("Counter for the tender") : no + "HH.Venue.CheckNoCounter".TranslateOr("No counter (optional) → the tender stands next to the sign");
                bool anyone = parent.Map.mapPawns.FreeColonistsSpawned.Any(p => !p.WorkTypeIsDisabled(HHVenueDefOf.HH_Serving) && p.workSettings != null && p.workSettings.GetPriority(HHVenueDefOf.HH_Serving) > 0);
                yield return anyone ? ok + "HH.Venue.CheckWorker".TranslateOr("A colonist does Serving work") : no + "HH.Venue.CheckNoWorker".TranslateOr("Nobody has the Serving work type switched on");
            }
        }

        static readonly List<IntVec3> cellsScratch = new List<IntVec3>();
        public override void PostDrawExtraSelectionOverlays()
        {
            base.PostDrawExtraSelectionOverlays();
            var v = Venue; if (v?.Room == null) return;
            cellsScratch.Clear();
            foreach (var c in v.Room.Cells) cellsScratch.Add(c);
            if (cellsScratch.Count > 0) GenDraw.DrawFieldEdges(cellsScratch, type != null ? type.color : Color.white);
        }

        public override void DrawGUIOverlay()
        {
            base.DrawGUIOverlay();
            if (type == null || !parent.Spawned || Find.CameraDriver.CurrentZoom > CameraZoomRange.Close) return;
            GenMapUI.DrawThingLabel(parent, DisplayName, Color.white);
        }
    }
}
