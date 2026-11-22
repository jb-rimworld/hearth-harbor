// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Hearth.Guests
{
    public enum GuestPhase { Arriving = 0, Staying = 1, Leaving = 2 }

    /// <summary>
    /// The Inn module's guest file (5.1): the shared GuestInfo plus everything the Inn needs to run a stay.
    /// Lives only in <see cref="GuestRegistry"/>; never a comp on the pawn.
    /// </summary>
    public sealed class GuestRecord : GuestInfo, IExposable
    {
        public const int MaxEvents = 40;

        public int PawnId = -1;
        public int GroupId = -1;
        public int MapId = -1;
        public GuestPhase Phase = GuestPhase.Arriving;

        // room
        public Building_Bed Bed;
        public bool CheckedIn;
        public int CheckInTick = -1;
        public int CheckInFailures;
        public bool SelfCheckIn;
        public string RoomLabel;
        public string RoomClass;
        public float RoomScore = -1f;
        public Money NightPrice;
        public int NightsPlanned;
        public int NightsPaid;
        public int LastNightTick = -1;
        public int NightsSlept;
        public int LastVerdictTick = -999999;

        // money
        public Money StartBudget;
        public Dictionary<string, long> SpentBy = new Dictionary<string, long>();

        // feelings
        public float MoodSum;
        public int MoodSamples;
        public float ArrivalMood = -1f;
        public float ServiceSum;
        public float Incidents;
        public float Expectation = 0.35f;
        public float Value = 0.5f;   // V of 5.6, 0..1
        public int LowSince = -1;

        // handling
        public int LastAccompanyTick = -999999;
        public int LastActivityTick = -999999;
        public int Talks;
        public bool Insulted;
        public bool SentAway;
        public bool Settled;
        /// <summary>Shady / outlaw stay (5.2), marked for the Underworld module.</summary>
        public bool Shady;
        public int GoodwillPoints;
        public string Outcome;

        public float MoodMean => MoodSamples > 0 ? MoodSum / MoodSamples : (Pawn?.needs?.mood?.CurLevel ?? 0.5f);
        public string Account => "guest:" + PawnId;
        public bool Overnight => Kind == StayKind.Overnight || Kind == StayKind.Lodger;
        public bool Leaving => Phase == GuestPhase.Leaving;

        /// <summary>The most telling event of the stay for the departure report (higher priority wins, later wins ties).</summary>
        public string Highlight;
        public int HighlightPrio;

        public void Log(string e, int prio = 0)
        {
            if (e.NullOrEmpty()) return;
            if (prio > 0 && prio >= HighlightPrio) { Highlight = e; HighlightPrio = prio; }
            Events.Add(e);
            if (Events.Count > MaxEvents) Events.RemoveRange(0, Events.Count - MaxEvents);
        }

        public void AddSpent(string category, Money m)
        {
            category = category.NullOrEmpty() ? "other" : category;
            SpentBy[category] = (SpentBy.TryGetValue(category, out long v) ? v : 0) + m.Micro;
        }

        List<string> tmpEvents; List<string> tmpK; List<long> tmpV;
        public void ExposeData()
        {
            Scribe_References.Look(ref Pawn, "pawn");
            Scribe_Values.Look(ref PawnId, "pawnId", -1);
            Scribe_Values.Look(ref GroupId, "groupId", -1);
            Scribe_Values.Look(ref MapId, "mapId", -1);
            Scribe_Values.Look(ref Phase, "phase");
            Scribe_Values.Look(ref Kind, "kind");
            Scribe_Values.Look(ref Handling, "handling");
            Scribe_Values.Look(ref Satisfaction, "satisfaction");
            Scribe_Values.Look(ref ArrivedTick, "arrived");
            Scribe_Values.Look(ref PlannedLeaveTick, "plannedLeave");
            Scribe_Values.Look(ref WingId, "wing");
            Scribe_Values.Look(ref Wealth, "wealth", 30);
            Scribe_Values.Look(ref PaysDigital, "digital");
            MoneyLook(ref Budget, "budget");
            MoneyLook(ref Spent, "spent");
            MoneyLook(ref StartBudget, "startBudget");
            MoneyLook(ref NightPrice, "nightPrice");

            Scribe_References.Look(ref Bed, "bed");
            Scribe_Values.Look(ref CheckedIn, "checkedIn");
            Scribe_Values.Look(ref CheckInTick, "checkInTick", -1);
            Scribe_Values.Look(ref CheckInFailures, "checkInFailures");
            Scribe_Values.Look(ref SelfCheckIn, "selfCheckIn");
            Scribe_Values.Look(ref RoomLabel, "roomLabel");
            Scribe_Values.Look(ref RoomClass, "roomClass");
            Scribe_Values.Look(ref RoomScore, "roomScore", -1f);
            Scribe_Values.Look(ref NightsPlanned, "nightsPlanned");
            Scribe_Values.Look(ref NightsPaid, "nightsPaid");
            Scribe_Values.Look(ref LastNightTick, "lastNightTick", -1);
            Scribe_Values.Look(ref NightsSlept, "nightsSlept");
            Scribe_Values.Look(ref LastVerdictTick, "lastVerdict", -999999);

            Scribe_Values.Look(ref MoodSum, "moodSum");
            Scribe_Values.Look(ref MoodSamples, "moodSamples");
            Scribe_Values.Look(ref ArrivalMood, "arrivalMood", -1f);
            Scribe_Values.Look(ref ServiceSum, "serviceSum");
            Scribe_Values.Look(ref Incidents, "incidents");
            Scribe_Values.Look(ref Expectation, "expectation", 0.35f);
            Scribe_Values.Look(ref Value, "value", 0.5f);
            Scribe_Values.Look(ref LowSince, "lowSince", -1);

            Scribe_Values.Look(ref LastAccompanyTick, "lastAccompany", -999999);
            Scribe_Values.Look(ref Talks, "talks");
            Scribe_Values.Look(ref Insulted, "insulted");
            Scribe_Values.Look(ref SentAway, "sentAway");
            Scribe_Values.Look(ref Settled, "settled");
            Scribe_Values.Look(ref Shady, "shady");
            Scribe_Values.Look(ref GoodwillPoints, "goodwillPoints");
            Scribe_Values.Look(ref Outcome, "outcome");
            Scribe_Values.Look(ref Highlight, "highlight");
            Scribe_Values.Look(ref HighlightPrio, "highlightPrio");

            if (Scribe.mode == LoadSaveMode.Saving) tmpEvents = new List<string>(Events);
            Scribe_Collections.Look(ref tmpEvents, "events", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars) { Events.Clear(); if (tmpEvents != null) Events.AddRange(tmpEvents); }
            Scribe_Collections.Look(ref SpentBy, "spentBy", LookMode.Value, LookMode.Value, ref tmpK, ref tmpV);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && SpentBy == null) SpentBy = new Dictionary<string, long>();
        }

        internal static void MoneyLook(ref Money m, string label)
        {
            string s = m.Serialize();
            Scribe_Values.Look(ref s, label, "0");
            if (Scribe.mode == LoadSaveMode.LoadingVars) { try { m = Money.Parse(s); } catch { m = Money.Zero; } }
        }
    }

    /// <summary>One arriving party. Members are the records with the same GroupId.</summary>
    public sealed class GuestGroup : IExposable
    {
        public int Id;
        public Faction Faction;
        public int MapId = -1;
        public StayKind Kind;
        public int ArrivedTick;
        public bool LeaveSent;
        public bool Reported;
        public bool ByPod;
        public string LeaveReason;
        // report material collected at settlement (survives members leaving the map)
        public int GoodwillTotal;
        public float RenownDelta;
        public Money SpentTotal;
        public float MeanSurplus;
        public List<string> ReportLines = new List<string>();
        public List<string> Names = new List<string>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id");
            Scribe_References.Look(ref Faction, "faction");
            Scribe_Values.Look(ref MapId, "mapId", -1);
            Scribe_Values.Look(ref Kind, "kind");
            Scribe_Values.Look(ref ArrivedTick, "arrived");
            Scribe_Values.Look(ref LeaveSent, "leaveSent");
            Scribe_Values.Look(ref Reported, "reported");
            Scribe_Values.Look(ref ByPod, "byPod");
            Scribe_Values.Look(ref LeaveReason, "leaveReason");
            Scribe_Values.Look(ref GoodwillTotal, "goodwillTotal");
            Scribe_Values.Look(ref RenownDelta, "renownDelta");
            Scribe_Values.Look(ref MeanSurplus, "meanSurplus");
            GuestRecord.MoneyLook(ref SpentTotal, "spentTotal");
            Scribe_Collections.Look(ref ReportLines, "reportLines", LookMode.Value);
            Scribe_Collections.Look(ref Names, "names", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) { ReportLines = ReportLines ?? new List<string>(); Names = Names ?? new List<string>(); }
        }
    }
}
