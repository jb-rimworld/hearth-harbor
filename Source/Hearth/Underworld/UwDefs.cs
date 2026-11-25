// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Hearth.Underworld
{
    /// <summary>Def lookups of the module (all by defName, all optional) and the gate sets built once after loading.</summary>
    public static class UwDefs
    {
        public static ThingDef BackRoomDoor, FightPit, FenceCounter, SmugglerDock, HuntingGround, OrganClinic;
        public static ResearchProjectDef ResHunt, ResClinic;
        public static VenueTypeDef LoungeVenue;
        public static ThingCategoryDef CatOrgans; public static HediffDef Bruise;
        public static JobDef JobWatchBout, JobBackRoom, JobFenceDeal, JobLounge, JobInterrogate, JobHunt, JobQuarryFlee;
        public static ThoughtDef ThoughtSawFightKind, ThoughtSawFightBloodlust, ThoughtInterrogated, ThoughtOrganOutrage, ThoughtHuntKnown;
        public static IncidentDef IncGang, IncHunt, IncSmuggler;
        /// <summary>The buildings that make the underworld "in use" on a map (N08 guard, <see cref="Underworld.InUse"/>); only defs that exist.</summary>
        public static readonly List<ThingDef> Facilities = new List<ThingDef>();
        public static readonly HashSet<ResearchProjectDef> GatedResearch = new HashSet<ResearchProjectDef>();
        public static readonly HashSet<BuildableDef> GatedBuildings = new HashSet<BuildableDef>();

        public static void Init()
        {
            BackRoomDoor = Underworld.Def("HH_BackRoomDoor"); FightPit = Underworld.Def("HH_FightPitMarker"); FenceCounter = Underworld.Def("HH_FenceCounter");
            SmugglerDock = Underworld.Def("HH_SmugglerDock"); HuntingGround = Underworld.Def("HH_HuntingGroundMarker"); OrganClinic = Underworld.Def("HH_OrganClinic");
            ResHunt = Underworld.Def<ResearchProjectDef>("HH_TheHunt"); ResClinic = Underworld.Def<ResearchProjectDef>("HH_BlackClinic");
            CatOrgans = Underworld.Def<ThingCategoryDef>("BodyPartsNatural"); Bruise = Underworld.Def<HediffDef>("Bruise");
            LoungeVenue = Underworld.Def<VenueTypeDef>("HH_Venue_DrugLounge");
            JobWatchBout = Underworld.Def<JobDef>("HH_WatchBout"); JobBackRoom = Underworld.Def<JobDef>("HH_BackRoomVisit"); JobFenceDeal = Underworld.Def<JobDef>("HH_FenceDeal");
            JobLounge = Underworld.Def<JobDef>("HH_UseLounge"); JobInterrogate = Underworld.Def<JobDef>("HH_Interrogate"); JobHunt = Underworld.Def<JobDef>("HH_HuntQuarry"); JobQuarryFlee = Underworld.Def<JobDef>("HH_QuarryFlee");
            ThoughtSawFightKind = Underworld.Def<ThoughtDef>("HH_SawFightPit_Kind"); ThoughtSawFightBloodlust = Underworld.Def<ThoughtDef>("HH_SawFightPit_Bloodlust");
            ThoughtInterrogated = Underworld.Def<ThoughtDef>("HH_Interrogated"); ThoughtOrganOutrage = Underworld.Def<ThoughtDef>("HH_OrganTradeOutrage"); ThoughtHuntKnown = Underworld.Def<ThoughtDef>("HH_HuntOnPrisoners");
            IncGang = Underworld.Def<IncidentDef>("HH_GangVisit");
            IncHunt = Underworld.Def<IncidentDef>("HH_HuntParty"); IncSmuggler = Underworld.Def<IncidentDef>("HH_SmugglerLanding");

            Facilities.Clear();
            foreach (var d in new[] { BackRoomDoor, FightPit, FenceCounter, SmugglerDock, HuntingGround, OrganClinic }) if (d != null) Facilities.Add(d);
            foreach (var r in DefDatabase<ResearchProjectDef>.AllDefs) if (r.GetModExtension<UwGateExtension>() != null) GatedResearch.Add(r);
            foreach (var d in DefDatabase<ThingDef>.AllDefs) if (d.GetModExtension<UwGateExtension>() != null) GatedBuildings.Add(d);
            HLog.Info("Underworld", $"Defs: {GatedBuildings.Count} gated buildings, {GatedResearch.Count} gated research projects, door={(BackRoomDoor != null)}, pit={(FightPit != null)}, fence={(FenceCounter != null)}");
        }

        /// <summary>Does the gate extension of a def let it show/work right now? Module on, content not Cozy and at least minContent (needsDiscovery is ignored since 1.2.0).</summary>
        public static bool Open(UwGateExtension e)
        {
            if (e == null) return true;
            if (e.alwaysHidden) return false;
            if (!Underworld.Visible) return false;
            if ((int)Underworld.Effective < (int)e.minContent) return false;
            return true;
        }
    }

    /// <summary>On every underworld facility (XML): tells the N08 guard <see cref="Underworld.InUse"/> that the map changed. No saved data.</summary>
    public class CompProperties_UwFacility : CompProperties { public CompProperties_UwFacility() { compClass = typeof(CompUwFacility); } }
    public class CompUwFacility : ThingComp
    {
        public override void PostSpawnSetup(bool respawningAfterLoad) { base.PostSpawnSetup(respawningAfterLoad); Underworld.MarkInUseDirty(parent.Map); }
        public override void PostDeSpawn(Map map
#if RW16PLUS
            , DestroyMode mode = DestroyMode.Vanish
#endif
            )
        {
            base.PostDeSpawn(map
#if RW16PLUS
                , mode
#endif
                );
            Underworld.MarkInUseDirty(map);
        }
    }

    /// <summary>Hides the underworld in the architect menu (and blocks placement) when the content gate is shut.</summary>
    public class PlaceWorker_UnderworldGate : PlaceWorker
    {
        public override bool IsBuildDesignatorVisible(BuildableDef def)
        {
            var e = def.GetModExtension<UwGateExtension>();
            return UwDefs.Open(e);
        }

        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            var e = checkingDef.GetModExtension<UwGateExtension>();
            if (!UwDefs.Open(e)) return new AcceptanceReport("HH.Underworld.GateClosed".Translate());
            return true;
        }
    }

    /// <summary>Research of the underworld stays hidden (tree, search, start button) until the gate opens.</summary>
    [HarmonyPatch(typeof(ResearchProjectDef), nameof(ResearchProjectDef.IsHidden), MethodType.Getter)]
    public static class Patch_ResearchHidden
    {
        public static void Postfix(ResearchProjectDef __instance, ref bool __result)
        {
            if (__result || UwDefs.GatedResearch.Count == 0 || !UwDefs.GatedResearch.Contains(__instance)) return;
            if (!UwDefs.Open(__instance.GetModExtension<UwGateExtension>())) __result = true;
        }
    }

    /// <summary>
    /// During a non-lethal bout nobody takes damage once beaten (downed or below the yield line). Only an active bout of that very pawn is touched;
    /// everything else (and a module that is off) returns at once. The blow is not cancelled (no "return false"): its amount is set to 0, so every other
    /// prefix and postfix on PreApplyDamage (Combat Extended armour/stagger, shields, Vanilla Expanded) still runs on the damage pipeline as usual.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PreApplyDamage))]
    public static class Patch_PitDamageGuard
    {
        [HarmonyPriority(Priority.Last)]
        public static bool Prefix(Pawn __instance, ref DamageInfo dinfo, out bool absorbed)
        {
            absorbed = false;
            if (FightBout.ActiveCount == 0 || !Modules.On(ModuleId.Underworld)) return true;
            var b = FightBout.OfPawn(__instance);
            if (b == null) return true;
            if (b.ShouldAbsorb(__instance)) { dinfo.SetAmount(0f); return true; }
            // non-lethal: a heavy blow on a fighter close to the yield line (a muffalo's kick) must not kill before the bout referee notices; soften it there
            if (b.NonLethal && b.IsNearYield(__instance)) dinfo.SetAmount(dinfo.Amount * 0.3f);
            return true;
        }
    }
}
