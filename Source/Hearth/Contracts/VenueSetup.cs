// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using Verse;

namespace Hearth
{
    /// <summary>
    /// What a venue type needs to work (F12, 1.1.0): XML data on a <see cref="VenueTypeDef"/>. Each module puts the extension on its
    /// own venue types, so a module that is copied elsewhere (N07) brings its checklist along. Evaluated by Hearth.ShopsFood.VenueSetup;
    /// required structural items that are missing make the venue "Not ready" (it does not open) unless the player switched that off.
    /// <code>
    /// &lt;li Class="Hearth.VenueSetupExtension"&gt;
    ///   &lt;requirements&gt;
    ///     &lt;li&gt;&lt;id&gt;game&lt;/id&gt;&lt;label&gt;a game&lt;/label&gt;&lt;check&gt;Things&lt;/check&gt;&lt;compProps&gt;Hearth.Casino.CompProperties_CasinoGame&lt;/compProps&gt;&lt;/li&gt;
    ///   &lt;/requirements&gt;
    /// &lt;/li&gt;
    /// </code>
    /// </summary>
    public class VenueSetupExtension : DefModExtension
    {
        public List<VenueRequirement> requirements = new List<VenueRequirement>();

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (var e in base.ConfigErrors()) yield return e;
            if (requirements == null) yield break;
            var ids = new HashSet<string>();
            foreach (var r in requirements)
            {
                if (r == null) { yield return "null requirement"; continue; }
                if (r.id.NullOrEmpty()) yield return "requirement without id";
                else if (!ids.Add(r.id)) yield return "duplicate requirement id " + r.id;
                if (r.label.NullOrEmpty()) yield return "requirement " + r.id + " has no label";
                if (r.check == VenueCheck.Things && r.things.NullOrEmpty() && r.compProps == null && r.thingClass == null) yield return "requirement " + r.id + " (Things) matches nothing";
                if (r.check == VenueCheck.Custom && (r.worker == null || !typeof(IVenueSetupCheck).IsAssignableFrom(r.worker))) yield return "requirement " + r.id + " (Custom) needs a worker implementing Hearth.IVenueSetupCheck";
            }
        }
    }

    /// <summary>How a requirement is counted in the venue's room.</summary>
    public enum VenueCheck
    {
        /// <summary>Buildings matching <c>things</c>, <c>compProps</c> or <c>thingClass</c>.</summary>
        Things,
        /// <summary>Anything one can sit on (building.isSittable): chairs, stools, armchairs, sofas, benches.</summary>
        Seats,
        /// <summary>Eating surfaces (surfaceType Eat): tables of any mod.</summary>
        Tables,
        /// <summary>Storage places of the room: stockpile zones and shelves.</summary>
        Storage,
        /// <summary>Buildings with art (CompArt): sculptures.</summary>
        Art,
        /// <summary>Seats in the watch area of buildings matching <c>things</c> (cinema screens).</summary>
        SeatsFacing,
        /// <summary>Everything matching <c>things</c>/<c>compProps</c> that uses power is powered. Shown only when such things stand in the room. Never blocks opening.</summary>
        Power,
        /// <summary>Goods/drinks/plates for sale in the room (follows what the type sells). Never blocks opening (an empty venue just sells nothing).</summary>
        Stock,
        /// <summary>A colonist has the Serving work type switched on. Never blocks opening (staffing has its own rule).</summary>
        ServingWorker,
        /// <summary>Asks <c>worker</c> (an <see cref="IVenueSetupCheck"/>).</summary>
        Custom,
    }

    /// <summary>One line of a venue's setup checklist.</summary>
    public class VenueRequirement
    {
        /// <summary>Stable id (tests, guide).</summary>
        public string id;
        /// <summary>Short noun phrase: "a counter", "seats", "a projector screen".</summary>
        [MustTranslate] public string label;
        /// <summary>One sentence: why it is needed or what it does (tooltip).</summary>
        [MustTranslate] public string hint;
        public VenueCheck check = VenueCheck.Things;
        /// <summary>Required items are listed first; missing structural ones make the venue "Not ready". Others are recommendations.</summary>
        public bool required = true;
        public int min = 1;
        /// <summary>Optional HSettings int key whose value replaces <c>min</c> (e.g. city.cinemaMinSeats).</summary>
        public string minSetting;
        public List<ThingDef> things;
        /// <summary>CompProperties class a matching thing has (e.g. Hearth.Casino.CompProperties_CasinoGame).</summary>
        public Type compProps;
        /// <summary>Thing class (or a base class of it) a matching thing has.</summary>
        public Type thingClass;
        /// <summary>Things offered as build buttons; empty = <c>things</c>.</summary>
        public List<ThingDef> build;
        /// <summary>For <see cref="VenueCheck.Custom"/>.</summary>
        public Type worker;
        /// <summary>Only needed while a colonist serves here (the tender's post): a venue on the honor system (unattended) does without it.</summary>
        public bool staffOnly;

        [Unsaved] IVenueSetupCheck workerInt;
        public IVenueSetupCheck Worker
        {
            get
            {
                if (workerInt == null && worker != null && typeof(IVenueSetupCheck).IsAssignableFrom(worker)) workerInt = (IVenueSetupCheck)Activator.CreateInstance(worker);
                return workerInt;
            }
        }

        /// <summary>Does a missing item of this kind keep the venue closed? Stock, staff and power are shown but never block.</summary>
        public bool Blocks => required && check != VenueCheck.Stock && check != VenueCheck.ServingWorker && check != VenueCheck.Power;

        public IEnumerable<ThingDef> BuildOptions => !build.NullOrEmpty() ? build : (IEnumerable<ThingDef>)(things ?? new List<ThingDef>());
    }

    /// <summary>Custom requirement check (VenueCheck.Custom). Return how many are there and how many are needed.</summary>
    public interface IVenueSetupCheck
    {
        /// <param name="have">what the room has</param>
        /// <param name="need">what it needs (usually r.min)</param>
        /// <param name="note">optional short extra text ("2 of 4 seats have chairs")</param>
        /// <returns>false when the check does not apply here (the row is hidden)</returns>
        bool Evaluate(Venue v, VenueRequirement r, out int have, out int need, out string note);
    }
}
