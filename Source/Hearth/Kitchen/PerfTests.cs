// SPDX-License-Identifier: MIT
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Hearth.Testing;
using Hearth.Dlc;

namespace Hearth.Kitchen.Tests
{
    /// <summary>
    /// perf.hotpaths (explicit only): per-call cost of the hottest Harmony patches of the module family with a few hundred pawns, things and bills.
    /// The patch bodies are called directly through delegates, so the numbers are the cost we add to a vanilla call, nothing else.
    /// Limit: a few microseconds per call (checked as 5 us; the typical value is tens of nanoseconds).
    /// </summary>
    public sealed class PerfHotPathsSuite : HearthTestSuite
    {
        public override string Name => "perf.hotpaths";
        public override bool ExplicitOnly => true;
        public override int MaxTicks => 20000;

        delegate void RefFloat<T>(T inst, ref float x);
        delegate void RefBool<T>(T inst, ref bool x);
        delegate void RefString<T>(T inst, ref string x);
        delegate bool DamagePrefix(Pawn p, ref DamageInfo d, out bool absorbed);
        delegate void OutFloat<T>(T inst, out float state);
        delegate void ChatPost(Pawn_InteractionsTracker tr, bool result, Pawn recipient);
        delegate void NeedRestPost(Need_Rest n, float state);

        const double LimitNs = 5000;
        static MethodInfo M(Type t, string name) => AccessTools.Method(t, name) ?? throw new Exception("no method " + t.Name + "." + name);
        static TDel D<TDel>(Type t, string name) where TDel : Delegate => (TDel)Delegate.CreateDelegate(typeof(TDel), M(t, name));

        TestContext ctx;
        /// <summary>Average nanoseconds per call of <paramref name="call"/> over <paramref name="n"/> calls (index i is passed through).</summary>
        double Measure(string label, int n, Action<int> call, double limitNs = LimitNs)
        {
            for (int i = 0; i < Math.Min(n, 2000); i++) call(i);   // warm up (JIT)
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < n; i++) call(i);
            sw.Stop();
            double ns = sw.Elapsed.TotalMilliseconds * 1e6 / n;
            ctx.Note($"{label}: {ns:0.0} ns/call ({n} calls)");
            ctx.Check(ns < limitNs, $"{label} below {limitNs / 1000.0:0.#} us per call ({ns:0.0} ns)");
            return ns;
        }

        public override IEnumerable<int> Run(TestContext t)
        {
            ctx = t; var map = t.Map;
            const int Humans = 150, Animals = 150, N = 200000;
            var pawns = new List<Pawn>();
            var dishRecipe0 = DefDatabase<RecipeDef>.AllDefs.First(r => DishRecipeExtension.Of(r) != null);
            var wellFed = DefDatabase<HediffDef>.GetNamedSilentFail("HH_WellFed");
            for (int i = 0; i < Humans; i++)
            {
                var p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, PawnGenerationContext.NonPlayer, -1, forceGenerateNewPawn: true));
                if (i % 3 == 0 && wellFed != null) p.health.AddHediff(wellFed);
                pawns.Add(p);
            }
            var animalKind = DefDatabase<PawnKindDef>.GetNamed("Muffalo");
            for (int i = 0; i < Animals; i++) pawns.Add(PawnGenerator.GeneratePawn(new PawnGenerationRequest(animalKind, Faction.OfPlayer, PawnGenerationContext.NonPlayer, -1, forceGenerateNewPawn: true)));
            t.Note($"{pawns.Count} pawns generated");
            yield return 1;

            Measure("Modules.On(ShopsFood) (reference)", N, i => Modules.On(ModuleId.ShopsFood));

            Measure("DishRecipeExtension.Of (dish recipe)", N, i => DishRecipeExtension.Of(dishRecipe0));
            // Kitchen: well fed (every pawn, every 150 ticks), recipe availability and bill scan (every colonist, every bill scan)
            var wf = D<RefFloat<Need_Food>>(typeof(Patch_WellFed), "Postfix");
            Measure("Patch_WellFed.Postfix", N, i => { float r = 0.02f; wf(pawns[i % pawns.Count].needs.food, ref r); });
            var dishRecipe = DefDatabase<RecipeDef>.AllDefs.First(r => DishRecipeExtension.Of(r) != null);
            var vanillaRecipe = DefDatabase<RecipeDef>.AllDefs.First(r => r.defName.StartsWith("Make_") && r.modExtensions == null);
            var modExtRecipe = DefDatabase<RecipeDef>.AllDefs.FirstOrDefault(r => r.modExtensions != null && DishRecipeExtension.Of(r) == null);
            var avail = D<RefBool<RecipeDef>>(typeof(Patch_RecipeAvailableNow), "Postfix");
            Measure("Patch_RecipeAvailableNow.Postfix (vanilla recipe)", N, i => { bool r = true; avail(vanillaRecipe, ref r); });
            if (modExtRecipe != null) Measure("Patch_RecipeAvailableNow.Postfix (foreign mod extension)", N, i => { bool r = true; avail(modExtRecipe, ref r); });
            Measure("Patch_RecipeAvailableNow.Postfix (dish recipe)", N, i => { bool r = true; avail(dishRecipe, ref r); });

            var shouldDo = D<RefBool<Bill_Production>>(typeof(Patch_BillShouldDoNow), "Postfix");
            var vanillaBill = (Bill_Production)vanillaRecipe.MakeNewBill();
            var dishBill = (Bill_Production)dishRecipe.MakeNewBill();
            Measure("Patch_BillShouldDoNow.Postfix (vanilla bill)", N, i => { bool r = true; shouldDo(vanillaBill, ref r); });
            Measure("Patch_BillShouldDoNow.Postfix (dish bill, no table)", N, i => { bool r = true; shouldDo(dishBill, ref r); });
            // a dish bill on a spawned work table: the real station check (cached per table)
            var area = t.FreeCellNearCenter(30);
            KT.Clear(map, area, 2);
            var mains = Stations.MainDefs(DishRecipeExtension.Of(dishRecipe).dish.MainStation);
            Thing table = null;
            if (mains.Count > 0) { table = KT.Spawn(map, mains[0], area); }
            if (table is IBillGiver giver)
            {
                var onTable = (Bill_Production)dishRecipe.MakeNewBill(); giver.BillStack.AddBill(onTable);
                Measure("Patch_BillShouldDoNow.Postfix (dish bill on a table)", N / 10, i => { bool r = true; shouldDo(onTable, ref r); }, 20000);
                var status = D<RefString<Bill_Production>>(typeof(Patch_BillStatusString), "Postfix");
                Measure("Patch_BillStatusString.Postfix (dish bill on a table)", N / 10, i => { string r = null; status(onTable, ref r); }, 20000);
                giver.BillStack.Delete(onTable);
            }
            else t.Note("no work table for the dish recipe; table cases skipped");
            if (table != null) table.Destroy();

            // Hosting / serving / chat
            var learn = D<RefFloat<SkillRecord>>(typeof(Patch_HostingLearn), "Prefix");
            Measure("Patch_HostingLearn.Prefix (social xp, no job)", N, i => { float xp = 5f; learn(pawns[i % Humans].skills.GetSkill(SkillDefOf.Social), ref xp); });
            Measure("Patch_HostingLearn.Prefix (other skill)", N, i => { float xp = 5f; learn(pawns[i % Humans].skills.GetSkill(SkillDefOf.Melee), ref xp); });
            var restPre = D<OutFloat<Need_Rest>>(typeof(Patch_ServeRest), "Prefix");
            var restPost = D<NeedRestPost>(typeof(Patch_ServeRest), "Postfix");
            Measure("Patch_ServeRest.Prefix+Postfix (not serving)", N, i => { var n = pawns[i % Humans].needs.rest; restPre(n, out float s); restPost(n, s); });
            var chat = D<ChatPost>(typeof(Patch_ServiceQualityChat), "Postfix");
            var trackers = pawns.Take(Humans).Select(p => p.interactions ?? new Pawn_InteractionsTracker(p)).ToList();
            Measure("Patch_ServiceQualityChat.Postfix", N, i => chat(trackers[i % Humans], true, pawns[(i + 1) % Humans]));
            var trait = D<RefFloat<TraitDef>>(typeof(Patch_TraitCommonality), "Postfix");
            var someTrait = DefDatabase<TraitDef>.AllDefs.First(d => !d.defName.StartsWith("HH_"));
            Measure("Patch_TraitCommonality.Postfix (foreign trait)", N, i => { float r = 1f; trait(someTrait, ref r); });

            // Underworld: damage guard on every pawn damage, no bout running and one running
            var guard = D<DamagePrefix>(typeof(Hearth.Underworld.Patch_PitDamageGuard), "Prefix");
            var dinfo = new DamageInfo(DamageDefOf.Cut, 5f);
            Measure("Patch_PitDamageGuard.Prefix (no bout)", N, i => { var d = dinfo; guard(pawns[i % pawns.Count], ref d, out _); });

            foreach (var p in pawns) if (p.Discarded == false) Find.WorldPawns.PassToWorld(p, RimWorld.Planet.PawnDiscardDecideMode.Discard);
            yield break;
        }
    }
}
#endif
