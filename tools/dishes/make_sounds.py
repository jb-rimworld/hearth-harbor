#!/usr/bin/env python3
"""Generator for the kitchen sound and effect defs (F31, kitchen part).

Writes Common/Defs/Kitchen/Sounds_Kitchen.xml (SoundDefs, MoteDefs, EffecterDefs), silent placeholder OGGs for every clip that
does not exist yet (ffmpeg) and docs/v110/sfx_kitchen.json (prompts for the sound generator).
Usage: python3 tools/dishes/make_sounds.py [--no-audio]
"""
import json, subprocess, sys
from pathlib import Path
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parent.parent.parent
OUT = ROOT / "Common" / "Defs" / "Kitchen" / "Sounds_Kitchen.xml"
SND = ROOT / "Common" / "Sounds" / "HH" / "Kitchen"
JSON = ROOT / "docs" / "v110" / "sfx_kitchen.json"

NOVOICE = "no music, no voices, no speech, clean close recording"

# Working loops (sustained while the cook works). clips = number of different clips the sustainer rotates through.
LOOPS = [
    # name, clips, seconds, volume, start, stop, prompt
    ("Chop", 4, 3.0, "6~8", None, None, "chef's knife chopping vegetables on a wooden cutting board, irregular rhythm, steady and busy, occasional scrape"),
    ("Simmer", 3, 6.0, "5~7", "HH_Cook_Start_Lid", "HH_Cook_Start_Lid", "thick stew simmering in a heavy iron pot, soft bubbling, wooden spoon stirring now and then, cosy and warm, seamless loop"),
    ("Grill", 3, 6.0, "6~8", None, None, "meat searing on an open grill, fire crackle and sizzle hiss, occasional flare-up, seamless loop"),
    ("Bake", 3, 6.0, "5~7", "HH_Cook_Start_OvenDoor", "HH_Cook_Stop_OvenDing", "bread oven burning, low fire roar and soft wood crackle inside a stone oven, seamless loop"),
    ("PanFry", 3, 5.0, "6~8", None, None, "food sizzling in a pan, spatula scraping on iron, butter popping, seamless loop"),
    ("DeepFry", 3, 5.0, "6~8", "HH_Cook_Start_Basket", None, "deep fryer with vigorously bubbling hot oil, crackling fritters, seamless loop"),
    ("StirFry", 3, 5.0, "6~8", None, None, "wok stir-fry, flame whoosh, tossing food, metal spatula clanging against the wok, fast cadence, seamless loop"),
    ("Steam", 3, 6.0, "4~6", None, None, "steady steam hissing from a bamboo steamer, soft wooden lid rattle, seamless loop"),
    ("Knead", 3, 4.0, "6~8", None, None, "kneading dough on a wooden bench, rhythmic soft slaps and pushes, flour pats, seamless loop"),
    ("Smoke", 3, 6.0, "4~6", None, None, "smoker box smouldering, slow quiet hiss and occasional wood chip pop, seamless loop"),
    ("Chill", 3, 6.0, "4~6", "HH_Cook_Start_Latch", "HH_Cook_Start_Latch", "refrigerated cold counter, low electric hum, ice cubes clinking and a cold air flutter, seamless loop"),
]

# One-shots: name, clips, seconds, volume, prompt. Folder = HH/Kitchen/<name>
ONESHOTS = [
    ("Cook_Start_Lid", 1, 1.0, "9~10", "heavy iron pot lid being lifted and set back with a short clack"),
    ("Cook_Start_OvenDoor", 1, 1.2, "9~10", "stone oven door creaking open and shut with a soft thud"),
    ("Cook_Stop_OvenDing", 1, 1.2, "9~10", "small kitchen timer ding, one clear bright bell tone"),
    ("Cook_Start_Basket", 1, 1.0, "9~10", "wire frying basket lowered into hot oil with a splash and sizzle burst"),
    ("Cook_Start_Latch", 1, 0.8, "9~10", "metal fridge latch click and rubber seal release"),
    ("Cook_Done_Fine", 1, 1.5, "9~10", "soft restaurant service bell, single gentle ding with a short warm tail"),
    ("Cook_Done_Signature", 1, 2.0, "10~11", "short elegant chef's flourish, bright double bell ding with a tiny shimmer, celebratory but brief"),
    ("Plate_Drop_Ceramic", 2, 0.8, "9~11", "ceramic bowl set down on a wooden table, light clink"),
    ("Plate_Drop_Wood", 2, 0.8, "9~11", "wooden serving board set down on a table, dull friendly thud"),
    ("Plate_Drop_Glass", 2, 0.8, "9~11", "glass dessert dish set down on a table, a tiny bright clink"),
]

# Eating sounds (sustained while chewing, like vanilla Meal_Eat)
EATS = [
    ("Eat_Slurp", 3, 2.5, "8~10", "person slurping hot soup or noodles from a bowl, short and satisfied, no speech"),
    ("Eat_Crunch", 3, 2.0, "8~10", "person biting into crispy fried food, crunchy chewing, no speech"),
]
EAT_ONE = ("Eat_Satisfied", 2, 1.2, "9~10", "short contented 'mmm' of enjoying food, a tiny happy sigh, quiet, no words")


def clipname(name, i):
    return f"{name}_{i:02d}.ogg"


def sound_def(name, folder, vol, sustain, start=None, stop=None, loop_false=True, dist="10~45", pitch="0.94~1.08", filters=False, single=False):
    x = [f"  <SoundDef>", f"    <defName>HH_{name}</defName>", "    <context>MapOnly</context>"]
    if sustain:
        x += ["    <sustain>True</sustain>", "    <maxVoices>3</maxVoices>", "    <priorityMode>PrioritizeNearest</priorityMode>"]
        if start: x.append(f"    <sustainStartSound>{start}</sustainStartSound>")
        if stop: x.append(f"    <sustainStopSound>{stop}</sustainStopSound>")
        if start or stop: x.append("    <sustainFadeoutTime>0.25</sustainFadeoutTime>")
    else:
        x.append("    <maxSimultaneous>2</maxSimultaneous>")
    x += ["    <subSounds>", "      <li>"]
    if sustain: x.append("        <muteWhenPaused>True</muteWhenPaused>")
    x += ["        <grains>", f'          <li Class="AudioGrain_Folder"><clipFolderPath>HH/Kitchen/{folder}</clipFolderPath></li>', "        </grains>",
          f"        <volumeRange>{vol}</volumeRange>", f"        <pitchRange>{pitch}</pitchRange>"]
    if sustain or dist: x.append(f"        <distRange>{dist}</distRange>")
    if sustain and loop_false: x.append("        <sustainLoop>False</sustainLoop>")
    x += ["      </li>", "    </subSounds>", "  </SoundDef>"]
    return "\n".join(x)


MOTES = [  # name, color, fadeIn, solid, fadeOut, growth, accel z
    ("Steam", "(255,255,255)", 0.2, 0.5, 1.6, 0.35, "(0,0,0.0016)"),
    ("Flour", "(245,235,215)", 0.05, 0.25, 0.7, 0.2, "(0,0,0.0004)"),
    ("Smoke", "(150,150,150)", 0.3, 1.2, 2.4, 0.4, "(0,0,0.0010)"),
    ("Cold", "(190,225,255)", 0.2, 0.5, 1.4, 0.25, "(0,0,-0.0002)"),
]


def mote_xml():
    x = []
    for n, col, fi, so, fo, gr, acc in MOTES:
        x += ['  <ThingDef ParentName="MoteBase">', f"    <defName>HH_Mote_Cook{n}</defName>", "    <thingClass>MoteThrown</thingClass>",
              "    <altitudeLayer>MoteOverhead</altitudeLayer>",
              f"    <graphicData><texPath>Things/Mote/Smoke</texPath><color>{col}</color></graphicData>",
              f"    <mote><fadeInTime>{fi}</fadeInTime><solidTime>{so}</solidTime><fadeOutTime>{fo}</fadeOutTime><growthRate>{gr}</growthRate><acceleration>{acc}</acceleration></mote>",
              "  </ThingDef>"]
    return "\n".join(x)


def spray(mote, scale, ticks, radius="0.5", extra=""):
    return f"""      <li>
        <subEffecterClass>SubEffecter_SprayerContinuous</subEffecterClass>
        <scale>{scale}</scale>
        <ticksBetweenMotes>{ticks}</ticksBetweenMotes>
        <positionLerpFactor>0.6</positionLerpFactor>
        <positionRadius>{radius}</positionRadius>
        <moteDef>{mote}</moteDef>{extra}
      </li>"""


def effecter(name, *kids):
    return f"  <EffecterDef>\n    <defName>HH_Cook_{name}</defName>\n    <children>\n" + "\n".join(kids) + "\n    </children>\n  </EffecterDef>"


def main(audio=True):
    parts = ["<?xml version=\"1.0\" encoding=\"utf-8\"?>", "<Defs>",
             "  <!-- GENERATED by tools/dishes/make_sounds.py. Cooking loops per technique, one-shots, plate and eating sounds, small cooking effecters (F31). Clips: Common/Sounds/HH/Kitchen/<Name>/. -->"]
    sfx, files = [], []
    for name, n, sec, vol, start, stop, prompt in LOOPS:
        parts.append(sound_def("Cook_" + name, name, vol, True, start, stop))
        for i in range(1, n + 1):
            files.append((name, i)); sfx.append({"file": f"Common/Sounds/HH/Kitchen/{name}/{clipname(name, i)}", "prompt": f"{prompt}, take {i}, {NOVOICE}", "seconds": sec, "loop": False})
    for name, n, sec, vol, prompt in ONESHOTS:
        parts.append(sound_def(name, name, vol, False, dist="12~50", pitch="0.96~1.06"))
        for i in range(1, n + 1):
            files.append((name, i)); sfx.append({"file": f"Common/Sounds/HH/Kitchen/{name}/{clipname(name, i)}", "prompt": f"{prompt}, {NOVOICE}", "seconds": sec, "loop": False})
    for name, n, sec, vol, prompt in EATS:
        parts.append(sound_def(name, name, vol, True, pitch="0.9~1.0", filters=True))
        for i in range(1, n + 1):
            files.append((name, i)); sfx.append({"file": f"Common/Sounds/HH/Kitchen/{name}/{clipname(name, i)}", "prompt": f"{prompt}, take {i}, {NOVOICE}", "seconds": sec, "loop": False})
    name, n, sec, vol, prompt = EAT_ONE
    parts.append(sound_def(name, name, vol, False, dist="8~30", pitch="0.95~1.1"))
    for i in range(1, n + 1):
        files.append((name, i)); sfx.append({"file": f"Common/Sounds/HH/Kitchen/{name}/{clipname(name, i)}", "prompt": f"{prompt}, {NOVOICE}", "seconds": sec, "loop": False})
    parts.append("  <!-- Motes use the vanilla smoke texture (Things/Mote/Smoke), tinted. -->")
    parts.append(mote_xml())
    parts.append(effecter("Steam", spray("HH_Mote_CookSteam", "0.35~0.8", 18, "0.45")))
    parts.append(effecter("Flour", spray("HH_Mote_CookFlour", "0.15~0.35", 6, "0.5")))
    parts.append(effecter("Smoke", spray("HH_Mote_CookSmoke", "0.3~0.6", 28, "0.35")))
    parts.append(effecter("Chill", spray("HH_Mote_CookCold", "0.25~0.5", 24, "0.5")))
    parts.append(effecter("Chop", spray("Mote_CookBit", "0.2~0.45", 9, "0.45")))
    parts.append(effecter("Sparks", spray("Mote_SparkThrown", "0.14~0.26", 14, "0.3",
                  "\n        <airTime>0.2~0.5</airTime>\n        <rotationRate>-240~240</rotationRate>\n        <speed>1.5~3.5</speed>\n        <angle>-40~40</angle>")))
    parts.append("</Defs>")
    OUT.write_text("\n".join(parts) + "\n", encoding="utf-8")
    JSON.parent.mkdir(parents=True, exist_ok=True)
    JSON.write_text(json.dumps(sfx, indent=1, ensure_ascii=False) + "\n", encoding="utf-8")
    made = 0
    if audio:
        for name, i in files:
            p = SND / name / clipname(name, i)
            if p.exists(): continue
            p.parent.mkdir(parents=True, exist_ok=True)
            subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-f", "lavfi", "-i", "anullsrc=r=44100:cl=mono", "-t", "0.3", "-c:a", "libvorbis", str(p)], check=True)
            made += 1
    print(f"{len(files)} clips listed ({made} placeholders created) -> {JSON}")


if __name__ == "__main__":
    main("--no-audio" not in sys.argv)
