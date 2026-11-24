// NudgeNav.cs -- the left stick walks the sliders of the highlighted part, A ticks (2026-11-24, Tefa: "can you make LS select the
// boxes in hand poser, in the highlighted section - the section still has to be highlighted and only RT to antually use the pointer
// to click, A to tick/untick a box, the idea is to move through the sliders with LS and tune the selected slider with RS, A to tick
// boxes to select multiple. right click is not needed in here at all i think").
//
// Hand Poser turns it on (Navigate = true); the other helpers' copies of Nudge.cs keep the old buttons.
//   the highlighted part: the part (a finger's box, the docked wrist, a magazine box, a joint's row) clicked last, shaded dark
//     yellow while this window answers the buttons, whether it has ticks or not
//   left stick: the selection (a bright yellow frame on a slider and its tick box) moves to the next slider that way inside the
//     part; held past HOLD_MS it repeats every NAV_REPEAT_MS; at the part's edge it stays
//   right stick: steps the part's ticked sliders, or the selected one when nothing in the part is ticked
//   A: ticks / unticks the selected slider's box. Window View still sends A as a right click: this window ignores right clicks
//   RT: Window View's click, as before; a click on a slider or its tick box also selects it
//
// 2026-11-24 evening (Tefa: "leave the tick marks, these will serve as the chosen section so if i want LS to move through multiple
// sections, i can tick the thumb and middle finger and it will bypass the index finger if i keep going down", "LT needs to now
// select the whole section of sliders, for example the thumb sliders and tick the box of that section", "X to undo"):
//   every part has a SECTION tick in its header (key "section:<part>"). With any section ticked, the left stick walks the sliders
//     of all the ticked sections (in screen order, skipping the others) and they are all shaded; with none, the part clicked last
//   LT: Window View 1.14 sends the middle button at the left dot while a helper holds the buttons; here it ticks / unticks the
//     section of the part under it (and selects its first slider when nothing in the sections is selected)
//   X: undo on the last slider used (SliderUndo.cs)
//   2026-11-24 later (Tefa: "Y redo, X undo B deselect all ticks in the selected section"): Y redo, B unticks the sliders of the
//     highlighted section(s); CLEAR ALL TICKS on the window still clears everything. A grip held (a window carried) stops all of it
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

partial class Nudge {
    public bool Navigate;
    const int LSTICK_Y = 624, LSTICK_X = 640;   // Window View's shared block (window-view-vr shared.h, 1.10)
    const int NAV_REPEAT_MS = 180;              // slower than the right stick's steps: a slider at a time, readable
    const int ROW_SLOP_PX = 18;                 // two tick boxes this close in height are on one row
    int navDir, navSince, navLast;
    string pickedKey;                           // the selected slider's tick key: survives the rows being rebuilt
    SkinCheckBox framedBox;
    FancySlider framedBar;

    // the highlighted part: the last one clicked that is still there
    Control NavSection {
        get {
            foreach (var n in recent) {
                Control c;
                if (sections.TryGetValue(n, out c) && c != null && !c.IsDisposed) return c;
            }
            return null;
        }
    }

    static bool IsSectionTick(CheckBox b) { return ((b.Tag as string) ?? "").StartsWith("section:"); }

    // the parts whose section tick is ticked, in the order they were named
    List<Control> ChosenSections() {
        var found = new List<Control>();
        foreach (var kv in sections) {
            var c = kv.Value;
            if (c == null || c.IsDisposed) continue;
            var t = FindSectionTick(c, "section:" + kv.Key);
            if (t != null && t.Checked) found.Add(c);
        }
        return found;
    }
    static CheckBox FindSectionTick(Control c, string key) {
        foreach (Control k in c.Controls) {
            var b = k as CheckBox;
            if (b != null && (b.Tag as string) == key) return b;
            var r = FindSectionTick(k, key);
            if (r != null) return r;
        }
        return null;
    }
    // where the left stick walks and the right stick reaches: the ticked sections, else the part clicked last
    List<Control> Scope() {
        var chosen = ChosenSections();
        if (chosen.Count > 0) return chosen;
        var n = NavSection;
        return n == null ? chosen : new List<Control> { n };
    }
    List<CheckBox> ScopeTicks() {
        var all = new List<CheckBox>();
        foreach (var c in Scope()) all.AddRange(Ticks(c));
        return all;
    }

    // the selected slider's key, when it is in the highlighted part (null = none)
    public string PickedKey { get { return PickedBox() != null ? pickedKey : null; } }

    CheckBox PickedBox() {
        if (pickedKey == null) return null;
        foreach (var b in ScopeTicks()) if ((string)b.Tag == pickedKey) return b;
        return null;
    }

    static List<CheckBox> Ticks(Control part) {
        var all = new List<CheckBox>();
        var stack = new Stack<Control>();
        stack.Push(part);
        while (stack.Count > 0) {
            var c = stack.Pop();
            var b = c as CheckBox;
            if (b != null && IsTick(b) && b.Tag is string && !IsSectionTick(b) && b.Visible) all.Add(b);
            foreach (Control k in c.Controls) stack.Push(k);
        }
        return all;
    }

    static Point Mid(Control c) { return c.PointToScreen(new Point(c.Width / 2, c.Height / 2)); }

    // the left stick, once a poll while this window answers the buttons in Window View's controller mode
    void NavStick(float sx, float sy) {
        int dir = 0;
        float ax = Math.Abs(sx), ay = Math.Abs(sy), on = navDir == 0 ? STICK_ON : STICK_OFF;
        if (Math.Max(ax, ay) > on) dir = ax >= ay ? (sx > 0 ? 1 : 2) : (sy > 0 ? 3 : 4);   // right, left, up, down
        int now = Environment.TickCount;
        if (dir != navDir) {
            navDir = dir;
            if (dir != 0) { navSince = navLast = now; Move(dir); }
        } else if (dir != 0 && now - navSince >= HOLD_MS && now - navLast >= NAV_REPEAT_MS) {
            navLast = now; Move(dir);
        }
    }

    void Move(int dir) {
        var boxes = ScopeTicks();
        if (boxes.Count == 0) return;
        var cur = PickedBox();
        CheckBox best = null;
        if (cur == null) {   // nothing selected in this part yet: its first slider (top row, leftmost)
            foreach (var b in boxes) {
                if (best == null) { best = b; continue; }
                Point p = Mid(b), q = Mid(best);
                if (p.Y < q.Y - ROW_SLOP_PX || (Math.Abs(p.Y - q.Y) <= ROW_SLOP_PX && p.X < q.X)) best = b;
            }
        } else {
            Point c = Mid(cur);
            if (dir == 1 || dir == 2) {   // along the row
                int bestDx = int.MaxValue;
                foreach (var b in boxes) {
                    Point p = Mid(b);
                    if (b == cur || Math.Abs(p.Y - c.Y) > ROW_SLOP_PX) continue;
                    int dx = dir == 1 ? p.X - c.X : c.X - p.X;
                    if (dx > 4 && dx < bestDx) { bestDx = dx; best = b; }
                }
            } else {                      // the nearest row up / down, then the box in it nearest across
                int rowY = int.MinValue, bestDy = int.MaxValue;
                foreach (var b in boxes) {
                    Point p = Mid(b);
                    int dy = dir == 4 ? p.Y - c.Y : c.Y - p.Y;
                    if (dy > ROW_SLOP_PX && dy < bestDy) { bestDy = dy; rowY = p.Y; }
                }
                if (rowY != int.MinValue) {
                    int bestDx = int.MaxValue;
                    foreach (var b in boxes) {
                        Point p = Mid(b);
                        if (Math.Abs(p.Y - rowY) > ROW_SLOP_PX) continue;
                        int dx = Math.Abs(p.X - c.X);
                        if (dx < bestDx) { bestDx = dx; best = b; }
                    }
                }
            }
        }
        if (best == null) return;   // the edge of the part: stay
        pickedKey = (string)best.Tag;
        ScrollTo(best);
        Frame(true);
    }

    // B (2026-11-24): the slider ticks of the highlighted section(s) off; the section ticks stay
    void UntickScope() {
        foreach (var b in ScopeTicks()) if (b.Checked) b.Checked = false;
    }

    // A: the selected slider's tick box flips (its own CheckedChanged keeps the app's list right)
    void ToggleSelected() {
        var b = PickedBox();
        if (b != null && b.Enabled) b.Checked = !b.Checked;
    }

    // LT (the middle button from Window View 1.14): the section of the part under it ticked / unticked
    void SectionClicked(Control c) {
        string n = SectionOf(c);
        Control part;
        if (n == null || !sections.TryGetValue(n, out part)) return;
        recent.Remove(n);
        recent.Insert(0, n);
        var t = FindSectionTick(part, "section:" + n);
        if (t == null) return;
        t.Checked = !t.Checked;
        if (t.Checked && PickedBox() == null && Ticks(part).Count > 0) { pickedKey = null; Move(4); }   // its first slider
    }

    // the ticked sections (or the part clicked last) shaded dark yellow while this window answers the buttons
    readonly Dictionary<Control, object> shadedMany = new Dictionary<Control, object>();
    void ShadeMany(List<Control> want) {
        var keep = new HashSet<Control>(want ?? new List<Control>());
        foreach (var c in new List<Control>(shadedMany.Keys)) {
            if (keep.Contains(c)) continue;
            if (!c.IsDisposed) SetTint(c, shadedMany[c]);
            shadedMany.Remove(c);
        }
        foreach (var c in keep) {
            if (shadedMany.ContainsKey(c) || c.IsDisposed) continue;
            shadedMany[c] = GetTint(c);
            SetTint(c, ACTIVE_TINT);
        }
    }

    // a click on a tick box or a slider selects that slider
    void NavClicked(Control c) {
        var b = c as CheckBox;
        if (b == null && c is FancySlider) b = TickOf((FancySlider)c);
        if (b != null && IsTick(b) && b.Tag is string && !IsSectionTick(b)) pickedKey = (string)b.Tag;
    }

    // the yellow frame on the selected slider and its box (only while this window answers the buttons)
    void Frame(bool on) {
        var b = on ? PickedBox() as SkinCheckBox : null;
        var bar = b != null ? SliderOf(b) : null;
        if (b != framedBox) { if (framedBox != null && !framedBox.IsDisposed) framedBox.Picked = false; framedBox = b; if (b != null) b.Picked = true; }
        if (bar != framedBar) { if (framedBar != null && !framedBar.IsDisposed) framedBar.Picked = false; framedBar = bar; if (bar != null) bar.Picked = true; }
    }

    // a tick box's slider: the one right of it on the same row, in the same parent (the rows, the docked wrist and the magazine
    // boxes all place the box just left of its slider)
    static FancySlider SliderOf(CheckBox b) {
        if (b.Parent == null) return null;
        int cy = b.Top + b.Height / 2;
        FancySlider best = null;
        foreach (Control k in b.Parent.Controls) {
            var s = k as FancySlider;
            if (s == null || s.Left < b.Right - 4 || Math.Abs(s.Top + s.Height / 2 - cy) > ROW_SLOP_PX) continue;
            if (best == null || s.Left < best.Left) best = s;
        }
        return best;
    }
    static CheckBox TickOf(FancySlider s) {
        if (s.Parent == null) return null;
        int cy = s.Top + s.Height / 2;
        CheckBox best = null;
        foreach (Control k in s.Parent.Controls) {
            var b = k as CheckBox;
            if (b == null || !IsTick(b) || b.Right > s.Left + 4 || Math.Abs(b.Top + b.Height / 2 - cy) > ROW_SLOP_PX) continue;
            if (best == null || b.Left > best.Left) best = b;
        }
        return best;
    }

    // a selected slider below the fold of a scrolling list is brought into view
    static void ScrollTo(Control c) {
        for (var p = c.Parent; p != null; p = p.Parent) {
            var sc = p as ScrollableControl;
            if (sc != null && sc.AutoScroll) { sc.ScrollControlIntoView(c); return; }
        }
    }
}
