// SPDX-License-Identifier: MIT
// DLC integration, traits and genes (KONZEPT chapter 11). Entry point, settings and shared helpers.
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Hearth.Dlc
{
    public enum FundDestination { ToDeity = 0, ToFaction = 1 }

    /// <summary>
    /// Entry point of the DLC integration. It belongs to the always-on core (each feature checks the module it serves:
    /// Casino for luck, Inn for hosting and nobles, CityLife for shrines and indulgences). All DLC content is MayRequire in XML and
    /// guarded by ModsConfig at runtime, so this loads cleanly without any DLC.
    /// </summary>
    public sealed class DlcModule : HearthModuleBase
    {
        public override ModuleId Id => ModuleId.Core;

        public override void RegisterSettings()
        {
            const ModuleId M = ModuleId.Core;
            HSettings.Bool(M, "DLC and traits", "dlc.traitSpawn", true, "New traits appear on generated people",
                "Lucky, Unlucky, Born host and the other Hearth & Harbor traits can show up on newly generated pawns. Existing pawns keep theirs. Off: only added by hand or editor.");
            HSettings.Float(M, "DLC and traits", "dlc.traitRarity", 1f, 0f, 3f, "Trait frequency",
                "Multiplier on how often our traits are rolled for new pawns (1 = same rarity as most vanilla traits).", nerd: true, hardMin: 0f, hardMax: 10f);
            HSettings.Percent(ModuleId.Casino, "Luck", "dlc.luckShift", 0.015f, 0f, 0.03f, "Luck: payout shift of Lucky and Unlucky",
                "Lucky people get this many percentage points added to a game's payout plan, Unlucky people lose as many. The shift never lifts a game to 100 % or above and never bends the random draw.", hardMin: 0f, hardMax: 0.05f);
            HSettings.Float(ModuleId.CityLife, "Faith and money", "dlc.indulgenceFactor", 1f, 0.25f, 4f, "Indulgence price factor",
                "Multiplier on every indulgence and ascension price (the Faith-for-sale factor of the money-and-faith table).", nerd: true, hardMin: 0.05f, hardMax: 20f);
            HSettings.Float(ModuleId.CityLife, "Faith and money", "dlc.indulgenceCapScale", 1f, 0.5f, 4f, "Indulgence purchase caps",
                "Multiplier on the purchase limits per pawn, colony and ideoligion. 1 = limits as designed.", nerd: true, hardMin: 0.1f, hardMax: 20f);
            HSettings.Enum(ModuleId.CityLife, "Faith and money", "dlc.fundDestination", FundDestination.ToDeity, "Colonists' offerings go to",
                "To the deity: the silver simply disappears. To the faith's faction: if a faction shares the faith, it gains goodwill (+1 per 500 silver, at most +5 per quadrum). Colonists never pay into your own temple fund.");
            HSettings.Bool(ModuleId.CityLife, "Faith and money", "dlc.aiIdeoPrecepts", true, "Also give AI ideoligions money precepts",
                "Random AI ideoligions get a stance on indulgences and paid ascension once, weighted by their memes. Fixed faction ideoligions are never touched.");
            HSettings.Bool(ModuleId.CityLife, "Shrines", "dlc.shrineReactions", true, "Colonists react to shrines of other faiths",
                "Proselytizers and supremacists dislike shrines of other faiths on their land; tolerant ideoligions like them.");
            HSettings.Bool(ModuleId.Inn, "Nobles", "dlc.nobleStays", true, "Noble stays",
                "Nobles of the Empire may ask for paid lodging. You can always decline. Needs a bedroom that meets their title.");
            HSettings.Silver(ModuleId.Inn, "Nobles", "dlc.nobleBasePrice", 120, 30, 600, "Noble stay base price per noble and day",
                "Silver per noble and day before the title bonus (+15 % per 100 seniority) and the satisfaction factor.");
            HSettings.Float(ModuleId.Inn, "Nobles", "dlc.nobleMoneyFactor", 1f, 0.25f, 5f, "Noble money factor",
                "Follows the guest-money row of the difficulty table: richer on easy tiers, poorer on hard ones.", hardMin: 0f, hardMax: 20f,
                tiers: new[] { 5f, 1.6f, 1.25f, 1f, 0.85f, 0.7f, 0.5f });
            HSettings.Bool(ModuleId.Inn, "Nobles", "dlc.mandatoryBilleting", false, "Mandatory hospitality for imperial billeting",
                "Billeting orders of the Empire arrive even below Overbooked. Off: only on hard tiers (Overbooked and higher).");
            HSettings.Bool(ModuleId.Inn, "Guests", "dlc.xenoHospitality", true, "Xenotype preferences",
                "Guests' xenotypes and genes shape what rooms and goods they like (dark rooms for sanguophages, cold rooms for yttakin, ...).");
        }

        public override void OnDefsLoaded()
        {
            HLog.Info("Dlc", $"DLC integration: royalty={ModsConfig.RoyaltyActive} ideology={ModsConfig.IdeologyActive} biotech={ModsConfig.BiotechActive} anomaly={ModsConfig.AnomalyActive} odyssey={ModsConfig.OdysseyActive}");
            if (ModsConfig.IdeologyActive) GuestActivities.Register(new PrayActivity());
            Caretaker.Register(ModuleId.CityLife, "dlc.ideologyMoney", () => DlcState.Current?.Maintain() ?? 0);
            DlcDefs.Validate();
        }

        public override void OnGameStarted() { DlcState.Current?.OnGameReady(); }
        public override void OnGameLoaded() { DlcState.Current?.OnGameReady(); }

        public override void GameTick(int ticksGame)
        {
            if (ticksGame % 250 != 7) return;
            DlcState.Current?.RareTick(ticksGame);
        }

        public override IEnumerable<string> Diagnosis()
        {
            var s = DlcState.Current;
            yield return $"DLC: royalty {ModsConfig.RoyaltyActive}, ideology {ModsConfig.IdeologyActive}, biotech {ModsConfig.BiotechActive}";
            if (s != null) yield return $"DLC state: {s.PurchaseCount} indulgence purchases, {s.RankCount} ranked pawns, {s.NobleStayCount} noble stays";
        }
    }

    public static class DlcText
    {
        /// <summary>string.Format for our numbered keys ({0}); never throws on a bad translation.</summary>
        public static string Fmt(this string s, params object[] a) { try { return string.Format(s, a); } catch (FormatException) { return s; } }
    }

    /// <summary>Cached def lookups; every one is null-safe so nothing breaks without the DLC.</summary>
    public static class DlcDefs
    {
        static T Get<T>(string name) where T : Def => DefDatabase<T>.GetNamedSilentFail(name);
        public static StatDef ServiceQuality => Get<StatDef>("HH_ServiceQuality");
        public static StatDef HostingLearn => Get<StatDef>("HH_HostingLearn");
        public static StatDef ServeRestFactor => Get<StatDef>("HH_ServeRestFactor");
        public static StatDef GamblingJoy => Get<StatDef>("HH_GamblingJoy");
        public static StatDef GamblingAddiction => Get<StatDef>("HH_GamblingAddiction");
        public static WorkTypeDef Hosting => Get<WorkTypeDef>("HH_Hosting");
        public static WorkTypeDef Serving => Get<WorkTypeDef>("HH_Serving");
        public static TraitDef Lucky => Get<TraitDef>("HH_Lucky");
        public static TraitDef Unlucky => Get<TraitDef>("HH_Unlucky");
        public static TraitDef GamblingAverse => Get<TraitDef>("HH_GamblingAverse");
        public static TraitDef Gambler => Get<TraitDef>("HH_Gambler");
        public static IssueDef IssueIndulgences => Get<IssueDef>("HH_Indulgences");
        public static IssueDef IssueAscension => Get<IssueDef>("HH_PaidAscension");
        public static HistoryEventDef EvBoughtIndulgence => Get<HistoryEventDef>("HH_BoughtIndulgence");
        public static HistoryEventDef EvBoughtAscension => Get<HistoryEventDef>("HH_BoughtAscension");
        public static ThoughtDef Thought(string n) => Get<ThoughtDef>(n);

        public static bool Biotech => ModsConfig.BiotechActive;
        public static bool Royalty => ModsConfig.RoyaltyActive;
        public static bool Ideology => ModsConfig.IdeologyActive;

        /// <summary>Logs missing optional defs once; a missing core def of this module is a bug, a missing DLC def is fine.</summary>
        public static void Validate()
        {
            if (ServiceQuality == null || Lucky == null) HLog.Error("Dlc", "Core DLC defs missing (stats/traits): the Dlc defs folder did not load");
            if (Ideology && (IssueIndulgences == null || IssueAscension == null)) HLog.Error("Dlc", "Ideology is active but the money issues are missing");
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    /// <summary>Public numbers other modules read: service quality, expectations, tips, fraud detection ... (11.1, 11.2).</summary>
    public static class DlcApi
    {
        static float Stat(Pawn p, StatDef s, float fallback) => (p != null && s != null && p.RaceProps != null && p.RaceProps.Humanlike) ? p.GetStatValue(s) : fallback;
        static float Stat(Pawn p, string name, float fallback) => Stat(p, DefDatabase<StatDef>.GetNamedSilentFail(name), fallback);

        /// <summary>Service quality offset of a host, waiter or clerk (born host +0.15, grump -0.2 ...).</summary>
        public static float ServiceQuality(Pawn p) => Stat(p, DlcDefs.ServiceQuality, 0f);
        /// <summary>Added to a guest's expectations: refined palate +0.05, silver tongue -0.03 around them, abhorrent faith +0.05 (11.4).</summary>
        public static float GuestExpectationOffset(Pawn guest)
        {
            float v = Stat(guest, "HH_GuestExpectation", 0f);
            var ex = XenotypeHospitality.For(guest);
            if (ex != null && HSettings.GetBool("dlc.xenoHospitality")) v += ex.expectationOffset;
            if (DlcDefs.Ideology && IdeoStances.Of(guest, MoneyTheme.Indulgences) == MoneyStance.Abhorrent) v += 0.05f;
            return v;
        }
        /// <summary>The nearby host lowers expectations (silver tongue -0.03).</summary>
        public static float HostExpectationOffset(Pawn host) => Math.Min(0f, Stat(host, "HH_GuestExpectation", 0f));
        public static float TipFactor(Pawn guest) => Stat(guest, "HH_TipFactor", 1f);
        public static float FraudDetection(Pawn p) => Stat(p, "HH_FraudDetection", 1f);
        public static float TheftFactor(Pawn p) => Stat(p, "HH_TheftChance", 1f);
        public static float BrawlRiskFactor(Pawn p) => Stat(p, "HH_BrawlRisk", 1f);
        public static float RumorFactor(Pawn p) => Stat(p, "HH_RumorSpread", 1f);
        public static float CareProgressFactor(Pawn p) => Stat(p, "HH_CareProgress", 1f);
        public static float BankFeeFactor(Pawn p) => Stat(p, "HH_BankFeeFactor", 1f);
        public static float NegotiationBonus(Pawn p) => Stat(p, "HH_Negotiation", 0f);
        /// <summary>Multiplier on the recruitment surcharge above the price floor (silver tongue 0.9).</summary>
        public static float RecruitPriceFactor(Pawn host) => Stat(host, "HH_RecruitPrice", 1f);
        public static float FamilyCheer(Pawn p) => Stat(p, "HH_FamilyCheer", 0f);
        public static float PriceFeelOffset(Pawn seller) => Stat(seller, "HH_PriceFeel", 0f);
        /// <summary>Skill levels added for croupiers, cashiers and bank clerks (number sense +3, gambler +1).</summary>
        public static float TableSkillBonus(Pawn p) => Stat(p, "HH_TableSkill", 0f);
        /// <summary>Skill levels added to fine hand work: dealing, shuffling, house cheating (steady hands +4, dexterous fingers +3).</summary>
        public static float DexterityBonus(Pawn p) => Stat(p, "HH_Dexterity", 0f);
        /// <summary>Chance to lift a cooked dish by one quality step (refined palate 0.1).</summary>
        public static float DishQualityLift(Pawn cook) => Math.Max(0f, Stat(cook, "HH_DishQuality", 0f));
        public static float ServeRestFactor(Pawn p) => Stat(p, DlcDefs.ServeRestFactor, 1f);
    }

    /// <summary>
    /// Luck without bending the draw (11.1, 8.11). The casino module calls RtpShiftFor(player) when it builds the payout plan of a game
    /// and shows the shifted plan openly in the Pit Boss view. A shift never produces a positive expectation.
    /// </summary>
    public static class LuckApi
    {
        static readonly Dictionary<int, int> streak = new Dictionary<int, int>();
        static readonly Dictionary<int, int> lottoBought = new Dictionary<int, int>();

        public static bool IsLucky(Pawn p) => p?.story?.traits != null && DlcDefs.Lucky != null && p.story.traits.HasTrait(DlcDefs.Lucky);
        public static bool IsUnlucky(Pawn p) => p?.story?.traits != null && DlcDefs.Unlucky != null && p.story.traits.HasTrait(DlcDefs.Unlucky);

        /// <summary>Signed RTP shift (a fraction, +0.015 = +1.5 percentage points) for this player. 0 when the casino module is off.</summary>
        public static float RtpShiftFor(Pawn p)
        {
            if (!Modules.On(ModuleId.Casino) || p == null) return 0f;
            float s = HSettings.Get("dlc.luckShift");
            if (IsLucky(p)) return s;
            if (IsUnlucky(p)) return -s;
            return 0f;
        }

        /// <summary>The payout plan (RTP, 0..1) of a game for this player: base plus shift, capped below 100 % and never raised above an already generous base.</summary>
        public static float AdjustRtp(float baseRtp, Pawn p) => (float)DlcMath.LuckRtp(baseRtp, RtpShiftFor(p));

        /// <summary>False for gambling-averse pawns: they never play (addiction chance 0).</summary>
        public static bool WillGamble(Pawn p) => !(p?.story?.traits != null && DlcDefs.GamblingAverse != null && p.story.traits.HasTrait(DlcDefs.GamblingAverse));
        public static float JoyFactor(Pawn p) => p == null || DlcDefs.GamblingJoy == null || !p.RaceProps.Humanlike ? 1f : p.GetStatValue(DlcDefs.GamblingJoy);
        /// <summary>Multiplier on the addiction chance: gambler 1.5, compulsive 2.0, addiction-proof 0.2, averse 0.</summary>
        public static float AddictionFactor(Pawn p) => p == null || DlcDefs.GamblingAddiction == null || !p.RaceProps.Humanlike ? 1f : p.GetStatValue(DlcDefs.GamblingAddiction);

        /// <summary>Call for every lottery ticket the pawn buys; true if this one is free (every 10th for Lucky pawns).</summary>
        public static bool LottoTicketIsFree(Pawn p)
        {
            if (p == null || !IsLucky(p)) return false;
            lottoBought.TryGetValue(p.thingIDNumber, out int n); n++; lottoBought[p.thingIDNumber] = n;
            return DlcMath.FreeLottoTicket(n);
        }

        /// <summary>Tell us how a bet ended: Lucky pawns get "Lucky streak" after two wins in a row (others after three); Unlucky ones "Of course I lost".</summary>
        public static void NoteResult(Pawn p, bool won)
        {
            if (p?.needs?.mood?.thoughts?.memories == null) return;
            streak.TryGetValue(p.thingIDNumber, out int s);
            if (won)
            {
                s++; streak[p.thingIDNumber] = s;
                if (s >= (IsLucky(p) ? 2 : 3)) p.needs.mood.thoughts.memories.TryGainMemory(DlcDefs.Thought("HH_LuckyStreak"));
            }
            else
            {
                streak[p.thingIDNumber] = 0;
                if (IsUnlucky(p)) p.needs.mood.thoughts.memories.TryGainMemory(DlcDefs.Thought("HH_OfCourseILost"));
            }
        }
    }

    /// <summary>Gambling-averse pawns dislike rooms where the house takes bets (-3).</summary>
    public class ThoughtWorker_GamblingAround : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (!Modules.On(ModuleId.Casino) || !p.Spawned) return ThoughtState.Inactive;
            var v = Venues.At(p.Position, p.Map);
            return v != null && v.Type != null && v.Type.module == ModuleId.Casino && v.IsOpenNow ? ThoughtState.ActiveAtStage(0) : ThoughtState.Inactive;
        }
    }
}
