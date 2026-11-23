// SPDX-License-Identifier: MIT
using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Hearth.Music
{
    public enum MusicMode { Off = 0, WhenHosting = 1, Always = 2 }

    /// <summary>Marks a SongDef as one of ours and holds its base weight in the playlist (the live commonality is base × core.musicShare).</summary>
    public class HHSongExtension : DefModExtension
    {
        public float baseCommonality = 1f;
    }

    /// <summary>
    /// F07/F32 (1.1.0): our three calm tracks join the normal RimWorld playlist as plain SongDefs, one per season (Common/Defs/Music). No patch:
    /// vanilla, P-Music and Music Expanded's generated "vanilla" theme all pick SongDefs by themselves. This class only follows the player's
    /// choice (core.music: Off / only in a colony that uses Hearth &amp; Harbor / Always, and core.musicShare) by setting playOnMap and
    /// commonality of our defs every few seconds. Music Expanded copies SongDefs into its own track objects at startup, so the same values
    /// are written to those copies too (reflection, only when it is loaded).
    /// </summary>
    public static class HearthMusic
    {
        public const string ModeKey = "core.music", ShareKey = "core.musicShare";
        static readonly List<SongDef> songs = new List<SongDef>();
        static readonly List<float> baseWeights = new List<float>();
        static bool meChecked; static IList meTracks; static bool meLogged;
        public static IReadOnlyList<SongDef> Songs => songs;
        public static bool LastOn { get; private set; }
        public static float LastShare { get; private set; } = 1f;

        public static void RegisterSettings()
        {
            var mode = HSettings.Enum(ModuleId.Core, "Music", ModeKey, MusicMode.WhenHosting, "Hearth & Harbor music",
                "Our three calm songs (one for spring and summer days, one for autumn, one for winter) join the normal RimWorld playlist. When hosting: only in colonies that use Hearth & Harbor (a guest bed, a venue sign or a bank account).");
            mode.Global = true;
            var share = HSettings.Float(ModuleId.Core, "Music", ShareKey, 1f, 0f, 3f, "How often our songs come up",
                "Weight of our songs in the playlist compared with one vanilla song. 1 = like any other song of the season, 0 = never.", hardMin: 0f, hardMax: 10f);
            share.Global = true;
        }

        public static void Init()
        {
            songs.Clear(); baseWeights.Clear();
            foreach (var d in DefDatabase<SongDef>.AllDefsListForReading)
            {
                var ext = d.GetModExtension<HHSongExtension>();
                if (ext == null) continue;
                songs.Add(d); baseWeights.Add(Math.Max(0f, ext.baseCommonality));
            }
            Refresh();
            HLog.Info("Core", "Music: " + songs.Count + " Hearth & Harbor songs in the playlist pool" + (MusicExpandedActive ? " (Music Expanded is active: its copies follow the settings too)" : ""));
        }

        public static MusicMode Mode => (MusicMode)Math.Max(0, Math.Min(2, HSettings.GetInt(ModeKey)));

        /// <summary>Should our songs play right now (setting + N08: in "when hosting" only a colony that uses the mod hears them)?</summary>
        public static bool WantedNow()
        {
            switch (Mode)
            {
                case MusicMode.Off: return false;
                case MusicMode.Always: return true;
                default: return Current.ProgramState == ProgramState.Playing && Shell.HearthUI.IsUsedInColony();
            }
        }

        /// <summary>Writes playOnMap and commonality of our songs from the settings. Cheap (three defs); called every 250 ticks and on game start/load.</summary>
        public static void Refresh()
        {
            bool on = false; float share = 1f;
            try { on = WantedNow(); share = Math.Max(0f, HSettings.Get(ShareKey)); }
            catch (Exception e) { HLog.Once("music.settings", LogLevel.Warn, "Core", "Music settings unreadable, songs stay off: " + e.Message); }
            if (share <= 0f) on = false;
            LastOn = on; LastShare = share;
            for (int i = 0; i < songs.Count; i++)
            {
                songs[i].playOnMap = on;
                songs[i].commonality = Math.Max(0.0001f, baseWeights[i] * share);
            }
            SyncMusicExpanded(on);
        }

        public static bool MusicExpandedActive => ModsConfig.IsActive("musicexpanded.framework") || ModsConfig.IsActive("musicexpanded.framework_steam") || GenTypes.GetTypeInAnyAssembly("MusicExpanded.TrackManager") != null;

        /// <summary>Music Expanded keeps its own copies of every SongDef (TrackDef, same clipPath). Mirror our values onto them.</summary>
        static void SyncMusicExpanded(bool on)
        {
            if (!meChecked)
            {
                meChecked = true;
                try
                {
                    var t = GenTypes.GetTypeInAnyAssembly("MusicExpanded.TrackManager");
                    var f = t == null ? null : AccessTools.Field(t, "tracks");
                    meTracks = f?.GetValue(null) as IList;
                    if (t != null && meTracks == null) HLog.Once("music.me", LogLevel.Warn, "Core", "Music Expanded found, but its track list is not where we expected: our songs follow its own theme settings");
                }
                catch (Exception e) { HLog.Once("music.me", LogLevel.Warn, "Core", "Music Expanded integration off: " + e.Message); }
            }
            if (meTracks == null) return;
            int n = SyncCopies(meTracks, on);
            if (n > 0 && !meLogged) { meLogged = true; HLog.Info("Core", "Music Expanded integration active: " + n + " track copies follow the Hearth & Harbor music settings"); }
        }

        /// <summary>Writes our current playOnMap/commonality onto foreign copies of our songs (same clipPath). Music Expanded's layout, checked
        /// against its source (original and "Continued"): <c>public static List&lt;TrackDef&gt; TrackManager.tracks</c>, <c>TrackDef : SongDef</c>,
        /// copies made by <c>TrackDef.FromSong</c> keep clipPath, and its vanilla-logic tracks ask vanilla's AppropriateNow (which reads playOnMap).
        /// Returns how many copies were updated.</summary>
        internal static int SyncCopies(IList tracks, bool on)
        {
            int n = 0;
            try
            {
                foreach (var o in tracks)
                {
                    if (!(o is SongDef track) || songs.Contains(track)) continue;
                    for (int i = 0; i < songs.Count; i++)
                        if (track.clipPath == songs[i].clipPath) { track.playOnMap = on; track.commonality = songs[i].commonality; n++; break; }
                }
            }
            catch (Exception e) { HLog.Once("music.meSync", LogLevel.Warn, "Core", "Could not update Music Expanded's copies of our songs: " + e.Message); }
            return n;
        }

        public static IEnumerable<string> Diagnosis()
        {
            yield return "Music: " + songs.Count + " songs, mode " + Mode + ", playing " + (LastOn ? "on" : "off") + ", share " + LastShare.ToString("0.##") + (meTracks != null ? ", Music Expanded copies synced" : "");
        }
    }
}
