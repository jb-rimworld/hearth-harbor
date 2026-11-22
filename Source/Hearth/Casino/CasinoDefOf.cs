// SPDX-License-Identifier: MIT
using RimWorld;
using Verse;

namespace Hearth.Casino
{
    [DefOf]
    public static class CasinoDefOf
    {
        public static ThingDef HH_SlotMachine, HH_RouletteTable, HH_BlackjackTable, HH_PokerTable, HH_CashierCage, HH_CasinoChips, HH_GamblingSession;
        public static JobDef HH_PlayCasino, HH_RunCasinoTable;
        public static JoyKindDef HH_Gambling;
        public static JoyGiverDef HH_PlayCasinoJoy;
        public static ChemicalDef HH_GamblingChemical;
        public static HediffDef HH_GamblingAddiction;
        public static NeedDef HH_Need_Gambling;
        public static MentalStateDef HH_GamblingSpree;
        public static ThoughtDef HH_WonAtTheTables, HH_LostAtTheTables, HH_GamblingWithdrawal;
        public static VenueTypeDef HH_Venue_Casino;
        public static ResearchProjectDef HH_Casino_Slots, HH_Casino_Tables;
        static CasinoDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(CasinoDefOf)); }
    }
}
