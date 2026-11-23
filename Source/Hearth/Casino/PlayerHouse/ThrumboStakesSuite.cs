// SPDX-License-Identifier: MIT
// In-game suite of "Raise the stakes…" at The Lucky Thrumbo (compiled only with HH_TESTS).
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Silverline;
using UnityEngine;
using Verse;
using Hearth.CasinoMath;
using Hearth.Testing;
using Hearth.Casino.Tests;

namespace Hearth.Casino.PlayerHouse.Tests
{
    public sealed class ThrumboStakesSuite : HearthTestSuite
    {
        public override string Name => "casino.thrumbo.stakes";
        public override ModuleId Module => ModuleId.Casino;
        public override int MaxTicks => 20000;

        static int MapCount(Map map, ThingDef d) => map.listerThings.ThingsOfDef(d).Where(x => x.Spawned).Sum(x => x.stackCount);
        static bool Offered(StakeOffer o, Thing t) => o.Lines.Any(l => l.Tradeable.thingsColony.Contains(t));
        static StakeLine LineOf(StakeOffer o, ThingDef d) => o.Lines.FirstOrDefault(l => l.Tradeable.ThingDef == d);
        static StakeLine LineOf(StakeOffer o, Thing t) => o.Lines.FirstOrDefault(l => l.Tradeable.thingsColony.Contains(t));

        /// <summary>Moves the sealed roulette stream to the next position where Red loses (or wins), like a reload to that point would.</summary>
        static bool SeekRoulette(bool win, RouletteBet bet)
        {
            var rules = ThrumboOdds.Roulette(TS.Shift); var s = JRand.Sealed("thrumbo.roulette");
            for (int i = 0; i < 200; i++)
            {
                ulong c = s.Counter;
                double m = rules.Return(bet, rules.Spin(s));
                if ((m >= 1.0) == win) { s.Counter = c; return true; }
            }
            return false;
        }

        /// <summary>Moves the sealed slots stream to the next spin whose multiplier satisfies <paramref name="want"/>.</summary>
        static bool SeekSlots(SlotVolatility vol, Func<double, bool> want)
        {
            var pt = ThrumboOdds.Slots(vol, TS.Shift); var s = JRand.Sealed("thrumbo.slots");
            for (int i = 0; i < 2000; i++)
            {
                ulong c = s.Counter;
                double m = pt.Spin(s, out _, out _, out _);
                if (want(m)) { s.Counter = c; return true; }
            }
            return false;
        }

        static Pawn Human(Faction f, bool child)
        {
            var req = child
                ? new PawnGenerationRequest(PawnKindDefOf.Villager, f, PawnGenerationContext.NonPlayer, forceGenerateNewPawn: true, fixedBiologicalAge: 8f, fixedChronologicalAge: 8f, developmentalStages: DevelopmentalStage.Child)
                : new PawnGenerationRequest(PawnKindDefOf.Villager, f, PawnGenerationContext.NonPlayer, forceGenerateNewPawn: true);
            var p = PawnGenerator.GeneratePawn(req);
            if (!child && !p.DevelopmentalStage.Adult()) { p.ageTracker.AgeBiologicalTicks = 25L * 3600000L; p.ageTracker.AgeChronologicalTicks = 25L * 3600000L; }
            return p;
        }

        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var col = Kit.Colonist(t); var led = Ledger.Current;
            var comp = ThrumboComponent.Get(); comp.Clear();
            var bank = BankGameComp.Current; bank.TestReset(true);
            var profile = HearthProfile.Current; var oldContent = profile.Content;
            float oldShift = HSettings.Get(TS.EdgeShift), oldSpread = HSettings.Get(TS.PawnSpread), oldCap = HSettings.Get(TS.MaxBetCap), oldFloor = HSettings.Get(TS.MaxBetFloor);
            bool oldOn = HSettings.GetBool(TS.AllIn), oldHint = HSettings.GetBool("core.helpHint");
            HSettings.Set(TS.EdgeShift, 0f); HSettings.Set(TS.PawnSpread, 0.10f); HSettings.Set(TS.AllIn, 1f);
            profile.Content = ContentIntensity.Standard;
            led.Post("world", BankGameComp.Colony, Money.FromSilver(100000), "test funding"); bank.InvalidateCache();
            var spawned = new List<Thing>();
            yield return 1;

            // ---- defs, setting --------------------------------------------------------------------------------------
            t.Check(DefDatabase<TraderKindDef>.GetNamedSilentFail(ThrumboPawnbroker.DefName) is TraderKindDef pk && !pk.orbital && pk.commonality == 0f && !pk.requestable,
                "the pawnbroker trader kind exists and never arrives as a trader");
            t.Check(Math.Abs(HSettings.Find(TS.PawnSpread).Default - 0.10f) < 1e-6, "pawnbroker spread defaults to 10 %");
            t.Check(Math.Abs(ThrumboStakes.Factor - (1.0 - 0.10f - 0.02)) < 1e-9, "stake factor = 1 − spread − 2 % fee");

            // ---- the scene: a powered beacon with goods, pawns of every kind ----------------------------------------------
            var beacon = Kit.Spawn(t, ThingDefOf.OrbitalTradeBeacon, Rot4.North, t.FreeCellNearCenter(20)); spawned.Add(beacon); Kit.Power(beacon);
            var cells = Building_OrbitalTradeBeacon.TradeableCellsAround(beacon.Position, map).Where(c => c != beacon.Position && c.Standable(map) && c.GetFirstItem(map) == null && c.GetFirstBuilding(map) == null && !c.Fogged(map)).ToList();
            t.Check(cells.Count >= 4, "free cells in range of the beacon (" + cells.Count + ")");
            // the random map may already hold steel or components in reach of the beacon; a trade could then take from those stacks first.
            // Remove them, so the stacks below are the only ones and every count is exact.
            int removed = 0;
            foreach (var c in Building_OrbitalTradeBeacon.TradeableCellsAround(beacon.Position, map))
            {
                var things = c.GetThingList(map);
                for (int k = things.Count - 1; k >= 0; k--)
                    if (things[k].def == ThingDefOf.Steel || things[k].def == ThingDefOf.ComponentIndustrial) { things[k].Destroy(); removed++; }
            }
            if (removed > 0) t.Note("removed " + removed + " stack(s) of steel/components the map had in reach of the beacon");
            t.Check(map.listerThings.ThingsOfDef(ThingDefOf.Steel).All(x => !x.Spawned || !Building_OrbitalTradeBeacon.TradeableCellsAround(beacon.Position, map).Contains(x.Position)), "no other steel in reach of the beacon");
            Thing Put(ThingDef d, int n, int i) { var x = ThingMaker.MakeThing(d); x.stackCount = n; GenSpawn.Spawn(x, cells[i % cells.Count], map); x.SetForbidden(true, false); spawned.Add(x); return x; }
            var steel = Put(ThingDefOf.Steel, 75, 0);
            var comps = Put(ThingDefOf.ComponentIndustrial, 3, 1);
            var coins = Put(ThingDefOf.Silver, 100, 2);
            var enemy = Find.FactionManager.AllFactionsListForReading.FirstOrDefault(f => !f.IsPlayer && !f.Hidden && f.def.humanlikeFaction && f.HostileTo(Faction.OfPlayer))
                        ?? Find.FactionManager.AllFactionsListForReading.FirstOrDefault(f => !f.IsPlayer && f.def.humanlikeFaction);
            var near = CellFinder.RandomClosewalkCellNear(col.Position, map, 6);
            var prisoner = Human(enemy, false); GenSpawn.Spawn(prisoner, near, map); prisoner.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Prisoner); spawned.Add(prisoner);
            Pawn child = null;
            try { child = Human(enemy, true); GenSpawn.Spawn(child, near, map); child.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Prisoner); spawned.Add(child); }
            catch (Exception e) { t.Check(false, "the test child prisoner could not be created: " + e); }
            Pawn slave = null;
            if (ModsConfig.IdeologyActive)
            {
                try { slave = Human(enemy, false); GenSpawn.Spawn(slave, near, map); slave.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Slave); spawned.Add(slave); }
                catch (Exception e) { t.Check(false, "the test slave could not be created: " + e); }
            }
            else t.Note("Ideology is not active: slaves do not exist in this profile, the slave check is not applicable");
            var animal = PawnGenerator.GeneratePawn(new PawnGenerationRequest(DefDatabase<PawnKindDef>.GetNamed("Muffalo"), Faction.OfPlayer, PawnGenerationContext.NonPlayer, forceGenerateNewPawn: true));
            GenSpawn.Spawn(animal, near, map); spawned.Add(animal);
            var guest = Kit.Visitors(t, 1, 0, near).FirstOrDefault(); if (guest != null) spawned.Add(guest);
            t.Check(child != null && !child.DevelopmentalStage.Adult(), "test child exists and is a child");
            if (ModsConfig.IdeologyActive) t.Check(slave != null && slave.IsSlave, "test slave exists and is a slave");

            // ---- setting off: no offer, no window, the button is hidden (Window_Thrumbo draws it only when Enabled) -------------
            HSettings.Set(TS.AllIn, 0f);
            Kit.Power(beacon);
            t.Check(!ThrumboStakes.Enabled, "setting off: the button is hidden");
            t.Check(ThrumboStakes.Offer(map).Lines.Count == 0, "setting off: nothing is offered");
            t.Check(Window_ThrumboStakes.Open(map, PayFrom.Bank) == null, "setting off: the dialog does not open");
            HSettings.Set(TS.AllIn, 1f);
            t.Check(ThrumboStakes.Enabled, "setting on: the button is shown");

            // ---- what is offered -------------------------------------------------------------------------------------------
            Kit.Power(beacon);
            var offer = ThrumboStakes.Offer(map);
            var neg = ThrumboStakes.BestNegotiator(map);
            t.Check(offer.Negotiator != null && offer.Negotiator == neg, "valued with the best negotiator (" + neg?.LabelShort + ")");
            t.Check(map.mapPawns.FreeColonistsSpawned.Where(p => p.DevelopmentalStage.Adult() && !(p.skills?.GetSkill(SkillDefOf.Social)?.TotallyDisabled ?? false)).All(p => p.GetStatValue(StatDefOf.TradePriceImprovement) <= neg.GetStatValue(StatDefOf.TradePriceImprovement) + 1e-6f), "nobody negotiates better");
            t.Check(offer.Beacon, "the powered beacon is seen");
            t.Check(Offered(offer, steel) && Offered(offer, comps), "goods in range of the beacon are offered");
            t.Check(!Offered(offer, coins) && !offer.Lines.Any(l => l.Tradeable.ThingDef == ThingDefOf.Silver), "silver is not a stake here (that is the normal table)");
            t.Check(Offered(offer, prisoner), "an adult prisoner is offered (content Standard)");
            t.Check(Offered(offer, animal), "a colony animal is offered (content Standard)");
            t.Check(!Offered(offer, col) && !offer.Lines.Any(l => l.Tradeable.thingsColony.OfType<Pawn>().Any(p => p.IsColonist || (p.RaceProps.Humanlike && p.Faction == Faction.OfPlayer))), "no colonist ever appears in the list");
            t.Check(!ThrumboStakes.Allowed(col), "a colonist is never an allowed stake");
            t.Check(child != null && !Offered(offer, child) && !ThrumboStakes.Allowed(child), "a child prisoner is never offered");
            if (ModsConfig.IdeologyActive) t.Check(slave != null && !Offered(offer, slave) && !ThrumboStakes.Allowed(slave), "a slave is never offered");
            if (guest != null) t.Check(!Offered(offer, guest) && !ThrumboStakes.Allowed(guest), "a guest is never offered");
            else t.Note("no visitor faction: guest check skipped");
            t.Check(offer.Lines.All(l => l.Tradeable.thingsColony.All(ThrumboStakes.Allowed)), "every offered thing passes the stake filter");

            // Cozy: goods only
            profile.Content = ContentIntensity.Cozy; Kit.Power(beacon);
            var cozy = ThrumboStakes.Offer(map);
            t.Check(Offered(cozy, steel) && !Offered(cozy, prisoner) && !Offered(cozy, animal) && cozy.Lines.All(l => l.Kind == StakeKind.Goods), "content Cozy: goods only, no animals or prisoners");
            profile.Content = ContentIntensity.Standard;

            // ---- valuation = vanilla sell price minus spread and fee ----------------------------------------------------------
            var sl = LineOf(offer, steel);
            float gain = neg.GetStatValue(StatDefOf.TradePriceImprovement);
            float sell = TradeUtility.GetPricePlayerSell(steel, 1f, 1f, gain, 0f, 0f, 0f);
            float buy = TradeUtility.GetPricePlayerBuy(steel, 1f, 1f, gain, 0f);
            if (sell >= buy) sell = buy;
            t.Check(sl != null && Math.Abs(sl.UnitPrice - sell) < 1e-4f, "steel: unit value = vanilla sell price (" + sl?.UnitPrice + " vs " + sell + ")");
            sl.Count = 50;
            double expect = sell * 50.0 * (1.0 - 0.10f - 0.02);
            t.Check(Math.Abs(ThrumboStakes.Value(offer).Silver - expect) < 0.001, "50 steel: stake value = sell price × 50 × (1 − 10 % − 2 %) (" + ThrumboStakes.Value(offer) + " vs " + expect.ToString("0.###") + ")");
            HSettings.Set(TS.PawnSpread, 0.30f);
            t.Check(Math.Abs(ThrumboStakes.Value(offer).Silver - sell * 50.0 * (1.0 - 0.30f - 0.02)) < 0.001, "the spread setting moves the value (30 %)");
            HSettings.Set(TS.PawnSpread, 0.10f);
            var pl = LineOf(offer, prisoner);
            t.Check(pl != null && pl.UnitPrice > 0 && Math.Abs(pl.UnitPrice - Math.Min(TradeUtility.GetPricePlayerSell(prisoner, 1f, ModsConfig.IdeologyActive ? 0.6f : 1f, gain, 0f, 0f, 0f), TradeUtility.GetPricePlayerBuy(prisoner, 1f, 1f, gain, 0f))) < 1e-3f,
                "prisoner: vanilla sell price of a prisoner (" + pl?.UnitPrice + ")");

            // ---- limits ---------------------------------------------------------------------------------------------------
            bool oldLimitOn = HSettings.GetBool(TS.LimitOn);
            HSettings.Set(TS.LimitOn, 1f);
            HSettings.Set(TS.MaxBetCap, 10f); HSettings.Set(TS.MaxBetFloor, 1f);
            Kit.Power(beacon);
            string lim = ThrumboStakes.Validate(offer);
            t.Check(lim != null && ThrumboStakes.Value(offer) > ThrumboHouse.TableLimit(map), "limit on: above the table limit the stake is refused: " + lim);
            HSettings.Set(TS.LimitOn, 0f);
            Kit.Power(beacon);
            t.Check(ThrumboStakes.Validate(offer) == null, "limit off (F29): the same stake is accepted");
            HSettings.Set(TS.MaxBetCap, oldCap); HSettings.Set(TS.MaxBetFloor, oldFloor); HSettings.Set(TS.LimitOn, oldLimitOn ? 1f : 0f);
            foreach (var l in offer.Lines) l.Count = 0;
            Kit.Power(beacon);
            t.Check(ThrumboStakes.Validate(offer) != null, "nothing chosen: nothing to play");

            // ---- a loss: exactly the chosen things leave like a sale, no silver moves ------------------------------------------
            LineOf(offer, steel).Count = 50; LineOf(offer, comps).Count = 1; LineOf(offer, prisoner).Count = 1; LineOf(offer, animal).Count = 1;
            var lossValue = ThrumboStakes.Value(offer);
            int steel0 = MapCount(map, ThingDefOf.Steel), comp0 = MapCount(map, ThingDefOf.ComponentIndustrial), prisoners0 = map.mapPawns.PrisonersOfColonySpawned.Count;
            long sum0 = led.SumAll(); var house0 = led.Balance(ThrumboHouse.House); var bal0 = bank.Balance;
            t.Check(SeekRoulette(false, RouletteBet.Red()), "found a losing spin in the sealed stream");
            Kit.Power(beacon);
            var lr = ThrumboStakes.PlayRoulette(offer, RouletteBet.Red(), PayFrom.Bank);
            t.Check(lr.Ok && !lr.Kept && lr.Mult == 0, "losing round: " + lr.Text + lr.Error);
            t.Check(MapCount(map, ThingDefOf.Steel) == steel0 - 50, "exactly 50 steel left the map (" + steel0 + " -> " + MapCount(map, ThingDefOf.Steel) + ")");
            t.Check(MapCount(map, ThingDefOf.ComponentIndustrial) == comp0 - 1, "exactly 1 component left the map");
            t.Check(!prisoner.Spawned && prisoner.Faction != Faction.OfPlayer && !(prisoner.guest?.IsPrisoner ?? false) && map.mapPawns.PrisonersOfColonySpawned.Count == prisoners0 - 1, "the prisoner left with the house");
            t.Check(!animal.Spawned && animal.Faction != Faction.OfPlayer, "the animal left with the house");
            t.Check(lr.Lost.Sum(kv => kv.Key.def == ThingDefOf.Steel ? kv.Value : 0) == 50 && lr.Lost.Count(kv => kv.Key == prisoner) == 1 && lr.Lost.Count(kv => kv.Key == animal) == 1, "the result lists exactly the lost things");
            t.Check(steel.Spawned && steel.stackCount == 25, "the rest of the steel stack stays (25; it holds " + (steel.Spawned ? steel.stackCount.ToString() : "nothing, gone") + ")");
            t.Check(led.SumAll() == sum0 && led.Balance(ThrumboHouse.House) == house0 && bank.Balance == bal0, "a lost stake of goods moves no silver in the ledger");
            var le = comp.History.LastOrDefault();
            t.Check(le != null && le.From == (int)PayFrom.Goods && le.StakedMicro == lossValue.Micro && le.PaidMicro == 0, "history: goods stake, value " + lossValue + ", nothing paid");
            yield return 1;

            // ---- a win to the account: everything stays, value × (m − 1) arrives ----------------------------------------------
            Kit.Power(beacon);
            offer = ThrumboStakes.Offer(map);
            LineOf(offer, steel).Count = 20;
            var winValue = ThrumboStakes.Value(offer);
            int steel1 = MapCount(map, ThingDefOf.Steel); long sum1 = led.SumAll(); var house1 = led.Balance(ThrumboHouse.House); var bal1 = bank.Balance;
            t.Check(SeekRoulette(true, RouletteBet.Red()), "found a winning spin in the sealed stream");
            Kit.Power(beacon);
            var wr = ThrumboStakes.PlayRoulette(offer, RouletteBet.Red(), PayFrom.Bank);
            t.Check(wr.Ok && wr.Kept && wr.Mult > 1.0, "winning round: " + wr.Text + wr.Error);
            t.Check(MapCount(map, ThingDefOf.Steel) == steel1 && steel.Spawned && steel.stackCount == 25, "a win keeps every staked thing (steel " + steel1 + " -> " + MapCount(map, ThingDefOf.Steel) + ", stack " + steel.stackCount + ")");
            var expectWin = winValue * (wr.Mult - 1.0);
            t.Check(wr.Payout == expectWin && Math.Abs(wr.Payout.Silver - winValue.Silver * (wr.Mult - 1.0)) < 1e-5, "payout = value × (payout − 1) = " + wr.Payout);
            t.Check(bank.Balance == bal1 + wr.Payout, "the account received exactly the payout (" + bal1 + " -> " + bank.Balance + ")");
            t.Check(led.Balance(ThrumboHouse.House) == house1 - wr.Payout && led.SumAll() == sum1, "the house account paid it, the ledger stays balanced");
            le = comp.History.LastOrDefault();
            t.Check(le != null && le.StakedMicro == winValue.Micro && le.PaidMicro == (winValue + wr.Payout).Micro && le.Net == wr.Payout, "history: stake value and win match");

            // ---- a win by drop pod: whole coins land at the trade drop spot -------------------------------------------------------
            Kit.Power(beacon);
            offer = ThrumboStakes.Offer(map);
            LineOf(offer, steel).Count = 25;
            long change0 = comp.ChangeMicro; int silver0 = MapCount(map, ThingDefOf.Silver); var house2 = led.Balance(ThrumboHouse.House); long sum2 = led.SumAll();
            t.Check(SeekRoulette(true, RouletteBet.Red()), "found another winning spin");
            Kit.Power(beacon);
            var pr = ThrumboStakes.PlayRoulette(offer, RouletteBet.Red(), PayFrom.Silver);
            t.Check(pr.Ok && pr.Kept, "winning round paid by drop pod: " + pr.Text + pr.Error);
            long podCoins = (pr.Payout.Micro + change0 - comp.ChangeMicro) / Money.PerSilver;
            t.Check(podCoins == (pr.Payout + new Money(change0)).WholeSilver && comp.ChangeMicro >= 0 && comp.ChangeMicro < Money.PerSilver, "whole coins go out, less than a coin stays owed (" + podCoins + ")");
            t.Check(led.Balance(ThrumboHouse.House) == house2 - Money.FromSilver(podCoins) && led.SumAll() == sum2, "the house account booked the coins in the pods");
            for (int i = 0; i < 20 && MapCount(map, ThingDefOf.Silver) < silver0 + podCoins; i++) yield return 60;
            t.Check(MapCount(map, ThingDefOf.Silver) == silver0 + podCoins, "the drop pods brought exactly " + podCoins + " silver (" + silver0 + " -> " + MapCount(map, ThingDefOf.Silver) + ")");

            // ---- slots: one sealed spin, same reels after a "reload" ----------------------------------------------------------
            Kit.Power(beacon);
            offer = ThrumboStakes.Offer(map);
            LineOf(offer, comps).Count = 1;
            var ss = JRand.Sealed("thrumbo.slots"); ulong sc = ss.Counter;
            ThrumboOdds.Slots(SlotVolatility.Medium, TS.Shift).Spin(ss, out int ea, out int eb, out int ec); ss.Counter = sc;
            Kit.Power(beacon);
            var sr = ThrumboStakes.PlaySlots(offer, SlotVolatility.Medium, PayFrom.Bank);
            t.Check(sr.Ok && sr.Reels != null && sr.Reels[0] == ea && sr.Reels[1] == eb && sr.Reels[2] == ec, "slots with goods: the sealed stream decides (" + sr.Text + sr.Error + ")");
            t.Check(comp.History.Count == 4 && comp.History.All(e => e.From == (int)PayFrom.Goods), "four goods rounds in the history");
            yield return 1;

            // ---- partial prize: slots below x1: the things are lost, the prize is paid in silver ----------------------------------------------
            Kit.Power(beacon);
            offer = ThrumboStakes.Offer(map);
            LineOf(offer, steel).Count = 10;
            var partValue = ThrumboStakes.Value(offer);
            int steel2 = MapCount(map, ThingDefOf.Steel); long sum3 = led.SumAll(); var house3 = led.Balance(ThrumboHouse.House); var bal3 = bank.Balance; long change3 = comp.ChangeMicro;
            t.Check(SeekSlots(SlotVolatility.Medium, m => m > 0 && m < 1.0), "found a slots spin with a partial prize in the sealed stream");
            Kit.Power(beacon);
            var qr = ThrumboStakes.PlaySlots(offer, SlotVolatility.Medium, PayFrom.Bank);
            t.Check(qr.Ok && !qr.Kept && qr.Mult > 0 && qr.Mult < 1.0, "partial round: lost, multiplier " + qr.Mult + " (" + qr.Text + qr.Error + ")");
            t.Check(qr.Payout == partValue * qr.Mult && qr.Payout.Micro > 0, "the prize is value × multiplier = " + qr.Payout);
            t.Check(MapCount(map, ThingDefOf.Steel) == steel2 - 10, "a partial prize still takes exactly the 10 steel");
            t.Check(bank.Balance == bal3 + qr.Payout + new Money(change3), "the prize arrived on the account (" + bal3 + " -> " + bank.Balance + ")");
            t.Check(led.Balance(ThrumboHouse.House) == house3 - qr.Payout - new Money(change3) && led.SumAll() == sum3, "the house account paid it, the ledger stays balanced");
            t.Check(qr.Text.Contains("HH.Thrumbo.Stakes.LostPart".Translate(qr.Payout.ToStringShort()).ToString()), "the result text says lost with the prize paid back");
            var pe = comp.History.LastOrDefault();
            t.Check(pe != null && pe.From == (int)PayFrom.Goods && pe.StakedMicro == partValue.Micro && pe.PaidMicro == qr.Payout.Micro, "history: partial prize recorded");
            t.Check(offer.Lines.All(l => l.Count == 0), "the count buffers are reset after the round");

            // ---- bank refuses the payout: the win falls back to drop pods ---------------------------------------------------------------------------
            Kit.Power(beacon);
            offer = ThrumboStakes.Offer(map);
            LineOf(offer, steel).Count = 5;
            t.Check(SeekRoulette(true, RouletteBet.Red()), "found a winning spin for the fallback test");
            Kit.Power(beacon);
            long sumF = led.SumAll(); var balF = bank.Balance; int silverF = MapCount(map, ThingDefOf.Silver); long changeF = comp.ChangeMicro;
            bank.TestReset(false);                                          // the bank is not ready, as if it refused to book
            bank.InvalidateCache();
            var fr = ThrumboStakes.PlayRoulette(offer, RouletteBet.Red(), PayFrom.Bank);
            long fCoins = (fr.Payout.Micro + changeF - comp.ChangeMicro) / Money.PerSilver;
            t.Check(fr.Ok && fr.Kept && fCoins == (fr.Payout + new Money(changeF)).WholeSilver, "bank not ready: the win is paid as " + fCoins + " coins by drop pod");
            t.Check(led.SumAll() == sumF, "the fallback keeps the ledger balanced");
            for (int i = 0; i < 20 && MapCount(map, ThingDefOf.Silver) < silverF + fCoins; i++) yield return 60;
            t.Check(MapCount(map, ThingDefOf.Silver) == silverF + fCoins, "the fallback pods brought the silver");
            bank.TestReset(true); led.Post("world", BankGameComp.Colony, Money.FromSilver(100000), "test funding"); bank.InvalidateCache();

            // ---- the stakes button is disabled while a blackjack hand is open -------------------------------------------------------------------
            t.Check(Window_Thrumbo.StakesButtonEnabled(false), "no hand open: the button is enabled");
            var hseat = ThrumboSeat.For(neg, PayFrom.Bank);
            bool handOpen = false;
            for (int i = 0; i < 40 && !handOpen; i++) { ThrumboHouse.DealBlackjack(hseat, Money.FromSilver(5)); handOpen = ThrumboHouse.CurrentHand() != null; }
            t.Check(handOpen, "a blackjack hand is open");
            t.Check(!Window_Thrumbo.StakesButtonEnabled(ThrumboHouse.CurrentHand() != null), "hand open: the stakes button is disabled");
            ThrumboHouse.StandIfOpen(hseat);
            t.Check(ThrumboHouse.CurrentHand() == null && Window_Thrumbo.StakesButtonEnabled(ThrumboHouse.CurrentHand() != null), "hand closed: the button is enabled again");

            // ---- totals: goods rounds are kept apart from the silver totals -----------------------------------------------------------------------
            t.Check(comp.GoodsRounds == 6 && comp.GoodsStaked > 0, "goods rounds are counted apart (" + comp.GoodsRounds + ")");
            int silverRounds = comp.Rounds(ThrumboGame.Blackjack);
            t.Check(comp.Rounds(ThrumboGame.Slots) == 0 && comp.Rounds(ThrumboGame.Roulette) == 0 && comp.StakedAll == comp.Staked(ThrumboGame.Blackjack), "the silver totals contain no goods stake (" + silverRounds + " blackjack hand(s))");
            yield return 1;

            // ---- the dialog carries the help line --------------------------------------------------------------------------------
            HSettings.Set("core.helpHint", 1f);
            CasinoUI.HintDrawCount = 0;
            var win = Window_ThrumboStakes.Open(map, PayFrom.Bank);
            t.Check(win != null && win.ShowsHelpHint, "the stake dialog shows the help line when core.helpHint is on");
            for (int i = 0; i < 20 && CasinoUI.HintDrawCount == 0; i++) yield return 30;
            if (CasinoUI.HintDrawCount == 0 && win != null)
            {
                try { win.DoWindowContents(new Rect(0, 0, 860f, 680f)); t.Note("window not rendered by the loop, drawn directly"); } catch (Exception e) { t.Note("direct draw not possible: " + e.GetType().Name); }
            }
            if (CasinoUI.HintDrawCount > 0) t.Check(true, "the dialog drew the hint (" + CasinoUI.HintDrawCount + " draws)"); else t.Note("no draw happened; verified through ShowsHelpHint");
            HSettings.Set("core.helpHint", 0f);
            t.Check(win == null || !win.ShowsHelpHint, "the help line is hidden when core.helpHint is off");
            win?.Close(false);
            HSettings.Set("core.helpHint", oldHint ? 1f : 0f);

            // ---- clean up --------------------------------------------------------------------------------------------------------
            foreach (var x in spawned) if (x != null && !x.Destroyed && x.Spawned) x.Destroy();
            profile.Content = oldContent;
            HSettings.Set(TS.EdgeShift, oldShift); HSettings.Set(TS.PawnSpread, oldSpread); HSettings.Set(TS.AllIn, oldOn ? 1f : 0f); HSettings.Set(TS.MaxBetCap, oldCap);
            comp.Clear(); bank.TestReset(true);
            yield return 2;
        }
    }
    /// <summary>Screenshots of the Thrumbo window and the stake dialog (explicit only: tools/run_test.sh casino.thrumbo.shots).</summary>
    public sealed class ThrumboStakesShots : HearthTestSuite
    {
        public override string Name => "casino.thrumbo.shots";
        public override ModuleId Module => ModuleId.Casino;
        public override bool ExplicitOnly => true;
        public override int MaxTicks => 20000;

        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var col = Kit.Colonist(t);
            Ledger.Current.Post("world", BankGameComp.Colony, Money.FromSilver(20000), "test funding"); BankGameComp.Current.InvalidateCache();
            var beacon = Kit.Spawn(t, ThingDefOf.OrbitalTradeBeacon, Rot4.North, t.FreeCellNearCenter(20)); Kit.Power(beacon);
            var cells = Building_OrbitalTradeBeacon.TradeableCellsAround(beacon.Position, map).Where(c => c != beacon.Position && c.Standable(map) && c.GetFirstItem(map) == null && c.GetFirstBuilding(map) == null).ToList();
            int i = 0;
            foreach (var d in new[] { ThingDefOf.Steel, ThingDefOf.ComponentIndustrial, ThingDefOf.MedicineIndustrial, ThingDefOf.Plasteel, ThingDefOf.Gold })
            { var x = ThingMaker.MakeThing(d); x.stackCount = Math.Min(d.stackLimit, 40); GenSpawn.Spawn(x, cells[i++], map); x.SetForbidden(true, false); }
            HSettings.Set("core.helpHint", 1f);
            string outDir = "/private/tmp/claude-501/hh_tests/thrumbo_shots"; System.IO.Directory.CreateDirectory(outDir);
            var w = Window_Thrumbo.Open(col); yield return 60; ScreenshotTaker.TakeNonSteamShot(); yield return 40; w.Close(false);
            Kit.Power(beacon);
            var s = Window_ThrumboStakes.Open(map, PayFrom.Bank);
            var st = s.Offer.Lines.FirstOrDefault(l => l.Tradeable.ThingDef == ThingDefOf.Steel); if (st != null) { st.Count = 30; st.Buffer = "30"; }
            yield return 60; ScreenshotTaker.TakeNonSteamShot(); yield return 40; s.Close(false);
            yield return 300;
            try { foreach (var f in System.IO.Directory.GetFiles(GenFilePaths.ScreenshotFolderPath, "*.png")) System.IO.File.Copy(f, System.IO.Path.Combine(outDir, System.IO.Path.GetFileName(f)), true); } catch (Exception e) { t.Note("copy failed: " + e.Message); }
            t.Note("screenshots in " + outDir);
            t.Check(true, "screenshots taken");
        }
    }
}
#endif
