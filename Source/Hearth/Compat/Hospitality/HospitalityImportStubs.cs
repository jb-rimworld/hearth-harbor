// SPDX-License-Identifier: MIT
// Stand-ins for Hospitality's saved classes when Hospitality is no longer installed (KONZEPT 15.7, D12).
// They read the same save keys (names only, no code taken over), hand what they read to the importer and then
// remove themselves, so a save that went through the import never depends on these classes either.
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Hearth.Compat.Hospitality
{
    /// <summary>Reads Hospitality's per-map settings (guest areas, "guests welcome", food rule …) and is removed after loading.</summary>
    public sealed class HospitalityMapStub : MapComponent
    {
        public string SourceClass;
        public Area DefaultGuestArea, DefaultShoppingArea;
        public bool GuestsAreWelcome = true, RefuseUntilBeds, FreeFood, DefaultEntertain, DefaultMakeFriends, AskForSafety = true;

        public HospitalityMapStub(Map map) : base(map) { }

        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving) return; // never written; removed after loading
            Scribe_Values.Look(ref DefaultEntertain, "defaultEntertain");
            Scribe_Values.Look(ref DefaultMakeFriends, "defaultMakeFriends");
            Scribe_Values.Look(ref GuestsAreWelcome, "guestsAreWelcome", true);
            Scribe_Values.Look(ref AskForSafety, "askForSafety", true);
            Scribe_References.Look(ref DefaultGuestArea, "defaultAreaRestriction");
            Scribe_References.Look(ref DefaultShoppingArea, "defaultAreaShopping");
            Scribe_Values.Look(ref RefuseUntilBeds, "refuseGuestsUntilWeHaveBeds");
            Scribe_Values.Look(ref FreeFood, "guestsCanTakeFoodForFree");
            if (Scribe.mode == LoadSaveMode.PostLoadInit) HospitalityImporter.Pending.AddOnce(HospitalityImporter.Pending.MapStubs, this);
        }
    }

    /// <summary>Any other map component of the family (Storefront, Gastronomy …): read nothing, counted as dropped, removed after loading.</summary>
    public sealed class HospitalityDroppedMapComponent : MapComponent
    {
        public HospitalityDroppedMapComponent(Map map) : base(map) { }
        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.PostLoadInit) HospitalityImporter.Pending.AddOnce(HospitalityImporter.Pending.DroppedMapComponents, this);
        }
    }

    /// <summary>Hospitality's game component (guest food policy): dropped, removed after loading.</summary>
    public sealed class HospitalityDroppedGameComponent : GameComponent
    {
        public HospitalityDroppedGameComponent(Game game) { }
        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.PostLoadInit) HospitalityImporter.Pending.AddOnce(HospitalityImporter.Pending.DroppedGameComponents, this);
        }
    }

    public sealed class HospitalityDroppedWorldComponent : WorldComponent
    {
        public HospitalityDroppedWorldComponent(World world) : base(world) { }
        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.PostLoadInit) HospitalityImporter.Pending.AddOnce(HospitalityImporter.Pending.DroppedWorldComponents, this);
        }
    }

    /// <summary>
    /// A running Hospitality visit. Tells vanilla that an important reference was lost, so vanilla's Lord removes itself
    /// cleanly at PostLoadInit (before toil indices are checked). The importer then sends the visitors home neutrally (15.7).
    /// </summary>
    public sealed class HospitalityLordJobStub : LordJob
    {
        public Faction Faction;
        public int StayDuration;
        public override bool LostImportantReferenceDuringLoading => true;
        public override StateGraph CreateGraph() { var g = new StateGraph(); g.AddToil(new LordToil_ExitMap(LocomotionUrgency.Walk)); return g; }
        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving) return;
            Scribe_References.Look(ref Faction, "faction");
            Scribe_Values.Look(ref StayDuration, "stayDuration", 60000);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) HospitalityImporter.Pending.DroppedLords.Add(this);
        }
    }

    /// <summary>Toil data of a family lord; the lord is removed, so this is discarded.</summary>
    public sealed class HospitalityLordToilDataStub : LordToilData
    {
        public override void ExposeData() { }
    }

    public sealed class HospitalityTriggerDataStub : TriggerData
    {
        public override void ExposeData() { }
    }

    /// <summary>A job a guest was doing for Hospitality (buying, claiming a bed …). Ends at once after loading; the job is dropped.</summary>
    public sealed class HospitalityJobDriverStub : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;
        protected override IEnumerable<Toil> MakeNewToils()
        {
            var t = ToilMaker.MakeToil("HospitalityDroppedJob");
            t.initAction = () => EndJobWith(JobCondition.Incompletable);
            t.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return t;
        }
        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.PostLoadInit) { HospitalityImporter.Pending.DroppedJobs.Add(this); if (pawn != null) HospitalityImporter.Pending.JobPawns.Add(pawn); }
        }
    }
}
