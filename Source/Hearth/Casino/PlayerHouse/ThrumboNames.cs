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

        /// <summary>Label of a paytable row ("3 × thrumbo", "2 × berries (reels 1+2)", "berries on reel 1").</summary>
        public static string PayRow(int symbol, SlotPaytable.RowKind kind)
        {
            switch (kind)
            {
                case SlotPaytable.RowKind.Three: return Key("HH.Thrumbo.Pay.Three", "3 × " + Symbol(symbol), Symbol(symbol));
                case SlotPaytable.RowKind.FirstTwo: return Key("HH.Thrumbo.Pay.First2", "2 × " + Symbol(0) + " (reels 1+2)", Symbol(0));
                default: return Key("HH.Thrumbo.Pay.First1", Symbol(0) + " on reel 1", Symbol(0));
            }
        }

        /// <summary>"thrumbo · core · corn" for history lines and the guest machines' last result.</summary>
        public static string Reels(int a, int b, int c) => Symbol(a) + " · " + Symbol(b) + " · " + Symbol(c);

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
