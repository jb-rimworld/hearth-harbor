// SPDX-License-Identifier: MIT
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Hearth.Testing;
using Hearth.Shell;

namespace Hearth.Underworld
{
    /// <summary>
    /// Final check 1 of 1.2.0, "one colonist" (explicit only): what the Workshop commenter tried. Default settings, nothing switched on by hand,
    /// a map with exactly one colonist, no animals and no guests. Places a fence ring, a gate and the fight pit marker through the build designators
    /// (god mode places them at once), then walks the three ways to a bout: right-click on the marker, the "Fighters…" window, "Call an opponent".
    /// Every inspect string, gizmo, tooltip, float menu option, message and letter the player would see is written in order to the transcript
    /// (env HH_PLAYTEST_OUT, else hh_tests/playtest_&lt;lang&gt;.md). With HH_UI_SHOTS set it saves screenshots of the tab, the inspect pane and the window.
    /// The checks only fail on things that break (errors, keys shown raw, a bout that never ends); the judging is done by a person reading the transcript.
    /// </summary>
    public sealed class UwPlaytestOneColonistSuite : HearthTestSuite
    {
        public override string Name => "underworld.playtest.onecolonist";
        public override ModuleId Module => ModuleId.Underworld;
        public override bool ExplicitOnly => true;
        public override int MaxTicks => 60000;

        // ---- transcript --------------------------------------------------------------------------------------------------
        static readonly List<string> lines = new List<string>();
        static string outPath, lang;
        static void R(string s) { lines.Add(s); Log.Message("[HHPLAY] " + s); }
        static void H(string s) { lines.Add(""); lines.Add("## " + s); lines.Add(""); Log.Message("[HHPLAY] ## " + s); }
        static void Flush()
        {
            if (outPath == null) return;
            try { System.IO.File.WriteAllLines(outPath, lines); } catch (Exception e) { Log.Warning("[HHTEST] playtest transcript not written: " + e.Message); }
        }
        static string One(string s) => s == null ? "(null)" : s.Replace("\r", "").Replace("\n", " / ");

        // ---- messages and letters the player sees -------------------------------------------------------------------------
        const string HarmonyId = "juliusberger00.hearthharbor.tests.playtest";
        static readonly List<string> msgs = new List<string>();
        static readonly HashSet<Letter> seenLetters = new HashSet<Letter>();
        public static void MessagePostfix(Message msg) { if (msg != null) msgs.Add("MESSAGE [" + (msg.def?.defName ?? "?") + "] " + One(msg.text)); }
        static void Drain(TestContext t)
        {
            foreach (var m in msgs) { R("- " + m); CheckRaw(t, m, "message"); }
            msgs.Clear();
            foreach (var l in Find.LetterStack.LettersListForReading)
            {
                if (!seenLetters.Add(l)) continue;
                string text = (l as ChoiceLetter)?.Text.ToString();
                R("- LETTER [" + l.def?.defName + "] " + One(l.Label.ToString()) + (text != null ? " :: " + One(text) : ""));
                CheckRaw(t, l.Label.ToString() + " " + text, "letter");
            }
        }

        /// <summary>A key shown raw ("HH.Underworld.…") is a bug in any language.</summary>
        static void CheckRaw(TestContext t, string s, string what)
        {
            if (s != null && s.Contains("HH.")) t.Check(false, "raw key in a " + what + ": " + One(s));
        }

        // ---- what the player sees on the screen -------------------------------------------------------------------------
        static void Inspect(TestContext t, Thing thing, string title)
        {
            if (thing == null || !thing.Spawned) { R("**" + title + "**: (not spawned)"); return; }
            string s = Guard.Run(ModuleId.Underworld, "Playtest.Inspect", () => thing.GetInspectString(), "(exception)");
            R("**Inspect pane, " + title + "** (" + thing.LabelCap + "):");
            foreach (var l in (s ?? "").Split('\n')) if (!l.Trim().NullOrEmpty()) R("    " + l.TrimEnd());
            CheckRaw(t, s, "inspect string");
        }

        static void Gizmos(TestContext t, Thing thing, string title)
        {
            if (thing == null || !thing.Spawned) return;
            R("**Gizmos, " + title + "**:");
            foreach (var g in thing.GetGizmos())
            {
                if (!(g is Command c)) { R("    - (" + g.GetType().Name + ")"); continue; }
                string state = c is Command_Toggle tg ? (tg.isActive != null && tg.isActive() ? " [on]" : " [off]") : "";
                string dis = c.Disabled ? " DISABLED: " + One(c.disabledReason) : "";
                R("    - `" + One(c.LabelCap) + "`" + state + dis + " | tooltip: " + One(c.Desc));
                CheckRaw(t, c.LabelCap + " " + c.Desc + " " + c.disabledReason, "gizmo");
            }
        }

        static Command OurGizmo(CompFightPit pit, string labelStart)
        {
            foreach (var g in pit.CompGetGizmosExtra()) if (g is Command c && c.defaultLabel != null && c.defaultLabel.StartsWith(labelStart)) return c;
            return null;
        }

        static void FloatMenu(TestContext t, Pawn col, CompFightPit pit, string title)
        {
            R("**Right-click on the marker with " + col.LabelShortCap + ", " + title + "**:");
            Find.Selector.ClearSelection(); Find.Selector.Select(col, false, false);
            List<FloatMenuOption> all = null;
            try
            {
                var m = AccessTools.GetDeclaredMethods(typeof(FloatMenuMakerMap)).FirstOrDefault(x => x.Name == "GetOptions" && x.IsPublic && x.GetParameters().Length == 3);
                if (m != null)
                {
                    var args = new object[] { new List<Pawn> { col }, pit.parent.Position.ToVector3Shifted(), null };
                    all = m.Invoke(null, args) as List<FloatMenuOption>;
                }
            }
            catch (Exception e) { R("    (the full vanilla menu could not be built: " + One((e.InnerException ?? e).Message) + ")"); }
            if (all != null)
            {
                R("    full menu as the game builds it (" + all.Count + " options):");
                foreach (var o in all) { R("    - `" + One(o.Label) + "`" + (o.Disabled ? " (greyed out)" : "")); CheckRaw(t, o.Label, "float menu option"); }
            }
            var ours = pit.CompFloatMenuOptions(col).ToList();
            R("    options of the marker itself (" + ours.Count + "):");
            foreach (var o in ours) { R("    - `" + One(o.Label) + "`" + (o.Disabled ? " (greyed out)" : " (clickable)")); CheckRaw(t, o.Label, "float menu option"); }
            if (all == null) R("    (only the marker's own options were read)");
        }

        static void FightersWindow(TestContext t, CompFightPit pit, string title)
        {
            R("**Fighters window, " + title + "**:");
            R("    title: " + UwPay.T("Pit.Win.Title"));
            R("    status line (grey, under the title): " + One(pit.StatusLine()));
            var cands = pit.Candidates(true);
            foreach (var g in new[] { PitKind.Colonist, PitKind.Prisoner, PitKind.Animal, PitKind.Guest, PitKind.Beast })
            {
                var rows = cands.Where(c => c.Kind == g && c.Pawn != null && !c.Pawn.Destroyed).ToList();
                string off = g == PitKind.Animal ? (pit.allowAnimals ? null : UwPay.T("Pit.Win.GroupOff", UwPay.T("Pit.Animals")))
                    : g == PitKind.Guest ? (pit.allowGuests ? null : UwPay.T("Pit.Win.GroupOff", UwPay.T("Pit.Guests")))
                    : g == PitKind.Prisoner ? (pit.allowPrisoners ? null : UwPay.T("Pit.Win.GroupOff", UwPay.T("Pit.Prisoners"))) : null;
                if (rows.Count == 0 && off == null && g != PitKind.Colonist) continue;
                R("    section `" + UwPay.T("Pit.Win.Group." + g) + "`");
                if (off != null) { R("      - " + off); continue; }
                if (rows.Count == 0) { R("      - " + UwPay.T("Pit.Win.Nobody")); continue; }
                foreach (var c in rows)
                {
                    var p = c.Pawn;
                    string skill = p.RaceProps.Humanlike && p.skills != null ? UwPay.T("Pit.Win.Melee", p.skills.GetSkill(SkillDefOf.Melee).Level) : UwPay.T("Pit.Win.Power", c.Power.ToString("0"));
                    string box = c.Kind == PitKind.Colonist ? (pit.IsTicked(p) ? " [x] " : " [ ] ") + "(box tooltip: " + UwPay.T("Pit.Win.Tick") + ") " : " ";
                    R("      - " + p.LabelShortCap + " | " + PitFighters.KindLabel(c.Kind) + " | " + skill + " | " + p.health.summaryHealth.SummaryHealthPercent.ToStringPercent() + " |" + box + (c.Reason == null ? UwPay.T("Pit.Win.Ready") : c.Reason + (c.SoftReason ? " (amber)" : c.Reason == UwPay.T("Pit.Why.NotTicked") ? " (amber)" : " (red)")));
                    CheckRaw(t, c.Reason, "Fighters row");
                }
            }
            R("    bottom: " + UwPay.T("Pit.Win.PickSides") + " | `" + UwPay.T("Pit.Win.SideA") + ": " + UwPay.T("Pit.Win.Anyone") + "` `" + UwPay.T("Pit.Win.SideB") + ": " + UwPay.T("Pit.Win.Anyone") + "`");
            var manual = pit.ManualReady();
            R("    side menus list: " + (manual.Count == 0 ? "only `" + UwPay.T("Pit.Win.Anyone") + "`" : string.Join(", ", manual.Select(c => c.Pawn.LabelShortCap + " (" + PitFighters.KindLabel(c.Kind) + ")"))));
            string closed = pit.ClosedReason() ?? (FightBout.OfPit(pit) != null ? UwPay.T("Pit.Why.BoutRunning") : null) ?? pit.ManualShortage(manual);
            R("    button `" + UwPay.T("Pit.Win.Start") + "`: " + (closed == null ? "clickable" : "greyed out, tooltip: " + closed));
            string hw = PitOpponents.CannotHire(pit, pit.LoneFighter());
            R("    button `" + PitOpponents.CallLabel + "`: " + (hw == null ? "clickable, fights it: " + pit.LoneFighter()?.LabelShortCap : "greyed out, tooltip: " + hw));
        }

        static void Tab(TestContext t, Map map, string title)
        {
            UnderworldPage.Invalidate(); FacilityStatus.MarkDirty();
            R("**Underworld tab, " + title + "**:");
            R("    header: " + UwPay.T("Page.Title") + " / " + "HH.UI.Page.Sub.underworld".TranslateOr("(sub missing)"));
            R("    " + UwPay.T("Page.ContentLevel", ("HH.UI.Content." + Underworld.Intensity).Translate().Resolve()) + " [button `" + "HH.UI.Tab.Settings".Translate() + "`]");
            var offers = UnderworldPage.Offers(map, refresh: true);
            if (offers.Count > 0)
            {
                R("    section `" + UwPay.T("Page.Offers") + "`");
                foreach (var o in offers)
                {
                    bool can = FacilityStatus.CanAccept(o, out string why);
                    R("      - " + One(FacilityStatus.OfferLine(o)) + " [`" + UnderworldPage.AcceptLabel(o) + "`" + (can ? "" : " greyed out, tooltip: " + why) + "] [`" + UnderworldPage.DeclineLabel(o) + "`]");
                }
            }
            R("    section `" + UwPay.T("Page.Facilities") + "`");
            var rows = UnderworldPage.Rows(map, refresh: true);
            if (rows.Count == 0) R("      " + UwPay.T("Page.NoMap"));
            foreach (var r in rows)
            {
                var btns = new List<string>();
                if (r.State == FacilityState.NotBuilt && r.BuildDef != null)
                {
                    var des = UwBuild.DesignatorFor(r.BuildDef, out string why);
                    btns.Add("`" + UwPay.T("Page.Build") + "`" + (des != null ? " (tooltip: " + UwPay.T("Page.Build.Desc", r.BuildDef.label) + ")" : " greyed out, tooltip: " + why));
                }
                if (r.Pit != null) { btns.Add("`" + UwPay.T("Pit.FightersBtn") + "`"); btns.Add("`" + UwPay.T("Page.Show") + "`"); }
                R("      - (" + r.State + " dot, tooltip: " + UwPay.T("Page.State." + r.State) + ") **" + r.Name + "**: " + One(r.Line) + (btns.Count > 0 ? "  " + string.Join(" ", btns) : ""));
                foreach (var s in r.Sub) R("          small grey: " + One(s));
                CheckRaw(t, r.Name + " " + r.Line + " " + string.Join(" ", r.Sub), "tab row");
            }
            R("    section `" + UwPay.T("Page.WhispersHead") + "`: " + One(UwPay.T("Page.Whispers", Underworld.Whispers.ToString("0.0")) + " " + UwPay.T("Page.Whispers.Desc")));
            R("      " + One(UwPay.T("Page.Weights", Underworld.RespectableArrivalFactor.ToString("0.00"), Underworld.OutlawArrivalFactor.ToString("0.00"))));
            if (Underworld.HighRollersActive) R("      " + UwPay.T("Page.HighRollers"));
            var c = Underworld.Comp;
            R("    section `" + UwPay.T("Page.StatsHead") + "`: " + One(UwPay.T("Page.Stats1", c.Bouts, c.Bets, new Money(c.HouseTakeMicro).ToStringShort())) + " / " + One(UwPay.T("Page.Stats2", c.DealsBought, c.DealsSold, new Money(c.FenceProfitMicro).ToStringShort())) + " / " + One(UwPay.T("Page.Stats3", c.LoungeSessions, c.GangVisits, c.Smuggles, c.Hunts, c.OrgansSold)));
        }

        static void Guide(string title)
        {
            GuideState.Update();
            var v = GuideState.Views.FirstOrDefault(x => x.Def?.defName == "HH_Guide_BackRoom");
            if (v == null) { R("**Guide, " + title + "**: topic HH_Guide_BackRoom not in the guide"); return; }
            R("**Guide topic `" + v.Def.LabelCap + "`, " + title + "**: unlocked=" + v.Unlocked + ", " + v.DoneCount + "/" + v.Steps.Count + " steps");
            foreach (var s in v.Steps) R("    - [" + (s.done ? "x" : " ") + "] " + s.step.label + (s.step.description.NullOrEmpty() ? "" : " :: " + One(s.step.description)));
        }

        static void Shot(string name) => Hearth.Shell.Tests.UiTestUtil.Shot((lang ?? "xx") + "_" + name);

        static void Designator(TestContext t, ThingDef d, IntVec3 cell, out Designator_Build des)
        {
            des = null;
            if (d == null) { R("- (def missing)"); return; }
            Designator_Build visible = null;
            try { des = BuildCopyCommandUtility.FindAllowedDesignator(d, mustBeVisible: false); visible = BuildCopyCommandUtility.FindAllowedDesignator(d, mustBeVisible: true); }
            catch (Exception e) { R("- " + d.defName + ": lookup failed: " + e.Message); }
            var cat = d.designationCategory;
            string research = d.researchPrerequisites == null || d.researchPrerequisites.Count == 0 ? "none" : string.Join(", ", d.researchPrerequisites.Select(r => r.LabelCap.ToString() + (r.IsFinished ? " (done)" : " (NOT done)")));
            R("- **" + d.LabelCap + "** (" + d.defName + "): architect tab `" + (cat?.LabelCap.ToString() ?? "none") + "`" + (d.designatorDropdown != null ? ", inside the dropdown `" + d.designatorDropdown.LabelCap.ToString() + "`" : "")
                + ", research: " + research + ", allowed designator: " + (des != null) + ", visible in the architect: " + (visible != null)
                + (des != null ? ", designator disabled: " + des.Disabled + (des.Disabled ? " (" + des.disabledReason + ")" : "") : ""));
            R("    cost: " + (d.CostList == null ? "-" : string.Join(", ", d.CostList.Select(x => x.count + " " + x.thingDef.label))) + (d.MadeFromStuff ? " + " + d.CostStuffCount + " stuff" : "") + ", work " + d.GetStatValueAbstract(StatDefOf.WorkToBuild, d.MadeFromStuff ? ThingDefOf.WoodLog : null).ToString("0"));
            R("    tooltip (description): " + One(d.description));
            t.Check(des != null && visible != null && d.IsResearchFinished, d.defName + ": the player can build it from the architect without research");
            if (des != null && cell.IsValid)
            {
                bool god = DebugSettings.godMode; DebugSettings.godMode = false;
                if (d.MadeFromStuff) des.SetStuffDef(ThingDefOf.WoodLog);
                var acc = des.CanDesignateCell(cell);
                DebugSettings.godMode = god;
                R("    a blueprint at the chosen cell would be accepted (no god mode): " + acc.Accepted + (acc.Reason.NullOrEmpty() ? "" : " (" + acc.Reason + ")"));
            }
        }

        static int Place(Designator_Build des, IEnumerable<IntVec3> cells, Map map, ThingDef d, List<string> refused)
        {
            int n = 0;
            bool god = DebugSettings.godMode; DebugSettings.godMode = true;
            try
            {
                if (d.MadeFromStuff) des.SetStuffDef(ThingDefOf.WoodLog);
                foreach (var c in cells)
                {
                    var acc = des.CanDesignateCell(c);
                    if (!acc.Accepted) { refused.Add(c + ": " + acc.Reason); continue; }
                    des.DesignateSingleCell(c);
                    var th = c.GetThingList(map).FirstOrDefault(x => x.def == d);
                    if (th != null) { UwKit.Track(th); n++; }
                    else refused.Add(c + ": nothing spawned");
                }
            }
            finally { DebugSettings.godMode = god; }
            return n;
        }

        // ---- the run ------------------------------------------------------------------------------------------------------
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map;
            int errors0 = HLog.ErrorCount;
            lines.Clear(); msgs.Clear(); seenLetters.Clear();
            lang = LanguageDatabase.activeLanguage?.folderName?.StartsWith("German") == true ? "de" : LanguageDatabase.activeLanguage?.folderName?.StartsWith("English") == true ? "en" : "xx";
            outPath = Environment.GetEnvironmentVariable("HH_PLAYTEST_OUT");
            if (outPath.NullOrEmpty()) outPath = "/private/tmp/claude-501/hh_tests/playtest_" + lang + ".md";
            foreach (var l in Find.LetterStack.LettersListForReading) seenLetters.Add(l);
            var harmony = new Harmony(HarmonyId);
            var msgMethod = AccessTools.Method(typeof(Messages), nameof(Messages.Message), new[] { typeof(Message), typeof(bool) });
            if (msgMethod != null) harmony.Patch(msgMethod, postfix: new HarmonyMethod(typeof(UwPlaytestOneColonistSuite), nameof(MessagePostfix)));
            var removed = new List<(Pawn p, IntVec3 c)>();
            Pawn col = null;
            try
            {
                R("# Playtest \"one colonist\" (transcript, " + LanguageDatabase.activeLanguage?.folderName + ")");
                R("");
                R("Game " + VersionControl.CurrentVersionStringWithRev + ", map " + map.Size.x + "x" + map.Size.z + ", tick " + Find.TickManager.TicksGame + ". Messages patch: " + (msgMethod != null));
                R("Settings as the save has them (nothing changed by the suite): content level " + Underworld.Intensity + ", tier " + HearthProfile.Current.Tier + ", Underworld module on " + Modules.On(ModuleId.Underworld) + ", visible " + Underworld.Visible + ", peaceful " + Underworld.Peaceful
                    + ", opponent fee " + PitFighters.Silver(PitOpponents.Fee) + ", score limit " + HSettings.Get("underworld.pit.scoreLimit") + ", interval " + HSettings.Get("underworld.pit.intervalHours") + " h.");
                R("Messages and letters are listed where they appear, as `- MESSAGE [type]` and `- LETTER [type]`.");

                // ---- a map with one colonist, no animals, no guests
                H("0. The colony: one colonist, no animals, no guests");
                col = PitKit.Fighter(map);
                if (col == null) { t.Check(false, "no colonist who can fight"); yield break; }
                var colonists = map.mapPawns.FreeColonistsSpawned.ToList();
                var animals = map.mapPawns.SpawnedColonyAnimals.ToList();
                var guests = GuestApi.GuestsOn(map).ToList();
                R("The quicktest map had " + colonists.Count + " colonists (" + string.Join(", ", colonists.Select(p => p.LabelShortCap)) + "), " + animals.Count + " colony animals (" + string.Join(", ", animals.Select(p => p.LabelShortCap + " " + p.def.label)) + "), " + guests.Count + " guests.");
                foreach (var p in colonists) if (p != col) { removed.Add((p, p.Position)); p.DeSpawn(); }
                foreach (var p in animals) { removed.Add((p, p.Position)); p.DeSpawn(); }
                foreach (var g in guests) if (g.Spawned) g.Destroy();
                if (col.Drafted) col.drafter.Drafted = false;
                R("Kept: " + col.LabelShortCap + " (" + col.story?.TitleCap + ", melee " + col.skills?.GetSkill(SkillDefOf.Melee)?.Level + ", traits " + string.Join(", ", col.story?.traits?.allTraits?.Select(x => x.LabelCap) ?? new string[0]) + "). Everyone else was taken off the map.");
                R("Colony till (the account bouts and the opponent fee use): " + UwKit.Till(map).ToStringShort() + ". Silver lying on the map: " + map.resourceCounter.Silver + ".");
                t.Check(map.mapPawns.FreeColonistsSpawnedCount == 1 && map.mapPawns.SpawnedColonyAnimals.Count == 0 && !GuestApi.GuestsOn(map).Any(), "one colonist, no animals, no guests");
                // the commenter's case: nothing in the till (no guests ever paid); the suite never puts silver into it
                if (UwKit.Till(map).Micro > 0) { R("The suite empties the till (" + UwKit.Till(map).ToStringShort() + ")."); Ledger.Current.Post(Underworld.Till(map), "world", UwKit.Till(map), "playtest: empty till"); }
                R("Opponents hired in this game so far: " + Underworld.Comp.OpponentsHired + " (the first one is free).");
                Drain(t);
                Find.DesignatorManager.Deselect(); Find.Selector.ClearSelection();
                UnderworldPage.Invalidate(); FacilityStatus.MarkDirty();
                yield return 30;

                // ---- 1. the tab with nothing built
                H("1. The Underworld tab before anything is built");
                Tab(t, map, "nothing built");
                Guide("nothing built");
                var tabDef = DefDatabase<MainButtonDef>.GetNamed("HH_MainTab");
                HUI.TestScreen = new Vector2(1280f, 720f);
                MainTabWindow_HH.SelectedPage = "underworld";
                Find.MainTabsRoot.SetCurrentTab(tabDef, false);
                yield return 30;
                Shot("uw_tab_nothing_built");
                yield return 20;
                Find.MainTabsRoot.EscapeCurrentTab(false);
                HUI.TestScreen = null;
                Flush();

                // ---- 2. the designators
                H("2. Can the player build it? (architect)");
                if (!UwKit.TryFindRect(map, 7, 7, out var inner)) { t.Check(false, "no free 9x9 patch for the arena"); yield break; }
                var outer = inner.ExpandedBy(1);
                var fenceDef = DefDatabase<ThingDef>.GetNamedSilentFail("Fence");
                var gateDef = DefDatabase<ThingDef>.GetNamedSilentFail("FenceGate");
                Designator(t, fenceDef, outer.EdgeCells.First(), out var fenceDes);
                Designator(t, gateDef, outer.EdgeCells.First(), out var gateDes);
                Designator(t, UwDefs.FightPit, inner.CenterCell, out var pitDes);
                bool sel = UnderworldPage.StartBuilding(UwDefs.FightPit);
                R("The tab's `" + UwPay.T("Page.Build") + "` button on the pit row selects: " + ((Find.DesignatorManager.SelectedDesignator as Designator_Build)?.PlacingDef?.LabelCap.ToString() ?? "nothing") + " (" + sel + ")");
                Find.DesignatorManager.Deselect();
                Drain(t);
                if (fenceDes == null || pitDes == null) { t.Check(false, "no designator for the fence or the marker"); yield break; }

                // ---- 3. build the ring, the marker, the gate
                H("3. Building: a fence ring, the marker in the middle, then a gate");
                var refused = new List<string>();
                int nf = Place(fenceDes, outer.EdgeCells, map, fenceDef, refused);
                R("Fence ring of " + outer.Width + "x" + outer.Height + " (inside " + inner.Width + "x" + inner.Height + " = " + inner.Area + " tiles) placed through the fence designator in god mode: " + nf + " pieces" + (refused.Count > 0 ? ", refused: " + string.Join("; ", refused.Take(5)) : ""));
                map.regionAndRoomUpdater.TryRebuildDirtyRegionsAndRooms();
                yield return 5;
                refused.Clear();
                int np = Place(pitDes, new[] { inner.CenterCell }, map, UwDefs.FightPit, refused);
                var marker = inner.CenterCell.GetThingList(map).FirstOrDefault(x => x.def == UwDefs.FightPit);
                var pit = marker?.TryGetComp<CompFightPit>();
                R("Marker placed: " + (np == 1) + (refused.Count > 0 ? " (" + string.Join("; ", refused) + ")" : ""));
                if (pit == null) { t.Check(false, "the marker was not placed"); yield break; }
                yield return 30;
                Drain(t);
                R("Marker settings as built (defaults): open " + pit.enabled + ", animals " + pit.allowAnimals + ", guests " + pit.allowGuests + ", prisoners " + pit.allowPrisoners + ", volunteers " + pit.allowVolunteers + ".");
                var ai = pit.ArenaInfo();
                R("Arena as the marker sees it: closed " + ai?.Closed + ", " + ai?.Cells.Count + " tiles, reached edge " + ai?.ReachedEdge + ", too big " + ai?.TooBig + ".");
                bool reach = col.CanReach(marker, Verse.AI.PathEndMode.OnCell, Danger.Deadly);
                R(col.LabelShortCap + " can walk to the marker without a gate: " + reach + ".");
                Inspect(t, marker, "ring without a gate");
                Gizmos(t, marker, "ring without a gate");
                if (gateDes != null)
                {
                    refused.Clear();
                    var gateCell = new IntVec3(inner.CenterCell.x, 0, outer.minZ);
                    gateCell.GetEdifice(map)?.Destroy();
                    int ng = Place(gateDes, new[] { gateCell }, map, gateDef, refused);
                    map.regionAndRoomUpdater.TryRebuildDirtyRegionsAndRooms(); pit.Invalidate();
                    yield return 30;
                    Drain(t);
                    reach = col.CanReach(marker, Verse.AI.PathEndMode.OnCell, Danger.Deadly);
                    ai = pit.ArenaInfo();
                    R("Gate placed in the south side (one fence piece replaced): " + (ng == 1) + (refused.Count > 0 ? " (" + string.Join("; ", refused) + ")" : "") + ". Arena: closed " + ai?.Closed + ", " + ai?.Cells.Count + " tiles. " + col.LabelShortCap + " can walk to the marker: " + reach + ".");
                }
                Inspect(t, marker, "ring with a gate, nothing ticked");
                Gizmos(t, marker, "ring with a gate, nothing ticked");
                Tab(t, map, "pit built");
                Guide("pit built");
                HUI.TestScreen = new Vector2(1280f, 720f);
                MainTabWindow_HH.SelectedPage = "underworld";
                Find.MainTabsRoot.SetCurrentTab(tabDef, false);
                yield return 30;
                Shot("uw_tab_pit_built");
                yield return 20;
                Find.MainTabsRoot.EscapeCurrentTab(false);
                HUI.TestScreen = null;
                Find.Selector.ClearSelection();
                CameraJumper.TryJumpAndSelect(marker);
                Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Inspect, false);
                yield return 30;
                Shot("pit_inspect_gizmos");
                yield return 20;
                Flush();

                // ---- 4. way 1: right-click on the marker
                H("4. Way 1: right-click on the marker with the colonist selected");
                FloatMenu(t, col, pit, "nothing ticked, empty till");
                var ours = pit.CompFloatMenuOptions(col).ToList();
                t.Check(ours.Count > 0, "the marker offers at least one right-click option to a lone colonist");
                var clickable = ours.FirstOrDefault(o => !o.Disabled);
                R(clickable == null ? "Nothing in the marker's menu can be clicked." : "Clickable: `" + clickable.Label + "` (not clicked here; way 3 below)");
                Drain(t);
                Flush();

                // ---- 5. way 2: the Fighters window
                H("5. Way 2: the gizmo \"" + UwPay.T("Pit.FightersBtn") + "\"");
                var fightersCmd = OurGizmo(pit, UwPay.T("Pit.FightersBtn"));
                (fightersCmd as Command_Action)?.action?.Invoke();
                yield return 20;
                var win = Find.WindowStack.WindowOfType<Window_PitFighters>();
                t.Check(win != null, "the Fighters gizmo opens the window");
                if (win != null) Hearth.Shell.Tests.UiTestUtil.NoPause(win);
                FightersWindow(t, pit, "as it opens");
                yield return 10;
                Shot("pit_fighters_window");
                yield return 20;
                R("The player ticks the box next to " + col.LabelShortCap + ".");
                pit.SetTicked(col, true); pit.Candidates(true);
                yield return 5;
                FightersWindow(t, pit, "colonist ticked");
                R("The player presses `" + UwPay.T("Pit.Win.Start") + "`:");
                if (pit.ClosedReason() == null && FightBout.OfPit(pit) == null)
                {
                    if (!FightBout.TryStartFor(pit, true, out string why)) Messages.Message(UwPay.T("Pit.CannotStart", why), pit.parent, MessageTypeDefOf.RejectInput, false);
                }
                else R("    (greyed out, nothing happens)");
                Drain(t);
                yield return 10;
                Shot("pit_fighters_window_ticked");
                yield return 20;
                win?.Close(false);
                Inspect(t, marker, "colonist ticked");
                Gizmos(t, marker, "colonist ticked");
                FloatMenu(t, col, pit, "colonist ticked, empty till");
                Flush();

                // ---- 6. way 3: Call an opponent
                H("6. Way 3: \"Call an opponent\"");
                var hireCmd = OurGizmo(pit, PitOpponents.CallLabel);
                R("Gizmo `" + hireCmd?.LabelCap + "`: " + (hireCmd == null ? "missing" : hireCmd.Disabled ? "greyed out, tooltip ends with: " + One(hireCmd.disabledReason) : "clickable") + " | tooltip: " + One(hireCmd?.Desc));
                var tillBefore = UwKit.Till(map);
                t.Check(hireCmd != null && !hireCmd.Disabled && UwKit.Till(map).Micro == 0, "with an empty till and no silver given, Call an opponent is clickable: " + (hireCmd?.disabledReason ?? "ok"));
                if (hireCmd != null && hireCmd.Disabled) R("**Stuck here as a real player:** the gizmo stays greyed out: " + hireCmd.disabledReason);
                tillBefore = UwKit.Till(map);
                var healthBefore = col.health.summaryHealth.SummaryHealthPercent;
                int inj0 = col.health.hediffSet.hediffs.Count(h => h is Hediff_Injury && h.def != PitSetup.Bruise);
                if (hireCmd == null || hireCmd.Disabled) yield break;
                R("The player clicks `" + hireCmd.LabelCap + "`.");
                (hireCmd as Command_Action)?.action?.Invoke();
                yield return 2;
                Drain(t);
                var bout = FightBout.OfPit(pit);
                t.Check(bout != null, "a bout starts after Call an opponent");
                if (bout == null) { yield break; }
                var beast = bout.Other(col);
                R("A beast appears: " + beast?.LabelCap + " (" + beast?.kindDef?.defName + ", combat power " + beast?.kindDef?.combatPower + " vs " + col.LabelShortCap + " " + PitFighters.Power(col).ToString("0") + "), inside the arena: " + (beast != null && pit.ArenaInfo().Cells.Contains(beast.Position)) + ". Till " + tillBefore.ToStringShort() + " -> " + UwKit.Till(map).ToStringShort() + ".");
                R("The bout: scored " + bout.Scored + ", non-lethal " + bout.NonLethal + ", limits " + bout.LimitA.ToString("0") + " / " + bout.LimitB.ToString("0") + ", max ticks " + bout.MaxTicks + ".");
                Inspect(t, marker, "bout announced");
                Gizmos(t, marker, "bout announced");
                FloatMenu(t, col, pit, "bout running");
                Tab(t, map, "bout running");
                Inspect(t, beast, "the beast");
                Flush();
                string lastStatus = null; var lastPhase = (FightBout.Phase)(-1); string lastJob = null;
                int waited = 0;
                while (!bout.Finished && waited < 20000)
                {
                    string st = pit.StatusLine(); string job = col.CurJobDef?.defName;
                    if (st != lastStatus || bout.phase != lastPhase || job != lastJob)
                    {
                        R("- tick +" + waited + ": phase " + bout.phase + ", marker status `" + One(st) + "`, " + col.LabelShortCap + " job " + job + ", points " + bout.ScoreA.ToString("0") + "/" + bout.LimitA.ToString("0") + " vs " + bout.ScoreB.ToString("0") + "/" + bout.LimitB.ToString("0"));
                        lastStatus = st; lastPhase = bout.phase; lastJob = job;
                    }
                    Drain(t);
                    yield return 30; waited += 30;
                }
                Drain(t);
                t.Check(bout.Finished, "the bout ends (" + waited + " ticks)");
                R("Bout over after " + waited + " ticks: winner side " + bout.WinnerSide + ", draw " + bout.Draw + ", points " + bout.ScoreA.ToString("0") + "/" + bout.LimitA.ToString("0") + " vs " + bout.ScoreB.ToString("0") + "/" + bout.LimitB.ToString("0") + ".");
                R("Last message of the bout: " + One(FightBout.LastMessage));
                yield return 120;
                Drain(t);
                int inj1 = col.health.hediffSet.hediffs.Count(h => h is Hediff_Injury && h.def != PitSetup.Bruise);
                R(col.LabelShortCap + ": health " + healthBefore.ToStringPercent() + " -> " + col.health.summaryHealth.SummaryHealthPercent.ToStringPercent() + ", injuries other than bruises " + inj0 + " -> " + inj1 + ", downed " + col.Downed + ". Beast still on the map: " + (beast != null && beast.Spawned) + ". Till now " + UwKit.Till(map).ToStringShort() + ".");
                t.Check(!col.Dead && !col.Downed && inj1 == inj0, "the colonist is not hurt beyond bruises");
                Inspect(t, marker, "after the bout");
                Gizmos(t, marker, "after the bout");
                FloatMenu(t, col, pit, "after the bout");
                Tab(t, map, "after the bout");
                Guide("after the bout");
                Flush();

                // ---- extra: a pet on the map
                H("7. Extra: the same colonist with a pet (the quicktest colony's animals put back)");
                var pets = removed.Where(x => x.p.RaceProps.Animal).ToList();
                if (pets.Count > 0)
                {
                    foreach (var x in pets) { GenSpawn.Spawn(x.p, CellFinder.RandomClosewalkCellNear(col.Position, map, 4), map); removed.Remove(x); }
                    pit.Invalidate();
                    yield return 30;
                    FightersWindow(t, pit, "with the pets back");
                    FloatMenu(t, col, pit, "with the pets back");
                    Inspect(t, marker, "with the pets back");
                    Drain(t);
                }
                else R("The quicktest colony had no animals; nothing to add.");
                Flush();

                // ---- extra: a tab with an open offer (for the screenshot)
                H("8. Extra: the tab with an open offer (a smuggler's dock placed by the suite)");
                var dock = FacKit.Dock(map, t); FacKit.Refresh(map);
                // (no money is given: the offer row then shows what the colony is short of)
                bool fired = Smuggler.Fire(map, force: true);
                yield return 5;
                Drain(t);
                R("Boat fired: " + fired + ".");
                Tab(t, map, "an offer waits");
                HUI.TestScreen = new Vector2(1280f, 720f);
                MainTabWindow_HH.SelectedPage = "underworld";
                Find.MainTabsRoot.SetCurrentTab(tabDef, false);
                yield return 30;
                Shot("uw_tab_offer_open");
                yield return 20;
                Find.MainTabsRoot.EscapeCurrentTab(false);
                HUI.TestScreen = null;
                FacKit.CloseOffers();
                yield return 5;
                Drain(t);
                t.Check(HLog.ErrorCount == errors0, "no errors of ours during the playtest (" + (HLog.ErrorCount - errors0) + ")");
                R("");
                R("Errors of ours during the run: " + (HLog.ErrorCount - errors0) + ".");
            }
            finally
            {
                Flush();
                harmony.UnpatchAll(HarmonyId);
                HUI.TestScreen = null;
                Find.WindowStack.TryRemove(typeof(Window_PitFighters), false);
                if (col != null) foreach (var pit in CompFightPit.All) pit.SetTicked(col, false);
                UwKit.End();
                foreach (var x in removed) if (!x.p.Destroyed && !x.p.Spawned) { try { GenSpawn.Spawn(x.p, x.c.IsValid && x.c.Standable(map) ? x.c : CellFinder.RandomClosewalkCellNear(t.Center, map, 8), map); } catch (Exception e) { Log.Warning("[HHTEST] could not put " + x.p.LabelShort + " back: " + e.Message); } }
                UnderworldPage.Invalidate(); FacilityStatus.MarkDirty();
            }
        }
    }
}
#endif
