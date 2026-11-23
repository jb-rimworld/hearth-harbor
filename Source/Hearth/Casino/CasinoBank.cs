// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Hearth.CasinoMath;

namespace Hearth.Casino
{
    /// <summary>
    /// Money of the casino (KONZEPT 6.6, 6.14). Guests buy in with real money (Payments), play with a virtual purse and cash out.
    /// The house account is "Venue.Account" inside a Casino venue room, else "house:casino:&lt;map&gt;". The house's surplus goes to the
    /// colony till when the casino closes. Colonists never touch real money: they play with house chips.
    /// </summary>
    public static class CasinoBank
    {
        public static string HouseAccountFor(Thing device, out Venue venue, out string label)
        {
            venue = null; label = null;
            if (device?.Map == null) return "house:casino:0";
            try
            {
                venue = Venues.At(device.Position, device.Map);
                if (venue != null && venue.Type == CasinoDefOf.HH_Venue_Casino) { label = venue.Comp.DisplayName; return venue.Account; }
                venue = null;
            }
            catch (Exception e) { HLog.Once("casino.venueLookup", LogLevel.Warn, "Casino", "Venue lookup failed, using the map house account: " + e.Message); }
            label = "HH.Casino.HouseOfMap".Translate(device.Map.Parent?.LabelCap ?? "map");
            return "house:casino:" + device.Map.uniqueID;
        }

        public static Money Cash(string account) => Ledger.Current?.Balance(account) ?? Money.Zero;

        public static bool HasCage(Map map) => map != null && map.listerThings.ThingsOfDef(CasinoDefOf.HH_CashierCage).Count > 0;

        /// <summary>Casino devices that share one house account (for the ruin protection).</summary>
        public static int DevicesSharing(string account, Map map)
        {
            if (map == null) return 1;
            int n = 0;
            foreach (var def in GameDefs)
                foreach (var t in map.listerThings.ThingsOfDef(def))
                    if (t.TryGetComp<CompCasinoGame>() != null && HouseAccountFor(t, out _, out _) == account) n++;
            return Math.Max(1, n);
        }
        public static IEnumerable<ThingDef> GameDefs { get { yield return CasinoDefOf.HH_SlotMachine; yield return CasinoDefOf.HH_RouletteTable; yield return CasinoDefOf.HH_BlackjackTable; yield return CasinoDefOf.HH_PokerTable; } }

        public static IEnumerable<CompCasinoGame> AllDevices(Map map)
        {
            foreach (var def in GameDefs)
                foreach (var t in map.listerThings.ThingsOfDef(def))
                { var c = t.TryGetComp<CompCasinoGame>(); if (c != null) yield return c; }
        }

        // Limits ----------------------------------------------------------------------------------------------------------
        /// <summary>Highest stake the device accepts from this kind of player right now (silver).</summary>
        public static double MaxStake(CompCasinoGame comp, SessionKind kind)
        {
            double hi = double.MaxValue;
            if (CS.B(CS.LimitsOn)) hi = CS.F(CS.MaxBet);
            if (kind == SessionKind.Guest && CS.B(CS.RiskLimit))
            {
                string account = HouseAccountFor(comp.parent, out _, out _);
                var c = CasinoComponent.Get();
                double cash = Math.Max(0, (Cash(account) - (c?.OutstandingAt(account) ?? Money.Zero)).Silver);
                double perDay = Math.Max(CS.RoundsPerHour(comp.Game) * 4.0, RoundsYesterday(account, comp.Game));
                double limit = HouseRisk.MaxBet(cash, comp.SigmaPerStake, perDay, DevicesSharing(account, comp.parent.Map));
                hi = Math.Min(hi, Math.Max(limit, CS.F(CS.MinBet)));
            }
            return Math.Max(hi, CS.F(CS.MinBet));
        }

        static double RoundsYesterday(string account, CasinoGame g)
        {
            var c = CasinoComponent.Get(); if (c == null) return 0;
            int day = Find.TickManager.TicksGame / 60000;
            return c.Rows.Where(r => r.Account == account && r.Game == (int)g && (r.Day == day || r.Day == day - 1)).Select(r => (double)r.Rounds).DefaultIfEmpty(0).Max();
        }

        // Guest bankroll ----------------------------------------------------------------------------------------------------
        /// <summary>Silver a guest would bring to the tables: share of the budget (personality), minus a reserve for food, in whole silver.</summary>
        public static Money Bankroll(Pawn guest, GuestInfo info, Map map)
        {
            if (info == null) return Money.Zero;
            double budget = info.Budget.Silver;
            double share = CS.F(CS.GuestShare) * PersonalityFactor(guest);
            if (HasCage(map)) share *= 1.0 + CS.F(CS.ChipBonus);
            double reserve = Math.Min(20, budget * 0.2);
            double bank = Math.Min(budget * share, budget - reserve);
            return Money.FromSilver(Math.Floor(Math.Max(0, bank)));
        }

        public static double PersonalityFactor(Pawn p)
        {
            double f = 1.0;
            var t = p.story?.traits; if (t == null) return f;
            if (t.HasTrait(TraitDefOf.Greedy)) f *= 1.5;
            var ascetic = DefDatabase<TraitDef>.GetNamedSilentFail("Ascetic"); if (ascetic != null && t.HasTrait(ascetic)) f *= 0.4;
            if (Hearth.Dlc.DlcDefs.Gambler != null && t.HasTrait(Hearth.Dlc.DlcDefs.Gambler)) f *= 2.0;
            if (Hearth.Dlc.LuckApi.IsHighRoller(p)) f *= 1.5;
            if (p.Ideo != null) { /* Ideology "Gambling" precept arrives with the Ideology module; nothing to read yet */ }
            return f;
        }

        public static float StakeShareFor(Pawn p)
        {
            double s = CS.F(CS.GuestBetShare);
            var t = p.story?.traits;
            if (t != null)
            {
                if (t.HasTrait(TraitDefOf.Greedy)) s *= 1.5;
                if (Hearth.Dlc.DlcDefs.Gambler != null && t.HasTrait(Hearth.Dlc.DlcDefs.Gambler)) s *= 2.0;
                var nerves = DefDatabase<TraitDef>.GetNamedSilentFail("Nerves"); if (nerves != null && t.HasTrait(nerves) && t.DegreeOfTrait(nerves) >= 1) s *= 0.8;
            }
            if (Hearth.Dlc.LuckApi.IsHighRoller(p)) s *= 1.5;   // high roller gene: bets bigger
            return (float)s;
        }

        // Buy-in and cash-out --------------------------------------------------------------------------------------------
        public static bool TryBuyIn(GamblerSession s, Money amount, string account)
        {
            var info = GuestApi.Info(s.Pawn);
            if (info == null || amount.Micro <= 0) return false;
            GuestApi.EnsureFunds(s.Pawn, amount);
            if (info.Budget < amount) return false;
            if (!Payments.TryCharge(s.Pawn, amount, account, "Casino buy-in: " + s.Pawn.LabelShortCap, out _))
            {
                // Vanilla visitors that carry no silver gamble "virtually" (same rule as GuestApi.ChargeGuest)
                bool innActive = GuestApi.Impl != null && Modules.On(ModuleId.Inn);
                if (innActive) return false;
                Ledger.Current?.Post("world", account, amount, "Casino buy-in: " + s.Pawn.LabelShortCap + " (visitor)");
                s.VirtualMoney = true;
            }
            GuestApi.Spend(s.Pawn, amount, "casino");
            return true;
        }

        /// <summary>Settles a guest session: returns the purse (whole coins, stochastic rounding) and books the house result.</summary>
        public static Money CashOut(GamblerSession s)
        {
            var ledger = Ledger.Current;
            Money purse = s.Purse < Money.Zero ? Money.Zero : s.Purse;
            if (s.Kind != SessionKind.Guest || ledger == null || purse.Micro <= 0) return Money.Zero;
            var rng = JRand.Sealed("casino.cashout");
            Money ret = purse;
            int chipsGiven = 0;
            Money win = purse - s.BuyIn;
            if (s.Chips && win.IsPositive)
            {
                // a few winnings stay with the guest as chips that are worthless outside the house (chip float, KONZEPT 6.14)
                double keep = CS.F(CS.ChipTakeHome) * (0.33 + 0.67 * rng.NextDouble());
                Money kept = win * keep;
                chipsGiven = (int)Math.Floor(kept.Silver);
                ret = purse - Money.FromSilver(chipsGiven);
            }
            double whole = Math.Floor(ret.Silver), frac = ret.Silver - whole;
            int coins = (int)whole + (rng.Chance(frac) ? 1 : 0);
            Money returned = Money.FromSilver(coins);
            string account = s.HouseAccount;
            if (coins > 0)
            {
                EnsureCovered(account, returned, s.Pawn?.MapHeld ?? s.Device?.Map);
                ledger.Post(account, "world", returned, "Casino cash-out: " + s.Pawn?.LabelShortCap, allowNegative: true);
                if (!s.VirtualMoney && s.Pawn != null && !s.Pawn.Dead)
                {
                    var silver = ThingMaker.MakeThing(ThingDefOf.Silver); silver.stackCount = coins;
                    if (!s.Pawn.inventory.innerContainer.TryAdd(silver)) { silver.Destroy(); ledger.Post("world", account, returned, "Casino cash-out refused (inventory full)", allowNegative: true); returned = Money.Zero; coins = 0; }
                }
            }
            if (chipsGiven > 0 && s.Pawn != null && !s.Pawn.Dead)
            {
                var chips = ThingMaker.MakeThing(CasinoDefOf.HH_CasinoChips); chips.stackCount = chipsGiven;
                if (!s.Pawn.inventory.innerContainer.TryAdd(chips)) chips.Destroy();
            }
            var info = s.Pawn != null ? GuestApi.Info(s.Pawn) : null;
            if (info != null && coins > 0) { info.Budget += returned; info.Spent = Money.Max(Money.Zero, info.Spent - returned); }
            return returned;
        }

        /// <summary>If the house drawer cannot pay, the colony till covers it first (a bad night costs the colony real income); only then the account goes negative.</summary>
        static void EnsureCovered(string account, Money amount, Map map)
        {
            var l = Ledger.Current; var bal = l.Balance(account);
            if (bal >= amount) return;
            Money missing = amount - bal;
            string till = Payments.ColonyTill(map);
            Money take = Money.Min(missing, l.Balance(till));
            if (take.IsPositive) l.Post(till, account, take, "Casino: till covers a payout", false, "casino");
            if (l.Balance(account) < amount) HLog.Once("casino.negative:" + account, LogLevel.Info, "Casino", "House account " + account + " pays out more than it holds; it will recover from the next guests");
        }

        // Float and closing -----------------------------------------------------------------------------------------------
        /// <summary>Top up the drawer from the till (when the till has money) so the house can pay out.</summary>
        public static void EnsureFloat(Map map, string account)
        {
            var l = Ledger.Current; if (l == null || map == null) return;
            Money target = CS.F(CS.HouseFloat) > 0 ? HSettings.GetSilver(CS.HouseFloat) : Money.Zero;
            var bal = l.Balance(account);
            if (bal >= target) return;
            string till = Payments.ColonyTill(map);
            Money take = Money.Min(target - bal, l.Balance(till));
            if (take.IsPositive) l.Post(till, account, take, "Casino float from the till", false, "casino");
        }

        /// <summary>Moves everything above float + what guests currently play with into the colony till. Returns the amount swept.</summary>
        public static Money Sweep(HouseInfo h)
        {
            var l = Ledger.Current; var c = CasinoComponent.Get(); if (l == null || c == null) return Money.Zero;
            var map = Find.Maps.FirstOrDefault(m => m.uniqueID == h.MapId); if (map == null) return Money.Zero;
            Money keep = HSettings.GetSilver(CS.HouseFloat) + c.OutstandingAt(h.Account);
            Money bal = l.Balance(h.Account);
            Money surplus = bal - keep;
            if (!surplus.IsPositive) { h.Dirty = false; return Money.Zero; }
            if (l.Post(h.Account, Payments.ColonyTill(map), surplus, "Casino takings (" + h.Label + ")", false, "casino")) { h.Dirty = false; h.LastSweepDay = Find.TickManager.TicksGame / 60000; return surplus; }
            return Money.Zero;
        }

        /// <summary>Called every 250 ticks: sweeps houses whose venue is closed (or, without a venue, at midnight).</summary>
        public static void ClosingCheck()
        {
            var c = CasinoComponent.Get(); if (c == null) return;
            int now = Find.TickManager.TicksGame;
            foreach (var h in c.Houses.ToList())
            {
                if (!h.Dirty) continue;
                var map = Find.Maps.FirstOrDefault(m => m.uniqueID == h.MapId); if (map == null) continue;
                bool closed;
                var venue = Venues.All(map).FirstOrDefault(v => v.Account == h.Account);
                if (venue != null) closed = !venue.IsOpenNow;
                else closed = GenLocalDate.HourOfDay(map) == 0;
                if (closed && now - h.LastActivityTick > 250) Guard.Run(ModuleId.Casino, "Casino.Sweep", () => Sweep(h));
            }
        }
    }
}
