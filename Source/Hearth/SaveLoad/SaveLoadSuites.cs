// SPDX-License-Identifier: MIT
// Save/load suites (KONZEPT 3.7, 15.5 "removal"/"addmidgame", 15.7). Two-phase: one game saves, the next one loads that save,
// possibly with a different mod list. Driven by tools/run_saveload.sh; every suite here is ExplicitOnly. Compiled only with HH_TESTS.
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;
using Verse.AI.Group;
using Hearth.Casino;
using Hearth.City;
using Hearth.Guests;
using Hearth.Insurance;
using Hearth.Shell;
using Hearth.ShopsFood;
using Hearth.Showcase;
using Hearth.Testing;
using Silverline;
using UW = Hearth.Underworld.Underworld;

namespace Hearth.SaveLoad
{
    /// <summary>Facts a player would notice, written next to the save and compared after the load (the fingerprint covers every field).</summary>
    static class Facts
    {
        public const string RoundtripSave = "hh_roundtrip";
        public const string ChangedSetting = "casino.guestMinBuyIn";
        /// <summary>The facts taken right after the load, at the same moment as the fingerprint (null in a game that was not loaded).</summary>
        public static List<string> AfterLoad;
        public static int AfterLoadTick = -1;
        public static string Get(List<string> facts, string key) => facts?.FirstOrDefault(x => x.StartsWith(key + "="));

        /// <summary>Compares the saved facts with the ones right after the load, one check per fact.</summary>
        public static void CompareAfterLoad(TestContext t, string save, IEnumerable<string> keys = null, string prefix = "survived the load: ")
        {
            var before = SaveLoadKit.ReadSide(save, "facts") ?? new List<string>();
            t.Check(before.Count > 0, "facts of the save found");
            t.Check(AfterLoad != null, "facts taken right after the load (tick " + AfterLoadTick + ")");
            var now = AfterLoad ?? Take(Find.CurrentMap);
            foreach (var l in before)
            {
                var key = l.Split('=')[0];
                if (keys != null && !keys.Contains(key)) continue;
                var cur = Get(now, key);
                t.Check(cur == l, prefix + l + (cur == l ? "" : "   now: " + cur));
            }
        }

        public static List<string> Take(Map map)
        {
            var l = new List<string>();
            l.Add("colonists=" + map.mapPawns.FreeColonistsSpawnedCount);
            l.Add("guests=" + string.Join(",", map.mapPawns.AllPawnsSpawned.Where(GuestApi.IsGuest).Select(p => p.ThingID).OrderBy(x => x)));
            l.Add("guestRecords=" + (GuestRegistry.Instance?.Records.Count ?? -1));
            l.Add("checkedIn=" + (GuestRegistry.Instance?.Records.Count(r => r.CheckedIn) ?? -1));
            l.Add("guestBeds=" + string.Join(",", GuestBeds.On(map).Select(b => b.ThingID).OrderBy(x => x)));
            l.Add("wings=" + string.Join(",", (GuestWings.Comp(map)?.wings ?? new List<Area_HH_GuestWing>()).Select(a => a.Label + ":" + a.TrueCount).OrderBy(x => x)));
            l.Add("ledgerSum=" + Ledger.Current.SumAll());
            l.Add("bankAccount=" + (BankGameComp.Current?.HasAccount ?? false));
            l.Add("bankBalance=" + Ledger.Current.Balance(BankGameComp.Colony).ToStringFull());
            l.Add("policies=" + (InsuranceComponent.Instance?.Active.Count() ?? -1));
            l.Add("portfolio=" + (FinanceGame.Current?.PortfolioValue().ToStringFull() ?? "-"));
            l.Add("lotteryTickets=" + (LotteryGame.Current?.tickets.Count ?? -1));
            l.Add("casinoSessions=" + (CasinoComponent.Get()?.Sessions.Count ?? -1));
            l.Add("screenings=" + map.lordManager.lords.Count(x => x.LordJob is LordJob_Joinable_Screening));
            l.Add("uwDiscovered=" + UW.Discovered);
            // facts are taken at save time and right after the load, i.e. at the same tick: the decaying whispers compare as they are
            l.Add("whispers=" + UW.Comp?.WhispersAt(Find.TickManager.TicksGame).ToString("0.000"));
            l.Add("setting " + ChangedSetting + "=" + HSettings.Get(ChangedSetting));
            l.Add("venueSigns=" + string.Join(";", map.listerThings.AllThings.Select(t => t.TryGetComp<CompVenueSignHH>()).Where(c => c != null)
                .Select(c => c.parent.ThingID + ":" + c.type?.defName + ":" + c.customName + ":" + c.alwaysOpen + ":" + c.priceFactor).OrderBy(x => x)));
            l.Add("physicalSilver=" + PhysicalSilver(map));
            // 1.1.0 content (F08/F09): storyteller and our scenario parts are saved removal-safe ("hhStoryteller", "hhParts")
            l.Add("storyteller=" + Find.Storyteller?.def?.defName);
            l.Add("scenParts=" + string.Join(",", (ScenarioSeed.PartsOf(Find.Scenario) ?? new List<ScenPart>()).Where(x => RemovalSafeSave.OurType(x?.GetType())).Select(x => x.def?.defName).OrderBy(x => x)));
            return l;
        }

        public static int PhysicalSilver(Map map)
        {
            var all = new List<Thing>();
            ThingOwnerUtility.GetAllThingsRecursively(map, ThingRequest.ForDef(ThingDefOf.Silver), all, true, null, true);
            return all.Sum(t => t.stackCount);
        }

        /// <summary>Red errors of the game (probe), split into load time and play time.</summary>
        public static void ReportErrors(TestContext t, string when, bool mustBeClean)
        {
            var errs = SaveLoadKit.ProbeErrors();
            var load = errs.Where(e => e.StartsWith("load")).ToList(); var play = errs.Where(e => e.StartsWith("play")).ToList();
            foreach (var e in errs.Take(30)) t.Note("game error: " + e);
            if (mustBeClean) t.Check(errs.Count == 0, when + ": no red errors in the game log (load " + load.Count + ", play " + play.Count + ")");
            else t.Note(when + ": red errors load " + load.Count + ", play " + play.Count);
        }

        /// <summary>Hearth's own error count since the start of the run.</summary>
        public static int OurErrors => HLog.ErrorCount;
    }

    /// <summary>Takes the facts at the moment the fingerprint is taken (SaveLoadKit.AfterLoadHook), before the first tick of the loaded game.</summary>
    [StaticConstructorOnStartup]
    static class FactsAfterLoad
    {
        static FactsAfterLoad()
        {
            SaveLoadKit.AfterLoadHook += () =>
            {
                var map = Find.CurrentMap ?? Find.AnyPlayerHomeMap; if (map == null) return;
                Facts.AfterLoad = Facts.Take(map); Facts.AfterLoadTick = Find.TickManager.TicksGame;
            };
        }
    }

    /// <summary>Fields that are caches by design (rebuilt before use after a load); the fingerprint comparison ignores them.</summary>
    static class KnownTransient
    {
        static bool done;
        public static void Register()
        {
            if (done) return; done = true;
            var l = SaveFingerprint.Transient;
            l.Add((@"^game/BankGameComp\.cachedNet", "net-worth cache, 250-tick refresh"));
            l.Add((@"^game/FinanceGame\.assets\[\]\.(spec|specKey|drift|lnMa)(\.|$| )", "market spec cache, rebuilt by RefreshSpec before every price step"));
            l.Add((@"^game/FinanceGame\.(sysCache|sysKey)", "systemic-event cache, rebuilt on use"));
            l.Add((@"^game/HearthGame\.IsNewGame", "true only in the session that started the game (first-start card wording)"));
            l.Add((@"^game/GuestRegistry\.records\[\]\.(CachedWing|CachedWingBed|CachedWingVersion|LastActivityTick)$", "per-guest wing lookup cache and the 600-tick activity throttle"));
            l.Add((@"^map\d+/MapComponent_HH_GuestWings\.(venueCells|venueSignature|venueGridTick)", "venue-room grid, rebuilt within 250 ticks"));
            l.Add((@"^map\d+/[^/]+/CompCasinoGame\.(seatedCache|seatedCacheTick|paytable|paytableRtp|Expected|Variance|LastResultTick)", "seat cache, paytable built from the def, last-spin display"));
            l.Add((@"^game/ImportedGuestBeds\.bedIndex", "lazy lookup index over the saved bed list"));
            l.Add((@"^map\d+/[^/]+/CompProjectorScreen\.power$", "lazy lookup of the power comp"));
            l.Add((@"^game/HearthGame\.Rand\.live", "live random streams; their counters are saved and the streams recreated on use"));
        }
    }

    /// <summary>Phase 1 of every save/load scenario: builds the demo hotel with all modules busy and saves it as hh_roundtrip.</summary>
    public sealed class SaveBuildSuite : HearthTestSuite
    {
        public override string Name => "saveload.build";
        public override bool ExplicitOnly => true;
        public override int MaxTicks => 60000;

        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map;
            var b = new ShowcaseBuilder();
            foreach (var w in b.Build(t)) yield return w;

            // guests (two overnight groups by the Inn's own arrival, plus a vanilla visitor group)
            var f = InnTestKit.GuestFaction(map);
            t.Check(f != null, "friendly faction for guests: " + f?.Name);
            var groups = new List<GuestGroup>();
            MapComponent_HH_GuestScheduler.SuspendedForTests = false;
            if (f != null)
                foreach (var (size, nights, silver) in new[] { (2, 3, 700), (3, 3, 900) })
                {
                    var g = GuestArrival.TryFire(map, new GuestArrivalRequest { Faction = f, Size = size, Kind = StayKind.Overnight, Nights = nights, ForcePod = true, ForceBudget = Money.FromSilver(silver), Silent = true });
                    if (g != null) groups.Add(g);
                    yield return 30;
                }
            t.Check(groups.Count > 0, "guest groups arrived: " + groups.Count);
            var reg = GuestRegistry.Instance;
            int waited = 0;
            while (waited < 15000)
            {
                yield return 500; waited += 500; b.RefuelAll();
                var members = groups.SelectMany(g => reg.Members(g)).ToList();
                if (members.Count > 0 && members.Count(m => m.CheckedIn) >= Math.Min(3, members.Count) && waited >= 4000) break;
            }
            t.Check(reg.Records.Any(r => r.CheckedIn), "guests checked in: " + reg.Records.Count(r => r.CheckedIn) + " after " + waited + " ticks");

            // a casino session that is running at save time
            var guest = map.mapPawns.AllPawnsSpawned.FirstOrDefault(p => GuestApi.IsGuest(p) && CasinoComponent.Get()?.SessionOf(p) == null && p.DevelopmentalStage.Adult());
            var table = map.listerThings.AllThings.Select(x => x.TryGetComp<CompCasinoGame>()).FirstOrDefault(c => c != null && CasinoComponent.Get().CountAt(c.parent) == 0);
            if (CasinoComponent.Get().Sessions.Count == 0 && guest != null && table != null)
            {
                var s = CasinoEngine.Begin(guest, table, SessionKind.Guest, false, out string why);
                t.Note("casino session forced: " + (s != null ? "running" : why));
            }
            t.Check(CasinoComponent.Get().Sessions.Count > 0, "casino sessions running: " + CasinoComponent.Get().Sessions.Count);

            // bank: account, loan and policies come from the builder; add investments and lottery tickets
            var fin = FinanceGame.Current; fin?.EnsureInit();
            var idx = fin?.Asset("SL_Asset_Index");
            if (idx != null) { var r = fin.Buy(idx.id, Money.FromSilver(400)); t.Check(r.Ok, "index fund bought: " + r.Message); }
            var lot = LotteryGame.Current;
            if (lot != null) { var r = lot.BuyGlobalTicket(new[] { 3, 7, 12, 21, 33 }, 4); t.Check(r.Ok, "lottery ticket bought: " + r.Message); lot.QuickPick(3); }

            // underworld discovered, with whispers
            var prof = HearthProfile.Current;
            prof.UnderworldDiscovered = true;
            UW.Comp.ContentWarningAck = true;
            UW.AddWhispers(12.5f, "save/load test");
            t.Check(UW.Unlocked && UW.Whispers > 10f, "underworld unlocked, whispers " + UW.Whispers.ToString("0.0"));

            // a movie night of the colony (a player-faction lord with our LordJob) that is running at save time
            HSettings.Set(CitySettings.CinemaShowTicks, 20000);
            var org = map.mapPawns.FreeColonistsSpawned.FirstOrDefault(p => !p.Downed && !p.InMentalState);
            var spot = org != null ? t.FreeCellNearCenter(25) : IntVec3.Invalid;
            if (org != null && spot.IsValid && (spot + IntVec3.North * 3).InBounds(map))
            {
                var screen = Hearth.City.Tests.CityTestKit.Spawn(map, "HH_ProjectorScreen", spot + IntVec3.North * 3, Rot4.South);
                Hearth.City.Tests.CityTestKit.Power(screen);
                var gdef = DefDatabase<GatheringDef>.GetNamed("HH_Screening");
                LordMaker.MakeNewLord(Faction.OfPlayer, new LordJob_Joinable_Screening(spot, org, gdef, screen), map, new[] { org });
                yield return 60;
            }
            t.Check(map.lordManager.lords.Any(x => x.LordJob is LordJob_Joinable_Screening), "movie night running: " + map.lordManager.lords.Count(x => x.LordJob is LordJob_Joinable_Screening));

            // 1.1.0 content: the colony runs with Hetty Hearthwell and its scenario carries our parts. Both are written removal-safe (Phoebe +
            // "hhStoryteller", vanilla part list + "hhParts"): the roundtrip must bring them back, the raw load without the mod must stay clean.
            var hetty = DefDatabase<StorytellerDef>.GetNamedSilentFail("HH_Hetty");
            if (hetty != null) Find.Storyteller.def = hetty;
            var scenParts = ScenarioSeed.PartsOf(Find.Scenario);
            if (scenParts != null && !scenParts.Any(x => x is ScenPart_HHRules))
            {
                var rules = new ScenPart_HHRules { def = DefDatabase<ScenPartDef>.GetNamed("HH_ScenRules"), tier = 2f, content = "Cozy" };
                rules.SetSetting("inn.arrivalRate", 1.3f);
                scenParts.Add(rules);
                scenParts.Add(new ScenPart_HHSetting { def = DefDatabase<ScenPartDef>.GetNamed("HH_ScenSetting"), key = "venues.marketEnabled", value = 1f });
                scenParts.Add(new ScenPart_HHStartingResearch { def = DefDatabase<ScenPartDef>.GetNamed("HH_ScenStartingResearch"), project = "HH_Innkeeping" });
                scenParts.Add(new Silverline.ScenPart_SLStartingAccount { def = DefDatabase<ScenPartDef>.GetNamed("SL_ScenStartingAccount"), bank = "SL_Mutual", balance = 100 });
            }
            t.Check(Find.Storyteller.def == hetty && ScenarioSeed.PartsOf(Find.Scenario).Count(x => RemovalSafeSave.OurType(x.GetType())) == 4, "storyteller Hetty and four scenario parts of ours in the saved game");

            // a per-save setting that differs from the template
            float tmpl = HSettings.Get(Facts.ChangedSetting);
            HSettings.Set(Facts.ChangedSetting, tmpl + 7f);
            t.Check(Math.Abs(HSettings.Get(Facts.ChangedSetting) - (tmpl + 7f)) < 0.01f, "per-save setting changed to " + HSettings.Get(Facts.ChangedSetting));

            yield return 200;
            var facts = Facts.Take(map);
            foreach (var l in facts) t.Note("fact " + l);
            SaveLoadKit.WriteSide(Facts.RoundtripSave, "facts", facts);
            SaveLoadKit.SaveWithFingerprint(Facts.RoundtripSave, t);
            Facts.ReportErrors(t, "building and saving", false);
        }
    }

    /// <summary>Phase 2 with the same mods: every field of ours survived, the facts are equal, and the game runs two more days cleanly.</summary>
    public sealed class SaveRoundtripSuite : HearthTestSuite
    {
        public override string Name => "saveload.roundtrip";
        public override bool ExplicitOnly => true;
        public override bool KeepsLoadedGame => true;
        public override int MaxTicks => 140000;

        public override IEnumerable<int> Run(TestContext t)
        {
            string save = SaveLoadKit.LoadedSave;
            t.Check(save != null, "this game was loaded from a save: " + save);
            if (save == null) yield break;
            var map = t.Map;
            KnownTransient.Register();
            SaveLoadKit.CompareFingerprints(save, t);
            // the facts are taken right after the load (with the fingerprint); the suite itself starts a few hundred ticks later
            Facts.CompareAfterLoad(t, save);
            // ... and the guests are still in the house when the game has run for a moment (a load that sends them away shows up here)
            var now = Facts.Take(map);
            foreach (var key in new[] { "guests", "guestRecords", "checkedIn", "screenings" })
            {
                var a = Facts.Get(Facts.AfterLoad, key); var b = Facts.Get(now, key);
                t.Check(a == b, "still there " + (Find.TickManager.TicksGame - Facts.AfterLoadTick) + " ticks after the load: " + a + (a == b ? "" : "   now: " + b));
            }
            t.Check(!HearthMod.Template.Store.Has(Facts.ChangedSetting), "the per-save setting did not leak into the template");
            Facts.ReportErrors(t, "loading", true);

            // the loaded hotel keeps working
            int errs0 = Facts.OurErrors;
            int startTick = Find.TickManager.TicksGame;
            yield return 120000;
            t.Check(Find.TickManager.TicksGame - startTick >= 120000, "two days played after the load");
            t.Check(Facts.OurErrors == errs0, "no Hearth & Harbor errors in two days: " + (Facts.OurErrors - errs0));
            t.Check(Ledger.Current.SumAll() == 0, "ledger still sums to zero");
            t.Check(Guard.Paused.Count() == 0, "no module paused: " + string.Join(", ", Guard.PauseReasons));
            Facts.ReportErrors(t, "two days after the load", true);
        }
    }

    /// <summary>Phase 2b: the in-game "prepare removal" path of every module, then save as hh_prepared (loaded next WITHOUT the mod).</summary>
    public sealed class SavePrepareRemovalSuite : HearthTestSuite
    {
        public override string Name => "saveload.prepare";
        public override bool ExplicitOnly => true;
        public override bool KeepsLoadedGame => true;
        public override int MaxTicks => 60000;

        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map;
            int silver0 = Facts.PhysicalSilver(map);
            var report = new List<string>();
            foreach (var m in HearthMod.ModuleObjects)
            {
                var part = new List<string>();
                string err = null;
                try { m.PrepareRemoval(part); } catch (Exception e) { err = e.ToString(); }
                t.Check(err == null, m.GetType().Name + ".PrepareRemoval ran: " + part.Count + " line(s)" + (err == null ? "" : " THREW " + err));
                foreach (var l in part) t.Note(m.Id + ": " + l);
                report.AddRange(part);
            }
            // guests walk off the map; pods with paid-out silver land
            int waited = 0;
            while (waited < 30000 && map.mapPawns.AllPawnsSpawned.Any(GuestApi.IsGuest)) { yield return 1000; waited += 1000; }
            t.Note("guests left after " + waited + " ticks; still here: " + map.mapPawns.AllPawnsSpawned.Count(GuestApi.IsGuest));
            yield return 2500;
            int silver1 = Facts.PhysicalSilver(map);
            t.Note("physical silver " + silver0 + " → " + silver1 + " (bank balances paid out as silver)");
            t.Check(silver1 >= silver0 - 50, "no physical silver lost by the preparation");
            SaveLoadKit.WriteSide("hh_prepared", "facts", Facts.Take(map));
            SaveLoadKit.SaveWithFingerprint("hh_prepared", t);
            Facts.ReportErrors(t, "prepare removal", false);
        }
    }

    /// <summary>A save made WITHOUT Hearth &amp; Harbor, loaded with it: a profile from the template, the first-start card, nothing else.</summary>
    public sealed class SaveAdditionSuite : HearthTestSuite
    {
        public override string Name => "saveload.addition";
        public override bool ExplicitOnly => true;
        public override bool KeepsLoadedGame => true;
        public override int MaxTicks => 70000;

        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map;
            t.Check(SaveLoadKit.LoadedSave != null, "loaded from a vanilla save: " + SaveLoadKit.LoadedSave);
            var g = HearthGame.Instance; var prof = HearthProfile.Current;
            t.Check(g != null && prof != null, "profile created");
            var tmpl = HearthMod.Template.Profile;
            t.Check(prof != tmpl && prof.Tier == tmpl.Tier && prof.Content == tmpl.Content, "profile copied from the template (tier " + prof?.Tier + ", content " + prof?.Content + ")");
            // every template value is in the profile (the UI adds its own per-save entries such as the tutorial level on top)
            var missing = HSettings.All.Where(s => tmpl.Store.TryGet(s.Key, out var tv) && !(prof.Store.TryGet(s.Key, out var pv) && Math.Abs(pv - tv) < 1e-6f)).Select(s => s.Key).ToList();
            t.Check(prof != null && missing.Count == 0, "settings copied from the template (" + tmpl.Store.Count + " template values; profile holds " + prof?.Store.Count + ")" + (missing.Count > 0 ? " missing: " + string.Join(",", missing) : ""));
            t.Check(Modules.All.All(m => prof.ModuleOn(m) == tmpl.ModuleOn(m)), "module switches from the template");
            var shown = (bool)(typeof(Hearth.Shell.FirstStartCardTrigger).GetField("shownThisSession", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) ?? false);
            t.Check(shown || Find.WindowStack.WindowOfType<Hearth.Shell.Dialog_FirstStartCard>() != null, "first-start card shown");
            t.Note("card done flag after the test runner closed it: " + prof.FirstStartCardDone + " (closing the card accepts its defaults)");
            // nothing else changed
            t.Check(!map.listerThings.AllThings.Any(x => SaveFingerprint.OurDef(x.def)), "no Hearth & Harbor things on the map");
            t.Check(!map.areaManager.AllAreas.Any(a => SaveFingerprint.Ours(a.GetType())), "no Hearth & Harbor areas");
            t.Check(!map.mapPawns.AllPawns.Any(p => p.health.hediffSet.hediffs.Any(h => SaveFingerprint.OurDef(h.def))), "no Hearth & Harbor hediffs");
            t.Check(GuestRegistry.Instance.Records.Count == 0 && !map.mapPawns.AllPawnsSpawned.Any(GuestApi.IsGuest), "no guests");
            t.Check(!(BankGameComp.Current?.HasAccount ?? false), "no bank account");
            t.Check(Ledger.Current.SumAll() == 0, "ledger empty and balanced");
            Facts.ReportErrors(t, "adding the mod", true);
            int errs0 = Facts.OurErrors;
            yield return 60000;
            t.Check(Facts.OurErrors == errs0, "a day without Hearth & Harbor errors: " + (Facts.OurErrors - errs0));
            t.Check(!map.listerThings.AllThings.Any(x => SaveFingerprint.OurDef(x.def)), "still nothing of ours on the map after a day");
            Facts.ReportErrors(t, "a day after adding", true);
        }
    }

    /// <summary>Module off mid-game ("keep things in place"), saved as hh_modules_off; the next phase loads it and switches them on again.</summary>
    public sealed class SaveModulesOffSuite : HearthTestSuite
    {
        public override string Name => "saveload.modulesoff";
        public override bool ExplicitOnly => true;
        public override bool KeepsLoadedGame => true;
        public override int MaxTicks => 20000;
        internal static readonly ModuleId[] Switched = { ModuleId.Casino, ModuleId.Underworld, ModuleId.Wellness, ModuleId.Bank };

        public override IEnumerable<int> Run(TestContext t)
        {
            var prof = HearthProfile.Current;
            // the running casino session of the save is cashed out when the Casino goes to sleep ("keep things in place" settles money in play)
            var cc = CasinoComponent.Get();
            if (!cc.Sessions.Any(s => s.Kind == SessionKind.Guest))
            {
                // the session of the save may have been settled by the caretaker since the load (no guest at the table): start another one
                var map = t.Map;
                var guest = map.mapPawns.AllPawnsSpawned.FirstOrDefault(p => GuestApi.IsGuest(p) && cc.SessionOf(p) == null && p.DevelopmentalStage.Adult());
                var table = map.listerThings.AllThings.Select(x => x.TryGetComp<CompCasinoGame>()).FirstOrDefault(c => c != null && cc.CountAt(c.parent) == 0);
                if (guest != null && table != null) { var s0 = CasinoEngine.Begin(guest, table, SessionKind.Guest, false, out string why); t.Note("casino session started: " + (s0 != null ? "running" : why)); }
            }
            var guestSessions = cc.Sessions.Where(s => s.Kind == SessionKind.Guest && s.Pawn != null).Select(s => (s.Pawn, s.Purse, budget: GuestApi.Info(s.Pawn)?.Budget ?? Money.Zero)).ToList();
            t.Check(guestSessions.Count > 0, "a guest session is running before the switch: " + guestSessions.Count);
            foreach (var m in Switched) { prof.SetModule(m, false); t.Check(!Modules.On(m), m + " switched off"); }
            t.Check(cc.Sessions.Count == 0, "casino sessions settled at the switch: " + cc.Sessions.Count + " left");
            foreach (var (pawn, purse, budget) in guestSessions)
            {
                var info = GuestApi.Info(pawn);
                bool paid = purse.Silver < 1.0 || (info != null && info.Budget > budget);
                t.Check(paid, $"{pawn.LabelShort} got the purse back ({purse}): budget {budget} → {info?.Budget}");
            }
            t.Check(Ledger.Current.SumAll() == 0, "ledger balanced after the cash-out");
            int errs0 = Facts.OurErrors;
            yield return 5000;
            t.Check(Facts.OurErrors == errs0, "modules off for a while without errors: " + (Facts.OurErrors - errs0));
            SaveLoadKit.WriteSide("hh_modules_off", "facts", Facts.Take(t.Map));
            SaveLoadKit.SaveWithFingerprint("hh_modules_off", t);
        }
    }

    public sealed class SaveModulesOnSuite : HearthTestSuite
    {
        public override string Name => "saveload.moduleson";
        public override bool ExplicitOnly => true;
        public override bool KeepsLoadedGame => true;
        public override int MaxTicks => 80000;

        public override IEnumerable<int> Run(TestContext t)
        {
            string save = SaveLoadKit.LoadedSave;
            t.Check(save == "hh_modules_off", "loaded the modules-off save: " + save);
            KnownTransient.Register();
            SaveLoadKit.CompareFingerprints(save, t, 40);
            foreach (var m in SaveModulesOffSuite.Switched) t.Check(!Modules.On(m), m + " is still off after the load");
            // casinoSessions: 0 on both sides, the session was cashed out when the Casino was switched off (saveload.modulesoff checks that)
            Facts.CompareAfterLoad(t, save, new[] { "casinoSessions", "lotteryTickets", "policies", "bankBalance", "portfolio", "uwDiscovered", "whispers", "ledgerSum", "guests", "guestRecords" }, "kept while off: ");
            t.Check(CasinoComponent.Get().Sessions.Count == 0, "no casino session waiting in limbo while the module is off: " + CasinoComponent.Get().Sessions.Count);
            var prof = HearthProfile.Current;
            foreach (var m in SaveModulesOffSuite.Switched) { prof.SetModule(m, true); t.Check(Modules.On(m), m + " switched on again"); }
            int errs0 = Facts.OurErrors;
            yield return 60000;
            t.Check(Facts.OurErrors == errs0, "a day with the modules back on without errors: " + (Facts.OurErrors - errs0));
            t.Check(Ledger.Current.SumAll() == 0, "ledger balanced");
            t.Check(Guard.Paused.Count() == 0, "no module paused: " + string.Join(", ", Guard.PauseReasons));
            Facts.ReportErrors(t, "modules back on", true);
        }
    }
}
#endif
