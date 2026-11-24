// SpotNumbers.cs -- a fixed number for every hand spot on every gun (2026-11-24, Tefa: "have all the grip zones and different zone
// poses numbered ... example mag2 in hand but not docked on mag1 nr1. LG hand open on left side slide n2"; then "holsters can be H
// and number"; "A for ammo pouch, M for map and F for first aid med"). Each hand has its own list per gun: L1, L2... the left hand's
// spots on the gun, R1, R2... the right hand's; H1-H4 the holsters, A the ammo pouch, M the map, F first aid are the same for every
// gun. The numbers live in <data>/spot_numbers.json and are append-only: a spot keeps its number for good, a new spot takes the next.
// Design: docs/superpowers/specs/2026-11-24-numbered-hand-spots-design.md.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

static class SpotNumbers {
    public const string FILE = "spot_numbers.json";
    public const string NOW_FILE = "spot_now.json";   // what the hands are at now, for Animation & Timing and Grip Zones
    const string RIGHT = "right hand ";
    const string TRIGGER = "trigger pulled";

    // the same for every gun, in the order shown at the end of each hand's list; the left hand's name (the right's has "right hand " in front)
    public static readonly string[][] FIXED = {
        new[] { "H1", "at right shoulder holster" }, new[] { "H2", "at right hip holster" },
        new[] { "H3", "at left hip holster" }, new[] { "H4", "at left shoulder holster" },
        new[] { "A", "ready at pouch" }, new[] { "M", "at map" }, new[] { "F", "at first aid" },
    };
    static readonly HashSet<string> NO_NUMBER = new HashSet<string> { "", "free", "ready", "guard" };

    // seeding order: the spots of a reload, roughly in the order a hand meets them (anything else after, by name)
    static readonly string[] LEFT_ORDER = {
        "ready at slide", "ready on slide", "slide", "slide at the back",
        "ready at slide from the left", "ready on slide from the left", "slide from the left", "slide from the left at the back",
        "ready at magazine", "ready on magazine", "magazine", "magazine in hand", "two magazines in hand",
        "spare in hand, docked on the magazine", "spare in hand, pulling the magazine out", "spare in hand, magazine still in the gun",
        "old magazine in palm, the new one going in",
        "ready at well", "ready at handle", "handle", "ready at drum", "ready on drum", "drum", "ready on hammer", "hammer",
        "ready at insert point", "insert point", "rounds from the pouch", "shell port", "ready at front grip",
    };
    static readonly string[] RIGHT_ORDER = {
        "right hand on the cover", "right hand over the hammer", "right hand on the bolt", "right hand on the bolt at the back",
        "right hand, thumb on the latch", "right hand, the latch flicked open", "trigger pulled",
    };

    // ---- the game script's combined name, one hand at a time (as HandSpots.Line splits it) ----
    public static string LeftPart(string spot) {
        foreach (var p in Parts(spot)) if (!IsRight(p)) return p;
        return "";
    }
    public static string RightPart(string spot) {
        foreach (var p in Parts(spot)) if (IsRight(p)) return p;
        return "";
    }
    static IEnumerable<string> Parts(string spot) { return (spot ?? "").Split(new[] { " + " }, StringSplitOptions.RemoveEmptyEntries); }
    static bool IsRight(string p) { return p.StartsWith(RIGHT) || p.StartsWith("right hand,") || p == TRIGGER; }

    // ---- the numbers ----
    // a fixed letter for a body spot, or null; part is one hand's name
    static string FixedCode(string hand, string part) {
        string p = hand == "r" && part.StartsWith(RIGHT) ? part.Substring(RIGHT.Length) : part;
        foreach (var f in FIXED) if (f[1] == p) return f[0];
        return null;
    }
    public static string FixedName(string hand, string code) {
        foreach (var f in FIXED) if (f[0] == code) return hand == "r" ? RIGHT + f[1] : f[1];
        return null;
    }

    // "L5", "R3", "H2", "A", "M", "F", or "" (free, guard, nothing); a spot met for the first time on this gun takes the next number
    public static string Code(string gun, string hand, string part, string dataDir) {
        part = part ?? "";
        string bare = hand == "r" && part.StartsWith(RIGHT) ? part.Substring(RIGHT.Length) : part;
        if (NO_NUMBER.Contains(bare) || NO_NUMBER.Contains(part)) return "";
        string f = FixedCode(hand, part);
        if (f != null) return f;
        string key = hand == "r" ? "R" : "L";
        if (!Load(dataDir).ContainsKey(Gun(gun))) Seed(gun, dataDir);   // a gun met for the first time: its posed spots first
        var all = Load(dataDir);
        var list = ListOf(all, Gun(gun), key);
        int i = list.IndexOf(part);
        if (i < 0) {
            list.Add(part);   // append only, never renumber
            Save(dataDir, all);
            i = list.Count - 1;
        }
        return key + (i + 1);
    }

    // the hand's list: its numbers in order, then H1-H4, A, M, F (code, the hand's spot name)
    public static List<KeyValuePair<string, string>> List(string gun, string hand, string dataDir) {
        string key = hand == "r" ? "R" : "L";
        var list = ListOf(Load(dataDir), Gun(gun), key);
        var res = new List<KeyValuePair<string, string>>();
        for (int i = 0; i < list.Count; i++) res.Add(new KeyValuePair<string, string>(key + (i + 1), list[i]));
        foreach (var f in FIXED) res.Add(new KeyValuePair<string, string>(f[0], FixedName(hand, f[0])));
        return res;
    }

    // once per gun with no list yet: every spot that already has a pose file ("<gun> @ <spot>.json") gets its number, reload order first
    public static void Seed(string gun, string dataDir) {
        var all = Load(dataDir);
        string g = Gun(gun);
        if (all.ContainsKey(g)) return;
        var left = new List<string>(); var right = new List<string>();
        string dir = Path.Combine(dataDir, "guns");
        if (Directory.Exists(dir)) {
            string prefix = g + " @ ";
            foreach (var f in Directory.GetFiles(dir, "*.json")) {
                string n = Path.GetFileNameWithoutExtension(f);
                if (!n.StartsWith(prefix)) continue;
                string sp = n.Substring(prefix.Length);
                string l = LeftPart(sp), r = RightPart(sp);
                if (l != "" && !NO_NUMBER.Contains(l) && FixedCode("l", l) == null && !left.Contains(l)) left.Add(l);
                if (r != "" && FixedCode("r", r) == null && !NO_NUMBER.Contains(r.StartsWith(RIGHT) ? r.Substring(RIGHT.Length) : r) && !right.Contains(r)) right.Add(r);
            }
        }
        var entry = new Dictionary<string, object> {
            { "L", Ordered(left, LEFT_ORDER).Cast<object>().ToList() }, { "R", Ordered(right, RIGHT_ORDER).Cast<object>().ToList() } };
        all = Load(dataDir);   // read again just before writing: another window may have added a gun meanwhile
        if (all.ContainsKey(g)) return;
        all[g] = entry;
        Save(dataDir, all);
    }
    static List<string> Ordered(List<string> names, string[] order) {
        return names.OrderBy(n => { int i = Array.IndexOf(order, n); return i < 0 ? int.MaxValue : i; }).ThenBy(n => n, StringComparer.Ordinal).ToList();
    }

    // ---- the line for the other apps ----
    static string lastNow;
    static DateTime lastNowT = DateTime.MinValue;
    public static void WriteNow(string dataDir, string gun, string left, string right) {
        if (string.IsNullOrEmpty(dataDir)) return;
        string body = Json.Write(new Dictionary<string, object> { { "gun", gun ?? "" }, { "left", left ?? "" }, { "right", right ?? "" } });
        if (body == lastNow && (DateTime.UtcNow - lastNowT).TotalSeconds < 2.0) return;
        lastNow = body; lastNowT = DateTime.UtcNow;
        var o = new Dictionary<string, object> { { "t", (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds }, { "gun", gun ?? "" }, { "left", left ?? "" }, { "right", right ?? "" } };
        try { WriteAtomic(Path.Combine(dataDir, NOW_FILE), Json.Write(o)); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    // ---- the file ----
    static string Gun(string gun) { return string.IsNullOrEmpty(gun) ? "no gun" : gun; }
    // 2026-11-24: the window asks for the numbers on every status update; the file is parsed again only when it changed on disk
    // (helper windows must stay light beside a VR game). A copy is handed out, so a caller's Add never touches the cache.
    static string cachePath, cacheText;
    static DateTime cacheTime;
    static Dictionary<string, object> Load(string dataDir) {
        try {
            string f = Path.Combine(dataDir, FILE);
            if (File.Exists(f)) {
                var t = File.GetLastWriteTimeUtc(f);
                if (f != cachePath || t != cacheTime || cacheText == null) { cacheText = File.ReadAllText(f); cachePath = f; cacheTime = t; }
                var o = Json.Obj(Json.Parse(cacheText)); if (o != null) return o;
            }
        } catch (Exception) { }
        return new Dictionary<string, object>();
    }
    static List<object> ListOfRaw(Dictionary<string, object> all, string gun, string key) {
        object g;
        if (!all.TryGetValue(gun, out g) || Json.Obj(g) == null) { g = new Dictionary<string, object>(); all[gun] = g; }
        var go = Json.Obj(g);
        object l;
        if (!go.TryGetValue(key, out l) || Json.Arr(l) == null) { l = new List<object>(); go[key] = l; }
        return Json.Arr(l);
    }
    // a view of the raw list as strings that writes through on Add (the file keeps plain strings)
    static StringList ListOf(Dictionary<string, object> all, string gun, string key) { return new StringList(ListOfRaw(all, gun, key)); }
    sealed class StringList {
        readonly List<object> raw;
        public StringList(List<object> raw) { this.raw = raw; }
        public int Count { get { return raw.Count; } }
        public string this[int i] { get { return Json.Str(raw[i]); } }
        public int IndexOf(string s) { for (int i = 0; i < raw.Count; i++) if (Json.Str(raw[i]) == s) return i; return -1; }
        public void Add(string s) { raw.Add(s); }
    }
    static void Save(string dataDir, Dictionary<string, object> all) {
        Directory.CreateDirectory(dataDir);
        WriteAtomic(Path.Combine(dataDir, FILE), Json.Write(all));
        cacheText = null;   // read it fresh next time (two writes inside one file-time tick must not hide the second)
    }
    public static void WriteAtomic(string path, string text) {
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, text);
        if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
    }
}
