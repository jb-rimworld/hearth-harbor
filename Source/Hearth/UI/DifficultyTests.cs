// SPDX-License-Identifier: MIT
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Hearth.Guests;
using Hearth.Testing;
using Hearth.Underworld;
using Silverline;

namespace Hearth.Shell.Tests
{
    /// <summary>
    /// Every special switch and difficulty number that ships must change something in the game (no dead switches): infinite guest money, picky guests,
    /// iron bank, peaceful underworld, performance mode, research cost, guests never die.
    /// </summary>
    public sealed class DifficultySwitchesSuite : HearthTestSuite
    {
        public override string Name => "ui.difficultySwitches";

        static void Flip(string key, bool on) { var s = HSettings.Find(key); SettingRowUI.Set(s, HearthProfile.Current, on ? 1f : 0f); }
        static void Reset(string key) => HearthProfile.Current.Store.Remove(key);

        public override IEnumerable<int> Run(TestContext t)
        {
            t.Check(HearthProfile.Current != null, "a colony profile exists");
            // --- every shipped switch is registered, and nothing we removed is left behind
            foreach (var k in DifficultyPanel.SpecialKeys) t.Check(HSettings.Find(k) != null, "switch registered: " + k);
            foreach (var k in new[] { "core.noHostile", "core.feuding", "core.mandatory", "core.strictInspectors", "core.realConsequences", "core.upkeep", "core.consequenceStage", "core.houseStyle", "core.keepBuiltStyle", "core.perfGuardTps", "bank.defaultFactor" })
                t.Check(HSettings.Find(k) == null, "dead setting is gone: " + k);

            // --- infinite guest money
            var snapshot = HearthProfile.Current.Store.Clone();   // the suite puts the player's overrides back at the end
            var rng = JRand.Stream("test.difficulty");
            var map0 = t.Map;
            var p0 = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
            var normal = GuestEconomy.RollBudget(p0, 50, 2f, false, rng);
            Flip("core.infiniteMoney", true);
            var rolled = GuestEconomy.RollBudget(p0, 50, 2f, false, rng);
            t.Check(rolled.Silver < 5000 && rolled.Silver < normal.Silver * 3 + 10, "infinite money: the rolled budget stays normal (" + rolled.Silver + " vs " + normal.Silver + "), so bankrolls and fees do not inflate");
            if (!p0.Destroyed) Find.WorldPawns.PassToWorld(p0, PawnDiscardDecideMode.Discard);
            var guests0 = GuestTestKit.SpawnVisitors(map0, 1);
            if (guests0.Count > 0)
            {
                var g0 = guests0[0]; var gi0 = GuestApi.Info(g0);
                t.Check(gi0 != null, "a guest to pay with");
                if (gi0 != null)
                {
                    gi0.Budget = Money.FromSilver(3);
                    var inv = g0.inventory?.innerContainer;
                    if (inv != null) foreach (var th in inv.ToList()) if (th.def == ThingDefOf.Silver) th.Destroy();
                    var price = Money.FromSilver(900);
                    bool paid = GuestApi.ChargeGuest(g0, price, "test", "infinite money test");
                    t.Check(paid, "infinite money: a guest with a 3 silver budget pays 900 silver");
                    t.Check(gi0.Budget.Silver < 100, "infinite money: the budget is topped up to the price, not inflated (" + gi0.Budget.Silver + ")");
                }
                Reset("core.infiniteMoney");
                if (gi0 != null)
                {
                    gi0.Budget = Money.FromSilver(3);
                    t.Check(!GuestApi.ChargeGuest(g0, Money.FromSilver(900), "test", "no switch"), "without the switch the same guest cannot pay 900 silver");
                }
                foreach (var g in guests0) if (!g.Destroyed) g.Destroy();
            }
            else Reset("core.infiniteMoney");

            // --- picky guests
            float e0 = GuestEconomy.Expectation(50, 10f, null);
            Flip("core.picky", true); float e1 = GuestEconomy.Expectation(50, 10f, null); Reset("core.picky");
            t.Check(Mathf.Abs(e1 - e0 - GuestEconomy.PickyExpectation) < 1e-4f, "picky: expectation " + e0 + " -> " + e1);

            // --- iron bank
            int stage0 = BankSettings.MaxCollectionStage;
            Flip("core.ironBank", true); int stage1 = BankSettings.MaxCollectionStage; double anger1 = BankSettings.CollectionAnger; Reset("core.ironBank");
            t.Check(anger1 >= 1.0, "iron bank: collection anger is at least 1 so stage 5-7 are not toothless (" + anger1 + ")");
            // the panel's penalty caps are the numbers the underworld really applies
            for (int ti = 0; ti < 7; ti++)
            {
                string Fmt(float v, bool neg) => float.IsInfinity(v) ? "\u221e" : (v == 0f ? "0" : (neg ? "\u2212" : "") + v.ToString("0"));
                t.Check(TierTable.GoodwillCap[ti] == Fmt(UnderworldMath.GoodwillCapPerEvent[ti], true) && TierTable.RenownCap[ti] == Fmt(UnderworldMath.RenownCapPerEvent[ti], true) && TierTable.FineCap[ti] == Fmt(UnderworldMath.FineCap[ti], false), "penalty cap display matches the underworld at tier " + ti);
            }
            t.Check(stage1 == 7 && BankSettings.MaxCollectionStage == stage0, "iron bank: collection stage " + stage0 + " -> " + stage1);

            // --- peaceful underworld: every violent feature closes, the facilities stay
            bool fac0 = Underworld.Underworld.Allows(UwFeature.Facilities);
            Flip("core.peacefulUnderworld", true);
            bool anyViolent = false;
            foreach (UwFeature f in Enum.GetValues(typeof(UwFeature))) if (UnderworldMath.IsViolent(f) && Underworld.Underworld.Allows(f)) anyViolent = true;
            bool fac1 = Underworld.Underworld.Allows(UwFeature.Facilities), animals = UnderworldMath.Allows(3, UwFeature.HuntAnimals) && !UnderworldMath.IsViolent(UwFeature.HuntAnimals);
            Reset("core.peacefulUnderworld");
            t.Check(!anyViolent, "peaceful underworld: no violent feature is allowed");
            t.Check(fac1 == fac0 && animals, "peaceful underworld: facilities and animal hunts are untouched");
            t.Check(UnderworldMath.IsViolent(UwFeature.PitToDeath) && UnderworldMath.IsViolent(UwFeature.ProtectionRacket) && !UnderworldMath.IsViolent(UwFeature.Facilities), "violent feature list");

            // --- performance mode
            int i0 = PerfMode.Interval(250);
            Flip("core.perfMode", true); int i1 = PerfMode.Interval(250); Reset("core.perfMode");
            t.Check(i0 == 250 && i1 == 500, "performance mode doubles the rare-tick interval: " + i0 + " -> " + i1);

            // --- research cost
            var proj = DefDatabase<ResearchProjectDef>.AllDefsListForReading.FirstOrDefault(ResearchGate.IsOurs);
            t.Check(proj != null, "a project of our research tabs exists");
            if (proj != null)
            {
                ResearchGate.ApplyCostFactor();
                float printed = ResearchGate.PrintedCost(proj);
                SettingRowUI.Set(HSettings.Find("core.researchCost"), HearthProfile.Current, 2f);
                ResearchGate.ApplyCostFactor();
                float doubled = proj.baseCost;
                SettingRowUI.Set(HSettings.Find("core.researchCost"), HearthProfile.Current, 0.5f);
                ResearchGate.ApplyCostFactor();
                float halved = proj.baseCost;
                Reset("core.researchCost");
                ResearchGate.ApplyCostFactor();
                float back = proj.baseCost;
                float def = ResearchGate.CostFactor;
                t.Check(Mathf.Abs(doubled - Mathf.Round(printed * 2f)) < 1.01f && Mathf.Abs(halved - Mathf.Max(1f, Mathf.Round(printed * 0.5f))) < 1.01f, $"research cost follows the factor: printed {printed}, x2 {doubled}, x0.5 {halved}");
                t.Check(Mathf.Abs(back - Mathf.Max(1f, Mathf.Round(printed * def))) < 1.01f, "research cost returns to the setting default: " + back);
            }

            // --- guests never die
            var map = t.Map;
            var pair = GuestTestKit.SpawnVisitors(map, 2);
            t.Check(pair.Count >= 2, "two visitors spawned: " + pair.Count);
            if (pair.Count >= 2)
            {
                Flip("core.neverDie", true);
                var saved = pair[0]; var control = pair[1];
                // control without the switch dies from the same wound: switch it off for that one by hitting it first
                Reset("core.neverDie");
                Kill(control);
                Flip("core.neverDie", true);
                t.Check(Patch_HealthTracker_ShouldBeDead.Protects(saved), "the guest is protected by the switch (guest " + GuestApi.IsGuest(saved) + ", hostile " + saved.HostileTo(Faction.OfPlayer) + ", faction " + saved.Faction?.Name + ", inn " + Modules.On(ModuleId.Inn) + ")");
                Kill(saved);
                yield return 30;
                Reset("core.neverDie");
                t.Check(control.Dead, "control: without the switch a lethal wound kills the visitor");
                t.Check(!saved.Dead && saved.Downed, "with the switch the lethal wound only downs the guest (dead " + saved.Dead + ", downed " + saved.Downed + ")");
                var colonist = map.mapPawns.FreeColonistsSpawned.FirstOrDefault();
                if (colonist != null)
                {
                    Flip("core.neverDie", true);
                    t.Check(!Patch_HealthTracker_ShouldBeDead.Protects(colonist), "colonists are never protected by the guest switch");
                    Reset("core.neverDie");
                }
                foreach (var p in pair) if (!p.Destroyed) p.Destroy();
            }
            // the protection survives save/load: the set is a saved game component and prunes pawns that are gone
            var set = NeverDieSet.Instance;
            t.Check(set != null, "the guests-never-die set exists as a game component");
            if (set != null)
            {
                int before = set.Count;
                var ghost = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                set.Add(ghost);
                t.Check(set.Count == before + 1, "the set remembers a pawn");
                set.Prune();
                t.Check(set.Count == before, "pruning drops a pawn that is not on any map");
                if (!ghost.Destroyed) Find.WorldPawns.PassToWorld(ghost, PawnDiscardDecideMode.Discard);
            }
            HearthProfile.Current.Store = snapshot;
            ResearchGate.ApplyCostFactor();
            yield break;
        }

        static void Kill(Pawn p)
        {
            var core = p.RaceProps.body.corePart;
            p.TakeDamage(new DamageInfo(DamageDefOf.Cut, 5000f, 999f, -1f, null, core));
        }
    }
}
#endif
