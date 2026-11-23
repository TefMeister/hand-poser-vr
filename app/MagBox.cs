// MagBox.cs -- the magazine in the left hand (Visceral RE8 only), 2026-11-14. Tefa: "with a mag in hand, can i turn the mag in my
// hand too or just my hand around it", then "yes please i'm gonna need it, but even when i turn it, can we still make it go in the
// chamber the right way?". Six sliders for THE GUN IN HAND: move the magazine left / right, down / up, back / forward along its
// own hold, and turn, tilt and roll it there. Every change goes into reframework/data/visceral_re8/mag_hold.json, which the plugin
// (src/reload_pose.cpp held_pose) re-reads within a second. Only the held look changes: going in, the plugin still turns the
// magazine from the hand's turn to the gun's own, so it always goes in straight.
// Live while the left hand holds a magazine (spots 13 and 16-19 in zones_state.json); one set of numbers per gun.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

class MagBox : SkinPanel {
    const double STALE_SEC = 3.0;
    const int SAVE_DEBOUNCE_MS = 120;
    const double CM_PER_M = 100.0;
    const int STEPS = 10;                  // sliders count tenths of a centimetre / of a degree
    const double NUDGE_CM = 0.5;           // one controller press moves a position slider this far (an angle slider 1 degree)
    public const int OPEN_H = 40 + 3 * ROW_H + 10, SHUT_H = 34;
    const int ROW_H = 72;   // 2026-11-14: room for the bigger words above each slider
    static readonly Color Ink = Color.FromArgb(232, 232, 236), Dim = Color.FromArgb(150, 155, 170);
    // [hypothesis] the hold's own axes as named here (x right, y up, z forward); not yet checked in the game: if a slider moves
    // the magazine the other way, its Flip goes on (the file keeps its numbers)
    class Axis { public string Key, Words, Unit, L, R; public double Range; public bool Flip; public FancySlider Bar; public Label Value; public CheckBox Tick; }
    // 2026-11-23: the same box a second time for the SPARE magazine's place beside the held one (Visceral RE8's mag2.json "spare":
    // move in cm, turn in degrees, in the held magazine's own axes; one set for every gun; the game script reads the file again
    // twice a second while a spare is drawn). Until now that place could only be set in the game's own REFramework menu.
    readonly bool spare;
    Axis[] axes = {
        new Axis { Key = "z", Words = "back / forward", L = "BACK", R = "FORWARD", Unit = "cm", Range = 10 },
        new Axis { Key = "pitch", Words = "tilt down / up", L = "DOWN", R = "UP", Unit = "°", Range = 180 },
        new Axis { Key = "y", Words = "down / up", L = "DOWN", R = "UP", Unit = "cm", Range = 10 },
        new Axis { Key = "yaw", Words = "turn left / right", L = "LEFT", R = "RIGHT", Unit = "°", Range = 180 },
        new Axis { Key = "x", Words = "left / right", L = "LEFT", R = "RIGHT", Unit = "cm", Range = 10 },
        new Axis { Key = "roll", Words = "roll left / right", L = "LEFT", R = "RIGHT", Unit = "°", Range = 180 } };
    public const string TICK_PREFIX = "dock:mag-";   // "dock:" so the window treats its ticks like the docked wrist's
    readonly Button fold;
    readonly Label where;
    readonly Timer saveTimer = new Timer();
    string folder;
    int gun, loadedGun = -1;
    bool holding, open = true, syncing;
    public event Action HeightChanged;

    public MagBox(PoserUi ui, Func<string, EventHandler, int, Button> btn, bool startOpen, bool spareBox = false) {
        spare = spareBox;
        if (spare) axes = new[] {
            new Axis { Key = "z", Words = "move along Z", L = "-", R = "+", Unit = "cm", Range = 10 },
            new Axis { Key = "rx", Words = "turn about X", L = "-", R = "+", Unit = "°", Range = 180 },
            new Axis { Key = "y", Words = "move along Y", L = "-", R = "+", Unit = "cm", Range = 10 },
            new Axis { Key = "ry", Words = "turn about Y", L = "-", R = "+", Unit = "°", Range = 180 },
            new Axis { Key = "x", Words = "move along X", L = "-", R = "+", Unit = "cm", Range = 10 },
            new Axis { Key = "rz", Words = "turn about Z", L = "-", R = "+", Unit = "°", Range = 180 } };
        open = startOpen;
        Dock = DockStyle.Top; Tint = Color.FromArgb(150, 44, 36, 66); Padding = new Padding(6, 2, 6, 2);
        fold = btn("", (s, e) => SetOpen(!open), 0);
        fold.Location = new Point(6, 4);
        where = new SkinLabel { AutoSize = true, ForeColor = Dim, Font = new Font("Segoe UI Semibold", 11f) };
        Controls.Add(fold); Controls.Add(where);
        foreach (var a in axes) {
            var ax = a;
            ax.Bar = new FancySlider { Minimum = (int)(-a.Range * STEPS), Maximum = (int)(a.Range * STEPS), SmallChange = 5, LargeChange = 50, Height = 38, Signed = true, LeftText = a.L, RightText = a.R };
            ax.Bar.Scroll += (o, e) => { ShowValue(ax); if (!syncing) { saveTimer.Stop(); saveTimer.Start(); } };
            ax.Value = new SkinLabel { AutoSize = true, ForeColor = Ink };
            string key = TickPrefix + a.Key;
            ax.Tick = new SkinCheckBox { Width = 28, Height = 28, Checked = ui.IsTicked(key), Tag = key };
            ax.Tick.CheckedChanged += (o, e) => ui.SetTicked(key, ax.Tick.Checked);
            // 2026-11-14 (Tefa: "make the forward/backward, right/left headlines bigger ... the font is a bit too small and hard to read")
            Controls.Add(new SkinLabel { Text = a.Words, AutoSize = true, ForeColor = Ink, Font = new Font("Segoe UI Semibold", 12.5f), Tag = "words:" + a.Key });
            Controls.Add(ax.Tick); Controls.Add(ax.Bar); Controls.Add(ax.Value);
            ShowValue(ax);
        }
        saveTimer.Interval = SAVE_DEBOUNCE_MS; saveTimer.Tick += (s, e) => { saveTimer.Stop(); Save(); };
        Layout += (o, e) => DoLayout();
        MakeHistoryButtons(btn);
        SetOpen(open);
    }
    string HistKey { get { return loadedGun <= 0 ? null : spare ? "spare" : "mag:" + loadedGun; } }   // per gun (the spare: one for all)
    string TickPrefix { get { return spare ? "dock:spare-" : TICK_PREFIX; } }
    string Title { get { return spare ? "SPARE MAGAZINE" : "MAGAZINE IN HAND"; } }

    // ---- 2026-11-14: the box's own undo / redo (BoxHistory.cs) ---------------------------------------------------------------
    Button undoB, redoB;
    DateTime lastMark = DateTime.MinValue;
    int[] Values() { var v = new int[axes.Length]; for (int i = 0; i < axes.Length; i++) v[i] = axes[i].Bar.Value; return v; }
    void MarkNow() { if (HistKey != null) BoxHistory.Mark(HistKey, Values()); lastMark = DateTime.Now; ShowHistory(); }
    void MarkStream() { if ((DateTime.Now - lastMark).TotalMilliseconds > 400) MarkNow(); else lastMark = DateTime.Now; }   // a held button: one step
    void History(bool redo) {
        if (HistKey == null) return;
        var v = BoxHistory.Step(HistKey, Values(), redo);
        if (v == null) return;
        syncing = true;
        for (int i = 0; i < axes.Length && i < v.Length; i++) { axes[i].Bar.Value = Math.Max(axes[i].Bar.Minimum, Math.Min(axes[i].Bar.Maximum, v[i])); ShowValue(axes[i]); }
        syncing = false;
        saveTimer.Stop(); saveTimer.Start();
        ShowHistory();
    }
    void ShowHistory() {
        if (undoB == null) return;
        bool u = HistKey != null && BoxHistory.CanUndo(HistKey), r = HistKey != null && BoxHistory.CanRedo(HistKey);
        if (undoB.Enabled != u) undoB.Enabled = u;
        if (redoB.Enabled != r) redoB.Enabled = r;
    }
    void MakeHistoryButtons(Func<string, EventHandler, int, Button> btn) {
        undoB = btn("↶", (s, e) => History(false), 34); redoB = btn("↷", (s, e) => History(true), 34);
        Controls.Add(undoB); Controls.Add(redoB);
        foreach (var a in axes) a.Bar.Pressed += () => MarkNow();
        Layout += (o, e) => { redoB.Location = new Point(ClientSize.Width - redoB.Width - 8, 4); undoB.Location = new Point(redoB.Left - undoB.Width - 4, 4); };
        ShowHistory();
    }

    public bool Open { get { return open; } }

    void SetOpen(bool on) {
        open = on;
        fold.Text = Title + (open ? "  ▲" : "  ▼");
        foreach (Control c in Controls) if (c != fold && c != where) c.Visible = open;
        Height = open ? OPEN_H : SHUT_H;
        if (HeightChanged != null) HeightChanged();
    }

    void DoLayout() {   // as the docked wrist's box: where it sits on the left, how it is turned on the right
        where.Location = new Point(fold.Right + 12, 8);
        int w = ClientSize.Width, col = (w - 12) / 2;
        for (int i = 0; i < axes.Length; i++) {
            var a = axes[i];
            int x = 6 + (i % 2) * col, y = 40 + (i / 2) * ROW_H;   // 2026-11-14: the bigger words above each slider
            foreach (Control c in Controls) if ((c.Tag as string) == "words:" + a.Key) c.Location = new Point(x + 34, y);
            a.Tick.Location = new Point(x, y + 30);
            a.Value.Location = new Point(x + col - 70, y + 34);
            a.Bar.Location = new Point(x + 30, y + 32);
            a.Bar.Width = Math.Max(60, col - 30 - 76);
        }
    }

    void ShowValue(Axis a) { a.Value.Text = string.Format("{0:+0.0;-0.0;0.0} {1}", a.Bar.Value / (double)STEPS, a.Unit); }

    // the left hand holds a magazine: the plugin's spot 13 (a magazine in the hand) and 16-19 (with a spare, 2026-11-14)
    public static bool Holding(int spot) { return spot == 13 || (spot >= 16 && spot <= 19); }

    string DataDir { get { return folder == null ? null : Path.Combine(folder, "reframework", "data", "visceral_re8"); } }
    string StatePath { get { return DataDir == null ? null : Path.Combine(DataDir, "zones_state.json"); } }
    public string HoldPath { get { return DataDir == null ? null : Path.Combine(DataDir, spare ? "mag2.json" : "mag_hold.json"); } }
    // a spare is in the hand beside another magazine: spots 16 (two in the hand), 17, 18 and 19
    public static bool HoldingSpare(int spot) { return spot >= 16 && spot <= 19; }

    Dictionary<string, object> LoadAll() {
        try { if (HoldPath != null && File.Exists(HoldPath)) { var o = Json.Obj(Json.Parse(File.ReadAllText(HoldPath))); if (o != null) return o; } } catch (Exception) { }
        return new Dictionary<string, object>();
    }

    // the window's poll: which gun is in hand and whether the left hand holds a magazine; a new gun loads its numbers
    public void Poll(string gameFolder) {
        folder = gameFolder;
        bool alive = false;
        int spot = 0;
        gun = 0;
        try {
            if (StatePath != null && File.Exists(StatePath) && (DateTime.Now - File.GetLastWriteTime(StatePath)).TotalSeconds < STALE_SEC) {
                var o = Json.Obj(Json.Parse(File.ReadAllText(StatePath)));
                object v;
                if (o != null) {
                    alive = true;
                    if (o.TryGetValue("gun", out v)) gun = (int)Json.Num(v, 0);
                    if (o.TryGetValue("spot", out v)) spot = (int)Json.Num(v, 0);
                }
            }
        } catch (Exception) { return; }   // caught mid-write: the next poll gets it
        holding = alive && gun > 0 && (spare ? HoldingSpare(spot) : Holding(spot));
        where.Text = !alive ? "the game's zones script is not running" : gun <= 0 ? "no gun in hand"
                   : spare ? (holding ? "the spare beside the held magazine, every gun" : "take a spare from the pouch, the gun's magazine still in, to set it")
                   : holding ? "the magazine in the left hand, this gun" : "take a magazine in the left hand to set it";
        where.ForeColor = holding ? Ink : Dim;
        foreach (var a in axes) if (a.Bar.Enabled != (gun > 0)) a.Bar.Enabled = gun > 0;
        ShowHistory();
        if (gun > 0 && gun != loadedGun && !saveTimer.Enabled) Load(gun);
    }

    void Load(int g) {
        loadedGun = g;
        object e;
        var mine = LoadAll().TryGetValue(spare ? "spare" : g.ToString(), out e) ? Json.Obj(e) : null;
        syncing = true;
        foreach (var a in axes) {
            object v;
            double n = mine != null && mine.TryGetValue(a.Key, out v) ? Json.Num(v, 0) : 0;
            if (a.Unit == "cm" && !spare) n *= CM_PER_M;   // mag_hold.json keeps metres, mag2.json centimetres
            if (a.Flip) n = -n;
            a.Bar.Value = Math.Max(a.Bar.Minimum, Math.Min(a.Bar.Maximum, (int)Math.Round(n * STEPS)));
            ShowValue(a);
        }
        syncing = false;
    }

    // the controller buttons: ticked sliders step by n presses
    public bool Step(double n, HashSet<string> ticked) {
        if (gun <= 0) return false;
        bool any = false;
        foreach (var a in axes) {
            if (!ticked.Contains(TickPrefix + a.Key)) continue;
            if (!any) MarkStream();   // 2026-11-14: a controller step is an undo step too
            double per = a.Unit == "cm" ? NUDGE_CM : 1.0;
            a.Bar.Value = Math.Max(a.Bar.Minimum, Math.Min(a.Bar.Maximum, a.Bar.Value + (int)Math.Round(n * per * STEPS)));
            ShowValue(a);
            any = true;
        }
        if (any) { saveTimer.Stop(); saveTimer.Start(); }
        return any;
    }

    // 2026-11-23 (Remote.cs): the same numbers set from outside the window, in the box's own units (cm, degrees)
    public bool RemoteSet(Dictionary<string, object> d, out string note) {
        note = "";
        if (gun <= 0 || loadedGun != gun) { note = "magazine box: no magazine in the left hand"; return false; }
        bool any = false;
        foreach (var a in axes) {
            object v;
            if (!d.TryGetValue(a.Key, out v)) continue;
            if (!any) MarkStream();
            a.Bar.Value = Math.Max(a.Bar.Minimum, Math.Min(a.Bar.Maximum, (int)Math.Round(Json.Num(v, 0) * STEPS)));
            ShowValue(a);
            any = true;
        }
        if (any) { saveTimer.Stop(); saveTimer.Start(); } else note = "magazine box: wants x y z (cm) yaw pitch roll (degrees)";
        return any;
    }

    void Save() {
        if (HoldPath == null || gun <= 0 || loadedGun != gun) return;
        var all = LoadAll();
        var mine = new Dictionary<string, object>();
        foreach (var a in axes) {
            double n = a.Bar.Value / (double)STEPS;
            if (a.Flip) n = -n;
            mine[a.Key] = a.Unit == "cm" && !spare ? Math.Round(n / CM_PER_M, 4) : Math.Round(n, 2);
        }
        all[spare ? "spare" : gun.ToString()] = mine;
        try {
            Directory.CreateDirectory(DataDir);
            string tmp = HoldPath + ".tmp";
            File.WriteAllText(tmp, Json.Write(all));
            if (File.Exists(HoldPath)) File.Replace(tmp, HoldPath, null); else File.Move(tmp, HoldPath);
        } catch (Exception) { saveTimer.Start(); }   // the plugin was reading it: try again
    }
}
