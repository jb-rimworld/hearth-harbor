// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Hearth.Casino
{
    public enum SessionKind { Guest = 0, Colonist = 1, Demo = 2 }

    /// <summary>
    /// One person playing at one machine or table: a purse of chips, a stake plan and a tally. Saved, so a reload in the middle
    /// of a session neither loses a guest's money nor duplicates it. Money moves in the ledger only at buy-in and cash-out;
    /// the rounds in between change the purse (and the stats).
    /// </summary>
    public sealed class GamblerSession : IExposable
    {
        public Pawn Pawn;
        public Thing Device;
        public SessionKind Kind;
        public bool Spree;                // colonist on a gambling spree (ignores the drug policy)
        public Money Purse, BuyIn;
        public long StakedMicro, PaidMicro;
        public int Rounds, StartTick, EndTick;
        public string HouseAccount;
        public bool VirtualMoney;         // buy-in was booked without physical silver (vanilla visitors without coins)
        public bool Chips;                // played with chips of a cashier's cage
        public bool Done;
        public string EndReason;
        public float StakeShare = 0.10f;  // share of the buy-in per round
        public float Risk = 0.5f;         // 0 = cautious, 1 = reckless: picks roulette bets and poker aggression
        public int BestWinTick;

        public CompCasinoGame Comp => Device?.TryGetComp<CompCasinoGame>();
        public Money Net => Purse - BuyIn;
        public bool IsColonist => Kind == SessionKind.Colonist;
        public int Age => Find.TickManager.TicksGame - StartTick;

        public void ExposeData()
        {
            Scribe_References.Look(ref Pawn, "pawn");
            Scribe_References.Look(ref Device, "device");
            Scribe_Values.Look(ref Kind, "kind");
            Scribe_Values.Look(ref Spree, "spree");
            long p = Purse.Micro, b = BuyIn.Micro;
            Scribe_Values.Look(ref p, "purse"); Scribe_Values.Look(ref b, "buyIn");
            if (Scribe.mode == LoadSaveMode.LoadingVars) { Purse = new Money(p); BuyIn = new Money(b); }
            Scribe_Values.Look(ref StakedMicro, "staked"); Scribe_Values.Look(ref PaidMicro, "paid");
            Scribe_Values.Look(ref Rounds, "rounds"); Scribe_Values.Look(ref StartTick, "start"); Scribe_Values.Look(ref EndTick, "end");
            Scribe_Values.Look(ref HouseAccount, "account"); Scribe_Values.Look(ref VirtualMoney, "virtual"); Scribe_Values.Look(ref Chips, "chips");
            Scribe_Values.Look(ref Done, "done"); Scribe_Values.Look(ref EndReason, "endReason");
            Scribe_Values.Look(ref StakeShare, "stakeShare", 0.03f); Scribe_Values.Look(ref Risk, "risk", 0.5f); Scribe_Values.Look(ref BestWinTick, "bestWin");
        }
    }

    /// <summary>What the casino remembers about one colonist's gambling (sessions, craving after a cure).</summary>
    public sealed class GamblerRecord : IExposable
    {
        public int PawnId;
        public Pawn Pawn;
        public List<int> SessionTicks = new List<int>();
        public int TotalSessions, LastSessionTick = -999999;
        public int CravingUntil = -1;
        public int CuredStage;
        public bool EverAddicted;

        public int CountInWindow(int now)
        {
            SessionTicks.RemoveAll(t => now - t > Hearth.CasinoMath.GamblingMath.WindowTicks);
            return SessionTicks.Count;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref PawnId, "pawnId");
            Scribe_References.Look(ref Pawn, "pawn");
            Scribe_Collections.Look(ref SessionTicks, "sessionTicks", LookMode.Value);
            Scribe_Values.Look(ref TotalSessions, "total"); Scribe_Values.Look(ref LastSessionTick, "last", -999999);
            Scribe_Values.Look(ref CravingUntil, "craving", -1); Scribe_Values.Look(ref CuredStage, "curedStage"); Scribe_Values.Look(ref EverAddicted, "everAddicted");
            if (Scribe.mode == LoadSaveMode.PostLoadInit && SessionTicks == null) SessionTicks = new List<int>();
        }
    }

    /// <summary>One line of the day book: a house account's rounds of one game on one day.</summary>
    public sealed class DayRow : IExposable
    {
        public int Day; public string Account; public int Game;
        public int Rounds; public long StakedMicro, PaidMicro;
        public double Expected, Variance;      // expected house win and variance of the house win, in silver
        public void ExposeData()
        {
            Scribe_Values.Look(ref Day, "day"); Scribe_Values.Look(ref Account, "account"); Scribe_Values.Look(ref Game, "game");
            Scribe_Values.Look(ref Rounds, "rounds"); Scribe_Values.Look(ref StakedMicro, "staked"); Scribe_Values.Look(ref PaidMicro, "paid");
            Scribe_Values.Look(ref Expected, "expected"); Scribe_Values.Look(ref Variance, "variance");
        }
        public double HouseWin => (StakedMicro - PaidMicro) / (double)Money.PerSilver;
    }

    /// <summary>A house account the casino has seen (for the closing sweep and the Casino page).</summary>
    public sealed class HouseInfo : IExposable
    {
        public string Account; public int MapId; public string Label; public int LastActivityTick, LastSweepDay = -1; public bool Dirty;
        public void ExposeData()
        {
            Scribe_Values.Look(ref Account, "account"); Scribe_Values.Look(ref MapId, "map"); Scribe_Values.Look(ref Label, "label");
            Scribe_Values.Look(ref LastActivityTick, "lastActivity"); Scribe_Values.Look(ref LastSweepDay, "lastSweep", -1); Scribe_Values.Look(ref Dirty, "dirty");
        }
    }

    /// <summary>The module's save game data: active sessions, colonist gambling records, the day book and the known houses.</summary>
    public sealed class CasinoComponent : GameComponent
    {
        public static CasinoComponent Instance;
        public List<GamblerSession> Sessions = new List<GamblerSession>();
        public List<GamblerRecord> Records = new List<GamblerRecord>();
        public List<DayRow> Rows = new List<DayRow>();
        public List<HouseInfo> Houses = new List<HouseInfo>();
        public List<int> InitializedPolicies = new List<int>();
        public Dictionary<string, int> GuestSessionCount = new Dictionary<string, int>();   // not saved: guests per stay
        public int BetCounter;

        public CasinoComponent(Game g) { Instance = this; }

        public static CasinoComponent Get() => Instance ?? (Instance = Current.Game?.GetComponent<CasinoComponent>());

        public GamblerSession SessionOf(Pawn p) { for (int i = 0; i < Sessions.Count; i++) if (Sessions[i].Pawn == p) return Sessions[i]; return null; }
        public int CountAt(Thing device) { int n = 0; for (int i = 0; i < Sessions.Count; i++) if (Sessions[i].Device == device && !Sessions[i].Done) n++; return n; }
        public List<GamblerSession> SessionsAt(Thing device, List<GamblerSession> into = null)
        {
            into = into ?? new List<GamblerSession>();
            for (int i = 0; i < Sessions.Count; i++) if (Sessions[i].Device == device && !Sessions[i].Done) into.Add(Sessions[i]);
            return into;
        }
        public Money OutstandingAt(string account) { long m = 0; for (int i = 0; i < Sessions.Count; i++) if (Sessions[i].HouseAccount == account && Sessions[i].Kind == SessionKind.Guest) m += Sessions[i].Purse.Micro; return new Money(m); }

        public GamblerRecord RecordOf(Pawn p, bool create)
        {
            for (int i = 0; i < Records.Count; i++) if (Records[i].PawnId == p.thingIDNumber) { Records[i].Pawn = p; return Records[i]; }
            if (!create) return null;
            var r = new GamblerRecord { PawnId = p.thingIDNumber, Pawn = p };
            Records.Add(r); return r;
        }

        public HouseInfo HouseOf(string account, Map map, string label)
        {
            var h = Houses.Find(x => x.Account == account);
            if (h == null) { h = new HouseInfo { Account = account, MapId = map?.uniqueID ?? 0, Label = label }; Houses.Add(h); }
            if (label != null) h.Label = label;
            return h;
        }

        public DayRow RowFor(int day, string account, CasinoGame game)
        {
            for (int i = Rows.Count - 1; i >= 0; i--)
            {
                var r = Rows[i];
                if (r.Day == day && r.Account == account && r.Game == (int)game) return r;
                if (r.Day < day - 1) break;
            }
            var row = new DayRow { Day = day, Account = account, Game = (int)game };
            Rows.Add(row);
            Rows.RemoveAll(r => r.Day < day - 30);
            return row;
        }

        public IEnumerable<DayRow> RowsOn(int day, string account = null) => Rows.Where(r => r.Day == day && (account == null || r.Account == account));

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref Sessions, "sessions", LookMode.Deep);
            Scribe_Collections.Look(ref Records, "records", LookMode.Deep);
            Scribe_Collections.Look(ref Rows, "rows", LookMode.Deep);
            Scribe_Collections.Look(ref Houses, "houses", LookMode.Deep);
            Scribe_Collections.Look(ref InitializedPolicies, "initializedPolicies", LookMode.Value);
            Scribe_Values.Look(ref BetCounter, "betCounter");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                Sessions = Sessions ?? new List<GamblerSession>(); Records = Records ?? new List<GamblerRecord>();
                Rows = Rows ?? new List<DayRow>(); Houses = Houses ?? new List<HouseInfo>(); InitializedPolicies = InitializedPolicies ?? new List<int>();
                Sessions.RemoveAll(s => s == null || s.Pawn == null || s.Device == null);
                Records.RemoveAll(r => r == null);
            }
        }
    }
}
