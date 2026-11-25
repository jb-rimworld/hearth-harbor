// SPDX-License-Identifier: MIT
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Hearth.Testing;

namespace Hearth.Underworld
{
    /// <summary>Helpers of the package C suites (other facilities). Builds on UwKit, which is not partial.</summary>
    static class FacKit
    {
        /// <summary>A walled, roofed w×h room with one back room door in the south wall. Returns the inner rect and the door.</summary>
        public static bool MakeBackRoom(Map map, int w, int h, out CellRect inner, out Building_BackRoomDoor door, out CellRect outer)
        {
            door = null; outer = default;
            if (!UwKit.TryFindRect(map, w, h, out inner)) return false;
            outer = inner.ExpandedBy(1);
            foreach (var c in outer.EdgeCells) Wall(map, c);
            foreach (var c in inner) map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
            var dc = new IntVec3(outer.CenterCell.x, 0, outer.minZ); dc.GetEdifice(map)?.Destroy();
            door = (Building_BackRoomDoor)UwKit.SpawnBuilding(UwDefs.BackRoomDoor, dc, map);
            Refresh(map);
            return true;
        }
        public static Thing Wall(Map map, IntVec3 c) { var w = ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.WoodLog); w.SetFaction(Faction.OfPlayer); GenSpawn.Spawn(w, c, map); return UwKit.Track(w); }
        public static void Refresh(Map map) { map.regionAndRoomUpdater.TryRebuildDirtyRegionsAndRooms(); Hearth.Underworld.BackRoom.MarkDirty(map); Underworld.MarkInUseDirty(map); FacilityStatus.MarkDirty(); }
        public static void Unroof(CellRect inner, Map map) { foreach (var c in inner) map.roofGrid.SetRoof(c, null); }

        public static bool NodeTreeOpen() => Find.WindowStack.IsOpen<Dialog_NodeTree>();

        /// <summary>Shady pawns with a lord on the map (gangs spawned by a test), tracked for cleanup.</summary>
        public static List<Pawn> TrackGangs(Map map)
        {
            var l = map.mapPawns.AllPawnsSpawned.Where(p => p.RaceProps.Humanlike && Underworld.IsShady(p) && p.GetLord() != null && !p.IsColonist).ToList();
            foreach (var p in l) if (!UwKit.Spawned.Contains(p)) UwKit.Track(p);
            return l;
        }

        public static void CloseOffers() { foreach (var o in Underworld.Comp.Offers.ToList()) Offers.Close(o); }

        public static CompFenceStock Fence(Map map, TestContext t)
        {
            var cell = UwKit.TryFindRect(map, 3, 3, out var r) ? r.CenterCell : t.FreeCellNearCenter();
            return UwKit.SpawnBuilding(UwDefs.FenceCounter, cell, map).TryGetComp<CompFenceStock>();
        }

        public static Thing Dock(Map map, TestContext t)
        {
            var cell = UwKit.TryFindRect(map, 5, 5, out var r) ? r.CenterCell : t.FreeCellNearCenter();
            return UwKit.SpawnBuilding(UwDefs.SmugglerDock, cell, map);
        }

        public static bool HasKey(string s, string key) => !s.NullOrEmpty() && s.Contains(key.Translate().ToString().Split('{')[0].Trim());
    }

    /// <summary>The back room door says whether its room is a back room, and why not.</summary>
    public sealed class UwBackRoomFeedbackSuite : HearthTestSuite
    {
        public override string Name => "underworld.backroom.feedback";
        public override ModuleId Module => ModuleId.Underworld;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map;
            UwKit.Begin(ContentIntensity.Standard);
            if (!FacKit.MakeBackRoom(map, 5, 5, out var inner, out var door, out var outer)) { t.Note("no free area; skipped"); UwKit.End(); yield break; }
            yield return 2;
            var st = BackRoom.StateOf(door, out int cells);
            t.Check(st == BackRoom.DoorState.BackRoom && cells == 25, "a closed room behind only back room doors: back room with 25 tiles (" + st + ", " + cells + ")");
            var line = BackRoom.StatusLine(door);
            t.Check(line == UwPay.T("BackRoom.Yes", 25), "status line: " + line);
            t.Check(door.GetInspectString().Contains(line), "the inspect string carries it");
            var row = FacilityStatus.Get(map, FacilityStatus.BackRoomId);
            t.Check(row != null && row.State == FacilityState.Working && row.Line.Contains("25"), "Underworld tab row: working, same tile count (" + row?.Line + ")");
            // a second, ordinary door
            var other = new IntVec3(outer.CenterCell.x, 0, outer.maxZ); other.GetEdifice(map)?.Destroy();
            var plain = UwKit.SpawnBuilding(ThingDefOf.Door, other, map);
            FacKit.Refresh(map); yield return 2;
            t.Check(BackRoom.StateOf(door, out _) == BackRoom.DoorState.OrdinaryDoor && BackRoom.StatusLine(door) == UwPay.T("BackRoom.NoOrdinaryDoor"), "an ordinary door too: says so (" + BackRoom.StatusLine(door) + ")");
            row = FacilityStatus.Get(map, FacilityStatus.BackRoomId);
            t.Check(row.State == FacilityState.Waiting && row.Line == UwPay.T("BackRoom.NoOrdinaryDoor"), "tab row waits with the same sentence");
            // open the room to the outside: take the ordinary door out (a gap in the wall) and the roof off
            plain.Destroy(); FacKit.Unroof(inner, map);
            FacKit.Refresh(map); yield return 2;
            var st2 = BackRoom.StateOf(door, out _);
            t.Check(st2 == BackRoom.DoorState.OpenToOutside && BackRoom.StatusLine(door) == UwPay.T("BackRoom.NoOutside"), "a gap in the wall: open to the outside (" + st2 + ")");
            // a lone door in the open
            var lone = (Building_BackRoomDoor)UwKit.SpawnBuilding(UwDefs.BackRoomDoor, UwKit.TryFindRect(map, 3, 3, out var lr, 30) ? lr.CenterCell : t.FreeCellNearCenter(), map);
            FacKit.Refresh(map); yield return 2;
            var st3 = BackRoom.StateOf(lone, out _);
            t.Check(st3 == BackRoom.DoorState.OpenToOutside || st3 == BackRoom.DoorState.NoRoom, "a lone door: no back room, with a reason (" + BackRoom.StatusLine(lone) + ")");
            // Cozy: a plain door
            HearthProfile.Current.Content = ContentIntensity.Cozy;
            t.Check(BackRoom.StatusLine(door) == UwPay.T("BackRoom.Off"), "Cozy: says it is a plain door");
            HearthProfile.Current.Content = ContentIntensity.Standard;
            foreach (var k in new[] { "BackRoom.Yes", "BackRoom.NoOrdinaryDoor", "BackRoom.NoOutside", "BackRoom.NoRoom", "BackRoom.Off" })
                t.Check(!UwPay.T(k, 1).Contains("HH.Underworld"), "keyed text exists: " + k);
            UwKit.End();
        }
    }

    /// <summary>The first gang comes 0.5 to 1.5 days after the first facility; a failed visit keeps the timer, retries and says why.</summary>
    public sealed class UwGangFirstSuite : HearthTestSuite
    {
        public override string Name => "underworld.gang.first";
        public override ModuleId Module => ModuleId.Underworld;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var c = Underworld.Comp;
            UwKit.Begin(ContentIntensity.Standard);
            if (UwKit.Friendly() == null) { t.Note("no friendly faction; skipped"); UwKit.End(); yield break; }
            int visits0 = c.GangVisits, next0 = c.NextGangTick; bool planned0 = c.FirstGangPlanned; string key0 = c.GangRetryKey;
            int now = Find.TickManager.TicksGame;
            c.GangVisits = 0; c.FirstGangPlanned = false; c.NextGangTick = now + 10 * 60000;   // an old save: a long timer from before
            bool anyFacility = Find.Maps.Any(m => m.IsPlayerHome && Gangs.HasGangFacility(m));
            if (!anyFacility)
            {
                UwTicker.Events(now);
                t.Check(!c.FirstGangPlanned && c.NextGangTick == now + 10 * 60000, "no facility yet: nothing planned");
            }
            var fs = FacKit.Fence(map, t);
            FacKit.Refresh(map);
            t.Check(Gangs.HasGangFacility(map), "a fence counter is a facility gangs use");
            UwTicker.Events(now);
            float first = HSettings.Get("underworld.gang.firstDays");
            int lo = now + (int)(60000 * first * 0.5f) - 1, hi = now + (int)(60000 * first * 1.5f) + 1;
            t.Check(c.FirstGangPlanned && c.NextGangTick >= lo && c.NextGangTick <= hi, $"first gang in {(c.NextGangTick - now) / 60000f:0.00} days (0.5 to 1.5 days)");
            // the visit fails: the timer does not move a full interval, the reason is kept and shown
            Gangs.TestFailKey = "NoEntry";
            c.NextGangTick = now;
            UwTicker.Events(now);
            int retry = (int)(HSettings.Get("underworld.gang.retryHours") * GenDate.TicksPerHour);
            t.Check(c.GangVisits == 0 && c.NextGangTick == now + retry, "failed visit: next try in " + HSettings.Get("underworld.gang.retryHours") + " h (" + (c.NextGangTick - now) + " ticks)");
            t.Check(c.GangRetryKey == "NoEntry" && FacilityStatus.GangRetryReason() == UwPay.T("Gang.Why.NoEntry"), "the reason is kept: " + FacilityStatus.GangRetryReason());
            FacilityStatus.MarkDirty();
            var row = FacilityStatus.Get(map, FacilityStatus.GangsId);
            t.Check(row != null && row.State == FacilityState.Waiting && row.Line.IndexOf(UwPay.T("Gang.Why.NoEntry"), StringComparison.OrdinalIgnoreCase) >= 0, "Underworld tab row names the reason: " + row?.Line);
            Gangs.TestFailKey = null;
            // the retry works: the gang comes, the reason is cleared, the next visit is a normal interval away
            c.NextGangTick = now;
            UwTicker.Events(now);
            var gang = FacKit.TrackGangs(map);
            t.Check(c.GangVisits == 1 && gang.Count >= 2, "the retry brought a gang: " + gang.Count);
            t.Check(c.GangRetryKey == null && c.NextGangTick > now + 60000, "reason cleared, next visit in " + ((c.NextGangTick - now) / 60000f).ToString("0.0") + " days");
            foreach (var k in new[] { "NoFacility", "NoFaction", "NoEntry", "NoPawns" }) t.Check(!UwPay.T("Gang.Why." + k).Contains("HH.Underworld"), "reason keyed: " + k);
            FacKit.CloseOffers();
            c.GangVisits = visits0; c.NextGangTick = next0; c.FirstGangPlanned = planned0; c.GangRetryKey = key0;
            UwKit.End();
            yield return 1;
        }
    }

    /// <summary>A gang member spends the silver he carries; a fee forced past the budget is not taken off twice.</summary>
    public sealed class UwGangBudgetSuite : HearthTestSuite
    {
        public override string Name => "underworld.gang.budget";
        public override ModuleId Module => ModuleId.Underworld;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var c = Underworld.Comp;
            UwKit.Begin(ContentIntensity.Standard);
            if (UwKit.Friendly() == null) { t.Note("no friendly faction; skipped"); UwKit.End(); yield break; }
            int visits0 = c.GangVisits;
            FacKit.Fence(map, t); FacKit.Refresh(map);
            var ok = Gangs.Fire(map, out var pawns, force: true);
            t.Check(ok && pawns.Count >= 2, "a gang arrived: " + pawns?.Count);
            if (!ok) { UwKit.End(); yield break; }
            foreach (var p in pawns) UwKit.Track(p);
            var feeMax = HSettings.GetSilver("underworld.gang.feeMax"); var lounge = HSettings.GetSilver("underworld.lounge.price");
            foreach (var p in pawns)
            {
                var b = UwPay.Budget(p);
                t.Check(b.WholeSilver == UwKit.Silver(p), p.LabelShort + ": budget = carried silver (" + b + " / " + UwKit.Silver(p) + ")");
                t.Check(b >= feeMax + lounge, p.LabelShort + ": can pay the highest back room fee and a lounge session (" + b + ")");
            }
            var m = pawns[0]; var b0 = UwPay.Budget(m); var till0 = UwKit.Till(map);
            t.Check(UwPay.Charge(m, feeMax, Underworld.Till(map), "underworld.backroom", "Back room fee (test)", ignoreBudget: true), "back room fee charged");
            var b1 = UwPay.Budget(m);
            t.Check(b0 - b1 == feeMax, "the fee left the budget exactly once: " + b0 + " -> " + b1);
            t.Check(UwKit.Till(map) - till0 == feeMax, "and arrived in the till");
            t.Check(b1 >= lounge, "still affords the lounge (" + b1 + ")");
            t.Check(UwPay.Charge(m, lounge, Underworld.Till(map), "underworld.lounge", "Lounge (test)"), "the lounge price can be charged after the fee");
            t.Check(UwPay.Budget(m) == b1 - lounge, "and it is taken once too");
            var drug = DefDatabase<ThingDef>.GetNamedSilentFail("SmokeleafJoint");
            var other = pawns[1];
            if (drug != null && Lounge.AllowedLevel(other) > 0)
            {
                UwPay.Charge(other, feeMax, Underworld.Till(map), "underworld.backroom", "Back room fee (test)", ignoreBudget: true);
                t.Check(Lounge.Session(other, drug), "a second member pays the fee and then a real lounge session");
            }
            else t.Note("second member's ideology forbids drugs: real session skipped");
            t.Check(Ledger.Current.SumAll() == 0, "ledger balanced");
            FacKit.CloseOffers();
            c.GangVisits = visits0;
            UwKit.End();
        }
    }

    /// <summary>Fence: inspect lines with the reason, one message per hour, Take goods out, goods dropped when the counter is deconstructed.</summary>
    public sealed class UwFenceTakeoutSuite : HearthTestSuite
    {
        public override string Name => "underworld.fence.takeout";
        public override ModuleId Module => ModuleId.Underworld;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var f = UwKit.Friendly();
            UwKit.Begin(ContentIntensity.Standard);
            if (f == null) { t.Note("no friendly faction; skipped"); UwKit.End(); yield break; }
            var fs = FacKit.Fence(map, t); var counter = fs.parent;
            var ledger = Ledger.Current;
            // empty till and cash box: the inspect string says why it cannot buy
            ledger.Post(Underworld.Till(map), "world", UwKit.Till(map), "test drain");
            ledger.Post(fs.Account, "world", ledger.Balance(fs.Account), "test drain");
            var ins = fs.CompInspectStringExtra();
            t.Check(ins.IndexOf(UwPay.T("Fence.Why.TillEmpty"), StringComparison.OrdinalIgnoreCase) >= 0 && ins.Contains(FenceTrade.StockLine(fs)), "inspect: stock line and 'the till is empty': " + ins.Replace("\n", " | "));
            var seller = UwKit.Guests(map, 1, f, shady: true)[0];
            var item = ThingMaker.MakeThing(ThingDefOf.MedicineIndustrial); item.stackCount = 6; seller.inventory.innerContainer.TryAdd(item, false); Underworld.Comp.MarkStolen(item, f);
            fs.LastMessageTick = -999999;
            t.Check(!FenceTrade.BuyFrom(fs, seller, item, out string why) && why == UwPay.T("Fence.Why.TillEmpty"), "no cash: the deal fails with the reason (" + why + ")");
            t.Check(fs.LastMessageTick == Find.TickManager.TicksGame, "the failure was told once");
            // with cash: two deals in one hour give one message
            Payments.CreditColony(map, Money.FromSilver(2000), "test");
            t.Check(FenceTrade.CannotBuyReason(fs) == null, "with silver in the till: no reason line");
            fs.LastMessageTick = -999999;
            t.Check(FenceTrade.BuyFrom(fs, seller, item, out why), "deal works (" + why + ")");
            int msg1 = fs.LastMessageTick;
            var item2 = ThingMaker.MakeThing(ThingDefOf.MedicineHerbal); item2.stackCount = 5; seller.inventory.innerContainer.TryAdd(item2, false); Underworld.Comp.MarkStolen(item2, f);
            yield return 30;
            t.Check(FenceTrade.BuyFrom(fs, seller, item2, out why), "second deal works (" + why + ")");
            t.Check(msg1 >= 0 && fs.LastMessageTick == msg1, "second deal within the hour: no second message");
            t.Check(fs.Stock.Count == 2 && fs.SourceFaction(fs.Stock.First()) == f, "two stacks in stock, origin known");
            // take goods out
            var gizmo = fs.CompGetGizmosExtra().OfType<Command_Action>().FirstOrDefault(g => g.defaultLabel == UwPay.T("Fence.TakeOut"));
            t.Check(gizmo != null && !gizmo.Disabled, "Take goods out gizmo is there and enabled");
            var stocked = fs.Stock.ToList();
            t.Check(fs.TakeOut() == 2 && fs.Stock.Count == 0, "Take goods out empties the stock");
            t.Check(stocked.All(x => x.Spawned && x.Position.InHorDistOf(counter.Position, 6f)), "the goods lie next to the counter");
            t.Check(stocked.All(x => !x.IsForbidden(Faction.OfPlayer)), "and the colony may use them");
            t.Check(stocked.All(x => fs.SourceFaction(x) == null), "the stolen origin is dropped (as the tooltip says)");
            foreach (var x in stocked) UwKit.Track(x);
            gizmo = fs.CompGetGizmosExtra().OfType<Command_Action>().FirstOrDefault(g => g.defaultLabel == UwPay.T("Fence.TakeOut"));
            t.Check(gizmo != null && gizmo.Disabled, "empty stock: the gizmo is disabled with a reason");
            // deconstructing the counter does not lose its goods
            // a thing that cannot merge into the stacks already lying there
            var d3 = DefDatabase<ThingDef>.GetNamedSilentFail("Gun_Revolver") ?? DefDatabase<ThingDef>.GetNamedSilentFail("MeleeWeapon_Knife") ?? ThingDefOf.MealSurvivalPack;
            var item3 = ThingMaker.MakeThing(d3, d3.MadeFromStuff ? GenStuff.DefaultStuffFor(d3) : null); FenceTrade.Stock(fs, item3, f);
            var pos = counter.Position;
            ledger.Post(fs.Account, Underworld.Till(map), ledger.Balance(fs.Account), "test: empty the cash box before removal");
            counter.Destroy(DestroyMode.Deconstruct); UwKit.Spawned.Remove(counter);
            t.Check(item3.Spawned && item3.Position.InHorDistOf(pos, 6f), "deconstructed: the stock is dropped, not lost");
            if (item3.Spawned) UwKit.Track(item3);
            t.Check(ledger.SumAll() == 0, "ledger balanced");
            UwKit.End();
        }
    }

    /// <summary>Smuggler landings and protection demands are saved offers with a vanilla letter and gizmos; the game never opens a dialog on its own.</summary>
    public sealed class UwOffersSuite : HearthTestSuite
    {
        public override string Name => "underworld.offers";
        public override ModuleId Module => ModuleId.Underworld;
        public override int MaxTicks => 12000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var c = Underworld.Comp; var ledger = Ledger.Current;
            UwKit.Begin(ContentIntensity.Standard);
            Find.WindowStack.TryRemove(typeof(Dialog_NodeTree), false);
            FacKit.CloseOffers();
            var dock = FacKit.Dock(map, t); FacKit.Refresh(map);
            Payments.CreditColony(map, Money.FromSilver(3000), "test");
            int lettersBefore = Find.LetterStack.LettersListForReading.Count;
            // ---- smuggler landing
            t.Check(Smuggler.Fire(map, force: true), "a boat lands");
            t.Check(!FacKit.NodeTreeOpen(), "no dialog opened by the landing");
            var o = Offers.OpenOf(UwOfferKind.Smuggler, map);
            t.Check(o != null && o.goods.Count > 0 && o.price > Money.Zero && o.expireTick == Find.TickManager.TicksGame + Offers.HoursFor(UwOfferKind.Smuggler) * GenDate.TicksPerHour, "an open, saved offer with goods, price and expiry");
            if (o == null) { UwKit.End(); yield break; }
            var let = Find.LetterStack.LettersListForReading.FirstOrDefault(l => l.ID == o.letterId);
            t.Check(let != null && let.def == LetterDefOf.NeutralEvent && let.GetType().Assembly == typeof(Letter).Assembly, "announced by a vanilla standard letter (" + let?.GetType().Name + ")");
            t.Check(!Smuggler.Fire(map, force: true), "one boat at a time");
            var gz = dock.GetGizmos().OfType<Command_Action>().ToList();
            var buy = gz.FirstOrDefault(g => g.defaultLabel == UwPay.T("Smuggler.Buy", o.price.ToStringShort()));
            var away = gz.FirstOrDefault(g => g.defaultLabel == UwPay.T("Smuggler.Decline"));
            t.Check(buy != null && !buy.Disabled && away != null, "dock gizmos: buy (enabled) and send away");
            t.Check(dock.GetInspectString().Contains(Offers.Line(o)), "dock inspect: the waiting boat");
            t.Check(FacilityStatus.OpenOffers(map).Contains(o) && FacilityStatus.Get(map, FacilityStatus.DockId).State == FacilityState.Working, "Underworld tab API lists the offer");
            // the offer survives writing and reading the component
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hh_uw_offers.xml");
            UnderworldComponent copy = null; string err = null;
            try
            {
                Scribe.saver.InitSaving(path, "UwTest"); Scribe_Deep.Look(ref c, "uw", Current.Game); Scribe.saver.FinalizeSaving();
                Scribe.loader.InitLoading(path); Scribe.EnterNode("UwTest"); Scribe_Deep.Look(ref copy, "uw", Current.Game); Scribe.ExitNode(); Scribe.loader.FinalizeLoading();
            }
            catch (Exception e) { err = e.Message; try { Scribe.ForceStop(); } catch { } }
            var co = copy?.Offers.FirstOrDefault(x => x.id == o.id);
            t.Check(err == null && co != null && co.kind == UwOfferKind.Smuggler && co.price == o.price && co.marketValue == o.marketValue && co.expireTick == o.expireTick
                    && co.letterId == o.letterId && co.mapId == o.mapId && co.goods.Count == o.goods.Count && co.goods.Zip(o.goods, (a, b) => a.def == b.def && a.stuff == b.stuff && a.count == b.count && a.quality == b.quality).All(x => x),
                "open offer written and read back " + err);
            try { System.IO.File.Delete(path); } catch { }
            // buy
            var till0 = UwKit.Till(map);
            buy.action();
            t.Check(till0 - UwKit.Till(map) == o.price && Offers.OpenOf(UwOfferKind.Smuggler, map) == null, "Buy: price paid, offer closed");
            t.Check(!Find.LetterStack.LettersListForReading.Any(l => l.ID == o.letterId), "its letter left the stack");
            t.Check(ledger.SumAll() == 0, "ledger balanced");
            // decline
            Smuggler.Fire(map, force: true); o = Offers.OpenOf(UwOfferKind.Smuggler, map); till0 = UwKit.Till(map);
            Offers.Decline(o);
            t.Check(Offers.OpenOf(UwOfferKind.Smuggler, map) == null && UwKit.Till(map) == till0, "Send away: offer closed, nothing paid");
            // no cash: buy disabled with the reason
            Smuggler.Fire(map, force: true); o = Offers.OpenOf(UwOfferKind.Smuggler, map);
            var keep = UwKit.Till(map); ledger.Post(Underworld.Till(map), "world", keep, "test drain");
            t.Check(!Offers.CanAccept(o, out string why) && why == UwPay.T("Smuggler.NoCash"), "empty till: buy refused with the reason (" + why + ")");
            ledger.Post("world", Underworld.Till(map), keep, "test refill");
            // expiry = the boat leaves
            o.expireTick = Find.TickManager.TicksGame - 1; till0 = UwKit.Till(map);
            Offers.Tick(Find.TickManager.TicksGame);
            t.Check(Offers.OpenOf(UwOfferKind.Smuggler, map) == null && UwKit.Till(map) == till0, "expired: the boat left, nothing else happened");
            // ---- protection demand from a real gang visit
            var f = UwKit.Friendly();
            if (f == null) { t.Note("no friendly faction: protection part skipped"); UwKit.End(); yield break; }
            float chance0 = HSettings.Get("underworld.gang.intimidateChance");
            HSettings.Set("underworld.gang.intimidateChance", 1f);
            int visits0 = c.GangVisits;
            bool came = Gangs.Fire(map, out var gang, force: true);
            HSettings.Set("underworld.gang.intimidateChance", chance0);
            t.Check(came && gang.Count >= 2, "a gang arrived");
            if (!came) { UwKit.End(); yield break; }
            foreach (var p in gang) UwKit.Track(p);
            var d = c.Offers.FirstOrDefault(x => x.kind == UwOfferKind.Protection && x.leader == gang[0]);
            t.Check(d != null && !d.Announced && d.openTick > Find.TickManager.TicksGame, "the demand is planned for later, not shown yet");
            t.Check(Offers.OfLeader(gang[0]) == null && !gang[0].GetGizmos().OfType<Command_Action>().Any(g => g.defaultLabel == UwPay.T("Gang.Demand.Refuse")), "no gizmos before it is made");
            d.openTick = Find.TickManager.TicksGame;
            Offers.Tick(Find.TickManager.TicksGame);
            t.Check(d.Announced && !FacKit.NodeTreeOpen(), "the demand is made: letter, no dialog");
            var dl = Find.LetterStack.LettersListForReading.FirstOrDefault(l => l.ID == d.letterId);
            t.Check(dl != null && dl.def == LetterDefOf.NeutralEvent && dl.lookTargets != null && dl.lookTargets.PrimaryTarget.Thing == gang[0], "standard letter that jumps to the leader");
            var lg = gang[0].GetGizmos().OfType<Command_Action>().ToList();
            var pay = lg.FirstOrDefault(g => g.defaultLabel == UwPay.T("Gang.Demand.Pay", d.price.ToStringShort()));
            t.Check(pay != null && !pay.Disabled && lg.Any(g => g.defaultLabel == UwPay.T("Gang.Demand.Refuse")), "the leader carries Pay and Refuse");
            t.Check(!gang[1].GetGizmos().OfType<Command_Action>().Any(g => g.defaultLabel == UwPay.T("Gang.Demand.Refuse")), "the other members do not");
            int gw0 = f.PlayerGoodwill; till0 = UwKit.Till(map);
            pay.action();
            t.Check(till0 - UwKit.Till(map) == d.price && !c.Offers.Contains(d), "Pay: the fee left the till, the demand is closed");
            t.Check(d.faction.PlayerGoodwill >= gw0, "paying does not cost goodwill");
            // an unanswered demand counts as a refusal
            var d2 = Gangs.PlanDemand(map, d.faction, gang[0], Find.TickManager.TicksGame);
            Offers.Tick(Find.TickManager.TicksGame);
            t.Check(d2.Announced, "a second demand is made");
            int gw1 = d.faction.PlayerGoodwill; float w1 = Underworld.Whispers;
            d2.expireTick = Find.TickManager.TicksGame - 1;
            Offers.Tick(Find.TickManager.TicksGame);
            t.Check(!c.Offers.Contains(d2) && d.faction.PlayerGoodwill <= gw1 && d.faction.PlayerGoodwill >= gw1 - 10 && Underworld.Whispers > w1 + 1.5f, $"expired: taken as a refusal (goodwill {gw1} -> {d.faction.PlayerGoodwill}, capped)");
            // the game runs a while with a fresh demand and a boat: still no dialog of ours
            var d3 = Gangs.PlanDemand(map, d.faction, gang[0], Find.TickManager.TicksGame + 500);
            Smuggler.Fire(map, force: true);
            for (int i = 0; i < 6; i++) { yield return 500; t.Check(!FacKit.NodeTreeOpen(), "no dialog after " + (i + 1) * 500 + " ticks"); }
            t.Check(d3.Announced || !c.Offers.Contains(d3), "the planned demand was made by the ticker (or ended with its gang)");
            FacKit.CloseOffers();
            t.Check(Find.LetterStack.LettersListForReading.Count <= lettersBefore + 2, "closed offers take their letters with them");
            c.GangVisits = visits0;
            UwKit.End();
        }
    }
}
#endif
