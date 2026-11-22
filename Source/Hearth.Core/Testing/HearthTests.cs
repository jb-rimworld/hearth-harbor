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
                foreach (var s in all.Where(s => wanted.Contains("all") || wanted.Contains(s.Name)))
                    runs.Add(new Run { Suite = s, Ctx = new TestContext { Suite = s.Name } });
                Verse.Log.Message($"[HHTEST] starting {runs.Count} suites: {string.Join(", ", runs.Select(r => r.Suite.Name))} (available: {string.Join(", ", all.Select(s => s.Name))})");
                current = 0;
                if (runs.Count == 0) { finished = true; return; }
            }
            if (current >= runs.Count) { finished = true; return; }
            var r = runs[current];
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
                if (r.Error != null) { r.Ctx.Failures++; r.Ctx.Log("EXCEPTION/TIMEOUT: " + r.Error); }
                r.Ctx.Log($"end: {r.Ctx.Checks} checks, {r.Ctx.Failures} failures, {now - r.Started} ticks");
                current++;
            }
        }

        void WriteResults()
        {
            var sb = new StringBuilder();
            int fails = runs.Sum(r => r.Ctx.Failures), checks = runs.Sum(r => r.Ctx.Checks);
            sb.AppendLine($"RESULT {(fails == 0 && runs.Count > 0 ? "PASS" : "FAIL")} suites={runs.Count} checks={checks} failures={fails} errorsInLog={HLog.ErrorCount}");
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
            var l = Ledger.Current;
            t.Check(l.Post("world", "test:a", Money.FromSilver(12.34567), "smoke"), "post world→a");
            t.Check(l.Post("test:a", "test:b", Money.FromSilver(2.5), "smoke"), "post a→b");
            t.Check(!l.Post("test:b", "test:a", Money.FromSilver(100), "smoke"), "overdraft refused");
            t.Check(l.SumAll() == 0, "ledger sums to zero");
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
