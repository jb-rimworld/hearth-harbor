// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Hearth.Underworld
{
    public enum UwOfferKind { Smuggler = 0, Protection = 1 }

    /// <summary>One line of a smuggler's crate, kept as a recipe: the things are made only when the player buys (nothing unspawned sits in the save).</summary>
    public sealed class UwGoodsLine : IExposable
    {
        public ThingDef def, stuff;
        public int count = 1;
        /// <summary>QualityCategory as int, -1 = none.</summary>
        public int quality = -1;

        public static UwGoodsLine Of(Thing t)
        {
            var l = new UwGoodsLine { def = t.def, stuff = t.Stuff, count = Math.Max(1, t.stackCount) };
            if (t.TryGetQuality(out var q)) l.quality = (int)q;
            return l;
        }

        /// <summary>The stuff to make the thing from: the saved one when it still fits the def (a mod may have changed it), else the default; null when none fits.</summary>
        ThingDef UsableStuff()
        {
            if (def == null || !def.MadeFromStuff) return null;
            if (stuff != null && stuff.IsStuff && stuff.stuffProps.CanMake(def)) return stuff;
            return GenStuff.DefaultStuffFor(def);
        }

        /// <summary>The line can still be made (its def exists and, if it needs stuff, some stuff fits).</summary>
        public bool CanMake => def != null && (!def.MadeFromStuff || UsableStuff() != null);

        public Thing Make()
        {
            if (!CanMake) return null;
            var t = ThingMaker.MakeThing(def, UsableStuff());
            t.stackCount = Math.Max(1, Math.Min(count, def.stackLimit));
            if (quality >= 0) t.TryGetComp<CompQuality>()?.SetQuality((QualityCategory)quality, ArtGenerationContext.Outsider);
            return t;
        }

        public void ExposeData()
        {
            Scribe_Defs.Look(ref def, "def");
            Scribe_Defs.Look(ref stuff, "stuff");
            Scribe_Values.Look(ref count, "count", 1);
            Scribe_Values.Look(ref quality, "quality", -1);
        }
    }

    /// <summary>
    /// An offer of the underworld that waits for the player's answer (1.2.0: no dialog). Saved in <see cref="UnderworldComponent"/>; announced by a
    /// vanilla standard letter; answered with the accept and decline gizmos on the dock (smuggler) or the gang leader (protection), or on the
    /// Underworld tab. An offer that runs out counts as declined. Before <see cref="openTick"/> it is a plan (a demand the gang makes later) and nobody sees it.
    /// </summary>
    public sealed class UwOffer : IExposable
    {
        public int id;
        public UwOfferKind kind;
        public int mapId = -1;
        public Faction faction;
        public Pawn leader;
        public Money price, marketValue;
        public List<UwGoodsLine> goods = new List<UwGoodsLine>();
        public int openTick, expireTick = -1, letterId = -1;

        public bool Announced => expireTick >= 0;
        public Map Map { get { var maps = Find.Maps; for (int i = 0; i < maps.Count; i++) if (maps[i].uniqueID == mapId) return maps[i]; return null; } }
        public int GoodsCount { get { int n = 0; foreach (var g in goods) n += g.count; return n; } }
        internal bool LoadedOk => kind == UwOfferKind.Smuggler ? goods.Count > 0 : faction != null;

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id");
            Scribe_Values.Look(ref kind, "kind");
            Scribe_Values.Look(ref mapId, "map", -1);
            Scribe_References.Look(ref faction, "faction");
            Scribe_References.Look(ref leader, "leader");
            ScribeMoney.Look(ref price, "price");
            ScribeMoney.Look(ref marketValue, "marketValue");
            Scribe_Collections.Look(ref goods, "goods", LookMode.Deep);
            Scribe_Values.Look(ref openTick, "open");
            Scribe_Values.Look(ref expireTick, "expire", -1);
            Scribe_Values.Look(ref letterId, "letter", -1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (goods == null) goods = new List<UwGoodsLine>();
                goods.RemoveAll(g => g == null || !g.CanMake);
            }
        }
    }

    /// <summary>Open offers: creation, letters, answers, expiry. Ticked every 250 ticks from <see cref="UwTicker"/>.</summary>
    public static class Offers
    {
        /// <summary>"1 item", "3 items" for the smuggler's texts.</summary>
        public static string Items(int n) => UwPay.T(n == 1 ? "Smuggler.Items.One" : "Smuggler.Items.Many", n);

        static List<UwOffer> List => Underworld.Comp?.Offers;
        static int Now => Find.TickManager.TicksGame;

        public static int HoursFor(UwOfferKind k) => Math.Max(1, (int)Math.Round(HSettings.Get(k == UwOfferKind.Smuggler ? "underworld.smuggler.offerHours" : "underworld.gang.demandHours")));

        /// <summary>Announced offers that wait for an answer (optionally of one map). A new list (for the UI; not per tick).</summary>
        public static List<UwOffer> Open(Map map = null)
        {
            var r = new List<UwOffer>(); var l = List; if (l == null) return r;
            foreach (var o in l) if (o.Announced && (map == null || o.mapId == map.uniqueID)) r.Add(o);
            return r;
        }

        public static UwOffer OpenOf(UwOfferKind kind, Map map)
        {
            var l = List; if (l == null || map == null) return null;
            foreach (var o in l) if (o.kind == kind && o.Announced && o.mapId == map.uniqueID) return o;
            return null;
        }

        /// <summary>Any offer (announced or planned) of this kind on the map.</summary>
        public static bool Any(UwOfferKind kind, Map map)
        {
            var l = List; if (l == null || map == null) return false;
            foreach (var o in l) if (o.kind == kind && o.mapId == map.uniqueID) return true;
            return false;
        }

        /// <summary>The announced protection demand whose leader is this pawn (cheap; used by the gizmo patch).</summary>
        public static UwOffer OfLeader(Pawn p)
        {
            var l = List; if (l == null || l.Count == 0 || p == null) return null;
            for (int i = 0; i < l.Count; i++) if (l[i].kind == UwOfferKind.Protection && l[i].Announced && l[i].leader == p) return l[i];
            return null;
        }

        public static UwOffer Add(UwOffer o)
        {
            var c = Underworld.Comp; if (c == null || o == null) return null;
            o.id = c.NextOfferId++;
            c.Offers.Add(o);
            FacilityStatus.MarkDirty();
            return o;
        }

        /// <summary>Every 250 ticks: announces planned offers that are due, closes the ones that ran out (as declined) or lost their subject.</summary>
        public static void Tick(int now)
        {
            var l = List; if (l == null || l.Count == 0) return;
            for (int i = l.Count - 1; i >= 0; i--)
            {
                if (i >= l.Count) continue;
                var o = l[i];
                Guard.Run(ModuleId.Underworld, "Offers.Tick", () =>
                {
                    if (!Underworld.Unlocked || o.Map == null) { Close(o); return; }
                    if (!o.Announced)
                    {
                        if (now < o.openTick) return;
                        if (StillValid(o)) Announce(o); else Close(o);
                        return;
                    }
                    if (!StillValid(o)) { Close(o); Messages.Message(UwPay.T("Offer.Gone"), MessageTypeDefOf.NeutralEvent, false); return; }
                    if (now >= o.expireTick) Decline(o, expired: true);
                });
            }
        }

        static bool StillValid(UwOffer o)
        {
            var map = o.Map; if (map == null) return false;
            if (o.kind == UwOfferKind.Smuggler) { if (Smuggler.Dock(map) == null) return false; foreach (var g in o.goods) if (g.CanMake) return true; return false; }
            var p = o.leader;
            return p != null && !p.Dead && p.Spawned && p.Map == map && o.faction != null && !o.faction.defeated && !o.faction.HostileTo(Faction.OfPlayer)
                   && Underworld.Allows(UwFeature.ProtectionRacket);
        }

        /// <summary>Makes the offer visible: a vanilla standard letter that jumps to the dock or the gang leader. Starts the clock.</summary>
        public static void Announce(UwOffer o)
        {
            var map = o.Map; if (map == null) return;
            int hours = HoursFor(o.kind);
            o.expireTick = Now + hours * GenDate.TicksPerHour;
            string label, text; LookTargets look;
            if (o.kind == UwOfferKind.Smuggler)
            {
                var dock = Smuggler.Dock(map);
                label = UwPay.T("Smuggler.Title");
                text = UwPay.T("Smuggler.Text", Offers.Items(o.GoodsCount), o.marketValue.ToStringShort(), o.price.ToStringShort()) + "\n\n" + UwPay.T("Smuggler.Letter.How", hours);
                look = dock != null ? new LookTargets(new TargetInfo(dock.Position, map)) : LookTargets.Invalid;
                Underworld.Comp?.NoteEvent(FacilityStatus.DockId);
            }
            else
            {
                label = UwPay.T("Gang.Demand.Title");
                text = UwPay.T("Gang.Demand.Text", o.faction?.Name ?? "?", o.price.ToStringShort()) + "\n\n" + UwPay.T("Gang.Demand.Letter.How", o.leader.LabelShortCap, hours);
                look = new LookTargets(o.leader);
            }
            var let = LetterMaker.MakeLetter(label, text, LetterDefOf.NeutralEvent, look, o.faction);
            Find.LetterStack.ReceiveLetter(let);
            o.letterId = let.ID;
            FacilityStatus.MarkDirty();
            HLog.Info("Underworld", $"Offer {o.id} ({o.kind}) announced, {o.price.ToStringShort()} silver, open for {hours} h");
        }

        public static bool CanAccept(UwOffer o, out string why)
        {
            why = null; var map = o?.Map;
            // a stale reference (the tab caches its rows) to an offer that was answered, ran out or was closed is gone too
            if (o == null || map == null || !o.Announced || !IsOpen(o) || !StillValid(o)) { why = UwPay.T("Offer.Gone"); return false; }
            if (UwPay.ColonyFunds(map) < o.price) { why = UwPay.ShortOf(map, o.price); return false; }
            return true;
        }

        /// <summary>The player says yes: buys the crate or pays the gang. Returns false with the reason when it cannot be done now.</summary>
        public static bool Accept(UwOffer o, out string why)
        {
            if (!CanAccept(o, out why)) return false;
            var map = o.Map;
            bool ok;
            if (o.kind == UwOfferKind.Smuggler) ok = Smuggler.Buy(map, o, out why);
            else { ok = Gangs.PayProtection(map, o.faction, o.price); if (!ok) why = UwPay.ShortOf(map, o.price); }
            if (!ok)
            {
                // nothing of the crate can be made any more (a removed mod): the offer is over, not short of cash
                if (why == UwPay.T("Offer.Gone")) Close(o);
                return false;
            }
            Close(o);
            Messages.Message(UwPay.T(o.kind == UwOfferKind.Smuggler ? "Smuggler.Bought" : "Gang.Demand.Paid", o.price.ToStringShort()),
                o.kind == UwOfferKind.Smuggler ? new LookTargets(new TargetInfo(Smuggler.Dock(map)?.Position ?? IntVec3.Invalid, map)) : new LookTargets(o.leader), MessageTypeDefOf.NeutralEvent, false);
            return true;
        }

        /// <summary>The player says no, or the time ran out: the boat leaves (nothing else), the gang takes the refusal as refusals were always taken.</summary>
        public static void Decline(UwOffer o, bool expired = false)
        {
            if (o == null || !IsOpen(o)) return;   // answered or closed already: a second refusal must not cost goodwill again
            var map = o.Map;
            if (o.kind == UwOfferKind.Protection && map != null && o.faction != null) Gangs.RefuseProtection(map, o.faction);
            Close(o);
            string key = o.kind == UwOfferKind.Smuggler ? (expired ? "Smuggler.Left" : "Smuggler.Declined") : (expired ? "Gang.Demand.Expired" : "Gang.Demand.Refused");
            Messages.Message(UwPay.T(key), o.kind == UwOfferKind.Protection && o.leader != null && o.leader.Spawned ? new LookTargets(o.leader) : LookTargets.Invalid,
                MessageTypeDefOf.NeutralEvent, false);
        }

        /// <summary>The offer is still in the list of offers (not answered, run out or closed).</summary>
        public static bool IsOpen(UwOffer o) { var l = List; return o != null && l != null && l.Contains(o); }

        /// <summary>Removes the offer and its letter (if the letter is still in the stack). A held reference then reads as not announced.</summary>
        public static void Close(UwOffer o)
        {
            if (o == null) return;
            List?.Remove(o);
            o.expireTick = -1;
            if (o.letterId >= 0 && Find.LetterStack != null)
            {
                var ls = Find.LetterStack.LettersListForReading;
                for (int i = ls.Count - 1; i >= 0; i--) if (ls[i].ID == o.letterId) { Find.LetterStack.RemoveLetter(ls[i]); break; }
            }
            FacilityStatus.MarkDirty();
        }

        public static string TimeLeft(UwOffer o) => Math.Max(0, o.expireTick - Now).ToStringTicksToPeriod();

        /// <summary>The one-line summary of an offer (dock inspect string, Underworld tab).</summary>
        public static string Line(UwOffer o)
        {
            if (o.kind == UwOfferKind.Smuggler) return UwPay.T("Smuggler.Waiting", Offers.Items(o.GoodsCount), o.price.ToStringShort(), TimeLeft(o));
            return UwPay.T("Gang.Demand.Waiting", o.leader?.LabelShortCap ?? "?", o.price.ToStringShort(), TimeLeft(o));
        }

        /// <summary>Accept and decline buttons for one announced offer.</summary>
        public static IEnumerable<Gizmo> Gizmos(UwOffer o)
        {
            bool smug = o.kind == UwOfferKind.Smuggler;
            var accept = new Command_Action
            {
                defaultLabel = UwPay.T(smug ? "Smuggler.Buy" : "Gang.Demand.Pay", o.price.ToStringShort()),
                defaultDesc = Line(o) + "\n\n" + UwPay.T(smug ? "Smuggler.Buy.Desc" : "Gang.Demand.Pay.Desc"),
                icon = OfferIcons.Accept,
                action = () => { if (!Accept(o, out string why)) Messages.Message(why, MessageTypeDefOf.RejectInput, false); }
            };
            if (!CanAccept(o, out string reason)) accept.Disable(reason);
            yield return accept;
            yield return new Command_Action
            {
                defaultLabel = UwPay.T(smug ? "Smuggler.Decline" : "Gang.Demand.Refuse"),
                defaultDesc = UwPay.T(smug ? "Smuggler.Decline.Desc" : "Gang.Demand.Refuse.Desc"),
                icon = OfferIcons.Decline,
                action = () => Decline(o)
            };
        }
    }

    [StaticConstructorOnStartup]
    static class OfferIcons
    {
        public static readonly Texture2D Accept = ContentFinder<Texture2D>.Get("UI/Commands/Trade", false) ?? ThingDefOf.Silver.uiIcon ?? BaseContent.BadTex;
        public static readonly Texture2D Decline = ContentFinder<Texture2D>.Get("UI/Designators/Cancel", false) ?? BaseContent.BadTex;
        public static readonly Texture2D TakeOut = ContentFinder<Texture2D>.Get("UI/Commands/PodEject", false) ?? BaseContent.BadTex;
    }

    /// <summary>Accept and decline gizmos on the gang leader who made a protection demand (a visitor, so it needs a patch). One list lookup when nothing is open.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_PawnGizmos_Offer
    {
        public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!Modules.On(ModuleId.Underworld) || __result == null) return;
            var o = Offers.OfLeader(__instance); if (o == null) return;
            __result = Append(__result, o);
        }

        static IEnumerable<Gizmo> Append(IEnumerable<Gizmo> list, UwOffer o)
        {
            foreach (var g in list) yield return g;
            foreach (var g in Offers.Gizmos(o)) yield return g;
        }
    }
}
