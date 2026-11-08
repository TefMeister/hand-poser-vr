// Nudge.cs -- the controller buttons step the ticked sliders (2026-10-25, Tefa: "add tick boxes to each customizable hand part,
// so that when ticked, i don't have to actually slide the sliders, i can use buttons to + and - sliders, maybe X is -1, Y is -5
// and holding LG makes X and Y repeat press instantly. A and B and RG to do the same respectively for + on the sliders").
//
// The buttons come from Window View (github.com/TefMeister/window-view-vr), whose OpenXR layer reads the controllers while its
// CONTROL mode is on (left stick click: the game's hands are frozen then, so the buttons reach nobody else). Its shared block is
// read here, and while any box is ticked this app writes a claim into it, so Window View leaves A, Y and the grips alone
// (no right-click, no recentre, no carrying) for as long as the claim is fresh.
//   (until 2026-11-09: X = -1 degree, Y = -5, A = +1, B = +5, the grips repeating; now the right stick, see NEW BUTTONS below)
//
// 2026-11-09 (Tefa, in the headset: "undo button and redo buttons, can they be Y and B, and change the slider tuning to the RS so
// left and right are -1 and +1, up and down on the stick are +5 and -5. that leaves A to use for selecting checkboxes ... and X for
// unselect all", "please use these buttons in all helper apps"): NEW BUTTONS, the same in every helper:
//   right stick right / left = +1 / -1, up / down = +5 / -5 on the ticked sliders (held past HOLD_MS: repeats every REPEAT_MS)
//   Y = this window's UNDO button, B = its REDO button (any button whose text is "undo" / "redo"), X = every tick box unticked
//   A = Window View's left click (it is no longer borrowed here); the grips are Window View's again (carrying windows)
// Y / B / X act only in the window clicked last (the yellow strip); the stick needs a ticked box, as before. Needs Window View
// 1.10 (the stick's left / right, header 644 bytes).
//
// 2026-11-08 (Tefa: "have this active only on the window that i clicked on last ... just clicking on the window should be
// enough, without needing to actually check a box"): every helper window with this file (Hand Poser, Grip Zones, Gun Poser,
// Animation & Timing) shares one small block, Local\FlatToVR_SliderOwner_v1 {int pid, int unused, ulong beat}. A click
// anywhere in a helper window (the real mouse, or Window View's clicks, which it posts straight to the window under the
// pointer and so pass through this window's own message queue) makes that window the OWNER: only the owner's ticked sliders
// step and only the owner claims the buttons from Window View. The owner keeps its beat fresh while it runs and lets go when
// it closes; with no fresh owner (nothing clicked yet, or the owner gone) every helper steps, as before.
// The owner shows a YELLOW STRIP across the top of its window (Tefa 2026-11-08: "a yellow top part would be nice"), so the
// window the buttons reach can be seen at a glance in the headset; the strip goes as soon as another helper is clicked.
//
// 2026-11-08 (Tefa: "make the category active, not just a window. for instance if index finger and thumb both have ticked boxes,
// just move the thumb ones if that was clicked on last and make that dark yellow for the whole thumb section"): inside the
// window, each app names its parts with Section(control, name) (a finger's box, a zone's box, a joint's row). The buttons
// reach only the ticked sliders of the part clicked last that HAS a ticked box (clicking a part with none changes nothing);
// that part is shaded DARK YELLOW while this window is the owner. Nothing clicked in a part yet: every ticked slider, as before.
// Apps ask Allows(control) (is this slider in the reached part?) or Filter(ticked keys) (the ticked keys in the reached part,
// read from the tick boxes' Tag). A tick box is a CheckBox with no text.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Windows.Forms;

class Nudge : IDisposable {
    // Window View's shared block (window-view-vr shared.h, header v2 of 636 bytes)
    const string MAPPING = @"Local\FlatToVR_WindowView_v2";
    const int HEADER = 644, CTRL_MODE = 536, STICK_Y = 580, BUTTONS = 584, BUTTON_CLAIM = 628, STICK_X = 636;
    const int B_A = 1, B_Y = 2, B_B = 4, B_X = 8;
    const float STICK_ON = 0.6f, STICK_OFF = 0.3f;   // the stick counts as pushed past ON, let go below OFF
    public const int HOLD_MS = 400;     // a stick held this long starts repeating
    public const int REPEAT_MS = 60;    // then a step every 60 ms (about 16 a second)
    int stickDir;                       // 0 none, 1 right (+1), 2 left (-1), 3 up (+5), 4 down (-5)
    int stickSince, stickLast;
    bool mine;                          // this window was clicked last (it wears the strip)
    MemoryMappedFile file;
    MemoryMappedViewAccessor view;
    int lastOpenTry = -100000, lastButtons;
    public bool Linked { get { return view != null; } }

    const string OWNER_MAPPING = @"Local\FlatToVR_SliderOwner_v1";
    const int OWNER_SIZE = 16, OWNER_PID = 0, OWNER_BEAT = 8, OWNER_STALE_MS = 3000;
    MemoryMappedFile ownerFile;
    MemoryMappedViewAccessor ownerView;
    readonly int myPid = Process.GetCurrentProcess().Id;
    public bool Owner { get; private set; }   // this window answers the buttons now (the last helper clicked, or no fresh owner)
    const int MARK_H = 8;
    static readonly System.Drawing.Color MARK = System.Drawing.Color.FromArgb(255, 205, 0);
    const string MARK_NAME = "abxy_owner_strip";
    Form markForm;     // the window last clicked in this app: the strip goes on it
    Panel mark;

    public Nudge() { Application.AddMessageFilter(new ClickWatch(this)); }

    // ---- the part clicked last (2026-11-08) ----
    readonly Dictionary<string, Control> sections = new Dictionary<string, Control>();
    readonly List<string> recent = new List<string>();   // part names, the last clicked first
    static readonly System.Drawing.Color ACTIVE_TINT = System.Drawing.Color.FromArgb(215, 104, 80, 0);   // dark yellow
    Control shaded;
    object shadedWas;
    // name a part; the same name again (a rebuilt box) replaces the old control
    public void Section(Control c, string name) { sections[name] = c; }
    string SectionOf(Control c) {
        for (; c != null; c = c.Parent) foreach (var kv in sections) if (kv.Value == c) return kv.Key;
        return null;
    }
    static bool IsTick(CheckBox b) { return string.IsNullOrEmpty(b.Text); }
    static bool HasTicked(Control c) {
        foreach (Control k in c.Controls) {
            var b = k as CheckBox;
            if (b != null && b.Checked && IsTick(b)) return true;
            if (HasTicked(k)) return true;
        }
        return false;
    }
    static void TagsOf(Control c, HashSet<string> into) {
        foreach (Control k in c.Controls) {
            var b = k as CheckBox;
            if (b != null && b.Checked && IsTick(b) && b.Tag is string) into.Add((string)b.Tag);
            TagsOf(k, into);
        }
    }
    // the part the buttons reach: the last clicked one with a ticked box, or null (then every ticked slider)
    public Control Active {
        get {
            foreach (var n in recent) {
                Control c;
                if (sections.TryGetValue(n, out c) && c != null && !c.IsDisposed && HasTicked(c)) return c;
            }
            return null;
        }
    }
    public bool Allows(Control c) {
        var a = Active;
        if (a == null) return true;
        for (; c != null; c = c.Parent) if (c == a) return true;
        return false;
    }
    public HashSet<string> Filter(HashSet<string> ticked) {
        var a = Active;
        if (a == null) return ticked;
        var keys = new HashSet<string>();
        TagsOf(a, keys);
        keys.IntersectWith(ticked);
        return keys;
    }
    void Clicked(Control c) {
        string n = SectionOf(c);
        if (n == null) return;
        recent.Remove(n);
        recent.Insert(0, n);
    }
    void Shade(Control want) {   // the reached part dark yellow (its Tint when it has one, else its BackColor)
        if (want == shaded) return;
        if (shaded != null && !shaded.IsDisposed) SetTint(shaded, shadedWas);
        shaded = want;
        shadedWas = null;
        if (want != null) { shadedWas = GetTint(want); SetTint(want, ACTIVE_TINT); }
    }
    static object GetTint(Control c) {
        var p = c.GetType().GetProperty("Tint");
        return p != null ? p.GetValue(c, null) : (object)c.BackColor;
    }
    static void SetTint(Control c, object v) {
        if (v == null) return;
        var p = c.GetType().GetProperty("Tint");
        if (p != null) p.SetValue(c, v, null); else c.BackColor = (System.Drawing.Color)v;
        c.Invalidate(true);
    }

    class ClickWatch : IMessageFilter {   // any mouse button down in this app's windows (client area or title bar)
        readonly Nudge n;
        public ClickWatch(Nudge n) { this.n = n; }
        public bool PreFilterMessage(ref Message m) {
            int k = m.Msg;
            if (k == 0x201 || k == 0x203 || k == 0x204 || k == 0x207 || k == 0xA1 || k == 0xA4) {
                var c = Control.FromHandle(m.HWnd);
                var f = c as Form ?? (c != null ? c.FindForm() : null);
                if (f != null) n.markForm = f;
                n.TakeOwnership();
                n.Clicked(c);
            }
            return false;
        }
    }
    bool OpenOwner() {
        if (ownerView != null) return true;
        try {
            ownerFile = MemoryMappedFile.CreateOrOpen(OWNER_MAPPING, OWNER_SIZE);
            ownerView = ownerFile.CreateViewAccessor(0, OWNER_SIZE);
            return true;
        } catch (Exception) { CloseOwner(); return false; }
    }
    void CloseOwner() {
        if (ownerView != null) { ownerView.Dispose(); ownerView = null; }
        if (ownerFile != null) { ownerFile.Dispose(); ownerFile = null; }
    }
    public void TakeOwnership() {
        if (!OpenOwner()) return;
        try { ownerView.Write(OWNER_PID, myPid); ownerView.Write(OWNER_BEAT, GetTickCount64()); } catch (Exception) { CloseOwner(); }
    }
    bool IsOwner() {   // also keeps the owner's beat fresh
        if (!OpenOwner()) return true;
        try {
            int pid = ownerView.ReadInt32(OWNER_PID);
            ulong beat = ownerView.ReadUInt64(OWNER_BEAT), now = GetTickCount64();
            mine = pid == myPid;
            if (mine) { ownerView.Write(OWNER_BEAT, now); ShowMark(true); return true; }
            ShowMark(false);
            return pid == 0 || now - beat > OWNER_STALE_MS;
        } catch (Exception) { CloseOwner(); return true; }
    }
    void ShowMark(bool on) {   // the yellow strip on top of the window last clicked (one per window, even with two Nudges)
        if (markForm == null || markForm.IsDisposed) { if (mark != null && !mark.IsDisposed) mark.Visible = false; return; }
        if (mark == null || mark.IsDisposed || mark.Parent != markForm) {
            if (mark != null && !mark.IsDisposed && mark.Parent != markForm) mark.Visible = false;
            var found = markForm.Controls.Find(MARK_NAME, false);
            mark = found.Length > 0 ? (Panel)found[0] : null;
            if (mark == null) {
                if (!on) return;
                mark = new Panel { Name = MARK_NAME, Dock = DockStyle.Top, Height = MARK_H, BackColor = MARK, Visible = false };
                markForm.Controls.Add(mark);   // added last = docked first = the very top
            }
        }
        if (mark.Visible != on) mark.Visible = on;
    }

    [DllImport("kernel32.dll")] static extern ulong GetTickCount64();

    bool Open() {
        if (view != null) return true;
        if (Environment.TickCount - lastOpenTry < 2000) return false;
        lastOpenTry = Environment.TickCount;
        try {
            file = MemoryMappedFile.OpenExisting(MAPPING, MemoryMappedFileRights.ReadWrite);
            view = file.CreateViewAccessor(0, HEADER);
            return true;
        } catch (Exception) { Close(); return false; }   // Window View not running, or an older one
    }
    void Close() {
        if (view != null) { view.Dispose(); view = null; }
        if (file != null) { file.Dispose(); file = null; }
    }

    // once a tick (about every 15 ms): returns the steps (in degrees) to apply now, 0 = none; `anyTicked` claims the stick
    public double Poll(bool anyTicked, out bool pressStarted) {
        pressStarted = false;
        mine = false;
        Owner = IsOwner();
        Shade(Owner && anyTicked ? Active : null);
        if (!Open()) return 0;
        int buttons; float sx, sy; bool ctrl;
        try {
            ctrl = view.ReadInt32(CTRL_MODE) != 0;
            buttons = view.ReadInt32(BUTTONS);
            sx = view.ReadSingle(STICK_X);
            sy = view.ReadSingle(STICK_Y);
            if (ctrl && Owner && (anyTicked || mine)) view.Write(BUTTON_CLAIM, GetTickCount64());
        } catch (Exception) { Close(); return 0; }
        if (!ctrl || !Owner) { lastButtons = buttons; stickDir = 0; return 0; }
        if (mine) {   // Y undo, B redo, X untick everything: the window clicked last only
            if (Pressed(buttons, B_Y)) ClickNamed("undo");
            if (Pressed(buttons, B_B)) ClickNamed("redo");
            if (Pressed(buttons, B_X)) UntickAll();
        }
        lastButtons = buttons;
        if (!anyTicked) { stickDir = 0; return 0; }
        // the stick: one direction at a time, the stronger axis
        int dir = 0;
        float ax = Math.Abs(sx), ay = Math.Abs(sy);
        float on = stickDir == 0 ? STICK_ON : STICK_OFF;
        if (Math.Max(ax, ay) > on) dir = ax >= ay ? (sx > 0 ? 1 : 2) : (sy > 0 ? 3 : 4);
        int now = Environment.TickCount;
        double[] steps = { 0, +1, -1, +5, -5 };
        double total = 0;
        if (dir != stickDir) {
            stickDir = dir;
            if (dir != 0) { total = steps[dir]; stickSince = stickLast = now; pressStarted = true; }
        } else if (dir != 0 && now - stickSince >= HOLD_MS && now - stickLast >= REPEAT_MS) {
            total = steps[dir]; stickLast = now;
        }
        return total;
    }
    bool Pressed(int buttons, int bit) { return (buttons & bit) != 0 && (lastButtons & bit) == 0; }
    // a button of this window's whose text is the word (UNDO / REDO): clicked as if by the mouse
    void ClickNamed(string word) {
        var f = markForm;
        if (f == null || f.IsDisposed) return;
        var b = FindButton(f, word);
        if (b != null && b.Enabled && b.Visible) b.PerformClick();
    }
    static Button FindButton(Control c, string word) {
        foreach (Control k in c.Controls) {
            var b = k as Button;
            if (b != null && string.Equals((b.Text ?? "").Trim(), word, StringComparison.OrdinalIgnoreCase)) return b;
            var r = FindButton(k, word);
            if (r != null) return r;
        }
        return null;
    }
    // every tick box in this app's open windows unticked (their own CheckedChanged keeps each app's list right)
    static void UntickAll() {
        var forms = new List<Form>();
        foreach (Form f in Application.OpenForms) forms.Add(f);
        foreach (var f in forms) Untick(f);
    }
    static void Untick(Control c) {
        foreach (Control k in c.Controls) {
            var b = k as CheckBox;
            if (b != null && b.Checked && IsTick(b)) b.Checked = false;
            Untick(k);
        }
    }

    public void Dispose() {
        try { if (OpenOwner() && ownerView.ReadInt32(OWNER_PID) == myPid) ownerView.Write(OWNER_PID, 0); } catch (Exception) { }   // let go: the others answer again
        CloseOwner();
        Close();
    }
}
