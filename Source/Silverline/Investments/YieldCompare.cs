// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using Hearth;
using Hearth.Markets;
using Verse;

namespace Silverline
{
    /// <summary>
    /// Expected yearly return of the bank account next to the investment products (F21, 1.1.0). One source for the Terms page, the lobby tooltip
    /// and the Invest tab, so the player sees that the account pays less than investing. All numbers are the long-run averages, not promises.
    /// </summary>
    public static class YieldCompare
    {
        public struct Row { public string Label; public double YearlyPercent; public bool IsAccount; }

        /// <summary>Expected yearly gain of a product in percent (savings products follow the savings formula, the rest M^beta).</summary>
        public static double YearlyPercent(AssetDef d)
        {
            double m = FinanceSettings.M;
            double mult = d.IsSavings ? MarketMath.SavingsMultiple(m) : DetMath.Exp(MarketMath.LnMAsset(d.beta, FinanceSettings.LnMValue, d.annualAdjust));
            return (mult - 1.0) * 100.0;
        }

        static void Add(List<Row> rows, string defName)
        {
            var d = AssetDefs.Get(defName); if (d == null) return;
            rows.Add(new Row { Label = d.label.NullOrEmpty() ? d.defName : d.LabelCap.ToString(), YearlyPercent = YearlyPercent(d) });
        }

        /// <summary>Account of the current bank, savings product, a bond, the reference index, a faction stock. Always the account first.</summary>
        public static List<Row> Rows(SL_BankDef bank)
        {
            var rows = new List<Row>();
            double acc = (BankSettings.AccountMultiple(bank?.interestMod ?? 1.0) - 1.0) * 100.0;
            rows.Add(new Row { Label = L.T("Yield.Account", "Your bank account"), YearlyPercent = acc, IsAccount = true });
            if (FinanceAccounts.Available)
            {
                Add(rows, "SL_Asset_Mattress"); Add(rows, "SL_Asset_OutlanderBond"); Add(rows, "SL_Asset_Index"); Add(rows, "SL_Asset_StockMicaton");
            }
            return rows;
        }

        public static string Pct(double v) => v.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " %";

        /// <summary>"Account +4.0 % · Mattress Plus +8.0 % · Index +19.8 %" for one line under a header.</summary>
        public static string OneLine(SL_BankDef bank, int max = 3)
        {
            var rows = Rows(bank); var parts = new List<string>();
            for (int i = 0; i < rows.Count && i < max; i++) parts.Add(rows[i].Label + " " + Pct(rows[i].YearlyPercent));
            return string.Join("  ·  ", parts);
        }
    }
}
