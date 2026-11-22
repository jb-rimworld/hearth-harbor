// SPDX-License-Identifier: MIT
// KONZEPT 11.6: guest chapels for other ideoligions (altar, prayer job, guest activity, colony reactions).
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Hearth.Dlc
{
    /// <summary>The guest altar. Holds the ideoligion it is dedicated to; takes its colour from it.</summary>
    public class Building_GuestAltar : Building
    {
        Ideo ideo;
        public Ideo Ideo { get => ideo; set { ideo = value; Map?.mapDrawer?.MapMeshDirty(Position, MapMeshFlagDefOf.Things); ShrineApi.MarkDirty(Map); } }

        public override Color DrawColor => ideo != null ? ideo.Color : base.DrawColor;
        public override string LabelNoCount => ideo == null ? base.LabelNoCount : base.LabelNoCount + " (" + ideo.name + ")";

        public override void ExposeData() { base.ExposeData(); Scribe_References.Look(ref ideo, "ideo"); }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            ShrineApi.MarkDirty(map);
            if (!respawningAfterLoad && ideo == null) ideo = ShrineApi.SuggestIdeo(map);
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            var map = Map; ShrineApi.OnAltarRemoved(this, mode);
            base.DeSpawn(mode); ShrineApi.MarkDirty(map);
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var g in base.GetGizmos()) yield return g;
            if (Faction != Faction.OfPlayer) yield break;
            yield return new Command_Action
            {
                defaultLabel = "HH.Dlc.Gizmo.ChooseFaith".TranslateOr("Choose faith"),
                defaultDesc = "HH.Dlc.Gizmo.ChooseFaithDesc".TranslateOr("Dedicate this altar to an ideoligion. Its followers among your guests will come here to pray."),
                icon = ContentFinder<Texture2D>.Get("UI/HH/Dlc/Issues/Indulgences", false) ?? BaseContent.BadTex,
                action = () =>
                {
                    var opts = Find.IdeoManager.IdeosListForReading.Select(i => { var id = i; return new FloatMenuOption(id.name, () => Ideo = id, id.Icon, id.Color); }).ToList();
                    if (opts.Count > 0) Find.WindowStack.Add(new FloatMenu(opts));
                }
            };
        }

        public override string GetInspectString()
        {
            string s = base.GetInspectString();
            if (ideo == null) return s + "\n" + "HH.Dlc.NoFaith".TranslateOr("No faith chosen yet.");
            double q = ShrineApi.Quality(this);
            return s + "\n" + "HH.Dlc.ShrineQuality".TranslateOr("Faith: {0}. Chapel quality: {1}.").Fmt(ideo.name, q.ToString("P0"));
        }
    }

    public class CompProperties_GuestShrine : CompProperties { public CompProperties_GuestShrine() { compClass = typeof(CompGuestShrine); } }
    /// <summary>Marker comp so other modules can find shrines through TryGetComp without referencing the building class.</summary>
    public class CompGuestShrine : ThingComp { }

    public struct PrayerResult
    {
        public bool Prayed; public double Quality; public int MoodStage; public double Satisfaction; public Money Donation, IndulgenceSale;
    }

    public static class ShrineApi
    {
        static readonly Dictionary<int, List<Building_GuestAltar>> cache = new Dictionary<int, List<Building_GuestAltar>>();
        static readonly Dictionary<int, int> lastPrayer = new Dictionary<int, int>();
        static readonly HashSet<int> stayExtended = new HashSet<int>();
        public const int PrayerCooldown = 20000;

        public static void MarkDirty(Map map) { if (map != null) cache.Remove(map.uniqueID); }

        public static List<Building_GuestAltar> Altars(Map map)
        {
            if (map == null) return new List<Building_GuestAltar>();
            if (cache.TryGetValue(map.uniqueID, out var l)) return l;
            l = new List<Building_GuestAltar>();
            foreach (var b in map.listerBuildings.allBuildingsColonist) if (b is Building_GuestAltar a && a.Ideo != null) l.Add(a);
            cache[map.uniqueID] = l; return l;
        }

        public static Ideo SuggestIdeo(Map map)
        {
            var mine = Faction.OfPlayer.ideos?.PrimaryIdeo;
            return Find.IdeoManager.IdeosListForReading.FirstOrDefault(i => i != mine) ?? mine;
        }

        public static IEnumerable<Building_GuestAltar> ForeignShrines(Map map, Ideo ownIdeo) => Altars(map).Where(a => a.Ideo != ownIdeo);

        /// <summary>Quality 0..1 from impressiveness, seats and style share (11.6).</summary>
        public static double Quality(Building_GuestAltar a)
        {
            var map = a.Map; if (map == null) return 0;
            double imp = 0; var room = a.GetRoom();
            if (room != null && !room.PsychologicallyOutdoors) imp = room.GetStat(RoomStatDefOf.Impressiveness);
            int seats = 0, styled = 0, total = 0;
            foreach (var c in GenRadial.RadialCellsAround(a.Position, 6f, true))
            {
                if (!c.InBounds(map)) continue;
                foreach (var t in c.GetThingList(map))
                {
                    if (!(t is Building b) || b == a) continue;
                    if (!(b is Building_Bed) && (b.def.defName == "Pew" || b.def.defName == "KneelPillow" || b.def.defName == "KneelSheet" || b.def.statBases != null && b.GetStatValue(StatDefOf.Comfort) > 0f && b.def.passability != Traversability.Impassable)) seats++;
                    if (b.def.building != null && !b.def.building.isNaturalRock && b.def.useHitPoints)
                    {
                        total++; if (b.StyleSourcePrecept?.ideo == a.Ideo) styled++;
                    }
                }
            }
            return DlcMath.ShrineQuality(imp, seats, total == 0 ? 0 : styled / (double)total);
        }

        public static Building_GuestAltar FindAltarFor(Pawn guest)
        {
            if (guest?.Ideo == null || !guest.Spawned) return null;
            Building_GuestAltar best = null; float bd = float.MaxValue;
            foreach (var a in Altars(guest.Map))
            {
                if (a.Ideo != guest.Ideo || a.IsForbidden(guest)) continue;
                float d = a.Position.DistanceToSquared(guest.Position);
                if (d < bd && guest.CanReach(a, PathEndMode.InteractionCell, Danger.Some)) { best = a; bd = d; }
            }
            return best;
        }

        public static bool RecentlyPrayed(Pawn p) => lastPrayer.TryGetValue(p.thingIDNumber, out int t) && Find.TickManager.TicksGame - t < PrayerCooldown;

        /// <summary>The effects of one prayer (11.6): mood, satisfaction, certainty, donation into the temple fund, indulgence purchase, longer stay.</summary>
        public static PrayerResult Visit(Pawn guest, Building_GuestAltar altar)
        {
            var res = new PrayerResult();
            if (guest?.Ideo == null || altar?.Ideo == null || altar.Ideo != guest.Ideo || !DlcDefs.Ideology) return res;
            int now = Find.TickManager.TicksGame; lastPrayer[guest.thingIDNumber] = now;
            double q = Quality(altar); res.Quality = q; res.Prayed = true;
            int stage = DlcMath.PrayerStage(q); res.MoodStage = stage;
            var th = DlcDefs.Thought("HH_PlaceToPray");
            if (th != null && guest.needs?.mood?.thoughts?.memories != null)
            {
                var m = (Thought_Memory)ThoughtMaker.MakeThought(th, stage);
                guest.needs.mood.thoughts.memories.TryGainMemory(m);
            }
            res.Satisfaction = DlcMath.PrayerSatisfaction(q);
            GuestApi.AddSatisfaction(guest, (float)res.Satisfaction, "A place to pray");
            guest.ideo?.OffsetCertainty(0.03f);
            var info = GuestApi.Info(guest);
            // donation: 1-3 % of the remaining budget times certainty, into the temple fund of the shrine's faith
            if (info != null && info.Budget.IsPositive)
            {
                double d = DlcMath.ShrineDonation(info.Budget.Silver, guest.ideo?.Certainty ?? 0.5f, Rand.Value);
                var amt = Money.FromSilver(Math.Floor(d));
                if (amt.IsPositive && PayTemple(guest, amt, altar.Ideo, "Shrine donation")) { res.Donation = amt; GuestApi.Spend(guest, amt, "Faith"); }
            }
            // guests of an accepting faith buy an indulgence at the altar
            res.IndulgenceSale = TryGuestPurchase(guest, info);
            if (info != null && info.Kind == StayKind.Overnight && info.PlannedLeaveTick > 0 && stayExtended.Add(guest.thingIDNumber)) info.PlannedLeaveTick += 30000;
            return res;
        }

        static bool PayTemple(Pawn guest, Money amt, Ideo ideo, string memo)
        {
            string acc = IndulgenceApi.TempleAccount(ideo);
            if (Payments.TryCharge(guest, amt, acc, memo, out _)) return true;
            if (GuestApi.Impl == null && Ledger.Current != null) return Ledger.Current.Post("world", acc, amt, memo + " (visitor)");
            return false;
        }

        static Money TryGuestPurchase(Pawn guest, GuestInfo info)
        {
            if (info == null || !Modules.On(ModuleId.CityLife)) return Money.Zero;
            var stance = IdeoStances.Of(guest, MoneyTheme.Indulgences);
            double share = DlcMath.GuestIndulgenceShare(stance, Rand.Value);
            if (share <= 0) return Money.Zero;
            double spend = info.Budget.Silver * share;
            var options = IndulgenceApi.AllDefs.Where(d => d.theme == MoneyTheme.Indulgences && d.rank == 0 && IndulgenceApi.CanBuy(guest, d, out _) && IndulgenceApi.PriceFor(guest, d).Silver <= spend && IndulgenceApi.PriceFor(guest, d) <= info.Budget).ToList();
            if (options.Count == 0) return Money.Zero;
            var pick = options.RandomElement(); var price = IndulgenceApi.PriceFor(guest, pick);
            return IndulgenceApi.TryBuy(guest, pick, out _) == BuyResult.Ok ? price : Money.Zero;
        }

        /// <summary>Removing a shrine while believers are around costs standing with factions of that faith (11.6): ignored for non-guests; mood only otherwise.</summary>
        public static void OnAltarRemoved(Building_GuestAltar altar, DestroyMode mode)
        {
            if (mode != DestroyMode.Deconstruct || altar.Map == null || altar.Ideo == null) return;
            int present = altar.Map.mapPawns.AllPawnsSpawned.Count(p => p.Ideo == altar.Ideo && GuestApi.IsGuest(p));
            if (present == 0) return;
            foreach (var f in Find.FactionManager.AllFactionsVisible.Where(f => !f.IsPlayer && f.ideos != null && f.ideos.Has(altar.Ideo) && !f.HostileTo(Faction.OfPlayer)))
                Faction.OfPlayer.TryAffectGoodwillWith(f, -10, canSendMessage: false, canSendHostilityLetter: false);
        }

        // ---- colony reactions
        static readonly Dictionary<int, (int tick, int count)> foreignCache = new Dictionary<int, (int, int)>();
        public static int ForeignShrineCount(Map map, Ideo own)
        {
            if (map == null) return 0; int now = Find.TickManager.TicksGame; int key = map.uniqueID * 1000 + (own?.id ?? 0);
            if (foreignCache.TryGetValue(key, out var c) && now - c.tick < 250) return c.count;
            int n = ForeignShrines(map, own).Select(a => a.Ideo).Distinct().Count();
            foreignCache[key] = (now, n); return n;
        }
    }

    // ---------------------------------------------------------------------------------------------------------------- guest activity and job
    public class PrayActivity : IGuestActivity
    {
        public string Id => "dlc.pray";
        public ModuleId Module => ModuleId.CityLife;
        public float Desire(Pawn guest, GuestInfo info)
        {
            if (!DlcDefs.Ideology || guest?.Ideo == null || ShrineApi.RecentlyPrayed(guest)) return 0f;
            return ShrineApi.Altars(guest.Map).Any(a => a.Ideo == guest.Ideo) ? 0.8f + 0.6f * (guest.ideo?.Certainty ?? 0.5f) : 0f;
        }
        public Job TryGiveJob(Pawn guest, GuestInfo info)
        {
            var a = ShrineApi.FindAltarFor(guest); if (a == null) return null;
            var def = DefDatabase<JobDef>.GetNamedSilentFail("HH_PrayAtShrine"); if (def == null) return null;
            return JobMaker.MakeJob(def, a);
        }
    }

    public class JobDriver_PrayAtShrine : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);
            var pray = Toils_General.Wait(450, TargetIndex.A);
            pray.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            pray.WithProgressBarToilDelay(TargetIndex.A);
#if RW16PLUS
            pray.tickIntervalAction = delegate (int delta) { pawn.rotationTracker.FaceTarget(TargetA); };
#else
            pray.tickAction = delegate { pawn.rotationTracker.FaceTarget(TargetA); };
#endif
            yield return pray;
            yield return new Toil
            {
                initAction = () => { if (TargetA.Thing is Building_GuestAltar a) ShrineApi.Visit(pawn, a); },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }
    }

    // ---------------------------------------------------------------------------------------------------------------- colony thoughts
    public class ThoughtWorker_HereticShrine : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (!DlcDefs.Ideology || !Modules.On(ModuleId.CityLife) || !HSettings.GetBool("dlc.shrineReactions")) return ThoughtState.Inactive;
            if (p.Faction != Faction.OfPlayer || p.MapHeld == null || p.Ideo == null || p.IsSlave) return ThoughtState.Inactive;
            bool zealous = p.Ideo.memes.Any(m => m.defName == "Proselytizer" || m.defName == "Supremacist");
            if (!zealous) return ThoughtState.Inactive;
            int n = ShrineApi.ForeignShrineCount(p.MapHeld, p.Ideo);
            return n <= 0 ? ThoughtState.Inactive : ThoughtState.ActiveAtStage(Math.Min(3, n) - 1);
        }
    }

    public class ThoughtWorker_ShrineDiversity : ThoughtWorker
    {
        static readonly string[] Dislike = { "IdeoDiversity_Abhorrent", "IdeoDiversity_Horrible", "IdeoDiversity_Disapproved" };
        static readonly string[] Like = { "IdeoDiversity_Approved", "IdeoDiversity_Respected", "IdeoDiversity_Exalted" };
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (!DlcDefs.Ideology || !Modules.On(ModuleId.CityLife) || !HSettings.GetBool("dlc.shrineReactions")) return ThoughtState.Inactive;
            if (p.Faction != Faction.OfPlayer || p.MapHeld == null || p.Ideo == null || p.IsSlave) return ThoughtState.Inactive;
            if (ShrineApi.ForeignShrineCount(p.MapHeld, p.Ideo) <= 0) return ThoughtState.Inactive;
            foreach (var pr in p.Ideo.PreceptsListForReading)
            {
                string n = pr.def.defName;
                if (Array.IndexOf(Dislike, n) >= 0) return ThoughtState.ActiveAtStage(2);
                if (n == "IdeoDiversity_Approved") return ThoughtState.ActiveAtStage(0);
                if (n == "IdeoDiversity_Respected" || n == "IdeoDiversity_Exalted") return ThoughtState.ActiveAtStage(1);
            }
            return ThoughtState.Inactive;
        }
    }
}
