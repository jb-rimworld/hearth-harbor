// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Hearth.City
{
    public class CompProperties_ProjectorScreen : CompProperties
    {
        public CompProperties_ProjectorScreen() { compClass = typeof(CompProjectorScreen); }
    }

    /// <summary>
    /// State of one projector screen: which film runs, until when, who watches. A screening starts when the first viewer sits
    /// down (guest, colonist) or when a movie night begins, and runs for the film length (city.cinemaShowTicks).
    /// </summary>
    public class CompProjectorScreen : ThingComp
    {
        public const int FilmCount = 8;
        static readonly string[] DefaultFilms =
        {
            "Muffalo Love III", "The Last Thrumbo", "Cargo Pod Blues", "Dust Between the Stars",
            "A Boomalope's Tale", "Siege of Stonecutter Hill", "The Long Sleep", "Smoke over Haven",
        };

        public int ShowStart = -1, ShowEnd = -1, FilmIndex = -1, ShowLength;
        public int ShowsHeld;
        readonly HashSet<int> viewers = new HashSet<int>();

        CompPowerTrader power;
        public CompPowerTrader Power => power ?? (power = parent.TryGetComp<CompPowerTrader>());

        public bool Powered => Power == null || Power.PowerOn;
        public bool ShowActive => Find.TickManager != null && ShowEnd > Find.TickManager.TicksGame;
        public int TicksLeft => ShowActive ? ShowEnd - Find.TickManager.TicksGame : 0;
        public float FractionLeft => ShowActive && ShowLength > 0 ? Mathf.Clamp01((float)TicksLeft / ShowLength) : 0f;
        public int ViewerCount => viewers.Count;

        public static string FilmTitleOf(int index)
        {
            if (index < 0) return null;
            index %= FilmCount;
            return ("HH.City.Film." + index).TranslateOr(DefaultFilms[index]);
        }
        public string FilmTitle => FilmTitleOf(FilmIndex);

        /// <summary>A guest may sit down now: powered, and either idle (a new film starts) or the running one has enough left.</summary>
        public bool CanJoin(float minFractionLeft = 0.4f) => parent.Spawned && Powered && (!ShowActive || FractionLeft >= minFractionLeft);

        public bool StartShow(int duration)
        {
            if (!parent.Spawned || !Powered || ShowActive) return false;
            int now = Find.TickManager.TicksGame;
            ShowStart = now; ShowLength = Math.Max(60, duration); ShowEnd = now + ShowLength;
            FilmIndex = FilmIndex < 0 ? Rand.Range(0, FilmCount) : (FilmIndex + 1 + Rand.Range(0, 3)) % FilmCount;
            ShowsHeld++;
            HLog.Debug("City", $"Screening '{FilmTitle}' starts at {parent} for {ShowLength} ticks");
            return true;
        }

        public void StopShow() { ShowEnd = -1; viewers.Clear(); }

        public void Join(Pawn p) { if (p != null) viewers.Add(p.thingIDNumber); }
        public void Leave(Pawn p) { if (p != null) viewers.Remove(p.thingIDNumber); }

        public override void CompTickRare()
        {
            base.CompTickRare();
            if (!ShowActive && viewers.Count > 0) viewers.Clear();
            if (ShowActive && !Powered) { HLog.Debug("City", "Power failed during a screening at " + parent); StopShow(); }
        }

        public override string CompInspectStringExtra()
        {
            if (!CityAccess.ModuleOn) return null;
            if (!Powered) return "HH.City.ScreenNoPower".TranslateOr("No power: the screen stays dark.");
            if (ShowActive)
                return "HH.City.ScreenShowing".TranslateOr("Showing \"{0}\" ({1} left, {2} watching)")
                    .Replace("{0}", FilmTitle).Replace("{1}", TicksLeft.ToStringTicksToPeriod()).Replace("{2}", viewers.Count.ToString());
            return "HH.City.ScreenIdle".TranslateOr("No film running.");
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (var g in base.CompGetGizmosExtra()) yield return g;
            if (parent.Faction != Faction.OfPlayer || !CityAccess.ModuleOn) yield break;
            var def = DefDatabase<GatheringDef>.GetNamedSilentFail("HH_Screening");
            if (def != null)
            {
                var cmd = new Command_Action
                {
                    defaultLabel = "HH.City.MovieNightLabel".TranslateOr("Movie night"),
                    defaultDesc = "HH.City.MovieNightDesc".TranslateOr("Call the colony to a movie night at this screen. Everyone who sits down gains recreation and a good mood."),
                    icon = CityDefs.VenueCinema?.icon ?? TexCommand.Attack,
                    action = () =>
                    {
                        var map = parent.Map;
                        if (!def.Worker.TryExecute(map)) Messages.Message("HH.City.MovieNightFailed".TranslateOr("Cannot start a movie night now: needs a powered screen, enough seats and colonists who are free."), parent, MessageTypeDefOf.RejectInput, false);
                    },
                };
                string why = null;
                if (!Powered) why = "HH.City.ScreenNoPower".TranslateOr("No power");
                else if (ShowActive) why = "HH.City.ScreenBusy".TranslateOr("A film is already running");
                else if (CityScan.CountSeats(parent) < 1) why = "HH.City.NoSeats".TranslateOr("No seats in front of the screen");
                if (why != null) cmd.Disable(why);
                yield return cmd;
            }
            if (DebugSettings.godMode)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEV: start film", action = () => StartShow(CitySettings.Int(CitySettings.CinemaShowTicks)),
                };
            }
        }

        public override void PostExposeData()
        {
            Scribe_Values.Look(ref ShowStart, "showStart", -1);
            Scribe_Values.Look(ref ShowEnd, "showEnd", -1);
            Scribe_Values.Look(ref FilmIndex, "filmIndex", -1);
            Scribe_Values.Look(ref ShowLength, "showLength");
            Scribe_Values.Look(ref ShowsHeld, "showsHeld");
        }
    }
}
