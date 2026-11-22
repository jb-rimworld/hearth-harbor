#!/usr/bin/env python3
# Generates Common/Languages/English/Keyed/HH_UI.xml and checks that every static "HH.UI.*" key used in Source/Hearth/UI exists.
# Usage: python3 tools/gen_ui_keyed.py Source/Hearth/UI Common/Languages/English/Keyed/HH_UI.xml
import re, sys
from xml.sax.saxutils import escape
K = {}
def a(k, v): K[k] = v

# --- pages / sidebar
a("HH.UI.Page.Overview","Overview"); a("HH.UI.Page.Guests","Guests"); a("HH.UI.Page.Modules","Modules"); a("HH.UI.Page.Difficulty","Difficulty")
a("HH.UI.Page.General","General"); a("HH.UI.Page.Diagnosis","Diagnosis"); a("HH.UI.Page.About","About")
a("HH.UI.Silver","silver")
a("HH.UI.Tab.Settings","Settings"); a("HH.UI.Tab.Guide","Guide"); a("HH.UI.Tab.GuideTip","Show or hide the little lesson panel at the top right.")
a("HH.UI.Tab.NoPages","Nothing to show yet.")
# --- settings window
a("HH.UI.Settings.ThisSave","This save"); a("HH.UI.Settings.Template","Template")
a("HH.UI.Settings.ThisSaveTip","Edit the settings of the running colony. They are saved with it.")
a("HH.UI.Settings.TemplateTip","Edit the template that new colonies start from. The running colony is not changed.")
a("HH.UI.Settings.TemplateOnly","Template for new games. Each new colony gets its own copy.")
a("HH.UI.Settings.EditingTemplate","Editing the template for new games. The running colony is not changed.")
a("HH.UI.Settings.SaveAsTemplate","Save colony as template"); a("HH.UI.Settings.SaveAsTemplateTip","Copies modules, difficulty and all settings of this colony into the template for new games.")
a("HH.UI.Settings.SaveAsTemplateConfirm","Replace the template for new games with the settings of this colony?")
a("HH.UI.Settings.ApplyTemplate","Apply template to colony"); a("HH.UI.Settings.ApplyTemplateTip","Replaces the settings of this colony with the template.")
a("HH.UI.Settings.ApplyTemplateConfirm","Replace modules, difficulty and all settings of this colony with the template? This cannot be undone.")
a("HH.UI.Settings.TemplateSaved","Template for new games saved."); a("HH.UI.Settings.TemplateApplied","Template applied to this colony.")
a("HH.UI.Settings.Search","Search settings..."); a("HH.UI.Settings.SearchResults","Search results"); a("HH.UI.Settings.NoMatch","No setting matches. Nerd settings are hidden unless you switch them on.")
a("HH.UI.Settings.Nerd","Show nerd settings"); a("HH.UI.Settings.NerdTip","Shows the settings meant for people who like numbers. Everything else is unchanged.")
a("HH.UI.Settings.Off","off"); a("HH.UI.Settings.Yes","yes"); a("HH.UI.Settings.No","no")
a("HH.UI.Settings.ResetModule","Reset module"); a("HH.UI.Settings.ResetModuleConfirm","Reset every setting of {0} to its default?")
a("HH.UI.Settings.ModuleIsOff","This module is switched off for this colony. Its settings have no effect."); a("HH.UI.Settings.SwitchOn","Switch on")
a("HH.UI.Row.On","on"); a("HH.UI.Row.Off","off"); a("HH.UI.Row.Free","Free"); a("HH.UI.Row.FreeTip","Free entry: type any number up to the technical limit of the game.")
a("HH.UI.Row.Reset","reset"); a("HH.UI.Row.Nerd","nerd"); a("HH.UI.Row.Beyond","Beyond the slider range. Free entry allows this.")
a("HH.UI.Row.Limited","limit {0}"); a("HH.UI.Row.Limit","Technical limit"); a("HH.UI.Row.LimitGeneric","The game cannot use this value, so the nearest possible one is used.")
a("HH.UI.Row.FollowsTier","The default follows the hosting difficulty.")
a("HH.UI.General.Interface","Interface"); a("HH.UI.General.ShowCard","Show the first-start card in every new game")
a("HH.UI.General.ShowCardTip","Off: new games quietly use the template and no card appears.")
a("HH.UI.General.Tutorial","Tutorial"); a("HH.UI.General.Verbose","Verbose log (HearthHarbor.log)")
a("HH.UI.Modules.IntroSave","Modules can be switched per colony. A module that is off has no visible effect at all.")
a("HH.UI.Modules.IntroTemplate","Modules for new colonies. Each colony can still change its own.")
a("HH.UI.Modules.NotInstalled","not part of this installation")
a("HH.UI.Modules.BankHint","Bank: use the bank inside Hearth & Harbor and switch all guest modules off. You do not need the standalone entry.")
a("HH.UI.Modules.Content","Content intensity"); a("HH.UI.Modules.ContentDesc","How dark the optional content may get. Never sexual content, whatever you pick.")
for c,(n,t) in {"Cozy":("Cozy","Mild stories and consequences only."),"Standard":("Standard","The default. Rough edges, nothing nasty."),"Dark":("Dark","Crime, debts and violence may show up."),"Grim":("Grim","Everything the game offers, in full.")}.items():
    a("HH.UI.Content."+c,n); a("HH.UI.Content."+c+".Tip",t)
a("HH.UI.Underworld.Discover","Discover in game"); a("HH.UI.Underworld.On","On"); a("HH.UI.Underworld.Off","Off")
a("HH.UI.Underworld.Discover.Tip","Off the radar until a shady stranger turns up in your game."); a("HH.UI.Underworld.On.Tip","Available from the start."); a("HH.UI.Underworld.Off.Tip","Never available. The shady stranger stays away too.")
mods = {"Inn":"Guests, guest beds, wings, reception, travellers and recruiting.","ShopsFood":"Venue signs, shops, vending, markets, restaurants and dishes.","Casino":"Tables, slot machines, chips and the risks of gambling for guests and colonists.","Wellness":"Spa, sauna and rehabilitation stays.","CityLife":"Cinema, mall, salon, temples, taxis and security.","Underworld":"A discreet back room, a fight pit and a fence. Found in game, never forced.","Bank":"Silverline: accounts, payments, ATMs, investments, loans, insurance and lottery.","AiFlavour":"Optional flavour texts written by an AI through a relay. Off by default."}
for k,v in mods.items(): a("HH.UI.ModuleDesc."+k,v)
# --- difficulty
a("HH.UI.Diff.Title","Hosting difficulty"); a("HH.UI.Diff.Intensity","Hosting intensity {0} %"); a("HH.UI.Diff.ChipTip","{0}: about {1} % hosting intensity.")
a("HH.UI.Diff.Calculator","What this tier means for you"); a("HH.UI.Diff.Ladder","What can go wrong"); a("HH.UI.Diff.Special","Special switches"); a("HH.UI.Diff.ShowAll","Show all {0} values that follow the difficulty")
a("HH.UI.Diff.WarnHard","From Overbooked on, real consequences are switched on: fines, licence loss and worse.")
a("HH.UI.Diff.WarnEasy","This tier is very forgiving. Money comes easy and nothing goes wrong.")
a("HH.UI.Diff.WarnBreaks","This breaks the game balance. On purpose?")
a("HH.UI.Calc.StandardIs","Standard Room: {0}"); a("HH.UI.Calc.Reference","reference value"); a("HH.UI.Calc.SetByYou","You changed this value yourself, so it does not follow the slider.")
a("HH.UI.Calc.NoEffectInfinite","No effect while Infinite guest money is on.")
for i,t in enumerate(["Everything is on the house.","Money flows and nothing goes wrong.","Gentle and generous.","Small losses are possible. Profit takes work.","Consequences switch on: quarrels, fines, licence loss.","Feuds, contracts, strict inspectors, revenge raids.","Everything is hard and nothing is capped."]): a("HH.UI.Feel.%d"%i,t)
calc = [("guestMoney","Guest money","Multiplies what guests carry and are willing to spend."),
("priceTolerance","Price tolerance","How far above a fair price guests still buy."),
("arrivalRate","Arrival rate","How often guests turn up."),
("expectation","Guest expectation","Added to what every guest expects. Higher is harder to satisfy."),
("trouble","Trouble danger","Chance and weight of quarrels, fights and thefts among guests."),
("stage","Highest consequence","The worst thing a bad stay can do to you at this tier."),
("slotEdge","Slot machine house edge","Share of every bet a slot machine keeps on average."),
("bankM","Bank market return (M per year)","Yearly multiplier of the Silverline market. 1.2 means plus twenty percent."),
("defaultFactor","Payment defaults","How often customers fail to pay what they owe."),
("collectionStage","Highest collection stage","How rough debt collection can get when you owe money."),
("researchCost","Research cost","Multiplies the cost of Hearth & Harbor and Silverline research."),
("upkeep","Upkeep","Multiplies the running costs of your venues and rooms."),
("insuranceLoading","Insurance loading","The insurer's markup on top of a fair premium."),
("salesPressure","Insurance sales pressure","How pushy the insurer's sales tricks get."),
("addiction","Gambling addiction chance","Base chance that a colonist who gambles a lot becomes addicted."),
("detection","Cheat detection rate","How quickly cheating at a table is noticed.")]
for i,l,d in calc: a("HH.UI.Calc."+i,l); a("HH.UI.Calc."+i+".Desc",d)
for i,n in enumerate(["No harm","Mood","Renown","Goodwill","Money","Licence","Raids","Bounty"]): a("HH.UI.Stage.%d"%i, n)
a("HH.UI.Ladder.Goodwill","Goodwill loss per bad stay (cap)"); a("HH.UI.Ladder.Goodwill.Desc","The most goodwill one faction can take away from you per incident.")
a("HH.UI.Ladder.Renown","Renown loss per incident (cap)"); a("HH.UI.Ladder.Renown.Desc","The most renown one incident can cost you.")
a("HH.UI.Ladder.Fine","Fines (cap, silver)"); a("HH.UI.Ladder.Fine.Desc","The most a single inspection or incident can fine you.")
a("HH.UI.Ladder.Raids","Retaliation raids"); a("HH.UI.Ladder.Raids.Desc","Whether angry factions or mercenaries may come after you.")
a("HH.UI.Ladder.No","no"); a("HH.UI.Ladder.Yes","yes"); a("HH.UI.Ladder.Footer","Higher tiers unlock more consequences instead of just raising the odds.")
# --- diagnosis
a("HH.UI.Diag.Copy","Copy diagnosis to clipboard"); a("HH.UI.Diag.Copied","Diagnosis copied to the clipboard.")
a("HH.UI.Diag.PrepareRemoval","Prepare removal"); a("HH.UI.Diag.PrepareRemovalTip","Gets the colony ready for taking the mod out: guests go home, open accounts are settled, special items are cleaned up.")
a("HH.UI.Diag.PrepareRemovalConfirm","Prepare this colony for removing Hearth & Harbor? Guests are sent home and accounts are settled. Save a backup first.")
a("HH.UI.Diag.RemovalTitle","Prepare removal"); a("HH.UI.Diag.RemovalDone","Done. Save the game, then remove the mod."); a("HH.UI.Diag.NothingToDo","Nothing needed cleaning up."); a("HH.UI.Diag.NeedsSave","Open a colony first. Preparing removal works on a running game.")
a("HH.UI.Diag.Summary","{0} settings registered · {1} warnings · {2} errors in the log"); a("HH.UI.Diag.Modules","Modules"); a("HH.UI.Diag.Paused","paused after errors"); a("HH.UI.Diag.Resume","Resume")
a("HH.UI.Diag.Caretaker","Caretaker (repairs so far: {0})"); a("HH.UI.Diag.NoChecks","No checks registered."); a("HH.UI.Diag.Fixes","last run {0} · total {1}")
a("HH.UI.Diag.Patches","Patches ({0} skipped)"); a("HH.UI.Diag.PatchesOk","All patch classes applied."); a("HH.UI.Diag.Log","Latest log lines")
a("HH.UI.About.Body","Hearth & Harbor turns your colony into a host: guests, inns and hotels, shops and restaurants, a casino, a spa, city life, a discreet back room and Silverline, a full bank. Nothing is forced on you. Until you mark a guest bed or put up a sign, the game plays like vanilla.")
a("HH.UI.About.Parts","Parts"); a("HH.UI.About.PartsBody","Silverline bank included: {0}. Guest modules included: {1}."); a("HH.UI.About.Project","Open the project page"); a("HH.UI.About.License","MIT licence. Made by juliusberger00.")
# --- first start card
a("HH.UI.Card.Title","Welcome to Hearth & Harbor"); a("HH.UI.Card.Sub","Pick your hosting style. Everything can be changed later in the mod settings.")
a("HH.UI.Card.TitleJoined","Hearth & Harbor joined this colony"); a("HH.UI.Card.SubJoined","Nothing changes until you build something for guests. Pick how you want to host.")
a("HH.UI.Card.Modules","Modules"); a("HH.UI.Card.Difficulty","Difficulty and tutorial"); a("HH.UI.Card.DifficultyLine","Hosting difficulty: {0} · {1} %"); a("HH.UI.Card.Change","Change...")
a("HH.UI.Card.UseForAll","Use these choices for all future games"); a("HH.UI.Card.UseForAllTip","Copies modules, difficulty and tutorial level into the template that new games start from."); a("HH.UI.Card.Start","Start")
for k,(n,d) in {"JustWork":("Just work","Standard difficulty, everything on. Start building, we explain on the way."),"CozyInn":("Cozy inn","Gentler, with fewer ways to lose: no casino and no underworld."),"Tycoon":("Tycoon","Everything on with a short guide. Staff, bookings and the ledger are yours to run."),"Nerd":("Nerd","Every slider, formulas and nerd settings. No guide.")}.items():
    a("HH.UI.Preset."+k,n); a("HH.UI.Preset."+k+".Desc",d)
tut = {"Off":("Off","Nothing opens by itself."),"Short":("Short","Guide topics start folded."),"FullFirstTime":("Full first time, then short","Full guide in the first colony with this mod, short afterwards."),"AlwaysFull":("Always full","Topics open on first contact, every time.")}
for k,(n,t) in tut.items(): a("HH.UI.Tutorial."+k,n); a("HH.UI.Tutorial."+k+".Tip",t)
a("HH.UI.Tutorial.Full","Full this time")
# --- storyteller
a("HH.UI.Storyteller.Head","HOSTING DIFFICULTY · {0} %"); a("HH.UI.Storyteller.Button","Hosting difficulty..."); a("HH.UI.Storyteller.Tip","How hard hosting guests gets in this colony. It is separate from the raid difficulty above.")
# --- overview
a("HH.UI.Overview.Guests","Guests here"); a("HH.UI.Overview.GuestsTip","Guests on the current map."); a("HH.UI.Overview.Income","Income today"); a("HH.UI.Overview.IncomeTip","Silver paid into your tills since dawn.")
a("HH.UI.Overview.Venues","Venues"); a("HH.UI.Overview.VenuesTip","Rooms with a venue sign on the current map."); a("HH.UI.Overview.Bank","Bank balance"); a("HH.UI.Overview.BankTip","Balance of the colony account at Silverline.")
a("HH.UI.Overview.IncomeHead","Money"); a("HH.UI.Overview.Today","Today"); a("HH.UI.Overview.Yesterday","Yesterday"); a("HH.UI.Overview.Sources","Income by source so far"); a("HH.UI.Overview.NoIncome","No income yet. It shows up here as soon as a guest pays.")
# --- guests page
a("HH.UI.Guests.Head","Guests · {0} here · {1} day guests"); a("HH.UI.Guests.None","Nobody is staying right now."); a("HH.UI.Guests.VanillaNote","The Inn module is off, so ordinary visitors are shown.")
a("HH.UI.Guests.SatTip","How happy this guest is, from -1 to +1. Latest reasons:")
for k,v in {"Name":"Guest","Faction":"Faction","Stay":"Stay","Mood":"Mood","Satisfaction":"Satisfaction","Budget":"Budget left","Spent":"Spent","Wing":"Wing","Leaves":"Leaves in"}.items(): a("HH.UI.Guests.Col."+k,v)
for k,v in {"DayVisitor":"Day guest","Overnight":"Overnight","Traveler":"Traveller","Caravan":"Caravan","Lodger":"Lodger","Patient":"Patient"}.items(): a("HH.UI.Kind."+k,v)
# --- guest tab
a("HH.UI.GuestTab.Label","Guest"); a("HH.UI.GuestTab.NoInfo","No guest record for this pawn."); a("HH.UI.GuestTab.Stay","Stay"); a("HH.UI.GuestTab.Faction","Faction"); a("HH.UI.GuestTab.Kind","Stay type"); a("HH.UI.GuestTab.Wing","Wing")
a("HH.UI.GuestTab.Arrived","Arrived"); a("HH.UI.GuestTab.Ago","{0} ago"); a("HH.UI.GuestTab.Leaves","Leaves"); a("HH.UI.GuestTab.In","in {0}"); a("HH.UI.GuestTab.Payment","Pays with"); a("HH.UI.GuestTab.Card","card"); a("HH.UI.GuestTab.Purse","purse")
a("HH.UI.GuestTab.Satisfaction","Satisfaction"); a("HH.UI.GuestTab.SatisfactionTip","From -1 (furious) to +1 (delighted). It drives renown, goodwill and whether the guest comes back."); a("HH.UI.GuestTab.Reasons","What this guest thinks"); a("HH.UI.GuestTab.NoReasons","Nothing remarkable yet.")
a("HH.UI.GuestTab.Money","Money"); a("HH.UI.GuestTab.Budget","Budget left"); a("HH.UI.GuestTab.Spent","Spent so far"); a("HH.UI.GuestTab.Handling","Handling")
a("HH.UI.Handling.Basics","Basics"); a("HH.UI.Handling.Basics.Tip","Paid services only, no entertainment. Costs no colonist time.")
a("HH.UI.Handling.Accompany","Accompany"); a("HH.UI.Handling.Accompany.Tip","Colonists on Hosting entertain this guest and show them around: mood +1 to +4 by social skill. Costs colonist time. Without a colonist on Hosting it stays Basics.")
a("HH.UI.Handling.Recruit","Recruit"); a("HH.UI.Handling.Recruit.Tip","Like Accompany, plus recruitment talks. A clumsy talk can offend: mood -6, 30 % less spending, leaves half a day early.")
# --- guide
a("HH.UI.Guide.Title","Hearth & Harbor Guide"); a("HH.UI.Guide.TitleTip","Right-click to hide. You can bring it back in the Hearth & Harbor tab or the settings."); a("HH.UI.Guide.Hidden","Guide hidden. Bring it back with the Guide button in the Hearth & Harbor tab or in the settings.")
a("HH.UI.Guide.More","+{0} more topics follow"); a("HH.UI.Guide.Done","Done ({0})"); a("HH.UI.Guide.ClickJump","Click to go there.")

# --- setting labels (registered in UiModule)
S = {
"core.consequenceStage":("Highest consequence stage","How far bad outcomes can climb: 1 mood, 2 renown, 3 goodwill, 4 money, 5 licence, 6 raids, 7 bounty. Follows the hosting difficulty."),
"core.researchCost":("Research cost factor","Multiplies the cost of Hearth & Harbor and Silverline research projects. Follows the hosting difficulty."),
"core.upkeep":("Upkeep factor","Multiplies the running costs of your venues and rooms. Follows the hosting difficulty."),
"core.neverDie":("Guests never die","Guests are knocked down instead of killed and are rescued for free."),
"core.noHostile":("No hostile guests ever","Guests never turn hostile, whatever their faction thinks of you."),
"core.infiniteMoney":("Infinite guest money","Guests always have enough to pay. The guest money factor has no effect while this is on."),
"core.picky":("Picky guests","Guests expect a little more from every stay (expectation +0.1)."),
"core.feuding":("Feuding factions","Hostile factions dislike each other twice as much when they share a roof."),
"core.mandatory":("Mandatory hospitality","Contracts and billeting are forced on you, even below the Overbooked tier."),
"core.strictInspectors":("Strict inspectors","Inspectors check three times as often."),
"core.realConsequences":("Real consequences","Raises the consequence ladder by one stage."),
"core.ironBank":("Iron purse / Iron bank","No bailout, no overdraft, compound interest without mercy."),
"core.noResearchGating":("No research gating","Everything on the Hearth & Harbor and Silverline trees is available from the start."),
"core.peacefulUnderworld":("Peaceful underworld","Dark venues work without violence."),
"core.showMath":("Show me the math","Shows nerd settings and formulas everywhere."),
"core.perfMode":("Performance mode","Fewer guests at once and simpler guest behaviour on slow computers."),
"core.houseStyle":("House style","The look of furniture and signs. Automatic follows your ideology style, otherwise your highest researched era. Looks only: guests and prices are not affected."),
"core.keepBuiltStyle":("Built furniture keeps its old style","When the house style changes with a new era, already built furniture keeps its look."),
}
for k,(l,d) in S.items(): a("HH.Setting."+k,l); a("HH.Setting."+k+".Desc",d)
for k,v in {"Automatic":"Automatic","StoneAge":"Stone age","Medieval":"Medieval","Industrial":"Industrial","Spacer":"Spacer","Ultra":"Ultra"}.items(): a("HH.Enum.HouseStyle."+k,v)

# verify against source
src = ""
import glob
for f in glob.glob(sys.argv[1]+"/*.cs"): src += open(f).read()
used = set(re.findall(r'"(HH\.[A-Za-z0-9_.]*)"', src))
static = {u for u in used if not u.endswith('.')}
missing = sorted(u for u in static if u not in K and not u.startswith("HH.Setting"))
print("missing static keys:", missing)
out = ['<?xml version="1.0" encoding="utf-8"?>','<LanguageData>']
for k,v in K.items(): out.append("  <%s>%s</%s>" % (k, escape(v), k))
out.append('</LanguageData>')
open(sys.argv[2],"w",encoding="utf-8").write("\n".join(out)+"\n")
print(len(K),"keys")
