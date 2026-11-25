// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Hearth.Underworld
{
    /// <summary>Finds the fenced area around a marker: flood fill over walkable cells; doors, walls and fences are the boundary. Open areas (reaching the map edge or too big) are invalid.</summary>
    public static class Arena
    {
        public sealed class Info { public List<IntVec3> Cells = new List<IntVec3>(); public bool Closed; public IntVec3 Center; public int Builtick; }

        public static Info Flood(Map map, IntVec3 start, int cap)
        {
            var info = new Info { Builtick = Find.TickManager.TicksGame };
            var seen = new HashSet<IntVec3> { start }; var q = new Queue<IntVec3>(); q.Enqueue(start);
            bool leaked = false;
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                info.Cells.Add(c);
                if (info.Cells.Count > cap) { leaked = true; break; }
                for (int i = 0; i < 4; i++)
                {
                    var n = c + GenAdj.CardinalDirections[i];
                    if (!n.InBounds(map)) { leaked = true; continue; }
                    if (seen.Contains(n)) continue;
                    if (!n.Walkable(map) || n.GetDoor(map) != null) continue;
                    seen.Add(n); q.Enqueue(n);
                }
            }
            info.Closed = !leaked;
            if (info.Cells.Count > 0) info.Center = new IntVec3((int)info.Cells.Average(c => c.x), 0, (int)info.Cells.Average(c => c.z));
            return info;
        }

        /// <summary>A spot around the arena where a spectator can stand and see it.</summary>
        public static bool TrySpectatorCell(Map map, Info arena, Pawn p, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            if (arena == null || !arena.Closed) return false;
            var inside = new HashSet<IntVec3>(arena.Cells);
            var ring = new List<IntVec3>();
            foreach (var c in arena.Cells)
                for (int dx = -3; dx <= 3; dx++)
                    for (int dz = -3; dz <= 3; dz++)
                    {
                        var n = new IntVec3(c.x + dx, 0, c.z + dz);
                        if (!n.InBounds(map) || inside.Contains(n) || !n.Standable(map) || n.GetFirstBuilding(map) != null) continue;
                        if (n.GetRoom(map) is Room r && BackRoom.IsBackRoom(r)) continue;
                        ring.Add(n);
                    }
            foreach (var n in ring.Distinct().InRandomOrder())
                if ((p == null || p.CanReach(n, PathEndMode.OnCell, Danger.Some)) && GenSight.LineOfSight(n, arena.Center, map, true)) { cell = n; return true; }
            return false;
        }
    }

    public class CompProperties_FightPit : CompProperties { public CompProperties_FightPit() { compClass = typeof(CompFightPit); } }

    /// <summary>
    /// The fight pit marker (6.11, reworked in 1.2.0): marks a fenced arena. Fighters are the colony's animals, willing guests, prisoners (toggle)
    /// and the colonists and slaves the player ticks under "Fighters…". Bouts with a free humanlike are scored (nobody gets hurt), guests bet,
    /// the house takes its cut. Saved here: the toggles, the ticked fighters, hired beasts (so they vanish after a load) and the last bout.
    /// </summary>
    public class CompFightPit : ThingComp
    {
        public bool enabled = true, allowAnimals = true, allowGuests = true, allowPrisoners, allowVolunteers, lethal;
        public int nextBoutTick = -1;
        List<Pawn> ticked = new List<Pawn>();
        List<Pawn> beastList = new List<Pawn>();
        // last bout (kept for the inspect string and the Underworld tab)
        public string lastA, lastB; public int lastWinner = -2, lastTick = -1, settled; public long lastPoolMicro, lastHouseMicro;
        // transient
        Arena.Info cache;
        List<PitCandidate> cands; int candTick = int.MinValue;
        int lastAbortMsgTick = -999999;
        /// <summary>Sides picked in the Fighters window (null = anyone suitable). Not saved.</summary>
        public Pawn pickA, pickB;
        /// <summary>A colonist who asked to fight (right-click) while a bout was running; starts the next bout. Not saved.</summary>
        public Pawn queued;

        static readonly List<CompFightPit> all = new List<CompFightPit>();
        /// <summary>Spawned pits of the current game (entries of an earlier game or a removed map are dropped on read).</summary>
        public static IReadOnlyList<CompFightPit> All { get { all.RemoveAll(c => c?.parent == null || !c.parent.Spawned || c.parent.Map == null || Find.Maps == null || !Find.Maps.Contains(c.parent.Map)); return all; } }

        public Map Map => parent.Map;
        public Arena.Info ArenaInfo()
        {
            if (!parent.Spawned) return null;
            if (cache == null || Find.TickManager.TicksGame - cache.Builtick > 250 || Find.TickManager.TicksGame < cache.Builtick) cache = Arena.Flood(parent.Map, parent.Position, 700);
            return cache;
        }
        public void Invalidate() { cache = null; cands = null; guestsDirty = true; }

        public bool ArenaValid => ArenaInfo() is Arena.Info a && a.Closed && a.Cells.Count >= 9;

        // ---- ticked colonists, beasts ----------------------------------------------------------------------
        public bool IsTicked(Pawn p) => p != null && ticked.Contains(p);
        public void SetTicked(Pawn p, bool on)
        {
            if (p == null) return;
            if (on) { if (!ticked.Contains(p)) ticked.Add(p); } else ticked.Remove(p);
            cands = null;
        }
        public IReadOnlyList<Pawn> Ticked => ticked;
        public bool HasBeast { get { for (int i = 0; i < beastList.Count; i++) if (beastList[i] != null && !beastList[i].Destroyed) return true; return false; } }
        internal void AddBeast(Pawn p) { if (p != null && !beastList.Contains(p)) beastList.Add(p); cands = null; }
        internal void RemoveBeast(Pawn p) { beastList.Remove(p); cands = null; }
        internal void NoteBeasts() { foreach (var b in beastList) PitOpponents.NoteBeast(b); }

        /// <summary>Beasts with no bout of their own left (after a load, an abort, a removed pit) are sent back. Called by the bout caretaker.</summary>
        internal int DismissIdleBeasts()
        {
            int n = 0;
            for (int i = beastList.Count - 1; i >= 0; i--)
            {
                var b = beastList[i];
                if (b != null && FightBout.OfPawn(b) != null) continue;
                beastList.RemoveAt(i);
                if (b != null) { PitOpponents.Dismiss(b); n++; }
            }
            if (n > 0) cands = null;
            return n;
        }

        // ---- candidates (cached) --------------------------------------------------------------------------------
        /// <summary>Everyone who could fight here, with reasons. Rebuilt at most every 250 ticks, or at once when <paramref name="refresh"/> (window opened, box ticked).</summary>
        public List<PitCandidate> Candidates(bool refresh = false)
        {
            int now = Find.TickManager.TicksGame;
            if (refresh || cands == null || now - candTick >= 250 || now < candTick)
            {
                cands = Guard.Run(ModuleId.Underworld, "Pit.Candidates", () => PitFighters.Build(this), new List<PitCandidate>());
                candTick = now;
            }
            return cands;
        }

        /// <summary>Fighters ready for an automatic bout (no reason at all; hired beasts and prizefighters only fight their own bout).</summary>
        public List<PitCandidate> Ready(bool refresh = false)
        {
            var r = new List<PitCandidate>();
            foreach (var c in Candidates(refresh)) if (c.Ready && c.Kind != PitKind.Beast && c.Kind != PitKind.Prizefighter && c.Pawn != null && !c.Pawn.Destroyed) r.Add(c);
            return r;
        }

        /// <summary>Fighters a manual start may use (reasons that only keep pawns out of automatic bouts are ignored).</summary>
        public List<PitCandidate> ManualReady(bool refresh = false)
        {
            var r = new List<PitCandidate>();
            foreach (var c in Candidates(refresh)) if (c.ManualOk && c.Kind != PitKind.Beast && c.Kind != PitKind.Prizefighter && c.Pawn != null && !c.Pawn.Destroyed) r.Add(c);
            return r;
        }

        /// <summary>Why this pit holds no bout at all right now (translated), or null when it is open: module/Cozy, peaceful switch, closed, open arena.</summary>
        public string ClosedReason()
        {
            if (!Underworld.Visible) return UwPay.T("Pit.Status.Off");
            if (Underworld.Peaceful) return UwPay.T("Pit.Status.Peaceful");
            if (!Underworld.Allows(UwFeature.PitToIncapacitation)) return UwPay.T("Pit.Status.Off");
            if (!enabled) return UwPay.T("Pit.Status.Closed");
            if (!ArenaValid) return UwPay.T("Pit.ArenaOpen");
            return null;
        }

        bool guestsOn, guestsDirty = true; int guestsTick;
        /// <summary>At least one adult guest on the map (bets are the point of an automatic show). Cached for 250 ticks: the inspect string asks every frame.</summary>
        bool AdultGuestOn(Map map, bool refresh = false)
        {
            int now = Find.TickManager.TicksGame;
            if (refresh || guestsDirty || now - guestsTick >= 250 || now < guestsTick)
            {
                guestsOn = false; guestsTick = now; guestsDirty = false;
                foreach (var g in GuestApi.GuestsOn(map)) if (!Underworld.IsMinor(g) && !PitOpponents.IsPrizefighter(g)) { guestsOn = true; break; }
            }
            return guestsOn;
        }

        // ---- inspect ------------------------------------------------------------------------------------------------
        /// <summary>The one status line of the marker (B.5).</summary>
        public string StatusLine()
        {
            var b = FightBout.OfPit(this);
            if (b != null) return UwPay.T("Pit.Status.Running", b.A?.LabelShortCap ?? "?", b.B?.LabelShortCap ?? "?");
            string closed = ClosedReason();
            if (closed != null) return closed == UwPay.T("Pit.ArenaOpen") ? UwPay.T("Pit.Status.NeedArena") : closed;
            if (Ready().Count < 2) return UwPay.T("Pit.Status.WaitFighter");
            if (!AdultGuestOn(Map)) return UwPay.T("Pit.Status.WaitGuests");
            int now = Find.TickManager.TicksGame;
            float h = nextBoutTick < 0 ? HSettings.Get("underworld.pit.intervalHours") * 0.5f : Math.Max(0, nextBoutTick - now) / 2500f;
            return UwPay.T("Pit.Status.Next", h.ToString("0.0"));
        }

        public string ReadyLine()
        {
            var ready = Ready();
            if (ready.Count == 0) return UwPay.T("Pit.Ready.None");
            var parts = new List<string>();
            foreach (PitKind k in new[] { PitKind.Colonist, PitKind.Prisoner, PitKind.Animal, PitKind.Guest })
            {
                int n = 0; foreach (var c in ready) if (c.Kind == k) n++;
                if (n > 0) parts.Add(UwPay.T(n == 1 ? "Pit.Ready.One." + k : "Pit.Ready.Many." + k, n));
            }
            return UwPay.T("Pit.Ready", ready.Count, string.Join(", ", parts));
        }

        public string LastBoutLine()
        {
            if (lastTick < 0 || lastA == null) return null;
            if (lastWinner < 0) return UwPay.T("Pit.Last.Draw", lastA, lastB);
            string w = lastWinner == 0 ? lastA : lastB, l = lastWinner == 0 ? lastB : lastA;
            return UwPay.T("Pit.Last", w, l, PitFighters.Silver(new Money(lastPoolMicro)), PitFighters.Silver(new Money(lastHouseMicro)));
        }

        public override string CompInspectStringExtra()
        {
            return Guard.Run(ModuleId.Underworld, "Pit.Inspect", () =>
            {
                var lines = new List<string>();
                var a = ArenaInfo();
                if (a != null) lines.Add(a.Closed && a.Cells.Count >= 9 ? UwPay.T("Pit.ArenaOk", a.Cells.Count) : UwPay.T("Pit.ArenaOpen"));
                if (Underworld.Visible && !Underworld.Peaceful) lines.Add(ReadyLine());
                lines.Add(StatusLine());
                var last = LastBoutLine(); if (last != null) lines.Add(last);
                return string.Join("\n", lines);
            }, "");
        }

        // ---- gizmos ---------------------------------------------------------------------------------------------------
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (parent.Faction != Faction.OfPlayer || !Underworld.Visible) yield break;
            yield return Toggle("Pit.Enabled", PitSetup.IconOpen, () => enabled, v => enabled = v);
            yield return Toggle("Pit.Animals", PitSetup.IconAnimals, () => allowAnimals, v => allowAnimals = v);
            yield return Toggle("Pit.Guests", PitSetup.IconGuests, () => allowGuests, v => allowGuests = v);
            yield return Toggle("Pit.Prisoners", PitSetup.IconPrisoners, () => allowPrisoners, v => allowPrisoners = v);
            if (allowVolunteers) yield return Toggle("Pit.Volunteers", PitSetup.IconFighters, () => allowVolunteers, v => allowVolunteers = v);
            if (Underworld.Allows(UwFeature.PitToDeath)) yield return Toggle("Pit.Lethal", PitSetup.IconLethal, () => lethal, v => lethal = v);
            yield return new Command_Action
            {
                defaultLabel = UwPay.T("Pit.FightersBtn"), defaultDesc = UwPay.T("Pit.FightersBtn.Desc"), icon = PitSetup.IconFighters,
                action = () => Window_PitFighters.Open(this)
            };
            var start = new Command_Action
            {
                defaultLabel = UwPay.T("Pit.StartNow"), defaultDesc = UwPay.T("Pit.StartNow.Desc"), icon = PitSetup.IconStart,
                action = () => { if (!FightBout.TryStartFor(this, true, out string why)) Messages.Message(UwPay.T("Pit.CannotStart", why), parent, MessageTypeDefOf.RejectInput, false); }
            };
            string closed = ClosedReason();
            if (closed != null) start.Disable(closed);
            else if (FightBout.OfPit(this) != null) start.Disable(UwPay.T("Pit.Why.BoutRunning"));
            yield return start;
            var lone = LoneFighter();
            var hire = new Command_Action
            {
                defaultLabel = UwPay.T("Pit.CallOpponent", PitFighters.Silver(PitOpponents.Fee)), defaultDesc = UwPay.T("Pit.CallOpponent.Desc", PitFighters.Silver(PitOpponents.Fee)), icon = PitSetup.IconOpponent,
                action = () => { var f = LoneFighter(); if (PitOpponents.HireBeast(this, f, out string why) == null) Messages.Message(UwPay.T("Pit.CannotStart", why), parent, MessageTypeDefOf.RejectInput, false); }
            };
            string hw = PitOpponents.CannotHire(this, lone);
            if (hw != null) hire.Disable(hw);
            yield return hire;
        }

        Command_Toggle Toggle(string key, Texture2D icon, Func<bool> get, Action<bool> set) => new Command_Toggle
        {
            defaultLabel = UwPay.T(key), defaultDesc = UwPay.T(key + ".Desc"), icon = icon,
            isActive = get, toggleAction = () => { set(!get()); cands = null; }
        };

        /// <summary>The fighter a hired opponent is matched to: side A of the window, else a fighter a manual start may use (ticked colonists first).</summary>
        public Pawn LoneFighter()
        {
            if (pickA != null && !pickA.Destroyed && pickA.Spawned && pickA.Map == Map && FightBout.OfPawn(pickA) == null) return pickA;
            var m = ManualReady();
            foreach (var c in m) if (c.Kind == PitKind.Colonist && IsTicked(c.Pawn)) return c.Pawn;
            foreach (var c in m) if (c.Kind == PitKind.Colonist) return c.Pawn;
            return m.Count > 0 ? m[0].Pawn : null;
        }

        // ---- right-click on the marker ---------------------------------------------------------------------------
        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn selPawn)
        {
            if (selPawn == null || parent.Faction != Faction.OfPlayer || !Underworld.Visible) yield break;
            if (!selPawn.RaceProps.Humanlike || selPawn.IsPrisoner || !(selPawn.Faction == Faction.OfPlayer || selPawn.IsSlaveOfColony)) yield break;
            string label = UwPay.T("Pit.FightHere");
            string closed = ClosedReason();
            if (closed != null) { yield return new FloatMenuOption(label + ": " + closed, null); yield break; }
            var me = PitFighters.Colonist(this, selPawn);
            if (!me.ManualOk && me.Reason != UwPay.T("Pit.Why.NotTicked")) { yield return new FloatMenuOption(label + ": " + me.Reason, null); yield break; }
            if (FightBout.OfPit(this) != null)
            {
                yield return new FloatMenuOption(UwPay.T("Pit.FightHere.Queue"), () => { SetTicked(selPawn, true); queued = selPawn; });
                yield break;
            }
            var opp = FightBout.BestOpponent(this, selPawn, ManualReady(true));
            if (opp != null)
            {
                yield return new FloatMenuOption(UwPay.T("Pit.FightHere.Against", opp.LabelShortCap), () =>
                {
                    SetTicked(selPawn, true);
                    if (FightBout.Start(this, selPawn, opp, false, 300, manual: true) == null) Messages.Message(UwPay.T("Pit.CannotStart", UwPay.T("Pit.Why.NoBout")), parent, MessageTypeDefOf.RejectInput, false);
                });
                yield break;
            }
            yield return new FloatMenuOption(label + ": " + UwPay.T("Pit.Why.NoOpponent"), null);
            string hw = PitOpponents.CannotHire(this, selPawn);
            var hire = new FloatMenuOption(UwPay.T("Pit.CallOpponent", PitFighters.Silver(PitOpponents.Fee)) + (hw != null ? ": " + hw : ""), hw != null ? (Action)null : () =>
            {
                SetTicked(selPawn, true);
                if (PitOpponents.HireBeast(this, selPawn, out string why) == null) Messages.Message(UwPay.T("Pit.CannotStart", why), parent, MessageTypeDefOf.RejectInput, false);
            });
            yield return hire;
        }

        // ---- automatic bouts ------------------------------------------------------------------------------------------
        public override void CompTickRare()
        {
            if (!parent.Spawned) return;
            Guard.Run(ModuleId.Underworld, "Pit.TickRare", TickRare);
        }

        void TickRare()
        {
            if (ClosedReason() != null) return;
            int now = Find.TickManager.TicksGame;
            float interval = HSettings.Get("underworld.pit.intervalHours") * 2500f;
            if (nextBoutTick < 0) nextBoutTick = now + (int)(interval * 0.5f);
            if (FightBout.OfPit(this) != null) return;
            if (queued != null)
            {
                var q = queued; queued = null;
                if (!q.Destroyed && q.Spawned && q.Map == Map && PitFighters.Colonist(this, q).ManualOk)
                {
                    var opp = FightBout.BestOpponent(this, q, ManualReady(true));
                    if (opp != null && FightBout.Start(this, q, opp, false, 300, manual: true) != null) return;
                    Messages.Message(UwPay.T("Pit.CalledOff", UwPay.T("Pit.Why.NoOpponentFor", q.LabelShortCap)), parent, MessageTypeDefOf.RejectInput, false);
                }
            }
            if (now < nextBoutTick) return;
            if (!AdultGuestOn(Map, true)) { nextBoutTick = now + 2500; return; }   // nobody to bet: no show
            if (FightBout.TryStartFor(this, false, out _)) nextBoutTick = now + (int)interval;
            else nextBoutTick = now + 2500;
        }

        internal bool MayTellAbort(int now)
        {
            if (now - lastAbortMsgTick < 2500 && now >= lastAbortMsgTick) return false;
            lastAbortMsgTick = now; return true;
        }

        internal void Record(FightBout b, long poolMicro, long houseMicro)
        {
            lastA = b.A?.LabelShortCap ?? "?"; lastB = b.B?.LabelShortCap ?? "?";
            lastWinner = b.WinnerSide; lastPoolMicro = poolMicro; lastHouseMicro = houseMicro; lastTick = Find.TickManager.TicksGame;
            settled++;
            PitRecords.NoteSettled(Map);
        }

        // ---- saving and lifecycle -----------------------------------------------------------------------------------------
        public override void PostExposeData()
        {
            Scribe_Values.Look(ref enabled, "enabled", true); Scribe_Values.Look(ref allowAnimals, "allowAnimals", true);
            Scribe_Values.Look(ref allowGuests, "allowGuests", true);
            Scribe_Values.Look(ref allowPrisoners, "allowPrisoners"); Scribe_Values.Look(ref allowVolunteers, "allowVolunteers");
            Scribe_Values.Look(ref lethal, "lethal"); Scribe_Values.Look(ref nextBoutTick, "nextBout", -1);
            if (Scribe.mode == LoadSaveMode.Saving) { ticked.RemoveAll(p => p == null || p.Destroyed || p.Dead); beastList.RemoveAll(p => p == null || p.Destroyed); }
            Scribe_Collections.Look(ref ticked, "fighters", LookMode.Reference);
            Scribe_Collections.Look(ref beastList, "beasts", LookMode.Reference);
            Scribe_Values.Look(ref lastA, "lastA"); Scribe_Values.Look(ref lastB, "lastB");
            Scribe_Values.Look(ref lastWinner, "lastWinner", -2); Scribe_Values.Look(ref lastTick, "lastTick", -1);
            Scribe_Values.Look(ref lastPoolMicro, "lastPool"); Scribe_Values.Look(ref lastHouseMicro, "lastHouse");
            Scribe_Values.Look(ref settled, "settled");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (ticked == null) ticked = new List<Pawn>(); ticked.RemoveAll(p => p == null);
                if (beastList == null) beastList = new List<Pawn>(); beastList.RemoveAll(p => p == null);
                foreach (var b in beastList) PitOpponents.NoteBeast(b);   // still not the colony's until the caretaker sends it back
            }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (!all.Contains(this)) all.Add(this);
        }

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
            all.Remove(this);
            FightBout.OfPit(this)?.Abort("pit removed", null);
            DismissIdleBeasts();
        }

    }

    /// <summary>Per-map facts of the pit for other parts of the module (the Underworld tab, the guide): readable without UnderworldComponent.</summary>
    public static class PitRecords
    {
        static string Key(Map m) => "uw.pitSettled." + (m?.uniqueID ?? 0);

        /// <summary>How many bouts were settled (with a winner or a draw, not called off) on this map, ever. Saved per game (UiState), survives a removed pit.</summary>
        public static int Settled(Map m) => m == null ? 0 : (int)Hearth.Shell.UiState.Value(Key(m), 0f);
        public static bool EverSettled(Map m) => Settled(m) > 0;
        internal static void NoteSettled(Map m) { if (m != null) Hearth.Shell.UiState.SetValue(Key(m), Settled(m) + 1); }

        /// <summary>The latest "Last bout: …" line of any pit on the map, or null.</summary>
        public static string LastBoutLine(Map m)
        {
            CompFightPit best = null;
            foreach (var p in CompFightPit.All) if (p.Map == m && p.lastTick >= 0 && (best == null || p.lastTick > best.lastTick)) best = p;
            return best?.LastBoutLine();
        }

        /// <summary>The status line of the first pit on the map (the same line the marker shows), or null without a pit.</summary>
        public static string StatusLine(Map m)
        {
            foreach (var p in CompFightPit.All) if (p.Map == m) return p.StatusLine();
            return null;
        }
    }

    /// <summary>
    /// One bout. Transient (never saved): a bout interrupted by a save/load simply ends; its jobs fail on their first tick (<see cref="JobDriver_PitFight"/>),
    /// the caretaker sweep clears melee threats and sends hired beasts back, and the pit-cash caretaker refunds the stakes into the till.
    /// <para>Scored bouts (any free humanlike): every hit between the two fighters is absorbed and counted as points (the damage it would have done).
    /// A fighter loses when his points reach his limit = underworld.pit.scoreLimit (default 100) x his health scale (1 for a human, more for big
    /// animals); after maxBoutTicks the fighter with the smaller share of his limit wins (equal shares: draw). With fists a human deals about
    /// 3 damage per second, so the usual bout lasts 25 to 40 seconds at speed 1; a knife or a strong animal ends it sooner. The loser gets one or
    /// two small bruises at the end.</para>
    /// </summary>
    public sealed class FightBout
    {
        public enum Phase { Assemble, Betting, Fight, Done }

        static readonly List<FightBout> active = new List<FightBout>();
        static bool sweepPending; static int lastCare;
        public static int ActiveCount => active.Count;
        public static IReadOnlyList<FightBout> All => active;
        public static FightBout OfPawn(Pawn p) { if (p == null) return null; for (int i = 0; i < active.Count; i++) if (active[i].A == p || active[i].B == p) return active[i]; return null; }
        public static FightBout OfPit(CompFightPit pit) { for (int i = 0; i < active.Count; i++) if (active[i].Pit == pit) return active[i]; return null; }
        public static FightBout OfMap(Map m) { for (int i = 0; i < active.Count; i++) if (active[i].Map == m && active[i].phase != Phase.Done) return active[i]; return null; }

        /// <summary>The fight job of <paramref name="p"/> against <paramref name="foe"/> belongs to a running bout in its fight phase.</summary>
        public static bool Holds(Pawn p, Pawn foe)
        {
            var b = OfPawn(p);
            return b != null && b.phase == Phase.Fight && foe != null && ((b.A == p && b.B == foe) || (b.B == p && b.A == foe));
        }

        public CompFightPit Pit; public Map Map; public Pawn A, B;
        public PitKind KindA, KindB;
        public Phase phase = Phase.Assemble;
        public bool NonLethal = true, Draw, Scored, Manual;
        public float ScoreA, ScoreB, LimitA, LimitB;
        public int StartTick, FightTick, bettingEnd, MaxTicks;
        public readonly List<Pawn> Bettors = new List<Pawn>();
        public readonly List<UnderworldMath.Bet> Bets = new List<UnderworldMath.Bet>();
        public UnderworldMath.Settlement Result; public int WinnerSide = -2;
        public Money PursePaid;
        int lastOrderA, lastOrderB;
        Verse.AI.Group.LordToil toilA, toilB;
        public string PitAccount => Underworld.PitAccount(Map);
        public bool BettingOpen => phase == Phase.Assemble || phase == Phase.Betting;
        public bool Finished => phase == Phase.Done;
        public static event Action<FightBout> Settled;
        /// <summary>Text of the last result or called-off message any pit showed (tests, the Underworld tab).</summary>
        public static string LastMessage;

        public bool IsPair(Pawn victim, Thing instigator) => instigator != null && ((victim == A && instigator == B) || (victim == B && instigator == A));
        public Pawn Other(Pawn p) => p == A ? B : p == B ? A : null;
        public PitKind KindOf(Pawn p) => p == A ? KindA : KindB;

        /// <summary>A scored hit: the victim's points go up by the damage the blow would have done.</summary>
        public void Score(Pawn victim, float amount)
        {
            if (phase == Phase.Done || amount <= 0f || float.IsNaN(amount)) return;
            amount = Math.Min(amount, 60f);   // one hit never decides alone (a thrumbo horn, a crit)
            if (victim == A) ScoreA += amount; else if (victim == B) ScoreB += amount;
        }

        public float ShareA => LimitA > 0 ? ScoreA / LimitA : 0f;
        public float ShareB => LimitB > 0 ? ScoreB / LimitB : 0f;

        static float Limit(Pawn p) => Math.Max(20f, HSettings.Get("underworld.pit.scoreLimit") * Math.Max(0.3f, p?.HealthScale ?? 1f));

        // ---- pairing ------------------------------------------------------------------------------------
        static PitKind KindFor(CompFightPit pit, Pawn p)
        {
            if (PitOpponents.IsBeast(p)) return PitKind.Beast;
            if (PitOpponents.IsPrizefighter(p)) return PitKind.Prizefighter;
            if (p.IsPrisoner) return PitKind.Prisoner;
            if (!p.RaceProps.Humanlike) return PitKind.Animal;
            if (p.Faction == Faction.OfPlayer || p.IsSlaveOfColony) return PitKind.Colonist;
            return PitKind.Guest;
        }

        /// <summary>The opponent for a fighter from a candidate list: the one with the closest power (never the fighter himself, never a beast).</summary>
        public static Pawn BestOpponent(CompFightPit pit, Pawn me, List<PitCandidate> from)
        {
            Pawn best = null; float bestRatio = float.MaxValue, pw = PitFighters.Power(me);
            foreach (var c in from)
            {
                if (c.Pawn == me || c.Pawn == null || c.Kind == PitKind.Beast) continue;
                float r = Math.Max(c.Power, pw) / Math.Max(0.01f, Math.Min(c.Power, pw));
                if (r < bestRatio) { bestRatio = r; best = c.Pawn; }
            }
            return best;
        }

        /// <summary>
        /// Starts a bout. Automatic (manual=false): two ready fighters, a fair pair, chosen at random. Manual (the gizmo or the window): the picked
        /// sides of the window or the closest pair of fighters a manual start may use; fairness and the guest requirement do not apply, children,
        /// downed, drafted and hurt pawns never fight.
        /// </summary>
        public static bool TryStartFor(CompFightPit pit, bool manual, out string why)
        {
            why = pit.ClosedReason();
            if (why != null) return false;
            if (OfPit(pit) != null) { why = UwPay.T("Pit.Why.BoutRunning"); return false; }
            var c = manual ? pit.ManualReady(true) : pit.Ready();
            if (manual)
            {
                Pawn a = Valid(pit.pickA, c) ? pit.pickA : null, b = Valid(pit.pickB, c) && pit.pickB != a ? pit.pickB : null;
                if (a == null && b != null) { a = b; b = null; }
                if (a != null && b == null) b = BestOpponent(pit, a, c);
                if (a == null)
                {
                    float best = float.MaxValue;
                    for (int i = 0; i < c.Count; i++) for (int j = i + 1; j < c.Count; j++)
                    {
                        float r = Math.Max(c[i].Power, c[j].Power) / Math.Max(0.01f, Math.Min(c[i].Power, c[j].Power));
                        if (r < best) { best = r; a = c[i].Pawn; b = c[j].Pawn; }
                    }
                }
                if (a == null || b == null) { why = UwPay.T("Pit.NoFighters"); return false; }
                return Start(pit, a, b, false, 600, manual: true) != null;
            }
            if (c.Count < 2) { why = UwPay.T("Pit.NoFighters"); return false; }
            float ratio = HSettings.Get("underworld.pit.fairRatio");
            var pairs = new List<(Pawn, Pawn)>();
            for (int i = 0; i < c.Count; i++) for (int j = i + 1; j < c.Count; j++) if (UnderworldMath.FairPair(c[i].Power, c[j].Power, ratio)) pairs.Add((c[i].Pawn, c[j].Pawn));
            if (pairs.Count == 0) { why = UwPay.T("Pit.NoFairPair"); return false; }
            var pick = pairs[JRand.Stream("underworld.pit.pair").Range(0, pairs.Count)];
            return Start(pit, pick.Item1, pick.Item2, false, 600) != null;
        }

        static bool Valid(Pawn p, List<PitCandidate> c) { if (p == null) return false; foreach (var x in c) if (x.Pawn == p) return true; return false; }

        public static FightBout Start(CompFightPit pit, Pawn a, Pawn b, bool skipAssemble, int bettingTicks, bool manual = false)
        {
            if (pit == null || a == null || b == null || a == b || OfPawn(a) != null || OfPawn(b) != null) return null;
            if (Underworld.IsMinor(a) || Underworld.IsMinor(b)) return null;   // never children, whatever the caller says
            var bout = new FightBout { Pit = pit, Map = pit.Map, A = a, B = b, StartTick = Find.TickManager.TicksGame, Manual = manual };
            bout.KindA = KindFor(pit, a); bout.KindB = KindFor(pit, b);
            bout.MaxTicks = HSettings.GetInt("underworld.pit.maxBoutTicks");
            bout.Scored = PitFighters.FreeHumanlike(a) || PitFighters.FreeHumanlike(b);
            bout.NonLethal = bout.Scored || !(pit.lethal && Underworld.Allows(UwFeature.PitToDeath));
            bout.LimitA = Limit(a); bout.LimitB = Limit(b);
            bout.toilA = a.GetLord()?.CurLordToil; bout.toilB = b.GetLord()?.CurLordToil;
            active.Add(bout);
            if (skipAssemble) { bout.phase = Phase.Betting; bout.bettingEnd = bout.StartTick + bettingTicks; }
            else bout.bettingEnd = bettingTicks;
            pit.queued = pit.queued == a || pit.queued == b ? null : pit.queued;
            Underworld.Comp.Bouts++; UwActivity.Note(bout.Map);
            Underworld.AddWhispers(HSettings.Get("underworld.whispers.pit"), "fight pit bout");
            HLog.Info("Underworld", $"Bout: {a.LabelShortCap} ({bout.KindA}) vs {b.LabelShortCap} ({bout.KindB}), scored={bout.Scored}, non-lethal={bout.NonLethal}, manual={manual}");
            Messages.Message(UwPay.T("Pit.BoutAnnounced", a.LabelShortCap, b.LabelShortCap), pit.parent, MessageTypeDefOf.NeutralEvent, false);
            pit.Invalidate();
            return bout;
        }

        /// <summary>Ends every bout without a result (load, module removed, tests). Also schedules the sweep of jobs, melee threats and beasts that outlived their bout.</summary>
        public static void AbortAll(string why)
        {
            foreach (var b in active.ToList()) b.Abort(why, null);
            sweepPending = true;
            // right now (a load calls this from LoadedGame, before the first tick): melee threats between pawns whose pit jobs came from the save.
            // The jobs themselves fail on their first tick, but a threat left in place makes JobGiver_ReactToCloseMeleeThreat swing back for real.
            Guard.Run(ModuleId.Underworld, "FightBout.ClearThreats", ClearLoadedThreats);
        }

        // ---- betting -----------------------------------------------------------------------------------
        public bool HasBet(Pawn g) => Bettors.Contains(g);

        public bool TryBet(Pawn guest, int side, int stakeSilver)
        {
            if (!BettingOpen || guest == null || side < 0 || side > 1 || stakeSilver <= 0 || HasBet(guest) || Underworld.IsMinor(guest) || guest == A || guest == B) return false;
            if (!UwPay.Charge(guest, Money.FromSilver(stakeSilver), PitAccount, "underworld.bet", "Fight pit: bet")) return false;
            Bettors.Add(guest);
            Bets.Add(new UnderworldMath.Bet { Bettor = Bettors.Count - 1, Side = side, StakeMicro = Money.FromSilver(stakeSilver).Micro });
            Underworld.Comp.Bets++;
            return true;
        }

        public bool AutoBet(Pawn guest)
        {
            if (!BettingOpen || HasBet(guest)) return false;
            var budget = UwPay.Budget(guest);
            int max = (int)Math.Min(HSettings.Get("underworld.pit.betMax"), budget.Silver / 3.0);
            int min = (int)HSettings.Get("underworld.pit.betMin");
            if (max < min) return false;
            var rng = JRand.Stream("underworld.bet");
            int stake = rng.Range(min, max + 1);
            float share = UnderworldMath.FavouriteShare(PitFighters.Power(A), PitFighters.Power(B));
            bool favouriteIsA = share >= 0.5f, backFavourite = rng.Chance(0.62);
            int side = favouriteIsA == backFavourite ? 0 : 1;
            return TryBet(guest, side, stake);
        }

        public Money Pool { get { long m = 0; foreach (var b in Bets) m += b.StakeMicro; return new Money(m); } }

        // ---- ticking ------------------------------------------------------------------------------------------
        public static void TickAll(int now)
        {
            if (sweepPending) { sweepPending = false; Guard.Run(ModuleId.Underworld, "FightBout.Sweep", Sweep); }
            if (now - lastCare >= 250 || now < lastCare) { lastCare = now; Guard.Run(ModuleId.Underworld, "FightBout.Care", Care); }
            if (active.Count == 0) return;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                if (i >= active.Count) continue;
                var b = active[i];
                try { b.Tick(now); } catch (Exception e) { Guard.Fail(ModuleId.Underworld, "FightBout.Tick", e); b.Abort("error", null); }
            }
        }

        /// <summary>After a load (bouts are not saved): ends every job of ours that has no bout and clears the melee threats between pawns that held them.</summary>
        static void Sweep()
        {
            if (Find.Maps == null) return;
            int n = 0;
            foreach (var map in Find.Maps)
                foreach (var p in map.mapPawns.AllPawnsSpawned.ToList())
                {
                    if (p.jobs?.curJob == null || !PitSetup.IsPitJob(p.CurJobDef) || OfPawn(p) != null) continue;
                    if (p.CurJob.targetA.Thing is Pawn foe && foe.mindState?.meleeThreat == p) foe.mindState.meleeThreat = null;
                    if (p.mindState != null) p.mindState.meleeThreat = null;
                    p.jobs.EndCurrentJob(JobCondition.InterruptForced, true);
                    n++;
                }
            PitOpponents.ResetBeasts();
            foreach (var pit in CompFightPit.All.ToList()) { pit.NoteBeasts(); n += pit.DismissIdleBeasts(); }
            if (n > 0) HLog.Info("Underworld", $"Pit sweep after load: {n} jobs/beasts without a bout ended");
        }

        static void ClearLoadedThreats()
        {
            if (Current.Game == null || Find.Maps == null) return;
            foreach (var map in Find.Maps)
                foreach (var p in map.mapPawns.AllPawnsSpawned)
                {
                    var job = p.jobs?.curJob;
                    if (job == null || !PitSetup.IsPitJob(job.def) || OfPawn(p) != null) continue;
                    if (p.mindState != null) p.mindState.meleeThreat = null;
                    if (job.targetA.Thing is Pawn foe && foe.mindState?.meleeThreat == p) foe.mindState.meleeThreat = null;
                    foreach (var q in map.mapPawns.AllPawnsSpawned) if (q.mindState?.meleeThreat == p) q.mindState.meleeThreat = null;
                }
        }

        /// <summary>Caretaker every 250 ticks: beasts whose bout is over go home.</summary>
        static void Care()
        {
            if (!PitOpponents.AnyBeast) return;
            foreach (var pit in CompFightPit.All.ToList()) pit.DismissIdleBeasts();
        }

        bool InArena(Pawn p) { var a = Pit?.ArenaInfo(); return a != null && p.Spawned && p.Map == Map && a.Cells.Contains(p.Position); }

        /// <summary>Why a fighter cannot go on (translated) or null.</summary>
        string Broken(Pawn p, Verse.AI.Group.LordToil toil)
        {
            if (p == null || p.Destroyed || p.Dead || !p.Spawned || p.Map != Map) return UwPay.T("Pit.Abort.Gone", p?.LabelShortCap ?? "?");
            if (p.InMentalState) return UwPay.T("Pit.Abort.Mental", p.LabelShortCap);
            if (p.Drafted) return UwPay.T("Pit.Abort.Drafted", p.LabelShortCap);
            if (Scored && p.Downed) return UwPay.T("Pit.Abort.Hurt", p.LabelShortCap);
            // a visitor group that arrives moves from its travel toil to its stay toil: fine. Any other change (leaving, taking the wounded home,
            // turning hostile) or a lost lord calls the bout off.
            if (toil != null && p.GetLord()?.CurLordToil != toil && !PitFighters.StayToil(p)) return UwPay.T("Pit.Abort.Left", p.LabelShortCap);
            if (KindOf(p) == PitKind.Prisoner && !p.IsPrisonerOfColony) return UwPay.T("Pit.Abort.Gone", p.LabelShortCap);
            return null;
        }

        void Tick(int now)
        {
            if (phase == Phase.Done) { active.Remove(this); return; }
            if (Pit == null || Pit.parent == null || !Pit.parent.Spawned) { Abort("pit gone", null); return; }
            if (!Pit.enabled || !Underworld.Allows(UwFeature.PitToIncapacitation)) { Abort("pit closed", UwPay.T("Pit.Abort.Closed")); return; }
            string broken = Broken(A, toilA) ?? Broken(B, toilB);
            if (broken != null) { Abort("fighter cannot go on", broken); return; }
            switch (phase)
            {
                case Phase.Assemble:
                    if (now - StartTick > 3200)
                    {
                        var late = !InArena(A) ? A : B;
                        Abort("fighters did not show up", UwPay.T("Pit.Abort.NoShow", late.LabelShortCap)); return;
                    }
                    GoToArena(A, ref lastOrderA, now); GoToArena(B, ref lastOrderB, now);
                    if (InArena(A) && InArena(B)) { phase = Phase.Betting; bettingEnd = now + Math.Max(0, bettingEnd); }
                    break;
                case Phase.Betting:
                    StayPut(A, ref lastOrderA, now); StayPut(B, ref lastOrderB, now);
                    if (now >= bettingEnd) { phase = Phase.Fight; FightTick = now; }
                    break;
                case Phase.Fight:
                    if (EndCheck(now)) return;
                    Order(A, B, ref lastOrderA, now); Order(B, A, ref lastOrderB, now);
                    break;
            }
        }

        static bool MayBeBroughtIn(Pawn p) => !p.RaceProps.Humanlike || p.IsPrisoner;

        void GoToArena(Pawn p, ref int last, int now)
        {
            if (InArena(p)) { StayPut(p, ref last, now); return; }
            if (p.CurJobDef == PitSetup.JobGoto && now - last < 300) return;
            last = now;
            var a = Pit.ArenaInfo(); if (a == null || a.Cells.Count == 0) return;
            var cell = a.Cells[JRand.Stream("underworld.pit.cell").Range(0, a.Cells.Count)];
            // prisoners are always brought in by the guards (and back to their cell afterwards), never walked across the colony
            if (p.IsPrisoner) { PitOpponents.BringIn(p, cell); return; }
            if (!p.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
            {
                // animals cannot open fence gates and prisoners cannot leave the prison: their handlers bring them in (and back afterwards).
                // People who cannot get in simply do not show up (the bout is called off).
                if (MayBeBroughtIn(p)) PitOpponents.BringIn(p, cell);
                return;
            }
            if (PitSetup.JobGoto == null) return;
            var job = JobMaker.MakeJob(PitSetup.JobGoto, cell); job.locomotionUrgency = LocomotionUrgency.Jog; job.expiryInterval = 900;
            p.jobs.StartJob(job, JobCondition.InterruptForced);
        }

        void StayPut(Pawn p, ref int last, int now)
        {
            if (PitSetup.JobWait == null || p.CurJobDef == PitSetup.JobWait) return;
            if (!InArena(p)) { GoToArena(p, ref last, now); return; }
            var job = JobMaker.MakeJob(PitSetup.JobWait, p.Position); job.expiryInterval = 2000;
            p.jobs.StartJob(job, JobCondition.InterruptForced);
        }

        void Order(Pawn p, Pawn foe, ref int last, int now)
        {
            if (p.Downed || p.Dead || foe.Dead || PitSetup.JobFight == null) return;
            var cur = p.CurJob;
            if (cur != null && (cur.def == PitSetup.JobFight || cur.def == JobDefOf.AttackMelee) && cur.targetA.Thing == foe) return;
            if (now - last < 20) return;
            last = now;
            var job = JobMaker.MakeJob(PitSetup.JobFight, foe); job.expiryInterval = 900; job.checkOverrideOnExpire = false; job.killIncappedTarget = false;
            p.jobs.StartJob(job, JobCondition.InterruptForced);
        }

        bool Beaten(Pawn p)
        {
            if (p.Dead || p.Downed || p.Destroyed) return true;
            if (!NonLethal) return false;   // to the death: only Downed or dead ends it
            return p.health.summaryHealth.SummaryHealthPercent < HSettings.Get("underworld.pit.yieldHealth");
        }

        /// <summary>The damage guard asks: may this fighter take more damage? Not once beaten in a non-lethal bout.</summary>
        public bool IsNearYield(Pawn p) => phase != Phase.Done && p.health.summaryHealth.SummaryHealthPercent < HSettings.Get("underworld.pit.yieldHealth") + 0.25f;

        public bool ShouldAbsorb(Pawn p) => phase != Phase.Done && NonLethal && (p.Downed || Beaten(p));

        bool EndCheck(int now)
        {
            if (Scored)
            {
                float sa = ShareA, sb = ShareB;
                if (sa >= 1f || sb >= 1f) { Finish(sa >= 1f && sb >= 1f ? (sa >= sb ? 1 : 0) : sa >= 1f ? 1 : 0); return true; }
                if (now - FightTick > MaxTicks) { Finish(Math.Abs(sa - sb) < 0.02f ? -1 : sa > sb ? 1 : 0); return true; }
                return false;
            }
            bool a = Beaten(A), b = Beaten(B);
            if (a && b) { Finish(A.Dead && !B.Dead ? 1 : B.Dead && !A.Dead ? 0 : (A.health.summaryHealth.SummaryHealthPercent <= B.health.summaryHealth.SummaryHealthPercent ? 1 : 0)); return true; }
            if (a) { Finish(1); return true; }
            if (b) { Finish(0); return true; }
            if (now - FightTick > MaxTicks) { Finish(-1); return true; }
            return false;
        }

        /// <summary>Ends the fight jobs and the after-bout retaliation (meleeThreat), removes HarmedMe between the two after a real bout.</summary>
        void Release()
        {
            // threats first: ending a job lets the think tree pick the next one at once, and JobGiver_ReactToCloseMeleeThreat would swing back for real
            foreach (var p in new[] { A, B }) if (p?.mindState != null && p.mindState.meleeThreat == Other(p)) p.mindState.meleeThreat = null;
            foreach (var p in new[] { A, B })
            {
                if (p == null || p.Dead || !p.Spawned) continue;
                var d = p.CurJobDef;
                if (PitSetup.IsPitJob(d) || ((d == JobDefOf.AttackMelee) && p.CurJob.targetA.Thing == Other(p))) p.jobs.EndCurrentJob(JobCondition.InterruptForced, true);
                if (!Scored && ThoughtDefOf.HarmedMe != null) p.needs?.mood?.thoughts?.memories?.RemoveMemoriesOfDefWhereOtherPawnIs(ThoughtDefOf.HarmedMe, Other(p));
            }
            foreach (var p in new[] { A, B }) if (p?.mindState != null && p.mindState.meleeThreat == Other(p)) p.mindState.meleeThreat = null;
            foreach (var p in new[] { A, B }) PitOpponents.TakeBack(p);
            foreach (var p in new[] { A, B }) if (p != null && PitOpponents.IsBeast(p)) { PitOpponents.Dismiss(p); Pit?.RemoveBeast(p); }
        }

        public void Finish(int winnerSide)
        {
            if (phase == Phase.Done) return;
            phase = Phase.Done; WinnerSide = winnerSide; Draw = winnerSide < 0;
            // a non-lethal real bout must not end with a loser who bleeds out afterwards: stop the bleeding of both fighters
            if (NonLethal && !Scored)
                foreach (var p in new[] { A, B })
                {
                    if (p == null || p.Dead || p.health?.hediffSet == null) continue;
                    try { foreach (var h in p.health.hediffSet.hediffs.ToList()) if (h is Hediff_Injury inj && inj.Bleeding && inj.TendableNow()) inj.Tended(1f, 1f, 0); }
                    catch (Exception e) { HLog.Warn("Underworld", "tending the fighters failed: " + e.Message); }
                }
            if (Scored && !Draw) Bruise(winnerSide == 0 ? B : A);
            Settle(winnerSide);
            FighterRewards();
            WitnessEffects();
            Release();
            active.Remove(this);
            Settled?.Invoke(this);
        }

        /// <summary>The loser of a scored bout walks away with one or two small vanilla bruises (no lord signal: AddHediff, not damage).</summary>
        void Bruise(Pawn loser)
        {
            var def = PitSetup.Bruise;
            if (def == null || loser == null || loser.Dead || loser.health?.hediffSet == null || PitOpponents.IsBeast(loser)) return;
            try
            {
                var parts = loser.health.hediffSet.GetNotMissingParts().Where(pt => pt.depth == BodyPartDepth.Outside && pt.coverageAbs > 0f && pt.def.hitPoints >= 10 && !pt.def.tags.Contains(BodyPartTagDefOf.ConsciousnessSource)).ToList();
                if (parts.Count == 0) return;
                var rng = JRand.Stream("underworld.pit.bruise");
                int n = 1 + rng.Range(0, 2);
                for (int i = 0; i < n; i++)
                {
                    var part = parts[rng.Range(0, parts.Count)];
                    var h = (Hediff_Injury)HediffMaker.MakeHediff(def, loser, part);
                    h.Severity = (float)rng.Range(1.0, 2.5);
                    loser.health.AddHediff(h, part);
                }
            }
            catch (Exception e) { HLog.Warn("Underworld", "bruises for " + loser.LabelShort + " failed: " + e.Message); }
        }

        /// <summary>Pays out the totalisator. Pool = payouts + house exactly; a winning guest fighter gets his purse out of the house take; the rest goes to the till.</summary>
        void Settle(int winnerSide)
        {
            double cut = HSettings.Get("underworld.pit.houseCut");
            Result = UnderworldMath.Settle(Bets, winnerSide, cut);
            var l = Ledger.Current;
            for (int i = 0; i < Bets.Count; i++)
            {
                var bet = Bets[i]; if (bet.PayoutMicro <= 0) continue;
                var g = Bettors[bet.Bettor];
                if (g == null || g.Destroyed || g.Dead || g.MapHeld == null) { HLog.Info("Underworld", "A winning bettor left; the house keeps the payout"); continue; }
                UwPay.PayOut(g, new Money(bet.PayoutMicro), PitAccount, "Fight pit: payout");
            }
            PursePaid = Money.Zero;
            var winner = winnerSide == 0 ? A : winnerSide == 1 ? B : null;
            if (winner != null && KindOf(winner) == PitKind.Guest && !winner.Destroyed)
            {
                var purse = HSettings.GetSilver("underworld.pit.guestPurse");
                var avail = UwPay.Balance(PitAccount);
                if (avail < purse) purse = avail;
                if (purse.WholeSilver > 0) PursePaid = UwPay.PayOut(winner, Money.FromSilver(purse.WholeSilver), PitAccount, "Fight pit: purse");
            }
            var rest = UwPay.Balance(PitAccount);
            if (rest.Micro > 0) { l.Post(PitAccount, Underworld.Till(Map), rest, "Fight pit: house take"); Underworld.Comp.HouseTakeMicro += rest.Micro; }
            Pit?.Record(this, Result.PoolMicro, Math.Max(0L, rest.Micro));
            var w = winnerSide == 0 ? A : B; var lo = winnerSide == 0 ? B : A;
            string text = Draw ? UwPay.T("Pit.Result.Draw", A?.LabelShortCap ?? "-", B?.LabelShortCap ?? "-")
                : UwPay.T("Pit.Result.Won", w?.LabelShortCap ?? "-", lo?.LabelShortCap ?? "-", PitFighters.Silver(new Money(Result.PoolMicro)), PitFighters.Silver(new Money(Math.Max(0L, rest.Micro))));
            if (PursePaid.Micro > 0) text += " " + UwPay.T("Pit.PursePaid", w?.LabelShortCap ?? "-", PitFighters.Silver(PursePaid));
            LastMessage = text;
            Messages.Message(text, Pit?.parent, MessageTypeDefOf.NeutralEvent, false);
            HLog.Info("Underworld", $"Bout settled: winner side {winnerSide}, pool {Result.PoolMicro / 100000.0:0.##}, house {Result.HouseMicro / 100000.0:0.##}, purse {PursePaid.ToStringShort()}, bets {Bets.Count}, points {ScoreA:0}/{LimitA:0} vs {ScoreB:0}/{LimitB:0}");
        }

        void FighterRewards()
        {
            foreach (var p in new[] { A, B })
            {
                if (p == null || p.Dead || KindOf(p) != PitKind.Guest) continue;
                bool won = !Draw && ((WinnerSide == 0 && p == A) || (WinnerSide == 1 && p == B));
                GuestApi.AddSatisfaction(p, won ? 0.12f : 0.05f, won ? "won a bout in the fight pit" : "fought in the fight pit");
            }
        }

        void WitnessEffects()
        {
            if (Map == null) return;
            var center = Pit.ArenaInfo()?.Center ?? Pit.parent.Position;
            foreach (var p in Map.mapPawns.AllPawnsSpawned)
            {
                if (!p.RaceProps.Humanlike || p.Dead || p == A || p == B || Underworld.IsMinor(p) || p.Position.DistanceTo(center) > 16f) continue;
                if (p.Faction != Faction.OfPlayer && !GuestApi.IsGuest(p)) continue;
                var tr = p.story?.traits; if (tr == null) continue;
                if (tr.HasTrait(TraitDefOf.Kind) && UwDefs.ThoughtSawFightKind != null) p.needs?.mood?.thoughts?.memories?.TryGainMemory(UwDefs.ThoughtSawFightKind);
                if (tr.HasTrait(TraitDefOf.Bloodlust) && UwDefs.ThoughtSawFightBloodlust != null) p.needs?.mood?.thoughts?.memories?.TryGainMemory(UwDefs.ThoughtSawFightBloodlust);
                if (GuestApi.IsGuest(p)) GuestApi.AddSatisfaction(p, tr.HasTrait(TraitDefOf.Bloodlust) ? 0.12f : tr.HasTrait(TraitDefOf.Kind) ? -0.15f : 0.03f, "fight pit");
            }
        }

        /// <summary>Ends the bout without a result: all stakes are returned in full. <paramref name="playerText"/> (translated) is shown once per pit per hour; null = silent.</summary>
        public void Abort(string why, string playerText = null)
        {
            if (phase == Phase.Done) { active.Remove(this); return; }
            HLog.Info("Underworld", "Bout aborted: " + why + (playerText != null ? " (" + playerText + ")" : ""));
            phase = Phase.Done; Draw = true; WinnerSide = -1;
            Result = UnderworldMath.Settle(Bets, -1, 0);
            for (int i = 0; i < Bets.Count; i++) { var g = Bettors[Bets[i].Bettor]; if (g != null && !g.Destroyed && Bets[i].PayoutMicro > 0) UwPay.PayOut(g, new Money(Bets[i].PayoutMicro), PitAccount, "Fight pit: refund"); }
            var rest = UwPay.Balance(PitAccount); if (rest.Micro > 0 && Map != null) Ledger.Current.Post(PitAccount, Underworld.Till(Map), rest, "Fight pit: leftover");
            try { Release(); } catch (Exception e) { HLog.Warn("Underworld", "releasing the fighters failed: " + e.Message); }
            active.Remove(this);
            if (playerText != null && Pit?.parent != null && Pit.parent.Spawned && Pit.MayTellAbort(Find.TickManager.TicksGame))
            {
                LastMessage = UwPay.T("Pit.CalledOff", playerText);
                Messages.Message(LastMessage, Pit.parent, MessageTypeDefOf.NeutralEvent, false);
            }
        }
    }

    // ---- spectators ------------------------------------------------------------------------------------
    public class JobDriver_WatchBout : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;
        protected override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            yield return new Toil { initAction = () => { var b = FightBout.OfMap(pawn.Map); if (b != null && GuestApi.IsGuest(pawn)) b.AutoBet(pawn); }, defaultCompleteMode = ToilCompleteMode.Instant };
            var watch = new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Delay, defaultDuration = 4500,
                tickAction = () =>
                {
                    var b = FightBout.OfMap(pawn.Map);
                    if (b == null) { ReadyForNextToil(); return; }
                    var c = b.Pit.ArenaInfo()?.Center ?? b.Pit.parent.Position;
                    pawn.rotationTracker.FaceCell(c);
                }
            };
            yield return watch;
        }
    }

    public static partial class Activities
    {
        public sealed class PitActivity : IGuestActivity
        {
            public string Id => "underworld.fightpit";
            public ModuleId Module => ModuleId.Underworld;
            public float Desire(Pawn guest, GuestInfo info)
            {
                if (!Underworld.Unlocked || guest.Map == null || Underworld.IsMinor(guest) || UwDefs.JobWatchBout == null) return 0f;
                var b = FightBout.OfMap(guest.Map); if (b == null || !b.BettingOpen || b.HasBet(guest)) return 0f;
                if (info == null || FightBout.OfPawn(guest) != null || UwPay.Budget(guest) < Money.FromSilver(HSettings.Get("underworld.pit.betMin"))) return 0f;
                if (guest.story?.traits?.HasTrait(TraitDefOf.Kind) == true) return 0.15f;
                return guest.story?.traits?.HasTrait(TraitDefOf.Bloodlust) == true ? 3f : 1.4f;
            }
            public Job TryGiveJob(Pawn guest, GuestInfo info)
            {
                var b = FightBout.OfMap(guest.Map); if (b == null) return null;
                if (!Arena.TrySpectatorCell(guest.Map, b.Pit.ArenaInfo(), guest, out var cell)) return null;
                return JobMaker.MakeJob(UwDefs.JobWatchBout, cell);
            }
        }
    }
}
