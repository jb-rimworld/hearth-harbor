#!/usr/bin/env python3
"""Writes the two shipped scenarios of Hearth & Harbor (F08, 1.1.0) for every game version from ONE source (this file).
1.6/1.7 inherit vanilla's ScenarioBase (surface + Odyssey orbit layer); 1.5 has no ScenarioBase and gets its own player faction part.
Run: python3 tools/scenarios/gen.py   (writes <version>/Defs/Scenarios/HH_Scenarios.xml)."""
from pathlib import Path
R = Path(__file__).resolve().parents[2]

def thing(d, n=None, stuff=None):
    s = '        <li Class="ScenPart_StartingThing_Defined">\n          <def>StartingThing_Defined</def>\n          <thingDef>%s</thingDef>\n' % d
    if stuff: s += '          <stuff>%s</stuff>\n' % stuff
    if n: s += '          <count>%d</count>\n' % n
    return s + '        </li>\n'

def scatter(d, n, near=True):
    k = "ScatterThingsNearPlayerStart" if near else "ScatterThingsAnywhere"
    return '        <li Class="ScenPart_%s">\n          <def>%s</def>\n          <thingDef>%s</thingDef>\n          <count>%d</count>\n        </li>\n' % (k, k, d, n)

def pawns(n=3, choice=8):
    return ('        <li Class="ScenPart_ConfigPage_ConfigureStartingPawns">\n          <def>ConfigPage_ConfigureStartingPawns</def>\n'
            '          <pawnCount>%d</pawnCount>\n          <pawnChoiceCount>%d</pawnChoiceCount>\n        </li>\n'
            '        <li Class="ScenPart_PlayerPawnsArriveMethod">\n          <def>PlayerPawnsArriveMethod</def>\n          <method>Standing</method>\n          <visible>false</visible>\n        </li>\n') % (n, choice)

def dialog(key):
    return '        <li Class="ScenPart_GameStartDialog">\n          <def>GameStartDialog</def>\n          <textKey>%s</textKey>\n          <closeSound>GameStartSting</closeSound>\n        </li>\n' % key

def rules(tier, content, on, off, discovered, settings):
    s = '        <li Class="Hearth.ScenPart_HHRules">\n          <def>HH_ScenRules</def>\n          <tier>%s</tier>\n          <content>%s</content>\n' % (tier, content)
    s += '          <modulesOn>\n' + "".join('            <li>%s</li>\n' % m for m in on) + '          </modulesOn>\n'
    if off: s += '          <modulesOff>\n' + "".join('            <li>%s</li>\n' % m for m in off) + '          </modulesOff>\n'
    if discovered: s += '          <underworldDiscovered>true</underworldDiscovered>\n'
    s += '          <settings>\n' + "".join('            <li><key>%s</key><value>%s</value></li>\n' % kv for kv in settings) + '          </settings>\n        </li>\n'
    return s

def research(p):
    return '        <li Class="Hearth.ScenPart_HHStartingResearch">\n          <def>HH_ScenStartingResearch</def>\n          <project>%s</project>\n        </li>\n' % p

def account(bank, bal, loan=0, days=30, dd=False):
    s = '        <li Class="Silverline.ScenPart_SLStartingAccount">\n          <def>SL_ScenStartingAccount</def>\n          <bank>%s</bank>\n          <balance>%d</balance>\n' % (bank, bal)
    if loan: s += '          <loan>%d</loan>\n          <loanDays>%d</loanDays>\n' % (loan, days)
    if dd: s += '          <directDeposit>true</directDeposit>\n'
    return s + '        </li>\n'

def trait(t, chance, degree=0):
    return ('        <li Class="ScenPart_ForcedTrait">\n          <def>ForcedTrait</def>\n          <trait>%s</trait>\n          <degree>%d</degree>\n'
            '          <context>PlayerStarter</context>\n          <chance>%s</chance>\n        </li>\n') % (t, degree, chance)

SCEN = [
    dict(defName="HH_Scen_HarborTown", label="Harbor Town",
         desc="Three friends inherit a weathered roadside inn above a quiet natural harbour, right where the trade caravans pass. The roof leaks, the sign fell down years ago and the beds are bare - but the road is busy, the water is clear and the neighbours are friendly.\\n\\nBuild it back up: a few guest beds first, then a tavern and a market, and one day a whole little town that people travel to see.\\n\\nA bright, gentle start for Hearth & Harbor. Pairs well with Hetty Hearthwell, the storyteller who likes builders.",
         summary="Reopen an old inn by the harbour and grow it into a town people travel to see. A bright tourism start.",
         parts=pawns() + trait("Kind", "0.35")
               + thing("Silver", 1200) + thing("MealSurvivalPack", 40) + thing("MedicineHerbal", 20) + thing("MedicineIndustrial", 10)
               + thing("ComponentIndustrial", 20) + thing("Beer", 30) + thing("Gun_Revolver") + thing("MeleeWeapon_Knife", stuff="Steel")
               + thing("Bedroll", 3, stuff="Cloth")
               + '        <li Class="ScenPart_StartingAnimal">\n          <def>StartingAnimal</def>\n          <count>1</count>\n          <bondToRandomPlayerPawnChance>1.0</bondToRandomPlayerPawnChance>\n        </li>\n'
               + scatter("WoodLog", 700) + scatter("Steel", 350) + scatter("Cloth", 150)
               + rules("2", "Cozy", ["Inn", "ShopsFood", "Casino", "Wellness", "CityLife", "Bank"], ["Underworld"], False,
                       [("inn.arrivalRate", "1.4"), ("inn.firstGuestMinDays", "1"), ("inn.firstGuestMaxDays", "3"), ("venues.marketEnabled", "1")])
               + research("HH_Innkeeping") + account("SL_Mutual", 500, dd=True)
               + dialog("HH.Scen.HarborTown.Intro")),
    dict(defName="HH_Scen_BackRoom", label="The Back Room",
         desc="Your waystation on the old smugglers' road is failing. Then the debts are bought - quietly, all of them - by the Black Ledger. They do not want the money back. Not yet. They want the room behind the kitchen, a table with no questions, and a host who looks the other way.\\n\\nThe underworld already knows your name. Make it rich, make it fear you, or make it go away.\\n\\nA dark, morally grey start for the shady side of Hearth & Harbor: back room, fence, drug lounge, fight pit and an account at a bank that never forgets.",
         summary="A failing waystation in debt to the Black Ledger, with the underworld already at the door. A dark, shady start.",
         parts=pawns() + trait("Greedy", "0.4")
               + thing("Silver", 600) + thing("MealSurvivalPack", 30) + thing("MedicineIndustrial", 15) + thing("ComponentIndustrial", 15)
               + thing("SmokeleafJoint", 30) + thing("Beer", 20) + thing("Gun_Revolver", 2) + thing("MeleeWeapon_Knife", 2, stuff="Steel")
               + scatter("WoodLog", 450) + scatter("Steel", 300)
               + rules("4", "Dark", ["Inn", "ShopsFood", "Casino", "Underworld", "Bank"], [], True, [("core.peacefulUnderworld", "0")])
               + research("HH_Underworld_Discovery") + account("SL_BlackLedger", 100, loan=1500, days=45)
               + dialog("HH.Scen.BackRoom.Intro")),
]

HEAD = '<?xml version="1.0" encoding="utf-8"?>\n<!-- generated by tools/scenarios/gen.py - edit that file, not this one -->\n<Defs>\n'
FACTION = '        <!-- 1.5 has no ScenarioBase -->\n'

def write(version):
    out = HEAD
    for s in SCEN:
        parent = ' ParentName="ScenarioBase"' if version != "1.5" else ""
        out += '\n  <ScenarioDef%s>\n    <defName>%s</defName>\n    <label>%s</label>\n    <description>%s</description>\n    <scenario>\n      <summary>%s</summary>\n' % (
            parent, s["defName"], s["label"], s["desc"].replace("&", "&amp;"), s["summary"].replace("&", "&amp;"))
        if version == "1.5":
            out += '      <playerFaction>\n        <def>PlayerFaction</def>\n        <factionDef>PlayerColony</factionDef>\n      </playerFaction>\n'
        out += '      <parts>\n' + s["parts"] + '      </parts>\n    </scenario>\n  </ScenarioDef>\n'
    out += '\n</Defs>\n'
    p = R / version / "Defs" / "Scenarios" / "HH_Scenarios.xml"
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(out, encoding="utf-8")
    print("wrote", p.relative_to(R))

for v in ("1.5", "1.6", "1.7"):
    write(v)
