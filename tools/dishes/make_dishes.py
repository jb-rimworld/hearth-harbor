#!/usr/bin/env python3
"""Generator for the Hearth & Harbor kitchen data (KONZEPT 6.4, Anhang A).

Reads the dish tables of Anhang A (A.1 .. A.5) from KONZEPT_v2.md and writes
  Common/Defs/Kitchen/Dishes_L1.xml .. Dishes_L4.xml   (DishDefs of the base menu, one file per learning level)
  Common/Defs/Kitchen/Dishes_Mods.xml                  (extra recipes with other mods, MayRequire on the mod's packageId)
  Common/Defs/Kitchen/Plates.xml                       (the 12 plate items)
  Common/Defs/Kitchen/Thoughts_Dishes.xml              (the quality/level mood thought, 28 stages)
Recipes are NOT written here: they are built at game start from the DishDefs (ingredient classes are resolved against
whatever animals and plants the loaded mods bring, see Source/Hearth/Kitchen/RecipeBuilder.cs).

Usage:  python3 tools/dishes/make_dishes.py [path/to/KONZEPT_v2.md]
"""
import re, sys
from pathlib import Path
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parent.parent.parent
KONZEPT = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT.parent.parent / "_Planung_Hospitality_2026-11-22" / "KONZEPT_v2.md"
if not KONZEPT.exists():
    KONZEPT = Path("/Users/julius/Documents/Coding/22_RimWorld/_Planung_Hospitality_2026-11-22/KONZEPT_v2.md")
OUT = ROOT / "Common" / "Defs" / "Kitchen"
NS = "Hearth.Kitchen"

# ---- vocabulary -------------------------------------------------------------------------------------------------------
STATION = {  # label in the tables -> StationKind
    "campfire": "Campfire", "fueled stove": "Stove", "electric stove": "Stove", "butcher table": "ButcherTable",
    "prep counter": "PrepCounter", "stock pot": "StockPot", "grill": "Grill", "oven": "Oven", "wok station": "Wok",
    "deep fryer": "Fryer", "smoker": "Smoker", "steamer": "Steamer", "dough bench": "DoughBench", "cold station": "ColdStation",
}
STEP = {
    "chop": "Chop", "simmer": "Simmer", "boil": "Boil", "grill": "Grill", "knead dough": "KneadDough", "smoke": "Smoke",
    "roast": "Roast", "bake": "Roast", "stir-fry": "StirFry", "deep-fry": "DeepFry", "steam": "Steam", "chill": "Chill",
    "carve": "Carve", "spit-roast": "SpitRoast", "pan-fry": "PanFry", "sear": "Sear",
}
PLATE = {"soup": "Soup", "stew": "Stew", "grill": "Grill", "noodles": "Noodles", "rice": "Rice", "bake": "Bake", "fry": "Fry",
         "salad": "Salad", "raw": "Raw", "breakfast": "Breakfast", "dessert": "Dessert", "street food": "StreetFood"}
CUISINE = {"rustic/frontier": "Frontier", "tribal feast": "TribalFeast", "tavern fare": "TavernFare", "imperial haute": "ImperialHaute",
           "glitter fusion": "GlitterFusion", "street": "Street", "seafood": "Seafood", "insect": "Insect", "fungal": "Fungal",
           "vegetarian": "Vegetarian", "ascetic": "Ascetic", "long pork": "LongPork"}
ING = {  # ingredient name in the tables -> ingredient class key (Ingredients.cs)
    "meat": "meat", "insect meat": "insectmeat", "human meat": "humanmeat", "twisted meat": "twistedmeat", "hemogen pack": "hemogen",
    "fish": "fish", "eggs": "eggs", "egg": "eggs", "milk": "milk", "potatoes": "potatoes", "corn": "corn", "rice": "rice",
    "berries": "berries", "agave fruit": "agave", "agave": "agave", "mushrooms": "mushrooms", "chocolate": "chocolate", "beer": "beer",
    "cheese": "cheese", "pumpkin": "pumpkin", "flour": "flour", "fruit": "fruit", "soy sauce": "soysauce", "wasabi paste": "wasabi",
    "vegetables": "vegetables", "sugar": "sugar", "gourmet ingredients": "gourmet", "herbs": "herbs", "nuts": "nuts",
    "tea leaves": "tealeaves", "strawflour": "strawflour", "prickly pear": "pricklypear", "greenfruit": "greenfruit",
}
MODS = {  # Anhang A.5 "Mod" column -> packageId(s) as in facts/*.json (MayRequire is case-insensitive in RimWorld)
    "Vanilla Cooking Expanded": ["VanillaExpanded.VCookE"],
    "Vanilla Cooking Expanded - Sushi": ["VanillaExpanded.VCookESushi"],
    "Vanilla Cooking Expanded - Stews": ["VanillaExpanded.VCookEStews"],
    "Vanilla Cooking Expanded - Bakery": ["VanillaExpanded.VCookEBakery"],
    "Vanilla Cooking Expanded - Haute": ["VanillaExpanded.VCookEHaute"],
    "Vanilla Plants Expanded": ["VanillaExpanded.VPlantsE"],
    "Kenshi Cuisine": ["super.kenshicuisine"],
    "Fish mods": ["Ludeon.RimWorld.Odyssey", "rikiki.fishindustry", "sunsetmoderteam.rawfish", "OW.RU.Fishing"],  # any of
}
NAME_OVERRIDE = {"Campfire muffalo stew": "CampfireStew", "Salt-skin bug skewers": "BugSkewers"}
DLC_MARK = {"†": None, "‡": "Ludeon.RimWorld.Biotech", "§": "Ludeon.RimWorld.Anomaly"}
UNLOCK = {"frei": "free", "`HH_Innkeeping`": "HH_Innkeeping", "`HH_Gastronomy`": "HH_Gastronomy", "`HH_HotelManagement`": "HH_HotelManagement",
          "`HH_FineDining`": "HH_FineDining"}


# F11 (1.1.0): logic fixes applied on top of the KONZEPT tables. Key = defName id (after HH_Dish_).
#  finishers : classes that fit this plate as finishing touch of the Fine/Signature forms (never a class of the dish itself)
#  noVariants: no Fine/Signature form
#  label/desc_add: a dish named after one animal accepts every meat, so the name is generic and the animal moves to the description
#  stations/steps: a "main" that was never heated gets its heating steps (main device = last station stays)
OVERRIDES = {
    "FruitBowl": {"finishers": ["milk", "chocolate", "agave"]},
    "FriedMushrooms": {"finishers": ["milk", "eggs", "beer"]},
    "FermentedMushrooms": {"finishers": ["milk", "eggs", "berries"]},
    "ScrambledEggs": {"finishers": ["mushrooms", "berries"]},
    "PlainSteamedRice": {"noVariants": True},
    "RawFishSlices": {"noVariants": True},
    "CampfireStew": {"label": "Campfire meat stew", "desc_add": "Traditionally made from muffalo."},
    "MuffaloRamen": {"label": "Hearty meat ramen", "desc_add": "Traditionally made from muffalo."},
    "MuffaloGoulash": {"label": "Meat goulash", "desc_add": "Traditionally made from muffalo."},
    "MuffaloBourguignon": {"label": "Colony bourguignon", "desc_add": "Traditionally made from muffalo."},
    "MuffaloCarpaccio": {"label": "Meat carpaccio", "desc_add": "Traditionally made from muffalo."},
    "MuffaloWellington": {"label": "Meat wellington", "desc_add": "Traditionally made from muffalo."},
    "MuffaloStroganoff": {"label": "Creamy stroganoff", "desc_add": "Traditionally made from muffalo."},
    "ClayOvenMuffalo": {"label": "Clay-oven roast", "desc_add": "Traditionally made from muffalo."},
    "MongolianMuffalo": {"label": "Mongolian-style meat", "desc_add": "Traditionally made from muffalo."},
    "LacqueredThrumbo": {"label": "Lacquered roast", "desc_add": "Traditionally made from thrumbo, if the colony is that lucky."},
    "ThrumboPho": {"label": "Meat pho", "desc_add": "Traditionally made from thrumbo, if the colony is that lucky."},
    "ThrumboTartare": {"label": "Meat tartare", "desc_add": "Traditionally made from thrumbo, if the colony is that lucky."},
    "DromedaryTagine": {"label": "Meat and berry tagine", "desc_add": "Traditionally made from dromedary."},
    "BoomalopeConfit": {"label": "Slow-roast meat confit", "desc_add": "Traditionally made from boomalope, carefully."},
    "LambKoftasWithRice": {"label": "Meat koftas with rice", "desc_add": "Traditionally made from lamb."},
    "PadThrumbo": {"label": "Pad bug", "desc_add": "The crunch comes from insect meat."},
    "SevenCourseColonyMenu": {"stations": ["PrepCounter", "Stove", "Oven", "ColdStation"], "steps": ["Chop", "PanFry", "Roast", "Chill"]},
}


def camel(label: str) -> str:
    label = re.sub(r"[†‡§]", "", label)
    words = re.findall(r"[A-Za-z0-9]+", label.replace("'", ""))
    return "".join(w[:1].upper() + w[1:] for w in words)


def parse_ingredients(s: str):
    out = []
    for part in s.split(","):
        part = re.sub(r"\([^)]*\)", "", part).strip()
        m = re.match(r"^(.*?)(?:\s+(\d+(?:\.\d+)?))$", part)
        name, amt = (m.group(1).strip(), m.group(2)) if m else (part, "1")
        key = ING.get(name.lower())
        if key is None:
            raise SystemExit(f"unknown ingredient '{name}' in '{s}'")
        out.append((key, amt))
    return out


def parse_tables(text: str):
    dishes = []
    level = 0
    mods_section = False
    for line in text.splitlines():
        h = re.match(r"^### A\.(\d)", line)
        if h:
            n = int(h.group(1)); mods_section = (n == 5); level = n if n <= 4 else 0
            continue
        if line.startswith("## ") and not line.startswith("## Anhang A"):
            if dishes: break
        if not line.startswith("|") or "---|" in line or line.startswith("| Gericht"):
            continue
        if level == 0 and not mods_section:
            continue
        c = [x.strip() for x in line.strip().strip("|").split("|")]
        if not mods_section:
            if len(c) != 11: continue
            label, inspiration, cuisine, plate, stations, unlock, steps, ing, shelf, mincook, desc = c
            mod = None
        else:
            if len(c) != 10: continue
            label, mod, cuisine, plate, stations, steps, ing, shelf, mincook, desc = c
            inspiration = mod; unlock = "frei"
        marks = [m for m in "†‡§" if m in label]
        clean = re.sub(r"\s*[†‡§]", "", label).strip()
        st = [STATION[x.strip().lower()] for x in stations.split(",")]
        sm = re.match(r"^(\d+):\s*(.*)$", steps)
        stp = [STEP[x.strip().lower()] for x in sm.group(2).split("→")]
        assert int(sm.group(1)) == len(stp), (label, steps)
        d = dict(label=clean, inspiration=inspiration, cuisine=CUISINE[cuisine.lower()], plate=PLATE[plate.lower()], stations=st, steps=stp,
                 ingredients=parse_ingredients(ing), shelf=int(shelf), mincook=int(mincook), desc=desc, unlock=UNLOCK.get(unlock, unlock.strip("`")),
                 level=level if not mods_section else 0, mod=mod, marks=marks)
        dishes.append(d)
    return dishes


def mod_level(d):  # extra recipes: level from the minimum cooking skill, like the base menu
    m = d["mincook"]
    return 1 if m <= 4 else 2 if m <= 7 else 3 if m <= 10 else 4


def dish_xml(d):
    name = NAME_OVERRIDE.get(d["label"], camel(d["label"]))
    ov = OVERRIDES.get(name, {})
    d = dict(d)
    if "label" in ov: d["label"] = ov["label"]
    if "desc_add" in ov: d["desc"] = d["desc"].rstrip() + " " + ov["desc_add"]
    if "stations" in ov: d["stations"] = ov["stations"]
    if "steps" in ov: d["steps"] = ov["steps"]
    lvl = d["level"] or mod_level(d)
    attrs = ""
    req = []
    for m in d["marks"]:
        if DLC_MARK[m]: req.append(DLC_MARK[m])
    if d["mod"]:
        pk = MODS[d["mod"]]
        if d["mod"] == "Fish mods":
            attrs = ' MayRequireAnyOf="%s"' % ",".join(pk)
        else:
            req.extend(pk)
    if req: attrs = ' MayRequire="%s"' % ",".join(req) + attrs
    x = [f"  <{NS}.DishDef{attrs}>", f"    <defName>HH_Dish_{name}</defName>", f"    <label>{escape(d['label'].lower() if False else d['label'])}</label>",
         f"    <description>{escape(d['desc'])}</description>", f"    <inspiration>{escape(d['inspiration'])}</inspiration>",
         f"    <cuisine>{d['cuisine']}</cuisine>", f"    <plate>{d['plate']}</plate>",
         "    <stations>" + "".join(f"<li>{s}</li>" for s in d["stations"]) + "</stations>",
         "    <steps>" + "".join(f"<li>{s}</li>" for s in d["steps"]) + "</steps>",
         "    <ingredients>" + "".join(f"<li>{k} {a}</li>" for k, a in d["ingredients"]) + "</ingredients>",
         f"    <shelfLifeHours>{d['shelf']}</shelfLifeHours>", f"    <level>{lvl}</level>", f"    <minCooking>{d['mincook']}</minCooking>"]
    if d["unlock"] not in ("free",): x.append(f"    <unlockHint>{d['unlock']}</unlockHint>")
    if d["mod"]: x.append(f"    <fromMod>{escape(d['mod'])}</fromMod>")
    if "†" in d["marks"]: x.append("    <dark>true</dark>")
    if ov.get("finishers"): x.append("    <finishers>" + "".join(f"<li>{k}</li>" for k in ov["finishers"]) + "</finishers>")
    if ov.get("noVariants"): x.append("    <noVariants>true</noVariants>")
    x.append(f"  </{NS}.DishDef>")
    return name, "\n".join(x)


PLATE_TEXT = {
    "Soup": ("soup bowl", "A bowl of something hot and wet. Eaten with a spoon, a prayer, or both."),
    "Stew": ("stew pot", "A heavy bowl of slow-cooked stew, steaming with the promise of a second helping."),
    "Grill": ("grill platter", "Meat that has had a conversation with an open flame and lost."),
    "Noodles": ("noodle bowl", "A tangle of noodles in a bowl. Slurping is not optional."),
    "Rice": ("rice dish", "A mound of rice with opinions, served on a plate with a fork nobody asked for."),
    "Bake": ("baked dish", "Something that went into the oven as ingredients and came out as a decision."),
    "Fry": ("fried plate", "Golden, crunchy and wildly unhealthy. Exactly what the colony ordered."),
    "Salad": ("salad plate", "A cold plate with a lot of leaves, very little remorse, and a short shelf life."),
    "Raw": ("raw plate", "Thinly sliced, expensively arranged and absolutely not cooked."),
    "Breakfast": ("breakfast plate", "A plate that says the day has started, whether you like it or not."),
    "Dessert": ("dessert dish", "A small, sweet reward for surviving another day on the Rim."),
    "StreetFood": ("street food tray", "Hot, handheld and best eaten standing up, quickly, with one eye on the exits."),
}


PLATE_DROP = {"Soup": "Ceramic", "Stew": "Ceramic", "Noodles": "Ceramic", "Rice": "Ceramic", "Bake": "Ceramic", "Breakfast": "Ceramic",
              "Grill": "Wood", "StreetFood": "Wood", "Fry": "Wood", "Salad": "Glass", "Raw": "Glass", "Dessert": "Glass"}
PLATE_EAT = {"Soup": "HH_Eat_Slurp", "Noodles": "HH_Eat_Slurp", "Fry": "HH_Eat_Crunch", "StreetFood": "HH_Eat_Crunch"}


def write_plates():
    x = ['<?xml version="1.0" encoding="utf-8"?>', "<Defs>",
         "  <!-- GENERATED by tools/dishes/make_dishes.py. The 12 plate kinds that carry every dish (KONZEPT 6.4, S5). -->",
         '  <ThingDef Name="HH_PlateBase" Abstract="True">',
         f"    <thingClass>{NS}.DishPlate</thingClass><category>Item</category><drawerType>MapMeshOnly</drawerType>",
         "    <useHitPoints>true</useHitPoints><healthAffectsPrice>false</healthAffectsPrice><selectable>true</selectable>",
         "    <statBases><MaxHitPoints>50</MaxHitPoints><Flammability>1.0</Flammability><Beauty>0</Beauty><Mass>0.5</Mass><DeteriorationRate>10</DeteriorationRate>",
         "      <MarketValue>24</MarketValue><Nutrition>0.9</Nutrition><WorkToMake>600</WorkToMake></statBases>",
         "    <altitudeLayer>Item</altitudeLayer><stackLimit>1</stackLimit><tickerType>Rare</tickerType>",
         "    <socialPropernessMatters>true</socialPropernessMatters><alwaysHaulable>true</alwaysHaulable><tradeability>None</tradeability>",
         "    <thingCategories><li>FoodMeals</li></thingCategories>",
         "    <pathCost>14</pathCost><resourceReadoutPriority>Last</resourceReadoutPriority><drawGUIOverlay>true</drawGUIOverlay><uiIconForStackCount>1</uiIconForStackCount>",
         "    <comps>",
         '      <li Class="CompProperties_Forbiddable"/>',
         '      <li Class="CompProperties_Rottable"><daysToRotStart>1</daysToRotStart><rotDestroys>true</rotDestroys></li>',
         '      <li Class="CompProperties_FoodPoisonable"/>',
         '      <li MayRequire="Ludeon.RimWorld.Anomaly" Class="CompProperties_MetalhorrorInfectible"/>',
         '      <li Class="CompProperties_Ingredients"><splitTransferableFoodKind>true</splitTransferableFoodKind></li>',
         '      <li><compClass>CompQuality</compClass></li>',
         f'      <li Class="{NS}.CompProperties_Dish"/>',
         "    </comps>",
         "    <ingestible>",
         "      <foodType>Meal</foodType><maxNumToIngestAtOnce>1</maxNumToIngestAtOnce><defaultNumToIngestAtOnce>1</defaultNumToIngestAtOnce>",
         "      <optimalityOffsetHumanlikes>16</optimalityOffsetHumanlikes><optimalityOffsetFeedingAnimals>-75</optimalityOffsetFeedingAnimals>",
         "      <preferability>MealFine</preferability><ingestEffect>EatVegetarian</ingestEffect><ingestSound>Meal_Eat</ingestSound>",
         "    </ingestible>",
         "  </ThingDef>"]
    for k, (label, desc) in PLATE_TEXT.items():
        x += ['  <ThingDef ParentName="HH_PlateBase">', f"    <defName>HH_Plate_{k}</defName>", f"    <label>{label}</label>",
              f"    <description>{desc}</description>",
              f"    <soundDrop>HH_Plate_Drop_{PLATE_DROP[k]}</soundDrop>",
              *([f"    <ingestible><ingestSound>{PLATE_EAT[k]}</ingestSound></ingestible>"] if k in PLATE_EAT else []),
              f"    <graphicData><texPath>Things/HH/Kitchen/Plate_{k}</texPath><graphicClass>Graphic_Single</graphicClass></graphicData>", "  </ThingDef>"]
    x.append("</Defs>")
    (OUT / "Plates.xml").write_text("\n".join(x) + "\n", encoding="utf-8")


QUAL = ["awful", "poor", "normal", "good", "excellent", "masterwork", "legendary"]
BASE = [4, 6, 9, 12]
QOFF = [-2, -1, 0, 1, 2, 3, 4]
LVL = ["home cooking", "good home cooking", "fine cooking", "haute cuisine"]


def write_thoughts():
    st = []
    for li, lv in enumerate(LVL):
        for qi, q in enumerate(QUAL):
            mood = BASE[li] + QOFF[qi]
            st.append(f"      <li><label>{q} {lv}</label><description>I ate a {q} dish of {lv}.</description><baseMoodEffect>{mood}</baseMoodEffect></li>")
    x = ['<?xml version="1.0" encoding="utf-8"?>', "<Defs>",
         "  <!-- GENERATED by tools/dishes/make_dishes.py. Stage = (learning level - 1) * 7 + quality (KONZEPT 6.4: +4/+6/+9/+12, quality -2..+4, max +16). -->",
         "  <ThoughtDef>", "    <defName>HH_AteDish</defName>", "    <label>ate a proper dish</label>", "    <durationDays>0.6</durationDays>",
         "    <stackLimit>1</stackLimit>", "    <stages>", *st, "    </stages>", "  </ThoughtDef>", "</Defs>"]
    (OUT / "Thoughts_Dishes.xml").write_text("\n".join(x) + "\n", encoding="utf-8")


def main():
    text = KONZEPT.read_text(encoding="utf-8")
    start = text.index("## Anhang A")
    end = text.index("## Anhang B")
    dishes = parse_tables(text[start:end])
    OUT.mkdir(parents=True, exist_ok=True)
    seen = {}
    by_file = {1: [], 2: [], 3: [], 4: [], 0: []}
    for d in dishes:
        name, xml = dish_xml(d)
        if name in seen: raise SystemExit("duplicate defName " + name)
        seen[name] = 1
        by_file[d["level"]].append(xml)
    titles = {1: "Level 1: home cooking (A.1)", 2: "Level 2: hearty fare (A.2)", 3: "Level 3: upscale kitchen (A.3)", 4: "Level 4: haute cuisine (A.4)", 0: "Extra recipes with other mods (A.5)"}
    for lv, items in by_file.items():
        fn = OUT / ("Dishes_Mods.xml" if lv == 0 else f"Dishes_L{lv}.xml")
        fn.write_text('<?xml version="1.0" encoding="utf-8"?>\n<Defs>\n  <!-- GENERATED by tools/dishes/make_dishes.py from KONZEPT Anhang A. %s -->\n%s\n</Defs>\n' % (titles[lv], "\n".join(items)), encoding="utf-8")
    write_plates(); write_thoughts()
    base = sum(len(v) for k, v in by_file.items() if k)
    print(f"{base} base dishes + {len(by_file[0])} mod dishes written to {OUT}")
    for k in (1, 2, 3, 4): print(f"  level {k}: {len(by_file[k])}")


if __name__ == "__main__":
    main()
