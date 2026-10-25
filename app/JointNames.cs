// JointNames.cs -- what each joint is, in plain words (2026-10-25, Tefa: "please also add more clear description in the hand
// pose program, to what part is what: R_IndexF1 is something that i don't know what it is until i try it").
//
// Covers the names the RE Engine games use: Village (and RE7) L_IndexF1 .. L_PinkyF3, L_Thumb1 .. 3, L_Hand; RE2 / RE3
// l_hand_index_0 .. l_hand_little_2, l_hand_thumb_0 .. 2, l_arm_wrist / forearm / upperarm / clavicle; plus the body
// (hips, spine, neck, head, legs). A name it does not know is shown as it is.
using System;
using System.Text.RegularExpressions;

static class JointNames {
    // "R_IndexF1" -> "right index finger: knuckle"; null when the name is not one it knows
    public static string Describe(string name) {
        if (string.IsNullOrEmpty(name)) return null;
        string n = name.ToLowerInvariant();
        string side = n.StartsWith("l_") || n.StartsWith("l ") ? "left " : n.StartsWith("r_") || n.StartsWith("r ") ? "right " : "";
        string rest = side == "" ? n : n.Substring(2);

        // fingers: Village "indexf1", RE2 "hand_index_0"; the thumb: "thumb1" / "hand_thumb_0"
        var m = Regex.Match(rest, @"^(?:hand_)?(thumb|index|middle|ring|pinky|little)f?_?(\d)$");
        if (m.Success) {
            string finger = m.Groups[1].Value == "little" ? "pinky" : m.Groups[1].Value;
            int k = int.Parse(m.Groups[2].Value);
            bool fromZero = rest.Contains("_");          // RE2 counts 0, 1, 2; Village 1, 2, 3
            int part = fromZero ? k : k - 1;               // 0 = nearest the hand
            string what = finger == "thumb"
                ? (part <= 0 ? "base (by the wrist)" : part == 1 ? "middle joint" : "tip joint")
                : (part <= 0 ? "knuckle (base)" : part == 1 ? "middle joint" : "tip joint");
            return side + (finger == "thumb" ? "thumb" : finger + " finger") + ": " + what;
        }
        if (Regex.IsMatch(rest, @"^(hand|arm_wrist|wrist)$")) return side + "wrist (turns the whole hand)";
        if (rest == "palm") return side + "palm";
        if (Regex.IsMatch(rest, @"^(arm_)?fore_?arm$")) return side + "forearm (elbow)";
        if (Regex.IsMatch(rest, @"^(arm_)?fore_?arm_?twist")) return side + "forearm twist";
        if (Regex.IsMatch(rest, @"^(arm_)?upper_?arm$")) return side + "upper arm (shoulder joint)";
        if (Regex.IsMatch(rest, @"^(arm_)?upper_?arm_?twist")) return side + "upper arm twist";
        if (Regex.IsMatch(rest, @"^(arm_)?clavicle$|^shoulder$")) return side + "collarbone (shoulder)";
        if (Regex.IsMatch(rest, @"^(leg_)?thigh$|^upleg$")) return side + "thigh (hip joint)";
        if (Regex.IsMatch(rest, @"^(leg_)?(calf|knee|shin|leg)$")) return side + "lower leg (knee)";
        if (Regex.IsMatch(rest, @"^(leg_)?(foot|ankle)$")) return side + "foot (ankle)";
        if (Regex.IsMatch(rest, @"^(leg_)?(toe|ball)")) return side + "toes";
        if (side == "") {
            if (n == "root") return "root (the whole character)";
            if (n.Contains("null_offset") || n == "offset") return "body offset (the whole body)";
            if (n == "hip" || n == "hips" || n == "pelvis" || n == "cog") return "hips (the whole upper and lower body)";
            var sp = Regex.Match(n, @"^spine_?(\d)$");
            if (sp.Success) { int k = int.Parse(sp.Groups[1].Value); return k == 0 ? "lower back" : k == 1 ? "middle back" : "upper back (chest)"; }
            if (n.StartsWith("neck")) return "neck";
            if (n == "head") return "head";
        }
        return null;
    }

    // a row's title: the plain words first, the game's own name after
    public static string Title(string name) {
        string d = Describe(name);
        string t = d == null ? name : Capital(d) + "   (" + name + ")";
        // a held wrist (or anything above it) is set every frame, so the controller can no longer turn the hand (Tefa 2026-10-25)
        if (LocksControllerTurn(name)) t += "   ! while held, the controller cannot turn this hand";
        return t;
    }
    public static bool LocksControllerTurn(string name) {
        string d = Describe(name) ?? "";
        return d.Contains("wrist") || d.Contains("forearm") || d.Contains("upper arm") || d.Contains("collarbone");
    }
    // a finger bar's title: "Right index finger: bend all"
    public static string GroupTitle(string group) {
        string d = Describe(group + (group.EndsWith("_") ? "0" : "1"));
        if (d == null) return group + ": bend all";
        int colon = d.IndexOf(':');
        return Capital(colon > 0 ? d.Substring(0, colon) : d) + ": bend all   (" + group + ")";
    }
    static string Capital(string s) { return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1); }
}

// an entry in the "any joint" list: shows the plain words, keeps the game's name
class JointItem {
    public readonly string Name;
    public JointItem(string name) { Name = name; }
    public override string ToString() { string d = JointNames.Describe(Name); return d == null ? Name : Name + "  -  " + d; }
}
