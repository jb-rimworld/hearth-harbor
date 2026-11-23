// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Hearth.Casino
{
    /// <summary>
    /// Guest seats of gaming tables and slot machines (F17, 1.1.0). One plan decides where guests play: chairs first (most comfortable
    /// first), then cells spread around the table. The same plan is drawn while placing a table (ghost), shown on a selected table and used
    /// by the jobs, so what the player sees is where guests sit. A guest who sits comfortably stakes more and stays longer; the odds never change.
    /// </summary>
    public static class CasinoSeats
    {
        public const string ComfortSpendKey = "casino.comfortSpend";
        public const string ComfortStayKey = "casino.comfortStay";

        public static void RegisterSettings()
        {
            const ModuleId M = ModuleId.Casino;
            HSettings.Percent(M, "Seats", ComfortSpendKey, 0.25f, 0f, 0.6f, "Comfortable seats: more spending",
                "A guest on a seat with comfort 100 % stakes this much more per round (scaled by the seat's comfort; standing = no bonus). The house edge stays the same: more play, not better odds.", hardMax: 2f);
            HSettings.Percent(M, "Seats", ComfortStayKey, 0.30f, 0f, 1f, "Comfortable seats: longer stays",
                "A guest on a seat with comfort 100 % plays this much longer before leaving the table.", hardMax: 3f);
        }

        static float Setting(string key, float fallback) { try { return HSettings.Get(key); } catch (Exception) { return fallback; } }
        public static float SpendBonus(float comfort) => Mathf.Max(0f, Setting(ComfortSpendKey, 0.25f)) * Mathf.Clamp01(comfort);
        public static float StayBonus(float comfort) => Mathf.Max(0f, Setting(ComfortStayKey, 0.30f)) * Mathf.Clamp01(comfort);

        // ------------------------------------------------------------------ the plan (pure)

        /// <summary>
        /// Picks <paramref name="seats"/> cells: cells with a chair first (highest comfort first), then the free cells farthest from the
        /// cells already chosen (so four players spread around a table instead of crowding one side). Deterministic for the same input.
        /// <paramref name="comfort"/>[i] is the chair comfort of candidate i, or a negative number for "no chair".
        /// </summary>
        public static List<IntVec3> Plan(IList<IntVec3> cands, IList<float> comfort, int seats)
        {
            var res = new List<IntVec3>();
            PlanInto(cands, comfort, seats, res);
            return res;
        }

        static readonly List<int> chairScratch = new List<int>();

        /// <summary><see cref="Plan"/> into a list the caller owns (no allocation; the placement ghost runs every frame).</summary>
        public static void PlanInto(IList<IntVec3> cands, IList<float> comfort, int seats, List<IntVec3> res)
        {
            res.Clear();
            if (cands == null || seats <= 0) return;
            var chairs = chairScratch; chairs.Clear();
            for (int i = 0; i < cands.Count; i++) if (comfort[i] >= 0f) chairs.Add(i);
            chairs.Sort((a, b) => comfort[b] != comfort[a] ? comfort[b].CompareTo(comfort[a]) : a.CompareTo(b));
            for (int k = 0; k < chairs.Count && res.Count < seats; k++) res.Add(cands[chairs[k]]);
            while (res.Count < seats)
            {
                int best = -1; float bestD = -1f;
                for (int i = 0; i < cands.Count; i++)
                {
                    if (comfort[i] >= 0f || res.Contains(cands[i])) continue;
                    float d = res.Count == 0 ? 0f : float.MaxValue;
                    for (int j = 0; j < res.Count; j++) d = Mathf.Min(d, (cands[i] - res[j]).LengthHorizontalSquared);
                    if (d > bestD) { bestD = d; best = i; }
                }
                if (best < 0) break;
                res.Add(cands[best]);
            }
        }

        /// <summary>Comfort of the seat on a cell (any sittable building), or -1 when there is none.</summary>
        public static float ChairComfort(IntVec3 c, Map map)
        {
            if (map == null || !c.InBounds(map)) return -1f;
            var e = c.GetEdifice(map);
            if (e == null || e.def.building == null || !e.def.building.isSittable) return -1f;
            return Mathf.Max(0f, e.GetStatValue(StatDefOf.Comfort));
        }

        /// <summary>Candidate cells around a building that is (or would be) at center/rot: the interaction cell for a one-seat machine,
        /// otherwise every free cell next to a side, except the croupier's.</summary>
        public static List<IntVec3> Candidates(ThingDef def, IntVec3 center, Rot4 rot, Map map, int seats, out IntVec3 croupier)
        {
            var res = new List<IntVec3>();
            CandidatesInto(def, center, rot, map, seats, res, out croupier);
            return res;
        }

        /// <summary><see cref="Candidates"/> into a list the caller owns. Walks the cells next to each side of the footprint (no enumerator).</summary>
        public static void CandidatesInto(ThingDef def, IntVec3 center, Rot4 rot, Map map, int seats, List<IntVec3> res, out IntVec3 croupier)
        {
            res.Clear();
            croupier = def.hasInteractionCell ? ThingUtility.InteractionCellWhenAt(def, center, rot, map) : IntVec3.Invalid;
            if (map == null) return;
            if (seats <= 1 && def.hasInteractionCell)
            {
                if (croupier.InBounds(map) && croupier.Standable(map)) res.Add(croupier);
                croupier = IntVec3.Invalid;
                return;
            }
            var r = GenAdj.OccupiedRect(center, rot, def.size);
            for (int x = r.minX; x <= r.maxX; x++) AddCand(new IntVec3(x, 0, r.minZ - 1), croupier, map, res);   // south side
            for (int z = r.minZ; z <= r.maxZ; z++) AddCand(new IntVec3(r.maxX + 1, 0, z), croupier, map, res);   // east side
            for (int x = r.maxX; x >= r.minX; x--) AddCand(new IntVec3(x, 0, r.maxZ + 1), croupier, map, res);   // north side
            for (int z = r.maxZ; z >= r.minZ; z--) AddCand(new IntVec3(r.minX - 1, 0, z), croupier, map, res);   // west side
        }

        static void AddCand(IntVec3 c, IntVec3 croupier, Map map, List<IntVec3> res)
        {
            if (c != croupier && c.InBounds(map) && c.Standable(map)) res.Add(c);
        }

        static readonly List<float> comfortScratch = new List<float>();
        static readonly List<IntVec3> candScratch = new List<IntVec3>();
        static readonly List<IntVec3> ghostPlan = new List<IntVec3>();

        /// <summary>The seat plan for a building that is (or would be) at center/rot.</summary>
        public static List<IntVec3> PlanAt(ThingDef def, IntVec3 center, Rot4 rot, Map map, out IntVec3 croupier)
        {
            var res = new List<IntVec3>();
            PlanInto(def, center, rot, map, res, out croupier);
            return res;
        }

        /// <summary>The seat plan into a shared list (valid until the next call; for drawing only).</summary>
        public static List<IntVec3> PlanScratch(ThingDef def, IntVec3 center, Rot4 rot, Map map, out IntVec3 croupier)
        {
            PlanInto(def, center, rot, map, ghostPlan, out croupier);
            return ghostPlan;
        }

        static void PlanInto(ThingDef def, IntVec3 center, Rot4 rot, Map map, List<IntVec3> res, out IntVec3 croupier)
        {
            var props = def.GetCompProperties<CompProperties_CasinoGame>();
            int seats = props?.seats ?? 1;
            CandidatesInto(def, center, rot, map, seats, candScratch, out croupier);
            if (props == null || !props.needsCroupier) croupier = IntVec3.Invalid;
            comfortScratch.Clear();
            for (int i = 0; i < candScratch.Count; i++) comfortScratch.Add(ChairComfort(candScratch[i], map));
            PlanInto(candScratch, comfortScratch, seats, res);
        }

        // ------------------------------------------------------------------ live seats of a placed game

        sealed class Cached { public int Tick; public List<IntVec3> Cells; public int Chairs; }
        static readonly Dictionary<int, Cached> cache = new Dictionary<int, Cached>();
        static CasinoSeats() { HearthGame.OnGameInit += () => cache.Clear(); }

        /// <summary>Where guests play at this game (at most its seat count). Cached for two seconds.</summary>
        public static List<IntVec3> SeatCells(CompCasinoGame comp)
        {
            var t = comp?.parent;
            if (t == null || !t.Spawned) return new List<IntVec3>();
            int now = Find.TickManager.TicksGame;
            if (cache.TryGetValue(t.thingIDNumber, out var c) && now >= c.Tick && now - c.Tick < 120) return c.Cells;
            var cells = PlanAt(t.def, t.Position, t.Rotation, t.Map, out _);
            int chairs = 0;
            foreach (var cell in cells) if (ChairComfort(cell, t.Map) >= 0f) chairs++;
            cache[t.thingIDNumber] = new Cached { Tick = now, Cells = cells, Chairs = chairs };
            return cells;
        }

        public static int ChairsAt(CompCasinoGame comp)
        {
            SeatCells(comp);
            return comp?.parent != null && cache.TryGetValue(comp.parent.thingIDNumber, out var c) ? c.Chairs : 0;
        }

        public static void MarkDirty(Thing t) { if (t != null) cache.Remove(t.thingIDNumber); }

        // ------------------------------------------------------------------ comfort and spending

        /// <summary>Comfort of what the pawn sits on (0 when standing).</summary>
        public static float SeatComfort(Pawn p)
        {
            if (p?.Map == null) return 0f;
            return Mathf.Max(0f, ChairComfort(p.Position, p.Map));
        }

        /// <summary>A guest sits down at a game: a comfortable seat raises the stake per round and the length of the stay.</summary>
        public static void ApplyComfort(GamblerSession s, Pawn pawn)
        {
            if (s == null || pawn == null || s.Kind != SessionKind.Guest) return;
            float comfort = SeatComfort(pawn);
            if (comfort <= 0f) return;
            s.StakeShare *= 1f + SpendBonus(comfort);
            int now = Find.TickManager.TicksGame;
            if (s.EndTick > now) s.EndTick = now + Mathf.RoundToInt((s.EndTick - now) * (1f + StayBonus(comfort)));
            if (comfort >= 0.6f) GuestApi.AddSatisfaction(pawn, 0.01f, "HH.Casino.ComfySeat".TranslateOr("A comfortable seat at the table"));
        }

        /// <summary>"Guest seats 4 · 2 with chairs (+12 % stakes)".</summary>
        public static string SeatsLine(CompCasinoGame comp)
        {
            var cells = SeatCells(comp);
            int seats = comp.Props.seats, chairs = ChairsAt(comp);
            float avg = 0f; int n = 0;
            foreach (var c in cells) { float k = ChairComfort(c, comp.parent.Map); if (k >= 0f) { avg += k; n++; } }
            avg = n > 0 ? avg / n : 0f;
            string s = "HH.Casino.SeatsLine".TranslateOr("Guest seats: {0} of {1} free to use · {2} with a chair").Replace("{0}", cells.Count.ToString()).Replace("{1}", seats.ToString()).Replace("{2}", chairs.ToString());
            if (chairs > 0) s += " (" + "HH.Casino.SeatsBonus".TranslateOr("+{0} % stakes").Replace("{0}", (SpendBonus(avg) * 100f).ToString("0")) + ")";
            else if (seats > 0 && SpendBonus(1f) > 0f) s += " · " + "HH.Casino.SeatsTip".TranslateOr("chairs raise stakes up to +{0} %").Replace("{0}", (SpendBonus(1f) * 100f).ToString("0"));
            return s;
        }

        // ------------------------------------------------------------------ drawing

        public static readonly Color SeatCol = new Color(0.45f, 0.72f, 1f);
        public static readonly Color ChairCol = new Color(0.45f, 0.95f, 0.5f);
        public static readonly Color CroupierCol = new Color(1f, 0.8f, 0.3f);
        static readonly List<IntVec3> drawSeats = new List<IntVec3>(), drawChairs = new List<IntVec3>(), drawCroupier = new List<IntVec3>();

        /// <summary>Outlines the seats (blue), seats with a chair (green) and the croupier's cell (amber).
        /// <paramref name="pulseFree"/>: seats without a chair pulse ("a chair here raises stakes"), used while placing a game.</summary>
        public static void Draw(List<IntVec3> seats, IntVec3 croupier, Map map, bool pulseFree = false)
        {
            drawSeats.Clear(); drawChairs.Clear(); drawCroupier.Clear();
            for (int i = 0; i < seats.Count; i++) (ChairComfort(seats[i], map) >= 0f ? drawChairs : drawSeats).Add(seats[i]);
            if (croupier.IsValid && map != null && croupier.InBounds(map)) drawCroupier.Add(croupier);
            if (drawSeats.Count > 0)
            {
                var col = SeatCol;
                if (pulseFree && SpendBonus(1f) > 0f) col = Color.Lerp(SeatCol, ChairCol, 0.5f + 0.5f * Mathf.Sin(Time.realtimeSinceStartup * 4f));
                GenDraw.DrawFieldEdges(drawSeats, col);
            }
            if (drawChairs.Count > 0) GenDraw.DrawFieldEdges(drawChairs, ChairCol);
            if (drawCroupier.Count > 0) GenDraw.DrawFieldEdges(drawCroupier, CroupierCol);
        }
    }

    /// <summary>Shows the guest seats and the croupier's place while a game is being placed (F17).</summary>
    public class PlaceWorker_CasinoSeats : PlaceWorker
    {
        public override void DrawGhost(ThingDef def, IntVec3 center, Rot4 rot, Color ghostCol, Thing thing = null)
        {
            var map = Find.CurrentMap;
            if (map == null || !Modules.On(ModuleId.Casino)) return;
            var seats = CasinoSeats.PlanScratch(def, center, rot, map, out var croupier);
            CasinoSeats.Draw(seats, croupier, map, pulseFree: true);
        }

        public override void DrawPlaceMouseAttachments(float curX, ref float curY, BuildableDef bdef, IntVec3 center, Rot4 rot)
        {
            var def = bdef as ThingDef; var map = Find.CurrentMap;
            if (def == null || map == null || !Modules.On(ModuleId.Casino)) return;
            var props = def.GetCompProperties<CompProperties_CasinoGame>();
            int want = props?.seats ?? 1;
            var seats = CasinoSeats.PlanScratch(def, center, rot, map, out var croupier);
            int chairs = 0; for (int i = 0; i < seats.Count; i++) if (CasinoSeats.ChairComfort(seats[i], map) >= 0f) chairs++;
            string line = "HH.Casino.GhostSeats".TranslateOr("Guest seats: {0} / {1}").Replace("{0}", seats.Count.ToString()).Replace("{1}", want.ToString());
            if (want > 1) line += " · " + "HH.Casino.GhostChairs".TranslateOr("{0} with a chair").Replace("{0}", chairs.ToString());
            var r = new Rect(curX, curY, 999f, 24f);
            GUI.color = seats.Count < want ? new Color(1f, 0.55f, 0.45f) : CasinoSeats.SeatCol;
            Widgets.Label(r, line);
            curY += 22f;
            GUI.color = new Color(0.8f, 0.8f, 0.8f);
            string tip = want > 1 && props != null && props.needsCroupier
                ? "HH.Casino.GhostTipTable".TranslateOr("Blue: where guests play · amber: the croupier · put chairs on the pulsing cells to raise stakes")
                : "HH.Casino.GhostTipSlot".TranslateOr("Blue: where the player sits · a chair or stool on the pulsing cell raises stakes");
            Widgets.Label(new Rect(curX, curY, 999f, 24f), tip);
            curY += 22f;
            GUI.color = Color.white;
        }
    }

    /// <summary>Inspect line and selection overlay of the guest seats of a placed game (F17). No saved state.</summary>
    public class CompProperties_CasinoSeats : CompProperties
    {
        public CompProperties_CasinoSeats() { compClass = typeof(CompCasinoSeats); }
    }

    public class CompCasinoSeats : ThingComp
    {
        CompCasinoGame Game => parent.TryGetComp<CompCasinoGame>();

        public override void PostDrawExtraSelectionOverlays()
        {
            base.PostDrawExtraSelectionOverlays();
            var g = Game; if (g == null || !parent.Spawned || !Modules.On(ModuleId.Casino)) return;
            CasinoSeats.Draw(CasinoSeats.SeatCells(g), g.NeedsCroupier ? g.CroupierCell : IntVec3.Invalid, parent.Map);
        }

        public override string CompInspectStringExtra()
        {
            var g = Game; if (g == null || !parent.Spawned || !Modules.On(ModuleId.Casino)) return null;
            return Guard.Run(ModuleId.Casino, "Casino.SeatsLine", () => CasinoSeats.SeatsLine(g), null);
        }
    }

    /// <summary>Setup checklist row of the casino (F12 + F17): how many guest seats of the room's games have a chair.</summary>
    public class CasinoChairsCheck : IVenueSetupCheck
    {
        public bool Evaluate(Venue v, VenueRequirement r, out int have, out int need, out string note)
        {
            have = 0; need = 0; note = null;
            if (v?.Room == null) return false;
            var map = v.Map;
            foreach (var comp in CasinoBank.AllDevices(map))
            {
                if (comp?.parent == null || !comp.parent.Spawned || comp.parent.GetRoom() != v.Room) continue;
                var seats = CasinoSeats.SeatCells(comp);
                need += seats.Count;
                have += CasinoSeats.ChairsAt(comp);
            }
            if (need == 0) return false;
            note = "HH.Casino.ChairsNote".TranslateOr("up to +{0} % stakes on a comfortable seat").Replace("{0}", (CasinoSeats.SpendBonus(1f) * 100f).ToString("0"));
            return true;
        }
    }
}
