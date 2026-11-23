// SPDX-License-Identifier: MIT
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Hearth.Story
{
    /// <summary>
    /// F18 (1.1.0), a small easter egg: Hearth &amp; Harbor's author as one fixed pawn ("solid bio"), exactly like the creator and backer pawns of
    /// vanilla. Vanilla reads solid bios only from its own resources, so ours is added to SolidBioDatabase after vanilla has loaded them
    /// (startup and every later reload). From then on vanilla does everything: the bio may turn up for a generated colonist, a wanderer, a
    /// trader or a world pawn, at most once per game, never as a faction leader. Nothing is forced and nothing is announced.
    /// </summary>
    public static class CreatorPawn
    {
        public const string SettingKey = "core.creatorPawn";
        public const string ChildhoodDef = "HH_Creator_HarbourTinkerer", AdulthoodDef = "HH_Creator_WanderingInnkeeper";
        public static readonly NameTriple Name = new NameTriple("Julius", "Julius", "Berger");

        static PawnBio bio;

        public static PawnBio Bio
        {
            get
            {
                if (bio != null) return bio;
                var child = DefDatabase<BackstoryDef>.GetNamedSilentFail(ChildhoodDef);
                var adult = DefDatabase<BackstoryDef>.GetNamedSilentFail(AdulthoodDef);
                if (child == null || adult == null) return null;
                bio = new PawnBio { gender = GenderPossibility.Male, name = Name, childhood = child, adulthood = adult, pirateKing = false, rare = false };
                return bio;
            }
        }

        /// <summary>True when the pawn may turn up (global preference, default on).</summary>
        public static bool Enabled
        {
            get
            {
                var s = HSettings.Find(SettingKey);
                return s == null || HSettings.Get(s) >= 0.5f;
            }
        }

        public static bool InDatabase => SolidBioDatabase.allBios.Any(b => b == bio || (b?.name != null && b.name.First == Name.First && b.name.Last == Name.Last));

        /// <summary>Adds or removes the bio so the database matches the setting. Idempotent and cheap; returns true if the bio is in the database.</summary>
        public static bool Sync()
        {
            var b = Bio;
            if (b == null) { HLog.Once("creator.defs", LogLevel.Warn, "Core", "Creator pawn backstories missing: the easter egg pawn is not available"); return false; }
            bool want = Enabled, have = SolidBioDatabase.allBios.Contains(b);
            if (want && !have)
            {
                // another mod (or an older copy) may have added the same name: never twice
                if (SolidBioDatabase.allBios.Any(x => x?.name != null && x.name.First == Name.First && x.name.Last == Name.Last)) return true;
                b.name.ResolveMissingPieces();
                SolidBioDatabase.allBios.Add(b);
                HLog.Debug("Core", "Creator pawn bio added (" + SolidBioDatabase.allBios.Count + " solid bios)");
            }
            else if (!want && have) SolidBioDatabase.allBios.Remove(b);
            return want;
        }
    }

    /// <summary>Vanilla clears and refills the solid bios whenever play data is reloaded: put ours back right after.</summary>
    [HarmonyPatch(typeof(SolidBioDatabase), nameof(SolidBioDatabase.LoadAllBios))]
    static class Patch_SolidBios_Creator
    {
        static void Postfix() { Guard.Run(ModuleId.Core, "Creator.Sync", () => CreatorPawn.Sync()); }
    }
}
