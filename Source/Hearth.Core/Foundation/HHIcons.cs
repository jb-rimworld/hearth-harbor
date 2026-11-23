// SPDX-License-Identifier: MIT
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Hearth
{
    /// <summary>
    /// Icons of the settings windows and the comms console (F34, 1.1.0), loaded lazily on the main thread and cached (null = texture missing:
    /// callers then draw nothing, never a pink square). Every path is a literal here, so tools/art/needs.py sees it. UI/HH/Core ships with
    /// Silverline standalone too.
    /// </summary>
    public static class HHIcons
    {
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        static Texture2D Get(string id)
        {
            if (cache.TryGetValue(id, out var t)) return t;
            t = Load(id);
            cache[id] = t;
            return t;
        }

        static Texture2D Load(string id)
        {
            switch (id)
            {
                case "mark": return ContentFinder<Texture2D>.Get("UI/HH/Core/ModMark", false);
                case "markBank": return ContentFinder<Texture2D>.Get("UI/HH/Bank/SilverlineMark", false);
                case "page:modules": return ContentFinder<Texture2D>.Get("UI/HH/Core/Settings/Modules", false);
                case "page:difficulty": return ContentFinder<Texture2D>.Get("UI/HH/Core/Settings/Difficulty", false);
                case "page:general": return ContentFinder<Texture2D>.Get("UI/HH/Core/Settings/General", false);
                case "page:diagnosis": return ContentFinder<Texture2D>.Get("UI/HH/Core/Settings/Diagnosis", false);
                case "page:about": return ContentFinder<Texture2D>.Get("UI/HH/Core/Settings/About", false);
                case "mod:Inn": return ContentFinder<Texture2D>.Get("UI/HH/Core/Module_Inn", false);
                case "mod:ShopsFood": return ContentFinder<Texture2D>.Get("UI/HH/Core/Module_ShopsFood", false);
                case "mod:Casino": return ContentFinder<Texture2D>.Get("UI/HH/Core/Module_Casino", false);
                case "mod:Wellness": return ContentFinder<Texture2D>.Get("UI/HH/Core/Module_Wellness", false);
                case "mod:CityLife": return ContentFinder<Texture2D>.Get("UI/HH/Core/Module_CityLife", false);
                case "mod:Underworld": return ContentFinder<Texture2D>.Get("UI/HH/Core/Module_Underworld", false);
                case "mod:Bank": return ContentFinder<Texture2D>.Get("UI/HH/Core/Module_Bank", false);
                case "mod:AiFlavour": return ContentFinder<Texture2D>.Get("UI/HH/Core/Module_AiFlavour", false);
                default: return null;
            }
        }

        /// <summary>The Hearth &amp; Harbor mark (or the Silverline coin in Silverline standalone).</summary>
        public static Texture2D Mark => HearthMod.GuestAssemblyLoaded ? Get("mark") : (Get("markBank") ?? Get("mark"));
        public static Texture2D Module(ModuleId m) => m == ModuleId.Core ? Get("page:general") : Get("mod:" + m);
        /// <summary>Icon of a settings page: "modules", "difficulty", "general", "diagnosis", "about" or "mod:&lt;ModuleId&gt;".</summary>
        public static Texture2D Page(string pageId)
        {
            if (pageId == null) return null;
            if (pageId == "mod:Core") return Get("page:general");
            if (pageId.StartsWith("mod:")) return Get(pageId);
            return Get("page:" + pageId);
        }

        /// <summary>Draws an icon fitted into <paramref name="r"/>, dimmed when <paramref name="dim"/>. Nothing when the texture is missing.</summary>
        public static void Draw(Rect r, Texture2D tex, bool dim = false)
        {
            if (tex == null) return;
            var old = GUI.color;
            if (dim) GUI.color = new Color(old.r, old.g, old.b, old.a * 0.5f);
            Widgets.DrawTextureFitted(r, tex, 1f);
            GUI.color = old;
        }

        /// <summary>Tests: every icon id that should exist.</summary>
        public static IEnumerable<string> AllIds()
        {
            yield return "mark"; yield return "markBank";
            foreach (var p in new[] { "modules", "difficulty", "general", "diagnosis", "about" }) yield return "page:" + p;
            foreach (var m in Modules.All) if (m != ModuleId.Core) yield return "mod:" + m;
        }
        public static Texture2D ById(string id) => Get(id);
    }
}
