// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using Hearth;
using Hearth.Markets;
using RimWorld;
using Verse;

namespace Silverline
{
    public enum AssetKind { Savings, Bond, WarBond, Index, FactionStock, Fund, ReligiousFund, Crypto, Memecoin, Nft }

    /// <summary>
    /// One tradable product of the Silverline exchange (KONZEPT 8.5). Pure data: any mod can add assets by XML
    /// (&lt;Silverline.AssetDef&gt; with a defName starting "SL_Asset_"). Numbers are the table values of 8.5.
    /// label = name in the game, description = satirical blurb, tagline = the small print.
    /// </summary>
    public class AssetDef : Def
    {
        public AssetKind kind = AssetKind.Fund;
        public string ticker;
        public string tagline;
        public int order = 100;
        public float startPrice = 100f;

        // price process (8.5): beta couples to the profitability slider, sigma per day, own jumps, hazards
        public float beta = 1f;
        public float annualAdjust = 0f;
        public float sigmaDay = 0.015f;
        public float idioLambda = 0f, idioMean = 0f, idioSd = 0f, idioMaxY = 2.5f, idioMinY = -2.5f;
        public float hazard = 0f;                 // rug pull, default, hack: per day
        public float hazardUpChance = 0f;
        public float hazardUpMult = 1f, hazardDownMult = 0.3f;
        public float alphaMean = 0f, alphaSd = 0f;        // fund manager alpha per year (log)
        public float loadCrash = -1f, loadRally = -1f, loadRazzia = 0f;   // -1 = use beta
        public float freezeDaysOnHazard = 0f;     // exchange hack: trading frozen
        public float signalWeight = 1f;           // how strongly system B signals move this asset

        // links to the game world (system B)
        public string factionDefName;             // asset only exists in worlds that have this faction
        public bool colonySignals;                // follows the player colony (wealth, raids survived)

        // memecoins: new ones appear about every 5 days and are delisted after lifeDays
        public bool rotating;
        public float lifeDays = 0f;

        // NFTs: selling takes days and costs a spread
        public float saleDaysMin = 1f, saleDaysMax = 4f, saleSpreadMin = 0.10f, saleSpreadMax = 0.30f;

        public string Ticker => ticker ?? defName;
        public bool IsSavings => kind == AssetKind.Savings;
        public bool Volatile => kind == AssetKind.Crypto || kind == AssetKind.Memecoin || kind == AssetKind.Nft;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (var e in base.ConfigErrors()) yield return e;
            if (!defName.StartsWith("SL_Asset_")) yield return "asset defName must start with SL_Asset_";
            if (startPrice <= 0) yield return "startPrice must be positive";
            if (sigmaDay < 0) yield return "sigmaDay must not be negative";
            if (rotating && lifeDays <= 0) yield return "rotating assets need lifeDays";
        }
    }

    public static class AssetDefs
    {
        public static List<AssetDef> All => DefDatabase<AssetDef>.AllDefsListForReading;
        public static AssetDef Get(string defName) => DefDatabase<AssetDef>.GetNamedSilentFail(defName);
    }
}
