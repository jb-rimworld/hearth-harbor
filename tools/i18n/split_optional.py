#!/usr/bin/env python3
"""Keeps translations of optional content out of the always-loaded language folders (1.1.1).

    python3 tools/i18n/split_optional.py [--check]

Why: a def with MayRequire / MayRequireAnyOf only exists when that DLC or mod is active. Its DefInjected entries were in
Common/Languages, which always loads, so in every language except English the game counted "Found no <Def> named ..." for each of
them and logged the yellow line "Translation data for language X has N errors" (50 with all DLCs and none of the seven cooking
mods; far more without DLCs).

What it does:
  1. Reads every def with MayRequire/MayRequireAnyOf on its root element (Common/Defs and the version folders).
  2. Moves their entries from Common/Languages/<lang>/DefInjected/<Type>/<file>.xml to
     Compat/<ids>/Languages/<lang>/DefInjected/<Type>/<file>.xml  (<ids> = the package ids, joined with "+").
  3. Writes LoadFolders.xml: Common, the version folder, then one <li IfModActive="ids">Compat/<ids></li> per folder
     (IfModActive = any of the ids, which is what MayRequire with one id and MayRequireAnyOf mean; a MayRequire with several ids
     would need all of them and is refused here until one exists).
Run it after every change of the language files; tools/i18n/delta.py apply does so itself. --check changes nothing and exits 1 when
something would move or LoadFolders.xml would change.
"""
import re, sys
import xml.etree.ElementTree as ET
from pathlib import Path

R = Path(__file__).resolve().parents[2]
VERSIONS = ["1.5", "1.6", "1.7"]
ENTRY = re.compile(r"^[ \t]*<([^\s<>/!?]+)>.*?</\1>[ \t]*\n", re.M | re.S)
HEAD = '<?xml version="1.0" encoding="utf-8"?>\n<LanguageData>\n'
FOOT = "</LanguageData>\n"


def gated_defs():
    """{(def type, defName): ids}  ids = "a" or "a,b,c" (any of)."""
    out = {}
    for base in [R / "Common" / "Defs"] + [R / v / "Defs" for v in VERSIONS]:
        if not base.exists():
            continue
        for f in sorted(base.rglob("*.xml")):
            for d in ET.parse(f).getroot():
                dn = d.findtext("defName")
                if not dn:
                    continue
                one, anyof = d.get("MayRequire"), d.get("MayRequireAnyOf")
                if one and "," in one:
                    sys.exit(f"{f}: {dn} has MayRequire with several ids (all of them); split_optional.py only knows 'any of'")
                ids = one or anyof
                if ids:
                    ids = ",".join(s.strip() for s in ids.split(",") if s.strip())
                    if out.get((d.tag, dn), ids) != ids:
                        sys.exit(f"{dn}: different MayRequire values in different files")
                    out[(d.tag, dn)] = ids
    return out


def slug(ids):
    return ids.replace(",", "+")


def entries(path):
    """Ordered [(tag, raw text of the entry)] of a language file; exits when the file holds anything else (comments, loose text)."""
    txt = path.read_text(encoding="utf-8")
    a, b = txt.find("<LanguageData>"), txt.rfind("</LanguageData>")
    if a < 0 or b < 0:
        sys.exit(f"{path}: no <LanguageData>")
    body = txt[a + len("<LanguageData>"):b]
    if not body.startswith("\n"):
        body = "\n" + body
    body = body[1:] if body.startswith("\n") else body
    if body and not body.endswith("\n"):
        body += "\n"
    found = [(m.group(1), m.group(0)) for m in ENTRY.finditer(body)]
    if ENTRY.sub("", body).strip():
        sys.exit(f"{path}: content between entries that this tool would lose: {ENTRY.sub('', body).strip()[:80]!r}")
    return found


def write(path, pairs):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(HEAD + "".join(raw for _, raw in pairs) + FOOT, encoding="utf-8")


def load_folders(slugs_ids):
    lines = ['<?xml version="1.0" encoding="utf-8"?>',
             "<!-- Written by tools/i18n/split_optional.py. Compat/<ids> holds only translations of content that exists with that DLC or mod",
             "     (the defs themselves stay in Common with MayRequire); loading them always made every language but English report errors. -->",
             "<loadFolders>"]
    for v in VERSIONS:
        lines.append(f"  <v{v}>")
        lines.append("    <li>Common</li>")
        lines.append(f"    <li>{v}</li>")
        for s, ids in slugs_ids:
            lines.append(f'    <li IfModActive="{ids}">Compat/{s}</li>')
        lines.append(f"  </v{v}>")
    lines.append("</loadFolders>")
    return "\n".join(lines) + "\n"


def main():
    check = "--check" in sys.argv
    gated = gated_defs()
    moved = 0
    for lang in sorted((R / "Common" / "Languages").iterdir()):
        di = lang / "DefInjected"
        if lang.name == "English" or not di.is_dir():
            continue
        for f in sorted(di.rglob("*.xml")):
            dtype = f.parent.relative_to(di).as_posix()
            keep, out = [], {}
            for tag, raw in entries(f):
                ids = gated.get((dtype, tag.split(".", 1)[0]))
                if ids:
                    out.setdefault(ids, []).append((tag, raw))
                else:
                    keep.append((tag, raw))
            if not out:
                continue
            moved += sum(len(v) for v in out.values())
            if check:
                continue
            for ids, pairs in out.items():
                dst = R / "Compat" / slug(ids) / "Languages" / lang.name / "DefInjected" / dtype / f.name
                have = entries(dst) if dst.exists() else []
                new = dict(pairs)
                merged = [(t, new.pop(t, r)) for t, r in have] + [(t, r) for t, r in pairs if t in new]
                write(dst, merged)
            if keep:
                write(f, keep)
            else:
                f.unlink()
                if not any(f.parent.iterdir()):
                    f.parent.rmdir()
    # every Compat folder that exists gets a load folder line (sorted: DLCs first, then mods)
    ids_by_slug = {slug(i): i for i in set(gated.values())}
    present = sorted(p.name for p in (R / "Compat").iterdir() if p.is_dir()) if (R / "Compat").exists() else []
    unknown = [s for s in present if s not in ids_by_slug]
    if unknown:
        sys.exit("Compat folders without a def that asks for them: " + ", ".join(unknown))
    order = sorted(present, key=lambda s: (not s.startswith("Ludeon."), s.lower()))
    lf = load_folders([(s, ids_by_slug[s]) for s in order])
    cur = (R / "LoadFolders.xml").read_text(encoding="utf-8")
    if check:
        if moved or (present and cur != lf):
            print(f"split_optional: {moved} entries would move; LoadFolders.xml {'differs' if cur != lf else 'ok'}")
            sys.exit(1)
        print("split_optional: nothing to do")
        return
    if cur != lf:
        (R / "LoadFolders.xml").write_text(lf, encoding="utf-8")
    print(f"split_optional: moved {moved} entries; {len(order)} Compat folders; LoadFolders.xml {'written' if cur != lf else 'unchanged'}")


if __name__ == "__main__":
    main()
