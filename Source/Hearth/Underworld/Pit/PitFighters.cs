// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Hearth.Underworld
{
    /// <summary>Who a fighter is, for grouping and for the rules of the bout.</summary>
    public enum PitKind { Colonist = 0, Prisoner = 1, Animal = 2, Guest = 3, Beast = 4, Prizefighter = 5 }

    /// <summary>One row of the fighter list: the pawn, its group and why it cannot fight now (null = ready).</summary>
    public sealed class PitCandidate
    {
        public Pawn Pawn;
        public PitKind Kind;
        /// <summary>Keyed reason, already translated; null when the pawn may fight now.</summary>
        public string Reason;
        /// <summary>True when the reason only keeps the pawn out of automatic bouts (asleep); a manual start may still use it. "Not ticked" is a hard
        /// reason: colonists and slaves fight only when the player ticks them (B.1), also in manual starts (the right-click ticks the asking colonist first).</summary>
        public bool SoftReason;
        public float Power;
        public bool Ready => Reason == null;
        public bool ManualOk => Reason == null || SoftReason;
    }

    /// <summary>
    /// Eligibility of fighters and the reasons shown to the player (B.1). Pure reads of pawn state, no random numbers.
    /// The lists are built per pit and cached for 250 ticks by <see cref="CompFightPit.Candidates"/> (never per frame).
    /// </summary>
    public static class PitFighters
    {
        static string R(string key, params NamedArgument[] args) => UwPay.T("Pit.Why." + key, args);

        /// <summary>A free humanlike (guest, prizefighter, colonist, slave): a bout with one of them is scored, nobody gets hurt.</summary>
        public static bool FreeHumanlike(Pawn p) => p != null && p.RaceProps.Humanlike && !p.IsPrisoner;

        /// <summary>Combat power used for matchmaking and odds. Animals: their kind's combat power. Humanlikes: 35 + 5 per melee level, +25 with a melee weapon, +15 for Brawler.</summary>
        public static float Power(Pawn p)
        {
            if (p == null) return 5f;
            if (!p.RaceProps.Humanlike) return Math.Max(5f, p.kindDef?.combatPower ?? 40f);
            float v = 35f + 5f * (p.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0);
            var w = p.equipment?.Primary;
            if (w != null && w.def.IsMeleeWeapon) v += 25f;
            if (TraitDefOf.Brawler != null && p.story?.traits?.HasTrait(TraitDefOf.Brawler) == true) v += 15f;
            return v;
        }

        /// <summary>The same idle check as UwDirector (wandering or standing), copied because Gangs.cs belongs to another package.</summary>
        public static bool Idle(Pawn p)
        {
            var d = p.CurJobDef;
            return d == null || d == JobDefOf.Wait_Wander || d == JobDefOf.GotoWander || d == JobDefOf.Wait || d == JobDefOf.Wait_MaintainPosture;
        }

        /// <summary>Ticks a bout can take from the call to the result (walk in, bets, fight): guests whose leave is planned inside it are left out.</summary>
        public static int BoutWindowTicks => 3200 + 600 + Math.Max(300, HSettings.GetInt("underworld.pit.maxBoutTicks"));

        /// <summary>Hard reasons that apply to every fighter (no manual start overrides them).</summary>
        static string Basic(Pawn p)
        {
            if (p == null || p.Dead || p.Destroyed || !p.Spawned) return R("Gone");
            if (Underworld.IsMinor(p)) return R("Child");
            if (p.Downed) return R("Downed");
            if (p.InMentalState) return R("Mental");
            if (p.IsBurning()) return R("Burning");
            if (p.health.summaryHealth.SummaryHealthPercent <= 0.8f) return R("Hurt");
            if (FightBout.OfPawn(p) != null) return R("InBout");
            return null;
        }

        static PitCandidate Make(Pawn p, PitKind k, string reason, bool soft = false) => new PitCandidate { Pawn = p, Kind = k, Reason = reason, SoftReason = reason != null && soft, Power = Power(p) };

        public static PitCandidate Colonist(CompFightPit pit, Pawn p)
        {
            string why = Basic(p);
            if (why == null && p.Drafted) why = R("Drafted");
            if (why == null && p.WorkTagIsDisabled(WorkTags.Violent)) why = R("NoViolence");
            if (why != null) return Make(p, PitKind.Colonist, why);
            bool ticked = pit.IsTicked(p);
            bool volunteer = !ticked && pit.allowVolunteers && (p.story?.traits?.HasTrait(TraitDefOf.Bloodlust) == true || (p.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0) >= 8);
            if (!ticked && !volunteer) return Make(p, PitKind.Colonist, R("NotTicked"));
            if (!RestUtility.Awake(p)) return Make(p, PitKind.Colonist, R("Asleep"), soft: true);
            return Make(p, PitKind.Colonist, null);
        }

        public static PitCandidate Prisoner(Pawn p)
        {
            string why = Basic(p);
            return Make(p, PitKind.Prisoner, why);
        }

        public static PitCandidate Animal(Pawn p)
        {
            string why = Basic(p);
            if (why == null && p.CurJobDef == JobDefOf.LayDown) return Make(p, PitKind.Animal, R("Asleep"), soft: true);
            return Make(p, PitKind.Animal, why);
        }

        /// <summary>The lord toil of a visitor that counts as "staying": the Inn stay toil or vanilla's defend point (visitors). Anything else is arriving or leaving.</summary>
        public static bool StayToil(Pawn p)
        {
            var toil = p.GetLord()?.CurLordToil;
            if (toil == null) return false;
            return toil is LordToil_DefendPoint || toil.GetType().Name == "LordToil_HH_Stay";
        }

        public static PitCandidate Guest(Pawn p)
        {
            string why = Basic(p);
            if (why == null && p.WorkTagIsDisabled(WorkTags.Violent)) why = R("NoViolence");
            if (why == null && !RestUtility.Awake(p)) why = R("Asleep");
            if (why == null && p.IsQuestLodger()) why = R("QuestGuest");
            if (why == null && p.GetLord()?.LordJob is LordJob_TradeWithColony) why = R("Trader");
            if (why == null && !StayToil(p)) why = R("Leaving");
            if (why == null)
            {
                var info = GuestApi.Info(p);
                if (info == null) why = R("NotGuest");
                else if (info.PlannedLeaveTick > 0 && info.PlannedLeaveTick < Find.TickManager.TicksGame + BoutWindowTicks) why = R("LeavingSoon");
            }
            if (why == null && !Underworld.IsShady(p))
            {
                var tr = p.story?.traits;
                if (tr != null && tr.HasTrait(TraitDefOf.Kind)) why = R("Kind");
                else
                {
                    bool keen = tr != null && (tr.HasTrait(TraitDefOf.Bloodlust) || (TraitDefOf.Brawler != null && tr.HasTrait(TraitDefOf.Brawler)));
                    if (!keen && (p.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0) < 6) why = R("NoTaste");
                }
            }
            if (why == null && !Idle(p)) why = R("Busy");
            return Make(p, PitKind.Guest, why);
        }

        /// <summary>Everyone on the map who could fight in this pit, grouped by kind, with reasons. Groups switched off on the marker are left out.</summary>
        public static List<PitCandidate> Build(CompFightPit pit)
        {
            var list = new List<PitCandidate>();
            var map = pit.Map; if (map == null) return list;
            var seen = new HashSet<Pawn>();
            foreach (var p in map.mapPawns.FreeColonistsSpawned) if (p.RaceProps.Humanlike && seen.Add(p)) list.Add(Colonist(pit, p));
            foreach (var p in map.mapPawns.SlavesOfColonySpawned) if (seen.Add(p)) list.Add(Colonist(pit, p));
            if (pit.allowPrisoners) foreach (var p in map.mapPawns.PrisonersOfColonySpawned) if (seen.Add(p)) list.Add(Prisoner(p));
            foreach (var p in map.mapPawns.SpawnedColonyAnimals)
            {
                if (!p.RaceProps.Animal || !seen.Add(p)) continue;
                if (PitOpponents.IsBeast(p)) { list.Add(Make(p, PitKind.Beast, Basic(p))); continue; }
                if (pit.allowAnimals) list.Add(Animal(p));
            }
            if (pit.allowGuests)
                foreach (var p in GuestApi.GuestsOn(map))
                    if (p != null && p.RaceProps.Humanlike && seen.Add(p) && !PitOpponents.IsPrizefighter(p)) list.Add(Guest(p));
            return list;
        }

        // ---- the rating line ---------------------------------------------------------------------------
        /// <summary>One line that rates a match: "even", "X is the favourite", "X is the clear favourite".</summary>
        public static string Rating(Pawn a, Pawn b)
        {
            if (a == null || b == null) return "";
            float share = UnderworldMath.FavouriteShare(Power(a), Power(b));
            var fav = share >= 0.5f ? a : b; float s = Math.Max(share, 1f - share);
            if (s < 0.58f) return UwPay.T("Pit.Rating.Even");
            return UwPay.T(s >= 0.72f ? "Pit.Rating.Clear" : "Pit.Rating.Favourite", fav.LabelShortCap);
        }

        public static string KindLabel(PitKind k) => UwPay.T("Pit.Kind." + k);

        /// <summary>Silver for the pit's texts: whole coins ("84", not "84.00").</summary>
        public static string Silver(Money m) => Math.Round(m.Silver, MidpointRounding.AwayFromZero).ToString("0");
    }
}
