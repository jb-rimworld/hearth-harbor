// SPDX-License-Identifier: MIT
// In-game suites for the 1.1.0 guests package: couples and double beds (F02/F03), the Convert handling (F04), the one bed button (F10),
// and the bigger guest tab / door policy window (F35). Built only with HH_TESTS.
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Hearth.Shell;
using Hearth.Testing;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Hearth.Guests
{
    static class V110Kit
    {
        /// <summary>Two (or more) guests of one group; the first two become spouses when <paramref name="spouses"/> is set (before they check in).</summary>
        public static GuestGroup Arrive(Map map, int size, int nights, int budget, bool spouses)
        {
            var f = InnTestKit.GuestFaction(map);
            if (f == null) return null;
            var g = GuestArrival.TryFire(map, new GuestArrivalRequest { Faction = f, Size = size, Kind = StayKind.Overnight, Nights = nights, Silent = true, ForceBudget = Money.FromSilver(budget) });
            if (g == null) return null;
            var ms = GuestRegistry.Instance.Members(g).ToList();
            if (spouses && ms.Count > 1)
            {
                foreach (var m in new[] { ms[0], ms[1] }) { m.Pawn.relations?.ClearAllRelations(); }
                ms[0].Pawn.relations.AddDirectRelation(PawnRelationDefOf.Spouse, ms[1].Pawn);
            }
            return g;
        }

        public static bool IdeoForbids(GuestRecord a, GuestRecord b)
            => ModsConfig.IdeologyActive && (!BedUtility.WillingToShareBed(a.Pawn, b.Pawn) || !BedUtility.WillingToShareBed(b.Pawn, a.Pawn));

        /// <summary>Hands the front desk's work to nobody: the guests check in at once (no walking, no host).</summary>
        public static void CheckInAll(IEnumerable<GuestRecord> ms, Building_FrontDesk desk) { foreach (var m in ms) GuestRegistry.Instance.CheckIn(m.Pawn, desk); }
    }

    /// <summary>F02 + F03: a couple is booked together in one double bed and pays half each; double beds earn more; the wish for a double bed.</summary>
    public sealed class InnCouplesSuite : HearthTestSuite
    {
        public override string Name => "inn.couples";
        public override ModuleId Module => ModuleId.Inn;
        public override int MaxTicks => 30000;
        public override IEnumerable<int> Run(TestContext t)
        {
            InnTestKit.FastTime();
            var map = t.Map; var reg = GuestRegistry.Instance;
            MapComponent_HH_GuestScheduler.SuspendedForTests = true;
            // ---- pure maths
            float f0 = HSettings.Get(InnSettings.DoubleBedFactor);
            t.Check(Math.Abs(f0 - 1.6f) < 0.001f, "double bed factor defaults to 1.6: " + f0);
            t.Check(Math.Abs(GuestBedPricing.SlotFactor(1) - 1f) < 1e-5f && Math.Abs(GuestBedPricing.SlotFactor(2) - 1.6f) < 1e-4f && Math.Abs(GuestBedPricing.SlotFactor(3) - 2.2f) < 1e-4f, "slot factor 1 / 1.6 / 2.2");
            GuestRegistry.SplitPrice(Money.FromSilver(35), out var s1, out var s2);
            t.Check(s1.WholeSilver == 18 && s2.WholeSilver == 17, "35 silver splits 18 + 17: " + s1 + " + " + s2);
            GuestRegistry.SplitPrice(Money.FromSilver(30), out s1, out s2);
            t.Check(s1.WholeSilver == 15 && s2.WholeSilver == 15, "30 silver splits 15 + 15");
            GuestRegistry.SplitPrice(Money.Zero, out s1, out s2);
            t.Check(s1.Micro == 0 && s2.Micro == 0, "a free bed stays free for both");
            t.Check(InnSettings.CoupleWish != null && HSettings.GetBool(InnSettings.CoupleWish), "couple wish is on by default");
            var dbl = DefDatabase<ThingDef>.GetNamedSilentFail("DoubleBed");
            if (dbl == null) { t.Note("no DoubleBed def: suite skipped"); yield break; }

            // ---- a double bed pays more than a single one, in the same room
            var site = InnUxKit.Build(map, dbl, 1, desk: true);
            var bed = site.Beds[0];
            GuestBeds.Comp(bed).SetGuestBed(true, byPlayer: false);
            yield return 10;
            var q = GuestBedPricing.Quote(bed);
            t.Check(q != null && q.Slots == 2 && Math.Abs(q.SlotMult - f0) < 0.001f, $"quote knows the bed has two slots (x{q?.SlotMult:0.00})");
            t.Check(q != null && q.Breakdown.Contains("double bed"), "price breakdown mentions the double bed: " + q?.Breakdown);
            HSettings.Set(InnSettings.DoubleBedFactor, 1f); GuestBedPricing.ClearCache(); var plain = GuestBedPricing.Quote(bed).Price;
            HSettings.Set(InnSettings.DoubleBedFactor, 2f); GuestBedPricing.ClearCache(); var twice = GuestBedPricing.Quote(bed).Price;
            HSettings.Set(InnSettings.DoubleBedFactor, f0); GuestBedPricing.ClearCache();
            t.Check(twice > plain && Math.Abs(twice.Silver - 2 * plain.Silver) <= 5.01, $"the factor multiplies the price: x1 = {plain}, x2 = {twice}");
            var full = GuestBedPricing.Quote(bed).Price;
            t.Check(full >= plain && full <= twice, $"default factor lies between: {full}");

            // ---- a couple arrives: booked together at the first check-in, half each
            var g = V110Kit.Arrive(map, 2, 2, 600, spouses: true);
            t.Check(g != null, "couple arrived");
            if (g == null) { InnUxKit.Teardown(site, map); yield break; }
            var ms = reg.Members(g).ToList(); var a = ms[0]; var b = ms[1];
            if (V110Kit.IdeoForbids(a, b)) { t.Note("their ideology forbids sharing a bed even for spouses: couple checks skipped"); }
            else
            {
                reg.CheckIn(a.Pawn, site.Desk);   // only the first partner checks in: the second must already be booked
                t.Check(a.Bed == bed && b.Bed == bed, "both are booked into the double bed after ONE check-in");
                t.Check(a.WantsDouble && b.WantsDouble, "both wished for a double bed");
                t.Check(!b.CheckedIn, "the partner has not checked in yet, but has the bed");
                t.Check(Math.Abs((a.NightPrice + b.NightPrice).Silver - full.Silver) < 0.01 && Math.Abs(a.NightPrice.Silver - b.NightPrice.Silver) <= 1.01, $"each pays half, together exactly the bed's price: {a.NightPrice} + {b.NightPrice} = {full}");
                t.Check(a.NightPrice + b.NightPrice < full * 1.01, "no 1.3x surcharge anywhere");
                t.Check(bed.OwnersForReading.Contains(a.Pawn) && bed.OwnersForReading.Contains(b.Pawn), "both own the bed");
                reg.CheckIn(b.Pawn, site.Desk);
                t.Check(b.CheckedIn && b.Bed == bed, "the partner's own check-in keeps the booking");
                t.Check(a.DoubleWishDone && b.DoubleWishDone, "the wish is settled for both");
                t.Check(a.Events.Any(e => e.Contains("double bed")) && a.Events.Any(e => e.Contains("Sleeps next to")), "the stay log tells it: " + string.Join(" | ", a.Events.Skip(Math.Max(0, a.Events.Count - 3))));
                t.Check(a.NightsPaid == 1 && b.NightsPaid == 1, "first night paid by each");
                float satA = a.ServiceSum;
                t.Check(satA > 0f, "granting the wish raised the service score a little: " + satA);
                var verdictThought = InnDefOf.HH_SleptTogether;
                t.Check(verdictThought != null && verdictThought.stages.Count == 1 && verdictThought.stages[0].baseMoodEffect > 0f, "slept-together thought exists and is positive");
            }
            InnTestKit.RemoveAllGuests(map);
            InnUxKit.Teardown(site, map);
            yield return 5;

            // ---- a couple whose partner arrives later: the first one booked alone, the second moves in and the price is split
            site = InnUxKit.Build(map, dbl, 1, desk: true);
            bed = site.Beds[0]; GuestBeds.Comp(bed).SetGuestBed(true, byPlayer: false);
            yield return 10;
            full = GuestBedPricing.Quote(bed).Price;
            g = V110Kit.Arrive(map, 2, 2, 600, spouses: false);
            if (g != null)
            {
                ms = reg.Members(g).ToList(); a = ms[0]; b = ms[1];
                a.Pawn.relations?.ClearAllRelations(); b.Pawn.relations?.ClearAllRelations();
                reg.CheckIn(a.Pawn, site.Desk);
                t.Check(a.Bed == bed && a.NightPrice == full, $"a lone guest in a double bed pays the whole price: {a.NightPrice} of {full}");
                a.Pawn.relations.AddDirectRelation(PawnRelationDefOf.Lover, b.Pawn);
                if (V110Kit.IdeoForbids(a, b)) t.Note("ideology forbids sharing: late partner check skipped");
                else
                {
                    reg.CheckIn(b.Pawn, site.Desk);
                    t.Check(b.Bed == bed, "the late partner moves into the same bed");
                    t.Check(Math.Abs((a.NightPrice + b.NightPrice).Silver - full.Silver) < 0.01, $"from now on the price is split: {a.NightPrice} + {b.NightPrice} = {full}");
                    t.Check(a.Events.Any(e => e.Contains("moved into the bed")), "the first guest's log says the price was split");
                }
            }
            InnTestKit.RemoveAllGuests(map);
            InnUxKit.Teardown(site, map);
            yield return 5;

            // ---- no double bed: two single beds, the wish is missed (a little unhappy) and the guest overview says what to build
            var site2 = InnTestKit.BuildRoom(map, 2, true, 6, 4);
            InnTestKit.MarkGuestBeds(site2, true);
            yield return 10;
            g = V110Kit.Arrive(map, 2, 2, 600, spouses: true);
            if (g != null)
            {
                ms = reg.Members(g).ToList(); a = ms[0]; b = ms[1];
                if (V110Kit.IdeoForbids(a, b)) t.Note("ideology forbids sharing: single-bed couple check skipped");
                else
                {
                    V110Kit.CheckInAll(ms, site2.Desk);
                    t.Check(a.Bed != null && b.Bed != null && a.Bed != b.Bed, "with only single beds the couple gets two beds");
                    t.Check(a.DoubleWishDone && b.DoubleWishDone, "the wish is settled (missed) once both have checked in");
                    t.Check(a.Events.Any(e => e.Contains("Wanted a double bed")), "the stay log mentions the missed wish");
                    t.Check(a.Pawn.needs.mood.thoughts.memories.GetFirstMemoryOfDef(InnDefOf.HH_WantedDoubleBed) != null, "a small unhappy memory");
                    t.Check(GuestOverviewPage.Problems(a).Any(p => p.key == "double"), "the guest overview tells the player to build a double bed");
                    // switching the wish off removes every effect
                }
            }
            InnTestKit.RemoveAllGuests(map);
            yield return 5;

            // ---- review fix: a partner who never got a bed is no "missed double bed" (no penalty for either of them)
            g = V110Kit.Arrive(map, 2, 2, 600, spouses: true);
            if (g != null)
            {
                ms = reg.Members(g).ToList(); a = ms[0]; b = ms[1];
                if (V110Kit.IdeoForbids(a, b)) t.Note("ideology forbids sharing: bedless partner check skipped");
                else
                {
                    reg.CheckIn(a.Pawn, site2.Desk);
                    if (b.Bed != null) reg.ReleaseBedForTests(b);
                    b.CheckedIn = true; a.CheckedIn = true;
                    reg.ResolveDoubleWishForTests(a);
                    t.Check(!a.DoubleWishDone && !b.DoubleWishDone, "no verdict while the partner has no bed");
                    t.Check(a.Pawn.needs.mood.thoughts.memories.GetFirstMemoryOfDef(InnDefOf.HH_WantedDoubleBed) == null, "no unhappy memory for a bedless partner");
                }
            }
            InnTestKit.RemoveAllGuests(map);
            InnTestKit.Teardown(site2, map);
            MapComponent_HH_GuestScheduler.SuspendedForTests = false;
        }
    }

    /// <summary>F04: the Convert handling.</summary>
    public sealed class InnConvertSuite : HearthTestSuite
    {
        public override string Name => "inn.convert";
        public override ModuleId Module => ModuleId.Inn;
        public override int MaxTicks => 30000;
        public override IEnumerable<int> Run(TestContext t)
        {
            InnTestKit.FastTime();
            var map = t.Map; var reg = GuestRegistry.Instance;
            MapComponent_HH_GuestScheduler.SuspendedForTests = true;
            t.Check((int)HandlingMode.Convert == 3 && (int)HandlingMode.Recruit == 2, "Convert is appended as 3 (saved ints never move)");
            if (!ModsConfig.IdeologyActive)
            {
                var guestless = HandlingRules.All(null);
                t.Check(guestless.Count == 3 && !guestless.Contains(HandlingMode.Convert), "without Ideology only three modes are offered");
                t.Check(HandlingRules.Next(null, HandlingMode.Recruit) == HandlingMode.Basics, "and the cycle skips Convert");
                yield break;
            }
            var site = InnTestKit.BuildRoom(map, 2, true, 6, 4);
            InnTestKit.MarkGuestBeds(site, true);
            yield return 10;
            var g = V110Kit.Arrive(map, 2, 1, 400, spouses: false);
            t.Check(g != null, "guests arrived");
            if (g == null) { InnTestKit.Teardown(site, map); yield break; }
            var ms = reg.Members(g).ToList(); var a = ms[0]; var b = ms[1];
            V110Kit.CheckInAll(ms, site.Desk);
            var primary = Faction.OfPlayer.ideos?.PrimaryIdeo;
            t.Check(primary != null, "the colony has a primary ideoligion");
            var other = Find.IdeoManager.IdeosListForReading.FirstOrDefault(i => i != primary);
            if (primary == null || other == null) { t.Note("no second ideoligion in this world: conversion checks skipped"); InnTestKit.RemoveAllGuests(map); InnTestKit.Teardown(site, map); yield break; }
            foreach (var m in ms) if (m.Pawn.ideo.Ideo == primary || m.Pawn.ideo.Ideo == null) m.Pawn.ideo.SetIdeo(other);
            foreach (var m in ms) m.Pawn.ideo.OffsetCertainty(0.9f - m.Pawn.ideo.Certainty);
            // the modes on offer
            var modes = HandlingRules.All(a.Pawn);
            t.Check(modes.Count == 4 && modes.Contains(HandlingMode.Convert), "four modes are offered for a guest of another ideoligion");
            t.Check(HandlingRules.Next(a.Pawn, HandlingMode.Recruit) == HandlingMode.Convert && HandlingRules.Next(a.Pawn, HandlingMode.Convert) == HandlingMode.Basics, "Recruit → Convert → Basics");
            // the gizmo cycles through all four
            a.Handling = HandlingMode.Basics;
            for (int i = 0; i < 3; i++)
            {
                var giz = a.Pawn.GetGizmos().OfType<Command_Action>().First(x => x.defaultLabel != null && x.defaultLabel.StartsWith("Handling"));
                giz.action();
            }
            t.Check(a.Handling == HandlingMode.Convert, "three clicks on the guest gizmo: Basics → Accompany → Recruit → Convert (" + a.Handling + ")");
            var cgiz = a.Pawn.GetGizmos().OfType<Command_Action>().First(x => x.defaultLabel != null && x.defaultLabel.StartsWith("Handling"));
            t.Check(cgiz.defaultLabel.Contains("Convert") && cgiz.defaultDesc.Contains("Certainty"), "the gizmo names the mode and shows the guest's certainty");
            cgiz.action();
            t.Check(a.Handling == HandlingMode.Basics, "one more click goes back to Basics");
            a.Handling = HandlingMode.Convert;
            // the table / tab status
            var st = GuestOverviewPage.StatusFor(a.Pawn);
            t.Check(st != null && st.RecruitProgress >= 0f && st.RecruitProgress <= 1f && (st.RecruitLine ?? "").Contains("Certainty"), "the tab shows the certainty bar: " + st?.RecruitLine);
            bool drew = true; try { ITab_HH_Guest.DrawBody(470f, a.Pawn, a); } catch (Exception e) { drew = false; t.Note(e.ToString()); }
            t.Check(drew, "guest tab draws in Convert mode");
            // a talk that changes nothing / a talk that works
            var host = TestSetup.Colonist(map);
            t.Check(host != null, "a host");
            GuestRegistry.InsultChanceOverride = 0f;
            GuestRegistry.ConvertAttemptOverride = false;
            int talks0 = a.ConvertTalks; var ideoBefore = a.Pawn.ideo.Ideo;
            reg.Notify_Talk(host, a.Pawn);
            t.Check(a.ConvertTalks == talks0 + 1 && a.Handling == HandlingMode.Convert && a.Pawn.ideo.Ideo == ideoBefore, "a talk that does not convince leaves everything as it was");
            t.Check(a.Events.Any(e => e.Contains("sure of their own beliefs") || e.Contains("unmoved")), "the stay log tells how it went: " + a.Events.LastOrDefault());
            GuestRegistry.ConvertAttemptOverride = true;
            float sat0 = a.ServiceSum;
            reg.Notify_Talk(host, a.Pawn);
            t.Check(a.Handling == HandlingMode.Accompany, "after a convincing talk the guest goes back to plain company");
            t.Check(a.Events.Any(e => e.Contains("Embraced")) && a.ServiceSum > sat0 - 0.0001f, "log: embraced, satisfaction up a little");
            GuestRegistry.ConvertAttemptOverride = null;
            // vanilla's own conversion: with a strong setting a few talks flip the ideoligion
            b.Handling = HandlingMode.Convert;
            float p0 = HSettings.Get(InnSettings.ConvertPower);
            HSettings.Set(InnSettings.ConvertPower, 10f);
            int guard = 0;
            while (b.Pawn.ideo.Ideo != primary && guard++ < 40) { b.Handling = HandlingMode.Convert; reg.Notify_Talk(host, b.Pawn); }
            HSettings.Set(InnSettings.ConvertPower, p0);
            t.Check(b.Pawn.ideo.Ideo == primary, $"vanilla's conversion flips the ideoligion after {guard} talks of strength x10");
            t.Check(!HandlingRules.ConvertAvailable(b.Pawn) && b.Handling == HandlingMode.Accompany, "a converted guest cannot be converted again and is back to Accompany");
            t.Check(HandlingRules.All(b.Pawn).Count == 3, "and the fourth mode is no longer offered for them");
            // pushy talks
            var c = ms.Count > 2 ? ms[2] : null;
            a.Pawn.ideo.SetIdeo(other); a.Pawn.ideo.OffsetCertainty(0.9f - a.Pawn.ideo.Certainty); a.Handling = HandlingMode.Convert; a.Insulted = false;
            GuestRegistry.InsultChanceOverride = 1f;
            int ctalks = a.ConvertTalks; var budget0 = a.Budget;
            reg.Notify_Talk(host, a.Pawn);
            t.Check(a.Insulted && a.ConvertTalks == ctalks && a.Budget < budget0, "a pushy talk offends: no progress, less spending");
            t.Check(a.Pawn.needs.mood.thoughts.memories.GetFirstMemoryOfDef(InnDefOf.HH_GuestPreached) != null, "the guest remembers being preached at");
            GuestRegistry.InsultChanceOverride = null;
            // Convert never recruits at departure
            a.Handling = HandlingMode.Convert;
            var f = a.Pawn.Faction;
            var ap = a.Pawn;
            reg.DepartGroup(g, "planned");
            t.Check(ap.Faction == f, "a Convert guest leaves with their faction, nobody is recruited");
            InnTestKit.RemoveAllGuests(map);
            InnTestKit.Teardown(site, map);
            MapComponent_HH_GuestScheduler.SuspendedForTests = false;
        }
    }

    /// <summary>F10: the one bed button.</summary>
    public sealed class InnBedUseSuite : HearthTestSuite
    {
        public override string Name => "inn.beduse";
        public override ModuleId Module => ModuleId.Inn;
        public override int MaxTicks => 12000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map;
            var site = InnUxKit.Build(map, ThingDefOf.Bed, 2, desk: false);
            var b0 = site.Beds[0]; var b1 = site.Beds[1];
            yield return 10;
            bool rimward = BedRoles.RimWardPresent;
            t.Note("RimWard bed API present: " + rimward);
            t.Check(CompHH_GuestBed.DrawsBedSelector && CompHH_GuestBed.BedSelectorVersion >= 1, "protocol: Hearth & Harbor draws the selector");
            // a colonist bed: exactly one of our buttons, named for its role
            var gz = b0.GetGizmos().ToList();
            var one = gz.OfType<Command_HH_BedUse>().ToList();
            t.Check(one.Count == 1 && one[0].Role == BedRole.Colonist && one[0].defaultLabel == "Colonist bed", "a normal bed has one 'Colonist bed' button: " + string.Join(", ", one.Select(x => x.defaultLabel)));
            t.Check(!gz.OfType<Command_Toggle>().Any(x => x.defaultLabel == "Guest bed"), "the old Guest bed toggle is gone");
            t.Check(!gz.OfType<Command_Action>().Any(x => !(x is Command_HH_BedUse) && (x.defaultLabel ?? "").StartsWith("Price")), "and so is the separate Price button");
            t.Check(one[0].Order > -100f && one[0].Order <= -90f + 0.01f, "the button sits next to the vanilla bed buttons (order " + one[0].Order + ")");
            // the menu
            var opts = one[0].MenuOptions();
            t.Check(opts.Count >= 2 && opts.Any(o => o.Label.StartsWith("Colonist bed")) && opts.Any(o => o.Label.StartsWith("Guest bed")), "the menu lists the roles with a short explanation: " + string.Join(" / ", opts.Select(o => o.Label)));
            t.Check(opts.All(o => o.Label.Contains("—")), "every entry explains itself");
            if (!rimward) t.Check(opts.Count == 2, "without RimWard: Colonist and Guest only");
            // switch to guest through the menu
            var gopt = opts.First(o => o.Label.StartsWith("Guest bed"));
            t.Check(!gopt.Disabled, "guest entry is available on a normal bed");
            gopt.action();
            t.Check(GuestBeds.IsGuestBed(b0), "choosing Guest makes it a guest bed");
            gz = b0.GetGizmos().ToList();
            one = gz.OfType<Command_HH_BedUse>().ToList();
            t.Check(one.Count == 1 && one[0].Role == BedRole.Guest && one[0].defaultLabel == "Guest bed", "still exactly one button, now 'Guest bed' (with the price chip)");
            t.Check(one[0].defaultDesc.Contains("Price:") && one[0].defaultDesc.Contains("silver"), "its tooltip carries the price: " + one[0].defaultDesc.Replace("\n", " | "));
            // two selected beds: the menu applies to both
            Find.Selector.ClearSelection(); Find.Selector.Select(b0, false, false); Find.Selector.Select(b1, false, false);
            var multi = b1.GetGizmos().OfType<Command_HH_BedUse>().First();
            multi.MenuOptions().First(o => o.Label.StartsWith("Guest bed")).action();
            t.Check(GuestBeds.IsGuestBed(b0) && GuestBeds.IsGuestBed(b1), "with two beds selected the choice applies to both");
            multi.MenuOptions().First(o => o.Label.StartsWith("Colonist bed")).action();
            Find.Selector.ClearSelection();
            t.Check(!GuestBeds.IsGuestBed(b0) && !GuestBeds.IsGuestBed(b1), "and back to colonist beds for both");
            // inspect string: short
            GuestBeds.Comp(b0).SetGuestBed(true, false);
            string insp = GuestBeds.Comp(b0).CompInspectStringExtra() ?? "";
            t.Check(insp.Split('\n').Length <= 3 && insp.Contains("silver/night"), "the inspect text is short: " + insp.Replace("\n", " | "));
            // with RimWard: its own "Use:" button is not drawn (exactly one set of buttons), and a medical bed can be switched to Patient and back through our menu
            if (rimward)
            {
                t.Check(!b0.GetGizmos().Any(g => g.GetType().FullName == "RimWard.Command_BedUse"), "RimWard does not draw its own 'Use:' button next to ours");
                var pb = site.Beds[1];
                pb.Medical = true;
                var popts = pb.GetGizmos().OfType<Command_HH_BedUse>().First().MenuOptions();
                var patient = popts.FirstOrDefault(o => o.Label.StartsWith("Patient bed"));
                t.Check(patient != null && !patient.Disabled && popts.Any(o => o.Label.StartsWith("Surgery bed") && !o.Disabled), "on a medical bed the menu offers Patient and Surgery");
                patient?.action();
                t.Check(BedRoles.Current(pb) == BedRole.Patient && pb.GetGizmos().OfType<Command_HH_BedUse>().First().defaultLabel == "Patient bed", "choosing Patient: the button now says 'Patient bed' (" + BedRoles.Current(pb) + ")");
                pb.GetGizmos().OfType<Command_HH_BedUse>().First().MenuOptions().First(o => o.Label.StartsWith("Colonist bed")).action();
                t.Check(BedRoles.Current(pb) != BedRole.Patient, "and back to a colonist bed");
                pb.Medical = false;
            }
            // a medical bed without RimWard: the button is there, greyed out with the reason
            var mb = site.Beds[1];
            if (!rimward)
            {
                mb.Medical = true;
                var mg = mb.GetGizmos().OfType<Command_HH_BedUse>().FirstOrDefault();
                t.Check(mg != null && mg.Disabled, "a medical bed shows the button greyed out: " + mg?.disabledReason);
                mb.Medical = false;
            }
            // dev group: our own DEV buttons are folded, vanilla's are not
            var ours = new Command_Action { defaultLabel = "DEV: ours one" };
            var ours2 = new Command_Action { defaultLabel = "DEV: ours two" };
            var normal = new Command_Action { defaultLabel = "Plain" };
            var vanillaDev = new Command_Action { defaultLabel = "DEV: vanilla" };
            DevGizmos.Mark(new Gizmo[] { ours, ours2, normal }).ToList();
            var folded = DevGizmos.Fold(new Gizmo[] { ours, normal, vanillaDev, ours2 }).ToList();
            t.Check(folded.Count == 3 && folded.OfType<Command_HH_DevGroup>().Count() == 1 && folded.Contains(normal) && folded.Contains(vanillaDev), "two of our DEV buttons became one group; the plain and the vanilla DEV button stay: " + string.Join(", ", folded.OfType<Command>().Select(x => x.defaultLabel)));
            t.Check(!folded.Contains(ours) && !folded.Contains(ours2), "the originals are inside the group");
            InnUxKit.Teardown(site, map);
        }
    }

    /// <summary>F35: the guest tab and the door policy window are bigger and tidier.</summary>
    public sealed class InnWindowsSuite : HearthTestSuite
    {
        public override string Name => "inn.windows";
        public override ModuleId Module => ModuleId.Inn;
        public override int MaxTicks => 9000;
        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var reg = GuestRegistry.Instance;
            MapComponent_HH_GuestScheduler.SuspendedForTests = true;
            // ---- the guest tab: bigger than the old 430 x 520, never taller than the screen
            var tab = new ITab_HH_Guest();
            typeof(ITab_HH_Guest).GetMethod("UpdateSize", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(tab, null);
            t.Check(tab.CurrentSize.x >= 440f && tab.CurrentSize.y >= 460f && tab.CurrentSize.x <= UI.screenWidth && tab.CurrentSize.y <= UI.screenHeight * 0.8f, $"guest tab {tab.CurrentSize.x:0}x{tab.CurrentSize.y:0} on a {UI.screenWidth}x{UI.screenHeight} screen");
            float sc = HUI.Scale;
            t.Check(tab.CurrentSize.x > 430f * Math.Min(1f, sc) || UI.screenWidth < 700, "wider than the old tab (430) at the default setting");
            // ---- the door policy window
            var door = reg.Door; door.Reset();
            var dlg = new Dialog_HH_DoorPolicy(door);
            var sz = dlg.InitialSize;
            t.Check(sz.x >= 560f && sz.y >= 560f && sz.x <= UI.screenWidth && sz.y <= UI.screenHeight, $"door policy window {sz.x:0}x{sz.y:0}");
            t.Check(!dlg.closeOnClickedOutside, "a click next to the window does not close it");
            string before = door.Signature();
            foreach (var mode in new[] { 0, 1, 2 })
            {
                door.Reset();
                if (mode == 1) { door.AllowAll = true; }
                if (mode == 2)
                {
                    var f = InnTestKit.GuestFaction(map);
                    door.FactionMode = FilterMode.OnlyThese; if (f != null) door.Factions.Add(f);
                    door.GenderMode = FilterMode.OnlyThese; door.MinAge = 20; door.MinTech = TechLevel.Industrial; door.MinWealth = 20; door.AllowDay = false;
                }
                bool ok = true;
                try { dlg.DoWindowContents(new Rect(0, 0, sz.x - 36f, sz.y - 36f)); } catch (Exception e) { ok = false; t.Note(e.ToString()); }
                t.Check(ok, "the door policy window draws (case " + mode + ")");
            }
            door.Reset();
            // ---- the tab sections can be collapsed and the state is part of the profile keys
            var gi = new GuestInfo { Pawn = map.mapPawns.FreeColonistsSpawned.FirstOrDefault(), Kind = StayKind.Overnight };
            var pawn = gi.Pawn;
            if (pawn != null)
            {
                float open = 0f, closed = 0f; bool ok = true;
                try
                {
                    foreach (var id in ITab_HH_Guest.SectionIds) UiState.SetFlag("gt." + id, false);
                    open = ITab_HH_Guest.DrawBody(480f, pawn, gi);
                    foreach (var id in ITab_HH_Guest.SectionIds) UiState.SetFlag("gt." + id, true);
                    closed = ITab_HH_Guest.DrawBody(480f, pawn, gi);
                }
                catch (Exception e) { ok = false; t.Note(e.ToString()); }
                foreach (var id in ITab_HH_Guest.SectionIds) UiState.SetFlag("gt." + id, false);
                t.Check(ok && open > closed + 100f, $"collapsing all sections shortens the tab content ({open:0} → {closed:0})");
            }
            t.Check(ITab_HH_Guest.SectionIds.All(id => UiState.OwnKeys().Contains("ui.gt." + id)), "collapsed flags are kept out of templates");
            yield return 1;
            MapComponent_HH_GuestScheduler.SuspendedForTests = false;
        }
    }
}
#endif
