// SPDX-License-Identifier: MIT
// Smoke suite of the developer tools (built only with HH_TESTS): every debug action runs without an exception and does what it says.
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using Hearth.Casino;
using Hearth.Guests;
using Hearth.Testing;
using RimWorld;
using Silverline;
using Verse;

namespace Hearth.DevTools
{
    public sealed class DevToolsSuite : HearthTestSuite
    {
        public override string Name => "devtools.actions";
        public override int MaxTicks => 20000;

        static bool Run(TestContext t, string what, Action a)
        {
            try { a(); t.Check(true, what + " ran"); return true; }
            catch (Exception e) { t.Check(false, what + " threw " + e.GetType().Name + ": " + e.Message); return false; }
        }

        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; var reg = GuestRegistry.Instance;
            t.Check(reg != null, "guest registry exists");
            if (reg == null) yield break;
            int groups0 = reg.Groups.Count;

            Run(t, "arrive overnight", HearthDebugActions.ArriveOvernight);
            Run(t, "arrive day visitors", HearthDebugActions.ArriveDay);
            Run(t, "arrive shady", HearthDebugActions.ArriveShady);
            t.Check(reg.Groups.Count >= groups0 + 3, "three groups registered (" + (reg.Groups.Count - groups0) + ")");
            yield return 5;

            var members = reg.OnMap(map).Where(r => r.Pawn != null).ToList();
            t.Check(members.Count >= 3, "guests on the map (" + members.Count + ")");
            Run(t, "fill budgets", HearthDebugActions.FillBudgets);
            t.Check(members.Count > 0 && members.All(r => r.Budget.Silver >= 999.9), "every budget is at least 1000 silver (min " + (members.Count > 0 ? members.Min(r => r.Budget.Silver).ToString("0") : "-") + ")");

            float renown = GuestApi.Renown(map);
            Run(t, "renown +20", HearthDebugActions.RenownPlus);
            t.Check(GuestApi.Renown(map) > renown + 19.9f, "renown rose by 20");
            Run(t, "renown -20", HearthDebugActions.RenownMinus);
            float w0 = reg.CurrentWhispers;
            Run(t, "whispers +20", HearthDebugActions.WhispersPlus);
            t.Check(reg.CurrentWhispers > w0 + 19.9f, "whispers rose by 20 (" + w0 + " -> " + reg.CurrentWhispers + ")");
            Run(t, "whispers -20", HearthDebugActions.WhispersMinus);

            Run(t, "bank open + 10000", HearthDebugActions.BankOpen);
            var bank = BankGameComp.Current;
            t.Check(bank != null && bank.HasAccount && bank.Balance.Silver >= 9999.9, "bank account holds 10000 silver (" + bank?.Balance.Silver + ")");

            var slot = DefDatabase<ThingDef>.GetNamedSilentFail("HH_SlotMachine");
            if (slot != null)
            {
                var th = ThingMaker.MakeThing(slot, slot.MadeFromStuff ? GenStuff.DefaultStuffFor(slot) : null);
                th.SetFaction(Faction.OfPlayer);
                GenSpawn.Spawn(th, t.FreeCellNearCenter(15), map, Rot4.South);
                var comp = th.TryGetComp<CompCasinoGame>();
                long paid0 = comp.PaidMicro;
                Run(t, "casino jackpot", HearthDebugActions.Jackpot);
                t.Check(comp.PaidMicro > paid0 && comp.LastResult != null && comp.LastResult.Contains("dev jackpot"), "slot machine booked the jackpot (" + comp.LastResult + ")");
                th.Destroy();
            }
            else t.Note("no HH_SlotMachine def: jackpot not tested");

            Run(t, "stranger", HearthDebugActions.Stranger);
            Run(t, "caretaker", HearthDebugActions.RunCaretaker);
            Run(t, "diagnosis dump", HearthDebugActions.DumpDiagnosis);
            yield return 5;

            DepartureReport.LastReportText = null;
            Run(t, "force departure", HearthDebugActions.ForceDeparture);
            t.Check(reg.Groups.Where(g => g.MapId == map.uniqueID).All(g => g.LeaveSent || reg.Members(g).All(r => r.Pawn == null || r.Pawn.MapHeld != map)), "all groups were told to leave");
            t.Check(!DepartureReport.LastReportText.NullOrEmpty(), "a departure report was written");

            string rep = null;
            Run(t, "removal report", () => rep = HearthDebugActions.CollectRemovalReport());
            t.Check(!rep.NullOrEmpty() && rep.Contains("=="), "removal report lists the modules");
        }
    }
}
#endif
