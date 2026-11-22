// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

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

    /// <summary>The door policy window (front desk gizmo, guest overview). Shows a one-liner about the player when it closes (N29).</summary>
    public sealed class Dialog_HH_DoorPolicy : Window
    {
        readonly DoorPolicy pol;
        readonly string before;
        DoorPolicy.Category? lastChanged;
        Vector2 scroll;
        float preview = -1f;
        public override Vector2 InitialSize => new Vector2(560f, 640f);

        public Dialog_HH_DoorPolicy(DoorPolicy pol)
        {
            this.pol = pol; before = pol.Signature();
            doCloseX = true; doCloseButton = true; closeOnClickedOutside = true; absorbInputAroundWindow = true;
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

        void ModeButton(Listing_Standard ls, string label, FilterMode cur, Action<FilterMode> set, DoorPolicy.Category cat)
        {
            var r = ls.GetRect(28f);
            Widgets.Label(r.LeftPart(0.4f), label);
            if (Widgets.ButtonText(r.RightPart(0.58f), ModeLabel(cur)))
                Find.WindowStack.Add(new FloatMenu(((FilterMode[])Enum.GetValues(typeof(FilterMode))).Select(m => new FloatMenuOption(ModeLabel(m), () => { set(m); lastChanged = cat; preview = -1f; })).ToList()));
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, inRect.width, 34f), "HH.Inn.Door.Title".TranslateOr("Door policy"));
            Text.Font = GameFont.Small;
            var outer = new Rect(0, 40f, inRect.width, inRect.height - 40f - CloseButSize.y - 10f);
            var view = new Rect(0, 0, outer.width - 18f, 760f);
            Widgets.BeginScrollView(outer, ref scroll, view);
            var ls = new Listing_Standard(); ls.Begin(view);
            ls.Label("HH.Inn.Door.Intro".TranslateOr("Who may come. Filters apply when guests are planned and generated; hostile factions never visit."));
            bool all = pol.AllowAll;
            ls.CheckboxLabeled("HH.Inn.Door.AllowAll".TranslateOr("Let everyone in (ignore all filters below)"), ref all);
            if (all != pol.AllowAll) { pol.AllowAll = all; lastChanged = null; preview = -1f; }
            if (Widgets.ButtonText(ls.GetRect(28f).LeftPart(0.4f), "HH.Inn.Door.Reset".TranslateOr("Almost everyone (default)"))) { pol.Reset(); lastChanged = null; preview = -1f; }
            ls.GapLine();

            ModeButton(ls, "HH.Inn.Door.Factions".TranslateOr("Factions"), pol.FactionMode, m => pol.FactionMode = m, DoorPolicy.Category.Factions);
            if (pol.FactionMode != FilterMode.Everyone)
                foreach (var f in Find.FactionManager.AllFactionsListForReading.Where(f => MapComponent_HH_GuestScheduler.FactionCanVisit(f, null) || pol.Factions.Contains(f)))
                {
                    bool on = pol.Factions.Contains(f), was = on;
                    ls.CheckboxLabeled("   " + f.Name, ref on);
                    if (on != was) { if (on) pol.Factions.Add(f); else pol.Factions.Remove(f); lastChanged = DoorPolicy.Category.Factions; preview = -1f; }
                }
            if (ModsConfig.BiotechActive)
            {
                ModeButton(ls, "HH.Inn.Door.Xenotypes".TranslateOr("Xenotypes"), pol.XenoMode, m => pol.XenoMode = m, DoorPolicy.Category.Xenotypes);
                if (pol.XenoMode != FilterMode.Everyone)
                    foreach (var x in DefDatabase<XenotypeDef>.AllDefsListForReading.OrderBy(x => x.label))
                    {
                        bool on = pol.Xenotypes.Contains(x), was = on;
                        ls.CheckboxLabeled("   " + x.LabelCap, ref on);
                        if (on != was) { if (on) pol.Xenotypes.Add(x); else pol.Xenotypes.Remove(x); lastChanged = DoorPolicy.Category.Xenotypes; preview = -1f; }
                    }
            }
            ModeButton(ls, "HH.Inn.Door.Gender".TranslateOr("Gender"), pol.GenderMode, m => pol.GenderMode = m, DoorPolicy.Category.Gender);
            if (pol.GenderMode != FilterMode.Everyone)
            {
                var gr = ls.GetRect(28f);
                if (Widgets.ButtonText(gr.RightPart(0.58f), pol.Gender.GetLabel().CapitalizeFirst()))
                { pol.Gender = pol.Gender == Gender.Male ? Gender.Female : Gender.Male; lastChanged = DoorPolicy.Category.Gender; preview = -1f; }
            }
            ls.Label("HH.Inn.Door.Age".TranslateOr("Age: {0}–{1}").Replace("{0}", pol.MinAge.ToString()).Replace("{1}", pol.MaxAge >= 999 ? "∞" : pol.MaxAge.ToString()));
            int minA = (int)ls.Slider(pol.MinAge, 0, 100), maxA = (int)ls.Slider(Math.Min(pol.MaxAge, 121), 18, 121);
            if (maxA >= 121) maxA = 999;
            if (minA != pol.MinAge || maxA != pol.MaxAge) { pol.MinAge = minA; pol.MaxAge = Math.Max(maxA, minA); lastChanged = DoorPolicy.Category.Age; preview = -1f; }
            if (ModsConfig.RoyaltyActive)
                ModeButton(ls, "HH.Inn.Door.Title2".TranslateOr("Royal titles (only titled / no titled)"), pol.TitleMode, m => pol.TitleMode = m, DoorPolicy.Category.Title);
            var tr = ls.GetRect(28f);
            Widgets.Label(tr.LeftPart(0.4f), "HH.Inn.Door.Tech".TranslateOr("Lowest tech level"));
            if (Widgets.ButtonText(tr.RightPart(0.58f), pol.MinTech == TechLevel.Undefined ? "HH.Inn.Door.Any".TranslateOr("any") : pol.MinTech.ToStringHuman().CapitalizeFirst()))
                Find.WindowStack.Add(new FloatMenu(((TechLevel[])Enum.GetValues(typeof(TechLevel))).Where(t => t <= TechLevel.Archotech)
                    .Select(t => new FloatMenuOption(t == TechLevel.Undefined ? "HH.Inn.Door.Any".TranslateOr("any") : t.ToStringHuman().CapitalizeFirst(), () => { pol.MinTech = t; lastChanged = DoorPolicy.Category.Tech; preview = -1f; })).ToList()));
            ls.Label("HH.Inn.Door.Wealth".TranslateOr("Wealth band: {0}–{1}").Replace("{0}", pol.MinWealth.ToString()).Replace("{1}", pol.MaxWealth.ToString()));
            int minW = (int)ls.Slider(pol.MinWealth, 1, 100), maxW = (int)ls.Slider(pol.MaxWealth, 1, 100);
            if (minW != pol.MinWealth || maxW != pol.MaxWealth) { pol.MinWealth = minW; pol.MaxWealth = Math.Max(minW, maxW); lastChanged = DoorPolicy.Category.Wealth; }
            bool on1 = pol.AllowOvernight, on2 = pol.AllowDay;
            ls.CheckboxLabeled("HH.Inn.Door.Overnight".TranslateOr("Overnight guests"), ref on1);
            ls.CheckboxLabeled("HH.Inn.Door.Day".TranslateOr("Day visitors"), ref on2);
            if (on1 != pol.AllowOvernight || on2 != pol.AllowDay) { pol.AllowOvernight = on1; pol.AllowDay = on2; lastChanged = DoorPolicy.Category.StayKind; }
            ls.GapLine();
            if (preview < 0f && Find.CurrentMap != null) preview = pol.WelcomeShare(Find.CurrentMap, 16);
            if (preview >= 0f) ls.Label("HH.Inn.Door.Preview".TranslateOr("{0} of people from friendly factions would be welcome.").Replace("{0}", preview.ToStringPercent()));
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
