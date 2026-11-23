// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Hearth.Underworld
{
    /// <summary>Finds the fenced area around a marker: flood fill over walkable cells; doors, walls and fences are the boundary. Open areas (reaching the map edge or too big) are invalid.</summary>
    public static class Arena
    {
        public sealed class Info { public List<IntVec3> Cells = new List<IntVec3>(); public bool Closed; public IntVec3 Center; public int Builtick; }

        public static Info Flood(Map map, IntVec3 start, int cap)
        {
            var info = new Info { Builtick = Find.TickManager.TicksGame };
            var seen = new HashSet<IntVec3> { start }; var q = new Queue<IntVec3>(); q.Enqueue(start);
            bool leaked = false;
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                info.Cells.Add(c);
                if (info.Cells.Count > cap) { leaked = true; break; }
                for (int i = 0; i < 4; i++)
                {
                    var n = c + GenAdj.CardinalDirections[i];
                    if (!n.InBounds(map)) { leaked = true; continue; }
                    if (seen.Contains(n)) continue;
                    if (!n.Walkable(map) || n.GetDoor(map) != null) continue;
                    seen.Add(n); q.Enqueue(n);
                }
            }
            info.Closed = !leaked;
            if (info.Cells.Count > 0) info.Center = new IntVec3((int)info.Cells.Average(c => c.x), 0, (int)info.Cells.Average(c => c.z));
            return info;
        }

        /// <summary>A spot around the arena where a spectator can stand and see it.</summary>
        public static bool TrySpectatorCell(Map map, Info arena, Pawn p, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            if (arena == null || !arena.Closed) return false;
            var inside = new HashSet<IntVec3>(arena.Cells);
            var ring = new List<IntVec3>();
            foreach (var c in arena.Cells)
                for (int dx = -3; dx <= 3; dx++)
                    for (int dz = -3; dz <= 3; dz++)
                    {
                        var n = new IntVec3(c.x + dx, 0, c.z + dz);
                        if (!n.InBounds(map) || inside.Contains(n) || !n.Standable(map) || n.GetFirstBuilding(map) != null) continue;
                        if (n.GetRoom(map) is Room r && BackRoom.IsBackRoom(r)) continue;
                        ring.Add(n);
                    }
            foreach (var n in ring.Distinct().InRandomOrder())
                if ((p == null || p.CanReach(n, PathEndMode.OnCell, Danger.Some)) && GenSight.LineOfSight(n, arena.Center, map, true)) { cell = n; return true; }
            return false;
        }
    }

    public class CompProperties_FightPit : CompProperties { public CompProperties_FightPit() { compClass = typeof(CompFightPit); } }

    /// <summary>The fight pit marker (6.11): marks a fenced arena. Schedules bouts between volunteers, prisoners and animals; guests bet, the house takes its cut.</summary>
    public class CompFightPit : ThingComp
    {
        public bool enabled = true, allowAnimals = true, allowPrisoners, allowVolunteers, lethal;
        public int nextBoutTick = -1;
        Arena.Info cache;

        public Map Map => parent.Map;
        public Arena.Info ArenaInfo()
        {
            if (!parent.Spawned) return null;
            if (cache == null || Find.TickManager.TicksGame - cache.Builtick > 250) cache = Arena.Flood(parent.Map, parent.Position, 700);
            return cache;
        }
        public void Invalidate() => cache = null;

        public bool ArenaValid => ArenaInfo() is Arena.Info a && a.Closed && a.Cells.Count >= 9;

        public override string CompInspectStringExtra()
        {
            var a = ArenaInfo();
            string s = a == null ? "" : a.Closed ? UwPay.T("Pit.ArenaOk", a.Cells.Count) : UwPay.T("Pit.ArenaOpen");
            var b = FightBout.OfPit(this);
            if (b != null) s += "\n" + UwPay.T("Pit.BoutRunning", b.A?.LabelShortCap ?? "?", b.B?.LabelShortCap ?? "?");
            return s;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (parent.Faction != Faction.OfPlayer) yield break;
            yield return Toggle("Pit.Enabled", () => enabled, v => enabled = v);
            yield return Toggle("Pit.Animals", () => allowAnimals, v => allowAnimals = v);
            yield return Toggle("Pit.Volunteers", () => allowVolunteers, v => allowVolunteers = v);
            yield return Toggle("Pit.Prisoners", () => allowPrisoners, v => allowPrisoners = v);
            if (Underworld.Allows(UwFeature.PitToDeath)) yield return Toggle("Pit.Lethal", () => lethal, v => lethal = v);
            yield return new Command_Action
            {
                defaultLabel = UwPay.T("Pit.StartNow"), defaultDesc = UwPay.T("Pit.StartNow.Desc"), icon = TexCommand.Attack,
                action = () => { if (!FightBout.TryStartFor(this, true, out string why)) Messages.Message(UwPay.T("Pit.CannotStart", why), parent, MessageTypeDefOf.RejectInput, false); }
            };
        }

        Command_Toggle Toggle(string key, Func<bool> get, Action<bool> set) => new Command_Toggle
        {
            defaultLabel = UwPay.T(key), defaultDesc = UwPay.T(key + ".Desc"), icon = TexCommand.Attack,
            isActive = get, toggleAction = () => set(!get())
        };

        public override void CompTickRare()
        {
            if (!parent.Spawned || !enabled || !Underworld.Unlocked) return;
            int now = Find.TickManager.TicksGame;
            if (nextBoutTick < 0) nextBoutTick = now + (int)(HSettings.Get("underworld.pit.intervalHours") * 2500f * 0.5f);
            if (now < nextBoutTick || FightBout.OfPit(this) != null) return;
            if (!GuestApi.GuestsOn(parent.Map).Any(g => !Underworld.IsMinor(g))) return;   // nobody to bet: no show
            if (FightBout.TryStartFor(this, false, out _)) nextBoutTick = now + (int)(HSettings.Get("underworld.pit.intervalHours") * 2500f);
            else nextBoutTick = now + 2500;
        }

        public override void PostExposeData()
        {
            Scribe_Values.Look(ref enabled, "enabled", true); Scribe_Values.Look(ref allowAnimals, "allowAnimals", true);
            Scribe_Values.Look(ref allowPrisoners, "allowPrisoners"); Scribe_Values.Look(ref allowVolunteers, "allowVolunteers");
            Scribe_Values.Look(ref lethal, "lethal"); Scribe_Values.Look(ref nextBoutTick, "nextBout", -1);
        }

        public override void PostDeSpawn(Map map
#if RW16PLUS
            , DestroyMode mode = DestroyMode.Vanish
#endif
            )
        {
            base.PostDeSpawn(map
#if RW16PLUS
                , mode
#endif
                );
            FightBout.OfPit(this)?.Abort("pit removed");
        }
    }

    /// <summary>One bout. Transient (never saved): a bout interrupted by a save/load simply ends, bets are refunded by the caretaker.</summary>
    public sealed class FightBout
    {
        public enum Phase { Assemble, Betting, Fight, Done }

        static readonly List<FightBout> active = new List<FightBout>();
        public static int ActiveCount => active.Count;
        public static IReadOnlyList<FightBout> All => active;
        public static FightBout OfPawn(Pawn p) { for (int i = 0; i < active.Count; i++) if (active[i].A == p || active[i].B == p) return active[i]; return null; }
        public static FightBout OfPit(CompFightPit pit) { for (int i = 0; i < active.Count; i++) if (active[i].Pit == pit) return active[i]; return null; }
        public static FightBout OfMap(Map m) { for (int i = 0; i < active.Count; i++) if (active[i].Map == m && active[i].phase != Phase.Done) return active[i]; return null; }

        public CompFightPit Pit; public Map Map; public Pawn A, B;
        public Phase phase = Phase.Assemble;
        public bool NonLethal = true, Draw;
        public int StartTick, FightTick, bettingEnd, MaxTicks;
        public readonly List<Pawn> Bettors = new List<Pawn>();
        public readonly List<UnderworldMath.Bet> Bets = new List<UnderworldMath.Bet>();
        public UnderworldMath.Settlement Result; public int WinnerSide = -2;
        int lastOrderA, lastOrderB;
        public string PitAccount => Underworld.PitAccount(Map);
        public bool BettingOpen => phase == Phase.Assemble || phase == Phase.Betting;
        public bool Finished => phase == Phase.Done;
        public static event Action<FightBout> Settled;

        // ---- fighters --------------------------------------------------------------------------------
        static bool Fit(Pawn p) => p != null && p.Spawned && !p.Dead && !p.Downed && !p.InMentalState && !Underworld.IsMinor(p) && p.health.summaryHealth.SummaryHealthPercent > 0.8f && !p.IsBurning() && FightBout.OfPawn(p) == null;
        static float Power(Pawn p) => Math.Max(5f, p.kindDef?.combatPower ?? 40f);

        public static List<Pawn> Candidates(CompFightPit pit)
        {
            var map = pit.Map; var list = new List<Pawn>();
            if (pit.allowAnimals) foreach (var p in map.mapPawns.SpawnedColonyAnimals) if (Fit(p) && p.RaceProps.Animal && p.CurJob?.def != JobDefOf.LayDown) list.Add(p);
            if (pit.allowPrisoners) foreach (var p in map.mapPawns.PrisonersOfColonySpawned) if (Fit(p)) list.Add(p);
            if (pit.allowVolunteers)
                foreach (var p in map.mapPawns.FreeColonistsSpawned)
                    if (Fit(p) && !p.Drafted && !p.WorkTagIsDisabled(WorkTags.Violent) && (p.story.traits.HasTrait(TraitDefOf.Bloodlust) || p.skills.GetSkill(SkillDefOf.Melee).Level >= 8)
                        && JRand.Stream("underworld.volunteer").Chance(0.35)) list.Add(p);
            return list;
        }

        public static bool TryStartFor(CompFightPit pit, bool manual, out string why)
        {
            why = null;
            if (!Underworld.Unlocked) { why = "closed"; return false; }
            if (!pit.ArenaValid) { why = UwPay.T("Pit.ArenaOpen"); return false; }
            if (OfPit(pit) != null) { why = "running"; return false; }
            var c = Candidates(pit);
            if (c.Count < 2) { why = UwPay.T("Pit.NoFighters"); return false; }
            float ratio = HSettings.Get("underworld.pit.fairRatio");
            var pairs = new List<(Pawn, Pawn)>();
            for (int i = 0; i < c.Count; i++) for (int j = i + 1; j < c.Count; j++) if (UnderworldMath.FairPair(Power(c[i]), Power(c[j]), ratio)) pairs.Add((c[i], c[j]));
            if (pairs.Count == 0) { why = UwPay.T("Pit.NoFairPair"); return false; }
            var pick = pairs.RandomElement();
            Start(pit, pick.Item1, pick.Item2, false, 600);
            return true;
        }

        public static FightBout Start(CompFightPit pit, Pawn a, Pawn b, bool skipAssemble, int bettingTicks)
        {
            var bout = new FightBout { Pit = pit, Map = pit.Map, A = a, B = b, StartTick = Find.TickManager.TicksGame };
            bout.MaxTicks = HSettings.GetInt("underworld.pit.maxBoutTicks");
            bool humanPair = a.RaceProps.Humanlike || b.RaceProps.Humanlike;
            bool volunteer = (a.Faction == Faction.OfPlayer && a.RaceProps.Humanlike && !a.IsPrisoner) || (b.Faction == Faction.OfPlayer && b.RaceProps.Humanlike && !b.IsPrisoner);
            bout.NonLethal = !(pit.lethal && Underworld.Allows(UwFeature.PitToDeath) && !volunteer && !Underworld.IsMinor(a) && !Underworld.IsMinor(b));
            active.Add(bout);
            if (skipAssemble) { bout.phase = Phase.Betting; bout.bettingEnd = bout.StartTick + bettingTicks; }
            else bout.bettingEnd = bettingTicks;
            Underworld.Comp.Bouts++; UwActivity.Note(bout.Map);
            Underworld.AddWhispers(HSettings.Get("underworld.whispers.pit"), "fight pit bout");
            HLog.Info("Underworld", $"Bout: {a.LabelShortCap} vs {b.LabelShortCap} (non-lethal={bout.NonLethal})");
            Messages.Message(UwPay.T("Pit.BoutAnnounced", a.LabelShortCap, b.LabelShortCap), pit.parent, MessageTypeDefOf.NeutralEvent, false);
            return bout;
        }

        public static void AbortAll(string why) { foreach (var b in active.ToList()) b.Abort(why); }

        // ---- betting -----------------------------------------------------------------------------------
        public bool HasBet(Pawn g) => Bettors.Contains(g);

        public bool TryBet(Pawn guest, int side, int stakeSilver)
        {
            if (!BettingOpen || guest == null || side < 0 || side > 1 || stakeSilver <= 0 || HasBet(guest) || Underworld.IsMinor(guest)) return false;
            if (!UwPay.Charge(guest, Money.FromSilver(stakeSilver), PitAccount, "underworld.bet", "Fight pit: bet")) return false;
            Bettors.Add(guest);
            Bets.Add(new UnderworldMath.Bet { Bettor = Bettors.Count - 1, Side = side, StakeMicro = Money.FromSilver(stakeSilver).Micro });
            Underworld.Comp.Bets++;
            return true;
        }

        public bool AutoBet(Pawn guest)
        {
            if (!BettingOpen || HasBet(guest)) return false;
            var budget = GuestApi.Budget(guest);
            int max = (int)Math.Min(HSettings.Get("underworld.pit.betMax"), budget.Silver / 3.0);
            int min = (int)HSettings.Get("underworld.pit.betMin");
            if (max < min) return false;
            var rng = JRand.Stream("underworld.bet");
            int stake = rng.Range(min, max + 1);
            float share = UnderworldMath.FavouriteShare(Power(A), Power(B));
            bool favouriteIsA = share >= 0.5f, backFavourite = rng.Chance(0.62);
            int side = favouriteIsA == backFavourite ? 0 : 1;
            return TryBet(guest, side, stake);
        }

        public Money Pool { get { long m = 0; foreach (var b in Bets) m += b.StakeMicro; return new Money(m); } }

        // ---- ticking ------------------------------------------------------------------------------------------
        public static void TickAll(int now)
        {
            if (active.Count == 0) return;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var b = active[i];
                try { b.Tick(now); } catch (Exception e) { Guard.Fail(ModuleId.Underworld, "FightBout.Tick", e); b.Abort("error"); }
            }
        }

        bool InArena(Pawn p) { var a = Pit?.ArenaInfo(); return a != null && p.Spawned && a.Cells.Contains(p.Position); }

        void Tick(int now)
        {
            if (phase == Phase.Done) { active.Remove(this); return; }
            if (Pit == null || Pit.parent == null || !Pit.parent.Spawned) { Abort("pit gone"); return; }
            if (A == null || B == null || A.Destroyed || B.Destroyed) { Abort("fighter gone"); return; }
            switch (phase)
            {
                case Phase.Assemble:
                    if (now - StartTick > 3200) { Abort("fighters did not show up"); return; }
                    GoToArena(A, ref lastOrderA, now); GoToArena(B, ref lastOrderB, now);
                    if (InArena(A) && InArena(B)) { phase = Phase.Betting; bettingEnd = now + Math.Max(0, bettingEnd); }
                    break;
                case Phase.Betting:
                    if (now >= bettingEnd) { phase = Phase.Fight; FightTick = now; }
                    break;
                case Phase.Fight:
                    if (EndCheck(now)) return;
                    Order(A, B, ref lastOrderA, now); Order(B, A, ref lastOrderB, now);
                    break;
            }
        }

        void GoToArena(Pawn p, ref int last, int now)
        {
            if (InArena(p) || now - last < 150) return;
            last = now;
            var a = Pit.ArenaInfo(); if (a == null || a.Cells.Count == 0) return;
            var cell = a.Cells.RandomElement();
            if (!p.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
            {
                // animals cannot open fence gates: their handlers lead them in. People who cannot get in simply do not show up (the bout is called off).
                if (p.RaceProps.Animal && !p.Downed) { p.jobs.StopAll(); p.Position = cell; p.Notify_Teleported(true, true); }
                return;
            }
            var job = JobMaker.MakeJob(JobDefOf.Goto, cell); job.locomotionUrgency = LocomotionUrgency.Walk; job.expiryInterval = 600;
            p.jobs.StartJob(job, JobCondition.InterruptForced);
        }

        void Order(Pawn p, Pawn foe, ref int last, int now)
        {
            if (p.Downed || p.Dead || foe.Dead) return;
            var cur = p.CurJob;
            if (cur != null && cur.def == JobDefOf.AttackMelee && cur.targetA.Thing == foe) return;
            if (now - last < 20) return;
            last = now;
            var job = JobMaker.MakeJob(JobDefOf.AttackMelee, foe); job.expiryInterval = 900; job.checkOverrideOnExpire = true;
            p.jobs.StartJob(job, JobCondition.InterruptForced);
        }

        bool Beaten(Pawn p)
        {
            if (p.Dead || p.Downed || p.Destroyed) return true;
            if (!NonLethal) return false;   // to the death: only Downed or dead ends it
            return p.health.summaryHealth.SummaryHealthPercent < HSettings.Get("underworld.pit.yieldHealth");
        }

        /// <summary>The damage guard asks: may this fighter take more damage? Not once beaten in a non-lethal bout.</summary>
        public bool IsNearYield(Pawn p) => phase != Phase.Done && p.health.summaryHealth.SummaryHealthPercent < HSettings.Get("underworld.pit.yieldHealth") + 0.25f;

        public bool ShouldAbsorb(Pawn p) => phase != Phase.Done && NonLethal && (p.Downed || Beaten(p));

        bool EndCheck(int now)
        {
            bool a = Beaten(A), b = Beaten(B);
            if (a && b) { Finish(A.Dead && !B.Dead ? 1 : B.Dead && !A.Dead ? 0 : (A.health.summaryHealth.SummaryHealthPercent <= B.health.summaryHealth.SummaryHealthPercent ? 1 : 0)); return true; }
            if (a) { Finish(1); return true; }
            if (b) { Finish(0); return true; }
            if (now - FightTick > MaxTicks) { Finish(-1); return true; }
            return false;
        }

        public void Finish(int winnerSide)
        {
            if (phase == Phase.Done) return;
            phase = Phase.Done; WinnerSide = winnerSide; Draw = winnerSide < 0;
            foreach (var p in new[] { A, B }) if (p != null && !p.Dead && p.Spawned && p.CurJob?.def == JobDefOf.AttackMelee) p.jobs.EndCurrentJob(JobCondition.InterruptForced, true);
            // a non-lethal bout must not end with a loser who bleeds out afterwards: stop the bleeding of both fighters
            if (NonLethal)
                foreach (var p in new[] { A, B })
                {
                    if (p == null || p.Dead || p.health?.hediffSet == null) continue;
                    try { foreach (var h in p.health.hediffSet.hediffs.ToList()) if (h is Hediff_Injury inj && inj.Bleeding && inj.TendableNow()) inj.Tended(1f, 1f, 0); }
                    catch (Exception e) { HLog.Warn("Underworld", "tending the fighters failed: " + e.Message); }
                }
            Settle(winnerSide);
            WitnessEffects();
            active.Remove(this);
            Settled?.Invoke(this);
        }

        /// <summary>Pays out the totalisator. Pool = payouts + house exactly; the rest of the pit account is swept to the till.</summary>
        void Settle(int winnerSide)
        {
            double cut = HSettings.Get("underworld.pit.houseCut");
            Result = UnderworldMath.Settle(Bets, winnerSide, cut);
            var l = Ledger.Current;
            for (int i = 0; i < Bets.Count; i++)
            {
                var bet = Bets[i]; if (bet.PayoutMicro <= 0) continue;
                var g = Bettors[bet.Bettor];
                if (g == null || g.Destroyed || g.Dead || g.MapHeld == null) { HLog.Info("Underworld", "A winning bettor left; the house keeps the payout"); continue; }
                UwPay.PayOut(g, new Money(bet.PayoutMicro), PitAccount, "Fight pit: payout");
            }
            var rest = UwPay.Balance(PitAccount);
            if (rest.Micro > 0) { l.Post(PitAccount, Underworld.Till(Map), rest, "Fight pit: house take"); Underworld.Comp.HouseTakeMicro += rest.Micro; }
            if (Bets.Count > 0) Messages.Message(UwPay.T(Draw ? "Pit.Draw" : "Pit.Won", (WinnerSide == 0 ? A : B)?.LabelShortCap ?? "-", Result.HouseMicro > 0 ? new Money(Result.HouseMicro).ToStringShort() : "0"), Pit.parent, MessageTypeDefOf.NeutralEvent, false);
            HLog.Info("Underworld", $"Bout settled: winner side {winnerSide}, pool {Result.PoolMicro / 100000.0:0.##}, house {Result.HouseMicro / 100000.0:0.##}, bets {Bets.Count}");
        }

        void WitnessEffects()
        {
            if (Map == null) return;
            var center = Pit.ArenaInfo()?.Center ?? Pit.parent.Position;
            foreach (var p in Map.mapPawns.AllPawnsSpawned)
            {
                if (!p.RaceProps.Humanlike || p.Dead || p == A || p == B || Underworld.IsMinor(p) || p.Position.DistanceTo(center) > 16f) continue;
                if (p.Faction != Faction.OfPlayer && !GuestApi.IsGuest(p)) continue;
                var tr = p.story?.traits; if (tr == null) continue;
                if (tr.HasTrait(TraitDefOf.Kind) && UwDefs.ThoughtSawFightKind != null) p.needs?.mood?.thoughts?.memories?.TryGainMemory(UwDefs.ThoughtSawFightKind);
                if (tr.HasTrait(TraitDefOf.Bloodlust) && UwDefs.ThoughtSawFightBloodlust != null) p.needs?.mood?.thoughts?.memories?.TryGainMemory(UwDefs.ThoughtSawFightBloodlust);
                if (GuestApi.IsGuest(p)) GuestApi.AddSatisfaction(p, tr.HasTrait(TraitDefOf.Bloodlust) ? 0.12f : tr.HasTrait(TraitDefOf.Kind) ? -0.15f : 0.03f, "fight pit");
            }
        }

        /// <summary>Ends the bout without a result: all stakes are returned in full.</summary>
        public void Abort(string why)
        {
            if (phase == Phase.Done) { active.Remove(this); return; }
            HLog.Info("Underworld", "Bout aborted: " + why);
            phase = Phase.Done; Draw = true; WinnerSide = -1;
            foreach (var p in new[] { A, B }) if (p != null && !p.Dead && p.Spawned && p.CurJob?.def == JobDefOf.AttackMelee) p.jobs.EndCurrentJob(JobCondition.InterruptForced, true);
            Result = UnderworldMath.Settle(Bets, -1, 0);
            for (int i = 0; i < Bets.Count; i++) { var g = Bettors[Bets[i].Bettor]; if (g != null && !g.Destroyed && Bets[i].PayoutMicro > 0) UwPay.PayOut(g, new Money(Bets[i].PayoutMicro), PitAccount, "Fight pit: refund"); }
            var rest = UwPay.Balance(PitAccount); if (rest.Micro > 0 && Map != null) Ledger.Current.Post(PitAccount, Underworld.Till(Map), rest, "Fight pit: leftover");
            active.Remove(this);
        }
    }

    // ---- spectators ------------------------------------------------------------------------------------
    public class JobDriver_WatchBout : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;
        protected override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            yield return new Toil { initAction = () => { var b = FightBout.OfMap(pawn.Map); if (b != null && GuestApi.IsGuest(pawn)) b.AutoBet(pawn); }, defaultCompleteMode = ToilCompleteMode.Instant };
            var watch = new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Delay, defaultDuration = 4500,
                tickAction = () =>
                {
                    var b = FightBout.OfMap(pawn.Map);
                    if (b == null) { ReadyForNextToil(); return; }
                    var c = b.Pit.ArenaInfo()?.Center ?? b.Pit.parent.Position;
                    pawn.rotationTracker.FaceCell(c);
                }
            };
            yield return watch;
        }
    }

    public static partial class Activities
    {
        public sealed class PitActivity : IGuestActivity
        {
            public string Id => "underworld.fightpit";
            public ModuleId Module => ModuleId.Underworld;
            public float Desire(Pawn guest, GuestInfo info)
            {
                if (!Underworld.Unlocked || guest.Map == null || Underworld.IsMinor(guest) || UwDefs.JobWatchBout == null) return 0f;
                var b = FightBout.OfMap(guest.Map); if (b == null || !b.BettingOpen || b.HasBet(guest)) return 0f;
                if (info == null || info.Budget < Money.FromSilver(HSettings.Get("underworld.pit.betMin"))) return 0f;
                if (guest.story?.traits?.HasTrait(TraitDefOf.Kind) == true) return 0.15f;
                return guest.story?.traits?.HasTrait(TraitDefOf.Bloodlust) == true ? 3f : 1.4f;
            }
            public Job TryGiveJob(Pawn guest, GuestInfo info)
            {
                var b = FightBout.OfMap(guest.Map); if (b == null) return null;
                if (!Arena.TrySpectatorCell(guest.Map, b.Pit.ArenaInfo(), guest, out var cell)) return null;
                return JobMaker.MakeJob(UwDefs.JobWatchBout, cell);
            }
        }
    }
}
