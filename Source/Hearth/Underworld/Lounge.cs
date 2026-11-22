// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Hearth.Underworld
{
    /// <summary>The drug lounge (6.11): a venue where guests pay for a session. They only take what their ideology allows.</summary>
    public static class Lounge
    {
        static readonly string[] Light = { "SmokeleafJoint", "Beer", "PsychiteTea" };
        static readonly string[] Hard = { "GoJuice", "Yayo", "Flake" };

        /// <summary>0 = nothing, 1 = only light (social) drugs, 2 = everything. Follows the pawn's ideology precepts (Ideology DLC); without Ideology everything is allowed.</summary>
        public static int AllowedLevel(Pawn p)
        {
            if (Underworld.IsMinor(p)) return 0;
            if (!ModsConfig.IdeologyActive || p.Ideo == null) return 2;
            int level = 2;
            foreach (var pr in p.Ideo.PreceptsListForReading)
            {
                string n = pr.def.defName;
                if (n == "DrugUse_Prohibited" || n == "DrugUse_MedicalOnly" || n == "DrugUse_Abhorrent") return 0;
                if (n == "DrugUse_MedicalOrSocial") level = Math.Min(level, 1);
            }
            return level;
        }

        public static ThingDef ChooseDrug(Pawn p)
        {
            int lvl = AllowedLevel(p); if (lvl == 0) return null;
            var names = new List<string>(Light);
            var info = GuestApi.Info(p);
            if (lvl >= 2 && (info == null || info.Wealth >= 35 || Rand.Chance(0.3f))) names.AddRange(Hard);
            var defs = names.Select(n => DefDatabase<ThingDef>.GetNamedSilentFail(n)).Where(d => d != null && d.IsDrug).ToList();
            return defs.Count == 0 ? null : defs.RandomElement();
        }

        public static Venue OpenLounge(Map map)
        {
            if (UwDefs.LoungeVenue == null || map == null) return null;
            foreach (var v in Venues.OfType(map, UwDefs.LoungeVenue)) if (v.IsOpenNow) return v;
            return null;
        }

        public static bool TryFindSeat(Venue v, Pawn p, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            var cells = v.Room.Cells.Where(c => c.Standable(v.Map) && p.CanReach(c, PathEndMode.OnCell, Danger.Some)).ToList();
            if (cells.Count == 0) return false;
            var seats = cells.Where(c => c.GetEdifice(v.Map)?.def.building?.isSittable == true || c.GetThingList(v.Map).Any(t => t.def.building?.isSittable == true)).ToList();
            cell = (seats.Count > 0 ? seats : cells).RandomElement();
            return true;
        }

        /// <summary>One session: pay, take the drug, whispers. Returns false if the guest cannot pay. Also used by tests.</summary>
        public static bool Session(Pawn p, ThingDef drugDef)
        {
            var map = p.Map; var venue = OpenLounge(map);
            var price = HSettings.GetSilver("underworld.lounge.price") * (venue?.Comp?.priceFactor ?? 1f);
            if (!UwPay.Charge(p, price, Underworld.Till(map), "underworld.lounge", "Drug lounge session")) return false;
            var drug = ThingMaker.MakeThing(drugDef);
            bool wasAddicted = IsAddicted(p);
            try { drug.Ingested(p, drugDef.ingestible?.CachedNutrition ?? 0f); }
            catch (Exception e) { HLog.Error("Underworld", "Drug ingestion failed", e); }
            if (!drug.Destroyed) drug.Destroy();
            Underworld.Comp.LoungeSessions++; UwActivity.Note(map);
            Underworld.AddWhispers(HSettings.Get("underworld.whispers.lounge"), "drug lounge session");
            GuestApi.AddSatisfaction(p, 0.08f, "drug lounge");
            if (!wasAddicted && IsAddicted(p) && Underworld.Tier >= 4f && p.Faction != null && !p.Faction.IsPlayer)
                Underworld.AffectGoodwill(p.Faction, -3, "guest became addicted");
            return true;
        }

        static bool IsAddicted(Pawn p) { foreach (var h in p.health.hediffSet.hediffs) if (h is Hediff_Addiction) return true; return false; }
    }

    public static partial class Activities
    {
        public sealed class LoungeActivity : IGuestActivity
        {
            public string Id => "underworld.lounge";
            public ModuleId Module => ModuleId.Underworld;
            public float Desire(Pawn guest, GuestInfo info)
            {
                if (!Underworld.Unlocked || UwDefs.JobLounge == null || Lounge.AllowedLevel(guest) == 0 || guest.Map == null) return 0f;
                if (Lounge.OpenLounge(guest.Map) == null) return 0f;
                if (info == null || info.Budget < HSettings.GetSilver("underworld.lounge.price")) return 0f;
                return 0.9f;
            }
            public Job TryGiveJob(Pawn guest, GuestInfo info)
            {
                var v = Lounge.OpenLounge(guest.Map); if (v == null) return null;
                var drug = Lounge.ChooseDrug(guest); if (drug == null) return null;
                if (!Lounge.TryFindSeat(v, guest, out var cell)) return null;
                var j = JobMaker.MakeJob(UwDefs.JobLounge, cell); return j;
            }
        }
    }

    public class JobDriver_UseLounge : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;
        protected override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            yield return new Toil { defaultCompleteMode = ToilCompleteMode.Delay, defaultDuration = 360, tickAction = () => UwPay.Comfort(pawn) };
            yield return new Toil { defaultCompleteMode = ToilCompleteMode.Instant, initAction = () => { var d = Lounge.ChooseDrug(pawn); if (d != null) Lounge.Session(pawn, d); } };
            yield return new Toil { defaultCompleteMode = ToilCompleteMode.Delay, defaultDuration = 600, tickAction = () => UwPay.Comfort(pawn) };
        }
    }
}
