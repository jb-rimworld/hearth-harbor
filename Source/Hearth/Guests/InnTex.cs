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
        public static readonly Texture2D Handling_Convert = ContentFinder<Texture2D>.Get("UI/HH/Inn/Handling_Convert", false) ?? BaseContent.BadTex;
        public static readonly Texture2D BedUse_Colonist = ContentFinder<Texture2D>.Get("UI/HH/Inn/BedUse_Colonist", false) ?? BaseContent.BadTex;
        public static readonly Texture2D BedUse_Guest = ContentFinder<Texture2D>.Get("UI/HH/Inn/BedUse_Guest", false) ?? BaseContent.BadTex;
        public static readonly Texture2D BedUse_Patient = ContentFinder<Texture2D>.Get("UI/HH/Inn/BedUse_Patient", false) ?? BaseContent.BadTex;
        public static readonly Texture2D BedUse_Surgery = ContentFinder<Texture2D>.Get("UI/HH/Inn/BedUse_Surgery", false) ?? BaseContent.BadTex;
        public static readonly Texture2D DevTools = ContentFinder<Texture2D>.Get("UI/HH/Inn/DevTools", false) ?? BaseContent.BadTex;
        public static readonly Texture2D SendAway = ContentFinder<Texture2D>.Get("UI/HH/Inn/SendAway", false) ?? BaseContent.BadTex;
        public static readonly Texture2D Price = ContentFinder<Texture2D>.Get("UI/HH/Venues/Cmd_Price", false) ?? BaseContent.BadTex;
        public static readonly Texture2D DoorPolicy = ContentFinder<Texture2D>.Get("UI/HH/Inn/DoorPolicy", false) ?? BaseContent.BadTex;
    }
}
