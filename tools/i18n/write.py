#!/usr/bin/env python3
"""Writes translated strings for one language.

    python3 tools/i18n/write.py "German (Deutsch)" tools/i18n/out/de

Input: <json-dir>/<Module>.json with the same shape as tools/i18n/src/<Module>.json (keyed + defs), values translated.
Output: Common/Languages/<lang>/Keyed/HH_<Module>.xml and Common/Languages/<lang>/DefInjected/<DefType>/HH_<Module>.xml.
Checks: every {0}/{PAWN_label}/[token] placeholder of the English text is present in the translation, else the English text is kept
and the key is listed as needing review.
"""
import json, re, sys
from pathlib import Path
from xml.sax.saxutils import escape

R = Path(__file__).resolve().parents[2]
PH = re.compile(r"\{[^}]+\}|\[[A-Za-z_]+\]|<color=[^>]+>|</color>")


def placeholders(s):
    return sorted(PH.findall(s or ""))


def main():
    lang, src = sys.argv[1], Path(sys.argv[2])
    en = {p.stem: json.loads(p.read_text(encoding="utf-8")) for p in (R / "tools/i18n/src").glob("*.json")}
    review = []
    base = R / "Common" / "Languages" / lang
    for p in sorted(src.glob("*.json")):
        mod = p.stem
        tr = json.loads(p.read_text(encoding="utf-8"))
        e = en.get(mod, {"keyed": {}, "defs": {}})
        keyed = []
        for k, v in e["keyed"].items():
            t = tr.get("keyed", {}).get(k)
            if not t or placeholders(t) != placeholders(v):
                review.append(f"{mod}:{k}"); t = v
            keyed.append(f"  <{k}>{escape(t)}</{k}>")
        if keyed:
            (base / "Keyed").mkdir(parents=True, exist_ok=True)
            (base / "Keyed" / f"HH_{mod}.xml").write_text('<?xml version="1.0" encoding="utf-8"?>\n<LanguageData>\n' + "\n".join(keyed) + "\n</LanguageData>\n", encoding="utf-8")
        by_type = {}
        for k, v in e["defs"].items():
            t = tr.get("defs", {}).get(k)
            if not t or placeholders(t) != placeholders(v):
                review.append(f"{mod}:{k}"); t = v
            dtype, rest = k.split("/", 1)
            by_type.setdefault(dtype, []).append(f"  <{rest}>{escape(t)}</{rest}>")
        for dtype, lines in by_type.items():
            d = base / "DefInjected" / dtype
            d.mkdir(parents=True, exist_ok=True)
            (d / f"HH_{mod}.xml").write_text('<?xml version="1.0" encoding="utf-8"?>\n<LanguageData>\n' + "\n".join(lines) + "\n</LanguageData>\n", encoding="utf-8")
    print(f"{lang}: written; {len(review)} strings kept in English for review")
    # this writes everything to Common; translations of optional content (MayRequire defs) then move to their conditional folder
    import subprocess
    subprocess.run([sys.executable, str(R / "tools" / "i18n" / "split_optional.py")], check=True)
    if review:
        (R / "tools" / "i18n" / f"review_{lang.split(' ')[0]}.txt").write_text("\n".join(review) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
