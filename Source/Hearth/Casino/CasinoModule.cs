// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using Hearth.CasinoMath;

namespace Hearth.Casino
{
    /// <summary>Entry point of the Casino module: games, house money, guests at the tables, colonists gambling for joy, gambling addiction.</summary>
    public sealed class CasinoModule : HearthModuleBase
    {
        public override ModuleId Id => ModuleId.Casino;

        public override void RegisterSettings() { CS.Register(); PlayerHouse.TS.Register(); }

        public override void OnDefsLoaded()
        {
            GuestActivities.Register(new CasinoGuestActivity());
            Silverline.ConsoleComms.Register(ModuleId.Casino, PlayerHouse.ThrumboComm.Instance);
            HearthTabs.Register(new HearthTabPage { Id = "casino", LabelKey = "HH.Tab.Casino", LabelEn = "Casino", Module = ModuleId.Casino, Order = 40, Draw = CasinoTab.Draw, Visible = () => CasinoTab.Any() || PlayerHouse.ThrumboCard.Available });
            Caretaker.Register(ModuleId.Casino, "Casino.DrugPolicy", Gambling.InitPolicies);
            Caretaker.Register(ModuleId.Casino, "Casino.Sessions", CasinoCaretaker.RepairSessions);
            if (DefDatabase<ThingDef>.GetNamedSilentFail("PokerTable") != null)
                HLog.Info("Casino", "Vanilla PokerTable is still buildable: add it to the venue hide-list (N49, Common/Defs/Venues) when that def type exists; HH_PokerTable replaces it.");
        }

        public override void GameTick(int ticksGame)
        {
            if (ticksGame % 250 == 11) Guard.Run(ModuleId.Casino, "Casino.Closing", CasinoBank.ClosingCheck);
        }

        public override void OnGameLoaded() => Guard.Run(ModuleId.Casino, "Casino.Loaded", () => { Gambling.InitPolicies(); CasinoCaretaker.RepairSessions(); });
        public override void OnGameStarted() => Guard.Run(ModuleId.Casino, "Casino.Started", () => Gambling.InitPolicies());

        public override void PrepareRemoval(List<string> report)
        {
            var c = CasinoComponent.Get(); if (c == null) return;
            int sessions = c.Sessions.Count;
            foreach (var s in c.Sessions.ToList()) CasinoEngine.EndSession(s, "module removed");
            int hediffs = 0;
            foreach (var p in PawnsFinder.AllMapsAndWorld_Alive.ToList())
            {
                var h = Gambling.AddictionOf(p);
                if (h != null) { p.health.RemoveHediff(h); hediffs++; }
            }
            report.Add($"Casino: {sessions} running sessions settled, {hediffs} gambling addictions removed.");
        }

        public override IEnumerable<string> Diagnosis()
        {
            var c = CasinoComponent.Get();
            yield return "Casino: sessions " + (c?.Sessions.Count ?? 0) + ", gamblers " + (c?.Records.Count ?? 0) + ", houses " + (c?.Houses.Count ?? 0);
        }
    }

    public static class CasinoCaretaker
    {
        /// <summary>Settles sessions whose pawn is gone or no longer at the device (e.g. after a crash or a removed building).</summary>
        public static int RepairSessions()
        {
            var c = CasinoComponent.Get(); if (c == null) return 0;
            int fixedCount = 0;
            foreach (var s in c.Sessions.ToList())
            {
                bool stale = s.Pawn == null || s.Pawn.Destroyed || s.Pawn.Dead || s.Device == null || s.Device.Destroyed || !s.Device.Spawned;
                if (!stale && s.Pawn.Spawned && s.Pawn.CurJob != null && s.Pawn.CurJob.def == CasinoDefOf.HH_PlayCasino) continue;
                if (!stale && Find.TickManager.TicksGame - s.StartTick < 600) continue;      // job is still starting
                if (!stale && s.Pawn.Spawned && s.Pawn.CurJob == null && s.Age < 1200) continue;
                Caretaker.Fixed("Casino.Sessions", "settled orphaned session of " + (s.Pawn?.LabelShort ?? "?"));
                CasinoEngine.EndSession(s, "orphaned session");
                fixedCount++;
            }
            return fixedCount;
        }
    }

    /// <summary>Guests want to gamble when they have money to spare and a free, staffed machine or table.</summary>
    public sealed class CasinoGuestActivity : IGuestActivity
    {
        public string Id => "casino.play";
        public ModuleId Module => ModuleId.Casino;
        static readonly Dictionary<int, int> played = new Dictionary<int, int>();
        // thingIDNumbers repeat between saves: never carry the counts over to another game
        static CasinoGuestActivity() { HearthGame.OnGameInit += () => played.Clear(); }

        public float Desire(Pawn guest, GuestInfo info)
        {
            if (info == null || guest.Map == null || !guest.Spawned || guest.Downed || guest.Drafted) return 0f;
            if (!guest.DevelopmentalStage.Adult() || !guest.RaceProps.Humanlike) return 0f;
            var c = CasinoComponent.Get(); if (c == null || c.SessionOf(guest) != null) return 0f;
            played.TryGetValue(guest.thingIDNumber, out int n);
            if (n >= 3) return 0f;
            var bank = CasinoBank.Bankroll(guest, info, guest.Map);
            if (bank < HSettings.GetSilver(CS.GuestMinBuyIn)) return 0f;
            bool any = false;
            foreach (var comp in CasinoBank.AllDevices(guest.Map))
                if (comp.GuestUsable(out _) && comp.HasFreeSeat && (!comp.NeedsCroupier || comp.Staffed)) { any = true; break; }
            if (!any) return 0f;
            float w = (float)CasinoBank.PersonalityFactor(guest) * Math.Min(2f, (float)(bank.Silver / 40.0));
            return Math.Max(0.1f, w) * (1f - 0.3f * n);
        }

        public Job TryGiveJob(Pawn guest, GuestInfo info)
        {
            bool drunk = guest.health.hediffSet.HasHediff(HediffDefOf.AlcoholHigh);
            int intellect = guest.skills?.GetSkill(SkillDefOf.Intellectual).Level ?? 6;
            var comp = CasinoJobs.FindDeviceFor(guest, guest: true, weight: d =>
            {
                // Intellect >= 8 → card games; drunk or dim → slot machines (KONZEPT 6.6)
                bool cards = d.Game != CasinoGame.Slots;
                if (cards) return intellect >= 8 ? (drunk ? 0.3 : 1.6) : (drunk ? 0.1 : 0.4);
                return drunk || intellect < 8 ? 1.8 : 0.9;
            });
            if (comp == null) return null;
            var job = CasinoJobs.MakeJob(guest, comp);
            if (job != null) { played.TryGetValue(guest.thingIDNumber, out int n); played[guest.thingIDNumber] = n + 1; }
            return job;
        }
    }
}
