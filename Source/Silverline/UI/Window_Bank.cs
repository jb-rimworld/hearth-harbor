// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Hearth;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Silverline
{
    /// <summary>
    /// The bank lobby (KONZEPT 8.4): header with the bank, a tab strip filled from BankTabs, the page, and Clerk Ottoline's remark.
    /// 1.1.0 (F35): bigger by default (HUI.Size), resizeable, the tab strip never overflows (a "More" menu takes the last tabs), nothing is clipped,
    /// and the lobby makes a sound when it opens, when a tab is chosen and when money moves.
    /// </summary>
    public class Window_Bank : Window
    {
        public static Window_Bank Active;
        public readonly Map Map;
        public readonly BankSite Site;
        public readonly Building At;
        public string TabId = "lobby";
        public double ShownBalance = double.NaN;
        string openPick;
        /// <summary>Height of the page area in the last frame (pages read it to decide whether they need a scroll view).</summary>
        public float BodyHeight { get; private set; }

        public Window_Bank(Map map, BankSite site, Building at)
        {
            Map = map ?? BankGameComp.DefaultMap; Site = site; At = at;
            doCloseX = true; draggable = true; closeOnClickedOutside = false; absorbInputAroundWindow = false;
            forcePause = false; preventCameraMotion = false; closeOnAccept = false; onlyOneOfTypeAllowed = true;
            resizeable = true;
            Active = this;
        }

        public override Vector2 InitialSize => HUI.Size(1100f, 760f, 900f, 620f);
        protected override float Margin => 0f;
        public override void PreOpen() { base.PreOpen(); TrySetMinSize(); }
        public override void PostOpen() { base.PostOpen(); BankSound.Play(BankSound.Open); }
        public override void PostClose() { if (Active == this) Active = null; base.PostClose(); }

        void TrySetMinSize() => BankUI.SetMinSize(this, 860f, 560f);

        public override void DoWindowContents(Rect inRect)
        {
            Active = this;
            var f = Text.Font; var a = Text.Anchor; var wrap = Text.WordWrap;
            Guard.Run(ModuleId.Bank, "Window_Bank", () => Draw(inRect));
            Text.Font = f; Text.Anchor = a; Text.WordWrap = wrap; GUI.color = Color.white;
        }

        void Draw(Rect inRect)
        {
            var c = BankGameComp.Current;
            Widgets.DrawBoxSolid(inRect, BankUI.Walnut);
            GUI.color = BankUI.Brass; Widgets.DrawBox(inRect.ContractedBy(4f), 2); GUI.color = Color.white;
            if (c == null) return;
            var r = inRect.ContractedBy(14f);
            DrawHeader(new Rect(r.x, r.y, r.width, 74f), c);
            if (!c.HasAccount) { DrawOpenAccount(new Rect(r.x, r.y + 82f, r.width, r.height - 82f), c); return; }
            DrawTabs(new Rect(r.x, r.y + 80f, r.width, 34f));
            float clerkH = r.height < 560f ? 44f : 58f;
            var body = new Rect(r.x, r.y + 120f, r.width, r.height - 120f - clerkH - 8f);
            BodyHeight = body.height;
            var tab = BankTabs.VisibleTabs.FirstOrDefault(t => t.Id == TabId) ?? BankTabs.VisibleTabs.FirstOrDefault();
            if (tab != null) { var t = tab; Guard.Run(ModuleId.Bank, "BankTab.Draw." + t.Id, () => t.Draw?.Invoke(body)); }
            DrawClerk(new Rect(r.x, r.yMax - clerkH, r.width, clerkH), c);
        }

        // ---- header ---------------------------------------------------------------------------------
        void DrawHeader(Rect r, BankGameComp c)
        {
            var bank = c.HasAccount ? c.Bank : (SL_BankDef.AllSorted.FirstOrDefault(b => b.defName == openPick) ?? SL_BankDef.Default);
            if (bank != null) GUI.DrawTexture(new Rect(r.x, r.y + 4f, 64f, 64f), bank.Icon);
            string greeting = Greeting();
            string tierLine = null;
            if (c.HasAccount)
            {
                string tier = L.T("Tier." + c.ShownTier, FinanceMath.TierNamesEn[Math.Max(0, Math.Min(FinanceMath.TierNamesEn.Length - 1, c.ShownTier))]);
                tierLine = L.T("Header.Tier", "Wealth tier: {0}", tier.ToUpperInvariant());
            }
            // right block: as wide as its lines need (at least 240), the title takes the rest
            var f = Text.Font; Text.Font = GameFont.Small;
            float rightW = Mathf.Clamp(Math.Max(Text.CalcSize(greeting).x, tierLine != null ? Text.CalcSize(tierLine).x : 0f) + 16f, 240f, Math.Max(240f, r.width * 0.4f));
            Text.Font = f;
            var right = new Rect(r.xMax - rightW, r.y + 6f, rightW, 60f);
            float titleW = Math.Max(120f, r.width - 76f - rightW - 12f);
            string title = c.HasAccount ? bank?.Name : L.T("Open.Title", "Silverline: open an account");
            BankUI.LabelCut(new Rect(r.x + 76f, r.y + 4f, titleW, 32f), title ?? L.T("Window.Title", "Silverline"), BankUI.Brass, GameFont.Medium);
            string sub = c.HasAccount ? bank?.Slogan : L.T("Open.Sub", "Your silver, our problem.");
            BankUI.LabelCut(new Rect(r.x + 76f, r.y + 38f, titleW, 30f), sub ?? "", BankUI.CreamDim);
            string site = Site == BankSite.Atm ? L.T("Site.Atm", "ATM") : Site == BankSite.Branch ? L.T("Site.Counter", "Bank counter") : null;
            if (site != null) BankUI.Label(new Rect(right.x, right.y - 4f, right.width, 16f), site.ToUpperInvariant(), BankUI.BrassDim, GameFont.Tiny, TextAnchor.UpperRight, false);
            BankUI.LabelCut(new Rect(right.x, right.y + 10f, right.width, 24f), greeting, BankUI.Cream, GameFont.Small, TextAnchor.UpperRight);
            if (tierLine != null)
            {
                BankUI.LabelCut(new Rect(right.x, right.y + 34f, right.width, 24f), tierLine, BankUI.Brass, GameFont.Small, TextAnchor.UpperRight);
                TooltipHandler.TipRegion(right, L.T("Header.TierTip", "Net worth = account + deposits in transit + silver at home + investments (at market value) − debts: {0} silver. The tier only changes after a new value has held for a whole day. It sets the colony mood from this bank (setting: bank.moodOn).", c.NetWorth.ToStringShort()));
            }
            Widgets.DrawLineHorizontal(r.x, r.yMax, r.width, BankUI.BrassDim);
        }

        string Greeting()
        {
            int h = Map != null ? GenLocalDate.HourOfDay(Map) : 12;
            string who = Faction.OfPlayer?.Name ?? "";
            string part = h < 5 ? L.T("Greet.Night", "Good night") : h < 12 ? L.T("Greet.Morning", "Good morning") : h < 18 ? L.T("Greet.Afternoon", "Good afternoon") : L.T("Greet.Evening", "Good evening");
            return who.NullOrEmpty() ? part + "." : part + ", " + who + ".";
        }

        // ---- tabs -----------------------------------------------------------------------------------
        void DrawTabs(Rect r)
        {
            var tabs = BankTabs.VisibleTabs.ToList();
            var f = Text.Font; Text.Font = GameFont.Small;
            const float gap = 4f, moreW = 96f;
            float pad = 34f, total = 0f;
            Func<float, float> sum = p => { float s = 0f; foreach (var t in tabs) s += Text.CalcSize(t.Label).x + p + gap; return s; };
            if (sum(pad) > r.width) pad = 22f;
            total = sum(pad);
            var shown = new List<BankTab>(tabs); var more = new List<BankTab>();
            if (total > r.width)
            {
                // too many (or too long, translated) tabs: keep as many as fit next to the "More" button; the chosen tab always stays visible
                float room = r.width - moreW - gap, used = 0f; shown.Clear();
                foreach (var t in tabs)
                {
                    float w = Text.CalcSize(t.Label).x + pad + gap;
                    if (used + w <= room || t.Id == TabId) { shown.Add(t); used += w; } else more.Add(t);
                }
                while (used > room && shown.Count > 1)   // the chosen tab pushed us over: move the last other tab out
                {
                    var last = shown.LastOrDefault(t => t.Id != TabId); if (last == null) break;
                    shown.Remove(last); more.Insert(0, last); used -= Text.CalcSize(last.Label).x + pad + gap;
                }
            }
            float x = r.x;
            foreach (var t in shown)
            {
                float w = Text.CalcSize(t.Label).x + pad;
                var tr = new Rect(x, r.y, w, r.height);
                if (BankUI.Chip(tr, t.Label, t.Id == TabId)) TabId = t.Id;
                x += w + gap;
            }
            if (more.Count > 0)
            {
                var mr = new Rect(Math.Min(x, r.xMax - moreW), r.y, moreW, r.height);
                if (BankUI.Chip(mr, L.T("Tabs.More", "More") + " ▾", false, L.T("Tabs.MoreTip", "More pages of the bank: {0}", string.Join(", ", more.Select(t => t.Label)))))
                {
                    var opts = more.Select(t => { var tt = t; return new FloatMenuOption(tt.Label, () => TabId = tt.Id); }).ToList();
                    Find.WindowStack.Add(new FloatMenu(opts));
                }
            }
            Text.Font = f;
            Widgets.DrawLineHorizontal(r.x, r.yMax, r.width, BankUI.BrassDim);
        }

        // ---- Clerk Ottoline ---------------------------------------------------------------------------
        void DrawClerk(Rect r, BankGameComp c)
        {
            Widgets.DrawBoxSolid(r, BankUI.Green);
            GUI.color = BankUI.BrassDim; Widgets.DrawBox(r, 1); GUI.color = Color.white;
            float icon = Math.Min(48f, r.height - 8f);
            var tex = BankUI.Ottoline;
            if (tex != null) GUI.DrawTexture(new Rect(r.x + 6f, r.y + (r.height - icon) / 2f, icon, icon), tex);
            string line = L.T("Clerk.Line", "Clerk Ottoline: \"{0}\"", Quip(c));
            var tr = new Rect(r.x + icon + 16f, r.y + 3f, r.width - icon - 28f, r.height - 6f);
            var font = Text.Font; Text.Font = GameFont.Small;
            bool smallFont = Text.CalcHeight(line, tr.width) > tr.height;
            Text.Font = font;
            BankUI.FitLabel2(tr, line, BankUI.Cream, smallFont ? GameFont.Tiny : GameFont.Small, TextAnchor.MiddleLeft);
        }

        public static string Quip(BankGameComp c)
        {
            if (c.Loans.Any(l => l.InArrears)) return L.T("Quip.Overdue", "We do not wish to alarm you. We have simply noticed that you have not noticed.");
            if (c.Collectors.Count > 0) return L.T("Quip.Collectors", "My colleagues in the hats are not here for you. Do not look at them. They know.");
            double bal = c.Balance.Silver;
            var pool = new List<string>();
            if (bal < 1) pool.Add(L.T("Quip.Empty", "An empty account is just a very honest account."));
            if (bal >= 1000) pool.Add(L.T("Quip.Rich", "Four digits. Your hat is practically a top hat."));
            if (c.ShownTier >= 7) pool.Add(L.T("Quip.Huge", "At this size we are no longer your bank. We are your climate."));
            if (c.Debt.Micro > 0) pool.Add(L.T("Quip.Debt", "Debt is just savings from the future. The future is unusually keen to have them back."));
            if (c.InterestThisQuadrum.Micro > 0) pool.Add(L.T("Quip.Interest", "Interest this quadrum: {0}. That is, if I count correctly, some meals. Congratulations?", c.InterestThisQuadrum.ToStringShort()));
            if (c.InvestmentsValue.Micro > 0) pool.Add(L.T("Quip.Invest", "I see you have investments. I will pretend I am not jealous of a number."));
            pool.Add(L.T("Quip.A", "Your silver is safe*. *Terms apply."));
            pool.Add(L.T("Quip.B", "Please take a number. We have not had a number since the Third Collapse, but the gesture matters."));
            pool.Add(L.T("Quip.C", "Every coin here is accounted for. Mostly by me. At night."));
            pool.Add(L.T("Quip.D", "Mattresses are not a financial product. Though I hear they have a following."));
            pool.Add(L.T("Quip.E", "If anyone asks, the pen is not for sale."));
            return pool[Math.Abs(c.Today * 31 + (int)bal / 50) % pool.Count];
        }

        // ---- open an account --------------------------------------------------------------------------
        void DrawOpenAccount(Rect r, BankGameComp c)
        {
            var banks = SL_BankDef.AllSorted.Where(b => b.Available).ToList();
            if (openPick == null || banks.All(b => b.defName != openPick)) openPick = SL_BankDef.Default?.defName;
            string intro = L.T("Open.Intro", "Pick a bank. Opening an account is free; moving it later costs {0} silver. Until you have an account, nothing here bothers you, no letters, no mood, no fees.", c.SwitchFee.ToStringShort());
            Text.Font = GameFont.Small; float introH = Math.Max(26f, Text.CalcHeight(intro, r.width) + 4f);
            BankUI.Label(new Rect(r.x, r.y, r.width, introH), intro, BankUI.CreamDim);
            float y = r.y + introH + 6f;
            var list = new Rect(r.x, y, r.width, r.yMax - y - 78f);
            openScroll = BankCards.DrawList(list, openScroll, banks, b => b.defName == openPick, b => b.defName == openPick, (b, row) => { openPick = b.defName; BankSound.Play(BankSound.Click); }, null);
            var foot = new Rect(r.x, r.yMax - 70f, r.width, 66f);
            var chk = new Rect(foot.x, foot.y, foot.width - 250f, 26f);
            bool directNow = HSettings.GetBool("bank.directDeposit");
            BankUI.LabelCut(chk, L.T("Open.DirectState", "Till money goes straight to the bank at midnight: {0}", directNow ? L.T("Lobby.On", "on") : L.T("Lobby.Off", "off")), BankUI.CreamDim, GameFont.Small);
            TooltipHandler.TipRegion(chk, L.T("Open.DirectTip", "Takings of your shops, inn and casino go into the account instead of lying around as silver. This is a term of the bank: change it in the mod settings (Bank > Account, bank.directDeposit)."));
            if (Widgets.ButtonText(new Rect(foot.x, foot.y + 30f, 160f, 24f), L.T("Terms.OpenSettings", "Open the mod settings"))) { BankSound.Play(BankSound.Click); HearthMod.OpenSettings("mod:Bank"); }
            if (BankUI.Button(new Rect(foot.xMax - 230f, foot.y + 4f, 230f, 40f), L.T("Open.Button", "Open account")))
            {
                var def = banks.FirstOrDefault(b => b.defName == openPick);
                if (def != null && c.OpenAccount(def, directNow)) { BankSound.Play(BankSound.Stamp); }
            }
        }
        Vector2 openScroll;
    }

    /// <summary>Bank cards of the "open account" page and the "change banks" dialog: icon, name, slogan, every perk, and the numbers that matter. The height follows the content.</summary>
    static class BankCards
    {
        public const float TextX = 80f;

        public static string NumbersLine(SL_BankDef b)
        {
            double f = BankSettings.FeeFactor;
            double rate = (BankSettings.AccountMultiple(b.interestMod) - 1.0) * 100.0;
            return L.T("Card.Numbers", "Interest {0} a year  ·  deposit fee {1} %  ·  withdrawal {2} %  ·  account fee {3} per quadrum",
                YieldCompare.Pct(rate), (b.depositFee * f * 100).ToString("0.##", CultureInfo.InvariantCulture), (b.withdrawFee * f * 100).ToString("0.##", CultureInfo.InvariantCulture), (b.accountFeePerQuadrum * f).ToString("0.##", CultureInfo.InvariantCulture));
        }

        public static float RowHeight(SL_BankDef b, float textWidth)
        {
            var f = Text.Font;
            Text.Font = GameFont.Small; float slogan = Text.CalcHeight("\"" + b.Slogan + "\"", textWidth);
            Text.Font = GameFont.Tiny;
            float perks = 0f; foreach (var p in b.perks) perks += Text.CalcHeight("• " + p, textWidth);
            float nums = Text.CalcHeight(NumbersLine(b), textWidth);
            Text.Font = f;
            return Math.Max(96f, 6f + 30f + slogan + perks + nums + 12f);
        }

        /// <summary>Draws one card. Returns the rect left for a button on the right (the text area ends before it).</summary>
        public static void DrawCard(Rect row, SL_BankDef b, bool selected, float rightReserve, bool current)
        {
            Widgets.DrawBoxSolid(row, selected ? BankUI.GreenLight : (current ? BankUI.Green : BankUI.WalnutLight));
            GUI.color = selected ? BankUI.Brass : BankUI.BrassDim; Widgets.DrawBox(row, selected ? 2 : 1); GUI.color = Color.white;
            GUI.DrawTexture(new Rect(row.x + 8f, row.y + 10f, 64f, 64f), b.Icon);
            float tw = row.width - TextX - rightReserve - 8f; float y = row.y + 4f;
            BankUI.LabelCut(new Rect(row.x + TextX, y, tw, 30f), b.Name, BankUI.Brass, GameFont.Medium); y += 30f;
            string slogan = "\"" + b.Slogan + "\""; Text.Font = GameFont.Small; float sh = Text.CalcHeight(slogan, tw);
            BankUI.Label(new Rect(row.x + TextX, y, tw, sh), slogan, BankUI.Cream); y += sh;
            foreach (var p in b.perks)
            {
                Text.Font = GameFont.Tiny; float ph = Text.CalcHeight("• " + p, tw);
                BankUI.Label(new Rect(row.x + TextX, y, tw, ph), "• " + p, BankUI.CreamDim, GameFont.Tiny); y += ph;
            }
            string nums = NumbersLine(b); Text.Font = GameFont.Tiny; float nh = Text.CalcHeight(nums, tw);
            BankUI.Label(new Rect(row.x + TextX, y + 2f, tw, nh), nums, BankUI.Brass, GameFont.Tiny);
            Text.Font = GameFont.Small;
            TooltipHandler.TipRegion(row, b.description + "\n\n" + string.Join("\n", b.perks.Select(p => "• " + p)) + "\n\n" + L.T("Card.InterestTip", "The account interest is a share ({0}) of the market return, times this bank's own factor ({1}). Investments pay more.", BankSettings.AccountShare.ToString("0.##", CultureInfo.InvariantCulture), b.interestMod.ToString("0.##", CultureInfo.InvariantCulture)));
        }

        /// <summary>A scrolling list of cards. <paramref name="onPick"/> is called when a card is clicked; <paramref name="drawRight"/> may draw a control into the right strip (it gets the card and the strip rect).</summary>
        public static Vector2 DrawList(Rect list, Vector2 scroll, List<SL_BankDef> banks, Func<SL_BankDef, bool> selected, Func<SL_BankDef, bool> current, Action<SL_BankDef, Rect> onPick, Action<SL_BankDef, Rect> drawRight, float rightReserve = 12f)
        {
            float tw = list.width - 16f - TextX - rightReserve - 8f;
            var heights = banks.Select(b => RowHeight(b, tw)).ToList();
            var view = new Rect(0, 0, list.width - 16f, heights.Sum() + banks.Count * 6f);
            Widgets.BeginScrollView(list, ref scroll, view);
            float y = 0f;
            for (int i = 0; i < banks.Count; i++)
            {
                var b = banks[i]; var row = new Rect(0, y, view.width, heights[i]);
                DrawCard(row, b, selected(b), rightReserve, current(b));
                if (onPick != null && Widgets.ButtonInvisible(row)) onPick(b, row);
                if (drawRight != null) drawRight(b, new Rect(row.xMax - rightReserve - 4f, row.y, rightReserve, row.height));
                y += heights[i] + 6f;
            }
            Widgets.EndScrollView();
            return scroll;
        }
    }

    /// <summary>Dialog to move the account to another bank.</summary>
    public class Window_ChooseBank : Window
    {
        Vector2 scroll;
        public Window_ChooseBank() { doCloseX = true; draggable = true; absorbInputAroundWindow = false; closeOnClickedOutside = true; forcePause = false; resizeable = true; }
        public override Vector2 InitialSize => HUI.Size(760f, 640f, 640f, 480f);
        protected override float Margin => 0f;
        public override void PreOpen() { base.PreOpen(); BankUI.SetMinSize(this, 560f, 420f); }
        public override void PostOpen() { base.PostOpen(); BankSound.Play(BankSound.Open); }

        public override void DoWindowContents(Rect inRect)
        {
            var f = Text.Font; var a = Text.Anchor;
            Guard.Run(ModuleId.Bank, "Window_ChooseBank", () => Draw(inRect));
            Text.Font = f; Text.Anchor = a; GUI.color = Color.white;
        }

        void Draw(Rect inRect)
        {
            var c = BankGameComp.Current; if (c == null) return;
            Widgets.DrawBoxSolid(inRect, BankUI.Walnut);
            GUI.color = BankUI.Brass; Widgets.DrawBox(inRect.ContractedBy(4f), 2); GUI.color = Color.white;
            var r = inRect.ContractedBy(14f);
            BankUI.Label(new Rect(r.x, r.y, r.width - 30f, 30f), L.T("Choose.Title", "Change banks"), BankUI.Brass, GameFont.Medium);
            // the price strip: what moving costs and whether the account can pay it
            var strip = new Rect(r.x, r.y + 34f, r.width, 0f);
            string intro = L.T("Choose.Intro", "Moving the account costs {0} silver, taken from the account. Loans stay with the bank that made them.", c.SwitchFee.ToStringShort());
            Text.Font = GameFont.Small; float ih = Text.CalcHeight(intro, r.width - 16f) + 10f; strip.height = ih;
            Widgets.DrawBoxSolid(strip, BankUI.Green); GUI.color = BankUI.BrassDim; Widgets.DrawBox(strip, 1); GUI.color = Color.white;
            BankUI.Label(new Rect(strip.x + 8f, strip.y + 5f, strip.width - 16f, ih - 6f), intro, BankUI.Cream);
            var banks = SL_BankDef.AllSorted.Where(b => b.Available).ToList();
            float top = strip.yMax + 8f;
            var list = new Rect(r.x, top, r.width, r.yMax - top);
            const float btnW = 150f;
            scroll = BankCards.DrawList(list, scroll, banks, b => false, b => b.defName == c.Bank?.defName, null, (b, strip2) =>
            {
                var btn = new Rect(strip2.x - 6f, strip2.y + 12f, btnW - 6f, 34f);
                if (b.defName == c.Bank?.defName) BankUI.Label(btn, L.T("Choose.Current", "your bank"), BankUI.Good, anchor: TextAnchor.MiddleCenter);
                else
                {
                    bool ok = c.Balance >= c.SwitchFee;
                    if (BankUI.Button(btn, L.T("Choose.Switch", "Switch ({0})", c.SwitchFee.ToStringShort()), ok, L.T("Reason.SwitchFunds", "Moving the account costs {0} silver and the account does not hold that.", c.SwitchFee.ToStringShort())))
                    {
                        if (c.SwitchBank(b, out string why)) { BankSound.Play(BankSound.Stamp); Close(); } else { BankSound.Play(BankSound.Reject); Messages.Message(why, MessageTypeDefOf.RejectInput, false); }
                    }
                }
            }, btnW);
        }
    }
}
