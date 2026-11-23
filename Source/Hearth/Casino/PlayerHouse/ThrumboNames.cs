// SPDX-License-Identifier: MIT
// Lookup layer: the pure casino maths (Hearth.Core, no game references) names slot symbols, paytable rows and roulette bets in English.
// The window and the history text go through these helpers, which map the identifiers to Keyed strings (English fallback for other languages).
using Hearth.CasinoMath;
using Verse;

namespace Hearth.Casino.PlayerHouse
{
    public static class ThrumboNames
    {
        static string Key(string key, string fallback, params NamedArgument[] args)
        {
            if (!key.CanTranslate()) return fallback;
            return key.Translate(args).ToString();
        }

        public static string Symbol(int i)
        {
            string raw = i >= 0 && i < SlotPaytable.SymbolNames.Length ? SlotPaytable.SymbolNames[i] : "?";
            return Key("HH.Thrumbo.Sym." + raw, raw);
        }

        /// <summary>Maps a paytable row label of SlotPaytable.Rows() ("3 × plum", "2 × cherry (reels 1+2)", "cherry on reel 1").</summary>
        public static string PayRow(string label)
        {
            if (label == null) return "";
            const string three = "3 × ";
            if (label.StartsWith(three))
            {
                string sym = label.Substring(three.Length); int idx = System.Array.IndexOf(SlotPaytable.SymbolNames, sym);
                return idx >= 0 ? "HH.Thrumbo.Pay.Three".Translate(Symbol(idx)).ToString() : label;
            }
            if (label.StartsWith("2 × cherry")) return Key("HH.Thrumbo.Pay.Cherry2", label);
            if (label.StartsWith("cherry on reel")) return Key("HH.Thrumbo.Pay.Cherry1", label);
            return label;
        }

        /// <summary>Display name of a roulette bet (the pure Label is English).</summary>
        public static string Bet(RouletteBet b)
        {
            if (b == null) return "";
            var p = b.Pockets; string k = "HH.Thrumbo.Bet." + b.Kind;
            switch (b.Kind)
            {
                case RouletteBetKind.Straight: return Key(k, b.Label, RouletteBet.PocketName(p[0]));
                case RouletteBetKind.Split: return Key(k, b.Label, RouletteBet.PocketName(p[0]), RouletteBet.PocketName(p[1]));
                case RouletteBetKind.Street: return Key(k, b.Label, p[0].ToString(), p[p.Length - 1].ToString());
                case RouletteBetKind.SixLine: return Key(k, b.Label, p[0].ToString(), p[p.Length - 1].ToString());
                case RouletteBetKind.Corner: return Key(k, b.Label, p[0].ToString());
                case RouletteBetKind.Column: return Key(k, b.Label, p[0].ToString());
                case RouletteBetKind.Dozen: return Key(k, b.Label, ((p[0] - 1) / 12 + 1).ToString());
                default: return Key(k, b.Label);
            }
        }
    }
}
