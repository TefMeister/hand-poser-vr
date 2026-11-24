// SliderUndo.cs -- one undo / redo for the whole window, on the LAST slider used only (2026-11-24, Tefa: "X to undo, but undo only
// the current selected slider's actions and forget all undo after that, the same with undo/redo arrows, so we only need one set of
// arrow icons and they will only work on the last slider that was used, if a new slider gets tweaked, the last one is forgotten").
//
// Replaces each row's and each box's own ↶ / ↷ (RowHistory.cs, BoxHistory.cs, 2026-11-14). A "slider used" is what one press
// moves: one slider by the mouse or the pointer, or the ticked sliders the right stick steps together (they count as one). Its
// steps are kept until another slider (or set) is moved, or the pose in the window changes (another gun or spot): then they are
// forgotten. Undo / redo put the slider back as if it had been moved by hand, so the game, the autosave and the boxes' files
// follow exactly as for a drag. Sliders are found by their tick box's key, so rows rebuilt in between are no problem.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

class SliderUndo {
    public const int MAX_STEPS = 100;
    string context = "", keys = "";
    readonly List<Dictionary<string, int>> undo = new List<Dictionary<string, int>>(), redo = new List<Dictionary<string, int>>();

    static string Sig(IEnumerable<string> k) { return string.Join("|", k.OrderBy(x => x, StringComparer.Ordinal)); }

    // a press starts on these sliders, with their values before it
    public void Begin(string ctx, Dictionary<string, int> before) {
        if (before == null || before.Count == 0) return;
        string sig = Sig(before.Keys);
        if (ctx != context || sig != keys) { undo.Clear(); context = ctx; keys = sig; }   // another slider: the last one is forgotten
        redo.Clear();
        undo.Add(new Dictionary<string, int>(before));
        if (undo.Count > MAX_STEPS) undo.RemoveAt(0);
    }
    public bool CanUndo(string ctx) { return ctx == context && undo.Count > 0; }
    public bool CanRedo(string ctx) { return ctx == context && redo.Count > 0; }
    public string Keys { get { return keys; } }

    // the values to put back; `now` gives each slider's value now (kept for the way back)
    public Dictionary<string, int> Undo(string ctx, Func<string, int?> now) { return Step(ctx, now, undo, redo); }
    public Dictionary<string, int> Redo(string ctx, Func<string, int?> now) { return Step(ctx, now, redo, undo); }
    Dictionary<string, int> Step(string ctx, Func<string, int?> now, List<Dictionary<string, int>> from, List<Dictionary<string, int>> to) {
        if (ctx != context) { undo.Clear(); redo.Clear(); return null; }   // the pose changed: nothing of the old one applies
        if (from.Count == 0) return null;
        var want = from[from.Count - 1];
        from.RemoveAt(from.Count - 1);
        var back = new Dictionary<string, int>();
        foreach (var k in want.Keys) { var v = now(k); if (v.HasValue) back[k] = v.Value; }
        to.Add(back);
        return want;
    }
}

partial class Nudge {
    readonly SliderUndo history = new SliderUndo();
    public Func<string> UndoContext = () => "";   // the pose in the window (Hand Poser: the gun and spot)
    bool restoring;

    FancySlider SliderByKey(string key) {
        foreach (Form f in Application.OpenForms) {
            var b = FindTick(f, key);
            if (b != null) return SliderOf(b);
        }
        return null;
    }
    static CheckBox FindTick(Control c, string key) {
        foreach (Control k in c.Controls) {
            var b = k as CheckBox;
            if (b != null && IsTick(b) && (b.Tag as string) == key) return b;
            var r = FindTick(k, key);
            if (r != null) return r;
        }
        return null;
    }

    // the right stick starts a press on these sliders (keys), or the mouse took hold of one
    public void BeginUndo(IEnumerable<string> keys) {
        if (restoring) return;
        var before = new Dictionary<string, int>();
        foreach (var k in keys) {
            if (k.StartsWith("section:")) continue;
            var s = SliderByKey(k);
            if (s != null && !s.IsDisposed) before[k] = s.Value;
        }
        history.Begin(UndoContext(), before);
    }
    void SliderPressed(FancySlider s) {   // FancySlider.UserPressed: a slider taken hold of by the mouse or the pointer
        if (restoring) return;
        var t = TickOf(s);
        if (t != null && t.Tag is string) BeginUndo(new[] { (string)t.Tag });
    }

    public bool CanUndo { get { return history.CanUndo(UndoContext()); } }
    public bool CanRedo { get { return history.CanRedo(UndoContext()); } }

    public void UndoLast(bool redo) {
        Func<string, int?> now = k => { var s = SliderByKey(k); return s == null ? (int?)null : s.Value; };
        var want = redo ? history.Redo(UndoContext(), now) : history.Undo(UndoContext(), now);
        if (want == null) return;
        restoring = true;
        try {
            foreach (var kv in want) {
                var s = SliderByKey(kv.Key);
                if (s != null && !s.IsDisposed && s.Enabled) s.SetByUser(kv.Value);
            }
        } finally { restoring = false; }
    }
}
