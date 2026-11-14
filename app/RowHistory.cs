// RowHistory.cs -- undo / redo for ONE joint row, per pose (2026-11-14). Tefa: "undo and redo needs to be under each row, right now i
// can undo stuff from previous sessions maybe? not sure, but i can keep on clicking undo. and only the actual buttons on the window
// to do it". The window's one undo list (PoseModel.Mark) took a step at every gun or spot change too, so UNDO walked back through
// other poses. Now each row keeps its own steps, for the pose it is in ("<gun> @ <spot>", the window's poseGun): the row's ↶ / ↷
// buttons move only that row, and only through changes made to it in that pose. Steps are taken when a slider is pressed, a row is
// reset, BEND ALL is pressed (every finger joint it moves) or a controller step starts. Kept while the window is open only.
using System;
using System.Collections.Generic;

static class RowHistory {
    const int STEPS = 100;   // per row and pose
    class H { public readonly List<double[]> Undo = new List<double[]>(), Redo = new List<double[]>(); }
    static readonly Dictionary<string, H> all = new Dictionary<string, H>();

    static H Of(string pose, string joint, bool make) {
        string k = (pose ?? "") + "|" + joint;
        H h;
        if (!all.TryGetValue(k, out h) && make) { h = new H(); all[k] = h; }
        return h;
    }

    // call BEFORE a change to the row, with the numbers to come back to
    public static void Mark(string pose, Row r) {
        if (r == null || !r.Known) return;
        var h = Of(pose, r.Name, true);
        var now = new[] { r.X, r.Y, r.Z };
        if (h.Undo.Count > 0) { var last = h.Undo[h.Undo.Count - 1]; if (last[0] == now[0] && last[1] == now[1] && last[2] == now[2]) { h.Redo.Clear(); return; } }
        h.Undo.Add(now);
        if (h.Undo.Count > STEPS) h.Undo.RemoveAt(0);
        h.Redo.Clear();
    }

    public static bool CanUndo(string pose, string joint) { var h = Of(pose, joint, false); return h != null && h.Undo.Count > 0; }
    public static bool CanRedo(string pose, string joint) { var h = Of(pose, joint, false); return h != null && h.Redo.Count > 0; }

    // one step back (redo = forward) on this row; false = nothing to do. The caller writes the row's numbers into the pose.
    public static bool Step(string pose, Row r, bool redo, out double x, out double y, out double z) {
        x = y = z = 0;
        var h = Of(pose, r.Name, false);
        if (h == null) return false;
        var from = redo ? h.Redo : h.Undo;
        var to = redo ? h.Undo : h.Redo;
        if (from.Count == 0) return false;
        var v = from[from.Count - 1];
        from.RemoveAt(from.Count - 1);
        to.Add(new[] { r.X, r.Y, r.Z });
        x = v[0]; y = v[1]; z = v[2];
        return true;
    }
}
