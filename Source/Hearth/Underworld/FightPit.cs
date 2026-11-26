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
        /// <summary>Closed: the fill stopped at fences, walls and doors. ReachedEdge: it reached the map edge. TooBig: it passed the cap before either (an open area or a very big pen).</summary>
        public sealed class Info { public List<IntVec3> Cells = new List<IntVec3>(); public bool Closed, ReachedEdge, TooBig; public int Cap; public IntVec3 Center; public int Builtick; }

        public static Info Flood(Map map, IntVec3 start, int cap)
        {
            var info = new Info { Builtick = Find.TickManager.TicksGame, Cap = cap };
            var seen = new HashSet<IntVec3> { start }; var q = new Queue<IntVec3>(); q.Enqueue(start);
            bool leaked = false;
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                info.Cells.Add(c);
                if (info.Cells.Count > cap) { leaked = true; info.TooBig = true; break; }
                for (int i = 0; i < 4; i++)
                {
                    var n = c + GenAdj.CardinalDirections[i];
                    if (!n.InBounds(map)) { leaked = true; info.ReachedEdge = true; continue; }
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
        /// <summary>Pawns this pit brought into the arena (animals behind a pen fence, prisoners) and the cell each came from. Saved, so a game loaded in
        /// the middle of a bout (bouts are not saved) still takes them back: a prisoner left in the open arena would otherwise walk off.</summary>
        List<Pawn> broughtIn = new List<Pawn>(); List<IntVec3> broughtFrom = new List<IntVec3>();
        // last bout (kept for the inspect string and the Underworld tab)
        public string lastA, lastB; public int lastWinner = -2, lastTick = -1, settled; public long lastPoolMicro, lastHouseMicro;
        // transient
        Arena.Info cache;
        List<PitCandidate> cands; int candTick = int.MinValue;
        int lastAbortMsgTick = -999999;
        /// <summary>Why the last automatic attempt found no bout (translated, e.g. "no fair pair of fighters"), shown in the status line until the next
        /// attempt succeeds or the fighters change. Not saved.</summary>
        internal string lastAutoFail;
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
        public void Invalidate() { cache = null; cands = null; hint = null; guestsDirty = true; lastAutoFail = null; }

        public const int MinArenaCells = 9;
        public bool ArenaValid => ArenaInfo() is Arena.Info a && a.Closed && a.Cells.Count >= MinArenaCells;

        /// <summary>What is wrong with the arena (translated), or null when it is fine: open at the map edge, open or bigger than the cap, too small.</summary>
        public string ArenaProblem()
        {
            var a = ArenaInfo();
            if (a == null) return UwPay.T("Pit.ArenaOpen");
            if (a.Closed) return a.Cells.Count >= MinArenaCells ? null : UwPay.T("Pit.ArenaSmall", a.Cells.Count, MinArenaCells);
            if (a.TooBig) return UwPay.T("Pit.ArenaBig", a.Cap);
            return UwPay.T("Pit.ArenaOpen");
        }

        // ---- ticked colonists, beasts ----------------------------------------------------------------------
        public bool IsTicked(Pawn p) => p != null && ticked.Contains(p);
        public void SetTicked(Pawn p, bool on)
        {
            if (p == null) return;
            if (on) { if (!ticked.Contains(p)) ticked.Add(p); } else ticked.Remove(p);
            cands = null; hint = null; lastAutoFail = null;
        }
        public IReadOnlyList<Pawn> Ticked => ticked;
        public bool HasBeast { get { for (int i = 0; i < beastList.Count; i++) if (beastList[i] != null && !beastList[i].Destroyed) return true; return false; } }
        internal void AddBeast(Pawn p) { if (p != null && !beastList.Contains(p)) beastList.Add(p); cands = null; hint = null; }
        internal void RemoveBeast(Pawn p) { beastList.Remove(p); if (beastList.Count == 0) beastFeeMicro = -1; cands = null; hint = null; }
        /// <summary>What the hire of the pit's beast cost (0 = the free first one), saved with the pit so a bout lost with a load refunds what was paid.
        /// A pit has at most one beast (a second hire waits until it is gone). -1 = unknown (no beast, or a save from before 1.2.0).</summary>
        long beastFeeMicro = -1;
        internal void NoteBeastFee(Money fee) => beastFeeMicro = Math.Max(0L, fee.Micro);
        internal void NoteBeasts() { foreach (var b in beastList) PitOpponents.NoteBeast(b); }

        internal void NoteBroughtIn(Pawn p, IntVec3 from)
        {
            if (p == null || broughtIn.Contains(p)) return;
            broughtIn.Add(p); broughtFrom.Add(from);
        }
        internal void ForgetBroughtIn(Pawn p)
        {
            int i = broughtIn.IndexOf(p);
            if (i < 0) return;
            broughtIn.RemoveAt(i); if (i < broughtFrom.Count) broughtFrom.RemoveAt(i);
        }
        public bool WasBroughtIn(Pawn p) => p != null && broughtIn.Contains(p);

        /// <summary>Takes back every pawn this pit brought in that has no bout any more (after a load or a removed pit). Returns how many.</summary>
        internal int TakeBackIdle()
        {
            int n = 0;
            for (int i = broughtIn.Count - 1; i >= 0; i--)
            {
                var p = broughtIn[i];
                if (p != null && FightBout.OfPawn(p) != null) continue;
                var from = i < broughtFrom.Count ? broughtFrom[i] : IntVec3.Invalid;
                broughtIn.RemoveAt(i); if (i < broughtFrom.Count) broughtFrom.RemoveAt(i);
                if (p == null) continue;
                PitOpponents.TakeBackTo(p, from);
                n++;
            }
            return n;
        }

        /// <summary>Beasts with no bout of their own left (after a load, an abort, a removed pit) are sent back. Called by the bout caretaker.
        /// <paramref name="refund"/>: the bout of such a beast was lost with a load (bouts are not saved), so its fee goes back to the till.</summary>
        internal int DismissIdleBeasts(bool refund = false)
        {
            int n = 0;
            for (int i = beastList.Count - 1; i >= 0; i--)
            {
                var b = beastList[i];
                if (b != null && FightBout.OfPawn(b) != null) continue;
                beastList.RemoveAt(i);
                if (b == null) continue;
                var map = b.MapHeld ?? (parent.Spawned ? parent.Map : null);
                PitOpponents.Dismiss(b); n++;
                if (!refund) continue;
                if (beastFeeMicro == 0)
                {
                    // the free first hire: it is still to come
                    if (Underworld.Comp != null && Underworld.Comp.OpponentsHired > 0) Underworld.Comp.OpponentsHired--;
                    continue;
                }
                // the saved fee; a save from before 1.2.0 has none: the set fee, unless the only hire of the game was the free one
                var paid = beastFeeMicro > 0 ? new Money(beastFeeMicro) : (Underworld.Comp?.OpponentsHired ?? 0) > 1 ? HSettings.GetSilver("underworld.pit.opponentFee") : Money.Zero;
                if (map != null && paid.Micro > 0) Ledger.Current.Post("world", Underworld.Till(map), paid, "Fight pit: opponent fee refunded (bout lost with a load)");
            }
            if (beastList.Count == 0) beastFeeMicro = -1;
            if (n > 0) cands = null; hint = null;
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

        /// <summary>Why a bout the player starts himself cannot start for lack of fighters (translated), or null: nobody, or only one fighter.</summary>
        public string ManualShortage(List<PitCandidate> manual = null)
        {
            int n = (manual ?? ManualReady()).Count;
            if (n >= 2) return null;
            return n == 0 ? UwPay.T("Pit.Status.NoFighter") : UwPay.T("Pit.Why.OnlyOne", Hint());
        }

        string hint; int hintFrame = -999;
        /// <summary>What would bring a second fighter, said only where it helps: call an opponent (when the colony can), tick a second colonist (when
        /// there is one), allow animals (when the colony has some and they are not allowed), allow guests (when they are not), else wait for a guest.
        /// Cached for 30 frames: the status line and the Start button ask every frame.</summary>
        public string Hint()
        {
            int f = UnityEngine.Time.frameCount;
            if (hint != null && f - hintFrame < 30 && f >= hintFrame) return hint;
            hintFrame = f;
            hint = Guard.Run(ModuleId.Underworld, "Pit.Hint", () =>
            {
                var parts = new List<string>();
                var lone = LoneFighter();
                if (PitOpponents.CannotHire(this, lone) == null) parts.Add(UwPay.T("Pit.Hint.Call"));
                int unticked = 0;
                foreach (var c in Candidates()) if (c.Kind == PitKind.Colonist && c.NotTicked && c.Pawn != lone) unticked++;
                if (unticked > 0) parts.Add(UwPay.T("Pit.Hint.Tick"));
                if (!allowAnimals && Map != null && Map.mapPawns.SpawnedColonyAnimals.Count > 0) parts.Add(UwPay.T("Pit.Hint.Animals"));
                if (!allowGuests) parts.Add(UwPay.T("Pit.Hint.Guests"));
                if (parts.Count == 0) return UwPay.T("Pit.Hint.Wait");
                if (parts.Count == 1) return parts[0];
                return UwPay.T("Pit.Hint.Or", string.Join(", ", parts.GetRange(0, parts.Count - 1)), parts[parts.Count - 1]);
            }, UwPay.T("Pit.Hint.Wait"));
            return hint;
        }

        /// <summary>Why this pit holds no bout at all right now (translated), or null when it is open: module/Cozy, peaceful switch, closed, open arena.</summary>
        public string ClosedReason() => GateReason() ?? ArenaProblem();

        /// <summary>The part of <see cref="ClosedReason"/> that is not about the arena: module/Cozy, peaceful switch, pit closed.</summary>
        string GateReason()
        {
            if (!Underworld.Visible) return UwPay.T("Pit.Status.Off");
            if (Underworld.Peaceful) return UwPay.T("Pit.Status.Peaceful");
            if (!Underworld.Allows(UwFeature.PitToIncapacitation)) return UwPay.T("Pit.Status.Off");
            if (!enabled) return UwPay.T("Pit.Status.Closed");
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
            if (b != null) return UwPay.T("Pit.Status.Running", b.NameA, b.NameB);
            string gate = GateReason();
            if (gate != null) return gate;
            string arena = ArenaProblem();
            // an open arena: "waiting for a closed arena"; a closed one that is too small says so itself (the fence has no gap to look for)
            if (arena != null) return ArenaInfo() is Arena.Info ai && ai.Closed ? arena : UwPay.T("Pit.Status.NeedArena");
            int ready = Ready().Count;
            if (ready == 0) return UwPay.T("Pit.Status.NoFighter");
            if (ready < 2) return UwPay.T("Pit.Status.WaitFighter", Hint());
            if (!AdultGuestOn(Map)) return UwPay.T("Pit.Status.WaitGuests");
            if (lastAutoFail != null) return UwPay.T("Pit.Status.AutoFailed", lastAutoFail);
            int now = Find.TickManager.TicksGame;
            float h = nextBoutTick < 0 ? HSettings.Get("underworld.pit.intervalHours") * 0.5f : Math.Max(0, nextBoutTick - now) / 2500f;
            return UwPay.T("Pit.Status.Next", h.ToString("0.0"));
        }

        /// <summary>For the Underworld tab: Working while a bout runs or automatic bouts are planned, else Waiting (<see cref="StatusLine"/> says why).
        /// Reads the same caches as the status line.</summary>
        public FacilityState PageState()
        {
            if (FightBout.OfPit(this) != null) return FacilityState.Working;
            if (GateReason() != null || ArenaProblem() != null || Ready().Count < 2 || !AdultGuestOn(Map) || lastAutoFail != null) return FacilityState.Waiting;
            return FacilityState.Working;
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
            if (lastPoolMicro <= 0 && lastHouseMicro <= 0) return UwPay.T("Pit.Last.NoBets", w, l);   // nobody bet (a hired beast): no pool to talk about
            return UwPay.T("Pit.Last", w, l, PitFighters.Silver(new Money(lastPoolMicro)), PitFighters.Silver(new Money(lastHouseMicro)));
        }

        public override string CompInspectStringExtra()
        {
            return Guard.Run(ModuleId.Underworld, "Pit.Inspect", () =>
            {
                var lines = new List<string>();
                var a = ArenaInfo();
                if (a != null) lines.Add(ArenaProblem() ?? UwPay.T("Pit.ArenaOk", a.Cells.Count));
                // (no "Fighters ready: none." line: the status line says it and how to change it; the pane must not be cut at 1280x720)
                if (Underworld.Visible && !Underworld.Peaceful && Ready().Count > 0) lines.Add(ReadyLine());
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
            else if (ManualShortage() is string few) start.Disable(few);
            yield return start;
            var lone = LoneFighter();
            var hire = new Command_Action
            {
                defaultLabel = PitOpponents.CallLabel,
                defaultDesc = PitOpponents.CallDesc + (lone != null ? "\n\n" + UwPay.T("Pit.CallOpponent.For", lone.LabelShortCap) : ""),
                icon = PitSetup.IconOpponent,
                action = () => CallOpponentFor(lone)
            };
            string hw = PitOpponents.CannotHire(this, lone);
            if (hw != null) hire.Disable(hw);
            yield return hire;
        }

        /// <summary>The "Call an opponent" button of the marker and of the Fighters window: ticks a lone colonist first (as the right-click does), then hires.</summary>
        public void CallOpponentFor(Pawn lone)
        {
            bool tick = lone != null && lone.RaceProps.Humanlike && (lone.Faction == Faction.OfPlayer || lone.IsSlaveOfColony) && !lone.IsPrisoner && !IsTicked(lone);
            if (tick) SetTicked(lone, true);
            if (PitOpponents.HireBeast(this, lone, out string why) == null)
            {
                if (tick) SetTicked(lone, false);   // no bout: he stays out of automatic bouts, as before the click
                Messages.Message(UwPay.T("Pit.CannotStart", why), parent, MessageTypeDefOf.RejectInput, false);
            }
        }

        Command_Toggle Toggle(string key, Texture2D icon, Func<bool> get, Action<bool> set) => new Command_Toggle
        {
            defaultLabel = UwPay.T(key), defaultDesc = UwPay.T(key + ".Desc"), icon = icon,
            isActive = get, toggleAction = () => { set(!get()); cands = null; hint = null; lastAutoFail = null; }
        };

        /// <summary>The fighter a hired opponent is matched to: side A of the window, else a fighter a manual start may use: a ticked colonist or slave
        /// first; then, on a map with only one colonist, that colonist even when he is not ticked yet ("Call an opponent" ticks him, as the right-click does);
        /// then an animal, prisoner or guest. With two or more colonists an unticked one is never picked. The gizmo's tooltip names him.</summary>
        public Pawn LoneFighter()
        {
            if (pickA != null && !pickA.Destroyed && pickA.Spawned && pickA.Map == Map && FightBout.OfPawn(pickA) == null) return pickA;
            var m = ManualReady();
            foreach (var c in m) if (c.Kind == PitKind.Colonist && IsTicked(c.Pawn)) return c.Pawn;
            foreach (var c in m) if (c.Kind == PitKind.Colonist) return c.Pawn;
            // a colony of one: its only colonist, ticked on hire (as the right-click does) when "not ticked" is the only thing in his way
            PitCandidate only = null; int n = 0;
            foreach (var c in Candidates()) if (c.Kind == PitKind.Colonist) { n++; only = c; }
            if (n == 1 && only.Pawn != null && !only.Pawn.Destroyed && only.NotTicked) return only.Pawn;
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
            if (!me.ManualOk && !me.NotTicked) { yield return new FloatMenuOption(label + ": " + me.Reason, null); yield break; }
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
            var hire = new FloatMenuOption(PitOpponents.CallLabel + (hw != null ? ": " + hw : ""), hw != null ? (Action)null : () =>
            {
                bool was = IsTicked(selPawn);
                SetTicked(selPawn, true);
                if (PitOpponents.HireBeast(this, selPawn, out string why) == null)
                {
                    if (!was) SetTicked(selPawn, false);
                    Messages.Message(UwPay.T("Pit.CannotStart", why), parent, MessageTypeDefOf.RejectInput, false);
                }
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
            if (FightBout.TryStartFor(this, false, out string why)) { nextBoutTick = now + (int)interval; lastAutoFail = null; }
            else { nextBoutTick = now + 2500; lastAutoFail = why; }
        }

        internal bool MayTellAbort(int now)
        {
            if (now - lastAbortMsgTick < 2500 && now >= lastAbortMsgTick) return false;
            lastAbortMsgTick = now; return true;
        }

        internal void Record(FightBout b, long poolMicro, long houseMicro)
        {
            lastA = b.NameA; lastB = b.NameB;
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
            Scribe_Values.Look(ref beastFeeMicro, "beastFee", -1L);
            if (Scribe.mode == LoadSaveMode.Saving)
                for (int i = broughtIn.Count - 1; i >= 0; i--) if (broughtIn[i] == null || broughtIn[i].Destroyed || i >= broughtFrom.Count) { broughtIn.RemoveAt(i); if (i < broughtFrom.Count) broughtFrom.RemoveAt(i); }
            Scribe_Collections.Look(ref broughtIn, "broughtIn", LookMode.Reference);
            Scribe_Collections.Look(ref broughtFrom, "broughtFrom", LookMode.Value);
            Scribe_Values.Look(ref lastA, "lastA"); Scribe_Values.Look(ref lastB, "lastB");
            Scribe_Values.Look(ref lastWinner, "lastWinner", -2); Scribe_Values.Look(ref lastTick, "lastTick", -1);
            Scribe_Values.Look(ref lastPoolMicro, "lastPool"); Scribe_Values.Look(ref lastHouseMicro, "lastHouse");
            Scribe_Values.Look(ref settled, "settled");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (ticked == null) ticked = new List<Pawn>(); ticked.RemoveAll(p => p == null);
                if (beastList == null) beastList = new List<Pawn>(); beastList.RemoveAll(p => p == null);
                if (broughtIn == null) broughtIn = new List<Pawn>();
                if (broughtFrom == null) broughtFrom = new List<IntVec3>();
                // a pawn that is gone stays as null next to its cell until the load sweep (TakeBackIdle) drops both together
                while (broughtFrom.Count < broughtIn.Count) broughtFrom.Add(IntVec3.Invalid);
                if (broughtFrom.Count > broughtIn.Count) broughtFrom.RemoveRange(broughtIn.Count, broughtFrom.Count - broughtIn.Count);
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
            TakeBackIdle();
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
#if HH_TESTS
        /// <summary>Tests: drops every bout without releasing anything, as a load does (bouts are not saved).</summary>
        internal static void ForgetAllForTests() { active.Clear(); PitOpponents.ForgetReturningForTests(); }
#endif
        // fighters of bouts that ended lately: kept out of vanilla quarrels for a while (Patch_PitNoQuarrel). Not saved: a load ends the cool-down.
        static readonly Dictionary<Pawn, int> recent = new Dictionary<Pawn, int>();
        public const int QuarrelCooldownTicks = 2500;
        static int recentEnd = -1;
        /// <summary>A fighter of a bout that ended less than <see cref="QuarrelCooldownTicks"/> ago is still kept from quarrels. Clears the list once the last one is over.</summary>
        public static bool AnyRecent
        {
            get
            {
                if (recent.Count == 0) return false;
                int now = Find.TickManager.TicksGame;
                if (now < recentEnd && now >= recentEnd - QuarrelCooldownTicks) return true;
                recent.Clear(); return false;
            }
        }
        internal static void ForgetRecent() { recent.Clear(); recentEnd = -1; }
        /// <summary>True while <paramref name="p"/> fights in a bout or fought in one less than an hour ago: a vanilla social fight with him must not start.</summary>
        public static bool KeepFromQuarrel(Pawn p)
        {
            if (p == null) return false;
            if (OfPawn(p) != null) return true;
            if (recent.Count == 0 || !recent.TryGetValue(p, out int t)) return false;
            int now = Find.TickManager.TicksGame;
            if (now >= t && now - t < QuarrelCooldownTicks) return true;
            recent.Remove(p);
            return false;
        }

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
        /// <summary>Fee paid for a hired opponent of this bout (till → world); booked back when the bout is called off without a result.</summary>
        public Money FeePaid;
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
        /// <summary>The fighters as the player's texts name them: a hired beast as "the pit gazelle", everyone else by his short name.</summary>
        public string NameA => PitFighters.Name(A, KindA);
        public string NameB => PitFighters.Name(B, KindB);
        string NameOf(Pawn p) => p == null ? "-" : PitFighters.Name(p, KindOf(p));
        /// <summary>The first opponent of the game was free for this bout: called off without a result, the free hire is given back.</summary>
        public bool FreeHire;

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
            var c = manual ? pit.ManualReady(true) : pit.Ready(true);
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
                if (a == null || b == null) { why = pit.ManualShortage(c) ?? UwPay.T("Pit.NoFighters"); return false; }
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
            var beastSide = PitOpponents.IsBeast(b) ? b : PitOpponents.IsBeast(a) ? a : null;
            // only a scored bout may promise "a few bruises at most"; a beast against an animal or a prisoner is a real fight
            string announce = beastSide == null ? UwPay.T("Pit.BoutAnnounced", bout.NameA, bout.NameB)
                : bout.Scored ? UwPay.T("Pit.BoutAnnounced.Beast", beastSide == a ? bout.NameB : bout.NameA, beastSide == a ? bout.NameA : bout.NameB)
                : UwPay.T("Pit.BoutAnnounced.Plain", bout.NameA, bout.NameB);
            Messages.Message(announce, pit.parent, MessageTypeDefOf.NeutralEvent, false);
            pit.Invalidate();
            return bout;
        }

        /// <summary>Ends every bout without a result (load, module removed, tests). Also schedules the sweep of jobs, melee threats and beasts that outlived their bout.</summary>
        public static void AbortAll(string why)
        {
            foreach (var b in active.ToList()) b.Abort(why, null);
            sweepPending = true;
            // right now (a load calls this from LoadedGame, before the first tick, also with the module switched off: then no module tick ever runs the
            // sweep): melee threats and vanilla attacks between the two fighters of a lost bout, pawns brought into the arena, hired beasts.
            Guard.Run(ModuleId.Underworld, "FightBout.LoadSweep", LoadSweep);
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

        /// <summary>First module tick after a load (bouts are not saved): ends every job of ours that has no bout and every vanilla attack between the two
        /// fighters of a lost bout, and clears the melee threats between them. <see cref="LoadSweep"/> did the same at load time already (the jobs got a
        /// fail condition there); this is the second line for anything that slipped in between.</summary>
        static void Sweep()
        {
            if (Find.Maps == null) return;
            int n = 0;
            foreach (var map in Find.Maps)
            {
                var arena = ArenaCells(map);
                foreach (var p in map.mapPawns.AllPawnsSpawned.ToList())
                {
                    var job = p.jobs?.curJob;
                    if (job == null || OfPawn(p) != null) continue;
                    if (!PitSetup.IsPitJob(job.def) && !VanillaPairAttack(p, job, arena)) continue;
                    if (job.targetA.Thing is Pawn foe && foe.mindState?.meleeThreat == p) foe.mindState.meleeThreat = null;
                    if (p.mindState != null) p.mindState.meleeThreat = null;
                    p.jobs.EndCurrentJob(JobCondition.InterruptForced, true);
                    n++;
                }
            }
            PitOpponents.ResetBeasts();
            foreach (var pit in CompFightPit.All.ToList()) { pit.NoteBeasts(); n += pit.DismissIdleBeasts(); n += pit.TakeBackIdle(); }
            if (n > 0) HLog.Info("Underworld", $"Pit sweep after load: {n} jobs/beasts/brought-in pawns without a bout handled");
        }

        /// <summary>
        /// At load time (and whenever every bout is aborted): bouts are not saved, so whatever a bout held in the save is undone before the first tick.
        /// <list type="bullet">
        /// <item>Jobs: a pit job fails on its first tick by itself; a vanilla AttackMelee between two non-hostile pawns that were fighting each other in
        /// the pit (the foe holds a pit job or an attack on him, or both stand in an arena) gets a fail condition, so it ends before its first blow.</item>
        /// <item>Melee threats of both, so JobGiver_ReactToCloseMeleeThreat does not swing back for real.</item>
        /// <item>Pawns the pit brought in (saved per pit) go back where they came from; a prisoner of an older save left in an arena goes to his bed.</item>
        /// <item>Hired beasts without a bout are sent away, their fee goes back to the till.</item>
        /// </list>
        /// </summary>
        static void LoadSweep()
        {
            if (Current.Game == null || Find.Maps == null) return;
            int jobs = 0, back = 0, beasts = 0;
            foreach (var map in Find.Maps)
            {
                var arena = ArenaCells(map);
                var pawns = map.mapPawns.AllPawnsSpawned.ToList();
                foreach (var p in pawns)
                {
                    var job = p.jobs?.curJob;
                    if (job == null || OfPawn(p) != null) continue;
                    bool ours = PitSetup.IsPitJob(job.def), vanilla = !ours && VanillaPairAttack(p, job, arena);
                    if (!ours && !vanilla) continue;
                    if (p.mindState != null) p.mindState.meleeThreat = null;
                    if (job.targetA.Thing is Pawn foe && foe.mindState?.meleeThreat == p) foe.mindState.meleeThreat = null;
                    foreach (var q in pawns) if (q.mindState?.meleeThreat == p) q.mindState.meleeThreat = null;
                    if (vanilla && p.jobs.curDriver != null) { var j = job; p.jobs.curDriver.AddEndCondition(() => p.CurJob == j ? JobCondition.Incompletable : JobCondition.Ongoing); jobs++; }
                }
            }
            PitOpponents.ResetBeasts();
            foreach (var pit in CompFightPit.All.ToList()) { pit.NoteBeasts(); beasts += pit.DismissIdleBeasts(refund: true); back += pit.TakeBackIdle(); }
            // older saves (before the pit kept its list): a prisoner of the colony who stands in an arena without a bout goes to his bed
            foreach (var map in Find.Maps)
            {
                var arena = ArenaCells(map);
                if (arena.Count == 0) continue;
                foreach (var p in map.mapPawns.PrisonersOfColonySpawned.ToList())
                {
                    if (OfPawn(p) != null || !arena.Contains(p.Position)) continue;
                    if (p.ownership?.OwnedBed is Building_Bed bed && bed.Spawned && bed.Map == map) { PitOpponents.TakeBackTo(p, bed.Position); back++; }
                }
            }
            if (jobs + back + beasts > 0) HLog.Info("Underworld", $"Pit load sweep: {jobs} vanilla attacks between former fighters ended, {back} pawns taken back from the arena, {beasts} hired beasts sent away");
        }

        /// <summary>Every arena cell of the closed pits on the map.</summary>
        static HashSet<IntVec3> ArenaCells(Map map)
        {
            var set = new HashSet<IntVec3>();
            foreach (var pit in CompFightPit.All)
            {
                if (pit.Map != map) continue;
                var a = pit.ArenaInfo();
                if (a != null && a.Closed) foreach (var c in a.Cells) set.Add(c);
            }
            return set;
        }

        /// <summary>A vanilla melee attack of <paramref name="p"/> on a pawn he is not hostile to, who was his pit opponent: the foe holds a pit job or an
        /// attack on him, or both stand in a pit arena. Only asked when no bout holds <paramref name="p"/>.</summary>
        static bool VanillaPairAttack(Pawn p, Job job, HashSet<IntVec3> arena)
        {
            if (job.def != JobDefOf.AttackMelee || !(job.targetA.Thing is Pawn foe) || !foe.Spawned || foe.Map != p.Map || p.HostileTo(foe)) return false;
            var fj = foe.CurJob;
            if (fj != null && fj.targetA.Thing == p && (PitSetup.IsPitJob(fj.def) || fj.def == JobDefOf.AttackMelee)) return true;
            return arena.Count > 0 && arena.Contains(p.Position) && arena.Contains(foe.Position);
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
            if (p == null || p.Destroyed || p.Dead || !p.Spawned || p.Map != Map) return UwPay.T("Pit.Abort.Gone", p == null ? "?" : NameOf(p));
            if (p.InMentalState) return UwPay.T("Pit.Abort.Mental", NameOf(p));
            if (p.Drafted) return UwPay.T("Pit.Abort.Drafted", NameOf(p));
            if (Scored && p.Downed) return UwPay.T("Pit.Abort.Hurt", NameOf(p));
            // a visitor group that arrives moves from its travel toil to its stay toil: fine. Any other change (leaving, taking the wounded home,
            // turning hostile) or a lost lord calls the bout off.
            if (toil != null && p.GetLord()?.CurLordToil != toil && !PitFighters.StayToil(p)) return UwPay.T("Pit.Abort.Left", NameOf(p));
            if (KindOf(p) == PitKind.Prisoner && !p.IsPrisonerOfColony) return UwPay.T("Pit.Abort.Gone", NameOf(p));
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
                        Abort("fighters did not show up", UwPay.T("Pit.Abort.NoShow", NameOf(late))); return;
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
            if (p.IsPrisoner) { PitOpponents.BringIn(p, cell, Pit); return; }
            if (!p.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
            {
                // animals cannot open fence gates and prisoners cannot leave the prison: their handlers bring them in (and back afterwards).
                // People who cannot get in simply do not show up (the bout is called off).
                if (MayBeBroughtIn(p)) PitOpponents.BringIn(p, cell, Pit);
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
            if (cur != null && cur.def == PitSetup.JobFight && cur.targetA.Thing == foe) return;
            // a vanilla attack on the opponent (a retaliation the think tree handed out before Patch_PitNoRetaliation, another mod's job giver) is
            // replaced at once: it has no fail condition of ours, so saved with the game it would land one real blow after the load
            bool vanillaOnFoe = cur != null && cur.def == JobDefOf.AttackMelee && cur.targetA.Thing == foe;
            if (!vanillaOnFoe && now - last < 20) return;
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
            int nowR = Find.TickManager.TicksGame;
            if (recent.Count > 32) { var old = new List<Pawn>(); foreach (var kv in recent) if (nowR - kv.Value >= QuarrelCooldownTicks || nowR < kv.Value) old.Add(kv.Key); foreach (var o in old) recent.Remove(o); }
            if (A != null) recent[A] = nowR;
            if (B != null) recent[B] = nowR;
            recentEnd = nowR + QuarrelCooldownTicks;
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
            foreach (var p in new[] { A, B }) PitOpponents.TakeBack(p, Pit);
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

        /// <summary>The body parts a bruise of a scored bout may land on: outer parts of some size, never a source of consciousness.</summary>
        internal static List<BodyPartRecord> BruiseParts(Pawn p) =>
            p.health.hediffSet.GetNotMissingParts().Where(pt => pt.depth == BodyPartDepth.Outside && pt.coverageAbs > 0f && pt.def.hitPoints >= 10 && !pt.def.tags.Contains(BodyPartTagDefOf.ConsciousnessSource)).ToList();

        /// <summary>Most pain two bruises can add: two at the top severity, with the pain per severity the def really has, times 1.5 for pawns who feel pain more
        /// (a child's body, a pain-sensitive gene: the tests measured 1.1).</summary>
        internal static float WorstBruisePain() => 1.5f * 2f * 2.5f * (PitSetup.Bruise?.injuryProps?.painPerSeverity ?? 0.0125f);

        /// <summary>The loser of a scored bout walks away with one or two small vanilla bruises (no lord signal: AddHediff, not damage). A pawn who is already
        /// so close to his pain shock threshold that two bruises could tip him over gets none, and bruises that would down him anyway (a body already near the
        /// edge of Moving or Consciousness) are taken off again: the pit never sends a guest to the floor.</summary>
        internal void Bruise(Pawn loser)
        {
            var def = PitSetup.Bruise;
            if (def == null || loser == null || loser.Dead || loser.Downed || loser.health?.hediffSet == null || PitOpponents.IsBeast(loser)) return;
            try
            {
                if (loser.health.hediffSet.PainTotal + WorstBruisePain() >= loser.GetStatValue(StatDefOf.PainShockThreshold) - 0.02f) return;
                var parts = BruiseParts(loser);
                if (parts.Count == 0) return;
                var rng = JRand.Stream("underworld.pit.bruise");
                int n = 1 + rng.Range(0, 2);
                var added = new List<Hediff>();
                for (int i = 0; i < n; i++)
                {
                    var part = parts[rng.Range(0, parts.Count)];
                    var h = (Hediff_Injury)HediffMaker.MakeHediff(def, loser, part);
                    h.Severity = (float)rng.Range(1.0, 2.5);
                    loser.health.AddHediff(h, part);
                    added.Add(h);
                }
                if (loser.Downed && !loser.Dead)
                {
                    foreach (var h in added) loser.health.RemoveHediff(h);
                    HLog.Info("Underworld", "Bruises of " + loser.LabelShort + " would have downed him; taken off again");
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
            bool noBets = Bets.Count == 0;   // a bout nobody bet on (a lone colonist and a hired beast): no pool, no stakes to talk about
            string text = (Draw ? UwPay.T(noBets ? "Pit.Result.DrawNoBets" : "Pit.Result.Draw", NameA, NameB)
                : noBets ? UwPay.T("Pit.Result.WonNoBets", NameOf(w), NameOf(lo))
                : UwPay.T("Pit.Result.Won", NameOf(w), NameOf(lo), PitFighters.Silver(new Money(Result.PoolMicro)), PitFighters.Silver(new Money(Math.Max(0L, rest.Micro))))).CapitalizeFirst();
            // a bout against a hired beast: say plainly that the fighter is only bruised (it is fought for points)
            var person = KindA == PitKind.Beast ? B : KindB == PitKind.Beast ? A : null;
            if (Scored && person != null) text += " " + UwPay.T("Pit.Result.Points", person.LabelShortCap);
            else if (noBets && Scored) text += " " + UwPay.T("Pit.Result.PointsAll");
            if (PursePaid.Micro > 0) text += " " + UwPay.T("Pit.PursePaid", NameOf(w), PitFighters.Silver(PursePaid)).CapitalizeFirst();
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
            if (FeePaid.Micro > 0 && Map != null) { Ledger.Current.Post("world", Underworld.Till(Map), FeePaid, "Fight pit: opponent fee refunded"); FeePaid = Money.Zero; }
            if (FreeHire && Underworld.Comp != null && Underworld.Comp.OpponentsHired > 0) { Underworld.Comp.OpponentsHired--; FreeHire = false; }   // the free one is still to come
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
