// SPDX-License-Identifier: MIT
using System.Collections.Generic;
using System.Linq;
using Hearth;
using RimWorld;
using UnityEngine;
using Verse;

namespace Silverline
{
    /// <summary>One of the six banks (KONZEPT 2.2). Fees are fractions of the amount. Loaded from Common/Defs/Bank/Bank_Banks.xml.</summary>
    public class SL_BankDef : Def
    {
        public string slogan;
        public string iconPath;              // UI/HH/Bank/Logo_<defName>
        public int order = 100;
        public string requiresModule;        // "Underworld": only offered when this Hearth module is on
        public double depositFee;            // fraction, charged on arrival of a physical deposit
        public double withdrawFee = 0.01;    // fraction, drop pod / counter
        public double withdrawFeeMin = 5;    // silver
        public double atmFlatFee = 2;        // silver
        public double atmFee = 0.01;         // fraction
        public double accountFeePerQuadrum;  // silver, every 15 days
        public double interestMod = 1.0;     // account yearly multiple = 1 + bank.accountShare · interestMod · (M − 1), see FinanceMath.AccountYearlyMultiple
        public double loanSpread = 0.12;     // added to the yearly base rate
        public bool loansNeedTitle;          // Imperial Treasury
        public bool ignoresScore;            // Black Ledger
        public bool bailout;                 // First Rim Bank of Too Big To Fail
        public double seizureChancePerQuadrum; // Black Ledger
        public List<string> perks = new List<string>();

        [Unsaved] Texture2D icon;
        public Texture2D Icon
        {
            get
            {
                if (icon == null && !iconPath.NullOrEmpty()) icon = ContentFinder<Texture2D>.Get(iconPath, false);
                return icon ?? BaseContent.BadTex;
            }
        }

        public string Name => L.T("Bank." + defName + ".Name", label);
        public string Slogan => L.T("Bank." + defName + ".Slogan", slogan ?? "");

        /// <summary>Selectable in this game (module requirements; DLC requirements are handled by MayRequire on the def).</summary>
        public bool Available
        {
            get
            {
                if (requiresModule.NullOrEmpty()) return true;
                if (!System.Enum.TryParse(requiresModule, out ModuleId m)) return false;
                if (!Modules.On(m)) return false;
                // Underworld-bound banks show while the underworld is visible (module on, content not Cozy). Silverline standalone has no
                // Underworld module: Modules.On is false there, so such banks stay hidden as before.
                if (m == ModuleId.Underworld && HearthProfile.Current != null && HearthProfile.Current.Content == ContentIntensity.Cozy) return false;
                return true;
            }
        }

        public IEnumerable<string> PerkLines() => perks;

        public static IEnumerable<SL_BankDef> AllSorted => DefDatabase<SL_BankDef>.AllDefsListForReading.OrderBy(d => d.order);
        public static SL_BankDef Default => DefDatabase<SL_BankDef>.GetNamedSilentFail("SL_Mutual") ?? DefDatabase<SL_BankDef>.AllDefsListForReading.FirstOrDefault();
    }
}
