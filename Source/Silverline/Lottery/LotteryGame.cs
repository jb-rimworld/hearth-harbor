// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Hearth;
using Hearth.Markets;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI.Group;

namespace Silverline
{
    public sealed class LotteryTicket : IExposable
    {
        public List<int> nums = new List<int>(); public int star; public int drawIndex;
        public void ExposeData() { Scribe_Collections.Look(ref nums, "n", LookMode.Value); Scribe_Values.Look(ref star, "s"); Scribe_Values.Look(ref drawIndex, "d"); if (nums == null) nums = new List<int>(); }
        public int[] Numbers => nums.OrderBy(x => x).ToArray();
    }

    public sealed class LotteryDrawRecord : IExposable
    {
        public int index, tick, star, crowdTickets, playerTickets, jackpotWinners; public bool colony;
        public List<int> numbers = new List<int>();
        public long jackpotPaid, playerPrize;
        public void ExposeData()
        {
            Scribe_Values.Look(ref index, "i"); Scribe_Values.Look(ref tick, "t"); Scribe_Values.Look(ref star, "s"); Scribe_Values.Look(ref colony, "c");
            Scribe_Values.Look(ref crowdTickets, "ct"); Scribe_Values.Look(ref playerTickets, "pt"); Scribe_Values.Look(ref jackpotWinners, "jw");
            Scribe_Collections.Look(ref numbers, "n", LookMode.Value); Scribe_Values.Look(ref jackpotPaid, "jp"); Scribe_Values.Look(ref playerPrize, "pp");
            if (numbers == null) numbers = new List<int>();
        }
    }

    /// <summary>
    /// Lotteries (KONZEPT 8.14): the global "Glitterworld Grand Draw" (5 of 40 + star 1 of 10) with simulated players from all settlements,
    /// and the colony's own pot-model lottery. All money moves through the ledger; draws come from sealed streams. The pot is never negative.
    /// </summary>
    public sealed class LotteryGame : GameComponent
    {
        public static LotteryGame Instance;
        public static LotteryGame Current
        {
            get
            {
                if (Instance != null) return Instance;
                if (Verse.Current.Game == null) return null;
                var g = Verse.Current.Game.GetComponent<LotteryGame>();
                if (g == null) { g = new LotteryGame(Verse.Current.Game); Verse.Current.Game.components.Add(g); }
                return Instance = g;
            }
        }

        public bool initialized;
        public int nextGlobalDrawTick = -1, nextColonyDrawTick = -1, globalDrawCounter, colonyDrawCounter;
        public List<LotteryTicket> tickets = new List<LotteryTicket>();
        public List<LotteryDrawRecord> history = new List<LotteryDrawRecord>();
        public long totalSpent, totalWon, colonyEarned;
        public int colonyTicketsThisDraw; public double colonyTicketsPerDayLast;
        public bool colonyLastDrawJackpot;
        public const int HistoryKeep = 24;
        public static readonly string[] HelpUrls = { "https://findahelpline.com/", "https://www.gamblingtherapy.org/" };

        public LotteryGame(Game game) { Instance = this; }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref initialized, "initialized");
            Scribe_Values.Look(ref nextGlobalDrawTick, "nextGlobal", -1); Scribe_Values.Look(ref nextColonyDrawTick, "nextColony", -1);
            Scribe_Values.Look(ref globalDrawCounter, "globalCounter"); Scribe_Values.Look(ref colonyDrawCounter, "colonyCounter");
            Scribe_Collections.Look(ref tickets, "tickets", LookMode.Deep);
            Scribe_Collections.Look(ref history, "history", LookMode.Deep);
            Scribe_Values.Look(ref totalSpent, "spent"); Scribe_Values.Look(ref totalWon, "won"); Scribe_Values.Look(ref colonyEarned, "colonyEarned");
            Scribe_Values.Look(ref colonyTicketsThisDraw, "colonyTickets"); Scribe_Values.Look(ref colonyTicketsPerDayLast, "colonyRate");
            if (Scribe.mode == LoadSaveMode.PostLoadInit) { tickets = tickets ?? new List<LotteryTicket>(); history = history ?? new List<LotteryDrawRecord>(); Instance = this; }
        }

        // ---- prices and amounts -----------------------------------------------------------------------------------------
        public static Money GlobalPrice => HSettings.GetSilver("lottery.globalPrice");
        public static double GlobalQuote => HSettings.Get("lottery.globalQuote");
        public static int GlobalInterval => Math.Max(1, HSettings.GetInt("lottery.globalInterval"));
        public Money GlobalJackpot => Ledger.Current?.Balance(FinanceAccounts.LotteryGlobalJackpot) ?? Money.Zero;
        public Money ColonyJackpot => Ledger.Current?.Balance(FinanceAccounts.LotteryColonyJackpot) ?? Money.Zero;
        public Money ColonyPot => Ledger.Current?.Balance(FinanceAccounts.LotteryColonyPot) ?? Money.Zero;
        public static readonly Money JackpotSeed = Money.FromSilver(20000);

        public bool ColonyOpen => HSettings.GetBool("lottery.colonyOn");
        public Money ColonyPrice => HSettings.GetSilver("lottery.colonyPrice");
        public double ColonyQuote => HSettings.Get("lottery.colonyQuote");
        public double ColonyFee => HSettings.Get("lottery.colonyFee");
        public int ColonyInterval => Math.Max(1, HSettings.GetInt("lottery.colonyInterval"));

        public List<LotteryMath.Rank> ColonyRankList()
        {
            var r = LotteryMath.ColonyRanks(HSettings.Get("lottery.colonyJackpotShare"), HSettings.Get("lottery.colonySecondShare"), HSettings.Get("lottery.colonyThirdShare"));
            LotteryMath.NormaliseShares(r); return r;
        }

        // ---- lifecycle ------------------------------------------------------------------------------------------------------
        public void EnsureInit()
        {
            if (!Modules.Available(ModuleId.Bank) || Ledger.Current == null) return;
            if (initialized) return;
            initialized = true;
            int t = FinanceGame.Tick;
            nextGlobalDrawTick = (t / 60000 + GlobalInterval) * 60000;
            if (GlobalJackpot.Micro <= 0) Ledger.Current.Post("world", FinanceAccounts.LotteryGlobalJackpot, JackpotSeed, "Glitterworld Grand Draw: opening jackpot");
            HLog.Info("Bank", "Lottery initialised, first global draw at tick " + nextGlobalDrawTick);
        }

        public void Tick(int tick)
        {
            if (!initialized) EnsureInit();
            if (!initialized || tick % 60000 != 0) return;
            Guard.Run(ModuleId.Bank, "Lottery.Day", () =>
            {
                if (HSettings.GetBool("lottery.globalOn") && nextGlobalDrawTick >= 0 && tick >= nextGlobalDrawTick) { DrawGlobal(tick); nextGlobalDrawTick = tick + GlobalInterval * 60000; }
                if (ColonyOpen) ColonyDay(tick);
            });
        }

        // ---- tickets of the player ----------------------------------------------------------------------------------------
        public int PlayerTicketsForNextDraw => tickets.Count(t => t.drawIndex == globalDrawCounter);

        public static bool ValidTicket(int[] nums, int star)
            => nums != null && nums.Length == LotteryMath.GlobalPicks && nums.Distinct().Count() == nums.Length && nums.All(n => n >= 1 && n <= LotteryMath.GlobalPool) && star >= 1 && star <= LotteryMath.GlobalStarPool;

        public TradeResult BuyGlobalTicket(int[] nums, int star)
        {
            EnsureInit();
            if (!FinanceAccounts.Available) return TradeResult.Fail("HH.Finance.Trade.NoBank");
            if (!HSettings.GetBool("lottery.globalOn")) return TradeResult.Fail("HH.Finance.Trade.Off");
            if (!ValidTicket(nums, star)) return TradeResult.Fail("HH.Finance.Lottery.BadTicket");
            var price = GlobalPrice;
            if (FinanceAccounts.Cash < price) return TradeResult.Fail("HH.Finance.Trade.NoCash");
            if (!FinanceAccounts.Post(FinanceAccounts.Colony, FinanceAccounts.LotteryGlobalPool, price, "Glitterworld Grand Draw: ticket")) return TradeResult.Fail("HH.Finance.Trade.NoCash");
            var t = new LotteryTicket { star = star, drawIndex = globalDrawCounter };
            t.nums.AddRange(nums.OrderBy(x => x)); tickets.Add(t); totalSpent += price.Micro;
            return new TradeResult { Ok = true, Money = price, Message = FT.T("HH.Finance.Lottery.Bought", string.Join(" ", t.nums) + " ★" + star) };
        }

        public TradeResult QuickPick(int count)
        {
            var rng = JRand.Sealed("lottery.pick"); int bought = 0; TradeResult last = default;
            for (int i = 0; i < count; i++)
            {
                var n = LotteryMath.DrawNumbers(rng, LotteryMath.GlobalPool, LotteryMath.GlobalPicks);
                last = BuyGlobalTicket(n, rng.Range(1, LotteryMath.GlobalStarPool + 1));
                if (!last.Ok) break; bought++;
            }
            if (bought == 0) return last;
            return new TradeResult { Ok = true, Money = GlobalPrice * bought, Message = FT.T("HH.Finance.Lottery.BoughtMany", bought.ToString()) };
        }

        // ---- simulated players ------------------------------------------------------------------------------------------------
        /// <summary>Tickets the simulated players of all settlements buy for one global draw: sum of tech factor * wealth * 200 * noise.</summary>
        public double CrowdTickets(RngStream rng, int intervalDays)
        {
            double sum = 0; var ws = Find.WorldObjects?.Settlements; if (ws == null) return 0;
            for (int i = 0; i < ws.Count; i++)
            {
                var s = ws[i]; if (s.Faction == null || s.Faction.IsPlayer) continue;
                double wealth = 0.6 + 0.8 * ((RngStream.Mix((ulong)s.ID) >> 11) * (1.0 / 9007199254740992.0));
                double noise = 0.8 + 0.4 * rng.NextDouble();
                sum += LotteryMath.InternationalTickets((int)s.Faction.def.techLevel, wealth, noise, HSettings.Get("lottery.internationalScale"));
            }
            return sum * intervalDays / 5.0;
        }

        /// <summary>People the colony lottery can reach: guests on the map plus simulated players of settlements whose faction is not hostile.</summary>
        public static int GuestsOnMaps()
        {
            int n = 0;
            foreach (var m in Find.Maps)
            {
                if (!m.IsPlayerHome) continue;
                foreach (var p in m.mapPawns.AllPawnsSpawned)
                {
                    if (!p.RaceProps.Humanlike || p.Faction == null || p.Faction.IsPlayer || p.HostileTo(Faction.OfPlayer)) continue;
                    if (p.GetLord()?.LordJob is LordJob_VisitColony || (p.guest != null && p.guest.HostFaction == Faction.OfPlayer && !p.IsPrisoner)) n++;
                }
            }
            return n;
        }

        public static double InternationalPeople()
        {
            double sum = 0; var ws = Find.WorldObjects?.Settlements; if (ws == null) return 0;
            for (int i = 0; i < ws.Count; i++)
            {
                var s = ws[i]; if (s.Faction == null || s.Faction.IsPlayer || s.Faction.HostileTo(Faction.OfPlayer)) continue;
                double wealth = 0.6 + 0.8 * ((RngStream.Mix((ulong)s.ID) >> 11) * (1.0 / 9007199254740992.0));
                sum += LotteryMath.TechFactor((int)s.Faction.def.techLevel) * wealth * LotteryMath.InternationalsPerSettlement;
            }
            return sum;
        }

        public LotteryMath.DemandInput DemandFor(double reach)
        {
            return new LotteryMath.DemandInput
            {
                Basis = HSettings.Get("lottery.demandBase"), Reach = reach, OwnQuote = ColonyQuote, GlobalQuote = GlobalQuote,
                OwnJackpot = ColonyJackpot.Silver + ColonyRankList()[0].Share * ColonyPot.Silver, GlobalJackpot = Math.Max(1, GlobalJackpot.Silver),
                Renown = 50, GlobalPrice = GlobalPrice.Silver, TicketPrice = ColonyPrice.Silver, AcceptFee = ColonyFee,
            };
        }

        /// <summary>Expected tickets per day for the colony lottery (guests + simulated players), the number shown in the bank window.</summary>
        public double ColonyDemandPerDay(out double guestPart, out double intlPart)
        {
            guestPart = LotteryMath.TicketsPerDay(DemandFor(GuestsOnMaps()));
            intlPart = LotteryMath.InternationalTicketsPerDay(DemandFor(0), InternationalPeople());
            return guestPart + intlPart;
        }

        // ---- the global draw -------------------------------------------------------------------------------------------------------
        public LotteryDrawRecord DrawGlobal(int tick, int[] forcedNumbers = null, int forcedStar = 0)
        {
            EnsureInit(); var led = Ledger.Current; if (led == null) return null;
            var rng = JRand.Sealed("lottery.global.draw");
            int interval = GlobalInterval; var price = GlobalPrice;
            int crowd = DetMath.Poisson(rng, CrowdTickets(rng, interval)); if (HearthGame.Instance == null) crowd = 0;
            if (crowd > 0) led.Post("world", FinanceAccounts.LotteryGlobalPool, price * crowd, "Glitterworld Grand Draw: simulated players");

            var sales = led.Balance(FinanceAccounts.LotteryGlobalPool);
            double q = GlobalQuote;
            var operatorCut = sales * (1 - q);
            if (operatorCut.Micro > 0) led.Post(FinanceAccounts.LotteryGlobalPool, "world", operatorCut, "Glitterworld Grand Draw: operator");
            var prizePool = led.Balance(FinanceAccounts.LotteryGlobalPool);

            var numbers = LotteryMath.DrawNumbers(rng, LotteryMath.GlobalPool, LotteryMath.GlobalPicks);
            int star = rng.Range(1, LotteryMath.GlobalStarPool + 1);
            if (forcedNumbers != null) { numbers = forcedNumbers.OrderBy(x => x).ToArray(); star = forcedStar; }       // test hook
            var ranks = LotteryMath.GlobalRanks();
            var crowdW = ranks.Select(r => DetMath.Poisson(rng, crowd * r.Probability)).ToArray();
            var mine = tickets.Where(t => t.drawIndex == globalDrawCounter).ToList();
            var playerW = new int[ranks.Count]; var ticketRank = new Dictionary<LotteryTicket, int>();
            foreach (var t in mine)
            {
                int m = LotteryMath.CountMatches(t.Numbers, numbers);
                var r = LotteryMath.RankOf(ranks, m, t.star == star);
                if (r != null) { int i = ranks.IndexOf(r); playerW[i]++; ticketRank[t] = i; }
            }
            var jackpotCarry = led.Balance(FinanceAccounts.LotteryGlobalJackpot);
            var avail = prizePool + jackpotCarry;
            var res = LotteryMath.Allocate(ranks, prizePool.Silver, jackpotCarry.Silver, avail.Silver, crowdW, playerW, HSettings.Get("lottery.jackpotCap"));

            var rec = new LotteryDrawRecord { index = globalDrawCounter, tick = tick, star = star, crowdTickets = crowd, playerTickets = mine.Count };
            rec.numbers.AddRange(numbers);
            long playerPrize = 0;
            for (int i = 0; i < res.Count; i++)
            {
                var rr = res[i]; if (rr.Paid <= 0) continue;
                // pay each winner's share: crowd back to the world, the player's tickets to the colony account
                var perWinner = Money.FromSilver(Math.Floor(rr.PerWinner * Money.PerSilver) / Money.PerSilver);
                if (rr.CrowdWinners > 0) PayFromPots(FinanceAccounts.LotteryGlobalPool, FinanceAccounts.LotteryGlobalJackpot, "world", perWinner * rr.CrowdWinners, "Glitterworld Grand Draw: prizes of simulated players");
                if (rr.PlayerWinners > 0)
                {
                    var m = perWinner * rr.PlayerWinners;
                    PayFromPots(FinanceAccounts.LotteryGlobalPool, FinanceAccounts.LotteryGlobalJackpot, FinanceAccounts.Colony, m, "Glitterworld Grand Draw: " + ranks[i].Id + " prize");
                    playerPrize += m.Micro; totalWon += m.Micro;
                }
                if (ranks[i].IsJackpot) { rec.jackpotWinners = rr.CrowdWinners + rr.PlayerWinners; rec.jackpotPaid = (long)Math.Round(rr.Paid * Money.PerSilver); }
            }
            rec.playerPrize = playerPrize;
            // what is left in the pool of this draw rolls into the jackpot carry; above the cap the rest goes back to the operator
            var rest = led.Balance(FinanceAccounts.LotteryGlobalPool);
            if (rest.Micro > 0) led.Post(FinanceAccounts.LotteryGlobalPool, FinanceAccounts.LotteryGlobalJackpot, rest, "Glitterworld Grand Draw: roll over");
            var carry = led.Balance(FinanceAccounts.LotteryGlobalJackpot);
            LotteryMath.CapJackpot(carry.Silver, HSettings.Get("lottery.jackpotCap"), out double overflow);
            if (overflow > 0) led.Post(FinanceAccounts.LotteryGlobalJackpot, "world", Money.FromSilver(Math.Floor(overflow)), "Glitterworld Grand Draw: jackpot cap");
            if (rec.jackpotWinners > 0 && led.Balance(FinanceAccounts.LotteryGlobalJackpot).Micro < JackpotSeed.Micro)
                led.Post("world", FinanceAccounts.LotteryGlobalJackpot, JackpotSeed - led.Balance(FinanceAccounts.LotteryGlobalJackpot), "Glitterworld Grand Draw: reseed");

            tickets.RemoveAll(t => t.drawIndex == globalDrawCounter);
            history.Add(rec); if (history.Count > HistoryKeep) history.RemoveAt(0);
            globalDrawCounter++;
            var fg = FinanceGame.Current;
            string numText = string.Join(" ", numbers) + " ★" + star;
            fg?.Push(MarketEventKind.Lottery, FT.T(playerPrize > 0 ? "HH.Finance.Lottery.News.Won" : "HH.Finance.Lottery.News.Drawn", numText, rec.jackpotWinners > 0 ? FT.T("HH.Finance.Lottery.JackpotHit") : "", FT.Silver(new Money(playerPrize))), false, null, playerPrize > 0 ? 0.5f : 0, tick, mine.Count > 0, null);
            HLog.Info("Bank", $"Global draw #{rec.index}: {numText}; crowd {crowd}, player tickets {mine.Count}, player prize {new Money(playerPrize)}, jackpot now {led.Balance(FinanceAccounts.LotteryGlobalJackpot)}");
            return rec;
        }

        /// <summary>Pay from the draw's pool first, then from the jackpot carry. Never overdraws: the pot model.</summary>
        void PayFromPots(string pool, string carry, string to, Money amount, string memo)
        {
            var led = Ledger.Current; if (amount.Micro <= 0) return;
            var fromPool = Money.Min(amount, led.Balance(pool));
            if (fromPool.Micro > 0) led.Post(pool, to, fromPool, memo);
            var rest = amount - fromPool;
            if (rest.Micro > 0) { var fromCarry = Money.Min(rest, led.Balance(carry)); if (fromCarry.Micro > 0) led.Post(carry, to, fromCarry, memo); }
        }

        // ---- the colony lottery ---------------------------------------------------------------------------------------------------------
        public void ColonyDay(int tick)
        {
            EnsureInit(); var led = Ledger.Current; if (led == null) return;
            if (nextColonyDrawTick < 0) nextColonyDrawTick = tick + ColonyInterval * 60000;
            var rng = JRand.Sealed("lottery.colony.sales");
            double perDay = ColonyDemandPerDay(out _, out _);
            colonyTicketsPerDayLast = perDay;
            int n = DetMath.Poisson(rng, perDay);
            if (n > 0) SellColonyTickets(n, "crowd");
            if (tick >= nextColonyDrawTick) { DrawColony(tick); nextColonyDrawTick = tick + ColonyInterval * 60000; }
        }

        /// <summary>Sell tickets of the colony lottery to guests / simulated players (also callable by guest shops through the contract).</summary>
        public Money SellColonyTickets(int n, string who)
        {
            var led = Ledger.Current; if (led == null || n <= 0) return Money.Zero;
            var price = ColonyPrice; double q = ColonyQuote; var fee = Money.FromSilver(ColonyFee);
            var pot = price * q * n; var house = price * n - pot + fee * n;
            led.Post("world", FinanceAccounts.LotteryColonyPot, pot, "Colony lottery: tickets (" + who + ")");
            var map = Find.AnyPlayerHomeMap;
            if (house.Micro > 0 && map != null) { Payments.CreditColony(map, house, "Colony lottery: ticket sales and fees"); colonyEarned += house.Micro; }
            else if (house.Micro > 0) { led.Post("world", FinanceAccounts.LotteryColonyPot, house, "Colony lottery: no map to book the take"); }
            colonyTicketsThisDraw += n;
            return pot + house;
        }

        public LotteryDrawRecord DrawColony(int tick)
        {
            EnsureInit(); var led = Ledger.Current; if (led == null) return null;
            var rng = JRand.Sealed("lottery.colony.draw");
            var ranks = ColonyRankList(); int n = colonyTicketsThisDraw;
            var numbers = LotteryMath.DrawNumbers(rng, LotteryMath.ColonyPool, LotteryMath.ColonyPicks);
            var crowdW = ranks.Select(r => DetMath.Poisson(rng, n * r.Probability)).ToArray();
            var potMoney = led.Balance(FinanceAccounts.LotteryColonyPot); var carry = led.Balance(FinanceAccounts.LotteryColonyJackpot);
            var res = LotteryMath.Allocate(ranks, potMoney.Silver, carry.Silver, (potMoney + carry).Silver, crowdW, new int[ranks.Count], HSettings.Get("lottery.colonyJackpotCap"));
            var rec = new LotteryDrawRecord { index = colonyDrawCounter, tick = tick, crowdTickets = n, colony = true };
            rec.numbers.AddRange(numbers);
            foreach (var rr in res)
            {
                if (rr.Paid <= 0) continue;
                var perWinner = Money.FromSilver(Math.Floor(rr.PerWinner * Money.PerSilver) / Money.PerSilver);
                PayFromPots(FinanceAccounts.LotteryColonyPot, FinanceAccounts.LotteryColonyJackpot, "world", perWinner * rr.CrowdWinners, "Colony lottery: prizes");
                if (rr.Rank.IsJackpot) { rec.jackpotWinners = rr.CrowdWinners; rec.jackpotPaid = (perWinner * rr.CrowdWinners).Micro; }
            }
            var rest = led.Balance(FinanceAccounts.LotteryColonyPot);
            if (rest.Micro > 0) led.Post(FinanceAccounts.LotteryColonyPot, FinanceAccounts.LotteryColonyJackpot, rest, "Colony lottery: roll over");
            LotteryMath.CapJackpot(led.Balance(FinanceAccounts.LotteryColonyJackpot).Silver, HSettings.Get("lottery.colonyJackpotCap"), out double overflow);
            // above the cap the overflow goes into the next pot (it is paid to the lower ranks of the next draw)
            if (overflow >= 0.01) led.Post(FinanceAccounts.LotteryColonyJackpot, FinanceAccounts.LotteryColonyPot, Money.FromSilver(Math.Floor(overflow)), "Colony lottery: jackpot cap overflow");
            colonyTicketsThisDraw = 0; colonyDrawCounter++;
            history.Add(rec); if (history.Count > HistoryKeep) history.RemoveAt(0);
            FinanceGame.Current?.Push(MarketEventKind.Lottery, FT.T("HH.Finance.Lottery.News.Colony", string.Join(" ", numbers), rec.jackpotWinners.ToString(), n.ToString()), false, null, 0, tick, false, null);
            return rec;
        }

        public Money ReferenceTicketCost => GlobalPrice;
    }
}
