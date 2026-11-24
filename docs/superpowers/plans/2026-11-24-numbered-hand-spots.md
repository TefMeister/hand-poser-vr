# Numbered Hand Spots and SHOW Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (Tefa's standing rule: no subagents, work inline). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** every hand spot on a Village gun gets a fixed number (L / R per gun, H1-H4, A, M, F), shown in Hand Poser, Animation & Timing and Grip Zones; picking a number in Hand Poser loads that spot's pose to edit, and with SHOW the mod puts the hand at that spot without the real reload.

**Architecture:** Hand Poser (C#) owns the numbers (`spot_numbers.json`, append-only) and writes `spot_now.json` for the other apps and `show.json` for the game. SHOW is a *replay*: the mod records, every time a hand is really at a spot, where its arm target sat relative to the gun (or the body, for H / A / M / F), and while SHOW asks for that spot it puts the target back there and reports the spot's codes, so `dockpose` adds the spot's offset exactly as in play and the game script loads the spot's pose. A spot never visited for real is shown at its zone's centre (said so on the line).

**Tech Stack:** C# (.NET Framework 4, `csc`), REFramework Lua, the Visceral RE8 C++ plugin (`dev-archive/native`, its own `build.sh --deploy`).

**Spec:** `docs/superpowers/specs/2026-11-24-numbered-hand-spots-design.md`

## Global Constraints

- Numbers are append-only per gun: a name never changes number, nothing is removed or renumbered.
- Letters: `L<n>` left-hand gun spots, `R<n>` right-hand gun spots, `H1` right shoulder, `H2` right hip, `H3` left hip, `H4` left shoulder (the mod's codes 1-4), `A` ammo pouch, `M` map, `F` first aid.
- SHOW must never leak into play: no `show.json` heartbeat for 5 s, SHOW unticked or Hand Poser closed = real hands.
- No drop-down lists in anything new (Window View does not show their lists); big buttons; wider, not taller.
- Hand-written files: 800 lines soft limit (PoserUi.cs is at 703: new code goes in new files).
- Every comment that records a decision carries the date and Tefa's words, as the rest of these files do.

## Review Focus

- A spot name never seen before appears mid-play (a new build adds a spot): it takes the next number, older numbers unchanged. Test in Task 1.
- Two windows writing `spot_numbers.json` (Hand Poser restarted while the game runs): re-read before append, so a number is never given twice. Test in Task 1.
- SHOW picked for a spot never recorded: the hand goes to the zone centre, the line says "(not recorded yet: shown at the zone)". Test in Task 4 (fake game).
- The gun changes while SHOW is on: SHOW drops (the shown spot belongs to the old gun). Test in Task 4.
- Hand Poser crashes with SHOW on: the heartbeat stops, the mod gives the hands back within 5 s. Test in Task 4 (stale `t`).

---

### Task 1: The numbers (Hand Poser, `SpotNumbers.cs`)

**Files:**
- Create: `app/SpotNumbers.cs`
- Modify: `app/SelfTest.cs` (new checks)

**Interfaces:**
- Produces: `static class SpotNumbers` with
  - `static string LeftPart(string spot)`, `static string RightPart(string spot)`: split the game script's combined name (`"drum + right hand on the cover"`) the way `HandSpots.Line` does (`"guard"` belongs to both).
  - `static string Code(string gun, string hand /* "l"|"r" */, string part, string dataDir)` -> `"L5"`, `"R3"`, `"H2"`, `"A"`, `"M"`, `"F"`, or `""` (free, nothing, guard); assigns a new number when needed.
  - `static List<KeyValuePair<string,string>> List(string gun, string hand, string dataDir)` -> code, part, in number order, then H1-H4, A, M, F.
  - `static void Seed(string gun, string dataDir)`: once per gun, from the pose files in `<dataDir>/guns/` named `<gun> @ <spot>.json`, in Hand Poser's usual spot order.
- File: `<dataDir>/spot_numbers.json` = `{ "<gun>": { "L": ["drum", ...], "R": ["right hand on the cover", ...] } }`.
- Fixed parts: holster `at right shoulder holster` H1, `at right hip holster` H2, `at left hip holster` H3, `at left shoulder holster` H4 (right hand: the same with `right hand ` in front); `ready at pouch` / `rounds from the pouch` -> A; `at map` -> M; `at first aid` -> F (both hands, `right hand ` in front for the right).

- [ ] **Step 1: Write the failing self-test checks** in `SelfTest.cs`: a pretend data folder with `guns/ri3008_Inventory @ drum.json` and `@ handle.json`; `Seed` then `Code("ri3008_Inventory","l","handle")` and `"drum"` give two different L numbers; `Code(...,"hammer")` gives `L3`; a second call gives the same; deleting nothing and re-reading the file from a second `SpotNumbers` pass gives the same numbers; `Code(...,"r","right hand at right hip holster")` = `H2`; `Code(...,"l","ready at pouch")` = `A`; `Code(...,"l","free")` = `""`; another gun starts again at L1.
- [ ] **Step 2: Build and run** `build.bat` then `dist\HandPoser.exe --selftest build\selftest`; expected: the new checks FAIL (no `SpotNumbers`).
- [ ] **Step 3: Implement** `SpotNumbers.cs` (re-read the file before every append; write through a temp file and move).
- [ ] **Step 4: Run** the self-test; expected: all checks pass.
- [ ] **Step 5: Commit** `app/SpotNumbers.cs app/SelfTest.cs`.

### Task 2: The numbers on the line, and `spot_now.json` (Hand Poser)

**Files:**
- Modify: `app/HandSpots.cs` (`Line` gets the code in front: `"LEFT HAND: L5 · docked on the drum"`)
- Modify: `app/PoserUi.cs` (`UpdateStatus` passes gun and data dir; after the line changes, `SpotNumbers.WriteNow`)
- Modify: `app/SpotNumbers.cs`: `static void WriteNow(string dataDir, string gun, string left, string right)` writes `<dataDir>/spot_now.json` = `{ "t": <unix s>, "gun": "...", "left": "L5 docked on the drum", "right": "R3 on the cover" }` only when the text changed (or every 2 s for `t`).

- [ ] **Step 1: Failing check:** `Line("drum + right hand on the cover","l","ri3008_Inventory")` starts `"LEFT HAND: L"`; `WriteNow` writes both parts.
- [ ] **Step 2: Run, expect FAIL.**  **Step 3: Implement.**  **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit.**

### Task 3: The picker and SHOW (Hand Poser, `SpotPicker.cs`)

**Files:**
- Create: `app/SpotPicker.cs` (a box under the spot line: `LEFT` row of buttons from `SpotNumbers.List(gun,"l")`, `RIGHT` row, a `SHOW` tick box; the button of the hand's real spot outlined, the picked one filled)
- Modify: `app/PoserUi.cs` (add the box; `GunCheck` uses `picker.EditSpot` when set instead of `model.Spot`)
- Modify: `app/SelfTest.cs`

**Interfaces:**
- Produces: `class SpotPicker : SkinPanel` with `string EditSpot` (the combined spot name to edit, null = follow the game), `bool Show`, `void Refresh(string gun, string realSpot)`, and `static string ShowJson(int seq, double t, string gun, string left, string right)`.
- `show.json` (in Visceral RE8's data folder, `reframework/data/visceral_re8/show.json`): `{ "seq": n, "t": <unix s, rewritten every 1 s while on>, "gun": "ri3008", "left": {"name":"drum","spot":6,"area":0,"holster":0} | null, "right": {...} | null }`. Codes from the inverse of `hand_poser.lua`'s tables (`current_spot`, `AREA_NAMES`, `HOLSTER_NAMES`, `right_spot`): `SpotCodes.For(string hand, string part)`, inside `SpotPicker.cs`. A / M / F: `holster` 5 = first aid, 6 = map, 7 = pouch.
- Picking: SHOW off -> `EditSpot` = that hand's part joined with the other hand's real part (`" + "`), the pose loads into the sliders, saves go to that spot. SHOW on -> the same, plus `show.json` names that hand; the other hand keeps whatever it showed (or `null` = real).
- Unticking SHOW, closing the window, or the gun changing: `show.json` with both `null` (and the heartbeat stops).

- [ ] **Step 1: Failing checks:** `SpotCodes.For("l","drum")` = spot 6; `For("l","ready at slide")` = spot 14 area 12; `For("r","right hand on the cover")` = spot 1; `For("r","right hand at right hip holster")` = holster 2; `For("l","at first aid")` = holster 5; `ShowJson` round-trips through `Json.Parse`.
- [ ] **Step 2: Run, expect FAIL.**  **Step 3: Implement.**  **Step 4: Run, expect PASS.**
- [ ] **Step 5: Fake game:** `test/fake_game.py` with a gun and two spots: the window shows two L buttons; a click with SHOW on writes `show.json` with the right codes; untick writes nulls. Photograph the window (`test/grab_window.py`) and look at it.
- [ ] **Step 6: Commit.**

### Task 4: The mod: record and SHOW (`visceral-re8-vr`, `src/showspot.cpp`)

**Files:**
- Create: `dev-archive/native/src/showspot.h`, `showspot.cpp`
- Modify: `ik.cpp` (call `showspot::on_ik(ik)` right before `dockpose::on_ik(ik)`), `pairs.cpp` (where the spot slots are put: `showspot::override_left(spot, area)` / `override_right(spot, area)` replace the measured values while shown), `plugin.cpp` (`showspot::frame()` each UpdateBehavior), `zones.h/.cpp` (`bool shown_center(Key k, Vec3& c)` from the centres `publish` stores this frame), `settings.h` (`SHOW_STALE_S = 5.0f`, `SHOW_SAVE_EVERY_S = 3.0f`).

**Interfaces:**
- `void frame()`: reads `show.json` when its time changed; drops SHOW when `t` is older than `SHOW_STALE_S` or the gun's short id differs from `gun`.
- `void on_ik(MO* ik)`: not shown -> records the Target's pose for (gun, hand, spot, area, holster) in the gun's frame (`zones::gun_rot` + the weapon transform) or, for holster codes 1-7, in the player transform's frame (`player::now().tf`); shown -> sets the Target from the record, or from `shown_center` (gun spots) with the gun's turn when never recorded.
- Records: `reframework/data/visceral_re8/show_places.json`, `{ "<gun>": { "l|6|0|0": [x,y,z,qx,qy,qz,qw,"gun"|"body"] } }`, saved at most every `SHOW_SAVE_EVERY_S` when changed.
- Log lines: `showspot: left shown at drum (recorded)`, `showspot: left shown at drum (zone centre: not recorded yet)`, `showspot: off (window gone)`.

- [ ] **Step 1: Write** the code; `bash build.sh` compiles with no new warning `[compile-verified]`.
- [ ] **Step 2: Self-check by reading:** the record is taken only when not shown and the hand is at a real spot (code != 0 / != 15).
- [ ] **Step 3: Commit** (build number in the message), then `build.sh --deploy` when the game is closed (it is open now: deploy waits; say so).

### Task 5: The game script follows SHOW, and F / M / A (`hand_poser.lua`, `visceral_re8_holsters.lua`)

**Files:**
- Modify: `hand-poser-vr/reframework/autorun/hand_poser.lua`: `HOLSTER_NAMES` gains `[5] = "first aid"`, `[6] = "map"`, `[7] = "pouch"` (spot names `at first aid`, `at map`; 7 maps to the existing `ready at pouch`); while `show.json` is fresh and names a hand, that hand's spot name is the shown `name` (so the pose loads from that spot's file even before the mod's slots follow).
- Modify: `visceral-re8-vr/dev-archive/native/lua/visceral_re8_holsters.lua` `publish`: a hand in the medicine zone (`__vr_in_medicine_zone`, the hand that zone belongs to) reports 5; the map spot, if Village has one (look for it; Tefa's layout puts it at the LEFT back holster), 6; if Village has no map spot yet, M is listed but shown greyed "(no map spot in Village yet)".

- [ ] **Step 1: Write**; `luac -p` (or REFramework's load in the fake game) on both files.
- [ ] **Step 2: Commit both repos.**

### Task 6: The line in Animation & Timing and Grip Zones

**Files:**
- Modify: `animation-timing-vr/app/TimingUi.cs` (top line), `grip-zones-vr/app/ZonesUi.cs` (top line): read `reframework/data/hand_poser/spot_now.json` every poll; when fresh (< 5 s), show `L5 docked on the drum · R3 on the cover`.

- [ ] **Step 1:** add, build both, each app's self-test passes.
- [ ] **Step 2: Commit both; install all three apps** (each installs when its window is closed: wait-for-close then `install.ps1 -From dist`).

### Task 7: The headset check (Tefa)

- [ ] Tefa: Wolfsbane in hand, Hand Freeze on, Hand Poser: the line reads `L.. / R..`; pick L-drum with SHOW on -> the left hand goes to the drum; untick -> real hands. Board row on `hand-poser-vr` and `visceral-re8-vr` until Tefa says it works.
