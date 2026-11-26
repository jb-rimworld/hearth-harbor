// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Hearth
{
    /// <summary>Shared helpers of the Hearth &amp; Harbor scenario parts (editor rows, value formatting).</summary>
    internal static class ScenUI
    {
        public static string T(string key, string en) => key.TranslateOr(en);
        public static string T(string key, string en, params object[] args)
        {
            string s = key.TranslateOr(en);
            try { return string.Format(CultureInfo.InvariantCulture, s, args); } catch (FormatException) { return s; }
        }

        /// <summary>The label a setting value shows in summaries and the editor ("1.4", "On", "25 %").</summary>
        public static string Value(string key, float v)
        {
            var s = HSettings.Find(key);
            return s == null ? v.ToString("0.###", CultureInfo.InvariantCulture) : s.Format(s.Clamp(v));
        }

        public static string SettingLabel(string key)
        {
            var s = HSettings.Find(key);
            return s == null ? key : s.Label;
        }

        /// <summary>A setting a scenario may set: registered, per colony, of a module whose code is present.</summary>
        public static bool Settable(HSetting s) => s != null && !s.Global && Modules.Available(s.Module);

        /// <summary>Value editor for one setting: checkbox, enum menu or slider + number field. Returns the (possibly changed) value.</summary>
        public static float ValueEditor(Rect r, HSetting s, float value, ref string buffer)
        {
            if (s == null) { Widgets.Label(r, "?"); return value; }
            value = s.Clamp(value);
            switch (s.Kind)
            {
                case SettingKind.Bool:
                {
                    bool b = value >= 0.5f, nb = b;
                    Widgets.CheckboxLabeled(r, s.Format(value), ref nb);
                    return nb == b ? value : (nb ? 1f : 0f);
                }
                case SettingKind.Enum:
                {
                    if (Widgets.ButtonText(r, s.Format(value)))
                    {
                        var opts = new List<FloatMenuOption>();
                        for (int i = (int)s.SliderMin; i <= (int)s.SliderMax; i++)
                        {
                            int iv = i;
                            if (s.EnumType != null && Enum.GetName(s.EnumType, i) == null) continue;
                            opts.Add(new FloatMenuOption(s.Format(i), () => { pickedEnum = iv; pickedFor = s.Key; }));
                        }
                        Find.WindowStack.Add(new FloatMenu(opts));
                    }
                    if (pickedFor == s.Key && pickedEnum >= 0) { value = pickedEnum; pickedEnum = -1; pickedFor = null; }
                    return value;
                }
                default:
                {
                    float fieldW = Mathf.Min(90f, r.width * 0.35f);
                    var sr = new Rect(r.x, r.y + r.height / 2f - 8f, r.width - fieldW - 6f, 16f);
                    float input = s.ToSlider(value);
                    float nv = Widgets.HorizontalSlider(sr, input, s.SliderLo, s.SliderHi, false, null, null, null, s.Kind == SettingKind.Int || s.Kind == SettingKind.Money ? 1f : -1f);
                    if (Math.Abs(nv - input) > 1e-5f) { value = s.Clamp(s.FromSlider(nv)); buffer = null; }
                    var fr = new Rect(r.xMax - fieldW, r.y, fieldW, r.height);
                    if (buffer == null) buffer = value.ToString("0.###", CultureInfo.InvariantCulture);
                    string nb = Widgets.TextField(fr, buffer);
                    if (nb != buffer)
                    {
                        buffer = nb;
                        if (float.TryParse(nb.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)) value = s.Clamp(parsed);
                    }
                    TooltipHandler.TipRegion(fr, s.Format(value));
                    return value;
                }
            }
        }
        static int pickedEnum = -1; static string pickedFor;

        public static string ModuleState(int st) => st == 1 ? T("HH.Scen.Module.On", "on") : st == 2 ? T("HH.Scen.Module.Off", "off") : T("HH.Scen.Module.Template", "as template");
    }

    // ================================================================================================ rules
    /// <summary>
    /// "Hearth &amp; Harbor rules" (scenario editor, category Rule): module switches, hosting difficulty, content intensity
    /// and any number of per-colony settings. Only what is explicitly set here overrides the template for new games; the player
    /// can still change everything on the storyteller page. Saved only inside the "hhParts" node (RemovalSafeSave), never in vanilla data.
    /// </summary>
    public class ScenPart_HHRules : ScenPart, IHearthScenarioSeed
    {
        /// <summary>Hosting difficulty tier 0…6, negative = not set (the template decides).</summary>
        public float tier = -1f;
        /// <summary>Content intensity name (Cozy, Standard, Dark, Grim) or null.</summary>
        public string content;
        public List<string> modulesOn = new List<string>();
        public List<string> modulesOff = new List<string>();
        /// <summary>Old element &lt;underworldDiscovered&gt; (until 1.1.x): still parsed so old scenario files load, ignored since 1.2.0 (there is no discovery).</summary>
        public bool underworldDiscovered;
        public List<HHScenSetting> settings = new List<HHScenSetting>();

        public bool HasTier => tier >= 0f;
        public ContentIntensity? Content => !content.NullOrEmpty() && Enum.TryParse(content, true, out ContentIntensity c) ? c : (ContentIntensity?)null;

        /// <summary>0 = template, 1 = on, 2 = off.</summary>
        public int ModuleState(ModuleId m)
        {
            string n = m.ToString();
            if (modulesOn.Any(x => string.Equals(x, n, StringComparison.OrdinalIgnoreCase))) return 1;
            if (modulesOff.Any(x => string.Equals(x, n, StringComparison.OrdinalIgnoreCase))) return 2;
            return 0;
        }
        public void SetModuleState(ModuleId m, int st)
        {
            string n = m.ToString();
            modulesOn.RemoveAll(x => string.Equals(x, n, StringComparison.OrdinalIgnoreCase));
            modulesOff.RemoveAll(x => string.Equals(x, n, StringComparison.OrdinalIgnoreCase));
            if (st == 1) modulesOn.Add(n); else if (st == 2) modulesOff.Add(n);
        }
        public void SetSetting(string key, float v)
        {
            var e = settings.FirstOrDefault(x => x.key == key);
            if (e == null) settings.Add(new HHScenSetting(key, v)); else e.value = v;
        }

        public void Seed(HearthProfile p)
        {
            if (HasTier) p.Tier = Mathf.Clamp(tier, 0f, 6f);
            var c = Content; if (c.HasValue) p.Content = c.Value;
            foreach (var m in Modules.All)
            {
                if (m == ModuleId.Core || !Modules.Available(m)) continue;
                int st = ModuleState(m);
                if (st != 0) p.modules[m] = st == 1;   // direct write: the profile is not live yet, no switch-off side effects
            }
            if (settings != null) foreach (var s in settings) if (s != null && !s.key.NullOrEmpty()) ScenarioSeed.SetSetting(p, s.key, s.value);
        }

        /// <summary>A scratch profile carrying exactly the overrides of this part (for the full settings editor).</summary>
        public HearthProfile ToScratch()
        {
            var p = new HearthProfile { Tier = HasTier ? tier : HearthMod.Template.Profile.Tier, Content = Content ?? HearthMod.Template.Profile.Content };
            foreach (var m in Modules.All) { int st = ModuleState(m); if (st != 0) p.modules[m] = st == 1; }
            foreach (var s in settings) if (s != null && !s.key.NullOrEmpty()) p.Store.Set(s.key, s.value);
            return p;
        }

        /// <summary>Reads the overrides back from a scratch profile the editor changed (only keys it holds; tier/content only when changed).</summary>
        public void FromScratch(HearthProfile p, float tierBefore, ContentIntensity contentBefore)
        {
            if (Math.Abs(p.Tier - tierBefore) > 0.0001f) tier = p.Tier;
            if (p.Content != contentBefore) content = p.Content.ToString();
            foreach (var m in Modules.All) if (m != ModuleId.Core) SetModuleState(m, p.modules.TryGetValue(m, out bool on) ? (on ? 1 : 2) : 0);
            settings.Clear();
            foreach (var s in HSettings.All)
                if (ScenUI.Settable(s) && p.Store.TryGet(s.Key, out float v)) settings.Add(new HHScenSetting(s.Key, v));
        }

        // ---- editor -------------------------------------------------------------------------------------
        public override void DoEditInterface(Listing_ScenEdit listing)
        {
            var mods = Modules.All.Where(m => m != ModuleId.Core && Modules.Available(m)).ToList();
            float rh = RowHeight + 4f;
            int modRows = (mods.Count + 1) / 2;
            float h = rh * (3 + modRows) + 6f;
            var r = listing.GetScenPartRect(this, h);
            ScenarioSeed.GuardedEdit("Scenario.EditRules", () => DrawRules(r, mods, rh));
        }

        void DrawRules(Rect r, List<ModuleId> mods, float rh)
        {
            int modRows = (mods.Count + 1) / 2;
            float y = r.y;
            // difficulty
            var row = new Rect(r.x, y, r.width, rh - 2f);
            bool set = HasTier, nset = set;
            Widgets.Checkbox(new Vector2(row.x, row.y + 1f), ref nset, 22f);
            if (nset != set) tier = nset ? HearthMod.Template.Profile.Tier : -1f;
            var rest = new Rect(row.x + 28f, row.y, row.width - 28f, row.height);
            if (HasTier)
            {
                float pct = HearthProfile.PercentFromTier(tier);
                var sl = new Rect(rest.x, rest.y + 3f, rest.width * 0.5f, 16f);
                float np = Widgets.HorizontalSlider(sl, pct, 0f, 500f, false, null, null, null, 5f);
                if (Math.Abs(np - pct) > 0.01f) tier = HearthProfile.TierFromPercent(np);
                Widgets.Label(new Rect(sl.xMax + 6f, rest.y, rest.width - sl.width - 6f, rest.height), HearthProfile.PercentFromTier(tier).ToString("0") + " % · " + HearthProfile.TierName(tier));
            }
            else Widgets.Label(rest, ScenUI.T("HH.Scen.Rules.TierTemplate", "Hosting difficulty: as template"));
            TooltipHandler.TipRegion(row, ScenUI.T("HH.Scen.Rules.TierTip", "Tick to let this scenario set the hosting difficulty (0–500 %). Unticked, the player's template decides."));
            y += rh;
            // content
            row = new Rect(r.x, y, r.width, rh - 2f);
            var cur = Content;
            if (Widgets.ButtonText(row, ScenUI.T("HH.Scen.Rules.Content", "Content: {0}", cur.HasValue ? ("HH.UI.Content." + cur.Value).TranslateOr(cur.Value.ToString()) : ScenUI.T("HH.Scen.Module.Template", "as template"))))
            {
                var opts = new List<FloatMenuOption> { new FloatMenuOption(ScenUI.T("HH.Scen.Module.Template", "as template"), () => content = null) };
                foreach (ContentIntensity c in Enum.GetValues(typeof(ContentIntensity)))
                {
                    var cc = c;
                    opts.Add(new FloatMenuOption(("HH.UI.Content." + c).TranslateOr(c.ToString()), () => content = cc.ToString()));
                }
                Find.WindowStack.Add(new FloatMenu(opts));
            }
            y += rh;
            // modules, two per row, each a button cycling template → on → off
            float cw = (r.width - 4f) / 2f;
            for (int i = 0; i < mods.Count; i++)
            {
                var m = mods[i];
                var cr = new Rect(r.x + (i % 2) * (cw + 4f), y + (i / 2) * rh, cw, rh - 2f);
                int st = ModuleState(m);
                Color old = GUI.color;
                if (st == 1) GUI.color = new Color(0.75f, 1f, 0.75f); else if (st == 2) GUI.color = new Color(1f, 0.75f, 0.72f);
                Text.Font = GameFont.Tiny;
                if (Widgets.ButtonText(cr, Modules.Label(m) + ": " + ScenUI.ModuleState(st))) SetModuleState(m, (st + 1) % 3);
                Text.Font = GameFont.Small;
                GUI.color = old;
            }
            y += modRows * rh;
            // all other settings
            row = new Rect(r.x, y, r.width, rh - 2f);
            string more = ScenUI.T("HH.Scen.Rules.More", "More settings… ({0} set)", settings.Count);
            if (ScenarioSeed.OpenProfileEditor != null)
            {
                if (Widgets.ButtonText(row, more))
                {
                    var scratch = ToScratch(); float tb = scratch.Tier; var cb = scratch.Content;
                    ScenarioSeed.OpenProfileEditor(scratch, () => FromScratch(scratch, tb, cb));
                }
                TooltipHandler.TipRegion(row, ScenUI.T("HH.Scen.Rules.MoreTip", "Opens the full Hearth & Harbor settings for this scenario. Only what you change there is set by the scenario."));
            }
            else Widgets.Label(row, ScenUI.T("HH.Scen.Rules.Count", "{0} setting(s) set; add single settings with the 'Hearth & Harbor setting' part.", settings.Count));
        }

        public override string Summary(Scenario scen)
        {
            var sb = new StringBuilder();
            sb.Append(ScenUI.T("HH.Scen.Rules.SummaryHead", "Hearth & Harbor:"));
            foreach (var line in SummaryLines()) sb.Append("\n   -" + line);
            return sb.ToString();
        }

        public IEnumerable<string> SummaryLines()
        {
            if (HasTier) yield return ScenUI.T("HH.Scen.Rules.SumTier", "hosting difficulty {0} % ({1})", HearthProfile.PercentFromTier(tier).ToString("0"), HearthProfile.TierName(tier));
            var c = Content; if (c.HasValue) yield return ScenUI.T("HH.Scen.Rules.SumContent", "content: {0}", ("HH.UI.Content." + c.Value).TranslateOr(c.Value.ToString()));
            var on = Modules.All.Where(m => m != ModuleId.Core && Modules.Available(m) && ModuleState(m) == 1).Select(Modules.Label).ToList();
            var off = Modules.All.Where(m => m != ModuleId.Core && Modules.Available(m) && ModuleState(m) == 2).Select(Modules.Label).ToList();
            if (on.Count > 0) yield return ScenUI.T("HH.Scen.Rules.SumOn", "on: {0}", string.Join(", ", on));
            if (off.Count > 0) yield return ScenUI.T("HH.Scen.Rules.SumOff", "off: {0}", string.Join(", ", off));
            int shown = 0;
            foreach (var s in settings)
            {
                if (s == null || s.key.NullOrEmpty() || !ScenUI.Settable(HSettings.Find(s.key))) continue;
                if (shown == 8) { yield return ScenUI.T("HH.Scen.Rules.SumMore", "and {0} more settings", settings.Count - shown); break; }
                yield return ScenUI.SettingLabel(s.key) + ": " + ScenUI.Value(s.key, s.value);
                shown++;
            }
            if (!HasTier && !c.HasValue && on.Count == 0 && off.Count == 0 && shown == 0) yield return ScenUI.T("HH.Scen.Rules.SumNothing", "everything as in your template");
        }

        protected override ScenPart CopyForEditingInner()
        {
            var c = (ScenPart_HHRules)base.CopyForEditingInner();
            c.modulesOn = new List<string>(modulesOn ?? new List<string>());
            c.modulesOff = new List<string>(modulesOff ?? new List<string>());
            c.settings = (settings ?? new List<HHScenSetting>()).Where(s => s != null).Select(s => s.Copy()).ToList();
            return c;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref tier, "tier", -1f);
            Scribe_Values.Look(ref content, "content");
            Scribe_Collections.Look(ref modulesOn, "modulesOn", LookMode.Value);
            Scribe_Collections.Look(ref modulesOff, "modulesOff", LookMode.Value);
            Scribe_Values.Look(ref underworldDiscovered, "underworldDiscovered");
            Scribe_Collections.Look(ref settings, "settings", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit || Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (modulesOn == null) modulesOn = new List<string>();
                if (modulesOff == null) modulesOff = new List<string>();
                if (settings == null) settings = new List<HHScenSetting>();
            }
        }

        public override int GetHashCode()
        {
            int h = base.GetHashCode() ^ tier.GetHashCode() ^ (content ?? "").GetHashCode();
            foreach (var m in modulesOn) h = h * 31 + m.GetHashCode();
            foreach (var m in modulesOff) h = h * 37 + m.GetHashCode();
            foreach (var s in settings) if (s?.key != null) h = h * 41 + s.key.GetHashCode() ^ s.value.GetHashCode();
            return h;
        }
    }

    // ================================================================================================ one setting
    /// <summary>"Hearth &amp; Harbor setting": one per-colony setting with a value. Quick to add, readable in XML:
    /// &lt;li Class="Hearth.ScenPart_HHSetting"&gt;&lt;def&gt;HH_ScenSetting&lt;/def&gt;&lt;key&gt;inn.arrivalRate&lt;/key&gt;&lt;value&gt;1.4&lt;/value&gt;&lt;/li&gt;</summary>
    public class ScenPart_HHSetting : ScenPart, IHearthScenarioSeed
    {
        public string key;
        public float value;
        [Unsaved] string buffer;

        public HSetting Setting => key.NullOrEmpty() ? null : HSettings.Find(key);
#if !RW15   // 1.5 has no ScenPart.Valid

        public override bool Valid() => base.Valid() && !key.NullOrEmpty();
#endif

        public void Seed(HearthProfile p) { if (!key.NullOrEmpty()) ScenarioSeed.SetSetting(p, key, value); }

        public override void Randomize()
        {
            var s = HSettings.Find("inn.arrivalRate");
            if (!ScenUI.Settable(s)) s = HSettings.All.FirstOrDefault(x => ScenUI.Settable(x) && !x.Nerd);
            if (s != null) { key = s.Key; value = HSettings.Get(s); }
        }

        public override void DoEditInterface(Listing_ScenEdit listing)
        {
            float rh = RowHeight + 4f;
            var r = listing.GetScenPartRect(this, rh * 2f);
            ScenarioSeed.GuardedEdit("Scenario.EditSetting", () => DrawSetting(r, rh));
        }

        void DrawSetting(Rect r, float rh)
        {
            var s = Setting;
            var top = new Rect(r.x, r.y, r.width, rh - 2f);
            if (Widgets.ButtonText(top, s != null ? s.Label : (key.NullOrEmpty() ? ScenUI.T("HH.Scen.Setting.Pick", "Choose a setting…") : key)))
                Find.WindowStack.Add(new Dialog_HHSettingPicker(picked => { key = picked.Key; value = HSettings.Get(picked); buffer = null; }));
            if (s != null) TooltipHandler.TipRegion(top, s.Desc);
            var bot = new Rect(r.x, r.y + rh, r.width, rh - 2f);
            if (s != null) value = ScenUI.ValueEditor(bot, s, value, ref buffer);
            else if (!key.NullOrEmpty()) Widgets.Label(bot, ScenUI.T("HH.Scen.Setting.Unknown", "Unknown setting (module missing?)"));
        }

        public override string Summary(Scenario scen) => ScenSummaryList.SummaryWithList(scen, "HHSetting", ScenUI.T("HH.Scen.Setting.Intro", "Hearth & Harbor settings"));

        public override IEnumerable<string> GetSummaryListEntries(string tag)
        {
            if (tag == "HHSetting" && !key.NullOrEmpty()) yield return ScenUI.SettingLabel(key) + ": " + ScenUI.Value(key, value);
        }

        public override bool CanCoexistWith(ScenPart other) => !(other is ScenPart_HHSetting o) || o == this || o.key != key;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref key, "key");
            Scribe_Values.Look(ref value, "value");
        }

        public override int GetHashCode() => base.GetHashCode() ^ (key ?? "").GetHashCode();
    }

    // ================================================================================================ research
    /// <summary>"Start with Hearth &amp; Harbor research": like the vanilla part, but the project is kept as a name, so a save or scenario file
    /// never asks for a missing def once the mod is gone.</summary>
    public class ScenPart_HHStartingResearch : ScenPart, IHearthScenarioStart
    {
        public string project;

        public ResearchProjectDef Project => project.NullOrEmpty() ? null : DefDatabase<ResearchProjectDef>.GetNamedSilentFail(project);
        static IEnumerable<ResearchProjectDef> Ours => DefDatabase<ResearchProjectDef>.AllDefs.Where(ResearchGate.IsOurs).OrderBy(d => d.label);
#if !RW15   // 1.5 has no ScenPart.Valid

        public override bool Valid() => base.Valid() && !project.NullOrEmpty();
#endif

        public override void Randomize()
        {
            var d = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("HH_Innkeeping") ?? Ours.FirstOrDefault();
            project = d?.defName;
        }

        public override void DoEditInterface(Listing_ScenEdit listing)
        {
            var r = listing.GetScenPartRect(this, RowHeight);
            ScenarioSeed.GuardedEdit("Scenario.EditResearch", () =>
            {
                var p = Project;
                if (Widgets.ButtonText(r, p != null ? (string)p.LabelCap : (project ?? "?")))
                    FloatMenuUtility.MakeMenu(Ours, d => d.LabelCap, d => () => project = d.defName);
            });
        }

        public override string Summary(Scenario scen)
        {
            var p = Project;
            return "ScenPart_StartingResearchFinished".Translate(p != null ? p.LabelCap : (TaggedString)(project ?? "?"));
        }

        public void OnStart()
        {
            var p = Project;
            if (p == null) { HLog.Warn("Core", "Scenario research '" + project + "' not found (module missing?)"); return; }
            if (p.IsFinished) return;
            Find.ResearchManager.FinishProject(p, false, null, false);
            HLog.Info("Core", "Scenario: research " + p.defName + " finished");
        }

        public override bool CanCoexistWith(ScenPart other) => !(other is ScenPart_HHStartingResearch o) || o == this || o.project != project;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref project, "project");
        }

        public override int GetHashCode() => base.GetHashCode() ^ (project ?? "").GetHashCode();
    }

    // ================================================================================================ picker
    /// <summary>Searchable list of every setting a scenario may set, grouped by module (a float menu is far too long for 200+ keys).</summary>
    public class Dialog_HHSettingPicker : Window
    {
        readonly Action<HSetting> onPick;
        string search = "";
        bool nerd;
        Vector2 scroll;
        float viewH = 400f;

        public Dialog_HHSettingPicker(Action<HSetting> onPick)
        {
            this.onPick = onPick;
            doCloseX = true; closeOnClickedOutside = true; absorbInputAroundWindow = true; forcePause = true;
        }

        public override Vector2 InitialSize => HUI.Size(620f, 640f);

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - 40f, 34f), ScenUI.T("HH.Scen.Picker.Title", "Choose a Hearth & Harbor setting"));
            Text.Font = GameFont.Small;
            var sr = new Rect(inRect.x, inRect.y + 40f, inRect.width - 200f, 28f);
            search = Widgets.TextField(sr, search ?? "");
            if (search.NullOrEmpty()) { GUI.color = new Color(1f, 1f, 1f, 0.45f); Widgets.Label(new Rect(sr.x + 6f, sr.y + 4f, sr.width, 24f), ScenUI.T("HH.Scen.Picker.Search", "Search…")); GUI.color = Color.white; }
            Widgets.CheckboxLabeled(new Rect(sr.xMax + 12f, sr.y + 2f, 186f, 24f), ScenUI.T("HH.Scen.Picker.Nerd", "Nerd settings"), ref nerd);
            var outR = new Rect(inRect.x, sr.yMax + 8f, inRect.width, inRect.height - (sr.yMax + 8f - inRect.y));
            var view = new Rect(0f, 0f, outR.width - 16f, viewH);
            Widgets.BeginScrollView(outR, ref scroll, view);
            float y = 0f;
            string q = (search ?? "").ToLowerInvariant();
            foreach (var g in HSettings.All.Where(s => ScenUI.Settable(s) && (nerd || !s.Nerd) && (q.Length == 0 || s.Label.ToLowerInvariant().Contains(q) || s.Key.ToLowerInvariant().Contains(q) || (s.Desc ?? "").ToLowerInvariant().Contains(q))).GroupBy(s => s.Module).OrderBy(g => g.Key))
            {
                GUI.color = new Color(0.851f, 0.502f, 0.247f);
                Widgets.Label(new Rect(0f, y, view.width, 24f), (g.Key == ModuleId.Core ? ScenUI.T("HH.UI.Page.General", "General") : Modules.Label(g.Key)).ToUpperInvariant());
                GUI.color = Color.white;
                y += 26f;
                foreach (var s in g)
                {
                    var row = new Rect(0f, y, view.width, 26f);
                    if (y + 26f >= scroll.y && y <= scroll.y + outR.height)
                    {
                        Widgets.DrawHighlightIfMouseover(row);
                        Widgets.Label(new Rect(row.x + 8f, row.y + 2f, row.width * 0.62f, 24f), s.Label);
                        GUI.color = new Color(0.6f, 0.6f, 0.6f);
                        Text.Font = GameFont.Tiny;
                        Widgets.Label(new Rect(row.x + row.width * 0.64f, row.y + 5f, row.width * 0.36f, 20f), s.Category + " · " + s.Format(HSettings.Get(s)));
                        Text.Font = GameFont.Small; GUI.color = Color.white;
                        if (!s.Desc.NullOrEmpty()) TooltipHandler.TipRegion(row, s.Desc + "\n\n" + s.Key);
                        if (Widgets.ButtonInvisible(row)) { onPick?.Invoke(s); Close(); }
                    }
                    y += 26f;
                }
                y += 8f;
            }
            viewH = Mathf.Max(y, 40f);
            Widgets.EndScrollView();
        }
    }
}
