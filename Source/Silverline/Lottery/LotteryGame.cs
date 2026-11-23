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
        /// <summary>What the ticket cost in micro-silver; -1 = unknown (tickets saved before 1.1.0 count at the current price).</summary>
        public long paid = -1;
        /// <summary>A free ticket of a lucky colonist.</summary>
        public bool free;
        public void ExposeData()
        {
            Scribe_Collections.Look(ref nums, "n", LookMode.Value); Scribe_Values.Look(ref star, "s"); Scribe_Values.Look(ref drawIndex, "d");
            Scribe_Values.Look(ref paid, "p", -1L); Scribe_Values.Look(ref free, "f");
            if (nums == null) nums = new List<int>();
        }
        public int[] Numbers => nums.OrderBy(x => x).ToArray();
        /// <summary>"3 7 12 25 31|4": compact text of a ticket for the draw record.</summary>
        public string Compact() => string.Join(" ", Numbers) + "|" + star;
        public static bool TryParse(string compact, out int[] nums, out int star)
        {
            nums = null; star = 0; if (compact.NullOrEmpty()) return false;
            var parts = compact.Split('|'); if (parts.Length != 2 || !int.TryParse(parts[1], out star)) return false;
            var ns = new List<int>();
            foreach (var x in parts[0].Split(' ')) { if (x.Length == 0) continue; if (!int.TryParse(x, out int n)) return false; ns.Add(n); }
            nums = ns.ToArray(); return nums.Length > 0;
        }
    }

    public sealed class LotteryDrawRecord : IExposable
    {
        public int index, tick, star, crowdTickets, playerTickets, jackpotWinners; public bool colony;
        public List<int> numbers = new List<int>();
        public long jackpotPaid, playerPrize;
        /// <summary>The player's tickets of this draw as "n n n n n|star" (newest last, at most <see cref="LotteryGame.TicketsKept"/>), so the tab can show what matched after the draw. Since 1.1.0.</summary>
        public List<string> myTickets = new List<string>();
        /// <summary>What the player paid for the tickets of this draw (micro-silver). Since 1.1.0.</summary>
        public long playerSpent;
        public int freeTickets;
        /// <summary>Best number of matching numbers over ALL tickets of the draw (not only the kept ones), and whether a ticket with that many also had the star. -1 = record from before 1.1.1.</summary>
        public int bestMatches = -1; public bool bestStar;
        public void ExposeData()
        {
            Scribe_Values.Look(ref bestMatches, "bm", -1); Scribe_Values.Look(ref bestStar, "bs");
            Scribe_Values.Look(ref index, "i"); Scribe_Values.Look(ref tick, "t"); Scribe_Values.Look(ref star, "s"); Scribe_Values.Look(ref colony, "c");
            Scribe_Values.Look(ref crowdTickets, "ct"); Scribe_Values.Look(ref playerTickets, "pt"); Scribe_Values.Look(ref jackpotWinners, "jw");
            Scribe_Collections.Look(ref numbers, "n", LookMode.Value); Scribe_Values.Look(ref jackpotPaid, "jp"); Scribe_Values.Look(ref playerPrize, "pp");
            Scribe_Collections.Look(ref myTickets, "mt", LookMode.Value); Scribe_Values.Look(ref playerSpent, "ps"); Scribe_Values.Look(ref freeTickets, "ft");
            if (numbers == null) numbers = new List<int>();
            if (myTickets == null) myTickets = new List<string>();
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
        /// <summary>Tickets bought while a lucky colonist lived in the colony; every tenth of them is free (KONZEPT 11.1).</summary>
        public int luckyTickets;
        /// <summary>Set by Hearth's DLC integration: the name of a lucky colonist, or null. Silverline knows nothing about traits itself.</summary>
        public static Func<string> LuckyBuyer;
        public const int HistoryKeep = 24;
        /// <summary>How many tickets of a finished draw the record keeps for the "last draw" view.</summary>
        public const int TicketsKept = 100;
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
            Scribe_Values.Look(ref luckyTickets, "luckyTickets");
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
        // The tab asks for these every frame, so they come from one cached pass over the tickets (rebuilt when a ticket is added or removed, the draw counter or the price changes).
        int cacheCount = -1, cacheDraw = -1; long cachePrice = -1;
        int cNext, cFree; long cSpent; List<LotteryTicket> cList = new List<LotteryTicket>();
        public void InvalidateTicketCache() { cacheCount = -1; }
        void RefreshTicketCache()
        {
            long price = GlobalPrice.Micro;
            if (cacheCount == tickets.Count && cacheDraw == globalDrawCounter && cachePrice == price) return;
            cacheCount = tickets.Count; cacheDraw = globalDrawCounter; cachePrice = price;
            cNext = 0; cFree = 0; cSpent = 0; cList = new List<LotteryTicket>();
            for (int i = tickets.Count - 1; i >= 0; i--)
            {
                var t = tickets[i]; if (t.drawIndex != globalDrawCounter) continue;
                cNext++; cList.Add(t);
                if (t.free) cFree++; else cSpent += t.paid >= 0 ? t.paid : price;
            }
        }

        public int PlayerTicketsForNextDraw { get { RefreshTicketCache(); return cNext; } }

        /// <summary>Silver spent on the tickets of the next draw (free tickets cost nothing).</summary>
        public Money PlayerSpentThisDraw { get { RefreshTicketCache(); return new Money(cSpent); } }

        public int FreeTicketsThisDraw { get { RefreshTicketCache(); return cFree; } }

        /// <summary>The player's tickets of the next draw, newest first. The list is shared (cached): do not change it.</summary>
        public List<LotteryTicket> TicketsOfNextDraw() { RefreshTicketCache(); return cList; }

        /// <summary>The newest finished global draw, or null.</summary>
        public LotteryDrawRecord LastGlobalDraw => history.LastOrDefault(h => !h.colony);

        public static bool ValidTicket(int[] nums, int star)
            => nums != null && nums.Length == LotteryMath.GlobalPicks && nums.Distinct().Count() == nums.Length && nums.All(n => n >= 1 && n <= LotteryMath.GlobalPool) && star >= 1 && star <= LotteryMath.GlobalStarPool;

        public TradeResult BuyGlobalTicket(int[] nums, int star) { Money none = Money.Zero; return BuyCore(nums, star, false, ref none); }

        /// <summary>Buys one ticket. With <paramref name="defer"/> the money is not posted yet but added to <paramref name="deferred"/> (a quick pick batch posts once).</summary>
        TradeResult BuyCore(int[] nums, int star, bool defer, ref Money deferred)
        {
            EnsureInit();
            if (!FinanceAccounts.Available) return TradeResult.Fail("HH.Finance.Trade.NoBank");
            if (!HSettings.GetBool("lottery.globalOn")) return TradeResult.Fail("HH.Finance.Trade.Off");
            if (!ValidTicket(nums, star)) return TradeResult.Fail("HH.Finance.Lottery.BadTicket");
            var price = GlobalPrice;
            string lucky = null;
            try { lucky = LuckyBuyer?.Invoke(); } catch (Exception e) { HLog.Once("lottery.lucky", LogLevel.Warn, "Bank", "Lucky-buyer hook failed: " + e.Message); }
            bool free = lucky != null && DlcMath.FreeLottoTicket(luckyTickets + 1);
            if (!free)
            {
                if (FinanceAccounts.Cash < deferred + price) return TradeResult.Fail("HH.Finance.Trade.NoCash");
                if (defer) deferred += price;
                else if (!FinanceAccounts.Post(FinanceAccounts.Colony, FinanceAccounts.LotteryGlobalPool, price, "Glitterworld Grand Draw: ticket")) return TradeResult.Fail("HH.Finance.Trade.NoCash");
            }
            if (lucky != null) luckyTickets++;
            var t = new LotteryTicket { star = star, drawIndex = globalDrawCounter, free = free, paid = free ? 0 : price.Micro };
            t.nums.AddRange(nums.OrderBy(x => x)); tickets.Add(t);
            string numbers = string.Join(" ", t.nums) + " ★" + star;
            if (free) return new TradeResult { Ok = true, Money = Money.Zero, Units = 1, Message = FT.T("HH.Finance.Lottery.FreeTicket", numbers, lucky) };
            totalSpent += price.Micro;
            return new TradeResult { Ok = true, Money = price, Units = 1, Message = FT.T("HH.Finance.Lottery.Bought", numbers) };
        }

        public TradeResult QuickPick(int count)
        {
            var rng = JRand.Sealed("lottery.pick"); int bought = 0; TradeResult last = default; Money paid = Money.Zero, deferred = Money.Zero;
            int startTickets = tickets.Count, startLucky = luckyTickets;
            for (int i = 0; i < count; i++)
            {
                var n = LotteryMath.DrawNumbers(rng, LotteryMath.GlobalPool, LotteryMath.GlobalPicks);
                last = BuyCore(n, rng.Range(1, LotteryMath.GlobalStarPool + 1), true, ref deferred);
                if (!last.Ok) break; bought++; paid += last.Money;
            }
            if (bought == 0) return last;
            // one ledger entry for the whole batch (200 separate entries would flood the history)
            if (deferred.Micro > 0 && !FinanceAccounts.Post(FinanceAccounts.Colony, FinanceAccounts.LotteryGlobalPool, deferred, "Glitterworld Grand Draw: " + bought + " tickets"))
            {
                tickets.RemoveRange(startTickets, tickets.Count - startTickets); luckyTickets = startLucky; totalSpent -= paid.Micro; InvalidateTicketCache();
                return TradeResult.Fail("HH.Finance.Trade.NoCash");
            }
            return new TradeResult { Ok = true, Money = paid, Units = bought, Message = FT.T("HH.Finance.Lottery.BoughtMany", bought.ToString()) };
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
                Renown = RenownSource.Current, GlobalPrice = GlobalPrice.Silver, TicketPrice = ColonyPrice.Silver, AcceptFee = ColonyFee,
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
            // best result over ALL tickets; of a large batch the record keeps the 100 best (ties: newest), in buying order
            var score = new int[mine.Count]; rec.bestMatches = 0;
            for (int i = 0; i < mine.Count; i++)
            {
                int m = LotteryMath.CountMatches(mine[i].Numbers, numbers); bool sh = mine[i].star == star;
                score[i] = m * 2 + (sh ? 1 : 0);
                if (m > rec.bestMatches || (m == rec.bestMatches && sh)) { rec.bestMatches = m; rec.bestStar = sh; }
            }
            var keep = Enumerable.Range(0, mine.Count);
            if (mine.Count > TicketsKept) keep = keep.OrderByDescending(i => score[i]).ThenByDescending(i => i).Take(TicketsKept).OrderBy(i => i).ToList();
            foreach (int ki in keep) rec.myTickets.Add(mine[ki].Compact());
            foreach (var mt in mine) { if (mt.free) rec.freeTickets++; else rec.playerSpent += mt.paid >= 0 ? mt.paid : price.Micro; }
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
            globalDrawCounter++; InvalidateTicketCache();
            if (mine.Count > 0) BankSound.Play(playerPrize > 0 ? BankSound.Win : BankSound.Lose);   // F31: sounds when the draw resolves, whether or not the tab is open
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
