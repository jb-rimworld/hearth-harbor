// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace Hearth.Underworld
{
    public enum FacilityState { NotBuilt, Waiting, Working }

    /// <summary>One row of the Underworld tab for a facility of package C. All texts are translated, one sentence each.</summary>
    public sealed class FacilityInfo
    {
        public string Id;
        /// <summary>False when the content intensity or the module does not allow this facility in this save: leave the row out.</summary>
        public bool Allowed = true;
        public FacilityState State;
        /// <summary>What it is doing or why it is waiting (the same sentence the building shows), or what to build.</summary>
        public string Line;
        /// <summary>"Last …: 2 days ago", or null when nothing happened yet.</summary>
        public string Last;
        /// <summary>The next expected event ("Next gang visit in about 1.2 days"), or null.</summary>
        public string Next;
        /// <summary>The building to place for this facility (null for gangs, which are not a building; the lounge is a venue sign of type <see cref="UwDefs.LoungeVenue"/>).</summary>
        public ThingDef BuildDef;
    }

    /// <summary>
    /// Cheap status of the back room, gangs, fence, drug lounge, smuggler's dock, hunting ground and organ clinic, for the Underworld tab (package D)
    /// and the guide. Cached per map for 250 ticks (and refreshed when an offer or the gang timer changes). No UI here.
    /// The fight pit is not included (package B).
    /// </summary>
    public static class FacilityStatus
    {
        public const string BackRoomId = "backroom", GangsId = "gangs", FenceId = "fence", LoungeId = "lounge", DockId = "dock", HuntId = "hunt", ClinicId = "clinic";
        public static readonly string[] Ids = { BackRoomId, GangsId, FenceId, LoungeId, DockId, HuntId, ClinicId };

        sealed class Entry { public int tick = int.MinValue; public int version = -1; public ContentIntensity content; public List<FacilityInfo> rows; }
        static readonly Dictionary<int, Entry> cache = new Dictionary<int, Entry>();
        static int version;

        public static void MarkDirty() => version++;

        /// <summary>All rows of one map (allowed or not; check <see cref="FacilityInfo.Allowed"/>). Cached for 250 ticks.</summary>
        public static List<FacilityInfo> All(Map map)
        {
            if (map == null) return new List<FacilityInfo>();
            int now = Find.TickManager.TicksGame;
            if (!cache.TryGetValue(map.uniqueID, out var e)) cache[map.uniqueID] = e = new Entry();
            // a content switch on the settings page rebuilds at once (the game is often paused there, so the tick age would not run out)
            var content = Underworld.Effective;
            if (e.rows == null || e.version != version || e.content != content || now - e.tick >= 250 || now < e.tick)
            {
                e.rows = Guard.Run(ModuleId.Underworld, "FacilityStatus", () => Build(map), new List<FacilityInfo>());
                e.tick = now; e.version = version; e.content = content;
            }
            return e.rows;
        }

        public static FacilityInfo Get(Map map, string id) { foreach (var r in All(map)) if (r.Id == id) return r; return null; }

        /// <summary>New or loaded game.</summary>
        internal static void Clear() { cache.Clear(); version++; }

        // ---- offers (accept / decline for the tab) ------------------------------------------------------------------------------
        /// <summary>Offers waiting for the player's answer (all maps when null).</summary>
        public static List<UwOffer> OpenOffers(Map map = null) => Offers.Open(map);
        public static string OfferLine(UwOffer o) => Offers.Line(o);
        public static bool CanAccept(UwOffer o, out string why) => Offers.CanAccept(o, out why);
        public static bool Accept(UwOffer o, out string why) => Offers.Accept(o, out why);
        public static void Decline(UwOffer o) => Offers.Decline(o);

        /// <summary>Why the last gang visit failed ("no way onto the map"), or null when the last attempt worked.</summary>
        public static string GangRetryReason()
        {
            var c = Underworld.Comp; if (c == null || c.GangRetryKey.NullOrEmpty()) return null;
            return UwPay.T("Gang.Why." + c.GangRetryKey);
        }

        static string Ago(int tick) => Math.Max(0, Find.TickManager.TicksGame - tick).ToStringTicksToPeriod();
        static string In(int tick) => Math.Max(0, tick - Find.TickManager.TicksGame).ToStringTicksToPeriod();
        static string Last(string id, string key) { int t = Underworld.Comp?.LastEvent(id) ?? -1; return t < 0 ? null : UwPay.T(key, Ago(t)); }

        static List<FacilityInfo> Build(Map map)
        {
            var rows = new List<FacilityInfo>();
            var c = Underworld.Comp; if (c == null) return rows;
            int now = Find.TickManager.TicksGame;
            bool open = Underworld.Unlocked;

            // back room
            var br = new FacilityInfo { Id = BackRoomId, BuildDef = UwDefs.BackRoomDoor, Allowed = open, Last = Last(BackRoomId, "BackRoom.Status.Last") };
            if (!BackRoom.HasDoor(map)) { br.State = FacilityState.NotBuilt; br.Line = UwPay.T("BackRoom.Status.NotBuilt"); }
            else if (BackRoom.HasBackRoom(map))
            {
                br.State = FacilityState.Working; int cells = 0; foreach (var r in BackRoom.Rooms(map)) cells += r.CellCount;
                br.Line = UwPay.T("BackRoom.Yes", cells);
            }
            else { br.State = FacilityState.Waiting; br.Line = BackRoom.StatusLine(BackRoom.Doors(map).Count > 0 ? BackRoom.Doors(map)[0] : null); }
            rows.Add(br);

            // gangs
            var g = new FacilityInfo { Id = GangsId, Allowed = open, Last = Last(GangsId, "Gang.Status.Last") };
            int here = GangHere(map);
            if (!Gangs.HasGangFacility(map)) { g.State = FacilityState.NotBuilt; g.Line = UwPay.T("Gang.Status.NotBuilt"); }
            else if (here > 0) { g.State = FacilityState.Working; g.Line = UwPay.T("Gang.Status.Here", here); }
            else
            {
                g.State = FacilityState.Waiting;
                var why = GangRetryReason();
                g.Line = why != null ? UwPay.T("Gang.Status.Retry", why, In(c.NextGangTick)) : UwPay.T("Gang.Status.Waiting");
            }
            if (g.State != FacilityState.NotBuilt && c.NextGangTick >= 0) g.Next = UwPay.T("Gang.Status.Next", In(c.NextGangTick));
            if (Offers.OpenOf(UwOfferKind.Protection, map) != null) g.Line += " " + UwPay.T("Gang.Status.DemandAbove");
            if (g.State != FacilityState.NotBuilt && Underworld.Allows(UwFeature.PrisonInterrogation) && !HSettings.GetBool("underworld.gang.prisonAccess") && map.mapPawns.PrisonersOfColonySpawnedCount > 0)
                g.Line += " " + UwPay.T("Gang.Status.PrisonOff");
            rows.Add(g);

            // fence
            var fs = FenceTrade.FindCounter(map);
            var fe = new FacilityInfo { Id = FenceId, BuildDef = UwDefs.FenceCounter, Allowed = open, Last = Last(FenceId, "Fence.Status.Last") };
            if (fs == null) { fe.State = FacilityState.NotBuilt; fe.Line = UwPay.T("Fence.Status.NotBuilt"); }
            else
            {
                var why = FenceTrade.CannotBuyReason(fs);
                fe.State = why != null || (fs.Stock.Count == 0 && fe.Last == null) ? FacilityState.Waiting : FacilityState.Working;
                fe.Line = why != null ? UwPay.T("Fence.CannotBuy", why) : fe.State == FacilityState.Waiting ? UwPay.T("Fence.Status.Waiting") : FenceTrade.StockLine(fs);
            }
            rows.Add(fe);

            // drug lounge (a venue type, not a building)
            var lo = new FacilityInfo { Id = LoungeId, Allowed = open && UwDefs.LoungeVenue != null && UwDefs.Open(UwDefs.LoungeVenue.GetModExtension<UwGateExtension>()), Last = Last(LoungeId, "Lounge.Status.Last") };
            Venue lv = null; if (UwDefs.LoungeVenue != null) foreach (var v in Venues.OfType(map, UwDefs.LoungeVenue)) { lv = v; break; }
            if (lv == null) { lo.State = FacilityState.NotBuilt; lo.Line = UwPay.T("Lounge.Status.NotBuilt"); }
            else if (!Venues.IsReady(lv)) { lo.State = FacilityState.Waiting; lo.Line = Venues.ReadySummary(lv) ?? UwPay.T("Lounge.Status.NotReady"); }
            else if (!lv.IsOpenNow) { lo.State = FacilityState.Waiting; lo.Line = UwPay.T("Lounge.Status.Closed"); }
            else { lo.State = FacilityState.Working; lo.Line = UwPay.T("Lounge.Status.Open"); }
            rows.Add(lo);

            // smuggler's dock
            var dock = Smuggler.Dock(map);
            var dk = new FacilityInfo { Id = DockId, BuildDef = UwDefs.SmugglerDock, Allowed = open, Last = Last(DockId, "Smuggler.Status.Last") };
            var boat = Offers.OpenOf(UwOfferKind.Smuggler, map);
            if (dock == null) { dk.State = FacilityState.NotBuilt; dk.Line = UwPay.T("Smuggler.Status.NotBuilt"); }
            else if (boat != null) { dk.State = FacilityState.Working; dk.Line = Offers.Line(boat); }
            else { dk.State = FacilityState.Waiting; dk.Line = DockLine(map); }
            rows.Add(dk);

            // hunting ground
            var hg = new FacilityInfo { Id = HuntId, BuildDef = UwDefs.HuntingGround, Allowed = open && Underworld.Allows(UwFeature.HuntAnimals), Last = Last(HuntId, "Hunt.Status.Last") };
            CompHuntingGround ground = null;
            if (UwDefs.HuntingGround != null) foreach (var t in map.listerThings.ThingsOfDef(UwDefs.HuntingGround)) if (t.Faction == Faction.OfPlayer && t is ThingWithComps tw && tw.GetComp<CompHuntingGround>() is CompHuntingGround cg) { ground = cg; if (cg.Valid) break; }
            if (ground == null) { hg.State = FacilityState.NotBuilt; hg.Line = UwDefs.ResHunt != null && !UwDefs.ResHunt.IsFinished ? UwPay.T("Hunt.Status.NotBuilt", Hunts.MinCells) : UwPay.T("Hunt.Status.NotBuiltResearched", Hunts.MinCells, Hunts.MaxCells); }
            else if (Hunts.Running(map)) { hg.State = FacilityState.Working; hg.Line = UwPay.T("Hunt.Running"); }
            else { hg.State = FacilityState.Waiting; hg.Line = ground.ReasonLine() ?? UwPay.T("Hunt.Next", In(c.NextHuntTick)); }
            if (ground != null && ground.Valid && c.NextHuntTick >= 0) hg.Next = UwPay.T("Hunt.Next", In(c.NextHuntTick));
            rows.Add(hg);

            // organ clinic
            var oc = new FacilityInfo { Id = ClinicId, BuildDef = UwDefs.OrganClinic, Allowed = open && Underworld.Allows(UwFeature.OrganClinic), Last = Last(ClinicId, "Organ.Status.Last") };
            bool clinic = UwDefs.OrganClinic != null && map.listerThings.ThingsOfDef(UwDefs.OrganClinic).Count > 0;
            if (!clinic) { oc.State = FacilityState.NotBuilt; oc.Line = UwDefs.ResClinic != null && !UwDefs.ResClinic.IsFinished ? UwPay.T("Organ.Status.NotBuilt") : UwPay.T("Organ.Status.NotBuiltResearched"); }
            else { oc.State = oc.Last != null ? FacilityState.Working : FacilityState.Waiting; oc.Line = UwPay.T("Organ.Status.Waiting"); }
            rows.Add(oc);
            return rows;
        }

        /// <summary>Members of a gang visit on the map (not the Inn's shady guests).</summary>
        internal static int GangHere(Map map)
        {
            int n = 0; var all = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < all.Count; i++) { var p = all[i]; if (p.RaceProps.Humanlike && !p.Dead && !p.IsColonist && p.GetLord() != null && Underworld.Comp.IsGang(p)) n++; }
            return n;
        }

        /// <summary>The dock's status sentence: the waiting boat, or when the next one lands.</summary>
        public static string DockLine(Map map)
        {
            if (!Underworld.Unlocked) return UwPay.T("Facility.Off");
            var o = Offers.OpenOf(UwOfferKind.Smuggler, map); if (o != null) return Offers.Line(o);
            var c = Underworld.Comp; int now = Find.TickManager.TicksGame;
            if (c == null || c.NextSmugglerTick < 0 || c.NextSmugglerTick <= now) return UwPay.T("Smuggler.NextSoon");
            return UwPay.T("Smuggler.Next", In(c.NextSmugglerTick));
        }
    }
}
