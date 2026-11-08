// HandAxes.cs -- which way each slider of a hand joint turns it, measured in the game, so every slider reads the same way
// (2026-11-08, Tefa: "go over all the sliders and how they move hands. left side must always be down, inwards, rotate left, bend
// left and so on, right must always be up, outwards, rotate right and so on, and some of the sliders read left and right on
// them, but actually move the hand the opposite way").
//
// Measured on Village (visceral-re8-vr tools/sim/zz_test_axes.lua + axes_hand.py, 2026-11-08, simulator, n=1 pose per hand
// [verified-numerically]): every finger joint, thumb joint and the wrist turn the same way per hand, and the two hands are
// mirror images. +X twists the joint, +Y swings it sideways, +Z bends it:
//   left hand:  +X turns RIGHT, +Y goes LEFT, +Z goes OUT (the wrist: UP)
//   right hand: +X turns LEFT,  +Y goes LEFT, +Z goes IN  (the wrist: DOWN)
// ("left / right" as seen with the hand held out palm-down; "turn" as seen from behind the hand along the finger.)
// So the slider shows the number turned round where + would be on the wrong end (Flip), and the stored number never changes
// meaning: saved poses stay as they are. Joints that are not a hand's (arms, the body) keep their plain -X / +X ends.
// One file, the same in Hand Poser and Animation & Timing (keep the two in step).
static class HandAxes {
    // the side of a hand or finger joint ("l" / "r"), or null for anything else
    static string HandSide(string joint) {
        string n = (joint ?? "").ToLowerInvariant();
        string side = n.StartsWith("l_") ? "l" : n.StartsWith("r_") ? "r" : null;
        if (side == null) return null;
        string rest = n.Substring(2);
        if (rest == "hand" || rest == "arm_wrist" || rest == "wrist") return side;
        if (System.Text.RegularExpressions.Regex.IsMatch(rest, @"^(?:hand_)?(thumb|index|middle|ring|pinky|little)f?_?\d$")) return side;
        return null;
    }
    static bool IsWrist(string joint) {
        string rest = (joint ?? "").ToLowerInvariant();
        rest = rest.Length > 2 ? rest.Substring(2) : rest;
        return rest == "hand" || rest == "arm_wrist" || rest == "wrist";
    }
    // axis 0 = X, 1 = Y, 2 = Z: the words under the slider's two ends, and whether the slider shows the number turned round
    public static bool Ends(string joint, int axis, out string left, out string right, out bool flip) {
        string side = HandSide(joint);
        string a = "XYZ"[axis].ToString();
        left = "-" + a; right = "+" + a; flip = false;
        if (side == null) return false;
        bool r = side == "r";
        if (IsThumbTip(joint) && axis == 1) { left = "IN"; right = "OUT"; flip = !r; return true; }
        if (IsThumbTip(joint) && axis == 2) { left = "LEFT"; right = "RIGHT"; flip = true; return true; }
        if (axis == 0) { left = "TURN L"; right = "TURN R"; flip = r; }
        else if (axis == 1) { left = "LEFT"; right = "RIGHT"; flip = true; }
        else if (IsWrist(joint)) { left = "DOWN"; right = "UP"; flip = r; }
        else { left = "IN"; right = "OUT"; flip = r; }
        return true;
    }
    // 2026-11-09 (Tefa, in the headset: "thumb's tip still is in the middle bottom slider, where's other finger tip curls inwards and
    // outwards et the very bottom right one"): the thumb's tip joint curls on its Y axis, not Z (Tefa's poses: L_Thumb3 = 0 / 25.8 /
    // 0), so on that joint Y and Z swap places on screen: the curl sits in the right-hand column with IN / OUT like every other
    // finger, the sideways swing in the middle. The stored numbers keep their meaning; only where and how they are shown changes.
    // (Left hand: +Y bends it in, so IN shows on the left end; the right hand mirrored.)
    static bool IsThumbTip(string joint) {
        string n = (joint ?? "").ToLowerInvariant();
        return System.Text.RegularExpressions.Regex.IsMatch(n, @"^[lr]_(?:hand_)?thumbf?_?3$");
    }
    // the screen column (0 left, 1 middle, 2 right) that shows this axis of this joint
    public static int Column(string joint, int axis) { return IsThumbTip(joint) && axis > 0 ? 3 - axis : axis; }
    public static bool Flip(string joint, int axis) { string l, r; bool f; Ends(joint, axis, out l, out r, out f); return f; }
    public static double Sign(string joint, int axis) { return Flip(joint, axis) ? -1.0 : 1.0; }
}
