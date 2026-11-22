// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Hearth
{
    /// <summary>
    /// The one game component of Hearth &amp; Harbor/Silverline. Owns the per-save profile, the ledger and the random streams,
    /// drives module ticks and the caretaker. Module data that must be saved lives in each module's own GameComponent/WorldComponent
    /// (so removing a module never breaks this one).
    /// </summary>
    public sealed class HearthGame : GameComponent
    {
        public static HearthGame Instance;
        public HearthProfile Profile = new HearthProfile();
        public Ledger Ledger = new Ledger();
        public JRandState Rand = new JRandState();
        public bool IsNewGame;
        bool initialized;
        /// <summary>Local calendar day (year × 60 + day of year) of the last till flush; saved so a reload at midnight cannot flush twice.</summary>
        int lastTillDay = -1;
        /// <summary>True once ExposeData ran, i.e. this component came from a save. False after loading a save that never had the mod.</summary>
        bool exposed;

        /// <summary>
        /// Raised when a game starts or a save has been loaded (after the profile is in place). Static caches of modules and
        /// contracts (venues, guest fallbacks …) clear themselves here. Hearth.Core cannot see Hearth.dll, so they subscribe.
        /// </summary>
        public static event Action OnGameInit;

        public HearthGame(Game game) { Instance = this; }

        static void RaiseGameInit()
        {
            var h = OnGameInit; if (h == null) return;
            foreach (var d in h.GetInvocationList())
            {
                try { ((Action)d)(); }
                catch (Exception e) { HLog.Error("Core", "Subscriber of HearthGame.OnGameInit threw (ignored)", e); }
            }
        }

        public override void StartedNewGame()
        {
            Instance = this;
            Profile = HearthMod.Template.Profile.CloneForNewGame();
            Rand.Init(GenText.StableStringHash(Find.World?.info?.seedString ?? "hh") ^ (int)(DateTime.Now.Ticks & 0x7fffffff));
            IsNewGame = true; exposed = true;
            RaiseGameInit();
            HLog.Info("Core", "New game: profile copied from template, tier " + Profile.Tier.ToString("0.00"));
            foreach (var m in HearthMod.ModuleObjects) Guard.Run(m.Id, m.GetType().Name + ".OnGameStarted", m.OnGameStarted);
            initialized = true;
        }

        public override void LoadedGame()
        {
            Instance = this;
            if (Rand.Seed == 0) Rand.Init(GenText.StableStringHash(Find.World?.info?.seedString ?? "hh"));
            if (!exposed)
            {
                // the mod was added to an existing save: the component was created fresh, ExposeData never ran
                Profile = HearthMod.Template.Profile.CloneForNewGame();
                Profile.FirstStartCardDone = false;
                exposed = true;
                HLog.Info("Core", "Mod added to an existing save: new profile from template");
            }
            RaiseGameInit();
            HLog.Info("Core", $"Game loaded: tier {Profile.Tier:0.00}, modules {string.Join(",", Modules.All)}");
            foreach (var m in HearthMod.ModuleObjects) Guard.Run(m.Id, m.GetType().Name + ".OnGameLoaded", m.OnGameLoaded);
            Caretaker.RunAll();
            initialized = true;
        }

        public override void FinalizeInit()
        {
            Instance = this;
            HearthMod.Template.EverPlayed = true;
        }

        public override void GameComponentTick()
        {
            int t = Find.TickManager.TicksGame;
            var mods = HearthMod.ModuleObjects;
            for (int i = 0; i < mods.Count; i++)
            {
                var m = mods[i];
                if (!Modules.On(m.Id)) continue;
                try { m.GameTick(t); }
                catch (Exception e) { Guard.Fail(m.Id, m.GetType().Name + ".GameTick", e); }
            }
            Caretaker.Tick(t);
            if (t % 60000 == 1234) Ledger.DailyCheck();
            // midnight (local time of the first player home map): empty tills
            var home = Find.AnyPlayerHomeMap;
            if (home != null && t % 2500 == 0 && GenLocalDate.HourOfDay(home) == 0)
            {
                int localDay = GenLocalDate.Year(home) * GenDate.DaysPerYear + GenLocalDate.DayOfYear(home);
                if (lastTillDay == localDay) return;
                lastTillDay = localDay;
                Guard.Run(ModuleId.Core, "Tills.FlushAll", Tills.FlushAll);
            }
        }

        public override void ExposeData()
        {
            exposed = true;
            Scribe_Values.Look(ref lastTillDay, "lastTillDay", -1);
            Scribe_Deep.Look(ref Profile, "profile");
            Scribe_Deep.Look(ref Ledger, "ledger");
            Scribe_Deep.Look(ref Rand, "rand");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (Profile == null) { Profile = HearthMod.Template.Profile.CloneForNewGame(); Profile.FirstStartCardDone = false; HLog.Info("Core", "Mod added to an existing save: new profile from template"); }
                if (Ledger == null) Ledger = new Ledger();
                if (Rand == null) Rand = new JRandState();
            }
        }
    }

    /// <summary>Random streams for all modules (G10, 8.11). Use JRand.Stream("casino.slots") etc.</summary>
    public sealed class JRandState : IExposable
    {
        public ulong Seed;
        Dictionary<string, ulong> counters = new Dictionary<string, ulong>();
        readonly Dictionary<string, RngStream> live = new Dictionary<string, RngStream>();

        /// <summary>Sets the seed and drops all live streams (they may have been created with the old/zero seed before the game was set up).</summary>
        public void Init(int seed) { Seed = RngStream.Mix((ulong)(uint)seed ^ 0x48454152_54480000UL); live.Clear(); }

        public RngStream Stream(string name, bool sealedStream)
        {
            // map generation and component setup run before StartedNewGame/LoadedGame: seed lazily instead of caching seed 0
            if (Seed == 0) Init(GenText.StableStringHash(Current.Game?.World?.info?.seedString ?? "hh"));
            if (!live.TryGetValue(name, out var s))
            {
                ulong c = counters.TryGetValue(name, out var v) ? v : 0;
                s = new RngStream(Seed, RngStream.HashString(name), c);
                live[name] = s;
            }
            s.Algorithm = JRand.Algorithm;
            return s;
        }

        List<string> tk; List<ulong> tv;
        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving) foreach (var kv in live) counters[kv.Key] = kv.Value.Counter;
            Scribe_Values.Look(ref Seed, "seed");
            Scribe_Collections.Look(ref counters, "counters", LookMode.Value, LookMode.Value, ref tk, ref tv);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && counters == null) counters = new Dictionary<string, ulong>();
        }
    }

    public static class JRand
    {
        static RngStream fallback, vanilla;
        static HSetting sealedSetting, algoSetting;

        internal static RngAlgorithm Algorithm
        {
            get
            {
                var a = algoSetting ?? (algoSetting = HSettings.Find("core.rngAlgorithm"));
                return a == null ? RngAlgorithm.SplitMix64 : (RngAlgorithm)(int)Math.Round(HSettings.Get(a));
            }
        }
        static bool SealedFate
        {
            get
            {
                var a = sealedSetting ?? (sealedSetting = HSettings.Find("core.sealedFate"));
                return a != null && HSettings.Get(a) >= 0.5f;
            }
        }

        /// <summary>Stream for gameplay randomness (follows the fate setting).</summary>
        public static RngStream Stream(string name)
        {
            var g = HearthGame.Instance;
            if (g == null) return Fallback;
            if (!SealedFate)
            {
                // Vanilla fate: delegate to Verse.Rand so reloading re-rolls exactly like vanilla events.
                // One shared instance is re-seeded in place (no allocation); do not keep the reference across calls.
                if (vanilla == null) vanilla = new RngStream(1, 7);
                vanilla.Reseed((ulong)(uint)Rand.Int ^ ((ulong)(uint)Rand.Int << 32), 7, 0, Algorithm);
                return vanilla;
            }
            return g.Rand.Stream(name, true);
        }
        /// <summary>Sealed stream: same result after reload (player bets, bank trades, lottery).</summary>
        public static RngStream Sealed(string name)
        {
            var g = HearthGame.Instance;
            if (g == null) return Fallback;
            return g.Rand.Stream(name, true);
        }
        static RngStream Fallback => fallback ?? (fallback = new RngStream((ulong)DateTime.Now.Ticks, 1));
    }
}
