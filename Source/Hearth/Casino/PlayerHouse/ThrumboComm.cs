// SPDX-License-Identifier: MIT
using Silverline;
using UnityEngine;
using Verse;

namespace Hearth.Casino.PlayerHouse
{
    /// <summary>"Call The Lucky Thrumbo" in the comms console menu. A normal communicable target like the bank's, so the vanilla job walks the colonist to the console first.</summary>
    public sealed class ThrumboComm : ConsoleComm
    {
        public static readonly ThrumboComm Instance = new ThrumboComm();
        public override string GetCallLabel() => "HH.Thrumbo.Comm.Call".Translate();
        public override string GetInfoText() => "HH.Thrumbo.Comm.Info".Translate();
        public override void TryOpenComms(Pawn negotiator) => Window_Thrumbo.Open(negotiator);

        /// <summary>F34: a square thrumbo-and-chip mark (the wide wordmark would be unreadable in the 24 px icon slot).</summary>
        public override Texture2D Icon => ThrumboIcons.Mark;
    }
}
