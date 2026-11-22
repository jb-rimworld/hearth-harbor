// SPDX-License-Identifier: MIT
// KONZEPT 11.7: nobles as paying guests and imperial billeting (Royalty). Demands come from the title, payment from DlcMath.
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace Hearth.Dlc
{
    /// <summary>What a noble of a given title demands, read from Vanilla's RoyalTitleDef (bedroom requirements, food, seniority).</summary>
    public sealed class NobleDemand
    {
        public RoyalTitleDef Title;
        public int Seniority;
        public List<string> BedroomLabels = new List<string>();
        public int MinBedroomArea;
        public int MinBedroomImpressiveness;
        public string FoodRequirement;
        public double PricePerNobleDay;
    }

    public static class NobleDemands
    {
        /// <summary>Titles of the Empire that may come as paying guests, with their weight: Knight to Count often, Duke rarely.</summary>
        public static readonly (string title, float weight)[] Offered = { ("Knight", 4f), ("Praetor", 3f), ("Baron", 2.5f), ("Count", 1.5f), ("Duke", 0.3f) };

        public static NobleDemand For(RoyalTitleDef t)
        {
            if (t == null) return null;
            var d = new NobleDemand { Title = t, Seniority = t.seniority };
            if (t.bedroomRequirements != null)
                foreach (var r in t.bedroomRequirements)
                {
                    try { d.BedroomLabels.Add(r.LabelCap()); } catch { d.BedroomLabels.Add(r.GetType().Name); }
                    if (r is RoomRequirement_Area a) d.MinBedroomArea = Math.Max(d.MinBedroomArea, a.area);
                    if (r is RoomRequirement_Impressiveness im) d.MinBedroomImpressiveness = Math.Max(d.MinBedroomImpressiveness, im.impressiveness);
                }
            d.FoodRequirement = t.foodRequirement.Defined ? t.foodRequirement.minQuality.ToString() : null;
            d.PricePerNobleDay = HSettings.Get("dlc.nobleBasePrice") * (1.0 + 0.15 * t.seniority / 100.0);
            return d;
        }

        public static bool RoomMeets(Room room, RoyalTitleDef t, Pawn noble = null)
        {
            if (room == null || t == null) return false;
            if (t.bedroomRequirements == null) return true;
            foreach (var r in t.bedroomRequirements) if (!r.MetOrDisabled(room, noble)) return false;
            return true;
        }

        public static IEnumerable<Building_Bed> GuestBeds(Map map)
        {
            foreach (var b in map.listerBuildings.AllBuildingsColonistOfClass<Building_Bed>())
                if (b.def.building != null && b.def.building.bed_humanlike && !b.Medical && !b.ForPrisoners) yield return b;
        }

        public static bool HasSuitableBedroom(Map map, RoyalTitleDef t)
        {
            foreach (var b in GuestBeds(map)) { var room = b.GetRoom(); if (room != null && !room.PsychologicallyOutdoors && RoomMeets(room, t)) return true; }
            return false;
        }

        /// <summary>"Suitable up to: Baron" for the bed button: highest Empire title whose bedroom requirements this room meets, or null.</summary>
        public static RoyalTitleDef MaxTitleServedBy(Room room)
        {
            if (room == null || room.PsychologicallyOutdoors) return null;
            RoyalTitleDef best = null;
            foreach (var t in DefDatabase<RoyalTitleDef>.AllDefs.OrderBy(x => x.seniority))
                if (t.bedroomRequirements != null && RoomMeets(room, t)) best = t;
            return best;
        }

        public static RoyalTitleDef PickTitle(Map map, bool needRoom, out string why)
        {
            why = null;
            var pool = new List<(RoyalTitleDef t, float w)>();
            foreach (var (name, w) in Offered)
            {
                var t = DefDatabase<RoyalTitleDef>.GetNamedSilentFail(name); if (t == null) continue;
                if (needRoom && !HasSuitableBedroom(map, t)) continue;
                pool.Add((t, w));
            }
            if (pool.Count == 0) { why = needRoom ? "no bedroom that meets any noble's title" : "no title available"; return null; }
            return pool.RandomElementByWeight(x => x.w).t;
        }
    }

    // ---------------------------------------------------------------------------------------------------------------- data
    public sealed class NobleOffer : IExposable
    {
        public int Id, MapId, Nobles = 1, Guards, Days = 3, ExpireTick; public string Title; public bool Forced;
        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id"); Scribe_Values.Look(ref MapId, "map"); Scribe_Values.Look(ref Nobles, "n", 1); Scribe_Values.Look(ref Guards, "g");
            Scribe_Values.Look(ref Days, "days", 3); Scribe_Values.Look(ref ExpireTick, "exp"); Scribe_Values.Look(ref Title, "title"); Scribe_Values.Look(ref Forced, "forced");
        }
    }

    public sealed class NobleStay : IExposable
    {
        public int Id, MapId, ArriveTick, LeaveTick, Seniority, Nobles; public bool Forced, Settled; public string Title; public List<Pawn> Pawns = new List<Pawn>();
        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id"); Scribe_Values.Look(ref MapId, "map"); Scribe_Values.Look(ref ArriveTick, "arr"); Scribe_Values.Look(ref LeaveTick, "leave");
            Scribe_Values.Look(ref Seniority, "sen"); Scribe_Values.Look(ref Nobles, "n"); Scribe_Values.Look(ref Forced, "forced"); Scribe_Values.Look(ref Settled, "settled");
            Scribe_Values.Look(ref Title, "title"); Scribe_Collections.Look(ref Pawns, "pawns", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) { Pawns = Pawns ?? new List<Pawn>(); Pawns.RemoveAll(p => p == null); }
        }
    }

    public sealed class NobleData : IExposable
    {
        public List<NobleOffer> Offers = new List<NobleOffer>();
        public List<NobleStay> Stays = new List<NobleStay>();
        public int NextId = 1;
        public void ExposeData()
        {
            Scribe_Collections.Look(ref Offers, "offers", LookMode.Deep); Scribe_Collections.Look(ref Stays, "stays", LookMode.Deep); Scribe_Values.Look(ref NextId, "next", 1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) { Offers = Offers ?? new List<NobleOffer>(); Stays = Stays ?? new List<NobleStay>(); }
        }
    }

    // ---------------------------------------------------------------------------------------------------------------- logic
    public static class NobleStays
    {
        static NobleData Data => DlcState.Current?.Nobles;

        public static bool Active => DlcDefs.Royalty && Faction.OfEmpire != null && !Faction.OfEmpire.HostileTo(Faction.OfPlayer);
        public static bool Busy => Data != null && (Data.Offers.Count > 0 || Data.Stays.Any(s => !s.Settled));

        public static bool BilletingAllowed => (HearthProfile.Current?.HardTier ?? false) || HSettings.GetBool("dlc.mandatoryBilleting");

        /// <summary>Creates an offer (paid stay or forced billeting) and sends the letter. Returns null with a reason if it cannot be offered.</summary>
        public static NobleOffer MakeOffer(Map map, bool forced, out string why)
        {
            why = null; var d = Data;
            if (d == null || map == null) { why = "no game"; return null; }
            if (!Active) { why = "Royalty missing or the Empire is hostile"; return null; }
            if (Busy) { why = "a noble party is already pending or staying"; return null; }
            if (!forced && !HSettings.GetBool("dlc.nobleStays")) { why = "noble stays are switched off"; return null; }
            if (forced && !BilletingAllowed) { why = "billeting only on hard tiers or with mandatory hospitality"; return null; }
            var title = NobleDemands.PickTitle(map, needRoom: !forced, out why); if (title == null) return null;
            int now = Find.TickManager.TicksGame;
            var o = new NobleOffer
            {
                Id = d.NextId++, MapId = map.uniqueID, Title = title.defName, Forced = forced, Nobles = 1,
                Guards = forced ? Rand.RangeInclusive(1, 3) : Rand.RangeInclusive(0, 2),
                Days = forced ? Rand.RangeInclusive(3, 10) : Rand.RangeInclusive(2, 5),
                ExpireTick = now + 2 * 60000
            };
            d.Offers.Add(o); SendLetter(o, title);
            return o;
        }

        static void SendLetter(NobleOffer o, RoyalTitleDef title)
        {
            var demand = NobleDemands.For(title);
            string label = (o.Forced ? "HH.Dlc.Noble.BilletLabel" : "HH.Dlc.Noble.OfferLabel").TranslateOr(o.Forced ? "Imperial billeting" : "Noble stay offered");
            double pay = DlcMath.NoblePayment(demand.PricePerNobleDay * HSettings.Get("dlc.nobleMoneyFactor"), o.Nobles, o.Days, 0, 0.0) / (1.0 + 0.0);
            string text = o.Forced
                ? "HH.Dlc.Noble.BilletText".TranslateOr("The Empire orders you to lodge a {0} and {1} retainers for {2} days. They expect a bedroom worthy of their rank ({3}). You will receive about {4} silver for expenses. Refusing costs goodwill and honor.").Fmt(title.label, o.Guards, o.Days, string.Join(", ", demand.BedroomLabels), Math.Round(pay * DlcMath.BilletingCompensation(HearthProfile.Current?.Tier ?? 3f)))
                : "HH.Dlc.Noble.OfferText".TranslateOr("A {0} with {1} retainers asks to stay for {2} days and will pay about {3} silver if they are comfortable. They expect: {4}. Declining costs nothing.").Fmt(title.label, o.Guards, o.Days, Math.Round(pay), string.Join(", ", demand.BedroomLabels));
            var def = DefDatabase<LetterDef>.GetNamedSilentFail("HH_NobleOffer"); if (def == null) return;
            var letter = (ChoiceLetter_NobleOffer)LetterMaker.MakeLetter(label, text, def);
            letter.OfferId = o.Id; letter.Forced = o.Forced; letter.StartTimeout(2 * 60000);
            Find.LetterStack.ReceiveLetter(letter);
        }

        public static NobleOffer OfferById(int id) => Data?.Offers.FirstOrDefault(x => x.Id == id);

        public static void Decline(int offerId)
        {
            var o = OfferById(offerId); if (o == null) return; Data.Offers.Remove(o);
            if (o.Forced) ApplyRefusal();
        }

        /// <summary>Refusing an imperial billeting: -25 goodwill with the Empire and -2 honor for every title holder (11.7).</summary>
        public static void ApplyRefusal()
        {
            var e = Faction.OfEmpire; if (e == null) return;
            Faction.OfPlayer.TryAffectGoodwillWith(e, -25, canSendMessage: true, canSendHostilityLetter: false);
            foreach (var p in TitleHolders()) p.royalty.GainFavor(e, -2);
        }

        public static IEnumerable<Pawn> TitleHolders()
        {
            var e = Faction.OfEmpire; if (e == null) yield break;
            foreach (var p in PawnsFinder.AllMaps_FreeColonists) if (p.royalty != null && p.royalty.GetCurrentTitle(e) != null) yield return p;
        }

        public static NobleStay Accept(int offerId)
        {
            var o = OfferById(offerId); if (o == null) return null;
            var map = Find.Maps.FirstOrDefault(m => m.uniqueID == o.MapId); Data.Offers.Remove(o);
            return map == null ? null : StartStay(map, o);
        }

        /// <summary>Generates the noble party, walks it in as a visiting group and records the stay. Public so tests can drive it.</summary>
        public static NobleStay StartStay(Map map, NobleOffer o)
        {
            var e = Faction.OfEmpire; var title = DefDatabase<RoyalTitleDef>.GetNamedSilentFail(o.Title);
            if (e == null || title == null) return null;
            var pawns = new List<Pawn>();
            var kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Empire_Royal_" + o.Title);
            for (int i = 0; i < o.Nobles && kind != null; i++) pawns.Add(PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, e, PawnGenerationContext.NonPlayer, -1, forceGenerateNewPawn: true)));
            var guardKind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Empire_Fighter_Trooper");
            for (int i = 0; i < o.Guards && guardKind != null; i++) pawns.Add(PawnGenerator.GeneratePawn(new PawnGenerationRequest(guardKind, e, PawnGenerationContext.NonPlayer, -1, forceGenerateNewPawn: true)));
            if (pawns.Count == 0) return null;
            if (!CellFinder.TryFindRandomEdgeCellWith(c => c.Standable(map) && map.reachability.CanReachColony(c), map, CellFinder.EdgeRoadChance_Neutral, out IntVec3 edge)) edge = CellFinder.RandomEdgeCell(map);
            foreach (var p in pawns) GenSpawn.Spawn(p, CellFinder.RandomClosewalkCellNear(edge, map, 4), map);
            IntVec3 spot = map.Center;
            if (!RCellFinder.TryFindRandomSpotJustOutsideColony(pawns[0], out spot)) spot = map.Center;
            int now = Find.TickManager.TicksGame;
            LordMaker.MakeNewLord(e, new LordJob_VisitColony(e, spot, o.Days * 60000), map, pawns);
            var stay = new NobleStay { Id = o.Id, MapId = map.uniqueID, ArriveTick = now, LeaveTick = now + o.Days * 60000, Seniority = title.seniority, Nobles = o.Nobles, Forced = o.Forced, Title = o.Title, Pawns = pawns };
            Data.Stays.Add(stay);
            Messages.Message("HH.Dlc.Noble.Arrived".TranslateOr("A {0} and retinue arrive for {1} days.").Fmt(title.label, o.Days), pawns[0], MessageTypeDefOf.NeutralEvent, false);
            return stay;
        }

        public static double AverageSatisfaction(NobleStay s)
        {
            double sum = 0; int n = 0;
            foreach (var p in s.Pawns.Take(s.Nobles))
            {
                var info = GuestApi.Info(p); if (info == null) continue; sum += info.Satisfaction; n++;
            }
            return n == 0 ? 0 : sum / n;
        }

        /// <summary>Money, honor and goodwill when a stay ends (11.7). Paid: price × satisfaction. Forced: expense share by tier; unhappy guests cost goodwill.</summary>
        public static Money Settle(NobleStay s, double? satisfactionOverride = null)
        {
            if (s.Settled) return Money.Zero;
            s.Settled = true;
            var map = Find.Maps.FirstOrDefault(m => m.uniqueID == s.MapId) ?? Find.AnyPlayerHomeMap;
            var title = DefDatabase<RoyalTitleDef>.GetNamedSilentFail(s.Title);
            double sat = satisfactionOverride ?? AverageSatisfaction(s);
            double perDay = HSettings.Get("dlc.nobleBasePrice") * HSettings.Get("dlc.nobleMoneyFactor");
            double days = Math.Max(1, (s.LeaveTick - s.ArriveTick) / 60000.0);
            Money pay;
            if (!s.Forced)
            {
                pay = Money.FromSilver(Math.Round(DlcMath.NoblePayment(perDay, s.Nobles, days, s.Seniority, sat)));
                if (map != null && pay.IsPositive) Payments.CreditColony(map, pay, "Noble stay: " + (title?.label ?? s.Title));
                if (sat >= 0.0) { int honor = DlcMath.NobleHonor(s.Seniority); var h = TitleHolders().OrderByDescending(p => p.royalty.GetCurrentTitle(Faction.OfEmpire).seniority).FirstOrDefault(); h?.royalty.GainFavor(Faction.OfEmpire, honor); }
            }
            else
            {
                double comp = DlcMath.BilletingCompensation(HearthProfile.Current?.Tier ?? 3f);
                pay = Money.FromSilver(Math.Round(DlcMath.NoblePayment(perDay, s.Nobles, days, s.Seniority, 0.0) * comp));
                if (map != null && pay.IsPositive) Payments.CreditColony(map, pay, "Imperial billeting expenses");
                int pen = DlcMath.BilletingPenalty(sat);
                if (pen < 0 && Faction.OfEmpire != null) Faction.OfPlayer.TryAffectGoodwillWith(Faction.OfEmpire, pen, canSendMessage: true, canSendHostilityLetter: false);
            }
            Messages.Message("HH.Dlc.Noble.Left".TranslateOr("The noble party leaves. Payment: {0} silver.").Fmt(pay.ToStringShort()), MessageTypeDefOf.PositiveEvent, false);
            return pay;
        }

        public static void Tick(DlcState st, int now)
        {
            var d = st.Nobles; if (d == null) return;
            for (int i = d.Offers.Count - 1; i >= 0; i--)
                if (now >= d.Offers[i].ExpireTick) { var o = d.Offers[i]; d.Offers.RemoveAt(i); if (o.Forced) ApplyRefusal(); }
            foreach (var s in d.Stays.ToList())
            {
                if (!s.Settled && now >= s.LeaveTick) Settle(s);
                if (s.Settled && now > s.LeaveTick + 60000) d.Stays.Remove(s);
            }
        }
    }

    // ---------------------------------------------------------------------------------------------------------------- letter and incidents
    public class ChoiceLetter_NobleOffer : ChoiceLetter
    {
        public int OfferId; public bool Forced;
        public override bool CanDismissWithRightClick => false;
        public override bool CanShowInLetterStack => base.CanShowInLetterStack && NobleStays.OfferById(OfferId) != null;

        public override IEnumerable<DiaOption> Choices
        {
            get
            {
                yield return new DiaOption(Forced ? "HH.Dlc.Noble.AcceptOrders".TranslateOr("Obey the order") : "AcceptButton".Translate())
                {
                    action = () => { NobleStays.Accept(OfferId); Find.LetterStack.RemoveLetter(this); },
                    resolveTree = true
                };
                yield return new DiaOption(Forced ? "HH.Dlc.Noble.Refuse".TranslateOr("Refuse (goodwill and honor suffer)") : "RejectLetter".Translate())
                {
                    action = () => { NobleStays.Decline(OfferId); Find.LetterStack.RemoveLetter(this); },
                    resolveTree = true
                };
                yield return Option_Postpone;
            }
        }

        public override void ExposeData() { base.ExposeData(); Scribe_Values.Look(ref OfferId, "offerId"); Scribe_Values.Look(ref Forced, "forced"); }
    }

    public class IncidentWorker_NobleStay : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms)
        {
            if (!Modules.On(ModuleId.Inn) || !NobleStays.Active || NobleStays.Busy || !HSettings.GetBool("dlc.nobleStays")) return false;
            var map = parms.target as Map; if (map == null || !map.IsPlayerHome) return false;
            return NobleDemands.PickTitle(map, true, out _) != null;
        }
        protected override bool TryExecuteWorker(IncidentParms parms) => parms.target is Map m && NobleStays.MakeOffer(m, false, out _) != null;
    }

    public class IncidentWorker_ImperialBilleting : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms)
        {
            if (!Modules.On(ModuleId.Inn) || !NobleStays.Active || NobleStays.Busy || !NobleStays.BilletingAllowed) return false;
            var map = parms.target as Map; return map != null && map.IsPlayerHome && Faction.OfEmpire.GoodwillWith(Faction.OfPlayer) >= 0;
        }
        protected override bool TryExecuteWorker(IncidentParms parms) => parms.target is Map m && NobleStays.MakeOffer(m, true, out _) != null;
    }
}
