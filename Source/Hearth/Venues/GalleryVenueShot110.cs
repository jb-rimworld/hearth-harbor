// SPDX-License-Identifier: MIT
// Gallery shot of the venue sign's Setup tab (explicit only): HH_TEST=gallery.venue
#if HH_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Hearth.Testing;
using Hearth.Showcase;

namespace Hearth.ShopsFood
{
    public sealed class GalleryVenueSuite : HearthTestSuite
    {
        public override string Name => "gallery.venue";
        public override ModuleId Module => ModuleId.ShopsFood;
        public override bool ExplicitOnly => true;
        public override int MaxTicks => 900000;

        public override IEnumerable<int> Run(TestContext t)
        {
            var map = t.Map; GalleryShot.Init();
            var room = VenueTestKit.Build(map, 7);
            var sign = VenueTestKit.SpawnSign(map, room, HHVenueDefOf.HH_VenueSign, HHVenueDefOf.HH_Venue_Tavern);
            var comp = sign.TryGetComp<CompVenueSignHH>();
            comp.alwaysOpen = false; comp.openHour = 16; comp.closeHour = 2;
            var r = room.Inner;
            V11Kit.Spawn(map, "Table2x2c", new IntVec3(r.minX + 2, 0, r.minZ + 2), Rot4.North);
            V11Kit.Spawn(map, "DiningChair", new IntVec3(r.minX + 1, 0, r.minZ + 2), Rot4.East);
            yield return 20;
            foreach (var c in room.Outer.ExpandedBy(3)) if (c.InBounds(map)) map.fogGrid.Unfog(c);
            var cam = sign.DrawPos + new Vector3(0f, 0f, -1f);
            GalleryShot.Tick = () => Find.CameraDriver.SetRootPosAndSize(cam, 14f);
            Find.Selector.ClearSelection(); Find.Selector.Select(sign, false, false);
            comp.OpenSetupTab();
            foreach (var w in GalleryShot.Take(t, "venue_setup", 1.2f)) yield return w;
            GalleryShot.Tick = null;
            t.Check(true, "shots taken");
        }
    }
}
#endif
