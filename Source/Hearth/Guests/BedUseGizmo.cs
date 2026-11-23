// SPDX-License-Identifier: MIT
// F10 (1.1.0): ONE "bed use" button for every colony bed (what is this bed for?), with the guest price folded into it.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using Hearth.Shell;

namespace Hearth.Guests
{
    /// <summary>What a bed is used for. The names match RimWard's own enum (<c>RimWard.BedUse</c>) so both mods speak the same words.</summary>
    public enum BedRole { Colonist, Guest, Patient, Surgery }

    /// <summary>
    /// The roles a bed can have and how to switch them. Without RimWard: Colonist and Guest (this mod). With RimWard (soft, reflection, protocol in docs/rimward.md):
    /// Patient and Surgery as well, driven through <c>RimWard.BedApi</c>, so there is still exactly one button.
    /// </summary>
    public static class BedRoles
    {
        static bool resolved; static Type tSelector, tApi, tUse;
        static MethodInfo mCurrent, mEntries, mBlocked, mApply;

        /// <summary>RimWard's bed selector API is there and usable (resolved once, logged once).</summary>
        public static bool RimWardPresent
        {
            get
            {
                if (resolved) return tApi != null;
                resolved = true;
                try
                {
                    tSelector = GenTypes.GetTypeInAnyAssembly("RimWard.BedSelector");
                    tApi = GenTypes.GetTypeInAnyAssembly("RimWard.BedApi");
                    tUse = GenTypes.GetTypeInAnyAssembly("RimWard.BedUse");
                    if (tSelector == null || tApi == null || tUse == null) { tApi = null; return false; }
                    mCurrent = tSelector.GetMethod("Current", new[] { typeof(Building_Bed) });
                    mEntries = tSelector.GetMethod("Entries", new[] { typeof(Building_Bed) });
                    mBlocked = tSelector.GetMethod("BlockedReason", new[] { typeof(Building_Bed), tUse });
                    mApply = tApi.GetMethod("Apply", new[] { typeof(Building_Bed), tUse });
                    if (mCurrent == null || mEntries == null || mBlocked == null || mApply == null) { tApi = null; HLog.Warn("Inn", "RimWard found, but its bed API has another shape: using the Colonist/Guest bed button only"); return false; }
                    HLog.Info("Inn", "RimWard bed API found: the one bed button also offers Patient and Surgery");
                }
                catch (Exception e) { tApi = null; HLog.Warn("Inn", "RimWard bed API could not be resolved: " + e.Message); }
                return tApi != null;
            }
        }

        static object UseObj(BedRole r) => Enum.Parse(tUse, r.ToString());

        public static BedRole Current(Building_Bed bed)
        {
            if (RimWardPresent)
            {
                try { if (Enum.TryParse(mCurrent.Invoke(null, new object[] { bed }).ToString(), out BedRole rr)) return rr; }
                catch (Exception e) { HLog.Once("bedroles.current", LogLevel.Warn, "Inn", "RimWard bed role failed: " + e.Message); }
            }
            return GuestBeds.Comp(bed)?.Flag == true ? BedRole.Guest : BedRole.Colonist;
        }

        /// <summary>The roles to list for this bed, in menu order.</summary>
        public static List<BedRole> Entries(Building_Bed bed)
        {
            if (RimWardPresent)
            {
                try
                {
                    var res = new List<BedRole>();
                    foreach (var o in (System.Collections.IEnumerable)mEntries.Invoke(null, new object[] { bed }))
                        if (Enum.TryParse(o.ToString(), out BedRole rr)) res.Add(rr);
                    if (res.Count > 0) return res;
                }
                catch (Exception e) { HLog.Once("bedroles.entries", LogLevel.Warn, "Inn", "RimWard bed entries failed: " + e.Message); }
            }
            var list = new List<BedRole> { BedRole.Colonist };
            if (Modules.On(ModuleId.Inn)) list.Add(BedRole.Guest);
            return list;
        }

        /// <summary>Null when the bed may take this role right now, otherwise the reason (shown greyed out in the menu).</summary>
        public static string Blocked(Building_Bed bed, BedRole role)
        {
            if (RimWardPresent)
            {
                try { return (string)mBlocked.Invoke(null, new object[] { bed, UseObj(role) }); }
                catch (Exception e) { HLog.Once("bedroles.blocked", LogLevel.Warn, "Inn", "RimWard bed check failed: " + e.Message); }
            }
            var comp = GuestBeds.Comp(bed);
            switch (role)
            {
                case BedRole.Guest: return comp == null ? "HH.Inn.BedUse.NoBed".TranslateOr("This bed cannot be a guest bed.") : (comp.Flag ? null : comp.SelectorBlockedReason);
                case BedRole.Patient:
                case BedRole.Surgery: return "HH.Inn.BedUse.NeedsRimWard".TranslateOr("Patient and surgery beds need the RimWard mod.");
                default: return null;
            }
        }

        /// <summary>Sets the role; false (and nothing changed) when it is not possible.</summary>
        public static bool Apply(Building_Bed bed, BedRole role)
        {
            if (bed == null || Blocked(bed, role) != null) return false;
            if (RimWardPresent)
            {
                try { return (bool)mApply.Invoke(null, new object[] { bed, UseObj(role) }); }
                catch (Exception e) { HLog.Warn("Inn", "RimWard could not set the bed role: " + e.Message); return false; }
            }
            var comp = GuestBeds.Comp(bed); if (comp == null) return false;
            comp.SetGuestBed(role == BedRole.Guest);
            return true;
        }

        public static Texture2D Icon(BedRole r)
        {
            switch (r)
            {
                case BedRole.Guest: return InnTex.BedUse_Guest;
                case BedRole.Patient: return InnTex.BedUse_Patient;
                case BedRole.Surgery: return InnTex.BedUse_Surgery;
                default: return InnTex.BedUse_Colonist;
            }
        }

        public static string Label(BedRole r)
        {
            switch (r)
            {
                case BedRole.Guest: return "HH.Inn.BedUse.Guest".TranslateOr("Guest bed");
                case BedRole.Patient: return "HH.Inn.BedUse.Patient".TranslateOr("Patient bed");
                case BedRole.Surgery: return "HH.Inn.BedUse.Surgery".TranslateOr("Surgery bed");
                default: return "HH.Inn.BedUse.Colonist".TranslateOr("Colonist bed");
            }
        }

        public static string Explain(BedRole r)
        {
            switch (r)
            {
                case BedRole.Guest: return "HH.Inn.BedUse.GuestDesc".TranslateOr("rented to guests, colonists keep out");
                case BedRole.Patient: return "HH.Inn.BedUse.PatientDesc".TranslateOr("for the sick and wounded");
                case BedRole.Surgery: return "HH.Inn.BedUse.SurgeryDesc".TranslateOr("operations are done here");
                default: return "HH.Inn.BedUse.ColonistDesc".TranslateOr("the normal sleeping place of your colonists");
            }
        }
    }

    /// <summary>
    /// The single bed button (F10): its icon and name show what the bed is used for, a click opens a menu with every role, each with a short explanation.
    /// For a guest bed a small price chip sits in the corner of the same button and opens the price menu; there is never a second button.
    /// </summary>
    public sealed class Command_HH_BedUse : Command_Action
    {
        readonly Building_Bed bed;
        readonly CompHH_GuestBed comp;
        readonly BedRole role;

        // The gizmo is rebuilt every frame while the bed is selected: role (RimWard is read by reflection) and description are cached for a few ticks.
        struct Cached { public int Tick; public BedRole Role; public string Desc; }
        static readonly Dictionary<int, Cached> cache = new Dictionary<int, Cached>();
        const int CacheTicks = 20;
        internal static void ClearCache() => cache.Clear();

        public Command_HH_BedUse(CompHH_GuestBed comp, Building_Bed bed)
        {
            this.comp = comp; this.bed = bed;
            int now = Find.TickManager.TicksGame;
            if (cache.TryGetValue(bed.thingIDNumber, out var c) && now - c.Tick < CacheTicks && now >= c.Tick) { role = c.Role; defaultDesc = c.Desc; }
            else
            {
                role = BedRoles.Current(bed);
                defaultDesc = BuildDesc();
                if (cache.Count > 200) cache.Clear();
                cache[bed.thingIDNumber] = new Cached { Tick = now, Role = role, Desc = defaultDesc };
            }
            bool plainMedical = bed.Medical && !BedRoles.RimWardPresent;
            // a medical bed without RimWard is not a colonist bed: say so instead of showing a role it cannot have
            defaultLabel = plainMedical ? "HH.Inn.BedUse.Medical".TranslateOr("Medical bed") : BedRoles.Label(role);
            icon = BedRoles.Icon(role);
            hotKey = KeyBindingDefOf.Misc5;
            Order = -90f;
            action = OpenMenu;
            if (plainMedical)
                Disable("HH.Inn.GuestBed.NotColonistBed".TranslateOr("Only a normal colonist bed (not medical, prisoner or slave) can be a guest bed."));
        }

        public override bool GroupsWith(Gizmo other) => other is Command_HH_BedUse o && o.role == role;

        string BuildDesc()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(BedRoles.Label(role)).Append(": ").Append(BedRoles.Explain(role)).Append('.');
            if (role == BedRole.Guest && comp != null && bed.Spawned)
            {
                var q = GuestBedPricing.Quote(bed);
                if (q != null)
                {
                    sb.Append("\n\n").Append("HH.Inn.BedUse.Price".TranslateOr("Price: {0} silver a night ({1}).").Replace("{0}", q.Price.WholeSilver.ToString()).Replace("{1}", q.Breakdown));
                    sb.Append("\n").Append("HH.Inn.BedUse.PriceHint".TranslateOr("Click the price tag in the corner to change the price."));
                }
            }
            else if (role == BedRole.Colonist)
            {
                var owners = bed.OwnersForReading;
                if (owners.Count > 0) sb.Append("\n\n").Append("HH.Inn.BedUse.Owners".TranslateOr("Owner: {0}").Replace("{0}", string.Join(", ", owners.Select(o => o.LabelShort))));
            }
            sb.Append("\n\n").Append("HH.Inn.BedUse.Click".TranslateOr("Click to choose what this bed is used for."));
            return sb.ToString();
        }

        /// <summary>Every selected bed that can take the role gets it (works like vanilla gizmos).</summary>
        List<Building_Bed> SelectedBeds()
        {
            var list = new List<Building_Bed>();
            foreach (var o in Find.Selector.SelectedObjectsListForReading)
                if (o is Building_Bed b && b.Faction == Faction.OfPlayer && b.def.building != null && b.def.building.bed_humanlike) list.Add(b);
            if (!list.Contains(bed)) list.Add(bed);
            return list;
        }

        void OpenMenu() => Find.WindowStack.Add(new FloatMenu(MenuOptions()));

        internal List<FloatMenuOption> MenuOptions()
        {
            var beds = SelectedBeds();
            var opts = new List<FloatMenuOption>();
            foreach (var r in BedRoles.Entries(bed))
            {
                var rr = r;
                string name = BedRoles.Label(rr) + (rr == role ? "  ✓" : "");
                var ok = beds.Where(b => BedRoles.Blocked(b, rr) == null).ToList();
                if (ok.Count == 0)
                {
                    string why = BedRoles.Blocked(beds[0], rr);
                    opts.Add(new FloatMenuOption(name + " — " + (why.NullOrEmpty() ? BedRoles.Explain(rr) : why), null, BedRoles.Icon(rr), Color.white));
                }
                else
                    opts.Add(new FloatMenuOption(name + " — " + BedRoles.Explain(rr), () => { foreach (var b in ok) BedRoles.Apply(b, rr); GuestBedPricing.ClearCache(); ClearCache(); }, BedRoles.Icon(rr), Color.white));
            }
            return opts;
        }

        internal BedRole Role => role;

        // ---- the price chip in the corner of a guest bed's button
        static Rect ChipRect(Vector2 topLeft, float width) => new Rect(topLeft.x + width - 46f, topLeft.y + 3f, 43f, 19f);

        string ChipText()
        {
            switch (comp.PriceMode)
            {
                case BedPriceMode.Free: return "HH.Inn.PriceMode.Free".TranslateOr("free");
                default:
                    var q = GuestBedPricing.Quote(bed);
                    return q == null ? "?" : q.Price.WholeSilver.ToString();
            }
        }

        public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            bool chip = role == BedRole.Guest && comp != null && !Disabled && bed.Spawned;
            Rect cr = chip ? ChipRect(topLeft, GetWidth(maxWidth)) : default(Rect);
            if (chip && Event.current.type == EventType.MouseDown && Event.current.button == 0 && cr.Contains(Event.current.mousePosition))
            {
                Event.current.Use();
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                Guard.Run(ModuleId.Inn, "Inn.PriceChipClick", comp.OpenPriceMenu);
                return new GizmoResult(GizmoState.Clear);
            }
            var res = base.GizmoOnGUI(topLeft, maxWidth, parms);
            if (chip) Guard.Run(ModuleId.Inn, "Inn.PriceChip", () => DrawChip(cr));
            return res;
        }

        void DrawChip(Rect cr)
        {
            bool custom = comp.PriceMode != BedPriceMode.Auto;
            Widgets.DrawBoxSolid(cr, new Color(0.07f, 0.06f, 0.05f, 0.82f));
            GUI.color = custom ? UiStyle.Amber : new Color(1f, 1f, 1f, 0.35f);
            Widgets.DrawBox(cr);
            GUI.color = Mouse.IsOver(cr) ? Color.white : (custom ? UiStyle.Amber : new Color(0.95f, 0.9f, 0.8f));
            var anchor = Text.Anchor; var font = Text.Font;
            Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(cr, ChipText());
            Text.Anchor = anchor; Text.Font = font; GUI.color = Color.white;
            TooltipHandler.TipRegion(cr, () => "HH.Inn.BedUse.ChipTip".TranslateOr("Price a night: {0}. Click to change it: free, −25 %, automatic, +25 % or a fixed price.").Replace("{0}", CompHH_GuestBed.PriceModeLabel(comp.PriceMode)), 7142857 + bed.thingIDNumber);
        }
    }
}
