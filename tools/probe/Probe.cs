// SPDX-License-Identifier: MIT
// HH save probe: a tiny test-only mod for tools/run_saveload.sh. It never ships and does not reference Hearth & Harbor,
// so it also runs in the phase WITHOUT Hearth & Harbor (removal test).
//   - always: every error/exception the game logs is appended to $HH_PROBE_ERR (one line each, the first line of the message)
//   - HH_PROBE_DAYS=<n>: after the game is running (new game or loaded save), play n days at top speed (dialogs closed,
//     storyteller silenced), then write a census to $HH_PROBE_OUT, optionally save as $HH_PROBE_SAVE, and quit.
//     Without HH_PROBE_DAYS the probe only records errors (Hearth & Harbor's own test runner drives the game).
// Loading a save at start is done by vanilla: with dev mode on, a save named "autostart" is loaded from the main menu.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace HHProbe
{
    public sealed class ProbeMod : Mod
    {
        static readonly object fileLock = new object();
        static string errPath;
        public static int Errors;

        public ProbeMod(ModContentPack content) : base(content)
        {
            errPath = Environment.GetEnvironmentVariable("HH_PROBE_ERR");
            if (string.IsNullOrEmpty(errPath)) return;
            try { File.WriteAllText(errPath, ""); } catch { }
            Application.logMessageReceivedThreaded += OnLog;
            Log.Message("[HHPROBE] recording errors to " + errPath);
        }

        static void OnLog(string msg, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            Errors++;
            string first = (msg ?? "").Split('\n')[0].Trim();
            if (first.Length > 400) first = first.Substring(0, 400) + "…";
            // the phase marker tells load-time errors from play-time errors
            string phase = Current.ProgramState == ProgramState.Playing ? "play" : "load";
            try { lock (fileLock) File.AppendAllText(errPath, phase + "\t" + type + "\t" + first + "\n"); } catch { }
        }

        internal static string Env(string k) => Environment.GetEnvironmentVariable(k);
    }

    public sealed class ProbeRunner : GameComponent
    {
        int days = -1, startTick = -1, endTick = -1;
        bool done;
        readonly StringBuilder report = new StringBuilder();

        public ProbeRunner(Game g)
        {
            int.TryParse(ProbeMod.Env("HH_PROBE_DAYS") ?? "", out days);
            if (string.IsNullOrEmpty(ProbeMod.Env("HH_PROBE_DAYS"))) days = -1;
        }

        bool Driving => days >= 0;

        public override void GameComponentUpdate()
        {
            if (!Driving) return;
            var ws = Find.WindowStack;
            if (ws != null)
                foreach (var w in ws.Windows.ToList())
                    if (w is Dialog_MessageBox || w is Dialog_NodeTree || (w.forcePause && !(w is Page)))
                    { Log.Message("[HHPROBE] closing window " + w.GetType().Name); ws.TryRemove(w, false); }
            var tm = Find.TickManager;
            if (tm != null && !done && (tm.Paused || tm.CurTimeSpeed != TimeSpeed.Ultrafast)) tm.CurTimeSpeed = TimeSpeed.Ultrafast;
        }

        public override void GameComponentTick()
        {
            if (!Driving || done) return;
            int now = Find.TickManager.TicksGame;
            if (startTick < 0)
            {
                startTick = now; endTick = now + Math.Max(1, days * 60000);
                try { Find.Storyteller?.storytellerComps?.Clear(); } catch { }
                Census("at start");
                Log.Message($"[HHPROBE] running {days} day(s) from tick {now}");
                return;
            }
            if (now < endTick) return;
            done = true;
            Census("after " + days + " day(s)");
            string save = ProbeMod.Env("HH_PROBE_SAVE");
            if (!string.IsNullOrEmpty(save))
            {
                try { GameDataSaveLoader.SaveGame(save); report.AppendLine("saved as " + save); }
                catch (Exception e) { report.AppendLine("SAVE FAILED " + e.Message); }
            }
            // translation load errors of the active language (the game only logs their number, as one warning); 1.1.1
            try
            {
                var lang = LanguageDatabase.activeLanguage;
                var errs = new System.Collections.Generic.List<string>(lang.loadErrors);
                foreach (var di in lang.defInjections) errs.AddRange(di.loadErrors);
                report.AppendLine("langErrors=" + errs.Count + " (" + lang.folderName + ")");
                foreach (var e in errs.Take(120)) report.AppendLine("LANG " + e);
            }
            catch (Exception e) { report.AppendLine("langErrors failed: " + e.Message); }
            report.AppendLine("PROBE DONE errors=" + ProbeMod.Errors);
            try { File.WriteAllText(ProbeMod.Env("HH_PROBE_OUT") ?? Path.Combine(GenFilePaths.ConfigFolderPath, "hh_probe.txt"), report.ToString()); } catch (Exception e) { Log.Warning("[HHPROBE] " + e.Message); }
            Log.Message("[HHPROBE] done:\n" + report);
            LongEventHandler.ExecuteWhenFinished(() => Root.Shutdown());
        }

        /// <summary>Generic facts that must survive removing a mod: colonists, guests leaving, physical silver.</summary>
        void Census(string when)
        {
            try
            {
                var map = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
                report.AppendLine("== " + when + " (tick " + Find.TickManager.TicksGame + ")");
                if (map == null) { report.AppendLine("no map"); return; }
                var pawns = map.mapPawns.AllPawnsSpawned;
                report.AppendLine("colonists=" + map.mapPawns.FreeColonistsSpawnedCount);
                var visitors = pawns.Where(p => p.RaceProps.Humanlike && p.Faction != null && !p.Faction.IsPlayer && !p.HostileTo(Faction.OfPlayer)).ToList();
                report.AppendLine("friendlyHumanlikeOnMap=" + visitors.Count + (visitors.Count > 0 ? " (" + string.Join(", ", visitors.Take(12).Select(p => p.LabelShort + "/" + (p.GetLord()?.LordJob?.GetType().Name ?? "no lord"))) + ")" : ""));
                report.AppendLine("silverOnMap=" + SilverOnMap(map));
                report.AppendLine("areas=" + string.Join(",", map.areaManager.AllAreas.Select(a => a.GetType().Name)));
                report.AppendLine("gameComponents=" + Current.Game.components.Count);
            }
            catch (Exception e) { report.AppendLine("census failed: " + e.Message); }
        }

        /// <summary>All physical silver on the map: stacks, pawn inventories (carried, worn containers), and inside other holders.</summary>
        public static int SilverOnMap(Map map)
        {
            int n = 0;
            var all = new List<Thing>();
            ThingOwnerUtility.GetAllThingsRecursively(map, ThingRequest.ForDef(ThingDefOf.Silver), all, true, null, true);
            foreach (var t in all) n += t.stackCount;
            return n;
        }
    }
}
