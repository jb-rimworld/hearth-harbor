// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using Hearth;
using Verse;
using Verse.Sound;

namespace Silverline
{
    /// <summary>
    /// Menu sounds of the bank, the lottery and the investment screens (F31, 1.1.0). Every sound is a SoundDef HH_Bank_&lt;Name&gt; (Common/Defs/Bank/Bank_Sounds.xml)
    /// with a file in Common/Sounds/HH/Bank. Switched by the setting bank.sounds; a missing def or a failing sound never breaks a window.
    /// </summary>
    public static class BankSound
    {
        public const string Click = "UiClick", Tick = "UiTick", Open = "WindowOpen", CoinsIn = "CoinsIn", CoinsOut = "CoinsOut", Stamp = "Stamp", Reject = "Reject",
            Ticket = "TicketStamp", Ball = "BallClick", Ratchet = "Ratchet", Win = "LotteryWin", Lose = "LotteryLose", Trade = "Trade";

        static readonly Dictionary<string, SoundDef> cache = new Dictionary<string, SoundDef>();
        static int lastTick = -1; static string lastName;
        /// <summary>Counts every sound that was asked for (tests check that actions make a sound).</summary>
        public static int Played; public static string Last;

        static SoundDef Def(string name)
        {
            if (cache.TryGetValue(name, out var d)) return d;
            d = DefDatabase<SoundDef>.GetNamedSilentFail("HH_Bank_" + name);
            if (d == null) HLog.Once("banksound." + name, LogLevel.Warn, "Bank", "Sound HH_Bank_" + name + " is not defined");
            cache[name] = d; return d;
        }

        /// <summary>Plays a menu sound on the camera. The same sound is never played twice within one frame (several buttons may react to the same click).</summary>
        public static void Play(string name)
        {
            try
            {
                if (!BankSettings.Sounds) return;
                int f = UnityEngine.Time.frameCount;
                if (f == lastTick && name == lastName) return;
                lastTick = f; lastName = name;
                Played++; Last = name;
                Def(name)?.PlayOneShotOnCamera();
            }
            catch (Exception e) { HLog.Once("banksound.fail." + name, LogLevel.Warn, "Bank", "Sound " + name + " failed: " + e.Message); }
        }

        public static void ClearCache() => cache.Clear();
    }
}
