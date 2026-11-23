// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Hearth;
using Hearth.Insurance;
using RimWorld;
using UnityEngine;
using Verse;

namespace Silverline
{
    /// <summary>The kinds of cover shown per colonist (1.1.0, F28).</summary>
    /// <summary>The five named kinds have their own icon and label; Travel is the caravan cover; Other is any further per-person policy (data driven).</summary>
    public enum CoverKind { Pension, Health, Accident, Liability, Legal, Travel, Other }

    public enum CoverState { None, Active, Waiting, CancelPending, AtRisk }

    /// <summary>One line of the insurance status of a colonist.</summary>
    public sealed class CoverItem
    {
        public CoverKind kind;
        public CoverState state;
        public PolicyContract contract;       // the contract that gives the cover (person or colony)
        public bool colonyWide;
        public string policyLabel;
        public SL_PolicyDef def;              // the policy of this kind (also when nothing is signed: for the "buy" button)
        public bool Has => state != CoverState.None;
    }

    /// <summary>
    /// Which cover each colonist has: pension and accident are personal contracts, health, liability and legal are colony-wide contracts that
    /// cover every colonist ("colony cover"). Humanlike colonists only, so alien races and modded pawn kinds work like vanilla ones. Cached per pawn
    /// (refreshed every 250 ticks or when a contract changes).
    /// </summary>
    public static class InsuranceStatus
    {
        static readonly Dictionary<int, KeyValuePair<int, List<CoverItem>>> cache = new Dictionary<int, KeyValuePair<int, List<CoverItem>>>();   // pawn id -> (stamp, items)
        static int cacheVersion = -1, cacheTick = -9999;

        static readonly string[] KnownOrder = { "SL_GoldenYears", "SL_RimHealth", "SL_LimbByLimb", "SL_ItWasntMe", "SL_LegalShield", "SL_Wanderlust" };
        static List<SL_PolicyDef> defs;

        /// <summary>
        /// The policies that are shown per colonist: every policy of a person or a caravan (Person, Travel) and every colony policy that
        /// protects the people themselves (priced per colonist: health, liability, legal). Property policies (buildings, crops, livestock) are
        /// not about a person. Not a fixed list: a policy added by data shows up here and in all tables.
        /// </summary>
        public static List<SL_PolicyDef> Defs
        {
            get
            {
                if (defs == null)
                {
                    var l = DefDatabase<SL_PolicyDef>.AllDefs.Where(d => d.kind != PolicyKind.Colony || d.riskModel == RiskModel.PerColonist).ToList();
                    int Rank(SL_PolicyDef d) { int i = Array.IndexOf(KnownOrder, d.defName); return i >= 0 ? i : 100 + d.order; }
                    defs = l.OrderBy(Rank).ThenBy(d => d.defName, StringComparer.Ordinal).ToList();
                }
                return defs;
            }
        }

        public static CoverKind KindOf(SL_PolicyDef d)
        {
            switch (d?.defName)
            {
                case "SL_GoldenYears": return CoverKind.Pension;
                case "SL_RimHealth": return CoverKind.Health;
                case "SL_LimbByLimb": return CoverKind.Accident;
                case "SL_ItWasntMe": return CoverKind.Liability;
                case "SL_LegalShield": return CoverKind.Legal;
                case "SL_Wanderlust": return CoverKind.Travel;
                default: return d != null && d.kind == PolicyKind.Travel ? CoverKind.Travel : CoverKind.Other;
            }
        }

        public static string PolicyOf(CoverKind k)
        {
            switch (k)
            {
                case CoverKind.Pension: return "SL_GoldenYears";
                case CoverKind.Health: return "SL_RimHealth";
                case CoverKind.Accident: return "SL_LimbByLimb";
                case CoverKind.Liability: return "SL_ItWasntMe";
                case CoverKind.Travel: return "SL_Wanderlust";
                case CoverKind.Legal: return "SL_LegalShield";
                default: return null;
            }
        }

        public static string KindLabel(CoverKind k) => k == CoverKind.Other ? "?" : Ins.T("Status.Kind." + k);

        /// <summary>Short column / row label of a policy: the translated name of the five known kinds, the policy label otherwise.</summary>
        public static string KindLabel(SL_PolicyDef d)
        {
            var k = KindOf(d);
            return k == CoverKind.Other ? (d?.LabelCap.ToString() ?? "?") : KindLabel(k);
        }

        /// <summary>Insurance UI (tab, column, page) is only shown once the first policy exists: a module with nothing to show has no visible effect (N08).</summary>
        public static bool UiActive => Modules.On(ModuleId.Bank) && (InsuranceComponent.Instance?.Started ?? false);

        /// <summary>Colonists of the colony (humanlike, player faction) can be insured; slaves, prisoners, guests, animals and mechs cannot.</summary>
        public static bool Insurable(Pawn p) => p != null && !p.Dead && p.RaceProps != null && p.RaceProps.Humanlike && p.Faction == Faction.OfPlayer && p.IsColonist;

        /// <summary>Humanlike pawn of the colony that is shown (also slaves: they get "not insurable").</summary>
        /// <summary>The player's living humanlike colonists wherever they are (home map, other maps, caravans).</summary>
        public static List<Pawn> AllColonists()
        {
            var l = new List<Pawn>();
            IEnumerable<Pawn> all = PawnsFinder.AllMaps_FreeColonists;
            foreach (var p in all) if (p != null && !p.Dead && p.RaceProps.Humanlike) l.Add(p);
            foreach (var car in Find.WorldObjects.Caravans)   // pawns away on a caravan are not on any map
                foreach (var p in car.PawnsListForReading) if (p != null && !p.Dead && p.IsFreeColonist && p.RaceProps.Humanlike && !l.Contains(p)) l.Add(p);
            return l;
        }

        public static bool Shown(Pawn p) => p != null && !p.Dead && p.RaceProps != null && p.RaceProps.Humanlike && p.Faction == Faction.OfPlayer;

        public static List<CoverItem> Of(Pawn p)
        {
            var comp = InsuranceComponent.Instance;
            int now = Find.TickManager?.TicksGame ?? 0;
            if (comp == null || p == null) return new List<CoverItem>();
            if (cacheVersion != comp.statusVersion || now - cacheTick >= 250 || now < cacheTick) { cache.Clear(); cacheVersion = comp.statusVersion; cacheTick = now; }
            if (cache.TryGetValue(p.thingIDNumber, out var hit)) return hit.Value;
            var list = Build(comp, p, now);
            cache[p.thingIDNumber] = new KeyValuePair<int, List<CoverItem>>(now, list);
            return list;
        }

        /// <summary>For tests: drop the cache.</summary>
        public static void Reset() { cache.Clear(); cacheVersion = -1; defs = null; }

        static List<CoverItem> Build(InsuranceComponent comp, Pawn p, int now)
        {
            var res = new List<CoverItem>();
            foreach (var def in Defs)
            {
                var kind = KindOf(def);
                string defName = def.defName;
                var item = new CoverItem { kind = kind, def = def, state = CoverState.None, policyLabel = def.LabelCap };
                bool person = def.kind == PolicyKind.Person;
                var c = comp.contracts.FirstOrDefault(x => x.Active && x.defName == defName && (person ? x.holder == p : x.holder == null));
                if (c != null && Insurable(p))
                {
                    item.contract = c; item.colonyWide = !person;
                    item.state = c.CancelPending ? CoverState.CancelPending : c.InWaitingPeriod(now) ? CoverState.Waiting : CoverState.Active;
                    if (kind == CoverKind.Pension && PensionRule.AtRisk(c, now)) item.state = CoverState.AtRisk;
                }
                res.Add(item);
            }
            return res;
        }

        public static CoverItem Of(Pawn p, CoverKind k) => Of(p).FirstOrDefault(x => x.kind == k);
        public static CoverItem Of(Pawn p, SL_PolicyDef d) => Of(p).FirstOrDefault(x => x.def == d);

        public static Color ColorOf(CoverState s)
        {
            switch (s)
            {
                case CoverState.Active: return new Color(0.45f, 0.82f, 0.52f);
                case CoverState.Waiting: return new Color(0.96f, 0.78f, 0.25f);
                case CoverState.CancelPending: return new Color(0.9f, 0.62f, 0.4f);
                case CoverState.AtRisk: return new Color(0.92f, 0.45f, 0.40f);
                default: return new Color(0.5f, 0.52f, 0.56f);
            }
        }

        public static string StateLabel(CoverState s) => Ins.T("Status.State." + s);

        public static Texture2D Icon(CoverKind k)
        {
            switch (k)
            {
                case CoverKind.Pension: return ContentFinder<Texture2D>.Get("UI/HH/Insurance/Status/Pension", false);
                case CoverKind.Health: return ContentFinder<Texture2D>.Get("UI/HH/Insurance/Status/Health", false);
                case CoverKind.Accident: return ContentFinder<Texture2D>.Get("UI/HH/Insurance/Status/Accident", false);
                case CoverKind.Liability: return ContentFinder<Texture2D>.Get("UI/HH/Insurance/Status/Liability", false);
                case CoverKind.Travel: return ContentFinder<Texture2D>.Get("UI/HH/Insurance/Status/Travel", false);
                case CoverKind.Legal: return ContentFinder<Texture2D>.Get("UI/HH/Insurance/Status/Legal", false);
                default: return null;
            }
        }

        /// <summary>Icon of a cover item: its own kind icon, or the policy's icon (further policies), or nothing (the UI draws a coloured square).</summary>
        public static Texture2D Icon(CoverItem it)
        {
            var t = Icon(it.kind);
            if (t != null) return t;
            return it.def != null && !it.def.iconPath.NullOrEmpty() ? ContentFinder<Texture2D>.Get(it.def.iconPath, false) : null;
        }

        /// <summary>One-line text for a kind of cover ("Pension: yes (Good Enough, 3 strikes left)").</summary>
        public static string Line(Pawn p, CoverItem it)
        {
            if (!Insurable(p)) return KindLabel(it.def) + ": " + Ins.T("Status.NotInsurable");
            if (!it.Has) return KindLabel(it.def) + ": " + Ins.T("Status.No");
            var c = it.contract;
            string s = KindLabel(it.def) + ": " + Ins.T("Status.Yes") + " (" + Ins.TierName(c.tier) + (it.colonyWide ? ", " + Ins.T("Status.ColonyCover") : "") + ")";
            if (it.state == CoverState.Waiting) s += " · " + Ins.T("Status.State.Waiting") + " " + (c.waitUntilTick - Ins.Now).ToStringTicksToPeriod();
            if (it.state == CoverState.CancelPending) s += " · " + Ins.T("Status.State.CancelPending");
            if (it.kind == CoverKind.Pension && PensionRule.Enabled && InsSettings.PensionMode == PensionRuleMode.Enforce && c.pensionStrikes > 0) s += " · " + Ins.T("Status.Strikes", Math.Max(0, 3 - c.pensionStrikes));
            return s;
        }

        public static string Tooltip(Pawn p, CoverItem it)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<b>" + it.policyLabel + "</b>");
            if (!Insurable(p)) { sb.Append(Ins.T("Status.NotInsurableTip")); return sb.ToString(); }
            if (!it.Has) { sb.Append(Ins.T("Status.NoTip", it.policyLabel)); return sb.ToString(); }
            var c = it.contract;
            sb.AppendLine(Line(p, it));
            var q = InsurancePricing.QuoteFor(c);
            sb.AppendLine(Ins.T("UI.Mine.Sum", Ins.Silver(c.sum)));
            sb.AppendLine((q.perTravelDay ? Ins.T("UI.Price.Travel", Ins.Silver(q.travelPerHeadDay)) : Ins.T("UI.Price", Ins.Silver(q.billing), q.billingDays)) + (it.colonyWide ? " (" + Ins.T("Status.ColonyCover") + ")" : ""));
            if (c.InWaitingPeriod(Ins.Now)) sb.AppendLine(Ins.T("UI.Mine.Waiting", (c.waitUntilTick - Ins.Now).ToStringTicksToPeriod()));
            if (it.kind == CoverKind.Pension && c.holder == p)
            {
                if (it.state == CoverState.AtRisk) sb.AppendLine(Ins.T("Status.PensionRisk", PensionRule.WorkHours(p), InsSettings.PensionHours));
                else if (PensionRule.Enabled && c.holder.ageTracker.AgeBiologicalYears >= 60 && c.tier != CoverTier.BareMinimum) sb.AppendLine(Ins.T("Status.PensionOk", PensionRule.WorkHours(p), InsSettings.PensionHours));
            }
            if (c.HasPendingChange) sb.AppendLine(Ins.T("UI.Mine.PendingChange", (c.pendingEffectiveTick - Ins.Now).ToStringTicksToPeriod()));
            return sb.ToString().TrimEnd();
        }
    }

    /// <summary>"Insurance" tab on the pawn (colonists only, once the first policy exists).</summary>
    public class ITab_SL_Insurance : ITab
    {
        Vector2 scroll;

        public ITab_SL_Insurance()
        {
            size = TabSizeNow();
            labelKey = "HH.Ins.Tab.Pawn";
            tutorTag = "SLInsurance";
        }

        // F35: sized like the other tabs (HUI: screen height and the window-size setting), re-read every frame
        static Vector2 TabSizeNow() => HUI.Size(560f, 460f, 440f, 360f, 0.45f, 0.7f);

        protected override void UpdateSize() { size = TabSizeNow(); }

        /// <summary>The size the tab has right now (tests read it).</summary>
        public Vector2 CurrentSize => size;

        public override bool IsVisible
        {
            get
            {
                var p = SelPawn;
                if (p == null || !InsuranceStatus.UiActive || !HSettings.GetBool(InsSettings.ShowStatusTab)) return false;
                return Guard.Run(ModuleId.Bank, "Insurance.TabVisible", () => InsuranceStatus.Shown(p), false);
            }
        }

        protected override void FillTab()
        {
            var p = SelPawn; if (p == null) return;
            var rect = new Rect(0f, 0f, size.x, size.y).ContractedBy(10f);
            Guard.Run(ModuleId.Bank, "Insurance.Tab", () => Draw(rect, p));
        }

        void Draw(Rect rect, Pawn p)
        {
            Text.Font = GameFont.Medium; Widgets.Label(new Rect(rect.x, rect.y, rect.width, 30f), Ins.T("Status.Title", p.LabelShortCap)); Text.Font = GameFont.Small;
            float y = rect.y + 34f;
            var items = InsuranceStatus.Of(p);
            bool ins = InsuranceStatus.Insurable(p);
            float rowH = 44f;
            var view = new Rect(rect.x, y, rect.width, rect.height - 34f);
            var inner = new Rect(0, 0, view.width - 18f, items.Count * (rowH + 4f) + 120f);
            Widgets.BeginScrollView(view, ref scroll, inner);
            float yy = 0f;
            foreach (var it in items)
            {
                var row = new Rect(0, yy, inner.width, rowH);
                Widgets.DrawBoxSolid(row, new Color(0f, 0f, 0f, 0.18f));
                GUI.color = InsuranceStatus.ColorOf(ins ? it.state : CoverState.None); Widgets.DrawBox(row); GUI.color = Color.white;
                var icon = InsuranceStatus.Icon(it);
                var ir = new Rect(row.x + 6f, row.y + 6f, 32f, 32f);
                if (icon != null) { GUI.color = InsuranceStatus.ColorOf(ins ? it.state : CoverState.None); GUI.DrawTexture(ir, icon); GUI.color = Color.white; }
                float textW = row.width - 52f - 120f;
                string line = InsuranceStatus.Line(p, it);
                var f = Text.Font; Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(row.x + 46f, row.y + 2f, textW, rowH - 4f), line);
                Text.Font = f;
                TooltipHandler.TipRegion(row, InsuranceStatus.Tooltip(p, it));
                if (ins && !it.Has && it.def != null)
                {
                    if (Widgets.ButtonText(new Rect(row.xMax - 112f, row.y + 9f, 106f, 26f), Ins.T("Status.Buy"))) InsuranceUI.OpenShopFor(it.def, p);
                }
                else if (ins && it.Has && it.kind == CoverKind.Pension && it.state == CoverState.AtRisk)
                {
                    if (Widgets.ButtonText(new Rect(row.xMax - 112f, row.y + 9f, 106f, 26f), Ins.T("Pension.FixSchedule"))) { int n = PensionRule.FixSchedule(p, InsSettings.PensionHours); Messages.Message(Ins.T("Pension.Fixed", p.LabelShortCap, n), MessageTypeDefOf.PositiveEvent, false); }
                }
                yy += rowH + 4f;
            }
            if (ins) { GUI.color = InsuranceUI.Grey; Text.Font = GameFont.Tiny; Widgets.Label(new Rect(0, yy + 4f, inner.width, 60f), Ins.T("Status.Foot")); Text.Font = GameFont.Small; GUI.color = Color.white; }
            Widgets.EndScrollView();
        }
    }

    /// <summary>Compact insurance column (one little icon per kind of cover) for the colonist tables. Zero width until the first policy exists.</summary>
    public class PawnColumnWorker_Insurance : PawnColumnWorker
    {
        const float Icon = 18f, Gap = 2f;
        static float FullWidth => InsuranceStatus.Defs.Count * (Icon + Gap) + 6f;

        bool Visible => InsuranceStatus.UiActive && HSettings.GetBool(InsSettings.ShowStatusColumn);

        public override int GetMinWidth(PawnTable table) => Visible ? Mathf.CeilToInt(FullWidth) : 0;
        public override int GetOptimalWidth(PawnTable table) => Visible ? Mathf.CeilToInt(FullWidth) : 0;
        public override int GetMaxWidth(PawnTable table) => Visible ? Mathf.CeilToInt(FullWidth) : 0;
        public override int GetMinHeaderHeight(PawnTable table) => Visible ? base.GetMinHeaderHeight(table) : 0;

        public override void DoHeader(Rect rect, PawnTable table)
        {
            if (!Visible) return;
            var r = new Rect(rect.x, rect.y, rect.width, rect.height);
            Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.LowerCenter;
            Widgets.Label(r, Ins.T("Status.Column"));
            Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
            TooltipHandler.TipRegion(r, Ins.T("Status.ColumnTip"));
        }

        public override void DoCell(Rect rect, Pawn pawn, PawnTable table)
        {
            if (!Visible || !InsuranceStatus.Shown(pawn)) return;
            Guard.Run(ModuleId.Bank, "Insurance.Column", () =>
            {
                var items = InsuranceStatus.Of(pawn);
                bool ins = InsuranceStatus.Insurable(pawn);
                float x = rect.x + 3f, y = rect.y + (rect.height - Icon) / 2f;
                foreach (var it in items)
                {
                    var r = new Rect(x, y, Icon, Icon);
                    var tex = InsuranceStatus.Icon(it);
                    var col = InsuranceStatus.ColorOf(ins ? it.state : CoverState.None);
                    if (tex != null) { GUI.color = col; GUI.DrawTexture(r, tex); GUI.color = Color.white; }
                    else { Widgets.DrawBoxSolid(r.ContractedBy(3f), col); }
                    TooltipHandler.TipRegion(r, InsuranceStatus.Tooltip(pawn, it));
                    x += Icon + Gap;
                }
                var all = new Rect(rect.x, rect.y, rect.width, rect.height);
                if (Widgets.ButtonInvisible(all)) InsuranceUI.OpenOverview(pawn);
            });
        }

        public override int Compare(Pawn a, Pawn b) => Score(a).CompareTo(Score(b));
        static int Score(Pawn p) => InsuranceStatus.Shown(p) ? InsuranceStatus.Of(p).Count(i => i.Has) : -1;
    }
}
