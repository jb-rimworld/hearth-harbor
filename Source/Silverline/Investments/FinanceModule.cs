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
            BankGameComp.InvestmentsProvider = () => FinanceGame.Current?.InvestmentBreakdown() ?? default;
            Caretaker.Register(ModuleId.Bank, "Finance.Positions", RepairPositions);
            Caretaker.Register(ModuleId.Bank, "Finance.Lottery", RepairLottery);
            HLog.Info("Bank", $"Finance: {AssetDefs.All.Count} assets defined");
        }

        public override void OnGameStarted() { FinanceGame.Current?.EnsureInit(); LotteryGame.Current?.EnsureInit(); }
        public override void OnGameLoaded() { FinanceGame.Current?.EnsureInit(); LotteryGame.Current?.EnsureInit(); }

        public override void GameTick(int ticksGame)
        {
            var f = FinanceGame.Current; if (f != null) f.OnTick(ticksGame);
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

    /// <summary>
    /// Fallback host for the two tabs when the bank window does not exist (bank module in development or absent): comms console float menu.
    /// 1.1.0 (F35): the same walnut-and-brass frame as the bank lobby, bigger by default, tab buttons as wide as their text, and the chosen tab is remembered.
    /// </summary>
    public sealed class Window_Silverline : Window
    {
        static string lastTab = "invest";
        string current = lastTab;
        public override Vector2 InitialSize => HUI.Size(1100f, 740f, 900f, 600f);
        protected override float Margin => 0f;
        public Window_Silverline() { doCloseX = true; forcePause = false; absorbInputAroundWindow = false; closeOnClickedOutside = false; draggable = true; resizeable = true; preventCameraMotion = false; }
        public override void PreOpen() { base.PreOpen(); BankUI.SetMinSize(this, 760f, 520f); }
        public override void PostOpen() { base.PostOpen(); BankSound.Play(BankSound.Open); }

        public override void DoWindowContents(Rect inRect)
        {
            var f = Text.Font; var a = Text.Anchor; var wr = Text.WordWrap;
            Guard.Run(ModuleId.Bank, "Window_Silverline", () =>
            {
                Widgets.DrawBoxSolid(inRect, BankUI.Walnut);
                GUI.color = BankUI.Brass; Widgets.DrawBox(inRect.ContractedBy(4f), 2); GUI.color = Color.white;
                var r = inRect.ContractedBy(14f);
                BankUI.Label(new Rect(r.x, r.y, 220f, 34f), L.T("Window.Title", "Silverline"), BankUI.Brass, GameFont.Medium, wrap: false);
                var tabs = BankTabs.VisibleTabs.Where(t => t.Id == "invest" || t.Id == "lottery").ToList();
                if (tabs.Count > 0 && tabs.All(t => t.Id != current)) current = tabs[0].Id;
                float x = r.x + 230f; Text.Font = GameFont.Small;
                foreach (var t in tabs)
                {
                    float w = Text.CalcSize(t.Label).x + 34f;
                    if (BankUI.Chip(new Rect(x, r.y + 2f, w, 32f), t.Label, current == t.Id)) { current = t.Id; lastTab = t.Id; }
                    x += w + 4f;
                }
                Widgets.DrawLineHorizontal(r.x, r.y + 38f, r.width, BankUI.BrassDim);
                var body = new Rect(r.x, r.y + 46f, r.width, r.height - 46f);
                tabs.FirstOrDefault(t => t.Id == current)?.Draw?.Invoke(body);
            });
            Text.Font = f; Text.Anchor = a; Text.WordWrap = wr; GUI.color = Color.white;
        }
    }
}
