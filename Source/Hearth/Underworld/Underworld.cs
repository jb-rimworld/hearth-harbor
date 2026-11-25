// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Hearth.Underworld
{
    /// <summary>Content gate on any def of the underworld: building, research, venue type. Read by the PlaceWorker and the research patch.</summary>
    public class UwGateExtension : DefModExtension
    {
        /// <summary>Lowest content intensity (Standard … Grim) at which the def is visible and usable.</summary>
        public ContentIntensity minContent = ContentIntensity.Standard;
        /// <summary>Kept so that older XML (also of other mods) still parses. Since 1.2.0 there is no discovery: the field is ignored.</summary>
        public bool needsDiscovery = true;
        /// <summary>Never shown (old projects that are kept only because saves refer to them).</summary>
        public bool alwaysHidden;
        /// <summary>Which feature of the content table this def belongs to (only used by Dark+ gates that depend on the feature, e.g. OrganClinic).</summary>
        public string feature;
    }

    /// <summary>
    /// Public face of the Underworld module for the other modules (Inn, Venues, Casino, UI).
    /// Everything here is cheap and safe to call every tick; everything returns "nothing" when the module is off
    /// or the content intensity is Cozy. Since 1.2.0 there is no discovery and no warning dialog: the module and the content intensity decide.
    /// Nothing forced (N08): what reaches colonies that build nothing of the underworld (shady guests, gangs, scandals, whispers) also needs <see cref="InUse"/>.
    /// </summary>
    public static class Underworld
    {
        public const ModuleId Module = ModuleId.Underworld;

        static UnderworldComponent comp;
        public static UnderworldComponent Comp
        {
            get
            {
                if (Current.Game == null) return null;
                if (comp == null || comp.GameRef != Current.Game) { comp = Current.Game.GetComponent<UnderworldComponent>(); }
                return comp;
            }
        }

        // ---- the gates -------------------------------------------------------------------------------
        public static bool ModuleActive => Modules.On(Module);

        /// <summary>The content intensity chosen for this save (Cozy … Grim).</summary>
        public static ContentIntensity Intensity => HearthProfile.Current?.Content ?? HearthMod.Template.Profile.Content;

        /// <summary>Intensity that applies. Since 1.2.0 the same as <see cref="Intensity"/> (the choice itself is the consent; no warning dialog).</summary>
        public static ContentIntensity Effective => Intensity;

        public static bool Allows(UwFeature f) => ModuleActive && !(Peaceful && UnderworldMath.IsViolent(f)) && UnderworldMath.Allows((int)Effective, f);

        /// <summary>Special switch core.peacefulUnderworld: every violent feature is closed, whatever the content intensity.</summary>
        public static bool Peaceful => CoreSettings.Switch("core.peacefulUnderworld");

        /// <summary>Content intensity allows the underworld at all (not Cozy, module on).</summary>
        public static bool Visible => Allows(UwFeature.Facilities);

        /// <summary>Everything may happen. Since 1.2.0 the same as <see cref="Visible"/> (no discovery). Events, activities and buildings check this.</summary>
        public static bool Unlocked => Visible;

        // ---- N08 guard ----------------------------------------------------------------------------------------
        sealed class UseCache { public int tick = int.MinValue; public bool dirty = true; public bool inUse; }
        static readonly Dictionary<int, UseCache> useCache = new Dictionary<int, UseCache>();
        const int UseRefreshTicks = 250;

        /// <summary>
        /// Nothing forced (N08): the underworld is <see cref="Unlocked"/> and this map has at least one underworld facility of the player
        /// (back room door, fight pit marker, fence counter, smuggler's dock, hunting ground, organ clinic, or a drug lounge venue).
        /// Shady guests, whisper-based arrival weights, gang visits, scandals and smuggler landings need it. Cached per map:
        /// facilities mark the cache dirty when they spawn or leave, and it is rebuilt at least every 250 ticks (venue type changes).
        /// </summary>
        public static bool InUse(Map map)
        {
            if (map == null || !Unlocked) return false;
            int now = Find.TickManager?.TicksGame ?? 0;
            if (!useCache.TryGetValue(map.uniqueID, out var c)) useCache[map.uniqueID] = c = new UseCache();
            if (c.dirty || now - c.tick >= UseRefreshTicks || now < c.tick)
            {
                c.inUse = HasFacility(map); c.tick = now; c.dirty = false;
            }
            return c.inUse;
        }

        /// <summary>A facility was built or removed on this map (null: every map).</summary>
        public static void MarkInUseDirty(Map map)
        {
            if (map == null) { foreach (var kv in useCache) kv.Value.dirty = true; return; }
            if (useCache.TryGetValue(map.uniqueID, out var c)) c.dirty = true;
        }

        /// <summary>New or loaded game: map ids start again.</summary>
        internal static void ClearInUse() => useCache.Clear();

        static bool HasFacility(Map map)
        {
            var defs = UwDefs.Facilities;
            for (int i = 0; i < defs.Count; i++)
            {
                var list = map.listerThings.ThingsOfDef(defs[i]);
                for (int j = 0; j < list.Count; j++) if (list[j].Faction == Faction.OfPlayer) return true;
            }
            var lounge = UwDefs.LoungeVenue;
            if (lounge != null)
            {
                var venues = Venues.All(map);
                for (int i = 0; i < venues.Count; i++) if (venues[i].Type == lounge) return true;
            }
            return false;
        }

        public static bool DarkContent => Allows(UwFeature.OrganClinic);

        // ---- whispers -------------------------------------------------------------------------------------
        /// <summary>Whispers (Flüsterruf) 0-100: the reputation in the underworld. Decays by 2 % per 15 days (setting).</summary>
        public static float Whispers => Comp?.Whispers ?? 0f;
        public static void AddWhispers(float delta, string reason) => Comp?.AddWhispers(delta, reason);

        /// <summary>Arrival weight factor for respectable guests (exp(-N/40)). The Inn module multiplies its respectable archetypes with it.</summary>
        public static float RespectableArrivalFactor => UnderworldMath.RespectableArrivalFactor(Whispers);
        /// <summary>Arrival weight factor for outlaws (0.2 + N/50).</summary>
        public static float OutlawArrivalFactor => UnderworldMath.OutlawArrivalFactor(Whispers);
        /// <summary>True while a "secret casino" rumour draws high rollers (positive scandal): the Inn/Casino modules may raise their wealth levels.</summary>
        public static bool HighRollersActive => Comp != null && Find.TickManager != null && Comp.HighRollersUntil > Find.TickManager.TicksGame;

        // ---- shady guests -------------------------------------------------------------------------------------
        /// <summary>Gang members and other outlaws that may enter back rooms and (if released) prisons. The Inn module marks its Shady stays with MarkShady.</summary>
        public static bool IsShady(Pawn p) => p != null && Comp != null && Comp.IsShady(p);
        public static void MarkShady(Pawn p) { if (p != null) Comp?.MarkShady(p); }

        // ---- renown and goodwill hooks ----------------------------------------------------------------------------
        /// <summary>
        /// Raised when something of the underworld changes the public renown of the house (scandals). The Inn module owns Renown and subscribes;
        /// arguments are map, delta (already capped by the difficulty tier) and a short reason.
        /// </summary>
        public static event Action<Map, float, string> RenownDelta;
        internal static void RaiseRenown(Map map, float delta, string reason)
        {
            if (Math.Abs(delta) < 0.0001f) return;
            if (RenownDelta == null) HLog.Once("uw.noRenownListener", LogLevel.Info, "Underworld", "No renown listener (Inn module not loaded): renown effects of scandals are only logged");
            try { RenownDelta?.Invoke(map, delta, reason); } catch (Exception e) { HLog.Error("Underworld", "Renown listener failed", e); }
            Comp?.NoteRenew(delta);
        }

        public static float Tier => HearthProfile.Current?.Tier ?? HearthMod.Template.Tier;
        public static float CapRenown(float delta) => UnderworldMath.CapNegative(delta, UnderworldMath.RenownCapPerEvent, Tier);
        public static float CapGoodwill(float delta) => UnderworldMath.CapNegative(delta, UnderworldMath.GoodwillCapPerEvent, Tier);
        public static int CapFine(int silver) { float cap = UnderworldMath.CapAt(UnderworldMath.FineCap, Tier); return float.IsInfinity(cap) ? silver : (int)Math.Min(silver, cap); }

        /// <summary>Changes goodwill of a faction towards the player, capped by the tier. Returns the delta actually applied.</summary>
        public static int AffectGoodwill(Faction f, int delta, string reason)
        {
            if (f == null || f.IsPlayer || f.defeated || delta == 0) return 0;
            int d = delta < 0 ? (int)Math.Round(CapGoodwill(delta)) : delta;
            if (d == 0) return 0;
            try
            {
                f.TryAffectGoodwillWith(Faction.OfPlayer, d, true, true, null, null);
                HLog.Info("Underworld", $"Goodwill of {f.Name} {d:+#;-#}: {reason}");
            }
            catch (Exception e) { HLog.Error("Underworld", "Goodwill change failed", e); return 0; }
            return d;
        }

        // ---- defs ----------------------------------------------------------------------------------------------------
        public static ThingDef Def(string name) => DefDatabase<ThingDef>.GetNamedSilentFail(name);
        public static T Def<T>(string name) where T : Def => DefDatabase<T>.GetNamedSilentFail(name);

        // ---- pawn rules ------------------------------------------------------------------------------------------------
        /// <summary>Children never take part in gambling-adjacent dark mechanics (fights, hunts, interrogations, organs). Not switchable.</summary>
        public static bool IsMinor(Pawn p)
        {
            if (p == null) return true;
            if (!p.RaceProps.Humanlike) return false;
            return !p.DevelopmentalStage.Adult();
        }

        public static bool Home(Map m) => m != null && m.IsPlayerHome;
        public static IEnumerable<Map> HomeMaps => Find.Maps.Where(m => m.IsPlayerHome);

        // ---- accounts --------------------------------------------------------------------------------------------------
        public static string PitAccount(Map m) => "house:pit:" + (m?.uniqueID ?? 0);
        public static string FenceAccount(Map m) => "house:fence:" + (m?.uniqueID ?? 0);
        public static string Till(Map m) => Payments.ColonyTill(m);
    }
}
