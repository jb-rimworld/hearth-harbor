// SPDX-License-Identifier: MIT
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Hearth.Casino
{
    /// <summary>Reel symbol icons (UI/HH/Casino/Symbols/&lt;id&gt;) for the Lucky Thrumbo window and slot machines.
    /// Ids are <c>SlotPaytable.SymbolNames</c>: cherry, lemon, plum, bell, bar, seven, muffalo.</summary>
    public static class SlotSymbolIcons
    {
        const string Dir = "UI/HH/Casino/Symbols/";
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        /// <summary>Icon for a symbol id, or null if there is no texture (callers fall back to the word).</summary>
        public static Texture2D For(string symbolId)
        {
            if (symbolId.NullOrEmpty()) return null;
            if (cache.TryGetValue(symbolId, out var t)) return t;
            t = ContentFinder<Texture2D>.Get(Dir + symbolId, false);
            cache[symbolId] = t;
            return t;
        }

        /// <summary>Icon by reel index (index into <c>SlotPaytable.SymbolNames</c>), null when out of range or missing.</summary>
        public static Texture2D For(int symbolIndex)
        {
            var names = Hearth.CasinoMath.SlotPaytable.SymbolNames;
            return symbolIndex >= 0 && symbolIndex < names.Length ? For(names[symbolIndex]) : null;
        }
    }
}
