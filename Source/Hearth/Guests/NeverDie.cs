// SPDX-License-Identifier: MIT
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Hearth.Guests
{
    /// <summary>
    /// Special switch core.neverDie: a guest whose injuries or blood loss would be lethal stays alive instead (downed). Only guests are touched,
    /// never colonists, prisoners or hostile pawns; the check is a field read plus a dictionary lookup, and it only runs when vanilla says "dead".
    /// </summary>
    [HarmonyPatch]
    static class Patch_HealthTracker_ShouldBeDead
    {
        public const string Switch = "core.neverDie";
        static readonly AccessTools.FieldRef<Pawn_HealthTracker, Pawn> pawnOf = AccessTools.FieldRefAccess<Pawn_HealthTracker, Pawn>("pawn");

        static MethodBase TargetMethod() => AccessTools.Method(typeof(Pawn_HealthTracker), "ShouldBeDead");

        /// <summary>True when the pawn is a guest that the switch protects.</summary>
        public static bool Protects(Pawn p)
        {
            if (p == null || p.Dead || !CoreSettings.Switch(Switch) || !Modules.On(ModuleId.Inn) || p.IsPrisoner || p.IsColonist) return false;
            if (p.Faction == null || p.Faction.IsPlayer || p.HostileTo(Faction.OfPlayer)) return false;
            // A vanilla visitor stops counting as a guest once it is downed (it leaves its lord); the ones we saw as guests stay protected (saved, see NeverDieSet).
            if (GuestApi.IsGuest(p)) { NeverDieSet.Instance?.Add(p); return true; }
            return NeverDieSet.Instance != null && NeverDieSet.Instance.Contains(p);
        }

        static void Postfix(Pawn_HealthTracker __instance, ref bool __result)
        {
            if (!__result) return;
            if (Protects(pawnOf(__instance))) __result = false;
        }
    }

    /// <summary>The same protection for the direct kill path (vanilla kills a pawn from several places, not only from the health tracker).</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    static class Patch_Pawn_Kill_NeverDie
    {
        static bool Prefix(Pawn __instance) => !Patch_HealthTracker_ShouldBeDead.Protects(__instance);
    }
}

namespace Hearth.Guests
{
    /// <summary>The guests the special switch core.neverDie has seen, saved with the game and pruned (dead, gone or discarded pawns) every 2500 ticks.</summary>
    public sealed class NeverDieSet : GameComponent
    {
        public static NeverDieSet Instance;
        System.Collections.Generic.List<Pawn> pawns = new System.Collections.Generic.List<Pawn>();
        public NeverDieSet(Game game) { Instance = this; }

        public int Count => pawns.Count;
        public bool Contains(Pawn p) => pawns.Contains(p);
        public void Add(Pawn p) { if (p != null && !pawns.Contains(p)) pawns.Add(p); }

        public void Prune()
        {
            pawns.RemoveAll(p => p == null || p.Destroyed || p.Dead || p.Discarded || p.MapHeld == null || p.IsColonist || p.IsPrisoner);
        }

        public override void GameComponentTick()
        {
            if (pawns.Count > 0 && Find.TickManager.TicksGame % 2500 == 0) Prune();
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref pawns, "neverDie", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) { if (pawns == null) pawns = new System.Collections.Generic.List<Pawn>(); Prune(); }
        }
    }

    /// <summary>Hooks the always-on payment top-up of core.infiniteMoney into Payments.</summary>
    [StaticConstructorOnStartup]
    static class InfiniteMoneyHook
    {
        static InfiniteMoneyHook() { Payments.BeforeCharge = GuestApi.EnsureFunds; }
    }
}
