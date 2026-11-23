// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Hearth.ShopsFood
{
    [StaticConstructorOnStartup]
    static class VenueTex
    {
        public static readonly Texture2D Hours = ContentFinder<Texture2D>.Get("UI/HH/Venues/Cmd_Hours", false) ?? BaseContent.BadTex;
        public static readonly Texture2D Price = ContentFinder<Texture2D>.Get("UI/HH/Venues/Cmd_Price", false) ?? BaseContent.BadTex;
        public static readonly Texture2D TakeGoods = ContentFinder<Texture2D>.Get("UI/HH/Venues/Cmd_TakeGoods", false) ?? BaseContent.BadTex;
        public static readonly Texture2D Rename = ContentFinder<Texture2D>.Get("UI/HH/Venues/Cmd_Rename", false) ?? BaseContent.BadTex;
        public static readonly Texture2D Restock = ContentFinder<Texture2D>.Get("UI/HH/Venues/Cmd_Restock", false) ?? BaseContent.BadTex;
        public static readonly Texture2D Setup = ContentFinder<Texture2D>.Get("UI/HH/Venues/Cmd_Setup", false) ?? BaseContent.BadTex;
        public static readonly Texture2D Honor = ContentFinder<Texture2D>.Get("UI/HH/Venues/Cmd_Honor", false) ?? BaseContent.BadTex;
        public static readonly Texture2D Dice = ContentFinder<Texture2D>.Get("UI/HH/Venues/Cmd_NameDice", false) ?? BaseContent.BadTex;
        public static readonly Texture2D CheckOk = ContentFinder<Texture2D>.Get("UI/HH/Venues/Setup_Ok", false) ?? BaseContent.BadTex;
        public static readonly Texture2D CheckMissing = ContentFinder<Texture2D>.Get("UI/HH/Venues/Setup_Missing", false) ?? BaseContent.BadTex;
        public static readonly Texture2D CheckWarn = ContentFinder<Texture2D>.Get("UI/HH/Venues/Setup_Warn", false) ?? BaseContent.BadTex;
        public static readonly Texture2D Show = ContentFinder<Texture2D>.Get("UI/HH/Venues/Setup_Show", false) ?? BaseContent.BadTex;
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
        /// <summary>"Restock" (N45): the sign sets the room's storage places to take these goods with priority Important.</summary>
        public bool restockOn;
        public ThingFilter restockFilter = new ThingFilter();
        List<StorageBackup> restockBackups = new List<StorageBackup>();
        public int RestockPlaces => restockBackups.Count;
        long incomeMicro;
        int dayStamp = -1, daySales;
        long dayIncomeMicro;
        // honor system (F15): switch and statistics (what was paid, what is missing)
        public bool honorSystem;
        public int honorSales, honorUnderpaid, honorUnpaid;
        long honorPaidMicro, honorShortMicro, honorDayShortMicro, honorDayPaidMicro;
        int honorDaySales;
        // business names (F33): the name was generated (not typed) and how often it was rolled
        public bool nameAuto;
        public int nameRoll;
        /// <summary>The name has been decided once (generated, typed or cleared by the player): a reinstalled sign keeps that choice.
        /// Signs of loaded saves count as decided, so old signs are never renamed.</summary>
        public bool nameDecided;
        // things the setup tab's "Show" button highlights until this real time
        [Unsaved] public List<Thing> highlight = new List<Thing>();
        [Unsaved] public float highlightUntil;

        public Money Income => new Money(incomeMicro);
        public Money IncomeToday { get { RollDay(); return new Money(dayIncomeMicro); } }
        public int SalesToday { get { RollDay(); return daySales; } }

        void RollDay()
        {
            if (Find.TickManager == null) return;
            int day = Find.TickManager.TicksGame / GenDate.TicksPerDay;
            if (day != dayStamp) { dayStamp = day; daySales = 0; dayIncomeMicro = 0; honorDaySales = 0; honorDayShortMicro = 0; honorDayPaidMicro = 0; }
        }

        long tipsMicro;
        /// <summary>Tips guests left for the staff of this venue (F14), all time.</summary>
        public Money Tips => new Money(tipsMicro);
        public void RecordTip(Money tip) { if (tip.IsPositive) tipsMicro += tip.Micro; }

        public Money HonorPaid => new Money(honorPaidMicro);
        public Money HonorShort => new Money(honorShortMicro);
        public Money HonorShortToday { get { RollDay(); return new Money(honorDayShortMicro); } }
        public int HonorSalesToday { get { RollDay(); return honorDaySales; } }
        /// <summary>Share of the list price that went missing at the honor box (0..1), over all honor sales.</summary>
        public float HonorLossShare => honorPaidMicro + honorShortMicro <= 0 ? 0f : (float)(honorShortMicro / (double)(honorPaidMicro + honorShortMicro));
        public float HonorLossShareToday { get { RollDay(); return honorDayPaidMicro + honorDayShortMicro <= 0 ? 0f : (float)(honorDayShortMicro / (double)(honorDayPaidMicro + honorDayShortMicro)); } }

        /// <summary>Books one honor-box sale in the statistics (the paid part went through the ledger already).</summary>
        public void RecordHonor(HonorOutcome o, Money price, Money paid)
        {
            RollDay();
            honorSales++; honorDaySales++;
            if (o == HonorOutcome.PaidPart) honorUnderpaid++;
            else if (o == HonorOutcome.PaidNothing) honorUnpaid++;
            var missing = price - paid;
            if (missing.IsNegative) missing = Money.Zero;
            honorPaidMicro += paid.Micro; honorDayPaidMicro += paid.Micro;
            honorShortMicro += missing.Micro; honorDayShortMicro += missing.Micro;
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
            // a new sign gets a fitting business name (F33); old signs in loaded saves keep theirs (or none)
            if (!respawningAfterLoad && !nameDecided && customName.NullOrEmpty())
                Guard.Run(ModuleId.ShopsFood, "Venues.AutoName", () => { if (VS.B(VS.AutoNames)) AutoName(); });
            nameDecided = true;
        }

        /// <summary>Gives the sign a generated name for its type (or rolls another one).</summary>
        public void AutoName(bool reroll = false)
        {
            if (reroll) nameRoll++;
            string n = VenueNames.For(type, parent, nameRoll);
            if (n.NullOrEmpty())
            {
                // a type without a name generator: a generated name of the old type would not fit, show the type's label instead
                if (nameAuto) customName = null;
                return;
            }
            customName = n; nameAuto = true;
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
            Scribe_Values.Look(ref restockOn, "restockOn");
            Scribe_Deep.Look(ref restockFilter, "restockFilter");
            Scribe_Collections.Look(ref restockBackups, "restockBackups", LookMode.Deep);
            Scribe_Values.Look(ref honorSystem, "honorSystem");
            Scribe_Values.Look(ref honorSales, "honorSales");
            Scribe_Values.Look(ref honorUnderpaid, "honorUnderpaid");
            Scribe_Values.Look(ref honorUnpaid, "honorUnpaid");
            Scribe_Values.Look(ref honorPaidMicro, "honorPaidMicro");
            Scribe_Values.Look(ref honorShortMicro, "honorShortMicro");
            Scribe_Values.Look(ref honorDaySales, "honorDaySales");
            Scribe_Values.Look(ref honorDayPaidMicro, "honorDayPaidMicro");
            Scribe_Values.Look(ref honorDayShortMicro, "honorDayShortMicro");
            Scribe_Values.Look(ref nameAuto, "nameAuto");
            Scribe_Values.Look(ref nameRoll, "nameRoll");
            Scribe_Values.Look(ref nameDecided, "nameDecided");
            if (Scribe.mode == LoadSaveMode.PostLoadInit) nameDecided = true;
            Scribe_Values.Look(ref tipsMicro, "tipsMicro");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (restockFilter == null) restockFilter = new ThingFilter();
                if (restockBackups == null) restockBackups = new List<StorageBackup>();
                restockBackups.RemoveAll(b => b == null || b.filter == null);
            }
        }

        public override void PostDeSpawn(Map map
#if RW16PLUS
            , DestroyMode mode = DestroyMode.Vanish
#endif
            )
        {
            // the sign lets go of the storage it took over (also when it is only minified or moved)
            Guard.Run(ModuleId.ShopsFood, "Restock.Restore(despawn)", () => RestoreRestock(map));
            base.PostDeSpawn(map
#if RW16PLUS
                , mode
#endif
                );
        }

        // ------------------------------------------------------------------ restock (N45)

        /// <summary>Switch restock on or off. Switching on first fills an empty goods filter with what the room sells now.</summary>
        public void SetRestock(bool on)
        {
            if (on == restockOn) return;
            if (on)
            {
                var v = Venue;
                if (v == null || VenueApi.SellsOf(v) == SellKind.None) { Messages.Message("HH.Venue.RestockNoShop".TranslateOr("This business does not sell anything from its room, so there is nothing to restock."), parent, MessageTypeDefOf.RejectInput, false); return; }
                restockFilter ??= new ThingFilter();
                if (restockFilter.AllowedDefCount == 0) foreach (var d in VenueRestock.GoodsNow(v)) restockFilter.SetAllow(d, true);
                restockOn = true;
                SyncRestock();
            }
            else
            {
                var map = parent.Map;
                restockOn = false;
                RestoreRestock(map);
            }
        }

        /// <summary>Hand filter and priority to every storage place of the room; remember what they had. Called when switched on and every 250 ticks.</summary>
        public void SyncRestock()
        {
            if (!restockOn || !parent.Spawned) return;
            var v = Venue;
            if (v == null || VenueApi.SellsOf(v) == SellKind.None) { restockOn = false; RestoreRestock(parent.Map); return; }
            var places = VenueRestock.StorageOf(v);
            var now = new HashSet<long>();
            foreach (var p in places)
            {
                VenueRestock.KeyOf(p, out int kind, out int id);
                now.Add(((long)kind << 32) | (uint)id);
                var st = p.GetStoreSettings();
                if (!restockBackups.Any(b => b.kind == kind && b.id == id))
                {
                    var copy = new ThingFilter();
                    copy.CopyAllowancesFrom(st.filter);
                    restockBackups.Add(new StorageBackup { kind = kind, id = id, priority = (int)st.Priority, filter = copy });
                }
                if (st.Priority != StoragePriority.Important) st.Priority = StoragePriority.Important;
                st.filter.CopyAllowancesFrom(restockFilter);
                p.Notify_SettingsChanged();
            }
            // places that left the room (zone shrunk, shelf carried away): give them their settings back
            for (int i = restockBackups.Count - 1; i >= 0; i--)
            {
                var b = restockBackups[i];
                if (now.Contains(((long)b.kind << 32) | (uint)b.id)) continue;
                RestoreOne(parent.Map, b);
                restockBackups.RemoveAt(i);
            }
        }

        static void RestoreOne(Map map, StorageBackup b)
        {
            var p = VenueRestock.Find(map, b);
            var st = p?.GetStoreSettings();
            if (st == null) return;
            st.Priority = (StoragePriority)b.priority;
            if (b.filter != null) st.filter.CopyAllowancesFrom(b.filter);
            p.Notify_SettingsChanged();
        }

        /// <summary>Put every storage place back the way it was.</summary>
        public void RestoreRestock(Map map)
        {
            if (restockBackups.Count == 0) return;
            foreach (var b in restockBackups) RestoreOne(map, b);
            restockBackups.Clear();
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
                // gated kinds (the drug lounge) stay hidden while the underworld is hidden (Cozy, not discovered)
                var gate = t.GetModExtension<Hearth.Underworld.UwGateExtension>();
                if (gate != null && !Hearth.Underworld.UwDefs.Open(gate)) continue;
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
            VenueSetup.MarkDirty(parent);
            // a generated name follows the new type; a name the player typed stays
            if (nameAuto && parent.Spawned) Guard.Run(ModuleId.ShopsFood, "Venues.AutoName(type)", () => AutoName());
            if (honorSystem && !VenueHonor.Offered(newType)) honorSystem = false;
            if (restockOn && VenueApi.SaleOf(newType)?.sells is SellKind k && k == SellKind.None) { restockOn = false; RestoreRestock(parent.Map); }
        }

        public void OpenTypeMenu()
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
            foreach (var g in SetupGizmos()) yield return g;
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
                yield return new Command_Action
                {
                    defaultLabel = "HH.Venue.Restock".TranslateOr("Restock") + ": " + (restockOn ? "HH.Venue.On".TranslateOr("on") : "HH.Venue.Off".TranslateOr("off")),
                    defaultDesc = "HH.Venue.RestockDesc".TranslateOr("Let this sign set the storage places in its room to take your goods with priority Important, so haulers keep the shelves full. Opt-in; old settings come back when you switch it off."),
                    icon = VenueTex.Restock,
                    action = () => Find.WindowStack.Add(new Dialog_VenueRestock(this)),
                };
                if (VenueHonor.Offered(type))
                    yield return new Command_Toggle
                    {
                        defaultLabel = "HH.Honor.Toggle".TranslateOr("Honor system"),
                        defaultDesc = "HH.Honor.ToggleDesc".TranslateOr("Run this business unattended: guests take what they want and leave the money in the honor box, at the full price. Nobody needs to be on duty, but some guests pay less than the price and a few pay nothing at all (chances in the settings; fewer when a colonist can see them). The sign's report shows what went missing."),
                        icon = VenueTex.Honor,
                        isActive = () => honorSystem,
                        toggleAction = () => { honorSystem = !honorSystem; if (parent.Spawned) Venues.MarkDirty(parent.Map); VenueSetup.MarkDirty(parent); },
                    };
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
                case VenueGate.Open: state = "HH.Venue.Open".TranslateOr("open") + (VenueApi.InLastOrders(v) ? ", " + "HH.Venue.LastOrders".TranslateOr("last orders") : ""); break;
                case VenueGate.WaitingForStaff: state = "HH.Venue.NoStaffState".TranslateOr("open, but nobody on duty"); break;
                case VenueGate.NotReady: state = "HH.Setup.NotReadyState".TranslateOr("not ready"); break;
                default: state = "HH.Venue.Closed".TranslateOr("closed"); break;
            }
            sb.Append(type.LabelCap).Append(" · ").Append(hours).Append(" · ").Append(state);
            var staffer = VenueApi.Staffer(v);
            if (VenueHonor.IsOn(v))
                sb.Append("\n").Append("HH.Honor.Line".TranslateOr("Honor system: unattended"));
            else if (VenueApi.NeedsStaff(v) || staffer != null)
            {
                sb.Append("\n").Append("HH.Venue.Staff".TranslateOr("On duty")).Append(": ").Append(staffer != null ? VenueService.TenderLine(staffer) : "HH.Venue.Nobody".TranslateOr("nobody"));
            }
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
            if (honorSales > 0 || VenueHonor.IsOn(v))
                sb.Append("\n").Append(HonorReportLine());
            string setup = VenueSetup.Summary(v);
            if (!setup.NullOrEmpty()) sb.Append("\n").Append(setup);
            return sb.ToString();
        }

        /// <summary>Readiness checklist (B1, since 1.1.0 the setup report of F12): one line per requirement with a tick or a cross.</summary>
        public IEnumerable<string> Checklist(Venue v)
        {
            foreach (var r in VenueSetup.Report(v).Rows)
                yield return (r.Ok ? "✔ " : r.Req.required ? "✘ " : "○ ") + r.Label + (r.CountText.NullOrEmpty() ? "" : " (" + r.CountText + ")");
        }

        /// <summary>"Honor box today: 5 sales, 3.20 silver missing (−12 %) · total 2 unpaid, 4 short".</summary>
        public string HonorReportLine()
        {
            return "HH.Honor.Report".TranslateOr("Honor box today: {0} sales, {1} silver missing ({2} %) · all time: {3} sales, {4} underpaid, {5} unpaid, {6} silver missing")
                .Replace("{0}", HonorSalesToday.ToString()).Replace("{1}", HonorShortToday.ToStringShort()).Replace("{2}", (HonorLossShareToday * 100f).ToString("0"))
                .Replace("{3}", honorSales.ToString()).Replace("{4}", honorUnderpaid.ToString()).Replace("{5}", honorUnpaid.ToString()).Replace("{6}", HonorShort.ToStringShort());
        }

        // ------------------------------------------------------------------ setup (F12)

        /// <summary>Gizmos of the setup guide: the guide button (opens the Setup tab) and build buttons for what is still missing.</summary>
        IEnumerable<Gizmo> SetupGizmos()
        {
            var v = Venue;
            if (v == null || type == null) yield break;
            var rep = VenueSetup.Report(v);
            int missing = rep.RequiredMissing;
            yield return new Command_Action
            {
                defaultLabel = missing > 0 ? "HH.Setup.GizmoMissing".TranslateOr("Setup: {0} missing").Replace("{0}", missing.ToString()) : "HH.Setup.GizmoReady".TranslateOr("Setup: ready"),
                defaultDesc = "HH.Setup.GizmoDesc".TranslateOr("Open the setup guide of this business: what its kind needs, what it already has, with build buttons for everything that is missing."),
                icon = VenueTex.Setup,
                defaultIconColor = missing > 0 ? new Color(1f, 0.75f, 0.6f) : Color.white,
                action = OpenSetupTab,
            };
            if (!VS.B(VS.SetupHints)) yield break;
            int shown = 0;
            foreach (var row in rep.Rows)
            {
                if (row.Ok || shown >= 2) continue;
                if (row.Req.check == VenueCheck.Stock || row.Req.check == VenueCheck.ServingWorker || row.Req.check == VenueCheck.Power) continue;
                if (!row.Req.required) continue;
                var cmd = BuildCommandFor(row);
                if (cmd == null) continue;
                shown++;
                yield return cmd;
            }
        }

        /// <summary>A build button for the first buildable option of a missing requirement ("Build: counter").</summary>
        Command BuildCommandFor(SetupRow row)
        {
            foreach (var d in VenueSetup.BuildDefs(row.Req))
            {
                var des = VenueSetup.DesignatorFor(d, out _);
                if (des == null) continue;
                var cmd = new Command_Action
                {
                    defaultLabel = "HH.Setup.BuildButton".TranslateOr("Build: {0}").Replace("{0}", d.label),
                    defaultDesc = "HH.Setup.BuildButtonDesc".TranslateOr("This {0} still needs {1}. Click to place one in the room.").Replace("{0}", type.label).Replace("{1}", row.Req.label)
                                  + (row.Req.hint.NullOrEmpty() ? "" : "\n\n" + row.Req.hint),
                    icon = des.icon ?? d.uiIcon,
                    iconProportions = des.iconProportions,
                    iconDrawScale = des.iconDrawScale,
                    iconTexCoords = des.iconTexCoords,
                    iconAngle = des.iconAngle,
                    iconOffset = des.iconOffset,
                    defaultIconColor = des.IconDrawColor,
                    action = () => { SoundDefOf.Tick_Tiny.PlayOneShotOnCamera(); VenueSetup.StartBuilding(d); },
                };
                return cmd;
            }
            return null;
        }

        public void OpenSetupTab()
        {
            Guard.Run(ModuleId.ShopsFood, "Venues.OpenSetupTab", () =>
            {
                if (!Find.Selector.IsSelected(parent)) { Find.Selector.ClearSelection(); Find.Selector.Select(parent); }
                InspectPaneUtility.OpenTab(typeof(ITab_VenueSetup));
            });
        }

        /// <summary>Highlights things for a few seconds (the "Show" button of the setup tab).</summary>
        public void Highlight(IEnumerable<Thing> things)
        {
            highlight.Clear(); highlight.AddRange(things);
            highlightUntil = Time.realtimeSinceStartup + 4f;
        }

        static readonly List<IntVec3> cellsScratch = new List<IntVec3>();
        public override void PostDrawExtraSelectionOverlays()
        {
            base.PostDrawExtraSelectionOverlays();
            var v = Venue; if (v?.Room == null) return;
            cellsScratch.Clear();
            foreach (var c in v.Room.Cells) cellsScratch.Add(c);
            if (cellsScratch.Count > 0) GenDraw.DrawFieldEdges(cellsScratch, type != null ? type.color : Color.white);
            if (highlight.Count > 0)
            {
                if (Time.realtimeSinceStartup > highlightUntil) highlight.Clear();
                else foreach (var t in highlight) if (t != null && t.Spawned && t.Map == parent.Map) GenDraw.DrawTargetHighlight(t);
            }
        }

        public override void DrawGUIOverlay()
        {
            base.DrawGUIOverlay();
            if (type == null || !parent.Spawned || Find.CameraDriver.CurrentZoom > CameraZoomRange.Close) return;
            GenMapUI.DrawThingLabel(parent, DisplayName, Color.white);
        }
    }
}
