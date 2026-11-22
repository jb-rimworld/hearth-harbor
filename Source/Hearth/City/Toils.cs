// SPDX-License-Identifier: MIT
using System;
using RimWorld;
using Verse;
using Verse.AI;

namespace Hearth.City
{
    /// <summary>Small version bridge for toils (1.7 pools toils and ticks with a delta; 1.5/1.6 do not).</summary>
    public static class Toils
    {
        public static Toil Make(string name)
        {
#if RW16PLUS
            return ToilMaker.MakeToil(name);
#else
            return new Toil { debugName = name };
#endif
        }

        /// <summary>Runs <paramref name="a"/> every tick (with the number of ticks it stands for).</summary>
        public static void OnTick(Toil t, Action<int> a)
        {
#if RW16PLUS
            t.tickIntervalAction = a;
#else
            t.tickAction = () => a(1);
#endif
        }

        public static void Comfort(Pawn pawn, int delta)
        {
#if RW16PLUS
            pawn.GainComfortFromCellIfPossible(delta);
#else
            pawn.GainComfortFromCellIfPossible();
#endif
        }

        public static bool HashTick(Pawn pawn, int interval, int delta)
        {
#if RW16PLUS
            return pawn.IsHashIntervalTick(interval, delta);
#else
            return pawn.IsHashIntervalTick(interval);
#endif
        }

        public static bool JoyTick(Pawn pawn, int delta, JoyTickFullJoyAction action, Building source)
        {
#if RW16PLUS
            return JoyUtility.JoyTickCheckEnd(pawn, delta, action, 1f, source);
#else
            return JoyUtility.JoyTickCheckEnd(pawn, action, 1f, source);
#endif
        }
    }
}
