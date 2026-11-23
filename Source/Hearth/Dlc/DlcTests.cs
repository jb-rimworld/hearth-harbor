// SPDX-License-Identifier: MIT
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using Hearth.Testing;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Hearth.Dlc.Tests
{
    /// <summary>Shared helpers of the DLC suites.</summary>
    static class Kit
    {
        public static Pawn Colonist(Map map)
        {
            Pawn p = null;
            // a random backstory may disable Social: such a pawn learns no social xp and the tests would flake. Re-roll until Social works.
            for (int tries = 0; tries < 10; tries++)
            {
                p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, PawnGenerationContext.NonPlayer, -1, forceGenerateNewPawn: true));
                if (!p.skills.GetSkill(SkillDefOf.Social).TotallyDisabled || tries == 9) break;
                Find.WorldPawns.PassToWorld(p, RimWorld.Planet.PawnDiscardDecideMode.Discard);
            }
            GenSpawn.Spawn(p, CellFinder.RandomClosewalkCellNear(map.Center, map, 12), map);
            foreach (var tr in p.story.traits.allTraits.Where(x => x.def.defName.StartsWith("HH_")).ToList()) p.story.traits.RemoveTrait(tr); // generated pawns may roll our traits now: tests start clean
            if (ModsConfig.IdeologyActive && p.Ideo == null && Faction.OfPlayer.ideos?.PrimaryIdeo != null) p.ideo.SetIdeo(Faction.OfPlayer.ideos.PrimaryIdeo);
            return p;
        }

        public static Faction NeutralFaction()
        {
            Func<Faction, bool> ok = x => !x.IsPlayer && !x.Hidden && x.def.humanlikeFaction && !x.def.permanentEnemy && x != Faction.OfEmpire;
            var all = Find.FactionManager.AllFactionsListForReading;
            var f = all.FirstOrDefault(x => ok(x) && !x.def.fixedIdeo && !x.HostileTo(Faction.OfPlayer)) ?? all.FirstOrDefault(x => ok(x) && !x.HostileTo(Faction.OfPlayer)) ?? all.FirstOrDefault(ok);
            if (f != null && f.HostileTo(Faction.OfPlayer)) Faction.OfPlayer.TryAffectGoodwillWith(f, 100, false, false);
            return f;
        }

        /// <summary>A visitor of a neutral faction with a visiting lord, so GuestApi treats it as a guest (fallback without the Inn module).</summary>
        public static Pawn Visitor(Map map, IntVec3? near = null)
        {
            var f = NeutralFaction(); if (f == null) return null;
            var p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(f.def.basicMemberKind ?? f.RandomPawnKind(), f, PawnGenerationContext.NonPlayer, -1, forceGenerateNewPawn: true));
            GenSpawn.Spawn(p, CellFinder.RandomClosewalkCellNear(near ?? map.Center, map, 5), map);
            LordMaker.MakeNewLord(f, new LordJob_VisitColony(f, near ?? map.Center, 60000 * 3), map, new List<Pawn> { p });
            return p;
        }

        public static void GiveSilver(Pawn p, int n)
        {
            while (n > 0) { int k = Math.Min(n, 300); var s = ThingMaker.MakeThing(ThingDefOf.Silver); s.stackCount = k; p.inventory.innerContainer.TryAdd(s, false); n -= k; }
        }
        public static int Silver(Pawn p) => p.inventory.innerContainer.Where(x => x.def == ThingDefOf.Silver).Sum(x => x.stackCount);
        public static void Cleanup(params Pawn[] ps) { foreach (var p in ps) if (p != null && !p.Destroyed) p.Destroy(); }
        public static void Trait(Pawn p, TraitDef d) { if (d != null) p.story.traits.GainTrait(new Trait(d)); }
    }

    // ===================================================================================================================
    public sealed class DlcDefsSuite : HearthTestSuite
    {
        public override string Name => "dlc.defs";
        public override IEnumerable<int> Run(TestContext t)
        {
            t.Note($"DLC: royalty={ModsConfig.RoyaltyActive} ideology={ModsConfig.IdeologyActive} biotech={ModsConfig.BiotechActive}");
            // always present, with or without any DLC
            string[] traits = { "Lucky", "Unlucky", "GamblingAverse", "Gambler", "BornHost", "Grump", "PennyPincher", "SilverTongue", "EagleEye", "SteadyHands", "Caregiver", "Peacemaker", "Gossip", "KidMagnet" };
            foreach (var n in traits) { var d = DefDatabase<TraitDef>.GetNamedSilentFail("HH_" + n); t.Check(d != null && d.degreeDatas.Count == 1 && !d.degreeDatas[0].description.NullOrEmpty(), "trait HH_" + n); }
            t.Check(DefDatabase<StatDef>.AllDefs.Count(s => s.defName.StartsWith("HH_")) >= 18, "stat defs: " + DefDatabase<StatDef>.AllDefs.Count(s => s.defName.StartsWith("HH_")));
            foreach (var s in DefDatabase<StatDef>.AllDefs.Where(s => s.defName.StartsWith("HH_"))) t.Check(!s.label.NullOrEmpty() && !s.description.NullOrEmpty(), "stat label/description " + s.defName);
            t.Check(HSettings.Find("dlc.luckShift") != null && HSettings.Find("dlc.indulgenceFactor") != null && HSettings.Find("dlc.nobleBasePrice") != null, "dlc settings registered");
            t.Check(HSettings.Find("dlc.nobleMoneyFactor").TierAnchors?.Length == 7, "noble money follows the 7 tier anchors");
            t.Check(Math.Abs(HSettings.Find("dlc.luckShift").Default - 0.015f) < 1e-6, "luck shift default 1.5 pp");
            foreach (var th in new[] { "HH_LuckyStreak", "HH_OfCourseILost", "HH_GamblingAround" }) t.Check(DlcDefs.Thought(th) != null, "thought " + th);

            // DLC defs exist exactly when their DLC is active
            int genes = DefDatabase<GeneDef>.AllDefs.Count(g => g.defName.StartsWith("HH_Gene_"));
            t.Check(genes == (ModsConfig.BiotechActive ? 9 : 0), $"genes {genes} (biotech={ModsConfig.BiotechActive})");
            int xeno = DefDatabase<XenotypeHospitalityDef>.AllDefs.Count();
            t.Check(ModsConfig.BiotechActive ? xeno >= 13 : xeno == 0, $"xenotype hospitality entries {xeno}");
            foreach (var x in DefDatabase<XenotypeHospitalityDef>.AllDefs) t.Check(x.ConfigErrors().Count() == 0, "xeno entry ok " + x.defName);
            int ind = DefDatabase<IndulgenceDef>.AllDefs.Count();
            t.Check(ModsConfig.IdeologyActive ? ind >= 9 : ind == 0, $"indulgence defs {ind}");
            t.Check((DlcDefs.IssueIndulgences != null) == ModsConfig.IdeologyActive, "money issues exist only with Ideology");
            t.Check((DefDatabase<ThingDef>.GetNamedSilentFail("HH_GuestAltar") != null) == ModsConfig.IdeologyActive, "guest altar exists only with Ideology");
            t.Check((DefDatabase<IncidentDef>.GetNamedSilentFail("HH_NobleStay") != null) == ModsConfig.RoyaltyActive, "noble incidents exist only with Royalty");
            t.Check((DefDatabase<LetterDef>.GetNamedSilentFail("HH_NobleOffer") != null) == ModsConfig.RoyaltyActive, "noble letter exists only with Royalty");
            if (ModsConfig.IdeologyActive)
            {
                foreach (var d in DefDatabase<IndulgenceDef>.AllDefs) { t.Check(d.ConfigErrors().Count() == 0 && d.ListPrice > 0 && d.Worker != null, "indulgence ok " + d.defName + " " + d.ListPrice); }
                foreach (var th in MoneyTheme_All()) foreach (var st in new[] { MoneyStance.NotAThing, MoneyStance.Abhorrent, MoneyStance.Disapproved, MoneyStance.Accepted, MoneyStance.Sacred })
                    t.Check(IdeoStances.PreceptFor(th, st) != null, "precept for " + th + "/" + st);
                t.Check(DefDatabase<PreceptDef>.AllDefs.Count(p => p.issue == DlcDefs.IssueIndulgences) == 5 && DefDatabase<PreceptDef>.AllDefs.Count(p => p.issue == DlcDefs.IssueAscension) == 5, "five stances per issue");
                t.Check(DefDatabase<PreceptDef>.GetNamed("HH_Indulgences_NotAThing").countsTowardsPreceptLimit == false, "NotAThing does not count toward the precept limit");
            }
            if (!ModsConfig.RoyaltyActive && !ModsConfig.IdeologyActive && !ModsConfig.BiotechActive)
                t.Note("NO DLC run: the mod loaded cleanly with only Core");
            t.Check(HLog.ErrorCount == 0, "no HH errors logged during load/defs: " + HLog.ErrorCount);
            yield break;
        }
        static IEnumerable<MoneyTheme> MoneyTheme_All() { yield return MoneyTheme.Indulgences; yield return MoneyTheme.Ascension; }
    }

    // ===================================================================================================================
    public sealed class DlcTraitsSuite : HearthTestSuite
    {
        public override string Name => "dlc.traits";
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var p = Kit.Colonist(map);
            float sq0 = DlcApi.ServiceQuality(p);
            Kit.Trait(p, DefDatabase<TraitDef>.GetNamed("HH_BornHost"));
            t.Check(Math.Abs(DlcApi.ServiceQuality(p) - sq0 - 0.15f) < 1e-3, "born host: service quality +0.15 (" + DlcApi.ServiceQuality(p) + ")");
            t.Check(p.GetStatValue(DlcDefs.HostingLearn) > 1.49f, "born host: hosting learning x1.5");
            var q = Kit.Colonist(map); Kit.Trait(q, DefDatabase<TraitDef>.GetNamed("HH_Grump"));
            t.Check(DlcApi.ServiceQuality(q) < sq0 - 0.19f, "grump: service quality -0.2 (" + DlcApi.ServiceQuality(q) + ")");
            var st = Kit.Colonist(map); Kit.Trait(st, DefDatabase<TraitDef>.GetNamed("HH_SilverTongue"));
            t.Check(Math.Abs(DlcApi.HostExpectationOffset(st) + 0.03f) < 1e-3, "silver tongue: expectation -0.03");
            t.Check(Math.Abs(DlcApi.RecruitPriceFactor(st) - 0.9f) < 1e-3, "silver tongue: recruit surcharge x0.9");
            var ey = Kit.Colonist(map); Kit.Trait(ey, DefDatabase<TraitDef>.GetNamed("HH_EagleEye")); t.Check(Math.Abs(DlcApi.FraudDetection(ey) - 1.5f) < 1e-3, "eagle eye: fraud detection x1.5");
            var sh = Kit.Colonist(map); Kit.Trait(sh, DefDatabase<TraitDef>.GetNamed("HH_SteadyHands")); t.Check(Math.Abs(DlcApi.DexterityBonus(sh) - 4f) < 1e-3, "steady hands: dexterity +4");
            var cg = Kit.Colonist(map); Kit.Trait(cg, DefDatabase<TraitDef>.GetNamed("HH_Caregiver")); t.Check(Math.Abs(DlcApi.CareProgressFactor(cg) - 1.15f) < 1e-3, "caregiver: care progress x1.15");
            var pm = Kit.Colonist(map); Kit.Trait(pm, DefDatabase<TraitDef>.GetNamed("HH_Peacemaker")); t.Check(Math.Abs(DlcApi.BrawlRiskFactor(pm) - 0.6f) < 1e-3, "peacemaker: brawl risk x0.6");
            var gs = Kit.Colonist(map); Kit.Trait(gs, DefDatabase<TraitDef>.GetNamed("HH_Gossip")); t.Check(Math.Abs(DlcApi.RumorFactor(gs) - 1.5f) < 1e-3, "gossip: rumour spread x1.5");
            var pp = Kit.Colonist(map); Kit.Trait(pp, DefDatabase<TraitDef>.GetNamed("HH_PennyPincher")); t.Check(Math.Abs(DlcApi.BankFeeFactor(pp) - 0.8f) < 1e-3, "penny-pincher: bank fees x0.8");

            // luck: shift, cap, no bending of the draw
            var lucky = Kit.Colonist(map); var unlucky = Kit.Colonist(map); var plain = Kit.Colonist(map);
            Kit.Trait(lucky, DlcDefs.Lucky); Kit.Trait(unlucky, DlcDefs.Unlucky);
            t.Check(Math.Abs(LuckApi.RtpShiftFor(lucky) - 0.015f) < 1e-6, "lucky: RTP +1.5 pp");
            t.Check(Math.Abs(LuckApi.RtpShiftFor(unlucky) + 0.015f) < 1e-6, "unlucky: RTP -1.5 pp");
            t.Check(LuckApi.RtpShiftFor(plain) == 0f, "plain: no shift");
            t.Check(Math.Abs(LuckApi.AdjustRtp(0.92f, lucky) - 0.935f) < 1e-5 && Math.Abs(LuckApi.AdjustRtp(0.92f, unlucky) - 0.905f) < 1e-5, "adjusted plans 93.5 % / 90.5 %");
            t.Check(LuckApi.AdjustRtp(0.99f, lucky) <= 0.995f && LuckApi.AdjustRtp(1.0f, lucky) < 1f, "never reaches 100 %");
            t.Check(LuckApi.AdjustRtp(0.997f, lucky) <= 0.997f + 1e-6f, "no lift above an already generous plan");
            // streak thoughts
            LuckApi.NoteResult(lucky, true); LuckApi.NoteResult(lucky, true);
            t.Check(lucky.needs.mood.thoughts.memories.NumMemoriesOfDef(DlcDefs.Thought("HH_LuckyStreak")) == 1, "lucky streak after two wins");
            LuckApi.NoteResult(unlucky, false);
            t.Check(unlucky.needs.mood.thoughts.memories.NumMemoriesOfDef(DlcDefs.Thought("HH_OfCourseILost")) == 1, "of course I lost");
            // averse / gambler
            var av = Kit.Colonist(map); Kit.Trait(av, DlcDefs.GamblingAverse);
            t.Check(!LuckApi.WillGamble(av) && LuckApi.AddictionFactor(av) == 0f && LuckApi.WillGamble(plain), "averse never gambles, addiction 0");
            t.Check(Math.Abs(DlcApi.FraudDetection(av) - 1f) < 1e-3, "averse: no watchfulness claim any more (x1)");
            var gb = Kit.Colonist(map); Kit.Trait(gb, DlcDefs.Gambler);
            t.Check(Math.Abs(LuckApi.AddictionFactor(gb) - 1.5f) < 1e-3 && Math.Abs(LuckApi.JoyFactor(gb) - 1.5f) < 1e-3 && DlcApi.TableSkillBonus(gb) >= 1f, "gambler: joy x1.5, addiction x1.5, croupier bonus");
            t.Check(DefDatabase<TraitDef>.GetNamed("HH_Lucky").conflictingTraits.Contains(DlcDefs.Unlucky), "lucky/unlucky conflict");

            // casino module off -> luck is neutral
            var prof = HearthProfile.Current; bool was = prof.ModuleOn(ModuleId.Casino);
            prof.SetModule(ModuleId.Casino, false);
            t.Check(LuckApi.RtpShiftFor(lucky) == 0f, "casino module off: no luck shift");
            prof.SetModule(ModuleId.Casino, was);

            // spawn commonality follows the settings/module
            var def = DefDatabase<TraitDef>.GetNamed("HH_Lucky");
            float c0 = def.GetGenderSpecificCommonality(Gender.Male); t.Check(Math.Abs(c0 - 0.2f) < 1e-4, "commonality 0.2 by default (" + c0 + ")");
            HSettings.Set("dlc.traitSpawn", 0); t.Check(def.GetGenderSpecificCommonality(Gender.Male) == 0f, "trait spawn off: commonality 0");
            HSettings.Set("dlc.traitSpawn", 1); HSettings.Set("dlc.traitRarity", 2f);
            t.Check(Math.Abs(def.GetGenderSpecificCommonality(Gender.Male) - 0.4f) < 1e-4, "rarity x2");
            HSettings.Set("dlc.traitRarity", 1f);

            // hosting learning: only while working at Hosting
            var learner = Kit.Colonist(map); var sk = learner.skills.GetSkill(SkillDefOf.Social); var host = DlcDefs.Hosting;
            var wg = new WorkGiverDef { defName = "HH_TestHostingGiver", workType = host };
            Func<float> gain = () => { sk.xpSinceLastLevel = 0f; sk.Learn(20f, true); return sk.xpSinceLastLevel; };
            var job = JobMaker.MakeJob(JobDefOf.Wait); job.workGiverDef = wg; var oldJob = learner.jobs.curJob;
            float baseGain = gain();
            Kit.Trait(learner, DefDatabase<TraitDef>.GetNamed("HH_BornHost"));
            float idle = gain(); learner.jobs.curJob = job; float hosting = gain(); learner.jobs.curJob = oldJob;
            t.Check(Math.Abs(idle - baseGain) < 0.5f, $"born host does not speed up learning outside hosting ({baseGain:0.0} / {idle:0.0})");
            t.Check(baseGain > 0f && hosting / baseGain > 1.35f && hosting / baseGain < 1.65f, $"born host learns hosting x1.5 ({hosting:0.0} vs {baseGain:0.0})");

            // service quality chat -> guest satisfaction (works with vanilla visitors)
            var g = Kit.Visitor(map, map.Center); var g2 = Kit.Visitor(map, map.Center);
            if (g != null && g2 != null && GuestApi.IsGuest(g))
            {
                var friendly = p; var surly = q;
                // next to the guest on a walkable cell with a clear line: the cell to the east can be rock, a tree or water on a random map (or the guest has wandered next to it), and vanilla then refuses the interaction
                Action<Pawn, Pawn> near = (h, gu) => { h.Position = CellFinder.TryFindRandomCellNear(gu.Position, gu.Map, 2, c => c != gu.Position && c.Standable(gu.Map) && GenSight.LineOfSight(c, gu.Position, gu.Map), out var nc) ? nc : gu.Position + IntVec3.East; h.Notify_Teleported(false, false); };
                near(friendly, g); near(surly, g2);
                float s0 = GuestApi.Info(g).Satisfaction; bool ok1 = friendly.interactions.TryInteractWith(g, InteractionDefOf.Chitchat);
                t.Check(ok1 && GuestApi.Info(g).Satisfaction > s0 + 0.02f, $"born host chat raises guest satisfaction ({s0:0.000} -> {GuestApi.Info(g).Satisfaction:0.000}, chat={ok1})");
                float s1 = GuestApi.Info(g2).Satisfaction; bool ok2 = surly.interactions.TryInteractWith(g2, InteractionDefOf.Chitchat);
                t.Check(ok2 && GuestApi.Info(g2).Satisfaction < s1 - 0.02f, "grump chat lowers guest satisfaction (chat=" + ok2 + ")");
                yield return 150;
                near(friendly, g); float s2 = GuestApi.Info(g).Satisfaction; bool ok3 = friendly.interactions.TryInteractWith(g, InteractionDefOf.Chitchat);
                t.Check(ok3 && Math.Abs(GuestApi.Info(g).Satisfaction - s2) < 1e-6f, "throttled: a second chat within the hour changes nothing (chat=" + ok3 + ")");
            }
            else t.Note("no neutral faction/visitor available: chat effect not exercised");

            // serving rest: tireless smile factor (via stat on a pawn standing in for the gene)
            var waiter = Kit.Colonist(map); var rest = waiter.needs.rest;
            if (rest != null && DlcDefs.Serving != null)
            {
                var sg = new WorkGiverDef { defName = "HH_TestServeGiver", workType = DlcDefs.Serving };
                var sj = JobMaker.MakeJob(JobDefOf.Wait); sj.workGiverDef = sg; var oj = waiter.jobs.curJob; waiter.jobs.curJob = sj;
                rest.CurLevel = 0.8f; rest.NeedInterval(); float drop0 = 0.8f - rest.CurLevel;
                t.Check(drop0 > 0f, "serving drains rest (" + drop0 + ")");
                waiter.jobs.curJob = oj;
            }
            Kit.Cleanup(p, q, st, ey, sh, cg, pm, gs, pp, lucky, unlucky, plain, av, gb, learner, g, g2, waiter);
            yield break;
        }
    }

    // ===================================================================================================================
    public sealed class DlcGenesSuite : HearthTestSuite
    {
        public override string Name => "dlc.genes";
        public override IEnumerable<int> Run(TestContext t)
        {
            if (!ModsConfig.BiotechActive) { t.Check(DefDatabase<GeneDef>.AllDefs.All(g => !g.defName.StartsWith("HH_Gene_")), "no Biotech: no genes, no xenotype entries"); t.Check(XenotypeHospitality.For(Kit.Colonist(t.Map)) == null, "no Biotech: no xenotype preference"); yield break; }
            var map = t.Map; var p = Kit.Colonist(map);
            Func<string, GeneDef> G = n => DefDatabase<GeneDef>.GetNamed("HH_Gene_" + n);
            foreach (var d in DefDatabase<GeneDef>.AllDefs.Where(g => g.defName.StartsWith("HH_Gene_"))) t.Check(!d.label.NullOrEmpty() && !d.description.NullOrEmpty() && d.iconPath != null, "gene defined " + d.defName);
            p.genes.AddGene(G("Hospitable"), true);
            t.Check(Math.Abs(DlcApi.ServiceQuality(p) - 0.1f) < 0.02f && Math.Abs(p.GetStatValue(DlcDefs.HostingLearn) - 1.25f) < 1e-3, "hospitable: +0.1 service, hosting learning x1.25");
            p.genes.AddGene(G("NumberSense"), true); t.Check(Math.Abs(DlcApi.TableSkillBonus(p) - 3f) < 1e-3, "number sense: +3 counting skill");
            p.genes.AddGene(G("DexterousFingers"), true); t.Check(Math.Abs(DlcApi.DexterityBonus(p) - 3f) < 1e-3, "dexterous fingers: +3");
            p.genes.AddGene(G("TirelessSmile"), true); t.Check(Math.Abs(DlcApi.ServeRestFactor(p) - 0.7f) < 1e-3, "tireless smile: x0.7");
            p.genes.AddGene(G("RefinedPalate"), true);
            t.Check(Math.Abs(DlcApi.TipFactor(p) - 1.3f) < 1e-3 && Math.Abs(DlcApi.DishQualityLift(p) - 0.1f) < 1e-3 && Math.Abs(DlcApi.GuestExpectationOffset(p) - DlcApi.GuestExpectationOffset(Kit.Colonist(map)) - 0.05f) < 0.01f, "refined palate: dish quality +10 %, generosity x1.3, expectation +0.05");
            p.genes.AddGene(G("SilverTongue"), true); t.Check(Math.Abs(DlcApi.RecruitPriceFactor(p) - 0.9f) < 1e-3, "silver tongue gene: recruit surcharge x0.9");
            var gam = Kit.Colonist(map); gam.genes.AddGene(G("Compulsive"), true);
            t.Check(Math.Abs(LuckApi.AddictionFactor(gam) - 2f) < 1e-3 && Math.Abs(LuckApi.JoyFactor(gam) - 1.3f) < 1e-3, "compulsive: joy x1.3, addiction x2");
            var proof = Kit.Colonist(map); proof.genes.AddGene(G("AddictionProofReward"), true);
            t.Check(Math.Abs(LuckApi.AddictionFactor(proof) - 0.2f) < 1e-3, "addiction-proof reward circuit: x0.2");
            var roller = Kit.Colonist(map); roller.genes.AddGene(G("HighRoller"), true);
            t.Check(LuckApi.JoyFactor(roller) > 1.19f && DlcApi.TipFactor(roller) > 1.49f && LuckApi.IsHighRoller(roller) && !LuckApi.IsHighRoller(p), "high roller: joy x1.2, generosity x1.5, detected");
            t.Check(G("Compulsive").exclusionTags.Contains("HH_GamblingDrive") && G("AddictionProofReward").exclusionTags.Contains("HH_GamblingDrive"), "gambling-drive genes exclude each other");

            // tireless smile actually slows rest drain while serving
            var w = Kit.Colonist(map); var w2 = Kit.Colonist(map); w2.genes.AddGene(G("TirelessSmile"), true);
            var sg = new WorkGiverDef { defName = "HH_TestServeGiver2", workType = DlcDefs.Serving };
            Func<Pawn, float> drop = pw => { var sj = JobMaker.MakeJob(JobDefOf.Wait); sj.workGiverDef = sg; var oj = pw.jobs.curJob; pw.jobs.curJob = sj; pw.needs.rest.CurLevel = 0.8f; pw.needs.rest.NeedInterval(); pw.jobs.curJob = oj; return 0.8f - pw.needs.rest.CurLevel; };
            float dBase = drop(w), dSmile = drop(w2);
            t.Check(dBase > 0f && Math.Abs(dSmile / dBase - 0.7f) < 0.05f, $"tireless smile: rest drain x0.7 while serving ({dSmile:0.0000} vs {dBase:0.0000})");
            var idle = w2.needs.rest; idle.CurLevel = 0.8f; idle.NeedInterval(); float dIdle = 0.8f - idle.CurLevel;
            t.Check(Math.Abs(dIdle - dBase) < dBase * 0.05f, "no effect while not serving");

            // xenotype hospitality (11.5)
            var hem = Kit.Colonist(map); hem.genes.AddGene(DefDatabase<GeneDef>.GetNamed("Hemogenic"), true);
            var e1 = XenotypeHospitality.For(hem);
            t.Check(e1 != null && e1.defName == "HH_XenoHosp_Hemogenic", "hemogenic gene matches the gene entry (" + e1?.defName + ")");
            t.Check(Math.Abs(XenotypeHospitality.InterestWeight(hem, DefDatabase<ThingDef>.GetNamed("HemogenPack")) - 2f) < 1e-3, "hemogenic guests want hemogen packs");
            hem.genes.SetXenotype(DefDatabase<XenotypeDef>.GetNamed("Sanguophage"));
            var e2 = XenotypeHospitality.For(hem); t.Check(e2 != null && e2.defName == "HH_XenoHosp_Sanguophage", "exact xenotype beats the gene entry (" + e2?.defName + ")");
            var base1 = Kit.Colonist(map); var eb = XenotypeHospitality.For(base1); t.Check(eb != null && eb.defName == "HH_XenoHosp_Baseliner" && eb.priceTolerance < 0, "baseliner entry: price tolerance below 0");
            var pk = Kit.Colonist(map); pk.genes.SetXenotype(DefDatabase<XenotypeDef>.GetNamed("Yttakin"));
            var ey = XenotypeHospitality.For(pk); t.Check(ey != null && ey.tempPref < 0, "yttakin prefer cold rooms");
            var room = hem.GetRoom(); float sc = XenotypeHospitality.RoomScore(e2, room, map);
            t.Check(sc >= -1f && sc <= 1f && !float.IsNaN(sc), "room score finite and in range (" + sc + ")");
            // a dark bright pair via synthetic defs
            var darkLover = new XenotypeHospitalityDef { lightPref = -1f, indoorPref = 0f };
            var bright = new XenotypeHospitalityDef { lightPref = 1f };
            float a = XenotypeHospitality.RoomScore(darkLover, room, map), b = XenotypeHospitality.RoomScore(bright, room, map);
            t.Check(Math.Abs(a + b) < 1e-4, "light preference is symmetric (" + a + " / " + b + ")");
            HSettings.Set("dlc.xenoHospitality", 0); t.Check(XenotypeHospitality.SpendFactor(hem) == 1f && XenotypeHospitality.PriceToleranceOffset(base1) == 0f, "setting off: preferences ignored"); HSettings.Set("dlc.xenoHospitality", 1);
            t.Check(XenotypeHospitality.SpendFactor(hem) > 1f, "sanguophages spend a bit more");
            Kit.Cleanup(p, gam, proof, roller, w, w2, hem, base1, pk);
        }
    }

    // ===================================================================================================================
    public sealed class DlcIndulgenceSuite : HearthTestSuite
    {
        public override string Name => "dlc.indulgence";
        public override IEnumerable<int> Run(TestContext t)
        {
            if (!ModsConfig.IdeologyActive) { t.Check(DefDatabase<IndulgenceDef>.AllDefs.Count() == 0 && IndulgenceApi.OfficeOnMap(t.Map) == false, "no Ideology: nothing offered, nothing visible"); yield break; }
            var map = t.Map; var l = Ledger.Current; var pawn = Kit.Colonist(map); var ideo = pawn.Ideo;
            t.Check(ideo != null, "colonist has an ideoligion");
            Func<string, IndulgenceDef> D = n => DefDatabase<IndulgenceDef>.GetNamed(n);
            Action<MoneyTheme, MoneyStance> stance = (th, s) => DlcState.SetStance(ideo, th, s);
            // A guilty memory = a memory tied to a precept that really lowers mood. SleptOutside (the old pick) has minExpectationForNegativeThought,
            // so on a poor test colony its mood offset is 0 and there is nothing to absolve: pick a memory that always counts.
            Action guilt = () =>
            {
                var mem = pawn.needs.mood.thoughts.memories; var precept = ideo.PreceptsListForReading.First();
                foreach (var td in DefDatabase<ThoughtDef>.AllDefsListForReading)
                {
                    if (!td.IsMemory || td.stages.NullOrEmpty() || td.stages[0] == null || td.stages[0].baseMoodEffect > -1f || td.minExpectationForNegativeThought != null
                        || td.requiredTraits != null || td.nullifyingTraits != null || td.requiredGenes != null || td.workerClass != null || td.IsSocial || td.IsSituational) continue;
                    int before = mem.Memories.Count;
                    mem.TryGainMemory(td, null, precept);
                    var gained = mem.Memories.Skip(before).FirstOrDefault(m => m.def == td);
                    if (gained != null && gained.MoodOffset() < 0f) return;
                    if (gained != null) mem.RemoveMemory(gained);
                }
                t.Check(false, "found a memory thought that lowers mood");
            };
            Func<string, float> deity = _ => (float)l.Balance(IndulgenceApi.DeityAccount(ideo)).Silver;
            DlcState.Current.SetDestination(ideo, FundDestination.ToDeity);
            Faction.OfPlayer.ideos.SetPrimary(ideo);
            Kit.GiveSilver(pawn, 3000);

            // stances: precepts are real and readable
            stance(MoneyTheme.Indulgences, MoneyStance.Accepted); stance(MoneyTheme.Ascension, MoneyStance.Accepted);
            t.Check(IdeoStances.Of(pawn, MoneyTheme.Indulgences) == MoneyStance.Accepted && IdeoStances.Of(pawn, MoneyTheme.Ascension) == MoneyStance.Accepted, "stance read back from the ideoligion");
            stance(MoneyTheme.Indulgences, MoneyStance.Accepted);
            t.Check(ideo.PreceptsListForReading.Count(p => p.def.issue == DlcDefs.IssueIndulgences) == 1, "exactly one indulgence precept");

            // express absolution: price, no effect without guilt, money leaves, memory goes
            var abs = D("HH_Ind_ExpressAbsolution");
            t.Check(!IndulgenceApi.CanBuy(pawn, abs, out string why) && why != null, "no guilty memory: nothing to buy (" + why + ")");
            t.Check(IndulgenceApi.TryBuy(pawn, abs, out _) == BuyResult.NoEffect, "result NoEffect, no money moved");
            guilt(); t.Check(IndulgenceApi.CanBuy(pawn, abs, out _), "guilty memory: can buy");
            t.Check(IndulgenceApi.PriceFor(pawn, abs).WholeSilver == 150, "price 150 silver");
            int s0 = Kit.Silver(pawn); float dv0 = deity(""); long tot0 = l.SumAll();
            t.Check(IndulgenceApi.TryBuy(pawn, abs, out string rs) == BuyResult.Ok, "bought: " + rs);
            t.Check(s0 - Kit.Silver(pawn) == 150, "150 silver left the pocket (" + (s0 - Kit.Silver(pawn)) + ")");
            t.Check(Math.Abs(deity("") - dv0 - 150f) < 0.01f, "ledger: 150 to the deity account");
            t.Check(l.SumAll() == tot0 && l.SumAll() == 0, "ledger still sums to zero");
            t.Check(!IndulgenceApi.CanBuy(pawn, abs, out _), "memory is gone: nothing left to absolve");
            guilt(); t.Check(IndulgenceApi.PriceFor(pawn, abs).WholeSilver == 225, "second purchase costs x1.5 (225)");
            t.Check(IndulgenceApi.TryBuy(pawn, abs, out _) == BuyResult.Ok, "second ok");
            guilt(); t.Check(IndulgenceApi.TryBuy(pawn, abs, out _) == BuyResult.Ok, "third ok");
            guilt(); t.Check(IndulgenceApi.TryBuy(pawn, abs, out rs) == BuyResult.CapReached, "fourth: cap of 3 per quadrum (" + rs + ")");
            t.Check(DlcState.Current.PurchaseCount >= 3, "purchases recorded");

            // minor mercy heals a fresh small wound
            var mm = D("HH_Ind_MinorMercy"); var part = pawn.RaceProps.body.AllParts.First(x => x.def == BodyPartDefOf.Torso);
            var inj = HediffMaker.MakeHediff(HediffDefOf.Cut, pawn, part); inj.Severity = 4f; pawn.health.AddHediff(inj, part);
            t.Check(IndulgenceApi.CanBuy(pawn, mm, out _), "wound of 4 hp: minor mercy possible");
            int silverBefore = Kit.Silver(pawn); var priceMm = IndulgenceApi.PriceFor(pawn, mm);
            t.Check(IndulgenceApi.TryBuy(pawn, mm, out _) == BuyResult.Ok && !pawn.health.hediffSet.hediffs.Contains(inj), "minor mercy healed the wound");
            t.Check(silverBefore - Kit.Silver(pawn) == (int)Math.Ceiling(priceMm.Silver - 1e-9), "price paid: " + priceMm.ToStringShort());
            var big = HediffMaker.MakeHediff(HediffDefOf.Cut, pawn, part); big.Severity = 14f; pawn.health.AddHediff(big, part);
            t.Check(!IndulgenceApi.CanBuy(pawn, mm, out _, ignoreCaps: true), "a 14 hp wound is too big for minor mercy");
            pawn.health.RemoveHediff(big);

            // certainty
            var cert = D("HH_Ind_UnshakenFaith"); float c0 = pawn.ideo.Certainty;
            if (c0 < 0.85f) { t.Check(IndulgenceApi.TryBuy(pawn, cert, out _) == BuyResult.Ok && pawn.ideo.Certainty > c0, $"certificate raises certainty ({c0:0.00} -> {pawn.ideo.Certainty:0.00})"); }
            else t.Note("certainty already high, skipped");

            // stance rules
            stance(MoneyTheme.Indulgences, MoneyStance.Abhorrent); guilt(); int sx = Kit.Silver(pawn);
            t.Check(IndulgenceApi.TryBuy(pawn, abs, out _) == BuyResult.StanceForbids && Kit.Silver(pawn) == sx, "abhorrent: never buys, no money moved");
            stance(MoneyTheme.Indulgences, MoneyStance.NotAThing);
            t.Check(IndulgenceApi.TryBuy(pawn, abs, out _) == BuyResult.StanceForbids, "not a thing: nothing to buy");
            yield return 5;
            stance(MoneyTheme.Indulgences, MoneyStance.Disapproved);
            // wait out nothing: cap window uses ticks; use ignore by testing a fresh item
            var cheap = D("HH_Ind_UnshakenFaith");
            t.Check(IdeoStances.CanBuy(MoneyStance.Disapproved) && IndulgenceApi.PriceFor(pawn, cheap).WholeSilver >= 200, "disapproved: list price, buying allowed");
            stance(MoneyTheme.Indulgences, MoneyStance.Sacred);
            t.Check(IndulgenceApi.PriceFor(pawn, D("HH_Ind_ExpressAbsolution")).WholeSilver == (int)Math.Round(150 * 0.75 * Math.Pow(1.5, IndulgenceApi.RepeatsThisYear(pawn, abs)), MidpointRounding.AwayFromZero), "sacred: 25 % cheaper");
            // memories created by the stance comps
            pawn.needs.mood.thoughts.memories.Memories.RemoveAll(m => m.def.defName == "HH_PaidMyDues");
            Find.HistoryEventsManager.RecordEvent(new HistoryEvent(DlcDefs.EvBoughtIndulgence, pawn.Named(HistoryEventArgsNames.Doer)));
            t.Check(pawn.needs.mood.thoughts.memories.NumMemoriesOfDef(DlcDefs.Thought("HH_PaidMyDues")) == 1, "sacred: 'paid my dues' memory");
            stance(MoneyTheme.Indulgences, MoneyStance.Disapproved);
            Find.HistoryEventsManager.RecordEvent(new HistoryEvent(DlcDefs.EvBoughtIndulgence, pawn.Named(HistoryEventArgsNames.Doer)));
            t.Check(pawn.needs.mood.thoughts.memories.NumMemoriesOfDef(DlcDefs.Thought("HH_BoughtMyWayOut")) >= 1, "disapproved: 'bought my way out' memory");
            stance(MoneyTheme.Indulgences, MoneyStance.Accepted);

            // pay from colony silver when the pawn has none
            var poor = Kit.Colonist(map); stance(MoneyTheme.Ascension, MoneyStance.Accepted);
            poor.needs.mood.thoughts.memories.TryGainMemory(DefDatabase<ThoughtDef>.GetNamed("SleptOutside"), null, ideo.PreceptsListForReading.First());
            var stack = ThingMaker.MakeThing(ThingDefOf.Silver); stack.stackCount = 400; GenPlace.TryPlaceThing(stack, poor.Position, map, ThingPlaceMode.Near);
            int cs0 = ColonySilver.Count(map); var pc = IndulgenceApi.PriceFor(poor, D("HH_Ind_UnshakenFaith"));
            var bres = IndulgenceApi.TryBuy(poor, D("HH_Ind_UnshakenFaith"), out rs);
            if (poor.ideo.Certainty < 0.99f) t.Check(bres == BuyResult.Ok && cs0 - ColonySilver.Count(map) == 200, $"colony silver pays for a pawn without coins ({cs0 - ColonySilver.Count(map)}; {bres} {rs})");
            var broke = Kit.Colonist(map); DlcState.Current.SetRank(broke, 3);
            long before = l.SumAll(); int cs1 = ColonySilver.Count(map);
            var rk4 = D("HH_Asc_Rank4"); if (IndulgenceApi.PriceFor(broke, rk4).WholeSilver <= cs1) t.Note("colony stock is large enough; cannot-pay case skipped");
            else t.Check(IndulgenceApi.TryBuy(broke, rk4, out rs) == BuyResult.CannotPay && l.SumAll() == before && DlcState.Current.RankOf(broke) == 3, "cannot pay 10000: refused, nothing booked, rank unchanged (" + rs + ")");

            // paid ascension: ranks in order, mood and respect
            var asc = Kit.Colonist(map); Kit.GiveSilver(asc, 3000);
            t.Check(!IndulgenceApi.CanBuy(asc, D("HH_Asc_Rank2"), out why) && why != null, "rank II needs rank I first (" + why + ")");
            t.Check(IndulgenceApi.TryBuy(asc, D("HH_Asc_Rank1"), out rs) == BuyResult.Ok && DlcState.Current.RankOf(asc) == 1, "rank I bought");
            t.Check(!IndulgenceApi.CanBuy(asc, D("HH_Asc_Rank1"), out _), "rank I cannot be bought twice");
            DlcState.Current.SetRank(asc, 2); stance(MoneyTheme.Ascension, MoneyStance.Sacred);
            if (asc.Ideo != ideo) asc.ideo.SetIdeo(ideo);
            yield return 70;
            var th = new List<Thought>(); asc.needs.mood.thoughts.situational.AppendMoodThoughts(th);
            t.Check(th.Any(x => x.def.defName == "HH_AscensionMood" && x.CurStageIndex == 1 && x.MoodOffset() >= 1.9f), "sacred ascension: +2 mood at rank II");
            var low = Kit.Colonist(map); if (low.Ideo != ideo) low.ideo.SetIdeo(ideo);
            yield return 70;
            var ot = new List<ISocialThought>(); low.needs.mood.thoughts.GetSocialThoughts(asc, ot);
            t.Check(ot.Any(x => x.GetType().Name.Contains("Situational") && ((Thought)x).def.defName == "HH_AscensionRespect"), "sacred: lower ranks respect higher ranks (+5 opinion)");
            {   // sacred ascension: certainty gains 5 % faster for ranked pawns (same pawn, rank 0 vs rank 2)
                var ai = asc.ideo; DlcState.Current.SetRank(asc, 0); ai.OffsetCertainty(-0.4f); float a1 = ai.Certainty; ai.OffsetCertainty(0.1f); float g0 = ai.Certainty - a1;
                DlcState.Current.SetRank(asc, 2); ai.OffsetCertainty(-0.3f); float b1 = ai.Certainty; ai.OffsetCertainty(0.1f); float g1 = ai.Certainty - b1;
                t.Check(g0 > 0.001f && g1 / g0 > 1.03f && g1 / g0 < 1.07f, $"sacred ascension: certainty gain x1.05 ({g0:0.0000} -> {g1:0.0000})");
            }
            stance(MoneyTheme.Ascension, MoneyStance.Disapproved); yield return 70;
            ot.Clear(); low.needs.mood.thoughts.GetSocialThoughts(asc, ot);
            t.Check(ot.Any(x => ((Thought)x).def.defName == "HH_AscensionDisapproval"), "disapproved: co-believers think less of ranked pawns");
            stance(MoneyTheme.Ascension, MoneyStance.Accepted);

            // psychic ascension (Royalty)
            if (ModsConfig.RoyaltyActive)
            {
                var psy = Kit.Colonist(map); Kit.GiveSilver(psy, 4500); DlcState.Current.SetRank(psy, 2);
                t.Check(!IndulgenceApi.CanBuy(psy, D("HH_Asc_Psychic"), out why), "psychic ascension needs rank III (" + why + ")");
                DlcState.Current.SetRank(psy, 3); int lv0 = psy.GetPsylinkLevel();
                var pp = IndulgenceApi.PriceFor(psy, D("HH_Asc_Psychic"));
                t.Check(Math.Abs(pp.Silver - Math.Round(DefDatabase<ThingDef>.GetNamed("PsychicAmplifier").BaseMarketValue * 1.5)) < 1.5, "price = 1.5 x neuroformer market value (" + pp + ")");
                var r = IndulgenceApi.TryBuy(psy, D("HH_Asc_Psychic"), out rs);
                t.Check(r == BuyResult.Ok && psy.GetPsylinkLevel() == lv0 + 1, $"psylink +1 ({lv0} -> {psy.GetPsylinkLevel()}) {r} {rs}");
                t.Check(IndulgenceApi.CapReached(psy, D("HH_Asc_Psychic"), out _), "1 per pawn per year");
                Kit.Cleanup(psy);
            }
            else t.Check(DefDatabase<IndulgenceDef>.GetNamedSilentFail("HH_Asc_Psychic") == null, "no Royalty: no psychic ascension");

            // reform tithe: fluid only
            var tithe = D("HH_Ind_ReformTithe"); var fluid = ideo.Fluid && ideo.development != null;
            if (fluid && !ideo.development.CanReformNow) { int pts = ideo.development.points; var rr = IndulgenceApi.TryBuy(pawn, tithe, out rs); t.Check(rr == BuyResult.Ok && ideo.development.points == pts + 1, "reform tithe: +1 reform point"); }
            else { t.Check(!IndulgenceApi.CanBuy(pawn, tithe, out why) && why != null, "reform tithe refused: " + why); }

            // AI ideoligions: every ordinary ideo has a stance, memes weigh, rolls are repeatable
            DlcState.Current.Maintain();
            int checkedIdeos = 0;
            foreach (var i in Find.IdeoManager.IdeosListForReading)
            {
                if (Faction.OfPlayer.ideos.Has(i) || Find.FactionManager.AllFactionsListForReading.Any(f => f.def.fixedIdeo && f.ideos != null && f.ideos.Has(i))) continue;
                checkedIdeos++;
                t.Check(i.PreceptsListForReading.Any(p => p.def.issue == DlcDefs.IssueIndulgences) && i.PreceptsListForReading.Any(p => p.def.issue == DlcDefs.IssueAscension), "AI ideoligion has both stances: " + i.name);
            }
            t.Note("AI ideoligions checked: " + checkedIdeos);
            var any = Find.IdeoManager.IdeosListForReading.FirstOrDefault(i => !Faction.OfPlayer.ideos.Has(i));
            if (any != null) { var a = IdeoStances.Of(any, MoneyTheme.Indulgences); var b = DlcState.RollFor(any, MoneyTheme.Indulgences); var c = DlcState.RollFor(any, MoneyTheme.Indulgences); t.Check(b == c && IdeoStances.Of(any, MoneyTheme.Indulgences) == b, "roll is deterministic per ideoligion"); }

            // office comp is on vanilla altars
            var altarDef = DefDatabase<ThingDef>.GetNamedSilentFail("Altar_Small");
            t.Check(altarDef == null || altarDef.comps.Any(c => c is CompProperties_IndulgenceOffice), "vanilla altar carries the office comp");
            var ideogram = DefDatabase<ThingDef>.GetNamedSilentFail("Ideogram"); t.Check(ideogram == null || ideogram.comps.Any(c => c is CompProperties_IndulgenceOffice), "ideogram carries the office comp");
            Kit.Cleanup(pawn, poor, broke, asc, low);
        }
    }

    // ===================================================================================================================
    public sealed class DlcShrineSuite : HearthTestSuite
    {
        public override string Name => "dlc.shrine";
        public override int MaxTicks => 20000;
        public override IEnumerable<int> Run(TestContext t)
        {
            if (!ModsConfig.IdeologyActive) { t.Check(DefDatabase<ThingDef>.GetNamedSilentFail("HH_GuestAltar") == null && DefDatabase<JobDef>.GetNamedSilentFail("HH_PrayAtShrine") == null, "no Ideology: no shrine defs"); yield break; }
            var map = t.Map; var l = Ledger.Current; var own = Faction.OfPlayer.ideos.PrimaryIdeo;
            var other = Find.IdeoManager.IdeosListForReading.FirstOrDefault(i => i != own);
            if (other == null) { t.Note("only one ideoligion in this world: skipped"); yield break; }
            // a free ring around the altar, reachable from the colony: the first free cell near the centre can sit in a pocket of rock, trees or water on a random map
            var altarSize = DefDatabase<ThingDef>.GetNamed("HH_GuestAltar").size;
            var cell = TestSetup.FindOrMakeSpot(map, map.Center, altarSize.x, altarSize.z, 2);
            var altar = (Building_GuestAltar)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("HH_GuestAltar"), ThingDefOf.WoodLog);
            altar.SetFaction(Faction.OfPlayer); GenSpawn.Spawn(altar, cell, map); altar.Ideo = other;
            t.Check(altar.Spawned && ShrineApi.Altars(map).Contains(altar), "guest altar built and registered");
            t.Check(altar.TryGetComp<CompGuestShrine>() != null && altar.TryGetComp<CompIndulgenceOffice>() != null, "altar has shrine and office comps");
            t.Check(altar.LabelNoCount.Contains(other.name), "label shows the faith");
            double q = ShrineApi.Quality(altar); t.Check(q >= 0 && q <= 1, "quality in range: " + q.ToString("0.00"));
            t.Check(altar.GetGizmos().Count() >= 3, "gizmos: choose faith, buy indulgences, offerings (" + altar.GetGizmos().Count() + ")");
            t.Check(!altar.GetInspectString().NullOrEmpty() && altar.GetInspectString().Contains(other.name), "inspect string names the faith");
            t.Check(altar.TryGetComp<CompIndulgenceOffice>().CompInspectStringExtra() != null, "temple fund shown");

            var guest = Kit.Visitor(map, cell); var guest2 = Kit.Visitor(map, cell);
            if (guest == null) { t.Note("no visitor faction: skipped"); altar.Destroy(); yield break; }
            guest.ideo.SetIdeo(other); guest2.ideo.SetIdeo(other);
            var info = GuestApi.Info(guest); info.Budget = Money.FromSilver(5000);
            DlcState.SetStance(other, MoneyTheme.Indulgences, MoneyStance.Accepted);
            var act = new PrayActivity();
            t.Check(act.Desire(guest, info) > 0.5f, "guests of that faith want to pray here");
            t.Check(ShrineApi.FindAltarFor(guest) == altar, "closest reachable altar of their faith found (guest at " + guest.Position + ", altar at " + altar.Position + ", altars of the faith: "
                + ShrineApi.Altars(map).Count(a => a.Ideo == guest.Ideo) + ", reach " + guest.CanReach(altar, PathEndMode.InteractionCell, Danger.Some) + ", forbidden " + altar.IsForbidden(guest) + ")");
            var stranger = Kit.Visitor(map, cell); if (stranger != null) { if (stranger.Ideo == other) stranger.ideo.SetIdeo(own); t.Check(act.Desire(stranger, GuestApi.Info(stranger)) == 0f || stranger.Ideo == other, "other faiths do not come"); }

            float sat0 = info.Satisfaction; float temple0 = (float)IndulgenceApi.TempleBalance(other).Silver; float cert0 = guest.ideo.Certainty;
            var res = ShrineApi.Visit(guest, altar);
            t.Check(res.Prayed, "prayer happened");
            t.Check(info.Satisfaction - sat0 >= 0.039f, $"guest satisfaction rises ({sat0:0.000} -> {info.Satisfaction:0.000}, +{res.Satisfaction:0.000})");
            t.Check(guest.needs.mood.thoughts.memories.NumMemoriesOfDef(DlcDefs.Thought("HH_PlaceToPray")) == 1, "'a place to pray' memory (stage " + res.MoodStage + ")");
            t.Check(res.Donation.IsPositive && (float)IndulgenceApi.TempleBalance(other).Silver >= temple0 + (float)res.Donation.Silver - 0.01f, "donation lands in the temple fund (" + res.Donation + ")");
            t.Check(guest.ideo.Certainty >= cert0, "praying does not lower certainty");
            t.Check(l.SumAll() == 0, "ledger balanced");
            t.Check(act.Desire(guest, info) == 0f, "no second prayer right away (cooldown)");
            if (res.IndulgenceSale.IsPositive) t.Check(IndulgenceApi.TempleBalance(other) >= res.IndulgenceSale, "indulgence sale went to the temple: " + res.IndulgenceSale);
            else t.Note("no indulgence bought this time (random, needs a fitting item)");

            // the prayer job runs for real
            var job = act.TryGiveJob(guest2, GuestApi.Info(guest2));
            t.Check(job != null && job.def.defName == "HH_PrayAtShrine", "activity builds the prayer job");
            float g20 = GuestApi.Info(guest2).Satisfaction;
            guest2.jobs.StartJob(job, JobCondition.InterruptForced);
            yield return 1200;
            t.Check(GuestApi.Info(guest2).Satisfaction > g20 + 0.03f || guest2.needs.mood.thoughts.memories.NumMemoriesOfDef(DlcDefs.Thought("HH_PlaceToPray")) > 0, "walking to the altar and praying works as a job");

            // colony reactions to a foreign shrine
            var zealot = Kit.Colonist(map);
            var zi = Find.IdeoManager.IdeosListForReading.FirstOrDefault(i => i != other && i.memes.Any(m => m.defName == "Proselytizer" || m.defName == "Supremacist"));
            if (zi != null)
            {
                zealot.ideo.SetIdeo(zi); yield return 5;
                var worker = DlcDefs.Thought("HH_HereticShrine").Worker; var st = worker.CurrentState(zealot);
                t.Check(st.Active && st.StageIndex == 0, "proselytizer/supremacist: -1 'heretic shrine on our land' (stage " + st.StageIndex + ")");
                HSettings.Set("dlc.shrineReactions", 0); t.Check(!worker.CurrentState(zealot).Active, "reactions can be switched off"); HSettings.Set("dlc.shrineReactions", 1);
            }
            else t.Note("no proselytizer/supremacist ideoligion in this world: heretic reaction skipped");
            t.Check(ShrineApi.ForeignShrineCount(map, own) >= 1, "foreign shrine counted for diversity thoughts");

            // abhorrent colonists mind indulgences being sold on the map (the altar is an office)
            var abh = Kit.Colonist(map); var oi = abh.Ideo; var oldStance = IdeoStances.Of(oi, MoneyTheme.Indulgences);
            DlcState.SetStance(oi, MoneyTheme.Indulgences, MoneyStance.Abhorrent);
            yield return 270;
            var moods = new List<Thought>(); abh.needs.mood.thoughts.situational.AppendMoodThoughts(moods);
            t.Check(IndulgenceApi.OfficeOnMap(map) && moods.Any(x => x.def.defName == "HH_FaithForSale" && x.MoodOffset() <= -2.9f), "abhorrent: -3 while an indulgence office stands on the map");
            DlcState.SetStance(oi, MoneyTheme.Indulgences, oldStance);

            // demolition
            var pos = altar.Position; altar.Destroy(); yield return 2;
            t.Check(!ShrineApi.Altars(map).Any(a => a == altar), "removed altar leaves the registry");
            Kit.Cleanup(guest, guest2, stranger, zealot, abh);
        }
    }

    // ===================================================================================================================
    public sealed class DlcRemovalSuite : HearthTestSuite
    {
        public override string Name => "dlc.removal";
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var p = Kit.Colonist(map);
            Kit.Trait(p, DlcDefs.Lucky); Kit.Trait(p, DefDatabase<TraitDef>.GetNamed("HH_BornHost"));
            int vanilla = p.story.traits.allTraits.Count(x => !x.def.defName.StartsWith("HH_"));
            DlcState.Current.SetRank(p, 2);
            Ideo ideo = null;
            if (ModsConfig.IdeologyActive) { ideo = p.Ideo; DlcState.SetStance(ideo, MoneyTheme.Indulgences, MoneyStance.Sacred); DlcState.SetStance(ideo, MoneyTheme.Ascension, MoneyStance.Accepted); }
            var report = new List<string>();
            new DlcModule().PrepareRemoval(report);
            t.Check(!p.story.traits.allTraits.Any(x => x.def.defName.StartsWith("HH_")), "our traits are gone");
            t.Check(p.story.traits.allTraits.Count == vanilla, "vanilla traits stay");
            t.Check(DlcState.Current.RankOf(p) == 0 && DlcState.Current.RankCount == 0, "ranks dropped");
            if (ideo != null) t.Check(IdeoStances.Of(ideo, MoneyTheme.Indulgences) == MoneyStance.NotAThing && IdeoStances.Of(ideo, MoneyTheme.Ascension) == MoneyStance.NotAThing, "money precepts reset to Not a thing");
            t.Check(report.Count == 1 && report[0].StartsWith("DLC:"), "one report line: " + report.FirstOrDefault());
            Kit.Cleanup(p);
            yield break;
        }
    }

    // ===================================================================================================================
    public sealed class DlcNobleSuite : HearthTestSuite
    {
        public override string Name => "dlc.noble";
        public override int MaxTicks => 30000;
        public override IEnumerable<int> Run(TestContext t)
        {
            if (!ModsConfig.RoyaltyActive) { t.Check(DefDatabase<IncidentDef>.GetNamedSilentFail("HH_NobleStay") == null && !NobleStays.Active, "no Royalty: no noble stays"); yield break; }
            var map = t.Map; var l = Ledger.Current; var empire = Faction.OfEmpire; t.Check(empire != null, "Empire exists");
            var st = DlcState.Current.Nobles; st.Offers.Clear(); st.Stays.Clear();
            var reg = Hearth.Guests.GuestRegistry.Instance; t.Check(reg != null && Modules.On(ModuleId.Inn), "Inn module on with a guest register");
            Func<string, RoyalTitleDef> T = n => DefDatabase<RoyalTitleDef>.GetNamed(n);
            if (empire.HostileTo(Faction.OfPlayer) || empire.GoodwillWith(Faction.OfPlayer) < 0) Faction.OfPlayer.TryAffectGoodwillWith(empire, 100, false, false);
            var prof = HearthProfile.Current; float tier0 = prof.Tier; bool mand0 = HSettings.GetBool("dlc.mandatoryBilleting");
            var incNoble = DefDatabase<IncidentDef>.GetNamed("HH_NobleStay"); var incBillet = DefDatabase<IncidentDef>.GetNamed("HH_ImperialBilleting");
            var nw = (IncidentWorker_NobleStay)incNoble.Worker; var bw = (IncidentWorker_ImperialBilleting)incBillet.Worker;
            string why;

            // N08: no guest bed on the map -> no noble ever, even with colonist bedrooms that would meet a title
            foreach (var bd in map.listerBuildings.allBuildingsColonist.OfType<Building_Bed>()) Hearth.Guests.GuestBeds.Comp(bd)?.SetGuestBed(false, false);
            t.Check(Hearth.Guests.GuestBeds.CountOn(map) == 0 && !NobleDemands.GuestSetup(map), "test map starts without guest beds");
            t.Check(!NobleDemands.GuestBeds(map).Any(), "colonists' own beds never count as guest bedrooms");
            HSettings.Set("dlc.mandatoryBilleting", 1);
            t.Check(!nw.Eligible(StorytellerUtility.DefaultParmsNow(incNoble.category, map)), "N08: paid noble stay cannot fire without guest beds");
            t.Check(!bw.Eligible(StorytellerUtility.DefaultParmsNow(incBillet.category, map)), "N08: billeting cannot fire without guest beds, even with mandatory hospitality");
            t.Check(NobleStays.MakeOffer(map, false, out why) == null && NobleStays.MakeOffer(map, true, out why) == null && st.Offers.Count == 0, "N08: no offer without guest beds (" + why + ")");
            HSettings.Set("dlc.mandatoryBilleting", mand0 ? 1 : 0);

            // demands come from the title
            var dk = NobleDemands.For(T("Knight")); var db = NobleDemands.For(T("Baron")); var dc = NobleDemands.For(T("Count"));
            t.Check(dk.Seniority == T("Knight").seniority && db.Seniority > dk.Seniority && dc.Seniority > db.Seniority, "seniority rises Knight < Baron < Count");
            t.Check(dk.BedroomLabels.Count > 0 && db.BedroomLabels.Count >= dk.BedroomLabels.Count, "bedroom demands listed: " + string.Join("; ", db.BedroomLabels));
            t.Check(db.MinBedroomImpressiveness >= dk.MinBedroomImpressiveness && dc.MinBedroomImpressiveness >= db.MinBedroomImpressiveness, $"impressiveness demand grows ({dk.MinBedroomImpressiveness}/{db.MinBedroomImpressiveness}/{dc.MinBedroomImpressiveness})");
            t.Check(db.MinBedroomArea >= dk.MinBedroomArea, "area demand does not shrink");
            t.Check(dk.PricePerNobleDay < db.PricePerNobleDay && db.PricePerNobleDay < dc.PricePerNobleDay, "price per day rises with seniority");
            t.Check(Math.Abs(dk.PricePerNobleDay - HSettings.Get("dlc.nobleBasePrice") * (1 + 0.15 * dk.Seniority / 100.0)) < 1e-3, "price = base x (1 + 0.15 x seniority/100)");
            t.Check(NobleDemands.MaxTitleServedBy(null) == null, "no room: no title served");
            foreach (var (name, w) in NobleDemands.Offered) t.Check(DefDatabase<RoyalTitleDef>.GetNamedSilentFail(name) != null, "offered title exists: " + name);

            // two guest rooms: a large one that meets the (test-relaxed) knight demands and a small one that does not.
            // The real knight bedroom (impressiveness 40, double bed, end table, dresser ...) is too much to build by code, so the test swaps
            // the knight's requirements for "area 20" and puts them back at the end.
            var knight = T("Knight"); var savedReq = knight.bedroomRequirements;
            Hearth.Guests.InnTestKit.Site big = null, small = null;
            try { big = Hearth.Guests.InnTestKit.BuildRoom(map, 1, false, 6, 5); small = Hearth.Guests.InnTestKit.BuildRoom(map, 1, false, 3, 3); }
            catch (Exception e) { t.Check(false, "could not build the guest rooms: " + e.Message); }
            if (big == null || small == null) { knight.bedroomRequirements = savedReq; yield break; }
            knight.bedroomRequirements = new List<RoomRequirement> { new RoomRequirement_Area { area = 20 } };
            Hearth.Guests.InnTestKit.MarkGuestBeds(big, true); Hearth.Guests.InnTestKit.MarkGuestBeds(small, true);
            var bigBed = big.Beds[0]; var smallBed = small.Beds[0];
            t.Check(NobleDemands.GuestSetup(map) && NobleDemands.GuestBeds(map).Count() == 2, "two guest beds: the colony takes guests");
            t.Check(NobleDemands.BedFits(bigBed, knight) && !NobleDemands.BedFits(smallBed, knight), $"the big room fits a knight, the small one not ({big.Room?.CellCount} / {small.Room?.CellCount} cells)");
            bool anyRoom = NobleDemands.Offered.Any(o => { var td = DefDatabase<RoyalTitleDef>.GetNamedSilentFail(o.title); return td != null && NobleDemands.HasSuitableBedroom(map, td); });
            var pick = NobleDemands.PickTitle(map, true, out why);
            t.Check(anyRoom && pick != null, "title pick follows the guest bedrooms (" + (pick?.defName ?? why) + ")");
            t.Check(NobleDemands.PickTitle(map, false, out _) != null, "forced billeting needs no fitting room");
            bool canNoble = nw.Eligible(StorytellerUtility.DefaultParmsNow(incNoble.category, map)); t.Check(canNoble && !st.Offers.Any(), "with a fitting guest bedroom the paid stay may fire (" + canNoble + ")");

            // forced billeting gating
            prof.Tier = 3f; HSettings.Set("dlc.mandatoryBilleting", 0);
            t.Check(NobleStays.MakeOffer(map, true, out why) == null && !NobleStays.BilletingAllowed, "Standard Room: no forced billeting (" + why + ")");
            prof.Tier = 4f; t.Check(NobleStays.BilletingAllowed, "Overbooked: billeting allowed");
            prof.Tier = 3f; HSettings.Set("dlc.mandatoryBilleting", 1); t.Check(NobleStays.BilletingAllowed, "mandatory hospitality allows billeting on any tier");
            int gw0 = empire.GoodwillWith(Faction.OfPlayer);
            var forced = NobleStays.MakeOffer(map, true, out why);
            t.Check(forced != null && forced.Forced && forced.Days >= 3 && forced.Days <= 10, "billeting offer 3-10 days (" + (forced?.Days.ToString() ?? why) + ")");
            t.Check(NobleStays.MakeOffer(map, true, out why) == null, "only one party at a time");
            if (forced != null) t.Check(Find.LetterStack.LettersListForReading.OfType<ChoiceLetter_NobleOffer>().Any(x => x.OfferId == forced.Id && x.Choices.Count() == 3), "choice letter sent with accept / refuse / postpone");
            var holder = Kit.Colonist(map); holder.royalty.SetTitle(empire, T("Yeoman"), false, false, false); t.Check(holder.royalty.GetCurrentTitle(empire) != null, "colonist holds a title");
            int favor0 = holder.royalty.GetFavor(empire);
            if (forced != null) NobleStays.Decline(forced.Id);
            t.Check(empire.GoodwillWith(Faction.OfPlayer) <= gw0 - 20 || empire.GoodwillWith(Faction.OfPlayer) <= -75, $"refusing costs goodwill ({gw0} -> {empire.GoodwillWith(Faction.OfPlayer)})");
            t.Check(holder.royalty.GetFavor(empire) < favor0 || favor0 == 0, "refusing costs honor");
            Faction.OfPlayer.TryAffectGoodwillWith(empire, 100, false, false);
            prof.Tier = 3f; HSettings.Set("dlc.mandatoryBilleting", 0); t.Check(!bw.Eligible(StorytellerUtility.DefaultParmsNow(incBillet.category, map)), "billeting incident blocked on Standard Room");
            prof.Tier = 5f; t.Check(bw.Eligible(StorytellerUtility.DefaultParmsNow(incBillet.category, map)), "billeting incident allowed on One-Star Review with guest beds");
            prof.Tier = tier0; HSettings.Set("dlc.mandatoryBilleting", mand0 ? 1 : 0);
            Find.LetterStack.LettersListForReading.OfType<ChoiceLetter_NobleOffer>().ToList().ForEach(x => Find.LetterStack.RemoveLetter(x));

            // a stay: noble + retainer arrive as registered Inn guests and get the title-fitting guest bed
            if (empire.HostileTo(Faction.OfPlayer)) Faction.OfPlayer.TryAffectGoodwillWith(empire, 100, false, false);
            var stay = NobleStays.StartStay(map, new NobleOffer { Id = 901, MapId = map.uniqueID, Title = "Knight", Nobles = 1, Guards = 1, Days = 3 });
            t.Check(stay != null && stay.Pawns.Count == 2, "party arrived (noble + 1 guard)");
            if (stay == null) { knight.bedroomRequirements = savedReq; yield break; }
            for (int w = 0; w < 1500 && stay.Pawns.Any(x => !x.Spawned); w += 60) yield return 60;    // a drop pod may bring them
            var noble = stay.Pawns[0]; var guard = stay.Pawns[1]; var title = noble.royalty?.GetCurrentTitle(empire);
            t.Check(noble.Spawned && guard.Spawned, "the party is on the map");
            t.Check(title == knight, "the noble holds the Knight title (" + title?.defName + ")");
            var rn = reg.Get(noble); var rg = reg.Get(guard);
            t.Check(rn != null && rg != null, "noble and retainer are registered Inn guests (guest files)");
            t.Check(noble.GetLord()?.LordJob is Hearth.Guests.LordJob_HH_GuestStay, "the party has the Inn's guest lord job, not a vanilla visit (" + noble.GetLord()?.LordJob?.GetType().Name + ")");
            t.Check(GuestApi.IsGuest(noble) && GuestApi.Info(noble) != null, "noble counts as a guest");
            if (rn != null && !rn.CheckedIn) reg.CheckIn(noble, null);
            if (rg != null && !rg.CheckedIn) reg.CheckIn(guard, null);
            t.Check(rn != null && rn.Bed == bigBed, "the noble booked the guest bed that meets the title (" + rn?.Bed + ")");
            t.Check(rn != null && rn.NightPrice.Micro == 0, "the party's nights are free: the stay is paid at the end");
            t.Check(rg != null && rg.Bed == smallBed, "the retainer takes the other guest bed (" + rg?.Bed + ")");
            t.Check(RestUtility.IsValidBedFor(bigBed, noble, noble, false, false, false, GuestStatus.Guest) || RestUtility.FindBedFor(noble) == bigBed, "the noble may sleep in the booked guest bed");
            t.Check(RestUtility.FindBedFor(noble) == bigBed, "the noble's bed search finds the booked guest bed");
            // the room is judged: fits -> satisfaction rises; beneath the title -> falls
            float s0 = GuestApi.Info(noble).Satisfaction; NobleStays.CheckRooms(stay); float s1 = GuestApi.Info(noble).Satisfaction;
            t.Check(s1 > s0, $"a room that meets the title pleases the noble ({s0:0.000} -> {s1:0.000})");
            knight.bedroomRequirements = new List<RoomRequirement> { new RoomRequirement_Area { area = 200 } };
            NobleStays.CheckRooms(stay); float s2 = GuestApi.Info(noble).Satisfaction;
            t.Check(s2 < s1, $"a room beneath the title offends ({s1:0.000} -> {s2:0.000})");
            knight.bedroomRequirements = new List<RoomRequirement> { new RoomRequirement_Area { area = 20 } };
            double sat = NobleStays.AverageSatisfaction(stay); t.Check(Math.Abs(sat - GuestApi.Info(noble).Satisfaction) < 1e-5 && Math.Abs(stay.LastSatisfaction - sat) < 1e-5, "satisfaction read from the noble's guest file and remembered");
            // the room check runs from the rare tick every 6 hours
            float s3 = GuestApi.Info(noble).Satisfaction; DlcState.Current.RareTick(Find.TickManager.TicksGame / NobleStays.RoomCheckInterval * NobleStays.RoomCheckInterval + 7);
            t.Check(GuestApi.Info(noble).Satisfaction > s3, "the 6-hour tick judges the room");

            string till = Payments.ColonyTill(map); Func<double> basePDf = () => HSettings.Get("dlc.nobleBasePrice") * HSettings.Get("dlc.nobleMoneyFactor"); double basePD = basePDf();
            float t0 = (float)l.Balance(till).Silver; int fav1 = holder.royalty.GetFavor(empire);
            var pay = NobleStays.Settle(stay, 1.0);
            double expect = Math.Round(DlcMath.NoblePayment(basePD, 1, 3, stay.Seniority, 1.0));
            t.Check(Math.Abs(pay.Silver - expect) < 0.01 && Math.Abs((float)l.Balance(till).Silver - t0 - (float)expect) < 0.05f, $"paid {pay} silver = base x title x satisfaction ({expect})");
            t.Check(stay.Settled && Math.Abs(DlcMath.NoblePayment(basePD, 1, 3, 300, 1.0) / DlcMath.NoblePayment(basePD, 1, 3, 300, 0) - 1.25) < 1e-6, "satisfied guests pay x1.25");
            t.Check(NobleStays.Settle(stay, 1.0).Micro == 0, "settled once only");
            t.Check(l.SumAll() == 0, "ledger balanced");
            // once the guest file is gone, the remembered satisfaction counts
            var memo = new NobleStay { Nobles = 1, LastSatisfaction = 0.4f, Pawns = new List<Pawn> { null } };
            t.Check(Math.Abs(NobleStays.AverageSatisfaction(memo) - 0.4) < 1e-6, "without a guest file the last known satisfaction is used");

            // unhappy noble pays less and honor stays
            var stay2 = NobleStays.StartStay(map, new NobleOffer { Id = 902, MapId = map.uniqueID, Title = "Baron", Nobles = 1, Guards = 0, Days = 2 });
            t.Check(stay2 != null, "second party arrived");
            if (stay2 != null)
            {
                int fav2 = holder.royalty.GetFavor(empire); var pay2 = NobleStays.Settle(stay2, -1.0);
                t.Check(Math.Abs(pay2.Silver - Math.Round(DlcMath.NoblePayment(basePD, 1, 2, stay2.Seniority, -1.0))) < 0.01, "unhappy baron pays x0.5: " + pay2);
                t.Check(holder.royalty.GetFavor(empire) == fav2, "no honor for an unhappy stay");
            }

            // billeting settlement: compensation by tier, goodwill penalty for bad lodging
            prof.Tier = 4f; basePD = basePDf();
            var stay3 = NobleStays.StartStay(map, new NobleOffer { Id = 903, MapId = map.uniqueID, Title = "Knight", Nobles = 1, Guards = 2, Days = 4, Forced = true });
            t.Check(stay3 != null, "billeted party arrived");
            if (stay3 != null)
            {
                int g0 = empire.GoodwillWith(Faction.OfPlayer);
                var pay3 = NobleStays.Settle(stay3, -1.0);
                double comp = Math.Round(DlcMath.NoblePayment(basePD, 1, 4, stay3.Seniority, 0.0) * 0.25);
                t.Check(Math.Abs(pay3.Silver - comp) < 0.01, "forced billeting at Overbooked: 25 % expenses (" + pay3 + ")");
                t.Check(empire.GoodwillWith(Faction.OfPlayer) <= g0 - 5 || empire.GoodwillWith(Faction.OfPlayer) <= -75, "badly lodged billeted guests cost goodwill");
            }
            prof.Tier = tier0;
            knight.bedroomRequirements = savedReq;
            st.Stays.Clear(); st.Offers.Clear();
            Hearth.Guests.InnTestKit.RemoveAllGuests(map);
            Hearth.Guests.InnTestKit.Teardown(big, map); Hearth.Guests.InnTestKit.Teardown(small, map);
            Faction.OfPlayer.TryAffectGoodwillWith(empire, 100, false, false);
            Kit.Cleanup(holder);
            yield break;
        }
    }
}
#endif
