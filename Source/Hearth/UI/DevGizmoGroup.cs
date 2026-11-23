// SPDX-License-Identifier: MIT
// F10 (1.1.0): in dev mode our own "DEV: …" buttons (casino simulation, fence loot …) sit behind ONE "Dev: Hearth & Harbor" button.
// Pure UI regrouping: vanilla's and other mods' gizmos are never touched, nothing changes for players without dev mode.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Hearth.Guests;

namespace Hearth.Shell
{
    public static class DevGizmos
    {
        /// <summary>Our own dev buttons are recognisable by this label prefix (set by the comps; vanilla uses the same prefix, so we only group the ones OUR comps yielded).</summary>
        public const string Prefix = "DEV:";
        static readonly ConditionalWeakTable<Gizmo, object> ours = new ConditionalWeakTable<Gizmo, object>();

        public static bool Enabled => Prefs.DevMode && Modules.On(ModuleId.Core) && HSettings.GetBool(InnSettings.GroupDevGizmos);

        public static bool IsDevLabel(Command c) => c?.defaultLabel != null && c.defaultLabel.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);
        public static bool IsOurs(Gizmo g) => g != null && ours.TryGetValue(g, out _);

        /// <summary>Remembers which dev buttons came out of one of OUR comps.</summary>
        public static IEnumerable<Gizmo> Mark(IEnumerable<Gizmo> src)
        {
            foreach (var g in src)
            {
                if (g is Command c && IsDevLabel(c)) { try { if (!ours.TryGetValue(g, out _)) ours.Add(g, null); } catch (Exception) { } }
                yield return g;
            }
        }

        /// <summary>Folds the marked dev buttons of one thing into a single group button (the rest stays in order).</summary>
        public static IEnumerable<Gizmo> Fold(IEnumerable<Gizmo> src)
        {
            List<Command> dev = null; var rest = new List<Gizmo>();
            foreach (var g in src)
            {
                if (g is Command c && IsOurs(g)) { (dev ?? (dev = new List<Command>())).Add(c); continue; }
                rest.Add(g);
            }
            if (dev == null) return rest;
            rest.Add(new Command_HH_DevGroup(dev));
            return rest;
        }
    }

    /// <summary>The one button that stands for all our dev buttons of a thing; a click lists them.</summary>
    public sealed class Command_HH_DevGroup : Command_Action
    {
        readonly List<Command> inner;
        public Command_HH_DevGroup(List<Command> inner)
        {
            this.inner = inner;
            defaultLabel = "HH.UI.DevGroup.Label".TranslateOr("Dev: Hearth & Harbor");
            defaultDesc = "HH.UI.DevGroup.Desc".TranslateOr("Dev-mode tools of Hearth & Harbor for this building ({0}). Only shown in dev mode; switch the grouping off in the Inn settings.").Replace("{0}", inner.Count.ToString());
            icon = InnTex.DevTools;
            Order = 900f;
            action = Open;
        }
        public override bool GroupsWith(Gizmo other) => other is Command_HH_DevGroup;

        void Open()
        {
            var opts = new List<FloatMenuOption>();
            foreach (var c in inner)
            {
                var cmd = c;
                string label = (cmd.defaultLabel ?? "?").Substring(cmd.defaultLabel != null && cmd.defaultLabel.StartsWith(DevGizmos.Prefix, StringComparison.OrdinalIgnoreCase) ? DevGizmos.Prefix.Length : 0).Trim();
                if (cmd.Disabled) opts.Add(new FloatMenuOption(label + (cmd.disabledReason.NullOrEmpty() ? "" : " (" + cmd.disabledReason + ")"), null));
                else opts.Add(new FloatMenuOption(label, () => { try { cmd.ProcessInput(Event.current); } catch (Exception e) { HLog.Error("UI", "Dev button failed: " + label, e); } }));
            }
            Find.WindowStack.Add(new FloatMenu(opts));
        }
    }

    /// <summary>Marks the DEV buttons yielded by our own comps (every ThingComp subclass of Hearth.dll that overrides CompGetGizmosExtra).</summary>
    [HarmonyPatch]
    static class Patch_DevGizmos_Mark
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var asm = typeof(Patch_DevGizmos_Mark).Assembly;
            foreach (var t in asm.GetTypes())
            {
                if (!typeof(ThingComp).IsAssignableFrom(t) || t.IsAbstract) continue;
                var m = t.GetMethod("CompGetGizmosExtra", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (m != null && m.ReturnType == typeof(IEnumerable<Gizmo>)) yield return m;
            }
        }
        static void Postfix(ref IEnumerable<Gizmo> __result)
        {
            if (!Prefs.DevMode || __result == null) return;
            __result = DevGizmos.Mark(__result);
        }
    }

    /// <summary>Folds the marked buttons into the group button. Dev mode only: one static bool check otherwise.</summary>
    [HarmonyPatch(typeof(ThingWithComps), nameof(ThingWithComps.GetGizmos))]
    static class Patch_DevGizmos_Fold
    {
        static void Postfix(ref IEnumerable<Gizmo> __result)
        {
            if (!Prefs.DevMode || __result == null || !DevGizmos.Enabled) return;
            __result = DevGizmos.Fold(__result);
        }
    }
}
