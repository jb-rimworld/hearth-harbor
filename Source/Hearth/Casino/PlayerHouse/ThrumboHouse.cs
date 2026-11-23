// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Silverline;
using Verse;
using Hearth.CasinoMath;

namespace Hearth.Casino.PlayerHouse
{
    /// <summary>Who plays, where the money comes from and who receives the silver. A seat is cheap; the window makes one per frame.</summary>
    public sealed class ThrumboSeat
    {
        public Map Map;
        public Pawn Player;
        public PayFrom From;

        public ThrumboSeat(Map map, Pawn player, PayFrom from) { Map = map; Player = player; From = from; }

        /// <summary>The home map and a colonist to hand silver to. A negotiator that stands on a map is preferred.</summary>
        public static ThrumboSeat For(Pawn negotiator, PayFrom? from = null)
        {
            var map = negotiator?.MapHeld ?? BankGameComp.DefaultMap ?? Find.CurrentMap;
            var player = negotiator != null && !negotiator.Dead && negotiator.Faction == Faction.OfPlayer ? negotiator : BestNegotiator(map);
            return new ThrumboSeat(map, player, from ?? (BankApi.Ready ? PayFrom.Bank : PayFrom.Silver));
        }

        /// <summary>The free colonist with the best social skill on the map (receives silver winnings when nobody opened the window).</summary>
        public static Pawn BestNegotiator(Map map)
        {
            Pawn best = null; int bestLevel = -1;
            if (map?.mapPawns == null) return null;
            foreach (var p in map.mapPawns.FreeColonistsSpawned)
            {
                int l = p.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0;
                if (l > bestLevel) { best = p; bestLevel = l; }
            }
            return best;
        }
    }

    /// <summary>Outcome of one round (or one step of a hand).</summary>
    public sealed class ThrumboResult
    {
        public bool Ok, Done = true;
        public string Error;
        public Money Staked, Paid;
        public string Text;
        public int[] Reels;
        public int Pocket = -1;
        public double Mult;
        public Money Net => Paid - Staked;
        public static ThrumboResult Fail(string err) => new ThrumboResult { Ok = false, Error = err };
    }

    /// <summary>
    /// The engine of The Lucky Thrumbo (KONZEPT 6.13): a foreign house the player gambles against. Stakes are real colony silver and move
    /// only through Silverline's BankApi (account) or Payments and the bank's silver helper (silver on the map), always with a ledger entry on the
    /// house account <see cref="House"/>. Every round is drawn from a sealed random stream (JRand.Sealed), so a reload brings back the same result.
    /// </summary>
    public static class ThrumboHouse
    {
        public const string House = "house:thrumbo";
        const ulong HandStream = 0x54485255_4D424A4BUL;
        static ThrumboHand live;

        static ThrumboComponent Comp => ThrumboComponent.Get();

        // ============================================================================================================
        // Money
        // ============================================================================================================
        /// <summary>Net worth for the table limit: account + silver at home − debts when Silverline has an account, else the silver at home.</summary>
        public static Money NetWorth(Map map)
        {
            Money w = Money.Zero;
            try
            {
                var c = BankGameComp.Current;
                if (BankApi.Ready && c != null) w = c.NetWorth;
                else if (map != null) w = Money.FromSilver(map.resourceCounter.GetCount(ThingDefOf.Silver));
            }
            catch (Exception e) { HLog.Once("thrumbo.networth", LogLevel.Warn, "Casino", "Net worth for the Lucky Thrumbo limit failed: " + e.Message); }
            return w.IsNegative ? Money.Zero : w;
        }

        /// <summary>The house limit for one stake: share of the net worth, between a floor and a cap (KONZEPT 6.13).</summary>
        public static Money TableLimit(Map map)
        {
            var cap = HSettings.GetSilver(TS.MaxBetCap);
            var floor = Money.Min(HSettings.GetSilver(TS.MaxBetFloor), cap);
            var share = NetWorth(map) * (double)HSettings.Get(TS.MaxBetShare);
            return Money.Min(cap, Money.Max(floor, share));
        }

        /// <summary>What the player can put on the table from the chosen source right now.</summary>
        public static Money Funds(ThrumboSeat s, bool cached = false)
        {
            if (s.From == PayFrom.Bank) return BankApi.Ready ? BankApi.ColonyBalance : Money.Zero;
            if (s.Map == null) return Money.Zero;
            // the window asks every frame: it uses the bank's 30-frame cache; a real stake always scans the map
            return Money.FromSilver(cached ? BankSilver.DepositableCoinsCached(s.Map) : BankSilver.DepositableCoins(s.Map));
        }

        /// <summary>Largest stake right now: the table limit, but no more than the funds (whole coins for silver).</summary>
        public static Money MaxStake(ThrumboSeat s, bool cached = false)
        {
            var m = Money.Min(TableLimit(s.Map), Funds(s, cached));
            return s.From == PayFrom.Silver ? Money.FromSilver(m.WholeSilver) : m;
        }

        /// <summary>Null if the stake is acceptable, else a short explanation for the player.</summary>
        public static string Validate(ThrumboSeat s, Money stake)
        {
            if (!Modules.On(ModuleId.Casino)) return "HH.Thrumbo.Err.Closed".Translate();
            if (s == null || s.Map == null) return "HH.Thrumbo.Err.NoMap".Translate();
            if (Ledger.Current == null) return "HH.Thrumbo.Err.Closed".Translate();
            if (s.From == PayFrom.Bank && !BankApi.Ready) return "HH.Thrumbo.Err.NoBank".Translate();
            if (s.Player == null || s.Player.Dead) return "HH.Thrumbo.Err.NoPlayer".Translate();
            if (stake < TS.MinStake) return "HH.Thrumbo.Err.Min".Translate(TS.MinStake.ToStringShort());
            var limit = TableLimit(s.Map);
            if (stake > limit) return "HH.Thrumbo.Err.Max".Translate(limit.ToStringShort());
            if (s.From == PayFrom.Silver && Money.FromSilver(stake.WholeSilver) != stake) return "HH.Thrumbo.Err.Whole".Translate();
            if (Funds(s) < stake) return (s.From == PayFrom.Bank ? "HH.Thrumbo.Err.FundsBank" : "HH.Thrumbo.Err.FundsSilver").Translate(stake.ToStringShort());
            return null;
        }

        static bool Take(ThrumboSeat s, Money amount, string memo)
        {
            var led = Ledger.Current; if (led == null || amount.Micro <= 0) return false;
            if (s.From == PayFrom.Bank) return BankApi.TryPay(House, amount, memo + " (account)");
            int coins = amount.WholeSilver;
            if (coins <= 0 || Money.FromSilver(coins) != amount) return false;
            if (!BankSilver.TakeCoins(s.Map, coins)) return false;
            // the coins leave the map as things and enter the ledger as money
            led.Post("world", House, amount, memo + " (silver)");
            return true;
        }

        /// <summary>Pays the winnings (plus the change the house still owes) back the way the stake came. A fraction of a coin stays owed.</summary>
        static void Give(PayFrom from, Map map, Pawn player, Money pay, string memo)
        {
            var comp = Comp; if (comp == null) return;
            var total = pay + new Money(comp.ChangeMicro);
            if (total.Micro <= 0) return;
            if (from == PayFrom.Bank && BankApi.Ready && BankApi.Receive(House, total, memo + " (account)")) { comp.ChangeMicro = 0; return; }
            int coins = total.WholeSilver;
            if (coins > 0 && player != null && Payments.TryPayout(player, Money.FromSilver(coins), House, memo + " (silver)", null, true))
                comp.ChangeMicro = total.Micro - Money.FromSilver(coins).Micro;
            else
            {
                comp.ChangeMicro = total.Micro;
                if (coins > 0) HLog.Warn("Casino", "Lucky Thrumbo: could not hand " + coins + " silver to " + (player?.LabelShort ?? "nobody") + "; the house keeps it owed");
            }
        }

        /// <summary>
        /// Winnings of a "raise the stakes" round (KONZEPT 6.13: "Gewinne gehen aufs Konto oder kommen per Kapsel"): onto the account, or as
        /// silver in drop pods at the trade drop spot. Both book the house account; a fraction of a coin stays owed like other silver payouts.
        /// </summary>
        public static void PayStakeWin(PayFrom to, Map map, Money pay, string memo)
        {
            var comp = Comp; var led = Ledger.Current; if (comp == null || led == null) return;
            if (to == PayFrom.Bank && BankApi.Ready) { Give(PayFrom.Bank, map, null, pay, memo); return; }
            var total = pay + new Money(comp.ChangeMicro);
            int coins = total.WholeSilver;
            if (coins <= 0 || map == null) { comp.ChangeMicro = total.Micro; return; }
            if (!led.Post(House, "world", Money.FromSilver(coins), memo + " (drop pod)", true)) { comp.ChangeMicro = total.Micro; HLog.Warn("Casino", "Lucky Thrumbo: could not book a drop pod payout of " + coins); return; }
            comp.ChangeMicro = total.Micro - Money.FromSilver(coins).Micro;
            var spot = DropCellFinder.TradeDropSpot(map);
            int stackMax = Math.Max(1, ThingDefOf.Silver.stackLimit);
            for (int left = coins; left > 0;)
            {
                int n = Math.Min(left, stackMax); left -= n;
                var silver = ThingMaker.MakeThing(ThingDefOf.Silver); silver.stackCount = n;
                TradeUtility.SpawnDropPod(spot, map, silver);
            }
        }

        static ThrumboResult Finish(ThrumboSeat s, ThrumboGame g, Money stake, Money pay, string text, double mult)
        {
            Give(s.From, s.Map, s.Player, pay, "Lucky Thrumbo: " + g.ToString().ToLowerInvariant());
            Comp?.Record(g, s.From, stake, pay, text);
            return new ThrumboResult { Ok = true, Staked = stake, Paid = pay, Text = text, Mult = mult };
        }

        // ============================================================================================================
        // Slots and roulette: one stake, one draw, one payout
        // ============================================================================================================
        public static ThrumboResult PlaySlots(ThrumboSeat s, Money stake, SlotVolatility vol)
        {
            string err = Validate(s, stake); if (err != null) return ThrumboResult.Fail(err);
            if (!Take(s, stake, "Lucky Thrumbo: slots")) return ThrumboResult.Fail("HH.Thrumbo.Err.Take".Translate());
            var pt = ThrumboOdds.Slots(vol, TS.Shift);
            double mult = pt.Spin(JRand.Sealed("thrumbo.slots"), out int a, out int b, out int c);
            var pay = stake * mult;
            string text = SlotPaytable.SymbolNames[a] + " · " + SlotPaytable.SymbolNames[b] + " · " + SlotPaytable.SymbolNames[c] + " — " + (mult > 0 ? "HH.Thrumbo.Pays".Translate(mult.ToString("0.##")).ToString() : "HH.Thrumbo.NoWin".Translate().ToString());
            var r = Finish(s, ThrumboGame.Slots, stake, pay, text, mult);
            r.Reels = new[] { a, b, c };
            return r;
        }

        public static ThrumboResult PlayRoulette(ThrumboSeat s, Money stake, RouletteBet bet)
        {
            string err = Validate(s, stake); if (err != null) return ThrumboResult.Fail(err);
            if (bet == null) return ThrumboResult.Fail("HH.Thrumbo.Err.NoBet".Translate());
            if (!Take(s, stake, "Lucky Thrumbo: roulette")) return ThrumboResult.Fail("HH.Thrumbo.Err.Take".Translate());
            var rules = ThrumboOdds.Roulette(TS.Shift);
            int pocket = rules.Spin(JRand.Sealed("thrumbo.roulette"));
            double mult = rules.Return(bet, pocket);
            var pay = stake * mult;
            var r = Finish(s, ThrumboGame.Roulette, stake, pay, RouletteText(pocket, bet, mult), mult);
            r.Pocket = pocket;
            return r;
        }

        public static string PocketColor(int pocket) => pocket == 0 || pocket == 37 ? "green" : RouletteBet.IsRed(pocket) ? "red" : "black";
        static string RouletteText(int pocket, RouletteBet bet, double mult)
            => "HH.Thrumbo.Ball".Translate(RouletteBet.PocketName(pocket), ("HH.Thrumbo.Color." + PocketColor(pocket)).Translate()).ToString() + " · " + bet.Label + " — "
               + (mult > 0 ? "HH.Thrumbo.Pays".Translate(mult.ToString("0.##")).ToString() : "HH.Thrumbo.NoWin".Translate().ToString());

        // ============================================================================================================
        // Blackjack: a hand with decisions, saved as seed + actions
        // ============================================================================================================
        public static void DropLiveHand() { live = null; }

        /// <summary>The hand that is open right now (also after a reload), or null.</summary>
        public static ThrumboHand CurrentHand()
        {
            var c = Comp; if (c == null || !c.HandOpen) { live = null; return null; }
            if (live == null) live = Rebuild(c);
            return live;
        }

        static ThrumboHand Rebuild(ThrumboComponent c)
        {
            var h = new ThrumboHand { Solver = ThrumboOdds.Solver, Rng = new RngStream(c.HandSeed, HandStream), Stake = new Money(c.HandStakeMicro), From = (PayFrom)c.HandFrom, Scale = c.HandScale };
            h.Round = h.Solver.Deal(h.Rng);
            foreach (char a in c.HandActions)
            {
                if (h.Round.Done) break;
                if (a == 'H') h.Solver.Hit(h.Round, h.Rng); else if (a == 'S') h.Solver.Stand(h.Round, h.Rng); else if (a == 'D') h.Solver.DoubleDown(h.Round, h.Rng);
            }
            return h;
        }

        public static ThrumboResult DealBlackjack(ThrumboSeat s, Money stake)
        {
            var c = Comp; if (c == null) return ThrumboResult.Fail("HH.Thrumbo.Err.Closed".Translate());
            if (c.HandOpen) return ThrumboResult.Fail("HH.Thrumbo.Err.HandOpen".Translate());
            string err = Validate(s, stake); if (err != null) return ThrumboResult.Fail(err);
            if (!Take(s, stake, "Lucky Thrumbo: blackjack")) return ThrumboResult.Fail("HH.Thrumbo.Err.Take".Translate());
            c.HandSeed = JRand.Sealed("thrumbo.blackjack").NextULong();
            c.HandStakeMicro = stake.Micro; c.HandFrom = (int)s.From; c.HandActions = ""; c.HandOpen = true; c.HandMapId = s.Map.uniqueID;
            c.HandScale = ThrumboOdds.BlackjackWinScale(TS.Shift);
            live = Rebuild(c);
            return live.Round.Done ? Settle(s, live) : new ThrumboResult { Ok = true, Done = false, Staked = stake, Text = "HH.Thrumbo.BJ.Dealt".Translate() };
        }

        public static ThrumboResult Hit(ThrumboSeat s) => Act(s, 'H');
        public static ThrumboResult Stand(ThrumboSeat s) => Act(s, 'S');
        public static ThrumboResult DoubleDown(ThrumboSeat s) => Act(s, 'D');

        /// <summary>Can the open hand double? Needs the first two cards and enough funds in the source the hand was dealt from.</summary>
        public static bool CanDouble(ThrumboSeat s)
        {
            var h = CurrentHand(); if (h == null || h.Round.Done || h.Round.Player.Count != 2) return false;
            var seat = new ThrumboSeat(s.Map, s.Player, h.From);
            return Validate(seat, h.Stake) == null;
        }

        static ThrumboResult Act(ThrumboSeat s, char action)
        {
            var c = Comp; var h = CurrentHand();
            if (c == null || h == null || h.Round.Done) return ThrumboResult.Fail("HH.Thrumbo.Err.NoHand".Translate());
            var seat = new ThrumboSeat(s.Map, s.Player, h.From);
            if (action == 'D')
            {
                if (h.Round.Player.Count != 2) return ThrumboResult.Fail("HH.Thrumbo.Err.NoDouble".Translate());
                string err = Validate(seat, h.Stake); if (err != null) return ThrumboResult.Fail(err);
                if (!Take(seat, h.Stake, "Lucky Thrumbo: blackjack (double)")) return ThrumboResult.Fail("HH.Thrumbo.Err.Take".Translate());
                h.Solver.DoubleDown(h.Round, h.Rng);
            }
            else if (action == 'H') h.Solver.Hit(h.Round, h.Rng);
            else h.Solver.Stand(h.Round, h.Rng);
            c.HandActions += action;
            if (h.Round.Done) return Settle(seat, h);
            return new ThrumboResult { Ok = true, Done = false, Staked = h.Stake, Text = "" };
        }

        static ThrumboResult Settle(ThrumboSeat s, ThrumboHand h)
        {
            var c = Comp; var r = h.Round;
            var total = h.Stake * (r.Doubled ? 2.0 : 1.0);
            double net = r.Result * h.Stake.Silver;
            if (net > 0) net *= h.Scale;
            var pay = Money.Max(Money.Zero, total + Money.FromSilver(net));
            string text = BlackjackText(r, pay - total);
            c.HandOpen = false; c.HandActions = ""; live = null;
            var res = Finish(new ThrumboSeat(s.Map, s.Player, h.From), ThrumboGame.Blackjack, total, pay, text, h.Stake.Micro == 0 ? 0 : pay.Micro / (double)total.Micro);
            res.Done = true;
            return res;
        }

        public static string BlackjackText(BlackjackSolver.Round r, Money net)
            => "HH.Thrumbo.BJ.Line".Translate(r.PlayerTotal.ToString(), r.DealerTotal.ToString(), ("HH.Thrumbo.BJ." + r.Outcome).Translate()).ToString();

        /// <summary>Gives the open hand up the way a player walking away would: stand (so the stake is never lost without a result).</summary>
        public static void StandIfOpen(ThrumboSeat s) { if (CurrentHand() != null) Stand(s); }
    }

    /// <summary>The blackjack hand that is open: cards, the replayable stream and the stake it was dealt with.</summary>
    public sealed class ThrumboHand
    {
        public BlackjackSolver Solver;
        public BlackjackSolver.Round Round;
        public RngStream Rng;
        public Money Stake;
        public PayFrom From;
        public double Scale = 1.0;
    }
}
