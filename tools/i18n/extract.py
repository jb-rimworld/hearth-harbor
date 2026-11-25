#!/usr/bin/env python3
"""Collects every translatable English string of the mod into one JSON per module (input for translation batches).

    python3 tools/i18n/extract.py            → tools/i18n/src/<Module>.json  {"keyed": {key: text}, "defs": {"DefType/defName.field": text}}
    python3 tools/i18n/write.py <lang-folder> <json-dir>   → writes Keyed + DefInjected for one language

Translatable def fields follow vanilla: label, description, labelShort, pawnLabel, gerundLabel, verb, jobString, reportString,
letterLabel, letterText, baseInspectLine, labelNoun, labelNounPretty … (FIELDS), text fields of our own def types (TYPE_FIELDS) and texts inside
lists of a def (NESTED). rulesStrings are NOT extracted (grammar stays English).
"""
import json, re, sys
import xml.etree.ElementTree as ET
from pathlib import Path

R = Path(__file__).resolve().parents[2]
OUT = R / "tools" / "i18n" / "src"
FIELDS = {"label", "description", "labelShort", "pawnLabel", "gerundLabel", "verb", "jobString", "reportString", "letterLabel",
          "letterText", "baseInspectLine", "labelNoun", "labelNounPretty", "pawnsPlural", "beginLetter", "beginLetterLabel",
          "successfulInspectLine", "fixedName", "labelMale", "labelFemale", "inspectString", "skillLabel", "labelPlural", "slogan",
          # 1.1.2: vanilla text fields our defs use that were never collected
          "title", "titleShort", "pawnSingular", "gerund", "recoveryMessage", "letterTitle", "calledOffMessage", "finishedMessage"}

# text fields of our own def types (direct children of the def)
TYPE_FIELDS = {
    "Hearth.Kitchen.DishDef": {"inspiration"},          # fromMod is the name of a mod and stays as it is
    "Silverline.AssetDef": {"tagline"},
    "Silverline.SL_ClauseDef": {"text"},
    "Silverline.SL_PolicyDef": {"coversText", "benefitBare", "benefitGood", "benefitPremium"},
}

# Texts inside lists of a def. The game injects them by position (DefInjected path <defName>.<list>.<n>.<field>, checked against
# DefInjectionPackage.SetDefFieldAtPath of 1.7: every path part is resolved on the object at hand, so this also reaches our own
# classes and the items of modExtensions). Only what is named here is collected; 1.1.1 found the guide checklist English in every
# language because "steps" was missing, 1.1.2 the rest (venue requirements, trait degrees, riders, claim forms, precept comps).
#   (def types or None = any,  path below the def with "li" for a list item,  fields of the item or None = the item itself is the text,
#    {Class attribute: handle} = address the item by this handle instead of its position and skip items of other classes)
# A handle is needed where the position is not ours to know: the comps of a ThingDef start with those of its vanilla parent, and
# CompProperties carries [TranslationHandle] on compClass, so "comps.CompUsable.useLabel" finds it wherever it sits.
NESTED = (
    (None, "stages/li", ("label", "description"), None),
    (None, "steps/li", ("label", "description"), None),
    ({"TraitDef"}, "degreeDatas/li", ("label", "description", "labelMale", "labelFemale"), None),
    ({"PreceptDef"}, "comps/li", ("description",), None),
    ({"ThingDef"}, "comps/li", ("useLabel",), {"CompProperties_Usable": "CompUsable"}),
    ({"Hearth.VenueTypeDef"}, "modExtensions/li/requirements/li", ("label", "hint"), None),
    ({"Silverline.SL_PolicyDef"}, "riders/li", ("label", "description"), None),
    ({"Silverline.SL_ClaimFormDef"}, "fields/li", ("label", "description"), None),
    ({"Silverline.SL_ClaimFormDef"}, "fields/li/options/li", ("label",), None),
    ({"Silverline.SL_BankDef"}, "perks/li", None, None),
)


def top_fields(dtype):
    return FIELDS | TYPE_FIELDS.get(dtype, set())


def nested_pattern(dtype):
    """Regex for the nested field paths extract collects for this def type (delta.py uses it to know which keys it manages)."""
    alts = []
    for types, path, fields, handles in NESTED:
        if types is not None and dtype not in types:
            continue
        item = "(?:" + "|".join(re.escape(h) for h in handles.values()) + ")" if handles else r"\d+"
        segs = path.split("/")
        rx = r"\.".join(item if seg == "li" else re.escape(seg) for seg in segs)
        alts.append(rx + (r"\.(?:" + "|".join(fields) + ")" if fields else ""))
    return re.compile("^(?:" + "|".join(alts) + ")$") if alts else None


def nested_strings(d, dtype):
    """[(path below the def, text)] of one def element following NESTED."""
    out = []
    for types, path, fields, handles in NESTED:
        if types is not None and dtype not in types:
            continue
        segs = path.split("/")

        def walk(el, i, prefix):
            if i == len(segs):
                if fields is None:
                    if len(el) == 0 and el.text and el.text.strip():
                        out.append((prefix, el.text.strip()))
                    return
                for sub in el:
                    if sub.tag in fields and len(sub) == 0 and sub.text and sub.text.strip():
                        out.append((f"{prefix}.{sub.tag}", sub.text.strip()))
                return
            seg = segs[i]
            if seg == "li":
                last = i == len(segs) - 1
                for n, item in enumerate(el):
                    if handles and last:
                        h = handles.get(item.get("Class"))
                        if h is None:
                            continue
                        walk(item, i + 1, f"{prefix}.{h}")
                    else:
                        walk(item, i + 1, f"{prefix}.{n}")
            else:
                for child in el:
                    if child.tag == seg:
                        walk(child, i + 1, f"{prefix}.{seg}" if prefix else seg)

        walk(d, 0, "")
    return out


def inherited_lists(d, parents):
    """Top-level list tags a def gets from parents defined in this mod: positions in such a list do not start at 0 in this file, so
    a path by position would hit the wrong item. Returns {tag: name of the parent that brings items}. Vanilla parents cannot be
    read here; the abstract bases we use (AddictionBase …) bring no stages, and a wrong position shows up as a translation load
    error in the smoke test."""
    out, seen, p = {}, set(), d.get("ParentName")
    while p and p not in seen:
        seen.add(p)
        par = parents.get(p)
        if par is None:
            break
        for child in par:
            if len(child) and child.tag not in out:
                out[child.tag] = p
        p = par.get("ParentName")
    return out


def module_of(path: Path) -> str:
    parts = path.relative_to(R / "Common").parts
    if parts[0] == "Defs" and len(parts) > 2:
        return parts[1]
    m = re.match(r"HH_(\w+)\.xml", path.name)
    return m.group(1) if m else "Core"


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    data = {}
    for f in sorted((R / "Common" / "Languages" / "English" / "Keyed").glob("*.xml")):
        mod = module_of(f)
        root = ET.parse(f).getroot()
        for el in root:
            if el.text and el.text.strip():
                data.setdefault(mod, {"keyed": {}, "defs": {}})["keyed"][el.tag] = el.text
    def_files = sorted((R / "Common" / "Defs").rglob("*.xml"))
    roots, parents = {}, {}
    for f in def_files:
        try:
            roots[f] = ET.parse(f).getroot()
        except ET.ParseError as e:
            print("XML error", f, e); continue
        for d in roots[f]:
            if d.get("Name"):
                parents[d.get("Name")] = d
    problems = []
    for f, root in roots.items():
        mod = module_of(f)
        for d in root:
            dn = d.findtext("defName")
            if not dn or d.get("Abstract") == "True":
                continue
            defs = data.setdefault(mod, {"keyed": {}, "defs": {}})["defs"]
            fields = top_fields(d.tag)
            for child in d:
                if child.tag in fields and len(child) == 0 and child.text and child.text.strip():
                    defs[f"{d.tag}/{dn}.{child.tag}"] = child.text.strip()
            found = nested_strings(d, d.tag)
            if found:
                # an item that only exists with a DLC or mod shifts the position of every item after it for players without it
                for top in {path.split(".", 1)[0] for path, _ in found if path.split(".")[1].isdigit()}:
                    for li in d.find(top).iter("li"):
                        if li.get("MayRequire") or li.get("MayRequireAnyOf"):
                            problems.append(f"{f.relative_to(R)}: {dn}.{top}: a list item with MayRequire; positions differ between players")
                inh = inherited_lists(d, parents)
                for path, text in found:
                    top = path.split(".", 1)[0]
                    by_position = path.split(".")[1].isdigit()
                    own = d.find(top)
                    if by_position and own is not None and own.get("Inherit") != "False" and top in inh:
                        problems.append(f"{f.relative_to(R)}: {dn}.{path}: <{top}> also gets items from parent {inh[top]}; "
                                        "the position in this file is not the position in the game")
                        continue
                    defs[f"{d.tag}/{dn}.{path}"] = text
    if problems:
        print("\n".join(problems))
        sys.exit("extract: these lists cannot be addressed by position (inherited items: give the list Inherit=\"False\"; otherwise teach NESTED a handle)")
    total = 0
    for mod, d in data.items():
        (OUT / f"{mod}.json").write_text(json.dumps(d, ensure_ascii=False, indent=1), encoding="utf-8")
        n = len(d["keyed"]) + len(d["defs"]); total += n
        print(f"{mod:14} keyed {len(d['keyed']):4}  defs {len(d['defs']):4}")
    print("total strings", total)


if __name__ == "__main__":
    main()
