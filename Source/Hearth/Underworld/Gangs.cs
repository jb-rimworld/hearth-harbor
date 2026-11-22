// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Hearth.Underworld
{
    /// <summary>
    /// Gang guests (5.2 "Shady", 6.11): a group of outlaws arrives peacefully to use the back room, the fight pit and the fence, pays, and maybe leans on you.
    /// Implemented as the module's own incident, independent of the Inn module (which may later route Shady stays through MarkShady).
    /// </summary>
    public static class Gangs
    {
        public static Faction PickFaction()
        {
            var all = Find.FactionManager.AllFactionsVisible.Where(f => !f.IsPlayer && !f.defeated && f.def.humanlikeFaction && !f.HostileTo(Faction.OfPlayer) && !f.def.hidden && f.def.pawnGroupMakers != null).ToList();
            if (all.Count == 0) return null;
            var rough = all.Where(f => f.def.defName.IndexOf("Rough", StringComparison.OrdinalIgnoreCase) >= 0 || f.def.defName.IndexOf("Pirate", StringComparison.OrdinalIgnoreCase) >= 0 || f.def.defName.IndexOf("Outlaw", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            return (rough.Count > 0 ? rough : all).RandomElement();
        }

        public static bool Possible(Map map)
        {
            if (!Underworld.Unlocked || map == null || !map.IsPlayerHome) return false;
            bool anything = BackRoom.HasBackRoom(map) || FenceTrade.FindCounter(map) != null || map.listerThings.ThingsOfDef(UwDefs.FightPit ?? ThingDefOf.Silver).Count > 0;
            return anything && PickFaction() != null;
        }

        public static List<Pawn> Spawn(Map map, Faction faction, int size, out Lord lord)
        {
            lord = null;
            var pawns = new List<Pawn>();
            for (int i = 0; i < size; i++)
            {
                var kind = faction.RandomPawnKind(); if (kind == null) continue;
                var req = new PawnGenerationRequest(kind, faction, PawnGenerationContext.NonPlayer, map.Tile, forceGenerateNewPawn: false, canGeneratePawnRelations: false, mustBeCapableOfViolence: false);
                var p = PawnGenerator.GeneratePawn(req);
                if (p == null || p.DevelopmentalStage.Adult() == false) { if (p != null) Find.WorldPawns.PassToWorld(p, PawnDiscardDecideMode.Discard); continue; }
                pawns.Add(p);
            }
            if (pawns.Count == 0) return pawns;
            if (!RCellFinder.TryFindRandomPawnEntryCell(out var entry, map, CellFinder.EdgeRoadChance_Friendly)) { foreach (var p in pawns) Find.WorldPawns.PassToWorld(p, PawnDiscardDecideMode.Discard); pawns.Clear(); return pawns; }
            foreach (var p in pawns) GenSpawn.Spawn(p, CellFinder.RandomClosewalkCellNear(entry, map, 8), map);
            IntVec3 chill;
            if (!RCellFinder.TryFindRandomSpotJustOutsideColony(pawns[0], out chill)) chill = map.Center;
            var job = new LordJob_VisitColony(faction, chill, 60000);
            lord = LordMaker.MakeNewLord(faction, job, map, pawns);
            var comp = Underworld.Comp; var rng = JRand.Stream("underworld.gang");
            foreach (var p in pawns)
            {
                comp.MarkShady(p);
                UwPay.GiveSilver(p, (int)(HSettings.Get("underworld.gang.feeMax") * 2 + rng.Range(0, 200)));
                Loot.Equip(p, Find.FactionManager.AllFactionsVisible.Where(f => f != faction && !f.IsPlayer).RandomElementWithFallback());
            }
            return pawns;
        }

        public static bool Fire(Map map, out List<Pawn> pawns, bool force = false)
        {
            pawns = null;
            if (!force && !Possible(map)) return false;
            var faction = PickFaction(); if (faction == null) return false;
            int size = JRand.Stream("underworld.gang").Range(2, Math.Max(3, HSettings.GetInt("underworld.gang.sizeMax") + 1));
            pawns = Spawn(map, faction, size, out var lord);
            if (pawns.Count == 0) return false;
            Underworld.Comp.GangVisits++;
            Underworld.AddWhispers(HSettings.Get("underworld.whispers.gang"), "gang visit");
            Find.LetterStack.ReceiveLetter(UwPay.T("Gang.Title"), UwPay.T("Gang.Text", pawns.Count, faction.Name), LetterDefOf.NeutralEvent, pawns[0]);
            HLog.Info("Underworld", $"Gang of {pawns.Count} from {faction.Name} arrived");
            if (JRand.Stream("underworld.gang").Chance(HSettings.Get("underworld.gang.intimidateChance")))
                pending.Add(new Demand { Map = map, Faction = faction, Leader = pawns[0], Tick = Find.TickManager.TicksGame + 25000 });
            return true;
        }

        // ---- protection money ------------------------------------------------------------------------
        sealed class Demand { public Map Map; public Faction Faction; public Pawn Leader; public int Tick; }
        static readonly List<Demand> pending = new List<Demand>();

        public static void TickDemands(int now)
        {
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var d = pending[i]; if (now < d.Tick) continue;
                pending.RemoveAt(i);
                if (d.Leader == null || d.Leader.Dead || !d.Leader.Spawned || !Underworld.Allows(UwFeature.ProtectionRacket)) continue;
                AskProtection(d.Map, d.Faction, d.Leader);
            }
        }

        public static DiaNode AskProtection(Map map, Faction f, Pawn leader)
        {
            var fee = HSettings.GetSilver("underworld.gang.protectionFee");
            var till = Ledger.Current.Balance(Underworld.Till(map));
            var node = new DiaNode(UwPay.T("Gang.Demand.Text", f.Name, fee.ToStringShort()));
            var pay = new DiaOption(UwPay.T("Gang.Demand.Pay", fee.ToStringShort())) { action = () => PayProtection(map, f, fee), resolveTree = true };
            if (till < fee) pay.Disable(UwPay.T("Gang.Demand.NoCash"));
            node.options.Add(pay);
            node.options.Add(new DiaOption(UwPay.T("Gang.Demand.Refuse")) { action = () => RefuseProtection(map, f), resolveTree = true });
            Find.WindowStack.Add(new Dialog_NodeTree(node, true, false, UwPay.T("Gang.Demand.Title")));
            return node;
        }

        public static bool PayProtection(Map map, Faction f, Money fee)
        {
            if (!Ledger.Current.Post(Underworld.Till(map), "world", fee, "Protection money")) return false;
            Underworld.AddWhispers(1f, "paid protection");
            Underworld.AffectGoodwill(f, 3, "paid up");
            return true;
        }

        public static void RefuseProtection(Map map, Faction f)
        {
            Underworld.AddWhispers(2f, "refused protection");
            Underworld.AffectGoodwill(f, -10, "refused protection money");
        }
    }

    public class IncidentWorker_GangVisit : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms) => parms.target is Map m && Gangs.Possible(m);
        protected override bool TryExecuteWorker(IncidentParms parms) => parms.target is Map m && Gangs.Fire(m, out _);
    }

    /// <summary>Gives idle shady guests something to do (works with or without the Inn module): back room, fence, pit, lounge, holding cells.</summary>
    public static class UwDirector
    {
        static readonly Dictionary<int, int> nextAct = new Dictionary<int, int>();

        static bool Idle(Pawn p)
        {
            var d = p.CurJobDef;
            return d == null || d == JobDefOf.Wait_Wander || d == JobDefOf.GotoWander || d == JobDefOf.Wait || d == JobDefOf.Wait_MaintainPosture;
        }

        public static void Tick(Map map)
        {
            if (!Underworld.Unlocked) return;
            int now = Find.TickManager.TicksGame;
            foreach (var p in map.mapPawns.AllPawnsSpawned.ToList())
            {
                if (!p.RaceProps.Humanlike || p.Downed || p.Dead || p.GetLord() == null) continue;
                bool shady = Underworld.IsShady(p), hunter = Underworld.Comp.IsHunter(p);
                if (!shady && !hunter) continue;
                if (!Idle(p) || (nextAct.TryGetValue(p.thingIDNumber, out int n) && now < n)) continue;
                nextAct[p.thingIDNumber] = now + 400;
                Job job = shady ? PickShady(p) : Hunts.PickHunter(p);
                if (job != null) p.jobs.StartJob(job, JobCondition.InterruptForced, null, false, true);
            }
            if (nextAct.Count > 500) nextAct.Clear();
        }

        static Job PickShady(Pawn p)
        {
            var map = p.Map; var info = GuestApi.Info(p); var comp = Underworld.Comp;
            var options = new List<(Func<Job> make, float w)>();
            if (!comp.PaidFee(p) && BackRoom.HasBackRoom(map) && UwDefs.JobBackRoom != null && BackRoom.TryFindCell(map, p, out var cell)) options.Add((() => JobMaker.MakeJob(UwDefs.JobBackRoom, cell), 3f));
            var pit = new Activities.PitActivity(); float wp = pit.Desire(p, info); if (wp > 0) options.Add((() => pit.TryGiveJob(p, info), wp));
            var fence = new Activities.FenceActivity(); float wf = fence.Desire(p, info); if (wf > 0) options.Add((() => fence.TryGiveJob(p, info), wf));
            var lounge = new Activities.LoungeActivity(); float wl = lounge.Desire(p, info); if (wl > 0) options.Add((() => lounge.TryGiveJob(p, info), wl));
            if (Interrogation.Allowed() && comp.PaidFee(p) && UwDefs.JobInterrogate != null) options.Add((() => Interrogation.MakeJob(p), 0.8f));
            for (int tries = 0; tries < 3 && options.Count > 0; tries++)
            {
                float sum = options.Sum(o => o.w), roll = Rand.Value * sum; var pick = options[options.Count - 1];
                foreach (var o in options) { roll -= o.w; if (roll <= 0) { pick = o; break; } }
                var job = Guard.Run(ModuleId.Underworld, "Director.Pick", pick.make, null);
                if (job != null) return job;
                options.Remove(pick);
            }
            return null;
        }
    }

    public class JobDriver_BackRoomVisit : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;
        protected override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            yield return new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Instant,
                initAction = () =>
                {
                    var comp = Underworld.Comp; if (comp == null || comp.PaidFee(pawn)) return;
                    var rng = JRand.Stream("underworld.gang");
                    var fee = Money.FromSilver(rng.Range((int)HSettings.Get("underworld.gang.feeMin"), (int)HSettings.Get("underworld.gang.feeMax") + 1));
                    if (UwPay.Charge(pawn, fee, Underworld.Till(pawn.Map), "underworld.backroom", "Back room fee", ignoreBudget: true)) { comp.NotePaidFee(pawn); UwActivity.Note(pawn.Map); }
                }
            };
            yield return new Toil { defaultCompleteMode = ToilCompleteMode.Delay, defaultDuration = 1800, tickAction = () => { UwPay.Comfort(pawn); } };
        }
    }

    // ---- holding cells tour ------------------------------------------------------------------------------
    public static class Interrogation
    {
        public static bool Allowed() => Underworld.Allows(UwFeature.PrisonInterrogation) && HSettings.GetBool("underworld.gang.prisonAccess");

        public static Pawn FindPrisoner(Pawn visitor)
        {
            return visitor.Map.mapPawns.PrisonersOfColonySpawned.Where(p => !Underworld.IsMinor(p) && !p.Downed && p.health.summaryHealth.SummaryHealthPercent > 0.6f && visitor.CanReach(p, PathEndMode.Touch, Danger.Some)).RandomElementWithFallback();
        }

        public static Job MakeJob(Pawn visitor)
        {
            var pr = FindPrisoner(visitor); if (pr == null) return null;
            return JobMaker.MakeJob(UwDefs.JobInterrogate, pr);
        }

        /// <summary>Runs the outcome of one questioning (also used by tests). Never leaves anything worse than a bruise; nobody dies.</summary>
        public static void Apply(Pawn questioner, Pawn prisoner)
        {
            if (prisoner == null || prisoner.Dead || Underworld.IsMinor(prisoner)) return;
            var fee = HSettings.GetSilver("underworld.gang.tourFee");
            UwPay.Charge(questioner, fee, Underworld.Till(prisoner.Map), "underworld.tour", "Holding cells tour", ignoreBudget: true);
            if (UwDefs.ThoughtInterrogated != null) prisoner.needs?.mood?.thoughts?.memories?.TryGainMemory(UwDefs.ThoughtInterrogated);
            if (Underworld.Allows(UwFeature.PrisonHardQuestioning) && prisoner.health.summaryHealth.SummaryHealthPercent > 0.6f && JRand.Stream("underworld.tour").Chance(0.5))
            {
                var part = prisoner.RaceProps.body.AllParts.Where(bp => bp.def == BodyPartDefOf.Torso).FirstOrDefault();
                if (part != null && UwDefs.Bruise != null) { var h = HediffMaker.MakeHediff(UwDefs.Bruise, prisoner, part); h.Severity = 1f; prisoner.health.AddHediff(h); }
                if (prisoner.guest != null && prisoner.guest.resistance > 0f) prisoner.guest.resistance = Math.Max(0f, prisoner.guest.resistance * 0.85f);
            }
            Underworld.AddWhispers(HSettings.Get("underworld.whispers.tour"), "holding cells tour");
            if (JRand.Stream("underworld.tour").Chance(0.3))
                Find.LetterStack.ReceiveLetter(UwPay.T("Tour.IntelTitle"), UwPay.T("Tour.IntelText", prisoner.LabelShortCap), LetterDefOf.NeutralEvent, prisoner);
            var home = prisoner.HomeFaction;
            if (home != null && !home.IsPlayer && JRand.Stream("underworld.tour").Chance(0.2)) Underworld.AffectGoodwill(home, Underworld.Tier >= 4f ? -10 : -5, "prisoner questioned");
            Underworld.Comp.Tours++;
        }
    }

    public class JobDriver_Interrogate : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            var talk = Toils_General.Wait(2500).WithProgressBarToilDelay(TargetIndex.A);
            talk.tickAction = () => pawn.rotationTracker.FaceTarget(TargetThingA);
            yield return talk;
            yield return new Toil { defaultCompleteMode = ToilCompleteMode.Instant, initAction = () => Interrogation.Apply(pawn, TargetThingA as Pawn) };
        }
    }
}
