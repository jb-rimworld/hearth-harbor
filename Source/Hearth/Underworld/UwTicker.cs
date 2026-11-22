// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Hearth.Underworld
{
    /// <summary>Schedules everything of the module from the one module tick. Staggered offsets keep any single tick light.</summary>
    public static class UwTicker
    {
        public static void OnGame()
        {
            FightBout.AbortAll("game (re)started");
        }

        public static void Tick(int t)
        {
            if (t % 15 == 0) FightBout.TickAll(t);
            if (t % 60 == 7) Hunts.Tick(t);
            if (t % 250 == 91) Gangs.TickDemands(t);
            if (t % 600 == 3) { Discovery.EnsureAlwaysOn(); UwWarnings.Tick(); }
            if (!Underworld.Visible) return;
            if (t % 120 == 31) foreach (var m in Find.Maps) if (m.IsPlayerHome) UwDirector.Tick(m);
            if (t % 2500 == 13) foreach (var m in Find.Maps) if (m.IsPlayerHome) Discovery.Roll(m);
            if (!Underworld.Unlocked) return;
            if (t % 2500 == 713) foreach (var m in Find.Maps) if (m.IsPlayerHome) Scandals.Check(m);
            if (t % 2500 == 1313) { Events(t); Organs_.TickOrders(t); }
        }

        static int Next(float meanDays) => (int)(60000f * meanDays * Rand.Range(0.6f, 1.4f));

        static void Events(int t)
        {
            var c = Underworld.Comp; if (c == null) return;
            if (c.NextGangTick < 0) c.NextGangTick = t + Next(HSettings.Get("underworld.gang.intervalDays")) / 2;
            if (c.NextSmugglerTick < 0) c.NextSmugglerTick = t + Next(HSettings.Get("underworld.smuggler.intervalDays")) / 2;
            if (c.NextHuntTick < 0) c.NextHuntTick = t + Next(HSettings.Get("underworld.hunt.intervalDays")) / 2;
            foreach (var map in Find.Maps.Where(m => m.IsPlayerHome).ToList())
            {
                if (t >= c.NextGangTick && Gangs.Possible(map)) { c.NextGangTick = t + Next(HSettings.Get("underworld.gang.intervalDays")); Gangs.Fire(map, out _); }
                if (t >= c.NextSmugglerTick && Smuggler.Possible(map) && Smuggler.Night(map) && !Find.WindowStack.IsOpen<Dialog_NodeTree>()) { c.NextSmugglerTick = t + Next(HSettings.Get("underworld.smuggler.intervalDays")); Smuggler.Fire(map); }
                if (t >= c.NextHuntTick && Hunts.Possible(map)) { c.NextHuntTick = t + Next(HSettings.Get("underworld.hunt.intervalDays")); Hunts.Fire(map, out _); }
            }
        }
    }

    public static class Caretakers
    {
        /// <summary>Money left in a pit account without a running bout (after a save/load in the middle of a bout) goes back to the till.</summary>
        public static int PitCash()
        {
            if (FightBout.ActiveCount > 0) return 0;
            int n = 0;
            foreach (var m in Find.Maps)
            {
                var acc = Underworld.PitAccount(m); var bal = UwPay.Balance(acc);
                if (bal.Micro > 0) { UwPay.Sweep(acc, m, "Fight pit: leftover stakes"); Caretaker.Fixed("Underworld.PitCash", $"{bal.ToStringShort()} silver left in the pit account of map {m.uniqueID}"); n++; }
            }
            return n;
        }

        /// <summary>A fence cash box without a fence counter is swept into the till.</summary>
        public static int FenceCash()
        {
            int n = 0;
            foreach (var m in Find.Maps)
            {
                var acc = Underworld.FenceAccount(m); var bal = UwPay.Balance(acc);
                if (bal.Micro > 0 && FenceTrade.FindCounter(m) == null) { UwPay.Sweep(acc, m, "Fence: cash box closed"); Caretaker.Fixed("Underworld.FenceCash", $"{bal.ToStringShort()} silver in a fence cash box without a counter"); n++; }
            }
            return n;
        }
    }

    /// <summary>The "Underworld" page of the Hearth &amp; Harbor main tab (visible once the dark side is open).</summary>
    public static class UnderworldPage
    {
        static Vector2 scroll;
        public static void Draw(Rect r)
        {
            var c = Underworld.Comp; if (c == null) return;
            var l = new Listing_Standard(); l.Begin(r);
            Text.Font = GameFont.Medium; l.Label(UwPay.T("Page.Title")); Text.Font = GameFont.Small;
            l.Label(UwPay.T("Page.Whispers", Underworld.Whispers.ToString("0.0")));
            l.Label(UwPay.T("Page.Weights", Underworld.RespectableArrivalFactor.ToString("0.00"), Underworld.OutlawArrivalFactor.ToString("0.00")));
            l.Label(UwPay.T("Page.Content", Underworld.Intensity.ToString(), Underworld.Effective.ToString()));
            l.GapLine();
            l.Label(UwPay.T("Page.Stats1", c.Bouts, c.Bets, new Money(c.HouseTakeMicro).ToStringShort()));
            l.Label(UwPay.T("Page.Stats2", c.DealsBought, c.DealsSold, new Money(c.FenceProfitMicro).ToStringShort()));
            l.Label(UwPay.T("Page.Stats3", c.LoungeSessions, c.GangVisits, c.Smuggles, c.Hunts, c.OrgansSold));
            if (Underworld.HighRollersActive) l.Label(UwPay.T("Page.HighRollers"));
            l.End();
        }
    }
}
