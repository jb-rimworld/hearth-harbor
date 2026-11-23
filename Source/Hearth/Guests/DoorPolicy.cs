// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Hearth.Shell;

namespace Hearth.Guests
{
    public enum FilterMode { Everyone = 0, OnlyThese = 1, ExceptThese = 2 }

    /// <summary>
    /// Door policy / guest filters (5.9, N28): who may come at all. Categories are ANDed, entries in one category ORed.
    /// Applied already when the guests are generated (N28): faction, tech level and stay kind when the group is planned,
    /// gender and age as fixed values of the PawnGenerationRequest, xenotype and royal title by validator (8 retries, then the slot stays empty),
    /// wealth band when the guest's wealth is rolled. Hostile factions never visit, whatever the policy says.
    /// Lives in the guest register (saved with the colony).
    /// </summary>
    public sealed class DoorPolicy : IExposable
    {
        public const int Retries = 8;
        /// <summary>"Everyone": switches all filters off at once (the settings below are kept for later).</summary>
        public bool AllowAll;
        public FilterMode FactionMode; public List<Faction> Factions = new List<Faction>();
        public FilterMode GenderMode; public Gender Gender = Gender.Female;
        public int MinAge = 0, MaxAge = 999;
        public FilterMode XenoMode; public List<XenotypeDef> Xenotypes = new List<XenotypeDef>();
        public FilterMode TitleMode; // Only these = titled only, Except these = no titled guests
        public TechLevel MinTech = TechLevel.Undefined;
        public int MinWealth = 1, MaxWealth = 100;
        public bool AllowOvernight = true, AllowDay = true;

        public enum Category { Factions, Xenotypes, Gender, Age, Title, Tech, Wealth, StayKind }

        // ------------------------------------------------------------------ state
        public IEnumerable<Category> Restricting()
        {
            if (AllowAll) yield break;
            if (FactionMode != FilterMode.Everyone && Factions.Count > 0) yield return Category.Factions;
            if (XenoMode != FilterMode.Everyone && Xenotypes.Count > 0 && ModsConfig.BiotechActive) yield return Category.Xenotypes;
            if (GenderMode != FilterMode.Everyone) yield return Category.Gender;
            if (MinAge > 0 || MaxAge < 999) yield return Category.Age;
            if (TitleMode != FilterMode.Everyone && ModsConfig.RoyaltyActive) yield return Category.Title;
            if (MinTech > TechLevel.Undefined) yield return Category.Tech;
            if (MinWealth > 1 || MaxWealth < 100) yield return Category.Wealth;
            if (!AllowOvernight || !AllowDay) yield return Category.StayKind;
        }

        public bool IsDefault => !AllowAll && !Restricting().Any();

        public void Reset()
        {
            AllowAll = false; FactionMode = GenderMode = XenoMode = TitleMode = FilterMode.Everyone;
            Factions.Clear(); Xenotypes.Clear(); Gender = Gender.Female; MinAge = 0; MaxAge = 999;
            MinTech = TechLevel.Undefined; MinWealth = 1; MaxWealth = 100; AllowOvernight = AllowDay = true;
        }

        public string Signature()
            => $"{AllowAll}|{FactionMode}:{string.Join(",", Factions.Where(f => f != null).Select(f => f.loadID))}|{XenoMode}:{string.Join(",", Xenotypes.Where(x => x != null).Select(x => x.defName))}|{GenderMode}:{Gender}|{MinAge}-{MaxAge}|{TitleMode}|{MinTech}|{MinWealth}-{MaxWealth}|{AllowOvernight}{AllowDay}";

        // ------------------------------------------------------------------ checks
        static bool Match(FilterMode m, bool inList) => m == FilterMode.Everyone || (m == FilterMode.OnlyThese ? inList : !inList);

        public bool FactionOk(Faction f)
        {
            if (f == null) return false;
            if (AllowAll) return true;
            if (FactionMode != FilterMode.Everyone && Factions.Count > 0 && !Match(FactionMode, Factions.Contains(f))) return false;
            if (MinTech > TechLevel.Undefined && f.def.techLevel < MinTech) return false;
            return true;
        }

        public bool KindOk(StayKind k)
        {
            if (AllowAll) return true;
            if (k == StayKind.Overnight || k == StayKind.Lodger) return AllowOvernight;
            if (k == StayKind.DayVisitor) return AllowDay;
            return true;
        }

        public bool PawnOk(Pawn p)
        {
            if (p == null) return false;
            if (AllowAll) return true;
            if (GenderMode != FilterMode.Everyone && !Match(GenderMode, p.gender == Gender)) return false;
            if (p.ageTracker != null)
            {
                int age = p.ageTracker.AgeBiologicalYears;
                if (age < MinAge || age > MaxAge) return false;
            }
            if (XenoMode != FilterMode.Everyone && Xenotypes.Count > 0 && ModsConfig.BiotechActive)
                if (!Match(XenoMode, p.genes?.Xenotype != null && Xenotypes.Contains(p.genes.Xenotype))) return false;
            if (TitleMode != FilterMode.Everyone && ModsConfig.RoyaltyActive)
                if (!Match(TitleMode, p.royalty != null && p.royalty.AllTitlesForReading.Count > 0)) return false;
            return true;
        }

        public bool WealthOk(int w) => AllowAll || (w >= MinWealth && w <= MaxWealth);
        public int ClampWealth(int w) => AllowAll ? w : Mathf.Clamp(w, Math.Min(MinWealth, MaxWealth), Math.Max(MinWealth, MaxWealth));

        /// <summary>Puts gender and age into the generation request so most pawns already fit (N28); the rest goes through <see cref="PawnOk"/>.</summary>
        public void ApplyTo(ref PawnGenerationRequest req)
        {
            if (AllowAll) return;
            if (GenderMode == FilterMode.OnlyThese) req.FixedGender = Gender;
            else if (GenderMode == FilterMode.ExceptThese) req.FixedGender = Gender == Gender.Male ? Gender.Female : Gender.Male;
            if (MinAge > 0 || MaxAge < 999)
            {
                float lo = Math.Max(18, MinAge), hi = Math.Max(lo, Math.Min(MaxAge, 120));
                req.BiologicalAgeRange = new FloatRange(lo, hi + 0.99f);
            }
        }

        /// <summary>Share of pawns of the visiting factions that the current filters let in (sample of generated pawns; for the dialog preview).</summary>
        public float WelcomeShare(Map map, int samples = 24)
        {
            var facs = Find.FactionManager.AllFactionsListForReading.Where(f => MapComponent_HH_GuestScheduler.FactionCanVisit(f, null)).ToList();
            if (facs.Count == 0) return 0f;
            int ok = 0, n = 0;
            for (int i = 0; i < samples; i++)
            {
                var f = facs[i % facs.Count];
                n++;
                if (!FactionOk(f)) continue;
                try
                {
                    var p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(f.RandomPawnKind(), f, PawnGenerationContext.NonPlayer, tile: map?.Tile ?? -1, developmentalStages: DevelopmentalStage.Adult));
                    if (PawnOk(p)) ok++;
                    Find.WorldPawns.PassToWorld(p, RimWorld.Planet.PawnDiscardDecideMode.Discard);
                }
                catch { }
            }
            return n == 0 ? 0f : ok / (float)n;
        }

        // ------------------------------------------------------------------ the one-liners (N29)
        /// <summary>The line shown after the filter window closes. Aimed at the player, never at the filtered group.</summary>
        public string Quip(Category? changed = null)
        {
            if (AllowAll) return "HH.Inn.Door.Quip.AllowAll".TranslateOr("Ugh. A hippie.");
            var r = Restricting().ToList();
            if (r.Count == 0) return "HH.Inn.Door.Quip.Default".TranslateOr("Woke, but with limits, huh?");
            var c = changed.HasValue && r.Contains(changed.Value) ? changed.Value : r[r.Count - 1];
            return QuipFor(c);
        }

        public static string QuipFor(Category c)
        {
            switch (c)
            {
                case Category.Factions: return "HH.Inn.Door.Quip.Factions".TranslateOr("Holding a grudge? Classy.");
                case Category.Xenotypes: return "HH.Inn.Door.Quip.Xenotypes".TranslateOr("Gene snob detected. It's you.");
                case Category.Gender: return "HH.Inn.Door.Quip.Gender".TranslateOr("Half the world uninvited. Efficient, you sexist.");
                case Category.Age: return "HH.Inn.Door.Quip.Age".TranslateOr("Ageist. Grandma will remember this.");
                case Category.Title: return "HH.Inn.Door.Quip.Title".TranslateOr("Class warfare, but make it hospitality.");
                case Category.Tech: return "HH.Inn.Door.Quip.Tech".TranslateOr("Too primitive for your fine dining? Noted.");
                case Category.Wealth: return "HH.Inn.Door.Quip.Wealth".TranslateOr("Only the rich. Your landlord would be proud.");
                case Category.StayKind: return "HH.Inn.Door.Quip.StayKind".TranslateOr("Commitment issues, much?");
            }
            return "HH.Inn.Door.Quip.Saved".TranslateOr("Guest filter saved.");
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref AllowAll, "allowAll");
            Scribe_Values.Look(ref FactionMode, "factionMode");
            Scribe_Collections.Look(ref Factions, "factions", LookMode.Reference);
            Scribe_Values.Look(ref GenderMode, "genderMode");
            Scribe_Values.Look(ref Gender, "gender", Gender.Female);
            Scribe_Values.Look(ref MinAge, "minAge", 0);
            Scribe_Values.Look(ref MaxAge, "maxAge", 999);
            Scribe_Values.Look(ref XenoMode, "xenoMode");
            Scribe_Collections.Look(ref Xenotypes, "xenotypes", LookMode.Def);
            Scribe_Values.Look(ref TitleMode, "titleMode");
            Scribe_Values.Look(ref MinTech, "minTech", TechLevel.Undefined);
            Scribe_Values.Look(ref MinWealth, "minWealth", 1);
            Scribe_Values.Look(ref MaxWealth, "maxWealth", 100);
            Scribe_Values.Look(ref AllowOvernight, "allowOvernight", true);
            Scribe_Values.Look(ref AllowDay, "allowDay", true);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                Factions = Factions ?? new List<Faction>(); Factions.RemoveAll(f => f == null);
                Xenotypes = Xenotypes ?? new List<XenotypeDef>(); Xenotypes.RemoveAll(x => x == null);
            }
        }
    }

    /// <summary>
    /// The door policy window (front desk gizmo, guest overview). Shows a one-liner about the player when it closes (N29).
    /// F35 (1.1.0): bigger and scaled with the screen, collapsible sections (Factions, Xenotypes, Gender, Age, Titles, Tech, Wealth, Stay), a two-column
    /// check list when the window is wide, a sticky summary at the top that says how many people would pass, and a scroll height measured from the content.
    /// </summary>
    public sealed class Dialog_HH_DoorPolicy : Window
    {
        readonly DoorPolicy pol;
        readonly string before;
        DoorPolicy.Category? lastChanged;
        Vector2 scroll;
        float viewH = 900f;
        float preview = -1f;
        float previewDue;                      // realtime when the preview is recomputed (debounced: dragging a slider must not generate pawns every frame)
        readonly Dictionary<DoorPolicy.Category, bool> openState = new Dictionary<DoorPolicy.Category, bool>();

        public override Vector2 InitialSize => HUI.Size(720f, 760f, 560f, 560f);

        public Dialog_HH_DoorPolicy(DoorPolicy pol)
        {
            this.pol = pol; before = pol.Signature();
            doCloseX = true; doCloseButton = true; absorbInputAroundWindow = true;
            closeOnClickedOutside = false;      // a stray click next to the window must not close it in the middle of editing
            draggable = true;
        }

        public override void PostClose()
        {
            base.PostClose();
            if (pol.Signature() == before) return;
            string quip = pol.Quip(lastChanged);
            Messages.Message(quip, MessageTypeDefOf.SilentInput, false);
            DoorPolicyHub.LastQuip = quip;
            HLog.Info("Inn", "Door policy changed: " + pol.Signature());
        }

        static string ModeLabel(FilterMode m) => m == FilterMode.Everyone ? "HH.Inn.Door.Everyone".TranslateOr("Everyone")
            : m == FilterMode.OnlyThese ? "HH.Inn.Door.Only".TranslateOr("Only these") : "HH.Inn.Door.Except".TranslateOr("Everyone except these");

        void Changed(DoorPolicy.Category? cat) { lastChanged = cat; previewDue = Time.realtimeSinceStartup + 0.6f; }

        bool IsOpen(DoorPolicy.Category c)
        {
            if (openState.TryGetValue(c, out bool o)) return o;
            return pol.Restricting().Contains(c);   // sections that restrict something start open
        }

        /// <summary>A section header strip: +/- toggle, title and what it currently does. Returns true while the section is open.</summary>
        bool Section(Listing_Standard ls, DoorPolicy.Category c, string title, string summary)
        {
            bool open = IsOpen(c);
            var r = ls.GetRect(32f);
            Widgets.DrawBoxSolid(r, new Color(1f, 1f, 1f, Mouse.IsOver(r) ? 0.09f : 0.05f));
            bool active = pol.Restricting().Contains(c);
            if (active) Widgets.DrawBoxSolid(new Rect(r.x, r.y, 4f, r.height), UiStyle.Amber);
            UiStyle.LabelColored(new Rect(r.x + 10f, r.y, 22f, r.height), open ? "\u2212" : "+", UiStyle.Amber, TextAnchor.MiddleCenter, GameFont.Medium);
            // title and summary share the strip: the title keeps what it needs (at most 55 %), the summary gets the rest, both are truncated (no overlap)
            Text.Font = GameFont.Small;
            float titleW = Mathf.Min(Text.CalcSize(title).x + 6f, (r.width - 46f) * 0.55f);
            UiStyle.LabelColored(new Rect(r.x + 36f, r.y, titleW, r.height), title.Truncate(titleW), Color.white, TextAnchor.MiddleLeft, GameFont.Small);
            float sx = r.x + 36f + titleW + 8f, sw = r.xMax - 10f - sx;
            if (sw > 20f && !summary.NullOrEmpty())
            {
                Text.Font = GameFont.Tiny;
                string sum = summary.Truncate(sw);
                Text.Font = GameFont.Small;
                UiStyle.LabelColored(new Rect(sx, r.y, sw, r.height), sum, active ? UiStyle.Amber : UiStyle.Grey, TextAnchor.MiddleRight, GameFont.Tiny);
                if (!ReferenceEquals(sum, summary) && sum != summary) TooltipHandler.TipRegion(r, summary);
            }
            if (Widgets.ButtonInvisible(r)) openState[c] = !open;
            ls.Gap(2f);
            return open;
        }

        void ModeRow(Listing_Standard ls, string label, FilterMode cur, Action<FilterMode> set, DoorPolicy.Category cat)
        {
            var r = ls.GetRect(30f);
            Widgets.Label(new Rect(r.x + 8f, r.y + 3f, r.width * 0.4f, 26f), label);
            if (Widgets.ButtonText(new Rect(r.x + r.width * 0.42f, r.y, r.width * 0.58f, 30f), ModeLabel(cur)))
                Find.WindowStack.Add(new FloatMenu(((FilterMode[])Enum.GetValues(typeof(FilterMode))).Select(m => new FloatMenuOption(ModeLabel(m), () => { set(m); Changed(cat); })).ToList()));
        }

        /// <summary>A grid of check boxes: two columns when the window is wide enough, one otherwise.</summary>
        void CheckGrid<T>(Listing_Standard ls, IList<T> items, Func<T, string> label, Func<T, bool> isOn, Action<T, bool> set, DoorPolicy.Category cat, float width)
        {
            int cols = width >= 560f ? 2 : 1;
            for (int i = 0; i < items.Count; i += cols)
            {
                var row = ls.GetRect(26f);
                for (int c = 0; c < cols && i + c < items.Count; c++)
                {
                    var item = items[i + c];
                    var cell = new Rect(row.x + 14f + c * row.width / cols, row.y, row.width / cols - 18f, 26f);
                    bool on = isOn(item), was = on;
                    Widgets.CheckboxLabeled(cell, label(item).Truncate(cell.width - 32f), ref on);
                    if (on != was) { set(item, on); Changed(cat); }
                }
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            float w = inRect.width, y = 0f;
            // ---- fixed top: title, intro, "let everyone in" / reset and the live summary
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, y, w, 36f), "HH.Inn.Door.Title".TranslateOr("Door policy"));
            Text.Font = GameFont.Small; y += 38f;
            string intro = "HH.Inn.Door.Intro".TranslateOr("Who may come. Filters apply when guests are planned and generated; hostile factions never visit.");
            float ih = UiStyle.CachedHeight(intro, w, GameFont.Small);
            UiStyle.LabelColored(new Rect(0, y, w, ih), intro, UiStyle.Grey); y += ih + 6f;
            bool all = pol.AllowAll;
            var rowAll = new Rect(0, y, w, 28f);
            Widgets.CheckboxLabeled(new Rect(rowAll.x, rowAll.y, w - 230f, 28f), "HH.Inn.Door.AllowAll".TranslateOr("Let everyone in (ignore all filters below)"), ref all);
            if (all != pol.AllowAll) { pol.AllowAll = all; Changed(null); }
            if (Widgets.ButtonText(new Rect(w - 220f, y, 220f, 28f), "HH.Inn.Door.Reset".TranslateOr("Almost everyone (default)"))) { pol.Reset(); openState.Clear(); Changed(null); }
            y += 34f;
            // preview: debounced recomputation, with the old number kept (greyed) until the new one is there
            if (Find.CurrentMap != null && (preview < 0f || (previewDue > 0f && Time.realtimeSinceStartup >= previewDue))) { preview = pol.WelcomeShare(Find.CurrentMap, 16); previewDue = 0f; }
            var box = new Rect(0, y, w, 46f);
            Widgets.DrawBoxSolid(box, UiStyle.AmberSoft);
            if (preview >= 0f)
            {
                bool stale = previewDue > 0f;
                UiStyle.Bar(new Rect(box.x + 10f, box.y + 8f, w - 150f, 12f), preview, UiStyle.Heat(preview));
                UiStyle.LabelColored(new Rect(w - 130f, box.y + 2f, 120f, 24f), preview.ToStringPercent(), stale ? UiStyle.Grey : Color.white, TextAnchor.MiddleRight, GameFont.Medium);
                string pv = "HH.Inn.Door.Preview".TranslateOr("{0} of people from friendly factions would be welcome.").Replace("{0}", preview.ToStringPercent());
                UiStyle.LabelColored(new Rect(box.x + 10f, box.y + 24f, w - 20f, 20f), pv, stale ? UiStyle.DimGrey : UiStyle.Grey, TextAnchor.MiddleLeft, GameFont.Tiny);
            }
            y += 54f;

            // ---- scrolling sections
            var outer = new Rect(0, y, w, inRect.height - y - CloseButSize.y - 10f);
            var view = new Rect(0, 0, outer.width - 18f, viewH);
            Widgets.BeginScrollView(outer, ref scroll, view);
            var ls = new Listing_Standard(); ls.Begin(view);
            if (pol.AllowAll) GUI.color = new Color(1f, 1f, 1f, 0.45f);
            float gw = view.width;
            // factions
            var facs = Find.FactionManager.AllFactionsListForReading.Where(f => MapComponent_HH_GuestScheduler.FactionCanVisit(f, null) || pol.Factions.Contains(f)).ToList();
            if (Section(ls, DoorPolicy.Category.Factions, "HH.Inn.Door.Factions".TranslateOr("Factions"), pol.FactionMode == FilterMode.Everyone ? ModeLabel(pol.FactionMode) : ModeLabel(pol.FactionMode) + " (" + pol.Factions.Count + ")"))
            {
                ModeRow(ls, "HH.Inn.Door.Factions".TranslateOr("Factions"), pol.FactionMode, m => pol.FactionMode = m, DoorPolicy.Category.Factions);
                if (pol.FactionMode != FilterMode.Everyone)
                    CheckGrid(ls, facs, f => f.Name, f => pol.Factions.Contains(f), (f, on) => { if (on) pol.Factions.Add(f); else pol.Factions.Remove(f); }, DoorPolicy.Category.Factions, gw);
                ls.Gap(6f);
            }
            if (ModsConfig.BiotechActive)
            {
                var xenos = DefDatabase<XenotypeDef>.AllDefsListForReading.OrderBy(x => x.label).ToList();
                if (Section(ls, DoorPolicy.Category.Xenotypes, "HH.Inn.Door.Xenotypes".TranslateOr("Xenotypes"), pol.XenoMode == FilterMode.Everyone ? ModeLabel(pol.XenoMode) : ModeLabel(pol.XenoMode) + " (" + pol.Xenotypes.Count + ")"))
                {
                    ModeRow(ls, "HH.Inn.Door.Xenotypes".TranslateOr("Xenotypes"), pol.XenoMode, m => pol.XenoMode = m, DoorPolicy.Category.Xenotypes);
                    if (pol.XenoMode != FilterMode.Everyone)
                        CheckGrid(ls, xenos, x => x.LabelCap.ToString(), x => pol.Xenotypes.Contains(x), (x, on) => { if (on) pol.Xenotypes.Add(x); else pol.Xenotypes.Remove(x); }, DoorPolicy.Category.Xenotypes, gw);
                    ls.Gap(6f);
                }
            }
            // gender
            if (Section(ls, DoorPolicy.Category.Gender, "HH.Inn.Door.Gender".TranslateOr("Gender"), pol.GenderMode == FilterMode.Everyone ? ModeLabel(pol.GenderMode) : ModeLabel(pol.GenderMode) + ": " + pol.Gender.GetLabel().CapitalizeFirst()))
            {
                ModeRow(ls, "HH.Inn.Door.Gender".TranslateOr("Gender"), pol.GenderMode, m => pol.GenderMode = m, DoorPolicy.Category.Gender);
                if (pol.GenderMode != FilterMode.Everyone)
                {
                    var gr = ls.GetRect(30f);
                    if (Widgets.ButtonText(new Rect(gr.x + gr.width * 0.42f, gr.y, gr.width * 0.58f, 30f), pol.Gender.GetLabel().CapitalizeFirst()))
                    { pol.Gender = pol.Gender == Gender.Male ? Gender.Female : Gender.Male; Changed(DoorPolicy.Category.Gender); }
                }
                ls.Gap(6f);
            }
            // age
            if (Section(ls, DoorPolicy.Category.Age, "HH.Inn.Door.AgeTitle".TranslateOr("Age"), "HH.Inn.Door.Age".TranslateOr("Age: {0}–{1}").Replace("{0}", pol.MinAge.ToString()).Replace("{1}", pol.MaxAge >= 999 ? "∞" : pol.MaxAge.ToString())))
            {
                ls.Label("HH.Inn.Door.Age".TranslateOr("Age: {0}–{1}").Replace("{0}", pol.MinAge.ToString()).Replace("{1}", pol.MaxAge >= 999 ? "∞" : pol.MaxAge.ToString()));
                int minA = (int)ls.Slider(pol.MinAge, 0, 100), maxA = (int)ls.Slider(Math.Min(pol.MaxAge, 121), 18, 121);
                if (maxA >= 121) maxA = 999;
                if (minA != pol.MinAge || maxA != pol.MaxAge) { pol.MinAge = minA; pol.MaxAge = Math.Max(maxA, minA); Changed(DoorPolicy.Category.Age); }
                ls.Gap(6f);
            }
            // royal titles
            if (ModsConfig.RoyaltyActive && Section(ls, DoorPolicy.Category.Title, "HH.Inn.Door.Title2".TranslateOr("Royal titles (only titled / no titled)"), ModeLabel(pol.TitleMode)))
            {
                ModeRow(ls, "HH.Inn.Door.Title2".TranslateOr("Royal titles (only titled / no titled)"), pol.TitleMode, m => pol.TitleMode = m, DoorPolicy.Category.Title);
                ls.Gap(6f);
            }
            // tech
            if (Section(ls, DoorPolicy.Category.Tech, "HH.Inn.Door.Tech".TranslateOr("Lowest tech level"), pol.MinTech == TechLevel.Undefined ? "HH.Inn.Door.Any".TranslateOr("any") : pol.MinTech.ToStringHuman().CapitalizeFirst()))
            {
                var tr = ls.GetRect(30f);
                Widgets.Label(new Rect(tr.x + 8f, tr.y + 3f, tr.width * 0.4f, 26f), "HH.Inn.Door.Tech".TranslateOr("Lowest tech level"));
                if (Widgets.ButtonText(new Rect(tr.x + tr.width * 0.42f, tr.y, tr.width * 0.58f, 30f), pol.MinTech == TechLevel.Undefined ? "HH.Inn.Door.Any".TranslateOr("any") : pol.MinTech.ToStringHuman().CapitalizeFirst()))
                    Find.WindowStack.Add(new FloatMenu(((TechLevel[])Enum.GetValues(typeof(TechLevel))).Where(t => t <= TechLevel.Archotech)
                        .Select(t => new FloatMenuOption(t == TechLevel.Undefined ? "HH.Inn.Door.Any".TranslateOr("any") : t.ToStringHuman().CapitalizeFirst(), () => { pol.MinTech = t; Changed(DoorPolicy.Category.Tech); })).ToList()));
                ls.Gap(6f);
            }
            // wealth
            if (Section(ls, DoorPolicy.Category.Wealth, "HH.Inn.Door.WealthTitle".TranslateOr("Wealth"), "HH.Inn.Door.Wealth".TranslateOr("Wealth band: {0}–{1}").Replace("{0}", pol.MinWealth.ToString()).Replace("{1}", pol.MaxWealth.ToString())))
            {
                ls.Label("HH.Inn.Door.Wealth".TranslateOr("Wealth band: {0}–{1}").Replace("{0}", pol.MinWealth.ToString()).Replace("{1}", pol.MaxWealth.ToString()));
                int minW = (int)ls.Slider(pol.MinWealth, 1, 100), maxW = (int)ls.Slider(pol.MaxWealth, 1, 100);
                if (minW != pol.MinWealth || maxW != pol.MaxWealth) { pol.MinWealth = minW; pol.MaxWealth = Math.Max(minW, maxW); Changed(DoorPolicy.Category.Wealth); }
                ls.Gap(6f);
            }
            // stay kinds
            string stay = pol.AllowOvernight && pol.AllowDay ? "HH.Inn.Door.Everyone".TranslateOr("Everyone") : !pol.AllowOvernight ? "HH.Inn.Door.Day".TranslateOr("Day visitors") : "HH.Inn.Door.Overnight".TranslateOr("Overnight guests");
            if (Section(ls, DoorPolicy.Category.StayKind, "HH.Inn.Door.StayTitle".TranslateOr("Kind of stay"), stay))
            {
                bool on1 = pol.AllowOvernight, on2 = pol.AllowDay;
                var rr = ls.GetRect(28f);
                Widgets.CheckboxLabeled(new Rect(rr.x + 14f, rr.y, rr.width / 2f - 18f, 28f), "HH.Inn.Door.Overnight".TranslateOr("Overnight guests"), ref on1);
                Widgets.CheckboxLabeled(new Rect(rr.x + rr.width / 2f + 14f, rr.y, rr.width / 2f - 18f, 28f), "HH.Inn.Door.Day".TranslateOr("Day visitors"), ref on2);
                if (on1 != pol.AllowOvernight || on2 != pol.AllowDay) { pol.AllowOvernight = on1; pol.AllowDay = on2; Changed(DoorPolicy.Category.StayKind); }
                ls.Gap(6f);
            }
            GUI.color = Color.white;
            viewH = Mathf.Max(ls.CurHeight + 8f, outer.height);
            ls.End();
            Widgets.EndScrollView();
        }
    }

    /// <summary>Access to the colony's door policy.</summary>
    public static class DoorPolicyHub
    {
        public static DoorPolicy Current => GuestRegistry.Instance?.Door;
        public static string LastQuip;
        public static void Open() { var p = Current; if (p != null) Find.WindowStack.Add(new Dialog_HH_DoorPolicy(p)); }
    }
}
