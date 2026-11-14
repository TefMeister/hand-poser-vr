// BoxHistory.cs -- undo / redo for a whole box of sliders (2026-11-14, Tefa: "we need undo and redo for mag in hand and hand docked
// as well"): the LEFT HAND, DOCKED box (per gun and spot) and the MAGAZINE IN HAND box (per gun). A step is all six sliders at
// once, taken when a slider is pressed or a controller step starts; each box's ↶ / ↷ move only within the gun (and spot) shown,
// like the joint rows' own (RowHistory.cs). Kept while the window is open only.
using System;
using System.Collections.Generic;

static class BoxHistory {
    const int STEPS = 100;
    class H { public readonly List<int[]> Undo = new List<int[]>(), Redo = new List<int[]>(); }
    static readonly Dictionary<string, H> all = new Dictionary<string, H>();

    static H Of(string key, bool make) {
        H h;
        if (!all.TryGetValue(key ?? "", out h) && make) { h = new H(); all[key ?? ""] = h; }
        return h;
    }

    static bool Same(int[] a, int[] b) { if (a.Length != b.Length) return false; for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true; }

    public static void Mark(string key, int[] now) {   // BEFORE a change, with the values to come back to
        var h = Of(key, true);
        h.Redo.Clear();
        if (h.Undo.Count > 0 && Same(h.Undo[h.Undo.Count - 1], now)) return;
        h.Undo.Add((int[])now.Clone());
        if (h.Undo.Count > STEPS) h.Undo.RemoveAt(0);
    }

    public static bool CanUndo(string key) { var h = Of(key, false); return h != null && h.Undo.Count > 0; }
    public static bool CanRedo(string key) { var h = Of(key, false); return h != null && h.Redo.Count > 0; }

    // one step back (redo = forward); null = nothing to do
    public static int[] Step(string key, int[] now, bool redo) {
        var h = Of(key, false);
        if (h == null) return null;
        var from = redo ? h.Redo : h.Undo;
        var to = redo ? h.Undo : h.Redo;
        if (from.Count == 0) return null;
        var v = from[from.Count - 1];
        from.RemoveAt(from.Count - 1);
        to.Add((int[])now.Clone());
        return v;
    }
}
