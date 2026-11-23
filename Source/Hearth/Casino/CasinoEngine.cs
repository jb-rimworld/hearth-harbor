// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Hearth.CasinoMath;

namespace Hearth.Casino
{
    /// <summary>Plays the rounds: draws results from the sealed random streams, moves purses, books the numbers, ends sessions.</summary>
    public static class CasinoEngine
    {
        const double StakeStep = 0.05;

        // Sessions --------------------------------------------------------------------------------------------------------
        /// <summary>Starts a session for a pawn at a device. Returns null (with a reason) if it cannot start.</summary>
        public static GamblerSession Begin(Pawn pawn, CompCasinoGame comp, SessionKind kind, bool spree, out string failure)
        {
            failure = null;
            var c = CasinoComponent.Get();
            if (c == null || comp == null || pawn == null) { failure = "no component"; return null; }
            if (c.SessionOf(pawn) != null) { failure = "already playing"; return null; }
            if (!comp.Usable(kind == SessionKind.Guest, out failure)) return null;
            if (comp.Seated >= comp.Props.seats) { failure = "table full"; return null; }
            foreach (var other in c.SessionsAt(comp.parent)) if (other.Kind != kind) { failure = "table is used by " + other.Kind; return null; }
            if (kind != SessionKind.Demo && !pawn.DevelopmentalStage.Adult()) { failure = "children do not gamble"; return null; }
            if (kind != SessionKind.Demo && !Hearth.Dlc.LuckApi.WillGamble(pawn)) { failure = "gambling-averse"; return null; }

            int now = Find.TickManager.TicksGame;
            var s = new GamblerSession { Pawn = pawn, Device = comp.parent, Kind = kind, Spree = spree, StartTick = now, StakeShare = 0.10f };
            if (kind == SessionKind.Guest)
            {
                var info = GuestApi.Info(pawn);
                string account = CasinoBank.HouseAccountFor(comp.parent, out var venue, out string label);
                var bank = CasinoBank.Bankroll(pawn, info, comp.parent.Map);
                if (bank < HSettings.GetSilver(CS.GuestMinBuyIn)) { failure = "bankroll too small"; return null; }
                CasinoBank.EnsureFloat(comp.parent.Map, account);
                s.HouseAccount = account; s.Chips = CasinoBank.HasCage(comp.parent.Map);
                if (!CasinoBank.TryBuyIn(s, bank, account)) { failure = "buy-in refused"; return null; }
                s.BuyIn = bank; s.Purse = bank;
                s.StakeShare = CasinoBank.StakeShareFor(pawn);
                s.EndTick = now + Rand.RangeInclusive(1500, 3200);
                var h = c.HouseOf(account, comp.parent.Map, label); h.LastActivityTick = now; h.Dirty = true;
            }
            else
            {
                s.HouseAccount = "chips:" + comp.parent.Map.uniqueID;
                s.BuyIn = HSettings.GetSilver(CS.ColonistChips); s.Purse = s.BuyIn;
                s.StakeShare = 0.012f; s.EndTick = now + 9000;
            }
            s.Risk = RiskOf(pawn);
            c.Sessions.Add(s);
            return s;
        }

        static float RiskOf(Pawn p)
        {
            float r = 0.4f; var t = p.story?.traits; if (t == null) return r;
            if (t.HasTrait(TraitDefOf.Greedy)) r += 0.3f;
            var d = DefDatabase<TraitDef>.GetNamedSilentFail("DrugDesire"); if (d != null && t.HasTrait(d)) r += 0.15f;
            return Mathf.Clamp01(r);
        }

        public static void EndGuestSessions(CompCasinoGame comp, string reason)
        {
            var c = CasinoComponent.Get(); if (c == null) return;
            foreach (var s in c.SessionsAt(comp.parent).Where(x => x.Kind == SessionKind.Guest).ToList()) EndSession(s, reason);
        }

        public static void EndSession(GamblerSession s, string reason)
        {
            if (s == null || s.Done) return;
            s.Done = true; s.EndReason = reason;
            var c = CasinoComponent.Get();
            try
            {
                if (s.Kind == SessionKind.Guest)
                {
                    CasinoBank.CashOut(s);
                    float d = s.Net.IsPositive ? 0.08f : s.Net.IsNegative && s.Net.Silver < -0.4 * s.BuyIn.Silver ? -0.06f : s.Net.IsNegative ? -0.02f : 0f;
                    if (s.Pawn != null && d != 0f) GuestApi.AddSatisfaction(s.Pawn, d, "casino: " + (s.Net.IsPositive ? "won" : "lost"));
                    var h = c?.Houses.Find(x => x.Account == s.HouseAccount); if (h != null) { h.Dirty = true; h.LastActivityTick = Find.TickManager.TicksGame; }
                }
                else if (s.Kind == SessionKind.Colonist && s.Pawn != null) Gambling.OnColonistSessionEnd(s);
                // luck thoughts (11.1): a session counts as one bet, won when it ends above the buy-in
                if (s.Kind != SessionKind.Demo && s.Pawn != null && s.Rounds > 0 && Modules.On(ModuleId.Casino)) Hearth.Dlc.LuckApi.NoteResult(s.Pawn, s.Net.IsPositive);
            }
            finally { c?.Sessions.Remove(s); }
            HLog.Debug("Casino", $"Session of {s.Pawn?.LabelShort} ended ({reason}): {s.Rounds} rounds, net {s.Net}");
        }

        // Stakes ---------------------------------------------------------------------------------------------------------------
        public static Money NextStake(GamblerSession s, CompCasinoGame comp, bool doublingGame = false)
        {
            double min = CS.F(CS.MinBet);
            double max = CasinoBank.MaxStake(comp, s.Kind);
            double want = s.BuyIn.Silver * s.StakeShare * (0.8 + 0.5 * JRand.Stream("casino.stake").NextDouble());
            double stake = Math.Max(min, Math.Min(max, want));
            stake = Math.Round(stake / StakeStep) * StakeStep;
            double room = s.Purse.Silver / (doublingGame ? 2.0 : 1.0);
            if (stake > room) stake = Math.Floor(room / StakeStep) * StakeStep;
            if (stake < StakeStep - 1e-9) return Money.Zero;
            return Money.FromSilver(stake);
        }

        public static double AccuracyOf(Pawn p)
        {
            if (p == null || p.skills == null) return 0.6;
            double cons = p.health?.capacities?.GetLevel(PawnCapacityDefOf.Consciousness) ?? 1.0;
            // number sense and gamblers (HH_TableSkill) count cards as if that many Intellectual levels better
            int intel = Math.Min(20, p.skills.GetSkill(SkillDefOf.Intellectual).Level + (int)Math.Round(Hearth.Dlc.DlcApi.TableSkillBonus(p)));
            return BlackjackSolver.AccuracyFor(intel, p.skills.GetSkill(SkillDefOf.Social).Level, Math.Min(1.0, cons));
        }

        // Luck (11.1) ----------------------------------------------------------------------------------------------------------
        /// <summary>Factor on every payout of a house-banked game for this player: Lucky and Unlucky pawns play with a payout plan
        /// shifted by dlc.luckShift (adjusted RTP / base RTP). The random draw itself is never touched. 1 for demo play and plain pawns.</summary>
        public static double LuckFactor(GamblerSession s, double baseRtp)
        {
            if (s == null || s.Kind == SessionKind.Demo || s.Pawn == null || baseRtp <= 0) return 1.0;
            double shift = Hearth.Dlc.LuckApi.RtpShiftFor(s.Pawn);
            if (shift == 0) return 1.0;
            return DlcMath.LuckRtp(baseRtp, shift) / baseRtp;
        }

        /// <summary>The payout plan (RTP) this player is dealt at a device: the game's plan with the luck shift (shown in the Pit Boss view).</summary>
        public static double PlayerRtp(CompCasinoGame comp, Pawn p)
        {
            double b = 1 - comp.ExpectedEdge;
            double shift = p == null ? 0 : Hearth.Dlc.LuckApi.RtpShiftFor(p);
            return shift == 0 || comp.Game == CasinoGame.Poker ? b : DlcMath.LuckRtp(b, shift);
        }

        static readonly Dictionary<int, (int tick, float f)> dealSpeed = new Dictionary<int, (int, float)>();
        static CasinoEngine() { HearthGame.OnGameInit += () => dealSpeed.Clear(); }

        /// <summary>Steady hands and dexterous fingers (HH_Dexterity): a croupier deals faster, 3 % shorter rounds per point (at most 30 %).
        /// Cached per croupier for 2500 ticks, because the device asks every tick.</summary>
        public static float DealSpeedFactor(Pawn croupier)
        {
            if (croupier == null) return 1f;
            int now = Find.TickManager.TicksGame;
            if (dealSpeed.TryGetValue(croupier.thingIDNumber, out var e) && now - e.tick < 2500 && now >= e.tick) return e.f;
            float dex = Hearth.Dlc.DlcApi.DexterityBonus(croupier);
            float f = Mathf.Clamp(1f - 0.03f * dex, 0.7f, 1f);
            dealSpeed[croupier.thingIDNumber] = (now, f);
            return f;
        }

        /// <summary>Last luck factor applied to a payout (tests read it).</summary>
        public static double LastLuckFactor = 1.0;

        static Money Lucky(GamblerSession s, double rawPayoutSilver, double baseRtp, out double factor)
        {
            factor = LuckFactor(s, baseRtp);
            if (s != null && s.Kind != SessionKind.Demo) LastLuckFactor = factor;
            return Money.FromSilver(Math.Max(0, Math.Round(rawPayoutSilver * factor, 5)));
        }

        // Rounds ---------------------------------------------------------------------------------------------------------------
        public static void RunRound(CompCasinoGame comp, List<GamblerSession> sessions)
        {
            if (sessions.Count == 0) return;
            switch (comp.Game)
            {
                case CasinoGame.Slots: foreach (var s in sessions) SlotSpin(comp, s); break;
                case CasinoGame.Roulette: RouletteSpin(comp, sessions); break;
                case CasinoGame.Blackjack: foreach (var s in sessions) BlackjackHand(comp, s); break;
                default: PokerHand(comp, sessions); break;
            }
            foreach (var s in sessions) CheckStop(comp, s);
        }

        static void CheckStop(CompCasinoGame comp, GamblerSession s)
        {
            if (s.Done || s.Kind == SessionKind.Demo) return;
            int now = Find.TickManager.TicksGame;
            if (s.Purse.Silver < CS.F(CS.MinBet) - 1e-9) { EndSession(s, s.Kind == SessionKind.Guest ? "broke" : "out of chips"); return; }
            if (s.Kind == SessionKind.Guest)
            {
                if (s.Purse.Silver <= s.BuyIn.Silver * (1 - CS.F(CS.GuestStopLoss))) EndSession(s, "lost enough");
                else if (s.Purse.Silver >= s.BuyIn.Silver * (1 + CS.F(CS.GuestWinTarget))) EndSession(s, "cashing out");
                else if (now >= s.EndTick) EndSession(s, "had enough time");
            }
            else if (now >= s.EndTick + 600) EndSession(s, "time");
        }

        /// <param name="bookStake">Stake shown in the books when it differs from the money put down (blackjack doubles count the first stake only).</param>
        static void Settle(CompCasinoGame comp, GamblerSession s, Money stake, Money payout, double expectedHouse, double variance, string result, Money? bookStake = null)
        {
            s.Purse = s.Purse - stake + payout;
            s.StakedMicro += stake.Micro; s.PaidMicro += payout.Micro; s.Rounds++;
            long bs = bookStake?.Micro ?? stake.Micro;
            Book(comp, s, bs, payout.Micro - (stake.Micro - bs), expectedHouse, variance, result);
            Celebrate(comp, s, stake, payout);
        }

        static void Book(CompCasinoGame comp, GamblerSession s, long staked, long paid, double expected, double variance, string result)
        {
            comp.Book(s, staked, paid, expected, variance, result);
            if (s.Kind == SessionKind.Demo) return;
            var c = CasinoComponent.Get(); if (c == null) return;
            var row = c.RowFor(Find.TickManager.TicksGame / 60000, s.HouseAccount, comp.Game);
            row.Rounds++; row.StakedMicro += staked; row.PaidMicro += paid; row.Expected += expected; row.Variance += variance;
        }

        static void Celebrate(CompCasinoGame comp, GamblerSession s, Money stake, Money payout)
        {
            if (s.Pawn == null || !s.Pawn.Spawned || stake.Micro <= 0) return;
            double mult = payout.Silver / stake.Silver;
            if (mult >= 20 && payout.Silver >= 5)
            {
                MoteMaker.ThrowText(s.Pawn.DrawPos + new Vector3(0, 0, 0.5f), s.Pawn.Map, "+" + payout.Silver.ToString("0") + (s.Kind == SessionKind.Colonist ? "" : " $"), new Color(1f, 0.85f, 0.2f), 2.5f);
                s.BestWinTick = Find.TickManager.TicksGame;
            }
            if (mult >= 100 && payout.Silver >= 25 && s.Kind != SessionKind.Demo)
                Messages.Message("HH.Casino.BigWin".Translate(s.Pawn.LabelShortCap, payout.Silver.ToString("0"), comp.parent.LabelNoCount), s.Pawn, MessageTypeDefOf.PositiveEvent, false);
        }

        static void SlotSpin(CompCasinoGame comp, GamblerSession s)
        {
            Money stake = NextStake(s, comp);
            if (stake.Micro <= 0) { EndSession(s, "broke"); return; }
            var table = comp.Paytable; var rng = JRand.Sealed("casino.slots");
            double mult = table.Spin(rng, out int a, out int b, out int c);
            Money payout = Lucky(s, stake.Silver * mult, table.Rtp, out double lf);
            string res = SlotPaytable.SymbolNames[a] + " · " + SlotPaytable.SymbolNames[b] + " · " + SlotPaytable.SymbolNames[c] + (mult > 0 ? "  → ×" + mult.ToString("0.##") : "");
            Settle(comp, s, stake, payout, stake.Silver * (1 - table.Rtp * lf), Math.Pow(table.Sigma * stake.Silver, 2), res);
        }

        static void RouletteSpin(CompCasinoGame comp, List<GamblerSession> sessions)
        {
            var rules = CS.Roulette; var rng = JRand.Sealed("casino.roulette");
            int pocket = rules.Spin(rng);
            string color = pocket == 0 || pocket == 37 ? "green" : RouletteBet.IsRed(pocket) ? "red" : "black";
            string res = "ball on " + RouletteBet.PocketName(pocket) + " (" + color + ")";
            foreach (var s in sessions)
            {
                if (s.Done) continue;
                Money stake = NextStake(s, comp);
                if (stake.Micro <= 0) { EndSession(s, "broke"); continue; }
                RouletteBet bet; int guard = 0;
                do { bet = RouletteBet.RandomBet(rng, s.Risk); } while (bet.Covers(37) && rules.Zeros == 1 && ++guard < 20);
                if (bet.Covers(37) && rules.Zeros == 1) bet = RouletteBet.Red();
                double ret = rules.Return(bet, pocket);
                Money payout = Lucky(s, stake.Silver * ret, 1 - rules.TargetEdge, out double lf);
                Settle(comp, s, stake, payout, stake.Silver * (1 - (1 - rules.TargetEdge) * lf), Math.Pow(rules.Sigma(bet) * stake.Silver, 2), res + " · " + bet.Label);
            }
        }

        static void BlackjackHand(CompCasinoGame comp, GamblerSession s)
        {
            Money stake = NextStake(s, comp, doublingGame: true);
            if (stake.Micro <= 0) { EndSession(s, "broke"); return; }
            var solver = BlackjackSolver.Get(CS.Blackjack, AccuracyOf(s.Pawn));
            var round = solver.PlayRound(JRand.Sealed("casino.blackjack"));
            double staked = stake.Silver * (round.Doubled ? 2 : 1);
            Money outlay = Money.FromSilver(staked);
            // the stake is returned with the net result: net = stake × Result (a double counts twice)
            Money payout = Lucky(s, Math.Max(0, staked + stake.Silver * round.Result), 1 - solver.HouseEdge, out double lf);
            string res = "you " + round.PlayerTotal + " · dealer " + round.DealerTotal + " (" + round.Outcome + ")";
            Settle(comp, s, outlay, payout, stake.Silver * (1 - (1 - solver.HouseEdge) * lf), Math.Pow(solver.Sigma * stake.Silver, 2), res, bookStake: stake);
        }

        static void PokerHand(CompCasinoGame comp, List<GamblerSession> sessions)
        {
            double rate = CS.PokerRakeRate, cap = CS.PokerCap;
            var players = sessions.Where(x => !x.Done).ToList(); if (players.Count == 0) return;
            // everyone antes the same: the smallest wanted stake at this table
            var stakes = players.ToDictionary(x => x, x => NextStake(x, comp));
            double ante = stakes.Values.Where(v => v.Micro > 0).Select(v => v.Silver).DefaultIfEmpty(0).Min();
            var seated = players.Where(x => stakes[x].Micro > 0).ToList();
            foreach (var x in players.Except(seated).ToList()) EndSession(x, "broke");
            if (seated.Count == 0 || ante <= 0) return;
            int total = Math.Max(comp.Props.seats, seated.Count);
            var skills = new List<double>();
            foreach (var x in seated) skills.Add(RimdeckMath.Skill(Math.Min(20, (x.Pawn?.skills?.GetSkill(SkillDefOf.Intellectual).Level ?? 8) + (int)Math.Round(Hearth.Dlc.DlcApi.TableSkillBonus(x.Pawn))), x.Pawn?.skills?.GetSkill(SkillDefOf.Social).Level ?? 8));
            var rng = JRand.Sealed("casino.poker");
            for (int i = seated.Count; i < total; i++) skills.Add(6.0 + 4.0 * rng.NextDouble());    // regulars who fill the table, funded by the house
            var hand = RimdeckMath.Deal(rng, skills, ante, rate, cap);
            Money anteM = Money.FromSilver(ante), potPayout = Money.FromSilver(Math.Round(hand.Payout, 5));
            double pReal = 0; for (int i = 0; i < seated.Count; i++) pReal += hand.Chances[i];
            double expected = 0; for (int i = 0; i < seated.Count; i++) expected -= RimdeckMath.ExpectedNet(skills, i, ante, rate, cap);
            double variance = hand.Payout * hand.Payout * pReal * (1 - pReal);
            long stakedTotal = 0, paidTotal = 0;
            for (int i = 0; i < seated.Count; i++)
            {
                var s = seated[i]; bool win = hand.Winner == i;
                s.Purse = s.Purse - anteM + (win ? potPayout : Money.Zero);
                s.StakedMicro += anteM.Micro; if (win) s.PaidMicro += potPayout.Micro; s.Rounds++;
                stakedTotal += anteM.Micro; if (win) paidTotal += potPayout.Micro;
                if (win) Celebrate(comp, s, anteM, potPayout);
            }
            string who = hand.Winner < seated.Count ? seated[hand.Winner].Pawn?.LabelShort ?? "a player" : "a regular";
            string res = who + " wins with " + hand.WinningHand + " (pot " + hand.Pot.ToString("0.#") + ", rake " + hand.Rake.ToString("0.##") + ")";
            Book(comp, seated[0], stakedTotal, paidTotal, expected, variance, res);
        }

        /// <summary>Pit Boss: plays rounds on paper (own random stream, nothing is booked) and reports what comes back per stake.</summary>
        public static string Simulate(CompCasinoGame comp, int rounds)
        {
            var rng = new RngStream((ulong)DateTime.Now.Ticks, RngStream.HashString("casino.pitboss.sim"));
            double staked = 0, paid = 0;
            switch (comp.Game)
            {
                case CasinoGame.Slots:
                    for (int i = 0; i < rounds; i++) { staked += 1; paid += comp.Paytable.Spin(rng, out _, out _, out _); }
                    break;
                case CasinoGame.Roulette:
                    var rules = CS.Roulette;
                    for (int i = 0; i < rounds; i++) { var bet = RouletteBet.RandomBet(rng, 0.4); if (bet.Covers(37) && rules.Zeros == 1) bet = RouletteBet.Red(); staked += 1; paid += rules.Return(bet, rules.Spin(rng)); }
                    break;
                case CasinoGame.Blackjack:
                    var solver = BlackjackSolver.Get(CS.Blackjack, 0.6);
                    for (int i = 0; i < rounds; i++) { var r = solver.PlayRound(rng); staked += 1; paid += 1 + r.Result; }
                    break;
                default:
                    var skills = new List<double> { 8, 8, 8, 8 };
                    for (int i = 0; i < rounds; i++) { var h = RimdeckMath.Deal(rng, skills, 10, CS.PokerRakeRate, CS.PokerCap); staked += 10; if (h.Winner == 0) paid += h.Payout; }
                    break;
            }
            return "HH.Casino.SimResult".Translate(rounds.ToString("N0"), (paid / staked * 100).ToString("0.0"), ((1 - comp.ExpectedEdge) * 100).ToString("0.0")).ToString();
        }

        /// <summary>Developer helper: runs rounds without any pawn (demo purse), for balancing in the dev tools.</summary>
        public static void DevSimulate(CompCasinoGame comp, int rounds)
        {
            var s = new GamblerSession { Kind = SessionKind.Demo, Purse = Money.FromSilver(1_000_000), BuyIn = Money.FromSilver(1_000_000), StakeShare = 0.00002f, HouseAccount = "dev" };
            var list = new List<GamblerSession> { s };
            for (int i = 0; i < rounds; i++) RunRound(comp, list);
            Messages.Message($"{comp.parent.LabelCap}: {rounds} rounds, edge {comp.ActualEdge:P2} (expected {comp.ExpectedEdge:P2})", MessageTypeDefOf.NeutralEvent, false);
        }
    }
}
