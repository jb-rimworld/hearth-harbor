using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Hearth
{
    /// <summary>Small API differences between RimWorld 1.5, 1.6 and 1.7, in one place. Keep the 1.7 behaviour as the reference.</summary>
    public static class VersionCompat
    {
        /// <summary>1.6+: Map.TreatAsPlayerHomeForThreatPoints (also true for temporary player maps). 1.5: Map.IsPlayerHome.</summary>
        public static bool TreatAsPlayerHome(this Map m)
        {
#if RW16PLUS
            return m.TreatAsPlayerHomeForThreatPoints;
#else
            return m.IsPlayerHome;
#endif
        }

        /// <summary>1.6+: SocialInteractionUtility. 1.5: InteractionUtility.</summary>
        public static bool CanInitiateInteraction(Pawn pawn)
        {
#if RW16PLUS
            return SocialInteractionUtility.CanInitiateInteraction(pawn);
#else
            return InteractionUtility.CanInitiateInteraction(pawn);
#endif
        }

        /// <summary>1.6+: ActiveTransporterInfo (pods, shuttles). 1.5: ActiveDropPodInfo.</summary>
        public static bool IsTransporterInfo(IThingHolder h)
        {
#if RW16PLUS
            return h is ActiveTransporterInfo;
#else
            return h is ActiveDropPodInfo;
#endif
        }

#if RW16PLUS
        public static BoolGrid InnerGridOf(Area a) => a.InnerGrid;
#else
        static readonly FieldInfo innerGridField = AccessTools.Field(typeof(Area), "innerGrid");
        /// <summary>1.5 has the grid only as a private field.</summary>
        public static BoolGrid InnerGridOf(Area a) => (BoolGrid)innerGridField.GetValue(a);
#endif

#if RW16PLUS
        public static bool FactionCanBeGroupSource(IncidentWorker_NeutralGroup w, Faction f, IncidentParms parms) => w.FactionCanBeGroupSource(f, parms);
#else
        static readonly MethodInfo groupSourceMethod = AccessTools.Method(typeof(IncidentWorker_NeutralGroup), "FactionCanBeGroupSource", new[] { typeof(Faction), typeof(Map), typeof(bool) });
        public static bool FactionCanBeGroupSource(IncidentWorker_NeutralGroup w, Faction f, IncidentParms parms)
            => (bool)groupSourceMethod.Invoke(w, new object[] { f, parms.target as Map, false });
#endif
    }
}
