# Hearth & Harbor art pipeline

SVG-in-Python generator. The Python functions are the source in git; PNGs in `Common/Textures` are build output
(committed because the mod needs them, never edited by hand). Rendering uses **resvg** (`resvg-py` wheel, no cairo or Mac apps needed) plus
Pillow for a tiny deterministic paper grain.

```
uv run --with resvg-py --with pillow python3 tools/art/build.py              # render everything + contact sheets
uv run --with resvg-py --with pillow python3 tools/art/build.py Kitchen      # only textures whose path contains "Kitchen"
uv run --with resvg-py --with pillow python3 tools/art/build.py --sheet-only # only rebuild tools/art/out/contact_sheet*.png
uv run --with resvg-py --with pillow python3 tools/art/build.py Slot --svg   # also write the SVG source to tools/art/out/svg/
```
`tools/art/out/contact_sheet.png` shows every texture on a floor at 2 px/px; `contact_sheet_views.png` shows all north/east/south views of the
Multi textures. This is the quality gate: look at it after every change.

## Integration round additions
- Modules: `inn, venues, kitchen, casino, bank, ui` (round 1) plus `city, underworld, wellness, icons` (all in `hhart/all.py`).
- `tools/art/needs.py` is the audit: every texPath/iconPath/uiIconPath in Common/Defs and every `ContentFinder<Texture2D>.Get("literal")` in Source must be a registered generator entry with the right pixel size (drawSize/size of the def). Run it after adding a def; exit code 1 lists what is missing.
- `build.py` now also writes one sheet per module (`out/module_<name>.png`, all views) and `out/readability.png` (all UI icons at 32 and 64 px next to a 64 px copy on the dark UI background).
- `previews.py` renders the Workshop previews `About/Preview.png` (ribbon "Main Mod", no other text) and `About/Silverline_Preview.png` from the same sprite functions at 120 px per cell.
- Defs that use a different size than the first guess: BlackjackTable 3x2, Oven 2x1, NightSafe is a single texture. `Things/HH/Underworld/BackRoomDoor` is only the LEFT half of a door leaf: `Building_Door` draws the texture twice (second mirrored) and slides both apart.
  Because the def also uses it as `uiIconPath`, the build menu icon shows one half leaf (integrator: give the def its own icon texture if that bothers anyone).

## House style (KONZEPT 14.1)
- Design space is **128 units per cell**; the renderer scales to pixels. Furniture and items render at **64 px per cell** (`ppc=64`), UI icons at 128 (`ppc=128`).
- Warm near-black outline `#1c1411` (never `#000`): 3.5 units on the silhouette, 2-3 inside, 1-2 on small parts. Round joins and caps.
- Flat fills, **no gradients on game art** (only the wide UI header uses a few). Light from the top left: every shape gets one light line (+18 % warm) on its
  top/left inner edge and one shadow line (-25 %) on its bottom/right inner edge. That consistent light edge is our only signature: no logo, no frame, **no gold edge** (N04).
- Palette lives in `hhart/style.py` (`P[...]`): steel `#b9b2a3`, dark steel `#2b2f38`, paper `#e9e4d4`, wood `#7a5638` + one accent per family
  (Inn amber `#d9803f`, Silverline emerald `#3fa66b`, casino felt green, kitchen copper/flame). Tiers use the accents Mk I `#5e8fc2` / Mk II `#4faf7a` / Mk III `#e0b44a`.
- Perspective: top-down 3/4. Low furniture = top surface plus a front face (apron) at the **screen bottom**. Upright machines = front elevation with a thin lighter top cap.
- Grain: +-2 random brightness on opaque pixels, seeded by the file name, so renders are reproducible. UI icons have `grain=0`.

## Views and sizes
- `Graphic_Multi` gets `_south`, `_east`, `_north` (west is the east image mirrored by the game). **RimWorld draws east/west with the size rotated**, so a 2x1 object has a 1x2 east texture (64x128). The generator handles that: a texture function always receives the canvas for the requested view.
- Plan-type furniture (`slab(...)` + `Frame`) is drawn once in a local south frame (x right, y toward the viewer); `Frame` rotates the layout for east/north while keeping the light and the apron at the screen bottom. Draw with `c.rect(*f.r(x, y, w, h), ...)` and `f.pt(x, y)`.
- Upright machines use `cabinet(...)` (front/back/side face + a front strip on the side view).
- **Footprints are set in the `@tex(path, kind, cells=(w, h))` line** (south orientation). Layouts adapt proportionally; to change a size edit that one tuple (or `SIZES` in `registry.py`) and re-run. Current guesses, to be matched by the defs:
  FrontDesk 2x1, ShopCounter 2x1, VendingMachine 1x1, VenueSign/Wall 1x1, PrepCounter 2x1, StockPot 1x1, Grill 2x1, Oven 1x1, WokStation 1x1, DeepFryer 1x1, Smoker 1x1,
  Steamer 1x1, DoughBench 2x1, ColdStation 2x1, SlotMachine 1x1, RouletteTable 3x2, BlackjackTable 2x2, PokerTable 3x2, CashierCage 2x2, ATM 1x1, BankCounter 3x1, NightSafe 1x1.
  Items (plates, chips, wallet, bag) are 64x64; UI icons 128x128; CardHeader 1024x256 (8x2 at 128).

## Stuff masks (`_southm.png`, ...)
`masked=True` textures also write `<name>_<view>m.png` (R = main stuff colour area, G = second colour, black = untinted, transparent outside). Their stuff areas are drawn in a
neutral pale tone so a `CutoutComplex` graphic tints them nicely. Currently masked: FrontDesk, ShopCounter, PrepCounter, DoughBench, BankCounter. In a shape call pass `m="a"` (red), `m="b"` (green), `m=None` (black, default) or `m=False` (leave out of the mask, e.g. highlights).
If a def does not use stuff colour, the masks are simply ignored and the counter looks pale stone.

## Adding a texture
1. Pick the module file in `tools/art/hhart/` (`inn.py`, `venues.py`, `kitchen.py`, `casino.py`, `bank.py`, `ui.py`) or add a new one and import it in `build.py`.
2. Write a function and register it:
```python
@tex("Things/HH/Venues/Barrel", "multi", (1, 1), doc="Ale barrel, 1x1")
def barrel(view, cells, masked):
    c = canvas_for_(cells, view, masked)           # right canvas size for this view
    f = slab(c, view, P["wood"], apron=20)         # 3/4 block; f = Frame of the top surface
    c.circle(*f.pt(f.w / 2, f.h / 2), 40, P["wood_l"])   # shaded shape: outline + light + shadow line
    return c
```
3. Run `build.py <name>`, look at the contact sheet, fix, commit. Primitives: `rect`, `circle`, `ellipse`, `poly`, `path`, `line`, `pline`, `dot`, `glow`, `steam`; parts in `parts.py` (chips, coins, cards, dice, keys, flames, shields, plates).
4. Texture paths must be `Things/HH/<Module>/...` or `UI/HH/<Module>/...`; `tools/placeholder_textures.py` never overwrites existing PNGs.

## Not done yet / plan
- **Epoch and Ideology styles (KONZEPT 14.7) are not generated yet.** Plan: give every `@tex` an optional `style` argument (`palette`, `ornament set`) and render `<Def>/<Style>/<Def>_south.png`; epochs differ by material palette (neolithic bone/hide, medieval timber/iron, industrial brass/riveted steel, spacer white/blue light strips, ultra black/violet), Ideology categories add ornament modules (notch, rivet, panel, light strip, bone, spikes).
- Colour-blind patterns for guest bed covers (grey, blue, green, gold plus pattern) come with the bed textures once the Inn agent names the paths.
- A second-pass of small details for all views (east/north) of the upright machines.
