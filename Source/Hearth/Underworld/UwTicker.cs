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
            Underworld.ClearInUse();
        }

        public static void Tick(int t)
        {
            if (t % 15 == 0) FightBout.TickAll(t);
            if (t % 60 == 7) Hunts.Tick(t);
            if (t % 250 == 91) Offers.Tick(t);
            if (!Underworld.Unlocked) return;
            if (t % 120 == 31) foreach (var m in Find.Maps) if (m.IsPlayerHome) UwDirector.Tick(m);
            if (t % 2500 == 713) foreach (var m in Find.Maps) if (m.IsPlayerHome) Scandals.Check(m);
            if (t % 2500 == 1313) { Events(t); Organs_.TickOrders(t); }
        }

        static int Next(float meanDays) => (int)(60000f * meanDays * Rand.Range(0.6f, 1.4f));

        static void Events(int t)
        {
            var c = Underworld.Comp; if (c == null) return;
            var homes = new List<Map>(); foreach (var m in Find.Maps) if (m.IsPlayerHome) homes.Add(m);
            Map gangMap = null; foreach (var m in homes) if (Gangs.HasGangFacility(m)) { gangMap = m; break; }
            // first customers: 0.5 to 1.5 days after the first facility gangs use (also pulls an old, long timer of an old save forward)
            if (c.GangVisits == 0 && !c.FirstGangPlanned && gangMap != null)
            {
                int plan = t + (int)(60000f * HSettings.Get("underworld.gang.firstDays") * Rand.Range(0.5f, 1.5f));
                if (c.NextGangTick < t || c.NextGangTick > plan) c.NextGangTick = plan;
                c.FirstGangPlanned = true;
                HLog.Info("Underworld", $"First gang visit planned in {(c.NextGangTick - t) / 60000f:0.0} days");
            }
            if (c.NextGangTick < 0) c.NextGangTick = t + Next(HSettings.Get("underworld.gang.intervalDays")) / 2;
            if (c.NextSmugglerTick < 0) c.NextSmugglerTick = t + Next(HSettings.Get("underworld.smuggler.intervalDays")) / 2;
            if (c.NextHuntTick < 0) c.NextHuntTick = t + Next(HSettings.Get("underworld.hunt.intervalDays")) / 2;
            // the gang timer moves forward only when a visit really started; a failed attempt retries after a few hours and says why
            if (t >= c.NextGangTick && gangMap != null)
            {
                if (Gangs.Fire(gangMap, out _, out string why)) { c.NextGangTick = t + Next(HSettings.Get("underworld.gang.intervalDays")); c.GangRetryKey = null; c.GangRetryTick = -1; }
                else
                {
                    c.NextGangTick = t + (int)(HSettings.Get("underworld.gang.retryHours") * GenDate.TicksPerHour);
                    c.GangRetryKey = why ?? "NoPawns"; c.GangRetryTick = t;
                    HLog.Info("Underworld", "Gang visit failed (" + c.GangRetryKey + "), next try in " + HSettings.Get("underworld.gang.retryHours") + " h");
                    FacilityStatus.MarkDirty();
                }
            }
            foreach (var map in homes)
            {
                if (t >= c.NextSmugglerTick && Smuggler.Possible(map) && Smuggler.Night(map) && Smuggler.Fire(map)) c.NextSmugglerTick = t + Next(HSettings.Get("underworld.smuggler.intervalDays"));
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

    /// <summary>The "Underworld" page of the Hearth &amp; Harbor main tab (visible whenever the underworld is visible: module on, content not Cozy).</summary>
    public static class UnderworldPage
    {
        static Vector2 scroll; static float viewH = 400f;
        public static void Draw(Rect r)
        {
            var c = Underworld.Comp; if (c == null) return;
            var body = Hearth.Shell.PageFrame.Header(r, UwPay.T("Page.Title"), "HH.UI.Page.Sub.underworld".TranslateOr("Whispers, arrival weights and the takings of the dark side."));
            float vw = Hearth.Shell.PageFrame.BeginScroll(body, ref scroll, viewH);
            var l = new Listing_Standard(); l.Begin(new Rect(0f, 0f, vw, 3000f));
            l.Gap(2f);
            l.Label(UwPay.T("Page.Whispers", Underworld.Whispers.ToString("0.0")));
            l.Label(UwPay.T("Page.Weights", Underworld.RespectableArrivalFactor.ToString("0.00"), Underworld.OutlawArrivalFactor.ToString("0.00")));
            l.Label(UwPay.T("Page.ContentLevel", ("HH.UI.Content." + Underworld.Intensity).Translate().Resolve()));
            l.GapLine();
            l.Label(UwPay.T("Page.Stats1", c.Bouts, c.Bets, new Money(c.HouseTakeMicro).ToStringShort()));
            l.Label(UwPay.T("Page.Stats2", c.DealsBought, c.DealsSold, new Money(c.FenceProfitMicro).ToStringShort()));
            l.Label(UwPay.T("Page.Stats3", c.LoungeSessions, c.GangVisits, c.Smuggles, c.Hunts, c.OrgansSold));
            if (Underworld.HighRollersActive) l.Label(UwPay.T("Page.HighRollers"));
            float used = l.CurHeight;
            l.End();
            viewH = Hearth.Shell.PageFrame.EndScroll(used);
        }
    }
}
