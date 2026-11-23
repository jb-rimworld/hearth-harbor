// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Hearth.Guests
{
    /// <summary>
    /// What the Inn module adds to the shell's Guests page and Guest tab (10.4, 10.5): the page header (renown stars, arrival forecast,
    /// wing status, door policy), the read-only state of leaving guests, recruitment progress and actionable "why unhappy" lines.
    /// There is one Guests page (the shell's); this class only fills its hooks (B1, C1).
    /// </summary>
    public static class GuestOverviewPage
    {
        public static void Register()
        {
            Hearth.Shell.HearthUI.GuestsHeader = r => Guard.Run(ModuleId.Inn, "Inn.GuestsHeader", () => DrawHeader(r), 0f);
            Hearth.Shell.HearthUI.GuestStatus = p => Modules.On(ModuleId.Inn) ? Guard.Run(ModuleId.Inn, "Inn.GuestStatus", () => StatusFor(p), null) : null;
            Hearth.Shell.HearthUI.TopComplaint = m => Modules.On(ModuleId.Inn) ? Guard.Run(ModuleId.Inn, "Inn.TopComplaint", () => TopComplaint(m), null) : null;
        }

        // ------------------------------------------------------------------ header of the Guests page
        /// <summary>Draws the Inn header into r and returns the height used.</summary>
        public static float DrawHeader(Rect r)
        {
            var reg = GuestRegistry.Instance; var map = Find.CurrentMap;
            if (reg == null || map == null || !Modules.On(ModuleId.Inn)) return 0f;
            float y = r.y, w = r.width;
            Text.Font = GameFont.Small;
            string summary = GuestRegistry.StarsText() + "   " + "HH.Inn.Tab.Summary".TranslateOr("Renown {0} · Whispers {1} · {2} groups hosted · {3} guest beds ({4} free)")
                .Replace("{0}", reg.Renown.ToString("0.0")).Replace("{1}", reg.CurrentWhispers.ToString("0.0")).Replace("{2}", reg.GroupsHosted.ToString())
                .Replace("{3}", GuestBeds.CountOn(map).ToString()).Replace("{4}", GuestBeds.FreeOn(map).Count.ToString());
            var btn = new Rect(r.xMax - 150f, y, 150f, 26f);
            Widgets.Label(new Rect(r.x, y + 2f, w - 160f, 24f), summary.Truncate(w - 160f));
            if (Widgets.ButtonText(btn, "HH.Inn.Door.Gizmo".TranslateOr("Door policy"))) DoorPolicyHub.Open();
            if (!reg.Door.IsDefault) TooltipHandler.TipRegion(btn, reg.Door.AllowAll ? "HH.Inn.Door.Everyone".TranslateOr("Everyone") : string.Join(", ", reg.Door.Restricting()));
            y += 28f;
            GUI.color = new Color(0.75f, 0.75f, 0.75f);
            foreach (var line in WingStatus.Lines(map))
            {
                float h = Text.CalcHeight(line, w);
                Widgets.Label(new Rect(r.x, y, w, h), line); y += h;
            }
            var sch = map.GetComponent<MapComponent_HH_GuestScheduler>();
            string forecast;
            if (sch == null || !sch.Active) forecast = "HH.Inn.Tab.Inactive".TranslateOr("Nobody is coming: mark a bed as guest bed or open a venue.");
            else
            {
                var next = sch.GetForecast(HSettings.GetInt(InnSettings.ForecastDays));
                forecast = next.Count == 0 ? "HH.Inn.Tab.NoneYet".TranslateOr("No groups planned yet.")
                    : string.Join("   ·   ", next.Take(3).Select(g => g.Describe(map))) + (next.Count > 3 ? "  (+" + (next.Count - 3) + ")" : "");
            }
            string fl = "HH.Inn.Tab.Forecast".TranslateOr("Expected arrivals") + ": " + forecast;
            float fh = Text.CalcHeight(fl, w);
            Widgets.Label(new Rect(r.x, y, w, fh), fl); y += fh;
            GUI.color = Color.white;
            return y - r.y + 4f;
        }

        // ------------------------------------------------------------------ guest tab status
        public static Hearth.Shell.HearthUI.GuestTabStatus StatusFor(Pawn p)
        {
            var reg = GuestRegistry.Instance; var r = reg?.Get(p);
            if (r == null) return null;
            var st = new Hearth.Shell.HearthUI.GuestTabStatus { ReadOnly = r.Settled, WingLabel = GuestWings.WingLabelFor(p) };
            if (r.Settled) st.StateLine = LeavingLine(r, reg.Group(r.GroupId));
            if (r.Handling == HandlingMode.Recruit && p.MapHeld != null) RecruitInfo(r, p, st);
            else if (r.Handling == HandlingMode.Convert) ConvertInfo(r, p, st);
            if (!r.Settled) foreach (var pr in Problems(r)) st.Problems.Add(pr.line);
            return st;
        }

        public static string LeavingLine(GuestRecord r, GuestGroup g)
        {
            string why;
            switch (g?.LeaveReason)
            {
                case "temperature": why = "HH.Inn.Leaving.Temperature".TranslateOr("Leaving early: the temperature was dangerous"); break;
                case "unhappy": why = "HH.Inn.Leaving.Unhappy".TranslateOr("Leaving early: unhappy with the stay"); break;
                case "money": why = "HH.Inn.Leaving.Money".TranslateOr("Leaving early: the money ran out"); break;
                case "sentAway": why = "HH.Inn.Leaving.SentAway".TranslateOr("Sent away"); break;
                case "wounded": why = "HH.Inn.Leaving.Wounded".TranslateOr("Leaving early: carrying the wounded home"); break;
                case "hostile": why = "HH.Inn.Leaving.Hostile".TranslateOr("Leaving as an enemy"); break;
                case "closing": why = "HH.Inn.Leaving.Closing".TranslateOr("Going home: the venues are closing"); break;
                case "removal": why = "HH.Inn.Leaving.Removal".TranslateOr("Going home: the Inn module was switched off"); break;
                default: why = "HH.Inn.Leaving.Planned".TranslateOr("Checked out"); break;
            }
            return why + " · " + "HH.Inn.Leaving.Paid".TranslateOr("paid {0} silver").Replace("{0}", r.Spent.WholeSilver.ToString());
        }

        static void RecruitInfo(GuestRecord r, Pawn p, Hearth.Shell.HearthUI.GuestTabStatus st)
        {
            int bond = GuestRegistry.Bond(p, p.MapHeld, out int thr);
            st.RecruitProgress = thr <= 0 ? 1f : Mathf.Clamp01(bond / (float)thr);
            string line = "HH.Inn.Handling.Bond".TranslateOr("Bond: {0} / {1}").Replace("{0}", bond.ToString()).Replace("{1}", thr.ToString());
            if (r.Insulted) line += " · " + "HH.Inn.Recruit.Insulted".TranslateOr("felt pestered: will not join this time");
            else if (GuestRegistry.TooImportantToJoin(p)) line += " · " + "HH.Inn.Recruit.TooImportant".TranslateOr("too important at home to join");
            else if (bond >= thr)
                line += " · " + "HH.Inn.Recruit.Chance".TranslateOr("may join at check-out (about {0} %)").Replace("{0}", (GuestRegistry.RecruitChance(r.Value - r.Expectation, bond, thr) * 100f).ToString("0"));
            else line += " · " + "HH.Inn.Recruit.NotYet".TranslateOr("not close enough yet");
            if (p.Faction != null && !p.Faction.IsPlayer)
                line += " · " + "HH.Inn.Recruit.Cost".TranslateOr("costs {0} goodwill with {1}").Replace("{0}", GuestRegistry.RecruitGoodwillCost(p, GuestRegistry.LikelyRecruiter(p, p.MapHeld)).ToString()).Replace("{1}", p.Faction.Name);
            st.RecruitLine = line;
        }

        /// <summary>F04: the progress bar of the Convert handling is how far the guest's certainty has fallen (full = ready to take on the colony's ideoligion).</summary>
        static void ConvertInfo(GuestRecord r, Pawn p, Hearth.Shell.HearthUI.GuestTabStatus st)
        {
            string line = GuestRegistry.ConvertLine(p);
            if (line == null) { st.RecruitLine = "HH.Inn.Convert.Nothing".TranslateOr("Nothing to convert: the guest already shares your ideoligion or cannot be talked to."); return; }
            st.RecruitProgress = Mathf.Clamp01(1f - p.ideo.Certainty);
            if (r.Insulted) line += " · " + "HH.Inn.Convert.Insulted".TranslateOr("felt preached at: will not listen this time");
            st.RecruitLine = line;
        }

        // ------------------------------------------------------------------ why unhappy (C10)
        /// <summary>Actionable problems of one staying guest: (key, line for the guest, line for "n guests …").</summary>
        public static IEnumerable<(string key, string line, string many)> Problems(GuestRecord r)
        {
            var p = r.Pawn;
            if (p == null || !p.Spawned || p.Dead) yield break;
            // temperature first: it sends whole groups home
            var range = p.ComfortableTemperatureRange();
            float temp = p.AmbientTemperature;
            if (temp > range.max + 2f || HasHediff(p, "Heatstroke"))
                yield return ("hot", "HH.Inn.Problem.Hot".TranslateOr("Too hot ({0}): cool the guest wing with coolers.").Replace("{0}", temp.ToStringTemperature("F0")),
                    "HH.Inn.Problem.HotMany".TranslateOr("{0} guests are too hot: cool the guest wing with coolers."));
            else if (temp < range.min - 2f || HasHediff(p, "Hypothermia"))
                yield return ("cold", "HH.Inn.Problem.Cold".TranslateOr("Too cold ({0}): heat the guest wing.").Replace("{0}", temp.ToStringTemperature("F0")),
                    "HH.Inn.Problem.ColdMany".TranslateOr("{0} guests are too cold: heat the guest wing."));
            var food = p.needs?.food;
            if (food != null && food.CurLevelPercentage < 0.25f)
                yield return ("hungry", "HH.Inn.Problem.Hungry".TranslateOr("Hungry: stock a restaurant or tavern with meals (open, staffed)."),
                    "HH.Inn.Problem.HungryMany".TranslateOr("{0} guests are hungry: stock a restaurant or tavern with meals."));
            if (r.Overnight && r.CheckedIn && r.Bed == null)
                yield return ("nobed", "HH.Inn.Problem.NoBed".TranslateOr("Has no bed: mark another guest bed, or make one cheaper."),
                    "HH.Inn.Problem.NoBedMany".TranslateOr("{0} guests have no bed: mark more guest beds or make them cheaper."));
            if (r.Bed != null && r.NightPrice > GuestEconomy.ComfortPrice(r.Wealth))
                yield return ("pricey", "HH.Inn.Problem.Pricey".TranslateOr("Finds {0} silver a night steep: give the bed a lower price (−25 %) or a better room.").Replace("{0}", r.NightPrice.WholeSilver.ToString()),
                    "HH.Inn.Problem.PriceyMany".TranslateOr("{0} guests find their rooms too expensive: lower bed prices or improve the rooms."));
            // F02: a couple that would like one double bed but has none (and no free one exists)
            if (r.WantsDouble && r.Overnight && r.CheckedIn && HSettings.GetBool(InnSettings.CoupleWish) && p.MapHeld != null
                && !GuestRegistry.SharesBed(r, GuestRegistry.Instance?.FindPartner(r)) && !GuestBeds.FreeOn(p.MapHeld).Any(b => b.SleepingSlotsCount >= 2))
                yield return ("double", "HH.Inn.Problem.Double".TranslateOr("Would like a double bed together with their partner: build or mark one as a guest bed."),
                    "HH.Inn.Problem.DoubleMany".TranslateOr("{0} guests would like a double bed for the couple: build or mark one as a guest bed."));
            var joy = p.needs?.joy;
            if (joy != null && joy.CurLevelPercentage < 0.2f && r.CheckedIn)
                yield return ("bored", "HH.Inn.Problem.Bored".TranslateOr("Bored: add recreation to the guest wing (TV, chess, a venue)."),
                    "HH.Inn.Problem.BoredMany".TranslateOr("{0} guests are bored: add recreation to the guest wing."));
            if (r.Events.Any(e => e.Contains("Nobody came")))
                yield return ("waited", "HH.Inn.Problem.Waited".TranslateOr("Waited in vain for service: give your venues staff (Serving work)."),
                    "HH.Inn.Problem.WaitedMany".TranslateOr("{0} guests waited in vain: give your venues staff (Serving work)."));
            if (r.Insulted)
                yield return ("insulted", "HH.Inn.Problem.Insulted".TranslateOr("Felt pestered by recruiting: switch the handling to Accompany."),
                    "HH.Inn.Problem.InsultedMany".TranslateOr("{0} guests felt pestered: recruit less pushily."));
            if (r.CheckedIn && r.Budget < Money.FromSilver(8) && r.PlannedLeaveTick - Find.TickManager.TicksGame > 30000)
                yield return ("broke", "HH.Inn.Problem.Broke".TranslateOr("Almost out of money: will leave early."),
                    "HH.Inn.Problem.BrokeMany".TranslateOr("{0} guests are almost out of money."));
            var mood = p.needs?.mood;
            if (mood != null && mood.CurLevelPercentage < 0.3f)
            {
                var worst = new List<Thought>();
                try { mood.thoughts.GetAllMoodThoughts(worst); } catch { }
                var bad = worst.Where(t => t.MoodOffset() < 0f).OrderBy(t => t.MoodOffset()).Select(t => t.LabelCap.ToString()).Distinct().Take(2).ToList();
                if (bad.Count > 0)
                    yield return ("mood", "HH.Inn.Problem.Mood".TranslateOr("In a bad mood: {0}.").Replace("{0}", string.Join(", ", bad)),
                        "HH.Inn.Problem.MoodMany".TranslateOr("{0} guests are in a bad mood (see their Guest tab)."));
            }
        }

        static bool HasHediff(Pawn p, string defName)
        {
            var def = DefDatabase<HediffDef>.GetNamedSilentFail(defName);
            return def != null && p.health?.hediffSet?.GetFirstHediffOfDef(def) is Hediff h && h.Severity > 0.04f;
        }

        /// <summary>The most common problem of the guests on this map as one line ("3 guests are too hot: …"), or null when all is well.</summary>
        public static string TopComplaint(Map map)
        {
            var reg = GuestRegistry.Instance; if (reg == null || map == null) return null;
            var counts = new Dictionary<string, (int n, string line, string many)>();
            foreach (var r in reg.OnMap(map))
            {
                if (r.Settled) continue;
                foreach (var pr in Problems(r))
                {
                    counts.TryGetValue(pr.key, out var c);
                    counts[pr.key] = (c.n + 1, pr.line, pr.many);
                }
            }
            if (counts.Count == 0) return null;
            var top = counts.OrderByDescending(kv => kv.Value.n).First().Value;
            return top.n == 1 ? (reg.OnMap(map).FirstOrDefault(x => !x.Settled && Problems(x).Any(pp => pp.line == top.line))?.Pawn?.LabelShortCap + ": " + top.line) : top.many.Replace("{0}", top.n.ToString());
        }
    }
}
