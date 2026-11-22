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
    /// </summary>
    public class Dialog_CasinoDemo : Window
    {
        readonly CompCasinoGame comp;
        double chips = 100; double stake = 5; int rounds; double staked, paid;
        string last = "";
        // roulette
        int rouletteChoice; string straightText = "17";
        // blackjack
        BlackjackSolver.Round bj; BlackjackSolver solver;
        // slots
        string[] reels = { "?", "?", "?" };

        static readonly string[] rouletteBets = { "Red", "Black", "Odd", "Even", "Low 1-18", "High 19-36", "Dozen 1", "Dozen 2", "Dozen 3", "Column 1", "Column 2", "Column 3", "Straight…" };

        public override Vector2 InitialSize => new Vector2(560f, 520f);
        /// <summary>True when this window reserves its quiet help line (core.helpHint is on).</summary>
        public bool ShowsHelpHint => CasinoUI.HelpHintText() != null;

        public Dialog_CasinoDemo(CompCasinoGame comp)
        {
            this.comp = comp; doCloseX = true; absorbInputAroundWindow = false; closeOnClickedOutside = false; draggable = true; preventCameraMotion = false;
        }

        RngStream Rng => JRand.Sealed("casino.player." + comp.Game.ToString().ToLowerInvariant());

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium; Widgets.Label(new Rect(0, 0, inRect.width, 34f), comp.parent.LabelCap); Text.Font = GameFont.Small;
            var info = new Rect(0, 36f, inRect.width, 44f);
            GUI.color = new Color(0.8f, 0.8f, 0.8f);
            Widgets.Label(info, "HH.Casino.DemoInfo".Translate().ToString().Replace("{ODDS}", OddsText()));
            GUI.color = Color.white;
            float y = 84f;
            Widgets.Label(new Rect(0, y, 260f, 26f), "HH.Casino.DemoChips".Translate(chips.ToString("0.##"), rounds));
            if (Widgets.ButtonText(new Rect(inRect.width - 150f, y - 2f, 150f, 26f), "HH.Casino.DemoReset".Translate())) { chips = 100; rounds = 0; staked = paid = 0; last = ""; bj = null; }
            y += 30f;
            // stake buttons
            Widgets.Label(new Rect(0, y, 70f, 26f), "HH.Casino.DemoStake".Translate());
            float x = 74f;
            foreach (double v in new double[] { 1, 5, 25, 100 })
            {
                var r = new Rect(x, y - 2f, 60f, 26f); x += 66f;
                if (Math.Abs(stake - v) < 1e-9) Widgets.DrawBoxSolid(r, new Color(0.25f, 0.45f, 0.25f, 0.6f));
                if (Widgets.ButtonText(r, v.ToString("0"), false)) stake = v;
                Widgets.DrawBox(r);
            }
            y += 36f;
            switch (comp.Game)
            {
                case CasinoGame.Slots: DrawSlots(ref y, inRect.width); break;
                case CasinoGame.Roulette: DrawRoulette(ref y, inRect.width); break;
                case CasinoGame.Blackjack: DrawBlackjack(ref y, inRect.width); break;
                default: DrawPoker(ref y, inRect.width); break;
            }
            y += 8f;
            Widgets.Label(new Rect(0, y, inRect.width, 60f), last);
            y += 62f;
            if (staked > 0)
                Widgets.Label(new Rect(0, y, inRect.width, 26f), "HH.Casino.DemoStats".Translate((paid / staked * 100).ToString("0.0"), ((1 - comp.ExpectedEdge) * 100).ToString("0.0")));
            // the help hint closes every window where the player gambles
            CasinoUI.DrawHelpHint(new Rect(0, inRect.height - 22f, inRect.width, 20f));
        }

        string OddsText()
        {
            switch (comp.Game)
            {
                case CasinoGame.Slots: return "RTP " + (comp.Paytable.Rtp * 100).ToString("0.#") + " %";
                case CasinoGame.Roulette: return "house edge " + (CS.Roulette.TargetEdge * 100).ToString("0.##") + " %";
                case CasinoGame.Blackjack: return CS.Blackjack.ToString();
                default: return "rake " + (CS.PokerRakeRate * 100).ToString("0.#") + " % (max " + CS.PokerCap.ToString("0.#") + ")";
            }
        }

        bool Afford(double amount) { if (chips + 1e-9 >= amount) return true; last = "HH.Casino.DemoBroke".Translate(); return false; }

        void Result(double staked_, double returned, string text)
        {
            chips += returned - staked_; staked += staked_; paid += returned; rounds++;
            last = text + "   (" + (returned - staked_ >= 0 ? "+" : "") + (returned - staked_).ToString("0.##") + ")";
        }

        void DrawSlots(ref float y, float w)
        {
            Text.Font = GameFont.Medium; Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(0, y, w, 40f), reels[0] + "   |   " + reels[1] + "   |   " + reels[2]);
            Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
            y += 44f;
            if (Widgets.ButtonText(new Rect(w / 2 - 70f, y, 140f, 34f), "HH.Casino.Spin".Translate()) && Afford(stake))
            {
                double mult = comp.Paytable.Spin(Rng, out int a, out int b, out int c);
                reels = new[] { SlotPaytable.SymbolNames[a], SlotPaytable.SymbolNames[b], SlotPaytable.SymbolNames[c] };
                Result(stake, stake * mult, mult > 0 ? "HH.Casino.DemoPays".Translate(mult.ToString("0.##")).ToString() : "HH.Casino.DemoNoWin".Translate().ToString());
            }
            y += 40f;
        }

        void DrawRoulette(ref float y, float w)
        {
            Widgets.Label(new Rect(0, y, 60f, 26f), "HH.Casino.DemoBet".Translate());
            if (Widgets.ButtonText(new Rect(64f, y - 2f, 170f, 26f), rouletteBets[rouletteChoice]))
                Find.WindowStack.Add(new FloatMenu(rouletteBets.Select((n, i) => new FloatMenuOption(n, () => rouletteChoice = i)).ToList()));
            if (rouletteChoice == rouletteBets.Length - 1) straightText = Widgets.TextField(new Rect(244f, y - 2f, 60f, 26f), straightText, 2);
            y += 36f;
            if (Widgets.ButtonText(new Rect(w / 2 - 70f, y, 140f, 34f), "HH.Casino.Spin".Translate()) && Afford(stake))
            {
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
                string color = pocket == 0 || pocket == 37 ? "green" : RouletteBet.IsRed(pocket) ? "red" : "black";
                Result(stake, stake * rules.Return(bet, pocket), "Ball: " + RouletteBet.PocketName(pocket) + " " + color + " · " + bet.Label);
            }
            y += 40f;
        }

        void DrawBlackjack(ref float y, float w)
        {
            solver = solver ?? BlackjackSolver.Get(CS.Blackjack, 1.0);
            if (bj == null || bj.Done)
            {
                if (bj != null) Widgets.Label(new Rect(0, y, w, 44f), CardsText(bj, true));
                y += 46f;
                if (Widgets.ButtonText(new Rect(w / 2 - 70f, y, 140f, 34f), "HH.Casino.Deal".Translate()) && Afford(stake * 2))
                {
                    bj = solver.Deal(Rng);
                    if (bj.Done) FinishBlackjack();
                }
            }
            else
            {
                Widgets.Label(new Rect(0, y, w, 44f), CardsText(bj, false));
                y += 46f;
                float bw = 110f;
                if (Widgets.ButtonText(new Rect(w / 2 - 175f, y, bw, 34f), "HH.Casino.Hit".Translate())) { solver.Hit(bj, Rng); if (bj.Done) FinishBlackjack(); }
                if (Widgets.ButtonText(new Rect(w / 2 - 55f, y, bw, 34f), "HH.Casino.Stand".Translate())) { solver.Stand(bj, Rng); FinishBlackjack(); }
                if (bj.Player.Count == 2 && Widgets.ButtonText(new Rect(w / 2 + 65f, y, bw, 34f), "HH.Casino.DoubleDown".Translate())) { solver.DoubleDown(bj, Rng); FinishBlackjack(); }
            }
            y += 40f;
        }

        void FinishBlackjack()
        {
            double st = stake * (bj.Doubled ? 2 : 1);
            Result(st, Math.Max(0, st + stake * bj.Result), "you " + bj.PlayerTotal + " · dealer " + bj.DealerTotal + " · " + bj.Outcome);
        }

        static string CardsText(BlackjackSolver.Round r, bool showDealer)
        {
            string P = string.Join(" ", r.Player.Select(c => c == 1 ? "A" : c.ToString()));
            string D = showDealer ? string.Join(" ", r.Dealer.Select(c => c == 1 ? "A" : c.ToString())) : (r.Dealer[0] == 1 ? "A" : r.Dealer[0].ToString()) + " ?";
            return "You: " + P + " (" + BlackjackSolver.HandTotal(r.Player) + ")    Dealer: " + D;
        }

        void DrawPoker(ref float y, float w)
        {
            Widgets.Label(new Rect(0, y, w, 26f), "HH.Casino.DemoPoker".Translate());
            y += 30f;
            if (Widgets.ButtonText(new Rect(w / 2 - 70f, y, 140f, 34f), "HH.Casino.Deal".Translate()) && Afford(stake))
            {
                var skills = new List<double> { 9.0, 8.0, 7.0, 9.0 };
                var hand = RimdeckMath.Deal(Rng, skills, stake, CS.PokerRakeRate, CS.PokerCap);
                Result(stake, hand.Winner == 0 ? hand.Payout : 0, (hand.Winner == 0 ? "you win with " : "seat " + (hand.Winner + 1) + " wins with ") + hand.WinningHand + " (rake " + hand.Rake.ToString("0.##") + ")");
            }
            y += 40f;
        }
    }

    /// <summary>The "Casino" page of the Hearth &amp; Harbor main tab.</summary>
    public static class CasinoTab
    {
        static Vector2 scroll; static float viewHeight = 600f;

        public static bool Any() => Find.Maps.Any(m => m.IsPlayerHome && CasinoBank.AllDevices(m).Any());

        public static void Draw(Rect rect)
        {
            var c = CasinoComponent.Get(); if (c == null) return;
            Rect hint = new Rect(rect.x, rect.yMax - 22f, rect.width, 20f);
            var view = new Rect(0, 0, rect.width - 18f, viewHeight);
            Widgets.BeginScrollView(new Rect(rect.x, rect.y, rect.width, rect.height - 24f), ref scroll, view);
            float y = 0f;
            Text.Font = GameFont.Medium; Widgets.Label(new Rect(0, y, view.width, 32f), "HH.Casino.TabTitle".Translate()); Text.Font = GameFont.Small; y += 36f;

            bool limits = CS.B(CS.LimitsOn);
            Widgets.CheckboxLabeled(new Rect(0, y, 360f, 24f), "HH.Casino.LimitsToggle".Translate(CS.F(CS.MinBet).ToString("0.##"), CS.F(CS.MaxBet).ToString("0.##")), ref limits);
            if (limits != CS.B(CS.LimitsOn)) HSettings.Set(CS.LimitsOn, limits ? 1f : 0f);
            y += 30f;

            // balance badge: any game that pays out more than it takes in is a gift to the guests
            var losing = new List<string>();
            if (CS.SlotTargetRtp >= 1.0) losing.Add("HH.Casino.Game.Slots".Translate());
            if (CS.Roulette.TargetEdge <= 0) losing.Add("HH.Casino.Game.Roulette".Translate());
            if (BlackjackSolver.Get(CS.Blackjack, 0.6).HouseEdge <= 0) losing.Add("HH.Casino.Game.Blackjack".Translate());
            if (CS.PokerRakeRate <= 0) losing.Add("HH.Casino.Game.Poker".Translate());
            if (losing.Count > 0)
            {
                GUI.color = new Color(1f, 0.6f, 0.35f);
                Widgets.Label(new Rect(0, y, view.width, 24f), "HH.Casino.LosingWarning".Translate(string.Join(", ", losing)));
                GUI.color = Color.white; y += 28f;
            }
            else
            {
                GUI.color = new Color(0.6f, 0.85f, 0.6f);
                Widgets.Label(new Rect(0, y, view.width, 24f), "HH.Casino.BalancedBadge".Translate());
                GUI.color = Color.white; y += 28f;
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
                var row = new Rect(0, y, view.width, 24f);
                Widgets.Label(new Rect(0, y, 260f, 24f), h.Label ?? h.Account);
                GUI.color = cash.IsNegative ? new Color(1f, 0.5f, 0.4f) : Color.white;
                Widgets.Label(new Rect(260f, y, 200f, 24f), "HH.Casino.Drawer".Translate(cash.ToStringShort()));
                GUI.color = win >= 0 ? new Color(0.6f, 0.9f, 0.6f) : new Color(1f, 0.55f, 0.45f);
                Widgets.Label(new Rect(470f, y, 220f, 24f), "HH.Casino.TodayNet".Translate((win >= 0 ? "+" : "") + win.ToString("0.##")));
                GUI.color = Color.white;
                if (Mouse.IsOver(row)) TooltipHandler.TipRegion(row, "HH.Casino.HouseTip".Translate(c.OutstandingAt(h.Account).ToStringShort(), HSettings.GetSilver(CS.HouseFloat).ToStringShort()));
                y += 26f;
            }
            y += 6f;

            // RTP per game
            Section(ref y, view.width, "HH.Casino.Games".Translate());
            DrawGameHeader(ref y);
            foreach (CasinoGame g in Enum.GetValues(typeof(CasinoGame)))
            {
                var rows = c.Rows.Where(r => r.Game == (int)g).ToList();
                long staked = rows.Sum(r => r.StakedMicro), paid = rows.Sum(r => r.PaidMicro); int n = rows.Sum(r => r.Rounds);
                double exp = rows.Sum(r => r.Expected), varr = rows.Sum(r => r.Variance), win = (staked - paid) / (double)Money.PerSilver;
                double? z = n >= 30 && varr > 0 ? (win - exp) / Math.Sqrt(varr) : (double?)null;
                double actualRtp = staked > 0 ? paid / (double)staked : 0, expectedRtp = staked > 0 ? 1 - exp / (staked / (double)Money.PerSilver) : ExpectedRtp(g);
                DrawGameRow(ref y, g.ToString(), n, staked, paid, actualRtp, expectedRtp, z, win);
            }
            y += 8f;

            // gamblers
            Section(ref y, view.width, "HH.Casino.Gamblers".Translate());
            var gamblers = c.Records.Where(r => r.Pawn != null && !r.Pawn.Dead && r.Pawn.Faction == Faction.OfPlayer).ToList();
            if (gamblers.Count == 0) { GUI.color = Color.gray; Widgets.Label(new Rect(0, y, view.width, 24f), "HH.Casino.NoGamblers".Translate()); GUI.color = Color.white; y += 26f; }
            foreach (var r in gamblers)
            {
                var add = Gambling.AddictionOf(r.Pawn);
                Widgets.Label(new Rect(0, y, 200f, 24f), r.Pawn.LabelShortCap);
                string txt = Gambling.CounterText(r.Pawn) ?? "";
                if (add != null) GUI.color = new Color(1f, 0.6f, 0.45f);
                Widgets.Label(new Rect(200f, y, view.width - 200f, 24f), txt + (add != null ? "  · " + add.LabelInBrackets : ""));
                GUI.color = Color.white;
                y += 24f;
            }
            viewHeight = y + 10f;
            Widgets.EndScrollView();
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
            Widgets.Label(new Rect(0, y, w, 24f), "<b>" + title + "</b>"); y += 22f; Widgets.DrawLineHorizontal(0, y, w); y += 4f;
        }

        static readonly float[] cols = { 0, 130, 205, 285, 385, 495, 600 };
        static void DrawGameHeader(ref float y)
        {
            GUI.color = new Color(0.75f, 0.75f, 0.75f);
            string[] h = { "HH.Casino.ColGame", "HH.Casino.ColRounds", "HH.Casino.ColStaked", "HH.Casino.ColRtp", "HH.Casino.ColExpected", "HH.Casino.ColLuck", "HH.Casino.ColHouse" };
            for (int i = 0; i < h.Length; i++) Widgets.Label(new Rect(cols[i], y, (i + 1 < cols.Length ? cols[i + 1] - cols[i] : 110f) - 4f, 22f), h[i].Translate());
            GUI.color = Color.white; y += 22f;
        }
        static void DrawGameRow(ref float y, string game, int rounds, long staked, long paid, double rtp, double expected, double? z, double houseWin)
        {
            Widgets.Label(new Rect(cols[0], y, 126f, 22f), ("HH.Casino.Game." + game).Translate());
            Widgets.Label(new Rect(cols[1], y, 80f, 22f), rounds.ToString("N0"));
            Widgets.Label(new Rect(cols[2], y, 90f, 22f), (staked / (double)Money.PerSilver).ToString("N0"));
            Widgets.Label(new Rect(cols[3], y, 90f, 22f), staked > 0 ? (rtp * 100).ToString("0.0") + " %" : "–");
            Widgets.Label(new Rect(cols[4], y, 90f, 22f), (expected * 100).ToString("0.0") + " %");
            Widgets.Label(new Rect(cols[5], y, 110f, 22f), z == null ? "–" : z.Value.ToString("+0.0;-0.0") + " σ");
            GUI.color = houseWin >= 0 ? new Color(0.6f, 0.9f, 0.6f) : new Color(1f, 0.55f, 0.45f);
            Widgets.Label(new Rect(cols[6], y, 90f, 22f), (houseWin >= 0 ? "+" : "") + houseWin.ToString("0.#"));
            GUI.color = Color.white;
            y += 22f;
        }
    }
}
