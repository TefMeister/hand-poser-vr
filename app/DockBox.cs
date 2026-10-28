// DockBox.cs -- the left wrist while the hand is docked on a gun (Visceral RE8 only). Was its own window, Dock Poser, in Grip
// Zones' download until 2026-10-28 (Tefa: "add the dock poser to it instead please and delete dock poser as a separate program").
// It follows where the left hand is docked right now (the spot the zones script writes into zones_state.json: slide / pump /
// bolt, slide from the left, magazine, rounds from the pouch, grip handle, drum, insert point, a hand pair's landing spot) and
// shows six sliders for THAT gun and spot: move the hand forward / up / right, tilt, turn and roll it about the gun's own axes.
// Every change goes into reframework/data/visceral_re8/dock_poses.json; the plugin (src/dockpose.cpp) eases the hand there.
// Hidden in any game without Visceral RE8's data folder.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

class DockBox : SkinPanel {
    const double STALE_SEC = 3.0;          // zones_state.json older than this = the zones script is not running
    const int SAVE_DEBOUNCE_MS = 120;
    const double CM_PER_M = 100.0;
    const int STEPS = 10;                  // sliders count tenths of a centimetre / of a degree
    const double NUDGE_CM = 0.5;           // one controller press moves a position slider this far (and an angle slider 1 degree)
    public const int OPEN_H = 190, SHUT_H = 34;
    static readonly Color Ink = Color.FromArgb(232, 232, 236), Dim = Color.FromArgb(150, 155, 170), Panel2 = Color.FromArgb(38, 41, 52);

    class Axis { public string Key, Words, Unit; public double Range; public FancySlider Bar; public Label Value; public CheckBox Tick; }
    readonly Axis[] axes = {
        new Axis { Key = "z", Words = "forward / back", Unit = "cm", Range = 15 }, new Axis { Key = "rx", Words = "tilt fingers up / down", Unit = "°", Range = 180 },
        new Axis { Key = "y", Words = "up / down", Unit = "cm", Range = 15 },      new Axis { Key = "ry", Words = "turn left / right", Unit = "°", Range = 180 },
        new Axis { Key = "x", Words = "right / left", Unit = "cm", Range = 15 },   new Axis { Key = "rz", Words = "roll", Unit = "°", Range = 180 } };
    readonly PoserUi ui;
    readonly Button fold;
    readonly Label where;
    readonly Timer saveTimer = new Timer();
    string folder, loadedKey = "";
    int gun, spot;
    bool open = true, syncing;

    public event Action HeightChanged;

    public DockBox(PoserUi ui, Func<string, EventHandler, int, Button> btn, bool startOpen) {
        this.ui = ui;
        open = startOpen;
        Dock = DockStyle.Top; Tint = Color.FromArgb(150, 30, 44, 66); Padding = new Padding(6, 2, 6, 2);
        fold = btn("", (s, e) => SetOpen(!open), 0);
        fold.Location = new Point(6, 4);
        where = new SkinLabel { AutoSize = true, ForeColor = Dim, Font = new Font("Segoe UI Semibold", 11f) };
        Controls.Add(fold); Controls.Add(where);
        foreach (var a in axes) {
            var ax = a;
            ax.Bar = new FancySlider { Minimum = (int)(-a.Range * STEPS), Maximum = (int)(a.Range * STEPS), SmallChange = 5, LargeChange = 50, Height = 30, Signed = true };
            ax.Bar.Scroll += (o, e) => { ShowValue(ax); if (!syncing) { saveTimer.Stop(); saveTimer.Start(); } };
            ax.Value = new SkinLabel { AutoSize = true, ForeColor = Ink };
            string key = "dock:" + a.Key;
            ax.Tick = new SkinCheckBox { Width = 28, Height = 28, Checked = ui.IsTicked(key), Tag = key };
            ax.Tick.CheckedChanged += (o, e) => ui.SetTicked(key, ax.Tick.Checked);
            Controls.Add(new SkinLabel { Text = a.Words, AutoSize = true, ForeColor = Dim, Tag = "words:" + a.Key });
            Controls.Add(ax.Tick); Controls.Add(ax.Bar); Controls.Add(ax.Value);
            ShowValue(ax);
        }
        saveTimer.Interval = SAVE_DEBOUNCE_MS; saveTimer.Tick += (s, e) => { saveTimer.Stop(); Save(); };
        Layout += (o, e) => DoLayout();
        SetOpen(open);
    }

    public bool Open { get { return open; } }

    void SetOpen(bool on) {
        open = on;
        fold.Text = open ? "LEFT HAND, DOCKED  ▲" : "LEFT HAND, DOCKED  ▼";
        foreach (Control c in Controls) if (c != fold && c != where) c.Visible = open;
        Height = open ? OPEN_H : SHUT_H;
        if (HeightChanged != null) HeightChanged();
    }

    // two columns: where the hand sits on the left, how it is turned on the right; three rows each
    void DoLayout() {
        where.Location = new Point(fold.Right + 12, 8);
        int w = ClientSize.Width, col = (w - 12) / 2;
        for (int i = 0; i < axes.Length; i++) {
            var a = axes[i];
            int x = 6 + (i % 2) * col, y = 40 + (i / 2) * 48;
            foreach (Control c in Controls) if ((c.Tag as string) == "words:" + a.Key) c.Location = new Point(x + 34, y - 2);
            a.Tick.Location = new Point(x, y + 10);
            a.Value.Location = new Point(x + col - 70, y + 14);
            a.Bar.Location = new Point(x + 30, y + 12);
            a.Bar.Width = Math.Max(60, col - 30 - 76);
        }
    }

    void ShowValue(Axis a) { a.Value.Text = string.Format("{0:+0.0;-0.0;0.0} {1}", a.Bar.Value / (double)STEPS, a.Unit); }

    // the spots the plugin docks the left hand on (visceral-re8-vr src/dockpose.h)
    static bool Docked(int s) { return (s >= 3 && s <= 9) || (s >= 1000 && s < 2000); }
    static string SpotName(int s) {
        switch (s) {
        case 3: return "the slide / pump / bolt";
        case 4: return "the magazine";
        case 5: return "the grip handle";
        case 6: return "the drum";
        case 7: return "the insert point";
        case 8: return "the slide, from the left";
        case 9: return "rounds from the pouch";
        default: return s >= 1000 && s < 2000 ? "pair " + (s - 1000) + "'s landing spot" : "nothing";
        }
    }

    string DataDir { get { return folder == null ? null : Path.Combine(folder, "reframework", "data", "visceral_re8"); } }
    string StatePath { get { return DataDir == null ? null : Path.Combine(DataDir, "zones_state.json"); } }
    string PosesPath { get { return DataDir == null ? null : Path.Combine(DataDir, "dock_poses.json"); } }

    // true when this game has Visceral RE8 in it (the window shows the box only then)
    public static bool HasVisceral(string gameFolder) {
        try { return gameFolder != null && Directory.Exists(Path.Combine(gameFolder, "reframework", "data", "visceral_re8")); } catch (Exception) { return false; }
    }

    Dictionary<string, object> LoadPoses() {
        try { if (PosesPath != null && File.Exists(PosesPath)) { var o = Json.Obj(Json.Parse(File.ReadAllText(PosesPath))); if (o != null) return o; } } catch (Exception) { }
        return new Dictionary<string, object>();
    }
    static Dictionary<string, object> Child(Dictionary<string, object> d, string k) {
        object v;
        if (d.TryGetValue(k, out v) && v is Dictionary<string, object>) return (Dictionary<string, object>)v;
        var c = new Dictionary<string, object>();
        d[k] = c;
        return c;
    }

    // called by the window's poll; reads where the left hand is and loads that gun and spot's numbers when it changes
    public void Poll(string gameFolder) {
        folder = gameFolder;
        bool alive = false;
        gun = 0; spot = 0;
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
        bool docked = alive && gun > 0 && Docked(spot);
        where.Text = !alive ? "the game's zones script is not running" : docked ? "on " + SpotName(spot) : "not docked: dock it (LG) to pose its wrist";
        where.ForeColor = docked ? Ink : Dim;
        foreach (var a in axes) if (a.Bar.Enabled != docked) a.Bar.Enabled = docked;
        string key = gun + ":" + spot;
        if (docked && key != loadedKey && !saveTimer.Enabled) {   // a new spot: its own numbers
            loadedKey = key;
            var mine = Child(Child(LoadPoses(), gun.ToString()), spot.ToString());
            syncing = true;
            foreach (var a in axes) {
                object v;
                double n = mine.TryGetValue(a.Key, out v) ? Json.Num(v, 0) : 0;
                if (a.Unit == "cm") n *= CM_PER_M;
                a.Bar.Value = (int)Math.Round(n * STEPS);
                ShowValue(a);
            }
            syncing = false;
        }
    }

    // the controller buttons: ticked sliders step by n presses (positions half a centimetre each, angles a degree)
    public bool Step(double n, HashSet<string> ticked) {
        if (!Docked(spot) || gun <= 0) return false;
        bool any = false;
        foreach (var a in axes) {
            if (!ticked.Contains("dock:" + a.Key)) continue;
            double per = a.Unit == "cm" ? NUDGE_CM : 1.0;
            a.Bar.Value += (int)Math.Round(n * per * STEPS);
            ShowValue(a);
            any = true;
        }
        if (any) { saveTimer.Stop(); saveTimer.Start(); }
        return any;
    }

    void Save() {
        if (PosesPath == null || !Docked(spot) || gun <= 0 || loadedKey != gun + ":" + spot) return;
        var all = LoadPoses();
        var mine = Child(Child(all, gun.ToString()), spot.ToString());
        foreach (var a in axes) {
            double n = a.Bar.Value / (double)STEPS;
            mine[a.Key] = a.Unit == "cm" ? Math.Round(n / CM_PER_M, 4) : Math.Round(n, 2);
        }
        try { Directory.CreateDirectory(DataDir); File.WriteAllText(PosesPath, Json.Write(all)); }
        catch (Exception) { saveTimer.Start(); }   // the plugin was reading it: try again
    }
}
