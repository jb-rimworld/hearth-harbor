// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Hearth.Compat.Hospitality
{
    /// <summary>
    /// Main-menu dialog (KONZEPT 12.6 step 3/4): "Hospitality is active alongside Hearth &amp; Harbor. Disable it for you?"
    /// Yes → ModsConfig.SetActive(id,false) for each listed mod, ModsConfig.Save(), GenCommandLine.Restart().
    /// Later → ask again next start. Don't ask again → setting compat.hospitalityNeverAsk. If disabling fails, the same
    /// window shows three manual steps.
    /// </summary>
    public sealed class Dialog_HospitalityTakeover : Window
    {
        readonly List<DetectedMod> offered;
        readonly List<DetectedMod> mentioned;
        string failure;
        Vector2 scroll;

        public override Vector2 InitialSize => HUI.Size(760f, 600f, 640f, 500f, 0.92f, 0.92f);

        public Dialog_HospitalityTakeover()
        {
            offered = HospitalityFamily.OfferedForDisabling;
            mentioned = HospitalityFamily.DetectedWithRole(FamilyRole.MentionOnly).ToList();
            doCloseX = false;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = true;
            forcePause = true;
            closeOnAccept = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            float y = inRect.y;
            Text.Font = GameFont.Medium;
            string title = "HH.Compat.DialogTitle".TranslateOr("Hospitality is active alongside Hearth & Harbor. Disable it for you?");
            float th = Text.CalcHeight(title, inRect.width);
            Widgets.Label(new Rect(inRect.x, y, inRect.width, th), title);
            y += th + 8f;
            Text.Font = GameFont.Small;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("HH.Compat.DialogBody".TranslateOr("Hearth & Harbor replaces Hospitality and its add-ons. While both are active, Hearth & Harbor has switched Hospitality off for this session: its patches, events, buildings and guest tab do nothing. Your Hospitality guest beds and areas are kept and taken over when Hospitality is gone."));
            sb.AppendLine();
            sb.AppendLine("HH.Compat.DialogListHeader".TranslateOr("These mods will be disabled:"));
            foreach (var d in offered) sb.AppendLine("  • " + d.Label + "  (" + d.Entry.PackageId + ")");
            if (mentioned.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("HH.Compat.DialogMentionHeader".TranslateOr("These guest mods stay as they are; their visitors remain theirs:"));
                foreach (var d in mentioned) sb.AppendLine("  • " + d.Label);
            }
            if (failure != null)
            {
                sb.AppendLine();
                sb.AppendLine("HH.Compat.DialogFailed".TranslateOr("The mod list could not be changed automatically ({0}). Please do it by hand:").Replace("{0}", failure));
                sb.AppendLine("HH.Compat.DialogStep1".TranslateOr("1. Main menu → Mods"));
                sb.AppendLine("HH.Compat.DialogStep2".TranslateOr("2. Untick the mods listed above"));
                sb.AppendLine("HH.Compat.DialogStep3".TranslateOr("3. Save the mod list and restart the game"));
            }
            string body = sb.ToString().TrimEnd();
            // buttons: three in a row when every label fits a third of the width, stacked otherwise
            string yesLabel = failure == null ? "HH.Compat.Yes".TranslateOr("Yes, disable and restart") : "HH.Compat.OpenMods".TranslateOr("Open the mod list");
            string laterLabel = "HH.Compat.Later".TranslateOr("Later"), neverLabel = "HH.Compat.Never".TranslateOr("Don't ask again");
            float third = (inRect.width - 20f) / 3f;
            bool stacked = Mathf.Max(HUI.TextW(yesLabel), Mathf.Max(HUI.TextW(laterLabel), HUI.TextW(neverLabel))) + 24f > third;
            float bh2 = 40f, buttonsH = stacked ? 3f * bh2 + 2f * 6f : bh2;
            var outer = new Rect(inRect.x, y, inRect.width, inRect.yMax - y - buttonsH - 10f);
            float bh = Text.CalcHeight(body, outer.width - 16f);
            var view = new Rect(0f, 0f, outer.width - 16f, bh);
            Widgets.BeginScrollView(outer, ref scroll, view);
            Widgets.Label(view, body);
            Widgets.EndScrollView();

            Rect Btn(int i) => stacked
                ? new Rect(inRect.x, inRect.yMax - buttonsH + i * (bh2 + 6f), inRect.width, bh2)
                : new Rect(inRect.x + i * (third + 10f), inRect.yMax - bh2, third, bh2);
            if (failure == null)
            {
                if (Widgets.ButtonText(Btn(0), yesLabel))
                {
                    if (HospitalityDisabler.TryDisable(offered.Select(d => d.Entry.PackageId).ToList(), out failure)) HospitalityDisabler.Restart();
                }
            }
            else if (Widgets.ButtonText(Btn(0), yesLabel))
            {
                Close();
                try { Find.WindowStack.Add(new Page_ModsConfig()); } catch (Exception e) { HLog.Warn("Compat", "Could not open the mod list: " + e.Message); }
            }
            if (Widgets.ButtonText(Btn(1), laterLabel))
            {
                HLog.Info("Compat", "Hospitality dialog: player chose Later (takeover stays active, asked again next start)");
                Close();
            }
            if (Widgets.ButtonText(Btn(2), neverLabel))
            {
                HospitalityDisabler.SetNeverAsk(true);
                HLog.Info("Compat", "Hospitality dialog: player chose Don't ask again (takeover stays active)");
                Close();
            }
        }
    }

    public static class HospitalityDisabler
    {
        public const string NeverAskKey = "compat.hospitalityNeverAsk";
        public static bool DialogOffered { get; private set; }
        public static bool DialogShown { get; private set; }

        public static bool NeverAsk => HSettings.GetBool(NeverAskKey);

        public static void SetNeverAsk(bool v)
        {
            HSettings.Set(NeverAskKey, v ? 1f : 0f);
            try { HearthMod.Instance?.WriteSettings(); } catch (Exception e) { HLog.Warn("Compat", "Could not write settings: " + e.Message); }
        }

        /// <summary>Queues the dialog for the main menu when the takeover is active and the player has not said "never".</summary>
        public static void OfferAtMainMenu()
        {
            if (!HospitalityTakeover.Active || NeverAsk) return;
            DialogOffered = true;
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HH_TEST"))) { HLog.Info("Compat", "Hospitality dialog suppressed in test run"); return; }
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                try
                {
                    if (Current.ProgramState != ProgramState.Entry) return;
                    Find.WindowStack.Add(new Dialog_HospitalityTakeover());
                    DialogShown = true;
                }
                catch (Exception e) { HLog.Error("Compat", "Could not show the Hospitality dialog", e); }
            });
        }

        /// <summary>Deactivates the mods and saves ModsConfig.xml. Verifies the result; on failure returns the reason.</summary>
        public static bool TryDisable(List<string> packageIds, out string failure)
        {
            failure = null;
            try
            {
                if (AccessTools.Field(typeof(ModsConfig), "commandLineModList")?.GetValue(null) is bool cl && cl)
                { failure = "the mod list came from the command line"; return false; }
                foreach (var id in packageIds) ModsConfig.SetActive(id, false);
                ModsConfig.Save();
                var still = packageIds.Where(ModsConfig.IsActive).ToList();
                if (still.Count > 0) { failure = "still active: " + string.Join(", ", still); return false; }
                string path = GenFilePaths.ModsConfigFilePath;
                if (File.Exists(path))
                {
                    string xml = File.ReadAllText(path).ToLowerInvariant();
                    var inFile = packageIds.Where(id => xml.Contains("<li>" + id.ToLowerInvariant() + "</li>")).ToList();
                    if (inFile.Count > 0) { failure = "ModsConfig.xml still lists " + string.Join(", ", inFile); return false; }
                }
                HLog.Info("Compat", "Disabled " + string.Join(", ", packageIds) + " and saved the mod list");
                return true;
            }
            catch (Exception e)
            {
                failure = e.GetType().Name + ": " + e.Message;
                HLog.Error("Compat", "Disabling the Hospitality family failed", e);
                return false;
            }
        }

        public static void Restart()
        {
            HLog.Info("Compat", "Restarting the game after disabling the Hospitality family");
            GenCommandLine.Restart();
        }
    }
}
