// SPDX-License-Identifier: MIT
using RimWorld;
using UnityEngine;
using Verse;

namespace Hearth.Underworld
{
    /// <summary>Package-owned setup hooks of the fight pit: settings registered from UnderworldModule.RegisterSettings, def lookups from OnDefsLoaded, gizmo icons.</summary>
    public static class PitSetup
    {
        public static JobDef JobGoto, JobFight, JobWait;
        public static HediffDef Bruise;

        public static void RegisterSettings(ModuleId m)
        {
            HSettings.Silver(m, "Fight pit", "underworld.pit.guestPurse", 25, 0, 200, "Purse for a winning guest",
                "A guest who wins a bout gets this much silver, paid out of the house take of that bout (no take, no purse).");
            HSettings.Silver(m, "Fight pit", "underworld.pit.opponentFee", 40, 0, 500, "Fee for a hired opponent",
                "\"Call an opponent\" hires a pit beast for a lone fighter. The colony pays this fee: from the till first, then silver in stock, then the bank.");
            HSettings.Float(m, "Fight pit", "underworld.pit.scoreLimit", 100f, 20f, 400f, "Points to lose a bout",
                "Bouts with a guest, colonist or slave are fought for points: a fighter loses when the damage he would have taken reaches this many points times his health scale. Nobody is hurt.",
                nerd: true, hardMin: 5f, hardMax: 5000f);
        }

        public static void OnDefsLoaded()
        {
            JobGoto = DefDatabase<JobDef>.GetNamedSilentFail("HH_PitGoto");
            JobFight = DefDatabase<JobDef>.GetNamedSilentFail("HH_PitFight");
            JobWait = DefDatabase<JobDef>.GetNamedSilentFail("HH_PitWait");
            Bruise = DefDatabase<HediffDef>.GetNamedSilentFail("Bruise");
            if (JobGoto == null || JobFight == null || JobWait == null) HLog.Warn("Underworld", "Fight pit job defs missing: bouts cannot be fought");
            // a bout must not outlive the module: with the module off the damage guard stands aside, so the fighters would hit for real
            HearthGame.OnGameInit += FightBout.ForgetRecent;
            ModuleGate.Changed += _ => Guard.Run(ModuleId.Underworld, "Pit.ModuleSwitch", () => { if (!Modules.On(ModuleId.Underworld) && FightBout.ActiveCount > 0) FightBout.AbortAll("module switched off"); });
        }

        public static bool IsPitJob(JobDef d) => d != null && (d == JobGoto || d == JobFight || d == JobWait);

        // ---- gizmo icons (held by a [StaticConstructorOnStartup] class: the game loads them on the main thread at startup) ----------
        public static Texture2D IconOpen => PitIcons.Open;
        public static Texture2D IconAnimals => PitIcons.Animals;
        public static Texture2D IconGuests => PitIcons.Guests;
        public static Texture2D IconPrisoners => PitIcons.Prisoners;
        public static Texture2D IconLethal => PitIcons.Lethal;
        public static Texture2D IconFighters => PitIcons.Fighters;
        public static Texture2D IconStart => PitIcons.Start;
        public static Texture2D IconOpponent => PitIcons.Opponent;
    }

    [StaticConstructorOnStartup]
    static class PitIcons
    {
        static Texture2D Get(string path) => ContentFinder<Texture2D>.Get(path, false) ?? TexCommand.Attack;
        public static readonly Texture2D Open = Get("UI/HH/Underworld/PitOpen"), Animals = Get("UI/HH/Underworld/PitAnimals"), Guests = Get("UI/HH/Underworld/PitGuests"),
            Prisoners = Get("UI/HH/Underworld/PitPrisoners"), Lethal = Get("UI/HH/Underworld/PitLethal"), Fighters = Get("UI/HH/Underworld/PitFighters"),
            Start = Get("UI/HH/Underworld/PitStart"), Opponent = Get("UI/HH/Underworld/PitOpponent");
    }
}
