// SPDX-License-Identifier: MIT
using System;
using UnityEngine;
using Verse;

namespace Hearth.Shell
{
    /// <summary>
    /// F08: the full Hearth &amp; Harbor settings window, drawn against the scratch profile of a scenario's rules part ("More settings…" in the
    /// scenario editor). Same pages and rows as the mod settings; only what the player changes ends up in the scenario.
    /// </summary>
    public sealed class Dialog_HHProfileEditor : Window
    {
        readonly HearthProfile scratch;
        readonly Action onClose;

        public Dialog_HHProfileEditor(HearthProfile scratch, Action onClose)
        {
            this.scratch = scratch; this.onClose = onClose;
            doCloseX = true; doCloseButton = true; forcePause = true; absorbInputAroundWindow = true; closeOnClickedOutside = false;
            optionalTitle = "HH.Scen.Editor.Title".TranslateOr("Hearth & Harbor rules of this scenario");
        }

        public override Vector2 InitialSize => HUI.Size(1000f, 720f, 760f, 520f);

        public override void DoWindowContents(Rect inRect)
        {
            var r = new Rect(inRect.x, inRect.y, inRect.width, inRect.height - CloseButSize.y - 10f);
            HearthSettingsWindow.DrawForScenario(r, scratch);
        }

        public override void PostClose()
        {
            base.PostClose();
            Guard.Run(ModuleId.Core, "Scenario.EditorClose", () => onClose?.Invoke());
        }
    }
}
