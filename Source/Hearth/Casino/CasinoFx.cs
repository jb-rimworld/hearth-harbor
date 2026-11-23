// SPDX-License-Identifier: MIT
// Casino life on the map (1.1.0, F31): the machines and tables show what happens (reels spin and stop, the wheel turns and the ball
// settles, cards fly to the seats), players react to wins, losses and near misses (small icons and sounds), and a busy casino room has a
// soft ambience. Everything here is transient display: nothing is saved, nothing touches money or results (those are decided by the
// engine before any of this runs). Gates: Modules.On(Casino) plus the global settings casino.worldFx / casino.reactions / casino.ambience.
using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using Hearth.CasinoMath;

namespace Hearth.Casino
{
    [DefOf]
    public static class CasinoFxDefOf
    {
        public static FleckDef HH_Fleck_Coin, HH_Fleck_Sparkle, HH_Fleck_Groan, HH_Fleck_NearMiss, HH_Fleck_Cheer, HH_Fleck_Chips;
        public static EffecterDef HH_Effecter_PlayCards, HH_Effecter_PlayRoulette, HH_Effecter_Croupier;
        public static SoundDef HH_World_SlotPlay, HH_World_SlotStop, HH_World_RouletteRoll, HH_World_CardSnap, HH_World_Shuffle, HH_World_WinBell, HH_World_WinBig,
            HH_World_Cheer, HH_World_Groan, HH_World_CashOut, HH_World_Ambience;
        static CasinoFxDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(CasinoFxDefOf)); }
    }

    /// <summary>What a device shows between rounds (not saved: after a load the tables simply start with their idle look).</summary>
    public sealed class DeviceShow
    {
        public int[] Reels;              // slots: last result
        public int Pocket = -1, Zeros = 1; // roulette: last pocket
        public int RoundTick = -99999;   // when the last round was decided
        public double Mult;
        public int Seq;                  // rounds seen (cosmetic seeds)
        public int Seats;                // players in the last round (cards to deal)
        public float WheelBase;          // wheel angle at the start of the last spin
        public RouletteSpinPlan Plan;
        public bool BigWin;
    }

    public enum FxReaction { SmallWin, BigWin, Jackpot, NearMiss, Loss, CashOutUp, CashOutDown }

    public static class CasinoFx
    {
        /// <summary>Ticks the table shows a round: slot reels stop by then, the ball settles, the cards are out.</summary>
        public const int SlotShowTicks = 80, RouletteShowTicks = 210, CardShowTicks = 60;

        static readonly Dictionary<int, DeviceShow> shows = new Dictionary<int, DeviceShow>();
        static readonly Dictionary<int, int> lastReaction = new Dictionary<int, int>();     // pawn id → tick
        static readonly Dictionary<int, int> lossStreak = new Dictionary<int, int>();       // pawn id → losses in a row
        static readonly List<(int tick, Action act)> queue = new List<(int, Action)>();
        static readonly Dictionary<int, (int tick, int count)> mapReactions = new Dictionary<int, (int, int)>();   // map id → reactions in the current second
        static readonly List<int> pruneKeys = new List<int>();
        static readonly HashSet<int> playing = new HashSet<int>();
        /// <summary>At most this many reactions per map and second; big wins and jackpots have their own, higher cap.</summary>
        public const int MapReactionsPerSecond = 4, MapBigReactionsPerSecond = 8;
        // ambience
        static Sustainer ambience; static int ambienceMap = -1; static IntVec3 ambienceCell; static int ambienceCheck = -9999; static bool ambienceWanted;
        // counters for the tests
        public static int Flecks, Sounds, Reactions, OverlayDraws, AmbienceStarts;

        static CasinoFx() { HearthGame.OnGameInit += Clear; }

        public static void Clear()
        {
            shows.Clear(); lastReaction.Clear(); lossStreak.Clear(); queue.Clear(); mapReactions.Clear();
            try { ambience?.End(); } catch (Exception) { }
            ambience = null; ambienceMap = -1; ambienceWanted = false;
            Flecks = Sounds = Reactions = OverlayDraws = AmbienceStarts = 0;
        }

        static bool Setting(string key) { try { return HSettings.GetBool(key); } catch (Exception) { return true; } }
        public static bool WorldOn => Modules.On(ModuleId.Casino) && Setting(CS.WorldFx);
        public static bool ReactOn => Modules.On(ModuleId.Casino) && Setting(CS.Reactions);
        public static bool AmbienceOn => Modules.On(ModuleId.Casino) && Setting(CS.Ambience);

        public static DeviceShow ShowOf(CompCasinoGame comp, bool create = false)
        {
            if (comp?.parent == null) return null;
            int id = comp.parent.thingIDNumber;
            if (!shows.TryGetValue(id, out var s) && create) { s = new DeviceShow(); shows[id] = s; }
            return s;
        }

        /// <summary>A device left the map (destroyed, minified, moved): its show is dropped (called from <see cref="CompCasinoGame"/>.PostDeSpawn).</summary>
        public static void Forget(CompCasinoGame comp)
        {
            if (comp?.parent != null) shows.Remove(comp.parent.thingIDNumber);
        }

        /// <summary>
        /// Drops what is no longer needed (every 250 ticks, from the ambience pass): reaction times older than the per-pawn pause, loss streaks of
        /// pawns that no longer play, shows of devices that are gone. Keeps the transient dictionaries as small as the casino.
        /// </summary>
        static void Prune(int now)
        {
            pruneKeys.Clear();
            foreach (var kv in lastReaction) if (now - kv.Value > 600) pruneKeys.Add(kv.Key);
            for (int i = 0; i < pruneKeys.Count; i++) lastReaction.Remove(pruneKeys[i]);
            pruneKeys.Clear();
            foreach (var kv in mapReactions) if (now - kv.Value.tick > 600) pruneKeys.Add(kv.Key);
            for (int i = 0; i < pruneKeys.Count; i++) mapReactions.Remove(pruneKeys[i]);
            if (lossStreak.Count > 0)
            {
                playing.Clear();
                var c = CasinoComponent.Get();
                if (c != null) foreach (var s in c.Sessions) if (s.Pawn != null && !s.Done) playing.Add(s.Pawn.thingIDNumber);
                pruneKeys.Clear();
                foreach (var kv in lossStreak) if (!playing.Contains(kv.Key)) pruneKeys.Add(kv.Key);
                for (int i = 0; i < pruneKeys.Count; i++) lossStreak.Remove(pruneKeys[i]);
            }
            if (shows.Count > 0)
            {
                // a device that despawned without PostDeSpawn (e.g. its map was removed) is dropped after a long idle time
                pruneKeys.Clear();
                foreach (var kv in shows) if (now - kv.Value.RoundTick > 60000) pruneKeys.Add(kv.Key);
                for (int i = 0; i < pruneKeys.Count; i++) shows.Remove(pruneKeys[i]);
            }
        }

        /// <summary>Tests: sizes of the transient dictionaries (shows, reaction times, loss streaks).</summary>
        public static (int shows, int reactions, int streaks) TrackedCounts => (shows.Count, lastReaction.Count, lossStreak.Count);

        // ---- notes from the engine (the round is decided) ------------------------------------------------------------
        public static void NoteSlots(CompCasinoGame comp, int a, int b, int c, double mult)
        {
            if (!WorldOn || comp?.parent == null || !comp.parent.Spawned) return;
            var s = ShowOf(comp, true); int now = Find.TickManager.TicksGame;
            s.Reels = new[] { a, b, c }; s.Mult = mult; s.RoundTick = now; s.Seq++;
            if (Near(comp.parent)) { Play(CasinoFxDefOf.HH_World_SlotPlay, comp.parent); Later(SlotShowTicks - 6, () => Play(CasinoFxDefOf.HH_World_SlotStop, comp.parent)); }
        }

        public static void NoteRoulette(CompCasinoGame comp, int pocket, int zeros)
        {
            if (!WorldOn || comp?.parent == null || !comp.parent.Spawned) return;
            var s = ShowOf(comp, true); int now = Find.TickManager.TicksGame;
            float start = s.Plan != null ? (float)s.Plan.WheelAngle(1.0) : 0f;
            s.Pocket = pocket; s.Zeros = zeros; s.RoundTick = now; s.Seq++; s.WheelBase = start;
            s.Plan = RouletteSpinPlan.Make(pocket, zeros, start, (ulong)(comp.parent.thingIDNumber * 7919 + s.Seq));
            if (Near(comp.parent)) Play(CasinoFxDefOf.HH_World_RouletteRoll, comp.parent);
        }

        public static void NoteCards(CompCasinoGame comp, int players)
        {
            if (!WorldOn || comp?.parent == null || !comp.parent.Spawned) return;
            var s = ShowOf(comp, true);
            s.RoundTick = Find.TickManager.TicksGame; s.Seq++; s.Seats = Math.Max(1, players);
            if (Near(comp.parent)) Play(CasinoFxDefOf.HH_World_CardSnap, comp.parent);
        }

        /// <summary>A settled round of one player: queues the reaction for the moment the table shows the result.</summary>
        public static void NoteResult(CompCasinoGame comp, GamblerSession s, Money stake, Money payout)
        {
            if (s?.Pawn == null || comp == null || stake.Micro <= 0) return;
            double mult = payout.Silver / stake.Silver;
            int id = s.Pawn.thingIDNumber;
            if (mult > 1.0001) lossStreak[id] = 0;
            else { lossStreak.TryGetValue(id, out int n); lossStreak[id] = n + 1; }
            if (!ReactOn) return;
            FxReaction? r = null;
            if (comp.Game == CasinoGame.Slots && mult >= 200 || mult >= 100) r = FxReaction.Jackpot;
            else if (mult >= 20) r = FxReaction.BigWin;
            else if (mult >= 1.5) r = FxReaction.SmallWin;
            else if (comp.Game == CasinoGame.Slots && mult <= 0 && ShowOf(comp)?.Reels is int[] rl && rl[0] == rl[1] && rl[0] >= 4 && rl[2] != rl[0]) r = FxReaction.NearMiss;
            else if (lossStreak.TryGetValue(id, out int streak) && streak >= 4 && streak % 4 == 0) r = FxReaction.Loss;
            if (r == null) return;
            int delay = comp.Game == CasinoGame.Slots ? SlotShowTicks : comp.Game == CasinoGame.Roulette ? RouletteShowTicks : CardShowTicks;
            var pawn = s.Pawn; var kind = r.Value;
            Later(delay, () => React(pawn, comp, kind));
        }

        public static void NoteSessionEnd(GamblerSession s)
        {
            if (s?.Pawn == null) return;
            lossStreak.Remove(s.Pawn.thingIDNumber);
            if (!ReactOn || s.Rounds < 3 || s.Kind == SessionKind.Demo) return;
            React(s.Pawn, s.Comp, s.Net.IsPositive ? FxReaction.CashOutUp : FxReaction.CashOutDown, force: s.Net.IsPositive);
        }

        // ---- reactions ----------------------------------------------------------------------------------------------------------
        /// <summary>
        /// Shows a reaction above a pawn: at most one per pawn every 300 ticks (big wins and jackpots skip that pause) and a few per map and
        /// second: <see cref="MapReactionsPerSecond"/> for everything, <see cref="MapBigReactionsPerSecond"/> including big wins, so a busy
        /// casino never floods the screen.
        /// </summary>
        public static bool React(Pawn p, CompCasinoGame comp, FxReaction kind, bool force = false)
        {
            if (p == null || !p.Spawned || p.Map == null || !ReactOn) return false;
            int now = Find.TickManager.TicksGame;
            bool big = kind == FxReaction.BigWin || kind == FxReaction.Jackpot;
            if (!big && !force && lastReaction.TryGetValue(p.thingIDNumber, out int t) && now - t < 300) return false;
            int mapId = p.Map.uniqueID;
            if (!mapReactions.TryGetValue(mapId, out var mr) || now - mr.tick > 60) mr = (now, 0);
            if (mr.count >= (big ? MapBigReactionsPerSecond : MapReactionsPerSecond)) return false;
            mapReactions[mapId] = (mr.tick, mr.count + 1);
            lastReaction[p.thingIDNumber] = now; Reactions++;
            bool visible = Visible(p.Position, p.Map);
            var loc = p.DrawPos + new Vector3(0f, 0f, 0.6f);
            switch (kind)
            {
                case FxReaction.SmallWin:
                    Fleck(loc, p.Map, CasinoFxDefOf.HH_Fleck_Coin, 0.7f, visible, Rand.Range(-30f, 30f), 0.6f);
                    if (visible) Play(CasinoFxDefOf.HH_World_WinBell, p);
                    break;
                case FxReaction.BigWin:
                case FxReaction.Jackpot:
                    for (int i = 0; i < (kind == FxReaction.Jackpot ? 9 : 5); i++) Fleck(loc, p.Map, i % 2 == 0 ? CasinoFxDefOf.HH_Fleck_Coin : CasinoFxDefOf.HH_Fleck_Sparkle, Rand.Range(0.5f, 0.9f), visible, Rand.Range(-60f, 60f), Rand.Range(0.8f, 1.6f));
                    Fleck(loc + new Vector3(0, 0, 0.3f), p.Map, CasinoFxDefOf.HH_Fleck_Cheer, 1.0f, visible, 0f, 0.35f);
                    if (visible) { Play(CasinoFxDefOf.HH_World_WinBig, p); Later(30, () => Play(CasinoFxDefOf.HH_World_Cheer, p)); }
                    // the people around clap: a small cheer icon over the other players within five cells
                    foreach (var other in NearbyPlayers(p, 5f))
                    {
                        var o = other;
                        Later(Rand.Range(8, 40), () => { if (o.Spawned) Fleck(o.DrawPos + new Vector3(0, 0, 0.6f), o.Map, CasinoFxDefOf.HH_Fleck_Cheer, 0.6f, Visible(o.Position, o.Map), 0f, 0.3f); });
                    }
                    break;
                case FxReaction.NearMiss:
                    Fleck(loc, p.Map, CasinoFxDefOf.HH_Fleck_NearMiss, 0.8f, visible, 0f, 0.4f);
                    if (visible) Play(CasinoFxDefOf.HH_World_Groan, p, 0.7f);
                    break;
                case FxReaction.Loss:
                case FxReaction.CashOutDown:
                    Fleck(loc, p.Map, CasinoFxDefOf.HH_Fleck_Groan, 0.8f, visible, 0f, 0.25f);
                    if (visible) Play(CasinoFxDefOf.HH_World_Groan, p);
                    break;
                case FxReaction.CashOutUp:
                    Fleck(loc, p.Map, CasinoFxDefOf.HH_Fleck_Chips, 0.8f, visible, 0f, 0.5f);
                    if (visible) Play(CasinoFxDefOf.HH_World_CashOut, p);
                    break;
            }
            return true;
        }

        static IEnumerable<Pawn> NearbyPlayers(Pawn p, float radius)
        {
            var c = CasinoComponent.Get(); if (c == null) yield break;
            foreach (var s in c.Sessions)
            {
                var o = s.Pawn; if (o == null || o == p || !o.Spawned || o.Map != p.Map) continue;
                if ((o.Position - p.Position).LengthHorizontalSquared <= radius * radius) yield return o;
            }
        }

        static void Fleck(Vector3 loc, Map map, FleckDef def, float scale, bool visible, float angle, float speed)
        {
            if (def == null || map == null) return;
            Flecks++;
            if (!visible) return;
            try
            {
                var d = FleckMaker.GetDataStatic(loc, map, def, scale);
                d.velocityAngle = angle; d.velocitySpeed = speed; d.rotationRate = Rand.Range(-30f, 30f);
                map.flecks.CreateFleck(d);
            }
            catch (Exception e) { HLog.Once("casino.fx.fleck", LogLevel.Warn, "Casino", "fleck failed: " + e.Message); }
        }

        static void Play(SoundDef def, Thing at, float volume = 1f)
        {
            if (def == null || at == null || !at.Spawned) return;
            Sounds++;
            try { var info = SoundInfo.InMap(new TargetInfo(at.Position, at.Map)); info.volumeFactor = volume; def.PlayOneShot(info); }
            catch (Exception e) { HLog.Once("casino.fx.sound." + def.defName, LogLevel.Warn, "Casino", "sound failed: " + e.Message); }
        }

        /// <summary>Only what the player can see gets drawn and heard (cheap: the current map and the camera view).</summary>
        public static bool Visible(IntVec3 c, Map map)
        {
            if (map == null || map != Find.CurrentMap) return false;
            try { return Find.CameraDriver.CurrentViewRect.ExpandedBy(3).Contains(c); } catch (Exception) { return false; }
        }
        static bool Near(Thing t) => t != null && t.Spawned && Visible(t.Position, t.Map);

        static void Later(int ticks, Action a) { if (a == null) return; if (queue.Count > 256) queue.RemoveAt(0); queue.Add((Find.TickManager.TicksGame + Math.Max(0, ticks), a)); }

        // ---- per tick (from the module) -----------------------------------------------------------------------------------------------
        public static void Tick(int now)
        {
            if (queue.Count > 0)
            {
                for (int i = 0; i < queue.Count;)
                {
                    if (queue[i].tick <= now) { var a = queue[i].act; queue.RemoveAt(i); try { a(); } catch (Exception e) { HLog.Once("casino.fx.later", LogLevel.Warn, "Casino", "reaction failed: " + e.Message); } }
                    else i++;
                }
            }
            TickAmbience(now);
        }

        /// <summary>One soft room sound for the busiest casino room on the current map (two or more players, camera close).</summary>
        static void TickAmbience(int now)
        {
            if (now - ambienceCheck >= 250)
            {
                ambienceCheck = now; ambienceWanted = false;
                Prune(now);
                if (AmbienceOn && CasinoFxDefOf.HH_World_Ambience != null)
                {
                    var map = Find.CurrentMap; var c = CasinoComponent.Get();
                    if (map != null && c != null)
                    {
                        // the device with the most players around it
                        var counts = new Dictionary<Room, (int n, IntVec3 cell)>();
                        foreach (var s in c.Sessions)
                        {
                            var d = s.Device; if (d == null || !d.Spawned || d.Map != map || s.Done) continue;
                            var room = d.GetRoom(); if (room == null || room.PsychologicallyOutdoors) continue;
                            counts.TryGetValue(room, out var e); counts[room] = (e.n + 1, e.n == 0 ? d.Position : e.cell);
                        }
                        Room best = null; int bn = 0; IntVec3 cell = IntVec3.Invalid;
                        foreach (var kv in counts) if (kv.Value.n > bn) { best = kv.Key; bn = kv.Value.n; cell = kv.Value.cell; }
                        if (best != null && bn >= 2)
                        {
                            var centre = cell; try { var ext = best.ExtentsClose; centre = ext.CenterCell; } catch (Exception) { }
                            var cam = Find.CameraDriver.MapPosition;
                            if ((cam - centre).LengthHorizontalSquared <= 30 * 30) { ambienceWanted = true; ambienceCell = centre; }
                        }
                    }
                }
            }
            var m = Find.CurrentMap;
            if (ambienceWanted && m != null)
            {
                if (ambience == null || ambience.Ended || ambienceMap != m.uniqueID)
                {
                    try { ambience?.End(); } catch (Exception) { }
                    try
                    {
                        ambience = CasinoFxDefOf.HH_World_Ambience.TrySpawnSustainer(SoundInfo.InMap(new TargetInfo(ambienceCell, m), MaintenanceType.PerTick));
                        ambienceMap = m.uniqueID; AmbienceStarts++;
                    }
                    catch (Exception e) { HLog.Once("casino.fx.ambience", LogLevel.Warn, "Casino", "ambience failed: " + e.Message); ambience = null; ambienceWanted = false; }
                }
                ambience?.Maintain();
            }
            else if (ambience != null) { try { ambience.End(); } catch (Exception) { } ambience = null; }
        }

        public static int Queued => queue.Count;
        public static bool AmbiencePlaying => ambience != null && !ambience.Ended;

        // ---- effecters for the play and croupier toils --------------------------------------------------------------------------------
        public static EffecterDef PlayEffecter(CompCasinoGame comp)
        {
            if (comp == null || !WorldOn) return null;
            switch (comp.Game)
            {
                case CasinoGame.Slots: return null;          // the machine itself spins and rattles
                case CasinoGame.Roulette: return CasinoFxDefOf.HH_Effecter_PlayRoulette;
                default: return CasinoFxDefOf.HH_Effecter_PlayCards;
            }
        }

        public static EffecterDef CroupierEffecter(CompCasinoGame comp) => comp != null && WorldOn && comp.Game != CasinoGame.Slots ? CasinoFxDefOf.HH_Effecter_Croupier : null;

        // ---- the device overlay -----------------------------------------------------------------------------------------------------------
        // overlay parts are plain single graphics (cached per texture and size); Graphic.Draw takes an extra rotation for the wheel
        static readonly Dictionary<(string, int, int), Graphic> graphics = new Dictionary<(string, int, int), Graphic>();
        static Graphic G(string path, float w, float h)
        {
            var key = (path, (int)Math.Round(w * 1000), (int)Math.Round(h * 1000));
            if (!graphics.TryGetValue(key, out var g))
            {
                g = ContentFinder<Texture2D>.Get(path, false) != null ? GraphicDatabase.Get<Graphic_Single>(path, ShaderDatabase.Transparent, new Vector2(w, h), Color.white) : null;
                graphics[key] = g;
            }
            return g;
        }

        static Thing drawingFor;     // the device being drawn (Graphic.Draw wants a thing; 1.7 reads it)
        static void Quad(Vector3 c, float w, float h, float angle, string path, float alt)
        {
            var g = G(path, w, h); if (g == null || drawingFor == null) return;
            g.Draw(new Vector3(c.x, alt, c.z), Rot4.North, drawingFor, angle);
            OverlayDraws++;
        }

        /// <summary>Draws the live parts of a device on the map (called from <see cref="CompCasinoGame.PostDraw"/>; things outside the view are not drawn by the game).</summary>
        public static void DrawOverlay(CompCasinoGame comp)
        {
            var t = comp.parent;
            if (!WorldOn || t == null || !t.Spawned || t.Map != Find.CurrentMap) return;
            if (!(t.def.graphicData?.texPath ?? "").StartsWith("Things/HH/Casino")) return;     // re-textured by another mod: our anchors would not fit
            var s = ShowOf(comp);
            int now = Find.TickManager.TicksGame;
            float alt = AltitudeLayer.BuildingOnTop.AltitudeFor() + 0.02f;
            var rot = t.Rotation; var centre = t.DrawPos;
            drawingFor = t;
            try
            {
                switch (comp.Game)
                {
                    case CasinoGame.Slots: DrawSlots(comp, s, now, centre, rot, alt); break;
                    case CasinoGame.Roulette: DrawRoulette(comp, s, now, centre, rot, alt); break;
                    default: DrawCards(comp, s, now, centre, rot, alt); break;
                }
            }
            finally { drawingFor = null; }
        }

        // The reel windows of Things/HH/Casino/SlotMachine (casino.py; design units 128 per cell, y down):
        //  south (front): reels at x 42/64/86, y 68.5, 14 x 27 each; marquee centre y 31
        //  east (profile, front to the right): the window slice x 78..94, y 44..88 shows the three reels edge-on, stacked; marquee centre (65, 28)
        //  west: the east image mirrored by the game (x → 128 − x); north (back): no reels, only the marquee light
        static int ReelSymbol(CompCasinoGame comp, DeviceShow s, int now, int i)
        {
            bool spinning = s != null && now - s.RoundTick < SlotShowTicks;
            if (spinning && now - s.RoundTick < 30 + i * 18) return (int)((now / 3 + i * 2 + s.Seq) % SlotPaytable.Symbols);
            if (s?.Reels != null) return s.Reels[i];
            return (6 - i * 2 + comp.parent.thingIDNumber) % SlotPaytable.Symbols;
        }

        static void DrawSlots(CompCasinoGame comp, DeviceShow s, int now, Vector3 c, Rot4 rot, float alt)
        {
            bool side = rot == Rot4.East || rot == Rot4.West;
            if (rot == Rot4.South || side)
            {
                for (int i = 0; i < 3; i++)
                {
                    int sym = ReelSymbol(comp, s, now, i);
                    if (sym < 0 || sym >= SlotPaytable.SymbolNames.Length) continue;
                    string tex = SlotSymbolIcons.Dir + SlotPaytable.SymbolNames[sym];
                    if (!side) Quad(c + new Vector3((42f + 22f * i - 64f) / 128f, 0, (64f - 68.5f) / 128f), 0.1f, 0.1f, 0f, tex, alt);
                    else
                    {
                        // seen from the side the three drums stand one above the other in the narrow window, squeezed to its width
                        float x = (86f - 64f) / 128f; if (rot == Rot4.West) x = -x;
                        Quad(c + new Vector3(x, 0, (64f - (52f + 14f * i)) / 128f), 0.07f, 0.095f, 0f, tex, alt);
                    }
                }
            }
            // the marquee blinks after a big win (seen from every side)
            if (s != null && s.Mult >= 20 && now - s.RoundTick < SlotShowTicks + 600 && (now / 10) % 2 == 0)
            {
                if (rot == Rot4.South) Quad(c + new Vector3(0f, 0f, (64f - 31f) / 128f), 0.62f, 0.3f, 0f, CasinoFxTex.SlotLight, alt + 0.01f);
                else if (rot == Rot4.North) Quad(c + new Vector3(0f, 0f, (64f - 25f) / 128f), 0.62f, 0.2f, 0f, CasinoFxTex.SlotLight, alt + 0.01f);
                else Quad(c + new Vector3((rot == Rot4.East ? 1f : -1f) * (65f - 64f) / 128f, 0f, (64f - 28f) / 128f), 0.48f, 0.22f, 0f, CasinoFxTex.SlotLight, alt + 0.01f);
            }
        }

        // the wheel of Things/HH/Casino/RouletteTable (casino.py: wheel at local x = 1.25 R + 14, y = h/2 of the felt; R = 77 units south/north, 73 east)
        static void WheelPlace(Rot4 rot, out Vector3 off, out float radius)
        {
            if (rot == Rot4.North) { off = new Vector3(0.506f, 0, 0.07f); radius = 77f / 128f; }
            else if (rot == Rot4.East) { off = new Vector3(0f, 0, -0.404f); radius = 73f / 128f; }
            else if (rot == Rot4.West) { off = new Vector3(0f, 0, -0.404f); radius = 73f / 128f; }
            else { off = new Vector3(-0.506f, 0, 0.07f); radius = 77f / 128f; }
        }

        // the layout of an idle wheel (no round yet): the current rules, read at most once a second (the setting is a dictionary lookup)
        static int idleZeros = 1, idleZerosFrame = -9999;
        static int IdleZeros()
        {
            int f = Time.frameCount;
            if (f - idleZerosFrame > 60) { idleZerosFrame = f; try { idleZeros = CS.Roulette.Zeros; } catch (Exception) { idleZeros = 1; } }
            return idleZeros;
        }

        static void DrawRoulette(CompCasinoGame comp, DeviceShow s, int now, Vector3 c, Rot4 rot, float alt)
        {
            WheelPlace(rot, out var off, out float R);
            var wc = c + off;
            double wheel, ball = 0, br = 0; bool showBall = false;
            if (s?.Plan != null)
            {
                double u = Mathf.Clamp01((now - s.RoundTick) / (float)RouletteShowTicks);
                wheel = s.Plan.WheelAngle(u) + Math.Max(0, now - s.RoundTick - RouletteShowTicks) * 0.05;
                ball = s.Plan.BallAngle(u) + Math.Max(0, now - s.RoundTick - RouletteShowTicks) * 0.05;
                br = s.Plan.BallRadius(u); showBall = true;
            }
            else wheel = (now * 0.05f) % 360f;
            // the pockets ring (0.78 R) turns; the ball rides on top
            // the drawn wheel must have the layout the ball follows (single or double zero)
            int zeros = s?.Plan != null ? s.Zeros : IdleZeros();
            Quad(wc, R * 2f * 0.8f, R * 2f * 0.8f, (float)wheel, zeros >= 2 ? CasinoFxTex.MiniWheelUS : CasinoFxTex.MiniWheel, alt);
            if (showBall)
            {
                double r = (0.52 + (0.72 - 0.52) * br) * R;
                double a = ball * Mathf.Deg2Rad;
                var p = wc + new Vector3((float)(Math.Sin(a) * r), 0, (float)(Math.Cos(a) * r));
                Quad(p, 0.07f, 0.07f, 0f, CasinoFxTex.Ball, alt + 0.01f);
            }
        }

        static void DrawCards(CompCasinoGame comp, DeviceShow s, int now, Vector3 c, Rot4 rot, float alt)
        {
            if (s == null) return;
            int age = now - s.RoundTick;
            if (age > CardShowTicks + 900) return;           // the table is cleared a while after a round
            var t = comp.parent;
            var dealer = comp.CroupierCell.IsValid && comp.parent.def.hasInteractionCell ? comp.CroupierCell.ToVector3Shifted() : c;
            // one card pair per player: from the dealer side to a point between the table centre and the seat
            int n = 0;
            var cc = CasinoComponent.Get(); if (cc == null) return;
            foreach (var sess in cc.Sessions)
            {
                if (sess.Device != t || sess.Pawn == null || !sess.Pawn.Spawned) continue;
                var seat = sess.Pawn.DrawPos;
                var target = Vector3.Lerp(c, seat, 0.42f);
                for (int k = 0; k < 2; k++)
                {
                    int start = n * 8 + k * 4;
                    float u = Mathf.Clamp01((age - start) / 14f);
                    if (u <= 0f) continue;
                    var p = Vector3.Lerp(Vector3.Lerp(c, dealer, 0.55f), target, (float)Ease.InOutCubic(u)) + new Vector3(k * 0.08f, 0, 0);
                    bool face = comp.Game == CasinoGame.Blackjack ? u >= 1f : false;
                    Quad(p, 0.16f, 0.23f, k == 0 ? -8f : 8f, face ? CasinoFxTex.CardFace : CasinoFxTex.CardBack, alt + 0.002f * (n * 2 + k));
                }
                // the player's chips in front of the cards: a stack that follows the purse
                double share = sess.BuyIn.Silver > 0 ? Math.Max(0.0, Math.Min(2.0, sess.Purse.Silver / sess.BuyIn.Silver)) : 1.0;
                int h = Mathf.Clamp((int)Math.Round(share * 3), 1, 5);
                var chipPos = Vector3.Lerp(c, seat, 0.62f);
                for (int k = 0; k < h; k++) Quad(chipPos + new Vector3(0, 0, k * 0.025f), 0.13f, 0.13f, 0f, CasinoFxTex.Chip, alt + 0.01f + k * 0.001f);
                n++; if (n >= 6) break;
            }
            // poker: the board in the middle, dealt one by one after the hole cards
            if (comp.Game == CasinoGame.Poker && n > 0)
            {
                for (int b = 0; b < 5; b++)
                {
                    int start = n * 8 + 6 + (b < 3 ? b * 3 : 12 + (b - 3) * 8);
                    if (age < start) break;
                    Quad(c + new Vector3((b - 2) * 0.19f, 0, 0.05f), 0.16f, 0.23f, 0f, CasinoFxTex.CardFace, alt + 0.003f);
                }
            }
        }
    }

    /// <summary>Texture paths of the device overlays (Things/HH/Casino/Fx/…).</summary>
    public static class CasinoFxTex
    {
        const string D = "Things/HH/Casino/Fx/";
        public const string MiniWheel = D + "MiniWheel", MiniWheelUS = D + "MiniWheelUS", Ball = D + "Ball", CardFace = D + "CardFace", CardBack = D + "CardBack", Chip = D + "Chip", SlotLight = D + "SlotLight";
    }
}
