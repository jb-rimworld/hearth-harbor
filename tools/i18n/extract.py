#!/usr/bin/env python3
"""Collects every translatable English string of the mod into one JSON per module (input for translation batches).

    python3 tools/i18n/extract.py            → tools/i18n/src/<Module>.json  {"keyed": {key: text}, "defs": {"DefType/defName.field": text}}
    python3 tools/i18n/write.py <lang-folder> <json-dir>   → writes Keyed + DefInjected for one language

Translatable def fields follow vanilla: label, description, labelShort, pawnLabel, gerundLabel, verb, jobString, reportString,
letterLabel, letterText, baseInspectLine, labelNoun, labelNounPretty, rulesStrings are NOT extracted (grammar stays English).
"""
import json, re, sys
import xml.etree.ElementTree as ET
from pathlib import Path

R = Path(__file__).resolve().parents[2]
OUT = R / "tools" / "i18n" / "src"
FIELDS = {"label", "description", "labelShort", "pawnLabel", "gerundLabel", "verb", "jobString", "reportString", "letterLabel",
          "letterText", "baseInspectLine", "labelNoun", "labelNounPretty", "pawnsPlural", "beginLetter", "beginLetterLabel",
          "successfulInspectLine", "fixedName", "labelMale", "labelFemale", "inspectString", "skillLabel", "labelPlural", "slogan"}


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
    for f in sorted((R / "Common" / "Defs").rglob("*.xml")):
        mod = module_of(f)
        try:
            root = ET.parse(f).getroot()
        except ET.ParseError as e:
            print("XML error", f, e); continue
        for d in root:
            dn = d.findtext("defName")
            if not dn or d.get("Abstract") == "True":
                continue
            for child in d:
                if child.tag in FIELDS and child.text and child.text.strip():
                    data.setdefault(mod, {"keyed": {}, "defs": {}})["defs"][f"{d.tag}/{dn}.{child.tag}"] = child.text.strip()
                # one level of nested lists commonly translated: stages/li/label
                if child.tag == "stages":
                    for i, st in enumerate(child):
                        for sub in st:
                            if sub.tag in ("label", "description") and sub.text and sub.text.strip():
                                data.setdefault(mod, {"keyed": {}, "defs": {}})["defs"][f"{d.tag}/{dn}.stages.{i}.{sub.tag}"] = sub.text.strip()
    total = 0
    for mod, d in data.items():
        (OUT / f"{mod}.json").write_text(json.dumps(d, ensure_ascii=False, indent=1), encoding="utf-8")
        n = len(d["keyed"]) + len(d["defs"]); total += n
        print(f"{mod:14} keyed {len(d['keyed']):4}  defs {len(d['defs']):4}")
    print("total strings", total)


if __name__ == "__main__":
    main()
