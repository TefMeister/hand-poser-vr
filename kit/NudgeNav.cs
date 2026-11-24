// KIT FILE -- the one copy lives in hand-poser-vr/kit/; edit it THERE, then run `python tools/sync_kit.py` (it copies the kit into
// Grip Zones, Holster Zones, Gun Poser and Animation & Timing, and `--check` fails when a copy has drifted).
//
// NudgeNav.cs -- the left stick walks the window's controls, A presses or ticks the one selected (2026-11-24, Tefa: "can you make
// LS select the boxes in hand poser, in the highlighted section ... the idea is to move through the sliders with LS and tune the
// selected slider with RS, A to tick boxes to select multiple").
//
// An app turns it on with Navigate = true (all five helpers do since 2026-11-25).
//   STOPS: what the left stick can land on. A slider (its tick box and the slider beside it are framed together), a button (the
//     numbered movement boxes, PLAY ONCE, ↶ ...), a tick box with words (COPY ON +1), an option (LEFT / RIGHT / BOTH). A
//     section's own tick is not one (LT ticks sections; A on it would tick a whole section by accident). Hidden and greyed-out
//     ones are skipped. (Until 2026-11-25 only sliders: Tefa, in the headset after
//     Animation & Timing's drop-down got stuck: "what i really need now is a system where i have to do as little pointing and
//     clicking with RT, like you did with hand poser, and that system to work in here too")
//   left stick: the selection (a bright yellow frame) moves to the nearest stop that way; held past HOLD_MS it repeats every
//     NAV_REPEAT_MS. With any SECTION ticked it walks only the ticked sections' stops (skipping the rest: "i can tick the thumb and
//     middle finger and it will bypass the index finger if i keep going down"); with none it walks the whole window, and the part
//     it walks into becomes the highlighted one (dark yellow). Left / right keep to the row, or a stop at most ROW_REACH_PX higher
//     or lower (the next column); up / down take the nearest row that way. The first push in a part lands on its first slider
//     (its first stop when it has none). At the edge it stays
//   A: presses the selected stop as a click would: a button clicks, a tick box flips, an option is chosen
//   right stick: steps the highlighted sections' ticked sliders, or the selected slider when none there is ticked (Nudge.cs)
//   LT: Window View 1.14 sends the middle button at the left dot while a helper holds the buttons; here it ticks / unticks the
//     section of the part under it, and selects its first stop
//   B: the slider ticks of the highlighted section(s) off (the section ticks stay); X undo / Y redo on the last slider used
//     (SliderUndo.cs); a grip held (a window carried in Window View) stops all of it
//   RT: Window View's click, as before; a click on a stop also selects it
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

partial class Nudge {
    public bool Navigate;
    // the help line every helper shows (2026-11-25: the same words in all five)
    public const string HELP = "LT section · LS move · A press / tick · RS tune (±1, ±5) · B untick · X undo · Y redo · a grip stops all";
    public const string HELP_OFF = "start Window View and click the left stick in: ";
    const int LSTICK_Y = 624, LSTICK_X = 640;   // Window View's shared block (window-view-vr shared.h, 1.10)
    const int NAV_REPEAT_MS = 180;              // slower than the right stick's steps: a stop at a time, readable
    const int ROW_SLOP_PX = 18;                 // two stops this close in height are on one row
    const int ROW_REACH_PX = 36;                // left / right reach a stop at most this much higher or lower (another column)
    const int ACROSS_WEIGHT = 3;                // a step sideways counts this much more than a step the way the stick points
    const int REFIND_MS = 200;                  // how often a selection lost to a rebuilt row is looked for again
    int navDir, navSince, navLast, lastRefind;
    Control picked;                             // the selected stop
    string pickedKey;                           // its key: finds it again when its row is rebuilt
    readonly List<IPickable> framed = new List<IPickable>();

    // ---- sections (2026-11-08, 2026-11-24) -------------------------------------------------------------------------------------

    // the highlighted part: the last one clicked (or walked into) that is still there
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
    static bool IsSliderTick(CheckBox b) { return IsTick(b) && b.Tag is string && !IsSectionTick(b); }

    // 2026-11-25: a part's SECTION tick, made the same way in every helper (a 24 px box; in a row of buttons it goes first).
    // Its state is kept by name here, so a part rebuilt with the same name comes back ticked. `into`: the control that shows the
    // tick when it is not the part itself (the part's head row).
    static readonly HashSet<string> sectionOn = new HashSet<string>();
    public SkinCheckBox SectionTick(Control part, string name, int x = 4, int y = 4, Control into = null) {
        Section(part, name);
        if (into != null) part = into;
        var t = new SkinCheckBox { Width = 24, Height = 24, Location = new Point(x, y), Tag = "section:" + name, Checked = sectionOn.Contains(name), Margin = new Padding(0, 3, 6, 0) };
        t.CheckedChanged += (s, e) => { if (t.Checked) sectionOn.Add(name); else sectionOn.Remove(name); };
        part.Controls.Add(t);
        part.Controls.SetChildIndex(t, 0);   // a flowing row puts it first
        return t;
    }

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
    // where the right stick reaches and what is shaded: the ticked sections, else the part clicked (or walked into) last
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
    static List<CheckBox> Ticks(Control part) {
        var all = new List<CheckBox>();
        var stack = new Stack<Control>();
        stack.Push(part);
        while (stack.Count > 0) {
            var c = stack.Pop();
            var b = c as CheckBox;
            if (b != null && IsSliderTick(b) && b.Visible) all.Add(b);
            foreach (Control k in c.Controls) stack.Push(k);
        }
        return all;
    }

    // ---- stops (2026-11-25) ----------------------------------------------------------------------------------------------------

    static bool IsStop(Control c) {
        if (c == null || c.IsDisposed || !c.Visible) return false;
        var b = c as CheckBox;
        if (b != null) return IsTick(b) ? IsSliderTick(b) : b.Enabled;   // a slider's tick; a box with words (never a section's tick)
        if (c is RadioButton || c is Button) return c.Enabled;
        return false;
    }
    static void StopsUnder(Control c, List<Control> into) {
        foreach (Control k in c.Controls) {
            if (IsStop(k)) into.Add(k);
            else if (k.Visible) StopsUnder(k, into);
        }
    }
    List<Control> StopsIn(List<Control> parts) {
        var all = new List<Control>();
        var seen = new HashSet<Control>();
        foreach (var p in parts) {
            var some = new List<Control>();
            StopsUnder(p, some);
            foreach (var s in some) if (seen.Add(s)) all.Add(s);
        }
        return all;
    }
    Form NavForm() {
        if (markForm != null && !markForm.IsDisposed && markForm.Visible) return markForm;
        foreach (var c in sections.Values) if (c != null && !c.IsDisposed) { var f = c.FindForm(); if (f != null) return f; }
        foreach (Form f in Application.OpenForms) if (f.Visible) return f;
        return null;
    }
    List<Control> AllStops() {
        var f = NavForm();
        var all = new List<Control>();
        if (f != null) StopsUnder(f, all);
        return all;
    }

    // a stop's key: a tick box's own key, else its part, kind and words (a numbered box rebuilt with the same words is found again)
    string KeyOf(Control c) {
        var b = c as CheckBox;
        if (b != null && b.Tag is string) return (string)b.Tag;
        return "stop:" + (SectionOf(c) ?? "") + "|" + c.GetType().Name + "|" + (c.Text ?? "");
    }
    void Pick(Control c) { picked = c; pickedKey = c == null ? null : KeyOf(c); }

    Control PickedStop() {
        if (picked != null && !picked.IsDisposed && picked.FindForm() != null) return IsStop(picked) ? picked : null;
        if (pickedKey == null) return null;
        int now = Environment.TickCount;
        if (now - lastRefind < REFIND_MS) return null;
        lastRefind = now;
        foreach (var c in AllStops()) if (KeyOf(c) == pickedKey) { picked = c; return c; }
        return null;
    }

    // the selected slider's key, when it is in the highlighted part (null = none): the right stick's fall-back
    public string PickedKey {
        get {
            var b = PickedStop() as CheckBox;
            if (b == null || !IsSliderTick(b)) return null;
            foreach (var t in ScopeTicks()) if (t == b) return (string)b.Tag;
            return null;
        }
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

    // a part's first stop: its first slider when it has one (a box's header buttons, e.g. a fold, come after), else its first stop
    static Control First(List<Control> stops) {
        var sliders = stops.FindAll(s => s is CheckBox && IsSliderTick((CheckBox)s));
        return TopLeft(sliders.Count > 0 ? sliders : stops);
    }
    static Control TopLeft(List<Control> stops) {
        Control best = null;
        foreach (var b in stops) {
            if (best == null) { best = b; continue; }
            Point p = Mid(b), q = Mid(best);
            if (p.Y < q.Y - ROW_SLOP_PX || (Math.Abs(p.Y - q.Y) <= ROW_SLOP_PX && p.X < q.X)) best = b;
        }
        return best;
    }

    // the nearest stop that way: along the stick at least a step, sideways counting ACROSS_WEIGHT times; left / right prefer the
    // same row (a stop within ROW_SLOP_PX in height counts as straight across)
    static Control Nearest(List<Control> stops, Control cur, int dir) {
        Point c = Mid(cur);
        Control best = null;
        long bestScore = long.MaxValue;
        bool across = dir == 1 || dir == 2;
        foreach (var s in stops) {
            if (s == cur) continue;
            Point p = Mid(s);
            int dx = p.X - c.X, dy = p.Y - c.Y;
            int along = dir == 1 ? dx : dir == 2 ? -dx : dir == 4 ? dy : -dy;
            int side = across ? Math.Abs(dy) : Math.Abs(dx);
            if (across ? along <= 4 || side > ROW_REACH_PX : along <= ROW_SLOP_PX) continue;
            if (across && side <= ROW_SLOP_PX) side = 0;
            long score = along + (long)ACROSS_WEIGHT * side;
            if (score < bestScore) { bestScore = score; best = s; }
        }
        return best;
    }

    void Move(int dir) {
        var chosen = ChosenSections();
        var stops = chosen.Count > 0 ? StopsIn(chosen) : AllStops();
        if (stops.Count == 0) return;
        var cur = PickedStop();
        if (cur != null && !stops.Contains(cur)) cur = null;
        Control best;
        if (cur == null) {   // nothing selected there yet: the first stop (top row, leftmost) of the sections, or of the part clicked last
            var home = stops;
            if (chosen.Count == 0 && NavSection != null) { var h = StopsIn(new List<Control> { NavSection }); if (h.Count > 0) home = h; }
            best = First(home);
        } else best = Nearest(stops, cur, dir);
        if (best == null) return;   // the edge: stay
        Pick(best);
        if (chosen.Count == 0) {    // walked into another part: it is the highlighted one now
            string n = SectionOf(best);
            if (n != null) { recent.Remove(n); recent.Insert(0, n); }
        }
        ScrollTo(best);
        Frame(true);
    }

    // B (2026-11-24): the slider ticks of the highlighted section(s) off; the section ticks stay
    void UntickScope() {
        foreach (var b in ScopeTicks()) if (b.Checked) b.Checked = false;
    }

    // A: the selected stop pressed the way a click presses it (a button's Click, a box flipped, an option chosen), so each app's
    // own handlers run exactly as for the mouse
    static readonly MethodInfo onClick = typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic);
    void ToggleSelected() {
        var c = PickedStop();
        if (c == null || !c.Enabled) return;
        var b = c as CheckBox;
        if (b != null && IsTick(b)) { b.Checked = !b.Checked; return; }   // a slider's or a section's tick (CheckedChanged keeps the lists)
        onClick.Invoke(c, new object[] { EventArgs.Empty });   // a button clicks, a labelled box flips, an option is chosen (AutoCheck)
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
        if (t.Checked) {   // its first stop, unless the selection is already in the ticked sections
            var cur = PickedStop();
            if (cur == null || !StopsIn(ChosenSections()).Contains(cur)) {
                var first = First(StopsIn(new List<Control> { part }));
                if (first != null) { Pick(first); ScrollTo(first); }
            }
        }
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

    // a click on a stop (or on a slider: its tick box) selects it
    void NavClicked(Control c) {
        if (c is FancySlider) c = TickOf((FancySlider)c);
        if (c != null && IsStop(c)) Pick(c);
    }

    // the yellow frame on the selected stop (a slider's tick box and the slider together), only while this window answers the buttons
    void Frame(bool on) {
        var want = new List<IPickable>();
        if (on) {
            var c = PickedStop();
            var p = c as IPickable;
            if (p != null) want.Add(p);
            var b = c as CheckBox;
            if (b != null && IsSliderTick(b)) { var bar = SliderOf(b); if (bar != null) want.Add(bar); }
        }
        foreach (var f in framed) if (!want.Contains(f) && !((Control)f).IsDisposed) f.Picked = false;
        foreach (var f in want) f.Picked = true;
        framed.Clear(); framed.AddRange(want);
    }

    // a tick box's slider: the one right of it on the same row, in the same parent (the rows, the boxes all place the box just
    // left of its slider)
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
            if (b == null || !IsSliderTick(b) || b.Right > s.Left + 4 || Math.Abs(b.Top + b.Height / 2 - cy) > ROW_SLOP_PX) continue;
            if (best == null || b.Left > best.Left) best = b;
        }
        return best;
    }

    // a selected stop below the fold of a scrolling list is brought into view
    static void ScrollTo(Control c) {
        for (var p = c.Parent; p != null; p = p.Parent) {
            var sc = p as ScrollableControl;
            if (sc != null && sc.AutoScroll) { sc.ScrollControlIntoView(c); return; }
        }
    }
}
