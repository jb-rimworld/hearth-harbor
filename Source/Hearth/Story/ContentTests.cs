// SPDX-License-Identifier: MIT
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Hearth.Casino.PlayerHouse;
using Hearth.Music;
using Hearth.Shell;
using Hearth.Testing;
using RimWorld;
using Silverline;
using UnityEngine;
using Verse;

namespace Hearth.Story
{
    static class ContentTestKit
    {
        public static string TempPath(string name)
        {
            string dir = Path.Combine(GenFilePaths.SaveDataFolderPath, "HHTests");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, name);
        }
        public static ScenarioDef Scen(string defName) => DefDatabase<ScenarioDef>.GetNamedSilentFail(defName);
        public static T Part<T>(Scenario s) where T : ScenPart => ScenarioSeed.PartsOf(s)?.OfType<T>().FirstOrDefault();
    }

    // ============================================================================================ music (F07/F32)
    public sealed class MusicSuite : HearthTestSuite
    {
        public override string Name => "content.music";
        public override int MaxTicks => 2000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var songs = DefDatabase<SongDef>.AllDefsListForReading.Where(d => d.defName.StartsWith("HH_Song_")).ToList();
            t.Check(songs.Count == 3, "three Hearth & Harbor songs in the def database (" + songs.Count + ")");
            t.Check(HearthMusic.Songs.Count == 3 && songs.All(s => HearthMusic.Songs.Contains(s)), "the music refresher knows all three");
            foreach (var s in songs)
            {
                t.Check(s.clip != null, s.defName + ": clip loaded (" + s.clipPath + ")");
                float len = s.clip != null ? s.clip.length : 0f;
                t.Check(len >= 100f && len <= 170f, s.defName + ": about two minutes long (" + len.ToString("0.0") + " s)");
                t.Check(!s.tense, s.defName + ": calm, never plays in danger");
                t.Check(!s.allowedSeasons.NullOrEmpty(), s.defName + ": tied to seasons");
                t.Check(s.volume > 0.5f && s.volume <= 1f, s.defName + ": volume matched to vanilla (" + s.volume + ")");
                if (s.clip != null)
                {
                    long native = 0;
                    try { native = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(s.clip); } catch (Exception) { }
                    long pcm = (long)s.clip.samples * s.clip.channels * 2;   // what a fully decoded 16-bit clip would take
                    t.Note(s.defName + ": " + (s.clip.GetType().GetProperty("loadType")?.GetValue(s.clip, null) ?? "?") + ", " + s.clip.frequency + " Hz, " + s.clip.channels + " ch, in memory " + (native / 1048576f).ToString("0.0") + " MB (decoded 16-bit would be " + (pcm / 1048576f).ToString("0.0") + " MB)");
                }
            }
            // one song for each season of a normal year, none twice
            foreach (var season in new[] { Season.Spring, Season.Summer, Season.Fall, Season.Winter })
                t.Check(songs.Count(s => s.allowedSeasons.Contains(season)) == 1, "exactly one of our songs for " + season);

            var mode = HSettings.Find(HearthMusic.ModeKey); var share = HSettings.Find(HearthMusic.ShareKey);
            t.Check(mode != null && mode.Global && share != null && share.Global, "music settings registered as global preferences");
            t.Check(mode != null && (int)mode.Default == (int)MusicMode.WhenHosting, "default: only in colonies that host");
            float m0 = HSettings.Get(mode), s0 = HSettings.Get(share); bool m0set = HSettings.IsOverridden(mode), s0set = HSettings.IsOverridden(share);
            var appropriate = AccessTools.Method(typeof(MusicManagerPlay), "AppropriateNow");
            try
            {
                HSettings.Set(mode, (int)MusicMode.Off); HearthMusic.Refresh();
                t.Check(songs.All(s => !s.playOnMap), "Off: no song of ours plays");
                if (appropriate != null && Find.MusicManagerPlay != null)
                    t.Check(songs.All(s => !(bool)appropriate.Invoke(Find.MusicManagerPlay, new object[] { s })), "Off: vanilla never finds them appropriate");
                HSettings.Set(mode, (int)MusicMode.Always); HSettings.Set(share, 2f); HearthMusic.Refresh();
                t.Check(songs.All(s => s.playOnMap), "Always: all songs may play");
                t.Check(songs.All(s => Math.Abs(s.commonality - s.GetModExtension<HHSongExtension>().baseCommonality * 2f) < 0.001f), "share 2: weight doubled");
                if (appropriate != null && Find.MusicManagerPlay != null && !Find.MusicManagerPlay.DangerMusicMode)
                {
                    var season = GenLocalDate.Season(Find.AnyPlayerHomeMap ?? t.Map);
                    var cand = songs.Where(s => s.allowedSeasons.Contains(season) && (s.allowedTimeOfDay == TimeOfDay.Any)).ToList();
                    foreach (var s in cand) t.Check((bool)appropriate.Invoke(Find.MusicManagerPlay, new object[] { s }) || RecentlyPlayed(s), s.defName + " is in the vanilla pool now (" + season + ")");
                    var wrong = songs.Where(s => !s.allowedSeasons.Contains(season)).ToList();
                    t.Check(wrong.All(s => !(bool)appropriate.Invoke(Find.MusicManagerPlay, new object[] { s })), "songs of other seasons stay out of the pool (" + season + ")");
                }
                // Music Expanded keeps copies (TrackDef : SongDef, same clipPath, see HearthMusic.SyncCopies): they follow our values
                var copies = new System.Collections.Generic.List<SongDef>();
                foreach (var s in songs) copies.Add(new FakeTrackCopy { defName = "ME_Vanilla_" + s.defName, clipPath = s.clipPath, playOnMap = false, commonality = 1f });
                var foreign = new FakeTrackCopy { defName = "ME_Vanilla_Other", clipPath = "Some/Other/Song", playOnMap = true, commonality = 1f };
                var list = new System.Collections.ArrayList(copies) { foreign };
                t.Check(HearthMusic.SyncCopies(list, true) == 3 && copies.All(c => c.playOnMap && Math.Abs(c.commonality - songs.First(x => x.clipPath == c.clipPath).commonality) < 0.0001f), "copies of our songs (Music Expanded layout) follow 'on' and the weight");
                HearthMusic.SyncCopies(list, false);
                t.Check(copies.All(c => !c.playOnMap) && foreign.playOnMap && Math.Abs(foreign.commonality - 1f) < 0.0001f, "'off' reaches the copies, other songs are never touched");
                HSettings.Set(share, 0f); HearthMusic.Refresh();
                t.Check(songs.All(s => !s.playOnMap), "share 0: off");
                HSettings.Set(mode, (int)MusicMode.WhenHosting); HSettings.Set(share, 1f); HearthMusic.Refresh();
                bool used = HearthUI.IsUsedInColony();
                t.Check(songs.All(s => s.playOnMap == used), "When hosting: follows 'colony uses the mod' (" + used + ")");
            }
            finally
            {
                if (m0set) HSettings.Set(mode, m0); else HSettings.Reset(mode);
                if (s0set) HSettings.Set(share, s0); else HSettings.Reset(share);
                HearthMusic.Refresh();
            }
            yield return 1;
        }

        /// <summary>Stands in for Music Expanded's TrackDef (a SongDef subclass holding a copy of a song).</summary>
        sealed class FakeTrackCopy : SongDef { }

        static bool RecentlyPlayed(SongDef s)
        {
            var q = AccessTools.Field(typeof(MusicManagerPlay), "recentSongs")?.GetValue(Find.MusicManagerPlay) as IEnumerable<SongDef>;
            return q != null && q.Contains(s);
        }
    }

    // ============================================================================================ scenarios (F08)
    public sealed class ScenarioSuite : HearthTestSuite
    {
        public override string Name => "content.scenario";
        public override int MaxTicks => 3000;
        public override IEnumerable<int> Run(TestContext t)
        {
            foreach (var n in new[] { "HH_ScenRules", "HH_ScenSetting", "HH_ScenStartingResearch", "SL_ScenStartingAccount" })
            {
                var d = DefDatabase<ScenPartDef>.GetNamedSilentFail(n);
                t.Check(d != null && d.scenPartClass != null && !d.label.NullOrEmpty() && !d.description.NullOrEmpty() && d.PlayerAddRemovable && !d.canBeRandomlyAdded, "scenario part " + n + " is in the editor (label, description, not random)");
            }
            var town = ContentTestKit.Scen("HH_Scen_HarborTown"); var dark = ContentTestKit.Scen("HH_Scen_BackRoom");
            t.Check(town != null && dark != null, "both shipped scenarios load");
            if (town == null || dark == null) yield break;
            foreach (var sd in new[] { town, dark })
            {
                var errs = sd.scenario.ConfigErrors().ToList();
                t.Check(errs.Count == 0, sd.defName + ": no config errors " + string.Join("; ", errs));
                string info = null;
                try { info = sd.scenario.GetFullInformationText(); } catch (Exception e) { t.Check(false, sd.defName + ": summary threw " + e.Message); }
                t.Check(!info.NullOrEmpty() && info.Contains("Hearth & Harbor"), sd.defName + ": the summary lists the Hearth & Harbor rules");
                t.Check(sd.scenario.showInUI && sd.scenario.enabled, sd.defName + ": selectable");
                t.Check(ContentTestKit.Part<ScenPart_PlayerPawnsArriveMethod>(sd.scenario) != null && ContentTestKit.Part<ScenPart_ConfigPage_ConfigureStartingPawns>(sd.scenario) != null, sd.defName + ": has the vanilla pawn pages");
            }
            // seeding
            var p = new HearthProfile { Tier = 3f };
            t.Check(ScenarioSeed.Apply(p, town.scenario) == 1, "Harbor Town: one seeding part");
            t.Check(Math.Abs(p.Tier - 2f) < 0.001f && p.Content == ContentIntensity.Cozy, "Harbor Town: Bed & Breakfast, cozy (" + p.Tier + ", " + p.Content + ")");
            t.Check(!p.ModuleOn(ModuleId.Underworld) && p.ModuleOn(ModuleId.Inn) && p.ModuleOn(ModuleId.Casino), "Harbor Town: underworld off, inn and casino on");
            t.Check(p.Store.TryGet("inn.arrivalRate", out float ar) && Math.Abs(ar - 1.4f) < 0.001f, "Harbor Town: busier road (arrival rate 1.4)");
            t.Check(p.Store.TryGet("venues.marketEnabled", out float me) && me >= 0.5f, "Harbor Town: market on");
            var q = new HearthProfile { Tier = 3f };
            ScenarioSeed.Apply(q, dark.scenario);
            t.Check(Math.Abs(q.Tier - 4f) < 0.001f && q.Content == ContentIntensity.Dark, "The Back Room: Overbooked, dark");
            t.Check(q.ModuleOn(ModuleId.Underworld) && q.UnderworldDiscovered, "The Back Room: underworld on and discovered");
            t.Check(q.Store.TryGet("core.peacefulUnderworld", out float pu) && pu < 0.5f, "The Back Room: violence allowed");
            // only explicit values: an untouched setting stays absent, global preferences and unknown keys are refused
            var rules = new ScenPart_HHRules { def = DefDatabase<ScenPartDef>.GetNamed("HH_ScenRules") };
            rules.SetSetting(HearthMusic.ModeKey, 0f); rules.SetSetting("no.suchKey", 3f); rules.SetSetting("inn.arrivalRate", 0.7f);
            var r = new HearthProfile { Tier = 3f }; rules.Seed(r);
            t.Check(!r.Store.Has(HearthMusic.ModeKey) && !r.Store.Has("no.suchKey") && r.Store.Has("inn.arrivalRate"), "a scenario never sets global preferences or unknown keys");
            t.Check(Math.Abs(r.Tier - 3f) < 0.001f && r.modules.Count == 0, "unset tier and modules stay with the template");
            var single = new ScenPart_HHSetting { def = DefDatabase<ScenPartDef>.GetNamed("HH_ScenSetting"), key = "inn.arrivalRate", value = 99f };
            var r2 = new HearthProfile(); single.Seed(r2);
            var ars = HSettings.Find("inn.arrivalRate");
            t.Check(r2.Store.TryGet("inn.arrivalRate", out float v2) && Math.Abs(v2 - ars.Clamp(99f)) < 0.001f, "single setting part seeds a clamped value");
            // scratch profile round trip (the "More settings…" editor)
            var scratch = rules.ToScratch(); float tb = scratch.Tier; var cb = scratch.Content;
            scratch.Tier = 5f; scratch.modules[ModuleId.Casino] = false; scratch.Store.Set("inn.arrivalRate", 0.5f);
            rules.FromScratch(scratch, tb, cb);
            t.Check(Math.Abs(rules.tier - 5f) < 0.001f && rules.ModuleState(ModuleId.Casino) == 2 && rules.settings.Any(s => s.key == "inn.arrivalRate" && Math.Abs(s.value - 0.5f) < 0.001f), "editor changes come back into the rules part");
            t.Check(rules.content == null, "content stays 'as template' when the editor did not touch it");
            // copy for editing is deep
            var copy = town.scenario.CopyForEditing();
            var cr = ContentTestKit.Part<ScenPart_HHRules>(copy); var orig = ContentTestKit.Part<ScenPart_HHRules>(town.scenario);
            cr.settings.Clear(); cr.modulesOn.Clear();
            t.Check(orig.settings.Count > 0 && orig.modulesOn.Count > 0, "editing a copy never changes the shipped scenario");
            // the storyteller page starts from the scenario's values
            var game = Current.Game; var before = game.Scenario;
            try
            {
                game.Scenario = town.scenario;
                UiState.ClearPendingDifficulty();
                var pend = UiState.EnsurePendingProfile();
                t.Check(Math.Abs(pend.Tier - 2f) < 0.001f && !pend.ModuleOn(ModuleId.Underworld), "the storyteller page's profile starts from the scenario");
                t.Check(Math.Abs(UiState.PreviewProfile().Tier - 2f) < 0.001f, "the storyteller line shows the scenario's difficulty");
                t.Check(ScenarioSeed.HasSeeds(town.scenario) && !ScenarioSeed.HasSeeds(before), "'set by the scenario' only for scenarios with our parts");
            }
            finally { game.Scenario = before; UiState.ClearPendingDifficulty(); }
            // .rsc round trip: our parts live in "hhParts", the vanilla list never sees them, and the shipped list is whole again after saving
            string path = ContentTestKit.TempPath("hh_scen_test.rsc");
            var toSave = dark.scenario.CopyForEditing(); toSave.name = "HH test"; toSave.summary = "test"; toSave.description = "test";
            int partsBefore = ScenarioSeed.PartsOf(toSave).Count;
            GameDataSaveLoader.SaveScenario(toSave, path);
            t.Check(ScenarioSeed.PartsOf(toSave).Count == partsBefore && ScenarioSeed.PartsOf(toSave).OfType<ScenPart_HHRules>().Count() == 1, "saving leaves the scenario's own part list whole");
            string xml = File.Exists(path) ? File.ReadAllText(path) : "";
            int pi = xml.IndexOf("<parts>", StringComparison.Ordinal), pe = xml.IndexOf("</parts>", StringComparison.Ordinal), hi = xml.IndexOf("<hhParts>", StringComparison.Ordinal);
            t.Check(pi >= 0 && pe > pi && hi > 0, "file has a vanilla part list and an hhParts node");
            t.Check(pi >= 0 && pe > pi && !xml.Substring(pi, pe - pi).Contains("Hearth.") && !xml.Substring(pi, pe - pi).Contains("Silverline."), "no class of ours inside the vanilla part list");
            t.Check(hi > 0 && xml.Substring(hi).Contains("Hearth.ScenPart_HHRules") && xml.Substring(hi).Contains("Silverline.ScenPart_SLStartingAccount"), "our parts are in hhParts");
            Scenario loaded = null;
            bool ok = GameDataSaveLoader.TryLoadScenario(path, ScenarioCategory.CustomLocal, out loaded);
            var lr = loaded == null ? null : ContentTestKit.Part<ScenPart_HHRules>(loaded);
            t.Check(ok && lr != null && Math.Abs(lr.tier - 4f) < 0.001f && lr.underworldDiscovered && lr.Content == ContentIntensity.Dark, "loading the file brings the rules part back with its values");
            var la = loaded == null ? null : ContentTestKit.Part<ScenPart_SLStartingAccount>(loaded);
            t.Check(la != null && la.bank == "SL_BlackLedger" && la.loan == 1500, "starting account part survives the file");
            t.Check(loaded != null && ScenarioSeed.PartsOf(loaded).Count == partsBefore, "same number of parts after loading (" + (loaded == null ? -1 : ScenarioSeed.PartsOf(loaded).Count) + "/" + partsBefore + ")");
            try { File.Delete(path); } catch (Exception) { }
            yield return 1;
        }
    }

    public sealed class ScenarioStartSuite : HearthTestSuite
    {
        public override string Name => "content.scenstart";
        public override ModuleId Module => ModuleId.Bank;
        public override int MaxTicks => 2000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var comp = BankGameComp.Current;
            t.Check(comp != null, "bank component");
            if (comp == null) yield break;
            var acc = new ScenPart_SLStartingAccount { def = DefDatabase<ScenPartDef>.GetNamed("SL_ScenStartingAccount"), bank = "SL_Mutual", balance = 250, loan = 300, loanDays = 20 };
            bool had = comp.HasAccount; var bal0 = comp.Balance; var debt0 = comp.Debt;
            acc.OnStart();
            t.Check(comp.HasAccount, "the colony has an account after the scenario start");
            if (!had) t.Check(comp.Bank?.defName == "SL_Mutual", "at the bank the scenario names");
            var gained = comp.Balance - bal0;
            t.Check(Math.Abs(gained.Silver - 550.0) < 0.01, "opening balance plus the loan paid out (" + gained.Silver.ToString("0.##") + ")");
            t.Check(comp.Debt.Silver - debt0.Silver >= 299.99, "the starting loan is owed (" + (comp.Debt - debt0).Silver.ToString("0.##") + ")");
            t.Check(!acc.Summary(Find.Scenario).NullOrEmpty(), "summary text");
            // starting research
            var proj = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("HH_Innkeeping");
            if (proj != null)
            {
                var res = new ScenPart_HHStartingResearch { def = DefDatabase<ScenPartDef>.GetNamed("HH_ScenStartingResearch"), project = proj.defName };
                res.OnStart();
                t.Check(proj.IsFinished, "starting research finished: " + proj.defName);
                var missing = new ScenPart_HHStartingResearch { def = res.def, project = "HH_NoSuchProject" };
                missing.OnStart();
                t.Check(true, "a missing project only warns");
            }
            // start hooks run exactly once through the seeding contract
            yield return 1;
        }
    }

    // ============================================================================================ storyteller (F09)
    public sealed class StorytellerSuite : HearthTestSuite
    {
        public override string Name => "content.storyteller";
        public override int MaxTicks => 3000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var hetty = DefDatabase<StorytellerDef>.GetNamedSilentFail("HH_Hetty");
            t.Check(hetty != null && hetty.label == "Hetty Hearthwell" && !hetty.description.NullOrEmpty(), "Hetty Hearthwell loads with label and description");
            if (hetty == null) yield break;
            t.Check(hetty.portraitLargeTex != null && hetty.portraitLargeTex != BaseContent.BadTex && hetty.portraitTinyTex != null && hetty.portraitTinyTex != BaseContent.BadTex, "both portraits load");
            t.Check(hetty.listVisible, "listed on the storyteller page");
            var hints = StorytellerHints.Of(hetty);
            t.Check(hints != null && Math.Abs(hints.guestArrivalFactor - 1.25f) < 0.001f && Math.Abs(hints.suggestedTier - 2f) < 0.001f, "hints: guests ×1.25, suggests Bed & Breakfast");
            var phoebe = DefDatabase<StorytellerDef>.GetNamedSilentFail("Phoebe");
            t.Check(phoebe != null && hetty.pointsFactorFromAdaptDays.Evaluate(0f) < phoebe.pointsFactorFromAdaptDays.Evaluate(0f) && hetty.pointsFactorFromAdaptDays.Evaluate(180f) < 1f, "raids start smaller than Phoebe's and stay below full strength");
            t.Check(hetty.pointsFactorFromAdaptDays.PointsCount == 4 && hetty.pointsFactorFromDaysPassed.PointsCount == 3, "curves replaced, not appended to the base storyteller's");
            Storyteller st = null;
            try { st = new Storyteller(hetty, DifficultyDefOf.Rough); } catch (Exception e) { t.Check(false, "storyteller constructs: " + e.Message); }
            if (st == null) yield break;
            var gentle = st.storytellerComps.OfType<StorytellerComp_HH_GentleCycle>().ToList();
            var kind = st.storytellerComps.OfType<StorytellerComp_HH_KindMisc>().FirstOrDefault();
            t.Check(gentle.Count == 2 && kind != null, "gentle threat comps and the kind event comp instantiate (" + gentle.Count + ")");
            t.Check(st.storytellerComps.Any(c => c.GetType().Name == "StorytellerComp_SingleOnceFixed"), "inherits the shared comps of the base storyteller (endgame quests …)");
            var big = gentle.FirstOrDefault(g => ((StorytellerCompProperties_OnOffCycle)g.props).IncidentCategory == IncidentCategoryDefOf.ThreatBig);
            t.Check(big != null && ((StorytellerCompProperties_OnOffCycle)big.props).minDaysPassed >= 20f, "no big threat in the first 20 days");
            var target = (IIncidentTarget)t.Map;
            if (big != null)
            {
                var pool = big.PoolForTests(IncidentCategoryDefOf.ThreatBig, target);
                t.Check(!pool.Any(d => d.defName == "MechCluster" || d.defName.EndsWith("ShipPartCrash")), "never mech clusters or ship parts (pool: " + string.Join(",", pool.Select(d => d.defName)) + ")");
                int inf = 0, n = 200;
                for (int i = 0; i < n; i++) if (big.PoolForTests(IncidentCategoryDefOf.ThreatBig, target).Any(d => d.defName == "Infestation")) inf++;
                if (pool.Any(d => d.defName == "RaidEnemy")) t.Check(inf < n * 0.4f, "infestations rarely in the pool (" + inf + "/" + n + ")");
            }
            if (kind != null)
            {
                var pool = kind.PoolForTests(target);
                t.Check(!pool.Any(d => d.defName == "ToxicFallout" || d.defName == "VolcanicWinter" || d.defName == "RansomDemand"), "never toxic fallout, volcanic winter or ransom demands");
                int pos = 0, neg = 0, total = 0;
                if (pool.Count > 0)
                    for (int i = 0; i < 600; i++)
                    {
                        var d = kind.Pick(pool, target); if (d == null) continue;
                        total++;
                        var mood = kind.MoodOf(d);
                        if (mood == IncidentMood.Positive || mood == IncidentMood.Ours) pos++; else if (mood == IncidentMood.Negative) neg++;
                    }
                t.Note("misc pool " + pool.Count + ": " + string.Join(",", pool.Select(d => d.defName)) + "; picks positive " + pos + ", negative " + neg + " of " + total);
                if (total > 0) t.Check(pos >= total * 0.6f && neg <= total * 0.15f, "mostly good news (" + pos + " good, " + neg + " bad of " + total + ")");
                t.Check(kind.MoodOf(DefDatabase<IncidentDef>.GetNamed("ResourcePodCrash")) == IncidentMood.Positive && kind.MoodOf(DefDatabase<IncidentDef>.GetNamed("ShortCircuit")) == IncidentMood.Negative, "events classified");
            }
            // Anomaly: the threat comps take their pool from vanilla (which draws from the anomaly pool by the Anomaly settings); Hetty filters out
            // none of its threats, only the named ones (mech clusters, ship parts) and a few misc events
            if (big != null)
            {
                var bp = (StorytellerCompProperties_HH_GentleCycle)big.props;
                t.Check(IncidentCategoryDefOf.ThreatBig.canUseAnomalyChance, "big threats use vanilla's anomaly chance");
                if (ModsConfig.AnomalyActive)
                {
                    var anomBig = Storyteller.AnomalyIncidents.Where(d => d.category == IncidentCategoryDefOf.ThreatBig).ToList();
                    var blocked = anomBig.Where(d => bp.neverFire.Contains(d.defName) || bp.keepChance.Any(k => k.incident == d.defName)).Select(d => d.defName).ToList();
                    t.Check(anomBig.Count > 0 && blocked.Count == 0, "no anomaly threat is filtered by Hetty (" + anomBig.Count + " anomaly big threats" + (blocked.Count > 0 ? "; filtered: " + string.Join(",", blocked) : "") + ")");
                }
                else t.Note("Anomaly not active: anomaly threat check skipped");
            }
            t.Check(!hetty.comps.Any(c => c is StorytellerCompProperties_Disease dc && dc.category?.defName == "DiseaseAnimal") && hetty.comps.Any(c => c is StorytellerCompProperties_Disease dc && dc.category?.defName == "DiseaseHuman"),
                "people may fall ill, animals never (as her description says)");
            // the storyteller page: the private field holds the chosen storyteller on this game version, and the suggestion chip is drawn
            var page = new Page_SelectStoryteller();
            var fld = AccessTools.Field(typeof(Page_SelectStoryteller), "storyteller");
            t.Check(fld != null, "Page_SelectStoryteller.storyteller exists on this game version");
            if (fld != null)
            {
                fld.SetValue(page, hetty);
                t.Check(HostingLine.ChosenStoryteller(page) == hetty, "the hosting line reads the chosen storyteller from the page");
                UiState.ClearPendingDifficulty(); HostingLine.ClearCache();
                UiState.EnsurePendingProfile().Tier = 4f;
                var win = new StorytellerLineTestWindow(hetty);
                Find.WindowStack.Add(win);
                Hearth.Shell.Tests.UiTestUtil.Shot("storyteller_line_hetty");
                for (int w = 0; w < 12 && win.Frames < 3; w++) yield return 10;   // a few real frames (fast-forwarded ticks can outrun OnGUI)
                Hearth.Shell.Tests.UiTestUtil.DrawCheck(t, win.Frames > 0, "the storyteller line was drawn (" + win.Frames + " frames)");
                if (win.Frames > 0)
                {
                    t.Check(win.LastChip != null && win.LastChip.Contains("Hetty") && win.LastChip.Contains(HearthProfile.TierName(2f)), "Hetty's suggestion chip is shown (" + win.LastChip + ")");
                    UiState.EnsurePendingProfile().Tier = 2f;
                    int f0 = win.Frames;
                    for (int w = 0; w < 12 && win.Frames < f0 + 2; w++) yield return 10;
                    if (win.Frames > f0) t.Check(win.LastChip == null, "no chip once the suggested difficulty is chosen");
                    win.Chosen = phoebe;
                    UiState.EnsurePendingProfile().Tier = 4f;
                    f0 = win.Frames;
                    for (int w = 0; w < 12 && win.Frames < f0 + 2; w++) yield return 10;
                    if (win.Frames > f0) t.Check(win.LastChip == null, "no chip for a storyteller without hints");
                }
                win.Close(false);
                UiState.ClearPendingDifficulty();
            }
            // guest arrivals follow the storyteller's hint
            var real = Find.Storyteller.def;
            try
            {
                Find.Storyteller.def = hetty;
                t.Check(Math.Abs(StorytellerHints.GuestArrivalFactor - 1.25f) < 0.001f, "with Hetty guests arrive ×1.25");
                Find.Storyteller.def = phoebe ?? real;
                t.Check(Math.Abs(StorytellerHints.GuestArrivalFactor - 1f) < 0.001f, "other storytellers ×1");
            }
            finally { Find.Storyteller.def = real; }
            // removal-safe save: written as Phoebe + hhStoryteller, read back as Hetty
            string path = ContentTestKit.TempPath("hh_storyteller_test.xml");
            var toSave = new Storyteller(hetty, DifficultyDefOf.Rough);
            Scribe.saver.InitSaving(path, "hhtest");
            try { Scribe_Deep.Look(ref toSave, "storyteller"); } finally { Scribe.saver.FinalizeSaving(); }
            t.Check(toSave.def == hetty, "saving gives the live storyteller its def back");
            string xml = File.ReadAllText(path);
            t.Check(xml.Contains("<def>Phoebe</def>") && xml.Contains("<hhStoryteller>HH_Hetty</hhStoryteller>") && !xml.Contains("<def>HH_Hetty</def>"), "the vanilla node says Phoebe, ours says Hetty");
            Storyteller back = null;
            Scribe.loader.InitLoading(path);
            try { Scribe_Deep.Look(ref back, "storyteller"); Scribe.loader.FinalizeLoading(); }
            catch (Exception e) { Scribe.ForceStop(); t.Check(false, "loading threw " + e.Message); }
            t.Check(back != null && back.def == hetty && back.storytellerComps.OfType<StorytellerComp_HH_KindMisc>().Any(), "loading brings Hetty and her comps back");
            var plain = new Storyteller(phoebe ?? real, DifficultyDefOf.Rough);
            Scribe.saver.InitSaving(path, "hhtest");
            try { Scribe_Deep.Look(ref plain, "storyteller"); } finally { Scribe.saver.FinalizeSaving(); }
            t.Check(!File.ReadAllText(path).Contains("hhStoryteller"), "vanilla storytellers are saved untouched");
            try { File.Delete(path); } catch (Exception) { }
            yield return 1;
        }
    }

    /// <summary>Draws the hosting line of the storyteller page in a plain window (the real page belongs to the new-game flow).</summary>
    sealed class StorytellerLineTestWindow : Window
    {
        public StorytellerDef Chosen; public int Frames; public string LastChip;
        public StorytellerLineTestWindow(StorytellerDef chosen) { Chosen = chosen; forcePause = false; absorbInputAroundWindow = false; doCloseX = true; }
        public override Vector2 InitialSize => new Vector2(1000f, 260f);
        public override void DoWindowContents(Rect inRect)
        {
            HostingLine.Draw(new Rect(inRect.x, inRect.y, inRect.width, 200f), Chosen);
            LastChip = HostingLine.LastDrawn; Frames++;
        }
    }

    /// <summary>Draws parts in a real Listing_ScenEdit, exactly as vanilla's scenario editor does.</summary>
    sealed class ScenEditTestWindow : Window
    {
        readonly Scenario scen; readonly List<ScenPart> parts;
        public int Frames; public float LastHeight;
        public ScenEditTestWindow(Scenario scen, List<ScenPart> parts) { this.scen = scen; this.parts = parts; forcePause = false; absorbInputAroundWindow = false; doCloseX = true; }
        public override Vector2 InitialSize => new Vector2(900f, 760f);
        public override void DoWindowContents(Rect inRect)
        {
            var listing = new Listing_ScenEdit(scen);
            listing.Begin(inRect);
            foreach (var p in parts) p.DoEditInterface(listing);
            LastHeight = listing.CurHeight;
            listing.End();
            Frames++;
        }
    }

    // ============================================================================================ creator pawn (F18)
    public sealed class CreatorSuite : HearthTestSuite
    {
        public override string Name => "content.creator";
        public override int MaxTicks => 3000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var bio = CreatorPawn.Bio;
            t.Check(bio != null && bio.name.First == "Julius" && bio.name.Last == "Berger" && bio.gender == GenderPossibility.Male, "fixed bio: Julius Berger");
            if (bio == null) yield break;
            t.Check(SolidBioDatabase.allBios.Count(b => b.name != null && b.name.First == "Julius" && b.name.Last == "Berger") == 1, "exactly once among the solid bios (" + SolidBioDatabase.allBios.Count + " bios)");
            t.Check(bio.childhood.slot == BackstorySlot.Childhood && bio.adulthood.slot == BackstorySlot.Adulthood && !bio.childhood.shuffleable && !bio.adulthood.shuffleable, "own childhood and adulthood, never given to random pawns");
            t.Check(bio.childhood.forcedTraits.Any(x => x.def == TraitDefOf.Kind) && bio.adulthood.forcedTraits.Any(x => x.def.defName == "Industriousness" && x.degree == 2), "kind and industrious");
            t.Check(!bio.pirateKing, "not flagged as a leader bio (pirateKing = false)");
            // vanilla's own rule (PawnBioAndNameGenerator.IsBioUseable): a faction-leader pawn kind only takes bios with pirateKing. Asked directly,
            // so the claim "never a faction leader" does not rest on the flag alone.
            var useable = AccessTools.Method(typeof(PawnBioAndNameGenerator), "IsBioUseable");
            var leaderKinds = DefDatabase<PawnKindDef>.AllDefsListForReading.Where(k => k.factionLeader && k.RaceProps != null && k.RaceProps.Humanlike).ToList();
            var anyFilter = new BackstoryCategoryFilter { categories = new List<string> { "Offworld", "Outlander", "Pirate", "Tribal", "Imperial", "ImperialCommon", "ImperialRoyal" } };
            t.Check(useable != null, "vanilla's bio check is there (PawnBioAndNameGenerator.IsBioUseable)");
            if (useable != null)
            {
                t.Check((bool)useable.Invoke(null, new object[] { bio, new BackstoryCategoryFilter { categories = new List<string> { "Offworld" } }, PawnKindDefOf.Colonist, Gender.Male, "Berger" }), "vanilla accepts the bio for a colonist");
                var takenBy = leaderKinds.Where(k => (bool)useable.Invoke(null, new object[] { bio, anyFilter, k, Gender.Male, "Berger" })).Select(k => k.defName).ToList();
                t.Check(leaderKinds.Count > 0 && takenBy.Count == 0, "vanilla refuses the bio for every faction-leader kind (" + leaderKinds.Count + " kinds" + (takenBy.Count > 0 ? "; taken by " + string.Join(",", takenBy) : "") + ")");
            }
            // and one real generation run per leader kind with the last name pinned: the leader may get any other Berger, never ours
            int leaderRuns = 0; bool leaderGotBio = false;
            foreach (var k in leaderKinds.Take(6))
            {
                var fac = k.defaultFactionDef != null ? Find.FactionManager.FirstFactionOfDef(k.defaultFactionDef) : null;
                if (fac == null) continue;
                for (int i = 0; i < 10; i++)
                {
                    Pawn lp = null;
                    try { lp = PawnGenerator.GeneratePawn(new PawnGenerationRequest(k, fac, PawnGenerationContext.NonPlayer, fixedGender: Gender.Male, fixedLastName: "Berger", forceGenerateNewPawn: true)); }
                    catch (Exception e) { t.Note("leader kind " + k.defName + " did not generate: " + e.Message); break; }
                    leaderRuns++;
                    if (lp.story?.Childhood == bio.childhood || lp.story?.Adulthood == bio.adulthood) leaderGotBio = true;
                    Find.WorldPawns.PassToWorld(lp, RimWorld.Planet.PawnDiscardDecideMode.Discard);
                }
            }
            t.Note("faction-leader generation runs: " + leaderRuns);
            t.Check(!leaderGotBio, "no generated faction leader carries the creator bio (" + leaderRuns + " runs)");
            var filter = new BackstoryCategoryFilter { categories = new List<string> { "Offworld" } };
            t.Check(filter.Matches(bio), "matches the offworld colonist backstories (crashlanded, wanderers)");
            t.Check(new BackstoryCategoryFilter { categories = new List<string> { "Outlander" } }.Matches(bio), "matches outlander pawns (traders, visitors, world pawns)");
            // generated like any creator pawn: ask for the last name so the test does not depend on the 1-in-several-hundred roll
            Pawn found = null;
            for (int i = 0; i < 300 && found == null; i++)
            {
                var req = new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, PawnGenerationContext.NonPlayer, fixedGender: Gender.Male, fixedLastName: "Berger", fixedBiologicalAge: 34f, forceGenerateNewPawn: true);
                var p = PawnGenerator.GeneratePawn(req);
                if (p.Name is NameTriple nt && nt.First == "Julius" && nt.Last == "Berger") found = p;
                else Find.WorldPawns.PassToWorld(p, RimWorld.Planet.PawnDiscardDecideMode.Discard);
            }
            t.Check(found != null, "the bio is picked by vanilla generation");
            if (found != null)
            {
                t.Check(found.story.Childhood == bio.childhood && found.story.Adulthood == bio.adulthood, "generated pawn carries both backstories");
                t.Check(found.story.traits.HasTrait(TraitDefOf.Kind) && found.story.traits.DegreeOfTrait(DefDatabase<TraitDef>.GetNamed("Industriousness")) == 2, "generated pawn is kind and industrious");
                Find.WorldPawns.PassToWorld(found, RimWorld.Planet.PawnDiscardDecideMode.Discard);
            }
            // the setting removes and restores the bio
            var s = HSettings.Find(CreatorPawn.SettingKey);
            t.Check(s != null && s.Global && s.Nerd, "the easter egg can be switched off (hidden nerd setting)");
            bool set0 = HSettings.IsOverridden(s); float v0 = HSettings.Get(s);
            try
            {
                HSettings.Set(s, 0f); CreatorPawn.Sync();
                t.Check(!SolidBioDatabase.allBios.Contains(bio), "switched off: not in the bios");
                HSettings.Set(s, 1f); CreatorPawn.Sync();
                t.Check(SolidBioDatabase.allBios.Count(b => b == bio) == 1, "switched on again: back once");
            }
            finally { if (set0) HSettings.Set(s, v0); else HSettings.Reset(s); CreatorPawn.Sync(); }
            yield return 1;
        }
    }

    // ============================================================================================ icons (F34)
    public sealed class IconsSuite : HearthTestSuite
    {
        public override string Name => "content.icons";
        public override int MaxTicks => 1500;
        public override IEnumerable<int> Run(TestContext t)
        {
            foreach (var id in HHIcons.AllIds())
            {
                var tex = HHIcons.ById(id);
                t.Check(tex != null && tex != BaseContent.BadTex, "icon " + id);
            }
            foreach (var p in HearthSettingsWindow.BuildPages()) t.Check(p.Icon != null, "settings page " + p.Id + " has an icon");
            t.Check(ConsoleComm.SilverlineMark != null && ConsoleComm.FinanceMark != null, "Silverline and finance marks");
            t.Check(SilverlineComm.Instance.Icon != null && ClawbackComm.Instance.Icon != null && SilverlineFallbackComm.Instance.Icon != null && ThrumboComm.Instance.Icon != null, "every comms console entry has its own icon");
            var comp = BankGameComp.Current;
            if (comp != null && comp.HasAccount && comp.Bank != null && !comp.Bank.iconPath.NullOrEmpty())
                t.Check(SilverlineComm.Instance.Icon == comp.Bank.Icon, "the bank entry shows the colony's own bank (" + comp.Bank.defName + ")");
            var pawn = t.Map.mapPawns.FreeColonistsSpawned.FirstOrDefault();
            var def = DefDatabase<ThingDef>.GetNamed("CommsConsole");
            var console = (Building_CommsConsole)ThingMaker.MakeThing(def, GenStuff.DefaultStuffFor(def));
            GenSpawn.Spawn(console, t.FreeCellNearCenter(30), t.Map);   // the menu decorator checks reservations on the map
            console.SetFaction(Faction.OfPlayer);
            var iconField = AccessTools.Field(typeof(FloatMenuOption), "iconTex") ?? AccessTools.Field(typeof(FloatMenuOption), "itemIcon");
            if (pawn != null && iconField != null)
            {
                foreach (ConsoleComm c in new ConsoleComm[] { SilverlineComm.Instance, ClawbackComm.Instance, SilverlineFallbackComm.Instance, ThrumboComm.Instance })
                {
                    var opt = c.CommFloatMenuOption(console, pawn);
                    t.Check(opt != null && iconField.GetValue(opt) as Texture2D == c.Icon, c.GetType().Name + ": menu option carries the icon");
                }
            }
            if (console.Spawned) console.Destroy();
            var meta = HearthMod.Instance?.Content?.ModMetaData;
            t.Check(meta != null && File.Exists(meta.ModIconImagePath), "About/ModIcon.png is there for the mod list");
            yield return 1;
        }
    }

    // ============================================================================================ scenario editor window (F08 + F34 drawing)
    public sealed class ScenarioEditorSuite : HearthTestSuite
    {
        public override string Name => "content.editor";
        public override int MaxTicks => 2000;
        public override IEnumerable<int> Run(TestContext t)
        {
            int errors0 = HLog.ErrorCount;
            var rules = new ScenPart_HHRules { def = DefDatabase<ScenPartDef>.GetNamed("HH_ScenRules"), tier = 2f };
            rules.SetSetting("inn.arrivalRate", 1.3f);
            rules.SetModuleState(ModuleId.Casino, 2);   // an overridden module: its row shows the amber mark and the Reset button
            var scratch = rules.ToScratch(); float tb = scratch.Tier; var cb = scratch.Content;
            bool closed = false;
            var dlg = new Dialog_HHProfileEditor(scratch, () => { closed = true; rules.FromScratch(scratch, tb, cb); }) { forcePause = false, absorbInputAroundWindow = false };
            Find.WindowStack.Add(dlg);
            string page0 = HearthSettingsWindow.SelectedPage;
            SettingRowUI.ForceDrawAll = true;
            foreach (var id in new[] { "modules", "difficulty", "mod:Core", "mod:Inn", "mod:Bank" })
            {
                HearthSettingsWindow.SelectedPage = id;
                int frames0 = UiProbe.Count("settings");
                Hearth.Shell.Tests.UiTestUtil.Shot("scenario_editor_" + id.Replace(':', '_'));
                yield return 20;
                t.Check(dlg.IsOpen, "scenario editor page " + id + " stays open");
                Hearth.Shell.Tests.UiTestUtil.DrawCheck(t, UiProbe.Count("settings") > frames0, "scenario editor page " + id + " was drawn (" + (UiProbe.Count("settings") - frames0) + " frames)");
            }
            SettingRowUI.ForceDrawAll = false;
            t.Check(HearthSettingsWindow.Target != scratch, "outside the editor the settings window edits the colony again");
            dlg.Close(false);
            yield return 2;
            t.Check(closed && rules.settings.Any(s => s.key == "inn.arrivalRate") && rules.ModuleState(ModuleId.Casino) == 2, "closing the editor hands the values back");
            // the module Reset of the editor removes the entry from the scratch profile: the part then leaves the module to the template
            var scratch2 = rules.ToScratch(); float tb2 = scratch2.Tier; var cb2 = scratch2.Content;
            scratch2.SetModule(ModuleId.Casino, true); scratch2.SetModule(ModuleId.Casino, false);
            rules.FromScratch(scratch2, tb2, cb2);
            t.Check(rules.ModuleState(ModuleId.Casino) == 2, "switching a module back and forth keeps an explicit 'off'");
            scratch2.modules.Remove(ModuleId.Casino);
            rules.FromScratch(scratch2, tb2, cb2);
            t.Check(rules.ModuleState(ModuleId.Casino) == 0, "after Reset the module follows the template again");
            HearthSettingsWindow.SelectedPage = page0;
            // the picker window
            var pick = new Dialog_HHSettingPicker(_ => { }) { forcePause = false, absorbInputAroundWindow = false };
            Find.WindowStack.Add(pick);
            yield return 20;
            t.Check(pick.IsOpen, "setting picker drew");
            pick.Close(false);
            // the scenario parts' own rows in a real Listing_ScenEdit (vanilla's editor), each wrapped in Guard
            var dark = ContentTestKit.Scen("HH_Scen_BackRoom");
            if (dark != null)
            {
                var scen = dark.scenario.CopyForEditing();
                var parts = ScenarioSeed.PartsOf(scen).Where(p => p.GetType().Namespace == "Hearth" || p.GetType().Namespace == "Silverline").ToList();
                parts.Add(new ScenPart_HHSetting { def = DefDatabase<ScenPartDef>.GetNamed("HH_ScenSetting"), key = "inn.arrivalRate", value = 1.2f });
                parts.Add(new ScenPart_HHSetting { def = DefDatabase<ScenPartDef>.GetNamed("HH_ScenSetting"), key = "no.suchKey", value = 1f });
                parts.Add(new ScenPart_HHStartingResearch { def = DefDatabase<ScenPartDef>.GetNamed("HH_ScenStartingResearch"), project = "HH_Innkeeping" });
                t.Check(parts.OfType<ScenPart_HHRules>().Any() && parts.OfType<ScenPart_SLStartingAccount>().Any(), "the dark scenario has a rules part and a bank account part to draw");
                var ew = new ScenEditTestWindow(scen, parts);
                Find.WindowStack.Add(ew);
                Hearth.Shell.Tests.UiTestUtil.Shot("scenario_parts_listing");
                yield return 15;
                Hearth.Shell.Tests.UiTestUtil.DrawCheck(t, ew.Frames > 0, "our scenario parts were drawn in vanilla's editor listing (" + ew.Frames + " frames, " + ew.LastHeight.ToString("0") + " px)");
                t.Check(ew.IsOpen, "the editor listing window survived our rows");
                ew.Close(false);
            }
            t.Check(HLog.ErrorCount == errors0, "no errors while drawing (" + (HLog.ErrorCount - errors0) + ")");
            yield return 1;
        }
    }
}
#endif
