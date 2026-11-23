#!/usr/bin/env python3
"""Builds the release folders for both Workshop entries from one source state (KONZEPT 3.5, 15.8).

    python3 tools/package.py [--no-build]

Output (next to the repo, never inside it):
  ../_Release/HearthHarbor/   main mod: About, LoadFolders, Common, 1.5/1.6/1.7 (release DLLs, no test harness), LICENSE, README
  ../_Release/Silverline/     standalone bank: Hearth.Core.dll + Silverline.dll, bank/insurance defs, languages, textures;
                              its content folders load only when Hearth & Harbor is NOT active (IfModNotActive), so with both
                              active every type exists exactly once.
Checks: identical SHA-256 of the shared DLLs and shared files in both packages; no Source/, no test harness in the DLLs.
"""
import hashlib, re, shutil, subprocess, sys
from pathlib import Path

R = Path(__file__).resolve().parent.parent
import os
OUT = Path(os.environ.get("HH_RELEASE_OUT", R.parent / "_Release"))
MAIN, BANK = OUT / "HearthHarbor", OUT / "Silverline"
VERSIONS = ["1.5", "1.6", "1.7"]
BANK_DEF_DIRS = ["Core", "Bank", "Insurance", "Finance"]
BANK_LANG = ["HH_Bank.xml", "HH_Insurance.xml", "HH_Finance.xml", "HH_Research.xml", "HH_UI.xml"]
# DefInjected: Silverline.* folders whole; generic folders filtered to bank defNames (written fresh, so outside the byte-identity check)
BANK_DEFINJ_PREFIX = ("SL_", "HH_Bank", "HH_Banking")
BANK_TEX = ["UI/HH/Bank", "UI/HH/Insurance", "UI/HH/Finance", "Things/HH/Bank", "UI/HH/Core"]


def sh(cmd):
    print("$", cmd)
    subprocess.run(cmd, shell=True, check=True, cwd=R)


def sha(p: Path) -> str:
    return hashlib.sha256(p.read_bytes()).hexdigest()


def main():
    if "--no-build" not in sys.argv:
        sh("Source/build.sh all")
    for v in VERSIONS:
        for dll in ("Hearth.Core.dll", "Silverline.dll", "Hearth.dll"):
            data = (R / v / "Assemblies" / dll).read_bytes()
            if b"HearthTestRunner" in data:
                sys.exit(f"{v}/{dll} contains the test harness – build without --tests")
    if OUT.exists():
        shutil.rmtree(MAIN, ignore_errors=True); shutil.rmtree(BANK, ignore_errors=True)
    OUT.mkdir(exist_ok=True)

    # ---- main mod
    for d in ["About", "Common"] + VERSIONS:
        if (R / d).exists():
            shutil.copytree(R / d, MAIN / d, ignore=shutil.ignore_patterns("*.pdb", ".DS_Store"))
    for f in ["LoadFolders.xml", "LICENSE", "README.md", "AGENTS.md"]:
        if (R / f).exists():
            shutil.copy2(R / f, MAIN / f)

    # ---- standalone bank
    (BANK / "About").mkdir(parents=True)
    about = (R / "About" / "About.xml").read_text(encoding="utf-8")
    about = re.sub(r"<name>.*?</name>", "<name>Silverline – Banking, Loans &amp; Insurance</name>", about, flags=re.S)
    about = about.replace("juliusberger00.hearthharbor", "juliusberger00.silverline")
    about = re.sub(r"<url>.*?</url>", "<url>https://github.com/jb-rimworld/hearth-harbor</url>", about)
    about = re.sub(r"<description>.*?</description>", "<description>Silverline is the bank of Hearth &amp; Harbor as a standalone mod: accounts at six very different banks, deposits and withdrawals, ATMs and branches, interest, loans with a polite-then-not-so-polite collections ladder, and insurance by Clawback Mutual.\n\nAlready using Hearth &amp; Harbor? Then you do not need this mod – the bank is built in. If both are active, this one steps aside automatically.</description>", about, flags=re.S)
    about = re.sub(r"<loadBefore>.*?</loadBefore>", "", about, flags=re.S)
    about = re.sub(r"<incompatibleWith>.*?</incompatibleWith>", "", about, flags=re.S)
    (BANK / "About" / "About.xml").write_text(about, encoding="utf-8")
    for extra in ["Preview.png"]:
        src = R / "About" / "Silverline_Preview.png"
        if src.exists():
            shutil.copy2(src, BANK / "About" / extra)
    lf = ['<?xml version="1.0" encoding="utf-8"?>', "<loadFolders>"]
    for v in VERSIONS:
        lf.append(f'  <v{v}><li>/</li><li IfModNotActive="juliusberger00.hearthharbor">Content/Common</li><li IfModNotActive="juliusberger00.hearthharbor">Content/{v}</li></v{v}>')
    lf.append("</loadFolders>")
    (BANK / "LoadFolders.xml").write_text("\n".join(lf) + "\n", encoding="utf-8")
    C = BANK / "Content" / "Common"
    for d in BANK_DEF_DIRS:
        if (R / "Common" / "Defs" / d).exists():
            shutil.copytree(R / "Common" / "Defs" / d, C / "Defs" / d)
    for lang in (R / "Common" / "Languages").iterdir():
        for f in BANK_LANG:
            src = lang / "Keyed" / f
            if src.exists():
                (C / "Languages" / lang.name / "Keyed").mkdir(parents=True, exist_ok=True)
                shutil.copy2(src, C / "Languages" / lang.name / "Keyed" / f)
        di = lang / "DefInjected"
        if di.is_dir():
            for sub in di.iterdir():
                if not sub.is_dir():
                    continue
                dst = C / "Languages" / lang.name / "DefInjected" / sub.name
                if sub.name.startswith("Silverline."):
                    shutil.copytree(sub, dst)
                    continue
                for xf in sub.glob("*.xml"):
                    txt = xf.read_text(encoding="utf-8")
                    keep = [m.group(0) for m in re.finditer(r"^\s*<([A-Za-z0-9_]+)\.[^>]*>.*?</\1\.[^>]*>\s*$", txt, flags=re.M | re.S) if m.group(1).startswith(BANK_DEFINJ_PREFIX)]
                    if keep:
                        dst.mkdir(parents=True, exist_ok=True)
                        (dst / xf.name).write_text('<?xml version="1.0" encoding="utf-8"?>\n<LanguageData>\n' + "\n".join(k.rstrip() for k in keep) + "\n</LanguageData>\n", encoding="utf-8")
    for t in BANK_TEX:
        if (R / "Common" / "Textures" / t).exists():
            shutil.copytree(R / "Common" / "Textures" / t, C / "Textures" / t)
    for v in VERSIONS:
        (BANK / "Content" / v / "Assemblies").mkdir(parents=True)
        for dll in ("Hearth.Core.dll", "Silverline.dll"):
            shutil.copy2(R / v / "Assemblies" / dll, BANK / "Content" / v / "Assemblies" / dll)
    shutil.copy2(R / "LICENSE", BANK / "LICENSE")

    # ---- byte identity
    bad = []
    for v in VERSIONS:
        for dll in ("Hearth.Core.dll", "Silverline.dll"):
            if sha(MAIN / v / "Assemblies" / dll) != sha(BANK / "Content" / v / "Assemblies" / dll):
                bad.append(f"{v}/{dll}")
    for f in (BANK / "Content" / "Common").rglob("*"):
        if f.is_file() and not ("DefInjected" in f.parts and not any(p.startswith("Silverline.") for p in f.parts)):
            rel = f.relative_to(BANK / "Content")
            if not (MAIN / rel).exists() or sha(MAIN / rel) != sha(f):
                bad.append(str(rel))
    if bad:
        sys.exit("Byte identity broken: " + ", ".join(bad[:20]))
    n_main = sum(1 for _ in MAIN.rglob("*") if _.is_file())
    n_bank = sum(1 for _ in BANK.rglob("*") if _.is_file())
    print(f"OK  {MAIN} ({n_main} files)  {BANK} ({n_bank} files)  – shared files byte-identical")


if __name__ == "__main__":
    main()
