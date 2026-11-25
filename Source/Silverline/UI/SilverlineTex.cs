// SPDX-License-Identifier: MIT
using UnityEngine;
using Verse;

namespace Silverline
{
    /// <summary>
    /// UI textures of the bank, the insurance desk and the investment tab, loaded once on the main thread at startup (1.1.1: these were
    /// lazy static fields inside ConsoleComm, InsuranceUI and InvestTab, which made the game warn "probably needs a
    /// StaticConstructorOnStartup attribute" for each class). A field is null when its texture is missing (the Finance and Insurance
    /// folders are optional in some packages): callers then draw nothing, never a pink square.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class SilverlineTex
    {
        /// <summary>Silverline's own coin mark (comms console entry before an account exists).</summary>
        public static readonly Texture2D SilverlineMark = ContentFinder<Texture2D>.Get("UI/HH/Bank/SilverlineMark", false);
        /// <summary>Investments and lottery mark.</summary>
        public static readonly Texture2D FinanceMark = ContentFinder<Texture2D>.Get("UI/HH/Finance/FinanceMark", false);
        /// <summary>Clawback Mutual's logo.</summary>
        public static readonly Texture2D ClawbackLogo = ContentFinder<Texture2D>.Get("UI/HH/Insurance/ClawbackLogo", false);
        /// <summary>News ticker: something that just happened / something the colony caused.</summary>
        public static readonly Texture2D NewsChance = ContentFinder<Texture2D>.Get("UI/HH/Finance/NewsChance", false);
        public static readonly Texture2D NewsYou = ContentFinder<Texture2D>.Get("UI/HH/Finance/NewsYou", false);
    }
}
