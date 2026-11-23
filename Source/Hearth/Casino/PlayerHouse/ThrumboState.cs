// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using Verse;

namespace Hearth.Casino.PlayerHouse
{
    /// <summary>One line of the player's history at The Lucky Thrumbo.</summary>
    public sealed class ThrumboEntry : IExposable
    {
        public int Tick, Game, From;
        public long StakedMicro, PaidMicro;
        public string Text;
        public Money Net => new Money(PaidMicro - StakedMicro);
        public void ExposeData()
        {
            Scribe_Values.Look(ref Tick, "t"); Scribe_Values.Look(ref Game, "g"); Scribe_Values.Look(ref From, "f");
            Scribe_Values.Look(ref StakedMicro, "s"); Scribe_Values.Look(ref PaidMicro, "p"); Scribe_Values.Look(ref Text, "x");
        }
    }

    /// <summary>
    /// Save game data of The Lucky Thrumbo: the history, the totals per game, the small change the house still owes (a fraction of a silver
    /// coin cannot be paid out in coins) and the blackjack hand that is open right now. The hand is stored as seed + actions, so a
    /// reload in the middle of a hand brings back exactly the same cards (the stake is sealed).
    /// </summary>
    public sealed class ThrumboComponent : GameComponent
    {
        public const int HistoryMax = 60;
        public static ThrumboComponent Instance;
        public List<ThrumboEntry> History = new List<ThrumboEntry>();
        List<long> totals = new List<long>();          // staked ×3, paid ×3, rounds ×3
        /// <summary>What the house owes the player below one coin (silver payouts only); always 0 … &lt; 1 silver.</summary>
        public long ChangeMicro;
        // the open blackjack hand
        public bool HandOpen;
        public ulong HandSeed;
        public long HandStakeMicro;
        public int HandFrom;
        public string HandActions = "";
        public double HandScale = 1.0;                 // win scale at the deal (the shift may change in the settings while the hand is open)
        public int HandMapId;

        public ThrumboComponent(Game g) { Instance = this; EnsureTotals(); }
        public static ThrumboComponent Get() => Instance ?? (Instance = Current.Game?.GetComponent<ThrumboComponent>());

        void EnsureTotals() { while (totals.Count < 9) totals.Add(0); }
        public long Staked(ThrumboGame g) { EnsureTotals(); return totals[(int)g]; }
        public long Paid(ThrumboGame g) { EnsureTotals(); return totals[3 + (int)g]; }
        public int Rounds(ThrumboGame g) { EnsureTotals(); return (int)totals[6 + (int)g]; }
        public long StakedAll => Staked(ThrumboGame.Slots) + Staked(ThrumboGame.Roulette) + Staked(ThrumboGame.Blackjack);
        public long PaidAll => Paid(ThrumboGame.Slots) + Paid(ThrumboGame.Roulette) + Paid(ThrumboGame.Blackjack);
        public int RoundsAll => Rounds(ThrumboGame.Slots) + Rounds(ThrumboGame.Roulette) + Rounds(ThrumboGame.Blackjack);

        public void Record(ThrumboGame g, PayFrom from, Money staked, Money paid, string text)
        {
            EnsureTotals();
            totals[(int)g] += staked.Micro; totals[3 + (int)g] += paid.Micro; totals[6 + (int)g]++;
            History.Add(new ThrumboEntry { Tick = Find.TickManager?.TicksGame ?? 0, Game = (int)g, From = (int)from, StakedMicro = staked.Micro, PaidMicro = paid.Micro, Text = text });
            if (History.Count > HistoryMax) History.RemoveRange(0, History.Count - HistoryMax);
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref History, "history", LookMode.Deep);
            Scribe_Collections.Look(ref totals, "totals", LookMode.Value);
            Scribe_Values.Look(ref ChangeMicro, "change");
            Scribe_Values.Look(ref HandOpen, "handOpen"); Scribe_Values.Look(ref HandSeed, "handSeed"); Scribe_Values.Look(ref HandStakeMicro, "handStake");
            Scribe_Values.Look(ref HandFrom, "handFrom"); Scribe_Values.Look(ref HandActions, "handActions", ""); Scribe_Values.Look(ref HandScale, "handScale", 1.0);
            Scribe_Values.Look(ref HandMapId, "handMap");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                History = History ?? new List<ThrumboEntry>(); totals = totals ?? new List<long>(); EnsureTotals();
                History.RemoveAll(e => e == null);
                if (HandActions == null) HandActions = "";
                ThrumboHouse.DropLiveHand();
            }
        }

        /// <summary>Forgets everything (tests).</summary>
        public void Clear()
        {
            History.Clear(); totals.Clear(); EnsureTotals(); ChangeMicro = 0; HandOpen = false; HandActions = ""; ThrumboHouse.DropLiveHand();
        }
    }
}
