#!/usr/bin/env python3
"""Incremental translation (1.1.0): translate only new or changed English strings, keep every existing translation.

    python3 tools/i18n/delta.py plan <old_src_dir>        → tools/i18n/delta/<code>/<n>.json   (flat {"k:key"|"d:Type/def.field": English})
    python3 tools/i18n/delta.py plan git:<ref>            the same against the English of a git ref (e.g. git:main, the last release):
                                                          its Common/ is extracted with this extract.py into a temp folder, so a text
                                                          whose English changed since then is planned again even when it is translated
    python3 tools/i18n/delta.py apply                     ← tools/i18n/delta_out/<code>/<n>.json (same keys, translated values)

plan:  todo(lang) = English keys that are new since <old_src_dir>, whose English text changed, or that the language file lacks.
       Also writes delta/stale_<code>.txt: keys of that language that no longer exist in English and that apply will remove.
apply: updates the language files in place: a key that already exists in some file of that language (Keyed or the
       DefInjected/<Type> folder) is replaced there; new keys go into the module's file (Keyed/HH_<Module>.xml,
       DefInjected/<Type>/HH_<Module>.xml); keys that no longer exist in English are removed. Placeholders ({0}, [token],
       <color>) must match the English text, else the English text is used and the key is listed in delta/review_<code>.txt.
"""
import json, re, shutil, subprocess, sys, tempfile
import xml.etree.ElementTree as ET
from pathlib import Path
from xml.sax.saxutils import escape

sys.path.insert(0, str(Path(__file__).resolve().parent))
import extract  # noqa: E402  (the rules which def texts are collected)

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
    """Every file of a language: Common/Languages/<lang> plus the conditional folders Compat/<slug>/Languages/<lang> (translations of
    content that needs a DLC or another mod; tools/i18n/split_optional.py moves them there)."""
    base = R / "Common" / "Languages" / lang
    files = list((base / "Keyed").glob("*.xml")) + list((base / "DefInjected").rglob("*.xml"))
    for c in sorted((R / "Compat").glob("*/Languages/" + glob_escape(lang) + "/DefInjected")) if (R / "Compat").exists() else []:
        files += list(c.rglob("*.xml"))
    return files, base


def glob_escape(s):
    return re.sub(r"([\[\]*?])", r"[\1]", s)


def def_type(f):
    """DefInjected folder of a file as the game sees it: the path below .../DefInjected (Common or Compat)."""
    parts = f.parent.parts
    i = len(parts) - 1 - parts[::-1].index("DefInjected")
    return "/".join(parts[i + 1:])


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
            t = def_type(f)
            for e in root: have[f"d:{t}/{e.tag}"] = f
    return have


def src_of_ref(ref):
    """English source of a git ref, extracted with today's extract.py (its tables) from that ref's Common/: {flat key: (module, English)}."""
    tmp = Path(tempfile.mkdtemp(prefix="hh_i18n_"))
    try:
        arc = subprocess.run(["git", "-C", str(R), "archive", ref, "Common"], check=True, capture_output=True).stdout
        subprocess.run(["tar", "-x", "-C", str(tmp)], input=arc, check=True)
        (tmp / "tools" / "i18n" / "src").mkdir(parents=True)
        shutil.copy(I / "extract.py", tmp / "tools" / "i18n" / "extract.py")
        subprocess.run([sys.executable, str(tmp / "tools" / "i18n" / "extract.py")], check=True, capture_output=True)
        return load_src(tmp / "tools" / "i18n" / "src")
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


def plan(old_dir):
    new = load_src(I / "src")
    old = src_of_ref(old_dir[4:]) if old_dir.startswith("git:") else load_src(old_dir)
    known_defs = known_defs_of(new)
    out = I / "delta"
    if out.exists():
        for p in out.rglob("*.json"): p.unlink()
        for p in out.glob("stale_*.txt"): p.unlink()
    total = {}
    for code, lang in LANGS.items():
        have = lang_entries(lang)
        missing = {k for k in new if k not in have}
        changed = {k for k, (m, v) in new.items() if k in have and (k not in old or old[k][1] != v)}
        todo = {k: v for k, (m, v) in new.items() if v and v.strip() and (k in missing or k in changed)}
        items = sorted(todo.items())
        d = out / code; d.mkdir(parents=True, exist_ok=True)
        for i in range(0, len(items), N):
            (d / f"{i // N:03d}.json").write_text(json.dumps(dict(items[i:i + N]), ensure_ascii=False, indent=1), encoding="utf-8")
        stale = sorted(k for k in have if k not in new and managed(k, known_defs))
        (out / f"stale_{code}.txt").write_text("".join(k + "\n" for k in stale), encoding="utf-8")
        total[code] = {"todo": len(items), "missing": len(missing & todo.keys()), "changed": len(changed & todo.keys()), "stale": len(stale)}
    print(json.dumps(total))


def write_xml(path, pairs):
    path.parent.mkdir(parents=True, exist_ok=True)
    body = "\n".join(f"  <{k}>{escape(v)}</{k}>" for k, v in pairs)
    path.write_text('<?xml version="1.0" encoding="utf-8"?>\n<LanguageData>\n' + body + ("\n" if body else "") + "</LanguageData>\n", encoding="utf-8")


def known_defs_of(src):
    """{"<DefType>/<defName>"} of every def in src. The def type may contain dots (Silverline.SL_PolicyDef), the defName never does;
    until 1.1.2 this cut at the first dot of the whole key, so no def of our own types was ever known and their stale keys stayed."""
    out = set()
    for k in src:
        if k.startswith("d:"):
            dtype, rest = k[2:].split("/", 1)
            out.add(dtype + "/" + rest.split(".", 1)[0])
    return out


def managed(fk, known_defs):
    """True for a key apply() may delete when it is missing from src: a Keyed key, or a def field extract.py collects (its FIELDS,
    TYPE_FIELDS and NESTED rules, read from extract.py itself so the two cannot drift apart) of a def it knows."""
    if fk.startswith("k:"):
        return True
    dtype, rest = fk[2:].split("/", 1)
    defname, _, field = rest.partition(".")
    if f"{dtype}/{defname}" not in known_defs:
        return False
    if field in extract.top_fields(dtype):
        return True
    if dtype not in _NESTED_RX:
        _NESTED_RX[dtype] = extract.nested_pattern(dtype)
    return bool(_NESTED_RX[dtype] and _NESTED_RX[dtype].match(field))


_NESTED_RX = {}


def apply():
    new = load_src(I / "src")
    known_defs = known_defs_of(new)
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
        # only files whose entries change are written back: a file is rewritten as flat <key>text</key> pairs, which would empty the
        # hand-made nested ones (rulesStrings lists of RulePackDefs) if they were rewritten too (that happened in 1.1.0 and again in 1.1.1)
        before = {f: list(pairs) for f, pairs in content.items()}
        index = {}
        for f, pairs in content.items():
            pre = "k:" if f.parent.name == "Keyed" else "d:" + def_type(f) + "/"
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
            if pairs == before.get(f): continue
            if pairs: write_xml(f, pairs)
            elif f.exists(): f.unlink()
        if review: (I / "delta" / f"review_{code}.txt").write_text("\n".join(review) + "\n", encoding="utf-8")
        print(code, "applied", len(tr), "translations;", len(review), "kept English (placeholder mismatch or empty)")
    # new entries were written to Common; those of optional content (MayRequire defs) belong in their conditional folder
    subprocess.run([sys.executable, str(I / "split_optional.py")], check=True)


if __name__ == "__main__":
    if sys.argv[1] == "plan": plan(sys.argv[2])
    elif sys.argv[1] == "apply": apply()
