# The helper windows' kit

One copy of the parts every Window View helper shares (Hand Poser, Grip Zones, Holster Zones, Gun Poser, Animation & Timing).
Edit them **here only**, then run `python tools/sync_kit.py` from hand-poser-vr: it copies them into each helper's own `kit/`
folder. Each helper's `build.bat` runs `sync_kit.py --check` first (when hand-poser-vr is cloned beside it) and stops when a copy
has drifted.

| file | what it is |
| --- | --- |
| `Skin.cs` | the look: the still backdrop, the drawn slider, buttons and tick boxes that take Window View's clicks, the colours (`Look`), the yellow frame of the left stick's selection |
| `Nudge.cs` | the controller buttons through Window View: the right stick steps the ticked sliders, the window clicked last answers (the yellow strip), a held grip stops everything |
| `NudgeNav.cs` | the left stick walks every control (sliders, buttons, the numbered boxes, tick boxes with words, options, section ticks); A presses; LT ticks a section; B unticks its sliders |
| `SliderUndo.cs` | one undo / redo for the whole window, on the last slider used (X / Y, the window's ↶ ↷) |
| `Json.cs` | the little JSON reader and writer between a window and its game script |

The buttons, the same in every helper (Window View's controller mode):
LT ticks the section under the pointer · left stick moves the yellow frame · A presses or ticks what is framed · right stick
tunes (±1 left / right, ±5 up / down) · B unticks the section's sliders · X undo · Y redo · a held grip stops all of it.
