// SPDX-License-Identifier: MIT
// In-game test harness. Compiled only with HH_TESTS (Source/build.sh --tests). Never part of a release build.
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace Hearth.Testing
{
    /// <summary>
    /// One in-game test suite. Run() is a coroutine: yield return N waits N game ticks (0 = next tick).
    /// Use t.Check(cond, "what") for assertions; an exception fails the suite. Suites are discovered in all our assemblies.
    /// Start: HH_TEST=all or HH_TEST=suiteA,suiteB  (tools/run_test.sh does this for you).
    /// </summary>
    public abstract class HearthTestSuite
    {
        public abstract string Name { get; }
        /// <summary>Hard tick budget; the suite fails if it runs longer.</summary>
        public virtual int MaxTicks => 30000;
        public virtual ModuleId Module => ModuleId.Core;
        public abstract IEnumerable<int> Run(TestContext t);
        /// <summary>True for suites that need special mods/env (e.g. Hospitality loaded); they run only when named explicitly, never with "all" or a prefix.</summary>
        public virtual bool ExplicitOnly => false;
    }

    public sealed class TestContext
    {
        public Map Map => Find.CurrentMap;
        public string Suite;
        public int Checks, Failures;
        public readonly List<string> Lines = new List<string>();
        public void Check(bool ok, string what)
        {
            Checks++;
            if (!ok) Failures++;
            Log((ok ? "  ok   " : "  FAIL ") + what);
        }
        public void Note(string what) => Log("  note " + what);
        public void Log(string line) { Lines.Add(line); Verse.Log.Message("[HHTEST] " + Suite + ": " + line); }
        public IntVec3 FreeCellNearCenter(int radius = 20)
        {
            var map = Map; var c = map.Center;
            foreach (var cell in GenRadial.RadialCellsAround(c, radius, true))
                if (cell.InBounds(map) && cell.Standable(map) && cell.GetFirstBuilding(map) == null && !cell.Fogged(map)) return cell;
            return c;
        }
    }

    public sealed class HearthTestRunner : GameComponent
    {
        sealed class Run { public HearthTestSuite Suite; public IEnumerator<int> It; public TestContext Ctx; public int Started; public int WaitUntil; public bool Done; public string Error; }
        readonly List<Run> runs = new List<Run>();
        int current = -1, startDelay = 300;
        bool finished, written;
        int errorsAtStart;
        readonly List<string> unknownSuites = new List<string>();
        static string Env(string k) => Environment.GetEnvironmentVariable(k);
        public static bool Active => !string.IsNullOrEmpty(Env("HH_TEST"));

        public HearthTestRunner(Game g) { }

        public override void GameComponentUpdate()
        {
            if (!Active) return;
            var ws = Find.WindowStack;
            foreach (var w in ws.Windows.ToList())
                if (w is Dialog_MessageBox || w is Dialog_NodeTree || (w.forcePause && !(w is Page)))
                { Verse.Log.Message("[HHTEST] closing window " + w.GetType().Name); ws.TryRemove(w, false); }
            if (Find.TickManager.Paused || Find.TickManager.CurTimeSpeed != TimeSpeed.Ultrafast) Find.TickManager.CurTimeSpeed = TimeSpeed.Ultrafast;
            if (finished && !written) { written = true; WriteResults(); LongEventHandler.ExecuteWhenFinished(() => Root.Shutdown()); }
        }

        public override void GameComponentTick()
        {
            if (!Active || finished) return;
            int now = Find.TickManager.TicksGame;
            if (runs.Count == 0 && current == -1)
            {
                if (--startDelay > 0) return;
                var wanted = Env("HH_TEST").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                var all = GenTypes.AllTypes.Where(t => typeof(HearthTestSuite).IsAssignableFrom(t) && !t.IsAbstract)
                    .Select(t => (HearthTestSuite)Activator.CreateInstance(t)).OrderBy(s => s.Name).ToList();
                errorsAtStart = HLog.ErrorCount;
                foreach (var s in all.Where(s => wanted.Contains(s.Name) || (!s.ExplicitOnly && (wanted.Contains("all") || wanted.Any(w => w.EndsWith(".*") && s.Name.StartsWith(w.Substring(0, w.Length - 1)))))))
                    runs.Add(new Run { Suite = s, Ctx = new TestContext { Suite = s.Name } });
                // a misspelled suite name must fail the run, not silently pass with fewer suites
                foreach (var w in wanted) if (w != "all" && !w.EndsWith(".*") && !all.Any(s => s.Name == w)) unknownSuites.Add(w);
                Verse.Log.Message($"[HHTEST] starting {runs.Count} suites: {string.Join(", ", runs.Select(r => r.Suite.Name))} (available: {string.Join(", ", all.Select(s => s.Name))})");
                current = 0;
                if (runs.Count == 0) { finished = true; return; }
            }
            if (current >= runs.Count) { finished = true; return; }
            var r = runs[current];
            if (r.It == null) NeutralizeGame();
            if (r.It == null) { r.Started = now; r.WaitUntil = now; try { r.It = r.Suite.Run(r.Ctx).GetEnumerator(); } catch (Exception e) { r.Error = e.ToString(); r.Done = true; } r.Ctx.Log("start at tick " + now); }
            if (!r.Done && now >= r.WaitUntil)
            {
                try
                {
                    if (r.It.MoveNext()) r.WaitUntil = now + Math.Max(0, r.It.Current);
                    else r.Done = true;
                }
                catch (Exception e) { r.Error = e.ToString(); r.Done = true; }
            }
            if (!r.Done && now - r.Started > r.Suite.MaxTicks) { r.Error = "timeout after " + r.Suite.MaxTicks + " ticks"; r.Done = true; }
            if (r.Done)
            {
                try { r.It?.Dispose(); } catch (Exception e) { r.Ctx.Log("dispose threw: " + e.Message); }
                if (r.Error != null) { r.Ctx.Failures++; r.Ctx.Log("EXCEPTION/TIMEOUT: " + r.Error); }
                r.Ctx.Log($"end: {r.Ctx.Checks} checks, {r.Ctx.Failures} failures, {now - r.Started} ticks");
                current++;
            }
        }

        /// <summary>The quicktest colony is a live game: the storyteller fires raids/fires/events and colonists sleep or take joy at random hours,
        /// which made suites fail by luck. Before every suite: no storyteller incidents, colonists awake, rested and free to work at any hour.</summary>
        /// <summary>Modules hook in here to calm their own background activity before every suite (the guest scheduler stops sending groups).</summary>
        public static event Action BeforeEachSuite;

        static void NeutralizeGame()
        {
            try { BeforeEachSuite?.Invoke(); } catch (Exception e) { Verse.Log.Warning("[HHTEST] BeforeEachSuite hook failed: " + e.Message); }
            try
            {
                Find.Storyteller?.storytellerComps?.Clear();
                var map = Find.CurrentMap; if (map == null) return;
                // outdoor joy (benches, saunas) is refused in rain or snow
                if (map.weatherManager.curWeather != WeatherDefOf.Clear) { map.weatherManager.curWeather = WeatherDefOf.Clear; map.weatherManager.lastWeather = WeatherDefOf.Clear; }
                foreach (var c in map.mapPawns.FreeColonistsSpawned)
                {
                    if (c.needs?.rest != null) c.needs.rest.CurLevel = 1f;
                    var joy = c.needs?.joy;   // joy givers refuse kinds the colonist is bored of (earlier suites exhaust them) and bored colonists flake the 'joy giver offers X' checks
                    if (joy != null)
                    {
                        var set = joy.tolerances; // JoyToleranceSet: DefMaps of tolerance (float) and boredom (bool); reflection keeps this independent of the version
                        foreach (var f in set.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                        {
                            var m = f.GetValue(set); var setAll = m?.GetType().GetMethod("SetAll");
                            if (setAll == null) continue;
                            var vt = setAll.GetParameters()[0].ParameterType;
                            if (vt == typeof(float)) setAll.Invoke(m, new object[] { 0f }); else if (vt == typeof(bool)) setAll.Invoke(m, new object[] { false });
                        }
                    }
                    if (c.timetable != null) for (int h = 0; h < 24; h++) c.timetable.SetAssignment(h, TimeAssignmentDefOf.Anything);
                }
            }
            catch (Exception e) { Verse.Log.Warning("[HHTEST] could not neutralize the game: " + e.Message); }
        }

        void WriteResults()
        {
            var sb = new StringBuilder();
            int fails = runs.Sum(r => r.Ctx.Failures) + unknownSuites.Count, checks = runs.Sum(r => r.Ctx.Checks);
            int newErrors = HLog.ErrorCount - errorsAtStart;
            bool pass = fails == 0 && runs.Count > 0 && newErrors == 0;
            sb.AppendLine($"RESULT {(pass ? "PASS" : "FAIL")} suites={runs.Count} checks={checks} failures={fails} errorsInLog={HLog.ErrorCount} newErrors={newErrors} unknownSuites={unknownSuites.Count}");
            foreach (var u in unknownSuites) sb.AppendLine($"## {u}: FAIL (unknown suite name)");
            if (newErrors > 0) foreach (var l in HLog.RecentSnapshot().Where(l => l.Contains("| ERROR |")).Take(20)) sb.AppendLine("  logged error: " + l.Split('\n')[0]);
            foreach (var r in runs)
            {
                sb.AppendLine($"## {r.Suite.Name}: {(r.Ctx.Failures == 0 ? "PASS" : "FAIL")} ({r.Ctx.Checks} checks, {r.Ctx.Failures} failures)");
                foreach (var l in r.Ctx.Lines) sb.AppendLine(l);
            }
            string outPath = Env("HH_TEST_OUT");
            if (string.IsNullOrEmpty(outPath)) outPath = Path.Combine(GenFilePaths.ConfigFolderPath, "hh_test_results.txt");
            try { File.WriteAllText(outPath, sb.ToString()); } catch (Exception e) { Verse.Log.Error("[HHTEST] could not write results: " + e); }
            Verse.Log.Message("[HHTEST] " + sb.ToString().Split('\n')[0]);
        }
    }

    /// <summary>Smoke test: the core is alive, settings resolve, ledger keeps balance, random streams are reproducible.</summary>
    public sealed class SmokeSuite : HearthTestSuite
    {
        public override string Name => "smoke";
        public override IEnumerable<int> Run(TestContext t)
        {
            t.Check(HearthGame.Instance != null, "HearthGame component exists");
            t.Check(HearthProfile.Current != null, "profile exists");
            t.Check(HSettings.All.Count > 0, "settings registered: " + HSettings.All.Count);
            t.Check(HearthMod.PatchFailures.Count == 0, "all patch classes applied (" + string.Join("; ", HearthMod.PatchFailures) + ")");
            // checks run on a separate ledger instance: the colony's real books stay untouched
            var l = new Ledger();
            t.Check(l.Post("world", "test:a", Money.FromSilver(12.34567), "smoke"), "post world→a");
            t.Check(l.Post("test:a", "test:b", Money.FromSilver(2.5), "smoke"), "post a→b");
            t.Check(!l.Post("test:b", "test:a", Money.FromSilver(100), "smoke"), "overdraft refused");
            t.Check(!l.Post("", "test:b", Money.FromSilver(1), "smoke") && !l.Post("test:a", null, Money.FromSilver(1), "smoke"), "empty account ids refused");
            t.Check(l.SumAll() == 0, "ledger sums to zero");
            int day = Find.TickManager.TicksGame / 60000;
            l.Post("world", "colony:till:99", Money.FromSilver(10), "smoke", false, "smoke.cat");
            l.Post("colony:till:99", "world", Money.FromSilver(10), "flush"); // a till flush must not erase the income
            l.Post("colony:till:99", "world", Money.FromSilver(0), "noop");
            t.Check(l.DayIncome(day, "smoke.cat") == Money.FromSilver(10), "category income survives the till flush: " + l.DayIncome(day, "smoke.cat"));
            t.Check(Money.FromSilver(1.00001).CeilSilver == 2 && Money.FromSilver(1).CeilSilver == 1, "integer coin ceiling");
            t.Check(l.Balance("test:a").ToStringFull() == "9.84567", "five decimals kept: " + l.Balance("test:a").ToStringFull());
            var a = JRand.Sealed("smoke.x"); ulong c0 = a.Counter; double v1 = a.NextDouble();
            var b = new RngStream(a.Seed, a.StreamId, c0); t.Check(Math.Abs(b.NextDouble() - v1) < 1e-15, "sealed stream reproducible");
            foreach (var m in HearthMod.ModuleObjects) t.Note("module " + m.Id + " on=" + Modules.On(m.Id));
            yield return 600;
            t.Check(Find.TickManager.TicksGame > 0, "game ticks");
            t.Check(Guard.Paused.Count() == 0, "no module paused: " + string.Join(", ", Guard.PauseReasons));
        }
    }
}
#endif
