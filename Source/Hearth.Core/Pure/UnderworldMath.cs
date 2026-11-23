// SPDX-License-Identifier: MIT
// Pure maths of the Underworld module (no game references) — compiled into Hearth.Core and into the unit tests.
using System;
using System.Collections.Generic;

namespace Hearth
{
    /// <summary>Features of the dark side that content intensity switches on or off (KONZEPT 6.11 table).</summary>
    public enum UwFeature
    {
        Facilities,          // back room, fence, fight pit, drug lounge, dock, gang visits
        PitToIncapacitation, // fight pit: bouts end when someone is incapacitated
        PitToDeath,          // fight pit: prisoners and animals may fight to the death (never volunteers, never children)
        PrisonInterrogation, // gang visits: questioning of prisoners
        PrisonHardQuestioning,
        HuntAnimals,
        HuntPrisoners,       // needs an ideology precept (Dark) or nothing (Grim)
        HuntPrisonersNoPrecept,
        OrganClinic,
        OrganOrders,
        ProtectionRacket,
    }

    public static class UnderworldMath
    {
        // ---- content gate ---------------------------------------------------------------------
        /// <summary>Features that need violence, threats or harm to people; the peaceful-underworld switch closes them all. Back room, fence, lounge and animal hunts stay.</summary>
        public static bool IsViolent(UwFeature f)
        {
            switch (f)
            {
                case UwFeature.PitToIncapacitation:
                case UwFeature.PitToDeath:
                case UwFeature.PrisonInterrogation:
                case UwFeature.PrisonHardQuestioning:
                case UwFeature.HuntPrisoners:
                case UwFeature.HuntPrisonersNoPrecept:
                case UwFeature.OrganClinic:
                case UwFeature.OrganOrders:
                case UwFeature.ProtectionRacket:
                    return true;
                default: return false;
            }
        }

        /// <summary>intensity: 0 Cozy, 1 Standard, 2 Dark, 3 Grim.</summary>
        public static bool Allows(int intensity, UwFeature f)
        {
            switch (f)
            {
                case UwFeature.Facilities:
                case UwFeature.PitToIncapacitation:
                case UwFeature.PrisonInterrogation:
                case UwFeature.HuntAnimals:
                case UwFeature.ProtectionRacket:
                    return intensity >= 1;
                case UwFeature.PitToDeath:
                case UwFeature.PrisonHardQuestioning:
                case UwFeature.HuntPrisoners:
                case UwFeature.OrganClinic:
                    return intensity >= 2;
                case UwFeature.HuntPrisonersNoPrecept:
                case UwFeature.OrganOrders:
                    return intensity >= 3;
                default: return false;
            }
        }

        // ---- whispers ---------------------------------------------------------------------------
        public const float WhispersMax = 100f;
        public const float WhispersDecayDays = 15f;
        /// <summary>Whispers lose <paramref name="pctPer15Days"/> (0.02 = 2 %) of their value every 15 days (continuous).</summary>
        public static float WhispersDecay(float value, float days, float pctPer15Days)
        {
            if (value <= 0f || days <= 0f) return Math.Max(0f, value);
            pctPer15Days = Math.Max(0f, Math.Min(0.99f, pctPer15Days));
            return Math.Max(0f, value * (float)Math.Pow(1.0 - pctPer15Days, days / WhispersDecayDays));
        }
        public static float WhispersClamp(float v) => float.IsNaN(v) ? 0f : Math.Max(0f, Math.Min(WhispersMax, v));
        /// <summary>Arrival weight of respectable guests: exp(-N/40) (KONZEPT 5.7).</summary>
        public static float RespectableArrivalFactor(float whispers) => (float)Math.Exp(-WhispersClamp(whispers) / 40.0);
        /// <summary>Arrival weight of outlaws: 0.2 + N/50.</summary>
        public static float OutlawArrivalFactor(float whispers) => 0.2f + WhispersClamp(whispers) / 50f;

        // ---- caps per difficulty tier (KONZEPT 9.4) ----------------------------------------------
        public static readonly float[] RenownCapPerEvent = { 0f, 0f, 2f, 3f, 8f, 15f, float.PositiveInfinity };
        public static readonly float[] GoodwillCapPerEvent = { 0f, 0f, 2f, 5f, 15f, 40f, float.PositiveInfinity };
        public static readonly float[] FineCap = { 0f, 0f, 0f, 300f, 1000f, 3000f, float.PositiveInfinity };

        /// <summary>Interpolated cap for a continuous tier 0..6 (anchors: Comped … Why?).</summary>
        public static float CapAt(float[] anchors, float tier)
        {
            tier = Math.Max(0f, Math.Min(6f, tier));
            int i = (int)Math.Floor(tier); if (i >= 6) return anchors[6];
            float a = anchors[i], b = anchors[i + 1], t = tier - i;
            if (float.IsInfinity(b)) return t >= 0.999f ? b : a;   // infinity only at the very last anchor
            return a + (b - a) * t;
        }
        /// <summary>Limits a negative effect (given as a positive magnitude) to the cap of the tier. Positive effects pass unchanged.</summary>
        public static float CapNegative(float delta, float[] anchors, float tier)
        {
            if (delta >= 0f) return delta;
            float cap = CapAt(anchors, tier);
            return Math.Max(delta, -cap);
        }

        // ---- fence ---------------------------------------------------------------------------------
        public static long FenceBuyPrice(long marketMicro, float percent) => (long)Math.Round(marketMicro * (double)percent, MidpointRounding.AwayFromZero);
        public static long FenceSellPrice(long marketMicro, float percent) => (long)Math.Round(marketMicro * (double)percent, MidpointRounding.AwayFromZero);

        // ---- totalisator ------------------------------------------------------------------------------
        public sealed class Bet { public int Bettor; public int Side; public long StakeMicro; public long PayoutMicro; }
        public sealed class Settlement
        {
            public long PoolMicro, CutMicro, HouseMicro, PaidMicro;
            public bool Refunded;      // nobody backed the winner (or draw): stakes returned minus the cut
            public bool Draw;
        }

        public const long Coin = 100_000L; // 1 silver in micro

        /// <summary>
        /// Parimutuel settlement. Stakes are summed into a pool, the house takes <paramref name="houseCut"/> (e.g. 0.10) of it,
        /// the rest goes to those who backed <paramref name="winnerSide"/> in proportion to their stake, rounded down to whole coins
        /// (physical silver). Whatever rounding leaves stays with the house, so pool = payouts + house exactly.
        /// A draw (winnerSide &lt; 0) or a winner nobody backed refunds all stakes pro rata (a draw without any cut).
        /// </summary>
        public static Settlement Settle(IList<Bet> bets, int winnerSide, double houseCut)
        {
            var s = new Settlement();
            foreach (var b in bets) s.PoolMicro += b.StakeMicro;
            houseCut = Math.Max(0, Math.Min(0.9, houseCut));
            long winnersStake = 0;
            if (winnerSide >= 0) foreach (var b in bets) if (b.Side == winnerSide) winnersStake += b.StakeMicro;
            s.Draw = winnerSide < 0;
            long cut = s.Draw ? 0 : (long)Math.Round(s.PoolMicro * houseCut, MidpointRounding.AwayFromZero);
            long net = s.PoolMicro - cut;
            s.CutMicro = cut;
            if (s.PoolMicro == 0) return s;
            bool refund = s.Draw || winnersStake == 0;
            s.Refunded = refund;
            foreach (var b in bets)
            {
                long share;
                if (refund) share = (long)Math.Floor(net * (double)b.StakeMicro / s.PoolMicro);
                else if (b.Side == winnerSide) share = (long)Math.Floor(net * (double)b.StakeMicro / winnersStake);
                else share = 0;
                share = share / Coin * Coin;
                b.PayoutMicro = share; s.PaidMicro += share;
            }
            s.HouseMicro = s.PoolMicro - s.PaidMicro;
            return s;
        }

        /// <summary>Side odds the AI uses to pick a favourite: share of the first fighter in [0.1, 0.9] from relative strength.</summary>
        public static float FavouriteShare(float powerA, float powerB)
        {
            float a = Math.Max(0.01f, powerA), b = Math.Max(0.01f, powerB);
            return Math.Max(0.1f, Math.Min(0.9f, a / (a + b)));
        }

        /// <summary>Pairs are fair when the stronger fighter has at most <paramref name="maxRatio"/> times the power of the weaker.</summary>
        public static bool FairPair(float powerA, float powerB, float maxRatio)
        {
            float lo = Math.Max(0.01f, Math.Min(powerA, powerB)), hi = Math.Max(powerA, powerB);
            return hi / lo <= maxRatio;
        }
    }
}
