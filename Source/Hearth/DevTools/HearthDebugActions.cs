// SPDX-License-Identifier: MIT
// N65: developer tools. Debug actions menu -> "Hearth & Harbor" (needs developer mode, like every vanilla debug action).
// Nothing here runs during normal play; every action is a plain call into the public APIs of the modules.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Hearth.Casino;
using Hearth.Guests;
using Hearth.Shell;
using LudeonTK;
using RimWorld;
using Silverline;
using Verse;

namespace Hearth.DevTools
{
    public static class HearthDebugActions
    {
        const string Cat = "Hearth & Harbor";

        internal static void Say(string text, bool bad = false) => Messages.Message("[HH dev] " + text, bad ? MessageTypeDefOf.RejectInput : MessageTypeDefOf.NeutralEvent, false);

        // ------------------------------------------------------------------ guests
        /// <summary>Picks a faction that may visit (and is not hostile / present as enemy), preferring rough ones for shady groups.</summary>
        internal static Faction PickFaction(Map map, bool shady)
        {
            var all = Find.FactionManager.AllFactionsListForReading
                .Where(f => MapComponent_HH_GuestScheduler.FactionCanVisit(f, map) && !MapComponent_HH_GuestScheduler.EnemyPresent(map, f)).ToList();
            if (shady)
            {
                var rough = all.Where(GuestArrival.IsRoughFaction).ToList();
                if (rough.Count > 0) return rough.RandomElement();
            }
            return all.Count > 0 ? all.RandomElement() : null;
        }

        internal static void SpawnGroup(StayKind kind, bool shady)
        {
            var map = Find.CurrentMap;
            if (map == null || !map.IsPlayerHome) { Say("Needs a player home map.", true); return; }
            if (!Modules.On(ModuleId.Inn)) { Say("The Inn module is off in this game.", true); return; }
            var f = PickFaction(map, shady);
            if (f == null) { Say("No faction can visit right now.", true); return; }
            var req = new GuestArrivalRequest
            {
                Faction = f, Kind = kind, Size = Rand.RangeInclusive(2, 3), Nights = kind == StayKind.Overnight ? 2 : 1,
                IgnoreDoorPolicy = true, ForceShady = shady ? (bool?)true : null,
            };
            var g = GuestArrival.TryFire(map, req);
            Say(g != null ? $"{kind}{(shady ? " (shady)" : "")} group of {g.Names.Count} from {f.Name}: {string.Join(", ", g.Names)}" : "Arrival failed (see the log).", g == null);
        }

        [DebugAction(Cat, "Guests: arrive now (overnight)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        internal static void ArriveOvernight() => SpawnGroup(StayKind.Overnight, false);

        [DebugAction(Cat, "Guests: arrive now (day visitors)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        internal static void ArriveDay() => SpawnGroup(StayKind.DayVisitor, false);

        [DebugAction(Cat, "Guests: arrive now (shady)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        internal static void ArriveShady() => SpawnGroup(StayKind.DayVisitor, true);

        [DebugAction(Cat, "Guests: fill budgets", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        internal static void FillBudgets()
        {
            var reg = GuestRegistry.Instance; var map = Find.CurrentMap;
            if (reg == null || map == null) { Say("No guest registry (Inn module).", true); return; }
            int n = 0; Money add = Money.FromSilver(1000);
            foreach (var r in reg.OnMap(map).ToList())
            {
                if (r.Pawn == null || r.Leaving) continue;
                var target = Money.Max(add, r.StartBudget);
                var diff = target - r.Budget; if (diff.Micro <= 0) continue;
                r.Budget = target;
                if (r.PaysDigital) Ledger.Current?.Post("world", r.Account, diff, "Dev: budget top-up");
                else GiveSilver(r.Pawn, diff);
                n++;
            }
            Say(n + " guest budget(s) filled to at least 1000 silver.");
        }

        internal static void GiveSilver(Pawn p, Money amount)
        {
            int coins = (int)Math.Ceiling(amount.Silver - 1e-9);
            if (coins <= 0 || p.inventory == null) return;
            while (coins > 0)
            {
                var s = ThingMaker.MakeThing(ThingDefOf.Silver);
                s.stackCount = Math.Min(coins, ThingDefOf.Silver.stackLimit);
                coins -= s.stackCount;
                if (!p.inventory.innerContainer.TryAdd(s)) { s.Destroy(); break; }
            }
        }

        [DebugAction(Cat, "Guests: force departure + report", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        internal static void ForceDeparture()
        {
            var reg = GuestRegistry.Instance; var map = Find.CurrentMap;
            if (reg == null || map == null) { Say("No guest registry (Inn module).", true); return; }
            var groups = reg.OnMap(map).Select(r => reg.Group(r.GroupId)).Where(g => g != null && !g.LeaveSent).Distinct().ToList();
            foreach (var g in groups) reg.DepartGroup(g, "dev");
            Say(groups.Count == 0 ? "No guest group on this map." : groups.Count + " group(s) sent home; report sent.", groups.Count == 0);
        }

        // ------------------------------------------------------------------ reputation, bank
        [DebugAction(Cat, "Renown +20", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        internal static void RenownPlus() => Say(GuestApi.HasRenown ? $"Renown {GuestApi.AddRenown(Find.CurrentMap, 20f, "dev tool"):+0.0} -> {GuestApi.Renown():0.0}" : "Renown needs the Inn module.", !GuestApi.HasRenown);

        [DebugAction(Cat, "Renown -20", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        internal static void RenownMinus() => Say(GuestApi.HasRenown ? $"Renown {GuestApi.AddRenown(Find.CurrentMap, -20f, "dev tool"):+0.0;-0.0} -> {GuestApi.Renown():0.0}" : "Renown needs the Inn module.", !GuestApi.HasRenown);

        [DebugAction(Cat, "Whispers +20", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        internal static void WhispersPlus()
        {
            var reg = GuestRegistry.Instance; if (reg == null) { Say("Needs the Inn module.", true); return; }
            reg.AddWhispers(20f, "dev tool"); Say("Whispers now " + reg.CurrentWhispers.ToString("0.0"));
        }

        [DebugAction(Cat, "Whispers -20", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        internal static void WhispersMinus()
        {
            var reg = GuestRegistry.Instance; if (reg == null) { Say("Needs the Inn module.", true); return; }
            reg.AddWhispers(-20f, "dev tool"); Say("Whispers now " + reg.CurrentWhispers.ToString("0.0"));
        }

        [DebugAction(Cat, "Bank: open account + 10000", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        internal static void BankOpen()
        {
            var bank = BankGameComp.Current;
            if (bank == null || !Modules.On(ModuleId.Bank)) { Say("The bank module is off.", true); return; }
            if (!bank.HasAccount && !bank.OpenAccount(SL_BankDef.Default, true)) { Say("Account could not be opened.", true); return; }
            bool ok = BankApi.Receive("world", Money.FromSilver(10000), "Dev: gift");
            Say(ok ? "Account holds " + bank.Balance.WholeSilver + " silver." : "Deposit failed.", !ok);
        }

        // ------------------------------------------------------------------ world events
        [DebugAction(Cat, "Underworld: stranger asks now", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        internal static void Stranger()
        {
            if (!Modules.On(ModuleId.Underworld)) { Say("The Underworld module is off.", true); return; }
            if (global::Hearth.Underworld.Underworld.Comp == null) { Say("No underworld data in this game.", true); return; }
            global::Hearth.Underworld.Discovery.Fire(Find.CurrentMap, true);
        }

        [DebugAction(Cat, "Casino: force a slot jackpot", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        internal static void Jackpot()
        {
            var map = Find.CurrentMap; var cc = CasinoComponent.Get();
            if (map == null || cc == null || !Modules.On(ModuleId.Casino)) { Say("The Casino module is off.", true); return; }
            CompCasinoGame comp = null; GamblerSession session = null;
            foreach (var t in map.listerThings.AllThings)
            {
                var c = t.TryGetComp<CompCasinoGame>(); if (c == null || c.Game != CasinoGame.Slots) continue;
                var seated = cc.SessionsAt(t).FirstOrDefault(s => s.Kind != SessionKind.Demo);
                if (comp == null || (seated != null && session == null)) { comp = c; session = seated; }
            }
            if (comp == null) { Say("No slot machine on this map.", true); return; }
            bool demo = session == null;
            if (demo) session = new GamblerSession { Kind = SessionKind.Demo, Purse = Money.FromSilver(1000), BuyIn = Money.FromSilver(1000), HouseAccount = "dev", Device = comp.parent };
            var mi = typeof(CasinoEngine).GetMethod("Settle", BindingFlags.NonPublic | BindingFlags.Static);
            if (mi == null) { Say("CasinoEngine.Settle not found (renamed?).", true); return; }
            var table = comp.Paytable; Money stake = Money.FromSilver(1);
            double mult = table.Multiplier(5, 5, 5);   // three sevens
            Money payout = Money.FromSilver(Math.Round(stake.Silver * mult, 5));
            try
            {
                mi.Invoke(null, new object[] { comp, session, stake, payout, stake.Silver * (1 - table.Rtp), Math.Pow(table.Sigma * stake.Silver, 2), "thrumbo · thrumbo · thrumbo  → ×" + mult.ToString("0.##") + " (dev jackpot)", null });
                Say($"Jackpot x{mult:0.#} ({payout.WholeSilver} silver for 1 staked) at {comp.parent.LabelCap}{(demo ? " (demo player, nobody was seated)" : " for " + session.Pawn?.LabelShort)}.");
            }
            catch (Exception e) { HLog.Error("DevTools", "Jackpot failed", e.InnerException ?? e); Say("Jackpot failed, see the log.", true); }
        }

        // ------------------------------------------------------------------ housekeeping
        [DebugAction(Cat, "Caretaker: run all checks now", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        internal static void RunCaretaker()
        {
            int before = Caretaker.TotalFixes;
            Caretaker.RunAll(true);
            Say($"Caretaker ran {Caretaker.Checks.Count} checks, {Caretaker.TotalFixes - before} repair(s).");
        }

        [DebugAction(Cat, "Diagnosis: dump to log", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Playing)]
        internal static void DumpDiagnosis()
        {
            string text = DiagnosisPage.BuildText(120);
            Log.Message("[HH] Diagnosis dump:\n" + text);
            Say("Diagnosis written to the log (" + text.Split('\n').Length + " lines).");
        }

        /// <summary>Runs every module's PrepareRemoval and returns the combined report. This CHANGES the game: only call it on a save you reload afterwards.</summary>
        internal static string CollectRemovalReport()
        {
            var report = new List<string>();
            foreach (var m in HearthMod.ModuleObjects)
            {
                var part = new List<string>();
                Guard.Run(m.Id, m.GetType().Name + ".PrepareRemoval(dry)", () => m.PrepareRemoval(part));
                report.Add("== " + Modules.Label(m.Id) + (part.Count == 0 ? ": nothing to do" : ""));
                report.AddRange(part.Select(l => "  " + l));
            }
            return string.Join("\n", report);
        }

        /// <summary>
        /// Prepare removal as a dry run: saves the game, runs every module's PrepareRemoval, keeps the report, then reloads the save so
        /// nothing it did stays. The report is shown after the reload and written to the log.
        /// </summary>
        [DebugAction(Cat, "Prepare removal: dry run (saves, runs, reloads)", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        internal static void RemovalDryRun()
        {
            const string save = "hh_dev_removal_dryrun";
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "Dry run: the game is saved as '" + save + "', every module's PrepareRemoval runs, then that save is loaded again so nothing sticks. Continue?",
                () =>
                {
                    try { GameDataSaveLoader.SaveGame(save); }
                    catch (Exception e) { HLog.Error("DevTools", "Dry run: saving failed, nothing was changed", e); Say("Saving failed, dry run cancelled.", true); return; }
                    string text = CollectRemovalReport();
                    HLog.Info("DevTools", "Prepare removal DRY RUN:\n" + text);
                    GameDataSaveLoader.LoadGame(save);
                    LongEventHandler.ExecuteWhenFinished(() => Find.WindowStack.Add(new Dialog_TextReport("Prepare removal (dry run, save reloaded)", text)));
                }, true));
        }
    }
}
