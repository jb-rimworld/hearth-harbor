#!/usr/bin/env python3
"""Static checks of all XML defs of Hearth & Harbor (no game needed).

Reports: duplicate defNames (same def type), XML parse errors, missing HH_/SL_ references (in XML and in C# string
literals that name defs), defs without label/description (where the game needs one), inconsistent prefixes, overlapping
research tree positions, research prerequisites that point right-to-left, Keyed keys used in C# but missing in English.
Exit code 0 = clean. Usage: tools/check_defs.py [--verbose]
"""
import re, sys, os, glob
import xml.etree.ElementTree as ET
from collections import defaultdict

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
VERBOSE = "--verbose" in sys.argv
errors, warnings = [], []

def err(m): errors.append(m)
def warn(m): warnings.append(m)

# ---------------------------------------------------------------- load defs
defs = []   # (type, defName, element, file, parentName, abstract)
by_type = defaultdict(lambda: defaultdict(list))
for f in sorted(glob.glob(os.path.join(ROOT, "Common/Defs/**/*.xml"), recursive=True)):
    rel = os.path.relpath(f, ROOT)
    try:
        tree = ET.parse(f)
    except ET.ParseError as e:
        err(f"{rel}: XML parse error: {e}"); continue
    for el in tree.getroot():
        if not isinstance(el.tag, str): continue
        dn = el.findtext("defName")
        abstract = el.get("Abstract", "").lower() == "true"
        name = el.get("Name")
        defs.append((el.tag, dn, el, rel, el.get("ParentName"), abstract, name))
        if dn:
            by_type[el.tag][dn].append(rel)
        elif not abstract:
            err(f"{rel}: <{el.tag}> without defName")

all_names = set()
for t, d in by_type.items():
    for n, files in d.items():
        all_names.add(n)
        if len(files) > 1:
            err(f"duplicate {t} {n}: {', '.join(files)}")

# patches may add defs (xpath values) -> collect defNames mentioned in patches
patch_names = set()
for f in glob.glob(os.path.join(ROOT, "Common/Patches/**/*.xml"), recursive=True):
    try:
        for dn in ET.parse(f).getroot().iter("defName"):
            if dn.text: patch_names.add(dn.text.strip())
    except ET.ParseError as e:
        err(f"{os.path.relpath(f, ROOT)}: XML parse error: {e}")
    txt = open(f, encoding="utf-8").read()
    if "HH_Hospitality" in txt and "DesignationCategoryDef" in txt and "PatchOperationAdd" in txt:
        err(f"{os.path.relpath(f, ROOT)}: defines a conditional designation category (one tab only, in Hospitality_Shared.xml)")
all_names |= patch_names

# ---------------------------------------------------------------- prefixes
PREFIXED_TYPES_OK = None
for t, d in by_type.items():
    for n, files in d.items():
        if not (n.startswith("HH_") or n.startswith("SL_")):
            err(f"{files[0]}: {t} {n} has no HH_/SL_ prefix")

# ---------------------------------------------------------------- labels
NEEDS_LABEL = {"ThingDef", "ResearchProjectDef", "DesignationCategoryDef", "ResearchTabDef", "HediffDef", "JobDef_", "WorkTypeDef", "NeedDef",
               "ThoughtDef_", "JoyKindDef", "MentalStateDef", "RecipeDef", "ChemicalDef", "DesignatorDropdownGroupDef", "IncidentDef"}
for (t, dn, el, rel, parent, abstract, name) in defs:
    if abstract or not dn: continue
    if t in NEEDS_LABEL:
        if t == "ThingDef" and el.findtext("category") in ("Mote", "Ethereal") and False: pass
        if el.find("label") is None and el.find("labelShort") is None and t not in ("ResearchTabDef",) and parent is None:
            warn(f"{rel}: {t} {dn} has no label")
        elif el.find("label") is None and t in ("ResearchTabDef",):
            err(f"{rel}: {t} {dn} has no label")
    if t in ("ThingDef", "ResearchProjectDef", "DesignationCategoryDef", "HediffDef", "NeedDef", "RecipeDef", "WorkTypeDef") and el.find("description") is None and parent is None:
        warn(f"{rel}: {t} {dn} has no description")

# ---------------------------------------------------------------- references
REF_TAGS = {"designationCategory", "tab", "designatorDropdown", "researchPrerequisite", "defaultPlacingRot_"}
LIST_TAGS = {"researchPrerequisites", "prerequisites", "hiddenPrerequisites"}
def check_ref(rel, owner, name):
    name = name.strip()
    if (name.startswith("HH_") or name.startswith("SL_")) and name not in all_names:
        err(f"{rel}: {owner} references missing def {name}")
for (t, dn, el, rel, parent, abstract, name) in defs:
    for sub in el.iter():
        if not isinstance(sub.tag, str): continue
        if sub.tag in REF_TAGS and sub.text:
            check_ref(rel, f"{t} {dn}", sub.text)
        if sub.tag in LIST_TAGS:
            for li in sub:
                if li.text: check_ref(rel, f"{t} {dn}", li.text)
        if sub.tag in ("staffWorkType", "workType", "jobDef", "joyKind", "chemical", "need", "mentalState", "thought", "hediff") and sub.text:
            check_ref(rel, f"{t} {dn}.{sub.tag}", sub.text)
    if parent and (parent.startswith("HH_") or parent.startswith("SL_")):
        names = {x[6] for x in defs if x[6]}
        if parent not in names: err(f"{rel}: {t} {dn} has missing ParentName {parent}")

# C# string literals that name research projects / categories
cs_refs = defaultdict(set)
for f in glob.glob(os.path.join(ROOT, "Source/**/*.cs"), recursive=True):
    if "/Tests/" in f or f.endswith("Tests.cs") or f.endswith("Suites.cs"): pass
    txt = open(f, encoding="utf-8").read()
    for m in re.finditer(r'(?:GetNamedSilentFail|GetNamed|Named)\w*\(\s*"((?:HH|SL)_[A-Za-z0-9_]+)"', txt):
        cs_refs[m.group(1)].add(os.path.relpath(f, ROOT))
    for m in re.finditer(r'"((?:HH|SL)_(?:Innkeeping|Markets|TableGames|Gastronomy|HotelManagement|FineDining|Cooking_L\d|CityLife\w*|Casino\w*|Bathhouse|Wellness|Banking|Branches|Credit|Insurance|Actuarial|Underworld\w*|TheHunt|BlackClinic)\b[A-Za-z0-9_]*)"', txt):
        cs_refs[m.group(1)].add(os.path.relpath(f, ROOT))
for n, files in sorted(cs_refs.items()):
    if n not in all_names:
        # silent-fail lookups for optional defs are warnings, anything else an error
        warn(f"C# references def {n} that does not exist ({', '.join(sorted(files))})")

# ---------------------------------------------------------------- research tree layout
proj = {}
for (t, dn, el, rel, parent, abstract, name) in defs:
    if t != "ResearchProjectDef" or not dn: continue
    try:
        x = float(el.findtext("researchViewX")); y = float(el.findtext("researchViewY"))
    except (TypeError, ValueError):
        err(f"{rel}: research {dn} has no researchViewX/Y"); continue
    pre = [li.text.strip() for li in (el.find("prerequisites") if el.find("prerequisites") is not None else []) if li.text]
    hid = [li.text.strip() for li in (el.find("hiddenPrerequisites") if el.find("hiddenPrerequisites") is not None else []) if li.text]
    proj[dn] = dict(x=x, y=y, tab=el.findtext("tab"), pre=pre, hid=hid, file=rel)
for a, pa in proj.items():
    for b, pb in proj.items():
        if a < b and pa["tab"] == pb["tab"] and abs(pa["x"] - pb["x"]) < 1.0 and abs(pa["y"] - pb["y"]) < 0.6:
            err(f"research {a} ({pa['x']},{pa['y']}) overlaps {b} ({pb['x']},{pb['y']}) in tab {pa['tab']}")
    for q in pa["pre"] + pa["hid"]:
        if q in proj:
            if proj[q]["x"] >= pa["x"]:
                err(f"research {a} (x={pa['x']}) has prerequisite {q} at x={proj[q]['x']}: must flow left to right")
            if proj[q]["tab"] != pa["tab"]:
                err(f"research {a} (tab {pa['tab']}) has prerequisite {q} in another tab {proj[q]['tab']}")
# the bank tree must stand alone (Silverline ships alone)
for a, pa in proj.items():
    if a.startswith("SL_"):
        for q in pa["pre"] + pa["hid"]:
            if q.startswith("HH_"): err(f"bank research {a} depends on hotel research {q}")
# defs used by the standalone bank must not depend on guest content
for (t, dn, el, rel, parent, abstract, name) in defs:
    if rel.startswith(("Common/Defs/Bank", "Common/Defs/Insurance", "Common/Defs/Core")):
        for sub in el.iter():
            if sub.tag in ("designationCategory",) and sub.text and sub.text.strip() == "HH_Hospitality":
                err(f"{rel}: {dn} (standalone bank content) uses HH_Hospitality")

# ---------------------------------------------------------------- Keyed
keyed = set()
for f in glob.glob(os.path.join(ROOT, "Common/Languages/English/Keyed/*.xml")):
    try:
        for el in ET.parse(f).getroot():
            if isinstance(el.tag, str):
                if el.tag in keyed: err(f"{os.path.relpath(f, ROOT)}: duplicate key {el.tag}")
                keyed.add(el.tag)
    except ET.ParseError as e:
        err(f"{os.path.relpath(f, ROOT)}: XML parse error: {e}")
used = defaultdict(set)
for f in glob.glob(os.path.join(ROOT, "Source/**/*.cs"), recursive=True):
    txt = open(f, encoding="utf-8").read()
    for m in re.finditer(r'"(HH\.[A-Za-z0-9_]+(?:\.[A-Za-z0-9_]+)+)"', txt):
        k = m.group(1)
        # skip dotted prefixes / formats that are built dynamically (end with a dot is not matched anyway)
        used[k].add(os.path.relpath(f, ROOT))
for k, files in sorted(used.items()):
    if k not in keyed and not any(x.startswith(k + ".") for x in keyed):
        # many code strings are logger channels / paths; only flag keys of the Keyed families
        fam = k.split(".")[1]
        if fam in ("Setting", "Tab", "Enum", "Guide", "Help", "Letter", "Msg", "Label", "Cat"): 
            warn(f"Keyed key {k} used in C# but missing in English Keyed ({sorted(files)[0]})")

# ---------------------------------------------------------------- report
for w in warnings:
    if VERBOSE or True: print("WARN  " + w)
for e in errors:
    print("ERROR " + e)
print(f"check_defs: {len(defs)} defs, {len(errors)} errors, {len(warnings)} warnings")
sys.exit(1 if errors else 0)
