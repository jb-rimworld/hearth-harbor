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
        /// <summary>A 7x7 fenced arena with a gate and the marker in the middle. The gate goes on the first side whose outside cell the colony can walk
        /// to (a random map can put rock, water or a cliff right behind one side); <paramref name="outside"/> is that cell, three steps outside the gate.</summary>
        public static CompFightPit Arena(Map map, out CellRect inner, out IntVec3 outside)
        {
            outside = IntVec3.Invalid;
            if (!UwKit.TryFindRect(map, 7, 7, out inner)) return null;
            var outer = inner.ExpandedBy(1);
            var anchor = TestCells.Anchor(map);
            IntVec3 gateCell = IntVec3.Invalid;
            foreach (var side in new[] { new IntVec3(0, 0, -1), new IntVec3(0, 0, 1), new IntVec3(-1, 0, 0), new IntVec3(1, 0, 0) })
            {
                var g = side.z != 0 ? new IntVec3(inner.CenterCell.x, 0, side.z < 0 ? outer.minZ : outer.maxZ) : new IntVec3(side.x < 0 ? outer.minX : outer.maxX, 0, inner.CenterCell.z);
                var o = g + side * 3;
                if (o.InBounds(map) && o.Standable(map) && (g + side).Standable(map) && (g + side * 2).Standable(map) && TestCells.Reachable(map, anchor, o)) { gateCell = g; outside = o; break; }
            }
            if (!gateCell.IsValid) { gateCell = new IntVec3(inner.CenterCell.x, 0, outer.minZ); outside = new IntVec3(gateCell.x, 0, gateCell.z - 3); }
            UwKit.BuildFence(map, outer);
            gateCell.GetEdifice(map)?.Destroy();
            var gateDef = DefDatabase<ThingDef>.GetNamedSilentFail("FenceGate");
            if (gateDef != null) UwKit.SpawnBuilding(gateDef, gateCell, map);
            map.regionAndRoomUpdater.TryRebuildDirtyRegionsAndRooms();
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
            var p = TestSetup.Colonist(map, skip, x => !x.WorkTagIsDisabled(WorkTags.Violent) && x.health.summaryHealth.SummaryHealthPercent > 0.9f && !x.health.hediffSet.hediffs.Any(h => h is Hediff_Injury && h.def != PitSetup.Bruise));
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
            public Pawn P; public int Injuries, Bruises; public float InjurySeverity, Bleed, BruiseSeverity; public Thing Weapon; public int Apparel, Items; public Lord Lord; public LordToil Toil; public bool Hostile;
            public static Snap Of(Pawn p) => new Snap
            {
                P = p, Injuries = Inj(p).Count(), InjurySeverity = Inj(p).Sum(h => h.Severity), Bleed = p.health.hediffSet.BleedRateTotal, Bruises = Br(p).Count(), BruiseSeverity = Br(p).Sum(h => h.Severity), Weapon = p.equipment?.Primary,
                Apparel = p.apparel?.WornApparelCount ?? 0, Items = NonSilver(p), Lord = p.GetLord(), Toil = p.GetLord()?.CurLordToil, Hostile = p.HostileTo(Faction.OfPlayer)
            };
            static IEnumerable<Hediff> Inj(Pawn p) => p.health.hediffSet.hediffs.Where(h => h is Hediff_Injury && h.def != PitSetup.Bruise);
            static IEnumerable<Hediff> Br(Pawn p) => p.health.hediffSet.hediffs.Where(h => h.def == PitSetup.Bruise);
            static int NonSilver(Pawn p) => p.inventory?.innerContainer?.Count(t => t.def != ThingDefOf.Silver) ?? 0;

            public void Check(TestContext t, string who, Pawn other)
            {
                var p = P;
                t.Check(!p.Dead && !p.Downed && p.Spawned, who + ": alive, up and on the map");
                t.Check(Inj(p).Count() == Injuries && Math.Abs(Inj(p).Sum(h => h.Severity) - InjurySeverity) < 0.01f, who + ": no injury beyond bruises (" + Injuries + " -> " + Inj(p).Count() + ", bruises " + p.health.hediffSet.hediffs.Count(h => h.def == PitSetup.Bruise) + ")");
                t.Check(p.health.hediffSet.BleedRateTotal <= Bleed + 0.0001f, who + ": bleeds no more than before");
                int nb = Br(p).Count() - Bruises; float sb = Br(p).Sum(h => h.Severity) - BruiseSeverity;
                t.Check(nb >= 0 && nb <= 2 && sb <= 5.01f, who + ": at most the two small bruises of the loser (" + nb + " new, severity " + sb.ToString("0.0") + ")");
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

        /// <summary>Waits after a bout and notes every change of job, mental state, downed state and bruise count of the two (diagnosis of after-bout fights).
        /// Every 30 ticks it also audits the map (<see cref="Audit"/>); each new finding is noted and, when <paramref name="alerts"/> is given, collected there.
        /// The first time one of the two is downed, <see cref="Dump"/> says why (injuries with their source, pain against the shock threshold, capacities).</summary>
        public static IEnumerable<int> Watch(TestContext t, Pawn a, Pawn b, int ticks, List<string> alerts = null)
        {
            string Line(Pawn p) => p.LabelShort + ":" + (p.CurJobDef?.defName ?? "-") + (p.CurJob?.targetA.Thing is Pawn tp ? ">" + tp.LabelShort : "") + (p.InMentalState ? " mental=" + p.MentalStateDef.defName : "") + (p.Downed ? " DOWNED" : "") + (p.Spawned ? "" : " unspawned")
                + " bruises=" + p.health.hediffSet.hediffs.Count(h => h.def == PitSetup.Bruise) + " threat=" + (p.mindState?.meleeThreat?.LabelShort ?? "-");
            string last = null; bool dumped = false; var seen = new HashSet<string>();
            for (int w = 0; w < ticks; w += 30)
            {
                string now = Line(a) + " | " + Line(b);
                if (now != last) { t.Note("after the bout +" + w + ": " + now); last = now; }
                if (!dumped && (a.Downed || b.Downed || a.Dead || b.Dead))
                {
                    dumped = true;
                    t.Note("after the bout +" + w + ": SOMEONE IS DOWN. " + Dump(a) + " || " + Dump(b));
                }
                foreach (var f in Audit(a.Map ?? b.Map, a, b)) if (seen.Add(f)) { t.Note("after the bout +" + w + ": audit: " + f); alerts?.Add(f); }
                yield return 30;
            }
        }

        /// <summary>Everything that can explain a fighter who is down or hurt: injuries (bruises counted, the rest with their source), pain against the
        /// shock threshold, bleeding, the capacities that down a pawn, mental state, fire, and the damage the pawn took in total (records).</summary>
        public static string Dump(Pawn p)
        {
            if (p == null) return "-";
            var hs = p.health.hediffSet;
            int bruises = hs.hediffs.Count(h => h.def == PitSetup.Bruise);
            var other = hs.hediffs.Where(h => h.def != PitSetup.Bruise).Select(h => h.def.defName + (h.Part != null ? "@" + h.Part.Label : "") + ":" + h.Severity.ToString("0.0") + (h is Hediff_Injury ? " src=" + (h.sourceDef?.defName ?? h.sourceLabel ?? "-") : ""));
            return p.LabelShort + " [downed " + p.Downed + ", dead " + p.Dead + ", hp " + p.health.summaryHealth.SummaryHealthPercent.ToString("0.00") + ", bruises " + bruises + ", pain " + hs.PainTotal.ToString("0.00") + " of shock " + p.GetStatValue(StatDefOf.PainShockThreshold).ToString("0.00")
                + ", bleed " + hs.BleedRateTotal.ToString("0.00") + ", moving " + p.health.capacities.GetLevel(PawnCapacityDefOf.Moving).ToString("0.00") + ", consciousness " + p.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness).ToString("0.00")
                + ", burning " + p.IsBurning() + ", mental " + (p.MentalStateDef?.defName ?? "-") + ", damage taken " + p.records.GetValue(RecordDefOf.DamageTaken).ToString("0.0") + ", hediffs: " + string.Join("; ", other) + "]";
        }

        /// <summary>What on the map could still hurt the two former fighters: another pawn who targets or threatens one of them (attack job, melee threat, social fight),
        /// a pawn hostile to one of them within 20 cells, one of them burning or in a mental state. Empty when nothing does.</summary>
        /// <summary>Prefix of audit lines that are a vanilla social fight (a quarrel between two pawns of one faction, nothing the pit did).</summary>
        public const string Quarrel = "[vanilla quarrel] ";
        public static bool IsQuarrel(string line) => line.StartsWith(Quarrel);
        public static List<string> Audit(Map map, Pawn a, Pawn b)
        {
            var found = new List<string>();
            if (map == null) return found;
            foreach (var p in map.mapPawns.AllPawnsSpawned)
            {
                if (p == a || p == b || p.Dead) continue;
                foreach (var f in new[] { a, b })
                {
                    if (f == null || !f.Spawned) continue;
                    if (p.mindState?.meleeThreat == f) found.Add(p.LabelShort + " has " + f.LabelShort + " as melee threat");
                    var j = p.CurJob;
                    if (j != null && j.targetA.Thing == f && (j.def == JobDefOf.AttackMelee || j.def == JobDefOf.AttackStatic || j.def == JobDefOf.Wait_Combat)) found.Add(p.LabelShort + " attacks " + f.LabelShort + " (" + j.def.defName + ")");
                    if (p.MentalState is MentalState_SocialFighting sf && sf.otherPawn == f) found.Add(Quarrel + p.LabelShort + " is in a social fight with " + f.LabelShort);
                    if (p.HostileTo(f) && p.Position.DistanceTo(f.Position) < 20f && !p.Downed) found.Add(p.LabelShort + " (" + p.kindDef.defName + ") is hostile to " + f.LabelShort + " within 20 cells");
                }
            }
            foreach (var f in new[] { a, b })
            {
                if (f == null) continue;
                if (f.IsBurning()) found.Add(f.LabelShort + " is burning");
                if (f.InMentalState) found.Add((f.MentalStateDef == MentalStateDefOf.SocialFighting ? Quarrel : "") + f.LabelShort + " is in mental state " + f.MentalStateDef.defName);
            }
            return found;
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
                    var alerts1 = new List<string>();
                    foreach (var w in PitKit.Watch(t, col, guest, 600, alerts1)) yield return w;   // the after-bout window in which a victim would swing back for real
                    var hard1 = alerts1.Where(x => !PitKit.IsQuarrel(x)).ToList(); bool quarrel1 = alerts1.Count > hard1.Count;
                    t.Check(hard1.Count == 0, "nothing on the map (apart from a vanilla quarrel) threatens the two for 600 ticks after the bout" + (hard1.Count > 0 ? ": " + string.Join("; ", hard1) : ""));
                    if (quarrel1) t.Note("a vanilla social fight broke out around the fighters (" + string.Join("; ", alerts1.Where(PitKit.IsQuarrel)) + "): injuries, mental state and goodwill are vanilla's here, those checks are skipped for this part");
                    if (!quarrel1) t.Check(PitKit.GoodwillDiff(gw) == "none", "goodwill of every faction unchanged: " + PitKit.GoodwillDiff(gw));
                    if (!quarrel1) { sc.Check(t, "colonist", guest); sg.Check(t, "guest", col); }
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
            foreach (var b2 in gs.Skip(2)) PitKit.Put(b2, CellFinder.RandomClosewalkCellNear(outside, map, 2));
            yield return 30;
            var gw2 = PitKit.Goodwill();
            var s0 = PitKit.Snap.Of(gs[0]); var s1 = PitKit.Snap.Of(gs[1]);
            int silver0 = UwKit.Silver(gs[0]), silver1 = UwKit.Silver(gs[1]);
            var till0 = UwKit.Till(map); long take0 = Underworld.Comp.HouseTakeMicro;
            var gg = FightBout.Start(pit, gs[0], gs[1], true, 600, manual: true);
            t.Check(gg != null && gg.Scored, "guest against guest is scored");
            bool left = false, bets = false, selfBet = true;
            if (gg != null)
            {
                bets = gg.TryBet(gs[2], 0, 100) && gg.TryBet(gs[3], 1, 100);
                selfBet = gg.TryBet(gs[0], 1, 10);
                int w0 = 0;
                while (!gg.Finished && gg.phase != FightBout.Phase.Fight && w0 < 1200) { yield return 30; w0 += 30; }
                left = gg.Finished && gg.FightTick <= 0 && !gs.Take(2).All(PitFighters.StayToil);
                if (left) t.Note("the visitor group left before the fight for a vanilla reason (lord toil " + gs[0].GetLord()?.CurLordToil?.GetType().Name + ", temperature/weather/time); the bout was called off as it should be: " + FightBout.LastMessage + "; guest part skipped");
            }
            if (gg != null && !left)
            {
                t.Check(bets, "two bets placed");
                t.Check(!selfBet, "a fighter cannot bet on his own bout");
                foreach (var w in PitKit.Await(t, gg)) yield return w;
                var alerts2 = new List<string>();
                foreach (var w in PitKit.Watch(t, gs[0], gs[1], 600, alerts2)) yield return w;
                var hard2 = alerts2.Where(x => !PitKit.IsQuarrel(x)).ToList(); bool quarrel2 = alerts2.Count > hard2.Count;
                t.Check(hard2.Count == 0, "nothing on the map (apart from a vanilla quarrel) threatens the two guests for 600 ticks after the bout" + (hard2.Count > 0 ? ": " + string.Join("; ", hard2) : ""));
                if (quarrel2) t.Note("a vanilla social fight broke out around the fighters (" + string.Join("; ", alerts2.Where(PitKit.IsQuarrel)) + ", Plum or another guest of the same faction stands close): injuries, mental state and goodwill are vanilla's here, those checks are skipped for this part");
                t.Check(gg.Finished && gg.Result != null, "guest bout came to a result");
                if (!quarrel2) t.Check(PitKit.GoodwillDiff(gw2) == "none", "goodwill unchanged after the guest bout: " + PitKit.GoodwillDiff(gw2));
                if (!quarrel2) { s0.Check(t, "guest A", gs[1]); s1.Check(t, "guest B", gs[0]); }
                var r = gg.Result;
                long payouts = gg.Bets.Sum(x => x.PayoutMicro);
                // the two test bets, plus any spectator of another suite who walked by and bet on his own (PitActivity)
                t.Check(r.PoolMicro >= Money.FromSilver(200).Micro && r.PoolMicro == gg.Bets.Sum(x => x.StakeMicro) && r.PoolMicro == payouts + r.HouseMicro, "pool = stakes = payouts + house (" + r.PoolMicro + " = " + payouts + " + " + r.HouseMicro + ", " + gg.Bets.Count + " bets)");
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

    /// <summary>The window after a scored guest-versus-guest bout (package B saw one guest downed within 600 ticks once, never reproduced). Proves what can be
    /// proved: (1) the worst bruises the pit could ever give cannot down even a very old guest (pain, capacities); (2) a scored bout leaves the opinion of
    /// the two of each other, and so their chance to quarrel, exactly as it was; (3) nobody else swings at, threatens or burns them in the 600 ticks after.
    /// And it shows the one cause the pit cannot rule out: a vanilla social fight between the two (the pit's damage guard ends with the bout) does down a
    /// guest and sends the group home with the wounded one (notes only, the outcome is vanilla's).</summary>
    public sealed class PitAftermathSuite : HearthTestSuite
    {
        public override string Name => "underworld.pit.aftermath";
        public override ModuleId Module => ModuleId.Underworld;
        public override int MaxTicks => 40000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var f = UwKit.Friendly();
            UwKit.Begin(ContentIntensity.Standard);
            CellRect inner = default; IntVec3 outside = IntVec3.Invalid; CompFightPit pit = null;
            if (f != null) pit = PitKit.Arena(map, out inner, out outside);
            if (pit == null || PitSetup.Bruise == null) { t.Note("no friendly faction, free area or Bruise def; skipped"); UwKit.End(); yield break; }
            var cells = pit.ArenaInfo().Cells;

            // ---- 1: the worst bruises the pit can give: the two parts that hurt movement most, at the top severity (the game gives one or two of 1.0 to 2.5),
            // on a very old guest and on young ones. Then the bound that cannot happen (a bruise at 2.5 on every allowed part), as notes.
            var old = PawnGenerator.GeneratePawn(new PawnGenerationRequest(f.RandomPawnKind() ?? PawnKindDefOf.Villager, f, PawnGenerationContext.NonPlayer, map.Tile, canGeneratePawnRelations: false, fixedBiologicalAge: 85f, fixedChronologicalAge: 85f));
            GenSpawn.Spawn(old, CellFinder.RandomClosewalkCellNear(TestCells.Anchor(map), map, 12), map); UwKit.Track(old);
            var gs = UwKit.Guests(map, 4, f);
            var subjects = new List<Pawn> { old }; subjects.AddRange(gs);
            float worstPain = FightBout.WorstBruisePain(); var edgeCases = new List<Pawn>();
            foreach (var p in subjects)
            {
                var parts = FightBout.BruiseParts(p);
                bool downedBefore = p.Downed; float painBefore = p.health.hediffSet.PainTotal, shock = p.GetStatValue(StatDefOf.PainShockThreshold);
                var byMoving = new List<KeyValuePair<float, BodyPartRecord>>();
                foreach (var part in parts)
                {
                    var h = (Hediff_Injury)HediffMaker.MakeHediff(PitSetup.Bruise, p, part); h.Severity = 2.5f; p.health.AddHediff(h, part);
                    byMoving.Add(new KeyValuePair<float, BodyPartRecord>(p.health.capacities.GetLevel(PawnCapacityDefOf.Moving) + p.health.capacities.GetLevel(PawnCapacityDefOf.Manipulation), part));
                    p.health.RemoveHediff(h);
                }
                var worst = byMoving.OrderBy(k => k.Key).Take(2).Select(k => k.Value).ToList();
                var added = new List<Hediff>();
                foreach (var part in worst) { var h = (Hediff_Injury)HediffMaker.MakeHediff(PitSetup.Bruise, p, part); h.Severity = 2.5f; p.health.AddHediff(h, part); added.Add(h); }
                float painAfter = p.health.hediffSet.PainTotal;
                string label = p.LabelShort + " (age " + p.ageTracker.AgeBiologicalYears + ", hp " + p.health.summaryHealth.SummaryHealthPercent.ToString("0.00") + ", shock " + shock.ToString("0.00") + ")";
                bool ruleAllows = painBefore + worstPain < shock - 0.02f;   // the pit's headroom rule (FightBout.Bruise)
                t.Check(painAfter - painBefore <= worstPain + 0.001f, label + ": two bruises add " + (painAfter - painBefore).ToString("0.000") + " pain at most (" + worstPain.ToString("0.000") + " with the margin)");
                if (ruleAllows) t.Check(p.Downed == downedBefore && !p.Dead, label + ": the headroom rule lets him be bruised, and the two worst bruises do not down him (pain " + painBefore.ToString("0.00") + " -> " + painAfter.ToString("0.00") + ")");
                else
                {
                    t.Note(label + ": pain " + painBefore.ToString("0.00") + " leaves too little headroom: the two worst bruises would " + (p.Downed && !downedBefore ? "DOWN him (the old rule did exactly that)" : "not down him this time") + "; the rule gives him none");
                    edgeCases.Add(p);
                }
                foreach (var h in added) p.health.RemoveHediff(h);
                var all = new List<Hediff>();
                foreach (var part in parts) { var h = (Hediff_Injury)HediffMaker.MakeHediff(PitSetup.Bruise, p, part); h.Severity = 2.5f; p.health.AddHediff(h, part); all.Add(h); }
                t.Note(label + ": (impossible case) " + parts.Count + " bruises at 2.5 = pain " + p.health.hediffSet.PainTotal.ToString("0.00") + ", downed " + p.Downed + ", moving " + p.health.capacities.GetLevel(PawnCapacityDefOf.Moving).ToString("0.00"));
                foreach (var h in all) p.health.RemoveHediff(h);
            }
            var a = gs[0]; var b = gs[1];

            // ---- 2: a scored guest-versus-guest bout adds nothing of the pit to how the two feel about each other (vanilla chatter between two pawns standing in one
            // arena does: insults and kind words are normal memories, they are only listed)
            int arrive = 0;
            while (arrive < 4000 && !gs.All(PitFighters.StayToil)) { yield return 100; arrive += 100; }
            PitKit.Put(a, cells[10]); PitKit.Put(b, cells[38]);
            foreach (var x in gs.Skip(2)) PitKit.Put(x, CellFinder.RandomClosewalkCellNear(outside, map, 2));
            yield return 30;
            var memBefore = Mems(a, b).Concat(Mems(b, a)).ToList();
            var gw = PitKit.Goodwill();
            var bout = FightBout.Start(pit, a, b, true, 600, manual: true);
            t.Check(bout != null && bout.Scored, "guest against guest is scored");
            if (bout == null) { UwKit.End(); yield break; }
            foreach (var w in PitKit.Await(t, bout)) yield return w;
            if (!bout.Finished || bout.FightTick <= 0) { t.Note("the bout did not reach a fight (" + FightBout.LastMessage + "); the rest is skipped"); UwKit.End(); yield break; }
            var alerts = new List<string>();
            foreach (var w in PitKit.Watch(t, a, b, 600, alerts)) yield return w;
            var newMems = Mems(a, b).Concat(Mems(b, a)).Except(memBefore).ToList();
            t.Note("new memories about the other fighter: " + (newMems.Count == 0 ? "none" : string.Join(", ", newMems)));
            t.Check(!newMems.Contains("HarmedMe") && !newMems.Contains("WitnessedDeathNonAlly") && !newMems.Any(m => m.IndexOf("Fight", StringComparison.OrdinalIgnoreCase) >= 0 && m != "HadAngeringFight" && m != "HadCatharticFight"),
                "none of them comes from the pit's blows (no HarmedMe)");
            var hard = alerts.Where(x => !PitKit.IsQuarrel(x)).ToList();
            t.Check(hard.Count == 0, "nobody else swung at, threatened or burned the two for 600 ticks" + (hard.Count > 0 ? ": " + string.Join("; ", hard) : ""));
            bool quarrel = alerts.Count > hard.Count;
            if (quarrel) t.Note("a vanilla quarrel broke out in the window: " + string.Join("; ", alerts.Where(PitKit.IsQuarrel)));
            else
            {
                t.Check(!a.Downed && !b.Downed && !a.Dead && !b.Dead, "both fighters are up 600 ticks after the bout: " + PitKit.Dump(a) + " | " + PitKit.Dump(b));
                t.Check(PitKit.GoodwillDiff(gw) == "none", "goodwill unchanged: " + PitKit.GoodwillDiff(gw));
            }

            // ---- 2a: pawns who naturally stand too close to their shock threshold get no bruise from the real thing
            foreach (var p in edgeCases)
            {
                int before = p.health.hediffSet.hediffs.Count(h => h.def == PitSetup.Bruise);
                bout.Bruise(p);
                t.Check(!p.Downed && p.health.hediffSet.hediffs.Count(h => h.def == PitSetup.Bruise) == before, p.LabelShort + ": FightBout.Bruise gave no bruise and left him up (pain " + p.health.hediffSet.PainTotal.ToString("0.00") + " of shock " + p.GetStatValue(StatDefOf.PainShockThreshold).ToString("0.00") + ")");
            }

            // ---- 2b: the pain headroom rule: a spectator whose pain stands a hair under his shock threshold gets no bruise (and is not downed by one)
            var edge = gs[3];
            {
                var parts = FightBout.BruiseParts(edge); float shock = edge.GetStatValue(StatDefOf.PainShockThreshold);
                var tmp = new List<Hediff>(); int guard = 0;
                while (guard++ < 60 && shock - edge.health.hediffSet.PainTotal > 0.04f)
                {
                    var part = parts[guard % parts.Count];
                    var h = (Hediff_Injury)HediffMaker.MakeHediff(PitSetup.Bruise, edge, part); h.Severity = 2.5f; edge.health.AddHediff(h, part); tmp.Add(h);
                    if (edge.Downed) break;
                }
                float head = shock - edge.health.hediffSet.PainTotal;
                int before = edge.health.hediffSet.hediffs.Count(h => h.def == PitSetup.Bruise);
                if (!edge.Downed && head > 0f && head < worstPain)
                {
                    bout.Bruise(edge);
                    t.Check(!edge.Downed && edge.health.hediffSet.hediffs.Count(h => h.def == PitSetup.Bruise) == before, "pain " + edge.health.hediffSet.PainTotal.ToString("0.000") + " of shock " + shock.ToString("0.00") + " (headroom " + head.ToString("0.000") + "): no bruise given, not downed");
                }
                else t.Note("pain headroom case not reached (" + head.ToString("0.000") + ", downed " + edge.Downed + "); skipped");
                foreach (var h in tmp) { try { edge.health.RemoveHediff(h); } catch { } }
            }

            // ---- 3: what the pit cannot rule out: a vanilla social fight between the two ex-fighters (nothing of the bout's guard is left then)
            t.Check(FightBout.OfPawn(a) == null && FightBout.OfPawn(b) == null, "the bout is gone, so the damage guard no longer covers the two (a quarrel is real damage)");
            if (a.Downed || b.Downed || a.Dead || b.Dead || a.InMentalState || b.InMentalState) { t.Note("one of them is already down or quarrelling; the forced fight is skipped"); UwKit.End(); yield break; }
            var toil0 = a.GetLord()?.CurLordToil?.GetType().Name;
            var gw3 = PitKit.Goodwill();
            a.interactions.StartSocialFight(b);
            t.Check(a.InMentalState && b.InMentalState, "a forced social fight started between the two ex-fighters");
            int sw = 0; string downed = null;
            while (sw < 3000 && a.InMentalState && b.InMentalState && !a.Downed && !b.Downed) { yield return 30; sw += 30; }
            if (a.Downed) downed = a.LabelShort; else if (b.Downed) downed = b.LabelShort;
            yield return 60;
            t.Note("forced social fight: ended after " + sw + " ticks, downed: " + (downed ?? "nobody") + "; lord toil " + toil0 + " -> " + a.GetLord()?.CurLordToil?.GetType().Name + " / " + b.GetLord()?.CurLordToil?.GetType().Name
                + "; goodwill " + PitKit.GoodwillDiff(gw3) + "; " + PitKit.Dump(a) + " | " + PitKit.Dump(b));
            UwKit.End();
        }

        /// <summary>Def names of the memories <paramref name="p"/> keeps about <paramref name="other"/>.</summary>
        static IEnumerable<string> Mems(Pawn p, Pawn other) => (p.needs?.mood?.thoughts?.memories?.Memories ?? new List<Thought_Memory>()).Where(m => m.otherPawn == other).Select(m => m.def.defName).ToList();
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
            pit.Candidates(true);
            t.Check(pit.LoneFighter() == null, "nobody ticked: no lone fighter (an unticked colonist is never called into a bout): " + pit.LoneFighter()?.LabelShort);
            t.Check(PitOpponents.CannotHire(pit, pit.LoneFighter()) != null, "nobody ticked: 'Call an opponent' is disabled: " + PitOpponents.CannotHire(pit, pit.LoneFighter()));
            pit.SetTicked(col, true);
            PitKit.Put(col, CellFinder.RandomClosewalkCellNear(outside, map, 2));
            var ready = pit.Ready(true);
            t.Check(ready.Count == 1 && ready[0].Pawn == col, "only the ticked colonist is ready: " + string.Join(", ", ready.Select(c => c.Pawn.LabelShort)));
            t.Check(pit.StatusLine() == UwPay.T("Pit.Status.WaitFighter"), "status says it waits for a second fighter: " + pit.StatusLine());
            t.Check(pit.LoneFighter() == col, "the lone fighter is the ticked colonist");
            var hireGizmo = pit.CompGetGizmosExtra().OfType<Command>().FirstOrDefault(g => g.icon == PitSetup.IconOpponent);
            t.Check(hireGizmo != null && hireGizmo.defaultDesc.Contains(UwPay.T("Pit.CallOpponent.For", col.LabelShortCap)), "the gizmo names who will fight: " + hireGizmo?.defaultDesc);
            var fee = PitOpponents.Fee;
            // an empty till disables the gizmo with the reason
            var tillAcc = Underworld.Till(map); var have = Ledger.Current.Balance(tillAcc);
            if (have.Micro > 0) Ledger.Current.Post(tillAcc, "world", have, "test: empty till");
            t.Check(PitOpponents.CannotHire(pit, col) == UwPay.T("Pit.Why.TillShort", PitFighters.Silver(fee)), "empty till: " + PitOpponents.CannotHire(pit, col));
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
            var tillHire = UwKit.Till(map);
            // a fighter drafted since the gizmo's cache was built is checked again: no beast, no fee
            col.drafter.Drafted = true;
            t.Check(PitOpponents.HireBeast(pit, col, out why) == null && why == UwPay.T("Pit.Why.Drafted") && UwKit.Till(map) == tillHire && !pit.HasBeast, "drafted fighter: no hire, no fee (" + why + ")");
            col.drafter.Drafted = false;
            var b2 = PitOpponents.HireBeast(pit, col, out why);
            t.Check(b2 != null, "second beast hired (" + why + ")");
            if (b2 != null)
            {
                var beast2 = b2.B;
                t.Check(UwKit.Till(map) == tillHire - fee && b2.FeePaid == fee, "fee booked for the second beast");
                yield return 120;
                int abortTick = Find.TickManager.TicksGame; var tillAbort = UwKit.Till(map);
                b2.Abort("test", UwPay.T("Pit.Abort.Closed"));
                // checked at once and in the journal: other suites' guests may pay into the same till while we wait
                t.Check(UwKit.Till(map) == tillAbort + fee, "called off without a result: the fee is back in the till (" + (UwKit.Till(map) - tillAbort).ToStringShort() + ")");
                t.Check(Ledger.Current.Journal.Count(e => e.Tick >= abortTick && e.From == "world" && e.To == Underworld.Till(map) && e.Amount == fee && e.Memo == "Fight pit: opponent fee refunded") == 1, "one refund booking in the journal");
                yield return 300;
                t.Check(beast2.Destroyed && !pit.HasBeast, "aborted: the beast is gone");
                t.Check(Ledger.Current.SumAll() == 0, "ledger balanced after the refund");
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
            // a prisoner of a friendly faction against an animal (real damage, non-lethal): brought in and back, no escape, no goodwill
            var f = UwKit.Friendly();
            if (f != null)
            {
                pit.allowPrisoners = true;
                var pr = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Villager, f, PawnGenerationContext.NonPlayer, map.Tile, canGeneratePawnRelations: false, mustBeCapableOfViolence: true));
                if (!pr.DevelopmentalStage.Adult()) { pr.ageTracker.AgeBiologicalTicks = 25L * 3600000L; pr.ageTracker.AgeChronologicalTicks = 25L * 3600000L; }
                var home = CellFinder.RandomClosewalkCellNear(outside, map, 2);
                GenSpawn.Spawn(pr, home, map); UwKit.Track(pr);
                pr.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Prisoner);
                var beast2 = UwKit.TameAnimal(map, CellFinder.RandomClosewalkCellNear(outside, map, 2), "Boar", "Muffalo");
                var from = pr.Position;
                var gw = PitKit.Goodwill();
                var pb = FightBout.Start(pit, pr, beast2, false, 60, manual: true);
                t.Check(pb != null && !pb.Scored && pb.NonLethal, "prisoner against animal: real damage, non-lethal");
                if (pb != null)
                {
                    int waited = 0; bool seenIn = false, escaped = false;
                    while (!pb.Finished && waited < 12000)
                    {
                        yield return 30; waited += 30;
                        if (pit.ArenaInfo().Cells.Contains(pr.Position)) seenIn = true;
                        if (FightBout.OfPawn(pr) != null && pr.CurJob != null && pr.CurJob.exitMapOnArrival) escaped = true;
                    }
                    t.Check(pb.Finished && seenIn, "the prisoner was brought into the arena and the bout ended (" + waited + " ticks)");
                    t.Check(!escaped, "the prisoner never tried to escape");
                    yield return 60;
                    t.Check(pr.Spawned && pr.IsPrisonerOfColony && !pit.ArenaInfo().Cells.Contains(pr.Position) && pr.Position.DistanceTo(from) < 8f, "the prisoner is back where he was (" + from + " -> " + pr.Position + ")");
                    t.Check(!pr.Dead && !beast2.Dead, "nobody died");
                    t.Check(PitKit.GoodwillDiff(gw) == "none", "goodwill of the prisoner's faction unchanged: " + PitKit.GoodwillDiff(gw));
                }
            }
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
            t.Check(c.Reason == UwPay.T("Pit.Why.NotTicked") && !c.SoftReason && !c.ManualOk, "unticked colonist: hard reason, no manual start either: " + c.Reason);
            t.Check(!pit.ManualReady(true).Any(x => x.Kind == PitKind.Colonist && !pit.IsTicked(x.Pawn)), "manual starts hold no unticked colonist: " + string.Join(", ", pit.ManualReady().Select(x => x.Pawn.LabelShort)));
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
                foreach (var g in gs) PitKit.Put(g, CellFinder.RandomClosewalkCellNear(outside, map, 2));
                if (!kind.story.traits.HasTrait(TraitDefOf.Kind)) kind.story.traits.GainTrait(new Trait(TraitDefOf.Kind));
                foreach (var tr in plain.story.traits.allTraits.Where(x => x.def == TraitDefOf.Bloodlust || x.def == TraitDefOf.Brawler).ToList()) plain.story.traits.RemoveTrait(tr);
                plain.skills.GetSkill(SkillDefOf.Melee).Level = 2;
                Underworld.MarkShady(shady); shady.skills.GetSkill(SkillDefOf.Melee).Level = 0;
                foreach (var g in gs) g.jobs.StopAll();
                yield return 5;
                var rk = PitFighters.Guest(kind); var rp = PitFighters.Guest(plain); var rs = PitFighters.Guest(shady);
                t.Check(kind.WorkTagIsDisabled(WorkTags.Violent) || rk.Reason == UwPay.T("Pit.Why.Kind"), "kind guest: " + rk.Reason);
                if (plain.skills.GetSkill(SkillDefOf.Melee).Level >= 6) t.Note("the plain guest's genes give melee " + plain.skills.GetSkill(SkillDefOf.Melee).Level + " even at skill 2 (aptitude); no-taste check skipped");
                else t.Check(plain.WorkTagIsDisabled(WorkTags.Violent) || rp.Reason == UwPay.T("Pit.Why.NoTaste"), "guest without taste: " + (rp.Reason ?? "ready") + " (traits " + string.Join("/", plain.story.traits.allTraits.Select(x => x.def.defName)) + ", melee " + plain.skills.GetSkill(SkillDefOf.Melee).Level + ", shady " + Underworld.IsShady(plain) + ")");
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
                PitKit.Put(extra, CellFinder.RandomClosewalkCellNear(outside, map, 2));
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
            t.Check(copy.LastBoutLine() == UwPay.T("Pit.Last", "Beta", "Alpha", PitFighters.Silver(new Money(8400000)), PitFighters.Silver(new Money(800000))), "last bout line: " + copy.LastBoutLine());
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
            t.Check(!pit.ManualReady(true).Any(c => c.Kind == PitKind.Colonist), "nobody ticked: no colonist for a manual start either");
            var col = PitKit.Fighter(map); var col2 = PitKit.Fighter(map, 1);
            bool guestsAlready = GuestApi.GuestsOn(map).Any(g => !Underworld.IsMinor(g));
            if (col != null)
            {
                // one ticked colonist alone: still waiting for a second fighter
                pit.SetTicked(col, true); pit.Invalidate();
                Expect("Pit.Status.WaitFighter", "one ticked colonist");
                // one ticked colonist and an animal of the colony: two fighters, no guests to bet
                pit.allowAnimals = true;
                var boar = UwKit.TameAnimal(map, CellFinder.RandomClosewalkCellNear(outside, map, 2), "Boar", "Muffalo");
                pit.Invalidate();
                t.Check(pit.Ready(true).Count >= 2, "ticked colonist + animal are two ready fighters: " + string.Join(", ", pit.Ready().Select(c => c.Pawn.LabelShort + "(" + c.Kind + ")")));
                if (!guestsAlready) Expect("Pit.Status.WaitGuests", "ticked colonist + animal, no guests");
                else t.Note("guests of another suite on the map; waiting-for-guests line with an animal not checked");
                boar.Destroy(); pit.allowAnimals = false; pit.Invalidate();
                // an unfair pair and a guest: the automatic attempt fails and the status line says why (not "next bout in ...")
                var big = DefDatabase<PawnKindDef>.GetNamedSilentFail("Thrumbo") ?? DefDatabase<PawnKindDef>.AllDefsListForReading.Where(k => k.RaceProps != null && k.RaceProps.Animal && k.combatPower >= 300f).OrderByDescending(k => k.combatPower).FirstOrDefault();
                if (f != null && big != null)
                {
                    pit.allowAnimals = true;
                    var giant = UwKit.Track(PawnGenerator.GeneratePawn(big, Faction.OfPlayer)); GenSpawn.Spawn(giant, CellFinder.RandomClosewalkCellNear(outside, map, 3), map);
                    UwKit.Guests(map, 1, f); pit.Invalidate();
                    var rd = pit.Ready(true);
                    t.Note("unfair pair: " + string.Join(", ", rd.Select(c => c.Pawn.LabelShort + " " + c.Power.ToString("0"))));
                    if (rd.Count == 2 && !UnderworldMath.FairPair(rd[0].Power, rd[1].Power, HSettings.Get("underworld.pit.fairRatio")))
                    {
                        pit.nextBoutTick = Find.TickManager.TicksGame;
                        pit.CompTickRare();
                        t.Check(FightBout.OfPit(pit) == null, "no automatic bout with an unfair pair");
                        Expect("Pit.Status.AutoFailed", "unfair pair and a guest", UwPay.T("Pit.NoFairPair"));
                        pit.SetTicked(col, false); pit.SetTicked(col, true);
                        t.Check(pit.StatusLine() != UwPay.T("Pit.Status.AutoFailed", UwPay.T("Pit.NoFairPair")), "ticking a box clears the old failure: " + pit.StatusLine());
                    }
                    else t.Note("the test map gave another ready set; unfair-pair line not checked");
                    giant.Destroy(); pit.allowAnimals = false; pit.Invalidate();
                }
                pit.SetTicked(col, false); pit.Invalidate();
            }
            if (col != null && col2 != null && col != col2)
            {
                pit.SetTicked(col, true); pit.SetTicked(col2, true); pit.Invalidate();
                bool guests = GuestApi.GuestsOn(map).Any(g => !Underworld.IsMinor(g));
                if (!guests) Expect("Pit.Status.WaitGuests", "two fighters, no guests");
                if (f != null)
                {
                    if (!guests) UwKit.Guests(map, 1, f);
                    pit.Invalidate();
                    pit.nextBoutTick = Find.TickManager.TicksGame + 2500 * 3;
                    Expect("Pit.Status.Next", "two fighters and a guest", "3.0");
                }
                t.Check(pit.ReadyLine() == UwPay.T("Pit.Ready", 2, UwPay.T("Pit.Ready.Many.Colonist", 2)), "ready line: " + pit.ReadyLine());
                pit.SetTicked(col, false); pit.SetTicked(col2, false);
            }
            else t.Note("fewer than two colonists; two-colonist lines skipped");
            // a closed pen that is too small says so (no gap to look for)
            if (UwKit.TryFindRect(map, 2, 3, out var small))
            {
                UwKit.BuildFence(map, small.ExpandedBy(1));
                map.regionAndRoomUpdater.TryRebuildDirtyRegionsAndRooms();
                var sp = UwKit.SpawnBuilding(UwDefs.FightPit, small.CenterCell, map).TryGetComp<CompFightPit>();
                var sa = sp.ArenaInfo();
                string want = UwPay.T("Pit.ArenaSmall", sa.Cells.Count, CompFightPit.MinArenaCells);
                t.Check(sa.Closed && sa.Cells.Count < CompFightPit.MinArenaCells, "small pen: closed, " + sa.Cells.Count + " tiles");
                t.Check(sp.ClosedReason() == want && sp.StatusLine() == want && sp.CompInspectStringExtra().Contains(want) && !sp.CompInspectStringExtra().Contains(UwPay.T("Pit.ArenaOpen")), "small pen: 'too small', not 'open': " + sp.StatusLine());
                var startG = sp.CompGetGizmosExtra().OfType<Command>().FirstOrDefault(g => g.icon == PitSetup.IconStart);
                t.Check(startG != null && startG.Disabled && startG.disabledReason == want, "small pen: start disabled with the reason: " + startG?.disabledReason);
                sp.parent.Destroy();
            }
            else t.Note("no room for a small pen; too-small line not checked");
            pit.enabled = false; pit.Invalidate(); Expect("Pit.Status.Closed", "closed"); pit.enabled = true;
            HSettings.Set("core.peacefulUnderworld", 1f); Expect("Pit.Status.Peaceful", "peaceful"); HSettings.Set("core.peacefulUnderworld", 0f);
            HearthProfile.Current.Content = ContentIntensity.Cozy; Underworld.MarkInUseDirty(null);
            t.Check(pit.StatusLine() == UwPay.T("Pit.Status.Off"), "Cozy: " + pit.StatusLine());
            t.Check(!pit.CompGetGizmosExtra().Any(), "Cozy: no gizmos");
            HearthProfile.Current.Content = ContentIntensity.Standard;
            // switching the module off ends a running bout at once (the damage guard would otherwise outlive the module)
            if (col != null && col2 != null && col != col2)
            {
                var mb = FightBout.Start(pit, col, col2, true, 600, manual: true);
                t.Check(mb != null, "a bout runs before the switch");
                HearthProfile.Current.SetModule(ModuleId.Underworld, false);
                t.Check(FightBout.ActiveCount == 0, "module switched off: no bout left");
                HearthProfile.Current.SetModule(ModuleId.Underworld, true);
            }
            // the gizmos all have their own icons and keyed labels
            var gizmos = pit.CompGetGizmosExtra().OfType<Command>().ToList();
            t.Check(gizmos.Count >= 7 && gizmos.All(g => !g.defaultLabel.StartsWith("HH.") && !g.defaultDesc.NullOrEmpty() && !g.defaultDesc.StartsWith("HH.")), "gizmos keyed: " + string.Join(" | ", gizmos.Select(g => g.defaultLabel)));
            t.Check(gizmos.Select(g => g.icon).Distinct().Count() == gizmos.Count, "every gizmo has its own icon");
            // an open arena
            var gap = new IntVec3(inner.minX, 0, inner.ExpandedBy(1).maxZ); gap.GetEdifice(map)?.Destroy(); pit.Invalidate();
            Expect("Pit.Status.NeedArena", "open arena");
            var oa = pit.ArenaInfo();
            t.Check(pit.CompInspectStringExtra().Contains(oa.TooBig ? UwPay.T("Pit.ArenaBig", oa.Cap) : UwPay.T("Pit.ArenaOpen")), "arena line says to close the fence (" + (oa.TooBig ? "big" : "edge") + "): " + pit.CompInspectStringExtra());
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
            UwKit.TameAnimal(map, CellFinder.RandomClosewalkCellNear(outside, map, 2), "Boar", "Muffalo");
            if (f != null) foreach (var g in UwKit.Guests(map, 2, f)) PitKit.Put(g, CellFinder.RandomClosewalkCellNear(outside, map, 2));
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

    /// <summary>
    /// The review fixes of 1.2.0 around retaliation, terrain kicks and lost bouts, in one running game:
    /// (1) inside a scored bout JobGiver_ReactToCloseMeleeThreat gives no vanilla attack on the opponent, and one given anyway is replaced by the pit job;
    /// (2) a terrain kick (Verb_MeleeApplyHediff) between the two adds no hediff and scores points instead;
    /// (3) a bout lost the way a load loses it (no Release): the vanilla attacks between the two end before a blow and the threats are cleared,
    /// a brought-in prisoner is taken back, a hired beast is sent away and its fee returned.
    /// </summary>
    public sealed class PitLostBoutSuite : HearthTestSuite
    {
        public override string Name => "underworld.pit.lostbout";
        public override ModuleId Module => ModuleId.Underworld;
        public override int MaxTicks => 24000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var f = UwKit.Friendly();
            UwKit.Begin(ContentIntensity.Standard);
            CellRect inner = default; IntVec3 outside = IntVec3.Invalid; CompFightPit pit = null;
            if (f != null) pit = PitKit.Arena(map, out inner, out outside);
            var col = PitKit.Fighter(map);
            if (pit == null || col == null) { t.Note("no friendly faction, area or colonist; skipped"); UwKit.End(); yield break; }
            var guest = UwKit.Guests(map, 1, f)[0];
            int arrive = 0;
            while (arrive < 4000 && !PitFighters.StayToil(guest)) { yield return 100; arrive += 100; }   // the snapshot compares lord toils: the visit must have arrived
            var cells = pit.ArenaInfo().Cells;
            PitKit.Put(col, cells[3]); PitKit.Put(guest, cells[45]);
            HSettings.Set("underworld.pit.scoreLimit", 4000f);   // long enough for every step below
            var gw = PitKit.Goodwill(); var sc = PitKit.Snap.Of(col); var sg = PitKit.Snap.Of(guest);
            var bout = FightBout.Start(pit, col, guest, true, 0, manual: true);
            int waited = 0;
            while (bout != null && !bout.Finished && waited < 3000 && !(col.CurJobDef == PitSetup.JobFight && guest.CurJobDef == PitSetup.JobFight)) { yield return 15; waited += 15; }
            t.Check(bout != null && !bout.Finished && col.CurJobDef == PitSetup.JobFight, "the scored bout is in its fight phase (" + col.CurJobDef?.defName + ")");
            if (bout == null || bout.Finished) { HSettings.Set("underworld.pit.scoreLimit", 100f); UwKit.End(); yield break; }

            // (1) no vanilla retaliation inside the bout
            col.mindState.meleeThreat = guest; col.mindState.lastMeleeThreatHarmTick = Find.TickManager.TicksGame;
            var giver = new JobGiver_ReactToCloseMeleeThreat();
            var res = giver.TryIssueJobPackage(col, default(JobIssueParams));
            t.Check(res.Job == null, "in a bout the close-melee-threat giver hands out no attack on the opponent (" + res.Job?.def?.defName + ")");
            var vj = JobMaker.MakeJob(JobDefOf.AttackMelee, guest); vj.maxNumMeleeAttacks = 1; vj.expiryInterval = 200; vj.reactingToMeleeThreat = true;
            col.jobs.StartJob(vj, JobCondition.InterruptForced);
            yield return 31;
            t.Check(col.CurJobDef == PitSetup.JobFight, "a vanilla attack on the opponent is replaced by the pit job at once (" + col.CurJobDef?.defName + ")");

            // (2) a terrain kick in a scored bout
            var sand = DefDatabase<HediffDef>.GetNamedSilentFail("SandInEyes");
            if (sand != null)
            {
                var verb = new Verb_MeleeApplyHediff { caster = col, tool = new Tool { label = "kick", hediff = sand }, verbProps = new VerbProperties { verbClass = typeof(Verb_MeleeApplyHediff), bodypartTagTarget = BodyPartTagDefOf.SightSource } };
                var apply = HarmonyLib.AccessTools.Method(typeof(Verb_MeleeApplyHediff), "ApplyMeleeDamageToTarget");
                float before = bout.ScoreB;
                apply.Invoke(verb, new object[] { new LocalTargetInfo(guest) });
                t.Check(!guest.health.hediffSet.HasHediff(sand), "a terrain kick between the two adds no hediff to the guest");
                t.Check(bout.ScoreB - before == Patch_PitMeleeHediff.Points, "and scores " + Patch_PitMeleeHediff.Points + " points (" + (bout.ScoreB - before) + ")");
            }
            else t.Note("no SandInEyes hediff in this game; terrain kick not checked");

            // (3) the bout is lost as by a load: both hold a vanilla attack on each other, threats set, no Release
            foreach (var pair in new[] { (col, guest), (guest, col) })
            {
                var j = JobMaker.MakeJob(JobDefOf.AttackMelee, pair.Item2); j.maxNumMeleeAttacks = 1; j.expiryInterval = 200; j.reactingToMeleeThreat = true;
                pair.Item1.jobs.StartJob(j, JobCondition.InterruptForced);
                pair.Item1.mindState.meleeThreat = pair.Item2; pair.Item1.mindState.lastMeleeThreatHarmTick = Find.TickManager.TicksGame;
            }
            FightBout.ForgetAllForTests();
            FightBout.AbortAll("test: lost bout");
            t.Check(FightBout.ActiveCount == 0 && col.mindState.meleeThreat == null && guest.mindState.meleeThreat == null, "lost bout: threats of both cleared at once");
            yield return 5;
            t.Check(!(col.CurJobDef == JobDefOf.AttackMelee && col.CurJob.targetA.Thing == guest) && !(guest.CurJobDef == JobDefOf.AttackMelee && guest.CurJob.targetA.Thing == col), "lost bout: the vanilla attacks ended before a blow (" + col.CurJobDef?.defName + ", " + guest.CurJobDef?.defName + ")");
            foreach (var w in PitKit.Watch(t, col, guest, 600)) yield return w;
            HSettings.Set("underworld.pit.scoreLimit", 100f);
            t.Check(PitKit.GoodwillDiff(gw) == "none", "lost bout: goodwill unchanged: " + PitKit.GoodwillDiff(gw));
            sc.Check(t, "colonist", guest); sg.Check(t, "guest", col);

            // (3b) a prisoner brought in against a hired beast, lost the same way
            pit.allowPrisoners = true;
            var pr = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Villager, f, PawnGenerationContext.NonPlayer, map.Tile, canGeneratePawnRelations: false, mustBeCapableOfViolence: true));
            if (!pr.DevelopmentalStage.Adult()) { pr.ageTracker.AgeBiologicalTicks = 25L * 3600000L; pr.ageTracker.AgeChronologicalTicks = 25L * 3600000L; }
            GenSpawn.Spawn(pr, CellFinder.RandomClosewalkCellNear(outside, map, 2), map); UwKit.Track(pr);
            pr.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Prisoner);
            var from = pr.Position;
            Payments.CreditColony(map, Money.FromSilver(100), "test");
            var till0 = UwKit.Till(map);
            var hb = PitOpponents.HireBeast(pit, pr, out string why);
            t.Check(hb != null, "a beast hired for the prisoner (" + why + ")");
            if (hb != null)
            {
                var beast = hb.B;
                int w3 = 0;
                while (!hb.Finished && w3 < 2000 && !pit.ArenaInfo().Cells.Contains(pr.Position)) { yield return 15; w3 += 15; }
                t.Check(pit.WasBroughtIn(pr), "the prisoner was brought in and the pit noted where he came from");
                FightBout.ForgetAllForTests();
                FightBout.AbortAll("test: lost bout");
                t.Check(pr.Spawned && !pit.ArenaInfo().Cells.Contains(pr.Position) && pr.Position.DistanceTo(from) < 8f && !pit.WasBroughtIn(pr), "lost bout: the prisoner is back where he was (" + from + " -> " + pr.Position + ")");
                t.Check(beast.Destroyed && !pit.HasBeast && !PitOpponents.IsBeast(beast), "lost bout: the hired beast is gone");
                t.Check(UwKit.Till(map) == till0, "lost bout: the fee is back in the till (" + (till0 - UwKit.Till(map)).ToStringShort() + " missing)");
                t.Check(Ledger.Current.SumAll() == 0, "ledger balanced");
                t.Check(pr.IsPrisonerOfColony, "still a prisoner of the colony");   // (he was spawned in the open, so vanilla may let him try to escape from there)
            }
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
            // a second arena: a prisoner and a pen animal are brought in (they cannot walk in) and wait in the betting phase when the game is saved
            var extra = new List<string>();
            var pit2 = PitKit.Arena(map, out var inner2, out var outside2);
            if (pit2 != null && pit2 != pit)
            {
                var pr = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Villager, f, PawnGenerationContext.NonPlayer, map.Tile, canGeneratePawnRelations: false, mustBeCapableOfViolence: true));
                if (!pr.DevelopmentalStage.Adult()) { pr.ageTracker.AgeBiologicalTicks = 25L * 3600000L; pr.ageTracker.AgeChronologicalTicks = 25L * 3600000L; }
                GenSpawn.Spawn(pr, CellFinder.RandomClosewalkCellNear(outside2, map, 2), map);
                pr.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Prisoner);
                var pen = UwKit.TameAnimal(map, CellFinder.RandomClosewalkCellNear(outside2, map, 2), "Boar", "Muffalo");
                IntVec3 prFrom = pr.Position, penFrom = pen.Position;
                var pb = FightBout.Start(pit2, pr, pen, false, 20000, manual: true);
                int w2 = 0;
                while (pb != null && !pb.Finished && w2 < 3000 && pb.phase == FightBout.Phase.Assemble) { yield return 30; w2 += 30; }
                bool inside = pit2.ArenaInfo().Cells.Contains(pr.Position);
                t.Check(pb != null && !pb.Finished && inside && pit2.WasBroughtIn(pr), "the prisoner was brought into the second arena and waits there at save time (phase " + pb?.phase + ")");
                extra.Add("prisoner=" + pr.ThingID); extra.Add("prisonerFrom=" + prFrom.x + "," + prFrom.z);
                if (pit2.WasBroughtIn(pen)) { extra.Add("pen=" + pen.ThingID); extra.Add("penFrom=" + penFrom.x + "," + penFrom.z); }
            }
            else t.Note("no room for a second arena; the prisoner part is not saved");
            PitKit.Put(col, cells[3]); PitKit.Put(guest, cells[45]);
            HSettings.Set("underworld.pit.scoreLimit", 4000f);   // the bout must still run when the game is saved
            var bout = FightBout.Start(pit, col, guest, true, 0, manual: true);
            int waited = 0;
            while (waited < 3000 && !(col.CurJobDef == PitSetup.JobFight && guest.CurJobDef == PitSetup.JobFight)) { yield return 15; waited += 15; }
            t.Check(bout != null && !bout.Finished && col.CurJobDef == PitSetup.JobFight && guest.CurJobDef == PitSetup.JobFight, "both fighters attack each other at save time (" + col.CurJobDef?.defName + ", " + guest.CurJobDef?.defName + ")");
            yield return 120;
            t.Check(bout.ScoreA + bout.ScoreB > 0, "hits were scored before the save");
            // the worst case of the review: both fighters hold a vanilla AttackMelee on each other when the game is saved (a retaliation the think tree
            // handed out between two pit jobs), with the melee threats of both set. Saved at once, before FightBout.Order replaces them.
            foreach (var pair in new[] { (col, guest), (guest, col) })
            {
                var j = JobMaker.MakeJob(JobDefOf.AttackMelee, pair.Item2); j.maxNumMeleeAttacks = 1; j.expiryInterval = 200; j.reactingToMeleeThreat = true;
                pair.Item1.jobs.StartJob(j, JobCondition.InterruptForced);
                pair.Item1.mindState.meleeThreat = pair.Item2; pair.Item1.mindState.lastMeleeThreatHarmTick = Find.TickManager.TicksGame;
            }
            t.Check(col.CurJobDef == JobDefOf.AttackMelee && guest.CurJobDef == JobDefOf.AttackMelee, "both fighters hold a vanilla attack on each other at save time");
            var lines = new List<string> { "colonist=" + col.ThingID, "guest=" + guest.ThingID, "faction=" + guest.Faction.loadID, "goodwill=" + guest.Faction.PlayerGoodwill,
                "colInj=" + col.health.hediffSet.hediffs.Count(h => h is Hediff_Injury), "guestInj=" + guest.health.hediffSet.hediffs.Count(h => h is Hediff_Injury) };
            lines.AddRange(extra);
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
            t.Check(col.mindState.meleeThreat == null && guest.mindState.meleeThreat == null, "melee threats of both cleared at load time");
            var pr = V("prisoner") != null ? map.mapPawns.AllPawns.FirstOrDefault(p => p.ThingID == V("prisoner")) : null;
            var pen = V("pen") != null ? map.mapPawns.AllPawns.FirstOrDefault(p => p.ThingID == V("pen")) : null;
            IntVec3 Cell(string k) { var v = V(k); if (v == null) return IntVec3.Invalid; var xs = v.Split(','); return new IntVec3(int.Parse(xs[0]), 0, int.Parse(xs[1])); }
            var arena = new HashSet<IntVec3>(CompFightPit.All.Where(c => c.Map == map).SelectMany(c => c.ArenaInfo().Cells));
            void Back(Pawn p, string what, IntVec3 from) => t.Check(p != null && p.Spawned && !arena.Contains(p.Position) && from.IsValid && p.Position.DistanceTo(from) < 8f, what + " taken back from the arena at load time (" + from + " -> " + p?.Position + ")");
            if (V("prisoner") != null)
            {
                Back(pr, "prisoner", Cell("prisonerFrom"));
                t.Check(pr != null && pr.IsPrisonerOfColony, "still a prisoner of the colony");
            }
            else t.Note("the save has no prisoner part");
            if (V("pen") != null) Back(pen, "pen animal", Cell("penFrom"));
            yield return 2;   // the loaded jobs tick once: a vanilla attack would land its blow now, unless the load sweep failed it
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
