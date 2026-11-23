// SPDX-License-Identifier: MIT
// Long run: builds the showcase hotel, then plays 15 in-game days at maximum speed with normal guest arrivals and working colonists.
// Logs one row per day (guests, nights, income, renown, ledger, errors, TPS …), checks the invariants at the end and writes a CSV + markdown
// report next to HH_TEST_OUT. Not a normal test (ExplicitOnly): HH_TEST=longrun, see tools/longrun/run_longrun.sh. Compiled only with HH_TESTS.
// Env: HH_LONGRUN_DAYS=<n> shortens the run (default 15) for quick tries.
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using RimWorld;
using Verse;
using Verse.AI;
using Hearth.Casino;
using Hearth.Guests;
using Hearth.Kitchen;
using Hearth.Testing;
using Silverline;

namespace Hearth.Showcase
{
    public sealed class LongRunSuite : HearthTestSuite
    {
        public override string Name => "longrun";
        public override bool ExplicitOnly => true;
        public override int MaxTicks => 1400000;

        const int Poll = 60;           // ticks between two samples
        const int Upkeep = 2500;       // ticks between two upkeep rounds (refuel, food)
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        sealed class Seen { public int Worst; public int First, Last, Nights, NightsPlanned, PlannedLeave; public StayKind Kind; public string Name; public bool Gone; public GuestPhase Phase; public int LeavingSince = -1; }

        sealed class Row
        {
            public int Day;               // 0 = build day (partial), 1..N = full days
            public int LedgerDay;
            public int Present, Arrived, Left, Nights, DishesCooked, DishesSold, Claims, Pawns, Colonists, Errors, Warns;
            public double Till, Bank, Income, Renown, Whispers, Satisfaction, CasinoHouse, Premiums, Benefits, SalesTakings, Ledger, Tps;
            public int SatSamples;
            public string IncomeByCat = "";
            public Dictionary<string, double> Cats = new Dictionary<string, double>();
        }

        Map map; TestContext T; ShowcaseBuilder b;
        readonly List<Row> rows = new List<Row>();
        readonly Dictionary<int, Seen> seen = new Dictionary<int, Seen>();
        readonly HashSet<int> platesSeen = new HashSet<int>();
        readonly List<string> problems = new List<string>();      // first occurrences of invariant breaks
        readonly List<string> upkeepLog = new List<string>();
        int refuels, foodAssists, rawTopUps, returns;
        readonly Dictionary<int, string> stuckSeen = new Dictionary<int, string>();
        bool reportWritten;
        string firstPausedAt, firstImbalanceAt;
        int errorsAtStart;

        // per-day accumulators
        int dArrived, dLeft, dNights, dCooked, dSatN; double dSat;
        int salesAtDayStart, claimsAtDayStart, errAtDayStart, warnAtDayStart; double takingsAtDayStart, premAtDayStart, benAtDayStart;
        long tsAtDayStart; int tickAtDayStart;

        static int Days
        {
            get { int n; return int.TryParse(Environment.GetEnvironmentVariable("HH_LONGRUN_DAYS"), out n) && n > 0 && n <= 60 ? n : 15; }
        }

        static double Ins(Func<InsuranceComponent, double> f)
        {
            var c = InsuranceComponent.Instance; if (c == null) return 0; return f(c);
        }

        // ------------------------------------------------------------------------------------------------- run
        public override IEnumerable<int> Run(TestContext t)
        {
            T = t; map = t.Map; b = new ShowcaseBuilder();
            errorsAtStart = HLog.ErrorCount;
            int days = Days;
            try
            {
                MapComponent_HH_GuestScheduler.SuspendedForTests = false;
                FinishResearch();
                yield return 5;
                foreach (var w in b.Build(t, true, true)) yield return w;
                DisableThreats();
                ReachCheck();
                AddBills();
                yield return 30;

                // --- day 1: the only forced guests, so the inn is not empty while the scheduler warms up (first natural guests: FirstGuestMinDays)
                var f = InnTestKit.GuestFaction(map);
                t.Check(f != null, "friendly faction for guests: " + f?.Name);
                int forced = 0;
                if (f != null)
                    foreach (var (size, kind, nights, silver) in new[] { (2, StayKind.Overnight, 2, 700), (3, StayKind.DayVisitor, 1, 500) })
                    {
                        var g = GuestArrival.TryFire(map, new GuestArrivalRequest { Faction = f, Size = size, Kind = kind, Nights = nights, ForcePod = true, ForceBudget = Money.FromSilver(silver), Silent = true });
                        if (g != null) forced++;
                        yield return 30;
                    }
                t.Note("forced guest groups on day 0: " + forced + " (no forced guests after this)");

                int start = Find.TickManager.TicksGame;
                int nextBoundary = (start / 60000 + 1) * 60000;
                BeginDay(start);
                t.Note($"start tick {start}, first day boundary {nextBoundary}, running {days} full days, colonists {b.Cols.Count}, map {map.Size}");
                int nextUpkeep = start + Upkeep, polls = 0;
                string lastCpu = null;
                while (rows.Count <= days)
                {
                    yield return Poll;
                    int now = Find.TickManager.TicksGame;
                    SamplePoll(now, ++polls);
                    if (now >= nextUpkeep) { nextUpkeep = now + Upkeep; DoUpkeep(now); }
                    if (now >= nextBoundary)
                    {
                        var row = CloseDay(now, nextBoundary / 60000 - 1);
                        rows.Add(row);
                        t.Note(DayLine(row));
                        WriteFiles(false);
                        nextBoundary += 60000;
                        BeginDay(now);
                    }
                    lastCpu = null;
                }
            }
            finally
            {
                // runs on completion, exception and timeout (the runner disposes the coroutine)
                if (!reportWritten) { try { Finish(); } catch (Exception e) { T.Log("report failed: " + e); } }
            }
        }

        // ------------------------------------------------------------------------------------------------- setup helpers
        void FinishResearch()
        {
            int n = 0;
            try
            {
                foreach (var d in DefDatabase<ResearchProjectDef>.AllDefsListForReading.Where(x => x.defName.StartsWith("HH_") || x.defName.StartsWith("SL_") || x.tab != null && (x.tab.defName == "HH_Hospitality" || x.tab.defName == "HH_Banking")).ToList())
                    if (!d.IsFinished) { Find.ResearchManager.FinishProject(d, false, null, false); n++; }
            }
            catch (Exception e) { T.Note("research: " + e.Message); }
            T.Note("research of the mod finished up front (hospitality + banking tabs): " + n + " projects (the run measures the finished hotel, not the research path)");
        }

        void ReachCheck()
        {
            try
            {
                var from = b.P(20, -3);   // the plaza in front of the entrance
                var parms = TraverseParms.For(TraverseMode.PassDoors, Danger.Deadly, false);
                foreach (var (name, x, z) in new[] { ("entrance door", 20, 0), ("lobby", 20, 6), ("restaurant", 7, 5), ("restaurant stations row", 7, 9), ("casino", 34, 5), ("corridor", 20, 12), ("room 1", 3, 17), ("room 2", 9, 17), ("room 3", 15, 17), ("spa", 24, 17), ("bank", 35, 17) })
                {
                    var c = b.P(x, z);
                    bool ok = map.reachability.CanReach(from, c, PathEndMode.OnCell, parms);
                    T.Check(ok, $"{name} reachable from the plaza ({c}, standable={c.Standable(map)})");
                }
                foreach (var th in map.listerBuildings.allBuildingsColonist.Where(x => x.def.defName.StartsWith("HH_Station_") || x.def.defName == "HH_ShopCounter" || x.def.defName == "HH_FrontDesk").Take(14))
                {
                    var ic = th.InteractionCell;
                    bool ok = map.reachability.CanReach(from, ic, PathEndMode.OnCell, parms);
                    T.Check(ok, $"{th.def.defName} at {th.Position} rot {th.Rotation}: interaction cell {ic} reachable (standable={ic.Standable(map)}, blockers=[{string.Join(",", ic.GetThingList(map).Select(q => q.def.defName))}])");
                }
            }
            catch (Exception e) { T.Note("reach check: " + e.Message); }
        }

        void DisableThreats()
        {
            try { Find.Storyteller.difficulty.threatScale = 0f; } catch (Exception e) { T.Note("threatScale: " + e.Message); }
            int n = 0;
            try
            {
                // threatScale 0 alone does not stop raids or mad animals: switch every threat incident off (this process only, the test profile is isolated)
                foreach (var d in DefDatabase<IncidentDef>.AllDefsListForReading)
                    if (d.category == IncidentCategoryDefOf.ThreatBig || d.category == IncidentCategoryDefOf.ThreatSmall || d.category == IncidentCategoryDefOf.DeepDrillInfestation || d.defName.StartsWith("Infestation") || d.defName.Contains("Manhunter") || d.defName.Contains("Revenge"))
                    { if (d.baseChance > 0f || d.baseChanceWithRoyalty > 0f) n++; d.baseChance = 0f; d.baseChanceWithRoyalty = 0f; }
            }
            catch (Exception e) { T.Note("threat incidents: " + e.Message); }
            T.Note("raids, mad animals and other threat incidents switched off (threatScale 0, " + n + " incident defs zeroed) so the run measures the hotel, not defence");
        }

        static readonly string[] StockNames = { "RawPotatoes", "RawRice", "RawCorn", "RawBerries", "Meat_Muffalo", "Meat_Boomalope", "Milk", "EggChickenUnfertilized", "RawAgave" };

        void AddBills()
        {
            int added = 0;
            try
            {
                foreach (var th in map.listerBuildings.allBuildingsColonist.OfType<Building_WorkTable>().Where(x => x.def.defName.StartsWith("HH_Station_")).ToList())
                {
                    int n = 0;
                    if (!th.InteractionCell.Standable(map)) { T.Note("skipping " + th.def.defName + ": interaction cell blocked"); continue; }
                    foreach (var r in th.def.AllRecipes)
                    {
                        if (n >= 2) break;
                        if (!r.defName.StartsWith("HH_Cook_") || r.defName.EndsWith("_Fine") || r.defName.EndsWith("_Signature") || !r.AvailableNow) continue;
                        // only dishes whose every ingredient class matches something the pantry restock provides (otherwise the bill just waits)
                        if (r.ingredients.Any(ing => !ing.filter.AllowedThingDefs.Any(d => StockNames.Contains(d.defName)))) continue;
                        var bill = (Bill_Production)r.MakeNewBill();
                        bill.repeatMode = BillRepeatModeDefOf.TargetCount; bill.targetCount = 3;
                        try { bill.SetStoreMode(BillStoreModeDefOf.DropOnFloor); } catch { }
                        th.BillStack.AddBill(bill); n++; added++;
                    }
                }
            }
            catch (Exception e) { T.Note("bills: " + e.Message); }
            foreach (var p in map.mapPawns.FreeColonistsSpawned)
            {
                var cook = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Cooking");
                T.Note($"cook check {p.LabelShort}: Cooking disabled={p.WorkTypeIsDisabled(cook)} priority={p.workSettings.GetPriority(cook)} skill={p.skills.GetSkill(SkillDefOf.Cooking).Level} manhunter/incapable={p.Downed}");
            }
            T.Note("kitchen bills added: " + added + " (do-until 3 plates, Everyday quality)");
            T.Check(added > 0, "at least one dish bill could be set up");
        }

        // ------------------------------------------------------------------------------------------------- upkeep
        void DoUpkeep(int now)
        {
            try
            {
                foreach (var th in b.fuelled)
                    if (th.Spawned) { var rf = th.TryGetComp<CompRefuelable>(); if (rf != null && rf.FuelPercentOfMax < 0.4f) { rf.Refuel(rf.Props.fuelCapacity); refuels++; } }
                // colonists' own food: top up the pantry when it is nearly empty (the run is about the hotel, not about farming)
                float nut = 0f;
                foreach (var th in map.listerThings.ThingsInGroup(ThingRequestGroup.FoodSourceNotPlantOrTree))
                    if (th.def.IsNutritionGivingIngestible && th.TryGetComp<CompDish>() == null && !th.def.IsDrug) nut += th.stackCount * th.GetStatValue(StatDefOf.Nutrition);
                if (nut < 12f)
                {
                    var at = b.P(7, 6);
                    foreach (var (name, count) in new[] { ("MealSimple", 12), ("RawPotatoes", 75), ("RawRice", 75), ("Meat_Muffalo", 75), ("RawCorn", 75), ("RawBerries", 75), ("Milk", 50), ("EggChickenUnfertilized", 40) })
                    {
                        var d = DefDatabase<ThingDef>.GetNamedSilentFail(name); if (d == null) continue;
                        var th = ThingMaker.MakeThing(d); th.stackCount = Math.Min(count, d.stackLimit);
                        GenPlace.TryPlaceThing(th, at, map, ThingPlaceMode.Near);
                    }
                    rawTopUps++;
                    upkeepLog.Add($"tick {now}: pantry had {nut:0.0} nutrition, restocked (meals + raw food)");
                }
                foreach (var p in map.mapPawns.FreeColonistsSpawned)
                    if (p.needs?.food != null && p.needs.food.CurLevelPercentage < 0.05f)
                    {
                        p.needs.food.CurLevelPercentage = 0.6f; foodAssists++;
                        upkeepLog.Add($"tick {now}: {p.LabelShort} was starving, fed once");
                    }
            }
            catch (Exception e) { T.Note("upkeep: " + e.Message); }
            try
            {
                // the showcase hotel has no cooling or heating: keep the staff alive in summer/winter (the run is about the hotel, not climate control)
                foreach (var p in map.mapPawns.FreeColonistsSpawned)
                    foreach (var h in p.health.hediffSet.hediffs.Where(h => h.def == HediffDefOf.Heatstroke || h.def == HediffDefOf.Hypothermia).ToList())
                        if (h.Severity > 0.2f) { h.Severity = 0.05f; heatAssists++; }
            }
            catch { }
            BillDiag(now);
            DiagColonists(now);
            try
            {
                foreach (var l in Find.LetterStack.LettersListForReading.ToList())
                {
                    string lab = l.Label.ToString();
                    if (letterLog.Count < 60 && !lab.Contains("arrive")) letterLog.Add($"tick {now}: letter [{l.def.defName}] {lab}");
                    Find.LetterStack.RemoveLetter(l);
                }
                Messages.Clear();
            }
            catch { }
            ThreatWatch(now);
        }

        int threatsRemoved, heatAssists;
        readonly List<string> letterLog = new List<string>();
        readonly HashSet<string> threatKinds = new HashSet<string>();
        void ThreatWatch(int now)
        {
            try
            {
                foreach (var p in map.mapPawns.AllPawnsSpawned)
                {
                    if (p.IsColonist || GuestApi.IsGuest(p)) continue;
                    bool hostile = p.HostileTo(Faction.OfPlayer) || (p.MentalStateDef != null && p.MentalStateDef.defName.StartsWith("Manhunter"));
                    if (!hostile) continue;
                    string key = (p.Faction?.Name ?? "wild") + "/" + p.kindDef.defName + (p.MentalStateDef != null ? "/" + p.MentalStateDef.defName : "");
                    if (threatKinds.Add(key)) letterLog.Add($"tick {now}: hostile on the map (removed): {key} (e.g. {p.LabelShort})");
                    threatsRemoved++;
                    try { p.Destroy(); } catch { }
                }
            }
            catch { }
        }

        static WorkTypeDef CookWT => DefDatabase<WorkTypeDef>.GetNamed("Cooking");
        int billDiagLines, wgLines;
        void BillDiag(int now)
        {
            if (billDiagLines >= 14 || (now / Upkeep) % 6 != 0) return;
            try
            {
                var ws = map.listerBuildings.allBuildingsColonist.OfType<Building_WorkTable>().Where(x => x.def.defName.StartsWith("HH_Station_") && x.BillStack.Count > 0).ToList();
                foreach (var th in ws.Take(3))
                {
                    var bill = th.BillStack.Bills[0] as Bill_Production;
                    if (bill == null) continue;
                    int have = bill.recipe.WorkerCounter.CountProducts(bill);
                    T.Note($"bill diag @{now}: {th.def.defName} '{bill.recipe.defName}' shouldNow={bill.ShouldDoNow()} suspended={bill.suspended} products={have}/{bill.targetCount} powered={th.TryGetComp<CompPowerTrader>()?.PowerOn} usable={th.CanWorkWithoutPower}");
                    billDiagLines++;
                    var wgd = DefDatabase<WorkGiverDef>.GetNamedSilentFail("HH_DoBillsKitchen");
                    if (wgLines < 6 && wgd?.Worker is WorkGiver_Scanner wg)
                        foreach (var cookP in map.mapPawns.FreeColonistsSpawned.Take(3))
                        {
                            string why = "";
                            try { var job = wg.JobOnThing(cookP, th, false); why = job == null ? "no job" : "JOB " + job.def.defName; if (job == null) why += $" (has={wg.HasJobOnThing(cookP, th, false)}, forbidden={th.IsForbidden(cookP)}, reach={cookP.CanReach(th, PathEndMode.InteractionCell, Danger.Some)}, reserve={cookP.CanReserve(th)}, cookDisabled={cookP.WorkTypeIsDisabled(CookWT)}, prio={cookP.workSettings.GetPriority(CookWT)}, curjob={cookP.CurJob?.def.defName})"; }
                            catch (Exception e) { why = "threw " + e.Message; }
                            wgLines++;
                            T.Note($"  workgiver {th.def.defName} for {cookP.LabelShort}: {why}");
                        }
                }
            }
            catch (Exception e) { T.Note("bill diag: " + e.Message); billDiagLines += 5; }
        }

        readonly Dictionary<int, string> lastDiag = new Dictionary<int, string>();
        int diagLines, battleNotes;
        /// <summary>Logs a colonist whenever its state changes in a way that matters (dead, downed, mental break, hunger, tired, new serious hediff).</summary>
        void DiagColonists(int now)
        {
            try
            {
                foreach (var p in b.Cols)
                {
                    string st;
                    if (p.Dead) st = "DEAD: " + string.Join(", ", p.health.hediffSet.hediffs.Select(h => h.def.defName + " " + h.Severity.ToString("0.00", Inv)));
                    else
                    {
                        string hs = string.Join(",", p.health.hediffSet.hediffs.Where(h => h.Visible && (h.def.isBad || h.Severity > 0.3f)).Select(h => h.def.defName + ":" + h.Severity.ToString("0.0", Inv)).Take(6));
                        float food = p.needs?.food?.CurLevelPercentage ?? -1f, rest = p.needs?.rest?.CurLevelPercentage ?? -1f;
                        st = $"spawned={p.Spawned} downed={p.Downed} mental={p.InMentalState} starving={food < 0.05f} exhausted={rest < 0.05f} hediffs=[{hs}] hp={p.health.summaryHealth.SummaryHealthPercent:0.00}";
                    }
                    if (lastDiag.TryGetValue(p.thingIDNumber, out var old) && old == st) continue;
                    lastDiag[p.thingIDNumber] = st;
                    if (diagLines++ < 40) T.Note($"colonist {p.LabelShort} @{now}: {st}");
                    if (!p.Dead && p.health.summaryHealth.SummaryHealthPercent < 0.9f && battleNotes++ < 12)
                    {
                        try
                        {
                            var bat = Find.BattleLog.Battles.OrderByDescending(x => x.CreationTimestamp).FirstOrDefault(x => x.Concerns(p));
                            var ents = bat?.Entries.Where(e => e.Concerns(p)).Reverse().Take(3).Select(e => e.ToGameStringFromPOV(p, false)).ToList();
                            if (ents != null && ents.Count > 0) T.Note($"  damage to {p.LabelShort}: " + string.Join(" | ", ents));
                        }
                        catch (Exception e) { T.Note("battle log: " + e.Message); }
                    }
                }
            }
            catch (Exception e) { T.Note("diag: " + e.Message); }
        }

        // ------------------------------------------------------------------------------------------------- sampling
        void BeginDay(int now)
        {
            dArrived = dLeft = dNights = dCooked = dSatN = 0; dSat = 0;
            salesAtDayStart = Restaurant.TotalSales; takingsAtDayStart = Restaurant.TotalTakings.Silver;
            claimsAtDayStart = InsuranceComponent.Instance?.claims.Count ?? 0;
            premAtDayStart = Ins(c => c.contracts.Sum(x => x.premiumPaid)); benAtDayStart = Ins(c => c.contracts.Sum(x => x.benefitPaid));
            errAtDayStart = HLog.ErrorCount; warnAtDayStart = HLog.WarnCount;
            tsAtDayStart = Stopwatch.GetTimestamp(); tickAtDayStart = now;
        }

        void SamplePoll(int now, int poll)
        {
            var reg = GuestRegistry.Instance;
            if (reg != null)
            {
                var present = new HashSet<int>();
                foreach (var r in reg.OnMap(map).ToList())
                {
                    present.Add(r.PawnId);
                    if (!seen.TryGetValue(r.PawnId, out var s))
                    {
                        seen[r.PawnId] = s = new Seen { First = now, Kind = r.Kind, Name = r.Pawn?.LabelShort ?? ("#" + r.PawnId) };
                        dArrived++;
                    }
                    if (s.Gone) { s.Gone = false; s.First = now; s.Nights = 0; s.LeavingSince = -1; dArrived++; returns++; }   // same pawn, new visit (return visit)
                    s.Last = now; s.Kind = r.Kind; s.NightsPlanned = r.NightsPlanned; s.PlannedLeave = r.PlannedLeaveTick; s.Phase = r.Phase;
                    if (r.Phase == GuestPhase.Leaving && s.LeavingSince < 0) s.LeavingSince = now;
                    int stayNow = now - s.First, lim = Math.Max(3, s.NightsPlanned + 1) * 60000;
                    if (s.Kind != StayKind.Lodger && stayNow > lim && stayNow > s.Worst) { s.Worst = stayNow; stuckSeen[r.PawnId] = $"{s.Name} ({s.Kind}, planned {s.NightsPlanned} nights, phase {s.Phase}, checked in {r.CheckedIn}) present for {stayNow / 60000f:0.0} days"; }
                    if (r.NightsPaid > s.Nights) { dNights += r.NightsPaid - s.Nights; s.Nights = r.NightsPaid; }
                    if (r.CheckedIn || r.Kind != StayKind.Overnight) { dSat += r.Satisfaction; dSatN++; }
                }
                foreach (var kv in seen) if (!kv.Value.Gone && !present.Contains(kv.Key)) { kv.Value.Gone = true; dLeft++; }
            }
            if (poll % 2 == 0)
            {
                try
                {
                    foreach (var d in DefDatabase<DishDef>.AllDefsListForReading.Select(x => x.plateDef).Where(x => x != null).Distinct())
                        foreach (var th in map.listerThings.ThingsOfDef(d))
                            if (th.TryGetComp<CompDish>()?.dish != null && platesSeen.Add(th.thingIDNumber)) dCooked++;
                }
                catch (Exception e) { HLog.Once("longrun.plates", LogLevel.Warn, "LongRun", "plate sampling: " + e.Message); }
            }
            var led = Ledger.Current;
            if (led != null && led.SumAll() != 0 && firstImbalanceAt == null)
            { firstImbalanceAt = "tick " + now + " sum " + new Money(led.SumAll()).ToStringFull(); problems.Add("ledger imbalance at " + firstImbalanceAt); T.Note("PROBLEM ledger imbalance at " + firstImbalanceAt); }
            if (Guard.Paused.Any() && firstPausedAt == null)
            { firstPausedAt = "tick " + now + ": " + string.Join("; ", Guard.PauseReasons); problems.Add("module paused at " + firstPausedAt); T.Note("PROBLEM module paused at " + firstPausedAt); }
        }

        Row CloseDay(int now, int ledgerDay)
        {
            var led = Ledger.Current;
            var row = new Row { Day = rows.Count, LedgerDay = ledgerDay };
            var reg = GuestRegistry.Instance;
            row.Present = reg != null ? reg.OnMap(map).Count() : 0;
            row.Arrived = dArrived; row.Left = dLeft; row.Nights = dNights; row.DishesCooked = dCooked;
            row.Till = led.Balance(Payments.ColonyTill(map)).Silver;
            row.Bank = led.Balance(BankGameComp.Colony).Silver;
            row.Income = led.DayIncomeTotal(ledgerDay).Silver;
            foreach (var cat in Categories(led, ledgerDay)) { double v = led.DayIncome(ledgerDay, cat).Silver; row.Cats[cat] = v; }
            row.IncomeByCat = string.Join("; ", row.Cats.OrderByDescending(k => k.Value).Select(k => k.Key + "=" + k.Value.ToString("0.0", Inv)));
            row.Renown = reg?.Renown ?? 0; row.Whispers = reg?.CurrentWhispers ?? 0;
            row.Satisfaction = dSatN > 0 ? dSat / dSatN : double.NaN; row.SatSamples = dSatN;
            row.CasinoHouse = CasinoComponent.Instance != null ? CasinoComponent.Instance.RowsOn(ledgerDay).Sum(r => r.HouseWin) : 0;
            row.DishesSold = Restaurant.TotalSales - salesAtDayStart; row.SalesTakings = Restaurant.TotalTakings.Silver - takingsAtDayStart;
            row.Premiums = Ins(c => c.contracts.Sum(x => x.premiumPaid)) - premAtDayStart;
            row.Benefits = Ins(c => c.contracts.Sum(x => x.benefitPaid)) - benAtDayStart;
            row.Claims = (InsuranceComponent.Instance?.claims.Count ?? 0) - claimsAtDayStart;
            row.Ledger = new Money(led.SumAll()).Silver;
            row.Errors = HLog.ErrorCount - errAtDayStart; row.Warns = HLog.WarnCount - warnAtDayStart;
            double secs = (Stopwatch.GetTimestamp() - tsAtDayStart) / (double)Stopwatch.Frequency;
            row.Tps = secs > 0.01 ? (now - tickAtDayStart) / secs : 0;
            row.Pawns = map.mapPawns.AllPawnsSpawnedCount; row.Colonists = map.mapPawns.FreeColonistsSpawnedCount;
            return row;
        }

        static IEnumerable<string> Categories(Ledger led, int day)
        {
            Dictionary<int, Dictionary<string, long>> daily = null;
            try { daily = (Dictionary<int, Dictionary<string, long>>)typeof(Ledger).GetField("daily", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(led); } catch { }
            if (daily == null || !daily.TryGetValue(day, out var d)) yield break;
            foreach (var k in d.Keys.ToList()) if (k.StartsWith("income:", StringComparison.Ordinal)) yield return k.Substring(7);
        }

        string DayLine(Row r) =>
            $"day {r.Day} (ledger day {r.LedgerDay}): guests present {r.Present} arrived {r.Arrived} left {r.Left}, nights {r.Nights}, till {r.Till:0.0} bank {r.Bank:0.0}, income {r.Income:0.0} [{r.IncomeByCat}], " +
            $"renown {r.Renown:0.0} whispers {r.Whispers:0.0}, satisfaction {(double.IsNaN(r.Satisfaction) ? "-" : r.Satisfaction.ToString("0.00", Inv))} ({r.SatSamples}), casino house {r.CasinoHouse:0.0}, " +
            $"dishes cooked {r.DishesCooked} sold {r.DishesSold} ({r.SalesTakings:0.0}), insurance premium {r.Premiums:0.0} claims {r.Claims} benefit {r.Benefits:0.0}, " +
            $"ledger {r.Ledger:0.#####}, errors {r.Errors} warns {r.Warns}, TPS {r.Tps:0}, pawns {r.Pawns} (colonists {r.Colonists})";

        // ------------------------------------------------------------------------------------------------- end
        void Finish()
        {
            reportWritten = true;
            var full = rows.Where(r => r.Day >= 1).ToList();
            T.Check(full.Count >= Days || Days != 15 || false, "all days played: " + full.Count + "/" + Days);
            var led = Ledger.Current;
            T.Check(led != null && led.SumAll() == 0 && firstImbalanceAt == null, "ledger sums to zero all the time" + (firstImbalanceAt != null ? " (broke at " + firstImbalanceAt + ")" : ""));
            int errs = HLog.ErrorCount - errorsAtStart;
            T.Check(errs == 0, "no HLog errors during the run: " + errs);
            if (errs > 0) foreach (var l in HLog.RecentSnapshot().Where(l => l.Contains("| ERROR |")).Take(10)) T.Note("logged error: " + l.Split('\n')[0]);
            int arrivalDays = full.Count(r => r.Arrived > 0);
            T.Check(arrivalDays >= Math.Min(5, Math.Max(1, Days / 3)), "guests arrived on at least 5 days (shortened runs: a third): " + arrivalDays + " of " + full.Count);
            double total = rows.Sum(r => r.Income);
            T.Check(total > 0, "income > 0: " + total.ToString("0.0", Inv) + " silver over " + rows.Count + " ledger days");
            var stuck = stuckSeen.Values.ToList();
            int end = Find.TickManager.TicksGame;
            foreach (var s in seen.Values)
                if (s.Kind != StayKind.Lodger && !s.Gone && s.LeavingSince >= 0 && end - s.LeavingSince > 60000) stuck.Add($"{s.Name} has been 'leaving' for {(end - s.LeavingSince) / 60000f:0.0} days");
            T.Check(stuck.Count == 0, "no guest stuck for more than 3 days: " + (stuck.Count == 0 ? "ok (" + seen.Count + " guests seen)" : string.Join("; ", stuck)));
            T.Check(Guard.Paused.Count() == 0 && firstPausedAt == null, "no module paused by Guard" + (firstPausedAt != null ? " (paused at " + firstPausedAt + ")" : ""));
            int colAlive = map.mapPawns.FreeColonistsSpawnedCount;
            T.Note($"return visits of known guests: {returns}; threat pawns removed: {threatsRemoved}; heat/cold assists: {heatAssists}");
            T.Note($"colonists alive at the end: {colAlive}/{b.Cols.Count}; upkeep: {refuels} generator refuels, {rawTopUps} pantry restocks, {foodAssists} starvation feeds");
            foreach (var u in upkeepLog.Take(12)) T.Note(u);
            foreach (var u in letterLog) T.Note("incident: " + u);
            WriteFiles(true);
        }

        // ------------------------------------------------------------------------------------------------- files
        static string OutBase()
        {
            string o = Environment.GetEnvironmentVariable("HH_TEST_OUT");
            string dir = string.IsNullOrEmpty(o) ? GenFilePaths.ConfigFolderPath : (Path.GetDirectoryName(o) ?? GenFilePaths.ConfigFolderPath);
            string name = string.IsNullOrEmpty(o) ? "result_longrun" : Path.GetFileNameWithoutExtension(o);
            return Path.Combine(dir, "longrun_" + (name.StartsWith("result_") ? name.Substring(7) : name));
        }

        void WriteFiles(bool final)
        {
            try
            {
                string bs = OutBase();
                var cats = rows.SelectMany(r => r.Cats.Keys).Distinct().OrderBy(x => x).ToList();
                var csv = new StringBuilder();
                csv.AppendLine("day,ledger_day,guests_present,guests_arrived,guests_left,nights_sold,till_silver,bank_silver,income_silver,renown,whispers,avg_satisfaction,casino_house_result,dishes_cooked,dishes_sold,dish_takings,insurance_premiums,insurance_claims,insurance_benefit,ledger_sum,hlog_errors,hlog_warnings,tps,pawns,colonists" + string.Concat(cats.Select(c => ",income_" + c)));
                foreach (var r in rows)
                    csv.AppendLine(string.Join(",", new[] {
                        r.Day.ToString(), r.LedgerDay.ToString(), r.Present.ToString(), r.Arrived.ToString(), r.Left.ToString(), r.Nights.ToString(), F(r.Till), F(r.Bank), F(r.Income), F(r.Renown), F(r.Whispers),
                        double.IsNaN(r.Satisfaction) ? "" : r.Satisfaction.ToString("0.000", Inv), F(r.CasinoHouse), r.DishesCooked.ToString(), r.DishesSold.ToString(), F(r.SalesTakings), F(r.Premiums), r.Claims.ToString(), F(r.Benefits),
                        r.Ledger.ToString("0.#####", Inv), r.Errors.ToString(), r.Warns.ToString(), r.Tps.ToString("0"), r.Pawns.ToString(), r.Colonists.ToString() }.Concat(cats.Select(c => r.Cats.TryGetValue(c, out var v) ? F(v) : "0"))));
                File.WriteAllText(bs + ".csv", csv.ToString());
                File.WriteAllText(bs + ".md", Markdown(cats, final));
            }
            catch (Exception e) { T.Log("could not write long-run files: " + e.Message); }
        }

        static string F(double v) => v.ToString("0.0#", Inv);

        string Markdown(List<string> cats, bool final)
        {
            var sb = new StringBuilder();
            var full = rows.Where(r => r.Day >= 1).ToList();
            sb.AppendLine("# Hearth & Harbor long run");
            sb.AppendLine();
            sb.AppendLine($"{(final ? "Final" : "Partial")} report. Showcase hotel (lobby inn with 3 guest rooms, restaurant with kitchen stations, casino, spa, Silverline branch, insurance) built by `ShowcaseBuilder`, then **{Days} in-game days at maximum speed**, normal guest arrivals, colonists working (hosting, serving, cooking at priority 1). Threats were switched off (threatScale 0). Only on day 0 two guest groups were forced; every later guest came from the scheduler.");
            sb.AppendLine();
            sb.AppendLine("Day 0 is the partial build day, days 1..N are full ledger days (60000 ticks). Money in silver.");
            sb.AppendLine();
            sb.AppendLine("| day | present | arrived | left | nights | income | till | bank | renown | whispers | satisf. | casino house | cooked | sold | premium | claims | ledger | errors | TPS | pawns |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var r in rows)
                sb.AppendLine($"| {r.Day} | {r.Present} | {r.Arrived} | {r.Left} | {r.Nights} | {r.Income:0.0} | {r.Till:0} | {r.Bank:0} | {r.Renown:0.0} | {r.Whispers:0.0} | {(double.IsNaN(r.Satisfaction) ? "-" : r.Satisfaction.ToString("0.00", Inv))} | {r.CasinoHouse:0.0} | {r.DishesCooked} | {r.DishesSold} | {r.Premiums:0.0} | {r.Claims} | {r.Ledger:0.#####} | {r.Errors} | {r.Tps:0} | {r.Pawns} |");
            sb.AppendLine();
            sb.AppendLine("## Income by category (silver per day)");
            sb.AppendLine();
            sb.AppendLine("| day | " + string.Join(" | ", cats) + " |");
            sb.AppendLine("|---|" + string.Concat(cats.Select(_ => "---|")));
            foreach (var r in rows) sb.AppendLine($"| {r.Day} | " + string.Join(" | ", cats.Select(c => r.Cats.TryGetValue(c, out var v) ? v.ToString("0.0", Inv) : "0")) + " |");
            sb.AppendLine();
            if (full.Count > 0)
            {
                double avg = full.Average(r => r.Income), early = full.Take(5).Average(r => r.Income);
                sb.AppendLine("## Balance");
                sb.AppendLine();
                sb.AppendLine($"* Average income per full day: **{avg:0.0}** (first 5 days: {early:0.0}); target KONZEPT D20 early game: 60-150 per day.");
                sb.AppendLine($"* Guests seen: {seen.Count}, nights sold: {rows.Sum(r => r.Nights)}, plates cooked (lower bound, polled): {rows.Sum(r => r.DishesCooked)}, plates sold: {rows.Sum(r => r.DishesSold)}.");
                sb.AppendLine($"* Renown {rows.First().Renown:0.0} to {rows.Last().Renown:0.0}, whispers {rows.Last().Whispers:0.0}; TPS average {full.Average(r => r.Tps):0}.");
                sb.AppendLine();
            }
            sb.AppendLine("## Checks");
            sb.AppendLine();
            foreach (var l in T.Lines.Where(l => l.StartsWith("  ok") || l.StartsWith("  FAIL"))) sb.AppendLine("* " + l.Trim());
            if (problems.Count > 0) { sb.AppendLine(); sb.AppendLine("## Problems seen during the run"); sb.AppendLine(); foreach (var p in problems) sb.AppendLine("* " + p); }
            var errLines = HLog.RecentSnapshot().Where(l => l.Contains("| ERROR |")).Take(15).ToList();
            if (errLines.Count > 0) { sb.AppendLine(); sb.AppendLine("## HLog errors"); sb.AppendLine(); foreach (var l in errLines) sb.AppendLine("* `" + l.Split('\n')[0] + "`"); }
            sb.AppendLine();
            sb.AppendLine("## Notes");
            sb.AppendLine();
            foreach (var l in T.Lines.Where(l => l.StartsWith("  note ") && !l.StartsWith("  note day "))) sb.AppendLine("* " + l.Substring(7));
            return sb.ToString();
        }
    }
}
#endif
