// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Hearth;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Silverline
{
    /// <summary>
    /// The visible side of the collection ladder (KONZEPT 8.9). Standard Room stops at stage 4 ("friendly visit"): two men in bowler hats
    /// stand around and nothing is ever taken. Stages 5 to 7 only exist on harder difficulties.
    /// </summary>
    public static class Collections
    {
        /// <summary>Tests: record what would happen instead of spawning raiders.</summary>
        public static bool TestMode;
        public static readonly List<string> TestLog = new List<string>();

        public static Faction Agency
        {
            get
            {
                var def = SLDefOf.SL_CollectionsAgency; if (def == null) return null;
                var f = Find.FactionManager.FirstFactionOfDef(def);
                if (f == null)
                {
                    f = FactionGenerator.NewGeneratedFaction(new FactionGeneratorParms(def));
                    // temporary: vanilla removes the faction by itself (FactionManager.Remove) once none of its people is left on a map, so an
                    // idle save carries no Silverline faction at all (and no "hostility" letters are sent for it). It is created again on demand.
                    f.temporary = true;
                    Find.FactionManager.Add(f);
                    HLog.Info("Bank", "Created the hidden faction " + f.Name);
                }
                return f;
            }
        }

        static void Note(string what) { if (TestMode) TestLog.Add(what); HLog.Debug("Bank", "Collections: " + what); }

        public static void Execute(BankGameComp c, Loan l, int stage)
        {
            Note($"loan {l.Id} stage {stage}");
            switch (stage)
            {
                case 1: Reminder(c, l); break;
                case 2: LateFee(c, l); break;
                case 3: OffsetLetter(c, l); break;
                case 4: FriendlyVisit(c, l); break;
                case 5: Seizure(c, l); break;
                case 6: Raid(c, l, false); break;
                default: Raid(c, l, true); break;
            }
        }

        static void Letter(string title, string text, LetterDef def, LookTargets look = null)
        {
            if (TestMode) return;
            Find.LetterStack.ReceiveLetter(title, text, def, look ?? LookTargets.Invalid);
        }

        static void Reminder(BankGameComp c, Loan l)
        {
            Letter(L.T("Letter.ReminderTitle", "A gentle reminder"),
                L.T("Letter.Reminder", "Dear customer,\n\nwe noticed that an instalment of {0} silver on loan {1} has not yet reached us. We are sure this is an oversight, and we remain, as ever, entirely at your service.\n\nWarm regards,\nClerk Ottoline, {2}", l.Arrears.ToStringShort(), l.Id, c.BankName),
                LetterDefOf.NeutralEvent);
        }

        static void LateFee(BankGameComp c, Loan l)
        {
            c.ApplyLateFee(l);
            c.LowerScore(-FinanceMath.ScoreLateStage);
            Letter(L.T("Letter.LateTitle", "Payment overdue"),
                L.T("Letter.Late", "Dear customer,\n\nloan {0} is now several days behind. A late fee has been added and your credit score has taken a small, unavoidable dent. The amount due is {1} silver.\n\nThe tone of these letters will change.\n\nClerk Ottoline", l.Id, l.Arrears.ToStringShort()),
                LetterDefOf.NegativeEvent);
        }

        static void OffsetLetter(BankGameComp c, Loan l)
        {
            Letter(L.T("Letter.OffsetTitle", "Offset against deposits"),
                L.T("Letter.Offset", "Until loan {0} is settled, half of every deposit that reaches your account is applied to the debt, and no new loans are available. This is the part of the contract nobody remembers signing.", l.Id),
                LetterDefOf.NegativeEvent);
        }

        // --- stage 4: friendly visit ---------------------------------------------------------------------
        static void FriendlyVisit(BankGameComp c, Loan l)
        {
            var map = BankGameComp.DefaultMap; if (map == null) return;
            if (TestMode) { SpawnVisitors(c, map, l); return; }
            SpawnVisitors(c, map, l);
        }

        public static List<Pawn> SpawnVisitors(BankGameComp c, Map map, Loan l)
        {
            c.Collectors.RemoveAll(p => p == null || p.Destroyed || !p.Spawned);
            if (c.Collectors.Count >= 2) return c.Collectors.ToList();
            var agency = Agency; if (agency == null) return new List<Pawn>();
            var kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Mercenary_Gunner");
            var pawns = new List<Pawn>();
            if (!RCellFinder.TryFindRandomPawnEntryCell(out var entry, map, CellFinder.EdgeRoadChance_Neutral)) entry = CellFinder.RandomEdgeCell(map);
            for (int i = 0; i < 2; i++)
            {
                var p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, agency, PawnGenerationContext.NonPlayer, canGeneratePawnRelations: false, forceGenerateNewPawn: true, fixedGender: Gender.Male));
                Dress(p);
                GenSpawn.Spawn(p, CellFinder.RandomClosewalkCellNear(entry, map, 6), map);
                pawns.Add(p); c.Collectors.Add(p);
            }
            var spot = DropCellFinder.TradeDropSpot(map);
            LordMaker.MakeNewLord(agency, new LordJob_VisitColony(agency, spot, 5 * 60000), map, pawns);
            Letter(L.T("Letter.VisitTitle", "A friendly visit"),
                L.T("Letter.Visit", "Two representatives of Rim Recovery Partners have arrived. They are wearing bowler hats. They have no weapons and no appointment. They will stand near your door, quite calmly, until loan {0} is paid. They never attack, and they leave the moment the money does.", l.Id),
                LetterDefOf.NegativeEvent, new LookTargets(pawns));
            return pawns;
        }

        static void Dress(Pawn p)
        {
            try
            {
                p.equipment?.DestroyAllEquipment();
                p.apparel?.DestroyAll();
                var cloth = DefDatabase<ThingDef>.GetNamedSilentFail("Cloth");
                foreach (var name in new[] { "Apparel_Pants", "Apparel_CollarShirt", "Apparel_Duster", "Apparel_BowlerHat" })
                {
                    var def = DefDatabase<ThingDef>.GetNamedSilentFail(name); if (def == null) continue;
                    var a = (Apparel)ThingMaker.MakeThing(def, def.MadeFromStuff ? cloth : null);
                    p.apparel.Wear(a, false);
                }
            }
            catch (Exception e) { HLog.Warn("Bank", "Could not dress the collectors: " + e.Message); }
        }

        /// <summary>Called when a payment cleared an overdue loan: the visitors leave.</summary>
        public static void AfterPayment(BankGameComp c)
        {
            if (c.Loans.Any(l => l.Stage >= 4 && l.InArrears)) return;
            var pawns = c.Collectors.Where(p => p != null && p.Spawned && !p.Dead).ToList();
            c.Collectors.Clear();
            if (pawns.Count == 0) return;
            foreach (var p in pawns) p.GetLord()?.Notify_PawnLost(p, PawnLostCondition.ForcedToJoinOtherLord);
            foreach (var g in pawns.GroupBy(p => p.Map))
                LordMaker.MakeNewLord(Agency, new LordJob_ExitMapBest(LocomotionUrgency.Walk), g.Key, g.ToList());
            Note("collectors leave");
        }

        // --- stage 5: seizure -----------------------------------------------------------------------------
        static void Seizure(BankGameComp c, Loan l)
        {
            c.LowerScore(-FinanceMath.ScoreDefault);
            var map = BankGameComp.DefaultMap; if (map == null) return;
            if (TestMode) { TestLog.Add("seizure value " + FinanceMath.SeizureValue(l.Balance.Silver).ToString("0")); return; }
            double target = FinanceMath.SeizureValue(l.Balance.Silver);
            var items = SeizableItems(map);
            double have = items.Sum(t => (double)t.MarketValue * t.stackCount);
            var team = SpawnTeam(map, Math.Max(60f, (float)FinanceMath.CollectionRaidPoints(l.Balance.Silver, StorytellerUtility.DefaultThreatPointsNow(map), HSettings.Get("bank.collectionAnger")) * 0.5f), false);
            var node = new DiaNode(L.T("Seizure.Text", "A seizure team from Rim Recovery Partners is at the door with a court order that appears to be a laminated napkin. They want valuables worth about {0} silver (120 % of loan {1}). Let them take it, and the loan is gone. Refuse, and they will take it the hard way.", ((int)target), l.Id));
            node.options.Add(new DiaOption(L.T("Seizure.Allow", "Let them take the valuables")) { action = () => ExecuteSeizure(c, l, map, team, target), resolveTree = true });
            node.options.Add(new DiaOption(L.T("Seizure.Refuse", "Refuse")) { action = () => TurnHostile(map, team), resolveTree = true });
            Find.WindowStack.Add(new Dialog_NodeTree(node, true, false, L.T("Seizure.Title", "Seizure notice")));
        }

        static List<Thing> SeizableItems(Map map)
        {
            return map.listerThings.AllThings.Where(t => t.Spawned && t.def.category == ThingCategory.Item && t.def.EverHaulable && t.MarketValue > 0.01f && t.IsInAnyStorage() && !t.IsForbidden(Faction.OfPlayer))
                .OrderByDescending(t => t.def == ThingDefOf.Silver ? 0f : t.MarketValue).ToList();
        }

        public static double ExecuteSeizure(BankGameComp c, Loan l, Map map, List<Pawn> team, double target)
        {
            double taken = 0; var names = new List<string>();
            foreach (var t in SeizableItems(map))
            {
                if (taken >= target) break;
                double per = t.MarketValue;
                int n = (int)Math.Min(t.stackCount, Math.Ceiling((target - taken) / Math.Max(0.01, per)));
                if (n <= 0) continue;
                taken += per * n; names.Add(n > 1 ? t.LabelNoCount + " x" + n : t.LabelNoCount);
                if (n >= t.stackCount) t.Destroy(); else t.SplitOff(n).Destroy();
            }
            double covered = taken / 1.2;
            var pay = Money.Min(l.Balance, Money.FromSilver(covered));
            if (pay.Micro > 0 && c.Loans.Contains(l))
            {
                Ledger.Current.Post("world", BankGameComp.Colony, pay, L.T("Memo.Seizure", "Seizure proceeds (loan {0})", l.Id), false);
                c.ApplyPayment(l, pay, BankGameComp.Colony, L.T("Memo.SeizurePay", "Loan {0}: paid by seizure", l.Id), false);
            }
            Find.LetterStack.ReceiveLetter(L.T("Seizure.DoneTitle", "Seized"), L.T("Seizure.Done", "The team loaded up {0} silver worth of your belongings ({1}) and left, whistling. The debt is {2}.",
                (int)taken, string.Join(", ", names.Take(6)) + (names.Count > 6 ? ", ..." : ""), c.Loans.Contains(l) ? L.T("Seizure.Partly", "only partly settled") : L.T("Seizure.Settled", "settled")), LetterDefOf.NegativeEvent);
            if (team != null) foreach (var g in team.Where(p => p.Spawned).GroupBy(p => p.Map)) LordMaker.MakeNewLord(Agency, new LordJob_ExitMapBest(LocomotionUrgency.Walk), g.Key, g.ToList());
            return taken;
        }

        static void TurnHostile(Map map, List<Pawn> team)
        {
            var agency = Agency; if (agency == null) return;
            agency.SetRelationDirect(Faction.OfPlayer, FactionRelationKind.Hostile, false);
            var pawns = team.Where(p => p.Spawned).ToList();
            foreach (var p in pawns) p.GetLord()?.Notify_PawnLost(p, PawnLostCondition.ForcedToJoinOtherLord);
            if (pawns.Count > 0) LordMaker.MakeNewLord(agency, new LordJob_AssaultColony(agency, false, true, false, false, true), map, pawns);
            Find.LetterStack.ReceiveLetter(L.T("Seizure.RefusedTitle", "They are not leaving"), L.T("Seizure.Refused", "The recovery team politely stops being polite."), LetterDefOf.ThreatSmall, pawns);
        }

        // --- stage 6/7: collection raid -------------------------------------------------------------------
        static void Raid(BankGameComp c, Loan l, bool bounty)
        {
            var map = BankGameComp.DefaultMap; if (map == null) return;
            double pts = FinanceMath.CollectionRaidPoints(l.Balance.Silver, StorytellerUtility.DefaultThreatPointsNow(map), HSettings.Get("bank.collectionAnger")) * (bounty ? 1.5 : 1.0);
            if (TestMode) { TestLog.Add("raid points " + pts.ToString("0")); return; }
            if (pts < 1) return;
            var agency = Agency; if (agency == null) return;
            agency.SetRelationDirect(Faction.OfPlayer, FactionRelationKind.Hostile, false);
            var pawns = SpawnTeam(map, (float)Math.Max(60, pts), true);
            LordMaker.MakeNewLord(agency, new LordJob_AssaultColony(agency, false, true, false, true, true), map, pawns);
            Find.LetterStack.ReceiveLetter(
                bounty ? L.T("Raid.BountyTitle", "Bounty") : L.T("Raid.Title", "Collection raid"),
                bounty ? L.T("Raid.Bounty", "{0} has sold the debt of loan {1} to people who do not send letters. There is a price on the head of your colony's leader.", c.BankName, l.Id)
                       : L.T("Raid.Text", "Rim Recovery Partners has run out of patience with loan {0}. Their agents are coming for valuables worth more than the debt. They will take what they can carry and go.", l.Id),
                LetterDefOf.ThreatBig, pawns);
        }

        /// <summary>A group of recovery agents, walking in from the map edge. Visitors when not <paramref name="hostile"/> (a lord is attached by the caller if hostile).</summary>
        public static List<Pawn> SpawnTeam(Map map, float points, bool hostile)
        {
            var agency = Agency;
            var parms = new IncidentParms { target = map, faction = agency, points = points, raidStrategy = RaidStrategyDefOf.ImmediateAttack, raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn };
            var gp = IncidentParmsUtility.GetDefaultPawnGroupMakerParms(PawnGroupKindDefOf.Combat, parms, true);
            var pawns = PawnGroupMakerUtility.GeneratePawns(gp).ToList();
            if (!RCellFinder.TryFindRandomPawnEntryCell(out var entry, map, CellFinder.EdgeRoadChance_Hostile)) entry = CellFinder.RandomEdgeCell(map);
            foreach (var p in pawns) GenSpawn.Spawn(p, CellFinder.RandomClosewalkCellNear(entry, map, 8), map);
            if (!hostile && pawns.Count > 0) LordMaker.MakeNewLord(agency, new LordJob_VisitColony(agency, DropCellFinder.TradeDropSpot(map), 3 * 60000), map, pawns);
            return pawns;
        }

        /// <summary>Daily housekeeping: the agency is only hostile while its people are on the map; an idle agency is handed to vanilla for removal.</summary>
        public static void Housekeeping(BankGameComp c)
        {
            var def = SLDefOf.SL_CollectionsAgency; if (def == null) return;
            var f = Find.FactionManager.FirstFactionOfDef(def); if (f == null) return;
            bool anyOnMap = false;
            foreach (var m in Find.Maps) if (m.mapPawns.SpawnedPawnsInFaction(f).Count > 0) { anyOnMap = true; break; }
            if (f.RelationKindWith(Faction.OfPlayer) == FactionRelationKind.Hostile && !anyOnMap)
            {
                f.SetRelationDirect(Faction.OfPlayer, FactionRelationKind.Neutral, false);
                HLog.Info("Bank", "Collection agency is neutral again");
            }
            c.Collectors.RemoveAll(p => p == null || p.Destroyed);
            if (!anyOnMap && c.Collectors.Count == 0 && !TestMode) QueueAgencyRemoval(f);
        }

        /// <summary>Marks the agency as temporary (also for factions of older saves) and asks vanilla to remove it if nothing keeps it alive. Vanilla does the removal at its next faction tick.</summary>
        static void QueueAgencyRemoval(Faction f)
        {
            try { f.temporary = true; Find.FactionManager.Notify_PawnLeftFaction(f); }
            catch (Exception e) { HLog.Once("bank.agencyQueue", LogLevel.Warn, "Bank", "Could not queue the agency for removal: " + e.Message); }
        }

        /// <summary>
        /// Module removal (KONZEPT 3.7): sends the agents away for good, ends their lords, forgets the collectors and removes the hidden faction, so the save
        /// holds no pawn, lord or faction of Silverline any more. Returns what was done (for the report).
        /// </summary>
        public static List<string> CleanUpForRemoval()
        {
            var done = new List<string>();
            var def = SLDefOf.SL_CollectionsAgency;
            var f = def == null ? null : Find.FactionManager.FirstFactionOfDef(def);
            var c = BankGameComp.Current;
            int lords = 0, pawns = 0;
            if (f != null)
            {
                foreach (var m in Find.Maps.ToList())
                {
                    foreach (var lord in m.lordManager.lords.Where(l => l.faction == f).ToList()) { m.lordManager.RemoveLord(lord); lords++; }
                    foreach (var p in m.mapPawns.AllPawns.Where(p => p.Faction == f).ToList()) { DiscardPawn(p); pawns++; }
                }
                foreach (var p in Find.WorldPawns.AllPawnsAliveOrDead.Where(p => p.Faction == f).ToList()) { DiscardPawn(p); pawns++; }
                f.SetRelationDirect(Faction.OfPlayer, FactionRelationKind.Neutral, false);
                QueueAgencyRemoval(f);
            }
            if (c != null)
            {
                foreach (var p in c.Collectors.ToList()) if (p != null && !p.Destroyed && p.Faction != null && p.Faction.def == def) { DiscardPawn(p); pawns++; }
                c.Collectors.Clear();
            }
            if (f != null) done.Add($"Silverline: removed the collection agency ({pawns} agents, {lords} lords); vanilla deletes the faction at its next tick.");
            return done;
        }

        static void DiscardPawn(Pawn p)
        {
            try
            {
                if (Find.WorldPawns.Contains(p)) Find.WorldPawns.RemoveAndDiscardPawnViaGC(p);
                else if (!p.Destroyed) p.Destroy();   // spawned agents: vanilla despawns them, the world pawn bookkeeping is told
            }
            catch (Exception e) { HLog.Warn("Bank", "Could not remove a collection agent: " + e.Message); }
        }
    }
}
