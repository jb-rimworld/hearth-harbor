// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Hearth;
using UnityEngine;
using Verse;

namespace Silverline
{
    /// <summary>
    /// Public face of the bank for the rest of Hearth &amp; Harbor and for other mods (G9). Implemented by the bank module,
    /// which sets <see cref="Impl"/> in OnDefsLoaded. All amounts are Money (micro-silver). Accounts are ledger accounts "bank:&lt;id&gt;".
    /// </summary>
    public interface IBankImpl
    {
        bool HasAccount { get; }
        string ColonyAccount { get; }        // ledger account of the colony's main account, e.g. "bank:colony"
        Money ColonyBalance { get; }
        bool DirectDepositOn { get; }
        bool Deposit(Map map, Money amount, string memo);   // move from till/world into the colony account
        bool Withdraw(Map map, Money amount, string memo);  // pay out as physical silver (drop pod / at bank building)
    }

    public static class BankApi
    {
        public static IBankImpl Impl;
        public static bool Ready => Impl != null && Modules.On(ModuleId.Bank) && Impl.HasAccount;
        public static Money ColonyBalance => Ready ? Impl.ColonyBalance : Money.Zero;
    }

    /// <summary>Tabs of the bank window (8.4). The bank module draws the lobby; insurance, lottery, investments add tabs.</summary>
    public sealed class BankTab
    {
        public string Id, LabelKey, LabelEn;
        public int Order = 100;
        /// <summary>Module that owns the tab (insurance, lottery … add tabs); the tab is hidden while that module is off.</summary>
        public ModuleId Module = ModuleId.Bank;
        public Action<Rect> Draw;
        public Func<bool> Visible;
        public string Label => LabelKey.TranslateOr(LabelEn ?? Id);
    }

    public static class BankTabs
    {
        public static readonly List<BankTab> Tabs = new List<BankTab>();
        public static void Register(BankTab t) { Tabs.RemoveAll(x => x.Id == t.Id); Tabs.Add(t); Tabs.Sort((a, b) => a.Order.CompareTo(b.Order)); }
        public static IEnumerable<BankTab> VisibleTabs => Tabs.Where(t => Modules.On(ModuleId.Bank) && Modules.On(t.Module) && (t.Visible == null || Guard.Run(t.Module, "BankTab.Visible." + t.Id, t.Visible, false)));
    }
}
