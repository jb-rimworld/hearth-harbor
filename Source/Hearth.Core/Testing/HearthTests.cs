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
using Verse.AI;
using Verse.AI.Group;

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
        static void NeutralizeGame()
        {
            try
            {
                Find.Storyteller?.storytellerComps?.Clear();
                HearthGame.Instance?.HoldTillFlushForTests();
                var map = Find.CurrentMap; if (map == null) return;
                // outdoor joy (benches, saunas) is refused in rain or snow
                if (map.weatherManager.curWeather != WeatherDefOf.Clear) { map.weatherManager.curWeather = WeatherDefOf.Clear; map.weatherManager.lastWeather = WeatherDefOf.Clear; }
                // heat waves, cold snaps, eclipses, toxic fallout … from before the storyteller was switched off also forbid outdoor joy and arrivals
                foreach (var gc in map.gameConditionManager.ActiveConditions.ToList()) { try { gc.End(); } catch (Exception e) { Verse.Log.Warning("[HHTEST] could not end game condition " + gc.def.defName + ": " + e.Message); } }
                // leftovers of earlier suites in this game: visitors, traders and animals of other factions keep reserving benches, tables and shop goods,
                // keep a lord alive that the next suite picks up as 'the' guest, and take the activities a suite wants to see offered to its own guest
                foreach (var p in map.mapPawns.AllPawnsSpawned.ToList())
                {
                    if (p.Faction == Faction.OfPlayer || p.IsPrisonerOfColony || p.IsSlaveOfColony || p.Destroyed) continue;
                    try { p.GetLord()?.Notify_PawnLost(p, PawnLostCondition.ForcedByPlayerAction); p.Destroy(); }
                    catch (Exception e) { Verse.Log.Warning("[HHTEST] could not remove leftover pawn " + p.LabelShort + ": " + e.Message); }
                }
                // a colonist or a pet in a mental break (a manhunting terrier counts as a threat to the colony and stops insurers from signing) is calmed down
                foreach (var mp in map.mapPawns.AllPawnsSpawned.Where(x => x.Faction == Faction.OfPlayer && x.InMentalState).ToList())
                    try { mp.MentalState.RecoverFromState(); } catch (Exception e) { Verse.Log.Warning("[HHTEST] could not end the mental state of " + mp.LabelShort + ": " + e.Message); }
                foreach (var c in map.mapPawns.FreeColonistsSpawned)
                {
                    if (c.needs?.rest != null) c.needs.rest.CurLevel = 1f;
                    if (c.needs?.food != null) c.needs.food.CurLevel = 1f;
                    // nothing left over from the last suite: no drafted pawn, no sleeping pawn, no stale reservation or job
                    if (c.Drafted) c.drafter.Drafted = false;
                    c.jobs?.StopAll(); map.reservationManager.ReleaseAllClaimedBy(c);
                    var joy = c.needs?.joy;   // joy givers refuse kinds the colonist is bored of (earlier suites exhaust them) and bored colonists flake the 'joy giver offers X' checks
                    if (joy != null)
                    {
                        joy.CurLevel = 1f;   // a colonist with a low joy need walks off to use the very bench/table/machine a suite is about to test
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

    /// <summary>
    /// Shared set-up helpers that make suites independent of the random quicktest colony and map (random backstories, terrain, fog, weather).
    /// </summary>
    public static class TestSetup
    {
        /// <summary>A colonist who can take part in recreation tests: adult, up, able to move, talk and manipulate, no royal title that forbids joy kinds.
        /// Falls back to a generated one when the random quicktest colonists do not fit. <paramref name="skip"/> picks the n-th fitting one.</summary>
        public static Pawn Colonist(Map map, int skip = 0, Func<Pawn, bool> extra = null)
        {
            Func<Pawn, bool> ok = p => !p.Dead && !p.Downed && p.Spawned && p.DevelopmentalStage.Adult() && !p.IsSlave && !p.IsPrisoner
                && p.health.capacities.CapableOf(PawnCapacityDefOf.Moving) && p.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) && p.health.capacities.CapableOf(PawnCapacityDefOf.Talking)
                && !HealthAIUtility.ShouldSeekMedicalRest(p) && !p.health.HasHediffsNeedingTend()   // an injured or sick colonist is sent to bed rest, not to a tub or a bench
                && (p.royalty == null || p.royalty.AllTitlesInEffectForReading.Count == 0) && (extra == null || extra(p));
            var list = map.mapPawns.FreeColonistsSpawned.Where(ok).ToList();
            for (int tries = 0; tries < 12 && list.Count <= skip; tries++)
            {
                var req = new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, PawnGenerationContext.NonPlayer, -1, forceGenerateNewPawn: true, mustBeCapableOfViolence: true);
                var np = PawnGenerator.GeneratePawn(req);
                GenSpawn.Spawn(np, CellFinder.RandomClosewalkCellNear(map.Center, map, 6), map);
                if (np.needs?.joy != null) np.needs.joy.CurLevel = 1f;
                if (ok(np)) list.Add(np); else np.Destroy();
            }
            return list.Count > skip ? list[skip] : null;
        }

        /// <summary>Makes <paramref name="rect"/> buildable and walkable whatever the random map put there: no plants, rocks or old buildings, plain floor, no roof, no fog.
        /// Pawns are left alone.</summary>
        public static void PrepareGround(Map map, CellRect rect, TerrainDef floor = null)
        {
            floor = floor ?? TerrainDefOf.Concrete;
            foreach (var c in rect.Cells)
            {
                if (!c.InBounds(map)) continue;
                foreach (var th in c.GetThingList(map).ToList()) if (!(th is Pawn) && th.def.destroyable && !th.Destroyed) th.Destroy();
                map.roofGrid.SetRoof(c, null);
                if (c.GetTerrain(map) != floor) map.terrainGrid.SetTerrain(c, floor);
                map.fogGrid.Unfog(c);
            }
        }

        /// <summary>A reachable spot near <paramref name="around"/> for something of <paramref name="rect"/> size: the first free one if there is one, otherwise the nearest
        /// reachable spot, prepared with <see cref="PrepareGround"/>. Never null on a map with a walkable cell near <paramref name="around"/>.</summary>
        public static IntVec3 FindOrMakeSpot(Map map, IntVec3 around, int w, int h, int ring, bool lightTerrain = true, float minDist = 0f)
        {
            IntVec3 first = IntVec3.Invalid;
            foreach (var c in GenRadial.RadialCellsAround(around, 40, true))
            {
                if (minDist > 0f && (c - around).LengthHorizontal < minDist) continue;   // not on top of the colonist who is going to use it
                var rect = new CellRect(c.x - w / 2, c.z - h / 2, w, h).ExpandedBy(ring);
                if (!rect.FullyContainedWithin(CellRect.WholeMap(map).ContractedBy(2))) continue;
                if (!map.reachability.CanReach(around, c, PathEndMode.OnCell, TraverseMode.PassDoors, Danger.Deadly)) continue;
                if (!first.IsValid && rect.Cells.All(x => x.GetFirstBuilding(map) == null)) first = c;   // never clear away a building a suite has just placed
                if (rect.Cells.All(x => x.Standable(map) && x.GetFirstBuilding(map) == null && !x.Fogged(map) && (!lightTerrain || x.GetTerrain(map).affordances.Contains(TerrainAffordanceDefOf.Light)))) return c;
            }
            var spot = first.IsValid ? first : around;
            PrepareGround(map, new CellRect(spot.x - w / 2, spot.z - h / 2, w, h).ExpandedBy(ring));
            return spot;
        }

        /// <summary>Why a colonist is not offered an 'interact with building' joy (bench, table, machine)? One line for the failure note of a check;
        /// walks through everything the vanilla joy giver looks at.</summary>
        public static string WhyNoJoy(Pawn pawn, Thing t)
        {
            if (pawn == null || t == null) return "pawn or thing missing";
            var map = pawn.Map; var sb = new StringBuilder();
            sb.Append(pawn.LabelShort).Append(": spawned ").Append(pawn.Spawned).Append(", downed ").Append(pawn.Downed).Append(", drafted ").Append(pawn.Drafted)
              .Append(", job ").Append(pawn.CurJob?.def.defName ?? "none").Append(", joy ").Append(pawn.needs?.joy?.CurLevel.ToString("0.00"));
            sb.Append("; thing ").Append(t.def.defName).Append(" spawned ").Append(t.Spawned).Append(" at ").Append(t.Position);
            if (!t.Spawned || map == null) return sb.ToString();
            sb.Append(", pawn at ").Append(pawn.Position).Append(", reserve ").Append(pawn.CanReserve(t, 1)).Append(" (by ").Append(map.reservationManager.FirstRespectedReserver(t, pawn)?.LabelShort ?? "nobody").Append(")")
              .Append(", forbidden ").Append(t.IsForbidden(pawn)).Append(", fogged ").Append(t.Fogged()).Append(", socially proper ").Append(t.IsSociallyProper(pawn))
              .Append(", roofed ").Append(t.Position.Roofed(map)).Append(", reach ").Append(pawn.CanReach(t, PathEndMode.OnCell, Danger.Deadly))
              .Append(", interaction cell standable ").Append(!t.def.hasInteractionCell || t.InteractionCell.Standable(map))
              .Append("; outdoor temp ").Append(map.mapTemperature.OutdoorTemp.ToString("0")).Append(", weather ").Append(map.weatherManager.curWeather.defName)
              .Append(", conditions ").Append(map.gameConditionManager.ActiveConditions.Count);
            return sb.ToString();
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
