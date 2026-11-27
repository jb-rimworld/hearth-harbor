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
     Compat/<slug>/Languages/<lang>/DefInjected/<Type>/<file>__<slug>.xml  (<slug> = a short name of the package ids, see slug()).
     The file name must differ from the one in Common and from those in the other Compat folders: within one mod the game loads a
     language file only once per path below the language folder (LoadedLanguage.TryRegisterFileIfNew), the later load folder
     wins, and the others are skipped without any message. 1.1.1 kept the name, so with a DLC active the Common file of the same
     name was not read: every venue type, policy, bank, investment and base dish went back to English in all ten languages.
  3. Writes LoadFolders.xml: Common, the version folder, then one <li IfModActive="ids">Compat/<slug></li> per folder
     (IfModActive = any of the ids, which is what MayRequire with one id and MayRequireAnyOf mean; a MayRequire with several ids
     would need all of them and is refused here until one exists).
Path length (1.2.1): 1.1.1/1.1.2 named the folder and the file suffix after the package ids joined with "+"; the fishing set made
relative paths of up to 326 characters, and on Windows (MAX_PATH 260 with the Steam folder in front) the Workshop download of 1.2.0
failed ("File commit failed: staging file is missing") and left a mod folder without About.xml. Slugs are now short, and no path
written here may be longer than MAX_REL characters relative to the mod folder (tools/package.py checks every packaged file the same
way). Folders and files with the old long names are renamed.
Run it after every change of the language files; tools/i18n/delta.py apply does so itself. --check changes nothing and exits 1 when
something would move or LoadFolders.xml would change.
"""
import hashlib, re, sys
import xml.etree.ElementTree as ET
from pathlib import Path

R = Path(__file__).resolve().parents[2]
VERSIONS = ["1.5", "1.6", "1.7"]
ENTRY = re.compile(r"^[ \t]*<([^\s<>/!?]+)>.*?</\1>[ \t]*\n", re.M | re.S)
HEAD = '<?xml version="1.0" encoding="utf-8"?>\n<LanguageData>\n'
FOOT = "</LanguageData>\n"
# Longest relative path (characters; Windows counts UTF-16 units) allowed below the mod folder. Steam puts about 80 characters in
# front ("C:\\Program Files (x86)\\Steam\\steamapps\\workshop\\downloads\\294100\\<id>\\"), Windows stops at 259.
MAX_REL = 140
# Readable names for id sets with more than one id (any of them). Single ids use their last part (Ludeon.RimWorld.Anomaly -> Anomaly).
SET_NAMES = {
    "Ludeon.RimWorld.Odyssey,rikiki.fishindustry,sunsetmoderteam.rawfish,OW.RU.Fishing": "Fishing",
}


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
    """Short, stable folder name for an id set ("a" or "a,b,c"): a name from SET_NAMES, else the last part of a single id
    (letters, digits, - and _ only), else "Any" + 8 hex digits of its SHA-1. Never longer than 16 characters."""
    if ids in SET_NAMES:
        return SET_NAMES[ids]
    h = hashlib.sha1(ids.encode("utf-8")).hexdigest()[:8]
    if "," in ids:
        return "Any" + h
    s = re.sub(r"[^A-Za-z0-9_-]", "", ids.rsplit(".", 1)[-1])
    return s if 0 < len(s) <= 16 else (s[:7] + h)


def old_slug(ids):
    """Folder name of 1.1.1 to 1.2.0 (package ids joined with "+"); only used to rename those folders."""
    return ids.replace(",", "+")


def slugs_for(id_sets):
    """{ids: slug}; exits when two id sets get the same folder name (case-insensitive, Windows and macOS do not tell them apart)."""
    out, seen = {}, {}
    for ids in sorted(id_sets):
        s = slug(ids)
        if s.lower() in seen:
            sys.exit(f"split_optional: '{ids}' and '{seen[s.lower()]}' both get the Compat folder name {s}; add one to SET_NAMES")
        seen[s.lower()], out[ids] = ids, s
    return out


def compat_name(name, s):
    """File name inside Compat/<slug>: <stem>__<slug>.xml, unique across the load folders (see the docstring, point 2).
    An older suffix (everything from the first "__") is replaced."""
    return name[:-4].split("__", 1)[0] + "__" + s + ".xml"


def rel_ok(path):
    """Exits when a path below the mod folder is longer than MAX_REL (see the docstring)."""
    rel = path.relative_to(R).as_posix()
    if len(rel.encode("utf-16-le")) // 2 > MAX_REL:
        sys.exit(f"split_optional: path longer than {MAX_REL} characters ({len(rel)}), Windows cannot install it from the Workshop: {rel}")


def collisions():
    """Language files of different load folders with the same path below the language folder: the game reads only one of them."""
    seen, out = {}, []
    bases = [R / "Common"] + (sorted(p for p in (R / "Compat").iterdir() if p.is_dir()) if (R / "Compat").exists() else [])
    for base in bases:
        for lang in sorted((base / "Languages").iterdir()) if (base / "Languages").is_dir() else []:
            for f in sorted(lang.rglob("*")):
                if f.is_file():
                    key = (lang.name, f.relative_to(lang).as_posix().lower())
                    if key in seen:
                        out.append(f"{f.relative_to(R)}  ==  {seen[key].relative_to(R)}")
                    seen.setdefault(key, f)
    return out


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
    rel_ok(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(HEAD + "".join(raw for _, raw in pairs) + FOOT, encoding="utf-8")


def load_folders(slugs_ids):
    lines = ['<?xml version="1.0" encoding="utf-8"?>',
             "<!-- Written by tools/i18n/split_optional.py. Compat/<slug> holds only translations of content that exists with that DLC or mod",
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
    slug_of = slugs_for(set(gated.values()))
    moved = 0
    # 1.2.1: folders of 1.1.1 to 1.2.0 carry the long name (all package ids joined with "+"): give them the short one (merged below)
    relocated = 0
    for ids, s in sorted(slug_of.items()):
        old = R / "Compat" / old_slug(ids)
        if old.name == s or not old.is_dir():
            continue
        relocated += 1
        if check:
            continue
        for f in sorted(p for p in old.rglob("*") if p.is_file()):
            rel = f.relative_to(old)
            dst = R / "Compat" / s / rel.parent / (compat_name(f.name, s) if "DefInjected" in rel.parts and f.suffix == ".xml" else f.name)
            if dst.exists():
                have, add = entries(dst), dict(entries(f))
                write(dst, [(t, add.pop(t, r)) for t, r in have] + [(t, r) for t, r in entries(f) if t in add])
                f.unlink()
            else:
                rel_ok(dst)
                dst.parent.mkdir(parents=True, exist_ok=True)
                f.rename(dst)
        for d in sorted((p for p in old.rglob("*") if p.is_dir()), key=lambda p: -len(p.parts)) + [old]:
            if not any(d.iterdir()):
                d.rmdir()
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
                dst = R / "Compat" / slug_of[ids] / "Languages" / lang.name / "DefInjected" / dtype / compat_name(f.name, slug_of[ids])
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
    ids_by_slug = {s: i for i, s in slug_of.items()}
    # files written by 1.1.1 still carry the name of their Common sibling, those of 1.1.2 to 1.2.0 the long suffix: rename them
    # (merging into a file that already has the new name)
    renamed = 0
    for cdir in sorted(p for p in (R / "Compat").iterdir() if p.is_dir()) if (R / "Compat").exists() else []:
        if cdir.name not in ids_by_slug:
            continue
        for f in sorted(cdir.rglob("DefInjected/**/*.xml")):
            new = f.with_name(compat_name(f.name, cdir.name))
            if new == f:
                continue
            renamed += 1
            if check:
                continue
            rel_ok(new)
            if new.exists():
                have, add = entries(new), dict(entries(f))
                write(new, [(t, add.pop(t, r)) for t, r in have] + [(t, r) for t, r in entries(f) if t in add])
                f.unlink()
            else:
                f.rename(new)
    present = sorted(p.name for p in (R / "Compat").iterdir() if p.is_dir()) if (R / "Compat").exists() else []
    unknown = [s for s in present if s not in ids_by_slug and not (check and s in {old_slug(i) for i in slug_of})]
    if unknown:
        sys.exit("Compat folders without a def that asks for them: " + ", ".join(unknown))
    ids_of = lambda s: ids_by_slug.get(s) or next(i for i in slug_of if old_slug(i) == s)
    order = sorted(present, key=lambda s: (not ids_of(s).startswith("Ludeon."), ids_of(s).lower()))
    lf = load_folders([(s, ids_of(s)) for s in order])
    too_long = [f.relative_to(R).as_posix() for base in (R / "Common" / "Languages", R / "Compat") if base.is_dir()
                for f in sorted(base.rglob("*")) if f.is_file() and len(f.relative_to(R).as_posix().encode("utf-16-le")) // 2 > MAX_REL]
    cur = (R / "LoadFolders.xml").read_text(encoding="utf-8")
    if check:
        clash = collisions()
        if moved or renamed or relocated or clash or too_long or (present and cur != lf):
            print(f"split_optional: {moved} entries would move; {relocated} folders and {renamed} files would be renamed; {len(clash)} files "
                  f"share a path with another load folder; {len(too_long)} paths longer than {MAX_REL} characters; "
                  f"LoadFolders.xml {'differs' if cur != lf else 'ok'}")
            sys.exit(1)
        print("split_optional: nothing to do")
        return
    if cur != lf:
        (R / "LoadFolders.xml").write_text(lf, encoding="utf-8")
    if too_long:
        print("\n".join(too_long[:20]))
        sys.exit(f"split_optional: {len(too_long)} language files have a path longer than {MAX_REL} characters (Windows, see the docstring)")
    print(f"split_optional: moved {moved} entries; renamed {relocated} folders and {renamed} files; {len(order)} Compat folders; LoadFolders.xml {'written' if cur != lf else 'unchanged'}")
    clash = collisions()
    if clash:
        print("\n".join(clash[:20]))
        sys.exit(f"split_optional: {len(clash)} language files share their path with a file of another load folder; the game reads only one of them")


if __name__ == "__main__":
    main()
