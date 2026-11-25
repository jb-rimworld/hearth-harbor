// SPDX-License-Identifier: MIT
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Hearth.Testing;
using Hearth.Guests;

namespace Hearth.Underworld
{
    /// <summary>Helpers of the fight pit suites (package B). Uses UwKit for content level, tracking and fences.</summary>
    static class PitKit
    {
        /// <summary>A 7x7 fenced arena with a gate on the south side and the marker in the middle.</summary>
        public static CompFightPit Arena(Map map, out CellRect inner, out IntVec3 outside)
        {
            outside = IntVec3.Invalid;
            if (!UwKit.TryFindRect(map, 7, 7, out inner)) return null;
            var outer = inner.ExpandedBy(1);
            UwKit.BuildFence(map, outer);
            var gateCell = new IntVec3(inner.CenterCell.x, 0, outer.minZ);
            gateCell.GetEdifice(map)?.Destroy();
            var gateDef = DefDatabase<ThingDef>.GetNamedSilentFail("FenceGate");
            if (gateDef != null) UwKit.SpawnBuilding(gateDef, gateCell, map);
            map.regionAndRoomUpdater.TryRebuildDirtyRegionsAndRooms();
            outside = new IntVec3(gateCell.x, 0, gateCell.z - 3);
            var marker = UwKit.SpawnBuilding(UwDefs.FightPit, inner.CenterCell, map);
            var pit = marker.TryGetComp<CompFightPit>();
            pit.allowAnimals = false; pit.allowGuests = false; pit.allowPrisoners = false; pit.allowVolunteers = false;
            pit.Invalidate();
            return pit;
        }

        public static void Put(Pawn p, IntVec3 c) { p.jobs?.StopAll(); p.Position = c; p.Notify_Teleported(true, true); }

        /// <summary>An adult colonist who can fight (a generated one when none of the quicktest colonists fits).</summary>
        public static Pawn Fighter(Map map, int skip = 0)
        {
            var p = TestSetup.Colonist(map, skip, x => !x.WorkTagIsDisabled(WorkTags.Violent) && x.health.summaryHealth.SummaryHealthPercent > 0.95f && !x.health.hediffSet.hediffs.Any(h => h is Hediff_Injury));
            if (p == null) return null;
            if (p.Drafted) p.drafter.Drafted = false;
            return p;
        }

        public static Dictionary<Faction, int> Goodwill() => Find.FactionManager.AllFactionsListForReading.Where(f => !f.IsPlayer && f.HasGoodwill).ToDictionary(f => f, f => f.PlayerGoodwill);

        public static string GoodwillDiff(Dictionary<Faction, int> before)
        {
            var diffs = new List<string>();
            foreach (var kv in before) if (kv.Key.PlayerGoodwill != kv.Value) diffs.Add(kv.Key.Name + " " + kv.Value + "->" + kv.Key.PlayerGoodwill);
            return diffs.Count == 0 ? "none" : string.Join(", ", diffs);
        }

        /// <summary>What a fighter is before the bout; compared afterwards (no injury beyond bruises, gear, lord, toil, guilt, HarmedMe).</summary>
        public sealed class Snap
        {
            public Pawn P; public int Injuries; public float InjurySeverity, Bleed; public Thing Weapon; public int Apparel, Items; public Lord Lord; public LordToil Toil; public bool Hostile;
            public static Snap Of(Pawn p) => new Snap
            {
                P = p, Injuries = Inj(p).Count(), InjurySeverity = Inj(p).Sum(h => h.Severity), Bleed = p.health.hediffSet.BleedRateTotal, Weapon = p.equipment?.Primary,
                Apparel = p.apparel?.WornApparelCount ?? 0, Items = NonSilver(p), Lord = p.GetLord(), Toil = p.GetLord()?.CurLordToil, Hostile = p.HostileTo(Faction.OfPlayer)
            };
            static IEnumerable<Hediff> Inj(Pawn p) => p.health.hediffSet.hediffs.Where(h => h is Hediff_Injury && h.def != PitSetup.Bruise);
            static int NonSilver(Pawn p) => p.inventory?.innerContainer?.Count(t => t.def != ThingDefOf.Silver) ?? 0;

            public void Check(TestContext t, string who, Pawn other)
            {
                var p = P;
                t.Check(!p.Dead && !p.Downed && p.Spawned, who + ": alive, up and on the map");
                t.Check(Inj(p).Count() == Injuries && Math.Abs(Inj(p).Sum(h => h.Severity) - InjurySeverity) < 0.01f, who + ": no injury beyond bruises (" + Injuries + " -> " + Inj(p).Count() + ", bruises " + p.health.hediffSet.hediffs.Count(h => h.def == PitSetup.Bruise) + ")");
                t.Check(p.health.hediffSet.BleedRateTotal <= Bleed + 0.0001f, who + ": bleeds no more than before");
                t.Check(p.equipment?.Primary == Weapon && (p.apparel?.WornApparelCount ?? 0) == Apparel && NonSilver(p) == Items, who + ": gear still carried (weapon, " + Apparel + " apparel, " + Items + " items)");
                t.Check(p.GetLord() == Lord && p.GetLord()?.CurLordToil == Toil, who + ": same lord and lord toil (" + Toil?.GetType().Name + " -> " + p.GetLord()?.CurLordToil?.GetType().Name + ")");
                t.Check(p.HostileTo(Faction.OfPlayer) == Hostile, who + ": hostility unchanged");
                t.Check(p.guilt == null || !p.guilt.IsGuilty, who + ": not guilty");
                var mem = p.needs?.mood?.thoughts?.memories?.Memories;
                t.Check(mem == null || !mem.Any(m => m.def == ThoughtDefOf.HarmedMe && m.otherPawn == other), who + ": no HarmedMe memory of the other fighter");
                t.Check(p.mindState?.meleeThreat == null, who + ": no melee threat left (no retaliation after the bout)");
                t.Check(!PitSetup.IsPitJob(p.CurJobDef), who + ": the pit job ended");
                t.Check(!p.InMentalState, who + ": no mental state (no panic flee)");
            }
        }

        /// <summary>Waits until the bout is over; notes how long the fight phase took.</summary>
        public static IEnumerable<int> Await(TestContext t, FightBout b, int max = 12000)
        {
            int waited = 0;
            while (!b.Finished && waited < max) { yield return 60; waited += 60; }
            t.Note($"bout {b.A?.LabelShort} vs {b.B?.LabelShort}: finished={b.Finished} after {waited} ticks, fight {(b.FightTick > 0 ? Find.TickManager.TicksGame - b.FightTick : -1)} ticks, points {b.ScoreA:0}/{b.LimitA:0} vs {b.ScoreB:0}/{b.LimitB:0}, winner {b.WinnerSide}");
        }

        /// <summary>One Inn day guest (registered stay) when the Inn is on, else null.</summary>
        public static IEnumerable<int> InnGuest(Map map, Faction f, List<Pawn> into)
        {
            if (!Modules.On(ModuleId.Inn) || GuestRegistry.Instance == null || f == null) yield break;
            GuestGroup g = null;
            try { g = GuestArrival.TryFire(map, new GuestArrivalRequest { Faction = f, Size = 1, Kind = StayKind.DayVisitor, Silent = true, IgnoreDoorPolicy = true, StayTicksOverride = 40000, ForceBudget = Money.FromSilver(300) }); }
            catch (Exception e) { Log.Warning("[HHTEST] inn guest failed: " + e.Message); }
            if (g == null) yield break;
            int waited = 0;
            Pawn p = null;
            while (waited < 2000) { p = GuestRegistry.Instance.Members(g).FirstOrDefault()?.Pawn; if (p != null && p.Spawned) break; yield return 60; waited += 60; }
            if (p != null && p.Spawned)
            {
                if (!p.DevelopmentalStage.Adult()) { p.ageTracker.AgeBiologicalTicks = 25L * 3600000L; p.ageTracker.AgeChronologicalTicks = 25L * 3600000L; }
                UwKit.Track(p); into.Add(p);
            }
        }
    }

    /// <summary>The safety proof (B.6 + Addendum 1): a guest against a colonist and a guest against a guest, both scored: goodwill of every faction
    /// exactly as before, nobody hurt beyond bruises, same lord and toil, no guilt, no HarmedMe, gear carried, stay records still there; bets settle,
    /// the purse comes out of the house take, the ledger balances.</summary>
    public sealed class PitSafetySuite : HearthTestSuite
    {
        public override string Name => "underworld.pit.safety";
        public override ModuleId Module => ModuleId.Underworld;
        public override int MaxTicks => 40000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var f = UwKit.Friendly();
            UwKit.Begin(ContentIntensity.Standard);
            CellRect inner = default; IntVec3 outside = IntVec3.Invalid; CompFightPit pit = null;
            if (f != null) pit = PitKit.Arena(map, out inner, out outside);
            if (pit == null) { t.Note("no friendly faction or free area; skipped"); UwKit.End(); yield break; }
            t.Check(pit.ArenaValid, "arena valid");
            var cells = pit.ArenaInfo().Cells;
            // ---- 1: guest (an Inn stay when the Inn is on) against a colonist
            var col = PitKit.Fighter(map);
            var inn = new List<Pawn>();
            foreach (var w in PitKit.InnGuest(map, f, inn)) yield return w;
            Pawn guest = inn.FirstOrDefault();
            bool innStay = guest != null;
            if (guest == null) guest = UwKit.Guests(map, 1, f)[0];
            t.Note("guest " + guest?.LabelShort + (innStay ? " (Inn stay, lord " + guest.GetLord()?.LordJob?.GetType().Name + ")" : " (vanilla visitor)") + ", colonist " + col?.LabelShort);
            if (col != null && guest != null)
            {
                PitKit.Put(col, cells[3]); PitKit.Put(guest, cells[45]);
                yield return 30;
                var gw = PitKit.Goodwill();
                var sc = PitKit.Snap.Of(col); var sg = PitKit.Snap.Of(guest);
                var bout = FightBout.Start(pit, col, guest, true, 60, manual: true);
                t.Check(bout != null && bout.Scored && bout.NonLethal, "guest against colonist is a scored bout");
                if (bout != null)
                {
                    foreach (var w in PitKit.Await(t, bout)) yield return w;
                    t.Check(bout.Finished && bout.Result != null, "the bout came to a result");
                    t.Check(bout.ScoreA + bout.ScoreB > 0f, "hits were scored: " + bout.ScoreA.ToString("0") + " / " + bout.ScoreB.ToString("0"));
                    yield return 600;   // the after-bout window in which a victim would swing back for real
                    t.Check(PitKit.GoodwillDiff(gw) == "none", "goodwill of every faction unchanged: " + PitKit.GoodwillDiff(gw));
                    sc.Check(t, "colonist", guest);
                    sg.Check(t, "guest", col);
                    t.Check(GuestApi.Info(guest) != null && (!innStay || GuestApi.Impl?.Info(guest) != null), "the guest's stay record still exists");
                    t.Check(!PitOpponents.IsReturning(col) && !PitOpponents.IsReturning(guest), "nobody waits to be taken back");
                }
            }
            else t.Note("no colonist who can fight; part 1 skipped");

            // ---- 2: guest against guest with bets
            var gs = UwKit.Guests(map, 4, f);
            int arrive = 0;
            while (arrive < 4000 && !gs.All(PitFighters.StayToil)) { yield return 100; arrive += 100; }
            t.Check(gs.All(PitFighters.StayToil), "the visitors arrived and stay (" + arrive + " ticks): " + string.Join(", ", gs.Select(g => g.GetLord()?.CurLordToil?.GetType().Name)));
            PitKit.Put(gs[0], cells[10]); PitKit.Put(gs[1], cells[38]);
            foreach (var b2 in gs.Skip(2)) PitKit.Put(b2, CellFinder.RandomClosewalkCellNear(outside, map, 3));
            yield return 30;
            var gw2 = PitKit.Goodwill();
            var s0 = PitKit.Snap.Of(gs[0]); var s1 = PitKit.Snap.Of(gs[1]);
            int silver0 = UwKit.Silver(gs[0]), silver1 = UwKit.Silver(gs[1]);
            var till0 = UwKit.Till(map); long take0 = Underworld.Comp.HouseTakeMicro;
            var gg = FightBout.Start(pit, gs[0], gs[1], true, 600, manual: true);
            t.Check(gg != null && gg.Scored, "guest against guest is scored");
            if (gg != null)
            {
                t.Check(gg.TryBet(gs[2], 0, 100) && gg.TryBet(gs[3], 1, 100), "two bets placed");
                t.Check(!gg.TryBet(gs[0], 1, 10), "a fighter cannot bet on his own bout");
                foreach (var w in PitKit.Await(t, gg)) yield return w;
                yield return 600;
                t.Check(gg.Finished && gg.Result != null, "guest bout came to a result");
                t.Check(PitKit.GoodwillDiff(gw2) == "none", "goodwill unchanged after the guest bout: " + PitKit.GoodwillDiff(gw2));
                s0.Check(t, "guest A", gs[1]); s1.Check(t, "guest B", gs[0]);
                var r = gg.Result;
                long payouts = gg.Bets.Sum(x => x.PayoutMicro);
                t.Check(r.PoolMicro == Money.FromSilver(200).Micro && r.PoolMicro == payouts + r.HouseMicro, "pool = payouts + house (" + r.PoolMicro + " = " + payouts + " + " + r.HouseMicro + ")");
                if (!gg.Draw)
                {
                    var winner = gg.WinnerSide == 0 ? gs[0] : gs[1];
                    int gain = UwKit.Silver(winner) - (gg.WinnerSide == 0 ? silver0 : silver1);
                    var expected = Math.Min(HSettings.GetSilver("underworld.pit.guestPurse").WholeSilver, new Money(r.HouseMicro).WholeSilver);
                    t.Check(gg.PursePaid.WholeSilver == expected && gain == expected, "the winning guest got his purse out of the house take: " + gain + " (expected " + expected + ")");
                    t.Check(UwKit.Till(map) - till0 == new Money(r.HouseMicro) - gg.PursePaid, "till got the house take minus the purse");
                    t.Check(Underworld.Comp.HouseTakeMicro - take0 == (new Money(r.HouseMicro) - gg.PursePaid).Micro, "house take statistic is net of the purse");
                }
                t.Check(Ledger.Current.Balance(Underworld.PitAccount(map)).Micro == 0 && Ledger.Current.SumAll() == 0, "ledger balanced, pit account empty");
                t.Check(gs.Take(2).All(g => GuestApi.Info(g) != null), "both guests are still guests");
                t.Check(FightBout.LastMessage != null && FightBout.LastMessage.Contains(gs[0].LabelShortCap) && !FightBout.LastMessage.Contains("HH.Underworld"), "result message: " + FightBout.LastMessage);
                t.Check(pit.LastBoutLine() != null && PitRecords.EverSettled(map), "last bout kept: " + pit.LastBoutLine());
            }
            UwKit.End();
        }
    }

    /// <summary>A lone colonist, no guests, no animals: "Call an opponent" hires a pit beast; the bout runs to a result, the fee is booked, the beast is gone.</summary>
    public sealed class PitLoneSuite : HearthTestSuite
    {
        public override string Name => "underworld.pit.lone";
        public override ModuleId Module => ModuleId.Underworld;
        public override int MaxTicks => 30000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map;
            UwKit.Begin(ContentIntensity.Standard);
            var pit = PitKit.Arena(map, out var inner, out var outside);
            var col = PitKit.Fighter(map);
            if (pit == null || col == null) { t.Note("no area or no colonist who can fight; skipped"); UwKit.End(); yield break; }
            pit.SetTicked(col, true);
            PitKit.Put(col, CellFinder.RandomClosewalkCellNear(outside, map, 2));
            var ready = pit.Ready(true);
            t.Check(ready.Count == 1 && ready[0].Pawn == col, "only the ticked colonist is ready: " + string.Join(", ", ready.Select(c => c.Pawn.LabelShort)));
            t.Check(pit.StatusLine() == UwPay.T("Pit.Status.WaitFighter"), "status says it waits for a second fighter: " + pit.StatusLine());
            t.Check(pit.LoneFighter() == col, "the lone fighter is the ticked colonist");
            var fee = PitOpponents.Fee;
            // an empty till disables the gizmo with the reason
            var tillAcc = Underworld.Till(map); var have = Ledger.Current.Balance(tillAcc);
            if (have.Micro > 0) Ledger.Current.Post(tillAcc, "world", have, "test: empty till");
            t.Check(PitOpponents.CannotHire(pit, col) == UwPay.T("Pit.Why.TillShort", fee.ToStringShort()), "empty till: " + PitOpponents.CannotHire(pit, col));
            Payments.CreditColony(map, Money.FromSilver(100), "test");
            var till0 = UwKit.Till(map);
            var gw = PitKit.Goodwill(); var sc = PitKit.Snap.Of(col);
            var bout = PitOpponents.HireBeast(pit, col, out string why);
            t.Check(bout != null, "a pit beast was hired (" + why + ")");
            if (bout == null) { UwKit.End(); yield break; }
            var beast = bout.B;
            t.Check(PitOpponents.IsBeast(beast) && beast.Faction == Faction.OfPlayer && pit.ArenaInfo().Cells.Contains(beast.Position), "beast in the arena, the house's: " + beast.kindDef.defName + " power " + beast.kindDef.combatPower + " vs " + PitFighters.Power(col));
            t.Check(UwKit.Till(map) == till0 - fee, "fee booked from the till: " + (till0 - UwKit.Till(map)).ToStringShort());
            t.Check(!new Designator_Slaughter().CanDesignateThing(beast).Accepted, "the beast cannot be slaughtered");
            t.Check(!TradeUtility.AllSellableColonyPawns(map).Contains(beast), "the beast cannot be sold");
            t.Check(bout.Scored, "colonist against beast is scored");
            foreach (var w in PitKit.Await(t, bout, 15000)) yield return w;
            t.Check(bout.Finished && (bout.Result != null), "the bout ran to a result");
            t.Check(FightBout.LastMessage != null && FightBout.LastMessage.Contains(col.LabelShortCap), "result message: " + FightBout.LastMessage);
            yield return 300;
            t.Check(beast.Destroyed && !beast.Spawned && !PitOpponents.IsBeast(beast) && !pit.HasBeast && !map.mapPawns.AllPawns.Contains(beast), "the beast is gone (destroyed " + beast.Destroyed + ", spawned " + beast.Spawned + ")");
            t.Check(beast.Corpse == null || beast.Corpse.Destroyed, "no corpse left");
            sc.Check(t, "colonist", beast);
            t.Check(PitKit.GoodwillDiff(gw) == "none", "goodwill unchanged: " + PitKit.GoodwillDiff(gw));
            t.Check(Ledger.Current.SumAll() == 0, "ledger balanced");
            // an aborted bout sends the beast away too
            Payments.CreditColony(map, Money.FromSilver(100), "test");
            var b2 = PitOpponents.HireBeast(pit, col, out why);
            t.Check(b2 != null, "second beast hired (" + why + ")");
            if (b2 != null)
            {
                var beast2 = b2.B;
                yield return 120;
                b2.Abort("test", UwPay.T("Pit.Abort.Closed"));
                yield return 300;
                t.Check(beast2.Destroyed && !pit.HasBeast, "aborted: the beast is gone");
                t.Check(FightBout.LastMessage == UwPay.T("Pit.CalledOff", UwPay.T("Pit.Abort.Closed")), "called-off message: " + FightBout.LastMessage);
            }
            pit.SetTicked(col, false);
            UwKit.End();
        }
    }

    /// <summary>A ticked colonist against a colony animal, started by hand from the window's sides, no guests on the map.</summary>
    public sealed class PitManualSuite : HearthTestSuite
    {
        public override string Name => "underworld.pit.manual";
        public override ModuleId Module => ModuleId.Underworld;
        public override int MaxTicks => 30000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map;
            UwKit.Begin(ContentIntensity.Standard);
            var pit = PitKit.Arena(map, out var inner, out var outside);
            var col = PitKit.Fighter(map);
            if (pit == null || col == null) { t.Note("no area or no colonist who can fight; skipped"); UwKit.End(); yield break; }
            pit.allowAnimals = true;
            var animal = UwKit.TameAnimal(map, CellFinder.RandomClosewalkCellNear(outside, map, 2), "Boar", "Muffalo");
            PitKit.Put(col, CellFinder.RandomClosewalkCellNear(outside, map, 2));
            pit.SetTicked(col, true);
            t.Check(!GuestApi.GuestsOn(map).Any(), "no guests on the map");
            pit.pickA = col; pit.pickB = animal;
            var sc = PitKit.Snap.Of(col); var sa = PitKit.Snap.Of(animal);
            t.Check(FightBout.TryStartFor(pit, true, out string why), "manual start without guests (" + why + ")");
            var bout = FightBout.OfPit(pit);
            t.Check(bout != null && bout.A == col && bout.B == animal && bout.Scored, "the picked sides fight, scored");
            if (bout != null)
            {
                t.Check(pit.StatusLine() == UwPay.T("Pit.Status.Running", col.LabelShortCap, animal.LabelShortCap), "status: " + pit.StatusLine());
                foreach (var w in PitKit.Await(t, bout, 15000)) yield return w;
                t.Check(bout.Finished, "the bout finished (walked in through the gate: " + (bout.FightTick > 0) + ")");
                yield return 300;
                sc.Check(t, "colonist", animal);
                t.Check(!animal.Dead && !animal.Downed && animal.health.hediffSet.hediffs.Count(h => h is Hediff_Injury && h.def != PitSetup.Bruise) == sa.Injuries, "animal unhurt beyond bruises");
            }
            pit.pickA = pit.pickB = null; pit.SetTicked(col, false);
            UwKit.End();
        }
    }

    /// <summary>Reasons, the cache, the old allowVolunteers field and the saved fields of the pit.</summary>
    public sealed class PitFightersSuite : HearthTestSuite
    {
        public override string Name => "underworld.pit.fighters";
        public override ModuleId Module => ModuleId.Underworld;
        public override int MaxTicks => 24000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var f = UwKit.Friendly();
            UwKit.Begin(ContentIntensity.Standard);
            var pit = PitKit.Arena(map, out var inner, out var outside);
            var col = PitKit.Fighter(map);
            if (pit == null || col == null) { t.Note("no area or no colonist; skipped"); UwKit.End(); yield break; }
            // drafted
            col.drafter.Drafted = true;
            var c = PitFighters.Colonist(pit, col);
            t.Check(c.Reason == UwPay.T("Pit.Why.Drafted") && !c.ManualOk, "drafted colonist: " + c.Reason);
            col.drafter.Drafted = false;
            c = PitFighters.Colonist(pit, col);
            t.Check(c.Reason == UwPay.T("Pit.Why.NotTicked") && c.SoftReason && c.ManualOk, "unticked colonist: soft reason " + c.Reason);
            pit.SetTicked(col, true);
            t.Check(PitFighters.Colonist(pit, col).Ready, "ticked colonist is ready");
            // the legacy volunteer field: a melee colonist fights without a tick
            pit.SetTicked(col, false);
            int oldMelee = col.skills.GetSkill(SkillDefOf.Melee).Level;
            col.skills.GetSkill(SkillDefOf.Melee).Level = 10;
            pit.allowVolunteers = true;
            t.Check(PitFighters.Colonist(pit, col).Ready, "allowVolunteers=true (old save): a melee-10 colonist is ready without a tick, no random roll");
            pit.allowVolunteers = false;
            t.Check(!PitFighters.Colonist(pit, col).Ready, "allowVolunteers=false: he needs the tick");
            col.skills.GetSkill(SkillDefOf.Melee).Level = oldMelee;
            // guests: kind, no taste, shady always
            if (f != null)
            {
                var gs = UwKit.Guests(map, 3, f);
                int arrive = 0;
                while (arrive < 4000 && !gs.All(PitFighters.StayToil)) { yield return 100; arrive += 100; }
                var kind = gs[0]; var plain = gs[1]; var shady = gs[2];
                foreach (var g in gs) PitKit.Put(g, CellFinder.RandomClosewalkCellNear(outside, map, 4));
                if (!kind.story.traits.HasTrait(TraitDefOf.Kind)) kind.story.traits.GainTrait(new Trait(TraitDefOf.Kind));
                foreach (var tr in plain.story.traits.allTraits.Where(x => x.def == TraitDefOf.Bloodlust || x.def == TraitDefOf.Brawler).ToList()) plain.story.traits.RemoveTrait(tr);
                plain.skills.GetSkill(SkillDefOf.Melee).Level = 2;
                Underworld.MarkShady(shady); shady.skills.GetSkill(SkillDefOf.Melee).Level = 0;
                foreach (var g in gs) g.jobs.StopAll();
                yield return 5;
                var rk = PitFighters.Guest(kind); var rp = PitFighters.Guest(plain); var rs = PitFighters.Guest(shady);
                t.Check(kind.WorkTagIsDisabled(WorkTags.Violent) || rk.Reason == UwPay.T("Pit.Why.Kind"), "kind guest: " + rk.Reason);
                t.Check(plain.WorkTagIsDisabled(WorkTags.Violent) || rp.Reason == UwPay.T("Pit.Why.NoTaste"), "guest without taste: " + rp.Reason);
                t.Check(shady.WorkTagIsDisabled(WorkTags.Violent) || rs.Reason == null || rs.Reason == UwPay.T("Pit.Why.Busy") || rs.Reason == UwPay.T("Pit.Why.Asleep"), "shady guest needs no trait or skill: " + (rs.Reason ?? "ready"));
                // cache: the list is not rebuilt within 250 ticks unless asked
                pit.allowGuests = true;
                var l1 = pit.Candidates(true); var l2 = pit.Candidates();
                t.Check(ReferenceEquals(l1, l2), "candidate list cached");
                t.Check(l1.Any(x => x.Pawn == kind && x.Kind == PitKind.Guest), "guests listed when allowed");
                pit.SetTicked(col, true);
                t.Check(!ReferenceEquals(l1, pit.Candidates()), "ticking a box refreshes the cache");
                pit.SetTicked(col, false);
                // guests alone are enough: two willing guests fight each other in an automatic bout (they walk in through the gate)
                var extra = UwKit.Guests(map, 1, f, shady: true)[0];
                arrive = 0;
                while (arrive < 4000 && !PitFighters.StayToil(extra)) { yield return 100; arrive += 100; }
                PitKit.Put(extra, CellFinder.RandomClosewalkCellNear(outside, map, 4));
                Underworld.MarkShady(kind); kind.story.traits.allTraits.RemoveAll(x => x.def == TraitDefOf.Kind);
                foreach (var g in new[] { extra, shady }) g.jobs.StopAll();
                var willing = new[] { extra, shady }.Where(g => !g.WorkTagIsDisabled(WorkTags.Violent)).ToList();
                pit.allowAnimals = false;
                var ready = pit.Ready(true);
                t.Note("ready for an automatic bout: " + string.Join(", ", ready.Select(c => c.Pawn.LabelShort + "(" + c.Kind + ")")) + "; reasons: " + string.Join(", ", pit.Candidates().Where(c => c.Kind == PitKind.Guest && !c.Ready).Select(c => c.Pawn.LabelShort + "=" + c.Reason)));
                if (willing.Count == 2 && ready.Count(c => c.Kind == PitKind.Guest) >= 2)
                {
                    pit.nextBoutTick = Find.TickManager.TicksGame;
                    pit.CompTickRare();
                    var auto = FightBout.OfPit(pit);
                    t.Check(auto != null && auto.KindA == PitKind.Guest && auto.KindB == PitKind.Guest && !auto.Manual, "an automatic bout between two guests started");
                    if (auto != null)
                    {
                        int w2 = 0;
                        while (w2 < 4000 && auto.phase == FightBout.Phase.Assemble && !auto.Finished) { yield return 60; w2 += 60; }
                        t.Check(auto.phase != FightBout.Phase.Assemble && !(auto.Finished && auto.Result == null), "both guests walked into the arena (" + w2 + " ticks, phase " + auto.phase + ", last message " + FightBout.LastMessage + ")");
                        auto.Abort("test", null);
                    }
                }
                else t.Note("fewer than two willing guests (pacifist roll or busy); automatic guest bout skipped");
            }
            // saved fields (values only; references are covered by the save/load suites)
            var oldTicked = pit.Ticked.ToList(); foreach (var p in oldTicked) pit.SetTicked(p, false);
            pit.allowVolunteers = true; pit.allowGuests = false; pit.lastA = "Alpha"; pit.lastB = "Beta"; pit.lastWinner = 1; pit.lastTick = 1234; pit.lastPoolMicro = 8400000; pit.lastHouseMicro = 800000; pit.settled = 3;
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hh_uw_pit_comp.xml");
            var copy = new CompFightPit { parent = pit.parent };
            bool ok = true; string err = null;
            try
            {
                Scribe.saver.InitSaving(path, "PitTest"); pit.PostExposeData(); Scribe.saver.FinalizeSaving();
                Scribe.loader.InitLoading(path); Scribe.EnterNode("PitTest"); copy.PostExposeData(); Scribe.ExitNode(); Scribe.loader.FinalizeLoading();
            }
            catch (Exception e) { ok = false; err = e.Message; try { Scribe.ForceStop(); } catch { } }
            t.Check(ok, "pit fields written and read back " + err);
            t.Check(copy.allowVolunteers && !copy.allowGuests && copy.lastA == "Alpha" && copy.lastWinner == 1 && copy.lastPoolMicro == 8400000 && copy.settled == 3, "old allowVolunteers, new allowGuests and the last bout survive");
            t.Check(copy.LastBoutLine() == UwPay.T("Pit.Last", "Beta", "Alpha", new Money(8400000).ToStringShort(), new Money(800000).ToStringShort()), "last bout line: " + copy.LastBoutLine());
            try { System.IO.File.Delete(path); } catch { }
            foreach (var p in oldTicked) pit.SetTicked(p, true);
            UwKit.End();
        }
    }

    /// <summary>The inspect string: the right status line in each waiting state, keyed texts only (run in English and German: no raw key).</summary>
    public sealed class PitStatusSuite : HearthTestSuite
    {
        public override string Name => "underworld.pit.status";
        public override ModuleId Module => ModuleId.Underworld;
        public override int MaxTicks => 8000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var f = UwKit.Friendly();
            UwKit.Begin(ContentIntensity.Standard);
            var pit = PitKit.Arena(map, out var inner, out var outside);
            if (pit == null) { t.Note("no area; skipped"); UwKit.End(); yield break; }
            void Expect(string key, string what, params NamedArgument[] args)
            {
                string want = UwPay.T(key, args), got = pit.StatusLine(), insp = pit.CompInspectStringExtra();
                t.Check(got == want, what + ": " + got);
                t.Check(!insp.Contains("HH.Underworld.") && insp.Contains(want), what + ": inspect string has no raw key and shows the line");
                t.Check(!want.StartsWith("HH."), what + ": key translated (" + key + ")");
            }
            // no fighters at all
            Expect("Pit.Status.WaitFighter", "no fighter");
            var col = PitKit.Fighter(map); var col2 = PitKit.Fighter(map, 1);
            if (col != null && col2 != null && col != col2)
            {
                pit.SetTicked(col, true); pit.SetTicked(col2, true); pit.Invalidate();
                bool guests = GuestApi.GuestsOn(map).Any(g => !Underworld.IsMinor(g));
                if (!guests) Expect("Pit.Status.WaitGuests", "two fighters, no guests");
                if (f != null)
                {
                    UwKit.Guests(map, 1, f); pit.Invalidate();
                    pit.nextBoutTick = Find.TickManager.TicksGame + 2500 * 3;
                    Expect("Pit.Status.Next", "two fighters and a guest", "3.0");
                }
                t.Check(pit.ReadyLine() == UwPay.T("Pit.Ready", 2, UwPay.T("Pit.Ready.Many.Colonist", 2)), "ready line: " + pit.ReadyLine());
                pit.SetTicked(col, false); pit.SetTicked(col2, false);
            }
            else t.Note("fewer than two colonists; waiting-for-guests and next-bout lines skipped");
            pit.enabled = false; pit.Invalidate(); Expect("Pit.Status.Closed", "closed"); pit.enabled = true;
            HSettings.Set("core.peacefulUnderworld", 1f); Expect("Pit.Status.Peaceful", "peaceful"); HSettings.Set("core.peacefulUnderworld", 0f);
            HearthProfile.Current.Content = ContentIntensity.Cozy; Underworld.MarkInUseDirty(null);
            t.Check(pit.StatusLine() == UwPay.T("Pit.Status.Off"), "Cozy: " + pit.StatusLine());
            t.Check(!pit.CompGetGizmosExtra().Any(), "Cozy: no gizmos");
            HearthProfile.Current.Content = ContentIntensity.Standard;
            // the gizmos all have their own icons and keyed labels
            var gizmos = pit.CompGetGizmosExtra().OfType<Command>().ToList();
            t.Check(gizmos.Count >= 7 && gizmos.All(g => !g.defaultLabel.StartsWith("HH.") && !g.defaultDesc.NullOrEmpty() && !g.defaultDesc.StartsWith("HH.")), "gizmos keyed: " + string.Join(" | ", gizmos.Select(g => g.defaultLabel)));
            t.Check(gizmos.Select(g => g.icon).Distinct().Count() == gizmos.Count, "every gizmo has its own icon");
            // an open arena
            var gap = new IntVec3(inner.minX, 0, inner.ExpandedBy(1).maxZ); gap.GetEdifice(map)?.Destroy(); pit.Invalidate();
            Expect("Pit.Status.NeedArena", "open arena");
            t.Check(pit.CompInspectStringExtra().Contains(UwPay.T("Pit.ArenaOpen")), "arena line says to close the fence");
            yield return 1;
            UwKit.End();
        }
    }

    /// <summary>The Fighters window opens, lists the candidates with reasons, and the right-click option of the marker; screenshot with HH_UI_SHOTS.</summary>
    public sealed class PitWindowSuite : HearthTestSuite
    {
        public override string Name => "underworld.pit.window";
        public override ModuleId Module => ModuleId.Underworld;
        public override int MaxTicks => 6000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var f = UwKit.Friendly();
            UwKit.Begin(ContentIntensity.Standard);
            var pit = PitKit.Arena(map, out var inner, out var outside);
            var col = PitKit.Fighter(map);
            if (pit == null || col == null) { t.Note("no area or colonist; skipped"); UwKit.End(); yield break; }
            pit.allowAnimals = true; pit.allowGuests = true;
            UwKit.TameAnimal(map, CellFinder.RandomClosewalkCellNear(outside, map, 3), "Boar", "Muffalo");
            if (f != null) foreach (var g in UwKit.Guests(map, 2, f)) PitKit.Put(g, CellFinder.RandomClosewalkCellNear(outside, map, 4));
            pit.SetTicked(col, true);
            var w = Window_PitFighters.Open(pit);
            Hearth.Shell.Tests.UiTestUtil.NoPause(w);
            yield return 30;
            t.Check(Find.WindowStack.IsOpen<Window_PitFighters>(), "Fighters window is open");
            var list = pit.Candidates();
            t.Check(list.Any(c => c.Kind == PitKind.Colonist && c.Pawn == col && c.Ready), "the ticked colonist is listed and ready");
            t.Check(list.Any(c => c.Kind == PitKind.Animal), "animals listed");
            t.Check(f == null || list.Any(c => c.Kind == PitKind.Guest), "guests listed");
            t.Check(list.Where(c => !c.Ready).All(c => !c.Reason.NullOrEmpty() && !c.Reason.StartsWith("HH.")), "every pawn that cannot fight has a keyed reason: " + string.Join("; ", list.Where(c => !c.Ready).Select(c => c.Pawn.LabelShort + "=" + c.Reason).Take(8)));
            Hearth.Shell.Tests.UiTestUtil.Shot("uw_pit_fighters");
            yield return 20;
            // right-click on the marker
            var opts = pit.CompFloatMenuOptions(col).ToList();
            t.Check(opts.Count > 0 && opts.All(o => !o.Label.Contains("HH.Underworld")), "right-click offers: " + string.Join(" | ", opts.Select(o => o.Label)));
            t.Check(opts.Any(o => o.Label.StartsWith(UwPay.T("Pit.FightHere"))), "the option is 'Fight in the pit'");
            col.drafter.Drafted = true;
            var dopts = pit.CompFloatMenuOptions(col).ToList();
            t.Check(dopts.Count == 1 && dopts[0].Disabled && dopts[0].Label.Contains(UwPay.T("Pit.Why.Drafted")), "drafted: disabled with the reason: " + dopts.FirstOrDefault()?.Label);
            col.drafter.Drafted = false;
            w.Close(false);
            pit.SetTicked(col, false);
            UwKit.End();
        }
    }

    /// <summary>Phase 1 of the bout save test (tools/run_test.sh with SAVES_OUT): a scored colonist-against-guest bout in its fight phase is saved as hh_pitbout.</summary>
    public sealed class PitSaveBoutSuite : HearthTestSuite
    {
        public const string Save = "hh_pitbout";
        public override string Name => "underworld.pit.savebout";
        public override bool ExplicitOnly => true;
        public override ModuleId Module => ModuleId.Underworld;
        public override int MaxTicks => 12000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var f = UwKit.Friendly();
            UwKit.Begin(ContentIntensity.Standard);
            CellRect inner = default; IntVec3 outside = IntVec3.Invalid; CompFightPit pit = null;
            if (f != null) pit = PitKit.Arena(map, out inner, out outside);
            var col = PitKit.Fighter(map);
            if (pit == null || col == null) { t.Check(false, "setup: friendly faction, area and colonist"); yield break; }
            var guest = UwKit.Guests(map, 1, f)[0];
            var cells = pit.ArenaInfo().Cells;
            PitKit.Put(col, cells[3]); PitKit.Put(guest, cells[45]);
            HSettings.Set("underworld.pit.scoreLimit", 4000f);   // the bout must still run when the game is saved
            var bout = FightBout.Start(pit, col, guest, true, 0, manual: true);
            int waited = 0;
            while (waited < 3000 && !(col.CurJobDef == PitSetup.JobFight && guest.CurJobDef == PitSetup.JobFight)) { yield return 15; waited += 15; }
            t.Check(bout != null && !bout.Finished && col.CurJobDef == PitSetup.JobFight && guest.CurJobDef == PitSetup.JobFight, "both fighters attack each other at save time (" + col.CurJobDef?.defName + ", " + guest.CurJobDef?.defName + ")");
            yield return 120;
            t.Check(bout.ScoreA + bout.ScoreB > 0, "hits were scored before the save");
            var lines = new List<string> { "colonist=" + col.ThingID, "guest=" + guest.ThingID, "faction=" + guest.Faction.loadID, "goodwill=" + guest.Faction.PlayerGoodwill,
                "colInj=" + col.health.hediffSet.hediffs.Count(h => h is Hediff_Injury), "guestInj=" + guest.health.hediffSet.hediffs.Count(h => h is Hediff_Injury) };
            SaveLoadKit.WriteSide(Save, "pit", lines);
            HSettings.Set("underworld.pit.scoreLimit", 100f);
            try { GameDataSaveLoader.SaveGame(Save); } catch (Exception e) { t.Check(false, "save threw " + e.Message); }
            t.Check(System.IO.File.Exists(GenFilePaths.FilePathForSavedGame(Save)), "saved as " + Save + " during the bout");
        }
    }

    /// <summary>Phase 2 (LOAD=…/hh_pitbout.rws): after the load nobody attacks for real; goodwill, injuries and hostility stay as they were.</summary>
    public sealed class PitLoadBoutSuite : HearthTestSuite
    {
        public override string Name => "underworld.pit.loadbout";
        public override bool ExplicitOnly => true;
        public override bool KeepsLoadedGame => true;
        public override ModuleId Module => ModuleId.Underworld;
        public override int MaxTicks => 12000;
        public override IEnumerable<int> Run(TestContext t)
        {
            t.Check(SaveLoadKit.LoadedSave == PitSaveBoutSuite.Save, "loaded " + SaveLoadKit.LoadedSave);
            var side = SaveLoadKit.ReadSide(PitSaveBoutSuite.Save, "pit");
            t.Check(side != null, "side file of the save");
            if (side == null) yield break;
            string V(string k) => side.FirstOrDefault(l => l.StartsWith(k + "="))?.Substring(k.Length + 1);
            var map = t.Map;
            var col = map.mapPawns.AllPawns.FirstOrDefault(p => p.ThingID == V("colonist"));
            var guest = map.mapPawns.AllPawns.FirstOrDefault(p => p.ThingID == V("guest"));
            var fac = guest?.Faction;
            t.Check(col != null && guest != null, "both fighters are on the loaded map");
            if (col == null || guest == null) yield break;
            int gw = int.Parse(V("goodwill")), ci = int.Parse(V("colInj")), gi = int.Parse(V("guestInj"));
            t.Check(FightBout.ActiveCount == 0, "no bout after the load (bouts are not saved)");
            for (int i = 0; i < 6; i++)
            {
                t.Check(!PitSetup.IsPitJob(col.CurJobDef) && !PitSetup.IsPitJob(guest.CurJobDef) && !(col.CurJobDef == JobDefOf.AttackMelee && col.CurJob.targetA.Thing == guest) && !(guest.CurJobDef == JobDefOf.AttackMelee && guest.CurJob.targetA.Thing == col),
                    "nobody fights after the load (tick " + Find.TickManager.TicksGame + ": " + col.CurJobDef?.defName + ", " + guest.CurJobDef?.defName + ")");
                yield return 250;
            }
            t.Check(fac.PlayerGoodwill == gw, "goodwill unchanged: " + gw + " -> " + fac.PlayerGoodwill);
            t.Check(!guest.HostileTo(Faction.OfPlayer), "guest not hostile");
            t.Check(col.health.hediffSet.hediffs.Count(h => h is Hediff_Injury) == ci && guest.health.hediffSet.hediffs.Count(h => h is Hediff_Injury) == gi, "no new injury on either fighter");
            t.Check(col.mindState.meleeThreat != guest && guest.mindState.meleeThreat != col, "no melee threat between them");
            t.Check(col.guilt == null || !col.guilt.IsGuilty, "no guilt");
            t.Check(Ledger.Current.SumAll() == 0, "ledger balanced");
        }
    }
}
#endif
