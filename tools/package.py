#!/usr/bin/env python3
"""Builds the release folders for both Workshop entries from one source state (KONZEPT 3.5, 15.8).

    python3 tools/package.py [--no-build]

Output (next to the repo, never inside it):
  ../_Release/HearthHarbor/   main mod: About, LoadFolders, Common, 1.5/1.6/1.7 (release DLLs, no test harness), LICENSE, README
  ../_Release/Silverline/     standalone bank: Hearth.Core.dll + Silverline.dll, bank/insurance defs, languages, textures;
                              its content folders load only when Hearth & Harbor is NOT active (IfModNotActive; an always-loaded stub texture keeps it non-empty), so with both
                              active every type exists exactly once.
Checks: identical SHA-256 of the shared DLLs and shared files in both packages; no Source/, no test harness in the DLLs.
"""
import hashlib, re, shutil, struct, subprocess, sys, zlib
from pathlib import Path

R = Path(__file__).resolve().parent.parent
import os
OUT = Path(os.environ.get("HH_RELEASE_OUT", R.parent / "_Release"))
MAIN, BANK = OUT / "HearthHarbor", OUT / "Silverline"
VERSIONS = ["1.5", "1.6", "1.7"]
BANK_DEF_DIRS = ["Core", "Bank", "Insurance", "Finance"]
BANK_LANG = ["HH_Bank.xml", "HH_Insurance.xml", "HH_Finance.xml", "HH_Research.xml", "HH_UI.xml", "HH_Scenario.xml", "HH_Settings.xml"]
# DefInjected: Silverline.* folders whole; generic folders filtered to bank defNames (written fresh, so outside the byte-identity check)
BANK_DEFINJ_PREFIX = ("SL_", "HH_Bank", "HH_Banking")
BANK_SND = ["HH/Bank", "HH/Insurance"]
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
            shutil.copytree(R / d, MAIN / d, ignore=shutil.ignore_patterns("*.pdb", ".DS_Store", "Silverline_*"))
    for f in ["LoadFolders.xml", "LICENSE", "README.md", "AGENTS.md", "CREDITS.md"]:
        if (R / f).exists():
            shutil.copy2(R / f, MAIN / f)

    # ---- standalone bank
    (BANK / "About").mkdir(parents=True)
    about = (R / "About" / "About.xml").read_text(encoding="utf-8")
    def must_sub(pat, repl, txt, what):
        out, n = re.subn(pat, lambda m: repl, txt, flags=re.S)
        if n != 1:
            sys.exit(f"Silverline About.xml: '{what}' matched {n} times (expected 1) - About/About.xml changed shape")
        return out
    about = must_sub(r"<name>.*?</name>", "<name>Silverline – Banking, Loans &amp; Insurance</name>", about, "name")
    if about.count("juliusberger00.hearthharbor") != 1:
        sys.exit("Silverline About.xml: packageId marker not found exactly once")
    about = about.replace("juliusberger00.hearthharbor", "juliusberger00.silverline")
    about = must_sub(r"<url>.*?</url>", "<url>https://github.com/jb-rimworld/hearth-harbor</url>", about, "url")
    about = must_sub(r"<description>.*?</description>", "<description>Silverline is the bank of Hearth &amp; Harbor as a standalone mod: accounts at six very different banks, deposits and withdrawals, ATMs and branches, interest, loans with a polite-then-not-so-polite collections ladder, and insurance by Clawback Mutual.\n\nAlready using Hearth &amp; Harbor? Then you do not need this mod – the bank is built in. If both are active, this one steps aside automatically.</description>", about, "description")
    about = must_sub(r"<loadBefore>.*?</loadBefore>", "", about, "loadBefore")
    # safe next to Hearth & Harbor: load after it so the (inactive) content folders and the stub resolve in a fixed order
    if about.count("<loadAfter>") != 1:
        sys.exit("Silverline About.xml: <loadAfter> block not found exactly once")
    about = about.replace("<loadAfter>", "<loadAfter>\n    <li>juliusberger00.hearthharbor</li>", 1)
    about = re.sub(r"<incompatibleWith>.*?</incompatibleWith>", "", about, flags=re.S)   # optional: H&H no longer declares one (takeover dialog instead)
    for need in ("<packageId>juliusberger00.silverline</packageId>", "<li>juliusberger00.hearthharbor</li>", "steps aside automatically"):
        if need not in about:
            sys.exit(f"Silverline About.xml: missing {need}")
    if "<loadBefore>" in about or "<incompatibleWith>" in about or about.count("Orion.Hospitality") != 0:
        sys.exit("Silverline About.xml: loadBefore/incompatibleWith not removed")
    (BANK / "About" / "About.xml").write_text(about, encoding="utf-8")
    for extra in ["Preview.png", "ModIcon.png"]:   # ModIcon: mod list / mod settings icon (F34, 1.1.0)
        src = R / "About" / ("Silverline_" + extra)
        if src.exists():
            shutil.copy2(src, BANK / "About" / extra)
    if (R / "About" / "Silverline_PublishedFileId.txt").exists():
        shutil.copy2(R / "About" / "Silverline_PublishedFileId.txt", BANK / "About" / "PublishedFileId.txt")
    lf = ['<?xml version="1.0" encoding="utf-8"?>', "<loadFolders>"]
    for v in VERSIONS:
        lf.append(f'  <v{v}><li>/</li><li>Content/Always</li><li IfModNotActive="juliusberger00.hearthharbor">Content/Common</li><li IfModNotActive="juliusberger00.hearthharbor">Content/{v}</li></v{v}>')
    lf.append("</loadFolders>")
    (BANK / "LoadFolders.xml").write_text("\n".join(lf) + "\n", encoding="utf-8")
    # Always-loaded stub: with Hearth & Harbor active every other content folder is skipped, and RimWorld would log
    # "did not load any content" (red error). One tiny texture under a Silverline-only path counts as content.
    stub = BANK / "Content" / "Always" / "Textures" / "Silverline" / "Stub.png"
    stub.parent.mkdir(parents=True)
    def chunk(t, d):
        c = struct.pack(">I", len(d)) + t + d
        return c + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    stub.write_bytes(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", 1, 1, 8, 6, 0, 0, 0))
                     + chunk(b"IDAT", zlib.compress(b"\x00\x00\x00\x00\x00")) + chunk(b"IEND", b""))
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
    # bank and insurance sounds (1.1.0): the Bank/Insurance SoundDefs point to these clips
    for snd in BANK_SND:
        if (R / "Common" / "Sounds" / snd).exists():
            shutil.copytree(R / "Common" / "Sounds" / snd, C / "Sounds" / snd)
    for v in VERSIONS:
        (BANK / "Content" / v / "Assemblies").mkdir(parents=True)
        for dll in ("Hearth.Core.dll", "Silverline.dll"):
            shutil.copy2(R / v / "Assemblies" / dll, BANK / "Content" / v / "Assemblies" / dll)
    shutil.copy2(R / "LICENSE", BANK / "LICENSE"); shutil.copy2(R / "CREDITS.md", BANK / "CREDITS.md")

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
