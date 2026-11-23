// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Hearth.ShopsFood
{
    /// <summary>
    /// "Setup" tab of a venue sign (F12, 1.1.0): choose the kind of business with one click, then see what this kind needs —
    /// required and recommended furniture with a tick or a cross, how many the room has, a Build button that picks the right
    /// architect tool (research and module gates respected) and a Show button that points at what is already there.
    /// Works like the bed tab of vanilla: select the sign, set the type, build what is listed.
    /// </summary>
    public class ITab_VenueSetup : ITab
    {
        Vector2 scroll;
        float viewH = 600f;
        // per-row lookups (things in the room, build options) refreshed once a second, not every frame
        sealed class RowCache { public float Time; public List<Thing> Things; public List<ThingDef> Defs; public List<string> Why; }
        readonly Dictionary<VenueRequirement, RowCache> rowCache = new Dictionary<VenueRequirement, RowCache>();
        Thing cachedFor;

        RowCache CacheFor(Venue v, VenueRequirement req)
        {
            if (cachedFor != v.Sign) { rowCache.Clear(); cachedFor = v.Sign; }
            float now = Time.realtimeSinceStartup;
            if (rowCache.TryGetValue(req, out var c) && now - c.Time < 1f) return c;
            c = new RowCache { Time = now, Things = VenueSetup.ThingsFor(v, req), Defs = VenueSetup.BuildDefs(req), Why = new List<string>() };
            foreach (var d in c.Defs) { VenueSetup.DesignatorFor(d, out string w); c.Why.Add(w); }
            rowCache[req] = c;
            return c;
        }

        static readonly Color Green = new Color(0.45f, 0.78f, 0.45f);
        static readonly Color Red = new Color(0.92f, 0.42f, 0.38f);
        static readonly Color Amber = new Color(0.95f, 0.74f, 0.35f);
        static readonly Color Grey = new Color(0.62f, 0.62f, 0.62f);

        public ITab_VenueSetup()
        {
            labelKey = "HH.Setup.TabLabel";
            tutorTag = "HHVenueSetup";
            size = new Vector2(500f, 600f);
        }

        protected override void UpdateSize()
        {
            base.UpdateSize();
            size = HUI.Size(500f, 600f, 460f, 480f, 0.45f, 0.8f);
        }

        CompVenueSignHH Comp => SelThing?.TryGetComp<CompVenueSignHH>();

        public override bool IsVisible => Comp != null && SelThing.Faction == Faction.OfPlayer;

        protected override void FillTab()
        {
            var comp = Comp;
            if (comp == null) return;
            var rect = new Rect(0f, 0f, size.x, size.y).ContractedBy(12f);
            Guard.Run(ModuleId.ShopsFood, "Venues.SetupTab", () => Draw(rect, comp));
            Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
        }

        void Draw(Rect outer, CompVenueSignHH comp)
        {
            var v = comp.parent.Spawned ? VenueApi.VenueOfSign(comp.parent.Map, comp.parent) : null;
            // header stays in place, the rest scrolls
            float y = DrawHeader(new Rect(outer.x, outer.y, outer.width, 64f), comp, v);
            var body = new Rect(outer.x, outer.y + y + 6f, outer.width, outer.height - y - 6f);
            var view = new Rect(0f, 0f, body.width - 16f, Mathf.Max(viewH, body.height - 1f));
            Widgets.BeginScrollView(body, ref scroll, view);
            float h = DrawBody(view.width, comp, v);
            Widgets.EndScrollView();
            if (Event.current.type == EventType.Layout || Event.current.type == EventType.Repaint) viewH = h + 8f;
        }

        // ------------------------------------------------------------------ header

        float DrawHeader(Rect r, CompVenueSignHH comp, Venue v)
        {
            var type = comp.type;
            var iconBox = new Rect(r.x, r.y, 60f, 60f);
            var col = type?.color ?? Grey;
            Widgets.DrawBoxSolid(iconBox, new Color(col.r * 0.35f, col.g * 0.35f, col.b * 0.35f, 0.9f));
            Widgets.DrawBox(iconBox, 2);
            if (type?.icon != null) GUI.DrawTexture(iconBox.ContractedBy(6f), type.icon);
            float x = iconBox.xMax + 10f, w = r.xMax - x;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(x, r.y, w, 32f), comp.DisplayName.Truncate(w));
            Text.Font = GameFont.Small;
            string sub = type != null ? type.LabelCap.ToString() : "HH.Venue.ChooseType".TranslateOr("Kind of business");
            var subR = new Rect(x, r.y + 32f, w, 26f);
            GUI.color = Grey; Widgets.Label(subR, sub); GUI.color = Color.white;
            // status chip
            string chip; Color chipCol;
            if (v == null) { chip = "HH.Setup.ChipNoRoom".TranslateOr("no room"); chipCol = Grey; }
            else
            {
                switch (VenueApi.Gate(v))
                {
                    case VenueGate.Open: chip = "HH.Venue.Open".TranslateOr("open"); chipCol = Green; break;
                    case VenueGate.WaitingForStaff: chip = "HH.Setup.ChipNoStaff".TranslateOr("waiting for staff"); chipCol = Amber; break;
                    case VenueGate.NotReady: chip = "HH.Setup.NotReadyState".TranslateOr("not ready"); chipCol = Red; break;
                    default: chip = "HH.Venue.Closed".TranslateOr("closed"); chipCol = Grey; break;
                }
            }
            float subW = Text.CalcSize(sub).x;
            float cw = Text.CalcSize(chip).x + 16f;
            var chipR = new Rect(Mathf.Min(x + subW + 10f, r.xMax - cw), subR.y + 2f, cw, 22f);
            Widgets.DrawBoxSolid(chipR, new Color(chipCol.r, chipCol.g, chipCol.b, 0.25f));
            GUI.color = chipCol; Widgets.DrawBox(chipR); GUI.color = Color.white;
            Text.Anchor = TextAnchor.MiddleCenter; Widgets.Label(chipR, chip); Text.Anchor = TextAnchor.UpperLeft;
            // rename shortcut on the name
            var nameR = new Rect(x, r.y, w, 32f);
            if (Mouse.IsOver(nameR)) Widgets.DrawHighlight(nameR);
            TooltipHandler.TipRegion(nameR, "HH.Setup.RenameTip".TranslateOr("Click to rename this business."));
            if (Widgets.ButtonInvisible(nameR)) Find.WindowStack.Add(new Dialog_RenameVenue(comp));
            return 64f;
        }

        // ------------------------------------------------------------------ body

        float DrawBody(float width, CompVenueSignHH comp, Venue v)
        {
            float y = 0f;
            y = DrawTypePicker(width, y, comp);
            if (comp.type == null) return y;
            if (v == null)
            {
                y += 6f;
                y = Paragraph(width, y, "HH.Venue.NoRoom".TranslateOr("Not a business yet: the sign needs an enclosed, roofed room."), Amber);
                y = Paragraph(width, y, comp.type.description, Grey, GameFont.Tiny);
                return y;
            }
            var rep = VenueSetup.Report(v, 30);
            y = DrawBanner(width, y + 4f, comp, v, rep);
            var req = rep.Required.ToList();
            var rec = rep.Recommended.ToList();
            if (req.Count > 0)
            {
                y = SectionHeader(width, y + 6f, "HH.Setup.Required".TranslateOr("Required"), req.Count(r => r.Ok) + " / " + req.Count);
                foreach (var row in req) y = DrawRow(width, y, comp, v, row);
            }
            if (rec.Count > 0)
            {
                y = SectionHeader(width, y + 8f, "HH.Setup.RecommendedHead".TranslateOr("Recommended"), rec.Count(r => r.Ok) + " / " + rec.Count);
                foreach (var row in rec) y = DrawRow(width, y, comp, v, row);
            }
            if (VenueHonor.IsOn(v) || comp.honorSales > 0)
            {
                y = SectionHeader(width, y + 8f, "HH.Honor.Section".TranslateOr("Honor box"), VenueHonor.IsOn(v) ? "HH.Venue.On".TranslateOr("on") : "HH.Venue.Off".TranslateOr("off"));
                y = Paragraph(width, y, comp.HonorReportLine(), Color.white, GameFont.Tiny);
            }
            if (comp.Tips.IsPositive)
                y = Paragraph(width, y + 2f, "HH.Serve.TipsTotal".TranslateOr("Tips for the staff so far: {0} silver").Replace("{0}", comp.Tips.ToStringShort()), Grey, GameFont.Tiny);
            y += 6f;
            y = Paragraph(width, y, HSettings.GetBool(VenueSetup.RequireKey)
                ? "HH.Setup.FooterStrict".TranslateOr("Required furniture keeps the business closed until it stands in the room. You can turn that rule off in the Shops & Food settings.")
                : "HH.Setup.FooterLoose".TranslateOr("The rule 'Venues need their furniture' is off: missing items are only hints."), Grey, GameFont.Tiny);
            return y;
        }

        float DrawTypePicker(float width, float y, CompVenueSignHH comp)
        {
            Text.Font = GameFont.Tiny; GUI.color = Grey;
            Widgets.Label(new Rect(0f, y, width, 18f), "HH.Setup.PickType".TranslateOr("Kind of business"));
            GUI.color = Color.white; Text.Font = GameFont.Small;
            y += 18f;
            var types = comp.SelectableTypes().ToList();
            const float sz = 40f, gap = 6f;
            int perRow = Mathf.Max(1, (int)((width + gap) / (sz + gap)));
            for (int i = 0; i < types.Count; i++)
            {
                var t = types[i];
                var r = new Rect((i % perRow) * (sz + gap), y + (i / perRow) * (sz + gap), sz, sz);
                bool cur = t == comp.type;
                Widgets.DrawBoxSolid(r, cur ? new Color(t.color.r * 0.45f, t.color.g * 0.45f, t.color.b * 0.45f) : new Color(0.16f, 0.16f, 0.16f));
                if (cur) { GUI.color = t.color; Widgets.DrawBox(r, 2); GUI.color = Color.white; }
                else if (Mouse.IsOver(r)) Widgets.DrawHighlight(r);
                if (t.icon != null) GUI.DrawTexture(r.ContractedBy(5f), t.icon);
                TooltipHandler.TipRegion(r, t.LabelCap + "\n\n" + t.description);
                if (!cur && Widgets.ButtonInvisible(r))
                {
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                    comp.SetType(t);
                }
            }
            int rows = (types.Count + perRow - 1) / perRow;
            return y + rows * (sz + gap);
        }

        float DrawBanner(float width, float y, CompVenueSignHH comp, Venue v, SetupReport rep)
        {
            bool strict = HSettings.GetBool(VenueSetup.RequireKey);
            var blockers = rep.Blockers.ToList();
            string text; Color col;
            if (blockers.Count > 0)
            {
                text = (strict ? "HH.Setup.BannerNotReady".TranslateOr("Not ready: this {0} opens once everything marked Required stands in the room.") : "HH.Setup.BannerMissing".TranslateOr("This {0} still misses some of its furniture."))
                    .Replace("{0}", comp.type.label);
                col = strict ? Red : Amber;
            }
            else if (rep.RequiredMissing > 0) { text = "HH.Setup.BannerAttention".TranslateOr("The furniture is complete. Check the open points below so guests are served."); col = Amber; }
            else { text = "HH.Setup.BannerReady".TranslateOr("Everything this business needs is in place."); col = Green; }
            Text.Font = GameFont.Small;
            float h = Mathf.Max(30f, Text.CalcHeight(text, width - 44f) + 10f);
            var r = new Rect(0f, y, width, h);
            Widgets.DrawBoxSolid(r, new Color(col.r, col.g, col.b, 0.16f));
            GUI.color = col; Widgets.DrawBox(r); GUI.color = Color.white;
            var icon = blockers.Count > 0 ? (strict ? VenueTex.CheckMissing : VenueTex.CheckWarn) : rep.RequiredMissing > 0 ? VenueTex.CheckWarn : VenueTex.CheckOk;
            GUI.DrawTexture(new Rect(r.x + 6f, r.y + (h - 24f) / 2f, 24f, 24f), icon);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(r.x + 38f, r.y, r.width - 42f, h), text);
            Text.Anchor = TextAnchor.UpperLeft;
            return y + h;
        }

        static float SectionHeader(float width, float y, string label, string count)
        {
            Text.Font = GameFont.Small;
            var r = new Rect(0f, y, width, 26f);
            GUI.color = new Color(1f, 0.92f, 0.75f);
            Widgets.Label(new Rect(r.x, r.y + 2f, r.width - 60f, 24f), label);
            GUI.color = Grey; Text.Anchor = TextAnchor.UpperRight;
            Widgets.Label(new Rect(r.xMax - 60f, r.y + 2f, 60f, 24f), count);
            Text.Anchor = TextAnchor.UpperLeft; GUI.color = new Color(1f, 1f, 1f, 0.25f);
            Widgets.DrawLineHorizontal(r.x, r.yMax - 1f, r.width);
            GUI.color = Color.white;
            return y + 28f;
        }

        float DrawRow(float width, float y, CompVenueSignHH comp, Venue v, SetupRow row)
        {
            const float h = 34f;
            var r = new Rect(0f, y, width, h);
            if (Mouse.IsOver(r)) Widgets.DrawHighlight(r);
            // status icon
            Texture2D icon = row.Ok ? VenueTex.CheckOk : row.Req.required ? (row.Blocking ? VenueTex.CheckMissing : VenueTex.CheckWarn) : VenueTex.CheckWarn;
            if (!row.Ok && !row.Req.required) GUI.color = new Color(1f, 1f, 1f, 0.55f);
            GUI.DrawTexture(new Rect(r.x + 2f, r.y + 5f, 24f, 24f), icon);
            GUI.color = Color.white;
            // buttons on the right
            float bx = r.xMax;
            bx = DrawActions(r, bx, comp, v, row);
            // count
            string count = row.CountText;
            float cw = 0f;
            if (!count.NullOrEmpty())
            {
                cw = Mathf.Max(44f, Text.CalcSize(count).x + 6f);
                GUI.color = row.Ok ? Green : row.Req.required ? Red : Grey;
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(new Rect(bx - cw - 4f, r.y, cw, h), count);
                Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
            }
            // label + note
            var lr = new Rect(r.x + 32f, r.y, bx - cw - 8f - (r.x + 32f), h);
            string note = row.Note;
            if (row.Planned > 0) note = (note.NullOrEmpty() ? "" : note + " · ") + "HH.Setup.Planned".TranslateOr("{0} planned").Replace("{0}", row.Planned.ToString());
            if (note.NullOrEmpty())
            {
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(lr, row.Label.Truncate(lr.width));
                Text.Anchor = TextAnchor.UpperLeft;
            }
            else
            {
                Widgets.Label(new Rect(lr.x, lr.y + 1f, lr.width, 20f), row.Label.Truncate(lr.width));
                Text.Font = GameFont.Tiny; GUI.color = Grey;
                Widgets.Label(new Rect(lr.x, lr.y + 18f, lr.width, 16f), note.Truncate(lr.width));
                Text.Font = GameFont.Small; GUI.color = Color.white;
            }
            string tip = row.Label + (count.NullOrEmpty() ? "" : " (" + count + ")");
            if (!row.Req.hint.NullOrEmpty()) tip += "\n\n" + row.Req.hint;
            if (!row.Req.required) tip += "\n\n" + "HH.Setup.RecommendedTip".TranslateOr("Recommended: the business works without it, but guests like it.");
            else if (!row.Req.Blocks) tip += "\n\n" + "HH.Setup.OperationalTip".TranslateOr("Needed for guests to be served; it does not keep the doors shut.");
            TooltipHandler.TipRegion(lr, tip);
            return y + h + 2f;
        }

        /// <summary>Build / Show / shortcut buttons of a row; returns the x where they start.</summary>
        float DrawActions(Rect r, float bx, CompVenueSignHH comp, Venue v, SetupRow row)
        {
            const float bh = 26f;
            float by = r.y + (r.height - bh) / 2f;
            switch (row.Req.check)
            {
                case VenueCheck.Stock:
                {
                    var b = new Rect(bx - 96f, by, 96f, bh);
                    if (Widgets.ButtonText(b, "HH.Venue.Restock".TranslateOr("Restock"))) Find.WindowStack.Add(new Dialog_VenueRestock(comp));
                    TooltipHandler.TipRegion(b, "HH.Setup.RestockTip".TranslateOr("Let the sign set the room's shelves and stockpiles to take your goods."));
                    return b.x - 4f;
                }
                case VenueCheck.ServingWorker:
                {
                    var b = new Rect(bx - 96f, by, 96f, bh);
                    if (Widgets.ButtonText(b, "HH.Setup.WorkTab".TranslateOr("Work tab"))) { var wb = DefDatabase<MainButtonDef>.GetNamedSilentFail("Work"); if (wb != null) Find.MainTabsRoot.SetCurrentTab(wb); };
                    TooltipHandler.TipRegion(b, "HH.Setup.WorkTabTip".TranslateOr("Open the work tab and give a sociable colonist the Serving work type."));
                    return b.x - 4f;
                }
                case VenueCheck.Power:
                    return bx;
            }
            // show button (eye)
            var cache = CacheFor(v, row.Req);
            var things = cache.Things;
            var showR = new Rect(bx - 28f, by, 26f, bh);
            if (things.Count > 0)
            {
                if (Widgets.ButtonImage(showR.ContractedBy(2f), VenueTex.Show))
                {
                    comp.Highlight(things);
                    CameraJumper.TryJump(things[0]);
                    SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                }
                TooltipHandler.TipRegion(showR, "HH.Setup.ShowTip".TranslateOr("Point at what the room already has."));
            }
            bx = showR.x - 4f;
            // build button
            var defs = cache.Defs;
            if (defs.Count == 0) return bx;
            var buildR = new Rect(bx - 96f, by, 96f, bh);
            var first = defs[0];
            string why = cache.Why[0];
            bool any = why == null;
            string label = defs.Count > 1 ? "HH.Setup.Build".TranslateOr("Build") + " ▾" : "HH.Setup.Build".TranslateOr("Build");
            if (!any) GUI.color = new Color(1f, 1f, 1f, 0.45f);
            if (Widgets.ButtonText(buildR, label, active: any || defs.Count > 1))
            {
                if (defs.Count == 1) { if (any) VenueSetup.StartBuilding(first); }
                else Find.WindowStack.Add(new FloatMenu(BuildOptions(defs)));
                rowCache.Clear();
            }
            GUI.color = Color.white;
            TooltipHandler.TipRegion(buildR, defs.Count == 1
                ? first.LabelCap + (any ? "" : " – " + why)
                : string.Join("\n", defs.Select((d, i) => d.LabelCap + (cache.Why[i] == null ? "" : " – " + cache.Why[i]))));
            return buildR.x - 4f;
        }

        static List<FloatMenuOption> BuildOptions(List<ThingDef> defs)
        {
            var opts = new List<FloatMenuOption>();
            foreach (var d in defs)
            {
                var dd = d;
                var des = VenueSetup.DesignatorFor(d, out string why);
                string label = d.LabelCap + (des == null ? " (" + why + ")" : "");
                var opt = new FloatMenuOption(label, des == null ? (Action)null : () => VenueSetup.StartBuilding(dd), d.uiIcon ?? BaseContent.BadTex, d.uiIconColor);
                opts.Add(opt);
            }
            return opts;
        }

        static float Paragraph(float width, float y, string text, Color col, GameFont font = GameFont.Small)
        {
            if (text.NullOrEmpty()) return y;
            Text.Font = font;
            float h = Text.CalcHeight(text, width);
            GUI.color = col; Widgets.Label(new Rect(0f, y, width, h), text); GUI.color = Color.white;
            Text.Font = GameFont.Small;
            return y + h + 2f;
        }
    }
}
