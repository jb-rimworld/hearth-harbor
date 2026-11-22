// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Hearth.Guests
{
    /// <summary>
    /// The departure report (5.14): first the result is rolled (points, renown), then a short, dry text is assembled
    /// from the stay's event log: header line, an opener by outcome (5 outcomes × 3 variants), one line per guest.
    /// </summary>
    public static class DepartureReport
    {
        public static readonly string[] Outcomes = { "great", "good", "fine", "poor", "bad" };

        static readonly Dictionary<string, string[]> openers = new Dictionary<string, string[]>
        {
            ["great"] = new[]
            {
                "The guests from {FACTION} left glowing. One of them hugged the doorframe.",
                "{FACTION} will be talking about this stay for a while, mostly in a good way.",
                "A flawless stay. {NAME} asked whether the {ROOM} could be booked again \"for life\".",
            },
            ["good"] = new[]
            {
                "The guests from {FACTION} left content and only slightly lighter in the purse.",
                "A pleasant stay. Nobody complained loudly enough to count.",
                "{NAME} called the {ROOM} \"honest\", which from {FACTION} is a compliment.",
            },
            ["fine"] = new[]
            {
                "The guests from {FACTION} left. It happened, and that is about all.",
                "A stay of the kind nobody writes home about. {NAME} wrote home anyway.",
                "{NAME} rated the {ROOM} \"a room\". Hard to argue.",
            },
            ["poor"] = new[]
            {
                "The guests from {FACTION} left early in spirit and on time in body.",
                "{NAME} described the {ROOM} with a word our clerk refused to write down.",
                "Not a disaster. Not a stay you would put on a sign, either.",
            },
            ["bad"] = new[]
            {
                "The guests from {FACTION} left with a list. It was not a shopping list.",
                "{NAME} promised to tell everyone about the {ROOM}. That is the problem.",
                "A stay to forget, if {FACTION} lets anyone forget it.",
            },
        };

        public static string OutcomeFromSurplus(float s) => s > 0.25f ? "great" : s > 0.05f ? "good" : s > -0.1f ? "fine" : s > -0.3f ? "poor" : "bad";

        public static int StarsFor(float value) => Mathf.Clamp(Mathf.RoundToInt(value * 5f), 1, 5);

        public static string GuestLine(GuestRecord r)
        {
            var sb = new StringBuilder();
            string name = r.Pawn?.LabelShort ?? "?";
            sb.Append(name).Append(": ");
            if (r.Bed != null || r.NightsPaid > 0)
                sb.Append("HH.Inn.Report.Room".TranslateOr("{0}, {1} night(s)").Replace("{0}", r.RoomLabel ?? "?").Replace("{1}", Math.Max(r.NightsPaid, r.NightsSlept).ToString()));
            else sb.Append("HH.Inn.Report.NoRoom".TranslateOr("day visit"));
            sb.Append(", ").Append("HH.Inn.Report.Spent".TranslateOr("spent {0} silver").Replace("{0}", r.Spent.WholeSilver.ToString()));
            sb.Append(" (").Append(r.PaysDigital ? "HH.Inn.Card".TranslateOr("card") : "HH.Inn.Cash".TranslateOr("cash")).Append(")");
            if (r.GoodwillPoints != 0) sb.Append(", ").Append("HH.Inn.Report.Points".TranslateOr("goodwill {0}").Replace("{0}", r.GoodwillPoints.ToString("+0;-0")));
            string highlight = Highlight(r);
            if (!highlight.NullOrEmpty()) sb.Append(". ").Append(highlight);
            return sb.ToString();
        }

        /// <summary>The most telling line of the stay: joined, insulted, the sleep verdict, an activity (priorities set when logged).</summary>
        static string Highlight(GuestRecord r)
        {
            if (r.Outcome == "joined") return "HH.Inn.Event.Joined".TranslateOr("Decided to stay and joined the colony.");
            return r.Highlight;
        }

        public static void Send(GuestGroup g, Map map)
        {
            var mode = HSettings.GetEnum<InnReportMode>(InnSettings.ReportMode);
            if (mode == InnReportMode.Off) return;
            var reg = GuestRegistry.Instance;
            string outcome = OutcomeFromSurplus(g.MeanSurplus);
            if (g.LeaveReason == "sentAway") outcome = "fine";
            var members = reg.Members(g).ToList();
            float vBar = members.Count > 0 ? members.Average(x => x.Value) : 0.5f;
            int stars = outcome == "great" ? 5 : outcome == "good" ? 4 : outcome == "fine" ? 3 : outcome == "poor" ? 2 : 1;
            string firstName = g.Names.FirstOrDefault() ?? "?";
            string room = members.Select(m => m.RoomLabel).FirstOrDefault(x => !x.NullOrEmpty()) ?? "HH.Inn.Report.Premises".TranslateOr("premises");

            var rng = JRand.Stream("inn.report");
            var arr = openers[outcome];
            int idx = rng.Range(0, arr.Length);
            string opener = ("HH.Inn.Report.Opener." + outcome + "." + idx).TranslateOr(arr[idx])
                .Replace("{FACTION}", g.Faction?.Name ?? "?").Replace("{NAME}", firstName).Replace("{ROOM}", room);

            var sb = new StringBuilder();
            sb.Append(new string('★', stars)).Append(new string('☆', 5 - stars));
            sb.Append(" · ").Append("HH.Inn.Report.SpentTotal".TranslateOr("Spent {0} silver").Replace("{0}", g.SpentTotal.WholeSilver.ToString()));
            sb.Append(" · ").Append("HH.Inn.Report.Goodwill".TranslateOr("Goodwill {0}").Replace("{0}", g.GoodwillTotal.ToString("+0;-0;0")));
            sb.Append(" · ").Append("HH.Inn.Report.Renown".TranslateOr("Renown {0}").Replace("{0}", g.RenownDelta.ToString("+0.0;-0.0;0.0")));
            sb.AppendLine().AppendLine();
            sb.AppendLine(opener);
            if (g.LeaveReason == "unhappy") sb.AppendLine("HH.Inn.Report.LeftEarly".TranslateOr("They left before their planned day."));
            else if (g.LeaveReason == "money") sb.AppendLine("HH.Inn.Report.LeftBroke".TranslateOr("Their money ran out before their stay did."));
            else if (g.LeaveReason == "sentAway") sb.AppendLine("HH.Inn.Report.SentAway".TranslateOr("You asked them to leave. They did, with the expression people use for that."));
            else if (g.LeaveReason == "temperature") sb.AppendLine("HH.Inn.Report.Temperature".TranslateOr("The weather sent them home early."));
            else if (g.LeaveReason == "wounded") sb.AppendLine("HH.Inn.Report.Wounded".TranslateOr("They carried their wounded home."));
            else if (g.LeaveReason == "hostile") sb.AppendLine("HH.Inn.Report.Hostile".TranslateOr("They left as enemies. No tip."));
            sb.AppendLine();
            foreach (var line in g.ReportLines) sb.Append("- ").AppendLine(line);

            string label = "HH.Inn.Report.Label".TranslateOr("Guests left: {0}").Replace("{0}", g.Faction?.Name ?? "?");
            var def = outcome == "great" || outcome == "good" ? LetterDefOf.PositiveEvent : outcome == "fine" ? LetterDefOf.NeutralEvent : LetterDefOf.NegativeEvent;
            var targets = members.Where(m => m.Pawn != null && m.Pawn.Spawned).Select(m => (TargetInfo)m.Pawn).ToList();
            if (mode == InnReportMode.Letter)
            {
                var letter = LetterMaker.MakeLetter(label, sb.ToString().TrimEnd(), def, targets.Count > 0 ? new LookTargets(targets) : LookTargets.Invalid, g.Faction);
                Find.LetterStack.ReceiveLetter(letter);
            }
            else
            {
                string shortText = label + ": " + sb.ToString().Split('\n')[0];
                Messages.Message(shortText, targets.Count > 0 ? new LookTargets(targets) : LookTargets.Invalid, def == LetterDefOf.NegativeEvent ? MessageTypeDefOf.NegativeEvent : MessageTypeDefOf.PositiveEvent, false);
            }
            LastReportText = sb.ToString();
        }

        /// <summary>For tests and the guest overview.</summary>
        public static string LastReportText;
    }
}
