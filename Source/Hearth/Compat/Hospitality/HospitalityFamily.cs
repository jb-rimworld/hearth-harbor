// SPDX-License-Identifier: MIT
// Hospitality compatibility (KONZEPT 3.8, 12.6). Only names of other mods are used here (package ids, type and
// namespace names for save compatibility). No code from Hospitality or its add-ons (GPL) is copied.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace Hearth.Compat.Hospitality
{
    /// <summary>How Hearth &amp; Harbor treats a related mod that runs alongside it.</summary>
    public enum FamilyRole
    {
        /// <summary>Hospitality itself or one of its add-ons: taken over (patches removed, content hidden) and offered for disabling.</summary>
        Replaced,
        /// <summary>A translation or patch of a replaced mod: no code of its own worth taking over, offered for disabling.</summary>
        Companion,
        /// <summary>A separate guest mod (Hotels and Recreation, Tourism, RimSim Hospitality): named in the dialog, never gutted (12.6).</summary>
        MentionOnly,
    }

    public sealed class FamilyMod
    {
        public string PackageId;     // lower case
        public string Name;          // English display name used when the mod is not installed
        public FamilyRole Role;
        public string WorkshopId;
        public FamilyMod(string id, string name, FamilyRole role, string workshop = null) { PackageId = id.ToLowerInvariant(); Name = name; Role = role; WorkshopId = workshop; }
    }

    /// <summary>A family mod that is running in this session.</summary>
    public sealed class DetectedMod
    {
        public FamilyMod Entry;
        public ModContentPack Pack;
        public readonly List<Assembly> Assemblies = new List<Assembly>();
        public string Label => Pack?.Name ?? Entry.Name;
        public override string ToString() => Label + " (" + Entry.PackageId + ")";
    }

    /// <summary>
    /// The Hospitality family as far as Hearth &amp; Harbor knows it (research h01–h11, facts/*.json).
    /// Detection compares running mods by package id (Steam "_steam" postfix ignored), never by Harmony id (12.6).
    /// </summary>
    public static class HospitalityFamily
    {
        public static readonly List<FamilyMod> Known = new List<FamilyMod>
        {
            // Hospitality and its add-ons (taken over)
            new FamilyMod("Orion.Hospitality", "Hospitality", FamilyRole.Replaced, "3509486825"),
            new FamilyMod("Orion.CashRegister", "Cash Register", FamilyRole.Replaced, "3509487668"),
            new FamilyMod("Orion.Gastronomy", "Gastronomy", FamilyRole.Replaced, "3509488152"),
            new FamilyMod("Adamas.HospitalityCasino", "Hospitality: Casino", FamilyRole.Replaced, "2939292644"),
            new FamilyMod("Adamas.HospitalitySpa", "Hospitality: Spa", FamilyRole.Replaced, "2971831654"),
            new FamilyMod("Adamas.Storefront", "Hospitality: Storefront", FamilyRole.Replaced, "2952321484"),
            new FamilyMod("Adamas.VendingMachines", "Hospitality: Vending machines", FamilyRole.Replaced, "3014885065"),
            new FamilyMod("gatoviejo.hospitality.invitetostay", "Hospitality - Invite to Stay", FamilyRole.Replaced, "3783522506"),
            new FamilyMod("ledronas.hospitalityroomservice", "Hospitality: Room Service", FamilyRole.Replaced, "3756771316"),
            new FamilyMod("Hospitality.SilverPatch", "Hospitality Silver Patch", FamilyRole.Replaced, "3768948312"),
            new FamilyMod("GentleCode.VisitorSpot", "Visitor Spot", FamilyRole.Replaced, "3484140623"),
            new FamilyMod("fumblesneeze.guestbedgizmo", "Hospitality + Ideology Patch", FamilyRole.Replaced, "3789536584"),
            // patches, retextures, progression and translation packs of the above (no own logic worth keeping)
            new FamilyMod("ferny.progressionhospitality", "Progression: Hospitality", FamilyRole.Companion, "3265298940"),
            new FamilyMod("cruesoe.progressionlesshospitality", "ProgressionLESS: Hospitality", FamilyRole.Companion, "3839211560"),
            new FamilyMod("mewn.cashregister", "Cash Register Retexture", FamilyRole.Companion, "2957271002"),
            new FamilyMod("vendingmachines.patch", "Vending machines: Deep Storage Patch", FamilyRole.Companion, "3235824337"),
            new FamilyMod("katsukiri.hvendingmachineadaptivepatch", "Vending Machines (Adaptive Storage Patch)", FamilyRole.Companion, "3319084133"),
            new FamilyMod("captainsnafu.hospitalitychinese", "Hospitality Chinese Complement", FamilyRole.Companion, "1940304848"),
            new FamilyMod("rwzh.chinesepack.hospitality", "Hospitality zh-pack", FamilyRole.Companion, "2029336674"),
            new FamilyMod("pl.hospitality.pl", "Hospitality [PL]", FamilyRole.Companion, "2308821188"),
            new FamilyMod("meandssh.rs.hospitality", "Hospitality (section header)", FamilyRole.Companion, "3543309465"),
            // other guest mods: named, not gutted (12.6, last paragraph)
            new FamilyMod("robtrg.hotelsandrecreation", "Hotels and Recreation", FamilyRole.MentionOnly, "3814336747"),
            new FamilyMod("local.tourism", "Tourism", FamilyRole.MentionOnly, "3810806777"),
            new FamilyMod("yourname.myinnmod", "RimSim Hospitality", FamilyRole.MentionOnly, "3825844200"),
        };

        /// <summary>Namespaces of the replaced mods' assemblies; used by the save importer to recognise their classes.</summary>
        public static readonly string[] SaveNamespaces =
        {
            "Hospitality.", "HospitalityCasino.", "HospitalitySpa.", "VendingMachines.", "Storefront.", "CashRegister.", "Gastronomy.", "HospitalityInviteToStay.",
        };

        public static bool IsFamilyClassName(string cls)
        {
            if (string.IsNullOrEmpty(cls)) return false;
            for (int i = 0; i < SaveNamespaces.Length; i++) if (cls.StartsWith(SaveNamespaces[i], StringComparison.Ordinal)) return true;
            return false;
        }

        static List<DetectedMod> detected;
        static HashSet<Assembly> takeoverAssemblies;

        /// <summary>Strips the "_steam" / "_copy" postfixes vanilla adds for duplicate installs.</summary>
        public static string NormalizeId(string packageId)
        {
            if (packageId == null) return "";
            string id = packageId.ToLowerInvariant();
            if (id.EndsWith("_steam")) id = id.Substring(0, id.Length - 6);
            if (id.EndsWith("_copy")) id = id.Substring(0, id.Length - 5);
            return id;
        }

        public static FamilyMod Lookup(string packageId)
        {
            string id = NormalizeId(packageId);
            for (int i = 0; i < Known.Count; i++) if (Known[i].PackageId == id) return Known[i];
            return null;
        }

        /// <summary>All family mods running in this session (any role). Cached after the first call.</summary>
        public static List<DetectedMod> Detected
        {
            get
            {
                if (detected != null) return detected;
                var list = new List<DetectedMod>();
                try
                {
                    foreach (var pack in LoadedModManager.RunningModsListForReading)
                    {
                        var e = Lookup(pack.PackageId);
                        if (e == null) continue;
                        var d = new DetectedMod { Entry = e, Pack = pack };
                        try
                        {
                            foreach (var a in pack.assemblies.loadedAssemblies)
                            {
                                string n = a.GetName().Name;
                                // never treat shared libraries as family code
                                if (n == "0Harmony" || n == "HarmonyMod" || n.StartsWith("System", StringComparison.Ordinal)) continue;
                                d.Assemblies.Add(a);
                            }
                        }
                        catch (Exception ex) { HLog.Warn("Compat", "Could not list assemblies of " + pack.PackageId + ": " + ex.Message); }
                        list.Add(d);
                    }
                }
                catch (Exception ex) { HLog.Error("Compat", "Hospitality family detection failed", ex); }
                detected = list;
                return detected;
            }
        }

        /// <summary>Re-reads the running mod list (tests only; the list never changes during a session).</summary>
        public static void ResetCache() { detected = null; takeoverAssemblies = null; takeoverAssemblyNames = null; }

        public static IEnumerable<DetectedMod> DetectedWithRole(FamilyRole r) => Detected.Where(d => d.Entry.Role == r);

        /// <summary>True if Hospitality or one of its add-ons runs alongside us (the takeover case of 12.6).</summary>
        public static bool AnyReplacedActive => Detected.Any(d => d.Entry.Role == FamilyRole.Replaced);

        /// <summary>Mods the dialog offers to disable (replaced + companions).</summary>
        public static List<DetectedMod> OfferedForDisabling => Detected.Where(d => d.Entry.Role != FamilyRole.MentionOnly).ToList();

        /// <summary>Assemblies whose Harmony patches and injected content are taken over (replaced mods only).</summary>
        public static HashSet<Assembly> TakeoverAssemblies
        {
            get
            {
                if (takeoverAssemblies != null) return takeoverAssemblies;
                var set = new HashSet<Assembly>();
                foreach (var d in Detected) if (d.Entry.Role == FamilyRole.Replaced) foreach (var a in d.Assemblies) set.Add(a);
                // never ourselves, whatever happens
                set.Remove(typeof(HospitalityFamily).Assembly);
                set.Remove(typeof(HearthMod).Assembly);
                takeoverAssemblies = set;
                return set;
            }
        }

        public static HashSet<ModContentPack> TakeoverPacks => new HashSet<ModContentPack>(Detected.Where(d => d.Entry.Role == FamilyRole.Replaced).Select(d => d.Pack));

        public static bool IsTakeoverType(Type t) => t != null && IsTakeoverAssembly(t.Assembly);

        static HashSet<string> takeoverAssemblyNames;
        /// <summary>
        /// By object, or by full assembly name: on 1.7 (CoreCLR) Harmony can hand back patch methods whose Assembly object is not
        /// the one the mod loader holds, so the name is compared as well.
        /// </summary>
        public static bool IsTakeoverAssembly(Assembly a)
        {
            if (a == null) return false;
            var set = TakeoverAssemblies;
            if (set.Contains(a)) return true;
            if (takeoverAssemblyNames == null) takeoverAssemblyNames = new HashSet<string>(set.Select(x => x.FullName));
            return takeoverAssemblyNames.Contains(a.FullName);
        }

        public static bool IsTakeoverDef(Def d)
        {
            if (d == null) return false;
            var p = d.modContentPack;
            if (p == null) return false;
            var e = Lookup(p.PackageId);
            return e != null && e.Role == FamilyRole.Replaced;
        }
    }
}
