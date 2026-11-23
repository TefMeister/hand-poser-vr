// Remote.cs -- a pose set from outside the window, as if the sliders had been moved (2026-11-23).
// Tefa 2026-11-23: "please use all the poser family tools for all this, and tweak the tools themselves as you please": a
// session with robot hands (nobody at the mouse, the real pointer left alone) had been writing the pose files itself, around
// the window. Now it writes ONE small file and the window does the rest exactly as for a slider: the row moves, the game gets
// the pose at once, it is autosaved under the gun and spot the hand is at, and UNDO takes it back.
//
//   <game>\reframework\data\hand_poser\remote.json   {"seq": 7, "set": {"L_IndexF1": [0, -5, -60], "L_Thumb2.y": 30}}
//       seq   a NEW number each time (the same number twice is read once)
//       set   joint -> [x, y, z] in degrees, or "joint.x" / ".y" / ".z" -> one number; a joint with no row yet gets one
//       mag   the MAGAZINE IN HAND box: x y z (cm), yaw pitch roll (degrees)
//       spare the spare magazine's place beside the held one (Visceral RE8's mag2.json): x y z (cm), rx ry rz (degrees)
//   <game>\reframework\data\hand_poser\remote_done.json   {"seq": 7, "ok": true, "pose": "<gun> @ <spot>", "held": 31, "note": ""}
//
// Nothing is applied before the window is connected and (with ONE POSE PER GUN ticked) knows which gun and spot it is posing.
using System;
using System.Collections.Generic;
using System.IO;

partial class PoserUi {
    double remoteSeq = -1;
    DateTime remoteWrote = DateTime.MinValue;

    void RemotePoll() {
        if (link.DataDir == null || !model.Connected) return;
        string file = Path.Combine(link.DataDir, "remote.json");
        DateTime wrote;
        try { if (!File.Exists(file)) return; wrote = File.GetLastWriteTime(file); } catch (IOException) { return; }
        if (wrote == remoteWrote) return;
        Dictionary<string, object> o;
        try { o = Json.Obj(Json.Parse(File.ReadAllText(file))); } catch (Exception) { return; }   // half-written: next time
        if (o == null) { remoteWrote = wrote; return; }
        object sv; double seq = o.TryGetValue("seq", out sv) ? Json.Num(sv, -1) : -1;
        if (seq == remoteSeq) { remoteWrote = wrote; return; }
        if (perGun && poseGun == null) return;   // the gun and spot are not decided yet: wait, do not pose the wrong file
        remoteWrote = wrote; remoteSeq = seq;
        object setv; var set = o.TryGetValue("set", out setv) ? Json.Obj(setv) : null;
        int n = 0; string note = "";
        if (set != null && set.Count > 0) {
            Change(() => {
                foreach (var kv in set) {
                    string name = kv.Key; int axis = -1;
                    int dot = name.LastIndexOf('.');
                    if (dot > 0 && dot == name.Length - 2 && "xyz".IndexOf(char.ToLowerInvariant(name[dot + 1])) >= 0) {
                        axis = "xyz".IndexOf(char.ToLowerInvariant(name[dot + 1])); name = name.Substring(0, dot);
                    }
                    if (model.Skeleton.Names.Count > 0 && !model.Skeleton.Names.Contains(name)) { note += "no joint " + name + "; "; continue; }
                    var r = model.Find(name);
                    if (r == null) {
                        if (axis >= 0) { note += name + " has no row yet: give all three; "; continue; }
                        r = new Row { Name = name }; model.Rows.Add(r);
                    }
                    var a = Json.Arr(kv.Value);
                    if (axis < 0 && a != null && a.Count == 3) { r.X = Json.Num(a[0], r.X); r.Y = Json.Num(a[1], r.Y); r.Z = Json.Num(a[2], r.Z); }
                    else if (axis == 0) r.X = Json.Num(kv.Value, r.X);
                    else if (axis == 1) r.Y = Json.Num(kv.Value, r.Y);
                    else if (axis == 2) r.Z = Json.Num(kv.Value, r.Z);
                    else { note += name + ": wants [x, y, z]; "; continue; }
                    r.Known = true; r.Moved = true; n++;
                }
            });
        }
        // the MAGAZINE IN HAND box (the hold of the magazine in the left hand, this gun's): "mag": {"x": 0, "y": 3.5, "pitch": 113}
        object mv; var mag = o.TryGetValue("mag", out mv) ? Json.Obj(mv) : null;
        if (mag != null) { string mn; if (magBox.RemoteSet(mag, out mn)) n++; note += mn == "" ? "" : mn + "; "; }
        // the SPARE magazine's place beside the held one (Visceral RE8's mag2.json "spare": move cm, turn deg, the magazine's own
        // axes), through the window's SPARE MAGAZINE box. "spare": {"x": 3, "rz": 20}
        object pv; var spare = o.TryGetValue("spare", out pv) ? Json.Obj(pv) : null;
        if (spare != null) { string sn; if (spareBox.RemoteSet(spare, out sn)) n++; note += sn == "" ? "" : sn + "; "; }
        if (n == 0 && note == "") note = "nothing to set";
        string done = "{\"seq\": " + seq.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", \"ok\": " + (n > 0 ? "true" : "false") +
                      ", \"set\": " + n + ", \"pose\": " + Json.Write(perGun ? (poseGun ?? "") : "") + ", \"held\": " + model.Rows.Count +
                      ", \"note\": " + Json.Write(note.Trim()) + "}";
        try { File.WriteAllText(Path.Combine(link.DataDir, "remote_done.json"), done); } catch (IOException) { }
    }
}
