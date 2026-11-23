#!/usr/bin/env python3
"""Incremental translation (1.1.0): translate only new or changed English strings, keep every existing translation.

    python3 tools/i18n/delta.py plan <old_src_dir>        → tools/i18n/delta/<code>/<n>.json   (flat {"k:key"|"d:Type/def.field": English})
    python3 tools/i18n/delta.py apply                     ← tools/i18n/delta_out/<code>/<n>.json (same keys, translated values)

plan:  todo(lang) = English keys that are new since <old_src_dir>, whose English text changed, or that the language file lacks.
apply: updates the language files in place: a key that already exists in some file of that language (Keyed or the
       DefInjected/<Type> folder) is replaced there; new keys go into the module's file (Keyed/HH_<Module>.xml,
       DefInjected/<Type>/HH_<Module>.xml); keys that no longer exist in English are removed. Placeholders ({0}, [token],
       <color>) must match the English text, else the English text is used and the key is listed in delta/review_<code>.txt.
"""
import json, re, sys
import xml.etree.ElementTree as ET
from pathlib import Path
from xml.sax.saxutils import escape

R = Path(__file__).resolve().parents[2]
I = R / "tools" / "i18n"
LANGS = {"de": "German (Deutsch)", "fr": "French (Français)", "es": "Spanish (Español(Castellano))", "pt": "PortugueseBrazilian (Português Brasileiro)",
         "ru": "Russian (Русский)", "uk": "Ukrainian (Українська)", "cs": "Czech (Čeština)", "zh": "ChineseSimplified (简体中文)",
         "ja": "Japanese (日本語)", "ko": "Korean (한국어)"}
PH = re.compile(r"\{[^}]+\}|\[[A-Za-z_]+\]|<color=[^>]+>|</color>")
N = 120


def load_src(d):
    out = {}  # flat key -> (module, english)
    for p in sorted(Path(d).glob("*.json")):
        j = json.loads(p.read_text(encoding="utf-8"))
        for k, v in j.get("keyed", {}).items(): out["k:" + k] = (p.stem, v)
        for k, v in j.get("defs", {}).items(): out["d:" + k] = (p.stem, v)
    return out


def lang_files(lang):
    base = R / "Common" / "Languages" / lang
    return list((base / "Keyed").glob("*.xml")) + list((base / "DefInjected").rglob("*.xml")), base


def lang_entries(lang):
    files, base = lang_files(lang)
    have = {}
    for f in files:
        try:
            root = ET.parse(f).getroot()
        except Exception as e:
            print("bad xml", f, e); continue
        if f.parent.name == "Keyed":
            for e in root: have["k:" + e.tag] = f
        else:
            t = f.parent.relative_to(base / "DefInjected").as_posix()
            for e in root: have[f"d:{t}/{e.tag}"] = f
    return have


def plan(old_dir):
    new, old = load_src(I / "src"), load_src(old_dir)
    out = I / "delta"
    if out.exists():
        for p in out.rglob("*.json"): p.unlink()
    total = {}
    for code, lang in LANGS.items():
        have = lang_entries(lang)
        todo = {k: v for k, (m, v) in new.items() if v and v.strip() and (k not in have or k not in old or old[k][1] != v)}
        items = sorted(todo.items())
        d = out / code; d.mkdir(parents=True, exist_ok=True)
        for i in range(0, len(items), N):
            (d / f"{i // N:03d}.json").write_text(json.dumps(dict(items[i:i + N]), ensure_ascii=False, indent=1), encoding="utf-8")
        total[code] = len(items)
    print(json.dumps(total))


def write_xml(path, pairs):
    path.parent.mkdir(parents=True, exist_ok=True)
    body = "\n".join(f"  <{k}>{escape(v)}</{k}>" for k, v in pairs)
    path.write_text('<?xml version="1.0" encoding="utf-8"?>\n<LanguageData>\n' + body + ("\n" if body else "") + "</LanguageData>\n", encoding="utf-8")


# def fields extract.py collects (keep in sync with extract.FIELDS); "stages.<n>.label/description" too
FIELDS = {"label", "description", "labelShort", "pawnLabel", "gerundLabel", "verb", "jobString", "reportString", "letterLabel",
          "letterText", "baseInspectLine", "labelNoun", "labelNounPretty", "pawnsPlural", "beginLetter", "beginLetterLabel",
          "successfulInspectLine", "fixedName", "labelMale", "labelFemale", "inspectString", "skillLabel", "labelPlural", "slogan"}
STAGE = re.compile(r"^stages\.\d+\.(label|description)$")


def managed(fk, known_defs):
    """True for a key apply() may delete when it is missing from src."""
    if fk.startswith("k:"):
        return True
    dtype, rest = fk[2:].split("/", 1)
    defname, _, field = rest.partition(".")
    if f"{dtype}/{defname}" not in known_defs:
        return False
    return field in FIELDS or bool(STAGE.match(field))


def apply():
    new = load_src(I / "src")
    known_defs = {k[2:].split(".", 1)[0] for k in new if k.startswith("d:")}
    for code, lang in LANGS.items():
        tr = {}
        for p in sorted((I / "delta_out" / code).glob("*.json")):
            try:
                tr.update(json.loads(p.read_text(encoding="utf-8")))
            except Exception as e:
                print("bad json", p, e)
        review = []
        files, base = lang_files(lang)
        # load every file into ordered pairs
        content = {}
        for f in files:
            try:
                content[f] = [(e.tag, e.text or "") for e in ET.parse(f).getroot()]
            except Exception:
                content[f] = []
        index = {}
        for f, pairs in content.items():
            pre = "k:" if f.parent.name == "Keyed" else "d:" + f.parent.relative_to(base / "DefInjected").as_posix() + "/"
            for i, (k, _) in enumerate(pairs): index[pre + k] = (f, i)
        # remove keys that no longer exist in English, but only keys extract.py manages: Keyed keys, and def fields it extracts of defs
        # it knows. Everything else (rulesStrings of RulePackDefs, scenario parts, defs from the version folders such as 1.7/Defs/Scenarios)
        # is never in src and was translated by hand: deleting it wiped the German venue names and the scenario texts in 1.1.0.
        for fk, (f, i) in list(index.items()):
            if fk not in new and managed(fk, known_defs):
                content[f][i] = None
        # update / add
        for fk, t in tr.items():
            if fk not in new: continue
            mod, en = new[fk]
            if not t or sorted(PH.findall(t)) != sorted(PH.findall(en)):
                review.append(fk); t = en
            if fk in index:
                f, i = index[fk]
                content[f][i] = (content[f][i][0] if content[f][i] else fk.split(":", 1)[1].split("/", 1)[-1], t)
            else:
                if fk.startswith("k:"):
                    f, tag = base / "Keyed" / f"HH_{mod}.xml", fk[2:]
                else:
                    dtype, tag = fk[2:].split("/", 1)
                    f = base / "DefInjected" / dtype / f"HH_{mod}.xml"
                content.setdefault(f, []).append((tag, t))
        for f, pairs in content.items():
            pairs = [p for p in pairs if p]
            if pairs: write_xml(f, pairs)
            elif f.exists(): f.unlink()
        if review: (I / "delta" / f"review_{code}.txt").write_text("\n".join(review) + "\n", encoding="utf-8")
        print(code, "applied", len(tr), "translations;", len(review), "kept English (placeholder mismatch or empty)")


if __name__ == "__main__":
    if sys.argv[1] == "plan": plan(sys.argv[2])
    elif sys.argv[1] == "apply": apply()
