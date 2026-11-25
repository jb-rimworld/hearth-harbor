// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Hearth.Underworld
{
    /// <summary>Entry point of the Underworld module: back room, fight pit, fence, drug lounge, smuggler's dock, the Hunt, organ clinic (KONZEPT 6.11).</summary>
    public sealed class UnderworldModule : HearthModuleBase
    {
        public override ModuleId Id => ModuleId.Underworld;

        const ModuleId M = ModuleId.Underworld;
        static readonly float[] ExposureTiers = { 0f, 0.02f, 0.04f, 0.08f, 0.10f, 0.14f, 0.20f };
        static readonly float[] FenceExposureTiers = { 0f, 0.03f, 0.06f, 0.10f, 0.14f, 0.20f, 0.30f };
        static readonly float[] IntimidateTiers = { 0f, 0f, 0.04f, 0.12f, 0.18f, 0.28f, 0.40f };

        public override void RegisterSettings()
        {
            // whispers (1.2.0: the discovery settings underworld.mode and underworld.stranger* are gone; values stored in old saves or the
            // settings file stay in the store unread, which is silent: HearthSettingsStore keeps unknown keys, nothing looks them up)
            HSettings.Percent(M, "Whispers", "underworld.whispersDecay", 0.02f, 0f, 0.2f, "Whispers decay per 15 days", "Whispers lose this share of their value every 15 days.", hardMin: 0f, hardMax: 0.9f);
            HSettings.Int(M, "Whispers", "underworld.scandalCooldownDays", 10, 1, 60, "Days between scandals", "At most one scandal per map in this many days.", hardMin: 1, hardMax: 400);
            HSettings.Percent(M, "Whispers", "underworld.exposureChance", 0.08f, 0f, 0.5f, "Chance a respectable guest stumbles on the back room",
                "Per check (about every 2 hours) while respectable guests are present and the back room was in use. Follows the difficulty slider.", tiers: ExposureTiers);
            HSettings.Float(M, "Whispers", "underworld.whispers.pit", 1f, 0f, 10f, "Whispers per fight bout", null, nerd: true, hardMax: 100f);
            HSettings.Float(M, "Whispers", "underworld.whispers.fence", 1f, 0f, 10f, "Whispers per fence deal", null, nerd: true, hardMax: 100f);
            HSettings.Float(M, "Whispers", "underworld.whispers.lounge", 0.5f, 0f, 10f, "Whispers per drug lounge session", null, nerd: true, hardMax: 100f);
            HSettings.Float(M, "Whispers", "underworld.whispers.gang", 2f, 0f, 10f, "Whispers per gang visit", null, nerd: true, hardMax: 100f);
            HSettings.Float(M, "Whispers", "underworld.whispers.tour", 1f, 0f, 10f, "Whispers per holding cells tour", null, nerd: true, hardMax: 100f);
            HSettings.Float(M, "Whispers", "underworld.whispers.hunt", 10f, 0f, 30f, "Whispers per hunt", null, nerd: true, hardMax: 100f);
            HSettings.Float(M, "Whispers", "underworld.whispers.organ", 5f, 0f, 30f, "Whispers per organ sale", null, nerd: true, hardMax: 100f);
            // fight pit
            HSettings.Percent(M, "Fight pit", "underworld.pit.houseCut", 0.10f, 0f, 0.4f, "House cut on bets", "The share of the betting pool the house keeps (totalisator).", hardMin: 0f, hardMax: 0.9f);
            HSettings.Float(M, "Fight pit", "underworld.pit.intervalHours", 8f, 1f, 48f, "Hours between bouts", "A fight pit with enough fighters and spectators starts a bout this often.", hardMin: 0.25f, hardMax: 600f);
            HSettings.Silver(M, "Fight pit", "underworld.pit.betMin", 10, 1, 100, "Smallest bet", "Guests bet at least this much silver.");
            HSettings.Silver(M, "Fight pit", "underworld.pit.betMax", 120, 20, 1000, "Largest bet", "Guests bet at most this much silver (and never more than a third of their budget).");
            HSettings.Float(M, "Fight pit", "underworld.pit.fairRatio", 2.2f, 1f, 6f, "Matchmaking fairness", "The stronger fighter may have at most this many times the combat power of the weaker one.", nerd: true, hardMin: 1f, hardMax: 100f);
            HSettings.Percent(M, "Fight pit", "underworld.pit.yieldHealth", 0.35f, 0.1f, 0.8f, "Yield at health", "Bouts of prisoners and animals end when a fighter drops below this share of health, unless they are to the death. Bouts with people end on points.", nerd: true, hardMin: 0.05f, hardMax: 0.95f);
            HSettings.Int(M, "Fight pit", "underworld.pit.maxBoutTicks", 5000, 1000, 20000, "Longest bout (ticks)", "After this time the fighter with the smaller share of his point limit wins. Equal shares are a draw, and all stakes are returned.", nerd: true, hardMin: 300, hardMax: 120000);
            PitSetup.RegisterSettings(M);
            // fence
            HSettings.Percent(M, "Fence", "underworld.fence.buyPercent", 0.40f, 0.1f, 0.9f, "Fence buys at", "Share of the market value the fence pays for stolen goods.", hardMin: 0.01f, hardMax: 2f);
            HSettings.Percent(M, "Fence", "underworld.fence.sellPercent", 0.70f, 0.2f, 1.2f, "Fence sells at", "Share of the market value shady buyers pay.", hardMin: 0.01f, hardMax: 3f);
            HSettings.Silver(M, "Fence", "underworld.fence.capital", 600, 100, 3000, "Cash box of the fence", "The fence pays from its own cash box. It tops it up from the till, and surplus goes back to the till.");
            HSettings.Percent(M, "Fence", "underworld.fence.exposureChance", 0.10f, 0f, 0.5f, "Chance the owner finds out", "Per resold stolen item: the faction it was stolen from learns where it ended up.", tiers: FenceExposureTiers);
            HSettings.Int(M, "Fence", "underworld.fence.goodwillHit", 5, 0, 40, "Goodwill lost when found out", "Before the difficulty cap of your tier.", hardMax: 200);
            // gangs
            HSettings.Float(M, "Gangs", "underworld.gang.intervalDays", 12f, 3f, 60f, "Days between gang visits", "Average time between gang visits once the back room, fight pit or fence exists.", hardMin: 0.5f, hardMax: 400f);
            HSettings.Silver(M, "Gangs", "underworld.gang.feeMin", 40, 10, 300, "Back room fee (lowest)", "What a gang member pays for the back room.");
            HSettings.Silver(M, "Gangs", "underworld.gang.feeMax", 120, 20, 500, "Back room fee (highest)", null);
            HSettings.Int(M, "Gangs", "underworld.gang.sizeMax", 5, 2, 10, "Largest gang", null, hardMin: 1, hardMax: 30);
            HSettings.Percent(M, "Gangs", "underworld.gang.intimidateChance", 0.12f, 0f, 0.6f, "Chance a gang demands protection money", "Follows the difficulty slider. On Standard Room and easier it is never more than an offer you can refuse.", tiers: IntimidateTiers);
            HSettings.Silver(M, "Gangs", "underworld.gang.protectionFee", 150, 20, 800, "Protection money demanded", null);
            HSettings.Bool(M, "Gangs", "underworld.gang.prisonAccess", false, "Gangs may visit the prison", "Allows the holding cells tour (questioning of prisoners) for gang guests. Off by default; prisons are closed to every guest otherwise.");
            HSettings.Silver(M, "Gangs", "underworld.gang.tourFee", 70, 10, 400, "Holding cells tour fee", "Paid by a gang member for questioning a prisoner for an hour.");
            // lounge, smuggler
            HSettings.Silver(M, "Drug lounge", "underworld.lounge.price", 35, 5, 200, "Price of a session", "What a guest pays in the drug lounge. Guests only take what their ideology allows.");
            HSettings.Float(M, "Smuggler's dock", "underworld.smuggler.intervalDays", 9f, 2f, 60f, "Days between landings", "Average time between night landings at a smuggler's dock.", hardMin: 0.5f, hardMax: 400f);
            HSettings.Percent(M, "Smuggler's dock", "underworld.smuggler.priceFactor", 0.30f, 0.1f, 1f, "Contraband price", "Share of the market value you pay for a crate.", hardMin: 0.01f, hardMax: 3f);
            HSettings.Percent(M, "Smuggler's dock", "underworld.smuggler.inspectChance", 0.10f, 0f, 0.5f, "Chance of an inspection", "Per landing, if an imperial authority exists.", hardMin: 0f, hardMax: 1f);
            HSettings.Silver(M, "Smuggler's dock", "underworld.smuggler.maxValue", 600, 100, 3000, "Largest crate", "Market value of the biggest crate offered.");
            FacilitySetup.RegisterSettings(M);
            // hunt
            HSettings.Silver(M, "The Hunt", "underworld.hunt.feeMin", 500, 100, 3000, "Hunt fee (lowest)", null);
            HSettings.Silver(M, "The Hunt", "underworld.hunt.feeMax", 2000, 200, 6000, "Hunt fee (highest)", null);
            HSettings.Int(M, "The Hunt", "underworld.hunt.quarryCount", 3, 1, 8, "Quarry per hunt", "Animals the party brings into the hunting ground.", hardMin: 1, hardMax: 20);
            HSettings.Float(M, "The Hunt", "underworld.hunt.intervalDays", 15f, 3f, 90f, "Days between hunt parties", null, hardMin: 0.5f, hardMax: 400f);
            HSettings.Float(M, "The Hunt", "underworld.hunt.quarryHours", 6f, 1f, 24f, "Hours a human quarry must survive", "A prisoner who survives this long or reaches the fence line is free.", hardMin: 0.5f, hardMax: 60f);
            // organs
            HSettings.Percent(M, "Organ clinic", "underworld.organ.priceFactor", 1.1f, 0.3f, 3f, "Price paid for organs", "Share of the market value the anonymous buyers pay.", hardMin: 0.05f, hardMax: 10f);
            HSettings.Float(M, "Organ clinic", "underworld.organ.orderPremium", 1.8f, 1f, 4f, "Premium on ordered organs", "Grim only: a ready organ delivered within the deadline pays this many times the normal price.", hardMin: 1f, hardMax: 20f);
            HSettings.Int(M, "Organ clinic", "underworld.organ.orderDays", 5, 2, 15, "Days to deliver an order", null, hardMin: 1, hardMax: 60);
        }

        public override void OnDefsLoaded()
        {
            Guard.Run(M, "Underworld.Defs", UwDefs.Init);
            Guard.Run(M, "Underworld.PitSetup", PitSetup.OnDefsLoaded);
            Caretaker.Register(M, "Underworld.PitCash", Caretakers.PitCash);
            Caretaker.Register(M, "Underworld.FenceCash", Caretakers.FenceCash);
            GuestActivities.Register(new Activities.PitActivity());
            GuestActivities.Register(new Activities.LoungeActivity());
            GuestActivities.Register(new Activities.FenceActivity());
            Guard.Run(M, "Underworld.FacilitySetup", FacilitySetup.OnDefsLoaded);
            Guard.Run(M, "Underworld.Guide", UwGuide.Register);
            HearthTabs.Register(new HearthTabPage { Id = "underworld", LabelKey = "HH.Tab.Underworld", LabelEn = "Underworld", Module = M, Order = 900, Draw = UnderworldPage.Draw, Visible = () => Underworld.Visible });
        }

        public override void OnGameStarted() => UwTicker.OnGame();
        public override void OnGameLoaded() => UwTicker.OnGame();

        public override void GameTick(int ticksGame) => UwTicker.Tick(ticksGame);

        public override IEnumerable<string> Diagnosis()
        {
            var c = Underworld.Comp;
            yield return $"Underworld: content {Underworld.Intensity}, whispers {Underworld.Whispers:0.0}";
            if (c != null) yield return $"  bouts {c.Bouts} · bets {c.Bets} · fence {c.DealsBought}/{c.DealsSold} · lounge {c.LoungeSessions} · gangs {c.GangVisits} · hunts {c.Hunts} · organs {c.OrgansSold} · scandals {c.Scandals}";
        }

        public override void PrepareRemoval(List<string> report)
        {
            report.Add("Underworld: bets in progress are refunded; fight pit fighters go back to normal life; hunting ground quarry stays as ordinary animals; open offers are closed with their letters.");
            FightBout.AbortAll("module removed");
            Guard.Run(M, "Underworld.CloseOffers", () =>
            {
                var c = Underworld.Comp; if (c == null) return;
                for (int i = c.Offers.Count - 1; i >= 0; i--) if (i < c.Offers.Count) Offers.Close(c.Offers[i]);
            });
        }
    }
}
