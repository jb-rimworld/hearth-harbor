// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Hearth
{
    /// <summary>
    /// "Switch work side" (F13, 1.1.0) for counters, desks and tables with a work cell: turns the building by 180 degrees on the very
    /// same cells, so the worker stands on the other side and the art turns with it. Bills, power, storage and links stay; nothing is
    /// saved apart from the vanilla position and rotation, so removing the mod leaves the building as it stands.
    /// <para><c>legacyFlip</c>: the art of these defs faced the wrong way before 1.1.0 and their work cell moved to the front with it.
    /// A building from an older save is turned once while loading, so its worker keeps standing where they stood (now in front).</para>
    /// Lives in Hearth.Core so that the bank (SL_ defs, standalone Silverline) can use it too.
    /// </summary>
    public class CompProperties_FlipWorkSide : CompProperties
    {
        public bool legacyFlip;
        public CompProperties_FlipWorkSide() { compClass = typeof(CompFlipWorkSide); }
    }

    /// <summary>
    /// The legacy turn for plans: blueprints and frames of a <see cref="LegacyWorkSideExtension"/> def that were placed before 1.1.0 are turned
    /// once while loading, like the buildings, so the finished building has its worker where the player planned. Added to the generated
    /// blueprint and frame defs at startup (<see cref="LegacyWorkSidePlans"/>). Saves one bool; vanilla ignores it when the mod is gone.
    /// </summary>
    public class CompProperties_LegacyWorkSidePlan : CompProperties
    {
        public CompProperties_LegacyWorkSidePlan() { compClass = typeof(CompLegacyWorkSidePlan); }
    }

    public class CompLegacyWorkSidePlan : ThingComp
    {
        bool sideChecked;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (!respawningAfterLoad) sideChecked = true;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref sideChecked, "hhSideChecked");
            if (Scribe.mode == LoadSaveMode.PostLoadInit && !sideChecked)
            {
                sideChecked = true;
                var built = parent.def.entityDefToBuild as ThingDef;
                if (built != null && built.rotatable && built.HasModExtension<LegacyWorkSideExtension>() && !parent.Spawned && parent.ParentHolder == null && parent.Position.IsValid)
                {
                    var before = parent.Position;
                    var pos = CompFlipWorkSide.PositionAfterFlip(built, parent.Position, parent.Rotation);
                    parent.Rotation = parent.Rotation.Opposite;
                    parent.Position = pos;
                    HLog.Debug("Core", $"{parent.def.defName} at {before} turned once for its new work side (1.1.0)");
                }
            }
        }
    }

    /// <summary>Puts <see cref="CompProperties_LegacyWorkSidePlan"/> on the blueprint and frame defs of every legacy def (after the implied defs exist).</summary>
    [StaticConstructorOnStartup]
    public static class LegacyWorkSidePlans
    {
        static LegacyWorkSidePlans()
        {
            int n = 0;
            try
            {
                foreach (var d in DefDatabase<ThingDef>.AllDefsListForReading)
                {
                    if (!d.HasModExtension<LegacyWorkSideExtension>()) continue;
                    foreach (var plan in new[] { d.blueprintDef, d.frameDef })
                    {
                        if (plan == null) continue;
                        if (plan.comps == null) plan.comps = new List<CompProperties>();
                        if (plan.comps.Any(c => c is CompProperties_LegacyWorkSidePlan)) continue;
                        plan.comps.Add(new CompProperties_LegacyWorkSidePlan());
                        n++;
                    }
                }
            }
            catch (Exception e) { HLog.Warn("Core", "Legacy work side for plans not set up: " + e.Message); }
            if (n > 0) HLog.Debug("Core", $"Legacy work side: {n} blueprint/frame defs watch for pre-1.1.0 plans");
        }
    }

    /// <summary>Marks a def whose work cell moved to the other side in 1.1.0 (its art faces the rotation). Buildings of older saves are turned once
    /// while loading (<see cref="CompFlipWorkSide"/>), so their worker keeps the same cell. An extension instead of a comp field because a child def
    /// cannot change the properties of a comp it inherits.</summary>
    public class LegacyWorkSideExtension : DefModExtension { }

    [StaticConstructorOnStartup]
    public class CompFlipWorkSide : ThingComp
    {
        static readonly Texture2D Icon = ContentFinder<Texture2D>.Get("UI/HH/Core/Cmd_FlipSide", false) ?? BaseContent.BadTex;

        /// <summary>False only in buildings saved before 1.1.0 (the legacy turn is done once, then this is true).</summary>
        bool sideChecked;

        public CompProperties_FlipWorkSide Props => (CompProperties_FlipWorkSide)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (!respawningAfterLoad) sideChecked = true;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref sideChecked, "hhSideChecked");
            if (Scribe.mode == LoadSaveMode.PostLoadInit && !sideChecked)
            {
                sideChecked = true;
                // a building standing on a map in a pre-1.1.0 save (not minified, not yet spawned): keep the worker's cell
                if ((Props.legacyFlip || parent.def.HasModExtension<LegacyWorkSideExtension>()) && !parent.Spawned && parent.ParentHolder == null && parent.Position.IsValid)
                    ApplyLegacyTurn(parent);
            }
        }

        /// <summary>The one-time turn of a building from a pre-1.1.0 save (not spawned yet): same cells, opposite rotation.</summary>
        public static void ApplyLegacyTurn(Thing t)
        {
            if (t == null || t.Spawned || !t.def.rotatable || !t.Position.IsValid) return;
            var before = t.Position;
            var pos = PositionAfterFlip(t.def, t.Position, t.Rotation);
            t.Rotation = t.Rotation.Opposite;
            t.Position = pos;
            HLog.Debug("Core", $"{t.def.defName} at {before} turned once for its new work side (1.1.0)");
        }

        // ------------------------------------------------------------------ geometry

        /// <summary>Position that keeps the occupied cells when the rotation turns by 180° (even sizes shift the centre cell).</summary>
        public static IntVec3 PositionAfterFlip(ThingDef def, IntVec3 pos, Rot4 rot)
        {
            var newRot = rot.Opposite;
            var oldRect = GenAdj.OccupiedRect(pos, rot, def.size);
            var atPos = GenAdj.OccupiedRect(pos, newRot, def.size);
            return pos + new IntVec3(oldRect.minX - atPos.minX, 0, oldRect.minZ - atPos.minZ);
        }

        /// <summary>The work cell the building would have after the turn.</summary>
        public static IntVec3 InteractionCellAfterFlip(Thing t)
        {
            var pos = PositionAfterFlip(t.def, t.Position, t.Rotation);
            return ThingUtility.InteractionCellWhenAt(t.def, pos, t.Rotation.Opposite, t.Map);
        }

        /// <summary>Can this building be turned now? The other side must be free to stand on.</summary>
        public static bool CanFlip(Thing t, out string reason)
        {
            reason = null;
            if (t == null || !t.Spawned || !t.def.hasInteractionCell || !t.def.rotatable) { reason = "HH.Core.FlipNoSide".TranslateOr("This has no work side."); return false; }
            var map = t.Map;
            var ic = InteractionCellAfterFlip(t);
            if (ic == t.InteractionCell) { reason = "HH.Core.FlipSame".TranslateOr("Both sides are the same."); return false; }
            if (!ic.InBounds(map) || !ic.Standable(map))
            {
                reason = "HH.Core.FlipBlocked".TranslateOr("The other side is blocked: a worker could not stand there.");
                return false;
            }
            if (InUse(t, out var user))
            {
                reason = "HH.Core.FlipBusy".TranslateOr("Busy: {0} is using it right now.").Replace("{0}", user.LabelShort);
                return false;
            }
            return true;
        }

        /// <summary>Is a pawn working at, sitting on or walking to this building right now (any target of its current job)?</summary>
        public static bool InUse(Thing t, out Pawn user)
        {
            user = null;
            var map = t?.Map; if (map == null) return false;
            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var j = pawns[i].CurJob;
                if (j == null) continue;
                if (j.targetA.Thing == t || j.targetB.Thing == t || j.targetC.Thing == t) { user = pawns[i]; return true; }
            }
            return false;
        }

        /// <summary>Turns a spawned building by 180° on its own cells. Returns false (and changes nothing) when it cannot.</summary>
        public static bool Flip(Thing t)
        {
            if (!CanFlip(t, out _)) return false;
            var map = t.Map;
            var rectBefore = t.OccupiedRect();
            var newPos = PositionAfterFlip(t.def, t.Position, t.Rotation);
            t.Rotation = t.Rotation.Opposite;
            t.Position = newPos;
            if (t.OccupiedRect() != rectBefore) HLog.Warn("Core", $"Switch work side moved {t} from {rectBefore} to {t.OccupiedRect()}");
            t.DirtyMapMesh(map);
            map.reachability.ClearCache();
            return true;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (var g in base.CompGetGizmosExtra()) yield return g;
            if (parent.Faction != Faction.OfPlayer || !parent.def.hasInteractionCell || !parent.def.rotatable) yield break;
            var cmd = new Command_Action
            {
                defaultLabel = "HH.Core.FlipSide".TranslateOr("Switch work side"),
                defaultDesc = "HH.Core.FlipSideDesc".TranslateOr("Turn this around on the spot so whoever works here stands on the other side. It keeps its place, bills and connections."),
                icon = Icon,
                action = () =>
                {
                    if (Flip(parent)) SoundDefOf.Tick_High.PlayOneShotOnCamera();
                },
                onHover = () =>
                {
                    if (!parent.Spawned) return;
                    GenDraw.DrawTargetHighlight(new LocalTargetInfo(InteractionCellAfterFlip(parent)));
                },
            };
            if (!CanFlip(parent, out string why)) cmd.Disable(why);
            yield return cmd;
        }
    }
}
