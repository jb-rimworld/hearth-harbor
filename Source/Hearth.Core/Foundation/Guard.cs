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
        static readonly Dictionary<ModuleId, int> consecutive = new Dictionary<ModuleId, int>();
        static readonly HashSet<ModuleId> paused = new HashSet<ModuleId>();
        static readonly HashSet<string> loggedSites = new HashSet<string>();
        public static readonly List<string> PauseReasons = new List<string>();

        public static bool IsPaused(ModuleId m) => m != ModuleId.Core && paused.Contains(m);
        public static IEnumerable<ModuleId> Paused => paused;

        public static void Run(ModuleId m, string site, Action a)
        {
            try { a(); Ok(m); }
            catch (Exception e) { Fail(m, site, e); }
        }

        public static T Run<T>(ModuleId m, string site, Func<T> f, T fallback)
        {
            try { var r = f(); Ok(m); return r; }
            catch (Exception e) { Fail(m, site, e); return fallback; }
        }

        static void Ok(ModuleId m) { if (consecutive.TryGetValue(m, out int c) && c != 0) consecutive[m] = 0; }

        public static void Fail(ModuleId m, string site, Exception e)
        {
            bool first;
            lock (loggedSites) first = loggedSites.Add(site + "|" + e.GetType().Name);
            if (first) HLog.Error(m.ToString(), "Error in " + site + " (logged once per site)", e);
            int c = consecutive.TryGetValue(m, out int v) ? v + 1 : 1;
            consecutive[m] = c;
            if (c >= MaxConsecutive && m != ModuleId.Core && paused.Add(m))
            {
                string reason = $"{Modules.DefaultLabel(m)} paused after {c} errors in a row ({site}: {e.GetType().Name}: {e.Message})";
                PauseReasons.Add(reason);
                HLog.Error(m.ToString(), reason);
                try
                {
                    if (Current.Game != null)
                        Find.LetterStack.ReceiveLetter("HH.Guard.PausedTitle".TranslateOr("Hearth & Harbor: module paused"),
                            "HH.Guard.PausedText".TranslateOr("A part of Hearth & Harbor ran into repeated errors and paused itself so your game keeps running.\n\n{0}\n\nYou can restart it in Mod settings → Hearth & Harbor → Diagnosis. Details are in HearthHarbor.log.").Replace("{0}", reason),
                            LetterDefOf.NegativeEvent);
                }
                catch { }
            }
        }

        public static void Resume(ModuleId m)
        {
            paused.Remove(m); consecutive[m] = 0;
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

        public static void Fixed(string checkName, string what)
        {
            TotalFixes++;
            HLog.Warn("Caretaker", checkName + ": repaired " + what);
        }

        public static void RunAll(bool force = false)
        {
            foreach (var c in Checks)
            {
                if (!Modules.On(c.Module)) continue;
                int n = Guard.Run(c.Module, "Caretaker." + c.Name, c.Run, 0);
                c.LastFixes = n; c.TotalFixes += n;
            }
        }

        internal static void Tick(int ticksGame)
        {
            if (Checks.Count == 0) return;
            // one check per 250 ticks, round robin → each check roughly every 2500 ticks with 10 checks
            if (ticksGame % 250 != 17) return;
            var c = Checks[(ticksGame / 250) % Checks.Count];
            if (!Modules.On(c.Module)) return;
            int n = Guard.Run(c.Module, "Caretaker." + c.Name, c.Run, 0);
            c.LastFixes = n; c.TotalFixes += n;
        }
    }
}
