// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Hearth;
using RimWorld;
using UnityEngine;
using Verse;

namespace Silverline
{
    /// <summary>Entry point of the investment products and the lottery (a second module object of ModuleId.Bank, next to BankModule).</summary>
    public sealed class FinanceModule : HearthModuleBase
    {
        public override ModuleId Id => ModuleId.Bank;

        public override void RegisterSettings() => FinanceSettings.Register();

        public override void OnDefsLoaded()
        {
            InvestTab.Register(); LotteryTab.Register();
            Caretaker.Register(ModuleId.Bank, "Finance.Positions", RepairPositions);
            Caretaker.Register(ModuleId.Bank, "Finance.Lottery", RepairLottery);
            HLog.Info("Bank", $"Finance: {AssetDefs.All.Count} assets defined");
        }

        public override void OnGameStarted() { FinanceGame.Current?.EnsureInit(); LotteryGame.Current?.EnsureInit(); }
        public override void OnGameLoaded() { FinanceGame.Current?.EnsureInit(); LotteryGame.Current?.EnsureInit(); }

        public override void GameTick(int ticksGame)
        {
            var f = FinanceGame.Current; if (f != null) f.Tick_(ticksGame);
            var l = LotteryGame.Current; if (l != null) l.Tick(ticksGame);
        }

        public override void PrepareRemoval(List<string> report)
        {
            var f = FinanceGame.Current; if (f == null) return;
            var v = f.PortfolioValue() + f.OmenStaked() + f.ProjectsStaked();
            if (v.Micro > 0) report.Add($"Investments worth {v} silver stay in the ledger and are lost if the bank is removed. Sell them first.");
        }

        public override IEnumerable<string> Diagnosis()
        {
            var f = FinanceGame.Instance; if (f == null) yield break;
            yield return $"Finance: {f.assets.Count} assets, {f.positions.Count} positions, {f.contracts.Count} Omen contracts, {f.projects.Count} projects, hour tick {f.lastHourTick}";
        }

        /// <summary>Caretaker: holdings or stakes whose asset/contract/project vanished (mod removed, def deleted) are refunded at cost.</summary>
        static int RepairPositions()
        {
            var f = FinanceGame.Instance; var led = Ledger.Current; if (f == null || led == null || !f.initialized) return 0;
            int fixedN = 0;
            foreach (var p in f.positions.ToList())
            {
                var a = f.Asset(p.assetId);
                if (p.units <= 0 || a == null || (a.def == null && AssetDefs.Get(a.defName) == null))
                {
                    if (p.cost > 0) led.Post(FinanceAccounts.Market(p.assetId), FinanceAccounts.Colony, new Money(p.cost), "Refund: asset no longer exists", true);
                    f.positions.Remove(p); fixedN++; Caretaker.Fixed("Finance.Positions", "position in " + p.assetId + " refunded at cost " + new Money(p.cost));
                }
            }
            foreach (var op in f.omenPositions.ToList())
                if (f.Contract(op.contractId) == null) { led.Post(FinanceAccounts.Omen, FinanceAccounts.Colony, new Money(op.paid), "Refund: Omen contract vanished", true); f.omenPositions.Remove(op); fixedN++; Caretaker.Fixed("Finance.Positions", "omen position refunded"); }
            foreach (var b in f.bets.ToList())
                if (!b.settled && f.Project(b.projectId) == null) { led.Post(FinanceAccounts.Projects, FinanceAccounts.Colony, new Money(b.stake), "Refund: project vanished", true); b.settled = true; fixedN++; Caretaker.Fixed("Finance.Positions", "project bet refunded"); }
            return fixedN;
        }

        static int RepairLottery()
        {
            var l = LotteryGame.Instance; var led = Ledger.Current; if (l == null || led == null) return 0;
            int n = 0;
            foreach (var acc in new[] { FinanceAccounts.LotteryGlobalPool, FinanceAccounts.LotteryGlobalJackpot, FinanceAccounts.LotteryColonyPot, FinanceAccounts.LotteryColonyJackpot })
            {
                var b = led.Balance(acc);
                if (b.Micro < 0) { led.Post("world", acc, -b, "Caretaker: pot was negative", true); n++; Caretaker.Fixed("Finance.Lottery", acc + " was negative (" + b + ")"); }
            }
            return n;
        }
    }

    /// <summary>Fallback host for the two tabs when the bank window does not exist (bank module in development or absent): comms console float menu.</summary>
    public sealed class Window_Silverline : Window
    {
        string current = "invest";
        public override Vector2 InitialSize => new Vector2(1020f, 700f);
        public Window_Silverline() { doCloseX = true; forcePause = false; absorbInputAroundWindow = false; closeOnClickedOutside = false; draggable = true; resizeable = true; preventCameraMotion = false; }

        public override void DoWindowContents(Rect inRect)
        {
            Guard.Run(ModuleId.Bank, "Window_Silverline", () =>
            {
                Text.Font = GameFont.Medium; Widgets.Label(new Rect(inRect.x, inRect.y, 400, 32), "Silverline"); Text.Font = GameFont.Small;
                var tabs = BankTabs.VisibleTabs.Where(t => t.Id == "invest" || t.Id == "lottery").ToList();
                float x = inRect.x + 200;
                foreach (var t in tabs)
                {
                    var b = new Rect(x, inRect.y + 2, 120, 28); x += 124;
                    if (current == t.Id) Widgets.DrawBoxSolid(b, new Color(1, 1, 1, 0.15f));
                    if (Widgets.ButtonText(b, t.Label, current != t.Id)) current = t.Id;
                }
                var body = new Rect(inRect.x, inRect.y + 38, inRect.width, inRect.height - 38);
                tabs.FirstOrDefault(t => t.Id == current)?.Draw?.Invoke(body);
            });
        }
    }

    [HarmonyPatch(typeof(Building_CommsConsole), nameof(Building_CommsConsole.GetFloatMenuOptions))]
    public static class Patch_CommsConsole_Silverline
    {
        static IEnumerable<FloatMenuOption> Postfix(IEnumerable<FloatMenuOption> __result, Building_CommsConsole __instance)
        {
            foreach (var o in __result) yield return o;
            bool show = false;
            try { show = Modules.On(ModuleId.Bank) && FinanceAccounts.Available && (BankApi.Impl == null || HSettings.GetBool("finance.fallbackWindow")) && __instance.CanUseCommsNow; } catch { }
            if (show) yield return new FloatMenuOption(FT.T("HH.Finance.Window.Option"), () => Find.WindowStack.Add(new Window_Silverline()));
        }
    }
}
