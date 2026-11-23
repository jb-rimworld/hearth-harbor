// SPDX-License-Identifier: MIT
#if HH_TESTS
using System.Collections.Generic;
using Hearth.Shell;
using Hearth.Testing;
using RimWorld;
using Verse;

namespace Hearth
{
    /// <summary>The bank pages point to the real mod settings (F19): "Open settings" must open the window on the Bank page.</summary>
    public sealed class BankSettingsJumpSuite : HearthTestSuite
    {
        public override string Name => "bank.settingsjump";
        public override ModuleId Module => ModuleId.Bank;
        public override int MaxTicks => 3000;
        public override IEnumerable<int> Run(TestContext t)
        {
            HearthSettingsWindow.Install();
            HearthSettingsWindow.SelectedPage = "modules"; HearthSettingsWindow.Search = "abc";
            HearthMod.OpenSettings("mod:Bank");
            var dlg = Find.WindowStack.WindowOfType<Dialog_ModSettings>();
            if (dlg != null) { dlg.forcePause = false; dlg.absorbInputAroundWindow = false; }   // the test harness closes pausing windows
            yield return 5;
            t.Check(dlg != null, "the mod settings window opened");
            t.Check(HearthSettingsWindow.SelectedPage == "mod:Bank" && HearthSettingsWindow.Search == "", "it shows the Bank page without a search");
            t.Check(HearthSettingsWindow.BuildPages().Exists(p => p.Id == "mod:Bank"), "the Bank page exists");
            dlg?.Close(); yield return 2;
            HearthMod.OpenSettings("mod:DoesNotExist");
            var dlg2 = Find.WindowStack.WindowOfType<Dialog_ModSettings>();
            t.Check(dlg2 != null, "an unknown page does not break opening");
            if (dlg2 != null) { dlg2.forcePause = false; dlg2.absorbInputAroundWindow = false; }
            yield return 3;
            dlg2?.Close(); yield return 2;
            HearthSettingsWindow.SelectedPage = "modules";
        }
    }
}
#endif
