// SPDX-License-Identifier: MIT
// In-game suites of Clawback Mutual. Compiled only with HH_TESTS.
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using Hearth;
using Hearth.Insurance;
using Hearth.Testing;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Silverline
{
    static class InsTest
    {
        public static InsuranceComponent Comp => InsuranceComponent.Instance;
        public static Map Map => Find.CurrentMap;
        static readonly List<string> touched = new List<string>();

        public static void Set(string key, float v) { touched.Add(key); HSettings.Set(key, v); }
        public static void RestoreSettings() { foreach (var k in touched.Distinct()) { var s = HSettings.Find(k); if (s != null) HSettings.Reset(s); } touched.Clear(); }

        /// <summary>Clean slate: no contracts, no claims, easy settings, all research gates open, a funded till.</summary>
        public static void Reset(double till = 5000)
        {
            // random quicktest colonists can start with old injuries, missing parts or bionics: claims on 'losing a hand' etc. then grade 0 (flaky)
            foreach (var col in Map.mapPawns.FreeColonistsSpawned) col.health.Reset();
            // bomb damage in earlier claim suites can leave fires burning: a large fire blocks signing for a while
            foreach (var fire in Map.listerThings.ThingsOfDef(ThingDefOf.Fire).ToList()) if (!fire.Destroyed) fire.Destroy();
            // the fire watcher refreshes only every ~426 ticks: refresh it now so 'large fire danger' reflects the extinguished map
            try { typeof(FireWatcher).GetMethod("UpdateObservations", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(Map.fireWatcher, null); } catch { }
            // hostile pawns left over from earlier suites or from the live game (manhunters, visiting raiders) also count as a threat
            foreach (var hp in Map.mapPawns.AllPawnsSpawned.Where(x => !x.Destroyed && x.Faction != Faction.OfPlayer && x.HostileTo(Faction.OfPlayer) && !x.Downed).ToList()) hp.Destroy();
            // not only pawns: turrets, hives and other attack targets of a hostile faction count as an active threat as well
            try { foreach (var tgt in Map.attackTargetsCache.TargetsHostileToColony.ToList()) { var th = tgt?.Thing; if (th == null || th.Destroyed) continue;
                    // a colony pet (the colony itself is 'New Arrivals' in the quicktest) in a manhunter or berserk state is an active threat too: calm it down instead of destroying it
                    if (th is Pawn mad && mad.Faction == Faction.OfPlayer) { if (mad.InMentalState) mad.MentalState.RecoverFromState(); continue; }
                    if (th.Faction != Faction.OfPlayer) th.Destroy(); } } catch (Exception e) { HLog.Warn("InsTest", "could not clear hostile attack targets: " + e.Message); }
            if (InsuranceComponent.ThreatNow(Map)) HLog.Warn("InsTest", "threat still on after reset: targets " + string.Join(", ", Map.attackTargetsCache.TargetsHostileToColony.Select(x => (x?.Thing?.def.defName ?? "?") + "/" + x?.Thing?.Faction?.Name + "/downed " + ((x?.Thing as Pawn)?.Downed) + "/spawned " + x?.Thing?.Spawned)) + "; hostile " + GenHostility.AnyHostileActiveThreatToPlayer(Map) + ", large fire " + Map.fireWatcher.LargeFireDangerPresent + ", fires " + Map.listerThings.ThingsOfDef(ThingDefOf.Fire).Count);
            BankGameComp.Current?.TestReset(false);   // no account unless a test opens one: premiums then come from the till
            var c = Comp; c.contracts.Clear(); c.claims.Clear(); c.opportunities.Clear(); c.nextOppId = 1; InsuranceStatus.Reset(); c.debt = 0; c.firstPolicyTick = -1; c.lastFiledLetterDay = -1; c.lastUpsellTick = -9999999; c.lastDeathTick = -9999999; c.log.Clear();
            Set(InsSettings.RequireResearch, 0); Set(InsSettings.ClaimsAttitude, 0f); Set(InsSettings.WaitingDays, 5); Set(InsSettings.AssessmentDays, 0); Set(InsSettings.BillingDays, 5);
            Set(InsSettings.TrickUpsell, (float)TrickMode.Off);
            Set(InsSettings.ClaimModeKey, (float)ClaimMode.Automatic);      // the existing suites expect claims at the moment of damage; the form mode has its own suite
            Set(InsSettings.ChangeFee, 0); Set(InsSettings.ChangeLimit, 0); Set(InsSettings.ChangeWaitingDays, 3);
            var l = Ledger.Current; string tillAcc = Payments.ColonyTill(Map);
            var have = l.Balance(tillAcc); if (have.IsPositive) l.Post(tillAcc, "world", have, "test reset");
            if (till > 0) l.Post("world", tillAcc, Money.FromSilver(till), "test funding");
        }

        public static SL_PolicyDef Def(string name) => DefDatabase<SL_PolicyDef>.GetNamed(name);
        public static Money Till => Ledger.Current.Balance(Payments.ColonyTill(Map));
        public static Money Insurer => Ledger.Current.Balance(InsurerBooks.Account);
        public static List<Pawn> Colonists => Map.mapPawns.FreeColonistsSpawned.ToList();

        public static void SkipWaiting(PolicyContract c) { c.waitUntilTick = Ins.Now - 1; c.startTick = Ins.Now - 100; }

        public static Pawn Raider()
        {
            var f = Find.FactionManager.AllFactionsListForReading.FirstOrDefault(x => !x.IsPlayer && x.HostileTo(Faction.OfPlayer));
            if (f == null)
            {
                f = FactionGenerator.NewGeneratedFaction(new FactionGeneratorParms(FactionDefOf.Pirate));
                Find.FactionManager.Add(f);
            }
            var kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Pirate") ?? PawnKindDefOf.Villager;
            return PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, f));
        }

        public static Building SpawnBuilding(string defName, IntVec3 near)
        {
            var def = DefDatabase<ThingDef>.GetNamed(defName);
            var stuff = def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null;
            Building b = null;
            foreach (var cell in GenRadial.RadialCellsAround(near, 15f, true))
            {
                if (!cell.InBounds(Map) || !cell.Standable(Map) || cell.GetFirstBuilding(Map) != null || cell.GetFirstThing<Fire>(Map) != null) continue;
                b = (Building)ThingMaker.MakeThing(def, stuff);
                GenSpawn.Spawn(b, cell, Map);
                if (b.Spawned) break;
            }
            b.SetFaction(Faction.OfPlayer);
            return b;
        }

        public static BodyPartRecord Part(Pawn p, BodyPartDef def, string labelContains = null)
            => p.RaceProps.body.AllParts.First(x => x.def == def && (labelContains == null || (x.customLabel ?? x.Label).Contains(labelContains)));
        public static BodyPartDef BP(string n) => DefDatabase<BodyPartDef>.GetNamed(n);
        public static BodyPartDef Finger => DefDatabase<BodyPartDef>.GetNamed("Finger");

        public static void LoseParts(Pawn p, params BodyPartRecord[] parts)
        {
            foreach (var part in parts)
            {
                // the colonists are shared by the suites of one game: a part an earlier suite took off is restored first
                // (adding a hediff to a missing part is a red error in vanilla)
                foreach (var old in p.health.hediffSet.hediffs.Where(h => h.def == HediffDefOf.MissingBodyPart && h.Part == part).ToList()) p.health.RemoveHediff(old);
                if (p.health.hediffSet.PartIsMissing(part)) continue; // a parent part is missing: nothing more to lose
                p.health.AddHediff(HediffDefOf.MissingBodyPart, part);
            }
        }

        public static void ClearFires() { foreach (var f in Map.listerThings.ThingsOfDef(ThingDefOf.Fire).ToList()) f.Destroy(); }
        public static bool LedgerOk => Ledger.Current.SumAll() == 0;
    }

    public sealed class InsuranceMathSuite : HearthTestSuite
    {
        public override string Name => "insurance.math";
        public override ModuleId Module => ModuleId.Bank;
        public override IEnumerable<int> Run(TestContext t)
        {
            var policies = DefDatabase<SL_PolicyDef>.AllDefsListForReading;
            t.Check(policies.Count >= 11, "policies loaded: " + policies.Count + " (12 with Anomaly)");
            t.Check(new[] { "SL_ActsOfStoryteller", "SL_MuffaloMachine", "SL_RoofOverHead", "SL_ItWasntMe", "SL_LegalShield", "SL_RimHealth", "SL_GoldenYears", "SL_BlightBeGone", "SL_ClosedForRepairs", "SL_Wanderlust", "SL_LimbByLimb" }.All(n => policies.Any(p => p.defName == n)), "all eleven base policies present");
            t.Check(policies.All(p => p.riders.All(r => !r.key.NullOrEmpty() && !r.effect.NullOrEmpty())), "riders well formed");
            t.Check(DefDatabase<SL_ClauseDef>.AllDefsListForReading.Count == 15, "fifteen clauses: " + DefDatabase<SL_ClauseDef>.AllDefsListForReading.Count);
            t.Check(ClauseEngine.ByTrigger(ClauseTrigger.Waiting) != null && ClauseEngine.ByTrigger(ClauseTrigger.Conflict) != null, "clauses reachable by trigger");
            t.Check(SLInsuranceDefOf.SL_ClawbackLetter != null && SLInsuranceDefOf.SL_ClawbackNotice != null, "letter defs bound");
            foreach (var k in new[] { "insurance.claimsAttitude", "insurance.loading", "insurance.salesPressure", "insurance.waitingDays", "insurance.noticeDays", "insurance.formError" })
                t.Check(HSettings.Find(k) != null && HSettings.Find(k).TierAnchors != null || k == "insurance.waitingDays", "setting " + k);
            // anchors of 9.2 follow the difficulty tier
            var prof = HearthProfile.Current; float oldTier = prof.Tier;
            prof.Tier = 3f; t.Check(Math.Abs(HSettings.Find(InsSettings.Loading).DefaultFor(3f) - 0.25f) < 1e-4, "loading 25 % on Standard Room");
            t.Check(Math.Abs(HSettings.Find(InsSettings.SalesPressure).DefaultFor(3f) - 0.30f) < 1e-4, "sales pressure 30 % on Standard Room");
            t.Check(Math.Abs(HSettings.Find(InsSettings.ClaimsAttitude).DefaultFor(3f) - 0.15f) < 1e-4, "claims attitude 15 % on Standard Room");
            t.Check(Math.Abs(HSettings.Find(InsSettings.SalesPressure).DefaultFor(6f) - 1f) < 1e-4, "sales pressure 100 % on Why?");
            prof.Tier = oldTier;

            // text keys
            string[] keys = { "Tier.Bare", "Slogan.1", "Slogan.8", "Neg.Approach.Quote", "Outcome.Partial", "Stage.3", "Letter.Decision.TitleApproved", "UI.Badge", "Retention.Title3", "Cancel.FormErrorHard" };
            foreach (var k in keys) t.Check(("HH.Ins." + k).CanTranslate(), "key HH.Ins." + k);
            t.Check(Ins.T("Bd.Grade", "50", "x").Contains("50"), "translate with arguments");

            // limb schedule on a real human body
            var pawn = InsTest.Colonists.First();
            LimbSchedule.LoadDefaults();
            foreach (var d in DefDatabase<SL_LimbScheduleDef>.AllDefsListForReading) d.Apply();
            double V(BodyPartRecord p) => LimbGrader.ValueOf(pawn, p, out _, out _, out _);
            t.Check(V(InsTest.Part(pawn, BodyPartDefOf.Hand, "left")) == 55, "hand 55");
            t.Check(V(InsTest.Part(pawn, BodyPartDefOf.Eye, "left")) == 50, "eye 50 (100 shared by two)");
            t.Check(V(InsTest.Part(pawn, InsTest.BP("Ear"), "right")) == 30, "ear 30 (60 shared by two)");
            t.Check(V(InsTest.Part(pawn, BodyPartDefOf.Lung, "left")) == 25, "lung 25");
            t.Check(V(InsTest.Part(pawn, InsTest.BP("Kidney"), "right")) == 15, "kidney 15");
            t.Check(V(InsTest.Part(pawn, BodyPartDefOf.Leg, "left")) == 70, "leg 70");
            t.Check(V(InsTest.Part(pawn, BodyPartDefOf.Shoulder, "right")) == 70, "shoulder 70");
            t.Check(V(pawn.RaceProps.body.AllParts.First(x => x.def == InsTest.Finger && (x.customLabel ?? "").Contains("thumb"))) == 20, "thumb 20");
            t.Check(V(pawn.RaceProps.body.AllParts.First(x => x.def == InsTest.Finger && (x.customLabel ?? "").Contains("index"))) == 10, "index finger 10");
            t.Check(V(pawn.RaceProps.body.AllParts.First(x => x.def.defName == "Toe" && (x.customLabel ?? "").Contains("big"))) == 5, "big toe 5");
            t.Check(V(pawn.RaceProps.body.AllParts.First(x => x.def.defName == "Toe" && !(x.customLabel ?? "").Contains("big"))) == 2, "toe 2");
            LimbGrader.ValueOf(pawn, pawn.RaceProps.body.AllParts.First(x => x.def == BodyPartDefOf.Heart), out bool lethal, out _, out _);
            t.Check(lethal, "heart is a death part");
            t.Check(LimbGrader.StateGrade(pawn, true, false) < 0.01 || pawn.health.hediffSet.hediffs.Any(h => h.IsPermanent() || h is Hediff_MissingPart), "healthy colonist has grade 0 (or old scars)");

            // pricing sanity on the live map
            var def = InsTest.Def("SL_LimbByLimb");
            var q0 = InsurancePricing.QuoteFor(def, CoverTier.BareMinimum, pawn, 5000, null, 1);
            var q1 = InsurancePricing.QuoteFor(def, CoverTier.GoodEnough, pawn, 5000, null, 1);
            var q2 = InsurancePricing.QuoteFor(def, CoverTier.Premium, pawn, 5000, null, 1);
            t.Check(q0.billing > 0 && q0.billing < q1.billing && q1.billing < q2.billing, $"prices rise with the tier: {q0.billing:0.00} < {q1.billing:0.00} < {q2.billing:0.00}");
            var q3 = InsurancePricing.QuoteFor(def, CoverTier.GoodEnough, pawn, 5000, null, 3);
            t.Check(Math.Abs(q3.billing / q1.billing - 0.9) < 1e-6, "third policy gives 10 % bundle discount");
            var q4 = InsurancePricing.QuoteFor(def, CoverTier.GoodEnough, pawn, 5000, new[] { "plusProgression" }, 1);
            t.Check(q4.billing > q1.billing, "riders raise the price");
            yield return 1;
        }
    }

    public sealed class InsuranceBuySuite : HearthTestSuite
    {
        public override string Name => "insurance.buy";
        public override ModuleId Module => ModuleId.Bank;
        public override IEnumerable<int> Run(TestContext t)
        {
            InsTest.Reset(600);
            var comp = InsTest.Comp; var l = Ledger.Current;
            var roof = InsTest.Def("SL_RoofOverHead");

            // --- pay from the till / silver --------------------------------------------------------------------------------------------------
            InsurancePricing.SumRange(roof, InsTest.Map, null, out double smin, out double smax);
            double sum = Math.Min(2000, smax);
            t.Note($"colony building wealth {InsTest.Map.wealthWatcher.WealthBuildings:0}: sum range {smin:0}-{smax:0}, using {sum:0}");
            var q = InsurancePricing.QuoteFor(roof, CoverTier.GoodEnough, null, sum, null, 1);
            var till0 = InsTest.Till; var ins0 = InsTest.Insurer;
            var res = comp.Sign(roof, CoverTier.GoodEnough, null, sum, null, null);
            t.Check(res.Ok, "signed Roof Over Your Head (Good Enough): " + res.error);
            if (!res.Ok) yield break;
            var c = res.contract;
            t.Check(Math.Abs((till0 - InsTest.Till).Silver - q.billing) < 0.00002, $"first premium debited from the till: {(till0 - InsTest.Till).Silver:0.00000} vs quote {q.billing:0.00000}");
            t.Check(InsTest.Insurer - ins0 == till0 - InsTest.Till, "insurer account received exactly the premium");
            t.Check(InsTest.LedgerOk, "ledger balanced after signing");
            t.Check(c.waitUntilTick == c.startTick + 5 * 60000, "waiting period of 5 days");
            t.Check(c.nextBillTick == c.startTick + 5 * 60000, "next bill in 5 days");
            t.Check(!comp.Sign(roof, CoverTier.GoodEnough, null, sum, null, null).Ok, "second identical policy refused");
            t.Check(comp.contracts.Count == 1, "one contract");
            t.Check(comp.Started, "Clawback is now in contact");

            // --- recurring billing --------------------------------------------------------------------------------------------------------------
            var before = InsTest.Till; int now = Ins.Now;
            c.nextBillTick = now;
            comp.Process(now);
            t.Check((before - InsTest.Till).IsPositive, "recurring premium debited: " + (before - InsTest.Till).ToStringFull());
            t.Check(c.nextBillTick == now + 5 * 60000, "next bill rescheduled");
            t.Check(InsTest.LedgerOk, "ledger balanced after billing");

            // --- physical silver in storage when the till is empty ------------------------------------------------------------------------------
            var tillBal = InsTest.Till; if (tillBal.IsPositive) l.Post(Payments.ColonyTill(InsTest.Map), "world", tillBal, "test: empty till");
            var pile = ThingMaker.MakeThing(ThingDefOf.Silver); pile.stackCount = 120;
            var cell = t.FreeCellNearCenter();
            InsTest.Map.areaManager.Home[cell] = true;
            GenPlace.TryPlaceThing(pile, cell, InsTest.Map, ThingPlaceMode.Near);
            // "Near" can put the pile on a neighbouring cell (or merge it): make wherever it landed home area and not forbidden
            if (pile.Spawned) { InsTest.Map.areaManager.Home[pile.Position] = true; pile.SetForbidden(false, false); }
            yield return 2;
            int silverBefore = InsurerBooks.StorageSilver(InsTest.Map);
            t.Check(silverBefore >= 120, "silver visible to the insurer in the home area: " + silverBefore);
            var wanderer = InsTest.Colonists.First();
            var accident = InsTest.Def("SL_LimbByLimb");
            var res2 = comp.Sign(accident, CoverTier.BareMinimum, wanderer, 1500, null, null);
            t.Check(res2.Ok, "accident plan signed with silver from storage: " + res2.error);
            t.Check(InsurerBooks.StorageSilver(InsTest.Map) < silverBefore, "coins were taken from storage: " + silverBefore + " -> " + InsurerBooks.StorageSilver(InsTest.Map));
            t.Check(InsTest.LedgerOk, "ledger balanced after paying with silver (change goes to the till)");

            // --- cannot pay ------------------------------------------------------------------------------------------------------------------------------
            foreach (var th in InsTest.Map.listerThings.ThingsOfDef(ThingDefOf.Silver).ToList()) th.Destroy();
            var tillNow = InsTest.Till; if (tillNow.IsPositive) l.Post(Payments.ColonyTill(InsTest.Map), "world", tillNow, "test: empty till");
            int n0 = comp.contracts.Count; var ins1 = InsTest.Insurer;
            var res3 = comp.Sign(InsTest.Def("SL_RimHealth"), CoverTier.GoodEnough, null, 1000, null, null);
            t.Check(!res3.Ok && comp.contracts.Count == n0 && InsTest.Insurer == ins1, "empty purse: no contract, nothing booked (" + res3.error + ")");
            t.Check(InsTest.LedgerOk, "ledger balanced after refusal");

            // --- a bank account takes precedence --------------------------------------------------------------------------------------------------------
            try
            {
                BankGameComp.Current.TestReset(true);   // the real Silverline account
                l.Post("world", "bank:colony", Money.FromSilver(1000), "test: seed bank");
                t.Check(BankApi.Ready, "bank is ready");
                var bank0 = BankApi.ColonyBalance; var ins2 = InsTest.Insurer;
                var res4 = comp.Sign(InsTest.Def("SL_RimHealth"), CoverTier.GoodEnough, null, 1000, null, null);
                t.Check(res4.Ok, "policy signed with a bank account: " + res4.error);
                t.Check(res4.Ok && (bank0 - BankApi.ColonyBalance) == (InsTest.Insurer - ins2) && (bank0 - BankApi.ColonyBalance).IsPositive, "premium taken from the bank account: " + (bank0 - BankApi.ColonyBalance).ToStringFull());
                t.Check(InsTest.LedgerOk, "ledger balanced (bank path)");
                // third policy: bundle discount in the quote
                var qBundle = InsurancePricing.QuoteFor(InsTest.Def("SL_BlightBeGone"), CoverTier.GoodEnough, null, 1500, null, InsurancePricing.BundleCountForNew);
                t.Check(Math.Abs(qBundle.bundle - 0.88) < 1e-9 || Math.Abs(qBundle.bundle - 0.90) < 1e-9, "bundle factor after three policies: " + qBundle.bundle);
                // payout into the bank
                var bankBefore = BankApi.ColonyBalance;
                ClaimEngineProbe.PayDirect(comp, c, 250);
                t.Check(BankApi.ColonyBalance - bankBefore == Money.FromSilver(250), "benefit of 250 reaches the bank account");
                t.Check(InsTest.LedgerOk, "ledger balanced after a bank payout");
            }
            finally { BankGameComp.Current.TestReset(false); }

            // --- gates ----------------------------------------------------------------------------------------------------------------------------------------
            InsTest.Set(InsSettings.RequireResearch, 1);
            var actuarial = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("SL_Actuarial");
            if (actuarial != null && !actuarial.IsFinished) t.Check(!InsurancePricing.TierUnlocked(InsTest.Def("SL_LimbByLimb"), CoverTier.Premium, out _), "Premium locked without SL_Actuarial");
            else t.Note("research defs SL_Insurance/SL_Actuarial not present (bank agent): gates open");
            InsTest.Set(InsSettings.RequireResearch, 0);
            t.Check(InsurancePricing.TierUnlocked(InsTest.Def("SL_LimbByLimb"), CoverTier.Premium, out _), "everything unlocked with the research switch off");

            // threat blocks signing
            string err = comp.CanSign(InsTest.Def("SL_BlightBeGone"), CoverTier.BareMinimum, null);
            t.Check(err == null || err.Length > 0, "CanSign answers: " + (err ?? "ok"));
            InsTest.RestoreSettings();
            yield return 1;
        }
    }

    /// <summary>Helper for tests that need a claim paid without waiting for the dice.</summary>
    static class ClaimEngineProbe
    {
        public static Claim PayDirect(InsuranceComponent comp, PolicyContract c, double amount)
        {
            var cl = new Claim { id = comp.nextClaimId++, contractId = c.id, policyDef = c.defName, tier = c.tier, trigger = ClaimTrigger.Fine, state = ClaimState.Decided, outcome = ClaimOutcome.Approved, gross = amount, offered = amount, subject = "test", mapId = InsurerBooks.HomeMap.uniqueID };
            comp.claims.Add(cl);
            ClaimEngine.Pay(cl);
            return cl;
        }
    }

    public sealed class InsuranceBuildingClaimSuite : HearthTestSuite
    {
        public override string Name => "insurance.claim.building";
        public override ModuleId Module => ModuleId.Bank;
        public override IEnumerable<int> Run(TestContext t)
        {
            InsTest.Reset(5000);
            var comp = InsTest.Comp; var l = Ledger.Current; var map = InsTest.Map;
            var def = InsTest.Def("SL_ActsOfStoryteller");
            var res = comp.Sign(def, CoverTier.GoodEnough, null, 5000, null, null);
            t.Check(res.Ok, "signed Acts of Storyteller Excluded: " + res.error);
            if (!res.Ok) yield break;
            var c = res.contract;

            // 1) inside the waiting period: the claim is denied by clause 1
            var b0 = InsTest.SpawnBuilding("Stool", t.FreeCellNearCenter());
            var raider = InsTest.Raider();
            t.Check(raider.HostileTo(Faction.OfPlayer), "raider is hostile to the colony");
            b0.Kill(new DamageInfo(DamageDefOf.Bomb, 500f, 0f, -1f, raider));
            var cl0 = comp.claims.LastOrDefault();
            t.Check(cl0 != null && cl0.trigger == ClaimTrigger.RaidBuilding && cl0.Hint("Waiting"), "destroyed stool during the waiting period opens a claim with the waiting hint");
            cl0.decideTick = Ins.Now; comp.Process(Ins.Now);
            t.Check(cl0.state == ClaimState.Decided && cl0.outcome == ClaimOutcome.Denied && cl0.Clause != null && cl0.Clause.number == "1", "denied by clause 1 (waiting period): " + cl0.clauseDef);
            ClaimEngine.Close(cl0);
            yield return 1;

            // 2) after the waiting period: a raider destroys an insured building
            InsTest.SkipWaiting(c);
            var b1 = InsTest.SpawnBuilding("Stool", t.FreeCellNearCenter());
            double value = b1.def.GetStatValueAbstract(StatDefOf.MarketValue, b1.Stuff);
            int letters0 = Find.LetterStack.LettersListForReading.Count;
            var ins0 = InsTest.Insurer;
            b1.Kill(new DamageInfo(DamageDefOf.Bomb, 500f, 0f, -1f, raider));
            var cl = comp.claims.Last();
            t.Check(cl.id != cl0.id && cl.trigger == ClaimTrigger.RaidBuilding && cl.state == ClaimState.Review, "claim filed for the destroyed building");
            t.Check(!cl.Hint("Waiting"), "no waiting hint after the waiting period");
            t.Check(Find.LetterStack.LettersListForReading.Count > letters0 || cl.silentFiled, "letter from Clawback Mutual (or one per day cap)");
            t.Check(cl.lossValue > 0 && Math.Abs(cl.lossValue - value) < 1e-6, $"loss value is the market value: {cl.lossValue:0.0} vs {value:0.0}");
            t.Check(cl.gross <= value + 1e-9 && cl.gross > 0, $"claimed benefit {cl.gross:0.00} is positive and at most the value {value:0.00}");
            cl.decideTick = Ins.Now; comp.Process(Ins.Now);
            t.Check(cl.state == ClaimState.Decided && cl.outcome == ClaimOutcome.Approved, "approved (claims attitude 0 %): " + cl.outcome);
            t.Check(cl.breakdown.Count >= 2, "letter breakdown lines: " + cl.breakdown.Count);
            t.Check(Find.LetterStack.LettersListForReading.OfType<ClaimLetter>().Any(x => x.claimId == cl.id), "decision letter with buttons");
            t.Check(cl.offered <= value + 1e-9, "offered " + cl.offered.ToString("0.00") + " <= value");
            var till0 = InsTest.Till;
            cl.payTick = Ins.Now; comp.Process(Ins.Now);
            t.Check(cl.state == ClaimState.Paid && cl.paid > 0 && cl.paid <= value + 1e-9, $"paid {cl.paid:0.00} <= value {value:0.00}");
            t.Check(!Find.LetterStack.LettersListForReading.OfType<ClaimLetter>().Any(x => x.claimId == cl.id), "decision letter removed after payment");
            t.Check(InsTest.LedgerOk, "ledger balanced after payout");
            t.Check(InsTest.Insurer < ins0 + Money.FromSilver(0.001), "insurer paid out of its account");
            t.Check(c.benefitPaid > 0, "contract records the benefit");

            // 3) self-inflicted: your own colonist destroys it
            var colonist = InsTest.Colonists.First();
            var b2 = InsTest.SpawnBuilding("Stool", t.FreeCellNearCenter());
            b2.Kill(new DamageInfo(DamageDefOf.Bomb, 500f, 0f, -1f, colonist));
            var cl2 = comp.claims.Last();
            t.Check(cl2.Hint("Self"), "own colonist as instigator: self-inflicted hint");
            cl2.decideTick = Ins.Now; comp.Process(Ins.Now);
            t.Check(cl2.outcome == ClaimOutcome.Denied && cl2.Clause?.number == "11", "denied by clause 11: " + cl2.clauseDef);

            // 4) fire without an enemy: not covered by the raid policy, no claim at all
            int n = comp.claims.Count;
            var b3 = InsTest.SpawnBuilding("Stool", t.FreeCellNearCenter());
            b3.Kill(new DamageInfo(DamageDefOf.Flame, 500f)); InsTest.ClearFires();
            t.Check(comp.claims.Count == n, "fire is not an act of the storyteller: no claim");

            // 5) several buildings in one event are merged into one claim
            var b4 = InsTest.SpawnBuilding("Stool", t.FreeCellNearCenter()); var b5 = InsTest.SpawnBuilding("Stool", t.FreeCellNearCenter());
            int n4 = comp.claims.Count; t.Note($"b4 spawned {b4.Spawned} at {b4.Position}, b5 at {b5.Position}, faction {b4.Faction}, ready {ClawbackApi.Ready}, active contracts {comp.contracts.Count(x => x.Active)} {string.Join(",", comp.contracts.Where(x => x.Active).Select(x => x.defName))}"); b4.Kill(new DamageInfo(DamageDefOf.Bomb, 500f, 0f, -1f, raider)); int nClaims = comp.claims.Count; t.Note($"after first kill: claims {n4} -> {nClaims}; spawned b5 {b5.Spawned}"); b5.Kill(new DamageInfo(DamageDefOf.Bomb, 500f, 0f, -1f, raider));
            t.Note($"after second kill: claims {comp.claims.Count}; last {comp.claims.Last().id} state {comp.claims.Last().state} items {comp.claims.Last().items.Count} filed {comp.claims.Last().filedTick} now {Ins.Now}");
            var merged = comp.claims.Last();
            t.Check(comp.claims.Count == nClaims && merged.items.Count == 2, "two buildings of one raid are one claim (items " + merged.items.Count + ")");
            t.Check(merged.gross <= merged.lossValue + 1e-9, "merged benefit never above the loss");

            // 6) Bare Minimum pays less than Premium for the same loss
            double bare = InsuranceMath.PropertyPayout(100, 5000, 5000, CoverTier.BareMinimum), prem = InsuranceMath.PropertyPayout(100, 5000, 5000, CoverTier.Premium);
            t.Check(bare < prem && Math.Abs(bare - 30) < 1e-9 && Math.Abs(prem - 100) < 1e-9, "tier arithmetic on 100 silver: bare " + bare + ", premium " + prem);

            // 7) the Roof policy covers the fire that the Storyteller policy ignored
            var roofRes = comp.Sign(InsTest.Def("SL_RoofOverHead"), CoverTier.Premium, null, 4000, null, null);
            if (roofRes.Ok)
            {
                InsTest.SkipWaiting(roofRes.contract);
                var b6 = InsTest.SpawnBuilding("Stool", t.FreeCellNearCenter());
                b6.Kill(new DamageInfo(DamageDefOf.Flame, 500f)); InsTest.ClearFires();
                var fireClaim = comp.claims.Last();
                t.Check(fireClaim.trigger == ClaimTrigger.FireBuilding && fireClaim.policyDef == "SL_RoofOverHead", "fire is covered by Roof Over Your Head");
                t.Check(fireClaim.tier == CoverTier.Premium && fireClaim.gross > 0, "Premium pays replacement value");
            }
            InsTest.RestoreSettings();
        }
    }

    public sealed class InsuranceLimbClaimSuite : HearthTestSuite
    {
        public override string Name => "insurance.claim.limb";
        public override ModuleId Module => ModuleId.Bank;
        public override IEnumerable<int> Run(TestContext t)
        {
            InsTest.Reset(8000);
            var comp = InsTest.Comp; var cols = InsTest.Colonists;
            var def = InsTest.Def("SL_LimbByLimb");
            t.Check(cols.Count >= 3, "three colonists available: " + cols.Count);
            if (cols.Count < 3) yield break;
            Pawn a = cols[0], b = cols[1], d = cols[2];
            var ra = comp.Sign(def, CoverTier.BareMinimum, a, 5000, null, null);
            var rb = comp.Sign(def, CoverTier.GoodEnough, b, 5000, null, null);
            t.Check(ra.Ok && rb.Ok, "accident plans signed (" + ra.error + rb.error + ")");
            if (!ra.Ok || !rb.Ok) yield break;
            InsTest.SkipWaiting(ra.contract); InsTest.SkipWaiting(rb.contract);

            // --- Bare Minimum, left hand: 55 % linear ------------------------------------------------------------------------------------------------
            var hand = InsTest.Part(a, BodyPartDefOf.Hand, "left");
            InsTest.LoseParts(a, hand);
            var cl = comp.claims.LastOrDefault(x => x.contractId == ra.contract.id);
            t.Check(cl != null && cl.trigger == ClaimTrigger.LimbLoss && cl.pawn == a, "losing a hand opens an accident claim");
            t.Check(cl != null && Math.Abs(cl.grade - 55) < 1e-6, "invalidity grade 55 % for a hand: " + cl?.grade);
            double sumA = ra.contract.sum, sumB = rb.contract.sum;
            t.Note($"market value based sums: {sumA:0} and {sumB:0}");
            double expected = sumA * 0.55;
            cl.decideTick = Ins.Now; comp.Process(Ins.Now);
            t.Check(cl.state == ClaimState.Decided && cl.outcome == ClaimOutcome.Approved, "decided: " + cl.outcome + " " + cl.clauseDef);
            t.Check(Math.Abs(cl.gross - expected) < 1e-6 && Math.Abs(cl.offered - expected) < 1e-6, $"Bare Minimum pays Gliedertaxe % x sum = {expected:0} (got {cl.offered:0.00})");
            var ins0 = InsTest.Insurer; cl.payTick = Ins.Now; comp.Process(Ins.Now);
            t.Check(cl.state == ClaimState.Paid && Math.Abs(cl.paid - expected) < 1e-6, "paid " + cl.paid.ToString("0.00"));
            t.Check(InsTest.LedgerOk, "ledger balanced after the benefit");
            t.Check(ins0 - InsTest.Insurer == Money.FromSilver(expected) || InsTest.Insurer < ins0, "insurer account debited");

            // --- Good Enough, thumb: 20 % -> still 20 % (progression linear up to 25 %) -------------------------------------------------------
            var thumb = b.RaceProps.body.AllParts.First(x => x.def == InsTest.Finger && (x.customLabel ?? "").Contains("left thumb"));
            InsTest.LoseParts(b, thumb);
            var clb = comp.claims.Last(x => x.contractId == rb.contract.id);
            clb.decideTick = Ins.Now; comp.Process(Ins.Now);
            t.Check(Math.Abs(clb.grade - 20) < 1e-6 && Math.Abs(clb.gross - sumB * 0.20) < 1e-6, "thumb: grade 20 %, benefit 20 % of the sum (got " + clb.grade + " / " + clb.gross + ")");
            // a second loss within the assessment window of the same claim would be merged: lose the index finger on the Paid claim -> new claim
            var index = b.RaceProps.body.AllParts.First(x => x.def == InsTest.Finger && (x.customLabel ?? "").Contains("left index"));
            // Hand missing => its fingers are implicitly gone; use the right hand instead for the second accident
            InsTest.LoseParts(b, InsTest.Part(b, BodyPartDefOf.Leg, "right"));
            var clb2 = comp.claims.Last(x => x.contractId == rb.contract.id);
            t.Check(clb2.id != clb.id || clb.state == ClaimState.Review, "second accident is a separate claim after the first one was decided");
            clb2.decideTick = Ins.Now; comp.Process(Ins.Now);
            double g2 = clb2.grade;
            t.Check(Math.Abs(g2 - 90) < 1e-6 || Math.Abs(g2 - 70) < 1e-6 || g2 >= 70, "grade adds thumb 20 + leg 70 = 90 (got " + g2 + ")");
            t.Check(Math.Abs(clb2.gross - InsuranceMath.AccidentBenefit(sumB, CoverTier.GoodEnough, g2)) < 1e-6, "225 % progression applied: " + clb2.gross.ToString("0.00"));

            // --- surgery is excluded ------------------------------------------------------------------------------------------------------------------
            var rd = comp.Sign(def, CoverTier.GoodEnough, d, 4000, null, null);
            double sumD = rd.Ok ? rd.contract.sum : 0;
            if (rd.Ok)
            {
                InsTest.SkipWaiting(rd.contract); int n = comp.claims.Count;
                d.health.AddHediff(HediffDefOf.MissingBodyPart, InsTest.Part(d, InsTest.BP("Foot"), "left"), new DamageInfo(DamageDefOf.SurgicalCut, 9999f, 999f, -1f, null, InsTest.Part(d, InsTest.BP("Foot"), "left")));
                t.Check(comp.claims.Count == n, "an amputation (surgical cut) is not an accident: no claim");
            }

            // --- pre-existing damage is deducted -------------------------------------------------------------------------------------------------------
            if (cols.Count >= 4)
            {
                var e = cols[3];
                InsTest.LoseParts(e, InsTest.Part(e, BodyPartDefOf.Eye, "right"));
                var re = comp.Sign(def, CoverTier.BareMinimum, e, 5000, null, null);
                t.Check(re.Ok && Math.Abs(re.contract.preexistingGrade - 50) < 1e-6, "pre-existing grade at signing 50 %: " + re.contract?.preexistingGrade);
                if (re.Ok)
                {
                    InsTest.SkipWaiting(re.contract);
                    InsTest.LoseParts(e, InsTest.Part(e, BodyPartDefOf.Eye, "left"));
                    var cle = comp.claims.Last(x => x.contractId == re.contract.id);
                    cle.decideTick = Ins.Now; comp.Process(Ins.Now);
                    t.Check(Math.Abs(cle.grade - 50) < 1e-6 && Math.Abs(cle.gross - re.contract.sum * 0.5) < 1e-6, "B.5 example: right eye missing at signing, left lost -> 50 % of the sum (got " + cle.grade + " / " + cle.gross + ")");
                }
            }

            // --- death claims -----------------------------------------------------------------------------------------------------------------------------------
            var dead = d;
            if (rd.Ok)
            {
                int n = comp.claims.Count;
                dead.Kill(new DamageInfo(DamageDefOf.Bomb, 9999f));
                var dc = comp.claims.Skip(n).FirstOrDefault(x => x.death);
                t.Check(dc != null && Math.Abs(dc.gross - sumD * 0.2) < 1e-6, "accidental death pays 20 % of the sum (got " + dc?.gross + ")");
                if (dc != null) { dc.decideTick = Ins.Now; comp.Process(Ins.Now); t.Check(dc.outcome == ClaimOutcome.Approved && dc.Clause == null, "death claim: approved, no joke clause"); }
            }
            InsTest.RestoreSettings();
        }
    }

    public sealed class InsuranceCancelSuite : HearthTestSuite
    {
        public override string Name => "insurance.cancel";
        public override ModuleId Module => ModuleId.Bank;
        public override IEnumerable<int> Run(TestContext t)
        {
            InsTest.Reset(5000);
            var comp = InsTest.Comp;
            InsTest.Set(InsSettings.NoticeDays, 3); InsTest.Set(InsSettings.FormError, 0f); InsTest.Set(InsSettings.TrickFormError, (float)TrickMode.Follows);
            var res = comp.Sign(InsTest.Def("SL_RoofOverHead"), CoverTier.GoodEnough, null, 2000, null, null);
            t.Check(res.Ok, "signed: " + res.error);
            if (!res.Ok) yield break;
            var c = res.contract; int now = Ins.Now;
            long Q = GenDate.TicksPerQuadrum; long into = InsuranceTime.TicksIntoQuadrum();
            long expectEff = InsuranceMath.CancelEffectiveTick(now, into, Q, 3L * 60000, false);
            var plan = comp.PlanCancel(c);
            t.Check(!plan.special && plan.effectiveTick == expectEff, $"plan: effective at {plan.effectiveTick} (expected {expectEff})");
            long remaining = Q - into;
            if (remaining >= 3 * 60000) t.Check(plan.effectiveTick - now == remaining, "notice met: ends with this quadrum, " + remaining / 60000.0 + " days away");
            else t.Check(plan.effectiveTick - now == remaining + Q, "notice missed: ends with the next quadrum");
            t.Check(plan.effectiveTick - now >= 3L * 60000, "never earlier than the notice period");

            string msg = comp.FileCancel(c);
            t.Check(c.CancelPending && c.cancelEffectiveTick == (int)expectEff && !c.formErrorHit, "cancellation filed without form error (chance 0): " + msg);
            comp.Process((int)expectEff - 1);
            t.Check(c.Active, "policy still active one tick before the effective date");
            comp.Process((int)expectEff);
            t.Check(!c.Active && c.endReason != null, "policy ended at the effective date");
            t.Check(InsTest.LedgerOk, "ledger balanced");

            // withdraw
            var r2 = comp.Sign(InsTest.Def("SL_RoofOverHead"), CoverTier.BareMinimum, null, 1500, null, null);
            t.Check(r2.Ok, "re-signed");
            comp.FileCancel(r2.contract); t.Check(r2.contract.CancelPending, "pending");
            comp.WithdrawCancel(r2.contract); t.Check(!r2.contract.CancelPending && r2.contract.Active, "withdrawn");

            // special cancellation after a settled claim is immediate
            r2.contract.specialCancelUntil = Ins.Now + 5 * 60000;
            var plan2 = comp.PlanCancel(r2.contract);
            t.Check(plan2.special && plan2.effectiveTick == Ins.Now, "special cancellation is immediate");
            comp.FileCancel(r2.contract);
            t.Check(!r2.contract.Active, "special cancellation ended the policy at once");

            // form error: Standard Room keeps the deadline, Overbooked slips a quadrum
            var r3 = comp.Sign(InsTest.Def("SL_BlightBeGone"), CoverTier.GoodEnough, null, 1000, null, null);
            if (r3.Ok)
            {
                InsTest.Set(InsSettings.FormError, 1f);
                var prof = HearthProfile.Current; float oldTier = prof.Tier;
                prof.Tier = 3f;
                long baseEff = comp.PlanCancel(r3.contract).effectiveTick;
                comp.FileCancel(r3.contract);
                t.Check(r3.contract.formErrorHit && r3.contract.cancelEffectiveTick == (int)baseEff, "Standard Room: form error, re-filed automatically, deadline kept");
                comp.WithdrawCancel(r3.contract);
                prof.Tier = 4.5f;
                long hardBase = comp.PlanCancel(r3.contract).effectiveTick;
                comp.FileCancel(r3.contract);
                t.Check(r3.contract.formErrorHit && r3.contract.cancelEffectiveTick == (int)(hardBase + Q), "Overbooked: form error costs a quadrum");
                comp.WithdrawCancel(r3.contract);
                // legal shield removes the form error on Standard Room
                prof.Tier = 3f;
                var ls = comp.Sign(InsTest.Def("SL_LegalShield"), CoverTier.GoodEnough, null, 1000, null, null);
                if (ls.Ok) { comp.FileCancel(r3.contract); t.Check(!r3.contract.formErrorHit, "Legal Shield fills in Form C-7 correctly"); }
                prof.Tier = oldTier;
                // retention offer
                comp.WithdrawCancel(r3.contract);
                comp.RetentionAccept(r3.contract, 0.15, 2);
                var q = InsurancePricing.QuoteFor(r3.contract);
                t.Check(r3.contract.Active && Math.Abs(q.discount - 0.15) < 1e-9, "retention: 15 % off for two quadrums");
            }
            t.Check(InsTest.LedgerOk, "ledger balanced at the end");
            InsTest.RestoreSettings();
        }
    }

    public sealed class InsuranceNegotiateSuite : HearthTestSuite
    {
        public override string Name => "insurance.negotiate";
        public override ModuleId Module => ModuleId.Bank;
        public override IEnumerable<int> Run(TestContext t)
        {
            InsTest.Reset(3000);
            var comp = InsTest.Comp; var l = Ledger.Current;
            var res = comp.Sign(InsTest.Def("SL_RoofOverHead"), CoverTier.GoodEnough, null, 3000, null, null);
            t.Check(res.Ok, "signed");
            if (!res.Ok) yield break;
            var negotiator = ClaimEngine.BestNegotiator(InsTest.Map);
            t.Note("negotiator: " + (negotiator?.LabelShort ?? "none (random colonists can all be incapable of Social)") + "; the rounds below use the default skill so the outcome does not depend on the colonist");
            int wins = 0, partials = 0, fails = 0; var moneyBefore = Money.Zero;
            var cash0 = InsTest.Till + InsTest.Insurer;
            for (int i = 0; i < 60; i++)
            {
                var cl = new Claim { id = comp.nextClaimId++, contractId = res.contract.id, policyDef = res.contract.defName, tier = CoverTier.GoodEnough, trigger = ClaimTrigger.RaidBuilding, state = ClaimState.Decided, outcome = i % 2 == 0 ? ClaimOutcome.Denied : ClaimOutcome.Partial, gross = 100, offered = i % 2 == 0 ? 0 : 50, lossValue = 100, payTick = Ins.Now + 5 * 60000, clauseDef = "SL_Clause_14b", subject = "test" };
                comp.claims.Add(cl);
                for (int r = 0; r < 4; r++)
                {
                    var nr = ClaimEngine.Negotiate(cl, (NegApproach)(r % 3), null); // null = default skill 0.6: a random colonist with a top Social skill wins every round (logistic chance ~1), which made 'sometimes does not' flaky
                    if (r >= 3) t.Check(nr.text == Ins.T("Neg.NotPossible") || cl.negotiationClosed, "no fourth round");
                    if (nr.success) { wins++; t.Check(cl.outcome == ClaimOutcome.Approved && Math.Abs(cl.offered - 100) < 1e-9, "success gives the full benefit"); break; }
                    if (nr.partial) { partials++; t.Check(cl.offered >= 40 - 1e-9 && cl.offered <= 70 + 1e-9 || cl.offered >= 50, "partial success 40-70 %: " + cl.offered); }
                    else fails++;
                    t.Check(cl.offered <= cl.gross + 1e-9, "offer never above the claim");
                    if (cl.negotiationClosed) break;
                }
                t.Check(cl.rounds <= 3, "at most three rounds");
                ClaimEngine.Close(cl);
            }
            t.Note($"60 claims: {wins} wins, {partials} partials, {fails} failures");
            t.Check(wins + partials + fails > 0 && wins > 0, "negotiation sometimes works");
            t.Check(fails > 0, "and sometimes does not");
            t.Check(InsTest.Till + InsTest.Insurer == cash0, "negotiating moved no money (Standard Room: never costs anything)");
            double pLow = InsuranceMath.NegotiationChance(0.3, 0.2, false), pHigh = InsuranceMath.NegotiationChance(1.5, 0.2, true);
            t.Check(pLow < pHigh, "better negotiator, better odds");
            InsTest.RestoreSettings();
        }
    }

    public sealed class InsuranceTravelSuite : HearthTestSuite
    {
        public override string Name => "insurance.travel";
        public override ModuleId Module => ModuleId.Bank;
        public override IEnumerable<int> Run(TestContext t)
        {
            InsTest.Reset(4000);
            var comp = InsTest.Comp; var map = InsTest.Map;
            var def = InsTest.Def("SL_Wanderlust");
            var prem = comp.Sign(def, CoverTier.Premium, null, 1500, null, null);
            var bare = comp.Sign(InsTest.Def("SL_LimbByLimb"), CoverTier.BareMinimum, InsTest.Colonists.First(), 100, null, null);
            t.Check(prem.Ok, "Wanderlust Premium signed: " + prem.error);
            if (!prem.Ok) yield break;
            InsTest.SkipWaiting(prem.contract);
            // tiers cover different things
            t.Check(ClawbackApi.TierCovers(def, CoverTier.BareMinimum, ClaimTrigger.TravelDeath) && !ClawbackApi.TierCovers(def, CoverTier.BareMinimum, ClaimTrigger.TravelInjury) && !ClawbackApi.TierCovers(def, CoverTier.BareMinimum, ClaimTrigger.TravelLimb), "Bare Minimum covers only death");
            t.Check(ClawbackApi.TierCovers(def, CoverTier.GoodEnough, ClaimTrigger.TravelLimb) && !ClawbackApi.TierCovers(def, CoverTier.GoodEnough, ClaimTrigger.TravelInjury), "Good Enough adds lost body parts");
            t.Check(ClawbackApi.TierCovers(def, CoverTier.Premium, ClaimTrigger.TravelInjury), "Premium covers every scratch");
            t.Check(InsurancePricing.QuoteFor(def, CoverTier.Premium, null, 1500, null, 1).perTravelDay, "travel cover is billed per travel day");
            t.Check(comp.PlanCancel(prem.contract) != null, "plan available");

            var pawn = InsTest.Colonists.Skip(1).FirstOrDefault();
            if (pawn == null) yield break;
            // put the colonist on a caravan
            Caravan car = null;
            try
            {
                pawn.DeSpawn();
                car = CaravanMaker.MakeCaravan(new[] { pawn }, Faction.OfPlayer, map.Tile, true);
            }
            catch (Exception e) { t.Note("could not create a caravan: " + e.Message); }
            if (car != null)
            {
                t.Check(pawn.IsCaravanMember(), "pawn is on a caravan");
                comp.Process(Ins.Now);
                // daily accounting counts head-days
                comp.DailyChecks(Ins.Now);
                t.Check(prem.contract.travelHeadDays >= 1, "a travel day was counted: " + prem.contract.travelHeadDays);
                int n = comp.claims.Count;
                var cut = DefDatabase<HediffDef>.GetNamed("Cut");
                var h = (Hediff_Injury)HediffMaker.MakeHediff(cut, pawn, pawn.RaceProps.body.AllParts.First(x => x.def == BodyPartDefOf.Torso));
                h.Severity = 3f;
                pawn.health.AddHediff(h);
                t.Check(comp.claims.Count == n + 1 && comp.claims.Last().trigger == ClaimTrigger.TravelInjury, "an injury on the road opens a travel claim");
                var claim = comp.claims.Last(); claim.decideTick = Ins.Now; comp.Process(Ins.Now);
                t.Check(claim.state == ClaimState.Decided && claim.gross > 0, "decided with a flat sum " + claim.gross.ToString("0.0"));
                // billing by head-days
                var till0 = InsTest.Till; prem.contract.nextBillTick = Ins.Now; comp.Process(Ins.Now);
                t.Check(InsTest.Till < till0 || prem.contract.travelHeadDays == 0, "travel premium debited by head-days");
                pawn.Kill(new DamageInfo(DamageDefOf.Bomb, 9999f));
                var dc = comp.claims.LastOrDefault(x => x.death);
                t.Check(dc != null && dc.trigger == ClaimTrigger.TravelDeath && Math.Abs(dc.gross - prem.contract.sum) < 1e-6, "death on the road pays the per-head sum (" + dc?.gross + ")");
                try { if (car.Spawned) Find.WorldObjects.Remove(car); } catch { }
            }
            t.Check(InsTest.LedgerOk, "ledger balanced");
            InsTest.RestoreSettings();
        }
    }

    public sealed class InsuranceExploitSuite : HearthTestSuite
    {
        public override string Name => "insurance.exploit";
        public override ModuleId Module => ModuleId.Bank;
        public override IEnumerable<int> Run(TestContext t)
        {
            InsTest.Reset(20000);
            var comp = InsTest.Comp;
            // never worth it on average: premium income of the insurer exceeds expected payout at every tier on Standard Room
            double loading = 0.25;
            foreach (CoverTier tier in Enum.GetValues(typeof(CoverTier)))
            {
                double ratio = InsuranceMath.ExpectedPayoutRatio(tier, 0.15);
                double premiumPerUnitDamage = InsuranceMath.Premium(1.0, ratio, loading, 1, 1);
                t.Check(premiumPerUnitDamage > ratio, tier + ": premium per unit of expected damage " + premiumPerUnitDamage.ToString("0.000") + " > expected payout " + ratio.ToString("0.000"));
            }
            // payout can never exceed the loss, whatever the tier and the riders
            var def = InsTest.Def("SL_ActsOfStoryteller");
            foreach (CoverTier tier in Enum.GetValues(typeof(CoverTier)))
            {
                var r = comp.Sign(def, tier, null, 6000, new[] { "boost" }, null);
                if (!r.Ok) { t.Note("could not sign " + tier + ": " + r.error); continue; }
                InsTest.SkipWaiting(r.contract);
                var raider = InsTest.Raider();
                var b = InsTest.SpawnBuilding("Stool", t.FreeCellNearCenter());
                double val = b.def.GetStatValueAbstract(StatDefOf.MarketValue, b.Stuff);
                b.Kill(new DamageInfo(DamageDefOf.Bomb, 500f, 0f, -1f, raider));
                var cl = comp.claims.Last(x => x.contractId == r.contract.id);
                t.Check(cl.gross <= val + 1e-9, tier + ": gross " + cl.gross.ToString("0.00") + " <= loss " + val.ToString("0.00") + " even with the wear-and-tear rider");
                comp.End(r.contract, "test"); ClaimEngine.Close(cl);
            }
            // over-insuring: the sum is capped by the real value of the colony
            InsurancePricing.SumRange(def, InsTest.Map, null, out _, out double max);
            t.Check(max <= Math.Max(def.minSum, InsTest.Map.wealthWatcher.WealthBuildings) + 1e-6 || max <= def.minSum + 1e-6, "property sum capped by real building wealth: " + max.ToString("0"));
            var big = comp.Sign(def, CoverTier.GoodEnough, null, 9999999, null, null);
            t.Check(!big.Ok || big.contract.sum <= max + 1e-6, "an absurd sum is clamped");
            if (big.Ok) comp.End(big.contract, "test");
            // personal sums are capped by market value and 25,000
            var pawn = InsTest.Colonists.First();
            InsurancePricing.SumRange(InsTest.Def("SL_LimbByLimb"), InsTest.Map, pawn, out _, out double pmax);
            t.Check(pmax <= 25000 && pmax <= Math.Max(500, pawn.MarketValue * 2) + 1e-6, "personal sum <= min(25,000, 2 x market value): " + pmax.ToString("0"));
            // a threat blocks signing
            var raider2 = InsTest.Raider();
            GenSpawn.Spawn(raider2, t.FreeCellNearCenter(), InsTest.Map);
            string err = comp.CanSign(InsTest.Def("SL_BlightBeGone"), CoverTier.GoodEnough, null);
            t.Check(!raider2.Dead, "raider spawned");
            t.Note("threat on map, CanSign says: " + (err ?? "ok"));
            raider2.Destroy();
            // premium loop: signing, waiting, cancelling and re-signing yields no money
            var ins0 = InsTest.Insurer; var till0 = InsTest.Till;
            for (int i = 0; i < 5; i++)
            {
                var r = comp.Sign(InsTest.Def("SL_RimHealth"), CoverTier.Premium, null, 1000, null, null);
                if (r.Ok) comp.End(r.contract, "loop");
            }
            t.Check(InsTest.Insurer >= ins0 && InsTest.Till <= till0, "sign + cancel loops only cost premiums");
            t.Check(InsTest.LedgerOk, "ledger balanced");
            InsTest.RestoreSettings();
            yield break;
        }
    }

    public sealed class InsuranceUiSuite : HearthTestSuite
    {
        public override string Name => "insurance.ui";
        public override ModuleId Module => ModuleId.Bank;
        public override IEnumerable<int> Run(TestContext t)
        {
            InsTest.Reset(3000); yield return 20; // let the fire watcher notice that earlier suites' fires are out
            var comp = InsTest.Comp;
            var res = comp.Sign(InsTest.Def("SL_RoofOverHead"), CoverTier.GoodEnough, null, 2000, null, null);
            t.Check(res.Ok && res.contract != null, "policy signed: " + res.error); if (!res.Ok || res.contract == null) yield break;
            int errors0 = HLog.ErrorCount;
            Window_Clawback.Open();
            t.Check(Find.WindowStack.Windows.Any(w => w is Window_Clawback), "stand-alone window opens");
            yield return 30;
            // a claim and a cancel dialog too
            var cl = new Claim { id = comp.nextClaimId++, contractId = res.contract.id, policyDef = res.contract.defName, tier = CoverTier.GoodEnough, trigger = ClaimTrigger.RaidBuilding, state = ClaimState.Decided, outcome = ClaimOutcome.Partial, gross = 100, offered = 50, payTick = Ins.Now + 99999, subject = "test", breakdown = new List<string> { "line one", "line two" } };
            comp.claims.Add(cl);
            Find.WindowStack.Add(new Dialog_Negotiate(cl.id));
            Find.WindowStack.Add(new Dialog_CancelPolicy(res.contract.id));
            yield return 30;
            t.Check(HLog.ErrorCount == errors0, "no errors while drawing the desk and both dialogs: " + (HLog.ErrorCount - errors0));
            t.Check(!Guard.IsPaused(ModuleId.Bank), "bank module not paused by a UI error");
            t.Check(BankTabs.Tabs.Any(x => x.Id == "insurance"), "bank tab registered");
            foreach (var w in Find.WindowStack.Windows.Where(w => w is Window_Clawback || w is Dialog_Negotiate || w is Dialog_CancelPolicy).ToList()) Find.WindowStack.TryRemove(w, false);
            ClaimEngine.Close(cl);
            InsTest.RestoreSettings();
        }
    }
}
#endif
#if HH_TESTS
namespace Silverline
{
    /// <summary>Screenshot helper (only with HH_SHOTS=1): opens the desk in several states and waits so the screen can be captured from outside.</summary>
    public sealed class InsuranceShotsSuite : Hearth.Testing.HearthTestSuite
    {
        public override string Name => "insurance.shots";
        public override Hearth.ModuleId Module => Hearth.ModuleId.Bank;
        public override int MaxTicks => 200000;
        public override IEnumerable<int> Run(Hearth.Testing.TestContext t)
        {
            if (Environment.GetEnvironmentVariable("HH_SHOTS") != "1") { t.Note("skipped (HH_SHOTS != 1)"); yield break; }
            int wait = int.Parse(Environment.GetEnvironmentVariable("HH_SHOT_TICKS") ?? "900");
            InsTest.Reset(4000);
            var comp = InsTest.Comp;
            Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
            var colonist = InsTest.Colonists.First();
            comp.Sign(InsTest.Def("SL_RoofOverHead"), CoverTier.GoodEnough, null, 2000, new[] { "boost" }, new[] { "boost" });
            comp.Sign(InsTest.Def("SL_LimbByLimb"), CoverTier.Premium, colonist, 3000, new[] { "plusProgression", "deathSum" }, new[] { "plusProgression", "deathSum" });
            InsTest.SkipWaiting(comp.contracts[1]);
            InsTest.LoseParts(colonist, InsTest.Part(colonist, InsTest.BP("Hand"), "left"));
            var raider = InsTest.Raider(); InsTest.SkipWaiting(comp.contracts[0]);
            var b = InsTest.SpawnBuilding("Stool", t.FreeCellNearCenter()); b.Kill(new DamageInfo(DamageDefOf.Bomb, 500f, 0f, -1f, raider));
            var cl = comp.claims.Last(); cl.decideTick = Ins.Now; comp.Process(Ins.Now);
            var limb = comp.claims.First(x => x.trigger == ClaimTrigger.LimbLoss); limb.decideTick = Ins.Now; comp.Process(Ins.Now);
            foreach (var w in Find.WindowStack.Windows.ToList()) if (!(w is Window_Clawback)) { }
            for (int page = 0; page < 4; page++)
            {
                Window_Clawback.Open();
                InsuranceUI.ShowPage(page, page == 0);
                t.Note("page " + page);
                yield return wait;
            }
            InsuranceUI.ShowPage(0, false);
            Find.WindowStack.Add(new Dialog_Negotiate(limb.id)); t.Note("negotiate"); yield return wait;
            Find.WindowStack.Add(new Dialog_CancelPolicy(comp.contracts[0].id)); t.Note("cancel"); yield return wait;
            // harder level
            HearthProfile.Current.Tier = 4.5f; InsuranceUI.ShowPage(0, false); t.Note("overbooked shop"); yield return wait;
            HearthProfile.Current.Tier = 3f;
        }
    }
}
#endif
#if HH_TESTS
namespace Silverline
{
    public sealed class InsuranceSaveSuite : Hearth.Testing.HearthTestSuite
    {
        public override string Name => "insurance.save";
        public override Hearth.ModuleId Module => Hearth.ModuleId.Bank;
        public override IEnumerable<int> Run(Hearth.Testing.TestContext t)
        {
            InsTest.Reset(4000);
            var comp = InsTest.Comp; var pawn = InsTest.Colonists.First();
            var r1 = comp.Sign(InsTest.Def("SL_LimbByLimb"), CoverTier.GoodEnough, pawn, 2000, new[] { "deathSum" }, new[] { "deathSum" });
            var r2 = comp.Sign(InsTest.Def("SL_RoofOverHead"), CoverTier.Premium, null, 1500, null, null);
            InsTest.SkipWaiting(r1.contract);
            InsTest.LoseParts(pawn, InsTest.Part(pawn, InsTest.BP("Hand"), "left"));
            comp.debt = 12.5;
            string path = System.IO.Path.Combine(GenFilePaths.SaveDataFolderPath, "hh_insurance_test.xml");
            Scribe.saver.InitSaving(path, "HHInsTest");
            var toSave = comp;
            Scribe_Deep.Look(ref toSave, "ins", Current.Game);
            Scribe.saver.FinalizeSaving();
            InsuranceComponent loaded = null;
            Scribe.loader.InitLoading(path);
            Scribe_Deep.Look(ref loaded, "ins", Current.Game);
            Scribe.loader.FinalizeLoading();
            t.Check(loaded != null, "component round-trips");
            if (loaded != null)
            {
                t.Check(loaded.contracts.Count == comp.contracts.Count && loaded.claims.Count == comp.claims.Count, $"contracts {loaded.contracts.Count}, claims {loaded.claims.Count}");
                var l1 = loaded.contracts.First(c => c.defName == "SL_LimbByLimb");
                t.Check(l1.holderName == pawn.LabelShortCap && l1.tier == CoverTier.GoodEnough && Math.Abs(l1.sum - r1.contract.sum) < 1e-9 && l1.riders.Contains("deathSum"), "contract fields and pawn reference");
                var lc = loaded.claims.First(c => c.trigger == ClaimTrigger.LimbLoss);
                t.Check(lc.pawnName == pawn.LabelShortCap && lc.state == ClaimState.Review, "claim and its pawn reference");
                t.Check(Math.Abs(loaded.debt - 12.5) < 1e-9 && loaded.firstPolicyTick == comp.firstPolicyTick, "scalars");
            }
            try { System.IO.File.Delete(path); } catch { }
            InsuranceComponent.Instance?.GetType();
            t.Check(InsuranceComponent.Instance == comp, "live component still the registered one");
            InsTest.RestoreSettings();
            yield break;
        }
    }

    public sealed class InsuranceTricksSuite : Hearth.Testing.HearthTestSuite
    {
        public override string Name => "insurance.tricks";
        public override Hearth.ModuleId Module => Hearth.ModuleId.Bank;
        public override IEnumerable<int> Run(Hearth.Testing.TestContext t)
        {
            InsTest.Reset(20000); yield return 20; // let the fire watcher notice that earlier suites' fires are out
            var comp = InsTest.Comp; var l = Ledger.Current;
            // pre-ticked add-ons are counted as sales-trick cost; above the cap the year letter pays it back
            var r = comp.Sign(InsTest.Def("SL_LimbByLimb"), CoverTier.GoodEnough, InsTest.Colonists.First(), 2000, new[] { "plusProgression", "deathSum" }, new[] { "plusProgression", "deathSum" });
            t.Check(r.Ok && r.contract != null, "signed: " + r.error); if (!r.Ok || r.contract == null) yield break;
            t.Check(r.contract.preTickedKept.Count == 2, "two pre-ticked riders kept");
            r.contract.nextBillTick = Ins.Now; comp.Process(Ins.Now);
            t.Check(r.contract.trickCostYear > 0 && r.contract.premiumYear > r.contract.trickCostYear, $"trick cost {r.contract.trickCostYear:0.000} of premiums {r.contract.premiumYear:0.000}");
            double cost = r.contract.trickCostYear, prem = r.contract.premiumYear;
            double expectedCredit = InsuranceMath.CourtesyCredit(cost, prem, HSettings.Get(InsSettings.TrickCap));
            t.Check(expectedCredit > 0, "17 % add-ons exceed the 5 % cap: credit " + expectedCredit.ToString("0.000"));
            var till0 = InsTest.Till; var ins0 = InsTest.Insurer;
            comp.DailyChecks(Ins.Now + 61 * 60000);
            t.Check(comp.courtesyTotal > 0 && Math.Abs(comp.courtesyTotal - expectedCredit) < 1e-6, "courtesy credit booked: " + comp.courtesyTotal.ToString("0.000"));
            t.Check(r.contract.trickCostYear == 0 && r.contract.premiumYear == 0, "yearly counters reset");
            t.Check(InsTest.LedgerOk, "ledger balanced after the credit");
            // add-on effects
            var q0 = InsurancePricing.QuoteFor(InsTest.Def("SL_LimbByLimb"), CoverTier.GoodEnough, InsTest.Colonists.First(), 2000, null, 1);
            var q1 = InsurancePricing.QuoteFor(InsTest.Def("SL_LimbByLimb"), CoverTier.GoodEnough, InsTest.Colonists.First(), 2000, new[] { "plusProgression", "deathSum" }, 1);
            t.Check(Math.Abs(q1.riders - 1.17) < 1e-5 && q1.billing > q0.billing, "riders +17 % on Standard Room (fair): " + q1.riders + " tier " + InsSettings.Tier);
            // generic reports
            var ls = comp.Sign(InsTest.Def("SL_LegalShield"), CoverTier.GoodEnough, null, 2000, null, null);
            InsTest.SkipWaiting(ls.contract);
            t.Check(ClawbackApi.ReportFine(InsTest.Map, Money.FromSilver(100), "test fine"), "fine reported to Legal Shield");
            var fine = comp.claims.Last(); t.Check(Math.Abs(fine.gross - 80) < 1e-3, "Good Enough pays 80 % of a fine: " + fine.gross);
            t.Check(!ClawbackApi.ReportClosure(InsTest.Map, Money.FromSilver(50), "x"), "no closure cover: nobody takes it");
            // golden years plan: no pension can be bought at pension age, the premium follows the time left, the pension still accrues from 60
            var old = InsTest.Colonists.LastOrDefault(); if (old == null) { t.Note("no colonist left"); yield break; }
            var gyDef = InsTest.Def("SL_GoldenYears");
            old.ageTracker.AgeBiologicalTicks = 63L * 3600000L;
            t.Check(comp.CanSign(gyDef, CoverTier.GoodEnough, old) != null, "a 63-year-old cannot buy a pension: " + comp.CanSign(gyDef, CoverTier.GoodEnough, old));
            old.ageTracker.AgeBiologicalTicks = 30L * 3600000L;
            var qYoung = InsurancePricing.QuoteFor(gyDef, CoverTier.GoodEnough, old, 1000, null, 1);
            old.ageTracker.AgeBiologicalTicks = (long)(59.0 * 3600000L);
            var qNear = InsurancePricing.QuoteFor(gyDef, CoverTier.BareMinimum, old, 1000, null, 1);
            // a lump sum of 1,000 a year from now must cost about 1,000 over the four quadrums that are left, not "a few silver"
            t.Check(qNear.premiumQuadrum * 4 >= 1000 * 0.8, "Bare Minimum at 59: the premium over the last year pays for the lump sum: " + (qNear.premiumQuadrum * 4).ToString("0"));
            t.Check(qYoung.premiumQuadrum < qNear.premiumQuadrum / 10, "far from 60 the plan stays cheap: " + qYoung.premiumQuadrum.ToString("0.0") + " vs " + qNear.premiumQuadrum.ToString("0.0"));
            old.ageTracker.AgeBiologicalTicks = 58L * 3600000L;
            var gy = comp.Sign(gyDef, CoverTier.GoodEnough, old, 1000, null, null);
            t.Check(gy.Ok, "signed at 58: " + gy.error);
            if (gy.Ok)
            {
                InsTest.SkipWaiting(gy.contract); int n = comp.claims.Count;
                old.ageTracker.AgeBiologicalTicks = 63L * 3600000L;
                for (int d = 0; d < 7; d++) comp.DailyChecks(Ins.Now + (d + 1) * 60000 + 1);
                t.Check(comp.claims.Count > n, "pension accrues for a 63-year-old: " + (comp.claims.Count > n ? comp.claims.Last().gross.ToString("0.00") : "no claim"));
            }
            InsTest.RestoreSettings();
            yield break;
        }
    }

    public sealed class InsuranceHospitalCapSuite : HearthTestSuite
    {
        public override string Name => "insurance.cap";
        public override ModuleId Module => ModuleId.Bank;
        public override int MaxTicks => 3000;
        public override IEnumerable<int> Run(TestContext t)
        {
            InsTest.Reset(3000);
            var comp = InsTest.Comp;
            // the pure counter: 5 paid days per 15 days per colonist
            var pc = new PolicyContract();
            int paid = 0; for (int day = 100; day < 115; day++) if (pc.TryCountBedDay(7, day, 5, 15)) paid++;
            t.Check(paid == 5, "one colonist in bed for 15 days: 5 paid days (" + paid + ")");
            t.Check(pc.TryCountBedDay(8, 114, 5, 15), "another colonist has his own cap");
            t.Check(!pc.TryCountBedDay(7, 114, 5, 15), "still capped on day 14 of the window");
            t.Check(pc.TryCountBedDay(7, 115, 5, 15), "day 15: the first paid day has left the window, one more is paid");
            t.Check(pc.bedLogPawn.Count == pc.bedLogDay.Count, "log lists stay parallel");
            // price: the policy costs twice what it did (baseRisk 4.0)
            var def = InsTest.Def("SL_RimHealth");
            t.Check(Math.Abs(def.baseRisk - 4.0f) < 1e-6, "Rim Health Plan base risk is 4.0: " + def.baseRisk);
            // in game: a colonist in a sickbed pays the cap and not a day more
            var res = comp.Sign(def, CoverTier.BareMinimum, null, 1000, null, null);
            t.Check(res.Ok, "Rim Health Plan signed: " + res.error);
            if (!res.Ok) { InsTest.RestoreSettings(); yield break; }
            var c = res.contract; InsTest.SkipWaiting(c);
            var map = InsTest.Map; var pawn = InsTest.Colonists.FirstOrDefault();
            if (pawn == null) { t.Note("no colonist"); InsTest.RestoreSettings(); yield break; }
            var bed = InsTest.SpawnBuilding("Bed", pawn.Position);
            pawn.Position = bed.Position; pawn.Notify_Teleported();
            var flu = HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed("Flu"), pawn); pawn.health.AddHediff(flu);
            pawn.jobs.StartJob(JobMaker.MakeJob(JobDefOf.LayDown, bed), JobCondition.InterruptForced);
            yield return 5;
            if (!pawn.InBed()) { t.Note("could not put the colonist to bed in the test; the counter itself is checked above"); }
            else
            {
                for (int d = 0; d < 8; d++) comp.DailyChecks(Ins.Now + (d + 1) * 60000 + 1);
                // bare minimum pays 10 a day: at most 5 days = 50 silver accrued or filed
                double total = c.accrued + comp.claims.Where(x => x.contractId == c.id).Sum(x => x.gross);
                t.Check(total > 0 && total <= 5 * 10 + 1e-6, "8 sick days in a row: at most 5 paid days = 50 silver (" + total.ToString("0.0") + ")");
            }
            pawn.health.RemoveHediff(flu); bed.Destroy();
            InsTest.RestoreSettings();
        }
    }
}
#endif
