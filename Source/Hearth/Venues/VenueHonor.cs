// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Hearth.ShopsFood
{
    /// <summary>What an unattended guest pays at an honor-system venue.</summary>
    public enum HonorOutcome { PaidFull, PaidPart, PaidNothing }

    /// <summary>
    /// Honor system (F15, 1.1.0): a shop or tavern can run unattended. Guests take what they want and leave the money in the honor box,
    /// at the full list price. Most pay; with the chances from the settings some pay less (30–80 % of the price) and some pay nothing.
    /// A colonist in sight, a kind guest or a wealthy one makes underpaying rarer; greedy guests are less honest. The money that is
    /// paid goes through GuestApi.ChargeGuest like every sale; what is missing is only a statistic on the sign (never silver that
    /// appears or vanishes). Opt-in per sign; with somebody on duty the honor box is not used.
    /// </summary>
    public static class VenueHonor
    {
        /// <summary>Only venues that sell from the room through the shared shop code (goods and drinks) can run on trust.</summary>
        public static bool Offered(VenueTypeDef t)
        {
            var k = VenueApi.SaleOf(t)?.sells ?? SellKind.None;
            return k == SellKind.Goods || k == SellKind.Drinks;
        }

        /// <summary>Is the honor system switched on for this venue (and possible for its type)?</summary>
        public static bool IsOn(Venue v) => v?.Comp is CompVenueSignHH c && c.honorSystem && Offered(v.Type);

        /// <summary>Does the next sale go through the honor box? On, and nobody on duty.</summary>
        public static bool Applies(Venue v) => IsOn(v) && VenueState.StaffOn(v.Sign) == null;

        /// <summary>Base chances (settings) times the guest's and the room's honesty factor, capped so that some guests always pay.</summary>
        public static void Chances(Pawn guest, Venue v, out float pNothing, out float pPart, out bool watched)
        {
            float f = DishonestyFactor(guest, v, out watched);
            pNothing = Mathf.Clamp(VS.F(VS.HonorNoPay) * f, 0f, 0.9f);
            pPart = Mathf.Clamp(VS.F(VS.HonorUnderpay) * f, 0f, 0.95f - pNothing);
        }

        /// <summary>1 = the settings' chances; below 1 = more honest. Traits, wealth and being watched.</summary>
        public static float DishonestyFactor(Pawn guest, Venue v, out bool watched)
        {
            float f = 1f;
            var traits = guest?.story?.traits;
            if (traits != null)
            {
                if (Has(traits, "Kind")) f *= 0.5f;
                if (Has(traits, "Greedy")) f *= 1.5f;
                if (Has(traits, "Psychopath")) f *= 1.4f;
            }
            var info = guest != null ? GuestApi.Info(guest) : null;
            if (info != null)
            {
                if (info.Wealth >= 75) f *= 0.8f;
                else if (info.Wealth < 20) f *= 1.25f;
            }
            watched = guest != null && v != null && Watched(guest);
            if (watched) f *= Mathf.Clamp01(1f - VS.F(VS.HonorWatched));
            return f;
        }

        static bool Has(TraitSet t, string defName)
        {
            var d = DefDatabase<TraitDef>.GetNamedSilentFail(defName);
            return d != null && t.HasTrait(d);
        }

        /// <summary>An awake colonist (not a guest) within 9 cells who can see the guest.</summary>
        public static bool Watched(Pawn guest)
        {
            var map = guest.Map; if (map == null) return false;
            var list = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i];
                if (p == guest || p.Downed || !p.Awake() || GuestApi.IsGuest(p)) continue;
                if (!p.Position.InHorDistOf(guest.Position, 9f)) continue;
                if (GenSight.LineOfSight(p.Position, guest.Position, map, true)) return true;
            }
            return false;
        }

        /// <summary>Rolls one sale. <paramref name="share"/> is the part of the price the guest pays (1, 0.3–0.8, or 0).</summary>
        public static HonorOutcome Roll(float pNothing, float pPart, RngStream rng, out float share)
        {
            double r = rng.NextDouble();
            if (r < pNothing) { share = 0f; return HonorOutcome.PaidNothing; }
            if (r < pNothing + pPart) { share = (float)Math.Round(rng.Range(0.3, 0.8), 2); return HonorOutcome.PaidPart; }
            share = 1f; return HonorOutcome.PaidFull;
        }

        /// <summary>Rolls the sale of a guest at an honor-system venue.</summary>
        public static HonorOutcome Resolve(Pawn guest, Venue v, Money price, out Money paid, out bool watched)
        {
            Chances(guest, v, out float pNothing, out float pPart, out watched);
            var outcome = Roll(pNothing, pPart, JRand.Stream("venues.honor"), out float share);
            paid = outcome == HonorOutcome.PaidFull ? price : Money.FromSilver(Math.Round(price.Silver * share, 2));
            if (outcome != HonorOutcome.PaidFull && paid >= price) { outcome = HonorOutcome.PaidFull; paid = price; }
            return outcome;
        }

        /// <summary>Visible feedback after an honor sale: a coin sound for honest payers, a coloured note otherwise.</summary>
        public static void Feedback(Pawn guest, Venue v, HonorOutcome o, Money price, Money paid, bool watched)
        {
            var map = guest.Map; if (map == null) return;
            switch (o)
            {
                case HonorOutcome.PaidFull:
                    HHVenueDefOf.HH_HonorCoin?.PlayOneShot(new TargetInfo(guest.Position, map));
                    break;
                case HonorOutcome.PaidPart:
                    MoteMaker.ThrowText(guest.DrawPos + new Vector3(0f, 0f, 0.4f), map,
                        "HH.Honor.MotePart".TranslateOr("paid {0} %").Formatted(Mathf.RoundToInt((float)(paid.Silver / Math.Max(0.0001, price.Silver)) * 100f).ToString()), new Color(1f, 0.7f, 0.3f), 3f);
                    if (paid.IsPositive) HHVenueDefOf.HH_HonorCoin?.PlayOneShot(new TargetInfo(guest.Position, map));
                    break;
                default:
                    MoteMaker.ThrowText(guest.DrawPos + new Vector3(0f, 0f, 0.4f), map, "HH.Honor.MoteNothing".TranslateOr("didn't pay!"), new Color(1f, 0.4f, 0.35f), 3.5f);
                    if (watched && VS.B(VS.HonorMessages))
                        Messages.Message("HH.Honor.Witnessed".TranslateOr("{0} walked out of {1} without paying – a colonist saw it.").Formatted(guest.LabelShort, v.DisplayNameOf()),
                            new LookTargets(guest), MessageTypeDefOf.NeutralEvent, false);
                    break;
            }
        }
    }
}
