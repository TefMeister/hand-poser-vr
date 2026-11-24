// SpotPicker.cs -- pick a numbered hand spot to tune, and SHOW it without doing it (2026-11-24, Tefa: "when hand freeze is on, to have
// these selectable - so if i select nr2, my hands automatically go on the gun in that grip zone and the pose both hands have in that
// zone ... without me having to release hand freeze"; approach A, "tune things without them being for real and once i'm finished
// tuning and try them out for real"). One button per number (no drop-down lists: Window View does not show their lists), a row per
// hand. A pick loads that spot's pose into the sliders (saves go to that spot); with SHOW ticked the mod also puts that hand there
// (show.json, Visceral RE8's showspot.cpp). Unticking SHOW, FOLLOW THE GAME, another gun or the window closing gives the real hands back.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

// one hand's spot name -> the codes the game script reads (hand_poser.lua current_spot / AREA_NAMES / HOLSTER_NAMES / right_spot)
struct SpotCode { public int Spot, Area, Holster; }
static class SpotCodes {
    static readonly Dictionary<string, int> LEFT = new Dictionary<string, int> {
        { "slide", 3 }, { "magazine", 4 }, { "handle", 5 }, { "drum", 6 }, { "insert point", 7 }, { "slide from the left", 8 },
        { "rounds from the pouch", 9 }, { "hammer", 10 }, { "slide at the back", 11 }, { "slide from the left at the back", 12 },
        { "magazine in hand", 13 }, { "two magazines in hand", 16 }, { "spare in hand, docked on the magazine", 17 },
        { "spare in hand, pulling the magazine out", 18 }, { "spare in hand, magazine still in the gun", 19 }, { "shell port", 20 },
    };
    static readonly Dictionary<string, int> RIGHT = new Dictionary<string, int> {
        { "on the cover", 1 }, { "on the bolt", 2 }, { "over the hammer", 3 }, { "on the bolt at the back", 4 },
        { "thumb on the latch", 5 }, { "the latch flicked open", 6 },
    };
    static readonly Dictionary<string, int> AREA = new Dictionary<string, int> {
        { "pouch", 1 }, { "magazine", 10 }, { "well", 11 }, { "slide", 12 }, { "front grip", 14 }, { "handle", 15 }, { "drum", 16 },
        { "insert point", 17 }, { "slide from the left", 18 },
    };
    static readonly Dictionary<string, int> HOLSTER = new Dictionary<string, int> {
        { "right shoulder holster", 1 }, { "right hip holster", 2 }, { "left hip holster", 3 }, { "left shoulder holster", 4 },
        { "first aid", 5 }, { "map", 6 },   // 2026-11-24: F and M (the holster script reports 5 / 6)
    };
    public static SpotCode For(string hand, string part) {
        var c = new SpotCode();
        string p = part ?? "";
        if (hand == "r") {
            if (p.StartsWith("right hand, ")) p = p.Substring("right hand, ".Length);
            else if (p.StartsWith("right hand ")) p = p.Substring("right hand ".Length);
            int v;
            if (RIGHT.TryGetValue(p, out v)) c.Spot = v;
            else if (p.StartsWith("ready at ") && AREA.TryGetValue(p.Substring(9), out v)) c.Area = v;
            else if (p.StartsWith("at ") && HOLSTER.TryGetValue(p.Substring(3), out v)) c.Holster = v;
            return c;
        }
        int n;
        if (p.StartsWith("ready on ")) p = p.Substring(9);   // docked on the part with LT up: the part's own code (the script adds "ready on")
        if (LEFT.TryGetValue(p, out n)) c.Spot = n;
        else if (p.StartsWith("ready at ") && AREA.TryGetValue(p.Substring(9), out n)) { c.Spot = 14; c.Area = n; }
        else if (p.StartsWith("at ") && HOLSTER.TryGetValue(p.Substring(3), out n)) c.Holster = n;
        else if (p.StartsWith("land ") && int.TryParse(FirstWord(p.Substring(5)), out n)) c.Spot = 1000 + n;
        else if (p.StartsWith("start ") && int.TryParse(FirstWord(p.Substring(6)), out n)) c.Spot = 2000 + n;
        return c;
    }
    static string FirstWord(string s) { int i = s.IndexOf(' '); return i < 0 ? s : s.Substring(0, i); }
}

class SpotPicker : SkinPanel {
    const int HEARTBEAT_MS = 1000;   // show.json's time is rewritten this often while showing; the mod lets go after 5 s without it
    const int BUTTON_H = 30;
    public event Action Changed;
    public event Action HeightChanged;   // the rows wrapped onto more lines (or fewer): the window re-sizes its top part
    const int HEAD_H = 32;     // a pick or SHOW changed: the window loads the pose of EditSpot
    readonly Func<string> dataDir, visceralDir;   // hand_poser's and Visceral RE8's data folders (null = no game)
    readonly Color ink, dim, picked, real;
    readonly FlowLayoutPanel leftRow, rightRow;
    readonly CheckBox showBox;
    readonly Timer beat = new Timer();
    string gun = "", realSpot = "", pickL, pickR, shownKey;
    int seq;

    public SpotPicker(Func<string> dataDir, Func<string> visceralDir, Color ink, Color dim, Func<string, EventHandler, int, Button> btn) {
        this.dataDir = dataDir; this.visceralDir = visceralDir; this.ink = ink; this.dim = dim;
        picked = Color.FromArgb(60, 120, 200); real = Color.FromArgb(62, 67, 86);
        Dock = DockStyle.Top; Height = 3 * 34 + 8; Tint = Color.FromArgb(60, 30, 44, 66);
        var head = new FlowLayoutPanel { Dock = DockStyle.Top, Height = HEAD_H, BackColor = Color.Transparent, WrapContents = false };
        head.Controls.Add(new SkinLabel { Text = "SPOTS:", AutoSize = true, ForeColor = dim, Font = new Font("Segoe UI Semibold", 9f), Padding = new Padding(4, 7, 4, 0) });
        showBox = new SkinCheckBox { Text = "SHOW (put the hand there without doing it)", AutoSize = true, ForeColor = ink, Padding = new Padding(4, 5, 0, 0) };
        showBox.CheckedChanged += (s, e) => { WriteShow(); Fire(); };
        head.Controls.Add(showBox);
        head.Controls.Add(btn("FOLLOW THE GAME", (s, e) => { pickL = pickR = null; showBox.Checked = false; Rebuild(); WriteShow(); Fire(); }, 0));
        leftRow = HandRow(); rightRow = HandRow();
        Controls.Add(rightRow); Controls.Add(leftRow); Controls.Add(head);   // docked Top: added bottom-up
        beat.Interval = HEARTBEAT_MS; beat.Tick += (s, e) => { if (Showing) WriteShow(); }; beat.Start();
        HandleDestroyed += (s, e) => { beat.Stop(); showBox.Checked = false; WriteShowOff(); };
    }
    FlowLayoutPanel HandRow() {
        // wraps onto a second line when a gun has many spots (the window is a fixed 820 px wide here)
        return new FlowLayoutPanel { Dock = DockStyle.Top, Height = BUTTON_H + 6, BackColor = Color.Transparent, WrapContents = true, AutoScroll = false };
    }

    bool Showing { get { return showBox.Checked && (pickL != null || pickR != null); } }

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
        if (newGun && (pickL != null || pickR != null)) { pickL = pickR = null; showBox.Checked = false; WriteShowOff(); Fire(); }
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
        Rebuild(); WriteShow(); Fire();
    }
    void Fire() { var c = Changed; if (c != null) c(); }

    // ---- show.json, for Visceral RE8 (showspot.cpp) and the game script (hand_poser.lua) ----
    public static string ShowJson(int seq, double t, string gun, string left, string right) {
        var o = new Dictionary<string, object> { { "seq", (double)seq }, { "t", t }, { "gun", Short(gun) },
                                                 { "left", Hand("l", left) }, { "right", Hand("r", right) } };
        return Json.Write(o);
    }
    static object Hand(string hand, string name) {
        if (name == null) return null;
        var c = SpotCodes.For(hand, name);
        return new Dictionary<string, object> { { "name", name }, { "spot", (double)c.Spot }, { "area", (double)c.Area }, { "holster", (double)c.Holster } };
    }
    static string Short(string gun) { gun = gun ?? ""; int k = gun.IndexOf('_'); return k < 0 ? gun : gun.Substring(0, k); }
    static double Now() { return (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds; }

    void WriteShow() {
        if (!Showing) { WriteShowOff(); return; }
        string key = gun + "|" + pickL + "|" + pickR;
        if (key != shownKey) { seq++; shownKey = key; }
        Write(ShowJson(seq, Now(), gun, pickL, pickR));
    }
    void WriteShowOff() {
        if (shownKey == null) return;   // nothing was shown: leave the file alone
        shownKey = null; seq++;
        Write(ShowJson(seq, Now(), gun, null, null));
    }
    void Write(string text) {
        string dir = visceralDir();
        if (dir == null || !Directory.Exists(dir)) return;
        try { SpotNumbers.WriteAtomic(Path.Combine(dir, "show.json"), text); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
