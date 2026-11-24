# Numbered hand spots, and SHOW a spot without doing it (2026-11-24)

## Why

Tefa is fine-tuning Visceral RE8's guns and hand poses. Today a pose can only be tuned by putting the hand at its spot for
real (dock, open the drum, take a spare magazine...), freezing it, and tuning; changing spot means unfreezing and doing it
again. Tefa (2026-11-24): *"have all the grip zones and different zone poses numbered ... and what would be the greatest is,
when hand freeze is on, to have these selectable - so if i select nr2, my hands automatically go on the gun in that grip zone
and the pose both hands have in that zone ... without me having to release hand freeze"*. Chosen: **A, show it** (*"yes if i
can tune things without them being for real and once i'm finished tuning and try them out for real"*).

## What Tefa decided

- **Every hand spot gets a number, per gun, the same in every app.** Each hand its own list: **L1, L2...** the left hand's
  spots, **R1, R2...** the right hand's, **H1...H4** the holsters (Tefa: *"holsters can be H and number"*). Approved by Tefa 2026-11-24 (*"ok for the plan"*).
- **Body spots by letter** (Tefa, the same day: *"A for ammo pouch, M for map and F for first aid med"*): **A** the ammo pouch,
  **M** the map spot, **F** the first-aid (medicine) spot. They sit in each hand's list like the holsters, the same for every gun.
- **A number never changes.** A spot keeps its number on that gun for good; a new spot takes the next free one.
- **Picking a number SHOWS the spot** (approach A): the mod puts that hand on the gun at that spot with that spot's pose, the
  real reload is not running. Unticking SHOW gives the real hands back, for trying it for real.
- Poses keep saving by themselves per gun and spot (as today).

## Approaches considered

- **A, show it (chosen):** the mod pretends the hand is at the spot: it reports that spot (so Hand Poser loads and saves its
  pose as usual) and puts the hand at the spot's place on the gun. Every spot reachable at once, mid-reload ones included.
- **B, do it for real:** Hand Freeze moves the frozen controllers to the spot and the game docks them normally. True to play,
  but a spot that is a moment in a reload (a spare magazine in hand) cannot be reached by moving a hand. Not chosen.

## The parts

1. **The number list** (Hand Poser's script and window): one file per game, `reframework/data/hand_poser/spot_numbers.json`,
   `{ "<gun>": { "L": ["drum", "handle", ...], "R": [...] }, "H": [the four holsters] }`; the index + 1 is the number.
   Seeded once from the pose files already saved (`<gun> @ <left spot>[ + right hand <right spot>].json`), then a spot met in
   play for the first time on a gun is appended. Holsters are one fixed list for all guns (H1 right shoulder, H2 right hip,
   H3 left hip, H4 left shoulder: the mod's own codes 1 to 4). Writes only append; nothing is renumbered or removed.
   A, M and F are fixed too: A = the pouch (the mod's area 1, "ready at pouch"); M and F need the holster script to report
   the map and medicine zones it already has (`re8_vr_holster.lua`, `re8_vr_hand_gestures.lua`) in slots 214 / 215 as two new
   codes (5 map, 6 medicine), so a hand there gets its own pose like a holster.
2. **Shown everywhere:** Hand Poser's top line says the numbers with the words (*"LEFT L5 drum · RIGHT R3 on the cover"*);
   Animation & Timing and Grip Zones show the same line (read from a small `spot_now.json` the script writes when it
   changes).
3. **The picker** (Hand Poser): two lists, LEFT and RIGHT (holsters at the end of each as H1 to H4), and a **SHOW** tick box.
   Picking with SHOW ticked sends that hand to the spot; the other hand stays where it is. With SHOW unticked a pick only
   loads that spot's pose into the sliders to edit (it saves under that spot), the hand stays where the game has it.
   Big buttons, wider not taller, usable through Window View (no drop-down lists: they do not show there).
4. **The mod's SHOW** (Visceral RE8): a `show.json` command `{ "seq", "left": "<spot>", "right": "<spot>" }` (empty = real).
   While a hand is shown: the spot codes the mod reports to Hand Poser are the shown ones (so the right pose loads, and
   saves go to that spot), and the hand's arm aims at the spot's place on the gun in hand (the part's zone centre on the gun,
   plus the spot's saved whole-hand offset, as a docked hand) instead of the controller. The gun stays in the other hand as
   it is. Nothing of the reload state changes (no magazine taken, no drum opened): a shown spot is only hands.
   Works with Hand Freeze on or off; meant for on.

## Not in this first build

- Props drawn for a shown spot (the spare magazine in the hand, the drum open). Hands only first; added if Tefa misses them.
- Other games (RE2 / RE3): the numbers follow there once Village works.
- Hand Freeze's own window stays a plain any-game tool (it knows nothing of guns); the picking is in Hand Poser.

## Anti-goals

- A shown pose must never leak into play: SHOW off (or the window closed, or no command for 5 s) gives the real hands back.
- Numbers that move would make the whole thing useless: append-only.

## Left to the builder

The zone centre per spot for every gun (from the mod's zones and dock places), the file seeding details, the test with the
fake game, the ordering of the first seeding (by the spot's place in the reload, as Hand Poser lists spots today).
