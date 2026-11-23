// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Hearth.CasinoMath;

namespace Hearth.Casino
{
    [StaticConstructorOnStartup]
    public static class CasinoIcons
    {
        public static readonly Texture2D Volatility = ContentFinder<Texture2D>.Get("UI/HH/Casino/Volatility", false) ?? BaseContent.BadTex;
        public static readonly Texture2D OpenToGuests = ContentFinder<Texture2D>.Get("UI/HH/Casino/OpenToGuests", false) ?? BaseContent.BadTex;
        public static readonly Texture2D Practice = ContentFinder<Texture2D>.Get("UI/HH/Casino/Practice", false) ?? BaseContent.BadTex;
        public static readonly Texture2D Simulate = ContentFinder<Texture2D>.Get("UI/HH/Casino/Simulate", false) ?? BaseContent.BadTex;
    }

    public class CompProperties_CasinoGame : CompProperties
    {
        public CasinoGame game;
        /// <summary>How many people can play at once (slot machine 1, tables 4–6).</summary>
        public int seats = 1;
        /// <summary>Tables need a croupier (a colonist working the Serving type) to run for guests.</summary>
        public bool needsCroupier;
        public CompProperties_CasinoGame() { compClass = typeof(CompCasinoGame); }
    }

    /// <summary>
    /// The brain of every game building: runs the rounds of everybody seated there, keeps its own books (the "Pit Boss" numbers)
    /// and offers the player a demo game. State that survives a reload: volatility, stats, croupier timing.
    /// </summary>
    public class CompCasinoGame : ThingComp
    {
        public CompProperties_CasinoGame Props => (CompProperties_CasinoGame)props;
        public CasinoGame Game => Props.game;

        public SlotVolatility Volatility = SlotVolatility.Low;
        public bool GuestsAllowed = true;
        int roundTimer;
        int croupierTick = -99999;
        public Pawn Croupier;
        int unstaffedTicks;
        // books
        public long Rounds, StakedMicro, PaidMicro;
        public double Expected, Variance;
        public int TodayDay = -1, TodayRounds;
        public long TodayStaked, TodayPaid;
        public string LastResult;
        public int LastResultTick = -99999;
        // runtime
        int seatedCache, seatedCacheTick = -999;
        SlotPaytable paytable; SlotVolatility paytableVol; double paytableRtp;
        static readonly List<GamblerSession> tmp = new List<GamblerSession>();

        CompPowerTrader Power => parent.TryGetComp<CompPowerTrader>();
        public bool Powered => Power == null || Power.PowerOn;
        public Map Map => parent.Map;

        public override void PostExposeData()
        {
            Scribe_Values.Look(ref Volatility, "volatility", SlotVolatility.Low);
            Scribe_Values.Look(ref GuestsAllowed, "guestsAllowed", true);
            Scribe_Values.Look(ref roundTimer, "roundTimer"); Scribe_Values.Look(ref croupierTick, "croupierTick", -99999);
            Scribe_Values.Look(ref Rounds, "rounds"); Scribe_Values.Look(ref StakedMicro, "staked"); Scribe_Values.Look(ref PaidMicro, "paid");
            Scribe_Values.Look(ref Expected, "expected"); Scribe_Values.Look(ref Variance, "variance");
            Scribe_Values.Look(ref TodayDay, "todayDay", -1); Scribe_Values.Look(ref TodayRounds, "todayRounds");
            Scribe_Values.Look(ref TodayStaked, "todayStaked"); Scribe_Values.Look(ref TodayPaid, "todayPaid");
            Scribe_Values.Look(ref LastResult, "lastResult");
        }

        // Paytable and odds --------------------------------------------------------------------------------
        public SlotPaytable Paytable
        {
            get
            {
                double rtp = CS.SlotTargetRtp;
                if (paytable == null || paytableVol != Volatility || Math.Abs(paytableRtp - rtp) > 1e-9) { paytable = SlotPaytable.For(Volatility, rtp); paytableVol = Volatility; paytableRtp = rtp; }
                return paytable;
            }
        }

        /// <summary>Expected house edge (fraction of the stakes) of this game at the current settings.</summary>
        public double ExpectedEdge
        {
            get
            {
                switch (Game)
                {
                    case CasinoGame.Slots: return 1 - Paytable.Rtp;
                    case CasinoGame.Roulette: return CS.Roulette.TargetEdge;
                    case CasinoGame.Blackjack: return BlackjackSolver.Get(CS.Blackjack, 0.6).HouseEdge;
                    default: return RimdeckMath.HouseEdge(4, 10, CS.PokerRakeRate, CS.PokerCap);
                }
            }
        }

        /// <summary>σ of the net result per stake, for the house-risk bet limit.</summary>
        public double SigmaPerStake
        {
            get
            {
                switch (Game)
                {
                    case CasinoGame.Slots: return Paytable.Sigma;
                    case CasinoGame.Roulette: return 2.5;
                    case CasinoGame.Blackjack: return BlackjackSolver.Get(CS.Blackjack, 0.6).Sigma;
                    default: return 1.7;
                }
            }
        }

        // Staffing ----------------------------------------------------------------------------------------------
        public bool NeedsCroupier => Props.needsCroupier && CS.B(CS.Croupier);
        public bool Staffed => !NeedsCroupier || Find.TickManager.TicksGame - croupierTick < 45;
        public void Notify_CroupierPresent(Pawn p) { croupierTick = Find.TickManager.TicksGame; Croupier = p; }

        int wantTick = -9999; bool wantCache;
        /// <summary>Is somebody (a guest at the table, or any guest around while the house is open) waiting for this table to run?</summary>
        public bool WantsCroupier
        {
            get
            {
                if (!NeedsCroupier || !parent.Spawned || !Powered) return false;
                int now = Find.TickManager.TicksGame;
                if (now - wantTick >= 120) { wantTick = now; wantCache = ComputeWants(); }
                return wantCache;
            }
        }

        bool ComputeWants()
        {
            var c = CasinoComponent.Get();
            if (c != null) foreach (var s in c.Sessions) if (s.Device == parent && !s.Done && s.Kind == SessionKind.Guest) return true;
            if (!GuestUsable(out _)) return false;
            foreach (var g in GuestApi.GuestsOn(parent.Map)) return true;
            return false;
        }

        /// <summary>Can guests play here right now? Reason explains why not.</summary>
        public bool GuestUsable(out string reason)
        {
            reason = null;
            if (!Modules.On(ModuleId.Casino)) { reason = "module off"; return false; }
            if (!parent.Spawned) { reason = "not spawned"; return false; }
            if (!GuestsAllowed) { reason = "closed to guests"; return false; }
            if (!Powered) { reason = "no power"; return false; }
            if (parent.IsBrokenDown()) { reason = "broken down"; return false; }
            var venue = Venues.At(parent.Position, parent.Map);
            if (venue != null && !venue.IsOpenNow) { reason = "closed"; return false; }
            return true;
        }

        public bool Usable(bool forGuest, out string reason)
        {
            if (forGuest) return GuestUsable(out reason);
            reason = null;
            if (!Modules.On(ModuleId.Casino)) { reason = "module off"; return false; }
            if (!parent.Spawned) { reason = "not spawned"; return false; }
            if (!Powered) { reason = "no power"; return false; }
            return true;
        }

        public int Seated
        {
            get
            {
                int now = Find.TickManager.TicksGame;
                if (now != seatedCacheTick) { var c = CasinoComponent.Get(); seatedCache = c == null ? 0 : c.CountAt(parent); seatedCacheTick = now; }
                return seatedCache;
            }
        }
        public bool HasFreeSeat => Seated < Props.seats;

        /// <summary>Cells where players stand/sit: one for machines (the interaction cell), the sides of a table otherwise.</summary>
        public IEnumerable<IntVec3> PlayCells()
        {
            if (Props.seats <= 1 && parent.def.hasInteractionCell) { yield return parent.InteractionCell; yield break; }
            var map = parent.Map; if (map == null) yield break;
            var rect = parent.OccupiedRect(); var ic = parent.def.hasInteractionCell ? parent.InteractionCell : IntVec3.Invalid;
            foreach (var c in GenAdj.CellsAdjacentCardinal(parent))
                if (c != ic && c.InBounds(map) && c.Standable(map)) yield return c;
        }

        public IntVec3 CroupierCell => parent.def.hasInteractionCell ? parent.InteractionCell : parent.Position;

        /// <summary>The live parts of the device on the map (reels, wheel, cards, chips; 1.1.0, F31). Only things in view are drawn by the game.</summary>
        public override void PostDraw()
        {
            base.PostDraw();
            if (Guard.IsPaused(ModuleId.Casino)) return;
            try { CasinoFx.DrawOverlay(this); }
            catch (Exception e) { Guard.Fail(ModuleId.Casino, "Casino.Fx.Draw", e); }
        }

        /// <summary>The device leaves the map: its transient table show is dropped (nothing saved, nothing else changes).</summary>
        public override void PostDeSpawn(Map map
#if RW16PLUS
            , DestroyMode mode = DestroyMode.Vanish
#endif
            )
        {
#if RW16PLUS
            base.PostDeSpawn(map, mode);
#else
            base.PostDeSpawn(map);
#endif
            CasinoFx.Forget(this);
        }

        // Rounds ----------------------------------------------------------------------------------------------------
        public override void CompTick()
        {
            if (!parent.Spawned || Seated == 0) { if (roundTimer > 0) roundTimer = 0; unstaffedTicks = 0; return; }
            if (!Modules.On(ModuleId.Casino)) return;
            if (!Powered) return;
            if (NeedsCroupier)
            {
                // players who are only colonists never need the croupier (they play with house chips)
                bool guestSeated = false; var c = CasinoComponent.Get();
                if (c != null) foreach (var s in c.Sessions) if (s.Device == parent && !s.Done && s.Kind == SessionKind.Guest) { guestSeated = true; break; }
                if (guestSeated && !Staffed)
                {
                    if (++unstaffedTicks > 900) { unstaffedTicks = 0; Guard.Run(ModuleId.Casino, "Casino.NoCroupier", () => CasinoEngine.EndGuestSessions(this, "waited too long for a croupier")); }
                    return;
                }
            }
            unstaffedTicks = 0;
            if (++roundTimer < CS.RoundInterval(Game) * (NeedsCroupier ? CasinoEngine.DealSpeedFactor(Croupier) : 1f)) return;
            roundTimer = 0;
            Guard.Run(ModuleId.Casino, "Casino.Round." + Game, () => { tmp.Clear(); CasinoComponent.Get().SessionsAt(parent, tmp); CasinoEngine.RunRound(this, tmp); tmp.Clear(); });
        }

        public void Book(GamblerSession s, long stakedMicro, long paidMicro, double expectedHouse, double variance, string result)
        {
            int day = Find.TickManager.TicksGame / 60000;
            if (TodayDay != day) { TodayDay = day; TodayRounds = 0; TodayStaked = 0; TodayPaid = 0; }
            Rounds++; StakedMicro += stakedMicro; PaidMicro += paidMicro; Expected += expectedHouse; Variance += variance;
            TodayRounds++; TodayStaked += stakedMicro; TodayPaid += paidMicro;
            LastResult = result; LastResultTick = Find.TickManager.TicksGame;
        }

        public double HouseWin => (StakedMicro - PaidMicro) / (double)Money.PerSilver;
        public double ActualEdge => StakedMicro <= 0 ? 0 : 1.0 - PaidMicro / (double)StakedMicro;
        /// <summary>How far the lifetime result is from its expectation, in standard deviations (null while there is too little data).</summary>
        public double? LuckSigma => Rounds < 30 || Variance <= 0 ? (double?)null : (HouseWin - Expected) / Math.Sqrt(Variance);

        public static string LuckText(double? z)
        {
            if (z == null) return "HH.Casino.LuckNoData".Translate();
            double a = Math.Abs(z.Value);
            string kind = a < 1 ? "HH.Casino.LuckNormal".Translate() : a < 2 ? (z > 0 ? "HH.Casino.LuckHouseUp" : "HH.Casino.LuckHouseDown").Translate() : (z > 0 ? "HH.Casino.LuckHouseWayUp" : "HH.Casino.LuckHouseWayDown").Translate();
            return $"{z:+0.0;-0.0} σ ({kind})";
        }

        public override string CompInspectStringExtra()
        {
            if (!Modules.On(ModuleId.Casino)) return null;
            var sb = new System.Text.StringBuilder();
            sb.Append("HH.Casino.InspectGame".Translate(Game.ToString().ToLowerInvariant(), Seated, Props.seats));
            if (Game == CasinoGame.Slots) sb.Append(" · ").Append(("HH.Casino.Vol." + Volatility).Translate());
            if (NeedsCroupier) sb.Append(" · ").Append(Staffed ? "HH.Casino.Staffed".Translate() : "HH.Casino.NoCroupier".Translate());
            if (Rounds > 0)
                sb.Append("\n").Append("HH.Casino.InspectBooks".Translate(Rounds, HouseWin.ToString("+0.0;-0.0"), (ActualEdge * 100).ToString("0.0"), (ExpectedEdge * 100).ToString("0.0")));
            if (TodayDay == Find.TickManager.TicksGame / 60000 && TodayRounds > 0)
                sb.Append("\n").Append("HH.Casino.InspectToday".Translate(TodayRounds, ((TodayStaked - TodayPaid) / (double)Money.PerSilver).ToString("+0.0;-0.0")));
            var lz = LuckSigma; if (lz != null) sb.Append("\n").Append("HH.Casino.InspectLuck".Translate(LuckText(lz)));
            return sb.ToString();
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!Modules.On(ModuleId.Casino)) yield break;
            if (Game == CasinoGame.Slots)
            {
                yield return new Command_Action
                {
                    defaultLabel = ("HH.Casino.Vol." + Volatility).Translate(),
                    defaultDesc = "HH.Casino.VolDesc".Translate(Paytable.HitRate.ToString("P0"), Paytable.Sigma.ToString("0.0"), Paytable.MaxMultiplier.ToString("0"), (Paytable.Rtp * 100).ToString("0.#")),
                    icon = CasinoIcons.Volatility,
                    action = () => { Volatility = (SlotVolatility)(((int)Volatility + 1) % 3); paytable = null; }
                };
            }
            yield return new Command_Toggle
            {
                defaultLabel = "HH.Casino.OpenToGuests".Translate(),
                defaultDesc = "HH.Casino.OpenToGuestsDesc".Translate(),
                icon = CasinoIcons.OpenToGuests,
                isActive = () => GuestsAllowed,
                toggleAction = () => GuestsAllowed = !GuestsAllowed
            };
            yield return new Command_Action
            {
                defaultLabel = "HH.Casino.TryDemo".Translate(),
                defaultDesc = "HH.Casino.TryDemoDesc".Translate(),
                icon = CasinoIcons.Practice,
                action = () => Find.WindowStack.Add(new Dialog_CasinoDemo(this))
            };
            yield return new Command_Action
            {
                defaultLabel = "HH.Casino.Simulate".Translate(),
                defaultDesc = "HH.Casino.SimulateDesc".Translate(),
                icon = CasinoIcons.Simulate,
                action = () => Messages.Message(CasinoEngine.Simulate(this, 10000), parent, MessageTypeDefOf.NeutralEvent, false)
            };
            if (DebugSettings.godMode)
            {
                yield return new Command_Action { defaultLabel = "DEV: simulate 1000 rounds", action = () => CasinoEngine.DevSimulate(this, 1000) };
            }
        }
    }
}
