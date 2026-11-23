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
| `static bool DrawsBedSelector` (= false) | Set true if H&H itself ever draws the selector; RimWard then only hides its toggles and drives the bed via `RimWard.BedApi`. |
| `bool SelectorAvailable` | The "Guest" entry exists: Inn module on, colony-owned humanlike bed. |
| `string SelectorBlockedReason` | Why the bed cannot be a guest bed (medical, prisoner or slave bed); null when it can or already is. Shown greyed out in RimWard's menu. |
| `bool IsGuestBed`, `SetGuestBed(bool, bool)` | Existing API RimWard calls to switch the bed. Unchanged. |
| `static bool OtherDrawsSelector(Building_Bed)` | Reflection call to `RimWard.BedSelector.Draws(bed)`. When true, `CompGetGizmosExtra` yields no Guest bed toggle. |

Resolved once, cached; a log line "RimWard.BedSelector found" appears when the integration is active. Any reflection error means "RimWard does not draw", so the
H&H toggle is shown (never both missing).

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
