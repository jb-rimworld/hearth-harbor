// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Verse;

namespace Hearth
{
    /// <summary>The modules of Hearth &amp; Harbor. Core is always on. Each module can be switched per save game.</summary>
    public enum ModuleId
    {
        Core = 0,
        Inn = 1,          // guests, guest beds, wings, reception, recruitment, travellers
        ShopsFood = 2,    // venue sign, shops, vending, market, kitchen and dishes
        Casino = 3,       // games, chips, colonist gambling and addiction
        Wellness = 4,     // spa, sauna, rehab
        CityLife = 5,     // cinema, mall, salon, temples, taxi, security
        Underworld = 6,   // back room, fight pit, fence, the hunt
        Bank = 7,         // Silverline: accounts, payments, ATMs, investments, loans, insurance, lottery
        AiFlavour = 8,    // optional AI texts via relay
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class HearthModuleAttribute : Attribute
    {
        public readonly ModuleId Module;
        public HearthModuleAttribute(ModuleId module) { Module = module; }
    }

    public static class Modules
    {
        public static readonly ModuleId[] All = (ModuleId[])Enum.GetValues(typeof(ModuleId));

        /// <summary>
        /// True if the module is enabled for the current game (or in the template when no game is running).
        /// Every Harmony patch of a module must return early (do nothing) when this is false.
        /// Also false while the module is paused by the error barrier (Guard).
        /// </summary>
        public static bool On(ModuleId m)
        {
            if (m == ModuleId.Core) return true;
            if (!Available(m)) return false;
            if (Guard.IsPaused(m)) return false;
            var p = HearthProfile.Current;
            return p == null ? HearthMod.Template.ModuleOn(m) : p.ModuleOn(m);
        }

        /// <summary>Is the code for this module present? Silverline standalone ships only Core + Bank.</summary>
        public static bool Available(ModuleId m)
        {
            if (m == ModuleId.Core) return true;
            if (m == ModuleId.Bank) return HearthMod.BankAssemblyLoaded;
            return HearthMod.GuestAssemblyLoaded;
        }

        public static string Label(ModuleId m) => ("HH.Module." + m).TranslateOr(DefaultLabel(m));
        public static string DefaultLabel(ModuleId m)
        {
            switch (m)
            {
                case ModuleId.Inn: return "Inn & Hotel";
                case ModuleId.ShopsFood: return "Shops & Food";
                case ModuleId.CityLife: return "City Life";
                case ModuleId.AiFlavour: return "AI flavour";
                case ModuleId.Bank: return "Bank (Silverline)";
                default: return m.ToString();
            }
        }
        public static bool DefaultOn(ModuleId m) => m != ModuleId.AiFlavour;
    }

    public static class TranslateUtil
    {
        /// <summary>Translate if the key exists, otherwise return the English fallback.</summary>
        public static string TranslateOr(this string key, string fallback)
        {
            if (key.CanTranslate()) return key.Translate().ToString();
            return fallback;
        }
    }
}
