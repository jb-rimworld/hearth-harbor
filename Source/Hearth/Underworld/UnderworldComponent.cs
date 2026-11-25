// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Hearth.Underworld
{
    /// <summary>One order for a specific organ (Grim only, KONZEPT 6.11): bring it within the deadline for a premium.</summary>
    public sealed class OrganOrder : IExposable
    {
        public ThingDef organ;
        public int deadlineTick;
        public float premium = 1.8f;
        public bool done;
        public void ExposeData()
        {
            Scribe_Defs.Look(ref organ, "organ");
            Scribe_Values.Look(ref deadlineTick, "deadline");
            Scribe_Values.Look(ref premium, "premium", 1.8f);
            Scribe_Values.Look(ref done, "done");
        }
    }

    /// <summary>
    /// Save data of the Underworld module: Whispers, discovery state, scandal cooldowns, shady pawns, warnings the player confirmed, order book.
    /// Removing the mod leaves a loadable save (this component is simply dropped). Transient things (bouts, bets) are never saved.
    /// </summary>
    public sealed class UnderworldComponent : GameComponent
    {
        public readonly Game GameRef;

        // whispers (decay is applied lazily on read/write)
        float whispers; int whispersTick;
        // legacy (until 1.1.x): the stranger's discovery and the three content warnings. Since 1.2.0 nothing reads them; they are only
        // kept (read and written as before) so old saves load and keep their data. Do not build new behaviour on them.
        public bool NeverAsk; public int LastAskTick = -9_999_999; public int AskCount; public int StrangerFirstTick = -1;
        public bool ContentWarningAck, HuntWarningAck, OrganWarningAck;
        // scandals: map id → last tick
        Dictionary<int, int> scandalTick = new Dictionary<int, int>();
        public int HighRollersUntil;
        // pawn marks (thingIDNumber)
        List<int> shady = new List<int>();
        List<int> hunters = new List<int>();
        List<int> gang = new List<int>();           // members of gang visits (shady Inn guests are shady too, but no gang)
        List<int> paidFee = new List<int>();
        List<int> stolenThings = new List<int>();   // thing ids of loot carried by outlaws
        List<int> stolenFactions = new List<int>(); // parallel: faction loadID the loot came from
        // schedule
        public int NextSmugglerTick = -1, NextGangTick = -1, NextHuntTick = -1, NextOrderTick = -1;
        // order book
        public List<OrganOrder> Orders = new List<OrganOrder>();
        // statistics for the UI page and tests
        public int Bouts, Bets, DealsBought, DealsSold, LoungeSessions, GangVisits, Tours, Hunts, OrgansSold, Scandals, Smuggles;
        public long HouseTakeMicro, FenceProfitMicro;
        public float RenownApplied;
        // 1.2.0 (package C): open offers (smuggler landings, protection demands) and the gang timer's state
        public List<UwOffer> Offers = new List<UwOffer>();
        public int NextOfferId = 1;
        /// <summary>The first gang visit was planned 0.5 to 1.5 days after the first facility gangs use (set once per save).</summary>
        public bool FirstGangPlanned;
        /// <summary>Key (below HH.Underworld.Gang.Why.) of the reason the last gang visit failed, null when the last attempt worked; and its tick.</summary>
        public string GangRetryKey; public int GangRetryTick = -1;
        // last event per facility id (FacilityStatus ids): tick
        Dictionary<string, int> lastEvent = new Dictionary<string, int>();
        List<string> lek; List<int> lev;
        public int LastEvent(string id) => lastEvent.TryGetValue(id, out int t) ? t : -1;
        public void NoteEvent(string id) { if (Find.TickManager != null) lastEvent[id] = Find.TickManager.TicksGame; }

        public UnderworldComponent(Game game) { GameRef = game; }

        // ---- whispers ----------------------------------------------------------------------------------
        static float DecayPct => HSettings.Get("underworld.whispersDecay");

        public float WhispersAt(int tick)
        {
            float days = Math.Max(0, tick - whispersTick) / 60000f;
            return UnderworldMath.WhispersDecay(whispers, days, DecayPct);
        }
        public float Whispers => WhispersAt(Find.TickManager?.TicksGame ?? whispersTick);

        void Rebase(int tick) { whispers = WhispersAt(tick); whispersTick = Math.Max(whispersTick, tick); }

        public void AddWhispers(float delta, string reason)
        {
            int now = Find.TickManager?.TicksGame ?? whispersTick;
            Rebase(now);
            float before = whispers;
            whispers = UnderworldMath.WhispersClamp(whispers + delta);
            if (Math.Abs(whispers - before) > 0.001f) HLog.Debug("Underworld", $"Whispers {before:0.0} → {whispers:0.0} ({reason})");
        }

        /// <summary>Test hook: sets the stored value as of a given tick.</summary>
        public void SetWhispers(float value, int atTick) { whispers = UnderworldMath.WhispersClamp(value); whispersTick = atTick; }

        public int ScandalTickOn(Map m) => scandalTick.TryGetValue(m?.uniqueID ?? 0, out int t) ? t : -9_999_999;
        public void NoteScandal(Map m, int tick) { scandalTick[m?.uniqueID ?? 0] = tick; Scandals++; }
        public void NoteRenew(float delta) { RenownApplied += delta; }

        // ---- marks -----------------------------------------------------------------------------------------
        public bool IsShady(Pawn p) => shady.Contains(p.thingIDNumber);
        public void MarkShady(Pawn p) { if (!shady.Contains(p.thingIDNumber)) { shady.Add(p.thingIDNumber); Trim(shady); } }
        public bool IsGang(Pawn p) => gang.Contains(p.thingIDNumber);
        public void MarkGang(Pawn p) { if (!gang.Contains(p.thingIDNumber)) { gang.Add(p.thingIDNumber); Trim(gang); } }
        public bool IsHunter(Pawn p) => hunters.Contains(p.thingIDNumber);
        public void MarkHunter(Pawn p) { if (!hunters.Contains(p.thingIDNumber)) { hunters.Add(p.thingIDNumber); Trim(hunters); } }
        public bool PaidFee(Pawn p) => paidFee.Contains(p.thingIDNumber);
        public void NotePaidFee(Pawn p) { if (!paidFee.Contains(p.thingIDNumber)) { paidFee.Add(p.thingIDNumber); Trim(paidFee); } }
        public void ClearPaidFee(Pawn p) => paidFee.Remove(p.thingIDNumber);
        static void Trim(List<int> l) { if (l.Count > 400) l.RemoveRange(0, l.Count - 300); }

        public void MarkStolen(Thing t, Faction from)
        {
            if (t == null) return;
            stolenThings.Add(t.thingIDNumber); stolenFactions.Add(from?.loadID ?? -1);
            if (stolenThings.Count > 300) { stolenThings.RemoveRange(0, 100); stolenFactions.RemoveRange(0, 100); }
        }
        public bool IsStolen(Thing t, out Faction from)
        {
            from = null;
            int i = t == null ? -1 : stolenThings.IndexOf(t.thingIDNumber);
            if (i < 0) return false;
            int fid = stolenFactions[i];
            if (fid >= 0 && Find.FactionManager != null) from = Find.FactionManager.AllFactionsListForReading.FirstOrDefault(f => f.loadID == fid);
            return true;
        }
        public void ForgetStolen(Thing t) { int i = stolenThings.IndexOf(t.thingIDNumber); if (i >= 0) { stolenThings.RemoveAt(i); stolenFactions.RemoveAt(i); } }

        // ---- save ---------------------------------------------------------------------------------------------
        List<int> sk, sv;
        public override void ExposeData()
        {
            Scribe_Values.Look(ref whispers, "whispers");
            Scribe_Values.Look(ref whispersTick, "whispersTick");
            Scribe_Values.Look(ref NeverAsk, "neverAsk");
            Scribe_Values.Look(ref LastAskTick, "lastAsk", -9_999_999);
            Scribe_Values.Look(ref AskCount, "askCount");
            Scribe_Values.Look(ref StrangerFirstTick, "strangerFirst", -1);
            Scribe_Values.Look(ref ContentWarningAck, "ackContent");
            Scribe_Values.Look(ref HuntWarningAck, "ackHunt");
            Scribe_Values.Look(ref OrganWarningAck, "ackOrgan");
            Scribe_Collections.Look(ref scandalTick, "scandalTick", LookMode.Value, LookMode.Value, ref sk, ref sv);
            Scribe_Values.Look(ref HighRollersUntil, "highRollersUntil");
            Scribe_Collections.Look(ref shady, "shady", LookMode.Value);
            Scribe_Collections.Look(ref hunters, "hunters", LookMode.Value);
            Scribe_Collections.Look(ref gang, "gangPawns", LookMode.Value);
            Scribe_Collections.Look(ref paidFee, "paidFee", LookMode.Value);
            Scribe_Collections.Look(ref stolenThings, "stolenThings", LookMode.Value);
            Scribe_Collections.Look(ref stolenFactions, "stolenFactions", LookMode.Value);
            Scribe_Values.Look(ref NextSmugglerTick, "nextSmuggler", -1);
            Scribe_Values.Look(ref NextGangTick, "nextGang", -1);
            Scribe_Values.Look(ref NextHuntTick, "nextHunt", -1);
            Scribe_Values.Look(ref NextOrderTick, "nextOrder", -1);
            Scribe_Collections.Look(ref Orders, "orders", LookMode.Deep);
            Scribe_Values.Look(ref Bouts, "bouts"); Scribe_Values.Look(ref Bets, "bets");
            Scribe_Values.Look(ref DealsBought, "dealsBought"); Scribe_Values.Look(ref DealsSold, "dealsSold");
            Scribe_Values.Look(ref LoungeSessions, "lounge"); Scribe_Values.Look(ref GangVisits, "gangVisits");
            Scribe_Values.Look(ref Hunts, "hunts"); Scribe_Values.Look(ref Tours, "tours"); Scribe_Values.Look(ref OrgansSold, "organsSold");
            Scribe_Values.Look(ref Scandals, "scandals"); Scribe_Values.Look(ref Smuggles, "smuggles");
            string h = HouseTakeMicro.ToString(), f = FenceProfitMicro.ToString();
            Scribe_Values.Look(ref h, "houseTake"); Scribe_Values.Look(ref f, "fenceProfit");
            Scribe_Values.Look(ref RenownApplied, "renownApplied");
            Scribe_Collections.Look(ref Offers, "offers", LookMode.Deep);
            Scribe_Values.Look(ref NextOfferId, "nextOfferId", 1);
            Scribe_Values.Look(ref FirstGangPlanned, "firstGangPlanned");
            Scribe_Values.Look(ref GangRetryKey, "gangRetryKey");
            Scribe_Values.Look(ref GangRetryTick, "gangRetryTick", -1);
            Scribe_Collections.Look(ref lastEvent, "lastEvent", LookMode.Value, LookMode.Value, ref lek, ref lev);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                long.TryParse(h, out HouseTakeMicro); long.TryParse(f, out FenceProfitMicro);
            }
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (scandalTick == null) scandalTick = new Dictionary<int, int>();
                if (shady == null) shady = new List<int>();
                if (hunters == null) hunters = new List<int>();
                if (gang == null) gang = new List<int>();
                if (paidFee == null) paidFee = new List<int>();
                if (stolenThings == null) stolenThings = new List<int>();
                if (stolenFactions == null) stolenFactions = new List<int>();
                if (Orders == null) Orders = new List<OrganOrder>();
                Orders.RemoveAll(o => o == null || o.organ == null);
                if (Offers == null) Offers = new List<UwOffer>();
                Offers.RemoveAll(o => o == null || !o.LoadedOk);
                if (lastEvent == null) lastEvent = new Dictionary<string, int>();
                int n = Math.Min(stolenThings.Count, stolenFactions.Count);
                if (stolenThings.Count != n) stolenThings.RemoveRange(n, stolenThings.Count - n);
                if (stolenFactions.Count != n) stolenFactions.RemoveRange(n, stolenFactions.Count - n);
            }
        }
    }
}
