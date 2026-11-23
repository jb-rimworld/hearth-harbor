// SPDX-License-Identifier: MIT
using Silverline;
using UnityEngine;
using Verse;

namespace Hearth.Casino.PlayerHouse
{
    /// <summary>The Lucky Thrumbo on the Casino page of the main tab: a quiet card with one button. The page also shows up with only a bank account (no casino building needed).</summary>
    public static class ThrumboCard
    {
        public static bool Available => BankApi.Ready;

        /// <summary>Draws the card at <paramref name="y"/> and moves y below it.</summary>
        public static void Draw(ref float y, float width)
        {
            var r = new Rect(0, y, width, 92f);
            Widgets.DrawBoxSolid(r, new Color(0.12f, 0.15f, 0.2f, 0.55f)); Widgets.DrawBox(r, 1);
            GUI.DrawTexture(new Rect(r.x + 6f, r.y + 6f, 240f, 40f), ThrumboIcons.Logo, ScaleMode.ScaleToFit);
            var oldColor = GUI.color;
            Widgets.Label(new Rect(r.x + 256f, r.y + 6f, r.width - 256f - 150f, 24f), "<b>" + "HH.Thrumbo.Title".Translate() + "</b>");
            Text.Font = GameFont.Tiny; GUI.color = new Color(0.78f, 0.78f, 0.82f);
            Widgets.Label(new Rect(r.x + 256f, r.y + 28f, r.width - 256f - 150f, r.height - 32f), "HH.Thrumbo.Card.Text".Translate());
            Widgets.Label(new Rect(r.x + 8f, r.y + 50f, 240f, 40f), "HH.Thrumbo.Card.Totals".Translate(Summary()));
            GUI.color = oldColor; Text.Font = GameFont.Small;
            if (Widgets.ButtonText(new Rect(r.xMax - 140f, r.y + 28f, 130f, 34f), "HH.Thrumbo.Card.Visit".Translate())) Window_Thrumbo.Open(null);
            y += 100f;
        }

        static string Summary()
        {
            var c = ThrumboComponent.Get();
            if (c == null || c.RoundsAll == 0) return "HH.Thrumbo.NoHistory".Translate();
            double net = (c.PaidAll - c.StakedAll) / (double)Money.PerSilver;
            return "HH.Thrumbo.Card.Net".Translate(c.RoundsAll.ToString("N0"), (net >= 0 ? "+" : "") + net.ToString("N0"));
        }
    }
}
