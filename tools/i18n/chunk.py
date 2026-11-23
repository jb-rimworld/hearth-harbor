#!/usr/bin/env python3
"""Splits tools/i18n/src/*.json into chunks of ≤ N strings (tools/i18n/chunks/<Module>__<n>.json, flat {key: text})
and merges translated chunks back (merge <lang-code>): tools/i18n/out/<code>/<Module>__<n>.json → tools/i18n/merged/<code>/<Module>.json
in the keyed/defs shape expected by write.py. Keys are prefixed "k:" (keyed) or "d:" (def injection)."""
import json, sys
from pathlib import Path
R = Path(__file__).resolve().parent
N = 180
if sys.argv[1:2] == ["split"]:
    out = R / "chunks"; out.mkdir(exist_ok=True)
    for f in out.glob("*.json"): f.unlink()
    names = []
    for src in sorted((R / "src").glob("*.json")):
        d = json.loads(src.read_text(encoding="utf-8"))
        flat = {"k:" + k: v for k, v in d["keyed"].items()} | {"d:" + k: v for k, v in d["defs"].items()}
        items = list(flat.items())
        for i in range(0, len(items), N):
            name = f"{src.stem}__{i // N}"
            (out / f"{name}.json").write_text(json.dumps(dict(items[i:i + N]), ensure_ascii=False, indent=1), encoding="utf-8")
            names.append(name)
    print(json.dumps(names))
elif sys.argv[1:2] == ["merge"]:
    code = sys.argv[2]
    tr = {}
    for f in sorted((R / "out" / code).glob("*.json")):
        mod = f.stem.split("__")[0]
        try:
            d = json.loads(f.read_text(encoding="utf-8"))
        except Exception as e:
            print("bad json", f, e); continue
        m = tr.setdefault(mod, {"keyed": {}, "defs": {}})
        for k, v in d.items():
            if k.startswith("k:"): m["keyed"][k[2:]] = v
            elif k.startswith("d:"): m["defs"][k[2:]] = v
    dst = R / "merged" / code; dst.mkdir(parents=True, exist_ok=True)
    for mod, m in tr.items():
        (dst / f"{mod}.json").write_text(json.dumps(m, ensure_ascii=False, indent=1), encoding="utf-8")
    print(code, sum(len(m["keyed"]) + len(m["defs"]) for m in tr.values()), "strings merged")
