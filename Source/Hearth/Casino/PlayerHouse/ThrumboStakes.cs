// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Silverline;
using Verse;
using Hearth.CasinoMath;

namespace Hearth.Casino.PlayerHouse
{
    /// <summary>
    /// The pawnbroker of The Lucky Thrumbo: a virtual trader that only exists while a stake is valued or lost (KONZEPT 6.13, K15/K16).
    /// It is an ordinary <see cref="ITrader"/>, so vanilla builds the <see cref="Tradeable"/> list (things in range of a powered orbital
    /// trade beacon, sellable colony pawns) and prices it with its own sell-price functions and the negotiator's skill. It never trades
    /// in the normal trade window and it has no faction (like most orbital traders): what it takes leaves the map for good.
    /// </summary>
    public sealed class ThrumboPawnbroker : ITrader
    {
        public const string DefName = "HH_Pawnbroker";
        public readonly Map Map;
        public ThrumboPawnbroker(Map map) { Map = map; }

        static TraderKindDef kind;
        public static TraderKindDef Kind
        {
            get
            {
                if (kind != null) return kind;
                kind = DefDatabase<TraderKindDef>.GetNamedSilentFail(DefName);
                if (kind == null)
                {
                    // the sell price type is always Normal (vanilla TraderKindDef.PriceTypeFor), so any trader kind values the same
                    kind = DefDatabase<TraderKindDef>.AllDefsListForReading.FirstOrDefault(d => d.orbital) ?? DefDatabase<TraderKindDef>.AllDefsListForReading.FirstOrDefault();
                    HLog.Once("thrumbo.pawnbroker", LogLevel.Warn, "Casino", "TraderKindDef " + DefName + " is missing; valuing stakes with " + (kind?.defName ?? "nothing"));
                }
                return kind;
            }
        }

        public TraderKindDef TraderKind => Kind;
        public IEnumerable<Thing> Goods { get { yield break; } }
        public int RandomPriceFactorSeed => 0;
        public string TraderName => "HH.Thrumbo.Stakes.Pawnbroker".Translate();
        public bool CanTradeNow => true;
        public float TradePriceImprovementOffsetForPlayer => 0f;
        public Faction Faction => null;
        public TradeCurrency TradeCurrency => TradeCurrency.Silver;

        /// <summary>What vanilla orbital trade offers (beacon range + sellable colony pawns), narrowed by <see cref="ThrumboStakes.Allowed"/>.</summary>
        public IEnumerable<Thing> ColonyThingsWillingToBuy(Pawn playerNegotiator)
        {
            if (Map == null) yield break;
            foreach (var t in TradeUtility.AllLaunchableThingsForTrade(Map, this))
                if (ThrumboStakes.Allowed(t)) yield return t;
            foreach (var p in TradeUtility.AllSellableColonyPawns(Map, false))
                if (ThrumboStakes.Allowed(p)) yield return p;
        }

        /// <summary>A lost stake: vanilla's sale steps (PreTraded: thoughts, relations, goodwill, tale), then the thing leaves with the house.</summary>
        public void GiveSoldThingToTrader(Thing toGive, int countToGive, Pawn playerNegotiator)
        {
            var thing = toGive.SplitOff(countToGive);
            thing.PreTraded(TradeAction.PlayerSells, playerNegotiator, this);
            ThrumboStakes.NoteLost(thing, countToGive);
            if (thing is Pawn pawn)
            {
                if (pawn.Spawned) pawn.DeSpawn();
                // like the pawns aboard a departing orbital trader: the world decides whether it keeps them
                if (!pawn.Destroyed && !pawn.IsWorldPawn()) Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.Decide);
            }
            else if (!thing.Destroyed) thing.Destroy(DestroyMode.Vanish);
        }

        public void GiveSoldThingToPlayer(Thing toGive, int countToGive, Pawn playerNegotiator)
            => HLog.Warn("Casino", "The Lucky Thrumbo's pawnbroker does not sell anything (" + toGive + ")");
    }

    public enum StakeKind { Goods = 0, Animals = 1, Prisoners = 2 }

    /// <summary>One row of the stake dialog: a vanilla <see cref="Tradeable"/>, its vanilla sell price and how many of it are staked.</summary>
    public sealed class StakeLine
    {
        public Tradeable Tradeable;
        public StakeKind Kind;
        /// <summary>Vanilla sell price of one unit to the pawnbroker (Tradeable.GetPriceFor(PlayerSells)).</summary>
        public float UnitPrice;
        public int Count;
        public string Buffer;
        public Thing Any => Tradeable?.AnyThing;
        public int Held => Tradeable?.CountHeldBy(Transactor.Colony) ?? 0;
        public string Label => Tradeable?.LabelCap ?? "?";
    }

    /// <summary>Everything that could be staked right now, valued with the best negotiator on the map.</summary>
    public sealed class StakeOffer
    {
        public Map Map;
        public Pawn Negotiator;
        public bool Beacon;
        public ThrumboPawnbroker Broker;
        public readonly List<StakeLine> Lines = new List<StakeLine>();
        public IEnumerable<StakeLine> Chosen => Lines.Where(l => l.Count > 0);
        public bool Contains(Thing t) => Lines.Any(l => l.Tradeable.thingsColony.Contains(t));
    }

    /// <summary>Outcome of one "raise the stakes" round.</summary>
    public sealed class StakeResult
    {
        public bool Ok;
        public string Error;
        public bool Kept;
        public Money Value, Payout;
        public double Mult;
        public string Text;
        public int[] Reels;
        public int Pocket = -1;
        public readonly List<KeyValuePair<Thing, int>> Lost = new List<KeyValuePair<Thing, int>>();
        public static StakeResult Fail(string e) => new StakeResult { Ok = false, Error = e };
    }

    /// <summary>
    /// "Raise the stakes…" of The Lucky Thrumbo (KONZEPT 6.13 "Alles verspielen", K15; fees K16). Goods in range of a powered orbital trade beacon,
    /// and from the content intensity Standard also colony animals and prisoners, are valued at what a pawnbroker pays for them (vanilla sell
    /// price with the best negotiator, MarketValue only read) minus the pawnbroker's spread and a 2 % fee, then one sealed round of slots or
    /// roulette decides. Lost: the things leave like a sale. Won: everything stays and the house pays value × (payout − 1) in silver.
    /// The table limit of the Thrumbo applies to the value.
    /// </summary>
    public static class ThrumboStakes
    {
        /// <summary>Fixed fee on top of the pawnbroker's spread (KONZEPT 6.13 table "Bewertung beim Verspielen von Gütern").</summary>
        public const double Fee = 0.02;

        public static double Spread => Math.Max(0.0, Math.Min(0.9, (double)HSettings.Get(TS.PawnSpread)));
        /// <summary>Share of the vanilla sell price that counts as stake value.</summary>
        public static double Factor => Math.Max(0.0, 1.0 - Spread - Fee);

        static ContentIntensity Content => HearthProfile.Current?.Content ?? ContentIntensity.Standard;
        /// <summary>Prisoners and animals as stakes from the content intensity Standard on, like vanilla selling (KONZEPT 6.2 table).</summary>
        public static bool PawnsAllowed => Content >= ContentIntensity.Standard;

        /// <summary>The setting thrumbo.raiseTheStakes and the casino module. The Lucky Thrumbo itself has no research gate, so neither has this.</summary>
        public static bool Enabled => Modules.On(ModuleId.Casino) && HSettings.GetBool(TS.AllIn);

        // ============================================================================================================
        // Who and what may be staked
        // ============================================================================================================
        /// <summary>The one filter for every stake, checked when the list is built and again right before the round.</summary>
        public static bool Allowed(Thing t)
        {
            if (t == null || t.Destroyed) return false;
            if (t is Pawn p) return PawnAllowed(p);
            if (t.def == ThingDefOf.Silver) return false;                     // silver is staked at the normal tables
            var inner = t.GetInnerIfMinified();
            if (inner is Pawn) return false;
            if (t is Corpse c && c.InnerPawn != null && c.InnerPawn.RaceProps.Humanlike) return false; // never human remains
            return true;
        }

        public static StakeKind KindOf(Thing t) => t is Pawn p ? (p.RaceProps.Humanlike ? StakeKind.Prisoners : StakeKind.Animals) : StakeKind.Goods;

        public static bool PawnAllowed(Pawn p)
        {
            if (p == null || p.Dead || p.Destroyed || !PawnsAllowed) return false;
            if (GuestApi.IsGuest(p)) return false;                            // never guests
            if (p.RaceProps.Humanlike)
            {
                // Colonists are never a stake in this release (KONZEPT 6.13 allows them only at Dark/Grim with an ideology condition,
                // a warning, a 10-day buy-back and a rescue quest – none of that is built yet). Slaves are members of the colony and
                // count as colonists here. So: only secure prisoners of the colony, adults only, never quest lodgers.
                if (p.Faction == Faction.OfPlayer || p.IsColonist || p.IsSlave) return false;
                if (!p.IsPrisonerOfColony || p.guest == null || !p.guest.PrisonerIsSecure) return false;
                if (!p.DevelopmentalStage.Adult()) return false;                 // never children, not even as prisoners
                if (p.IsQuestLodger()) return false;
                return true;
            }
            if (p.RaceProps.Animal) return p.Faction == Faction.OfPlayer && p.HostFaction == null;   // like vanilla: downed or berserk animals can be sold too
            return false;                                                     // mechanoids, entities: no
        }

        /// <summary>The colonist on the map who gets the best prices (vanilla stat Trade price improvement).</summary>
        public static Pawn BestNegotiator(Map map)
        {
            Pawn best = null; float bestV = float.MinValue;
            if (map?.mapPawns == null) return null;
            foreach (var p in map.mapPawns.FreeColonistsSpawned)
            {
                if (p.Dead || p.Downed || !p.DevelopmentalStage.Adult()) continue;
                if (p.skills?.GetSkill(SkillDefOf.Social)?.TotallyDisabled ?? false) continue;
                float v = p.GetStatValue(StatDefOf.TradePriceImprovement);
                if (v > bestV) { bestV = v; best = p; }
            }
            return best;
        }

        // ============================================================================================================
        // Vanilla trade session, only for the moment of valuing or losing
        // ============================================================================================================
        static T WithSession<T>(ThrumboPawnbroker broker, Pawn negotiator, Func<T> f)
        {
            var oT = TradeSession.trader; var oN = TradeSession.playerNegotiator; var oG = TradeSession.giftMode; var oD = TradeSession.deal;
            try
            {
                TradeSession.trader = broker; TradeSession.playerNegotiator = negotiator; TradeSession.giftMode = false;
                return f();
            }
            finally { TradeSession.trader = oT; TradeSession.playerNegotiator = oN; TradeSession.giftMode = oG; TradeSession.deal = oD; }
        }

        /// <summary>Builds the list of possible stakes for <paramref name="map"/>. Never null; empty without a powered beacon (pawns need none, like vanilla orbital trade).</summary>
        public static StakeOffer Offer(Map map)
        {
            var o = new StakeOffer { Map = map };
            if (map == null || !Enabled) return o;
            o.Negotiator = BestNegotiator(map);
            o.Beacon = Building_OrbitalTradeBeacon.AllPowered(map).Any();
            if (o.Negotiator == null) return o;
            o.Broker = new ThrumboPawnbroker(map);
            WithSession(o.Broker, o.Negotiator, () =>
            {
                var deal = new TradeDeal();                                    // vanilla: Tradeable / Tradeable_Pawn grouping, sellable checks, InSellablePosition
                foreach (var tr in deal.AllTradeables)
                {
                    if (tr.IsCurrency || !tr.HasAnyThing) continue;
                    tr.thingsTrader.Clear();
                    tr.thingsColony.RemoveAll(x => !Allowed(x));
                    if (tr.thingsColony.Count == 0) continue;
                    float price = tr.GetPriceFor(TradeAction.PlayerSells);
                    if (price <= 0f) continue;
                    o.Lines.Add(new StakeLine { Tradeable = tr, Kind = KindOf(tr.AnyThing), UnitPrice = price, Count = 0, Buffer = "0" });
                }
                return 0;
            });
            o.Lines.Sort((a, b) => a.Kind != b.Kind ? a.Kind.CompareTo(b.Kind) : string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
            return o;
        }

        /// <summary>Value of one line: vanilla sell price × count × (1 − spread − fee).</summary>
        public static double LineValue(StakeLine l) => l == null || l.Count <= 0 ? 0 : l.UnitPrice * (double)l.Count * Factor;
        public static double RawValue(StakeOffer o) => o == null ? 0 : o.Chosen.Sum(l => l.UnitPrice * (double)l.Count);
        public static Money Value(StakeOffer o) => o == null ? Money.Zero : Money.FromSilver(o.Chosen.Sum(LineValue));

        /// <summary>Null when the chosen stake can be played, else a short reason. Also drops things that left the beacon range or the map since the list was built.</summary>
        public static string Validate(StakeOffer o, bool recheck = true)
        {
            if (!Modules.On(ModuleId.Casino) || Ledger.Current == null) return "HH.Thrumbo.Err.Closed".Translate();
            if (!HSettings.GetBool(TS.AllIn)) return "HH.Thrumbo.Stakes.Err.Off".Translate();
            if (o == null || o.Map == null) return "HH.Thrumbo.Err.NoMap".Translate();
            if (o.Negotiator == null || o.Negotiator.Dead) return "HH.Thrumbo.Stakes.Err.NoNegotiator".Translate();
            if (!o.Chosen.Any()) return "HH.Thrumbo.Stakes.Err.Nothing".Translate();
            if (recheck)
            {
                // the things must still be where vanilla trade would take them from (the window skips this scan while it draws)
                var now = new HashSet<Thing>(o.Broker.ColonyThingsWillingToBuy(o.Negotiator).Where(t => TradeUtility.PlayerSellableNow(t, o.Broker)));
                foreach (var l in o.Chosen)
                    l.Tradeable.thingsColony.RemoveAll(t => t == null || t.Destroyed || !now.Contains(t) || !Allowed(t));
            }
            foreach (var l in o.Chosen)
                if (l.Count > l.Held) return "HH.Thrumbo.Stakes.Err.Gone".Translate(l.Label);
            var value = Value(o);
            if (value < TS.MinStake) return "HH.Thrumbo.Err.Min".Translate(TS.MinStake.ToStringShort());
            var limit = ThrumboHouse.TableLimit(o.Map);
            if (value > limit) return ThrumboHouse.OverLimitText(limit);
            return null;
        }

        // ============================================================================================================
        // One round
        // ============================================================================================================
        public static StakeResult PlaySlots(StakeOffer o, SlotVolatility vol, PayFrom winTo)
            => Play(o, ThrumboGame.Slots, winTo, r =>
            {
                var pt = ThrumboOdds.Slots(vol, TS.Shift);
                double m = pt.Spin(JRand.Sealed("thrumbo.slots"), out int a, out int b, out int c);
                r.Reels = new[] { a, b, c };
                r.Text = ThrumboNames.Reels(a, b, c);
                return m;
            });

        public static StakeResult PlayRoulette(StakeOffer o, RouletteBet bet, PayFrom winTo)
        {
            if (bet == null) return StakeResult.Fail("HH.Thrumbo.Err.NoBet".Translate());
            return Play(o, ThrumboGame.Roulette, winTo, r =>
            {
                var rules = ThrumboOdds.Roulette(TS.Shift);
                int pocket = rules.Spin(JRand.Sealed("thrumbo.roulette"));
                r.Pocket = pocket;
                r.Text = "HH.Thrumbo.Ball".Translate(RouletteBet.PocketName(pocket), ("HH.Thrumbo.Color." + ThrumboHouse.PocketColor(pocket)).Translate()).ToString() + " · " + ThrumboNames.Bet(bet);
                return rules.Return(bet, pocket);
            });
        }

        static List<KeyValuePair<Thing, int>> lostSink;
        internal static void NoteLost(Thing t, int count) { lostSink?.Add(new KeyValuePair<Thing, int>(t, count)); }

        static StakeResult Play(StakeOffer o, ThrumboGame game, PayFrom winTo, Func<StakeResult, double> draw)
        {
            string err = Validate(o); if (err != null) return StakeResult.Fail(err);
            var value = Value(o);
            string what = Describe(o);
            var r = new StakeResult { Ok = true, Value = value };
            double mult = draw(r);                                            // sealed stream: a reload brings back the same draw
            r.Mult = mult;
            r.Kept = mult >= 1.0;
            // win: the things stay and the house adds value × (mult − 1); otherwise the things go and a partial prize (0 < mult < 1) is paid back
            r.Payout = r.Kept ? value * (mult - 1.0) : value * mult;
            bool failed = false;
            try
            {
                if (!r.Kept)
                {
                    try { Lose(o, r); }
                    catch (Exception e) { failed = true; HLog.Error("Casino", "Lucky Thrumbo stakes: a vanilla sale step threw while the stake was taken: " + e); }
                }
                if (r.Payout.Micro > 0) ThrumboHouse.PayStakeWin(winTo, o.Map, r.Payout, "Lucky Thrumbo: raise the stakes (" + game.ToString().ToLowerInvariant() + ")");
            }
            catch (Exception e) { failed = true; HLog.Error("Casino", "Lucky Thrumbo stakes: payout failed: " + e); }
            finally
            {
                // whatever happened, the round is on record and the buffers are reset, so there is never a half round
                string outcome = r.Kept
                    ? (mult > 1.0 ? "HH.Thrumbo.Stakes.Won".Translate(mult.ToString("0.##"), r.Payout.ToStringShort()).ToString() : "HH.Thrumbo.Stakes.Push".Translate().ToString())
                    : (r.Payout.Micro > 0 ? "HH.Thrumbo.Stakes.LostPart".Translate(r.Payout.ToStringShort()).ToString() : "HH.Thrumbo.Stakes.Lost".Translate().ToString());
                r.Text = r.Text + " — " + outcome;
                ThrumboComponent.Get()?.Record(game, PayFrom.Goods, value, r.Kept ? value + r.Payout : r.Payout, "HH.Thrumbo.Stakes.HistoryLine".Translate(what, r.Text).ToString());
                foreach (var l in o.Lines) { l.Count = 0; l.Buffer = "0"; }
            }
            if (failed) r.Text += " (!)";
            return r;
        }

        static void Lose(StakeOffer o, StakeResult r)
        {
            lostSink = r.Lost;
            try
            {
                WithSession(o.Broker, o.Negotiator, () =>
                {
                    bool organ = false;
                    foreach (var l in o.Chosen.ToList())
                    {
                        if (l.Tradeable?.ThingDef?.IsNaturalOrgan ?? false) organ = true;
                        l.Tradeable.ForceToDestination(l.Count);
                        l.Tradeable.ResolveTrade();                           // vanilla: TransferNoSplit → GiveSoldThingToTrader (Tradeable_Pawn: sold-slave event)
                        l.Tradeable.ForceTo(0);
                    }
                    if (organ && ModsConfig.IdeologyActive)
                    {
                        Find.HistoryEventsManager.RecordEvent(new HistoryEvent(HistoryEventDefOf.TradedOrgan, o.Negotiator.Named(HistoryEventArgsNames.Doer)));
                        Find.HistoryEventsManager.RecordEvent(new HistoryEvent(HistoryEventDefOf.SoldOrgan, o.Negotiator.Named(HistoryEventArgsNames.Doer)));
                    }
                    return 0;
                });
            }
            finally { lostSink = null; }
        }

        /// <summary>"50 steel, muffalo, …" for the history line.</summary>
        public static string Describe(StakeOffer o)
        {
            var parts = new List<string>();
            foreach (var l in o.Chosen)
            {
                parts.Add(l.Count > 1 ? l.Count + "× " + l.Label : l.Label);
                if (parts.Count == 4) break;
            }
            int more = o.Chosen.Count() - parts.Count;
            string s = string.Join(", ", parts);
            if (more > 0) s += " " + "HH.Thrumbo.Stakes.More".Translate(more.ToString()).ToString();
            return s;
        }
    }
}
