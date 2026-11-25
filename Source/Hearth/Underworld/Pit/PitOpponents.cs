// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace Hearth.Underworld
{
    /// <summary>
    /// Opponents for a lone fighter (B.2 of the 1.2.0 brief, step 1: the pit beast) and the bookkeeping of pawns that were brought into the
    /// arena and must be taken back. A pit beast is a temporary animal of the house: player faction (no goodwill, no manhunter, no HarmedMe),
    /// spawned inside the arena, matched to the fighter's power, and gone after the bout (vanished, never killed or released). It is saved by
    /// reference in its <see cref="CompFightPit"/>, so a beast that outlives its bout (save/load, pit removed, aborted bout) is removed by the
    /// caretaker sweep in <see cref="FightBout.TickAll"/>. It cannot be slaughtered, released or sold (<see cref="Patch_PitBeastDesignators"/>).
    /// </summary>
    public static class PitOpponents
    {
        static readonly HashSet<Pawn> beasts = new HashSet<Pawn>();
        static readonly Dictionary<Pawn, IntVec3> returning = new Dictionary<Pawn, IntVec3>();

        static PitOpponents() { HearthGame.OnGameInit += () => returning.Clear(); }

        /// <summary>The sweep after a load rebuilds the set from the saved beast lists of the pits.</summary>
        internal static void ResetBeasts() => beasts.Clear();

        public static bool AnyBeast => beasts.Count > 0;
        public static bool IsBeast(Pawn p) => p != null && beasts.Count > 0 && beasts.Contains(p);
        internal static void NoteBeast(Pawn p) { if (p != null) beasts.Add(p); }

        /// <summary>Step 2 of B.2 (humanlike prizefighter) is not built in 1.2.0; kept as the one place the fighter list asks.</summary>
        public static bool IsPrizefighter(Pawn p) => false;

        public static bool AnyReturning => returning.Count > 0;
        public static bool IsReturning(Pawn p) => p != null && returning.Count > 0 && returning.ContainsKey(p);

        // ---- bring in and take back ----------------------------------------------------------------------
        /// <summary>Puts a pawn that cannot walk into the arena (animal behind a pen fence, prisoner behind a prison door) onto an arena cell and remembers where it came from.</summary>
        public static bool BringIn(Pawn p, IntVec3 cell)
        {
            if (p == null || !p.Spawned || p.Downed) return false;
            if (!returning.ContainsKey(p)) returning[p] = p.Position;
            p.jobs?.StopAll();
            p.Position = cell; p.Notify_Teleported(true, true);
            return true;
        }

        /// <summary>Takes a pawn that was brought in back to where it stood before the bout (or the nearest free cell).</summary>
        public static void TakeBack(Pawn p)
        {
            if (p == null || !returning.TryGetValue(p, out var from)) return;
            returning.Remove(p);
            if (!p.Spawned || p.Dead) return;
            var map = p.Map;
            if (!from.InBounds(map) || !from.Standable(map)) from = CellFinder.StandableCellNear(from, map, 6f);
            if (!from.IsValid) return;
            try { p.jobs?.StopAll(); p.Position = from; p.Notify_Teleported(true, true); }
            catch (Exception e) { HLog.Warn("Underworld", "Could not take " + p.LabelShort + " back from the pit: " + e.Message); }
        }

        // ---- the pit beast ----------------------------------------------------------------------------------
        public static Money Fee => HSettings.GetSilver("underworld.pit.opponentFee");

        /// <summary>Why the "Call an opponent" gizmo cannot be used now (translated), or null.</summary>
        public static string CannotHire(CompFightPit pit, Pawn fighter)
        {
            string closed = pit.ClosedReason();
            if (closed != null) return closed;
            if (FightBout.OfPit(pit) != null) return UwPay.T("Pit.Why.BoutRunning");
            if (pit.HasBeast) return UwPay.T("Pit.Why.BeastThere");
            if (fighter == null) return UwPay.T("Pit.Why.NoLoneFighter");
            if (Ledger.Current.Balance(Underworld.Till(pit.Map)) < Fee) return UwPay.T("Pit.Why.TillShort", PitFighters.Silver(Fee));
            // (no animal-kind lookup here: this runs for the gizmo every frame; KindFor always finds the closest animal anyway)
            return null;
        }

        /// <summary>An animal kind whose combat power is close to the fighter's: the map's wild animals first, then any animal. Power ratio within the fairness limit.</summary>
        public static PawnKindDef KindFor(float power, Map map)
        {
            float ratio = Math.Max(1.1f, Math.Min(1.6f, HSettings.Get("underworld.pit.fairRatio")));
            bool Fits(PawnKindDef k) => k?.RaceProps != null && k.RaceProps.Animal && k.RaceProps.IsFlesh && !k.RaceProps.Dryad && k.combatPower > 0f
                && UnderworldMath.FairPair(k.combatPower, power, ratio) && !k.RaceProps.IsAnomalyEntity
                && k.race?.tradeTags?.Contains("AnimalInsectSpace") != true;
            var wild = map?.Biome?.AllWildAnimals?.Where(Fits).ToList();
            var pool = wild != null && wild.Count > 0 ? wild : DefDatabase<PawnKindDef>.AllDefsListForReading.Where(Fits).ToList();
            if (pool.Count == 0)
            {
                // nothing within the ratio: the closest animal there is
                return DefDatabase<PawnKindDef>.AllDefsListForReading.Where(k => k?.RaceProps != null && k.RaceProps.Animal && k.RaceProps.IsFlesh && !k.RaceProps.Dryad && k.combatPower > 0f)
                    .OrderBy(k => Math.Abs(k.combatPower - power)).FirstOrDefault();
            }
            pool.Sort((x, y) => x.defName.CompareTo(y.defName));
            return pool[JRand.Stream("underworld.pit.beast").Range(0, pool.Count)];
        }

        /// <summary>Hires a beast for the fighter and starts the bout at once (manual start). The fee is booked only after the beast stands in the arena.</summary>
        public static FightBout HireBeast(CompFightPit pit, Pawn fighter, out string why)
        {
            why = CannotHire(pit, fighter);
            if (why != null) return null;
            var map = pit.Map;
            var kind = KindFor(PitFighters.Power(fighter), map);
            var arena = pit.ArenaInfo();
            var cells = arena.Cells.Where(c => c.Standable(map) && c.GetFirstPawn(map) == null).ToList();
            if (cells.Count == 0) { why = UwPay.T("Pit.Why.ArenaFull"); return null; }
            Pawn beast = null;
            try
            {
                beast = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, Faction.OfPlayer, PawnGenerationContext.NonPlayer, map.Tile, forceGenerateNewPawn: true, canGeneratePawnRelations: false));
                var stages = beast.RaceProps.lifeStageAges;
                if (stages != null && stages.Count > 0 && !beast.ageTracker.Adult)
                {
                    long adult = (long)(stages[stages.Count - 1].minAge * 3600000f) + 60000L;
                    beast.ageTracker.AgeBiologicalTicks = adult; beast.ageTracker.AgeChronologicalTicks = Math.Max(beast.ageTracker.AgeChronologicalTicks, adult);
                }
                var cell = cells[JRand.Stream("underworld.pit.beast").Range(0, cells.Count)];
                GenSpawn.Spawn(beast, cell, map);
            }
            catch (Exception e) { HLog.Error("Underworld", "Could not hire a pit beast", e); if (beast != null && !beast.Destroyed) beast.Destroy(); why = UwPay.T("Pit.Why.NoBeastKind"); return null; }
            NoteBeast(beast); pit.AddBeast(beast);
            if (!Ledger.Current.Post(Underworld.Till(map), "world", Fee, "Fight pit: opponent fee"))
            {
                Dismiss(beast); pit.RemoveBeast(beast);
                why = UwPay.T("Pit.Why.TillShort", PitFighters.Silver(Fee)); return null;
            }
            HLog.Info("Underworld", $"Pit beast hired: {beast.LabelShort} ({kind.defName}, power {kind.combatPower:0}) for {fighter.LabelShort} (power {PitFighters.Power(fighter):0}), fee {PitFighters.Silver(Fee)}");
            var bout = FightBout.Start(pit, fighter, beast, false, 300, manual: true);
            if (bout == null) { Dismiss(beast); pit.RemoveBeast(beast); why = UwPay.T("Pit.Why.NoBout"); }
            return bout;
        }

        /// <summary>Removes a beast for good: no corpse, no letter, nothing left (it was returned to its owner).</summary>
        public static void Dismiss(Pawn beast)
        {
            if (beast == null) return;
            beasts.Remove(beast);
            returning.Remove(beast);
            try
            {
                if (beast.Corpse is Corpse c && !c.Destroyed) { c.Destroy(DestroyMode.Vanish); }
                var caravan = beast.GetCaravan();
                if (caravan != null) caravan.RemovePawn(beast);
                if (!beast.Destroyed) beast.Destroy(DestroyMode.Vanish);
                if (Find.WorldPawns.Contains(beast)) Find.WorldPawns.RemoveAndDiscardPawnViaGC(beast);
            }
            catch (Exception e) { HLog.Warn("Underworld", "Could not remove the pit beast " + beast.LabelShort + ": " + e.Message); }
        }
    }
}
