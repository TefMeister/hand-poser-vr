# Hand Poser VR: design (2026-10-16)

## Why

Tefa (2026-10-16): *"can you extract just the poser bit from EMVengine to use in vr, and make it a separate program
just like the hand freeze, a neat compact window with all the sliders and the ability to add and remove things from
it?"* EMV Engine's poser (alphaZomega, MIT) lives inside REFramework's own menu, which smears in VR; a separate desktop
window can be hung in the headset by Window View (`window-view-vr`) and used with the mouse while Hand Freeze holds the
controllers still. Together the three make hand tuning possible without taking the headset off.

## What Tefa decided

- **RE games first** (REFramework); other engines maybe later.
- **Hands first**, the whole body maybe later.
- **Add as many tunable parts as wanted, and remove them again.**
- **Show which other bones a change carries along**; later, warn when parts overlap; last, nudge automatically.
  Tefa: *"i know i'm asking for a miracle here"*; agreed to do it in steps.
- **Every change saves by itself**; named **pose builds** to go back to; **undo and redo**.
- Later: hands that **glide** to the gun instead of jumping (filed on the RE2 ideas page).
- Public repo, description says untested; credit the creator of EMV Engine.

## Shape (builder's calls, marked)

Two parts, like Hand Freeze and Window View:

1. **The in-game part: one Lua script** `reframework/autorun/hand_poser.lua`, written fresh (EMV's poser is studied,
   not copied; the timing it proved is used: write each held joint's `LocalEulerAngle` at `LateUpdateBehavior` and
   again at `PrepareRendering`, the same two places Visceral RE2's hand poses use). It finds the player through
   `sdk.game_namespace("PlayerManager")` (EMV's generic route, works across RE games), lists the skeleton (joint
   names + parent indices) and applies what the app asks. *(call: Lua, not a plugin, because the posing itself is a
   few writes per frame and Lua reaches every RE game with REFramework alike; the "native by default" rule is for
   gameplay mods, and this is a tuning tool.)*
2. **The app** `HandPoser.exe` (C# WinForms, .NET Framework 4, `csc.exe` from Windows, like the other two): the
   window with the slider rows, add/remove, pose builds, undo/redo, autosave.

### Hand-over between them (call)

REFramework Lua can read and write only files under `<game>\reframework\data\`, so that is the channel:

- `reframework\data\hand_poser\state.json`, written by the script once a second and whenever asked: the game name,
  the character, every joint `[name, parent]`, and the live local Euler angles (degrees) of the joints the app is
  holding plus any it asked to read (`want_read`).
- `reframework\data\hand_poser\command.json`, written by the app on every change (debounced 30 ms) with a rising
  `seq`: `{seq, hold: {joint: [x,y,z] degrees...}, want_read: [...]}`. The script polls it every 3 frames and keeps
  the held joints at those angles (absolute, like EMV's freeze), everything else untouched.
- The app finds the game folder from the running game's exe path (the process that has `reframework\data` beside it),
  or the user picks it once; remembered in `%APPDATA%\HandPoserVR\settings.txt`.

### The window (call; Tefa tweaks it afterwards)

Compact: a top line (game, character, connected or not); **LEFT FINGERS** / **RIGHT FINGERS** / **LEFT HAND** /
**RIGHT HAND** quick-add buttons (all joints matching `l_hand_*` etc.); a filter box + list to add any single joint;
then one row per held joint: name, three small sliders X/Y/Z (-180..180 degrees, drag), a reset and a remove button.
While a slider is dragged, the rows of that joint's children light up (the skeleton's parent links from `state.json`).
Bottom: **UNDO**, **REDO**, the pose-build name box with **SAVE BUILD**, a list of saved builds with **LOAD**.
Autosave: every change to `%APPDATA%\HandPoserVR\<game>\autosave.json`, put back at start. Builds:
`%APPDATA%\HandPoserVR\<game>\builds\<name>.json` (joint -> degrees). Undo/redo: one step per slider release, add,
remove or load; 200 steps.

### What the pose is for (Tefa: the hands in play, per weapon, later)

v1 poses the live character for tuning and screenshots. Turning a saved build into Visceral RE2's per-weapon finger
pose (its `handpose_data.h` / tuning slots) is the next project, not this one.

## Not now

Overlap warning and auto-nudging (steps 3 and 4), the whole body, other engines, the glide.

## Testing

- The app against a hand-written `state.json` (fake game): rows, add/remove, undo/redo, builds, autosave,
  `command.json` written with rising `seq`.
- The script: in RE3 on the dev PC (installed with REFramework) when Tefa launches it, or RE2 on the home PC: the
  skeleton listed, one finger held at a set angle and seen to stay there.
