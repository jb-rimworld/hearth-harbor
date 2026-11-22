// SPDX-License-Identifier: MIT
// KONZEPT 11.4 / 8.16: indulgences and paid ascension. Stances come from precepts (XML), items from IndulgenceDefs (XML),
// money goes through Payments/Ledger. Everything is Ideology-only and degrades to nothing without it.
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Hearth
{
    public enum MoneyTheme { Indulgences = 0, Ascension = 1, Both = 2 }

    /// <summary>Put on the precept defs of our two issues: which theme and which stance they stand for.</summary>
    public class PreceptStanceExt : DefModExtension
    {
        public MoneyTheme theme;
        public MoneyStance stance = MoneyStance.Accepted;
    }

    /// <summary>
    /// A thing that can be bought for faith: an indulgence or a rank of the ascension ladder (11.4). Modders add their own in XML
    /// (set workerClass to their IndulgenceWorker). Prices are silver; priceThing × priceThingFactor reads the live market value.
    /// </summary>
    public class IndulgenceDef : Def
    {
        public MoneyTheme theme = MoneyTheme.Indulgences;
        public Type workerClass = typeof(Dlc.IndulgenceWorker_Absolution);
        public float basePrice;
        public ThingDef priceThing;
        public float priceThingFactor = 1f;
        public int capPawnQuadrum, capPawnYear, capColonyQuadrum, capIdeoQuadrum;
        public int rank;       // ascension rank this item grants (1-4), 0 if none
        public int minRank;    // rank the buyer must already hold
        public bool fluidOnly;
        [Unsaved] Dlc.IndulgenceWorker worker;

        public Dlc.IndulgenceWorker Worker
        {
            get
            {
                if (worker == null) { worker = (Dlc.IndulgenceWorker)Activator.CreateInstance(workerClass); worker.def = this; }
                return worker;
            }
        }

        /// <summary>List price in silver before factors: fixed, or the market value of priceThing × factor.</summary>
        public float ListPrice => priceThing != null ? priceThing.BaseMarketValue * priceThingFactor : basePrice;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (var e in base.ConfigErrors()) yield return e;
            if (ListPrice <= 0) yield return "no price";
            if (workerClass == null || !typeof(Dlc.IndulgenceWorker).IsAssignableFrom(workerClass)) yield return "workerClass must derive from IndulgenceWorker";
        }
    }
}

namespace Hearth.Dlc
{
    /// <summary>Reads the stance of an ideoligion towards one money issue (11.4). No precept = Not a thing.</summary>
    public static class IdeoStances
    {
        public static MoneyStance Of(Ideo ideo, MoneyTheme theme)
        {
            if (ideo == null || theme == MoneyTheme.Both) return MoneyStance.NotAThing;
            var issue = theme == MoneyTheme.Indulgences ? DlcDefs.IssueIndulgences : DlcDefs.IssueAscension;
            if (issue == null) return MoneyStance.NotAThing;
            var ps = ideo.PreceptsListForReading;
            for (int i = 0; i < ps.Count; i++)
                if (ps[i].def.issue == issue) return ps[i].def.GetModExtension<PreceptStanceExt>()?.stance ?? MoneyStance.NotAThing;
            return MoneyStance.NotAThing;
        }
        public static MoneyStance Of(Pawn p, MoneyTheme theme) => !DlcDefs.Ideology ? MoneyStance.NotAThing : Of(p?.Ideo, theme);

        /// <summary>For items of theme Both the more positive stance counts.</summary>
        public static MoneyStance For(Pawn p, IndulgenceDef d)
        {
            if (d.theme != MoneyTheme.Both) return Of(p, d.theme);
            var a = Of(p, MoneyTheme.Indulgences); var b = Of(p, MoneyTheme.Ascension);
            return Positive(a) >= Positive(b) ? a : b;
        }
        static int Positive(MoneyStance s) => s == MoneyStance.Accepted || s == MoneyStance.Sacred ? (int)s + 10 : s == MoneyStance.Disapproved ? 5 : 0;
        public static bool CanBuy(MoneyStance s) => s == MoneyStance.Disapproved || s == MoneyStance.Accepted || s == MoneyStance.Sacred;
        public static PreceptDef PreceptFor(MoneyTheme theme, MoneyStance stance)
        {
            var issue = theme == MoneyTheme.Indulgences ? DlcDefs.IssueIndulgences : DlcDefs.IssueAscension;
            return DefDatabase<PreceptDef>.AllDefs.FirstOrDefault(d => d.issue == issue && d.GetModExtension<PreceptStanceExt>()?.stance == stance);
        }
    }

    // ---------------------------------------------------------------------------------------------------------------- workers
    public abstract class IndulgenceWorker
    {
        public IndulgenceDef def;
        public abstract bool CanApply(Pawn p, out string reason);
        public abstract void Apply(Pawn p);
        protected static string R(string key, string en) => ("HH.Dlc.Reason." + key).TranslateOr(en);
    }

    public class IndulgenceWorker_Absolution : IndulgenceWorker
    {
        static Thought_Memory Worst(Pawn p)
        {
            Thought_Memory worst = null; float w = 0f;
            var mem = p.needs?.mood?.thoughts?.memories?.Memories; if (mem == null) return null;
            foreach (var m in mem)
            {
                if (m.sourcePrecept == null) continue;
                float o = m.MoodOffset(); if (o < w) { w = o; worst = m; }
            }
            return worst;
        }
        public override bool CanApply(Pawn p, out string reason)
        {
            reason = null;
            if (Worst(p) != null) return true;
            reason = R("NoGuilt", "no memory of a broken commandment"); return false;
        }
        public override void Apply(Pawn p) { var m = Worst(p); if (m != null) p.needs.mood.thoughts.memories.RemoveMemory(m); }
    }

    public class IndulgenceWorker_MinorMercy : IndulgenceWorker
    {
        public const float MaxSeverity = 10f;
        public static Hediff_Injury Pick(Pawn p)
        {
            Hediff_Injury best = null;
            foreach (var h in p.health.hediffSet.hediffs)
                if (h is Hediff_Injury inj && !inj.IsPermanent() && inj.Severity > 0f && inj.Severity <= MaxSeverity && (best == null || inj.Severity > best.Severity)) best = inj;
            return best;
        }
        public override bool CanApply(Pawn p, out string reason) { reason = null; if (Pick(p) != null) return true; reason = R("NoFresh", "no fresh wound of up to 10 hit points"); return false; }
        public override void Apply(Pawn p) { var h = Pick(p); if (h != null) p.health.RemoveHediff(h); }
    }

    public class IndulgenceWorker_GreaterMercy : IndulgenceWorker
    {
        public static Hediff_Injury Pick(Pawn p)
        {
            Hediff_Injury best = null;
            foreach (var h in p.health.hediffSet.hediffs)
                if (h is Hediff_Injury inj && inj.IsPermanent() && (best == null || inj.Severity > best.Severity)) best = inj;
            return best;
        }
        public override bool CanApply(Pawn p, out string reason) { reason = null; if (Pick(p) != null) return true; reason = R("NoScar", "no lasting scar"); return false; }
        public override void Apply(Pawn p) { var h = Pick(p); if (h != null) p.health.RemoveHediff(h); }
    }

    public class IndulgenceWorker_Certainty : IndulgenceWorker
    {
        public override bool CanApply(Pawn p, out string reason)
        {
            reason = null;
            if (p.ideo == null || p.Ideo == null || Find.IdeoManager.classicMode) { reason = R("NoIdeo", "no ideoligion"); return false; }
            if (p.ideo.Certainty >= 0.995f) { reason = R("CertainEnough", "certainty is already full"); return false; }
            return true;
        }
        public override void Apply(Pawn p) { p.ideo.OffsetCertainty(0.10f); }
    }

    public class IndulgenceWorker_Rank : IndulgenceWorker
    {
        public override bool CanApply(Pawn p, out string reason)
        {
            reason = null; int cur = DlcState.Current?.RankOf(p) ?? 0;
            if (cur >= def.rank) { reason = R("RankHeld", "already holds this rank or a higher one"); return false; }
            if (cur != def.rank - 1) { reason = R("RankOrder", "needs the previous rank first"); return false; }
            return true;
        }
        public override void Apply(Pawn p) { DlcState.Current?.SetRank(p, def.rank); }
    }

    public class IndulgenceWorker_Psylink : IndulgenceWorker
    {
        public override bool CanApply(Pawn p, out string reason)
        {
            reason = null;
            if (!DlcDefs.Royalty) { reason = R("NeedRoyalty", "needs Royalty"); return false; }
            if (p.GetPsylinkLevel() >= p.GetMaxPsylinkLevel()) { reason = R("PsylinkMax", "psylink is already at its limit"); return false; }
            if (p.GetStatValue(StatDefOf.PsychicSensitivity) <= 0f) { reason = R("NotPsychic", "cannot form a psylink"); return false; }
            return true;
        }
        public override void Apply(Pawn p) { p.ChangePsylinkLevel(1, sendLetter: true); }
    }

    public class IndulgenceWorker_Reform : IndulgenceWorker
    {
        public override bool CanApply(Pawn p, out string reason)
        {
            reason = null; var ideo = p.Ideo;
            if (ideo == null || !ideo.Fluid || ideo.development == null) { reason = R("NotFluid", "only fluid ideoligions can reform"); return false; }
            if (ideo.development.CanReformNow) { reason = R("ReformReady", "reform is already possible"); return false; }
            return true;
        }
        public override void Apply(Pawn p) { p.Ideo.development.TryAddDevelopmentPoints(1); }
    }

    // ---------------------------------------------------------------------------------------------------------------- state
    public sealed class Purchase : IExposable
    {
        public Pawn Pawn; public string Def; public int Tick; public int IdeoId; public bool Colony; public long PriceMicro;
        public void ExposeData()
        {
            Scribe_References.Look(ref Pawn, "p"); Scribe_Values.Look(ref Def, "d"); Scribe_Values.Look(ref Tick, "t");
            Scribe_Values.Look(ref IdeoId, "i"); Scribe_Values.Look(ref Colony, "c"); Scribe_Values.Look(ref PriceMicro, "m");
        }
    }

    /// <summary>
    /// Saved state of the DLC integration: purchase log, ranks, AI-roll bookkeeping, fund destinations, noble stays.
    /// Removing the mod leaves a loadable save (everything is plain values and references).
    /// </summary>
    public sealed class DlcState : GameComponent
    {
        public static DlcState Current => Verse.Current.Game?.GetComponent<DlcState>();

        List<Purchase> purchases = new List<Purchase>();
        Dictionary<Pawn, int> ranks = new Dictionary<Pawn, int>();
        Dictionary<int, int> firstSeen = new Dictionary<int, int>();
        List<int> rolledIdeos = new List<int>();
        Dictionary<int, int> fundDest = new Dictionary<int, int>();
        List<int> goodwillLog = new List<int>();   // tick, amount pairs
        public NobleData Nobles = new NobleData();
        List<Pawn> tmpP; List<int> tmpI, tmpK, tmpV, tmpFk, tmpFv;

        public DlcState(Game game) { }
        public int PurchaseCount => purchases.Count;
        public int RankCount => ranks.Count;
        public int NobleStayCount => Nobles.Stays.Count;
        public IReadOnlyList<Purchase> Purchases => purchases;

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref purchases, "purchases", LookMode.Deep);
            Scribe_Collections.Look(ref ranks, "ranks", LookMode.Reference, LookMode.Value, ref tmpP, ref tmpI);
            Scribe_Collections.Look(ref firstSeen, "firstSeen", LookMode.Value, LookMode.Value, ref tmpK, ref tmpV);
            Scribe_Collections.Look(ref rolledIdeos, "rolledIdeos", LookMode.Value);
            Scribe_Collections.Look(ref fundDest, "fundDest", LookMode.Value, LookMode.Value, ref tmpFk, ref tmpFv);
            Scribe_Collections.Look(ref goodwillLog, "goodwillLog", LookMode.Value);
            Scribe_Deep.Look(ref Nobles, "nobles");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                purchases = purchases ?? new List<Purchase>(); ranks = ranks ?? new Dictionary<Pawn, int>(); firstSeen = firstSeen ?? new Dictionary<int, int>();
                rolledIdeos = rolledIdeos ?? new List<int>(); fundDest = fundDest ?? new Dictionary<int, int>(); goodwillLog = goodwillLog ?? new List<int>();
                Nobles = Nobles ?? new NobleData();
                purchases.RemoveAll(p => p == null || p.Def == null);
                foreach (var k in ranks.Where(kv => kv.Key == null).Select(kv => kv.Key).ToList()) ranks.Remove(k);
            }
        }

        public void OnGameReady() { Maintain(); }

        public void RareTick(int ticksGame)
        {
            if (ticksGame % 2500 == 7) Guard.Run(ModuleId.CityLife, "Dlc.Maintain", () => Maintain());
            Guard.Run(ModuleId.Inn, "Dlc.Nobles", () => NobleStays.Tick(this, ticksGame));
        }

        // ---- ranks
        public int RankOf(Pawn p) => p != null && ranks.TryGetValue(p, out int r) ? r : 0;
        public void SetRank(Pawn p, int r) { if (p == null) return; ranks[p] = Mathf.Clamp(r, 0, 4); p.needs?.mood?.thoughts?.situational?.Notify_SituationalThoughtsDirty(); }
        public void ClearRanks() { ranks.Clear(); }

        // ---- purchases
        public void Record(Purchase p) { purchases.Add(p); firstSeen[p.Pawn.thingIDNumber] = p.Tick; Prune(p.Tick); }
        void Prune(int now) { purchases.RemoveAll(x => now - x.Tick > DlcMath.TicksPerYear + DlcMath.TicksPerQuadrum); }
        public int Count(Func<Purchase, bool> pred, int now, int window)
        {
            int n = 0; for (int i = 0; i < purchases.Count; i++) if (now - purchases[i].Tick < window && pred(purchases[i])) n++; return n;
        }
        public int LastPurchaseTick(Pawn p)
        {
            int t = int.MinValue;
            for (int i = 0; i < purchases.Count; i++) if (purchases[i].Pawn == p && purchases[i].Tick > t) t = purchases[i].Tick;
            if (t != int.MinValue) return t;
            int now = Find.TickManager.TicksGame;
            if (!firstSeen.TryGetValue(p.thingIDNumber, out t)) { firstSeen[p.thingIDNumber] = now; t = now; }
            return t;
        }

        // ---- fund destination per ideoligion
        public FundDestination DestinationFor(Ideo ideo)
            => ideo != null && fundDest.TryGetValue(ideo.id, out int v) ? (FundDestination)v : HSettings.GetEnum<FundDestination>("dlc.fundDestination");
        public void SetDestination(Ideo ideo, FundDestination d) { if (ideo != null) fundDest[ideo.id] = (int)d; }

        public int GoodwillGainedThisQuadrum(int now)
        {
            int sum = 0; for (int i = 0; i + 1 < goodwillLog.Count; i += 2) if (now - goodwillLog[i] < DlcMath.TicksPerQuadrum) sum += goodwillLog[i + 1];
            return sum;
        }
        public void LogGoodwill(int now, int amount) { goodwillLog.Add(now); goodwillLog.Add(amount); if (goodwillLog.Count > 200) goodwillLog.RemoveRange(0, goodwillLog.Count - 200); }

        // ---- AI ideoligions (11.4): once per ideoligion, weighted by memes, fixed-faction ideoligions untouched
        public int Maintain()
        {
            int changes = 0;
            if (ranks.Count > 0) { var dead = ranks.Keys.Where(k => k == null || k.Destroyed && !k.Dead).ToList(); foreach (var d in dead) { ranks.Remove(d); changes++; } }
            if (!DlcDefs.Ideology || Find.IdeoManager == null || DlcDefs.IssueIndulgences == null) return changes;
            if (!HSettings.GetBool("dlc.aiIdeoPrecepts")) return changes;
            foreach (var ideo in Find.IdeoManager.IdeosListForReading.ToList())
            {
                if (rolledIdeos.Contains(ideo.id)) continue;
                rolledIdeos.Add(ideo.id);
                if (IsFixedOrPlayer(ideo)) continue;
                RollFor(ideo, MoneyTheme.Indulgences); RollFor(ideo, MoneyTheme.Ascension);
                changes++;
            }
            return changes;
        }

        static bool IsFixedOrPlayer(Ideo ideo)
        {
            if (Faction.OfPlayer?.ideos != null && Faction.OfPlayer.ideos.Has(ideo)) return true;
            foreach (var f in Find.FactionManager.AllFactionsListForReading)
                if (f.def.fixedIdeo && f.ideos != null && f.ideos.Has(ideo)) return true;
            return false;
        }

        public static MoneyStance RollFor(Ideo ideo, MoneyTheme theme)
        {
            var memes = ideo.memes.Select(m => m.defName).ToList();
            var w = DlcMath.AiStanceWeights(theme == MoneyTheme.Indulgences ? "indulgences" : "ascension", memes);
            Rand.PushState(ideo.id * 7919 + 13 + (Find.World?.info?.persistentRandomValue ?? 0));
            double roll = Rand.Value; Rand.PopState();
            var stance = DlcMath.PickStance(w, roll);
            SetStance(ideo, theme, stance);
            return stance;
        }

        public static void SetStance(Ideo ideo, MoneyTheme theme, MoneyStance stance)
        {
            var target = IdeoStances.PreceptFor(theme, stance); if (target == null) return;
            var issue = target.issue;
            var cur = ideo.PreceptsListForReading.FirstOrDefault(p => p.def.issue == issue);
            if (cur != null && cur.def == target) return;
            if (cur != null) ideo.RemovePrecept(cur, replacing: true);
            ideo.AddPrecept(PreceptMaker.MakePrecept(target), init: true);
        }
    }

    // ---------------------------------------------------------------------------------------------------------------- API
    public enum BuyResult { Ok, NoDef, NoIdeoOrDlc, StanceForbids, CapReached, NoEffect, CannotPay, NoOffice }

    public static class IndulgenceApi
    {
        public static IEnumerable<IndulgenceDef> AllDefs => DefDatabase<IndulgenceDef>.AllDefs.OrderBy(d => d.theme).ThenBy(d => d.ListPrice);

        public static string TempleAccount(Ideo ideo) => "house:temple:" + (ideo?.id ?? 0);
        public static string DeityAccount(Ideo ideo) => "faith:divine:" + (ideo?.id ?? 0);
        public static string FactionAccount(Faction f) => "faith:faction:" + (f?.loadID ?? 0);
        public static Money TempleBalance(Ideo ideo) => Ledger.Current?.Balance(TempleAccount(ideo)) ?? Money.Zero;

        static int Scaled(int cap) => cap <= 0 ? 0 : Math.Max(1, (int)Math.Round(cap * HSettings.Get("dlc.indulgenceCapScale")));

        public static int RepeatsThisYear(Pawn p, IndulgenceDef d)
            => DlcState.Current?.Count(x => x.Pawn == p && x.Def == d.defName, Find.TickManager.TicksGame, DlcMath.TicksPerYear) ?? 0;

        /// <summary>Price in silver for this pawn right now: list price × factor × stance × repeat surcharge (8.16).</summary>
        public static Money PriceFor(Pawn buyer, IndulgenceDef d)
        {
            var stance = IdeoStances.For(buyer, d);
            double silver = DlcMath.IndulgencePrice(d.ListPrice, HSettings.Get("dlc.indulgenceFactor"), stance, RepeatsThisYear(buyer, d));
            return Money.FromSilver(silver);
        }

        public static bool CanBuy(Pawn buyer, IndulgenceDef d, out string reason, bool ignoreCaps = false)
        {
            reason = null;
            if (d == null) { reason = "no item"; return false; }
            if (!DlcDefs.Ideology || buyer?.Ideo == null || buyer.ideo == null) { reason = "HH.Dlc.Reason.NoIdeo".TranslateOr("needs an ideoligion (Ideology DLC)"); return false; }
            var stance = IdeoStances.For(buyer, d);
            if (!IdeoStances.CanBuy(stance))
            {
                reason = stance == MoneyStance.Abhorrent ? "HH.Dlc.Reason.Abhorrent".TranslateOr("their faith abhors this")
                       : "HH.Dlc.Reason.NotAThing".TranslateOr("their faith has no such thing");
                return false;
            }
            if (d.fluidOnly && (buyer.Ideo.Fluid == false)) { reason = "HH.Dlc.Reason.NotFluid".TranslateOr("only fluid ideoligions can reform"); return false; }
            var st = DlcState.Current; if (st == null) { reason = "no game"; return false; }
            if (d.minRank > 0 && st.RankOf(buyer) < d.minRank) { reason = "HH.Dlc.Reason.NeedRank".TranslateOr("needs rank {0}").Fmt(d.minRank); return false; }
            if (!ignoreCaps && CapReached(buyer, d, out reason)) return false;
            if (!d.Worker.CanApply(buyer, out reason)) return false;
            return true;
        }

        public static bool CapReached(Pawn buyer, IndulgenceDef d, out string reason)
        {
            reason = null; var st = DlcState.Current; int now = Find.TickManager.TicksGame;
            if (st == null) return false;
            int id = buyer.Ideo?.id ?? -1;
            if (d.capPawnQuadrum > 0 && st.Count(x => x.Pawn == buyer && x.Def == d.defName, now, DlcMath.TicksPerQuadrum) >= Scaled(d.capPawnQuadrum)) { reason = "HH.Dlc.Reason.CapPawnQ".TranslateOr("limit reached for this quadrum"); return true; }
            if (d.capPawnYear > 0 && st.Count(x => x.Pawn == buyer && x.Def == d.defName, now, DlcMath.TicksPerYear) >= Scaled(d.capPawnYear)) { reason = "HH.Dlc.Reason.CapPawnY".TranslateOr("limit reached for this year"); return true; }
            if (d.capColonyQuadrum > 0 && buyer.Faction == Faction.OfPlayer && st.Count(x => x.Colony && x.Def == d.defName, now, DlcMath.TicksPerQuadrum) >= Scaled(d.capColonyQuadrum)) { reason = "HH.Dlc.Reason.CapColony".TranslateOr("the colony has bought this already this quadrum"); return true; }
            if (d.capIdeoQuadrum > 0 && st.Count(x => x.IdeoId == id && x.Def == d.defName, now, DlcMath.TicksPerQuadrum) >= Scaled(d.capIdeoQuadrum)) { reason = "HH.Dlc.Reason.CapIdeo".TranslateOr("the ideoligion has had enough of this for this quadrum"); return true; }
            return false;
        }

        /// <summary>Buy an item for a pawn: checks, pays (Payments, then colony silver for colonists), applies, books and records. Money never appears from nowhere.</summary>
        public static BuyResult TryBuy(Pawn buyer, IndulgenceDef d, out string reason, Thing office = null)
        {
            reason = null;
            if (d == null) return BuyResult.NoDef;
            if (!CanBuy(buyer, d, out reason))
            {
                if (!DlcDefs.Ideology || buyer?.Ideo == null) return BuyResult.NoIdeoOrDlc;
                var s = IdeoStances.For(buyer, d);
                if (!IdeoStances.CanBuy(s)) return BuyResult.StanceForbids;
                return CapReached(buyer, d, out _) ? BuyResult.CapReached : BuyResult.NoEffect;
            }
            var ideo = buyer.Ideo; var st = DlcState.Current; var map = buyer.MapHeld ?? office?.Map ?? Find.CurrentMap;
            var price = PriceFor(buyer, d);
            bool colonist = buyer.Faction == Faction.OfPlayer;
            string account = DestinationAccount(buyer, ideo, out Faction goodwillFaction);
            string memo = "Faith: " + d.label + " (" + buyer.LabelShort + ")";
            bool paid = Payments.TryCharge(buyer, price, account, memo, out _);
            if (!paid && colonist) paid = ColonySilver.TryTake(map, price, account, memo);
            if (!paid && !colonist && GuestApi.IsGuest(buyer) && GuestApi.Impl == null)
            {
                // fallback guests carry trade goods, not silver: they pay virtually like in GuestApi.ChargeGuest
                paid = GuestApi.Budget(buyer) >= price && (Ledger.Current?.Post("world", account, price, memo + " (visitor)") ?? false);
            }
            if (!paid) { reason = "HH.Dlc.Reason.CannotPay".TranslateOr("cannot pay {0}").Fmt(price.ToStringShort()); return BuyResult.CannotPay; }
            if (!colonist) GuestApi.Spend(buyer, price, "Faith");
            int now = Find.TickManager.TicksGame;
            d.Worker.Apply(buyer);
            st.Record(new Purchase { Pawn = buyer, Def = d.defName, Tick = now, IdeoId = ideo.id, Colony = colonist, PriceMicro = price.Micro });
            var ev = d.theme == MoneyTheme.Ascension || d.rank > 0 ? DlcDefs.EvBoughtAscension : DlcDefs.EvBoughtIndulgence;
            if (ev != null) Find.HistoryEventsManager.RecordEvent(new HistoryEvent(ev, buyer.Named(HistoryEventArgsNames.Doer)));
            if (goodwillFaction != null)
            {
                int g = DlcMath.FactionGoodwill(price.Silver, st.GoodwillGainedThisQuadrum(now));
                if (g > 0) { Faction.OfPlayer.TryAffectGoodwillWith(goodwillFaction, g, canSendMessage: false, canSendHostilityLetter: false); st.LogGoodwill(now, g); }
            }
            Messages.Message("HH.Dlc.Bought".TranslateOr("{0} bought {1} for {2} silver.").Fmt(buyer.LabelShort, d.label, price.ToStringShort()), buyer, MessageTypeDefOf.PositiveEvent, false);
            return BuyResult.Ok;
        }

        /// <summary>Where the money lands (8.16): colonists to the deity (or the faith's faction); guests to the temple treasury of the buyer's faith.</summary>
        public static string DestinationAccount(Pawn buyer, Ideo ideo, out Faction goodwillFaction)
        {
            goodwillFaction = null;
            if (buyer.Faction != Faction.OfPlayer) return TempleAccount(ideo);
            var dest = DlcState.Current?.DestinationFor(Faction.OfPlayer.ideos?.PrimaryIdeo ?? ideo) ?? FundDestination.ToDeity;
            if (dest == FundDestination.ToFaction)
            {
                var f = Find.FactionManager.AllFactionsVisible.FirstOrDefault(x => !x.IsPlayer && !x.HostileTo(Faction.OfPlayer) && x.ideos != null && x.ideos.Has(ideo));
                if (f != null) { goodwillFaction = f; return FactionAccount(f); }
            }
            return DeityAccount(ideo);
        }

        // ---- offices on a map (altars, ideograms, guest altars)
        static readonly Dictionary<int, (int tick, bool any)> officeCache = new Dictionary<int, (int, bool)>();
        public static bool OfficeOnMap(Map map)
        {
            if (map == null || !DlcDefs.Ideology) return false;
            int now = Find.TickManager.TicksGame;
            if (officeCache.TryGetValue(map.uniqueID, out var c) && now - c.tick < 250) return c.any;
            bool any = false;
            foreach (var b in map.listerBuildings.allBuildingsColonist) { if (b.TryGetComp<CompIndulgenceOffice>() != null) { any = true; break; } }
            officeCache[map.uniqueID] = (now, any);
            return any;
        }
    }

    /// <summary>Colony silver as a payment source for colonists (stockpiled silver, not carried): destroys the coins and books the money into the ledger.</summary>
    public static class ColonySilver
    {
        public static int Count(Map map)
        {
            if (map == null) return 0; int n = 0;
            foreach (var t in map.listerThings.ThingsOfDef(ThingDefOf.Silver)) if (!t.IsForbidden(Faction.OfPlayer)) n += t.stackCount;
            return n;
        }
        public static bool TryTake(Map map, Money amount, string toAccount, string memo)
        {
            if (map == null) return false;
            int coins = (int)Math.Ceiling(amount.Silver - 1e-9);
            if (Count(map) < coins) return false;
            int left = coins;
            foreach (var t in map.listerThings.ThingsOfDef(ThingDefOf.Silver).ToList())
            {
                if (left <= 0) break; if (t.IsForbidden(Faction.OfPlayer)) continue;
                int take = Math.Min(left, t.stackCount); t.SplitOff(take).Destroy(); left -= take;
            }
            return Ledger.Current?.Post("world", toAccount, Money.FromSilver(coins), memo + " (colony silver)") ?? false;
        }
    }

    // ---------------------------------------------------------------------------------------------------------------- office comp
    public class CompProperties_IndulgenceOffice : CompProperties { public CompProperties_IndulgenceOffice() { compClass = typeof(CompIndulgenceOffice); } }

    /// <summary>Added to altars, ideograms and the guest altar: a gizmo to buy indulgences and ascension for the selected colonists (11.4).</summary>
    public class CompIndulgenceOffice : ThingComp
    {
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!DlcDefs.Ideology || !Modules.On(ModuleId.CityLife) || parent.Faction != Faction.OfPlayer) yield break;
            yield return new Command_Action
            {
                defaultLabel = "HH.Dlc.Gizmo.Buy".TranslateOr("Indulgences"),
                defaultDesc = "HH.Dlc.Gizmo.BuyDesc".TranslateOr("Buy absolution, mercy or rank for a colonist. Prices follow their ideoligion's stance. The silver comes from colony stock or their pocket."),
                icon = ContentFinder<Texture2D>.Get("UI/HH/Dlc/Issues/Indulgences", false) ?? BaseContent.BadTex,
                action = OpenPawnMenu
            };
            var primary = Faction.OfPlayer.ideos?.PrimaryIdeo;
            if (primary != null)
            {
                var st = DlcState.Current; var cur = st?.DestinationFor(primary) ?? FundDestination.ToDeity;
                yield return new Command_Action
                {
                    defaultLabel = (cur == FundDestination.ToDeity ? "HH.Dlc.Gizmo.ToDeity".TranslateOr("Offerings: to the deity") : "HH.Dlc.Gizmo.ToFaction".TranslateOr("Offerings: to the faith's faction")),
                    defaultDesc = "HH.Dlc.Gizmo.FundDesc".TranslateOr("Where your colonists' payments go. The deity keeps them for good; a faction of the same faith warms to you (+1 goodwill per 500 silver, at most +5 per quadrum)."),
                    icon = ContentFinder<Texture2D>.Get("UI/HH/Dlc/Issues/PaidAscension", false) ?? BaseContent.BadTex,
                    action = () => st?.SetDestination(primary, cur == FundDestination.ToDeity ? FundDestination.ToFaction : FundDestination.ToDeity)
                };
            }
        }

        void OpenPawnMenu()
        {
            var map = parent.Map; if (map == null) return;
            var opts = new List<FloatMenuOption>();
            var pawns = map.mapPawns.FreeColonistsSpawned.Where(p => p.Ideo != null).ToList();
            foreach (var sel in Find.Selector.SelectedPawns) if (pawns.Remove(sel)) pawns.Insert(0, sel);
            foreach (var p in pawns)
            {
                var pawn = p;
                opts.Add(new FloatMenuOption(pawn.LabelShortCap + " (" + pawn.Ideo.name + ")", () => OpenItemMenu(pawn)));
            }
            if (opts.Count == 0) opts.Add(new FloatMenuOption("HH.Dlc.NoBuyers".TranslateOr("no colonist with an ideoligion"), null));
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        void OpenItemMenu(Pawn pawn)
        {
            var opts = new List<FloatMenuOption>();
            foreach (var d in IndulgenceApi.AllDefs)
            {
                var def = d; bool ok = IndulgenceApi.CanBuy(pawn, def, out string why);
                var stance = IdeoStances.For(pawn, def);
                if (!ok && (stance == MoneyStance.NotAThing || stance == MoneyStance.Abhorrent)) continue; // not offered at all
                string label = def.LabelCap + " - " + IndulgenceApi.PriceFor(pawn, def).ToStringShort() + " silver" + (ok ? "" : " (" + why + ")");
                opts.Add(new FloatMenuOption(label, ok ? (Action)(() =>
                {
                    var r = IndulgenceApi.TryBuy(pawn, def, out string rs, parent);
                    if (r != BuyResult.Ok) Messages.Message(rs ?? r.ToString(), pawn, MessageTypeDefOf.RejectInput, false);
                }) : null));
            }
            if (opts.Count == 0) opts.Add(new FloatMenuOption("HH.Dlc.NothingOffered".TranslateOr("their faith offers nothing for sale"), null));
            Find.WindowStack.Add(new FloatMenu(opts));
        }

        public override string CompInspectStringExtra()
        {
            var ideo = (parent as Building_GuestAltar)?.Ideo;
            return ideo == null ? null : "HH.Dlc.TempleFund".TranslateOr("Temple fund: {0} silver").Fmt(IndulgenceApi.TempleBalance(ideo).ToStringShort());
        }
    }

    // ---------------------------------------------------------------------------------------------------------------- thoughts
    public class ThoughtWorker_Precept_FaithForSale : ThoughtWorker_Precept
    {
        protected override ThoughtState ShouldHaveThought(Pawn p) => p.MapHeld != null && p.Faction == Faction.OfPlayer && IndulgenceApi.OfficeOnMap(p.MapHeld) ? ThoughtState.ActiveAtStage(0) : ThoughtState.Inactive;
    }

    public class ThoughtWorker_Precept_StingySoul : ThoughtWorker_Precept
    {
        protected override ThoughtState ShouldHaveThought(Pawn p)
        {
            if (p.MapHeld == null || p.Faction != Faction.OfPlayer || !IndulgenceApi.OfficeOnMap(p.MapHeld)) return ThoughtState.Inactive;
            var st = DlcState.Current; if (st == null) return ThoughtState.Inactive;
            return Find.TickManager.TicksGame - st.LastPurchaseTick(p) >= DlcMath.TicksPerQuadrum ? ThoughtState.ActiveAtStage(0) : ThoughtState.Inactive;
        }
    }

    public class ThoughtWorker_Precept_AscensionMood : ThoughtWorker_Precept
    {
        protected override ThoughtState ShouldHaveThought(Pawn p)
        {
            int r = DlcState.Current?.RankOf(p) ?? 0;
            return r > 0 ? ThoughtState.ActiveAtStage(Mathf.Clamp(r, 1, 4) - 1) : ThoughtState.Inactive;
        }
    }

    public class ThoughtWorker_Precept_AscensionDisapproval : ThoughtWorker_Precept_Social
    {
        protected override ThoughtState ShouldHaveThought(Pawn p, Pawn other)
            => other.Ideo == p.Ideo && (DlcState.Current?.RankOf(other) ?? 0) > 0 ? ThoughtState.ActiveAtStage(0) : ThoughtState.Inactive;
    }

    public class ThoughtWorker_Precept_AscensionRespect : ThoughtWorker_Precept_Social
    {
        protected override ThoughtState ShouldHaveThought(Pawn p, Pawn other)
        {
            var st = DlcState.Current; if (st == null || other.Ideo != p.Ideo) return ThoughtState.Inactive;
            return st.RankOf(other) > st.RankOf(p) ? ThoughtState.ActiveAtStage(0) : ThoughtState.Inactive;
        }
    }

    // ---------------------------------------------------------------------------------------------------------------- patches
    /// <summary>Sacred paid ascension: ranked pawns gain certainty 5 % faster (11.4).</summary>
    [HarmonyPatch(typeof(Pawn_IdeoTracker), nameof(Pawn_IdeoTracker.OffsetCertainty))]
    public static class Patch_AscensionCertainty
    {
        static readonly AccessTools.FieldRef<Pawn_IdeoTracker, Pawn> PawnRef = AccessTools.FieldRefAccess<Pawn_IdeoTracker, Pawn>("pawn");
        static void Prefix(Pawn_IdeoTracker __instance, ref float offset)
        {
            if (offset <= 0f || !DlcDefs.Ideology || !Modules.On(ModuleId.CityLife)) return;
            var st = DlcState.Current; if (st == null || st.RankCount == 0) return;
            var p = PawnRef(__instance); if (p == null || st.RankOf(p) <= 0) return;
            if (IdeoStances.Of(p, MoneyTheme.Ascension) == MoneyStance.Sacred) offset *= 1.05f;
        }
    }
}
