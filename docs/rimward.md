# RimWard bridge (Hearth & Harbor side)

RimWard (`juliusberger.rimward`, GPL fork of the Hospital mods) and Hearth & Harbor know each other only by reflection. Neither has a
dependency on the other; each part is switched off silently when the other mod is missing. RimWard side of the protocol: its README,
section "Works with Hearth & Harbor". RimWorld 1.7 only for the bed selector and patient leisure.

## 1. One compact bed button (bed selector protocol v1)

RimWard replaces the separate toggles (Hospital Bed, Surgery Bed, our Guest bed) with a single "Use: Colonist / Guest / Patient / Surgery" button.
So that exactly one set of buttons shows, both sides check each other (`Source/Hearth/Guests/GuestBeds.cs`, `CompHH_GuestBed`):

| H&H member | Meaning |
|---|---|
| `static int BedSelectorVersion` (= 1) | The protocol version H&H speaks. RimWard only takes over when it finds >= 1. Older H&H builds keep their own button. |
| `static bool DrawsBedSelector` (= **true since 1.1.0**) | H&H itself draws the one bed button (`Command_HH_BedUse`); RimWard then only hides its toggles. H&H lists Colonist / Guest and, when `RimWard.BedSelector` / `BedApi` / `BedUse` are found by reflection, also Patient and Surgery, and drives the bed through `RimWard.BedApi.Apply`. |
| `bool SelectorAvailable` | The "Guest" entry exists: Inn module on, colony-owned humanlike bed. |
| `string SelectorBlockedReason` | Why the bed cannot be a guest bed (medical, prisoner or slave bed); null when it can or already is. Shown greyed out in RimWard's menu. |
| `bool IsGuestBed`, `SetGuestBed(bool, bool)` | Existing API RimWard calls to switch the bed. Unchanged. |
| `static bool OtherDrawsSelector(Building_Bed)` | Reflection call to `RimWard.BedSelector.Draws(bed)`. When true, `CompGetGizmosExtra` yields no Guest bed toggle. |

Resolved once, cached; a log line "RimWard.BedSelector found" appears when the integration is active. Any reflection error means "RimWard does not draw", so the
H&H toggle is shown (never both missing).

Since 1.1.0 (F10) the roles are listed and switched by H&H's own button (`Source/Hearth/Guests/BedUseGizmo.cs`, `BedRoles`): `BedSelector.Current / Entries / BlockedReason` and `BedApi.Apply` are called by reflection;
if one of them is missing the button falls back to Colonist / Guest. An older RimWard that still draws its own button (`BedSelector.Draws(bed)` true) makes H&H skip its role button and keep only the price menu
(`OtherDrawsSelector`). RimWard needs no change for this; it could drop its own `Command_BedUse` once H&H 1.1 is the minimum.

## 2. Patients use the spa

RimWard exposes `RimWard.PatientApi.IsWalkingPatient(Pawn)` (up and about: awake, can walk, not bleeding, nothing urgent, no operation waiting).
H&H (`Source/Hearth/Wellness`):

- `RehabBridge.IsRimWardWalkingPatient(pawn)` (reflection, false without RimWard) lets such a pawn pass `RehabBridge.MayUse`: a real RimWard patient is a guest of the
  colony and not a colonist, so the old "colonist in a medical bed" test would reject them.
- `JoyGiver_UseWellness.CanInteractWith` accepts them (they are never `GuestApi.IsGuest`, the inn does not bill them) and applies the same house rules as for rehab
  (no sauna after fresh surgery, spa switched off, `rehabJoy` setting, facility usable now).
- Patients are not guests of the Inn: no registers, no bills. RimWard stays the owner of the stay.

## 3. Tests

`MODDIR=$PWD tools/run_test.sh smoke,wellness.*,inn.* <label> juliusberger.hospitalbedsgen2 juliusberger.rimward` (the suites `wellness.rehab` and `inn.*` cover the bridge
with RimWard and Hospital Beds Gen 2 loaded).
