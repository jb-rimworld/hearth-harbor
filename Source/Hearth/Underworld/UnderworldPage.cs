// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Hearth.Shell;

namespace Hearth.Underworld
{
    /// <summary>One row of the Underworld tab: a facility with its state, its one sentence and the small grey lines below it.</summary>
    public sealed class UwPageRow
    {
        public string Id, Name, Line;
        public FacilityState State;
        public readonly List<string> Sub = new List<string>();
        /// <summary>What the "Build" button places (null: no button, e.g. gangs).</summary>
        public ThingDef BuildDef;
        /// <summary>The fight pit of the pit row (null when none is built).</summary>
        public CompFightPit Pit;
    }

    /// <summary>
    /// The "Underworld" page of the Hearth &amp; Harbor main tab (1.2.0, package D): content level, open offers with Accept and Decline, one row per
    /// facility (state dot, sentence, last/next line, Build / Fighters… / jump buttons), whispers and the statistics. Visible whenever the underworld
    /// is visible (module on, content not Cozy). Draw reads only cached data: the rows are rebuilt at most twice a second from
    /// <see cref="FacilityStatus.All"/> (cached 250 ticks) and the pit's cached status, never by scanning pawns or things.
    /// </summary>
    public static class UnderworldPage
    {
        public const string PitId = "pit";
        /// <summary>Order of the facility rows on the page.</summary>
        public static readonly string[] Order = { FacilityStatus.BackRoomId, PitId, FacilityStatus.FenceId, FacilityStatus.LoungeId, FacilityStatus.DockId, FacilityStatus.GangsId, FacilityStatus.HuntId, FacilityStatus.ClinicId };

        static Vector2 scroll; static float viewH = 400f;
        static List<UwPageRow> rows; static List<UwOffer> offers; static float rowsTime = -10f; static int rowsMap = -1;
        const float RefreshSeconds = 0.5f;

        /// <summary>Forget the cached rows (after a button press, a new game, a test).</summary>
        public static void Invalidate() { rows = null; offers = null; }

        static void EnsureRows(Map map, bool refresh)
        {
            float now = Time.realtimeSinceStartup;
            int id = map?.uniqueID ?? -1;
            if (!refresh && rows != null && rowsMap == id && now - rowsTime < RefreshSeconds && now >= rowsTime) return;
            rows = Guard.Run(ModuleId.Underworld, "Page.Rows", () => BuildRows(map), new List<UwPageRow>());
            offers = map == null ? new List<UwOffer>() : Guard.Run(ModuleId.Underworld, "Page.Offers", () => FacilityStatus.OpenOffers(map), new List<UwOffer>());
            rowsTime = now; rowsMap = id;
        }

        /// <summary>The facility rows of a map that the save allows, in <see cref="Order"/>. Cached for half a second (real time).</summary>
        public static List<UwPageRow> Rows(Map map, bool refresh = false) { EnsureRows(map, refresh); return rows; }

        /// <summary>The offers waiting for an answer on a map. Cached with the rows.</summary>
        public static List<UwOffer> Offers(Map map, bool refresh = false) { EnsureRows(map, refresh); return offers; }

        public static UwPageRow Row(Map map, string id, bool refresh = false) { foreach (var r in Rows(map, refresh)) if (r.Id == id) return r; return null; }

        static List<UwPageRow> BuildRows(Map map)
        {
            var list = new List<UwPageRow>();
            if (map == null || !Underworld.Visible) return list;
            var infos = FacilityStatus.All(map);
            foreach (var id in Order)
            {
                if (id == PitId) { var pr = PitRow(map); if (pr != null) list.Add(pr); continue; }
                FacilityInfo fi = null; foreach (var x in infos) if (x.Id == id) { fi = x; break; }
                if (fi == null || !fi.Allowed) continue;
                var r = new UwPageRow { Id = id, Name = UwPay.T("Page.Row." + id), State = fi.State, Line = fi.Line, BuildDef = fi.BuildDef };
                if (id == FacilityStatus.LoungeId) r.BuildDef = VenueSignDef;
                if (!fi.Last.NullOrEmpty()) r.Sub.Add(fi.Last);
                if (!fi.Next.NullOrEmpty()) r.Sub.Add(fi.Next);
                list.Add(r);
            }
            return list;
        }

        static UwPageRow PitRow(Map map)
        {
            CompFightPit pit = null;
            foreach (var p in CompFightPit.All) if (p.Map == map) { pit = p; break; }
            if (!Underworld.Unlocked) return null;
            // a built pit under "peaceful underworld" stays on the page so it can say why it holds no bouts
            if (!Underworld.Allows(UwFeature.PitToIncapacitation) && pit == null) return null;
            var r = new UwPageRow { Id = PitId, Name = UwPay.T("Page.Row." + PitId), BuildDef = UwDefs.FightPit, Pit = pit };
            if (pit == null)
            {
                r.State = FacilityState.NotBuilt; r.Line = UwPay.T("Page.Pit.NotBuilt");
                var lastAny = PitRecords.LastBoutLine(map); if (lastAny != null) r.Sub.Add(lastAny);
                return r;
            }
            r.State = pit.PageState(); r.Line = pit.StatusLine();
            if (Underworld.Allows(UwFeature.PitToIncapacitation)) r.Sub.Add(pit.ReadyLine());
            var last = pit.LastBoutLine(); if (last != null) r.Sub.Add(last);
            return r;
        }

        static ThingDef venueSign; static bool venueSignLooked;
        static ThingDef VenueSignDef { get { if (!venueSignLooked) { venueSign = DefDatabase<ThingDef>.GetNamedSilentFail("HH_VenueSign"); venueSignLooked = true; } return venueSign; } }

        // ---- actions (public: the tests press them like the buttons do) --------------------------------------------------------------------

        /// <summary>The "Build" button: closes the main tab and selects the architect tool of the def. False (with a message) when it cannot be built now.</summary>
        public static bool StartBuilding(ThingDef d)
        {
            var des = UwBuild.DesignatorFor(d, out string why);
            if (des == null) { Messages.Message((d?.LabelCap.ToString() ?? "?") + ": " + why, MessageTypeDefOf.RejectInput, false); return false; }
            CloseMainTab();
            Find.DesignatorManager.Select(des);
            return true;
        }

        /// <summary>The jump button: closes the main tab, moves the camera to the thing and selects it.</summary>
        public static void JumpTo(Thing t)
        {
            if (t == null || !t.Spawned) return;
            CloseMainTab();
            CameraJumper.TryJumpAndSelect(t);
        }

        public static bool AcceptOffer(UwOffer o)
        {
            bool ok = FacilityStatus.Accept(o, out string why);
            if (!ok && !why.NullOrEmpty()) Messages.Message(why, MessageTypeDefOf.RejectInput, false);
            Invalidate();
            return ok;
        }

        public static void DeclineOffer(UwOffer o) { FacilityStatus.Decline(o); Invalidate(); }

        static void CloseMainTab() { if (Find.MainTabsRoot?.OpenTab != null) Find.MainTabsRoot.EscapeCurrentTab(false); }

        public static string AcceptLabel(UwOffer o) => UwPay.T(o.kind == UwOfferKind.Smuggler ? "Smuggler.Buy" : "Gang.Demand.Pay", o.price.ToStringShort());
        public static string DeclineLabel(UwOffer o) => UwPay.T(o.kind == UwOfferKind.Smuggler ? "Smuggler.Decline" : "Gang.Demand.Refuse");

        // ---- drawing ------------------------------------------------------------------------------------------------------------------------
        public static Color DotColor(FacilityState s) => s == FacilityState.Working ? UiStyle.Good : s == FacilityState.Waiting ? UiStyle.Amber : UiStyle.DimGrey;

        public static void Draw(Rect r)
        {
            var c = Underworld.Comp; if (c == null) return;
            var map = Find.CurrentMap;
            var body = PageFrame.Header(r, UwPay.T("Page.Title"), "HH.UI.Page.Sub.underworld".TranslateOr("What each part of the back room is doing, offers that wait for you, whispers and takings."));
            float vw = PageFrame.BeginScroll(body, ref scroll, viewH);
            float y = 2f;

            // content level and where to change it
            string settings = "HH.UI.Tab.Settings".Translate();
            float sbw = Mathf.Clamp(HUI.TextW(settings) + 24f, 90f, 180f);
            string content = UwPay.T("Page.ContentLevel", ("HH.UI.Content." + Underworld.Intensity).Translate().Resolve());
            float ch = Mathf.Max(28f, UiStyle.CachedHeight(content, vw - sbw - 10f, GameFont.Small));
            Widgets.Label(new Rect(0f, y, vw - sbw - 10f, ch), content);
            if (Widgets.ButtonText(new Rect(vw - sbw, y, sbw, 28f), settings)) HearthMod.OpenSettings("modules");
            y += ch + 8f;

            // open offers
            var open = Offers(map);
            if (open.Count > 0)
            {
                y += UiStyle.Section(0f, y, vw, UwPay.T("Page.Offers"));
                foreach (var o in open) y = DrawOffer(o, y, vw);
                y += 6f;
            }

            // facilities
            y += UiStyle.Section(0f, y, vw, UwPay.T("Page.Facilities"));
            var list = Rows(map);
            if (list.Count == 0) { y = Para(UwPay.T("Page.NoMap"), y, vw, UiStyle.Grey); }
            foreach (var row in list) y = DrawRow(row, y, vw);
            y += 6f;

            // whispers
            y += UiStyle.Section(0f, y, vw, UwPay.T("Page.WhispersHead"));
            y = Para(UwPay.T("Page.Whispers", Underworld.Whispers.ToString("0.0")) + " " + UwPay.T("Page.Whispers.Desc"), y, vw, Color.white);
            y = Para(UwPay.T("Page.Weights", Underworld.RespectableArrivalFactor.ToString("0.00"), Underworld.OutlawArrivalFactor.ToString("0.00")), y, vw, UiStyle.Grey);
            if (Underworld.HighRollersActive) y = Para(UwPay.T("Page.HighRollers"), y, vw, UiStyle.Grey);
            y += 6f;

            // statistics
            y += UiStyle.Section(0f, y, vw, UwPay.T("Page.StatsHead"));
            y = Para(UwPay.T("Page.Stats1", c.Bouts, c.Bets, new Money(c.HouseTakeMicro).ToStringShort()), y, vw, Color.white);
            y = Para(UwPay.T("Page.Stats2", c.DealsBought, c.DealsSold, new Money(c.FenceProfitMicro).ToStringShort()), y, vw, Color.white);
            y = Para(UwPay.T("Page.Stats3", c.LoungeSessions, c.GangVisits, c.Smuggles, c.Hunts, c.OrgansSold), y, vw, Color.white);
            viewH = PageFrame.EndScroll(y);
        }

        static float Para(string text, float y, float w, Color col, GameFont font = GameFont.Small)
        {
            float h = UiStyle.CachedHeight(text, w, font);
            UiStyle.LabelColored(new Rect(0f, y, w, h), text, col, TextAnchor.UpperLeft, font);
            return y + h + 2f;
        }

        static float ButtonW(string label) => Mathf.Ceil(HUI.TextW(label) + 22f);

        /// <summary>Buttons of a row, stacked on the right. Returns the column width.</summary>
        static float ColumnW(float vw, params string[] labels)
        {
            float w = 0f; foreach (var l in labels) if (!l.NullOrEmpty()) w = Mathf.Max(w, ButtonW(l));
            return w <= 0f ? 0f : Mathf.Clamp(w, 96f, Mathf.Max(96f, vw * 0.38f));
        }

        static float DrawOffer(UwOffer o, float y, float vw)
        {
            string acc = AcceptLabel(o), dec = DeclineLabel(o);
            float bw = ColumnW(vw, acc, dec);
            float tw = vw - bw - 22f;
            string line = FacilityStatus.OfferLine(o);
            float th = UiStyle.CachedHeight(line, tw, GameFont.Small);
            float h = Mathf.Max(th, 2f * 28f + 4f) + 6f;
            var row = new Rect(0f, y, vw, h);
            if (Mouse.IsOver(row)) Widgets.DrawHighlight(row);
            Widgets.DrawBoxSolid(new Rect(4f, y + 7f, 9f, 9f), UiStyle.Amber);
            Widgets.Label(new Rect(20f, y + 2f, tw, th), line);
            bool can = FacilityStatus.CanAccept(o, out string why);
            var ar = new Rect(vw - bw, y + 2f, bw, 28f);
            if (can) { if (Widgets.ButtonText(ar, acc)) AcceptOffer(o); }
            else { Widgets.ButtonText(ar, acc, true, true, false); TooltipHandler.TipRegion(ar, why); }
            if (Widgets.ButtonText(new Rect(vw - bw, y + 34f, bw, 28f), dec)) DeclineOffer(o);
            return y + h + 2f;
        }

        static float DrawRow(UwPageRow row, float y, float vw)
        {
            // buttons: Build when not built; for a built pit "Fighters…" and a jump
            string build = row.State == FacilityState.NotBuilt && row.BuildDef != null ? UwPay.T("Page.Build") : null;
            string fighters = row.Pit != null ? UwPay.T("Pit.FightersBtn") : null;
            string jump = row.Pit != null ? UwPay.T("Page.Show") : null;
            float bw = ColumnW(vw, build, fighters, jump);
            int nb = (build != null ? 1 : 0) + (fighters != null ? 1 : 0) + (jump != null ? 1 : 0);
            float tx = 20f, tw = vw - tx - (bw > 0f ? bw + 10f : 0f);
            float nameH = UiStyle.CachedHeight(row.Name, tw, GameFont.Small);
            float lineH = UiStyle.CachedHeight(row.Line ?? "", tw, GameFont.Small);
            float subH = 0f; foreach (var s in row.Sub) subH += UiStyle.CachedHeight(s, tw, GameFont.Tiny);
            float textH = nameH + lineH + subH;
            float h = Mathf.Max(textH, nb * 32f) + 8f;
            var rr = new Rect(0f, y, vw, h);
            if (Mouse.IsOver(rr)) Widgets.DrawHighlight(rr);
            var dot = new Rect(4f, y + 7f, 9f, 9f);
            Widgets.DrawBoxSolid(dot, DotColor(row.State));
            TooltipHandler.TipRegion(new Rect(0f, y, 18f, 22f), UwPay.T("Page.State." + row.State));
            float ty = y + 2f;
            UiStyle.LabelColored(new Rect(tx, ty, tw, nameH), row.Name, row.State == FacilityState.NotBuilt ? UiStyle.Grey : UiStyle.Amber); ty += nameH;
            UiStyle.LabelColored(new Rect(tx, ty, tw, lineH), row.Line ?? "", row.State == FacilityState.NotBuilt ? UiStyle.Grey : Color.white); ty += lineH;
            foreach (var s in row.Sub) { float sh = UiStyle.CachedHeight(s, tw, GameFont.Tiny); UiStyle.LabelColored(new Rect(tx, ty, tw, sh), s, UiStyle.DimGrey, TextAnchor.UpperLeft, GameFont.Tiny); ty += sh; }
            float by = y + 2f;
            if (build != null)
            {
                var b = new Rect(vw - bw, by, bw, 28f);
                var des = UwBuild.DesignatorFor(row.BuildDef, out string why);
                if (des != null) { if (Widgets.ButtonText(b, build)) StartBuilding(row.BuildDef); TooltipHandler.TipRegion(b, UwPay.T("Page.Build.Desc", row.BuildDef.label)); }
                else { Widgets.ButtonText(b, build, true, true, false); TooltipHandler.TipRegion(b, why); }
                by += 32f;
            }
            if (fighters != null)
            {
                var b = new Rect(vw - bw, by, bw, 28f);
                if (Widgets.ButtonText(b, fighters)) Window_PitFighters.Open(row.Pit);
                TooltipHandler.TipRegion(b, UwPay.T("Pit.FightersBtn.Desc"));
                by += 32f;
            }
            if (jump != null)
            {
                var b = new Rect(vw - bw, by, bw, 28f);
                if (Widgets.ButtonText(b, jump)) JumpTo(row.Pit.parent);
                by += 32f;
            }
            Widgets.DrawBoxSolid(new Rect(0f, y + h + 0.5f, vw, 1f), UiStyle.Line);
            return y + h + 2f;
        }
    }

    /// <summary>
    /// The architect lookup of the Underworld module (its own small copy, so the module does not call Shops &amp; Food code): the build designator of a
    /// def, or the reason why it cannot be built now. The lookup scans every designator, so answers live for a second (real time).
    /// </summary>
    public static class UwBuild
    {
        struct Answer { public float Time; public Designator_Build Des; public string Why; }
        static readonly Dictionary<ThingDef, Answer> cache = new Dictionary<ThingDef, Answer>();

        public static Designator_Build DesignatorFor(ThingDef d, out string whyNot)
        {
            float now = Time.realtimeSinceStartup;
            if (d != null && cache.TryGetValue(d, out var a) && now - a.Time < 1f && now >= a.Time) { whyNot = a.Why; return a.Des; }
            var des = Lookup(d, out whyNot);
            if (d != null) cache[d] = new Answer { Time = now, Des = des, Why = whyNot };
            return des;
        }

        internal static void Clear() => cache.Clear();

        static Designator_Build Lookup(ThingDef d, out string whyNot)
        {
            whyNot = null;
            if (d == null) { whyNot = UwPay.T("Page.Build.Missing"); return null; }
            Designator_Build des = null;
            try { des = BuildCopyCommandUtility.FindAllowedDesignator(d, mustBeVisible: false); }
            catch (Exception e) { HLog.Once("underworld.page.designator." + d.defName, LogLevel.Warn, "Underworld", "No designator for " + d.defName + ": " + e.Message); }
            if (des == null) { whyNot = UwPay.T("Page.Build.Missing"); return null; }
            if (!d.IsResearchFinished)
            {
                ResearchProjectDef missing = null;
                if (d.researchPrerequisites != null) foreach (var p in d.researchPrerequisites) if (!p.IsFinished) { missing = p; break; }
                whyNot = UwPay.T("Page.Build.Research", missing?.LabelCap.ToString() ?? "?");
                return null;
            }
            if (!des.Visible) { whyNot = UwPay.T("Page.Build.Hidden"); return null; }
            return des;
        }
    }
}

namespace Hearth.Underworld
{
    /// <summary>Checks of the guide topic HH_Guide_BackRoom (Common/Defs/UI/GuideTopics.xml). Evaluated every 60 ticks by the guide; all cheap
    /// (cached arena, cached back rooms, counters).</summary>
    public static class UwGuide
    {
        public const string Visible = "underworldVisible", PitArena = "uwPitArena", BoutSettled = "uwBoutSettled", BackRoomBuilt = "uwBackRoom", GangVisit = "uwGangVisit", FenceBuilt = "uwFence";

        public static void Register()
        {
            Hearth.Shell.GuideChecks.Register(Visible, () => Current.ProgramState == ProgramState.Playing && Underworld.Visible);
            Hearth.Shell.GuideChecks.Register(PitArena, () => AnyHome(m => { foreach (var p in CompFightPit.All) if (p.Map == m && p.ArenaValid) return true; return false; }));
            Hearth.Shell.GuideChecks.Register(BoutSettled, () => AnyHome(PitRecords.EverSettled));
            Hearth.Shell.GuideChecks.Register(BackRoomBuilt, () => AnyHome(BackRoom.HasBackRoom));
            Hearth.Shell.GuideChecks.Register(GangVisit, () => (Underworld.Comp?.GangVisits ?? 0) > 0);
            Hearth.Shell.GuideChecks.Register(FenceBuilt, () => AnyHome(m => FenceTrade.FindCounter(m) != null));
        }

        static bool AnyHome(Func<Map, bool> f)
        {
            if (Current.ProgramState != ProgramState.Playing || Find.Maps == null) return false;
            foreach (var m in Find.Maps) if (m.IsPlayerHome && f(m)) return true;
            return false;
        }
    }
}
