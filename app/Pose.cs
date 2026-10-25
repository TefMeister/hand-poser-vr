// Pose.cs -- what the Hand Poser window holds, with no window in it: the rows (one per joint, three angles in degrees),
// undo and redo, the autosave, the named pose builds, and the command.json the in-game script reads. The window only
// shows this and calls into it, so it can be tested on its own (--selftest).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// One joint the window is holding. `Known` is false until the game has reported the joint's live angles, so a freshly
// added finger starts where the game has it instead of snapping to 0,0,0; an unknown row is read but not held.
class Row {
    public string Name;
    public double X, Y, Z;
    public bool Known;
    // 2026-10-25: the joint as turned "from the hand" (the game reports it several times a second): it changes live when a
    // joint above it turns, which the joint's own angles never do. Shown only; never saved or sent.
    public double RelX, RelY, RelZ;
    public bool RelKnown;
    public Row Copy() { return new Row { Name = Name, X = X, Y = Y, Z = Z, Known = Known }; }
}

class Skeleton {
    public readonly List<string> Names = new List<string>();
    public readonly List<int> Parent = new List<int>();
    readonly Dictionary<string, int> index = new Dictionary<string, int>();

    public void Load(List<object> joints) {
        Names.Clear(); Parent.Clear(); index.Clear();
        if (joints == null) return;
        foreach (var j in joints) {
            var pair = Json.Arr(j);
            if (pair == null || pair.Count < 2) continue;
            string name = Json.Str(pair[0]);
            if (!index.ContainsKey(name)) index[name] = Names.Count;
            Names.Add(name);
            Parent.Add((int)Json.Num(pair[1], -1));
        }
    }

    public bool Has(string name) { return index.ContainsKey(name); }

    // Every joint that hangs below `name` (children, their children ...): the ones a turn of `name` carries along.
    public HashSet<string> Descendants(string name) {
        var result = new HashSet<string>();
        int root;
        if (!index.TryGetValue(name, out root)) return result;
        var below = new HashSet<int> { root };
        bool grew = true;
        while (grew) {                       // joints are usually parent-before-child, but this does not rely on it
            grew = false;
            for (int i = 0; i < Parent.Count; i++)
                if (!below.Contains(i) && Parent[i] >= 0 && below.Contains(Parent[i])) { below.Add(i); grew = true; }
        }
        foreach (int i in below) if (i != root) result.Add(Names[i]);
        return result;
    }
}

class PoseModel {
    public const int UNDO_STEPS = 200;
    public const double MIN_DEG = -180, MAX_DEG = 180;

    public readonly List<Row> Rows = new List<Row>();
    public readonly Skeleton Skeleton = new Skeleton();
    public string Game = "";              // from state.json; "" until the game has spoken
    public string Character = "";
    public bool Connected;
    public string GameObjectName = "";    // optional: pose this named object instead of the player
    public string LastError = "";
    public long Seq;                      // rises with every command written
    public int SeqSeen = -1;              // the last seq the script reported back

    readonly List<List<Row>> undo = new List<List<Row>>();
    readonly List<List<Row>> redo = new List<List<Row>>();
    readonly string appData;

    public PoseModel(string appDataDir) { appData = appDataDir; }

    // ---- folders ------------------------------------------------------------------------------------------------------
    string GameDir { get { return Path.Combine(appData, Game == "" ? "unknown" : Game); } }
    public string BuildsDir { get { return Path.Combine(GameDir, "builds"); } }
    string AutosaveFile { get { return Path.Combine(GameDir, "autosave.json"); } }

    // ---- rows -----------------------------------------------------------------------------------------------------------
    public Row Find(string name) { return Rows.FirstOrDefault(r => r.Name == name); }

    public bool Add(string name) {
        if (Find(name) != null) return false;
        Rows.Add(new Row { Name = name });
        return true;
    }

    // All joints matching a quick-add pattern (prefix or exact names), in skeleton order.
    public int AddMatching(Func<string, bool> match) {
        int n = 0;
        foreach (var name in Skeleton.Names) if (match(name) && Add(name)) n++;
        return n;
    }

    public bool Remove(string name) { return Rows.RemoveAll(r => r.Name == name) > 0; }
    public void Clear() { Rows.Clear(); }

    public static double Clamp(double d) { return Math.Max(MIN_DEG, Math.Min(MAX_DEG, d)); }

    public void Set(string name, double x, double y, double z) {
        var r = Find(name);
        if (r == null) return;
        r.X = Clamp(x); r.Y = Clamp(y); r.Z = Clamp(z); r.Known = true;
    }

    // ---- undo / redo: one snapshot per finished change --------------------------------------------------------------
    List<Row> Snapshot() { return Rows.Select(r => r.Copy()).ToList(); }
    void Restore(List<Row> snap) { Rows.Clear(); Rows.AddRange(snap.Select(r => r.Copy())); }

    public void Mark() {                  // call BEFORE a change, with the state to come back to
        undo.Add(Snapshot());
        if (undo.Count > UNDO_STEPS) undo.RemoveAt(0);
        redo.Clear();
    }
    public bool CanUndo { get { return undo.Count > 0; } }
    public bool CanRedo { get { return redo.Count > 0; } }
    public void Undo() {
        if (undo.Count == 0) return;
        redo.Add(Snapshot());
        Restore(undo[undo.Count - 1]);
        undo.RemoveAt(undo.Count - 1);
    }
    public void Redo() {
        if (redo.Count == 0) return;
        undo.Add(Snapshot());
        Restore(redo[redo.Count - 1]);
        redo.RemoveAt(redo.Count - 1);
    }

    // ---- state.json from the game -------------------------------------------------------------------------------------
    // Returns true when the skeleton or character changed (the window re-fills its joint list then).
    public bool ReadState(string text) {
        var st = Json.Obj(Json.Parse(text));
        if (st == null) return false;
        string game = Json.Str(st.ContainsKey("game") ? st["game"] : null);
        string character = Json.Str(st.ContainsKey("character") ? st["character"] : null);
        bool gameChanged = game != "" && game != Game;
        if (gameChanged) Game = game;
        Connected = Json.Bool(st.ContainsKey("connected") ? st["connected"] : null);
        Gun = Json.Str(st.ContainsKey("gun") ? st["gun"] : null);
        LastError = Json.Str(st.ContainsKey("error") ? st["error"] : null);
        SeqSeen = (int)Json.Num(st.ContainsKey("seq_seen") ? st["seq_seen"] : null, -1);
        bool skeletonChanged = character != Character || gameChanged;
        if (skeletonChanged) {
            Character = character;
            Skeleton.Load(Json.Arr(st.ContainsKey("joints") ? st["joints"] : null));
        }
        var angles = Json.Obj(st.ContainsKey("angles") ? st["angles"] : null);
        if (angles != null) {
            foreach (var r in Rows) {
                if (r.Known) continue;        // once held, the window's numbers are the truth
                object v;
                var a = angles.TryGetValue(r.Name, out v) ? Json.Arr(v) : null;
                if (a == null || a.Count < 3) continue;
                r.X = Clamp(Json.Num(a[0], 0)); r.Y = Clamp(Json.Num(a[1], 0)); r.Z = Clamp(Json.Num(a[2], 0));
                r.Known = true;
            }
        }
        var rel = Json.Obj(st.ContainsKey("rel") ? st["rel"] : null);
        if (rel != null) {
            foreach (var r in Rows) {
                object v;
                var a = rel.TryGetValue(r.Name, out v) ? Json.Arr(v) : null;
                r.RelKnown = a != null && a.Count >= 3;
                if (r.RelKnown) { r.RelX = Json.Num(a[0], 0); r.RelY = Json.Num(a[1], 0); r.RelZ = Json.Num(a[2], 0); }
            }
        }
        return skeletonChanged;
    }

    // ---- finger groups (2026-10-25, Tefa: "one slider bends a whole finger and all its joints follow"): joints whose names
    // differ only in a trailing number (L_IndexF1..3, l_hand_index_0..2) are one finger
    public static string GroupOf(string name) {
        string g = name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        g = g.TrimEnd('_');
        return g.Length > 0 && g.Length < name.Length ? g : null;
    }
    public List<KeyValuePair<string, List<Row>>> Groups() {
        var groups = new Dictionary<string, List<Row>>();
        var order = new List<string>();
        foreach (var r in Rows) {
            string g = GroupOf(r.Name);
            if (g == null) continue;
            List<Row> list;
            if (!groups.TryGetValue(g, out list)) { list = new List<Row>(); groups[g] = list; order.Add(g); }
            list.Add(r);
        }
        return order.Where(g => groups[g].Count >= 2).Select(g => new KeyValuePair<string, List<Row>>(g, groups[g])).ToList();
    }

    // ---- command.json to the game -------------------------------------------------------------------------------------
    public string Command() {
        var hold = new Dictionary<string, object>();
        var read = new List<object>();
        foreach (var r in Rows) {
            read.Add(r.Name);
            if (r.Known) hold[r.Name] = new List<object> { r.X, r.Y, r.Z };
        }
        var cmd = new Dictionary<string, object> {
            { "seq", (double)(++Seq) }, { "hold", hold }, { "want_read", read }, { "go_name", GameObjectName }
        };
        return Json.Write(cmd);
    }

    // ---- saving: autosave and named builds -----------------------------------------------------------------------------
    Dictionary<string, object> ToJson() {
        var joints = new Dictionary<string, object>();
        foreach (var r in Rows) joints[r.Name] = r.Known ? new List<object> { r.X, r.Y, r.Z } : null;
        return new Dictionary<string, object> { { "game", Game }, { "character", Character }, { "joints", joints } };
    }

    void FromJson(string text) {
        var d = Json.Obj(Json.Parse(text));
        var joints = d != null && d.ContainsKey("joints") ? Json.Obj(d["joints"]) : null;
        Rows.Clear();
        if (joints == null) return;
        foreach (var kv in joints) {
            var row = new Row { Name = kv.Key };
            var a = Json.Arr(kv.Value);
            if (a != null && a.Count >= 3) { row.X = Json.Num(a[0], 0); row.Y = Json.Num(a[1], 0); row.Z = Json.Num(a[2], 0); row.Known = true; }
            Rows.Add(row);
        }
    }

    public void Autosave() {
        Directory.CreateDirectory(GameDir);
        File.WriteAllText(AutosaveFile, Json.Write(ToJson()));
        if (GunPoseFile != null) { Directory.CreateDirectory(GunsDir); File.WriteAllText(GunPoseFile, Json.Write(ToJson())); }
    }

    // ---- one pose per gun (2026-10-25, Tefa: "yes please make it automatic!"): guns\<gun>.json beside the autosave. While
    // GunPoseFile is set, every autosave also goes to the gun in hand's file; the window swaps files when the gun changes
    public string Gun = "";                 // the gun in the right hand, as the game script reports it ("" = none)
    public string GunPoseFile;              // null = poses are not kept per gun
    public string GunsDir { get { return Path.Combine(GameDir, "guns"); } }
    public string GunFile(string gun) { return Path.Combine(GunsDir, SafeName(gun == "" ? "no gun" : gun) + ".json"); }
    public bool LoadFile(string file) {
        if (!File.Exists(file)) return false;
        try { FromJson(File.ReadAllText(file)); return true; } catch (FormatException) { return false; }
    }

    public bool LoadAutosave() {
        if (!File.Exists(AutosaveFile)) return false;
        try { FromJson(File.ReadAllText(AutosaveFile)); return true; } catch (FormatException) { return false; }
    }

    public static string SafeName(string name) {
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Trim();
    }

    public string SaveBuild(string name) {
        name = SafeName(name);
        if (name == "") return null;
        Directory.CreateDirectory(BuildsDir);
        string file = Path.Combine(BuildsDir, name + ".json");
        File.WriteAllText(file, Json.Write(ToJson()));
        return file;
    }

    public List<string> Builds() {
        if (!Directory.Exists(BuildsDir)) return new List<string>();
        return Directory.GetFiles(BuildsDir, "*.json").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n).ToList();
    }

    public bool LoadBuild(string name) {
        string file = Path.Combine(BuildsDir, SafeName(name) + ".json");
        if (!File.Exists(file)) return false;
        FromJson(File.ReadAllText(file));
        return true;
    }

    public bool DeleteBuild(string name) {
        string file = Path.Combine(BuildsDir, SafeName(name) + ".json");
        if (!File.Exists(file)) return false;
        File.Delete(file);
        return true;
    }
}
