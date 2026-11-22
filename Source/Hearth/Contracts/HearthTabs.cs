// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Hearth
{
    /// <summary>
    /// Pages of the "Hearth &amp; Harbor" main tab (10.1). Modules register pages in OnDefsLoaded;
    /// the UI module draws a slim vertical tab list and the selected page. Pages of disabled modules are hidden.
    /// </summary>
    public sealed class HearthTabPage
    {
        public string Id;            // "guests", "casino", "kitchen" …
        public string LabelKey;      // translation key, e.g. HH.Tab.Guests
        public string LabelEn;
        public ModuleId Module;
        public int Order = 100;
        public Action<Rect> Draw;
        /// <summary>Optional: show the page only when this returns true (e.g. only if a casino exists).</summary>
        public Func<bool> Visible;
        public string Label => LabelKey.TranslateOr(LabelEn ?? Id);
    }

    public static class HearthTabs
    {
        public static readonly List<HearthTabPage> Pages = new List<HearthTabPage>();
        public static void Register(HearthTabPage p) { Pages.RemoveAll(x => x.Id == p.Id); Pages.Add(p); Pages.Sort((a, b) => a.Order.CompareTo(b.Order)); }
        public static IEnumerable<HearthTabPage> VisiblePages => Pages.Where(p => Modules.On(p.Module) && (p.Visible == null || Guard.Run(p.Module, "Tab.Visible." + p.Id, p.Visible, false)));
    }
}
