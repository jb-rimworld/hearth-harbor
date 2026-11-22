// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Hearth.Underworld
{
    /// <summary>
    /// D04: the dark side is found in the game world. When the colony has guests or a venue and Whispers are low, a shady stranger asks
    /// "Nice place. Got a back room?" - Show me (opens the research and the buildings), Not interested, Never ask again.
    /// A closed dialog without an answer counts as "Not interested" but does not use up one of his asks.
    /// </summary>
    public static class Discovery
    {
        public static bool AskConditionsMet(Map map, out string why)
        {
            why = null;
            var comp = Underworld.Comp;
            if (comp == null) { why = "no game"; return false; }
            if (!Underworld.Visible) { why = "hidden (module off or Cozy)"; return false; }
            if (Underworld.Discovered) { why = "already discovered"; return false; }
            if (HSettings.GetEnum<UnderworldMode>("underworld.mode") == UnderworldMode.AlwaysOn) { why = "always-on mode"; return false; }
            if (comp.NeverAsk) { why = "never ask again"; return false; }
            if (comp.AskCount >= HSettings.GetInt("underworld.strangerMaxAsks")) { why = "asked enough"; return false; }
            int now = Find.TickManager.TicksGame;
            if (now - comp.LastAskTick < HSettings.GetInt("underworld.strangerCooldownDays") * 60000) { why = "cooldown"; return false; }
            if (comp.Whispers > HSettings.Get("underworld.strangerMaxWhispers")) { why = "whispers too high"; return false; }
            if (map == null || !map.IsPlayerHome) { why = "not a home map"; return false; }
            bool hosting = GuestApi.GuestsOn(map).Any() || Venues.All(map).Count > 0;
            if (!hosting) { why = "no guests or venue"; return false; }
            return true;
        }

        /// <summary>Called every 2500 ticks per home map. Rolls with a mean time between asks from the settings.</summary>
        public static void Roll(Map map)
        {
            var comp = Underworld.Comp; if (comp == null) return;
            if (!AskConditionsMet(map, out _)) { comp.StrangerFirstTick = -1; return; }
            // the stranger waits until the colony has been hosting for a couple of days
            if (comp.StrangerFirstTick < 0) comp.StrangerFirstTick = Find.TickManager.TicksGame;
            if (Find.TickManager.TicksGame - comp.StrangerFirstTick < 2 * 60000) return;
            double perCheck = 2500.0 / (60000.0 * Math.Max(0.25, HSettings.Get("underworld.strangerMeanDays")));
            if (!JRand.Stream("underworld.stranger").Chance(perCheck)) return;
            Fire(map, true);
        }

        static bool answered;

        /// <summary>Builds the dialog tree (also used by tests); opens it when <paramref name="open"/> is set.</summary>
        public static DiaNode Fire(Map map, bool open)
        {
            var node = new DiaNode("HH.Underworld.Stranger.Text".Translate());
            node.options.Add(new DiaOption("HH.Underworld.Stranger.Show".Translate()) { action = () => ShowMe(map), resolveTree = true });
            node.options.Add(new DiaOption("HH.Underworld.Stranger.No".Translate()) { action = () => NotInterested(), resolveTree = true });
            node.options.Add(new DiaOption("HH.Underworld.Stranger.Never".Translate()) { action = () => NeverAgain(), resolveTree = true });
            if (open)
            {
                var dlg = new Dialog_NodeTree(node, delayInteractivity: true, radioMode: false, title: "HH.Underworld.Stranger.Title".Translate());
                answered = false;
                dlg.closeAction = () => { if (!answered) Dismissed(); };
                Find.WindowStack.Add(dlg);
                HLog.Info("Underworld", "The stranger asks about a back room");
            }
            return node;
        }

        public static void ShowMe(Map map)
        {
            answered = true;
            if (Underworld.Comp == null || HearthProfile.Current == null) return;
            Unlock("Show me");
            Find.LetterStack.ReceiveLetter("HH.Underworld.Unlocked.Title".Translate(), "HH.Underworld.Unlocked.Text".Translate(), LetterDefOf.PositiveEvent);
        }

        /// <summary>Opens the dark side for this save: marks discovery and finishes the hidden key project so the real research shows up.</summary>
        public static void Unlock(string why)
        {
            var prof = HearthProfile.Current; if (prof == null) return;
            if (!prof.UnderworldDiscovered) HLog.Info("Underworld", "Underworld discovered (" + why + ")");
            prof.UnderworldDiscovered = true;
            var key = UwDefs.ResDiscovery;
            if (key != null && !key.IsFinished && Find.ResearchManager != null)
            {
                try { Find.ResearchManager.FinishProject(key, doCompletionDialog: false, researcher: null, doCompletionLetter: false); }
                catch (Exception e) { HLog.Error("Underworld", "Could not finish the discovery key project", e); }
            }
        }

        public static void NotInterested()
        {
            answered = true;
            var comp = Underworld.Comp; if (comp == null) return;
            comp.LastAskTick = Find.TickManager.TicksGame; comp.AskCount++;
            HLog.Info("Underworld", "Stranger refused (ask " + comp.AskCount + ")");
        }

        static void Dismissed()
        {
            var comp = Underworld.Comp; if (comp == null) return;
            // he is back in three days
            comp.LastAskTick = Find.TickManager.TicksGame - HSettings.GetInt("underworld.strangerCooldownDays") * 60000 + 3 * 60000;
        }

        public static void NeverAgain()
        {
            answered = true;
            var comp = Underworld.Comp; if (comp == null) return;
            comp.NeverAsk = true;
            HLog.Info("Underworld", "Stranger: never ask again");
        }

        /// <summary>In always-on mode the underworld is simply open (if the content allows it).</summary>
        public static void EnsureAlwaysOn()
        {
            if (!Underworld.Visible || Underworld.Discovered) return;
            if (HSettings.GetEnum<UnderworldMode>("underworld.mode") == UnderworldMode.AlwaysOn) Unlock("always-on mode");
        }
    }

    /// <summary>Incident wrapper so the stranger can be fired from the dev menu; the storyteller never rolls it (baseChance 0).</summary>
    public class IncidentWorker_UnderworldStranger : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms) => parms.target is Map m && Discovery.AskConditionsMet(m, out _);
        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            if (!(parms.target is Map m)) return false;
            Discovery.Fire(m, true);
            return true;
        }
    }

    /// <summary>Content warnings: Dark and Grim need one explicit confirmation per save; two more dialogs guard the first human hunt and the first organ clinic.</summary>
    public static class UwWarnings
    {
        static bool open;

        public static void Tick()
        {
            var comp = Underworld.Comp; if (comp == null || !Underworld.ModuleActive) return;
            if (Underworld.Intensity >= ContentIntensity.Dark && !comp.ContentWarningAck && !open && !Find.WindowStack.IsOpen<Dialog_NodeTree>())
                ContentWarning();
        }

        public static void ContentWarning()
        {
            var comp = Underworld.Comp; if (comp == null) return;
            bool grim = Underworld.Intensity >= ContentIntensity.Grim;
            var node = new DiaNode((grim ? "HH.Underworld.Warn.Grim" : "HH.Underworld.Warn.Dark").Translate());
            node.options.Add(new DiaOption("HH.Underworld.Warn.Accept".Translate()) { action = () => { comp.ContentWarningAck = true; open = false; HLog.Info("Underworld", "Content warning confirmed (" + Underworld.Intensity + ")"); }, resolveTree = true });
            node.options.Add(new DiaOption("HH.Underworld.Warn.Back".Translate()) { action = () => { var p = HearthProfile.Current; if (p != null) p.Content = ContentIntensity.Standard; open = false; HLog.Info("Underworld", "Content set back to Standard"); }, resolveTree = true });
            var dlg = new Dialog_NodeTree(node, true, false, "HH.Underworld.Warn.Title".Translate());
            dlg.closeAction = () => open = false;
            open = true;
            Find.WindowStack.Add(dlg);
        }

        /// <summary>Second warning: the first hunting ground with a human quarry or the first organ clinic. onKeep/onRemove run after the answer.</summary>
        public static void FirstBuildWarning(bool hunt, Action onKeep, Action onRemove)
        {
            var comp = Underworld.Comp; if (comp == null) { onKeep?.Invoke(); return; }
            if (hunt ? comp.HuntWarningAck : comp.OrganWarningAck) { onKeep?.Invoke(); return; }
            var node = new DiaNode((hunt ? "HH.Underworld.Warn.Hunt" : "HH.Underworld.Warn.Organ").Translate());
            node.options.Add(new DiaOption("HH.Underworld.Warn.Keep".Translate()) { action = () => { if (hunt) comp.HuntWarningAck = true; else comp.OrganWarningAck = true; onKeep?.Invoke(); }, resolveTree = true });
            node.options.Add(new DiaOption("HH.Underworld.Warn.Remove".Translate()) { action = () => onRemove?.Invoke(), resolveTree = true });
            Find.WindowStack.Add(new Dialog_NodeTree(node, true, false, "HH.Underworld.Warn.Title".Translate()));
        }
    }
}
