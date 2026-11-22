// SPDX-License-Identifier: MIT
using HarmonyLib;
using Hearth;
using RimWorld;
using Verse;

namespace Silverline
{
    /// <summary>
    /// K4: silver the colony receives from a trader (selling to a visiting trader, a trade ship or a settlement) goes straight into the bank
    /// account when direct deposit is on, instead of landing as silver at the drop spot. The deposit fee of the direct deposit applies.
    /// Caravans are left alone (they need coins on the road), as is anything with no account.
    /// </summary>
    public static class BankTrade
    {
        public static bool Enabled => Modules.On(ModuleId.Bank) && BankApi.Ready && BankApi.Impl.DirectDepositOn && HSettings.GetBool("bank.tradeDeposit");

        /// <summary>Home map of the colonist who negotiates the current deal (null for caravans, gifts, no session).</summary>
        public static Map HomeMap()
        {
            if (!TradeSession.Active || TradeSession.giftMode) return null;
            var map = TradeSession.playerNegotiator?.MapHeld;
            return map != null && map.IsPlayerHome ? map : null;
        }

        /// <summary>True for the silver the colony gets (the currency line of the deal, "player buys").</summary>
        public static bool ShouldDivert(Tradeable t)
        {
            if (t == null || !Enabled || !t.IsCurrency || t.ThingDef != ThingDefOf.Silver) return false;
            if (t.ActionToDo != TradeAction.PlayerBuys || t.CountToTransferToSource <= 0) return false;
            return HomeMap() != null;
        }

        /// <summary>Takes the silver out of the trader's goods and books it onto the account. Returns the amount deposited (after the fee).</summary>
        public static Money Divert(Tradeable t)
        {
            int coins = t.CountToTransferToSource;
            var c = BankGameComp.Current;
            TransferableUtility.TransferNoSplit(t.thingsTrader, coins, (thing, n) => thing.SplitOff(n).Destroy());
            var net = c.Credit("world", Money.FromSilver(coins), L.T("Memo.Trade", "Trade income"), HSettings.Get("bank.directDepositFee"));
            HLog.Debug("Bank", $"Trade income {coins} silver deposited (net {net})");
            Messages.Message(L.T("Msg.TradeDeposit", "Trade income of {0} silver went straight into your account at {1}.", net.ToStringShort(), c.BankName), MessageTypeDefOf.PositiveEvent, false);
            return net;
        }
    }

    [HarmonyPatch(typeof(Tradeable), nameof(Tradeable.ResolveTrade))]
    public static class Patch_Tradeable_ResolveTrade
    {
        static bool Prefix(Tradeable __instance)
        {
            if (!Modules.On(ModuleId.Bank)) return true;
            bool divert = false;
            try { divert = BankTrade.ShouldDivert(__instance); } catch (System.Exception e) { HLog.Once("bank.tradeDivert", LogLevel.Warn, "Bank", "Trade income check failed: " + e.Message); }
            if (!divert) return true;
            try { BankTrade.Divert(__instance); return false; }
            catch (System.Exception e) { HLog.Error("Bank", "Trade income deposit failed, vanilla delivery used: " + e); return true; }
        }
    }
}
