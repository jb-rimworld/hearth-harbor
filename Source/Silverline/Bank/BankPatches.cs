// SPDX-License-Identifier: MIT
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Hearth;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Silverline
{
    /// <summary>"Call Silverline" in the comms console menu (8.2). A communicable target, so the vanilla job and the vanilla menu handle everything.</summary>
    public sealed class SilverlineComm : ICommunicable
    {
        public static readonly SilverlineComm Instance = new SilverlineComm();
        public string GetCallLabel() => L.T("Comm.Call", "Call Silverline");
        public string GetInfoText() => L.T("Comm.Info", "Banking, loans and the occasional dry remark.");
        public Faction GetFaction() => null;
        public void TryOpenComms(Pawn negotiator) => Find.WindowStack.Add(new Window_Bank(negotiator?.Map ?? BankGameComp.DefaultMap, BankSite.Remote, null));
        public FloatMenuOption CommFloatMenuOption(Building_CommsConsole console, Pawn negotiator)
        {
            string label = GetCallLabel();
            return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(label, () => console.GiveUseCommsJob(negotiator, this), MenuOptionPriority.InitiateSocial), negotiator, console);
        }
    }

    [HarmonyPatch(typeof(Building_CommsConsole), nameof(Building_CommsConsole.GetCommTargets))]
    public static class Patch_CommsConsole_Targets
    {
        static void Postfix(ref IEnumerable<ICommunicable> __result)
        {
            if (!Modules.On(ModuleId.Bank) || BankApi.Impl == null) return;
            __result = __result.Concat(new ICommunicable[] { SilverlineComm.Instance });
        }
    }

    /// <summary>The bank balance counts for the storyteller's colony wealth (D22, 8.10): the share is spread over the player maps by map wealth,
    /// so a balance never counts twice with several settlements.</summary>
    [HarmonyPatch(typeof(Map), nameof(Map.PlayerWealthForStoryteller), MethodType.Getter)]
    public static class Patch_PlayerWealth
    {
        [System.ThreadStatic] static bool inside;
        static int cacheTick = -1; static double cacheTotal;

        static void Postfix(Map __instance, ref float __result)
        {
            if (inside) return;
            if (!Modules.On(ModuleId.Bank)) return;
            var c = BankGameComp.Current;
            if (c == null || !c.HasAccount) return;
            float share = HSettings.Get("bank.wealthShare");
            if (share <= 0f || !__instance.TreatAsPlayerHomeForThreatPoints) return;
            if (Find.Storyteller?.difficulty != null && Find.Storyteller.difficulty.fixedWealthMode) return;
            double balance = c.Balance.Silver + c.InTransit.Silver;
            if (balance <= 0) return;
            __result += (float)(balance * share * ShareOf(__instance, __result));
        }

        /// <summary>Fraction of the account that this map carries: its wealth over the wealth of all player home maps.</summary>
        static double ShareOf(Map map, float own)
        {
            var homes = Find.Maps.Where(m => m.TreatAsPlayerHomeForThreatPoints).ToList();
            if (homes.Count <= 1) return 1.0;
            double total = 0;
            inside = true;
            try { foreach (var m in homes) total += m == map ? own : m.PlayerWealthForStoryteller; }
            finally { inside = false; }
            if (total <= 0.0001) return 1.0 / homes.Count;
            return own / total;
        }
    }
}
