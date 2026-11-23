// SPDX-License-Identifier: MIT
using UnityEngine;
using Verse;

namespace Hearth.Guests
{
    /// <summary>UI textures of the Inn module, loaded once on the main thread.</summary>
    [StaticConstructorOnStartup]
    public static class InnTex
    {
        public static readonly Texture2D GuestBed = ContentFinder<Texture2D>.Get("UI/HH/Inn/GuestBed", false) ?? BaseContent.BadTex;
        public static readonly Texture2D Handling_Basics = ContentFinder<Texture2D>.Get("UI/HH/Inn/Handling_Basics", false) ?? BaseContent.BadTex;
        public static readonly Texture2D Handling_Accompany = ContentFinder<Texture2D>.Get("UI/HH/Inn/Handling_Accompany", false) ?? BaseContent.BadTex;
        public static readonly Texture2D Handling_Recruit = ContentFinder<Texture2D>.Get("UI/HH/Inn/Handling_Recruit", false) ?? BaseContent.BadTex;
        public static readonly Texture2D SendAway = ContentFinder<Texture2D>.Get("UI/HH/Inn/SendAway", false) ?? BaseContent.BadTex;
        public static readonly Texture2D Price = ContentFinder<Texture2D>.Get("UI/HH/Inn/BedPrice", false) ?? BaseContent.BadTex;
        public static readonly Texture2D DoorPolicy = ContentFinder<Texture2D>.Get("UI/HH/Inn/DoorPolicy", false) ?? BaseContent.BadTex;
    }
}
