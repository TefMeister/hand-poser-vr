// SpotPicker.cs -- pick a numbered hand spot to tune (2026-11-24, Tefa: numbered spots, "when hand freeze is on, to have these
// selectable"). One button per number (no drop-down lists: Window View does not show their lists), a row per hand. A pick loads
// that spot's pose into the sliders (saves go to that spot); FOLLOW THE GAME, a second click on the picked button or another gun
// gives the window back to where the hands really are.
// 2026-11-24 evening (Tefa, after trying it: "the jump to hand pose isn't working as i imagined it to, so instead of going forward
// with it please scrap this, but leave the layout, it's good"): SHOW (the hand put on the picked spot by the mod, show.json) is
// gone, here, in the game script and in Visceral RE8; the buttons and the picking stay.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

class SpotPicker : SkinPanel {
    const int BUTTON_H = 30;
    const int HEAD_H = 32;
    public event Action Changed;         // a pick changed: the window loads the pose of EditSpot
    public event Action HeightChanged;   // the rows wrapped onto more lines (or fewer): the window re-sizes its top part
    readonly Func<string> dataDir;       // hand_poser's data folder (null = no game)
    readonly Color ink, dim, picked, real;
    readonly FlowLayoutPanel leftRow, rightRow;
    public readonly FlowLayoutPanel Head;   // 2026-11-25: the window puts the section tick in it (NudgeNav.cs)
    string gun = "", realSpot = "", pickL, pickR;

    public SpotPicker(Func<string> dataDir, Color ink, Color dim, Func<string, EventHandler, int, Button> btn) {
        this.dataDir = dataDir; this.ink = ink; this.dim = dim;
        picked = Color.FromArgb(60, 120, 200); real = Color.FromArgb(62, 67, 86);
        Dock = DockStyle.Top; Height = 3 * 34 + 8; Tint = Color.FromArgb(60, 30, 44, 66);
        var head = Head = new FlowLayoutPanel { Dock = DockStyle.Top, Height = HEAD_H, BackColor = Color.Transparent, WrapContents = false };
        head.Controls.Add(new SkinLabel { Text = "SPOTS:", AutoSize = true, ForeColor = dim, Font = new Font("Segoe UI Semibold", 9f), Padding = new Padding(4, 7, 4, 0) });
        head.Controls.Add(btn("FOLLOW THE GAME", (s, e) => { pickL = pickR = null; Rebuild(); Fire(); }, 0));
        leftRow = HandRow(); rightRow = HandRow();
        Controls.Add(rightRow); Controls.Add(leftRow); Controls.Add(head);   // docked Top: added bottom-up
    }
    FlowLayoutPanel HandRow() {
        // wraps onto a second line when a gun has many spots (the window is a fixed 820 px wide here)
        return new FlowLayoutPanel { Dock = DockStyle.Top, Height = BUTTON_H + 6, BackColor = Color.Transparent, WrapContents = true, AutoScroll = false };
    }

    // the combined spot whose pose the window edits: a picked hand's spot joined to the other hand's (picked or real); null = the game's
    public string EditSpot {
        get {
            if (pickL == null && pickR == null) return null;
            string l = pickL ?? SpotNumbers.LeftPart(realSpot), r = pickR ?? SpotNumbers.RightPart(realSpot);
            return l == "" ? r : r == "" ? l : l + " + " + r;
        }
    }

    // every poll: the gun and the real spot; a new gun drops the picks (they belong to the old gun)
    public void Refresh(string gunNow, string spotNow) {
        gunNow = gunNow ?? ""; spotNow = spotNow ?? "";
        bool newGun = gunNow != gun;
        if (newGun && (pickL != null || pickR != null)) { pickL = pickR = null; Fire(); }
        if (newGun || spotNow != realSpot || leftRow.Controls.Count == 0) { gun = gunNow; realSpot = spotNow; Rebuild(); }
    }

    void Rebuild() {
        Fill(leftRow, "l", "LEFT", pickL, SpotNumbers.LeftPart(realSpot));
        Fill(rightRow, "r", "RIGHT", pickR, SpotNumbers.RightPart(realSpot));
    }
    void Fill(FlowLayoutPanel row, string hand, string label, string pick, string realPart) {
        row.SuspendLayout();
        foreach (Control c in row.Controls) c.Dispose();
        row.Controls.Clear();
        row.Controls.Add(new SkinLabel { Text = label, Width = 52, ForeColor = dim, Font = new Font("Segoe UI Semibold", 9f), Padding = new Padding(2, 8, 0, 0) });
        string dir = dataDir();
        if (dir != null && gun != "") {
            foreach (var kv in SafeList(hand, dir)) {
                string part = kv.Value;
                var b = new SkinButton { Text = kv.Key + " " + Words(hand, part), AutoSize = true, Height = BUTTON_H, FlatStyle = FlatStyle.Flat, ForeColor = ink, Margin = new Padding(2), BackColor = part == pick ? picked : real };
                b.FlatAppearance.BorderSize = part == realPart ? 2 : 0;   // the hand's real spot: outlined
                b.FlatAppearance.BorderColor = Color.FromArgb(120, 200, 120);
                b.Click += (s, e) => Pick(hand, part);
                row.Controls.Add(b);
            }
        }
        row.ResumeLayout();
        int h = Math.Max(BUTTON_H + 6, row.GetPreferredSize(new Size(Math.Max(200, row.Width), 0)).Height);
        if (row.Height != h) row.Height = h;
        int total = HEAD_H + leftRow.Height + rightRow.Height + 6;
        if (Height != total) { Height = total; var c = HeightChanged; if (c != null) c(); }
    }
    List<KeyValuePair<string, string>> SafeList(string hand, string dir) {
        try { return SpotNumbers.List(gun, hand, dir); } catch (IOException) { return new List<KeyValuePair<string, string>>(); }
    }
    static string Words(string hand, string part) {
        string p = part.StartsWith("right hand, ") ? part.Substring(12) : part.StartsWith("right hand ") ? part.Substring(11) : part;
        if (p.StartsWith("at ")) p = p.Substring(3);
        if (p.EndsWith(" holster")) p = p.Substring(0, p.Length - 8);   // "H2 right hip"
        if (p == "ready at pouch") p = "pouch";
        return p;
    }

    void Pick(string hand, string part) {   // a second click on the same button lets that hand follow the game again
        if (hand == "r") pickR = pickR == part ? null : part; else pickL = pickL == part ? null : part;
        Rebuild(); Fire();
    }
    void Fire() { var c = Changed; if (c != null) c(); }
}
