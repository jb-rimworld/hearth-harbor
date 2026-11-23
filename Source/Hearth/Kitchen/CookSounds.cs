// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Hearth.Kitchen
{
    /// <summary>
    /// F31 (kitchen part): every kind of cooking has its own working sound and its own little effect, a finished Fine or Signature plate has a
    /// cue, plates have their own pickup/drop sounds and the sounds of eating follow the plate. The recipes are generated (RecipeBuilder), so the
    /// mapping lives here: the LAST step of a dish (= its main device, 6.4) decides which sound the cook makes at the bill.
    /// Everything is def data (SoundDefs and EffecterDefs in Defs/Kitchen/Sounds_Kitchen.xml); the setting kitchen.cookSounds switches back to the vanilla
    /// pan-frying sound and effect without a restart (checked every 250 ticks, applies to the next job).
    /// </summary>
    public static class CookSounds
    {
        public const string SettingKey = "kitchen.cookSounds";
        public const string VanillaSound = "Recipe_CookMeal";
        public const string VanillaEffecter = "Cook";

        /// <summary>Last step of a dish -> SoundDef of the working loop.</summary>
        public static string SoundNameOf(StepKind last)
        {
            switch (last)
            {
                case StepKind.Chop: case StepKind.Carve: return "HH_Cook_Chop";
                case StepKind.Simmer: case StepKind.Boil: return "HH_Cook_Simmer";
                case StepKind.Grill: case StepKind.SpitRoast: case StepKind.Sear: return "HH_Cook_Grill";
                case StepKind.Roast: return "HH_Cook_Bake";
                case StepKind.PanFry: return "HH_Cook_PanFry";
                case StepKind.DeepFry: return "HH_Cook_DeepFry";
                case StepKind.StirFry: return "HH_Cook_StirFry";
                case StepKind.Steam: return "HH_Cook_Steam";
                case StepKind.KneadDough: return "HH_Cook_Knead";
                case StepKind.Smoke: return "HH_Cook_Smoke";
                case StepKind.Chill: return "HH_Cook_Chill";
                default: return null;
            }
        }

        /// <summary>Last step of a dish -> EffecterDef of the small visual (null = the vanilla cooking effecter).</summary>
        public static string EffecterNameOf(StepKind last)
        {
            switch (last)
            {
                case StepKind.Chop: case StepKind.Carve: return "HH_Cook_Chop";
                case StepKind.Simmer: case StepKind.Boil: case StepKind.Steam: return "HH_Cook_Steam";
                case StepKind.Grill: case StepKind.SpitRoast: case StepKind.Sear: case StepKind.StirFry: return "HH_Cook_Sparks";
                case StepKind.KneadDough: return "HH_Cook_Flour";
                case StepKind.Smoke: return "HH_Cook_Smoke";
                case StepKind.Chill: return "HH_Cook_Chill";
                default: return null;   // pan-frying, deep-frying and baking keep the food bits of vanilla
            }
        }

        static StepKind Last(DishDef d) => d.steps.Count > 0 ? d.steps[d.steps.Count - 1] : StepKind.PanFry;

        static SoundDef Vanilla => DefDatabase<SoundDef>.GetNamedSilentFail(VanillaSound);
        static EffecterDef VanillaFx => DefDatabase<EffecterDef>.GetNamedSilentFail(VanillaEffecter);

        public static SoundDef OwnSound(DishDef d)
        {
            string n = SoundNameOf(Last(d));
            var s = n == null ? null : DefDatabase<SoundDef>.GetNamedSilentFail(n);
            if (s == null) HLog.Once("kitchen.snd." + n, LogLevel.Warn, "Kitchen", "Cooking sound " + n + " is missing, the vanilla sound is used.");
            return s;
        }

        public static EffecterDef OwnEffecter(DishDef d)
        {
            string n = EffecterNameOf(Last(d));
            if (n == null) return VanillaFx;
            var e = DefDatabase<EffecterDef>.GetNamedSilentFail(n);
            if (e == null) HLog.Once("kitchen.fx." + n, LogLevel.Warn, "Kitchen", "Cooking effecter " + n + " is missing, the vanilla effecter is used.");
            return e ?? VanillaFx;
        }

        /// <summary>The sound the recipes of a dish get at startup (own sounds on; Sync switches them off when the setting says so).</summary>
        public static SoundDef SoundFor(DishDef d) => OwnSound(d) ?? Vanilla;
        public static EffecterDef EffecterFor(DishDef d) => OwnEffecter(d);

        static bool? applied;

        static bool Enabled()
        {
            try { return HSettings.GetBool(SettingKey); } catch { return true; }
        }

        /// <summary>Puts the sounds and effecters of all dish recipes in line with the setting. Cheap when nothing changed.</summary>
        public static void Sync(bool force = false)
        {
            bool on = Enabled();
            if (!force && applied == on) return;
            applied = on;
            foreach (var dish in DefDatabase<DishDef>.AllDefsListForReading)
            {
                var own = OwnSound(dish) ?? Vanilla; var fx = OwnEffecter(dish);
                foreach (var r in AllRecipesOf(dish))
                {
                    r.soundWorking = on ? own : Vanilla;
                    r.effectWorking = on ? fx : VanillaFx;
                }
            }
        }

        /// <summary>Forget what was applied (a new game or a load can carry another setting).</summary>
        public static void Reset() => applied = null;

        static IEnumerable<RecipeDef> AllRecipesOf(DishDef dish)
        {
            // the offered recipes plus the hidden ones of retired forms
            for (int v = 0; v < 3; v++)
            {
                var r = DefDatabase<RecipeDef>.GetNamedSilentFail("HH_Cook_" + dish.Id + (v == 0 ? "" : "_" + RecipeBuilder.VariantName[v]));
                if (r != null) yield return r;
            }
        }

        // ---- one-shots ----------------------------------------------------------------------------------------------------------

        static void Play(string defName, Map map, IntVec3 cell)
        {
            if (map == null || !cell.IsValid || !Enabled() || !Modules.On(ModuleId.ShopsFood)) return;
            var def = DefDatabase<SoundDef>.GetNamedSilentFail(defName);
            if (def == null) { HLog.Once("kitchen.snd." + defName, LogLevel.Warn, "Kitchen", "Sound " + defName + " is missing."); return; }
            def.PlayOneShot(new TargetInfo(cell, map));
        }

        /// <summary>A finished Fine plate rings a soft bell, a Signature plate gets a short flourish; everyday plates stay quiet.</summary>
        public static string DoneSoundName(int variant) => variant == 1 ? "HH_Cook_Done_Fine" : variant >= 2 ? "HH_Cook_Done_Signature" : null;

        public static void PlayDone(int variant, Pawn worker)
        {
            string n = DoneSoundName(variant);
            if (n == null || worker == null || !worker.Spawned) return;
            Play(n, worker.Map, worker.Position);
        }

        /// <summary>A small cue after a really good plate: Fine or Signature, Good quality or better and not stale. Sound plus a heart over the eater's head.</summary>
        public static bool EatReactionApplies(CompDish c)
        {
            if (c == null || c.dish == null || c.variant < 1 || c.IsStale) return false;
            return c.Quality >= QualityCategory.Good;
        }

        public static void PlayEatReaction(CompDish c, Pawn eater)
        {
            if (!EatReactionApplies(c) || eater == null || !eater.Spawned || !Enabled()) return;
            Play("HH_Eat_Satisfied", eater.Map, eater.Position);
            try { FleckMaker.ThrowMetaIcon(eater.Position, eater.Map, FleckDefOf.Heart); } catch (Exception e) { HLog.Once("kitchen.heart", LogLevel.Warn, "Kitchen", "Heart icon failed: " + e.Message); }
        }
    }
}
