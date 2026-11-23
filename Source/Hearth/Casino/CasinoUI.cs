// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Hearth.CasinoMath;

namespace Hearth.Casino
{
    /// <summary>Links and text of the real-life help hint (K28). Defined in XML so they can change without a code update.</summary>
    public class HelpHintDef : Def
    {
        public string primaryUrl = "https://findahelpline.com";
        public string secondaryUrl = "https://www.gamblingtherapy.org";
        public string primaryLabel = "findahelpline.com";
        public string secondaryLabel = "gamblingtherapy.org";
    }

    public static class CasinoUI
    {
        public static int HintDrawCount;
        public static void HideHint() => HSettings.Set("core.helpHint", 0f);
        public static HelpHintDef Hint => DefDatabase<HelpHintDef>.GetNamedSilentFail("HH_HelpHint_Gambling");

        /// <summary>"If gambling is a problem in real life, help is available: findahelpline.com" – or null when the player turned it off.</summary>
        public static string HelpHintText()
        {
            if (!HSettings.GetBool("core.helpHint")) return null;
            var h = Hint;
            return "HH.Casino.HelpHint".Translate().ToString().Replace("{LINK}", h?.primaryLabel ?? "findahelpline.com");
        }

        /// <summary>Country page of the helpline when the system region is known, else the start page.</summary>
        public static string PrimaryUrl()
        {
            var h = Hint; string root = h?.primaryUrl ?? "https://findahelpline.com";
            try
            {
                string region = System.Globalization.RegionInfo.CurrentRegion.TwoLetterISORegionName.ToLowerInvariant();
                if (region.Length == 2 && region != "iv") return root.TrimEnd('/') + "/countries/" + region + "/topics/gambling";
            }
            catch (Exception) { }
            return root;
        }

        /// <summary>Draws the small, quiet help line. Left click opens the helpline, right click offers to hide it. Returns the height used.</summary>
        public static float DrawHelpHint(Rect rect)
        {
            string text = HelpHintText();
            if (text.NullOrEmpty()) return 0f;
            HintDrawCount++;
            var oldFont = Text.Font; var oldAnchor = Text.Anchor; var oldColor = GUI.color;
            Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = new Color(0.62f, 0.62f, 0.62f, 0.9f);
            Widgets.Label(rect, text);
            GUI.color = oldColor; Text.Font = oldFont; Text.Anchor = oldAnchor;
            if (Mouse.IsOver(rect)) { Widgets.DrawHighlightIfMouseover(rect); TooltipHandler.TipRegion(rect, "HH.Casino.HelpHintTip".Translate()); }
            if (Widgets.ButtonInvisible(rect)) Application.OpenURL(PrimaryUrl());
            if (Event.current.type == EventType.MouseDown && Event.current.button == 1 && Mouse.IsOver(rect))
            {
                Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                {
                    new FloatMenuOption("HH.Casino.HideHint".Translate(), HideHint),
                    new FloatMenuOption("HH.Casino.OpenHelpline".Translate(), () => Application.OpenURL(PrimaryUrl()))
                }));
                Event.current.Use();
            }
            return rect.height;
        }
    }

    /// <summary>
    /// A practice game for the player at a device (KONZEPT K14: the player never gambles against his own house with real money).
    /// Practice chips, sealed random stream, the odds of the current settings. Every window where the player gambles shows the help hint.
    /// 1.1.0 (F30/F35): the same tables as the Lucky Thrumbo (reels, wheel, cards), a Rimdeck table with real cards for the practice poker,
    /// the bets as buttons, a short "how to play", and a two-column layout that grows with the screen.
    /// </summary>
    public class Dialog_CasinoDemo : Window
    {
        readonly CompCasinoGame comp;
        double chips = 100; double stake = 5; int rounds; double staked, paid;
        string last = ""; Color lastColor = Color.white;
        double heldChips; bool holding; string pendingLast; Color pendingColor;
        // roulette
        int rouletteChoice = 0; string straightText = "17";
        // blackjack
        BlackjackSolver.Round bj; BlackjackSolver solver; ulong bjSeed;
        // the tables
        public readonly Anim.SlotMachineView Slots = new Anim.SlotMachineView();
        public readonly Anim.RouletteView Wheel = new Anim.RouletteView();
        public readonly Anim.BlackjackView Cards = new Anim.BlackjackView();
        public readonly Anim.PokerView Poker = new Anim.PokerView();
        ulong uiSeed = (ulong)DateTime.Now.Ticks ^ 0x44454D4FUL;
        Vector2 howScroll;

        static readonly string[] betKeys = { "Red", "Black", "Odd", "Even", "Low", "High", "Dozen1", "Dozen2", "Dozen3", "Column1", "Column2", "Column3", "Straight" };

        public override Vector2 InitialSize => HUI.Size(860f, 660f, 680f, 540f);
        /// <summary>True when this window reserves its quiet help line (core.helpHint is on).</summary>
        public bool ShowsHelpHint => CasinoUI.HelpHintText() != null;
        public bool Busy => Slots.Busy || Wheel.Busy || Cards.Busy || Poker.Busy;
        public double Chips => chips;
        public double ShownChips => holding && Busy ? heldChips : chips;
        public string LastLine => holding ? (Busy ? null : pendingLast) : last;

        public Dialog_CasinoDemo(CompCasinoGame comp)
        {
            this.comp = comp; doCloseX = true; absorbInputAroundWindow = false; closeOnClickedOutside = false; draggable = true; preventCameraMotion = false;
            Wheel.Zeros = CS.Roulette.Zeros;
        }

        RngStream Rng => JRand.Sealed("casino.player." + comp.Game.ToString().ToLowerInvariant());
        ulong Next() => uiSeed = RngStream.Mix(uiSeed + 0x9E3779B97F4A7C15UL);
        public void SkipAll() { Slots.Skip(); Wheel.Skip(); Cards.Skip(); Poker.Skip(); }

        public override void DoWindowContents(Rect inRect)
        {
            Guard.Run(ModuleId.Casino, "Casino.Demo.Draw", () => Draw(inRect));
        }

        void Draw(Rect inRect)
        {
            var of = Text.Font; var oa = Text.Anchor; var oc = GUI.color;
            try
            {
                if (holding && !Busy) { holding = false; last = pendingLast; lastColor = pendingColor; }
                // header: the device and what this is
                Text.Font = GameFont.Medium; Widgets.Label(new Rect(0, 0, inRect.width, 34f), comp.parent.LabelCap); Text.Font = GameFont.Small;
                string info = "HH.Casino.DemoInfo".Translate().ToString().Replace("{ODDS}", OddsText());
                Text.Font = GameFont.Tiny; float ih = Text.CalcHeight(info, inRect.width);
                GUI.color = new Color(0.8f, 0.8f, 0.8f); Widgets.Label(new Rect(0, 34f, inRect.width, ih), info); GUI.color = Color.white; Text.Font = GameFont.Small;
                float y = 36f + ih + 6f;
                float hint = ShowsHelpHint ? 24f : 0f;
                float resultH = 40f;
                float bodyH = inRect.height - y - resultH - 8f - hint;
                var cols = HUI.Cols(new Rect(0, y, inRect.width, bodyH), 12f, 0.63f, 0.37f);
                DrawTable(cols[0]);
                DrawSide(cols[1]);
                // the result, held back while a round plays out
                var box = new Rect(0, y + bodyH + 6f, inRect.width, resultH);
                Widgets.DrawBoxSolid(box, new Color(0.05f, 0.06f, 0.08f, 0.5f)); Widgets.DrawBox(box, 1);
                Text.Anchor = TextAnchor.MiddleLeft;
                string line = holding && Busy ? "HH.Thrumbo.Anim.Spinning".Translate().ToString() : last;
                GUI.color = holding && Busy ? new Color(0.72f, 0.72f, 0.72f) : lastColor;
                Widgets.Label(box.ContractedBy(10f, 2f), line ?? "");
                GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft;
                // the help hint closes every window where the player gambles
                CasinoUI.DrawHelpHint(new Rect(0, inRect.height - 22f, inRect.width, 20f));
            }
            finally { Text.Font = of; Text.Anchor = oa; GUI.color = oc; }
        }

        // ---- the table (left) -----------------------------------------------------------------------------------------
        void DrawTable(Rect r)
        {
            var stage = new Rect(r.x, r.y, r.width, r.height - 46f);
            Widgets.DrawBoxSolid(stage, new Color(0.22f, 0.14f, 0.09f));
            var felt = stage.ContractedBy(4f);
            var old = GUI.color; GUI.color = new Color(0.82f, 0.95f, 0.86f); Anim.AnimDraw.Tiled(felt, Anim.CasinoAnimTex.Felt, 160f); GUI.color = old;
            if (Busy && Anim.AnimDraw.SkipRequested(stage)) SkipAll();
            var a = felt.ContractedBy(8f);
            switch (comp.Game)
            {
                case CasinoGame.Slots: Slots.Draw(a, comp.parent.def.LabelCap); break;
                case CasinoGame.Roulette: Wheel.Draw(new Rect(a.center.x - a.height / 2f, a.y, a.height, a.height)); Wheel.DrawRecent(new Rect(a.x, a.yMax - 20f, Mathf.Min(300f, a.width), 20f), 10); break;
                case CasinoGame.Blackjack: Cards.Draw(a); if (!Cards.Busy) Cards.DrawBanner(a); break;
                default:
                    if (Poker.Deal == null) { GUI.color = new Color(1f, 1f, 1f, 0.7f); Text.Anchor = TextAnchor.MiddleCenter; Widgets.Label(a, "HH.Casino.DemoPoker".Translate()); Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white; }
                    Poker.Draw(a, Seats()); break;
            }
            // buttons under the table
            var br = new Rect(r.x, stage.yMax + 6f, r.width, 36f);
            string skip = "HH.Thrumbo.Anim.Skip".Translate();
            bool busy = Busy;
            switch (comp.Game)
            {
                case CasinoGame.Slots:
                    if (Widgets.ButtonText(new Rect(br.x, br.y, 160f, 36f), busy ? skip : "HH.Casino.Spin".Translate())) { if (busy) SkipAll(); else SpinSlots(); }
                    break;
                case CasinoGame.Roulette:
                    if (Widgets.ButtonText(new Rect(br.x, br.y, 160f, 36f), busy ? skip : "HH.Casino.Spin".Translate())) { if (busy) SkipAll(); else SpinRoulette(); }
                    break;
                case CasinoGame.Blackjack:
                    if (bj == null || bj.Done)
                    {
                        if (Widgets.ButtonText(new Rect(br.x, br.y, 160f, 36f), busy ? skip : (bj == null ? "HH.Casino.Deal" : "HH.Thrumbo.BJ.DealAgain").Translate())) { if (busy) SkipAll(); else DealBlackjack(); }
                    }
                    else
                    {
                        float bw = Mathf.Min(130f, (br.width - 12f) / 3f);
                        if (Widgets.ButtonText(new Rect(br.x, br.y, bw, 36f), busy ? skip : "HH.Casino.Hit".Translate())) { if (busy) SkipAll(); else { solver.Hit(bj, Rng); After(); } }
                        if (Widgets.ButtonText(new Rect(br.x + bw + 6f, br.y, bw, 36f), "HH.Casino.Stand".Translate(), true, true, !busy) && !busy) { solver.Stand(bj, Rng); After(); }
                        bool can = !busy && bj.Player.Count == 2 && chips + 1e-9 >= stake * 2;
                        if (Widgets.ButtonText(new Rect(br.x + 2 * (bw + 6f), br.y, bw, 36f), "HH.Casino.DoubleDown".Translate(), true, true, can) && can) { solver.DoubleDown(bj, Rng); Anim.CasinoSfx.Play(Anim.CasinoSfx.ChipsPlace); After(); }
                    }
                    break;
                default:
                    if (Widgets.ButtonText(new Rect(br.x, br.y, 160f, 36f), busy ? skip : "HH.Casino.Deal".Translate())) { if (busy) SkipAll(); else DealPoker(); }
                    break;
            }
            if (busy)
            {
                Text.Font = GameFont.Tiny; GUI.color = new Color(1f, 1f, 1f, 0.55f); Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(br.x + 170f, br.y, br.width - 170f, 36f), "HH.Thrumbo.Anim.SkipHint".Translate());
                Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white; Text.Font = GameFont.Small;
            }
        }

        string[] Seats() => new[] { "HH.Casino.Demo.You".Translate().ToString(), "HH.Casino.Demo.Regular".Translate(1).ToString(), "HH.Casino.Demo.Regular".Translate(2).ToString(), "HH.Casino.Demo.Regular".Translate(3).ToString() };

        // ---- chips, stake, bets, how to play (right) -------------------------------------------------------------------
        void DrawSide(Rect r)
        {
            Widgets.DrawBoxSolid(r, new Color(0.08f, 0.09f, 0.10f, 0.45f)); Widgets.DrawBox(r, 1);
            var a = r.ContractedBy(8f); float y = a.y;
            bool busy = Busy;
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(a.x, y, a.width, 24f), "HH.Casino.DemoChips".Translate(ShownChips.ToString("0.##"), rounds));
            y += 26f;
            if (Widgets.ButtonText(new Rect(a.x, y, Mathf.Min(170f, a.width), 26f), "HH.Casino.DemoReset".Translate(), true, true, !busy) && !busy)
            { chips = 100; rounds = 0; staked = paid = 0; last = ""; bj = null; Poker.Skip(); }
            y += 34f;
            Widgets.Label(new Rect(a.x, y, a.width, 22f), "HH.Casino.DemoStake".Translate()); y += 22f;
            float sx = a.x, sw = (a.width - 18f) / 4f;
            foreach (double v in new double[] { 1, 5, 25, 100 })
            {
                var rr = new Rect(sx, y, sw, 28f); sx += sw + 6f;
                bool on = Math.Abs(stake - v) < 1e-9;
                if (Pick(rr, v.ToString("0"), on, !busy)) { stake = v; Anim.CasinoSfx.Play(Anim.CasinoSfx.Chip); }
            }
            y += 36f;
            if (comp.Game == CasinoGame.Roulette)
            {
                Widgets.Label(new Rect(a.x, y, a.width, 22f), "HH.Casino.DemoBet".Translate()); y += 22f;
                float bw = (a.width - 8f) / 3f;
                for (int i = 0; i < betKeys.Length; i++)
                {
                    var rr = new Rect(a.x + (i % 3) * (bw + 4f), y + (i / 3) * 28f, bw, 26f);
                    bool on = rouletteChoice == i;
                    string label = i >= 9 && i <= 11 ? "HH.Casino.Demo.Column".Translate(i - 8).ToString() : i == 12 ? "HH.Casino.Demo.Straight".Translate().ToString() : ("HH.Thrumbo.Roulette." + betKeys[i]).Translate().ToString();
                    if (Pick(rr, label, on, !busy)) { rouletteChoice = i; Anim.CasinoSfx.Play(Anim.CasinoSfx.Chip); }
                }
                y += 5 * 28f;
                if (rouletteChoice == 12)
                {
                    Widgets.Label(new Rect(a.x, y + 3f, 90f, 24f), "HH.Casino.Demo.Number".Translate());
                    straightText = Widgets.TextField(new Rect(a.x + 94f, y, 60f, 26f), straightText, 2);
                    y += 30f;
                }
                y += 4f;
            }
            if (staked > 0 && !(holding && busy))
            {
                Text.Font = GameFont.Tiny; GUI.color = new Color(0.8f, 0.8f, 0.8f);
                string st = "HH.Casino.DemoStats".Translate((paid / staked * 100).ToString("0.0"), ((1 - comp.ExpectedEdge) * 100).ToString("0.0"));
                float h = Text.CalcHeight(st, a.width); Widgets.Label(new Rect(a.x, y, a.width, h), st); y += h + 6f;
                GUI.color = Color.white; Text.Font = GameFont.Small;
            }
            // how to play
            Widgets.DrawLineHorizontal(a.x, y, a.width); y += 6f;
            GUI.color = new Color(0.95f, 0.82f, 0.45f);
            Widgets.Label(new Rect(a.x, y, a.width, 22f), "HH.Thrumbo.HowTo.Title".Translate(("HH.Casino.Game." + comp.Game).Translate()));
            GUI.color = new Color(0.9f, 0.9f, 0.86f); Text.Font = GameFont.Tiny; y += 22f;
            string how = ("HH.Thrumbo.HowTo." + comp.Game).Translate();
            var outer = new Rect(a.x, y, a.width, a.yMax - y);
            float hh = Text.CalcHeight(how, outer.width - 16f);
            var view = new Rect(0, 0, outer.width - 16f, Math.Max(outer.height, hh + 4f));
            Widgets.BeginScrollView(outer, ref howScroll, view);
            Widgets.Label(new Rect(0, 0, view.width, hh + 2f), how);
            Widgets.EndScrollView();
            GUI.color = Color.white; Text.Font = GameFont.Small;
        }

        /// <summary>A choice button: the chosen one is a green plate with a centred label.</summary>
        static bool Pick(Rect r, string label, bool on, bool enabled)
        {
            if (on)
            {
                Widgets.DrawBoxSolid(r, new Color(0.25f, 0.45f, 0.30f, 0.6f)); Widgets.DrawBox(r, 1);
                var oa = Text.Anchor; Text.Anchor = TextAnchor.MiddleCenter; Widgets.Label(r, "<b>" + label + "</b>"); Text.Anchor = oa;
                return false;
            }
            if (!enabled) GUI.color = new Color(1f, 1f, 1f, 0.5f);
            bool click = Widgets.ButtonText(r, label, true, true, enabled);
            GUI.color = Color.white;
            return click && enabled;
        }

        string OddsText()
        {
            switch (comp.Game)
            {
                case CasinoGame.Slots: return "HH.Casino.Demo.OddsSlots".Translate((comp.Paytable.Rtp * 100).ToString("0.#"));
                case CasinoGame.Roulette: return "HH.Casino.Demo.OddsRoulette".Translate((CS.Roulette.TargetEdge * 100).ToString("0.##"));
                case CasinoGame.Blackjack: return "HH.Casino.Demo.OddsBlackjack".Translate(CS.Blackjack.ToString());
                default: return "HH.Casino.Demo.OddsPoker".Translate((CS.PokerRakeRate * 100).ToString("0.#"), CS.PokerCap.ToString("0.#"));
            }
        }

        bool Afford(double amount) { if (chips + 1e-9 >= amount) return true; last = "HH.Casino.DemoBroke".Translate(); lastColor = new Color(1f, 0.75f, 0.45f); return false; }

        /// <summary>Books a round at once (outcome first) and lets the table show it before the chips and the line change.</summary>
        void Result(double staked_, double returned, string text)
        {
            if (!holding) heldChips = chips - staked_;
            else heldChips -= staked_;
            chips += returned - staked_; staked += staked_; paid += returned; rounds++;
            double net = returned - staked_;
            pendingLast = text + "   (" + (net >= 0 ? "+" : "") + net.ToString("0.##") + ")";
            pendingColor = net > 1e-9 ? new Color(0.6f, 0.9f, 0.6f) : net < -1e-9 ? new Color(1f, 0.58f, 0.48f) : Color.white;
            holding = Busy;
            if (!holding) { last = pendingLast; lastColor = pendingColor; }
        }

        void SpinSlots()
        {
            if (!Afford(stake)) return;
            double mult = comp.Paytable.Spin(Rng, out int a, out int b, out int c);
            Slots.Start(new[] { a, b, c }, mult, Next());
            Result(stake, stake * mult, PlayerHouse.ThrumboNames.Reels(a, b, c) + " — " + (mult > 0 ? "HH.Casino.DemoPays".Translate(mult.ToString("0.##")).ToString() : "HH.Casino.DemoNoWin".Translate().ToString()));
        }

        void SpinRoulette()
        {
            if (!Afford(stake)) return;
            var rules = CS.Roulette; RouletteBet bet;
            switch (rouletteChoice)
            {
                case 0: bet = RouletteBet.Red(); break; case 1: bet = RouletteBet.Black(); break; case 2: bet = RouletteBet.Odd(); break; case 3: bet = RouletteBet.Even(); break;
                case 4: bet = RouletteBet.Low(); break; case 5: bet = RouletteBet.High(); break;
                case 6: case 7: case 8: bet = RouletteBet.Dozen(rouletteChoice - 5); break;
                case 9: case 10: case 11: bet = RouletteBet.Column(rouletteChoice - 8); break;
                default: int n; if (!int.TryParse(straightText, out n) || n < 0 || n > 36) n = 17; bet = RouletteBet.Straight(n); break;
            }
            int pocket = rules.Spin(Rng);
            double ret = rules.Return(bet, pocket);
            Wheel.Start(pocket, rules.Zeros, ret > 0, ret >= 20, Next());
            string text = "HH.Thrumbo.Ball".Translate(RouletteBet.PocketName(pocket), ("HH.Thrumbo.Color." + PlayerHouse.ThrumboHouse.PocketColor(pocket)).Translate()).ToString() + " · " + PlayerHouse.ThrumboNames.Bet(bet);
            Result(stake, stake * ret, text);
        }

        void DealBlackjack()
        {
            solver = solver ?? BlackjackSolver.Get(CS.Blackjack, 1.0);
            if (!Afford(stake * 2)) return;
            bj = solver.Deal(Rng); bjSeed = Next();
            After();
        }

        /// <summary>Puts the hand on the table; a finished hand is booked.</summary>
        void After()
        {
            if (bj == null) return;
            if (bj.Done)
            {
                double st = stake * (bj.Doubled ? 2 : 1), ret = Math.Max(0, st + stake * bj.Result);
                double net = ret - st;
                string banner = ("HH.Thrumbo.BJ." + bj.Outcome).Translate().CapitalizeFirst() + "   " + (net >= 0 ? "+" : "") + net.ToString("0.##");
                Cards.Sync(bj.Player, bj.Dealer, false, bjSeed, bj.Outcome, banner, net > 0 ? new Color(0.6f, 0.9f, 0.6f) : net < 0 ? new Color(1f, 0.58f, 0.48f) : Color.white, net > 0);
                Result(st, ret, "HH.Thrumbo.BJ.Line".Translate(bj.PlayerTotal.ToString(), bj.DealerTotal.ToString(), ("HH.Thrumbo.BJ." + bj.Outcome).Translate()).ToString());
            }
            else Cards.Sync(bj.Player, bj.Dealer, true, bjSeed, null, null, Color.white, false);
        }

        void DealPoker()
        {
            if (!Afford(stake)) return;
            var skills = new List<double> { 9.0, 8.0, 7.0, 9.0 };
            var hand = RimdeckMath.Deal(Rng, skills, stake, CS.PokerRakeRate, CS.PokerCap);
            var deal = PokerHands.Cosmetic(hand.WinningCategory, 4, hand.Winner, Next());
            string handName = ("HH.Casino.Hand." + deal.Category).Translate();
            bool you = hand.Winner == 0;
            string banner = you ? "HH.Casino.Demo.PokerYouWin".Translate(handName).ToString() : "HH.Casino.Demo.PokerSeatWins".Translate(Seats()[hand.Winner], handName).ToString();
            Poker.Start(deal, banner, you);
            Result(stake, you ? hand.Payout : 0, banner + " · " + "HH.Casino.Demo.Rake".Translate(hand.Rake.ToString("0.##")));
        }

#if HH_TESTS
        public void TestSpin() { switch (comp.Game) { case CasinoGame.Slots: SpinSlots(); break; case CasinoGame.Roulette: SpinRoulette(); break; case CasinoGame.Blackjack: DealBlackjack(); if (bj != null && !bj.Done) { solver.Stand(bj, Rng); After(); } break; default: DealPoker(); break; } }
#endif
    }

    /// <summary>The "Casino" page of the Hearth &amp; Harbor main tab.</summary>
    public static class CasinoTab
    {
        static Vector2 scroll; static float viewHeight = 600f;

        public static bool Any() => Find.Maps.Any(m => m.IsPlayerHome && CasinoBank.AllDevices(m).Any());

        public static void Draw(Rect rect)
        {
            var c = CasinoComponent.Get(); if (c == null) return;
            Rect hint = new Rect(rect.x, rect.yMax - 26f, rect.width, 24f);
            var inner = new Rect(rect.x, rect.y, rect.width, rect.height - 28f);
            var body = Hearth.Shell.PageFrame.Header(inner, "HH.Casino.TabTitle".Translate(), "HH.UI.Page.Sub.casino".TranslateOr("Houses, takings and how each game is doing against its odds."));
            float vw = Hearth.Shell.PageFrame.BeginScroll(body, ref scroll, viewHeight);
            var view = new Rect(0, 0, vw, viewHeight);
            float y = 0f;

            PlayerHouse.ThrumboCard.Draw(ref y, view.width);

            bool limits = CS.B(CS.LimitsOn);
            string limitsLabel = "HH.Casino.LimitsToggle".Translate(CS.F(CS.MinBet).ToString("0.##"), CS.F(CS.MaxBet).ToString("0.##"));
            float limitsH = Mathf.Max(26f, HUI.TextH(limitsLabel, Mathf.Min(view.width, 620f) - 30f));
            Widgets.CheckboxLabeled(new Rect(0, y, Mathf.Min(view.width, 620f), limitsH), limitsLabel, ref limits);
            if (limits != CS.B(CS.LimitsOn)) HSettings.Set(CS.LimitsOn, limits ? 1f : 0f);
            y += limitsH + 8f;

            // balance badge: any game that pays out more than it takes in is a gift to the guests
            var losing = new List<string>();
            if (CS.SlotTargetRtp >= 1.0) losing.Add("HH.Casino.Game.Slots".Translate());
            if (CS.Roulette.TargetEdge <= 0) losing.Add("HH.Casino.Game.Roulette".Translate());
            if (BlackjackSolver.Get(CS.Blackjack, 0.6).HouseEdge <= 0) losing.Add("HH.Casino.Game.Blackjack".Translate());
            if (CS.PokerRakeRate <= 0) losing.Add("HH.Casino.Game.Poker".Translate());
            if (losing.Count > 0)
            {
                string warn = "HH.Casino.LosingWarning".Translate(string.Join(", ", losing));
                float warnH = HUI.TextH(warn, view.width);
                GUI.color = new Color(1f, 0.6f, 0.35f);
                Widgets.Label(new Rect(0, y, view.width, warnH), warn);
                GUI.color = Color.white; y += warnH + 6f;
            }
            else
            {
                string okTxt = "HH.Casino.BalancedBadge".Translate();
                float okH = HUI.TextH(okTxt, view.width);
                GUI.color = new Color(0.6f, 0.85f, 0.6f);
                Widgets.Label(new Rect(0, y, view.width, okH), okTxt);
                GUI.color = Color.white; y += okH + 6f;
            }

            int today = Find.TickManager.TicksGame / 60000;
            // houses
            Section(ref y, view.width, "HH.Casino.Houses".Translate());
            var houses = c.Houses.Where(h => Find.Maps.Any(m => m.uniqueID == h.MapId)).ToList();
            if (houses.Count == 0) { GUI.color = Color.gray; Widgets.Label(new Rect(0, y, view.width, 24f), "HH.Casino.NoHouses".Translate()); GUI.color = Color.white; y += 26f; }
            foreach (var h in houses)
            {
                double win = c.RowsOn(today, h.Account).Sum(r => r.HouseWin);
                Money cash = CasinoBank.Cash(h.Account);
                var row = new Rect(0, y, view.width, 28f);
                var hc = HUI.Cols(row, 8f, 0.38f, 0.30f, 0.32f);
                if (Mouse.IsOver(row)) Widgets.DrawHighlight(row);
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(hc[0], (h.Label ?? h.Account).Truncate(hc[0].width));
                GUI.color = cash.IsNegative ? new Color(1f, 0.5f, 0.4f) : Color.white;
                Widgets.Label(hc[1], "HH.Casino.Drawer".Translate(cash.ToStringShort()));
                GUI.color = win >= 0 ? new Color(0.6f, 0.9f, 0.6f) : new Color(1f, 0.55f, 0.45f);
                Widgets.Label(hc[2], "HH.Casino.TodayNet".Translate((win >= 0 ? "+" : "") + win.ToString("0.##")));
                GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft;
                if (Mouse.IsOver(row)) TooltipHandler.TipRegion(row, "HH.Casino.HouseTip".Translate(c.OutstandingAt(h.Account).ToStringShort(), HSettings.GetSilver(CS.HouseFloat).ToStringShort()));
                y += 30f;
            }
            y += 6f;

            // RTP per game
            Section(ref y, view.width, "HH.Casino.Games".Translate());
            DrawGameHeader(ref y, view.width);
            foreach (CasinoGame g in Enum.GetValues(typeof(CasinoGame)))
            {
                var rows = c.Rows.Where(r => r.Game == (int)g).ToList();
                long staked = rows.Sum(r => r.StakedMicro), paid = rows.Sum(r => r.PaidMicro); int n = rows.Sum(r => r.Rounds);
                double exp = rows.Sum(r => r.Expected), varr = rows.Sum(r => r.Variance), win = (staked - paid) / (double)Money.PerSilver;
                double? z = n >= 30 && varr > 0 ? (win - exp) / Math.Sqrt(varr) : (double?)null;
                double actualRtp = staked > 0 ? paid / (double)staked : 0, expectedRtp = staked > 0 ? 1 - exp / (staked / (double)Money.PerSilver) : ExpectedRtp(g);
                DrawGameRow(ref y, view.width, g.ToString(), n, staked, paid, actualRtp, expectedRtp, z, win);
            }
            y += 8f;

            // gamblers
            Section(ref y, view.width, "HH.Casino.Gamblers".Translate());
            var gamblers = c.Records.Where(r => r.Pawn != null && !r.Pawn.Dead && r.Pawn.Faction == Faction.OfPlayer).ToList();
            if (gamblers.Count == 0) { GUI.color = Color.gray; Widgets.Label(new Rect(0, y, view.width, 24f), "HH.Casino.NoGamblers".Translate()); GUI.color = Color.white; y += 26f; }
            foreach (var r in gamblers)
            {
                var add = Gambling.AddictionOf(r.Pawn);
                float nameW = Mathf.Clamp(view.width * 0.22f, 160f, 260f);
                string txt = (Gambling.CounterText(r.Pawn) ?? "") + (add != null ? "  · " + add.LabelInBrackets : "");
                float gh = Mathf.Max(26f, HUI.TextH(txt, view.width - nameW));
                Widgets.Label(new Rect(0, y, nameW, gh), r.Pawn.LabelShortCap);
                if (add != null) GUI.color = new Color(1f, 0.6f, 0.45f);
                Widgets.Label(new Rect(nameW, y, view.width - nameW, gh), txt);
                GUI.color = Color.white;
                y += gh + 2f;
            }
            viewHeight = Hearth.Shell.PageFrame.EndScroll(y);
            CasinoUI.DrawHelpHint(hint);
        }

        static double ExpectedRtp(CasinoGame g)
        {
            switch (g)
            {
                case CasinoGame.Slots: return CS.SlotTargetRtp;
                case CasinoGame.Roulette: return 1 - CS.Roulette.TargetEdge;
                case CasinoGame.Blackjack: return 1 - BlackjackSolver.Get(CS.Blackjack, 0.6).HouseEdge;
                default: return 1 - RimdeckMath.HouseEdge(4, 10, CS.PokerRakeRate, CS.PokerCap);
            }
        }

        static void Section(ref float y, float w, string title)
        {
            y += 4f; Widgets.Label(new Rect(0, y, w, 26f), "<b>" + title + "</b>"); y += 26f; Widgets.DrawLineHorizontal(0, y, w); y += 6f;
        }

        // table columns as fractions of the page width (game, rounds, staked, RTP, expected, luck, house)
        static readonly float[] colFrac = { 0.20f, 0.11f, 0.15f, 0.12f, 0.14f, 0.14f, 0.14f };
        static float[] ColX(float w) { var x = new float[colFrac.Length + 1]; float a = 0; for (int i = 0; i < colFrac.Length; i++) { x[i] = a; a += w * colFrac[i]; } x[colFrac.Length] = w; return x; }
        static void DrawGameHeader(ref float y, float w)
        {
            GUI.color = new Color(0.75f, 0.75f, 0.75f);
            string[] h = { "HH.Casino.ColGame", "HH.Casino.ColRounds", "HH.Casino.ColStaked", "HH.Casino.ColRtp", "HH.Casino.ColExpected", "HH.Casino.ColLuck", "HH.Casino.ColHouse" };
            var cx = ColX(w);
            float rowH = 26f;
            for (int i = 0; i < h.Length; i++) rowH = Mathf.Max(rowH, HUI.TextH(h[i].Translate(), cx[i + 1] - cx[i] - 6f));
            for (int i = 0; i < h.Length; i++) Widgets.Label(new Rect(cx[i], y, cx[i + 1] - cx[i] - 4f, rowH), h[i].Translate());
            GUI.color = Color.white; y += rowH + 2f;
        }
        static void DrawGameRow(ref float y, float w, string game, int rounds, long staked, long paid, double rtp, double expected, double? z, double houseWin)
        {
            var cx = ColX(w); const float rh = 26f;
            Widgets.Label(new Rect(cx[0], y, cx[1] - cx[0] - 4f, rh), ("HH.Casino.Game." + game).Translate());
            Widgets.Label(new Rect(cx[1], y, cx[2] - cx[1] - 4f, rh), rounds.ToString("N0"));
            Widgets.Label(new Rect(cx[2], y, cx[3] - cx[2] - 4f, rh), (staked / (double)Money.PerSilver).ToString("N0"));
            Widgets.Label(new Rect(cx[3], y, cx[4] - cx[3] - 4f, rh), staked > 0 ? (rtp * 100).ToString("0.0") + " %" : "–");
            Widgets.Label(new Rect(cx[4], y, cx[5] - cx[4] - 4f, rh), (expected * 100).ToString("0.0") + " %");
            Widgets.Label(new Rect(cx[5], y, cx[6] - cx[5] - 4f, rh), z == null ? "–" : z.Value.ToString("+0.0;-0.0") + " σ");
            GUI.color = houseWin >= 0 ? new Color(0.6f, 0.9f, 0.6f) : new Color(1f, 0.55f, 0.45f);
            Widgets.Label(new Rect(cx[6], y, cx[7] - cx[6] - 4f, rh), (houseWin >= 0 ? "+" : "") + houseWin.ToString("0.#"));
            GUI.color = Color.white;
            y += rh;
        }
    }
}
