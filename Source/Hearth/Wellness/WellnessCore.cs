// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Hearth.Wellness
{
    /// <summary>The three things a spa offers (KONZEPT 6.7).</summary>
    public enum WellnessKind { HotTub = 0, Sauna = 1, Massage = 2 }

    [DefOf]
    public static class WellnessDefOf
    {
        public static ThingDef HH_HotTub, HH_Sauna, HH_MassageTable;
        public static JobDef HH_UseHotTub, HH_UseSauna, HH_ReceiveMassage, HH_GiveMassage;
        public static JoyKindDef HH_Relaxation;
        public static ThoughtDef HH_Relaxed;
        public static HediffDef HH_Soothed;
        public static WorkTypeDef HH_Serving;
        static WellnessDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(WellnessDefOf)); }
    }

    /// <summary>Entry point of the Wellness module: settings, guest activity, caretaker (KONZEPT 6.7, 13.1, 13.2).</summary>
    public sealed class WellnessModule : HearthModuleBase
    {
        public override ModuleId Id => ModuleId.Wellness;

        public override void RegisterSettings() => WellnessSettings.Register();

        public override void OnDefsLoaded()
        {
            GuestActivities.Register(new SpaGuestActivity());
            Caretaker.Register(ModuleId.Wellness, "wellness.state", WellnessState.PruneStale);
            if (RehabBridge.RimWardActive) HLog.Info("Wellness", "RimWard found: patients in medical beds may use spa buildings for recreation (rehab bridge active)");
        }

        public override IEnumerable<string> Diagnosis()
        {
            var s = WellnessState.Current;
            if (s == null) yield break;
            yield return $"Wellness sessions: hot tub {s.sessionsHotTub}, sauna {s.sessionsSauna}, massage {s.sessionsMassage}; guest revenue {new Money(s.revenueMicro).ToStringShort()} silver; day passes active {s.passExpiry.Count}";
        }

        public override void PrepareRemoval(List<string> report)
        {
            // Nothing to refund or strip: day passes are small and expire by themselves; the Soothed hediff disappears after a day.
            report.Add("Wellness: buildings stay as plain furniture; day passes and Relaxed/Soothed effects run out on their own.");
        }
    }

    /// <summary>All tunables of the module (rule 4). Prices are silver; durations are in game hours.</summary>
    public static class WellnessSettings
    {
        public const string PriceHotTub = "wellness.priceHotTub", PriceSauna = "wellness.priceSauna", PriceMassage = "wellness.priceMassage", PriceDayPass = "wellness.priceDayPass";
        public const string DayPass = "wellness.dayPass", DayPassHours = "wellness.dayPassHours", SelfService = "wellness.selfServiceDiscount";
        public const string HoursHotTub = "wellness.hoursHotTub", HoursSauna = "wellness.hoursSauna", HoursMassage = "wellness.hoursMassage";
        public const string JoyFactor = "wellness.joyFactor", RestBonus = "wellness.restBonus", Soothe = "wellness.soothe";
        public const string SaunaMinTemp = "wellness.saunaMinTemp", Satisfaction = "wellness.satisfaction";
        public const string GuestWeight = "wellness.guestWeight", GuestCooldown = "wellness.guestCooldownHours";
        public const string MassageXp = "wellness.massageXp", MassageWait = "wellness.massageWaitHours", RehabJoy = "wellness.rehabJoy";

        public static void Register()
        {
            const ModuleId M = ModuleId.Wellness;
            HSettings.Silver(M, "Prices", PriceHotTub, 12, 0, 80, "Hot tub price", "What a guest pays for one soak in a hot tub. Multiplied by the venue's price factor.");
            HSettings.Silver(M, "Prices", PriceSauna, 8, 0, 60, "Sauna price", "What a guest pays for one sauna session.");
            HSettings.Silver(M, "Prices", PriceMassage, 22, 0, 120, "Massage price", "What a guest pays for one massage. Only given when a colonist with the Serving work type is free to give it.");
            HSettings.Bool(M, "Prices", DayPass, true, "Sell day passes", "Wealthier guests buy a day pass instead of paying per session. It covers hot tub and sauna, not massage.");
            HSettings.Silver(M, "Prices", PriceDayPass, 28, 0, 150, "Day pass price", "Price of the spa day pass (hot tub and sauna for the pass duration).");
            HSettings.Float(M, "Prices", DayPassHours, 12f, 2f, 24f, "Day pass duration (hours)", "How long a day pass stays valid.", nerd: true, hardMin: 1f, hardMax: 48f);
            HSettings.Percent(M, "Prices", SelfService, 0.10f, 0f, 0.5f, "Self-service discount", "Price reduction when nobody on staff is in the room. A colonist with the Serving work type standing in the room counts as staff.", hardMin: 0f, hardMax: 0.9f);

            HSettings.Float(M, "Sessions", HoursHotTub, 0.8f, 0.2f, 3f, "Hot tub session (hours)", "How long one soak lasts.", hardMin: 0.1f, hardMax: 8f);
            HSettings.Float(M, "Sessions", HoursSauna, 0.7f, 0.2f, 3f, "Sauna session (hours)", "How long one sauna session lasts.", hardMin: 0.1f, hardMax: 8f);
            HSettings.Float(M, "Sessions", HoursMassage, 0.6f, 0.2f, 3f, "Massage (hours)", "How long one massage lasts. Both masseur and client are busy that long.", hardMin: 0.1f, hardMax: 8f);
            HSettings.Float(M, "Sessions", MassageWait, 0.7f, 0.1f, 3f, "Wait for a masseur (hours)", "How long someone lies on the table waiting for a masseur before giving up.", nerd: true, hardMin: 0.05f, hardMax: 8f);
            HSettings.Float(M, "Sauna", SaunaMinTemp, 45f, 30f, 70f, "Sauna minimum temperature (°C)", "A sauna stove only counts as a working sauna when its room is at least this warm.", hardMin: 25f, hardMax: 90f);

            HSettings.Float(M, "Effects", JoyFactor, 1f, 0.2f, 3f, "Recreation gain factor", "Multiplier on the recreation colonists get from hot tub, sauna and massage.", hardMin: 0f, hardMax: 10f);
            HSettings.Percent(M, "Effects", RestBonus, 0.08f, 0f, 0.3f, "Rest bonus per session", "Share of the rest bar restored by a session (a little more for a massage). Set 0 for none.", hardMin: 0f, hardMax: 1f);
            HSettings.Bool(M, "Effects", Soothe, true, "Soothed after a session", "A session leaves the pawn soothed for about a day: pain is felt 15 % less.");
            HSettings.Float(M, "Effects", MassageXp, 120f, 0f, 600f, "Masseur experience per massage", "Social and Medicine experience the masseur earns for a full massage.", hardMin: 0f, hardMax: 5000f);
            HSettings.Float(M, "Guests", Satisfaction, 0.15f, 0f, 0.5f, "Guest satisfaction per session", "How much a finished session raises a guest's satisfaction (scale -1 to +1).", hardMin: 0f, hardMax: 1f);
            HSettings.Float(M, "Guests", GuestWeight, 1f, 0f, 4f, "Guest interest in the spa", "How strongly guests want the spa compared to other activities. 0 = they never go.", hardMin: 0f, hardMax: 20f);
            HSettings.Float(M, "Guests", GuestCooldown, 3f, 0f, 12f, "Guest pause between sessions (hours)", "A guest does not start another spa session sooner than this.", nerd: true, hardMin: 0f, hardMax: 48f);
            HSettings.Bool(M, "Rehab", RehabJoy, true, "Patients may use the spa", "With RimWard active, walking patients in a colony medical bed may visit hot tub, sauna and massage table for recreation (never the sauna after surgery or with open wounds).");
        }

        public static Money PriceOf(WellnessKind k)
        {
            switch (k)
            {
                case WellnessKind.HotTub: return HSettings.GetSilver(PriceHotTub);
                case WellnessKind.Sauna: return HSettings.GetSilver(PriceSauna);
                default: return HSettings.GetSilver(PriceMassage);
            }
        }

        public static int SessionTicks(WellnessKind k)
        {
            string key = k == WellnessKind.HotTub ? HoursHotTub : k == WellnessKind.Sauna ? HoursSauna : HoursMassage;
            return Math.Max(60, (int)Math.Round(HSettings.Get(key) * GenDate.TicksPerHour));
        }
    }

    /// <summary>Saved module state (own GameComponent, rule 7): day passes, last visit per guest, small statistics.</summary>
    public sealed class WellnessState : GameComponent
    {
        public Dictionary<int, int> passExpiry = new Dictionary<int, int>();   // pawn id -> tick the pass runs out
        public Dictionary<int, int> passVenue = new Dictionary<int, int>();    // pawn id -> venue sign thing id
        public Dictionary<int, int> lastSession = new Dictionary<int, int>();  // pawn id -> tick of last finished session
        public int sessionsHotTub, sessionsSauna, sessionsMassage;
        public long revenueMicro;
        List<int> t1, t2, t3, t4, t5, t6;

        public WellnessState(Game g) { }

        public static WellnessState Current => Current_Game?.GetComponent<WellnessState>();
        static Game Current_Game => Verse.Current.Game;

        public void RecordSession(Pawn p, WellnessKind kind)
        {
            if (p != null) lastSession[p.thingIDNumber] = Find.TickManager.TicksGame;
            if (kind == WellnessKind.HotTub) sessionsHotTub++; else if (kind == WellnessKind.Sauna) sessionsSauna++; else sessionsMassage++;
        }

        public bool HasPass(Pawn p, int venueId)
        {
            if (!passExpiry.TryGetValue(p.thingIDNumber, out int exp) || exp < Find.TickManager.TicksGame) return false;
            return !passVenue.TryGetValue(p.thingIDNumber, out int v) || v == venueId;
        }

        public void GrantPass(Pawn p, int venueId, int hours)
        {
            passExpiry[p.thingIDNumber] = Find.TickManager.TicksGame + hours * GenDate.TicksPerHour;
            passVenue[p.thingIDNumber] = venueId;
        }

        public int TicksSinceLastSession(Pawn p)
            => lastSession.TryGetValue(p.thingIDNumber, out int t) ? Find.TickManager.TicksGame - t : int.MaxValue;

        /// <summary>Caretaker: forget expired passes and ancient visit marks so the save stays small.</summary>
        public static int PruneStale()
        {
            var s = Current; if (s == null) return 0;
            int now = Find.TickManager.TicksGame, n = 0;
            foreach (var k in s.passExpiry.Where(kv => kv.Value < now).Select(kv => kv.Key).ToList()) { s.passExpiry.Remove(k); s.passVenue.Remove(k); n++; }
            foreach (var k in s.passVenue.Keys.Where(k => !s.passExpiry.ContainsKey(k)).ToList()) { s.passVenue.Remove(k); n++; }
            foreach (var k in s.lastSession.Where(kv => now - kv.Value > 5 * GenDate.TicksPerDay).Select(kv => kv.Key).ToList()) { s.lastSession.Remove(k); n++; }
            return 0; // routine tidying, not a repair: no Caretaker.Fixed line
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref passExpiry, "passExpiry", LookMode.Value, LookMode.Value, ref t1, ref t2);
            Scribe_Collections.Look(ref passVenue, "passVenue", LookMode.Value, LookMode.Value, ref t3, ref t4);
            Scribe_Collections.Look(ref lastSession, "lastSession", LookMode.Value, LookMode.Value, ref t5, ref t6);
            Scribe_Values.Look(ref sessionsHotTub, "sessionsHotTub");
            Scribe_Values.Look(ref sessionsSauna, "sessionsSauna");
            Scribe_Values.Look(ref sessionsMassage, "sessionsMassage");
            Scribe_Values.Look(ref revenueMicro, "revenueMicro");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (passExpiry == null) passExpiry = new Dictionary<int, int>();
                if (passVenue == null) passVenue = new Dictionary<int, int>();
                if (lastSession == null) lastSession = new Dictionary<int, int>();
            }
        }
    }
}
