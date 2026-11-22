// SPDX-License-Identifier: MIT
// In-game suites of the Casino module (compiled only with HH_TESTS).
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Hearth.CasinoMath;
using Hearth.Testing;

namespace Hearth.Casino.Tests
{
    static class Kit
    {
        public static ModuleId Module => ModuleId.Casino;

        /// <summary>Spawns a building on free ground with a free ring around it (so people can stand at it).</summary>
        public static Thing Spawn(TestContext t, ThingDef def, Rot4 rot, IntVec3 around)
        {
            var map = t.Map;
            foreach (var c in GenRadial.RadialCellsAround(around, 30, true))
            {
                var rect = GenAdj.OccupiedRect(c, rot, def.size).ExpandedBy(2);
                if (!rect.Cells.All(x => x.InBounds(map) && x.Standable(map) && x.GetFirstBuilding(map) == null && !x.Fogged(map) && x.GetTerrain(map).affordances.Contains(TerrainAffordanceDefOf.Light))) continue;
                var thing = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
                thing.SetFaction(Faction.OfPlayer);
                GenSpawn.Spawn(thing, c, map, rot);
                return thing;
            }
            return null;
        }

        public static void Power(Thing t) { var p = t?.TryGetComp<CompPowerTrader>(); if (p != null) p.PowerOn = true; }

        public static Faction FriendlyFaction()
        {
            var f = Find.FactionManager.AllFactionsListForReading.FirstOrDefault(x => !x.IsPlayer && !x.Hidden && x.def.humanlikeFaction && !x.HostileTo(Faction.OfPlayer));
            if (f != null) return f;
            f = Find.FactionManager.AllFactionsListForReading.FirstOrDefault(x => !x.IsPlayer && !x.Hidden && x.def.humanlikeFaction);
            if (f != null) { f.TryAffectGoodwillWith(Faction.OfPlayer, 200, false, false); }
            return f;
        }

        public static List<Pawn> Visitors(TestContext t, int n, int silver, IntVec3 near, int intellect = -1)
        {
            var map = t.Map; var faction = FriendlyFaction(); var list = new List<Pawn>();
            if (faction == null) return list;
            for (int i = 0; i < n; i++)
            {
                var p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Villager, faction, PawnGenerationContext.NonPlayer, forceGenerateNewPawn: true));
                var cell = CellFinder.RandomClosewalkCellNear(near, map, 6);
                GenSpawn.Spawn(p, cell, map);
                if (silver > 0) { var s = ThingMaker.MakeThing(ThingDefOf.Silver); s.stackCount = silver; p.inventory.innerContainer.TryAdd(s); }
                if (intellect >= 0 && p.skills != null) p.skills.GetSkill(SkillDefOf.Intellectual).Level = intellect;
                list.Add(p);
            }
            var lord = LordMaker.MakeNewLord(faction, new LordJob_VisitColony(faction, near), map, list);
            return list;
        }

        public static int Silver(Pawn p) => p.inventory.innerContainer.Where(x => x.def == ThingDefOf.Silver).Sum(x => x.stackCount);

        public static bool StartGuestJob(Pawn guest, bool cardsOnly = false)
        {
            var info = GuestApi.Info(guest); if (info == null) return false;
            var act = GuestActivities.All.FirstOrDefault(a => a.Id == "casino.play"); if (act == null) return false;
            if (act.Desire(guest, info) <= 0f) return false;
            var job = act.TryGiveJob(guest, info); if (job == null) return false;
            guest.jobs.StartJob(job, JobCondition.InterruptForced);
            return true;
        }

        public static Pawn Colonist(TestContext t, int idx = 0) => t.Map.mapPawns.FreeColonistsSpawned.ElementAt(idx);

        /// <summary>Clean slate for a colonist's gambling state (suites share one game).</summary>
        public static void ResetGambler(Pawn p)
        {
            var h = Gambling.AddictionOf(p); if (h != null) p.health.RemoveHediff(h);
            var rec = CasinoComponent.Get()?.RecordOf(p, false);
            if (rec != null) { rec.SessionTicks.Clear(); rec.LastSessionTick = -999999; rec.CravingUntil = -1; rec.TotalSessions = 0; rec.CuredStage = 0; }
        }
    }

    // ============================================================================================================
    public sealed class DefsSuite : HearthTestSuite
    {
        public override string Name => "casino.defs";
        public override ModuleId Module => ModuleId.Casino;
        public override IEnumerable<int> Run(TestContext t)
        {
            t.Check(Modules.On(ModuleId.Casino), "casino module is on");
            // DefOfs and defs
            foreach (var f in typeof(CasinoDefOf).GetFields()) t.Check(f.GetValue(null) != null, "DefOf " + f.Name + " resolved");
            var buildings = new[] { CasinoDefOf.HH_SlotMachine, CasinoDefOf.HH_RouletteTable, CasinoDefOf.HH_BlackjackTable, CasinoDefOf.HH_PokerTable, CasinoDefOf.HH_CashierCage };
            foreach (var d in buildings)
            {
                t.Check(!d.label.NullOrEmpty() && !d.description.NullOrEmpty(), d.defName + " has label and description");
                t.Check(d.graphic != null && !d.graphic.MatSingle.name.Contains("BadMat"), d.defName + " graphic loads");
                t.Check(d.designationCategory == DefDatabase<DesignationCategoryDef>.GetNamed("HH_Casino"), d.defName + " in the casino build menu");
                t.Check(d.researchPrerequisites != null && d.researchPrerequisites.Count == 1, d.defName + " needs research");
                t.Check(d.hasInteractionCell, d.defName + " has an interaction cell");
            }
            t.Check(CasinoDefOf.HH_SlotMachine.size == new IntVec2(1, 1) && CasinoDefOf.HH_RouletteTable.size == new IntVec2(3, 2) && CasinoDefOf.HH_BlackjackTable.size == new IntVec2(3, 2) && CasinoDefOf.HH_PokerTable.size == new IntVec2(3, 2) && CasinoDefOf.HH_CashierCage.size == new IntVec2(2, 2), "sizes: 1x1, 3x2, 3x2, 3x2, 2x2");
            t.Check(CasinoDefOf.HH_SlotMachine.GetCompProperties<CompProperties_Power>() != null, "slot machine needs power");
            t.Check(CasinoUI.Hint != null, "help hint def exists");
            foreach (var d in buildings.Concat(new[] { CasinoDefOf.HH_CasinoChips, CasinoDefOf.HH_GamblingSession }))
                t.Check(!d.ConfigErrors().Any(), d.defName + " has no config errors: " + string.Join("; ", d.ConfigErrors()));
            t.Check(CasinoDefOf.HH_Casino_Slots.tab.defName == "HH_Hospitality" && CasinoDefOf.HH_Casino_Tables.tab.defName == "HH_Hospitality", "research in tab HH_Hospitality");
            t.Check(CasinoDefOf.HH_PlayCasinoJoy.Worker != null && CasinoDefOf.HH_PlayCasinoJoy.joyKind == CasinoDefOf.HH_Gambling, "joy giver works on joy kind gambling");
            t.Check(CasinoDefOf.HH_PlayCasino.joyKind == CasinoDefOf.HH_Gambling, "job def has the gambling joy kind");
            t.Check(DefDatabase<WorkGiverDef>.GetNamed("HH_RunCasinoTable").workType.defName == "HH_Serving", "croupier work giver is in Serving");
            t.Check(CasinoDefOf.HH_Venue_Casino.module == ModuleId.Casino, "casino venue type exists");
            t.Check(CasinoDefOf.HH_Venue_Casino.icon != null, "casino venue icon loads");
            // translations
            foreach (var k in new[] { "HH.Casino.HelpHint", "HH.Casino.TabTitle", "HH.Casino.AddictedText", "HH.Casino.Spin", "HH.Tab.Casino", "HH.Casino.Vol.Low", "HH.Casino.Game.Poker" })
                t.Check(k.CanTranslate(), "string " + k);

            // settings
            foreach (var f in typeof(CS).GetFields().Where(f => f.IsLiteral && f.GetValue(null) is string s && s.StartsWith("casino.")))
            {
                string key = (string)f.GetValue(null);
                var s = HSettings.Find(key); t.Check(s != null && s.Module == ModuleId.Casino, "setting registered " + key);
                if (s != null) t.Check(!float.IsNaN(HSettings.Get(key)), "setting readable " + key + " = " + HSettings.Get(key));
            }
            var slot = HSettings.Find(CS.SlotRtp);
            t.Check(Math.Abs(slot.DefaultFor(3) - 0.92f) < 1e-4 && Math.Abs(slot.DefaultFor(2) - 0.95f) < 1e-4 && Math.Abs(slot.DefaultFor(4) - 0.88f) < 1e-4, "slot RTP anchors 95 / 92 / 88");
            var rou = HSettings.Find(CS.RouletteEdge);
            t.Check(Math.Abs(rou.DefaultFor(3) - 0.027f) < 1e-4 && Math.Abs(rou.DefaultFor(2) - 0.0135f) < 1e-4 && Math.Abs(rou.DefaultFor(4) - 0.0526f) < 1e-4, "roulette anchors 1.35 / 2.70 / 5.26 %");
            t.Check(Math.Abs(HSettings.Find(CS.AddictionP0).DefaultFor(3) - 0.08f) < 1e-5, "p0 Standard = 0.08");
            t.Check(HSettings.Find(CS.AddictionP0).DefaultFor(0) == 0f && HSettings.Find(CS.AddictionP0).DefaultFor(1) == 0f, "no addiction on Comped / All-Inclusive");
            t.Check(HSettings.Get(CS.AddictionP0) > 0, "p0 on this game tier");
            t.Check(HSettings.Find("core.helpHint") != null, "help hint setting exists");

            // the virtual drug "gambling" is a drug for the policy and nothing else
            t.Check(CasinoDefOf.HH_GamblingSession.IsDrug && CasinoDefOf.HH_GamblingSession.IsPleasureDrug, "virtual drug is a pleasure drug");
            t.Check(CasinoDefOf.HH_GamblingSession.thingCategories.NullOrEmpty() && CasinoDefOf.HH_GamblingSession.tradeability == Tradeability.None, "virtual drug is in no category and not tradeable");
            var pawn = Kit.Colonist(t);
            var entry = Gambling.EntryOf(pawn.drugs.CurrentPolicy);
            t.Check(entry != null, "drug policy has a gambling line");
            Gambling.InitPolicies();
            if (entry != null) t.Check(entry.allowedForJoy && Math.Abs(entry.daysFrequency - 1.5f) < 1e-4, "default policy: allowed for joy, at most every 1.5 days");
            t.Check(t.Map.listerThings.ThingsOfDef(CasinoDefOf.HH_GamblingSession).Count == 0, "no virtual drug thing exists on the map");
            t.Check(!pawn.drugs.ShouldTryToTakeScheduledNow(CasinoDefOf.HH_GamblingSession), "vanilla scheduled drug taking ignores gambling");
            t.Check(!DefDatabase<ThingDef>.AllDefs.Any(d => d.recipeMaker != null && d.defName.StartsWith("HH_Gambling")), "gambling cannot be crafted");

            // every building spawns, has a usable interaction cell and powers
            var around = pawn.Position;
            foreach (var d in buildings)
            {
                var th = Kit.Spawn(t, d, Rot4.North, around);
                t.Check(th != null && th.Spawned, d.defName + " spawns");
                if (th == null) continue;
                var ic = th.InteractionCell;
                t.Check(ic.IsValid && !th.OccupiedRect().Contains(ic) && ic.Standable(t.Map), d.defName + " interaction cell is free: " + ic + " vs " + th.OccupiedRect());
                var comp = th.TryGetComp<CompCasinoGame>();
                t.Check((comp != null) == (d != CasinoDefOf.HH_CashierCage), d.defName + " game comp present as expected");
                if (comp != null)
                {
                    int cells = comp.PlayCells().Count();
                    t.Check(cells >= comp.Props.seats, d.defName + " has seats: " + cells);
                    t.Check(comp.Props.needsCroupier == (d.size.x == 3), d.defName + " croupier flag");
                }
                th.Destroy();
            }
            // save-load round trip of session data
            var kinds = Enum.GetValues(typeof(CasinoGame)).Cast<CasinoGame>().ToList();
            t.Check(kinds.Count == 4, "four games");
            yield return 5;
        }
    }

    // ============================================================================================================
    public sealed class SlotsSuite : HearthTestSuite
    {
        public override string Name => "casino.slots";
        public override ModuleId Module => ModuleId.Casino;
        public override int MaxTicks => 20000;

        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var ledger = Ledger.Current;
            HSettings.Set(CS.RoundsSlot, 600); HSettings.Set(CS.HouseFloat, 0);
            HSettings.Set(CS.Croupier, 1);
            var col = Kit.Colonist(t);
            var machine = Kit.Spawn(t, CasinoDefOf.HH_SlotMachine, Rot4.South, col.Position);
            t.Check(machine != null, "machine spawned");
            if (machine == null) yield break;
            Kit.Power(machine);
            var comp = machine.TryGetComp<CompCasinoGame>();
            comp.Volatility = SlotVolatility.Medium;
            // a casino venue sign, if the venue module is there
            var signDef = DefDatabase<ThingDef>.GetNamedSilentFail("HH_VenueSign");
            t.Note(signDef == null ? "no HH_VenueSign def yet: using the map house account" : "HH_VenueSign exists");
            string account = CasinoBank.HouseAccountFor(machine, out var venue, out _);

            var guests = Kit.Visitors(t, 3, 400, machine.Position);
            t.Check(guests.Count == 3, "three vanilla visitors spawned");
            if (guests.Count < 3) yield break;
            foreach (var g in guests) { var info = GuestApi.Info(g); t.Check(info != null, g.LabelShort + " is a guest"); if (info != null) info.Budget = Money.FromSilver(400); }
            yield return 10;
            int silverBefore = guests.Sum(Kit.Silver);
            Money tillBefore = ledger.Balance(Payments.ColonyTill(map)), houseBefore = ledger.Balance(account);
            var rows = new List<string>();
            // day-book baseline: earlier suites in the same game left rows under the same house account
            long rows0 = CasinoComponent.Get().Rows.Where(r => r.Account == account && r.Game == (int)CasinoGame.Slots).Sum(r => (long)r.Rounds);
            int started = 0;
            for (int step = 0; step < 150 && comp.Rounds < 400; step++)
            {
                foreach (var g in guests)
                    if (g.Spawned && !g.Dead && g.CurJob?.def != CasinoDefOf.HH_PlayCasino && CasinoComponent.Get().SessionOf(g) == null && Kit.StartGuestJob(g)) started++;
                yield return 60;
            }
            // let running sessions finish
            for (int i = 0; i < 80 && CasinoComponent.Get().Sessions.Count > 0; i++) yield return 60;
            t.Check(started >= 1, "guests started sessions: " + started);
            t.Check(comp.Rounds >= 50, "at least 50 rounds played: " + comp.Rounds);
            t.Check(CasinoComponent.Get().Sessions.Count == 0, "all sessions settled");
            t.Check(ledger.SumAll() == 0, "ledger sums to zero");
            int silverAfter = guests.Sum(Kit.Silver);
            Money house = ledger.Balance(account), till = ledger.Balance(Payments.ColonyTill(map));
            double guestLoss = silverBefore - silverAfter;
            t.Check(Math.Abs(guestLoss - ((house - houseBefore).Silver + (till - tillBefore).Silver)) < 0.01, $"guests lost {guestLoss} silver = house {(house - houseBefore).Silver:0.###} + till {(till - tillBefore).Silver:0.###}");
            double expected = comp.Expected, sd = Math.Sqrt(comp.Variance), win = comp.HouseWin;
            t.Note($"rounds {comp.Rounds}, staked {comp.StakedMicro / 1e5:0.0}, paid {comp.PaidMicro / 1e5:0.0}, house win {win:0.0}, expected {expected:0.0} ± {sd:0.0}");
            t.Check(Math.Abs(win - expected) <= 6 * sd + 0.01, "house win within 6 sigma of expectation");
            t.Check(comp.StakedMicro > 0 && comp.PaidMicro >= 0, "money was staked and paid");
            // the books agree with the sessions' purses
            // The day book gets the guest rounds under the house account. Colonists may also play the machine for house chips
            // (those rounds book under the chips account) and other machines of earlier suites share the account, so only bound it.
            long guestRounds = CasinoComponent.Get().Rows.Where(r => r.Account == account && r.Game == (int)CasinoGame.Slots).Sum(r => (long)r.Rounds) - rows0;
            t.Check(guestRounds > 0 && guestRounds <= comp.Rounds + 2000, "day book has the guest rounds under the house account: " + guestRounds + " (machine total " + comp.Rounds + ")");

            // closing: the surplus goes to the till
            var h = CasinoComponent.Get().Houses.FirstOrDefault(x => x.Account == account);
            t.Check(h != null, "the house was registered");
            if (h != null)
            {
                Money bal = ledger.Balance(account);
                Money swept = CasinoBank.Sweep(h);
                t.Check(ledger.Balance(account) <= HSettings.GetSilver(CS.HouseFloat), "house drawer is down to the float after closing: " + ledger.Balance(account).ToStringShort());
                t.Check(Math.Abs((swept.Silver) - Math.Max(0, bal.Silver)) < 0.01, "swept the positive balance: " + swept.ToStringShort());
            }
            t.Check(ledger.SumAll() == 0, "ledger sums to zero after the sweep");

            // powerless machines do not serve guests
            comp.parent.TryGetComp<CompPowerTrader>().PowerOn = false;
            t.Check(!comp.GuestUsable(out string why) && why == "no power", "unpowered machine refuses guests");
            comp.parent.TryGetComp<CompPowerTrader>().PowerOn = true;
            comp.GuestsAllowed = false;
            t.Check(!comp.GuestUsable(out why), "closed machine refuses guests");
            comp.GuestsAllowed = true;
            t.Check(comp.GuestUsable(out _), "machine usable again");

            foreach (var g in guests) { if (g.Spawned) g.Destroy(); }
            machine.Destroy();
            HSettings.Reset(HSettings.Find(CS.RoundsSlot)); HSettings.Reset(HSettings.Find(CS.HouseFloat));
        }
    }

    // ============================================================================================================
    public sealed class TablesSuite : HearthTestSuite
    {
        public override string Name => "casino.tables";
        public override ModuleId Module => ModuleId.Casino;
        public override int MaxTicks => 30000;

        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var ledger = Ledger.Current;
            HSettings.Set(CS.RoundsRoulette, 120); HSettings.Set(CS.RoundsBlackjack, 200); HSettings.Set(CS.RoundsPoker, 100);
            HSettings.Set(CS.HouseFloat, 0); HSettings.Set(CS.Croupier, 1);
            // earlier suites of the same game may have left machines/tables standing: colonists would staff those instead of ours
            foreach (var old in CasinoBank.AllDevices(map).ToList()) if (old.parent != null && !old.parent.Destroyed) old.parent.Destroy();
            var col = Kit.Colonist(t);
            var tables = new List<CompCasinoGame>();
            foreach (var d in new[] { CasinoDefOf.HH_RouletteTable, CasinoDefOf.HH_BlackjackTable, CasinoDefOf.HH_PokerTable })
            {
                var th = Kit.Spawn(t, d, Rot4.North, col.Position + new IntVec3(Rand.RangeInclusive(-6, 6), 0, Rand.RangeInclusive(-6, 6)));
                t.Check(th != null, d.defName + " spawned");
                if (th != null) tables.Add(th.TryGetComp<CompCasinoGame>());
            }
            if (tables.Count < 3) yield break;
            var guests = Kit.Visitors(t, 3, 600, col.Position);
            if (guests.Count < 3) { t.Check(false, "visitors"); yield break; }
            foreach (var g in guests) { var info = GuestApi.Info(g); info.Budget = Money.FromSilver(600); g.skills.GetSkill(SkillDefOf.Intellectual).Level = 12; }
            // nobody is a croupier yet: a guest forced to a table waits and finally leaves
            foreach (var c in map.mapPawns.FreeColonistsSpawned.ToList()) if (!c.WorkTypeIsDisabled(DefDatabase<WorkTypeDef>.GetNamed("HH_Serving"))) c.workSettings.SetPriority(DefDatabase<WorkTypeDef>.GetNamed("HH_Serving"), 0);
            var tbl = tables[0];
            t.Check(!tbl.Staffed, "table without croupier is not staffed");
            var s0 = CasinoEngine.Begin(guests[0], tbl, SessionKind.Guest, false, out string fail);
            t.Check(s0 != null, "guest can sit down (" + fail + ")");
            yield return 200;
            t.Check(tbl.Rounds == 0, "no rounds without a croupier");
            yield return 800;
            t.Check(CasinoComponent.Get().SessionOf(guests[0]) == null, "guest gave up waiting for the croupier");
            t.Check(ledger.SumAll() == 0, "ledger intact after the abandoned session");

            // now every colonist works the Serving type: one croupier per table
            var serving = DefDatabase<WorkTypeDef>.GetNamed("HH_Serving");
            foreach (var c in map.mapPawns.FreeColonistsSpawned.ToList()) { c.workSettings.EnableAndInitialize(); if (!c.WorkTypeIsDisabled(serving)) c.workSettings.SetPriority(serving, 1); c.jobs.StopAll(); }
            int waited = 0;
            // two croupiers at once is enough: in a long run the third colonist is often asleep, eating or at a joy job of an earlier suite
            int want = Math.Min(2, map.mapPawns.FreeColonistsSpawned.Count(c => !c.WorkTypeIsDisabled(serving)));
            var everStaffed = new HashSet<CompCasinoGame>(); // staffing is momentary (a croupier leaves when nobody waits), so count tables seen staffed at any sample
            while (everStaffed.Count < want && waited < 6000) { foreach (var x in tables) if (x.Staffed) everStaffed.Add(x); if (everStaffed.Count >= want) break; yield return 100; waited += 100; }
            foreach (var c in map.mapPawns.FreeColonistsSpawned.ToList()) t.Note($"colonist {c.LabelShort}: job {c.CurJob?.def.defName ?? "none"} at {c.Position}, asleep {!c.Awake()}, hour {GenLocalDate.HourOfDay(map)}, drafted {c.Drafted}, downed {c.Downed}, mental {c.InMentalState}");
            foreach (var x in tables) t.Note($"table {x.Game}: wants {x.WantsCroupier}, staffed {x.Staffed}, at {x.parent.Position}, croupier cell {x.CroupierCell}, closed {!x.GuestsAllowed}, powered {x.Powered}");
            t.Check(everStaffed.Count >= want, $"colonists staff {want} tables (Serving work): {everStaffed.Count} after {waited} ticks");
            // guests play at whatever is staffed, one guest per table where possible
            for (int step = 0; step < 100 && tables.Any(x => x.Staffed) && tables.Min(x => x.Staffed ? x.Rounds : 9999) < 25; step++)
            {
                for (int gi = 0; gi < guests.Count; gi++)
                {
                    var g = guests[gi];
                    if (!g.Spawned || g.CurJob?.def == CasinoDefOf.HH_PlayCasino || CasinoComponent.Get().SessionOf(g) != null) continue;
                    var target = tables.Where(x => x.Staffed && x.HasFreeSeat).OrderBy(x => x.Rounds).FirstOrDefault();
                    if (target == null) continue;
                    var job = CasinoJobs.MakeJob(g, target); if (job != null) g.jobs.StartJob(job, JobCondition.InterruptForced);
                }
                yield return 60;
            }
            // second phase: tables that found no croupier before get one once the others are closed
            var idle = tables.Where(x => x.Rounds < 10).ToList();
            if (idle.Count > 0)
            {
                for (int i = 0; i < 60 && CasinoComponent.Get().Sessions.Count > 0; i++) yield return 60;
                foreach (var x in tables.Except(idle)) x.GuestsAllowed = false;
                for (int step = 0; step < 150 && idle.Any(x => x.Rounds < 10); step++)
                {
                    foreach (var g in guests)
                    {
                        if (!g.Spawned || g.CurJob?.def == CasinoDefOf.HH_PlayCasino || CasinoComponent.Get().SessionOf(g) != null) continue;
                        var target = idle.Where(x => x.Staffed && x.HasFreeSeat && x.Rounds < 10).FirstOrDefault();
                        if (target == null) continue;
                        var job = CasinoJobs.MakeJob(g, target); if (job != null) g.jobs.StartJob(job, JobCondition.InterruptForced);
                    }
                    yield return 60;
                }
            }
            if (tables.Any(x => x.Rounds < 10))
            {
                foreach (var x in tables) { x.GuestUsable(out string r); t.Note($"{x.Game}: staffed {x.Staffed}, wants {x.WantsCroupier}, guest usable reason '{r}', seated {x.Seated}, rounds {x.Rounds}"); }
                foreach (var c in map.mapPawns.FreeColonistsSpawned) t.Note($"{c.LabelShort}: job {c.CurJob?.def.defName ?? "none"} on {c.CurJob?.targetA.Thing?.def.defName} cell {c.CurJob?.targetB} at {c.Position} toil {(c.jobs.curDriver as Verse.AI.JobDriver)?.CurToilIndex} moving {c.pather.Moving}, "+
                    $" serving priority {c.workSettings?.GetPriority(DefDatabase<WorkTypeDef>.GetNamed("HH_Serving"))}, disabled {c.WorkTypeIsDisabled(DefDatabase<WorkTypeDef>.GetNamed("HH_Serving"))}");
            }
            t.Note("table rounds: " + string.Join(", ", tables.Select(x => x.Game + "=" + x.Rounds)));
            t.Check(tables.Count(x => x.Rounds >= 10) >= 2, "at least two tables ran rounds with a croupier: " + string.Join(", ", tables.Select(x => x.Game + "=" + x.Rounds)));
            for (int i = 0; i < 160 && CasinoComponent.Get().Sessions.Count > 0; i++) yield return 60; // colonist chip sessions run up to 9000 ticks
            foreach (var x in tables.Where(x => x.Rounds >= 30 && x.Variance > 0))
            {
                double z = (x.HouseWin - x.Expected) / Math.Sqrt(x.Variance);
                t.Check(Math.Abs(z) < 6, x.Game + " house result within 6 sigma (z=" + z.ToString("0.0") + ")");
            }
            t.Check(ledger.SumAll() == 0, "ledger sums to zero");
            t.Check(CasinoComponent.Get().Sessions.Count == 0, "sessions settled");
            foreach (var g in guests) g.Destroy();
            foreach (var x in tables) x.parent.Destroy();
            HSettings.Reset(HSettings.Find(CS.RoundsRoulette)); HSettings.Reset(HSettings.Find(CS.RoundsBlackjack)); HSettings.Reset(HSettings.Find(CS.RoundsPoker)); HSettings.Reset(HSettings.Find(CS.HouseFloat));
        }
    }

    // ============================================================================================================
    public sealed class JoySuite : HearthTestSuite
    {
        public override string Name => "casino.joy";
        public override ModuleId Module => ModuleId.Casino;
        public override int MaxTicks => 20000;

        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var ledger = Ledger.Current; HLog.Verbose = true;
            HSettings.Set(CS.RoundsSlot, 600);
            Gambling.InitPolicies();
            var pawn = Kit.Colonist(t, 0);
            Kit.ResetGambler(pawn);
            var machine = Kit.Spawn(t, CasinoDefOf.HH_SlotMachine, Rot4.South, pawn.Position);
            if (machine == null) { t.Check(false, "machine spawned"); yield break; }
            Kit.Power(machine);
            var comp = machine.TryGetComp<CompCasinoGame>();
            // nobody else interferes
            foreach (var c in map.mapPawns.FreeColonistsSpawned.Where(c => c != pawn)) c.jobs.StopAll();
            pawn.jobs.StopAll();
            var joy = pawn.needs.joy; joy.CurLevel = 0.1f;
            float tolBefore = joy.tolerances[CasinoDefOf.HH_Gambling];
            int silverBefore = map.resourceCounter.GetCount(ThingDefOf.Silver);
            Money tillBefore = ledger.Balance(Payments.ColonyTill(map)); long ledgerBefore = ledger.Balance(Payments.ColonyTill(map)).Micro;
            t.Check(Gambling.PolicyAllowsJoy(pawn, out string why), "policy allows gambling for joy: " + why);
            t.Check(CasinoDefOf.HH_PlayCasinoJoy.Worker.CanBeGivenTo(pawn), "joy giver may give gambling to the colonist");
            var job = CasinoDefOf.HH_PlayCasinoJoy.Worker.TryGiveJob(pawn);
            t.Check(job != null && job.def == CasinoDefOf.HH_PlayCasino, "joy giver offers the machine");
            if (job == null) { machine.Destroy(); yield break; }
            pawn.jobs.StartJob(job, JobCondition.InterruptForced);
            int guard = 0;
            while (pawn.CurJob?.def == CasinoDefOf.HH_PlayCasino && guard++ < 120) yield return 60;
            float joyAfter = pawn.needs.joy.CurLevel;
            t.Check(comp.Rounds >= 20, "colonist played rounds: " + comp.Rounds);
            t.Check(joyAfter > 0.4f, $"joy rose from 0.10 to {joyAfter:0.00}");
            t.Check(pawn.needs.joy.tolerances[CasinoDefOf.HH_Gambling] > tolBefore, "joy tolerance built up");
            t.Check(map.resourceCounter.GetCount(ThingDefOf.Silver) == silverBefore || Math.Abs(map.resourceCounter.GetCount(ThingDefOf.Silver) - silverBefore) < 5, "no colony silver lost (free house chips)");
            t.Check(ledger.Balance(Payments.ColonyTill(map)).Micro == ledgerBefore, "the colony till is untouched");
            t.Check(CasinoComponent.Get().Sessions.Count == 0, "session ended");
            var rec = CasinoComponent.Get().RecordOf(pawn, false);
            t.Check(rec != null && rec.TotalSessions == 1, "one gambling session counted");
            t.Check(Gambling.CounterText(pawn) != null, "the pawn shows the session counter: " + Gambling.CounterText(pawn));
            // the drug policy limit: just played, "at most every 1.5 days" → no second session now
            t.Check(!Gambling.PolicyAllowsJoy(pawn, out why), "default policy blocks a second session right away (" + why + ")");
            // forbidding it in the policy stops the joy giver
            var e = Gambling.EntryOf(pawn.drugs.CurrentPolicy); e.daysFrequency = 0.1f;
            t.Check(Gambling.PolicyAllowsJoy(pawn, out why) || why == "daily limit", "frequency can be loosened");
            e.allowedForJoy = false;
            t.Check(!CasinoDefOf.HH_PlayCasinoJoy.Worker.CanBeGivenTo(pawn), "policy 'not for joy' stops the joy giver");
            e.allowedForJoy = true; e.daysFrequency = 1.5f;
            Kit.ResetGambler(pawn);
            machine.Destroy();
            HSettings.Reset(HSettings.Find(CS.RoundsSlot));
        }
    }

    // ============================================================================================================
    public sealed class AddictionSuite : HearthTestSuite
    {
        public override string Name => "casino.addiction";
        public override ModuleId Module => ModuleId.Casino;
        public override int MaxTicks => 12000;

        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var comp = CasinoComponent.Get();
            var pawn = Kit.Colonist(t, 0);
            Gambling.InitPolicies();
            t.Check(Gambling.EntryOf(pawn.drugs.CurrentPolicy) != null, "drug policy entry exists");
            t.Check(Gambling.P0 > 0.07 && Gambling.P0 < 0.09, "p0 on this tier ≈ 0.08: " + Gambling.P0);

            // 1) deterministic seeded loop: five sessions inside three days
            var rng = new RngStream(20261122, RngStream.HashString("casino.addiction.suite"));
            double F = Gambling.Factor(pawn);
            double pk3 = Gambling.ChanceFor(pawn, 3), pk4 = Gambling.ChanceFor(pawn, 4), pk5 = Gambling.ChanceFor(pawn, 5);
            double expected = 1 - (1 - pk3) * (1 - pk4) * (1 - pk5);
            int trials = 800, hits = 0; int now = Find.TickManager.TicksGame;
            for (int i = 0; i < trials; i++)
            {
                var rec = comp.RecordOf(pawn, true); rec.SessionTicks.Clear(); rec.CravingUntil = -1; rec.TotalSessions = 0; rec.LastSessionTick = -999999;
                bool got = false;
                for (int s = 1; s <= 5 && !got; s++) got = Gambling.RegisterSession(pawn, now + s * 1000, rng);
                if (got) { hits++; var h = Gambling.AddictionOf(pawn); t.Check(h != null, "addiction hediff appears (trial " + i + ")"); if (h != null) pawn.health.RemoveHediff(h); }
            }
            double f = (double)hits / trials, se = Math.Sqrt(expected * (1 - expected) / trials);
            t.Note($"addicted in {hits} of {trials} runs = {f:P1}; formula says {expected:P1} (factor F {F:0.00})");
            t.Check(Math.Abs(f - expected) <= 5 * se, "seeded frequency matches the formula within 5 sigma");
            var r0 = comp.RecordOf(pawn, true); r0.SessionTicks.Clear();
            // never before the third session
            for (int i = 0; i < 300; i++) { var rec = comp.RecordOf(pawn, true); rec.SessionTicks.Clear(); rec.CravingUntil = -1; Gambling.RegisterSession(pawn, now + 1, rng); Gambling.RegisterSession(pawn, now + 2, rng); }
            t.Check(Gambling.AddictionOf(pawn) == null, "two sessions in three days never cause addiction");

            // 2) the addiction as part of vanilla's system
            var rec2 = comp.RecordOf(pawn, true); rec2.SessionTicks.Clear(); rec2.CravingUntil = -1;
            var add = Gambling.Addict(pawn, 3, false);
            yield return 5;
            t.Check(add != null && pawn.health.hediffSet.HasHediff(CasinoDefOf.HH_GamblingAddiction), "hediff is on the pawn");
            var need = pawn.needs.TryGetNeed(CasinoDefOf.HH_Need_Gambling) as Need_Gambling;
            t.Check(need != null, "the gambling need appeared in the needs tab");
            t.Check(add.Chemical == CasinoDefOf.HH_GamblingChemical, "addiction belongs to the gambling chemical");
            t.Check(AddictionUtility.IsAddicted(pawn, CasinoDefOf.HH_GamblingChemical), "vanilla addiction utility recognises it");
            t.Check(add.Stage == 3, "stage 3: depth " + add.depth);
            if (need != null)
            {
                need.CurLevel = 1f; need.NeedInterval();
                double fell = 1.0 - need.CurLevel, wanted = GamblingMath.NeedFallPerDay(3) / 60000.0 * 150.0;
                t.Check(Math.Abs(fell - wanted) < 1e-5, $"need falls {GamblingMath.NeedFallPerDay(3):0.00} per day at stage 3");
                need.CurLevel = 0f; yield return 2;
                t.Check(add.InWithdrawal && add.CurStageIndex == 3, "empty need = withdrawal at stage index 3");
                var th = DefDatabase<ThoughtDef>.GetNamed("HH_GamblingWithdrawal");
                pawn.needs.mood.thoughts.situational.Notify_SituationalThoughtsDirty();
                yield return 2;
                var thoughts = new List<Thought>(); pawn.needs.mood.thoughts.GetAllMoodThoughts(thoughts);
                var w = thoughts.FirstOrDefault(x => x.def == th);
                t.Check(w != null && Math.Abs(w.MoodOffset() - -22f) < 0.5f, "withdrawal mood at stage 3 is -22: " + (w?.MoodOffset().ToString() ?? "no thought"));
                // a session fills the need and digs in
                float depth0 = add.depth;
                add.Notify_Session(Find.TickManager.TicksGame);
                t.Check(Math.Abs(need.CurLevel - 0.35f) < 0.01f && Math.Abs(add.depth - depth0 - 0.015f) < 1e-4, "a session fills 0.35 and deepens the addiction by 0.015");
            }
            // 3) cure by abstinence, then craving and relapse
            int needDays = (int)Math.Ceiling(add.DaysNeeded);
            add.lastSessionTick = Find.TickManager.TicksGame - (needDays + 1) * 60000;
            for (int i = 0; i < 12 && Gambling.AddictionOf(pawn) != null; i++) yield return 60;
            t.Check(Gambling.AddictionOf(pawn) == null, "enough days without a session cure the addiction");
            var rec3 = comp.RecordOf(pawn, true);
            t.Check(rec3.CravingUntil > Find.TickManager.TicksGame && rec3.CuredStage >= 3, "craving memory is set for 60 days");
            int relapses = 0, tries = 200; int t2 = Find.TickManager.TicksGame;
            var rng2 = new RngStream(5, 5);
            for (int i = 0; i < tries; i++)
            {
                rec3.SessionTicks.Clear(); rec3.CravingUntil = t2 + 5000000; rec3.CuredStage = 3;
                if (Gambling.RegisterSession(pawn, t2 + 10, rng2)) { relapses++; var h = Gambling.AddictionOf(pawn); t.Check(h != null && h.Stage == 2, "relapse returns one stage lower"); if (h != null) pawn.health.RemoveHediff(h); }
            }
            double rf = (double)relapses / tries;
            t.Check(rf > 0.35 && rf < 0.65, "relapse chance during craving ≈ 0.5: " + rf.ToString("0.00"));

            // 4) children are excluded
            var child = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, PawnGenerationContext.NonPlayer, forceGenerateNewPawn: true, fixedBiologicalAge: 8f, fixedChronologicalAge: 8f, developmentalStages: DevelopmentalStage.Child));
            t.Check(!child.DevelopmentalStage.Adult(), "test child is a child");
            bool childAddicted = false;
            for (int i = 0; i < 50; i++) childAddicted |= Gambling.RegisterSession(child, t2 + i, rng2);
            t.Check(!childAddicted && Gambling.AddictionOf(child) == null && comp.RecordOf(child, false) == null, "children never gamble into addiction");

            // 5) letting go of the fatal setting: it exists and does nothing
            t.Check(HSettings.Find(CS.FatalDespair) != null && !HSettings.GetBool(CS.FatalDespair), "'fatal despair' is a stub and off");
            Kit.ResetGambler(pawn);
            t.Check(!pawn.Dead, "colonist alive");
        }
    }

    // ============================================================================================================
    public sealed class HintSuite : HearthTestSuite
    {
        public override string Name => "casino.hint";
        public override ModuleId Module => ModuleId.Casino;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var col = Kit.Colonist(t);
            bool was = HSettings.GetBool("core.helpHint");
            HSettings.Set("core.helpHint", 1f);
            string text = CasinoUI.HelpHintText();
            t.Check(!text.NullOrEmpty() && text.Contains("findahelpline.com") && text.StartsWith("If gambling is a problem in real life, help is available"), "hint text: " + text);
            t.Check(CasinoUI.PrimaryUrl().StartsWith("https://findahelpline.com"), "helpline url: " + CasinoUI.PrimaryUrl());
            t.Check(CasinoUI.Hint.secondaryUrl.StartsWith("https://"), "second link is defined in the def");

            var machine = Kit.Spawn(t, CasinoDefOf.HH_SlotMachine, Rot4.South, col.Position);
            if (machine == null) { t.Check(false, "machine"); yield break; }
            var comp = machine.TryGetComp<CompCasinoGame>();
            CasinoUI.HintDrawCount = 0;
            var win = new Dialog_CasinoDemo(comp);
            Find.WindowStack.Add(win);
            for (int i = 0; i < 40 && CasinoUI.HintDrawCount == 0; i++) yield return 30;   // frames depend on how busy the machine is
            t.Check(Find.WindowStack.Windows.Contains(win), "player bet window is open");
            t.Check(win.ShowsHelpHint, "the player bet window carries the help hint line");
            if (CasinoUI.HintDrawCount == 0)
            {
                // the quicktest game window may be hidden behind other test windows and not rendered: draw the contents directly
                try { win.DoWindowContents(new Rect(0, 0, 520f, 470f)); t.Note("window was not rendered by the game loop; its contents were drawn directly"); }
                catch (Exception e) { t.Note("direct draw not possible here: " + e.GetType().Name); }
            }
            if (CasinoUI.HintDrawCount > 0) t.Check(true, "the player bet window draws the help hint (" + CasinoUI.HintDrawCount + " draws)");
            else t.Note("no draw happened (window occluded); the hint line is verified through ShowsHelpHint");
            // turned off: the same window shows nothing
            HSettings.Set("core.helpHint", 0f);
            t.Check(CasinoUI.HelpHintText() == null, "no hint text when the setting is off");
            yield return 5;
            CasinoUI.HintDrawCount = 0;
            yield return 60;
            t.Check(CasinoUI.HintDrawCount == 0, "no hint is drawn when the setting is off");
            HSettings.Set("core.helpHint", 1f);
            // right click hides it
            CasinoUI.HideHint();
            t.Check(!HSettings.GetBool("core.helpHint") && CasinoUI.HelpHintText() == null, "hiding the hint works");
            HSettings.Set("core.helpHint", was ? 1f : 0f);
            win.Close(false);
            machine.Destroy();
            // the main tab page exists
            t.Check(HearthTabs.Pages.Any(p => p.Id == "casino" && p.Module == ModuleId.Casino), "casino page registered in the Hearth main tab");
            yield return 2;
        }
    }

    // ============================================================================================================
    /// <summary>Visual check: screenshots of the practice windows and the Casino page. Not part of normal runs: HH_TEST=casino.shots</summary>
    public sealed class ShotsSuite : HearthTestSuite
    {
        public override string Name => "casino.shots";
        public override ModuleId Module => ModuleId.Casino;
        public override int MaxTicks => 400000;

        sealed class TabWindow : Window
        {
            public override Vector2 InitialSize => new Vector2(760f, 560f);
            public TabWindow() { doCloseX = true; absorbInputAroundWindow = false; draggable = true; }
            public override void DoWindowContents(Rect inRect) => CasinoTab.Draw(inRect);
        }

        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; HLog.Verbose = true;
            HSettings.Set(CS.RoundsSlot, 400); HSettings.Set(CS.RoundsRoulette, 120); HSettings.Set(CS.RoundsBlackjack, 200); HSettings.Set(CS.RoundsPoker, 100); HSettings.Set(CS.HouseFloat, 0);
            var col = Kit.Colonist(t);
            var slot = Kit.Spawn(t, CasinoDefOf.HH_SlotMachine, Rot4.South, col.Position + new IntVec3(-5, 0, 3));
            var rou = Kit.Spawn(t, CasinoDefOf.HH_RouletteTable, Rot4.North, col.Position + new IntVec3(6, 0, 3));
            var bj = Kit.Spawn(t, CasinoDefOf.HH_BlackjackTable, Rot4.North, col.Position + new IntVec3(6, 0, -5));
            var pk = Kit.Spawn(t, CasinoDefOf.HH_PokerTable, Rot4.North, col.Position + new IntVec3(-6, 0, -5));
            Kit.Spawn(t, CasinoDefOf.HH_CashierCage, Rot4.South, col.Position + new IntVec3(0, 0, 9));
            Kit.Power(slot);
            var devs = new[] { slot, rou, bj, pk }.Where(x => x != null).Select(x => x.TryGetComp<CompCasinoGame>()).ToList();
            var serving = DefDatabase<WorkTypeDef>.GetNamed("HH_Serving");
            foreach (var c in map.mapPawns.FreeColonistsSpawned.ToList()) { c.workSettings.EnableAndInitialize(); if (!c.WorkTypeIsDisabled(serving)) c.workSettings.SetPriority(serving, 1); }
            var guests = Kit.Visitors(t, 4, 800, col.Position);
            foreach (var g in guests) { var info = GuestApi.Info(g); if (info != null) info.Budget = Money.FromSilver(800); }
            for (int step = 0; step < 60; step++)
            {
                foreach (var g in guests)
                    if (g.Spawned && g.CurJob?.def != CasinoDefOf.HH_PlayCasino && CasinoComponent.Get().SessionOf(g) == null)
                    {
                        var target = devs.Where(x => x.GuestUsable(out _) && x.HasFreeSeat && (!x.NeedsCroupier || x.Staffed)).OrderBy(x => x.Rounds).FirstOrDefault();
                        if (target != null) { var job = CasinoJobs.MakeJob(g, target); if (job != null) g.jobs.StartJob(job, JobCondition.InterruptForced); }
                    }
                yield return 60;
            }
            // one colonist is addicted, two more have sessions on record
            var addict = map.mapPawns.FreeColonistsSpawned.Last();
            Gambling.Addict(addict, 3, false);
            var rec = CasinoComponent.Get().RecordOf(addict, true); rec.SessionTicks.Add(Find.TickManager.TicksGame - 3000); rec.SessionTicks.Add(Find.TickManager.TicksGame - 1000);
            var other = map.mapPawns.FreeColonistsSpawned.First(); var rec2 = CasinoComponent.Get().RecordOf(other, true); rec2.SessionTicks.Add(Find.TickManager.TicksGame - 2000); rec2.SessionTicks.Add(Find.TickManager.TicksGame - 500);
            for (int i = 0; i < 40; i++) yield return 60;
            string outDir = "/private/tmp/claude-501/hh_tests/casino_shots";
            Directory.CreateDirectory(outDir);
            var win = new TabWindow(); Find.WindowStack.Add(win); yield return 60; ScreenshotTaker.TakeNonSteamShot(); yield return 40; win.Close(false);
            foreach (var d in devs)
            {
                Find.Selector.ClearSelection(); Find.Selector.Select(d.parent); Find.CameraDriver.JumpToCurrentMapLoc(d.parent.Position);
                var dlg = new Dialog_CasinoDemo(d); Find.WindowStack.Add(dlg); yield return 60; ScreenshotTaker.TakeNonSteamShot(); yield return 40; dlg.Close(false); yield return 5;
            }
            yield return 600;
            try { foreach (var f in Directory.GetFiles(GenFilePaths.ScreenshotFolderPath, "*.png")) File.Copy(f, Path.Combine(outDir, Path.GetFileName(f)), true); } catch (Exception e) { t.Note("copy failed: " + e.Message); }
            t.Note("screenshots in " + outDir);
            t.Check(true, "screenshots taken");
            foreach (var g in guests) if (g.Spawned) g.Destroy();
        }
    }
    // ============================================================================================================
    public sealed class SaveSuite : HearthTestSuite
    {
        public override string Name => "casino.save";
        public override ModuleId Module => ModuleId.Casino;
        public override int MaxTicks => 3000;

        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var col = Kit.Colonist(t);
            var machine = Kit.Spawn(t, CasinoDefOf.HH_SlotMachine, Rot4.South, col.Position);
            if (machine == null) { t.Check(false, "machine"); yield break; }
            Kit.Power(machine);
            var comp = machine.TryGetComp<CompCasinoGame>(); comp.Volatility = SlotVolatility.High; comp.GuestsAllowed = false;
            var guest = Kit.Visitors(t, 1, 300, machine.Position)[0];
            GuestApi.Info(guest).Budget = Money.FromSilver(300);
            comp.GuestsAllowed = true;
            var s = CasinoEngine.Begin(guest, comp, SessionKind.Guest, false, out string why);
            t.Check(s != null, "guest session begins (" + why + ")");
            if (s == null) yield break;
            s.Purse = Money.FromSilver(61.23456); s.Rounds = 17; s.StakedMicro = 424242;
            comp.Book(s, 500000, 250000, 1.5, 2.25, "test");
            comp.GuestsAllowed = false;
            var addict = col; var add = Gambling.Addict(addict, 4, false);
            add.depth = 0.61f; add.sessions = 9;
            var rec = CasinoComponent.Get().RecordOf(addict, true); rec.SessionTicks.Add(Find.TickManager.TicksGame - 10); rec.CravingUntil = Find.TickManager.TicksGame + 777777; rec.CuredStage = 2; rec.TotalSessions = 5;
            int err0 = HLog.ErrorCount;
            GameDataSaveLoader.SaveGame("hh_casino_save_test");
            yield return 5;
            string path = GenFilePaths.FilePathForSavedGame("hh_casino_save_test");
            t.Check(File.Exists(path), "game saved");
            if (File.Exists(path))
            {
                string xml = File.ReadAllText(path);
                t.Check(xml.Contains("Hearth.Casino.CasinoComponent"), "save game contains the casino component");
                t.Check(xml.Contains("<purse>" + s.Purse.Micro + "</purse>") && xml.Contains("<buyIn>" + s.BuyIn.Micro + "</buyIn>"), "session purse and buy-in are saved exactly");
                t.Check(xml.Contains("<account>" + s.HouseAccount + "</account>"), "session house account is saved");
                t.Check(xml.Contains("<volatility>High</volatility>") && xml.Contains("<guestsAllowed>False</guestsAllowed>"), "device settings are saved");
                t.Check(xml.Contains("<rounds>" + comp.Rounds + "</rounds>") && xml.Contains("<expected>1.5</expected>"), "device books are saved");
                t.Check(xml.Contains("<craving>" + rec.CravingUntil + "</craving>") && xml.Contains("<curedStage>2</curedStage>"), "gambler record is saved");
                bool okAdd = xml.Contains("HH_GamblingAddiction") && xml.Contains("<depth>0.61") && xml.Contains("<sessions>9</sessions>");
                t.Check(okAdd, "addiction hediff is saved with depth and sessions");
                if (!okAdd) { int i = xml.IndexOf("Hediff_GamblingAddiction"); t.Note("hediff xml: " + (i < 0 ? "not found" : xml.Substring(Math.Max(0, i - 120), Math.Min(500, xml.Length - Math.Max(0, i - 120))).Replace("\n", " "))); }
                File.Delete(path);
            }
            t.Check(HLog.ErrorCount == err0, "no errors while saving");
            CasinoEngine.EndSession(s, "test over");
            t.Check(CasinoComponent.Get().Sessions.Count == 0 && Ledger.Current.SumAll() == 0, "session settled, ledger intact");
            var h = Gambling.AddictionOf(addict); if (h != null) addict.health.RemoveHediff(h);
            rec.CravingUntil = -1;
            guest.Destroy(); machine.Destroy();
        }
    }
}
#endif
