// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Hearth.Underworld
{
    public class CompProperties_FenceStock : CompProperties { public CompProperties_FenceStock() { compClass = typeof(CompFenceStock); } }

    /// <summary>
    /// The fence's back counter (6.11): buys stolen goods from shady guests at 40 % of the market value and sells them on to shady buyers at 70 %.
    /// The goods sit inside the counter (a ThingOwner), the cash box is the ledger account house:fence:&lt;map&gt;.
    /// </summary>
    public class CompFenceStock : ThingComp, IThingHolder
    {
        ThingOwner<Thing> stock;
        List<Thing> metaThings; List<int> metaFactions;     // for saving: loot → faction it was stolen from
        Dictionary<Thing, int> from = new Dictionary<Thing, int>();
        public int NextDealTick;
        /// <summary>Last deal message of this counter (at most one per in-game hour).</summary>
        public int LastMessageTick = -999999;

        public CompFenceStock() { stock = new ThingOwner<Thing>(this); }
        public ThingOwner<Thing> Stock => stock;
        public Map Map => parent.Map;
        public string Account => Underworld.FenceAccount(parent.Map);

        public void GetChildHolders(List<IThingHolder> outChildren) { ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, stock); }
        public ThingOwner GetDirectlyHeldThings() => stock;
        public IThingHolder ParentHolder => parent.ParentHolder;

        public Money StockValue { get { double v = 0; foreach (var t in stock) v += t.MarketValue * t.stackCount; return Money.FromSilver(v); } }

        public Faction SourceFaction(Thing t) => from.TryGetValue(t, out int id) && id >= 0 ? Find.FactionManager.AllFactionsListForReading.FirstOrDefault(f => f.loadID == id) : null;

        public override string CompInspectStringExtra()
        {
            if (!parent.Spawned) return null;
            var line = FenceTrade.StockLine(this);
            var why = FenceTrade.CannotBuyReason(this);
            if (why != null) return line + "\n" + UwPay.T("Fence.CannotBuy", why);
            var low = FenceTrade.LowCashLine(this);
            return low == null ? line : line + "\n" + low;
        }

        /// <summary>Drops the whole stock next to the counter as ordinary items (the stolen origin of what left is forgotten). Returns the number of
        /// stacks put out; stacks that found no free cell stay in stock and keep their origin.</summary>
        public int TakeOut()
        {
            var map = parent.MapHeld; if (map == null || stock.Count == 0) return 0;
            return DropAll(parent.PositionHeld, map);
        }

        int DropAll(IntVec3 at, Map map)
        {
            var before = new List<Thing>(stock);
            stock.TryDropAll(at, map, ThingPlaceMode.Near);
            return ForgetGone(before);
        }

        /// <summary>Forgets the origin of each of <paramref name="before"/> that is no longer in stock. Returns how many left.</summary>
        internal int ForgetGone(List<Thing> before)
        {
            int n = 0;
            foreach (var t in before) if (!stock.Contains(t)) { from.Remove(t); n++; }
            return n;
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            // deconstructed or destroyed: the goods are not lost with the counter
            if (mode != DestroyMode.Vanish && previousMap != null && stock.Count > 0) DropAll(parent.Position, previousMap);
        }

        /// <summary>The message after Take goods out: how many stacks, and whether some found no room.</summary>
        public static string TookOutText(int put, int had)
        {
            if (put < had) return UwPay.T("Fence.TookOut.NoRoom", had - put, had);
            return put == 1 ? UwPay.T("Fence.TookOut.One") : UwPay.T("Fence.TookOut", put);
        }

        /// <summary>A deal message, at most one per in-game hour for this counter.</summary>
        public void Say(string text, MessageTypeDef type)
        {
            int now = Find.TickManager.TicksGame;
            if (now - LastMessageTick < GenDate.TicksPerHour && now >= LastMessageTick) return;
            LastMessageTick = now;
            Messages.Message(text, parent, type, false);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (parent.Faction != Faction.OfPlayer) yield break;
            yield return new Command_Action
            {
                defaultLabel = UwPay.T("Fence.Fund"), defaultDesc = UwPay.T("Fence.Fund.Desc"), icon = TexCommand.Attack,
                action = () => { var n = FenceTrade.TopUp(this, Money.FromSilver(HSettings.Get("underworld.fence.capital"))); Messages.Message(UwPay.T("Fence.Funded", n.ToStringShort()), parent, MessageTypeDefOf.NeutralEvent, false); }
            };
            var take = new Command_Action
            {
                defaultLabel = UwPay.T("Fence.TakeOut"), defaultDesc = UwPay.T("Fence.TakeOut.Desc"), icon = OfferIcons.TakeOut,
                action = () =>
                {
                    int had = stock.Count, n = TakeOut();
                    Messages.Message(TookOutText(n, had), parent, n < had ? MessageTypeDefOf.RejectInput : MessageTypeDefOf.NeutralEvent, false);
                }
            };
            if (stock.Count == 0) take.Disable(UwPay.T("Fence.TakeOut.Empty"));
            yield return take;
            if (Prefs.DevMode)
                yield return new Command_Action { defaultLabel = "DEV: spawn loot", action = () => { var t = UwPay.MakeGoods(30, 300); if (t != null) FenceTrade.Stock(this, t, null); } };
        }

        public override void PostExposeData()
        {
            Scribe_Deep.Look(ref stock, "stock", this);
            Scribe_Values.Look(ref NextDealTick, "nextDeal");
            Scribe_Values.Look(ref LastMessageTick, "lastMessage", -999999);
            if (Scribe.mode == LoadSaveMode.Saving) { metaThings = new List<Thing>(from.Keys.Where(t => stock.Contains(t))); metaFactions = metaThings.Select(t => from[t]).ToList(); }
            Scribe_Collections.Look(ref metaThings, "metaThings", LookMode.Reference);
            Scribe_Collections.Look(ref metaFactions, "metaFactions", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (stock == null) stock = new ThingOwner<Thing>(this);
                from.Clear();
                if (metaThings != null && metaFactions != null) for (int i = 0; i < Math.Min(metaThings.Count, metaFactions.Count); i++) if (metaThings[i] != null) from[metaThings[i]] = metaFactions[i];
            }
        }
        public void NoteFrom(Thing t, Faction f) { from[t] = f?.loadID ?? -1; }
        public void ForgetFrom(Thing t) { from.Remove(t); }
    }

    public static class FenceTrade
    {
        public static float BuyPct => HSettings.Get("underworld.fence.buyPercent");
        public static float SellPct => HSettings.Get("underworld.fence.sellPercent");

        public static Money BuyPrice(Thing t) => new Money(UnderworldMath.FenceBuyPrice(Money.FromSilver(t.MarketValue * t.stackCount).Micro, BuyPct));
        public static Money SellPrice(Thing t) => new Money(UnderworldMath.FenceSellPrice(Money.FromSilver(t.MarketValue * t.stackCount).Micro, SellPct));

        /// <summary>Moves money from the till into the fence's cash box up to the target. Returns what was moved.</summary>
        /// <summary>"Stock: 3 items worth 210 silver. Cash box: 600 silver."</summary>
        public static string StockLine(CompFenceStock fs) => UwPay.T("Fence.Stock", fs.Stock.Count, fs.StockValue.ToStringShort(), UwPay.Balance(fs.Account).ToStringShort());

        /// <summary>Why the fence cannot buy anything right now (translated), or null when it can.</summary>
        public static string CannotBuyReason(CompFenceStock fs)
        {
            if (!Underworld.Unlocked) return UwPay.T("Fence.Why.Closed");
            if (OnHand(fs) < Money.FromSilver(1)) return UwPay.T("Fence.Why.TillEmpty");
            return null;
        }

        /// <summary>Market value of the dearest loot gangs carry (<see cref="Loot.Equip"/>).</summary>
        public const float LootMaxValue = 350f;

        /// <summary>Silver the fence can pay with right now: its cash box plus the till.</summary>
        public static Money OnHand(CompFenceStock fs) => UwPay.Balance(fs.Account) + UwPay.Balance(Underworld.Till(fs.Map));

        /// <summary>"Only 30 silver on hand …" when the fence cannot pay for the dearer loot gangs bring (translated), or null.</summary>
        public static string LowCashLine(CompFenceStock fs)
        {
            if (!Underworld.Unlocked) return null;
            var have = OnHand(fs);
            var dear = new Money(UnderworldMath.FenceBuyPrice(Money.FromSilver(LootMaxValue).Micro, BuyPct));
            return have < dear ? UwPay.T("Fence.LowCash", have.ToStringShort(), dear.ToStringShort()) : null;
        }

        public static Money TopUp(CompFenceStock fs, Money target)
        {
            var l = Ledger.Current; var have = l.Balance(fs.Account);
            if (have >= target) return Money.Zero;
            var till = l.Balance(Underworld.Till(fs.Map));
            var move = Money.Min(target - have, till);
            if (move.Micro <= 0) return Money.Zero;
            l.Post(Underworld.Till(fs.Map), fs.Account, move, "Fence: cash box top-up");
            return move;
        }

        static void Surplus(CompFenceStock fs)
        {
            var cap = Money.FromSilver(HSettings.Get("underworld.fence.capital"));
            var bal = Ledger.Current.Balance(fs.Account);
            if (bal > cap) Ledger.Current.Post(fs.Account, Underworld.Till(fs.Map), bal - cap, "Fence: surplus to the till");
        }

        public static void Stock(CompFenceStock fs, Thing t, Faction from)
        {
            if (t.Spawned) t.DeSpawn();
            if (fs.Stock.TryAdd(t, false)) fs.NoteFrom(t, from);
        }

        /// <summary>The colony buys a stolen item from a seller. Pays from the cash box (topped up from the till), items go into stock. Whispers +1.</summary>
        public static bool BuyFrom(CompFenceStock fs, Pawn seller, Thing item, out string why)
        {
            why = null;
            if (!Underworld.Unlocked) { why = "closed"; return false; }
            if (item == null || item.Destroyed || item.stackCount <= 0) { why = "nothing to buy"; return false; }
            var price = BuyPrice(item);
            if (Ledger.Current.Balance(fs.Account) < price) TopUp(fs, price);
            if (Ledger.Current.Balance(fs.Account) < price)
            {
                var have = Ledger.Current.Balance(fs.Account);   // after the top-up the till is empty: all there is sits in the cash box
                why = have < Money.FromSilver(1) ? UwPay.T("Fence.Why.TillEmpty") : UwPay.T("Fence.Why.TooLittle", have.ToStringShort(), price.ToStringShort());
                fs.Say(UwPay.T("Fence.BuyFailed", item.LabelCap, why), MessageTypeDefOf.NegativeEvent);
                return false;
            }
            Faction src = null; Underworld.Comp.IsStolen(item, out src);
            if (!Ledger.Current.Post(fs.Account, "world", price, "Fence: bought loot")) { why = "ledger"; return false; }
            UwPay.GiveSilver(seller, price.WholeSilver);
            // the fraction below one coin stays with the fence: book it back
            var frac = price.Fraction; if (frac.Micro > 0) Ledger.Current.Post("world", fs.Account, frac, "Fence: change");
            var taken = item.holdingOwner != null ? item.holdingOwner.Take(item, item.stackCount) : item;
            Underworld.Comp.ForgetStolen(item);
            if (fs.Stock.TryAdd(taken, false)) fs.NoteFrom(taken, src);
            else taken.Destroy();
            Underworld.Comp.DealsBought++; Underworld.Comp.NoteEvent(FacilityStatus.FenceId);
            Underworld.AddWhispers(HSettings.Get("underworld.whispers.fence"), "fence deal (bought)");
            UwActivity.Note(fs.Map);
            fs.Say(UwPay.T("Fence.Bought", taken.LabelCap, price.ToStringShort()), MessageTypeDefOf.NeutralEvent);
            return true;
        }

        /// <summary>A shady buyer takes an item out of stock at 70 %. The cash goes into the cash box; surplus to the till. The owner may find out.</summary>
        public static bool SellTo(CompFenceStock fs, Pawn buyer, Thing item, out string why)
        {
            why = null;
            if (!Underworld.Unlocked) { why = "closed"; return false; }
            if (!fs.Stock.Contains(item)) { why = "not in stock"; return false; }
            var price = SellPrice(item);
            if (!UwPay.Charge(buyer, price, fs.Account, "underworld.fence", "Fence: sold goods")) { why = UwPay.T("Fence.CannotPay"); return false; }
            var src = fs.SourceFaction(item);
            var cost = BuyPrice(item);
            var thing = fs.Stock.Take(item, item.stackCount); fs.ForgetFrom(item);
            if (!buyer.inventory.innerContainer.TryAdd(thing)) GenPlace.TryPlaceThing(thing, buyer.Position, buyer.Map, ThingPlaceMode.Near);
            Underworld.Comp.DealsSold++; Underworld.Comp.FenceProfitMicro += price.Micro - cost.Micro; Underworld.Comp.NoteEvent(FacilityStatus.FenceId);
            fs.Say(UwPay.T("Fence.Sold", thing.LabelCap, price.ToStringShort()), MessageTypeDefOf.NeutralEvent);
            Underworld.AddWhispers(HSettings.Get("underworld.whispers.fence"), "fence deal (sold)");
            UwActivity.Note(fs.Map);
            Surplus(fs);
            if (src != null) MaybeExposed(fs.Map, src);
            return true;
        }

        public static bool MaybeExposed(Map map, Faction owner, bool force = false)
        {
            float p = HSettings.Get("underworld.fence.exposureChance") * (1f + Underworld.Whispers / 100f);
            if (!force && !JRand.Stream("underworld.fenceExposure").Chance(p)) return false;
            int hit = HSettings.GetInt("underworld.fence.goodwillHit");
            Underworld.AffectGoodwill(owner, -hit, "fence: stolen goods found");
            Scandals.Raise(ScandalKind.LeakedLedger, map, force: force, detail: UwPay.T("Fence.Exposed", owner.Name));
            if (Underworld.Tier >= 5f && owner.HostileTo(Faction.OfPlayer) && map != null)
            {
                var parms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatBig, map);
                parms.faction = owner; parms.points *= 0.6f;
                if (IncidentDefOf.RaidEnemy.Worker.CanFireNow(parms)) IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
            }
            return true;
        }

        public static CompFenceStock FindCounter(Map map)
        {
            if (map == null || UwDefs.FenceCounter == null) return null;
            foreach (var t in map.listerThings.ThingsOfDef(UwDefs.FenceCounter))
                if (t is ThingWithComps tw && tw.Faction == Faction.OfPlayer && tw.GetComp<CompFenceStock>() is CompFenceStock c) return c;
            return null;
        }
    }

    /// <summary>Loot carried by outlaws: items marked as stolen from some faction. Generated when a gang arrives.</summary>
    public static class Loot
    {
        public static void Equip(Pawn p, Faction from)
        {
            int n = JRand.Stream("underworld.loot").Range(0, 3);
            for (int i = 0; i < n; i++)
            {
                var t = UwPay.MakeGoods(25, FenceTrade.LootMaxValue); if (t == null) continue;
                if (p.inventory.innerContainer.TryAdd(t, false)) Underworld.Comp.MarkStolen(t, from); else t.Destroy();
            }
        }
        public static Thing PickStolen(Pawn p)
        {
            foreach (var t in p.inventory.innerContainer) if (Underworld.Comp.IsStolen(t, out _)) return t;
            return null;
        }
    }

    public static partial class Activities
    {
        /// <summary>Shady guests sell their loot to the fence and buy goods from its stock.</summary>
        public sealed class FenceActivity : IGuestActivity
        {
            public string Id => "underworld.fence";
            public ModuleId Module => ModuleId.Underworld;
            public float Desire(Pawn guest, GuestInfo info)
            {
                if (!Underworld.Unlocked || UwDefs.JobFenceDeal == null || !Underworld.IsShady(guest) || Underworld.IsMinor(guest)) return 0f;
                var fs = FenceTrade.FindCounter(guest.Map); if (fs == null || Find.TickManager.TicksGame < fs.NextDealTick) return 0f;
                if (Loot.PickStolen(guest) != null) return 2.5f;
                return fs.Stock.Count > 0 ? 1.2f : 0f;
            }
            public Job TryGiveJob(Pawn guest, GuestInfo info)
            {
                var fs = FenceTrade.FindCounter(guest.Map); if (fs == null) return null;
                if (!guest.CanReserveAndReach(fs.parent, PathEndMode.InteractionCell, Danger.Some)) return null;
                fs.NextDealTick = Find.TickManager.TicksGame + 600;
                return JobMaker.MakeJob(UwDefs.JobFenceDeal, fs.parent);
            }
        }
    }

    public class JobDriver_FenceDeal : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);
            yield return Toils_General.Wait(240).WithProgressBarToilDelay(TargetIndex.A);
            yield return new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Instant,
                initAction = () =>
                {
                    var fs = (job.targetA.Thing as ThingWithComps)?.GetComp<CompFenceStock>(); if (fs == null) return;
                    var loot = Loot.PickStolen(pawn);
                    if (loot != null) { FenceTrade.BuyFrom(fs, pawn, loot, out _); return; }
                    var item = fs.Stock.InRandomOrder().FirstOrDefault(t => UwPay.Budget(pawn) >= FenceTrade.SellPrice(t));
                    if (item != null) FenceTrade.SellTo(fs, pawn, item, out _);
                }
            };
        }
    }

}
