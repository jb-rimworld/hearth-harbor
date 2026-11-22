// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Hearth
{
    /// <summary>
    /// Error barrier (S8). Wrap every module loop (ticks, UI, patches with real logic) in Guard.Run.
    /// An exception is logged once with full stack, counted, and swallowed. Three failures in a row
    /// pause the module for this session: Modules.On() returns false, a letter tells the player,
    /// the diagnosis window offers "Restart module". Successful runs reset the counter.
    /// </summary>
    public static class Guard
    {
        public const int MaxConsecutive = 3;
        // failures in a row are counted per (module, site): a healthy site must not hide a broken one in the same module
        static readonly Dictionary<(ModuleId, string), int> consecutive = new Dictionary<(ModuleId, string), int>();
        static readonly HashSet<ModuleId> paused = new HashSet<ModuleId>();
        static readonly Dictionary<ModuleId, string> pauseReason = new Dictionary<ModuleId, string>();
        static readonly HashSet<string> loggedSites = new HashSet<string>();
        public static readonly List<string> PauseReasons = new List<string>();

        public static bool IsPaused(ModuleId m) => m != ModuleId.Core && paused.Contains(m);
        public static IEnumerable<ModuleId> Paused => paused;
        /// <summary>Why a module is paused (null if it is not).</summary>
        public static string ReasonFor(ModuleId m) => pauseReason.TryGetValue(m, out var r) ? r : null;

        /// <summary>Runs the action unless the module is paused (then it returns immediately).</summary>
        public static void Run(ModuleId m, string site, Action a)
        {
            if (IsPaused(m)) return;
            try { a(); Ok(m, site); }
            catch (Exception e) { Fail(m, site, e); }
        }

        /// <summary>Runs the function unless the module is paused (then it returns the fallback immediately).</summary>
        public static T Run<T>(ModuleId m, string site, Func<T> f, T fallback)
        {
            if (IsPaused(m)) return fallback;
            try { var r = f(); Ok(m, site); return r; }
            catch (Exception e) { Fail(m, site, e); return fallback; }
        }

        static void Ok(ModuleId m, string site)
        {
            if (consecutive.Count == 0) return;
            var k = (m, site);
            if (consecutive.TryGetValue(k, out int c) && c != 0) consecutive[k] = 0;
        }

        public static void Fail(ModuleId m, string site, Exception e)
        {
            bool first;
            lock (loggedSites) first = loggedSites.Add(site + "|" + e.GetType().Name);
            if (first) HLog.Error(m.ToString(), "Error in " + site + " (logged once per site)", e);
            var k = (m, site);
            int c = consecutive.TryGetValue(k, out int v) ? v + 1 : 1;
            consecutive[k] = c;
            if (c >= MaxConsecutive && m != ModuleId.Core && paused.Add(m))
            {
                string reason = $"{Modules.DefaultLabel(m)} paused after {c} errors in a row ({site}: {e.GetType().Name}: {e.Message})";
                PauseReasons.Add(reason);
                pauseReason[m] = reason;
                HLog.Error(m.ToString(), reason);
                try
                {
                    if (Current.Game != null)
                        Find.LetterStack.ReceiveLetter("HH.Guard.PausedTitle".TranslateOr("Hearth & Harbor: module paused"),
                            "HH.Guard.PausedText".TranslateOr("A part of Hearth & Harbor ran into repeated errors and paused itself so your game keeps running.\n\n{0}\n\nYou can restart it in Mod settings → Hearth & Harbor (Resume button at the top). Details are in HearthHarbor.log.").Replace("{0}", reason),
                            LetterDefOf.NegativeEvent);
                }
                catch { }
            }
        }

        /// <summary>Un-pauses a module and forgets all its failure counters.</summary>
        public static void Resume(ModuleId m)
        {
            paused.Remove(m);
            if (pauseReason.TryGetValue(m, out var r)) { PauseReasons.Remove(r); pauseReason.Remove(m); }
            foreach (var k in consecutive.Keys.Where(k => k.Item1 == m).ToList()) consecutive.Remove(k);
            HLog.Info(m.ToString(), "Module resumed by player");
        }
    }

    /// <summary>
    /// Self-repair (N69, S8). Modules register named checks; the caretaker runs them every 2500 ticks
    /// (staggered) and once after loading. A check returns how many problems it fixed; each fix must be logged
    /// by the check itself with before/after (use Caretaker.Fixed). Checks must only repair what is clearly broken.
    /// </summary>
    public static class Caretaker
    {
        public sealed class Check
        {
            public string Name; public ModuleId Module; public Func<int> Run; public int TotalFixes; public int LastFixes;
        }
        public static readonly List<Check> Checks = new List<Check>();
        public static int TotalFixes;

        public static void Register(ModuleId module, string name, Func<int> run)
        {
            if (Checks.Any(c => c.Name == name)) return;
            Checks.Add(new Check { Name = name, Module = module, Run = run });
        }

        // fixes reported while a check is running count for that check; fixes reported from elsewhere only for the total
        static int runFixed;

        public static void Fixed(string checkName, string what)
        {
            TotalFixes++; runFixed++;
            HLog.Warn("Caretaker", checkName + ": repaired " + what);
        }

        /// <summary>Runs one check and books its fixes (the larger of what it reported via Fixed and what it returned).</summary>
        static void RunCheck(Check c)
        {
            runFixed = 0;
            int n = Guard.Run(c.Module, "Caretaker." + c.Name, c.Run, 0);
            int reported = runFixed; runFixed = 0;
            if (n > reported) TotalFixes += n - reported; else n = reported;
            c.LastFixes = n; c.TotalFixes += n;
        }

        /// <summary>Runs every check. force = also checks of switched-off modules (paused modules are still skipped by the Guard).</summary>
        public static void RunAll(bool force = false)
        {
            foreach (var c in Checks.ToArray())
            {
                if (!Modules.Available(c.Module)) continue;
                if (!force && !Modules.On(c.Module)) continue;
                RunCheck(c);
            }
        }

        internal static void Tick(int ticksGame)
        {
            if (Checks.Count == 0) return;
            // one check per 250 ticks, round robin → each check roughly every 2500 ticks with 10 checks
            if (ticksGame % 250 != 17) return;
            var c = Checks[(ticksGame / 250) % Checks.Count];
            if (!Modules.On(c.Module)) return;
            RunCheck(c);
        }
    }
}
