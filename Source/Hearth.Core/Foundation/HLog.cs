// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RimWorld;
using Verse;

namespace Hearth
{
    public enum LogLevel { Debug = 0, Info = 1, Warn = 2, Error = 3 }

    /// <summary>
    /// Transparent logging (N68). Writes to the game log (Warn/Error always, Info only in dev mode or with verbose logging)
    /// and to its own file next to the game config: HearthHarbor.log (rotated at 2 MB, previous kept as .old).
    /// Line format: 2026-11-22 21:04:11 | tick 123456 | WARN  | Inn      | message
    /// Use the module channel ("Inn", "Bank", "Casino" …) so problems can be filtered quickly.
    /// </summary>
    public static class HLog
    {
        public const string Prefix = "[Hearth&Harbor] ";
        static readonly object Lock = new object();
        static readonly HashSet<string> OnceKeys = new HashSet<string>();
        static string filePath;
        static bool fileBroken;
        public static bool Verbose;
        /// <summary>Last lines for the in-game diagnosis window.</summary>
        public static readonly LinkedList<string> Recent = new LinkedList<string>();
        public static int ErrorCount, WarnCount;

        /// <summary>Copy of the last lines (thread-safe), for the diagnosis window and bug reports.</summary>
        public static string[] RecentSnapshot()
        {
            lock (Lock) { var a = new string[Recent.Count]; Recent.CopyTo(a, 0); return a; }
        }

        public static string FilePath
        {
            get
            {
                if (filePath == null)
                {
                    try { filePath = Path.Combine(GenFilePaths.ConfigFolderPath, "HearthHarbor.log"); }
                    catch { filePath = Path.Combine(Path.GetTempPath(), "HearthHarbor.log"); }
                }
                return filePath;
            }
        }

        public static void Debug(string channel, string msg) => Write(LogLevel.Debug, channel, msg, null);
        public static void Info(string channel, string msg) => Write(LogLevel.Info, channel, msg, null);
        public static void Warn(string channel, string msg) => Write(LogLevel.Warn, channel, msg, null);
        public static void Error(string channel, string msg, Exception e = null) => Write(LogLevel.Error, channel, msg, e);

        /// <summary>Logs only the first time this key is seen in this session (for fallbacks, missing optional mods …).</summary>
        public static void Once(string key, LogLevel level, string channel, string msg)
        {
            lock (Lock) { if (!OnceKeys.Add(key)) return; }
            Write(level, channel, msg, null);
        }

        static void Write(LogLevel level, string channel, string msg, Exception e)
        {
            if (level == LogLevel.Debug && !Verbose) return; // before any work: no formatting, no disk, no eviction of real warnings
            string tick;
            try { tick = Current.Game != null ? Find.TickManager?.TicksGame.ToString() ?? "-" : "menu"; } catch { tick = "?"; }
            string lv = level == LogLevel.Debug ? "DEBUG" : level == LogLevel.Info ? "INFO " : level == LogLevel.Warn ? "WARN " : "ERROR";
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | tick {tick} | {lv} | {(channel ?? "Core"),-8} | {msg}";
            if (e != null) line += " | " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace;
            lock (Lock)
            {
                if (level == LogLevel.Error) ErrorCount++; else if (level == LogLevel.Warn) WarnCount++;
                Recent.AddLast(line); while (Recent.Count > 300) Recent.RemoveFirst();
                AppendFile(line);
            }
            string game = Prefix + "[" + channel + "] " + msg + (e != null ? "\n" + e : "");
            try
            {
                if (level == LogLevel.Error) Log.Error(game);
                else if (level == LogLevel.Warn) Log.Warning(game);
                else if (Verbose || Prefs.DevMode) Log.Message(game);
            }
            catch { }
        }

        static void AppendFile(string line)
        {
            if (fileBroken) return;
            try
            {
                var fi = new FileInfo(FilePath);
                if (fi.Exists && fi.Length > 2_000_000)
                {
                    string old = FilePath + ".old";
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(FilePath, old);
                }
                File.AppendAllText(FilePath, line + "\n", Encoding.UTF8);
            }
            catch { fileBroken = true; }
        }

        /// <summary>Session header so each start is easy to find in the file.</summary>
        public static void SessionStart(string version)
        {
            lock (Lock) AppendFile($"\n===== Hearth & Harbor {version} · RimWorld {VersionControl.CurrentVersionStringWithRev} · {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====");
        }
    }
}
