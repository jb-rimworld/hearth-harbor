// SPDX-License-Identifier: MIT
// Needs the mod "Definitely Not Dev Mode" (module Devil's Hospitality) loaded next to H&H, and H&H under its real packageId
// (tools/run_test.sh renames it per slot, so run this suite with NORENAME=1 or a copy of the script). Explicit only.
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Hearth.Casino;
using Hearth.Guests;
using Hearth.Testing;
using RimWorld;
using Silverline;
using Verse;

namespace Hearth.DevTools
{
    public sealed class DevToolsDndmSuite : HearthTestSuite
    {
        public override string Name => "devtools.dndm";
        public override bool ExplicitOnly => true;
        public override int MaxTicks => 20000;

        static ThingDef Def(string n) => DefDatabase<ThingDef>.GetNamedSilentFail(n);

        static ThingWithComps Place(TestContext t, ThingDef d)
        {
            var th = (ThingWithComps)ThingMaker.MakeThing(d, d.MadeFromStuff ? GenStuff.DefaultStuffFor(d) : null);
            th.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(th, t.FreeCellNearCenter(18), t.Map, Rot4.South);
            return th;
        }

        /// <summary>Makes the daily comp believe a day has passed, then lets it tick once.</summary>
        static void DueAndTick(ThingComp c)
        {
            c.GetType().GetField("lastTick", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(c, Find.TickManager.TicksGame - 70000);
            c.CompTickRare();
        }

        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var reg = GuestRegistry.Instance;
            var reg17 = AccessTools_TypeByName("DNDM.DNDMRegistry");
            t.Check(reg17 != null, "DNDM is loaded");
            if (reg17 == null) yield break;
            var modules = (System.Collections.IEnumerable)reg17.GetField("Modules", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            t.Check(modules.Cast<object>().Any(m => (string)m.GetType().GetProperty("Id").GetValue(m) == "Hospitality"), "module Hospitality is registered while H&H is active");

            string[] names = { "DNDM_Hospitality_Concierge_MkI", "DNDM_Hospitality_Concierge_MkII", "DNDM_Hospitality_Concierge_MkIII", "DNDM_Hospitality_Atm_MkI", "DNDM_Hospitality_Atm_MkII", "DNDM_Hospitality_Atm_MkIII", "DNDM_Hospitality_Croupier", "DNDM_Hospitality_Critic" };
            foreach (var n in names) t.Check(Def(n) != null && !Def(n).label.NullOrEmpty() && !Def(n).description.NullOrEmpty(), "def " + n + " with label and description");
            t.Check(Def("DNDM_Hospitality_Concierge_MkI").label == "Devil's Concierge Mk. I", "label comes from the translation: " + Def("DNDM_Hospitality_Concierge_MkI").label);

            // Concierge: Mk. II sends several groups
            int before = reg.Records.Count;
            var concierge = Place(t, Def("DNDM_Hospitality_Concierge_MkII"));
            DueAndTick(concierge.AllComps.First(c => c.GetType().Name == "CompDevilsConcierge"));
            yield return 3;
            int got = reg.Records.Count - before;
            t.Check(got >= 8 && got <= 14, "Concierge Mk. II brought about 10 guests (" + got + ")");

            // ATM: needs an account
            var atm = Place(t, Def("DNDM_Hospitality_Atm_MkI"));
            var atmComp = atm.AllComps.First(c => c.GetType().Name == "CompDevilsAtm");
            var bank = BankGameComp.Current;
            bool hadAccount = bank.HasAccount;
            double b0 = bank.Balance.Silver;
            DueAndTick(atmComp);
            if (!hadAccount) t.Check(Math.Abs(bank.Balance.Silver - b0) < 1e-9 && !bank.HasAccount, "no account: the ATM waits and books nothing");
            bank.OpenAccount(SL_BankDef.Default, true);
            double b1 = bank.Balance.Silver;
            DueAndTick(atmComp);
            t.Check(Math.Abs(bank.Balance.Silver - b1 - 300) < 0.01, "Mk. I ATM booked 300 silver (" + (bank.Balance.Silver - b1) + ")");

            // Croupier
            var slot = Place(t, Def("HH_SlotMachine"));
            var game = slot.TryGetComp<CompCasinoGame>();
            var croupier = Place(t, Def("DNDM_Hospitality_Croupier"));
            var cc = croupier.AllComps.First(c => c.GetType().Name == "CompDevilsCroupier");
            var modeField = cc.GetType().GetField("mode");
            var settle = typeof(CasinoEngine).GetMethod("Settle", BindingFlags.NonPublic | BindingFlags.Static);
            Func<double, double> payFor = payoutSilver =>
            {
                var s = new GamblerSession { Kind = SessionKind.Demo, Purse = Money.FromSilver(100), BuyIn = Money.FromSilver(100), HouseAccount = "dev", Device = slot };
                long p0 = game.PaidMicro;
                settle.Invoke(null, new object[] { game, s, Money.FromSilver(1), Money.FromSilver(payoutSilver), 0.05, 1.0, "test", null });
                return (game.PaidMicro - p0) / (double)Money.PerSilver;
            };
            modeField.SetValue(cc, Enum.ToObject(modeField.FieldType, 1));   // HouseWins
            t.Check(payFor(50) < 0.001, "house always wins: a 50x win pays nothing");
            modeField.SetValue(cc, Enum.ToObject(modeField.FieldType, 2));   // HouseLoses
            t.Check(Math.Abs(payFor(0) - 3) < 0.001, "house always loses: a lost round pays 3 stakes (" + payFor(0) + ")");
            modeField.SetValue(cc, Enum.ToObject(modeField.FieldType, 0));   // Off
            t.Check(Math.Abs(payFor(0)) < 0.001 && Math.Abs(payFor(7) - 7) < 0.001, "off: honest payouts untouched");
            croupier.Destroy();
            t.Check(Math.Abs(payFor(0)) < 0.001, "no croupier: honest payouts");

            // Critic: the departure report has five stars
            var critic = Place(t, Def("DNDM_Hospitality_Critic"));
            DepartureReport.LastReportText = null;
            HearthDebugActions.ArriveDay();
            yield return 5;
            foreach (var r in reg.OnMap(map)) r.Value = 0.1f;   // unhappy guests: without the Critic this would be a poor report
            HearthDebugActions.ForceDeparture();
            t.Check(DepartureReport.LastReportText != null && DepartureReport.LastReportText.StartsWith("★★★★★"), "Critic: five-star report (" + (DepartureReport.LastReportText ?? "null").Split('\n')[0] + ")");
            critic.Destroy();
        }

        static Type AccessTools_TypeByName(string n) => HarmonyLib.AccessTools.TypeByName(n);
    }
}
#endif
