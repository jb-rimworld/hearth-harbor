// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Hearth.Underworld
{
    public enum ScandalKind
    {
        BackRoomFound,      // a respectable guest found the back room            → renown −, whispers +
        LeakedLedger,       // fencing came out                                     → renown −5, whispers +6
        SecretCasinoRumour, // positive: "rumour of a secret casino draws high rollers"
        HonourAmongThieves, // positive: a gang speaks well of the house
    }

    /// <summary>
    /// Scandals of the underworld (KONZEPT 5.7): they hang on real incidents plus a small random share; at most one per map every 10 days.
    /// Effects: Whispers, renown (via Underworld.RenownDelta, capped by the tier), mood of respectable guests present, a letter.
    /// </summary>
    public static class Scandals
    {
        public sealed class Result { public ScandalKind Kind; public float Whispers, Renown; public bool Raised; public string Why; }

        static readonly Dictionary<ScandalKind, (float renown, float whispers)> Table = new Dictionary<ScandalKind, (float, float)>
        {
            { ScandalKind.BackRoomFound, (-4f, +4f) },
            { ScandalKind.LeakedLedger, (-5f, +6f) },
            { ScandalKind.SecretCasinoRumour, (0f, +3f) },
            { ScandalKind.HonourAmongThieves, (+1f, +2f) },
        };

        public static (float renown, float whispers) Values(ScandalKind k) => Table[k];

        /// <summary>Raises a scandal on a map. With force the cooldown is ignored (tests, dev menu). Returns what happened.</summary>
        public static Result Raise(ScandalKind kind, Map map, bool force = false, string detail = null)
        {
            var res = new Result { Kind = kind };
            var comp = Underworld.Comp;
            if (comp == null || map == null) { res.Why = "no game"; return res; }
            if (!Underworld.ModuleActive) { res.Why = "module off"; return res; }
            int now = Find.TickManager.TicksGame;
            int cd = HSettings.GetInt("underworld.scandalCooldownDays") * 60000;
            if (!force && now - comp.ScandalTickOn(map) < cd) { res.Why = "cooldown"; return res; }

            var (renown, whispers) = Table[kind];
            float cappedRenown = Underworld.CapRenown(renown);
            comp.NoteScandal(map, now);
            comp.AddWhispers(whispers, "scandal " + kind);
            Underworld.RaiseRenown(map, cappedRenown, "scandal " + kind);
            res.Whispers = whispers; res.Renown = cappedRenown; res.Raised = true;

            // respectable guests present feel it; shady ones do not
            float sat = kind == ScandalKind.BackRoomFound ? -0.35f : kind == ScandalKind.LeakedLedger ? -0.2f : kind == ScandalKind.HonourAmongThieves ? 0.05f : 0f;
            if (Math.Abs(sat) > 0.001f)
                foreach (var g in GuestApi.GuestsOn(map).ToList())
                    if (!Underworld.IsShady(g)) GuestApi.AddSatisfaction(g, sat, ("HH.Underworld.Scandal." + kind + ".Title").Translate());
            if (kind == ScandalKind.SecretCasinoRumour) comp.HighRollersUntil = now + 3 * 60000;

            string title = ("HH.Underworld.Scandal." + kind + ".Title").Translate();
            string text = ("HH.Underworld.Scandal." + kind + ".Text").Translate() + (detail.NullOrEmpty() ? "" : "\n\n" + detail) +
                "\n\n" + "HH.Underworld.Scandal.Effects".Translate(whispers.ToString("+0.#;-0.#"), cappedRenown.ToString("+0.#;-0.#;0"));
            var lt = renown >= 0 ? LetterDefOf.PositiveEvent : LetterDefOf.NegativeEvent;
            Find.LetterStack.ReceiveLetter(title, text, lt);
            HLog.Info("Underworld", $"Scandal {kind} on map {map.uniqueID}: whispers {whispers:+0.#;-0.#}, renown {cappedRenown:+0.#;-0.#;0}");
            return res;
        }

        /// <summary>Checks that run every ~2 hours per home map: a respectable guest stumbles on the back room; a rumour about a secret casino.</summary>
        public static void Check(Map map)
        {
            var comp = Underworld.Comp; if (comp == null || !Underworld.Unlocked) return;
            int now = Find.TickManager.TicksGame;
            if (now - comp.ScandalTickOn(map) < HSettings.GetInt("underworld.scandalCooldownDays") * 60000) return;
            float p = HSettings.Get("underworld.exposureChance");
            if (p > 0f && BackRoom.HasBackRoom(map) && UwActivity.RecentUse(map) && BackRoom.RespectableWitnesses(map).Any() && JRand.Stream("underworld.scandal").Chance(p))
            { Raise(ScandalKind.BackRoomFound, map); return; }
            // positive: a secret casino whispered about draws high rollers (needs a casino venue and some whispers)
            var casino = Underworld.Def<VenueTypeDef>("HH_Venue_Casino");
            if (casino != null && Underworld.Whispers >= 20f && Venues.OfType(map, casino).Any() && !Underworld.HighRollersActive && JRand.Stream("underworld.scandal").Chance(0.05))
                Raise(ScandalKind.SecretCasinoRumour, map);
        }
    }

    /// <summary>"The back room was in use lately": set by every underworld activity, read by the scandal check. Per map, transient.</summary>
    public static class UwActivity
    {
        static readonly Dictionary<int, int> last = new Dictionary<int, int>();
        public static void Note(Map map) { if (map != null) last[map.uniqueID] = Find.TickManager.TicksGame; }
        public static bool RecentUse(Map map) => map != null && last.TryGetValue(map.uniqueID, out int t) && Find.TickManager.TicksGame - t < 60000;
    }
}
