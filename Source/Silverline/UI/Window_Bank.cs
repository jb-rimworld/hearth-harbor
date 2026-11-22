// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Hearth;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Silverline
{
    /// <summary>The bank lobby (KONZEPT 8.4): header with the bank, a tab strip filled from BankTabs, the page, and Clerk Ottoline's remark.</summary>
    public class Window_Bank : Window
    {
        public static Window_Bank Active;
        public readonly Map Map;
        public readonly BankSite Site;
        public readonly Building At;
        public string TabId = "lobby";
        public double ShownBalance = double.NaN;
        string openPick;
        bool openDirect = true;

        public Window_Bank(Map map, BankSite site, Building at)
        {
            Map = map ?? BankGameComp.DefaultMap; Site = site; At = at;
            doCloseX = true; draggable = true; closeOnClickedOutside = false; absorbInputAroundWindow = false;
            forcePause = false; preventCameraMotion = false; closeOnAccept = false; onlyOneOfTypeAllowed = true;
            Active = this;
        }

        public override Vector2 InitialSize => new Vector2(900f, 660f);
        protected override float Margin => 0f;
        public override void PostClose() { if (Active == this) Active = null; base.PostClose(); }

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
            DrawTabs(new Rect(r.x, r.y + 80f, r.width, 32f));
            var body = new Rect(r.x, r.y + 118f, r.width, r.height - 118f - 66f);
            var tab = BankTabs.VisibleTabs.FirstOrDefault(t => t.Id == TabId) ?? BankTabs.VisibleTabs.FirstOrDefault();
            if (tab != null) { var t = tab; Guard.Run(ModuleId.Bank, "BankTab.Draw." + t.Id, () => t.Draw?.Invoke(body)); }
            DrawClerk(new Rect(r.x, r.yMax - 58f, r.width, 58f), c);
        }

        // ---- header ---------------------------------------------------------------------------------
        void DrawHeader(Rect r, BankGameComp c)
        {
            var bank = c.HasAccount ? c.Bank : (SL_BankDef.AllSorted.FirstOrDefault(b => b.defName == openPick) ?? SL_BankDef.Default);
            if (bank != null) GUI.DrawTexture(new Rect(r.x, r.y + 4f, 64f, 64f), bank.Icon);
            string title = c.HasAccount ? bank?.Name : L.T("Open.Title", "Silverline: open an account");
            BankUI.Label(new Rect(r.x + 76f, r.y + 4f, r.width - 330f, 32f), title ?? "Silverline", BankUI.Brass, GameFont.Medium, wrap: false);
            string sub = c.HasAccount ? bank?.Slogan : L.T("Open.Sub", "Your silver, our problem.");
            BankUI.Label(new Rect(r.x + 76f, r.y + 38f, r.width - 330f, 30f), sub ?? "", BankUI.CreamDim);
            var right = new Rect(r.xMax - 280f, r.y + 6f, 240f, 60f);
            BankUI.Label(new Rect(right.x, right.y, right.width, 24f), Greeting(), BankUI.Cream, anchor: TextAnchor.UpperRight, wrap: false);
            if (c.HasAccount)
            {
                string tier = L.T("Tier." + c.ShownTier, FinanceMath.TierNamesEn[Math.Max(0, Math.Min(FinanceMath.TierNamesEn.Length - 1, c.ShownTier))]);
                BankUI.Label(new Rect(right.x, right.y + 24f, right.width, 24f), L.T("Header.Tier", "Wealth tier: {0}", tier.ToUpperInvariant()), BankUI.Brass, anchor: TextAnchor.UpperRight, wrap: false);
                TooltipHandler.TipRegion(right, L.T("Header.TierTip", "Net worth = account + silver at home − debts: {0} silver. The tier only changes after a new value has held for a whole day. It sets the colony mood from this bank (setting: bank.moodOn).", c.NetWorth.ToStringShort()));
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
            float x = r.x;
            foreach (var t in BankTabs.VisibleTabs.ToList())
            {
                string label = t.Label;
                float w = Text.CalcSize(label).x + 34f;
                var tr = new Rect(x, r.y, w, r.height);
                bool active = t.Id == TabId;
                Widgets.DrawBoxSolid(tr, active ? BankUI.Green : BankUI.WalnutLight);
                if (active) Widgets.DrawBoxSolid(new Rect(tr.x, tr.yMax - 3f, tr.width, 3f), BankUI.Brass);
                BankUI.Label(tr, label, active ? BankUI.Cream : BankUI.CreamDim, anchor: TextAnchor.MiddleCenter, wrap: false);
                if (!active) Widgets.DrawHighlightIfMouseover(tr);
                if (Widgets.ButtonInvisible(tr)) { TabId = t.Id; SoundDefOf.Tick_Tiny.PlayOneShotOnCamera(); }
                x += w + 4f;
            }
            Widgets.DrawLineHorizontal(r.x, r.yMax, r.width, BankUI.BrassDim);
        }

        // ---- Clerk Ottoline ---------------------------------------------------------------------------
        void DrawClerk(Rect r, BankGameComp c)
        {
            Widgets.DrawBoxSolid(r, BankUI.Green);
            GUI.color = BankUI.BrassDim; Widgets.DrawBox(r, 1); GUI.color = Color.white;
            var tex = BankUI.Ottoline;
            if (tex != null) GUI.DrawTexture(new Rect(r.x + 6f, r.y + 5f, 48f, 48f), tex);
            BankUI.Label(new Rect(r.x + 64f, r.y + 4f, r.width - 76f, 50f), "Clerk Ottoline: \"" + Quip(c) + "\"", BankUI.Cream, anchor: TextAnchor.MiddleLeft);
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
            BankUI.Label(new Rect(r.x, r.y, r.width, 40f), L.T("Open.Intro", "Pick a bank. Opening an account is free; moving it later costs {0} silver. Until you have an account, nothing here bothers you, no letters, no mood, no fees.", c.SwitchFee.ToStringShort()), BankUI.CreamDim);
            float rowH = 74f; float y = r.y + 46f;
            var list = new Rect(r.x, y, r.width, r.height - 46f - 78f);
            var view = new Rect(0, 0, list.width - 16f, banks.Count * (rowH + 6f));
            Vector2 sc = openScroll;
            Widgets.BeginScrollView(list, ref sc, view);
            for (int i = 0; i < banks.Count; i++)
            {
                var b = banks[i]; var row = new Rect(0, i * (rowH + 6f), view.width, rowH);
                bool sel = b.defName == openPick;
                Widgets.DrawBoxSolid(row, sel ? BankUI.GreenLight : BankUI.WalnutLight);
                GUI.color = sel ? BankUI.Brass : BankUI.BrassDim; Widgets.DrawBox(row, sel ? 2 : 1); GUI.color = Color.white;
                GUI.DrawTexture(new Rect(row.x + 8f, row.y + 8f, 58f, 58f), b.Icon);
                BankUI.Label(new Rect(row.x + 76f, row.y + 4f, row.width - 90f, 24f), b.Name, BankUI.Brass, GameFont.Medium, wrap: false);
                BankUI.Label(new Rect(row.x + 76f, row.y + 30f, row.width - 90f, 20f), "\"" + b.Slogan + "\"", BankUI.Cream, wrap: false);
                BankUI.Label(new Rect(row.x + 76f, row.y + 50f, row.width - 90f, 22f), string.Join("  •  ", b.perks.Take(2)), BankUI.CreamDim, GameFont.Tiny, wrap: false);
                TooltipHandler.TipRegion(row, b.description + "\n\n" + string.Join("\n", b.perks.Select(p => "• " + p)));
                if (Widgets.ButtonInvisible(row)) openPick = b.defName;
            }
            openScroll = sc;
            Widgets.EndScrollView();
            var foot = new Rect(r.x, r.yMax - 70f, r.width, 66f);
            Widgets.CheckboxLabeled(new Rect(foot.x, foot.y, foot.width - 240f, 26f), L.T("Open.Direct", "Route till money straight to the bank at midnight"), ref openDirect);
            TooltipHandler.TipRegion(new Rect(foot.x, foot.y, foot.width - 240f, 26f), L.T("Open.DirectTip", "Takings of your shops, inn and casino go into the account instead of lying around as silver. You can switch this later at a night safe or in the settings (bank.directDeposit)."));
            if (BankUI.Button(new Rect(foot.xMax - 220f, foot.y + 4f, 220f, 40f), L.T("Open.Button", "Open account")))
            {
                var def = banks.FirstOrDefault(b => b.defName == openPick);
                if (def != null && c.OpenAccount(def, openDirect)) { SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera(); }
            }
        }
        Vector2 openScroll;
    }

    /// <summary>Dialog to move the account to another bank.</summary>
    public class Window_ChooseBank : Window
    {
        Vector2 scroll;
        public Window_ChooseBank() { doCloseX = true; draggable = true; absorbInputAroundWindow = false; closeOnClickedOutside = true; forcePause = false; }
        public override Vector2 InitialSize => new Vector2(620f, 520f);
        protected override float Margin => 0f;

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
            BankUI.Label(new Rect(r.x, r.y + 30f, r.width, 34f), L.T("Choose.Intro", "Moving the account costs {0} silver, taken from the account. Loans stay with the bank that made them.", c.SwitchFee.ToStringShort()), BankUI.CreamDim);
            var banks = SL_BankDef.AllSorted.Where(b => b.Available).ToList();
            var list = new Rect(r.x, r.y + 70f, r.width, r.height - 70f);
            float rowH = 84f;
            var view = new Rect(0, 0, list.width - 16f, banks.Count * (rowH + 6f));
            Widgets.BeginScrollView(list, ref scroll, view);
            for (int i = 0; i < banks.Count; i++)
            {
                var b = banks[i]; var row = new Rect(0, i * (rowH + 6f), view.width, rowH);
                bool current = b.defName == c.Bank?.defName;
                Widgets.DrawBoxSolid(row, current ? BankUI.Green : BankUI.WalnutLight);
                GUI.color = BankUI.BrassDim; Widgets.DrawBox(row, 1); GUI.color = Color.white;
                GUI.DrawTexture(new Rect(row.x + 8f, row.y + 10f, 60f, 60f), b.Icon);
                BankUI.Label(new Rect(row.x + 78f, row.y + 4f, row.width - 230f, 22f), b.Name, BankUI.Brass, wrap: false);
                BankUI.Label(new Rect(row.x + 78f, row.y + 26f, row.width - 230f, 20f), "\"" + b.Slogan + "\"", BankUI.Cream, GameFont.Tiny, wrap: false);
                BankUI.Label(new Rect(row.x + 78f, row.y + 46f, row.width - 230f, 36f), string.Join("\n", b.perks.Take(2)), BankUI.CreamDim, GameFont.Tiny);
                TooltipHandler.TipRegion(row, b.description + "\n\n" + string.Join("\n", b.perks.Select(p => "• " + p)));
                var btn = new Rect(row.xMax - 140f, row.y + 24f, 130f, 34f);
                if (current) BankUI.Label(btn, L.T("Choose.Current", "your bank"), BankUI.Good, anchor: TextAnchor.MiddleCenter);
                else
                {
                    bool ok = c.Balance >= c.SwitchFee;
                    if (BankUI.Button(btn, L.T("Choose.Switch", "Switch ({0})", c.SwitchFee.ToStringShort()), ok, L.T("Reason.SwitchFunds", "Moving the account costs {0} silver and the account does not hold that.", c.SwitchFee.ToStringShort())))
                    {
                        if (c.SwitchBank(b, out string why)) Close(); else Messages.Message(why, MessageTypeDefOf.RejectInput, false);
                    }
                }
            }
            Widgets.EndScrollView();
        }
    }
}
