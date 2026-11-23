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
    /// <summary>
    /// Base of every entry Silverline and Clawback Mutual add to the comms console menu. A communicable target, so the vanilla menu lists it
    /// and the vanilla job (UseCommsConsole) sends the colonist to the console first; the window opens when the colonist is there (K-rule: no
    /// window from across the map). There is deliberately no patch on GetFloatMenuOptions any more: the one GetCommTargets patch below adds all of them.
    /// </summary>
    public abstract class ConsoleComm : ICommunicable
    {
        public abstract string GetCallLabel();
        public abstract string GetInfoText();
        public Faction GetFaction() => null;
        public abstract void TryOpenComms(Pawn negotiator);
        public FloatMenuOption CommFloatMenuOption(Building_CommsConsole console, Pawn negotiator)
        {
            return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(GetCallLabel(), () => console.GiveUseCommsJob(negotiator, this), MenuOptionPriority.InitiateSocial), negotiator, console);
        }
    }

    /// <summary>"Call Silverline" in the comms console menu (8.2).</summary>
    public sealed class SilverlineComm : ConsoleComm
    {
        public static readonly SilverlineComm Instance = new SilverlineComm();
        public override string GetCallLabel() => L.T("Comm.Call", "Call Silverline");
        public override string GetInfoText() => L.T("Comm.Info", "Banking, loans and the occasional dry remark.");
        public override void TryOpenComms(Pawn negotiator) => Find.WindowStack.Add(new Window_Bank(negotiator?.Map ?? BankGameComp.DefaultMap, BankSite.Remote, null));
    }

    /// <summary>"Call Clawback Mutual" (insurance desk).</summary>
    public sealed class ClawbackComm : ConsoleComm
    {
        public static readonly ClawbackComm Instance = new ClawbackComm();
        public override string GetCallLabel() => Ins.T("Menu.Call");
        public override string GetInfoText() => Ins.T("Menu.Call");
        public override void TryOpenComms(Pawn negotiator) => Window_Clawback.Open();
    }

    /// <summary>Investments and lottery window for games where the bank window does not exist (bank module in development or absent) or the setting asks for it.</summary>
    public sealed class SilverlineFallbackComm : ConsoleComm
    {
        public static readonly SilverlineFallbackComm Instance = new SilverlineFallbackComm();
        public override string GetCallLabel() => FT.T("HH.Finance.Window.Option");
        public override string GetInfoText() => FT.T("HH.Finance.Window.Option");
        public override void TryOpenComms(Pawn negotiator) => Find.WindowStack.Add(new Window_Silverline());
    }

    /// <summary>The only patch on the comms console: adds Silverline, Clawback Mutual and (without a bank window) the finance window to the list of
    /// things a colonist can call. The vanilla float menu, the vanilla "can use comms now" and "can talk" checks and the vanilla job do the rest.</summary>
    [HarmonyPatch(typeof(Building_CommsConsole), nameof(Building_CommsConsole.GetCommTargets))]
    public static class Patch_CommsConsole_Targets
    {
        static void Postfix(ref IEnumerable<ICommunicable> __result)
        {
            if (!Modules.On(ModuleId.Bank)) return;
            __result = Extend(__result);
        }

        static IEnumerable<ICommunicable> Extend(IEnumerable<ICommunicable> orig)
        {
            foreach (var o in orig) yield return o;
            bool bank = false, fallback = false;
            try
            {
                bank = BankApi.Impl != null;
                fallback = FinanceAccounts.Available && (BankApi.Impl == null || HSettings.GetBool("finance.fallbackWindow"));
            }
            catch (System.Exception e) { HLog.Once("bank.commTargets", LogLevel.Warn, "Bank", "Comms console entries failed: " + e.Message); }
            if (bank) yield return SilverlineComm.Instance;
            yield return ClawbackComm.Instance;
            if (fallback) yield return SilverlineFallbackComm.Instance;
        }
    }

    /// <summary>The bank balance counts for the storyteller's colony wealth (D22, 8.10): the share is spread over the player maps by map wealth,
    /// so a balance never counts twice with several settlements. The per-map additions are computed once per game tick (no LINQ, no allocations
    /// after the first call) and reused for every call of the getter in that tick.</summary>
    [HarmonyPatch(typeof(Map), nameof(Map.PlayerWealthForStoryteller), MethodType.Getter)]
    public static class Patch_PlayerWealth
    {
        [System.ThreadStatic] static bool inside;
        static int cacheTick = -1;
        static long cacheBalanceMicro = long.MinValue; static float cacheShare = float.NaN;
        static readonly List<int> cacheMapIds = new List<int>(4);
        static readonly List<float> cacheExtra = new List<float>(4);
        /// <summary>Reset on new game / load (HearthGame.OnGameInit).</summary>
        public static void Reset() { cacheTick = -1; cacheBalanceMicro = long.MinValue; cacheMapIds.Clear(); cacheExtra.Clear(); }
        static Patch_PlayerWealth() { HearthGame.OnGameInit += Reset; }

        static void Postfix(Map __instance, ref float __result)
        {
            if (inside) return;
            if (!Modules.On(ModuleId.Bank)) return;
            var c = BankGameComp.Current;
            if (c == null || !c.HasAccount) return;
            if (!__instance.TreatAsPlayerHome()) return;
            int tick = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            // the balance and the slider are two cheap reads; a change inside the tick (a deposit, a test) refills at once
            long bal = c.Balance.Micro + c.InTransit.Micro; float share = HSettings.Get("bank.wealthShare");
            if (tick != cacheTick || bal != cacheBalanceMicro || share != cacheShare) Refill(c, tick, bal, share);
            var ids = cacheMapIds;
            for (int i = 0; i < ids.Count; i++)
                if (ids[i] == __instance.uniqueID) { __result += cacheExtra[i]; return; }
        }

        /// <summary>Computes the addition of every player home map for this tick.</summary>
        static void Refill(BankGameComp c, int tick, long balanceMicro, float share)
        {
            cacheTick = tick; cacheBalanceMicro = balanceMicro; cacheShare = share;
            cacheMapIds.Clear(); cacheExtra.Clear();
            if (share <= 0f) return;
            var st = Find.Storyteller;
            if (st != null && st.difficulty != null && st.difficulty.fixedWealthMode) return;
            double balance = balanceMicro / (double)Money.PerSilver;
            if (balance <= 0) return;
            var maps = Find.Maps;
            int homes = 0; double total = 0;
            // pass 1: wealth of every home map as vanilla sees it (our postfix returns at once while inside is set)
            inside = true;
            try
            {
                for (int i = 0; i < maps.Count; i++)
                {
                    var m = maps[i]; if (!m.TreatAsPlayerHome()) continue;
                    float w = m.PlayerWealthForStoryteller;
                    cacheMapIds.Add(m.uniqueID); cacheExtra.Add(w); total += w; homes++;
                }
            }
            finally { inside = false; }
            // pass 2: turn the wealth into the share of the balance this map carries
            for (int i = 0; i < cacheExtra.Count; i++)
            {
                double frac = homes <= 1 ? 1.0 : total <= 0.0001 ? 1.0 / homes : cacheExtra[i] / total;
                cacheExtra[i] = (float)(balance * share * frac);
            }
        }
    }
}
