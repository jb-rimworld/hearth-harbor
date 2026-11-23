// SPDX-License-Identifier: MIT
// In-game suites of the 1.1.0 fix round 2: solo guests like double beds (F03) and guest life cues (F31). Built only with HH_TESTS.
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using Hearth.Life;
using Hearth.ShopsFood;
using Hearth.Testing;
using RimWorld;
using Verse;
using Verse.AI;

namespace Hearth.Guests
{
    static class Fix2Kit
    {
        /// <summary>Spawns a wooden double bed next to the first bed of room A (x minX+2, top row) and marks it as a guest bed.</summary>
        public static Building_Bed AddDouble(InnUxKit.Pair site, Map map)
        {
            var def = DefDatabase<ThingDef>.GetNamedSilentFail("DoubleBed");
            if (def == null) return null;
            var t = ThingMaker.MakeThing(def, ThingDefOf.WoodLog);
            t.SetFaction(Faction.OfPlayer);
            var bed = (Building_Bed)GenSpawn.Spawn(t, new IntVec3(site.A.minX + 2, 0, site.A.maxZ - (def.size.z - 1)), map, Rot4.North, WipeMode.Vanish);
            site.Built.Add(bed); site.Beds.Add(bed);
            map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
            GuestBeds.Comp(bed).SetGuestBed(true, byPlayer: false);
            return bed;
        }

        public static void Price(Building_Bed b, int silver)
        {
            var c = GuestBeds.Comp(b); if (c == null) return;
            c.PriceMode = BedPriceMode.Manual; c.ManualPrice = silver;
        }
    }

    /// <summary>F03 acceptance: a guest travelling alone likes a double bed a little (utility bonus, a "roomy bed" once per stay).</summary>
    public sealed class InnSoloDoubleSuite : HearthTestSuite
    {
        public override string Name => "inn.solodouble";
        public override ModuleId Module => ModuleId.Inn;
        public override int MaxTicks => 8000;

        public override IEnumerable<int> Run(TestContext t)
        {
            InnTestKit.FastTime();
            var map = t.Map; var reg = GuestRegistry.Instance;
            MapComponent_HH_GuestScheduler.SuspendedForTests = true;
            // ---- pure numbers
            t.Check(Math.Abs(GuestEconomy.SoloDoubleBonus(0) - 8f) < 1e-4f && Math.Abs(GuestEconomy.SoloDoubleBonus(100) - 15f) < 1e-4f, "solo double-bed bonus: 8 (poor) to 15 (rich)");
            t.Check(GuestEconomy.SoloDoubleBonus(30) > GuestEconomy.SoloDoubleBonus(10) && GuestEconomy.SoloDoubleBonus(250) <= 15.0001f, "grows with wealth and is capped");
            t.Check(GuestEconomy.RoomyBedSatisfaction > 0f && GuestEconomy.RoomyBedSatisfaction < 0.06f, "roomy bed is a small plus (smaller than a couple's shared bed)");
            t.Check("HH.Inn.Event.RoomyBed".CanTranslate(), "roomy bed text is keyed");
            if (DefDatabase<ThingDef>.GetNamedSilentFail("DoubleBed") == null) { t.Note("no DoubleBed def: suite skipped"); yield break; }

            // ---- one single and one double bed in the same room at the same price: the lone guest takes the double bed
            var site = InnUxKit.Build(map, ThingDefOf.Bed, 1, desk: true);
            var single = site.Beds[0];
            GuestBeds.Comp(single).SetGuestBed(true, byPlayer: false);
            var dbl = Fix2Kit.AddDouble(site, map);
            Fix2Kit.Price(single, 20); Fix2Kit.Price(dbl, 20);
            GuestBedPricing.ClearCache();
            yield return 10;
            t.Check(dbl != null && single.GetRoom() == dbl.GetRoom(), "both beds stand in one room");
            var g = V110Kit.Arrive(map, 1, 1, 600, spouses: false);
            t.Check(g != null, "a lone guest arrived");
            GuestRecord r = null;
            try
            {
                if (g == null) yield break;
                r = reg.Members(g).First();
                r.Pawn.relations?.ClearAllRelations();
                reg.CheckIn(r.Pawn, site.Desk);
                t.Check(r.Bed == dbl, "with equal rooms and prices the lone guest takes the double bed (" + r.Bed?.def.defName + ")");
                t.Check(!r.Events.Any(e => e.Contains("steep")), "and does not find it steep");

                if (r.Bed != dbl) yield break;
                // ---- lying down in it for real (no test override: a real LayDown job, the registry's own pass notices it): pleased once
                var pawn = r.Pawn;
                reg.BedFxPassForTests();                            // first look only records "not in bed"
                float before = r.ServiceSum;
                t.Check(!r.RoomyBedDone, "not pleased before lying down");
                var stand = GenAdj.CellsAdjacent8Way(dbl).FirstOrDefault(c => c.InBounds(map) && c.Standable(map) && c.GetRoom(map) == dbl.GetRoom());
                if (stand.IsValid) { pawn.jobs.StopAll(); pawn.Position = stand; pawn.Notify_Teleported(false); }
                if (pawn.needs?.rest != null) pawn.needs.rest.CurLevel = 0.2f;
                pawn.jobs.StartJob(JobMaker.MakeJob(JobDefOf.LayDown, dbl), JobCondition.InterruptForced);
                int waited = 0;
                while (waited < 1500 && pawn.CurrentBed() != dbl) { yield return 10; waited += 10; }
                t.Check(pawn.CurrentBed() == dbl, $"the guest lies in the double bed after {waited} ticks ({pawn.CurJobDef?.defName})");
                yield return 40;                                    // the registry's own pass runs every 30 ticks
                t.Check(r.RoomyBedDone && Math.Abs(r.ServiceSum - before - GuestEconomy.RoomyBedSatisfaction) < 1e-4f, $"lying down alone in the double bed (real job): roomy bed (+{r.ServiceSum - before:0.000})");
                t.Check(r.Events.Any(e => e.Contains("roomy")), "the stay log mentions the roomy bed");
                pawn.jobs.StopAll();
                // ---- getting up and lying down again: no second time
                var lying = new HashSet<Pawn>();
                GuestRegistry.InBedOverride = (p, b) => lying.Contains(p) && b == r.Bed;
                reg.BedFxPassForTests(); lying.Add(pawn); reg.BedFxPassForTests();
                t.Check(Math.Abs(r.ServiceSum - before - GuestEconomy.RoomyBedSatisfaction) < 1e-4f, "only once per stay");
            }
            finally
            {
                GuestRegistry.InBedOverride = null;
                InnTestKit.RemoveAllGuests(map);
                InnUxKit.Teardown(site, map);
                MapComponent_HH_GuestScheduler.SuspendedForTests = false;
            }
        }
    }

    /// <summary>F31: every guest life cue fires exactly once per event, respects its pause and the setting core.lifeFx.</summary>
    public sealed class InnLifeFxSuite : HearthTestSuite
    {
        public override string Name => "inn.lifefx";
        public override ModuleId Module => ModuleId.Inn;
        public override int MaxTicks => 9000;

        static int F(LifeCue c) => GuestLifeFx.Fired[(int)c];

        public override IEnumerable<int> Run(TestContext t)
        {
            InnTestKit.FastTime();
            var map = t.Map; var reg = GuestRegistry.Instance;
            MapComponent_HH_GuestScheduler.SuspendedForTests = true;
            t.Check(HSettings.GetBool(GuestLifeFx.SettingKey), "core.lifeFx is on by default");
            t.Check(("HH.Setting." + GuestLifeFx.SettingKey).CanTranslate() && ("HH.Setting." + GuestLifeFx.SettingKey + ".Desc").CanTranslate(), "setting label and description are keyed");
            t.Check(LifeFxDefOf.HH_Fleck_Life_Suitcase != null && LifeFxDefOf.HH_Fleck_Life_Key != null && LifeFxDefOf.HH_Fleck_Life_Coins != null && LifeFxDefOf.HH_Fleck_Life_TipCoin != null
                    && LifeFxDefOf.HH_Fleck_Life_Wave != null && LifeFxDefOf.HH_Fleck_Life_Stars1 != null && LifeFxDefOf.HH_Fleck_Life_Stars5 != null && LifeFxDefOf.HH_Fleck_Life_Puff != null
                    && LifeFxDefOf.HH_Fleck_Life_Sparkle != null && LifeFxDefOf.HH_Fleck_Life_Heart != null, "fleck defs resolved");
            t.Check(LifeFxDefOf.HH_Life_Arrival != null && LifeFxDefOf.HH_Life_CheckIn != null && LifeFxDefOf.HH_Life_Pay != null && LifeFxDefOf.HH_Life_Tip != null
                    && LifeFxDefOf.HH_Life_Farewell != null && LifeFxDefOf.HH_Life_FiveStars != null, "sound defs resolved");
            t.Check(GuestLifeFx.StarsFor("great") == 5 && GuestLifeFx.StarsFor("good") == 4 && GuestLifeFx.StarsFor("fine") == 3 && GuestLifeFx.StarsFor("poor") == 2 && GuestLifeFx.StarsFor("bad") == 1 && GuestLifeFx.StarsFor("joined") == 0, "stars from the outcome");
            var dblDef = DefDatabase<ThingDef>.GetNamedSilentFail("DoubleBed");
            var vroom = VenueTestKit.Build(map);                   // a tavern for the real tip path (built first: the inn site keeps clear of it)
            var vsign = VenueTestKit.SpawnSign(map, vroom, HHVenueDefOf.HH_VenueSign, HHVenueDefOf.HH_Venue_Tavern);
            Venues.MarkDirty(map);
            var site = InnUxKit.Build(map, ThingDefOf.Bed, 2, desk: true);
            InnTestKit.FastTime();
            foreach (var b in site.Beds) GuestBeds.Comp(b).SetGuestBed(true, byPlayer: false);
            yield return 10;
            var lying = new HashSet<Pawn>();
            var extra = new List<Pawn>();
            try
            {
                // ---- arrival: one chime per group, when the first of them is on the map (drop pods open later)
                int arr0 = F(LifeCue.Arrival);
                var g = V110Kit.Arrive(map, 2, 1, 600, spouses: false);
                t.Check(g != null, "a group of two arrived");
                if (g == null) yield break;
                int waited = 0;
                while (waited < 900 && F(LifeCue.Arrival) == arr0) { yield return 20; waited += 20; }
                t.Check(F(LifeCue.Arrival) == arr0 + 1, $"one arrival cue for the group of two (after {waited} ticks, by {(g.ByPod ? "drop pod" : "foot")})");
                yield return 200;
                t.Check(F(LifeCue.Arrival) == arr0 + 1, "and not a second one");
                t.Check(GuestLifeFx.TrackedCounts.arrivals == 0, "the pending arrival is done");

                // ---- check-in at the desk: one cue each (the first night is charged at the same time: one pay cue each)
                yield return 61;
                var ms = reg.Members(g).ToList();
                foreach (var m in ms) m.Pawn.relations?.ClearAllRelations();
                int ci0 = F(LifeCue.CheckIn), pay0 = F(LifeCue.Pay);
                reg.CheckIn(ms[0].Pawn, site.Desk);
                t.Check(F(LifeCue.CheckIn) == ci0 + 1, "check-in at the desk: one cue");
                t.Check(F(LifeCue.Pay) == pay0 + (ms[0].NightsPaid > 0 && ms[0].NightPrice.Micro > 0 ? 1 : 0), "paying the first night: one pay cue (" + ms[0].NightPrice + ")");
                reg.CheckIn(ms[1].Pawn, site.Desk);
                t.Check(F(LifeCue.CheckIn) == ci0 + 2, "the second guest: one more");
                reg.CheckIn(ms[1].Pawn, site.Desk);
                t.Check(F(LifeCue.CheckIn) == ci0 + 2, "checking in twice does nothing");

                // ---- paying a bill: one cue, then a pause per pawn
                yield return 61;
                var a = ms[0].Pawn;
                GuestLifeFx.ResetPauses(a);
                pay0 = F(LifeCue.Pay);
                bool paid = GuestApi.ChargeGuest(a, Money.FromSilver(1), "inn.test", "test bill");
                t.Check(paid && F(LifeCue.Pay) == pay0 + 1, "a paid bill: one pay cue");
                GuestApi.ChargeGuest(a, Money.FromSilver(1), "inn.test", "test bill 2");
                t.Check(F(LifeCue.Pay) == pay0 + 1, "a second bill right after: no second cue (pause)");

                // ---- a tip through the real VenueService.TryTip (charged through Payments like any bill): the coin flip, not the pay cue
                yield return 61;
                GuestLifeFx.ResetPauses(a);
                pay0 = F(LifeCue.Pay); int tip0 = F(LifeCue.Tip);
                var server = map.mapPawns.FreeColonistsSpawned.FirstOrDefault();
                var venue = VenueApi.VenueOfSign(map, vsign);
                t.Check(venue != null && server != null, "a tavern and a server for the tip");
                VenueService.ForceTipForTests = Money.FromSilver(1);
                Money tipped = Money.Zero;
                try { if (venue != null) tipped = VenueService.TryTip(server, a, venue, Money.FromSilver(10), "food", 1f); }
                finally { VenueService.ForceTipForTests = null; }
                t.Check(tipped.IsPositive, "the tip was charged (" + tipped + ")");
                t.Check(F(LifeCue.Tip) == tip0 + 1 && F(LifeCue.Pay) == pay0, $"a tip: one coin flip and no pay cue (tip +{F(LifeCue.Tip) - tip0}, pay +{F(LifeCue.Pay) - pay0})");
                t.Check(GuestLifeFx.TipCharging == null, "the tip marker is cleared after the charge");
                VenueService.ForceTipForTests = Money.FromSilver(1);
                try { if (venue != null) VenueService.TryTip(server, a, venue, Money.FromSilver(10), "food", 1f); }
                finally { VenueService.ForceTipForTests = null; }
                t.Check(F(LifeCue.Tip) == tip0 + 1 && F(LifeCue.Pay) == pay0, "a second tip right after: no second flip, and still no pay cue");

                // ---- the setting switches everything off
                yield return 61;
                GuestLifeFx.ResetPauses(a);
                HSettings.Set(GuestLifeFx.SettingKey, 0f);
                pay0 = F(LifeCue.Pay); tip0 = F(LifeCue.Tip);
                GuestApi.ChargeGuest(a, Money.FromSilver(1), "inn.test", "test bill 3");
                GuestLifeFx.NoteTip(a, server);
                t.Check(F(LifeCue.Pay) == pay0 && F(LifeCue.Tip) == tip0, "core.lifeFx off: no cues");
                HSettings.Set(GuestLifeFx.SettingKey, 1f);

                // ---- departure: each guest waves once, the review shows a moment later
                yield return 61;
                int fw0 = F(LifeCue.Farewell), rv0 = F(LifeCue.Review);
                int spawned = ms.Count(m => m.Pawn.Spawned);
                reg.DepartGroup(g, "planned");
                t.Check(F(LifeCue.Farewell) == fw0 + spawned, $"departure: one wave per guest ({F(LifeCue.Farewell) - fw0} of {spawned})");
                t.Check(F(LifeCue.Review) == rv0, "the review is not shown at once");
                yield return GuestLifeFx.ReviewDelay + GuestLifeFx.ReviewStagger * spawned + 30;
                t.Check(F(LifeCue.Review) == rv0 + spawned, $"then one review per guest ({F(LifeCue.Review) - rv0}), last {GuestLifeFx.LastStars} stars");
                t.Check(GuestLifeFx.LastStars >= 1 && GuestLifeFx.LastStars <= 5, "1 to 5 stars");
                reg.DepartGroup(g, "planned");
                yield return GuestLifeFx.ReviewDelay + 30;
                t.Check(F(LifeCue.Farewell) == fw0 + spawned && F(LifeCue.Review) == rv0 + spawned, "departing twice changes nothing");
                InnTestKit.RemoveAllGuests(map);
                yield return 10;

                // ---- a big group on screen: the map's budget (5 a second) delays cues instead of dropping them, reviews come one after another
                var crowd = Hearth.Casino.Tests.Kit.Visitors(t, 7, 0, map.Center);
                extra.AddRange(crowd);
                if (crowd.Count >= 6)
                {
                    foreach (var q in crowd) GuestLifeFx.ResetPauses(q);
                    yield return 61;
                    GuestLifeFx.ForceVisible = true;
                    try
                    {
                        int w0 = F(LifeCue.Farewell), r0 = F(LifeCue.Review);
                        GuestLifeFx.NoteDeparture(-4242, crowd.Select(q => (q, 4, true)).ToList());
                        t.Check(F(LifeCue.Farewell) - w0 <= GuestLifeFx.MapCuesPerSecond, $"on screen at most {GuestLifeFx.MapCuesPerSecond} waves in the same second ({F(LifeCue.Farewell) - w0})");
                        yield return 90;
                        t.Check(F(LifeCue.Farewell) == w0 + crowd.Count, $"the rest wave a moment later: {F(LifeCue.Farewell) - w0} of {crowd.Count}");
                        yield return GuestLifeFx.ReviewDelay + GuestLifeFx.ReviewStagger * crowd.Count + 90;
                        t.Check(F(LifeCue.Review) == r0 + crowd.Count, $"every review shows: {F(LifeCue.Review) - r0} of {crowd.Count}");
                    }
                    finally { GuestLifeFx.ForceVisible = null; }
                    GuestLifeFx.ForceVisible = false;
                    try
                    {
                        foreach (var q in crowd) GuestLifeFx.ResetPauses(q);
                        int w1 = F(LifeCue.Farewell);
                        GuestLifeFx.NoteDeparture(-4243, crowd.Select(q => (q, 0, true)).ToList());
                        t.Check(F(LifeCue.Farewell) == w1 + crowd.Count, "off screen the budget is not spent: every wave counts at once");
                    }
                    finally { GuestLifeFx.ForceVisible = null; }
                    foreach (var q in crowd) GuestLifeFx.ResetPauses(q);
                }
                else t.Note("no visitors could be made: budget check skipped");
                foreach (var q in crowd) if (q != null && !q.Destroyed) q.Destroy();

                // ---- a couple lies down in their shared double bed: one heart, once a night
                if (dblDef == null) { t.Note("no DoubleBed def: heart check skipped"); yield break; }
                var dbl = Fix2Kit.AddDouble(site, map);
                yield return 61;
                var cg = V110Kit.Arrive(map, 2, 1, 900, spouses: true);
                if (cg == null) { t.Note("no couple arrived: heart check skipped"); yield break; }
                var cm = reg.Members(cg).ToList();
                if (V110Kit.IdeoForbids(cm[0], cm[1])) { t.Note("their ideology forbids sharing a bed: heart check skipped"); yield break; }
                V110Kit.CheckInAll(cm, site.Desk);
                t.Check(cm[0].Bed != null && cm[0].Bed == cm[1].Bed && cm[0].Bed.SleepingSlotsCount >= 2, "the couple shares the double bed");
                GuestRegistry.InBedOverride = (p, b) => lying.Contains(p) && b == GuestRegistry.Instance.Get(p)?.Bed;
                reg.BedFxPassForTests();
                yield return 61;
                int h0 = F(LifeCue.Heart);
                lying.Add(cm[0].Pawn); reg.BedFxPassForTests();
                t.Check(F(LifeCue.Heart) == h0, "one partner alone in bed: no heart yet");
                lying.Add(cm[1].Pawn); reg.BedFxPassForTests();
                t.Check(F(LifeCue.Heart) == h0 + 1, "both lie down: one heart");
                t.Check(!cm[0].RoomyBedDone && !cm[1].RoomyBedDone, "a shared bed is no 'roomy bed'");
                lying.Clear(); reg.BedFxPassForTests();
                lying.Add(cm[1].Pawn); lying.Add(cm[0].Pawn); reg.BedFxPassForTests();
                t.Check(F(LifeCue.Heart) == h0 + 1, "getting up and lying down again the same night: no second heart");
            }
            finally
            {
                GuestRegistry.InBedOverride = null;
                HSettings.Set(GuestLifeFx.SettingKey, 1f);
                GuestLifeFx.TipCharging = null; GuestLifeFx.ForceVisible = null; VenueService.ForceTipForTests = null;
                InnTestKit.RemoveAllGuests(map);
                InnUxKit.Teardown(site, map);
                VenueTestKit.Teardown(map, vroom);
                foreach (var q in extra) if (q != null && !q.Destroyed) q.Destroy();
                MapComponent_HH_GuestScheduler.SuspendedForTests = false;
            }
        }
    }
}
#endif
