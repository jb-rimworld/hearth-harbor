// SPDX-License-Identifier: MIT
using RimWorld;
using Verse;

namespace Hearth
{
    /// <summary>
    /// What a storyteller means for Hearth &amp; Harbor (F09, 1.1.0). A plain DefModExtension, so any storyteller (also of other mods, through a
    /// patch) can say "send a few more guests" or "I suggest this hosting difficulty". Hetty Hearthwell carries one.
    /// </summary>
    public class StorytellerHints : DefModExtension
    {
        /// <summary>Multiplies the guest arrival rate while this storyteller runs the colony.</summary>
        public float guestArrivalFactor = 1f;
        /// <summary>Hosting difficulty tier (0…6) the storyteller page suggests next to the difficulty button; negative = no suggestion.</summary>
        public float suggestedTier = -1f;

        static StorytellerDef cachedFor; static StorytellerHints cached;

        /// <summary>The hints of the storyteller of the running game (null if it has none). Cheap: cached per storyteller def.</summary>
        public static StorytellerHints Current
        {
            get
            {
                var d = Find.Storyteller?.def;
                if (d != cachedFor) { cachedFor = d; cached = d?.GetModExtension<StorytellerHints>(); }
                return cached;
            }
        }

        public static StorytellerHints Of(StorytellerDef d) => d?.GetModExtension<StorytellerHints>();

        /// <summary>Guest arrival factor of the current storyteller (1 without hints).</summary>
        public static float GuestArrivalFactor
        {
            get
            {
                var h = Current;
                return h == null || h.guestArrivalFactor <= 0f ? 1f : h.guestArrivalFactor;
            }
        }
    }
}
