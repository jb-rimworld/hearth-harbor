// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Hearth;
using RimWorld;
using Verse;

namespace Silverline
{
    /// <summary>
    /// The saved state of the bank (KONZEPT 8.2): the colony account (ledger account "bank:colony"), the chosen bank, pending transfers,
    /// loans, credit score and the daily history. Removing the mod leaves a loadable save: everything is in this one GameComponent
    /// plus the Ledger of Hearth.Core.
    /// </summary>
    public sealed partial class BankGameComp : GameComponent
    {
        public const string Colony = "bank:colony", Transit = "bank:transit", LoansAccount = "bank:loans";
        public static BankGameComp Current { get; private set; }

        // saved ------------------------------------------------------------------------------------------
        bool hasAccount;
        string bankDefName;
        int openedTick;
        List<PendingTransfer> pending = new List<PendingTransfer>();
        List<Loan> loans = new List<Loan>();
        int score = FinanceMath.ScoreStart;
        int nextLoanId = 1;
        int lastDay = -1, lastQuadrum = -1;
        List<DaySample> samples = new List<DaySample>();
        int shownTier = 4, candidateTier = 4, candidateSince;
        int atmDay = -1; long atmTakenMicro;
        int lastBailoutTick = -100_000_000;
        long interestQuadrumMicro, interestLastQuadrumMicro;
        long totalInterestMicro, totalFeesMicro;
        List<Pawn> collectors = new List<Pawn>();

        // runtime -----------------------------------------------------------------------------------------
        long cachedNetMicro, cachedInvMicro; int cachedNetTick = -9999; InvestmentBreakdown cachedInv;

        public BankGameComp(Game game) { Current = this; }

        // ------------------------------------------------------------------------------------------------
        // Read access
        // ------------------------------------------------------------------------------------------------
        public bool HasAccount => hasAccount;
        public SL_BankDef Bank => bankDefName == null ? null : DefDatabase<SL_BankDef>.GetNamedSilentFail(bankDefName) ?? SL_BankDef.Default;
        public string BankName => Bank?.Name ?? "Silverline";
        public Money Balance => Ledger.Current?.Balance(Colony) ?? Money.Zero;
        public Money InTransit
        {
            get { long sum = 0; for (int i = 0; i < pending.Count; i++) sum += pending[i].Micro; return new Money(sum); }   // deposits and withdrawals by pod: silver that left one side and is not on the other yet
        }
        public Money Debt => new Money(loans.Sum(l => l.BalanceMicro));
        public IReadOnlyList<Loan> Loans => loans;
        public IReadOnlyList<PendingTransfer> Pending => pending;
        public IReadOnlyList<DaySample> Samples => samples;
        public int Score => score;
        public int ShownTier => shownTier;
        public int OpenedTick => openedTick;
        public Money InterestThisQuadrum => new Money(interestQuadrumMicro);
        public Money TotalInterest => new Money(totalInterestMicro);
        public Money TotalFees => new Money(totalFeesMicro);
        public List<Pawn> Collectors => collectors;
        public bool DirectDepositOn => hasAccount && HSettings.GetBool("bank.directDeposit");
        public int Now => Find.TickManager?.TicksGame ?? 0;
        public int Today => Now / 60000;

        public static IEnumerable<Map> HomeMaps => Find.Maps.Where(m => m.IsPlayerHome);
        public static Map DefaultMap => (Find.CurrentMap != null && Find.CurrentMap.IsPlayerHome) ? Find.CurrentMap : (Find.AnyPlayerHomeMap ?? Find.CurrentMap);

        public int CashAtHome => HomeMaps.Sum(m => m.resourceCounter.GetCount(ThingDefOf.Silver));

        /// <summary>
        /// Net worth (KONZEPT 8.4, F22 in 1.1.0): account + deposits in transit + silver at home + investments at market value − debts.
        /// Investments are stocks, funds, crypto, NFTs, patience bonds (at their current value) and money staked in Omen contracts and projects (at cost);
        /// they come from <see cref="InvestmentsProvider"/> (set by the finance module) so the bank folder does not depend on the finance folder. Cached for 250 ticks.
        /// </summary>
        public Money NetWorth { get { RefreshNet(); return new Money(cachedNetMicro); } }

        /// <summary>Market value of all investments counted into the net worth (zero without the finance module). Cached with the net worth.</summary>
        public Money InvestmentsValue { get { RefreshNet(); return new Money(cachedInvMicro); } }

        /// <summary>Net worth for credit limits: only <see cref="BankSettings.LoanPortfolioShare"/> of the investments counts (nobody borrows against crypto at full value).</summary>
        public Money LoanNetWorth => new Money((long)Math.Round(FinanceMath.LoanNetWorth(NetWorth.Micro, InvestmentsValue.Micro, BankSettings.LoanPortfolioShare)));

        /// <summary>Set by FinanceModule: current market value of everything invested (breakdown for the tooltip). Null = nothing invested.</summary>
        public static Func<InvestmentBreakdown> InvestmentsProvider;

        /// <summary>Parts of the investments for the net worth tooltip (micro-silver).</summary>
        public struct InvestmentBreakdown
        {
            public long Securities, Bonds, Stakes;
            public long Total => Securities + Bonds + Stakes;
        }

        InvestmentBreakdown Breakdown()
        {
            if (!Modules.On(ModuleId.Bank) || InvestmentsProvider == null) return default;
            try { return InvestmentsProvider(); }
            catch (Exception e) { HLog.Once("bank.investments", LogLevel.Warn, "Bank", "Investment value could not be read, counted as zero: " + e.Message); return default; }
        }

        void RefreshNet()
        {
            int t = Now;
            if (t - cachedNetTick < 250 && t >= cachedNetTick) return;
            var inv = Breakdown();
            cachedInv = inv; cachedInvMicro = inv.Total;
            cachedNetMicro = (Balance + InTransit + Money.FromSilver(CashAtHome) + new Money(inv.Total) - Debt).Micro;
            cachedNetTick = t;
        }

        /// <summary>The parts of the net worth for the tooltip and the lobby rows.</summary>
        public InvestmentBreakdown InvestmentParts { get { RefreshNet(); return cachedInv; } }
        public void InvalidateCache() { cachedNetTick = -9999; }

        public double ColonyWealth() => HomeMaps.Sum(m => (double)m.wealthWatcher.WealthTotal);

        // ------------------------------------------------------------------------------------------------
        // Account
        // ------------------------------------------------------------------------------------------------
        public bool CanOpenAccount(SL_BankDef def, out string why)
        {
            why = null;
            if (def == null) { why = "no bank"; return false; }
            if (!def.Available) { why = L.T("Reason.BankUnavailable", "That bank is not open to you."); return false; }
            return true;
        }

        public bool OpenAccount(SL_BankDef def, bool directDeposit)
        {
            if (hasAccount) return false;
            if (!CanOpenAccount(def, out _)) return false;
            hasAccount = true; bankDefName = def.defName; openedTick = Now;
            HSettings.Set("bank.directDeposit", directDeposit ? 1f : 0f);
            lastDay = Today; lastQuadrum = Today / 15;
            samples.Clear();
            AddSample(Today);
            HLog.Info("Bank", $"Account opened at {def.defName} (direct deposit {directDeposit})");
            Messages.Message(L.T("Msg.Opened", "Account opened at {0}.", def.Name), MessageTypeDefOf.PositiveEvent, false);
            return true;
        }

        public Money SwitchFee => HSettings.GetSilver("bank.switchFee");

        public bool SwitchBank(SL_BankDef def, out string why)
        {
            why = null;
            if (!hasAccount || def == null) { why = "no account"; return false; }
            if (def.defName == bankDefName) { why = L.T("Reason.SameBank", "You already bank there."); return false; }
            if (!def.Available) { why = L.T("Reason.BankUnavailable", "That bank is not open to you."); return false; }
            var fee = SwitchFee;
            if (Balance < fee) { why = L.T("Reason.SwitchFunds", "Moving the account costs {0} silver and the account does not hold that.", fee.ToStringShort()); return false; }
            Ledger.Current.Post(Colony, "world", fee, "Fee: changed banks");
            totalFeesMicro += fee.Micro;
            bankDefName = def.defName;
            HLog.Info("Bank", "Switched bank to " + def.defName);
            Messages.Message(L.T("Msg.Switched", "Your account now lives at {0}.", def.Name), MessageTypeDefOf.NeutralEvent, false);
            return true;
        }

        // ------------------------------------------------------------------------------------------------
        // Fees and quotes
        // ------------------------------------------------------------------------------------------------
        double FeeFactor => BankSettings.FeeFactor;

        public Money DepositFee(Money amount) => amount * ((Bank?.depositFee ?? 0) * FeeFactor);

        public Money WithdrawFee(Money amount, BankSite site)
        {
            var b = Bank; if (b == null) return Money.Zero;
            double f = FeeFactor;
            if (site == BankSite.Atm) return FinanceMath.Fee(amount, b.atmFee * f, 0, b.atmFlatFee * f);
            return FinanceMath.Fee(amount, b.withdrawFee * f, b.withdrawFeeMin * f, 0);
        }

        public int AtmRemainingToday()
        {
            if (atmDay != Today) return HSettings.GetInt("bank.atmDailyLimit");
            return Math.Max(0, HSettings.GetInt("bank.atmDailyLimit") - (int)(atmTakenMicro / Money.PerSilver));
        }

        /// <summary>The most silver (whole coins) that can be withdrawn right now at this site: the balance minus the fee, and at an ATM what is left of the daily limit.</summary>
        public int MaxWithdrawCoins(BankSite site)
        {
            if (!hasAccount) return 0;
            double bal = Math.Floor(Balance.Silver);
            int coins = TransferMath.ClampToCoins(bal);
            for (int i = 0; i < 40 && coins > 0; i++)
            {
                var fee = WithdrawFee(Money.FromSilver(coins), site);
                var over = Money.FromSilver(coins) + fee - Balance;
                if (over.Micro <= 0) break;
                coins -= Math.Max(1, (int)Math.Ceiling(over.Silver));
            }
            if (coins < 0) coins = 0;
            if (site == BankSite.Atm) coins = Math.Min(coins, Math.Max(0, AtmRemainingToday()));
            return coins;
        }

        public bool HasInstantSite(Map map) => map != null && map.listerBuildings.allBuildingsColonist.Any(b => b is Building_SLTerminal t && t.Powered);

        // ------------------------------------------------------------------------------------------------
        // Deposit / withdraw (physical silver)
        // ------------------------------------------------------------------------------------------------
        /// <summary>Takes <paramref name="coins"/> silver from the map's storage (like a trade) and puts it on the account.
        /// At a counter or ATM it is booked at once, otherwise after the deposit delay.</summary>
        public bool TryDeposit(Map map, int coins, BankSite site, out string why)
        {
            why = null;
            if (!hasAccount) { why = L.T("Reason.NoAccount", "There is no account yet."); return false; }
            if (coins <= 0) { why = L.T("Reason.Amount", "Choose an amount first."); return false; }
            if (map == null) { why = "no map"; return false; }
            if (BankSilver.DepositableCoins(map) < coins) { why = L.T("Reason.NoSilver", "Not enough silver in storage or at the trade beacon ({0} available).", BankSilver.DepositableCoins(map)); return false; }
            if (!BankSilver.TakeCoins(map, coins)) { why = L.T("Reason.NoSilver", "Not enough silver in storage or at the trade beacon ({0} available).", BankSilver.DepositableCoins(map)); return false; }
            var amount = Money.FromSilver(coins);
            bool instant = site != BankSite.Remote || HasInstantSite(map);
            if (instant) Credit("world", amount, L.T("Memo.Deposit", "Deposit (silver)"), Bank?.depositFee * FeeFactor ?? 0);
            else
            {
                Ledger.Current.Post("world", Transit, amount, L.T("Memo.DepositTransit", "Deposit in transit"));
                pending.Add(new PendingTransfer
                {
                    Kind = TransferKind.Deposit, Micro = amount.Micro, MapId = map.uniqueID, Memo = L.T("Memo.Deposit", "Deposit (silver)"),
                    DueTick = Now + (int)(HSettings.Get("bank.depositDelayHours") * 2500f), FeeRate = Bank?.depositFee * FeeFactor ?? 0
                });
            }
            InvalidateCache();
            return true;
        }

        /// <summary>Pays out silver: instantly at an ATM or counter, otherwise by drop pod after a few hours.</summary>
        public bool TryWithdraw(Map map, int coins, BankSite site, out string why, Building at = null)
        {
            why = null;
            if (!hasAccount) { why = L.T("Reason.NoAccount", "There is no account yet."); return false; }
            if (coins <= 0) { why = L.T("Reason.Amount", "Choose an amount first."); return false; }
            if (map == null) { why = "no map"; return false; }
            var amount = Money.FromSilver(coins);
            var fee = WithdrawFee(amount, site);
            if (Balance < amount + fee) { why = L.T("Reason.Funds", "The account does not cover {0} plus {1} in fees.", amount.ToStringShort(), fee.ToStringShort()); return false; }
            if (site == BankSite.Atm)
            {
                if (atmDay != Today) { atmDay = Today; atmTakenMicro = 0; }
                if (coins > AtmRemainingToday()) { why = L.T("Reason.AtmLimit", "The ATM will only hand out {0} more today.", AtmRemainingToday()); return false; }
                atmTakenMicro += amount.Micro;
            }
            var led = Ledger.Current;
            if (fee.Micro > 0) { led.Post(Colony, "world", fee, L.T("Memo.WithdrawFee", "Fee: withdrawal")); totalFeesMicro += fee.Micro; }
            if (site != BankSite.Remote || HasInstantSite(map))
            {
                led.Post(Colony, "world", amount, L.T("Memo.Withdraw", "Withdrawal (cash)"));
                IntVec3 cell = at != null ? (at.InteractionCell.IsValid ? at.InteractionCell : at.Position) : BankSilver.PodTarget(map);
                BankSilver.PlaceAt(map, cell, coins);
            }
            else
            {
                led.Post(Colony, Transit, amount, L.T("Memo.WithdrawPod", "Withdrawal by drop pod"));
                float hrs = Rand.Range(HSettings.Get("bank.podMinHours"), Math.Max(HSettings.Get("bank.podMinHours"), HSettings.Get("bank.podMaxHours")));
                pending.Add(new PendingTransfer { Kind = TransferKind.Withdraw, Micro = amount.Micro, MapId = map.uniqueID, DueTick = Now + (int)(hrs * 2500f), Memo = "pod" });
            }
            InvalidateCache();
            return true;
        }

        // ------------------------------------------------------------------------------------------------
        // Crediting (shared by deposits, tills, API)
        // ------------------------------------------------------------------------------------------------
        /// <summary>Books <paramref name="gross"/> from <paramref name="fromAccount"/> onto the colony account: fee first, then, at collection stage 3 or higher,
        /// half goes to the overdue loan. Returns what reached the account.</summary>
        public Money Credit(string fromAccount, Money gross, string memo, double feeRate)
        {
            var led = Ledger.Current; if (led == null || gross.Micro <= 0) return Money.Zero;
            var fee = gross * Math.Max(0, feeRate);
            if (fee.Micro > 0) { led.Post(fromAccount, "world", fee, "Fee: " + memo); totalFeesMicro += fee.Micro; }
            var net = gross - fee;
            var offset = Money.Zero;
            if (BankSettings.MaxCollectionStage >= 3)
            {
                foreach (var l in loans.Where(x => x.InArrears && x.Stage >= 3).ToList())
                {
                    var take = Money.Min(net * 0.5 - offset, l.Arrears);
                    if (take.Micro <= 0) break;
                    offset += take;
                    ApplyPayment(l, take, fromAccount, L.T("Memo.Offset", "Collections offset"), false);
                }
            }
            var toAccount = net - offset;
            if (toAccount.Micro > 0) led.Post(fromAccount, Colony, toAccount, memo);
            InvalidateCache();
            return toAccount;
        }

        // ------------------------------------------------------------------------------------------------
        // Ticking
        // ------------------------------------------------------------------------------------------------
        public override void GameComponentTick()
        {
            if (!hasAccount) return;
            int t = Find.TickManager.TicksGame;
            bool fast = t % 60 == 7, slow = t % 250 == 17;
            if (!fast && !slow) return;
            if (!Modules.On(ModuleId.Bank)) return;
            Guard.Run(ModuleId.Bank, "BankGameComp.Tick", () => TickBody(t, fast, slow));
        }

        void TickBody(int t, bool fast, bool slow)
        {
            if (fast) ProcessPending(t);
            if (slow)
            {
                ProcessLoanDues(t);
                UpdateTier(t);
                int day = t / 60000;
                if (day != lastDay) RollDays(day);
            }
        }

        void ProcessPending(int t)
        {
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var p = pending[i];
                if (t < p.DueTick) continue;
                pending.RemoveAt(i);
                var led = Ledger.Current;
                if (p.Kind == TransferKind.Deposit)
                {
                    Credit(Transit, p.Amount, p.Memo ?? "Deposit", p.FeeRate);
                    Messages.Message(L.T("Msg.DepositArrived", "{0} silver was credited to your account.", p.Amount.ToStringShort()), MessageTypeDefOf.NeutralEvent, false);
                }
                else
                {
                    var map = Find.Maps.FirstOrDefault(m => m.uniqueID == p.MapId && m.IsPlayerHome) ?? Find.AnyPlayerHomeMap;
                    if (map == null) { led.Post(Transit, Colony, p.Amount, "Withdrawal returned (no map)"); continue; }
                    int coins = p.Amount.WholeSilver;
                    led.Post(Transit, "world", p.Amount, L.T("Memo.WithdrawCash", "Withdrawal (cash)"));
                    BankSilver.DropPods(map, BankSilver.PodTarget(map), coins);
                    Messages.Message(L.T("Msg.PodsComing", "{0} silver is on its way down in drop pods.", coins), new LookTargets(BankSilver.PodTarget(map), map), MessageTypeDefOf.PositiveEvent, false);
                }
                InvalidateCache();
            }
        }

        void UpdateTier(int t)
        {
            double scale = HSettings.Get("bank.moodScale") * Math.Max(1.0, Find.AnyPlayerHomeMap == null ? 1 : Find.AnyPlayerHomeMap.mapPawns.FreeColonistsSpawnedCount / 3.0);
            int cand = FinanceMath.WealthTier(NetWorth.Silver, scale);
            if (cand != candidateTier) { candidateTier = cand; candidateSince = t; }
            shownTier = FinanceMath.StableTier(shownTier, candidateTier, candidateSince, t);
        }

        void AddSample(int day)
        {
            samples.RemoveAll(s => s.Day == day);
            samples.Add(new DaySample { Day = day, Balance = Balance.Micro, NetWorth = NetWorth.Micro });
            while (samples.Count > 120) samples.RemoveAt(0);
        }

        /// <summary>Live points for the chart: one per saved day plus now.</summary>
        public List<KeyValuePair<int, double>> ChartPoints(int days)
        {
            var res = new List<KeyValuePair<int, double>>();
            int today = Today;
            foreach (var s in samples) if (s.Day >= today - days && s.Day < today) res.Add(new KeyValuePair<int, double>(s.Day, new Money(s.Balance).Silver));
            res.Add(new KeyValuePair<int, double>(today, Balance.Silver));
            return res;
        }

        /// <summary>Daily work: interest, loan interest, history, overdue loans, quadrum fee, Black Ledger risk, bailout.</summary>
        void RollDays(int day)
        {
            int from = Math.Max(lastDay + 1, day - 30);
            for (int d = from; d <= day; d++) DailyStep(d);
            lastDay = day;
        }

        public void DailyStep(int day)
        {
            var led = Ledger.Current; var bank = Bank;
            InvalidateCache();
            // 1. interest on the account: daily factor = M_acc^(1/60), M_acc = 1 + share*bankMod*(M-1) (a savings formula, F21; KONZEPT 8.8, D17)
            double f = FinanceMath.DailyFactor(BankSettings.AccountMultiple(bank?.interestMod ?? 1.0));
            var bal = Balance;
            var interest = FinanceMath.InterestForDay(bal, f);
            if (interest.Micro > 0) { led.Post("world", Colony, interest, L.T("Memo.Interest", "Interest")); interestQuadrumMicro += interest.Micro; totalInterestMicro += interest.Micro; }
            else if (interest.Micro < 0) { led.Post(Colony, "world", -interest, L.T("Memo.NegInterest", "Negative interest")); interestQuadrumMicro += interest.Micro; totalInterestMicro += interest.Micro; }
            // 2. interest on loans
            AccrueLoanInterest();
            Collections.Housekeeping(this);
            // 3. quadrum fee and risks
            if (day / 15 != lastQuadrum) { lastQuadrum = day / 15; QuadrumStep(); }
            // 4. overdue loans and the collection ladder
            ProcessOverdue(day);
            // 5. bailout
            TryBailout(day);
            // 6. history
            InvalidateCache();
            AddSample(day);
        }

        void QuadrumStep()
        {
            var led = Ledger.Current; var bank = Bank; if (bank == null) return;
            var fee = Money.FromSilver(bank.accountFeePerQuadrum * FeeFactor);
            if (fee.Micro > 0)
            {
                var pay = Money.Min(fee, Balance);
                if (pay.Micro > 0) { led.Post(Colony, "world", pay, L.T("Memo.AccountFee", "Account fee")); totalFeesMicro += pay.Micro; }
            }
            interestLastQuadrumMicro = interestQuadrumMicro; interestQuadrumMicro = 0;
            if (bank.seizureChancePerQuadrum > 0 && Balance.Micro > 0)
            {
                if (JRand.Sealed("bank.seizure").NextDouble() < bank.seizureChancePerQuadrum) SeizeAccount();
            }
        }

        /// <summary>The Black Ledger "loses" a share of the account.</summary>
        public Money SeizeAccount()
        {
            var share = HSettings.Get("bank.seizureShare");
            var taken = Balance * share;
            if (taken.Micro <= 0) return Money.Zero;
            Ledger.Current.Post(Colony, "world", taken, L.T("Memo.Seized", "Seized"));
            Find.LetterStack.ReceiveLetter(L.T("Letter.SeizedTitle", "Ledger audit"),
                L.T("Letter.Seized", "A clerk from {0} regrets to inform you that {1} silver of your balance has been \"reallocated for administrative reasons\". There is no appeal form. There is, however, a very nice pen.", BankName, taken.ToStringShort()), LetterDefOf.NegativeEvent);
            InvalidateCache();
            return taken;
        }

        public int LastBailoutTick => lastBailoutTick;

        void TryBailout(int day)
        {
            var bank = Bank;
            if (bank == null || !bank.bailout || !HSettings.GetBool("bank.bailout")) return;
            if (Now - lastBailoutTick < 60 * 60000) return;
            var net = NetWorth.Silver;
            if (net >= 0) return;
            double peak = samples.Where(s => s.Day >= day - 60).Select(s => new Money(s.NetWorth).Silver).DefaultIfEmpty(0).Max();
            if (peak < FinanceMath.TierLimits[FinanceMath.TierLimits.Length - 1]) return;   // was not Too Big To Fail in the last 60 days
            double amt = FinanceMath.BailoutAmount(net, peak);
            if (amt < 1) return;
            lastBailoutTick = Now;
            var principal = Money.FromSilver(Math.Floor(amt));
            var l = NewLoan(principal, 60, 0.0, true);
            Ledger.Current.Post(LoansAccount, Colony, principal, L.T("Memo.Bailout", "Bailout loan"), true);
            Find.LetterStack.ReceiveLetter(L.T("Letter.BailoutTitle", "Bailout"),
                L.T("Letter.Bailout", "{0} has decided that you are, in fact, too big to fail, and has rescued you with an interest-free loan of {1} silver. It runs for 60 days. This is a loan, not a gift. Nobody ever reads that part.", BankName, principal.ToStringShort()), LetterDefOf.PositiveEvent);
            HLog.Info("Bank", $"Bailout of {principal} (net {net:0}, peak {peak:0})");
        }

        // ------------------------------------------------------------------------------------------------
        // Saving
        // ------------------------------------------------------------------------------------------------
        public override void ExposeData()
        {
            Scribe_Values.Look(ref hasAccount, "hasAccount");
            Scribe_Values.Look(ref bankDefName, "bank");
            Scribe_Values.Look(ref openedTick, "opened");
            Scribe_Collections.Look(ref pending, "pending", LookMode.Deep);
            Scribe_Collections.Look(ref loans, "loans", LookMode.Deep);
            Scribe_Values.Look(ref score, "score", FinanceMath.ScoreStart);
            Scribe_Values.Look(ref nextLoanId, "nextLoanId", 1);
            Scribe_Values.Look(ref lastDay, "lastDay", -1);
            Scribe_Values.Look(ref lastQuadrum, "lastQuadrum", -1);
            Scribe_Collections.Look(ref samples, "samples", LookMode.Deep);
            Scribe_Values.Look(ref shownTier, "tier", 4);
            Scribe_Values.Look(ref candidateTier, "candTier", 4);
            Scribe_Values.Look(ref candidateSince, "candSince");
            Scribe_Values.Look(ref atmDay, "atmDay", -1);
            Scribe_Values.Look(ref atmTakenMicro, "atmTaken");
            Scribe_Values.Look(ref lastBailoutTick, "lastBailout", -100_000_000);
            Scribe_Values.Look(ref interestQuadrumMicro, "intQ"); Scribe_Values.Look(ref interestLastQuadrumMicro, "intLQ");
            Scribe_Values.Look(ref totalInterestMicro, "intTotal"); Scribe_Values.Look(ref totalFeesMicro, "feesTotal");
            Scribe_Collections.Look(ref collectors, "collectors", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                pending = pending ?? new List<PendingTransfer>();
                loans = loans ?? new List<Loan>();
                samples = samples ?? new List<DaySample>();
                collectors = collectors ?? new List<Pawn>();
                collectors.RemoveAll(p => p == null);
                Current = this;
            }
        }

        public override void StartedNewGame() { Current = this; }
        public override void LoadedGame() { Current = this; }
    }
}
