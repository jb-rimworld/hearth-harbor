// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Hearth.ShopsFood
{
    /// <summary>
    /// Entry point of the venues part of "Shops &amp; Food": venue signs, shops, taverns, vending machines, staffing, the big market
    /// and the hiding of replaced vanilla furniture. The kitchen and dishes live in their own entry point.
    /// </summary>
    public sealed class VenuesModule : HearthModuleBase
    {
        public override ModuleId Id => ModuleId.ShopsFood;

        public override void RegisterSettings() => VS.Register();

        public override void OnDefsLoaded()
        {
            GuestActivities.Register(new ShopActivity());
            GuestActivities.Register(new DrinkActivity());
            GuestActivities.Register(new VendingActivity());
            Caretaker.Register(ModuleId.ShopsFood, "venues.releaseOrphans", RepairForbidden);
            // other modules report served guests through the contract (casino croupiers, F14)
            Venues.ServedHook = p => VenueService.NoteServed(p);
            Venues.ReadyHook = VenueSetup.IsReady;
            Venues.ReadySummaryHook = v => Modules.On(ModuleId.ShopsFood) ? VenueSetup.Summary(v) : null;
            // guide step "build what the venue needs" (F12)
            Hearth.Shell.GuideChecks.Register("venueReady", () =>
            {
                if (Current.ProgramState != ProgramState.Playing) return false;
                foreach (var m in Find.Maps) if (m.IsPlayerHome && Venues.All(m).Any(v => VenueSetup.Report(v).Complete)) return true;
                return false;
            });
            // designators are resolved after the long loading event; hide replaced furniture once they exist
            LongEventHandler.ExecuteWhenFinished(() => Guard.Run(ModuleId.ShopsFood, "VanillaHiding.Refresh(startup)", VanillaHiding.Refresh));
            // switching Shops & Food off gives the replaced vanilla furniture back at once (and hides it again when switched on)
            ModuleGate.Changed += _ => Guard.Run(ModuleId.ShopsFood, "VanillaHiding.Refresh(module switch)", VanillaHiding.Refresh);
        }

        /// <summary>Caretaker (S8): goods we forbade that are no longer for sale (sign gone, type changed, thing moved) are released.</summary>
        static int RepairForbidden()
        {
            int fixedCount = 0;
            foreach (var map in Find.Maps)
            {
                var comp = map.GetComponent<VenueMapComp>(); if (comp == null) continue;
                int before = comp.ForbiddenCount;
                if (before == 0) continue;
                VenueSales.ClearCaches();
                comp.SyncForbidden();
                int released = before - comp.ForbiddenCount;
                if (released > 0) { fixedCount += released; Caretaker.Fixed("venues.releaseOrphans", released + " goods forbidden by a venue that no longer sells them"); }
            }
            return fixedCount;
        }

        public override void OnGameStarted() { VenueState.Clear(); VenueSetup.Clear(); VanillaHiding.Refresh(); }
        public override void OnGameLoaded()
        {
            VenueState.Clear(); VenueSetup.Clear(); VanillaHiding.Refresh();
            // 1.1.0 (F12): businesses that miss required furniture stay closed; say so once after loading (older saves: taverns without a counter)
            LongEventHandler.ExecuteWhenFinished(() => Guard.Run(ModuleId.ShopsFood, "Venues.NotReadyNotice", NotReadyNotice));
        }

        static void NotReadyNotice()
        {
            if (Current.ProgramState != ProgramState.Playing || !HSettings.GetBool(VenueSetup.RequireKey)) return;
            var names = new List<string>(); Thing first = null;
            foreach (var map in Find.Maps)
            {
                if (!map.IsPlayerHome) continue;
                foreach (var v in Venues.All(map))
                    if (!VenueSetup.IsReady(v)) { names.Add(v.DisplayNameOf() + " (" + string.Join(", ", VenueSetup.Report(v).Blockers.Select(b => b.Req.label)) + ")"); first = first ?? v.Sign; }
            }
            if (names.Count == 0) return;
            Messages.Message("HH.Setup.LoadNotice".TranslateOr("Not ready to open, furniture missing: {0}. The sign's Setup tab has build buttons.").Replace("{0}", string.Join("; ", names)),
                first != null ? new LookTargets(first) : LookTargets.Invalid, MessageTypeDefOf.CautionInput, false);
        }

        public override void PrepareRemoval(List<string> report)
        {
            int n = 0;
            foreach (var map in Find.Maps)
            {
                var comp = map.GetComponent<VenueMapComp>();
                n += comp?.ReleaseAll() ?? 0;
            }
            int restored = 0;
            foreach (var map in Find.Maps)
                foreach (var v in Venues.All(map))
                    if (v.Comp is CompVenueSignHH c && c.RestockPlaces > 0) { restored += c.RestockPlaces; c.restockOn = false; c.RestoreRestock(map); }
            VanillaHiding.Refresh();
            // shift moods (F14) are our ThoughtDefs: take them off every pawn so a save without the mod has no dangling memories
            int moods = 0;
            foreach (var p in PawnsFinder.AllMapsWorldAndTemporary_Alive)
            {
                var mem = p.needs?.mood?.thoughts?.memories; if (mem == null) continue;
                foreach (var d in new[] { HHVenueDefOf.HH_ServedBusy, HHVenueDefOf.HH_ServedIdle })
                    if (d != null) { int c = mem.NumMemoriesOfDef(d); if (c > 0) { mem.RemoveMemoriesOfDef(d); moods += c; } }
            }
            report.Add($"Venues: {n} forbidden goods released, {restored} storage places given back, {moods} shift moods removed, hidden vanilla furniture restored.");
        }

        public override IEnumerable<string> Diagnosis()
        {
            foreach (var map in Find.Maps.Where(m => m.IsPlayerHome))
            {
                var all = Venues.All(map);
                yield return $"Venues on {map}: {all.Count} ({string.Join(", ", all.Select(v => v.DisplayNameOf() + "=" + VenueApi.Gate(v)))})";
            }
            var g = VenuesGame.Instance;
            if (g != null) yield return $"Big market: next in {Math.Max(0, g.NextMarketTick - Find.TickManager.TicksGame) / (float)GenDate.TicksPerDay:0.0} days, {g.MarketsHeld} held";
        }
    }

    /// <summary>
    /// Per-map housekeeping: keeps goods for sale off limits for the colony (N45) and, without the Inn module,
    /// offers idle vanilla visitors something to do.
    /// </summary>
    public sealed class VenueMapComp : MapComponent
    {
        HashSet<Thing> forbiddenByUs = new HashSet<Thing>();

        public VenueMapComp(Map map) : base(map) { }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref forbiddenByUs, "forbiddenByUs", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (forbiddenByUs == null) forbiddenByUs = new HashSet<Thing>();
                forbiddenByUs.RemoveWhere(t => t == null);
            }
        }

        public override void MapComponentTick()
        {
            if (!Modules.On(ModuleId.ShopsFood) || !map.IsPlayerHome) return;
            int t = Find.TickManager.TicksGame;
            if (t % PerfMode.Interval(250) == 40) Guard.Run(ModuleId.ShopsFood, "Venues.SyncForbidden", SyncForbidden);
            if (t % PerfMode.Interval(250) == 140) Guard.Run(ModuleId.ShopsFood, "Venues.Restock", SyncRestock);
            if (t % 250 == 190) Guard.Run(ModuleId.ShopsFood, "Venues.ShiftSweep", () => VenueService.Sweep(map));
            if (t % 90 == 0) Guard.Run(ModuleId.ShopsFood, "Venues.GuestFallback", () => GuestFallback.Run(map));
            if (t % 2500 == 2000) GuestMemory.Prune(id => !map.mapPawns.AllPawnsSpawned.Any(p => p.thingIDNumber == id));
        }

        /// <summary>Goods for sale are forbidden for the colony unless the sign allows taking them. Only what we forbade is ever unforbidden.</summary>
        public void SyncForbidden()
        {
            var shouldForbid = new HashSet<Thing>();
            foreach (var v in Venues.All(map))
            {
                if (VenueApi.SellsOf(v) == SellKind.None) continue;
                foreach (var it in VenueSales.Stock(v, true))
                    if (!v.Comp.colonyMayTakeGoods) shouldForbid.Add(it);
            }
            foreach (var it in shouldForbid)
            {
                var cf = it.TryGetComp<CompForbiddable>();
                if (cf == null || cf.Forbidden) continue;
                cf.Forbidden = true;
                forbiddenByUs.Add(it);
            }
            List<Thing> release = null;
            foreach (var it in forbiddenByUs)
            {
                if (it == null || it.Destroyed) { (release ?? (release = new List<Thing>())).Add(it); continue; }
                if (shouldForbid.Contains(it)) continue;
                var cf = it.TryGetComp<CompForbiddable>();
                if (cf != null && cf.Forbidden) cf.Forbidden = false;
                (release ?? (release = new List<Thing>())).Add(it);
            }
            if (release != null) foreach (var it in release) forbiddenByUs.Remove(it);
        }

        /// <summary>"Restock" signs keep the storage of their room in line (N45).</summary>
        public void SyncRestock()
        {
            foreach (var v in Venues.All(map))
                if (v.Comp is CompVenueSignHH c && c.restockOn) c.SyncRestock();
        }

        /// <summary>Release everything we forbade (module removal, tests).</summary>
        public int ReleaseAll()
        {
            int n = 0;
            foreach (var it in forbiddenByUs)
            {
                if (it == null || it.Destroyed) continue;
                var cf = it.TryGetComp<CompForbiddable>();
                if (cf != null && cf.Forbidden) { cf.Forbidden = false; n++; }
            }
            forbiddenByUs.Clear();
            return n;
        }

        public int ForbiddenCount => forbiddenByUs.Count;
        public bool IsForbiddenByUs(Thing t) => forbiddenByUs.Contains(t);
    }
}
