// SPDX-License-Identifier: MIT
// In-game suites that check the DLC traits, genes, xenotype preferences and luck really act in the game (not only as stats).
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using Hearth.Casino;
using Hearth.Guests;
using Hearth.ShopsFood;
using Hearth.Testing;
using RimWorld;
using UnityEngine;
using Verse;

namespace Hearth.Dlc.Tests
{
    // ===================================================================================================================
    /// <summary>Luck (11.1): the payout plan shift in live rounds, streak thoughts at session end, gambling-averse pawns never play,
    /// the corrected trait/gene lookups of the addiction chance and bankroll, and the free tenth lottery ticket.</summary>
    public sealed class DlcLuckSuite : HearthTestSuite
    {
        public override string Name => "dlc.luck";
        public override ModuleId Module => ModuleId.Casino;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map;
            var lucky = Kit.Colonist(map); var unlucky = Kit.Colonist(map); var plain = Kit.Colonist(map); var av = Kit.Colonist(map); var gb = Kit.Colonist(map);
            Kit.Trait(lucky, DlcDefs.Lucky); Kit.Trait(unlucky, DlcDefs.Unlucky); Kit.Trait(av, DlcDefs.GamblingAverse);
            var slot = Hearth.Casino.Tests.Kit.Spawn(t, CasinoDefOf.HH_SlotMachine, Rot4.South, map.Center); Hearth.Casino.Tests.Kit.Power(slot);
            var comp = slot.TryGetComp<CompCasinoGame>();
            t.Check(comp != null && comp.Usable(false, out _), "slot machine usable for colonists");
            double baseRtp = 1 - comp.ExpectedEdge, shift = HSettings.Get("dlc.luckShift");
            t.Check(Math.Abs(CasinoEngine.PlayerRtp(comp, lucky) - DlcMath.LuckRtp(baseRtp, shift)) < 1e-9 && Math.Abs(CasinoEngine.PlayerRtp(comp, plain) - baseRtp) < 1e-9,
                $"payout plan shown per player: {CasinoEngine.PlayerRtp(comp, lucky):P2} lucky vs {baseRtp:P2}");

            // live rounds: the luck factor is applied to the payout, the draw is untouched
            foreach (var (p, expect, what) in new[] { (lucky, DlcMath.LuckRtp(baseRtp, shift) / baseRtp, "lucky"), (unlucky, DlcMath.LuckRtp(baseRtp, -shift) / baseRtp, "unlucky"), (plain, 1.0, "plain") })
            {
                var s = CasinoEngine.Begin(p, comp, SessionKind.Colonist, false, out string fail);
                t.Check(s != null, what + ": session starts (" + fail + ")");
                if (s == null) continue;
                CasinoEngine.LastLuckFactor = -1;
                CasinoEngine.RunRound(comp, new List<GamblerSession> { s });
                t.Check(Math.Abs(CasinoEngine.LastLuckFactor - expect) < 1e-9, $"{what}: payout factor {CasinoEngine.LastLuckFactor:0.00000} (expected {expect:0.00000})");
                CasinoEngine.EndSession(s, "test");
            }
            t.Check(DlcMath.LuckRtp(baseRtp, shift) / baseRtp > 1 && DlcMath.LuckRtp(baseRtp, -shift) / baseRtp < 1, "lucky wins pay a little more, unlucky ones a little less");

            // streak thoughts come from finished sessions (won = ended above the buy-in)
            var streakDef = DlcDefs.Thought("HH_LuckyStreak"); var lostDef = DlcDefs.Thought("HH_OfCourseILost");
            lucky.needs.mood.thoughts.memories.RemoveMemoriesOfDef(streakDef);
            for (int i = 0; i < 2; i++)
            {
                var s = CasinoEngine.Begin(lucky, comp, SessionKind.Colonist, false, out _); if (s == null) continue;
                s.Rounds = 1; s.Purse = s.BuyIn + Money.FromSilver(5); CasinoEngine.EndSession(s, "test win");
            }
            t.Check(lucky.needs.mood.thoughts.memories.NumMemoriesOfDef(streakDef) == 1, "lucky: two winning sessions give a lucky streak");
            var su = CasinoEngine.Begin(unlucky, comp, SessionKind.Colonist, false, out _);
            if (su != null) { su.Rounds = 1; su.Purse = su.BuyIn - Money.FromSilver(5); CasinoEngine.EndSession(su, "test loss"); }
            t.Check(unlucky.needs.mood.thoughts.memories.NumMemoriesOfDef(lostDef) == 1, "unlucky: a lost session gives 'of course I lost'");

            // gambling-averse: never plays, never gets hooked
            var sa = CasinoEngine.Begin(av, comp, SessionKind.Colonist, false, out string afail);
            t.Check(sa == null && afail == "gambling-averse", "averse colonist cannot start a session (" + afail + ")");
            t.Check(!CasinoDefOf.HH_PlayCasinoJoy.Worker.CanBeGivenTo(av), "averse colonist never gets the gambling joy job");
            t.Check(Gambling.Factor(av) == 0 && Gambling.ChanceFor(av, 20) == 0, "averse: addiction chance 0");
            var guest = Kit.Visitor(map, map.Center);
            if (guest != null)
            {
                Kit.Trait(guest, DlcDefs.GamblingAverse); Kit.GiveSilver(guest, 300);
                var act = GuestActivities.All.FirstOrDefault(a => a.Id == "casino.play");
                t.Check(act != null && act.Desire(guest, GuestApi.Info(guest)) == 0f, "averse guest has no wish to gamble");
                t.Check(CasinoEngine.Begin(guest, comp, SessionKind.Guest, false, out _) == null, "averse guest cannot start a session");
            }
            else t.Note("no neutral faction: averse guest not exercised");

            // gambler trait and high roller gene: the corrected lookups (formerly the non-existent "Gambler"/"AddictionImmune" names)
            double f0 = Gambling.Factor(gb), pf0 = CasinoBank.PersonalityFactor(gb), sf0 = CasinoBank.StakeShareFor(gb);
            Kit.Trait(gb, DlcDefs.Gambler);
            t.Check(Math.Abs(Gambling.Factor(gb) / f0 - 1.5) < 1e-3, $"gambler: addiction chance x1.5 ({f0:0.000} -> {Gambling.Factor(gb):0.000})");
            t.Check(Math.Abs(CasinoBank.PersonalityFactor(gb) / pf0 - 2.0) < 1e-3 && Math.Abs(CasinoBank.StakeShareFor(gb) / sf0 - 2.0) < 1e-3, "gambler: bankroll and bet share x2");
            if (ModsConfig.BiotechActive)
            {
                var hr = Kit.Colonist(map); double hp0 = CasinoBank.PersonalityFactor(hr), hs0 = CasinoBank.StakeShareFor(hr), ha0 = Gambling.Factor(hr);
                hr.genes.AddGene(DefDatabase<GeneDef>.GetNamed("HH_Gene_HighRoller"), true);
                t.Check(Math.Abs(CasinoBank.PersonalityFactor(hr) / hp0 - 1.5) < 1e-3 && Math.Abs(CasinoBank.StakeShareFor(hr) / hs0 - 1.5) < 1e-3, "high roller: bigger bankroll and bets x1.5");
                t.Check(Math.Abs(Gambling.Factor(hr) / ha0 - 1.3) < 1e-3, "high roller: addiction x1.3 via the stat");
                var pr = Kit.Colonist(map); double pa0 = Gambling.Factor(pr); pr.genes.AddGene(DefDatabase<GeneDef>.GetNamed("HH_Gene_AddictionProofReward"), true);
                t.Check(Math.Abs(Gambling.Factor(pr) / pa0 - 0.2) < 1e-3, "addiction-proof reward circuit: x0.2 (formerly never matched)");
                Kit.Cleanup(hr, pr);
            }

            // lottery: while a lucky colonist lives here, every tenth ticket of the colony is free
            if (Modules.On(ModuleId.Bank) && Silverline.BankGameComp.Current != null)
            {
                var bc = Silverline.BankGameComp.Current; if (!bc.HasAccount) bc.OpenAccount(Silverline.SL_BankDef.Default, false);
                Ledger.Current.Post("world", Silverline.FinanceAccounts.Colony, Money.FromSilver(20000), "test funding");
                float globalOn0 = HSettings.Get("lottery.globalOn"); HSettings.Set("lottery.globalOn", 1f);
                var lot = Silverline.LotteryGame.Current; lot.EnsureInit();
                LuckApi.LottoLuckInTests = true; lot.luckyTickets = 0;
                t.Check(Silverline.LotteryGame.LuckyBuyer != null && LuckApi.LuckyLottoBuyer() != null, "lottery sees a lucky colonist (" + LuckApi.LuckyLottoBuyer() + ")");
                int n0 = lot.tickets.Count, free = 0, paid = 0; var cash0 = Silverline.FinanceAccounts.Cash;
                for (int i = 0; i < 20; i++) { var r = lot.BuyGlobalTicket(new[] { 1, 2, 3, 4, 5 + i % 20 }, 1 + i % 2); if (!r.Ok) continue; if (r.Money.Micro == 0) free++; else paid++; }
                var spent = cash0 - Silverline.FinanceAccounts.Cash;
                t.Check(lot.tickets.Count == n0 + 20 && free == 2 && paid == 18, $"20 tickets with a lucky colonist: {free} free, {paid} paid");
                t.Check(spent == Silverline.LotteryGame.GlobalPrice * 18, "only the paid tickets cost money (" + spent + ")");
                t.Check(Ledger.Current.SumAll() == 0, "ledger balanced");
                LuckApi.LottoLuckInTests = false;
                t.Check(LuckApi.LuckyLottoBuyer() == null, "test switch off: no lucky buyer");
                HSettings.Set("lottery.globalOn", globalOn0);
            }
            else t.Note("bank module off: lottery part skipped");
            LuckApi.LottoLuckInTests = false;
            if (!slot.Destroyed) slot.Destroy();
            Kit.Cleanup(lucky, unlucky, plain, av, gb, guest);
            yield break;
        }
    }

    // ===================================================================================================================
    /// <summary>Xenotype hospitality (11.5): the 6-hour room nudge from the rare tick, interests, price tolerance, spending, expectations.</summary>
    public sealed class DlcXenoSuite : HearthTestSuite
    {
        public override string Name => "dlc.xeno";
        public override ModuleId Module => ModuleId.Inn;
        public override IEnumerable<int> Run(TestContext t)
        {
            if (!ModsConfig.BiotechActive) { t.Check(XenotypeHospitality.NudgeGuests(t.Map) == 0, "no Biotech: no nudges"); yield break; }
            var map = t.Map;
            InnTestKit.Site site = null;
            try { site = InnTestKit.BuildRoom(map, 1, false, 4, 4); } catch (Exception e) { t.Check(false, "room: " + e.Message); }
            if (site == null) yield break;
            var cell = site.Interior.Cells.First(c => c.Standable(map) && c.GetFirstBuilding(map) == null);
            var g = Kit.Visitor(map, cell);
            if (g == null) { t.Note("no neutral faction for a visitor"); InnTestKit.Teardown(site, map); yield break; }
            g.Position = cell; g.Notify_Teleported(false, false); g.jobs.StopAll();
            g.genes.SetXenotype(DefDatabase<XenotypeDef>.GetNamed("Sanguophage"));
            var d = XenotypeHospitality.For(g);
            t.Check(d != null && d.defName == "HH_XenoHosp_Sanguophage", "visitor is a sanguophage guest (" + d?.defName + ")");
            var room = g.GetRoom();
            t.Check(room != null && !room.PsychologicallyOutdoors, "visitor stands in the dark guest room");
            float v = XenotypeHospitality.RoomScore(d, room, map) * 0.1f;
            t.Check(v >= 0.02f, $"a dark, enclosed room suits a sanguophage (nudge {v:0.000})");
            float s0 = GuestApi.Info(g).Satisfaction; int n = XenotypeHospitality.NudgeGuests(map); float s1 = GuestApi.Info(g).Satisfaction;
            t.Check(n >= 1 && Math.Abs(s1 - s0 - v) < 1e-4, $"nudge raises satisfaction by the room score ({s0:0.000} -> {s1:0.000})");
            int tick = Find.TickManager.TicksGame / XenotypeHospitality.NudgeInterval * XenotypeHospitality.NudgeInterval + 7;
            DlcState.Current.RareTick(tick); float s2 = GuestApi.Info(g).Satisfaction;
            t.Check(Math.Abs(s2 - s1 - v) < 1e-4, $"the rare tick runs the nudge every 6 hours ({s1:0.000} -> {s2:0.000})");
            DlcState.Current.RareTick(tick + 250); t.Check(Math.Abs(GuestApi.Info(g).Satisfaction - s2) < 1e-6, "not on other rare ticks");
            HSettings.Set("dlc.xenoHospitality", 0);
            t.Check(XenotypeHospitality.NudgeGuests(map) == 0 && Math.Abs(GuestApi.Info(g).Satisfaction - s2) < 1e-6, "setting off: no nudge");
            HSettings.Set("dlc.xenoHospitality", 1);

            // shopping: favourites first, baseliners stingier, hussars spend more
            var hemo = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("HemogenPack")); var meal = ThingMaker.MakeThing(ThingDefOf.MealSimple);
            var list = new List<Thing> { meal, hemo }; VenueShopping.SortByInterest(g, list);
            t.Check(list.Count == 2 && list[0] == hemo, "sanguophage shoppers look at hemogen packs first");
            var b = Kit.Visitor(map, map.Center);
            if (b != null)
            {
                b.genes.SetXenotype(XenotypeDefOf.Baseliner);
                var info = GuestApi.Info(b);
                float on = VenueShopping.Rho(b, info, 1f, 4242); HSettings.Set("dlc.xenoHospitality", 0); float off = VenueShopping.Rho(b, info, 1f, 4242); HSettings.Set("dlc.xenoHospitality", 1);
                t.Check(Math.Abs(on / off - 1f / 0.95f) < 1e-3, $"baseliners find prices 5 % steeper (rho {off:0.000} -> {on:0.000})");
                b.genes.SetXenotype(DefDatabase<XenotypeDef>.GetNamed("Hussar"));
                t.Check(Math.Abs(VenueShopping.SpendFactor(b) / DlcApi.TipFactor(b) - 1.15f) < 1e-3, "hussars spend 15 % more per purchase (" + VenueShopping.SpendFactor(b) + ")");
            }
            // expectations: the Inn adds the guest's offset when it opens the file (genie +0.05)
            var reg = GuestRegistry.Instance; var f = Kit.NeutralFaction();
            if (reg != null && f != null)
            {
                var gen = PawnGenerator.GeneratePawn(new PawnGenerationRequest(f.def.basicMemberKind ?? f.RandomPawnKind(), f, PawnGenerationContext.NonPlayer, -1, forceGenerateNewPawn: true));
                GenSpawn.Spawn(gen, CellFinder.RandomClosewalkCellNear(map.Center, map, 6), map);
                gen.genes.SetXenotype(DefDatabase<XenotypeDef>.GetNamed("Genie"));
                var grp = reg.NewGroup(f, map, StayKind.DayVisitor, false);
                var r = reg.Register(gen, grp, map, 0, Find.TickManager.TicksGame + 5000);
                float baseE = GuestEconomy.Expectation(r.Wealth, reg.Renown, f), off = DlcApi.GuestExpectationOffset(gen);
                t.Check(off > 0.04f && Math.Abs(r.Expectation - Mathf.Clamp01(baseE + off)) < 1e-5, $"genie guest expects more: {baseE:0.000} + {off:0.000} = {r.Expectation:0.000}");
                reg.Unregister(r, "test"); Kit.Cleanup(gen);
            }
            Kit.Cleanup(g, b);
            InnTestKit.Teardown(site, map);
            yield break;
        }
    }

    // ===================================================================================================================
    /// <summary>The other trait and gene effects: peacemaker, gossip, eagle eye, silver tongue, family cheer, penny-pincher (bank fees and
    /// prices), steady hands, number sense, refined palate.</summary>
    public sealed class DlcWiringSuite : HearthTestSuite
    {
        public override string Name => "dlc.wiring";
        public override ModuleId Module => ModuleId.Inn;
        static TraitDef Tr(string n) => DefDatabase<TraitDef>.GetNamed(n);
        static void Near(Pawn mover, Pawn target)
        {
            var map = target.Map;
            mover.Position = CellFinder.TryFindRandomCellNear(target.Position, map, 2, c => c != target.Position && c.Standable(map) && c.GetRoom(map) == target.GetRoom(), out var nc) ? nc : target.Position;
            mover.Notify_Teleported(false, false);
        }

        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var made = new List<Pawn>();
            Func<string, Pawn> With = n => { var p = Kit.Colonist(map); made.Add(p); if (n != null) Kit.Trait(p, Tr(n)); return p; };

            // peacemaker: quarrels in the same room turn into fights less often
            var a = With(null); var b = With(null); Near(b, a);
            float pf0 = Patch_PeacemakerFight.PeaceFactorAround(a); float c0 = a.interactions.SocialFightChance(InteractionDefOf.Insult, b);
            var pm = With("HH_Peacemaker"); Near(pm, a);
            float pf1 = Patch_PeacemakerFight.PeaceFactorAround(a); float c1 = a.interactions.SocialFightChance(InteractionDefOf.Insult, b);
            t.Check(Math.Abs(pf1 - Math.Min(pf0, 0.6f)) < 1e-4, $"a peacemaker nearby: fight factor {pf0:0.00} -> {pf1:0.00}");
            if (c0 > 0f) t.Check(Math.Abs(c1 / c0 - pf1 / pf0) < 1e-3, $"social fight chance scaled ({c0:0.0000} -> {c1:0.0000})");
            else t.Note("vanilla gives this pair no fight chance; factor checked only");
            pm.Position = a.Position + new IntVec3(30, 0, 30); pm.Notify_Teleported(false, false);
            if (pm.Position.InBounds(map)) t.Check(Math.Abs(Patch_PeacemakerFight.PeaceFactorAround(a) - pf0) < 1e-4, "far away: no effect");

            // gossip and eagle eye: colony-wide maxima
            With("HH_Gossip"); With("HH_EagleEye");
            t.Check(Math.Abs(DlcApi.ColonyRumorFactor(map) - 1.5f) < 1e-3, "a gossip in the colony: renown moves x1.5");
            t.Check(Math.Abs(DlcApi.ColonyWatchfulness(map) - 1.5f) < 1e-3, "an eagle eye on the map: back-room exposure / 1.5");

            // silver tongue and kid magnet as hosts; recruitment cost
            var reg = GuestRegistry.Instance; var f = Kit.NeutralFaction();
            var st = With("HH_SilverTongue"); var km = With("HH_KidMagnet"); var plainHost = With(null);
            if (reg != null && f != null)
            {
                Func<bool, Pawn> GuestPawn = child =>
                {
                    var req = child ? new PawnGenerationRequest(f.def.basicMemberKind ?? f.RandomPawnKind(), f, PawnGenerationContext.NonPlayer, -1, forceGenerateNewPawn: true, fixedBiologicalAge: 8f, fixedChronologicalAge: 8f, developmentalStages: DevelopmentalStage.Child)
                                    : new PawnGenerationRequest(f.def.basicMemberKind ?? f.RandomPawnKind(), f, PawnGenerationContext.NonPlayer, -1, forceGenerateNewPawn: true);
                    var p = PawnGenerator.GeneratePawn(req); GenSpawn.Spawn(p, CellFinder.RandomClosewalkCellNear(map.Center, map, 6), map); made.Add(p); return p;
                };
                var adult = GuestPawn(false);
                var r = reg.Register(adult, reg.NewGroup(f, map, StayKind.DayVisitor, false), map, 0, Find.TickManager.TicksGame + 20000);
                float e0 = r.Expectation; reg.Notify_Talk(st, adult); float e1 = r.Expectation; reg.Notify_Talk(st, adult);
                t.Check(Math.Abs(e1 - Mathf.Clamp01(e0 - 0.03f)) < 1e-5 && Math.Abs(r.Expectation - e1) < 1e-6, $"silver tongue host: expectation -0.03 once ({e0:0.000} -> {e1:0.000} -> {r.Expectation:0.000})");
                int cost0 = GuestRegistry.RecruitGoodwillCost(adult), cost1 = GuestRegistry.RecruitGoodwillCost(adult, st);
                int want = cost0 <= 5 ? cost0 : Math.Max(5, 5 + Mathf.RoundToInt((cost0 - 5) * 0.9f));
                t.Check(cost1 == want && cost1 <= cost0 && GuestRegistry.RecruitGoodwillCost(adult, plainHost) == cost0, $"silver tongue recruiter: goodwill cost {cost0} -> {cost1}");
                var kid1 = GuestPawn(true); var kid2 = GuestPawn(true);
                var rk1 = reg.Register(kid1, reg.NewGroup(f, map, StayKind.DayVisitor, false), map, 0, Find.TickManager.TicksGame + 20000);
                var rk2 = reg.Register(kid2, reg.NewGroup(f, map, StayKind.DayVisitor, false), map, 0, Find.TickManager.TicksGame + 20000);
                float k1 = rk1.ServiceSum, k2 = rk2.ServiceSum; reg.Notify_Talk(km, kid1); reg.Notify_Talk(plainHost, kid2);
                t.Check(Math.Abs((rk1.ServiceSum - k1) - (rk2.ServiceSum - k2) - 0.1f) < 1e-4, $"kid magnet host: children +0.1 service ({rk1.ServiceSum - k1:0.00} vs {rk2.ServiceSum - k2:0.00})");
                foreach (var x in new[] { r, rk1, rk2 }) reg.Unregister(x, "test");
            }
            else t.Note("no register or faction: host effects not exercised");

            // penny-pincher: bank fees and steeper prices
            var pp = With("HH_PennyPincher");
            if (Modules.On(ModuleId.Bank))
            {
                Silverline.BankSettings.ColonistFeeInTests = true; Silverline.BankSettings.ResetFeeCache();
                t.Check(Math.Abs(Silverline.BankSettings.ColonistFeeFactor - 0.8) < 1e-4 && Math.Abs(Silverline.BankSettings.FeeFactor - HSettings.Get("bank.feeFactor") * 0.8) < 1e-4, "a penny-pincher in the colony: bank fees x0.8");
                Silverline.BankSettings.ColonistFeeInTests = false; Silverline.BankSettings.ResetFeeCache();
                t.Check(Silverline.BankSettings.ColonistFeeFactor == 1.0, "test switch off: fees as set");
            }
            var shopper = Kit.Visitor(map, map.Center); if (shopper != null) made.Add(shopper);
            if (shopper != null)
            {
                var info = GuestApi.Info(shopper);
                float r0 = VenueShopping.Rho(shopper, info, 1f, 77), r1 = VenueShopping.Rho(shopper, info, 1f, 77, pp), r2 = VenueShopping.Rho(shopper, info, 1f, 77, plainHost);
                t.Check(Math.Abs(r1 / r0 - 1.1f) < 1e-4 && Math.Abs(r2 - r0) < 1e-6, $"penny-pincher behind the counter: prices feel 10 % steeper ({r0:0.000} -> {r1:0.000})");
            }

            // steady hands: a croupier deals faster; number sense / gambler: better card play
            var sh = With("HH_SteadyHands");
            t.Check(Math.Abs(CasinoEngine.DealSpeedFactor(sh) - 0.88f) < 1e-4 && Math.Abs(CasinoEngine.DealSpeedFactor(plainHost) - 1f) < 1e-6 && CasinoEngine.DealSpeedFactor(null) == 1f, "steady hands: rounds 12 % shorter");
            var card = With(null); card.skills.GetSkill(SkillDefOf.Intellectual).Level = 6;
            double acc0 = CasinoEngine.AccuracyOf(card);
            if (ModsConfig.BiotechActive) card.genes.AddGene(DefDatabase<GeneDef>.GetNamed("HH_Gene_NumberSense"), true); else Kit.Trait(card, DlcDefs.Gambler);
            double acc1 = CasinoEngine.AccuracyOf(card);
            t.Check(acc1 > acc0, $"number sense plays blackjack better ({acc0:0.000} -> {acc1:0.000})");

            // refined palate: a cook lifts a dish by one quality step with 10 % chance
            if (ModsConfig.BiotechActive)
            {
                var cook = With(null); cook.genes.AddGene(DefDatabase<GeneDef>.GetNamed("HH_Gene_RefinedPalate"), true);
                var plate = ThingMaker.MakeThing(ThingDefOf.Bed, ThingDefOf.WoodLog); var qc = plate.TryGetComp<CompQuality>();
                int lifts = 0, plainLifts = 0;
                for (int i = 0; i < 400; i++) { qc.SetQuality(QualityCategory.Normal, ArtGenerationContext.Colony); if (Hearth.Kitchen.DishEffects.PalateLift(plate, cook)) { lifts++; t.Check(qc.Quality == QualityCategory.Good, "lifted by exactly one step"); } }
                for (int i = 0; i < 100; i++) { qc.SetQuality(QualityCategory.Normal, ArtGenerationContext.Colony); if (Hearth.Kitchen.DishEffects.PalateLift(plate, plainHost)) plainLifts++; }
                t.Check(lifts >= 18 && lifts <= 70 && plainLifts == 0, $"refined palate: {lifts} of 400 dishes lifted (about 40 expected), plain cook {plainLifts}");
                qc.SetQuality(QualityCategory.Legendary, ArtGenerationContext.Colony); t.Check(!Hearth.Kitchen.DishEffects.PalateLift(plate, cook) || qc.Quality == QualityCategory.Legendary, "never above legendary");
                var gen = Kit.Visitor(map, map.Center);
                if (gen != null) { made.Add(gen); float sp0 = VenueShopping.SpendFactor(gen); gen.genes.AddGene(DefDatabase<GeneDef>.GetNamed("HH_Gene_RefinedPalate"), true); t.Check(Math.Abs(VenueShopping.SpendFactor(gen) / sp0 - 1.3f) < 1e-3, "refined palate guest spends 30 % more per purchase"); }
            }
            Kit.Cleanup(made.ToArray());
            yield break;
        }
    }
}
#endif
