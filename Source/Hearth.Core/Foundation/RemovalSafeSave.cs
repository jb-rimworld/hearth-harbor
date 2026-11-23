// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
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
            // a visitor of ours walks home without the mod; a colonist at one of our gatherings gets the party duty of the party stand-in lord
            // (a lord pawn without a duty makes the think tree complain every few ticks); the player's other pawns simply get no duty
            if (p?.Faction != null && !p.Faction.IsPlayer) __instance.duty = new PawnDuty(DutyDefOf.ExitMapBest) { locomotion = LocomotionUrgency.Walk };
            else if (p?.GetLord()?.LordJob is LordJob_Joinable_Gathering g && RemovalSafeSave.OurType(g.GetType()) && GatheringDefOf.Party?.duty != null) __instance.duty = new PawnDuty(GatheringDefOf.Party.duty, g.Spot);
            else __instance.duty = null;
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
    /// A lord of the player's faction with one of our gatherings (movie night) is written as a vanilla party at the same spot with the same
    /// organizer and the time that is left: without the mod the colonists finish a short party and are free again (a lord whose job class
    /// is missing would keep them, refuse drafting and throw on every signal). Our other player lords do not exist.
    /// </summary>
    [HarmonyPatch(typeof(Lord), nameof(Lord.ExposeData))]
    static class Patch_Lord_RemovalSafe
    {
        sealed class Real { public LordJob job; public StateGraph graph; public LordToil toil; public int toilIdx = -1; public Dictionary<int, LordToilData> toilData; public Dictionary<int, TriggerData> triggerData; }
        static readonly ConditionalWeakTable<Lord, Real> loaded = new ConditionalWeakTable<Lord, Real>();
        static readonly AccessTools.FieldRef<Lord, LordJob> curJobRef = AccessTools.FieldRefAccess<Lord, LordJob>("curJob");
        static readonly AccessTools.FieldRef<Lord, StateGraph> graphRef = AccessTools.FieldRefAccess<Lord, StateGraph>("graph");
        static readonly AccessTools.FieldRef<Lord, LordToil> toilRef = AccessTools.FieldRefAccess<Lord, LordToil>("curLordToil");

        // reflection (not compile-checked): a missing field on another game version is logged once and the stand-in keeps its default duration
        static readonly AccessTools.FieldRef<LordJob_Joinable_Gathering, int> durationRef = MakeDurationRef();
        static AccessTools.FieldRef<LordJob_Joinable_Gathering, int> MakeDurationRef()
        {
            try { return AccessTools.FieldRefAccess<LordJob_Joinable_Gathering, int>("durationTicks"); }
            catch (Exception e) { HLog.Error("Core", "LordJob_Joinable_Gathering.durationTicks not found on this game version: gathering stand-ins keep the vanilla party duration", e); return null; }
        }

        static bool Protect(Lord l)
        {
            var job = curJobRef(l);
            if (l.faction == null || !RemovalSafeSave.OurType(job?.GetType())) return false;
            return !l.faction.IsPlayer || job is LordJob_Joinable_Gathering;
        }

        /// <summary>The vanilla job written in place of ours: visitors walk home, the colony's own gathering becomes a plain party.</summary>
        static LordJob StandIn(Lord l, LordJob real)
        {
            if (!l.faction.IsPlayer || !(real is LordJob_Joinable_Gathering g)) return new LordJob_ExitMapBest(LocomotionUrgency.Walk, canDig: false);
            int left;
            try { left = g.TicksLeft; } catch (Exception) { left = g.DurationTicks; }
            var party = new LordJob_Joinable_Party(g.Spot, g.Organizer, GatheringDefOf.Party);
            if (durationRef != null) durationRef(party) = Math.Max(250, Math.Min(g.DurationTicks > 0 ? g.DurationTicks : 60000, left));
            return party;
        }

        static void Prefix(Lord __instance, out Real __state)
        {
            __state = null;
            if (!RemovalSafeSave.Saving || !Protect(__instance)) return;
            var l = __instance;
            __state = new Real { job = curJobRef(l), graph = graphRef(l), toil = toilRef(l) };
            LordJob stand; StateGraph g;
            Rand.PushState(); Rand.Seed = l.loadID * 193;
            try { stand = StandIn(l, __state.job); stand.lord = l; g = stand.CreateGraph(); } finally { Rand.PopState(); }
            foreach (var t in g.lordToils) t.lord = l;
            // the party stand-in keeps who has been present for how long (the vanilla party thought scales with it)
            if (stand is LordJob_Joinable_Party && g.lordToils.Count > 0 && __state.graph?.lordToils.Count > 0 && __state.graph.lordToils[0].data is LordToilData_Gathering gd
                && g.lordToils[0] is LordToil_Party) g.lordToils[0].data = gd;
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

    // ------------------------------------------------------------------------------------------------ scenario parts (F08, 1.1.0)
    /// <summary>
    /// Our scenario parts (rules, settings, starting research, starting account) are written as "hhParts" next to the vanilla part list, never
    /// inside it: without the mod vanilla never meets a class or ScenPartDef it does not know (save games and .rsc files of the scenario editor
    /// alike). The parts are taken out of the list only while it is written and put back at once at their old places; Find.Scenario of a new
    /// game is the ScenarioDef's own object, so the list must never stay changed.
    /// </summary>
    [HarmonyPatch(typeof(Scenario), nameof(Scenario.ExposeData))]
    static class Patch_Scenario_RemovalSafe
    {
        sealed class Stash { public List<ScenPart> list; public List<KeyValuePair<int, ScenPart>> ours; }

        static void Prefix(Scenario __instance, out Stash __state)
        {
            __state = null;
            if (!RemovalSafeSave.Saving) return;
            var parts = ScenarioSeed.PartsOf(__instance);
            if (parts == null) return;
            List<KeyValuePair<int, ScenPart>> ours = null;
            for (int i = 0; i < parts.Count; i++)
                if (parts[i] != null && RemovalSafeSave.OurType(parts[i].GetType())) (ours ?? (ours = new List<KeyValuePair<int, ScenPart>>())).Add(new KeyValuePair<int, ScenPart>(i, parts[i]));
            if (ours == null) return;
            for (int i = ours.Count - 1; i >= 0; i--) parts.RemoveAt(ours[i].Key);
            __state = new Stash { list = parts, ours = ours };
        }

        static void Postfix(Scenario __instance, Stash __state)
        {
            if (RemovalSafeSave.Saving)
            {
                if (__state == null) return;
                var mine = new List<ScenPart>();
                foreach (var kv in __state.ours) { __state.list.Insert(Math.Min(kv.Key, __state.list.Count), kv.Value); mine.Add(kv.Value); }
                Scribe_Collections.Look(ref mine, "hhParts", LookMode.Deep);
                return;
            }
            if (RemovalSafeSave.LoadingVars)
            {
                List<ScenPart> mine = null;
                Scribe_Collections.Look(ref mine, "hhParts", LookMode.Deep);
                if (mine == null || mine.Count == 0) return;
                var parts = ScenarioSeed.PartsOf(__instance);
                if (parts == null) return;
                foreach (var p in mine) if (p != null && p.def != null) parts.Add(p);
            }
        }

        /// <summary>The prefix took our parts out; if vanilla throws while writing, put them back anyway (the def's own list must stay whole).</summary>
        static Exception Finalizer(Exception __exception, Stash __state)
        {
            if (__exception != null && __state != null && RemovalSafeSave.Saving)
                foreach (var kv in __state.ours) if (!__state.list.Contains(kv.Value)) __state.list.Insert(Math.Min(kv.Key, __state.list.Count), kv.Value);
            return __exception;
        }
    }

    // ------------------------------------------------------------------------------------------------ storyteller (F09, 1.1.0)
    /// <summary>
    /// A colony played with one of our storytellers (Hetty Hearthwell) is written with Phoebe as the vanilla storyteller and ours in
    /// "hhStoryteller". Without the mod the save simply continues with Phoebe (a missing storyteller def would leave Storyteller.def null and
    /// the save unplayable); with the mod the real storyteller is back before vanilla builds the storyteller comps.
    /// </summary>
    [HarmonyPatch(typeof(Storyteller), nameof(Storyteller.ExposeData))]
    static class Patch_Storyteller_RemovalSafe
    {
        static StorytellerDef StandIn => DefDatabase<StorytellerDef>.GetNamedSilentFail("Phoebe") ?? DefDatabase<StorytellerDef>.GetNamedSilentFail("Cassandra")
                                         ?? DefDatabase<StorytellerDef>.AllDefsListForReading.FirstOrDefault(d => !RemovalSafeSave.OurDef(d));

        static void Prefix(Storyteller __instance, out StorytellerDef __state)
        {
            __state = null;
            if (!RemovalSafeSave.Saving || !RemovalSafeSave.OurDef(__instance.def)) return;
            var stand = StandIn; if (stand == null) return;
            __state = __instance.def;
            __instance.def = stand;
        }

        static void Postfix(Storyteller __instance, StorytellerDef __state)
        {
            if (RemovalSafeSave.Saving)
            {
                if (__state == null) return;
                __instance.def = __state;
                string name = __state.defName;
                Scribe_Values.Look(ref name, "hhStoryteller");
                return;
            }
            if (RemovalSafeSave.LoadingVars)
            {
                string name = null;
                Scribe_Values.Look(ref name, "hhStoryteller");
                if (name.NullOrEmpty()) return;
                var d = DefDatabase<StorytellerDef>.GetNamedSilentFail(name);
                if (d != null) __instance.def = d;
                else HLog.Warn("Core", "Saved storyteller " + name + " not found; continuing with " + __instance.def?.defName);
            }
        }

        static Exception Finalizer(Exception __exception, Storyteller __instance, StorytellerDef __state)
        {
            if (__exception != null && __state != null && RemovalSafeSave.Saving) __instance.def = __state;
            return __exception;
        }
    }
}
