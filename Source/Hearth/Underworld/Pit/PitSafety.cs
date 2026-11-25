// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Hearth.Underworld
{
    /// <summary>
    /// The damage guard of the fight pit (moved here from UwDefs in 1.2.0). Only a hit between the two fighters of one active bout is touched;
    /// every other hit (and a module that is off) returns at once.
    /// <para>Scored bouts (any free humanlike: guest, prizefighter, colonist, slave): the hit is written to the bout's score and absorbed, and
    /// PreApplyDamage is skipped. Thing.TakeDamage then returns before the damage worker runs, so nothing of the 1.7 damage chain happens:
    /// no injury, no downing (no dropped gear, no lost lord), no guilt (Pawn_HealthTracker.PreApplyDamage), no lord signal, no goodwill
    /// (Faction.Notify_MemberTookDamage), no HarmedMe memory, no pain and no panic flee. What stays is cosmetic: sound, battle log, stagger, melee XP.
    /// The guard runs last (Priority.Last) so armour/shield prefixes of other mods have already shaped the amount that is scored.</para>
    /// <para>Real bouts (prisoners and animals among themselves): as before 1.2.0 a beaten fighter of a non-lethal bout takes no more damage, and
    /// a heavy blow close to the yield line is softened. <see cref="Patch_PitFactionDamage"/> keeps goodwill out of it.</para>
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PreApplyDamage))]
    public static class Patch_PitDamageGuard
    {
        [HarmonyPriority(Priority.Last)]
        public static bool Prefix(Pawn __instance, ref DamageInfo dinfo, ref bool absorbed)
        {
            // A running bout is itself proof that the module was on: the guard stays in force until the bout is gone (switching the module off
            // aborts every bout through ModuleGate.Changed, see PitSetup), so a hit in between can never land for real.
            if (FightBout.ActiveCount == 0) return true;
            var b = FightBout.OfPawn(__instance);
            if (b == null) return true;
            if (b.Scored)
            {
                if (!b.IsPair(__instance, dinfo.Instigator)) return true;
                b.Score(__instance, dinfo.Amount);
                absorbed = true;
                return false;
            }
            if (b.ShouldAbsorb(__instance)) { dinfo.SetAmount(0f); return true; }
            // non-lethal: a heavy blow on a fighter close to the yield line (a muffalo's kick) must not kill before the bout referee notices; soften it there
            if (b.NonLethal && b.IsNearYield(__instance)) dinfo.SetAmount(dinfo.Amount * 0.3f);
            return true;
        }
    }

    /// <summary>Safety net for real-damage bouts (a prisoner of a non-hostile faction): a hit between the two fighters of a bout never costs goodwill
    /// and never turns a faction hostile (factionHostileOnHarmByPlayer). Scored bouts never get here.</summary>
    [HarmonyPatch(typeof(Faction), nameof(Faction.Notify_MemberTookDamage))]
    public static class Patch_PitFactionDamage
    {
        public static bool Prefix(Pawn member, DamageInfo dinfo)
        {
            if (FightBout.ActiveCount == 0) return true;   // as the damage guard: only a running bout counts
            var b = FightBout.OfPawn(member);
            return b == null || !b.IsPair(member, dinfo.Instigator);
        }
    }

    /// <summary>A prisoner in the arena (a fenced arena reaches the outdoors through fence regions) or one being walked back does not try to escape.</summary>
    [HarmonyPatch(typeof(JobGiver_PrisonerEscape), "TryGiveJob")]
    public static class Patch_PitPrisonerEscape
    {
        public static bool Prefix(Pawn pawn, ref Job __result)
        {
            if (!Modules.On(ModuleId.Underworld)) return true;
            if (FightBout.ActiveCount == 0 && !PitOpponents.AnyReturning) return true;
            if (FightBout.OfPawn(pawn) == null && !PitOpponents.IsReturning(pawn)) return true;
            __result = null;
            return false;
        }
    }

    /// <summary>A hired pit beast is not the colony's: it cannot be designated for slaughter or released.</summary>
    [HarmonyPatch]
    public static class Patch_PitBeastDesignators
    {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            foreach (var n in new[] { "RimWorld.Designator_Slaughter", "RimWorld.Designator_ReleaseAnimalToWild" })
            {
                var t = AccessTools.TypeByName(n);
                var m = t == null ? null : AccessTools.Method(t, "CanDesignateThing", new[] { typeof(Thing) });
                if (m != null) yield return m;
            }
        }

        public static void Postfix(Thing t, ref AcceptanceReport __result)
        {
            if (!__result.Accepted || !(t is Pawn p) || !PitOpponents.IsBeast(p)) return;
            __result = new AcceptanceReport(UwPay.T("Pit.BeastNotYours"));
        }
    }

    /// <summary>A hired pit beast is never offered to traders.</summary>
    [HarmonyPatch(typeof(TradeUtility), nameof(TradeUtility.AllSellableColonyPawns))]
    public static class Patch_PitBeastNotSellable
    {
        public static void Postfix(ref IEnumerable<Pawn> __result)
        {
            if (!PitOpponents.AnyBeast) return;
            __result = Filter(__result);
        }

        static IEnumerable<Pawn> Filter(IEnumerable<Pawn> src)
        {
            foreach (var p in src) if (!PitOpponents.IsBeast(p)) yield return p;
        }
    }

    /// <summary>Job drivers of the pit: vanilla behaviour plus one fail condition, so a job of ours that outlives its bout (a save loaded in the
    /// middle of a bout: bouts are not saved) ends on its very first tick instead of attacking for real.</summary>
    public class JobDriver_PitFight : JobDriver_AttackMelee
    {
        /// <summary>No attack-target reservation: nobody else needs to be kept off the opponent, and a reservation saved with the game outlives the
        /// job's vanilla stand-in when the mod is removed ("Attack target reservation manager failed to clean up").</summary>
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => !FightBout.Holds(pawn, job.targetA.Thing as Pawn));
            foreach (var t in base.MakeNewToils()) yield return t;
        }
    }

    public class JobDriver_PitGoto : JobDriver_Goto
    {
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => FightBout.OfPawn(pawn) == null);
            foreach (var t in base.MakeNewToils()) yield return t;
        }
    }

    public class JobDriver_PitWait : JobDriver_Wait
    {
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => FightBout.OfPawn(pawn) == null);
            foreach (var t in base.MakeNewToils()) yield return t;
        }
    }
}
