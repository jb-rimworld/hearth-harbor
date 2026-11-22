// SPDX-License-Identifier: MIT
using UnityEngine;
using Verse;

namespace Hearth.Kitchen
{
    /// <summary>The plate item: ThingWithComps with a soft tint per dish (KONZEPT 6.4: "Tönung je Gericht").</summary>
    public class DishPlate : ThingWithComps
    {
        CompDish dishComp;
        public override Color DrawColor
        {
            get
            {
                if (dishComp == null) dishComp = GetComp<CompDish>();
                var d = dishComp?.dish;
                return d != null ? d.Tint : base.DrawColor;
            }
        }
    }
}
