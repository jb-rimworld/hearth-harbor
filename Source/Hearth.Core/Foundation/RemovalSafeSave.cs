// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Hearth
{
    /// <summary>
    /// Raw removal (KONZEPT 3.7 / 15.7, S6): the save must load cleanly even after the mod was simply unsubscribed. Vanilla copes with
    /// missing defs in most places, but three places break every tick when a def or class of ours is missing:
    ///   - a pawn's current job (JobDef/JobDriver of ours): the record and thought workers throw on a job without a def;
    ///   - a pawn's lord duty (DutyDef of ours): the think tree, social and drawing code throw on a duty without a def;
    ///   - a lord running one of our LordJobs (guest stay, gang visit …): the lord loses its job and throws on every signal;
    ///   - a letter with one of our LetterDefs (still in the letter stack): the letter stack throws on every frame.
    /// So these are written in a vanilla-safe form: the vanilla nodes get no job / a plain "exit the map" duty and lord (only for pawns
    /// that are not the player's), and the real objects go into extra nodes ("hh…") that only we read back. With the mod present the
    /// load restores the real objects, so nothing changes in play; without it vanilla sees ordinary visitors walking home.
    /// </summary>
    public static class RemovalSafeSave
    {
        static readonly HashSet<string> Ours = new HashSet<string> { "Hearth.Core", "Silverline", "Hearth" };
        public static bool OurType(Type t) => t != null && Ours.Contains(t.Assembly.GetName().Name);
        public static bool OurDef(Def d) => d != null && d.modContentPack != null && HearthMod.Instance != null && d.modContentPack == HearthMod.Instance.Content;
        public static bool OurJob(Job j) => j != null && (OurDef(j.def) || OurDef(j.workGiverDef));

        internal static bool Saving => Scribe.mode == LoadSaveMode.Saving;
        internal static bool LoadingVars => Scribe.mode == LoadSaveMode.LoadingVars;
    }

    // ------------------------------------------------------------------------------------------------ jobs
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.ExposeData))]
    static class Patch_JobTracker_RemovalSafe
    {
        sealed class Stash { public Job job; public JobDriver driver; public JobQueue queue; }
        static readonly AccessTools.FieldRef<Pawn_JobTracker, Pawn> pawnRef = AccessTools.FieldRefAccess<Pawn_JobTracker, Pawn>("pawn");

        static void Prefix(Pawn_JobTracker __instance, out Stash __state)
        {
            __state = null;
            if (!RemovalSafeSave.Saving) return;
            var t = __instance;
            bool ourCur = RemovalSafeSave.OurJob(t.curJob) || RemovalSafeSave.OurType(t.curDriver?.GetType());
            bool ourQueued = false;
            if (t.jobQueue != null) for (int i = 0; i < t.jobQueue.Count; i++) if (RemovalSafeSave.OurJob(t.jobQueue[i].job)) { ourQueued = true; break; }
            if (!ourCur && !ourQueued) return;
            __state = new Stash { job = t.curJob, driver = t.curDriver, queue = t.jobQueue };
            if (ourCur) { t.curJob = null; t.curDriver = null; }
            if (ourQueued)
            {
                var q = new JobQueue();
                for (int i = 0; i < __state.queue.Count; i++) if (!RemovalSafeSave.OurJob(__state.queue[i].job)) q.EnqueueLast(__state.queue[i].job, __state.queue[i].tag);
                t.jobQueue = q;
            }
        }

        static void Postfix(Pawn_JobTracker __instance, Stash __state)
        {
            var t = __instance;
            if (RemovalSafeSave.Saving)
            {
                if (__state == null) return;
                // the real job, written where only Hearth & Harbor looks
                t.curJob = __state.job; t.curDriver = __state.driver; t.jobQueue = __state.queue;
                Job j = t.curJob; JobDriver d = t.curDriver; JobQueue q = t.jobQueue;
                Scribe_Deep.Look(ref j, "hhRealJob");
                Scribe_Deep.Look(ref d, "hhRealDriver");
                Scribe_Deep.Look(ref q, "hhRealQueue");
                return;
            }
            if (RemovalSafeSave.LoadingVars)
            {
                Job j = null; JobDriver d = null; JobQueue q = null;
                Scribe_Deep.Look(ref j, "hhRealJob");
                Scribe_Deep.Look(ref d, "hhRealDriver");
                Scribe_Deep.Look(ref q, "hhRealQueue");
                if (j != null && d != null) { t.curJob = j; t.curDriver = d; d.pawn = pawnRef(t); d.job = j; }
                if (q != null) t.jobQueue = q;
            }
        }
    }

    // ------------------------------------------------------------------------------------------------ duties
    [HarmonyPatch(typeof(Pawn_MindState), nameof(Pawn_MindState.ExposeData))]
    static class Patch_MindState_RemovalSafe
    {
        static void Prefix(Pawn_MindState __instance, out PawnDuty __state)
        {
            __state = null;
            if (!RemovalSafeSave.Saving || __instance.duty == null || !RemovalSafeSave.OurDef(__instance.duty.def)) return;
            __state = __instance.duty;
            var p = __instance.pawn;
            // a visitor of ours walks home without the mod; the player's own pawns simply get no duty
            __instance.duty = p?.Faction != null && !p.Faction.IsPlayer ? new PawnDuty(DutyDefOf.ExitMapBest) { locomotion = LocomotionUrgency.Walk } : null;
        }

        static void Postfix(Pawn_MindState __instance, PawnDuty __state)
        {
            if (RemovalSafeSave.Saving)
            {
                if (__state == null) return;
                __instance.duty = __state;
                var d = __state; Scribe_Deep.Look(ref d, "hhRealDuty");
                return;
            }
            if (RemovalSafeSave.LoadingVars)
            {
                PawnDuty d = null; Scribe_Deep.Look(ref d, "hhRealDuty");
                if (d != null) __instance.duty = d;
            }
        }
    }

    // ------------------------------------------------------------------------------------------------ letters
    /// <summary>Letters with our LetterDef are written with a vanilla def; the real def follows in "hhRealDef". (Letters of our own classes
    /// are simply dropped by vanilla without the mod, with one load message each.)</summary>
    [HarmonyPatch(typeof(Letter), nameof(Letter.ExposeData))]
    static class Patch_Letter_RemovalSafe
    {
        static void Prefix(Letter __instance, out LetterDef __state)
        {
            __state = null;
            if (!RemovalSafeSave.Saving || !RemovalSafeSave.OurDef(__instance.def)) return;
            __state = __instance.def;
            __instance.def = __state.color.r > __state.color.g + 0.2f ? LetterDefOf.NegativeEvent : LetterDefOf.NeutralEvent;
        }

        static void Postfix(Letter __instance, LetterDef __state)
        {
            if (RemovalSafeSave.Saving)
            {
                if (__state == null) return;
                __instance.def = __state;
                var d = __state; Scribe_Defs.Look(ref d, "hhRealDef");
                return;
            }
            if (RemovalSafeSave.LoadingVars)
            {
                LetterDef d = null; Scribe_Defs.Look(ref d, "hhRealDef");
                if (d != null) __instance.def = d;
            }
        }
    }

    // ------------------------------------------------------------------------------------------------ lords
    /// <summary>
    /// A lord of another faction with one of our LordJobs is written as a vanilla "exit the map" lord; the real job and its state graph
    /// data (current toil, toil and trigger data such as stay timers) follow in hh… nodes and are put back after the vanilla load.
    /// Lords of the player's faction (screenings) are left alone: their pawns stay home either way.
    /// </summary>
    [HarmonyPatch(typeof(Lord), nameof(Lord.ExposeData))]
    static class Patch_Lord_RemovalSafe
    {
        sealed class Real { public LordJob job; public StateGraph graph; public LordToil toil; public int toilIdx = -1; public Dictionary<int, LordToilData> toilData; public Dictionary<int, TriggerData> triggerData; }
        static readonly ConditionalWeakTable<Lord, Real> loaded = new ConditionalWeakTable<Lord, Real>();
        static readonly AccessTools.FieldRef<Lord, LordJob> curJobRef = AccessTools.FieldRefAccess<Lord, LordJob>("curJob");
        static readonly AccessTools.FieldRef<Lord, StateGraph> graphRef = AccessTools.FieldRefAccess<Lord, StateGraph>("graph");
        static readonly AccessTools.FieldRef<Lord, LordToil> toilRef = AccessTools.FieldRefAccess<Lord, LordToil>("curLordToil");

        static bool Protect(Lord l) => l.faction != null && !l.faction.IsPlayer && RemovalSafeSave.OurType(curJobRef(l)?.GetType());

        static void Prefix(Lord __instance, out Real __state)
        {
            __state = null;
            if (!RemovalSafeSave.Saving || !Protect(__instance)) return;
            var l = __instance;
            __state = new Real { job = curJobRef(l), graph = graphRef(l), toil = toilRef(l) };
            var stand = new LordJob_ExitMapBest(LocomotionUrgency.Walk, canDig: false);
            stand.lord = l;
            Rand.PushState(); Rand.Seed = l.loadID * 193;
            StateGraph g; try { g = stand.CreateGraph(); } finally { Rand.PopState(); }
            foreach (var t in g.lordToils) t.lord = l;
            curJobRef(l) = stand; graphRef(l) = g; toilRef(l) = g.StartingToil;
        }

        static void Postfix(Lord __instance, Real __state)
        {
            var l = __instance;
            if (RemovalSafeSave.Saving)
            {
                if (__state == null) return;
                curJobRef(l) = __state.job; graphRef(l) = __state.graph; toilRef(l) = __state.toil;
                var job = __state.job; var g = __state.graph;
                int idx = g.lordToils.IndexOf(__state.toil);
                var toilData = new Dictionary<int, LordToilData>();
                for (int i = 0; i < g.lordToils.Count; i++) if (g.lordToils[i].data != null) toilData.Add(i, g.lordToils[i].data);
                var trigData = new Dictionary<int, TriggerData>();
                int n = 0;
                foreach (var tr in g.transitions) foreach (var trig in tr.triggers) { if (trig.data != null) trigData.Add(n, trig.data); n++; }
                Scribe_Deep.Look(ref job, "hhRealLordJob");
                Scribe_Values.Look(ref idx, "hhRealToilIdx", -1);
                Scribe_Collections.Look(ref toilData, "hhRealToilData", LookMode.Value, LookMode.Deep);
                Scribe_Collections.Look(ref trigData, "hhRealTriggerData", LookMode.Value, LookMode.Deep);
                return;
            }
            if (RemovalSafeSave.LoadingVars)
            {
                var r = new Real();
                Scribe_Deep.Look(ref r.job, "hhRealLordJob");
                if (r.job == null) return;
                Scribe_Values.Look(ref r.toilIdx, "hhRealToilIdx", -1);
                Scribe_Collections.Look(ref r.toilData, "hhRealToilData", LookMode.Value, LookMode.Deep);
                Scribe_Collections.Look(ref r.triggerData, "hhRealTriggerData", LookMode.Value, LookMode.Deep);
                loaded.Remove(l); loaded.Add(l, r);
                return;
            }
            if (Scribe.mode == LoadSaveMode.PostLoadInit && loaded.TryGetValue(l, out var real))
            {
                loaded.Remove(l);
                // vanilla has just set up the stand-in; replace it by the real job, exactly as vanilla restores a graph
                if (real.job.LostImportantReferenceDuringLoading) { HLog.Warn("Core", "Lord " + l.loadID + ": the real job lost a reference while loading; the visitors leave."); return; }
                try
                {
                    if (l.ownedPawns.Count > 0 || curJobRef(l) != null) l.SetJob(real.job, loading: true);
                    var g = graphRef(l);
                    if (real.toilData != null) foreach (var kv in real.toilData) if (kv.Key >= 0 && kv.Key < g.lordToils.Count) g.lordToils[kv.Key].data = kv.Value;
                    if (real.triggerData != null)
                    {
                        int n = 0;
                        foreach (var tr in g.transitions) foreach (var trig in tr.triggers) { if (real.triggerData.TryGetValue(n, out var td)) trig.data = td; n++; }
                    }
                    if (real.toilIdx >= 0 && real.toilIdx < g.lordToils.Count) toilRef(l) = g.lordToils[real.toilIdx];
                    else HLog.Warn("Core", "Lord " + l.loadID + ": saved toil index " + real.toilIdx + " out of range, starting toil used");
                    if (toilRef(l) == null) toilRef(l) = g.StartingToil;
                }
                catch (Exception e) { HLog.Error("Core", "Could not restore the lord job of lord " + l.loadID, e); }
            }
        }
    }
}
