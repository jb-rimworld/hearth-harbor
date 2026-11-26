// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Hearth.Underworld
{
    public class CompProperties_HuntingGround : CompProperties { public CompProperties_HuntingGround() { compClass = typeof(CompHuntingGround); } }

    /// <summary>
    /// The hunting ground marker (6.11 "The Hunt"): marks a fenced area. Rich hunters pay to hunt animals released into it.
    /// From Dark on a prisoner may be the quarry (the player switches it on at the marker; the ideology condition applies, Grim drops it).
    /// </summary>
    public class CompHuntingGround : ThingComp
    {
        public bool allowPrisoners;
        Arena.Info cache;
        /// <summary>Tiles of a closed ground that is larger than <see cref="Hunts.MaxCells"/> (counted up to <see cref="Hunts.CheckCells"/>), else -1.</summary>
        int tooBig = -1;
        public Arena.Info ArenaInfo()
        {
            if (!parent.Spawned) return null;
            if (cache == null || Find.TickManager.TicksGame - cache.Builtick > 250)
            {
                cache = Arena.Flood(parent.Map, parent.Position, Hunts.MaxCells);
                tooBig = -1;
                // the flood stopped at the cap: look further once to tell a large closed pasture from a gap in the fence
                if (!cache.Closed && cache.Cells.Count > Hunts.MaxCells)
                {
                    var wide = Arena.Flood(parent.Map, parent.Position, Hunts.CheckCells);
                    if (wide.Closed) tooBig = wide.Cells.Count;
                }
            }
            return cache;
        }
        /// <summary>The ground is fenced in but larger than <see cref="Hunts.MaxCells"/>.</summary>
        public bool TooBig => ArenaInfo() != null && tooBig > 0;
        public bool Valid => ArenaInfo() is Arena.Info a && a.Closed && a.Cells.Count >= Hunts.MinCells;

        /// <summary>Why no hunting party can come to this ground (translated), or null when the ground is fine.</summary>
        public string ReasonLine()
        {
            if (!Underworld.Allows(UwFeature.HuntAnimals)) return UwPay.T("Hunt.Closed");
            var a = ArenaInfo(); if (a == null) return null;
            if (!a.Closed) return tooBig > 0 ? UwPay.T("Hunt.TooBig", tooBig, Hunts.MaxCells) : UwPay.T("Hunt.Open", Hunts.MaxCells);
            if (a.Cells.Count < Hunts.MinCells) return UwPay.T("Hunt.TooSmall", a.Cells.Count, Hunts.MinCells);
            return null;
        }

        public override string CompInspectStringExtra()
        {
            var a = ArenaInfo(); if (a == null) return null;
            var why = ReasonLine(); if (why != null) return why;
            var s = UwPay.T("Hunt.GroundOk", a.Cells.Count, allowPrisoners ? UwPay.T("Hunt.QuarryHuman") : UwPay.T("Hunt.QuarryAnimals"));
            var c = Underworld.Comp;
            if (Hunts.Running(parent.Map)) s += "\n" + UwPay.T("Hunt.Running");
            else if (c != null && c.NextHuntTick > Find.TickManager.TicksGame) s += "\n" + UwPay.T("Hunt.Next", (c.NextHuntTick - Find.TickManager.TicksGame).ToStringTicksToPeriod());
            return s;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (parent.Faction != Faction.OfPlayer || !Underworld.Allows(UwFeature.HuntPrisoners)) yield break;
            yield return new Command_Toggle
            {
                defaultLabel = UwPay.T("Hunt.Prisoners"), defaultDesc = UwPay.T("Hunt.Prisoners.Desc"), icon = TexCommand.Attack, isActive = () => allowPrisoners,
                toggleAction = () =>
                {
                    if (allowPrisoners) { allowPrisoners = false; return; }
                    if (!Hunts.HumanQuarryAllowed(out string why)) { Messages.Message(why, parent, MessageTypeDefOf.RejectInput, false); return; }
                    allowPrisoners = true;
                }
            };
        }

        public override void PostExposeData() { Scribe_Values.Look(ref allowPrisoners, "allowPrisoners"); }
    }

    public static class Hunts
    {
        sealed class Session
        {
            public Map Map; public CompHuntingGround Ground; public List<Pawn> Hunters = new List<Pawn>(); public List<Pawn> Quarry = new List<Pawn>();
            public Pawn Human; public int Start, HumanStart; public Money Fee; public bool Done;
        }
        static readonly List<Session> sessions = new List<Session>();
        /// <summary>Smallest closed hunting ground (tiles).</summary>
        public const int MinCells = 60;
        /// <summary>Largest hunting ground (tiles); a closed ground up to <see cref="CheckCells"/> tiles is reported as too big rather than open.</summary>
        public const int MaxCells = 1600, CheckCells = 6000;
        public static bool Running(Map map) { foreach (var s in sessions) if (s.Map == map && s.Ground != null && s.Ground.parent.Spawned && !s.Done) return true; return false; }

        public static CompHuntingGround FindGround(Map map)
        {
            if (map == null || UwDefs.HuntingGround == null) return null;
            foreach (var t in map.listerThings.ThingsOfDef(UwDefs.HuntingGround))
                if (t is ThingWithComps tw && tw.Faction == Faction.OfPlayer && tw.GetComp<CompHuntingGround>() is CompHuntingGround g && g.Valid) return g;
            return null;
        }

        public static bool Possible(Map map) => Underworld.Allows(UwFeature.HuntAnimals) && map != null && map.IsPlayerHome && FindGround(map) != null;

        /// <summary>Ideology gate for hunting people (Dark): the colony's ideology must not forbid it. Grim has no condition. Without Ideology there is no condition.</summary>
        public static bool HumanQuarryAllowed(out string why)
        {
            why = null;
            if (!Underworld.Allows(UwFeature.HuntPrisoners)) { why = UwPay.T("Hunt.GateContent"); return false; }
            if (Underworld.Allows(UwFeature.HuntPrisonersNoPrecept)) return true;
            if (!ModsConfig.IdeologyActive) return true;
            var ok = new HashSet<string> { "Slavery_Acceptable", "Slavery_Honorable", "Cannibalism_Preferred", "Cannibalism_RequiredStrong", "Cannibalism_RequiredRavenous", "Execution_Required" };
            var ideos = Faction.OfPlayer.ideos?.AllIdeos;
            if (ideos != null) foreach (var i in ideos) foreach (var p in i.PreceptsListForReading) if (ok.Contains(p.def.defName)) return true;
            why = UwPay.T("Hunt.GatePrecept"); return false;
        }

        static ThingDef Weapon()
        {
            foreach (var n in new[] { "Gun_BoltActionRifle", "Bow_Recurve", "Bow_Short" }) { var d = DefDatabase<ThingDef>.GetNamedSilentFail(n); if (d != null) return d; }
            return null;
        }

        public static List<Pawn> SpawnQuarry(Map map, CompHuntingGround ground, int count)
        {
            var list = new List<Pawn>();
            var kinds = map.Biome.AllWildAnimals.Where(k => k.combatPower < 140f && k.RaceProps.IsFlesh && !k.RaceProps.predator || k.combatPower < 60f).ToList();
            if (kinds.Count == 0) kinds = DefDatabase<PawnKindDef>.AllDefs.Where(k => k.RaceProps.Animal && k.combatPower < 60f).ToList();
            var cells = ground.ArenaInfo().Cells.Where(c => c.Standable(map)).ToList();
            for (int i = 0; i < count && kinds.Count > 0 && cells.Count > 0; i++)
            {
                // owned by the house: wild animals walk through fences, house animals stay inside the ground
                var p = PawnGenerator.GeneratePawn(kinds.RandomElement(), Faction.OfPlayer);
                GenSpawn.Spawn(p, cells.RandomElement(), map);
                list.Add(p);
            }
            return list;
        }

        public static bool Fire(Map map, out List<Pawn> hunters, bool force = false)
        {
            hunters = null;
            if (!force && !Possible(map)) return false;
            var ground = FindGround(map); if (ground == null) return false;
            var faction = Find.FactionManager.AllFactionsVisible.Where(f => !f.IsPlayer && !f.defeated && f.def.humanlikeFaction && !f.HostileTo(Faction.OfPlayer) && !f.def.hidden && f.def.pawnGroupMakers != null).RandomElementWithFallback();
            if (faction == null) return false;
            int n = JRand.Stream("underworld.hunt").Range(1, 4);
            var pawns = new List<Pawn>();
            var weapon = Weapon();
            for (int i = 0; i < n; i++)
            {
                var kind = faction.RandomPawnKind(); if (kind == null) continue;
                var p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, faction, PawnGenerationContext.NonPlayer, map.Tile, canGeneratePawnRelations: false));
                if (Underworld.IsMinor(p) || p.WorkTagIsDisabled(WorkTags.Violent)) { Find.WorldPawns.PassToWorld(p, PawnDiscardDecideMode.Discard); continue; }
                if (weapon != null) { p.equipment.DestroyAllEquipment(); p.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(weapon, weapon.MadeFromStuff ? GenStuff.RandomStuffFor(weapon) : null)); }
                pawns.Add(p);
            }
            if (pawns.Count == 0) return false;
            if (!RCellFinder.TryFindRandomPawnEntryCell(out var entry, map, CellFinder.EdgeRoadChance_Friendly)) { foreach (var p in pawns) Find.WorldPawns.PassToWorld(p, PawnDiscardDecideMode.Discard); return false; }
            foreach (var p in pawns) GenSpawn.Spawn(p, CellFinder.RandomClosewalkCellNear(entry, map, 8), map);
            var lord = LordMaker.MakeNewLord(faction, new LordJob_VisitColony(faction, ground.parent.Position, 45000), map, pawns);
            var fee = Money.FromSilver(JRand.Stream("underworld.hunt").Range((int)HSettings.Get("underworld.hunt.feeMin"), (int)HSettings.Get("underworld.hunt.feeMax") + 1));
            UwPay.GiveSilver(pawns[0], (int)(fee.Silver * 1.2) + 20);
            var s = new Session { Map = map, Ground = ground, Hunters = pawns, Start = Find.TickManager.TicksGame, Fee = fee };
            foreach (var p in pawns) Underworld.Comp.MarkHunter(p);
            if (!UwPay.Charge(pawns[0], fee, Underworld.Till(map), "underworld.hunt", "The Hunt: fee", ignoreBudget: true)) { fee = Money.Zero; s.Fee = fee; }
            s.Quarry = SpawnQuarry(map, ground, HSettings.GetInt("underworld.hunt.quarryCount"));
            if (ground.allowPrisoners && HumanQuarryAllowed(out _)) TryAddHuman(s);
            sessions.Add(s);
            Underworld.Comp.Hunts++; UwActivity.Note(map); Underworld.Comp.NoteEvent(FacilityStatus.HuntId);
            Underworld.AddWhispers(HSettings.Get("underworld.whispers.hunt"), "the hunt");
            Find.LetterStack.ReceiveLetter(UwPay.T("Hunt.Title"), UwPay.T("Hunt.Text", pawns.Count, fee.ToStringShort(), s.Quarry.Count), LetterDefOf.NeutralEvent, ground.parent);
            hunters = pawns;
            return true;
        }

        static void TryAddHuman(Session s)
        {
            var pr = s.Map.mapPawns.PrisonersOfColonySpawned.Where(p => !Underworld.IsMinor(p) && !p.Downed && p.health.summaryHealth.SummaryHealthPercent > 0.7f).RandomElementWithFallback();
            if (pr == null) return;
            var cells = s.Ground.ArenaInfo().Cells.Where(c => c.Standable(s.Map)).ToList(); if (cells.Count == 0) return;
            pr.jobs.StopAll();
            pr.Position = cells.RandomElement(); pr.Notify_Teleported(true, true);
            s.Human = pr; s.HumanStart = Find.TickManager.TicksGame;
            HLog.Info("Underworld", "A prisoner is the quarry of this hunt: " + pr.LabelShortCap);
        }

        public static Job PickHunter(Pawn p)
        {
            if (UwDefs.JobHunt == null) return null;
            var s = sessions.FirstOrDefault(x => x.Hunters.Contains(p)); if (s == null) return null;
            Pawn target = null;
            if (s.Human != null && !s.Human.Dead && s.Human.Spawned && Find.TickManager.TicksGame - s.HumanStart > 600) target = s.Human;
            if (target == null) target = s.Quarry.Where(q => q != null && q.Spawned && !q.Dead && !q.Destroyed).OrderBy(q => q.Position.DistanceToSquared(p.Position)).FirstOrDefault();
            if (target == null) return null;
            return JobMaker.MakeJob(UwDefs.JobHunt, target);
        }

        /// <summary>Every 60 ticks: keeps a human quarry running, resolves survivals and deaths, ends sessions.</summary>
        public static void Tick(int now)
        {
            for (int i = sessions.Count - 1; i >= 0; i--)
            {
                var s = sessions[i];
                if (s.Map == null || s.Ground == null || !s.Ground.parent.Spawned) { sessions.RemoveAt(i); continue; }
                if (s.Human != null) TickHuman(s, now);
                bool anyHunter = s.Hunters.Any(h => h != null && h.Spawned && !h.Dead);
                bool anyQuarry = s.Quarry.Any(q => q != null && q.Spawned && !q.Dead) || (s.Human != null && !s.Human.Dead && s.Human.Spawned);
                if (!anyHunter || (!anyQuarry && now - s.Start > 3000)) sessions.RemoveAt(i);
            }
        }

        static void TickHuman(Session s, int now)
        {
            var h = s.Human;
            if (h.Dead)
            {
                HumanDied(s);
                s.Human = null; return;
            }
            if (!h.Spawned) { s.Human = null; return; }
            if (now - s.HumanStart > HSettings.Get("underworld.hunt.quarryHours") * 2500f) { HumanSurvived(s); s.Human = null; return; }
            if (UwDefs.JobQuarryFlee != null && h.CurJobDef != UwDefs.JobQuarryFlee && !h.Downed)
            {
                var hunters = s.Hunters.Where(x => x != null && x.Spawned).ToList();
                if (hunters.Count == 0) return;
                var cells = s.Ground.ArenaInfo().Cells;
                var far = cells.OrderByDescending(c => hunters.Min(x => c.DistanceToSquared(x.Position))).Take(12).RandomElement();
                var job = JobMaker.MakeJob(UwDefs.JobQuarryFlee, far); job.locomotionUrgency = LocomotionUrgency.Sprint; job.expiryInterval = 400;
                h.jobs.StartJob(job, JobCondition.InterruptForced);
            }
        }

        static void HumanDied(Session s)
        {
            var h = s.Human;
            HLog.Warn("Underworld", "Human quarry died in the hunt");
            var f = h.HomeFaction ?? h.Faction;
            if (f != null && !f.IsPlayer) Underworld.AffectGoodwill(f, -30, "prisoner killed in a hunt");
            Underworld.RaiseRenown(s.Map, Underworld.CapRenown(-15f), "human hunt");
            Underworld.AddWhispers(5f, "human hunt death");
            foreach (var c in s.Map.mapPawns.FreeColonistsSpawned) if (UwDefs.ThoughtHuntKnown != null) c.needs?.mood?.thoughts?.memories?.TryGainMemory(UwDefs.ThoughtHuntKnown);
            foreach (var g in GuestApi.GuestsOn(s.Map).Where(g => !Underworld.IsShady(g) && !Underworld.Comp.IsHunter(g))) GuestApi.AddSatisfaction(g, -0.6f, "hunt");
            Find.LetterStack.ReceiveLetter(UwPay.T("Hunt.DeathTitle"), UwPay.T("Hunt.DeathText", h.LabelShortCap), LetterDefOf.NegativeEvent);
        }

        static void HumanSurvived(Session s)
        {
            var h = s.Human;
            h.guest?.SetGuestStatus(null);
            if (h.Faction != null && !h.Faction.IsPlayer)
            {
                LordMaker.MakeNewLord(h.Faction, new LordJob_ExitMapBest(LocomotionUrgency.Jog), s.Map, new List<Pawn> { h });
                Underworld.AffectGoodwill(h.Faction, -10, "prisoner hunted and freed");
            }
            Find.LetterStack.ReceiveLetter(UwPay.T("Hunt.FreedTitle"), UwPay.T("Hunt.FreedText", h.LabelShortCap), LetterDefOf.NeutralEvent, h);
        }
    }

    public class IncidentWorker_HuntParty : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms) => parms.target is Map m && Hunts.Possible(m);
        protected override bool TryExecuteWorker(IncidentParms parms) => parms.target is Map m && Hunts.Fire(m, out _);
    }

    /// <summary>Shoots at one quarry animal (or the human quarry) until it is dead. Like vanilla hunting, without designations or hauling the corpse.</summary>
    public class JobDriver_HuntQuarry : JobDriver
    {
        int started = -1;
        Pawn Victim => job.targetA.Thing as Pawn;
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;
        public override void ExposeData() { base.ExposeData(); Scribe_Values.Look(ref started, "started", -1); }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => Victim == null || Victim.Destroyed || !Victim.Spawned);
            yield return new Toil { defaultCompleteMode = ToilCompleteMode.Instant, initAction = () => started = Find.TickManager.TicksGame };
            yield return Toils_Combat.TrySetJobToUseAttackVerb(TargetIndex.A);
            var goto_ = Toils_Combat.GotoCastPosition(TargetIndex.A, TargetIndex.None, true, 0.95f).FailOn(() => Find.TickManager.TicksGame > started + 4000);
            yield return goto_;
            var end = Toils_General.Label();
            yield return Toils_Jump.JumpIf(end, () => Victim.Dead);
            yield return Toils_Jump.JumpIfTargetNotHittable(TargetIndex.A, goto_);
            var cast = Toils_Combat.CastVerb(TargetIndex.A, false);
            yield return cast.FailOn(() => Find.TickManager.TicksGame > started + 4000);
            yield return Toils_Jump.JumpIfTargetDespawnedOrNull(TargetIndex.A, end);
            yield return Toils_Jump.JumpIf(end, () => Victim.Dead);
            yield return Toils_Jump.Jump(goto_);
            yield return end;
        }
    }

    public class JobDriver_QuarryFlee : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;
        protected override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell).FailOn(() => pawn.Downed);
        }
    }
}
