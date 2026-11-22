// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
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

        /// <summary>What the prefix saw before vanilla moved the coins: how many, where the negotiator stands and how much silver lay on the ground and in incoming pods.</summary>
        public struct DivertState
        {
            public int Coins; public Map Map; public IntVec3 Pos; public int GroundBefore, PodBefore; public HashSet<int> KnownStacks; public bool Active;
        }

        public static DivertState Begin(Tradeable t)
        {
            if (!ShouldDivert(t)) return default;
            var map = HomeMap();
            return new DivertState { Active = true, Coins = t.CountToTransferToSource, Map = map, Pos = TradeSession.playerNegotiator.PositionHeld, GroundBefore = GroundSilver(map), PodBefore = PodSilver(map, null), KnownStacks = StackIds(map) };
        }

        static HashSet<int> StackIds(Map map)
        {
            var set = new HashSet<int>(); var things = map.listerThings.ThingsOfDef(ThingDefOf.Silver);
            for (int i = 0; i < things.Count; i++) set.Add(things[i].thingIDNumber);
            return set;
        }

        /// <summary>Silver on the ground or carried on the map (lister), without the pods.</summary>
        public static int GroundSilver(Map map)
        {
            int n = 0; var things = map.listerThings.ThingsOfDef(ThingDefOf.Silver);
            for (int i = 0; i < things.Count; i++) n += things[i].stackCount;
            return n;
        }

        /// <summary>Silver inside incoming drop pods (trade ships and settlements deliver that way). Fills <paramref name="stacks"/> with the stacks when given.</summary>
        public static int PodSilver(Map map, List<KeyValuePair<ThingOwner, Thing>> stacks)
        {
            int n = 0; var all = map.listerThings.AllThings;
            for (int i = 0; i < all.Count; i++)
            {
                if (!(all[i] is Skyfaller sf) || sf.innerContainer == null) continue;
                n += Scan(sf.innerContainer, stacks, 0);
            }
            return n;
        }

        static int Scan(ThingOwner owner, List<KeyValuePair<ThingOwner, Thing>> stacks, int depth)
        {
            int n = 0;
            for (int k = 0; k < owner.Count; k++)
            {
                var th = owner[k];
                if (th.def == ThingDefOf.Silver) { n += th.stackCount; stacks?.Add(new KeyValuePair<ThingOwner, Thing>(owner, th)); }
                else if (depth < 2 && th is ActiveTransporter at && at.Contents?.innerContainer != null) n += Scan(at.Contents.innerContainer, stacks, depth + 1);
            }
            return n;
        }

        /// <summary>
        /// After vanilla delivered the coins (as stacks next to the negotiator, or inside a drop pod): takes the same number of coins away again and
        /// books them onto the account (less the deposit fee). Silver is fungible, so any stack will do; the ones nearest to the delivery spot go first.
        /// Returns the amount deposited, zero if nothing could be found (then the coins stay where vanilla put them, nothing is lost).
        /// </summary>
        public static Money Finish(DivertState st)
        {
            if (!st.Active || st.Map == null || st.Coins <= 0) return Money.Zero;
            var c = BankGameComp.Current; if (c == null || !c.HasAccount) return Money.Zero;
            var podStacks = new List<KeyValuePair<ThingOwner, Thing>>();
            int podNow = PodSilver(st.Map, podStacks);
            int fromPods = Math.Min(st.Coins, Math.Max(0, podNow - st.PodBefore));
            int fromGround = Math.Min(st.Coins - fromPods, Math.Max(0, GroundSilver(st.Map) - st.GroundBefore));
            if (fromPods + fromGround <= 0) { HLog.Once("bank.tradeNoCoins", LogLevel.Warn, "Bank", "Trade income: no delivered silver found, the coins stay as they are."); return Money.Zero; }
            int taken = 0;
            if (fromGround > 0)
            {
                var stacks = new List<Thing>(st.Map.listerThings.ThingsOfDef(ThingDefOf.Silver));
                // stacks that did not exist before the trade (the delivery) first, then the nearest ones: the colonist's own coins stay put if possible
                var known = st.KnownStacks;
                stacks.Sort((a, b) =>
                {
                    int ka = known != null && known.Contains(a.thingIDNumber) ? 1 : 0, kb = known != null && known.Contains(b.thingIDNumber) ? 1 : 0;
                    if (ka != kb) return ka.CompareTo(kb);
                    return (a.PositionHeld - st.Pos).LengthHorizontalSquared.CompareTo((b.PositionHeld - st.Pos).LengthHorizontalSquared);
                });
                int left = fromGround;
                for (int i = 0; i < stacks.Count && left > 0; i++)
                {
                    var t = stacks[i]; if (t.Destroyed) continue;
                    int n = Math.Min(left, t.stackCount);
                    if (n >= t.stackCount) t.Destroy(); else t.SplitOff(n).Destroy();
                    left -= n;
                }
                taken += fromGround - left;
            }
            if (fromPods > 0)
            {
                int left = fromPods;
                foreach (var kv in podStacks)
                {
                    if (left <= 0) break;
                    var t = kv.Value; int n = Math.Min(left, t.stackCount);
                    if (n >= t.stackCount) { kv.Key.Remove(t); t.Destroy(); } else t.SplitOff(n).Destroy();
                    left -= n;
                }
                taken += fromPods - left;
            }
            if (taken <= 0) return Money.Zero;
            var net = c.Credit("world", Money.FromSilver(taken), L.T("Memo.Trade", "Trade income"), HSettings.Get("bank.directDepositFee"));
            HLog.Debug("Bank", $"Trade income {taken} silver deposited (net {net})");
            Messages.Message(L.T("Msg.TradeDeposit", "Trade income of {0} silver went straight into your account at {1}.", net.ToStringShort(), c.BankName), MessageTypeDefOf.PositiveEvent, false);
            return net;
        }
    }

    /// <summary>
    /// Trade income (K4): vanilla runs unchanged (so every other trading mod sees the trade as usual); afterwards the silver it delivered is moved onto the account.
    /// The prefix only looks (it never skips the original) and remembers how much silver the map holds.
    /// </summary>
    [HarmonyPatch(typeof(Tradeable), nameof(Tradeable.ResolveTrade))]
    public static class Patch_Tradeable_ResolveTrade
    {
        static void Prefix(Tradeable __instance, out BankTrade.DivertState __state)
        {
            __state = default;
            if (!Modules.On(ModuleId.Bank)) return;
            try { __state = BankTrade.Begin(__instance); }
            catch (System.Exception e) { HLog.Once("bank.tradeDivert", LogLevel.Warn, "Bank", "Trade income check failed: " + e.Message); }
        }

        static void Postfix(BankTrade.DivertState __state)
        {
            if (!__state.Active) return;
            try { BankTrade.Finish(__state); }
            catch (System.Exception e) { HLog.Error("Bank", "Trade income deposit failed, the coins stay where vanilla put them: " + e); }
        }
    }
}
