// PoserUi.cs -- the Hand Poser window. Top: game and character, the game folder and script buttons, the quick-add
// buttons (LEFT HAND, RIGHT HAND: the wrist and the five fingers), ANY JOINT ▼ opening a filter box with the whole skeleton
// to add any one joint, and in Visceral RE8 the docked left wrist (DockBox.cs, once the Dock Poser window). Middle: one row per held joint with X / Y / Z sliders in degrees, RESET and a remove cross; while a slider is
// dragged, the rows of the joints it carries along light up amber. Bottom: UNDO, REDO, the pose-build name with SAVE
// BUILD, the saved builds with LOAD and DELETE. Every change goes to the game at once and into the autosave.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

partial class PoserUi : Form {
    const int POLL_MS = 100, COMMAND_DEBOUNCE_MS = 30, FIND_GAME_EVERY_MS = 5000;   // 2026-10-25: 100 ms, for the live "from the hand" numbers
    const int SLIDER_STEPS_PER_DEG = 10;   // sliders count tenths of a degree
    static readonly Color Back = Color.FromArgb(28, 30, 38), Panel2 = Color.FromArgb(38, 41, 52), Ink = Color.FromArgb(232, 232, 236);
    static readonly Color Dim = Color.FromArgb(150, 155, 170), Accent = Color.FromArgb(70, 120, 200), Lit = Color.FromArgb(96, 78, 30);
    static readonly Color Good = Color.FromArgb(80, 190, 110), Bad = Color.FromArgb(220, 110, 90);

    readonly Settings settings = new Settings();
    readonly GameLink link = new GameLink();
    readonly PoseModel model = new PoseModel(Program.AppData);
    readonly System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer(), sendTimer = new System.Windows.Forms.Timer();
    // 2026-10-25: the controller buttons step the ticked sliders (Nudge.cs)
    readonly System.Windows.Forms.Timer nudgeTimer = new System.Windows.Forms.Timer();
    readonly Nudge nudge = new Nudge();
    readonly HashSet<string> ticked = new HashSet<string>();   // "joint:X" for a joint's axis, "group:<name>" for a finger bar
    bool nudgeSaving;
    Label nudgeNote;
    readonly bool demo;
    int demoStep;
    DateTime lastFind = DateTime.MinValue, lastStateWrite = DateTime.MinValue;
    bool autosaveLoaded, syncing;

    Label status, scriptNote, whereLine;   // whereLine: where the shown hand is, big (2026-11-09)
    Button scriptButton;
    TextBox filter, buildName;
    ListBox allJoints;
    ComboBox builds;
    SkinPanel rowsPanel;
    readonly List<JointRow> rows = new List<JointRow>();

    public PoserUi(string gameArg, bool demo) {
        this.demo = demo;
        Text = "Hand Poser";
        BackColor = Back; ForeColor = Ink;
        Font = new Font("Segoe UI", 9f);
        ClientSize = new Size(1720, 1180);   // 2026-11-14 (Tefa: "widen ... when they get too tall, just make them wider"): the top in a left column
        MinimumSize = new Size(600, 600);
        StartPosition = FormStartPosition.CenterScreen;
        string ico = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hand_poser.ico");
        try { Icon = new Icon(ico); } catch (Exception) { }
        DoubleBuffered = true;
        Backdrop.Init(this, ico, Back);   // the icon, pixelated and faint, behind the whole window (Tefa 2026-10-25)

        BuildTop();
        BuildBottom();
        rowsPanel = new SkinPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(6, 4, 6, 4) };
        Controls.Add(rowsPanel);
        rowsPanel.BringToFront();

        string folder = gameArg ?? settings.Get("game", null);
        if (!GameLink.LooksLikeGame(folder) && gameArg == null) folder = GameLink.FindRunningGame();   // 2026-11-14: a --game folder is never swapped for the running game (tests)
        if (GameLink.LooksLikeGame(folder)) UseFolder(folder, gameArg == null);

        poll.Interval = POLL_MS; poll.Tick += (s, e) => Poll(); poll.Start();
        nudgeTimer.Interval = 15; nudgeTimer.Tick += (s, e) => NudgeTick(); nudgeTimer.Start();
        sendTimer.Interval = COMMAND_DEBOUNCE_MS; sendTimer.Tick += (s, e) => { sendTimer.Stop(); Send(); };
        UpdateStatus();
        ShowNudge();
    }

    protected override void OnPaintBackground(PaintEventArgs e) { Backdrop.PaintUnder(this, e.Graphics, e.ClipRectangle); }
    protected override CreateParams CreateParams { get { return Backdrop.Composited(base.CreateParams); } }

    // ---- building the window --------------------------------------------------------------------------------------------
    Button Btn(string text, EventHandler onClick, int width = 0) {
        var b = new SkinButton { Text = text, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(62, 67, 86), ForeColor = Ink, Height = 26, AutoSize = width == 0, Margin = new Padding(3) };
        b.FlatAppearance.BorderSize = 0;
        if (width > 0) b.Width = width;
        b.Click += onClick;
        return b;
    }

    FlowLayoutPanel Row(int height) {
        return new FlowLayoutPanel { Dock = DockStyle.Top, Height = height, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Color.Transparent, Padding = new Padding(4, 2, 4, 0) };
    }

    void BuildTop() {
        var top = new SkinPanel { Dock = DockStyle.Left, Width = 820, AutoScroll = true, Tint = Color.FromArgb(120, Back) };
        Controls.Add(top);

        status = new SkinLabel { Dock = DockStyle.Top, Height = 24, Padding = new Padding(8, 4, 0, 0), ForeColor = Dim };
        whereLine = HandSpots.MakeLine(Ink);
        var gameRow = Row(32);
        gameRow.Controls.Add(Btn("GAME FOLDER...", (s, e) => PickFolder()));
        scriptButton = Btn("PUT THE SCRIPT INTO THE GAME", (s, e) => InstallScript());
        gameRow.Controls.Add(scriptButton);
        scriptNote = new SkinLabel { AutoSize = true, ForeColor = Dim, Padding = new Padding(4, 6, 0, 0) };
        gameRow.Controls.Add(scriptNote);
        // 2026-10-25: one pose per gun, swapped by itself when another gun is drawn
        var gunRow = Row(28);
        perGun = settings.Get("per_gun", "1") == "1";
        perGunBox = new SkinCheckBox { Text = "EACH GUN KEEPS ITS OWN POSE (swapped when you draw another gun)", AutoSize = true, Checked = perGun, ForeColor = Ink, Padding = new Padding(4, 4, 0, 0) };
        perGunBox.CheckedChanged += (s, e) => {
            perGun = perGunBox.Checked; settings.Set("per_gun", perGun ? "1" : "0");
            if (!perGun) { model.GunPoseFile = null; poseGun = null; } else GunCheck();
            UpdateStatus();
        };
        gunRow.Controls.Add(perGunBox);
        // 2026-10-26 (Tefa: "automatic saving for hand poses on different land spots and different start grab spots")
        perSpot = settings.Get("per_spot", "1") == "1";
        perSpotBox = new SkinCheckBox { Text = "AND EACH HAND SPOT (start sphere, landing spot, slide, magazine, a free hand's area, a holster, the guard)", AutoSize = true, Checked = perSpot, ForeColor = Ink, Padding = new Padding(4, 4, 0, 0) };
        perSpotBox.CheckedChanged += (s, e) => { perSpot = perSpotBox.Checked; settings.Set("per_spot", perSpot ? "1" : "0"); GunCheck(); };
        gunRow.Controls.Add(perSpotBox);

        var quick = Row(32);
        // 2026-10-28 (Tefa: "change these to left hand and right hand, so it has both the wrist and fingers"): one button per hand
        // adds its wrist and its five fingers; the arrow beside it jumps to a finger. A held wrist is turned the same way every
        // frame, so the controller no longer turns that hand (2026-10-25); RESET or ✕ on the wrist row gives it back
        // 2026-11-01 (Tefa: "right hand options get added to the bottom of the list, and i started twisting the sliders wondering
        // why nothing was moving"): the list jumps to the new hand's thumb, so its rows are the ones in view
        // 2026-11-02 (Tefa: "make the hand poser app only display left or the right hand sliders, based on what button is pressed
        // LEFT HAND or RIGHT HAND, not both in one window"): the button picks the hand whose rows are SHOWN; the other hand's
        // joints stay held and saved, only out of view (other joints, a spine say, always show)
        shownSide = settings.Get("shown_hand", "");
        quick.Controls.Add(Btn("LEFT HAND", (s, e) => { ShowSide("l"); QuickAdd(n => IsHand(n, "l"), "left hand"); RebuildRows(); JumpTo("LEFT THUMB", "LEFT"); }));
        quick.Controls.Add(Btn("▼", (s, e) => JumpMenu((Control)s, "LEFT"), 34));   // 2026-10-27: jump to a finger
        quick.Controls.Add(Btn("RIGHT HAND", (s, e) => { ShowSide("r"); QuickAdd(n => IsHand(n, "r"), "right hand"); RebuildRows(); JumpTo("RIGHT THUMB", "RIGHT"); }));
        quick.Controls.Add(Btn("▼", (s, e) => JumpMenu((Control)s, "RIGHT"), 34));
        // (2026-10-27, Tefa: "please remove clear all from it")
        // 2026-10-28 (Tefa: "remove that part from the window ... hide it behind a dropdown button"): the whole skeleton list
        jointsButton = Btn("ANY JOINT  ▼", (s, e) => ShowJointList(!jointsOpen));
        quick.Controls.Add(jointsButton);

        filterRow = Row(30);
        filterRow.Controls.Add(new SkinLabel { Text = "any joint:", AutoSize = true, ForeColor = Dim, Padding = new Padding(0, 6, 0, 0) });
        filter = new TextBox { Width = 200, BackColor = Panel2, ForeColor = Ink, BorderStyle = BorderStyle.FixedSingle };
        filter.TextChanged += (s, e) => FillJointList();
        filterRow.Controls.Add(filter);
        filterRow.Controls.Add(Btn("ADD", (s, e) => AddSelected()));
        allJoints = new ListBox { Dock = DockStyle.Top, Height = 84, BackColor = Panel2, ForeColor = Ink, BorderStyle = BorderStyle.None, IntegralHeight = false };
        allJoints.DoubleClick += (s, e) => AddSelected();
        listHolder = new Panel { Dock = DockStyle.Top, Height = 88, Padding = new Padding(8, 0, 8, 4), BackColor = Color.Transparent };
        listHolder.Controls.Add(allJoints);

        // 2026-10-27 (Tefa: "add a clear all tick marks button"): beside the line about the ticked sliders
        var nudgeRow = Row(34);
        nudgeRow.Controls.Add(Btn("CLEAR ALL TICKS", (s, e) => ClearTicks()));
        nudgeNote = new SkinLabel { AutoSize = true, Padding = new Padding(4, 6, 0, 0), ForeColor = Dim };
        nudgeRow.Controls.Add(nudgeNote);
        // 2026-10-28: the docked left wrist (Visceral RE8), where the joint list was; folds on its own header
        dockBox = new DockBox(this, (t, h, w) => Btn(t, h, w), settings.Get("dock_open", "1") == "1") { Visible = false };
        nudge.Section(dockBox, "dock");
        dockBox.HeightChanged += () => { settings.Set("dock_open", dockBox.Open ? "1" : "0"); SizeTop(); };
        magBox = new MagBox(this, (t, h, w) => Btn(t, h, w), settings.Get("mag_open", "1") == "1") { Visible = false };   // 2026-11-14 (MagBox.cs)
        nudge.Section(magBox, "mag"); magBox.HeightChanged += () => { settings.Set("mag_open", magBox.Open ? "1" : "0"); SizeTop(); };
        // docked controls stack bottom-up in the order added, so add in reverse
        top.Controls.Add(nudgeRow);
        top.Controls.Add(magBox);
        top.Controls.Add(dockBox);
        top.Controls.Add(listHolder);
        top.Controls.Add(filterRow);
        top.Controls.Add(quick);
        top.Controls.Add(gunRow);
        top.Controls.Add(gameRow);
        top.Controls.Add(status);
        top.Controls.Add(whereLine);   // added last: the topmost line
        topPanel = top;
        ShowJointList(false);
    }

    SkinPanel topPanel;
    FlowLayoutPanel filterRow;
    Panel listHolder;
    Button jointsButton;
    DockBox dockBox; MagBox magBox;
    bool jointsOpen, dockShown;   // kept here: a control's Visible reads false until the window itself is shown
    void ShowJointList(bool on) {
        jointsOpen = on;
        filterRow.Visible = listHolder.Visible = on;
        jointsButton.Text = on ? "ANY JOINT  ▲" : "ANY JOINT  ▼";
        SizeTop();
    }
    // the top grows and shrinks with what is open in it
    void SizeTop() {
        if (topPanel == null) return;
        int h = topPanel.Padding.Vertical + 8;
        foreach (Control c in topPanel.Controls) {
            if ((c == filterRow || c == listHolder) && !jointsOpen) continue;
            if ((c == dockBox || c == magBox) && !dockShown) continue;
            h += c.Height;
        }
        topPanel.Height = h;
    }

    void BuildBottom() {
        var bottom = new SkinPanel { Dock = DockStyle.Bottom, Height = 70, Tint = Color.FromArgb(120, Back) };
        Controls.Add(bottom);
        var one = Row(32);
        // 2026-11-14 (Tefa: "undo and redo needs to be under each row"): the window's UNDO / REDO are gone; each joint row has
        // its own ↶ / ↷ for the pose it is in (RowHistory.cs)
        one.Controls.Add(new SkinLabel { Text = "   build name:", AutoSize = true, ForeColor = Dim, Padding = new Padding(0, 6, 0, 0) });
        buildName = new TextBox { Width = 170, BackColor = Panel2, ForeColor = Ink, BorderStyle = BorderStyle.FixedSingle };
        one.Controls.Add(buildName);
        one.Controls.Add(Btn("SAVE BUILD", (s, e) => SaveBuild()));
        var two = Row(32);
        two.Controls.Add(new SkinLabel { Text = "saved builds:", AutoSize = true, ForeColor = Dim, Padding = new Padding(0, 6, 0, 0) });
        builds = new ComboBox { Width = 220, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Panel2, ForeColor = Ink, FlatStyle = FlatStyle.Flat };
        two.Controls.Add(builds);
        two.Controls.Add(Btn("LOAD", (s, e) => { if (builds.SelectedItem != null) Change(() => model.LoadBuild((string)builds.SelectedItem)); }));
        two.Controls.Add(Btn("DELETE", (s, e) => DeleteBuild()));
        bottom.Controls.Add(two);
        bottom.Controls.Add(one);
    }

    // ---- the controller nudge (Nudge.cs) -----------------------------------------------------------------------------------
    internal bool IsTicked(string key) { return ticked.Contains(key); }
    internal void SetTicked(string key, bool on) { if (on) ticked.Add(key); else ticked.Remove(key); ShowNudge(); }
    // every tick box carries its key in Tag: unticking them all through the boxes keeps the boxes and the set in step
    void ClearTicks() {
        var stack = new Stack<Control>();
        stack.Push(this);
        while (stack.Count > 0) {
            var c = stack.Pop();
            var cb = c as CheckBox;
            if (cb != null && cb.Tag is string && cb.Checked) cb.Checked = false;
            foreach (Control k in c.Controls) stack.Push(k);
        }
        ticked.Clear();
        ShowNudge();
    }
    void ShowNudge() {
        if (nudgeNote == null) return;
        nudgeNote.Text = ticked.Count == 0 ? "tick sliders to step them with the controller buttons: X -1, Y -5, A +1, B +5, a held grip repeats"
            : !nudge.Linked ? ticked.Count + " ticked: start Window View and click the left stick in to step them with the buttons"
            : ticked.Count + " ticked: X -1, Y -5, A +1, B +5 (left grip repeats X / Y, right grip A / B), in Window View's controller mode";
        nudgeNote.ForeColor = ticked.Count > 0 && nudge.Linked ? Good : Dim;
    }
    void NudgeTick() {
        bool started;
        double step = nudge.Poll(ticked.Count > 0, out started);
        if (step == 0) {
            if (nudgeSaving && !started) { nudgeSaving = false; try { model.Autosave(); } catch (IOException) { } }
            return;
        }
        var use = nudge.Filter(ticked);   // 2026-11-08: only the part clicked last (Nudge.cs), when it has ticks
        if (dockShown) { dockBox.Step(step, use); magBox.Step(step, use); }   // the docked wrist's and the magazine's ticked sliders
        if (use.All(k => k.StartsWith("dock:"))) return;
        if (started) model.Mark();   // one undo step per press (its repeats included)
        bool markRows = started;     // 2026-11-14: and one step on each row it moves (RowHistory.cs), below
        nudgeSaving = true;
        // every slider to step, once each, whether ticked on its own or through its finger's bar
        var targets = new HashSet<string>();
        foreach (var r in model.Rows) foreach (var a in new[] { "X", "Y", "Z" }) if (use.Contains(r.Name + ":" + a)) targets.Add(r.Name + ":" + a);
        foreach (var g in model.Groups()) {
            if (!use.Contains("group:" + g.Key)) continue;
            string axis = GroupAxis(g.Key);
            foreach (var r in g.Value) targets.Add(r.Name + ":" + axis);
        }
        foreach (var r in model.Rows) {
            if (!r.Known) continue;
            if (markRows && (targets.Contains(r.Name + ":X") || targets.Contains(r.Name + ":Y") || targets.Contains(r.Name + ":Z"))) RowHistory.Mark(poseGun, r);
            // X / Y move toward the slider's left end, A / B toward its right end (HandAxes.cs, 2026-11-08)
            if (targets.Contains(r.Name + ":X")) r.X = PoseModel.Clamp(r.X + step * HandAxes.Sign(r.Name, 0));
            if (targets.Contains(r.Name + ":Y")) r.Y = PoseModel.Clamp(r.Y + step * HandAxes.Sign(r.Name, 1));
            if (targets.Contains(r.Name + ":Z")) r.Z = PoseModel.Clamp(r.Z + step * HandAxes.Sign(r.Name, 2));
            if (targets.Contains(r.Name + ":X") || targets.Contains(r.Name + ":Y") || targets.Contains(r.Name + ":Z")) r.Moved = true;
        }
        foreach (var row in rows) row.PullFromModel();
        RequestSend();
        UpdateStatus();
    }

    // ---- the game folder and the script ---------------------------------------------------------------------------------
    void UseFolder(string folder, bool remember) {
        try { link.Use(folder); } catch (Exception ex) { status.Text = "cannot use that folder: " + ex.Message; return; }
        if (remember) settings.Set("game", folder);
        UpdateStatus();
    }

    void PickFolder() {
        using (var dlg = new FolderBrowserDialog { Description = "The game's install folder (the one with the reframework folder in it)", ShowNewFolderButton = false }) {
            if (link.Folder != null) dlg.SelectedPath = link.Folder;
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            if (!GameLink.LooksLikeGame(dlg.SelectedPath)) { MessageBox.Show(this, "No reframework folder in there. Install REFramework into the game first.", "Hand Poser"); return; }
            UseFolder(dlg.SelectedPath, true);
        }
    }

    void InstallScript() {
        try { link.InstallScript(); status.Text = "script copied; restart the game or reset scripts in REFramework"; }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Hand Poser"); }
        UpdateStatus();
    }

    // ---- talking to the game --------------------------------------------------------------------------------------------
    void Poll() {
        if (link.Folder == null) {
            if ((DateTime.Now - lastFind).TotalMilliseconds > FIND_GAME_EVERY_MS && !Environment.CommandLine.Contains("--game")) {   // 2026-11-14: not in a --game run (tests)
                lastFind = DateTime.Now;
                string f = GameLink.FindRunningGame();
                if (f != null) UseFolder(f, true);
            }
            return;
        }
        DateTime wrote;
        try { wrote = File.Exists(link.StateFile) ? File.GetLastWriteTime(link.StateFile) : DateTime.MinValue; } catch (IOException) { return; }
        if (wrote != lastStateWrite) {
            lastStateWrite = wrote;
            string text = link.ReadState();
            if (text != null) {
                bool skeletonChanged = false;
                try { skeletonChanged = model.ReadState(text); } catch (FormatException) { }   // half-written: next time
                if (skeletonChanged) OnSkeleton();
                foreach (var r in rows) r.PullFromModel();
                GunCheck();
            }
        }
        bool village = DockBox.HasVisceral(link.Folder);
        if (dockShown != village) { dockShown = dockBox.Visible = magBox.Visible = village; SizeTop(); }
        if (village) { dockBox.Side = shownSide == "r" ? "r" : "l"; dockBox.Poll(link.Folder); magBox.Poll(link.Folder); }
        UpdateStatus();
        if (demo) Demo();
    }

    // ---- one pose per gun -------------------------------------------------------------------------------------------------
    bool perGun;
    CheckBox perGunBox;
    string poseGun;   // the gun (and spot) whose pose is in the window (null = not decided yet)
    bool perSpot;
    CheckBox perSpotBox;
    void GunCheck() {
        if (!perGun || !autosaveLoaded || !model.Connected || model.Game == "") return;
        if (model.MirrorDir == null && link.DataDir != null) { model.MirrorDir = Path.Combine(link.DataDir, "guns"); model.MirrorAll(); }
        string g = model.Gun ?? "";
        if (perSpot && !string.IsNullOrEmpty(model.Spot)) g += " @ " + model.Spot;   // a spot keeps its own pose too
        if (poseGun == g) { model.GunPoseFile = model.GunFile(g); return; }
        string file = model.GunFile(g);
        // 2026-11-09 (Tefa: at the end of the slide rack the fingers open): a pose file with no joints in it (a spot made but never
        // posed, "slide from the left at the back") loaded as a pose of nothing and let the fingers go. It counts as never posed now
        if (File.Exists(file) && !HasJoints(file)) { }
        else if (File.Exists(file)) {   // this gun has its own pose: it comes back (the previous gun's is already saved)
            model.Mark();
            if (model.LoadFile(file)) { if (g != (model.Gun ?? "")) KeepOtherHand(model.GunFile(model.Gun ?? "")); poseGun = g; model.GunPoseFile = file; AfterModelChange(); return; }
        }
        if (perSpot) foreach (var fb in HandSpots.Fallbacks(model.Gun, model.Spot)) {   // 2026-11-09: start from what the game shows there
            string ff = model.GunFile(fb);
            if (File.Exists(ff) && HasJoints(ff) && model.LoadFile(ff)) { KeepOtherHand(model.GunFile(model.Gun ?? "")); poseGun = g; model.GunPoseFile = file; try { model.Autosave(); } catch (IOException) { } AfterModelChange(); return; }
        }
        poseGun = g;               // a gun never posed: it starts from the pose in hand, kept from now on as its own
        model.GunPoseFile = file;
        try { model.Autosave(); } catch (IOException) { }
        UpdateStatus();
    }
    // 2026-11-09 (Tefa: "when i put my hand on the grip ... right hand goes into a relaxed pose again"): a spot's pose (the
    // magazine, the slide: the left hand's) held only that hand, so the right hand's grip from the gun's own pose was dropped. The
    // hand NOT at the spot now keeps the gun's own pose: its joints are taken from the gun's file (the game script does the same)
    void KeepOtherHand(string gunFile) {
        string sp = model.Spot ?? "";
        bool ownR = sp.Contains("right hand") || sp.Contains("trigger pulled") || sp == HandSpots.GUARD;   // the guard is both hands
        bool ownL = sp != "" && !sp.StartsWith("right hand") && !sp.StartsWith("trigger pulled");
        try {
            if (!File.Exists(gunFile)) return;
            var j = Json.Obj(Json.Obj(Json.Parse(File.ReadAllText(gunFile)))["joints"]);
            if (j == null) return;
            foreach (var kv in j) {
                string n = kv.Key.ToLowerInvariant();
                bool isL = n.StartsWith("l_"), isR = n.StartsWith("r_");
                if (!((isL && !ownL) || (isR && !ownR))) continue;
                var a = kv.Value as List<object>;
                if (a == null || a.Count != 3) continue;
                var r = model.Find(kv.Key);
                if (r == null) { r = new Row { Name = kv.Key }; model.Rows.Add(r); }
                r.X = Convert.ToDouble(a[0]); r.Y = Convert.ToDouble(a[1]); r.Z = Convert.ToDouble(a[2]);
                r.Known = true; r.Moved = true;
            }
        } catch (Exception) { }
    }
    static bool HasJoints(string file) {
        try {
            var o = Json.Obj(Json.Parse(File.ReadAllText(file)));
            object j;
            return o != null && o.TryGetValue("joints", out j) && Json.Obj(j) != null && Json.Obj(j).Count > 0;
        } catch (Exception) { return true; }   // unreadable: let LoadFile say so, as before
    }
    static string GunWords(string g) { return g == "" ? "no gun" : g.Replace("_Inventory", ""); }

    void OnSkeleton() {
        FillJointList();
        if (!autosaveLoaded && model.Game != "") {
            autosaveLoaded = true;
            if (model.Rows.Count == 0 && model.LoadAutosave()) AfterModelChange();
            FillBuilds();
        }
        RequestSend();   // the new character gets our held joints too
    }

    void RequestSend() { sendTimer.Stop(); sendTimer.Start(); }

    void Send() {
        try { link.WriteCommand(model.Command()); } catch (IOException) { RequestSend(); }
    }

    void UpdateStatus() {
        bool alive = link.Folder != null && link.ScriptAlive();
        whereLine.Text = dockShown && model.Connected ? HandSpots.Line(model.Spot, shownSide, model.Gun) : "";
        if (link.Folder == null) { status.Text = "no game found yet: start an RE game with REFramework, or pick its folder"; status.ForeColor = Bad; }
        else if (!alive) { status.Text = Path.GetFileName(link.Folder) + " · waiting for the script in the game"; status.ForeColor = Dim; }
        else if (!model.Connected) { status.Text = model.Game + " · script running, no character yet (load into the game)"; status.ForeColor = Dim; }
        else { status.Text = model.Game + " · " + model.Character + " · connected · " + model.Rows.Count + " held" + (perGun && poseGun != null ? " · pose of: " + GunWords(poseGun) : "") + (model.LastError != "" ? " · " + model.LastError : ""); status.ForeColor = Good; }
        if (link.Folder == null) { scriptButton.Enabled = false; scriptNote.Text = ""; }
        else if (link.ScriptUpToDate()) { scriptButton.Enabled = false; scriptNote.Text = "script is in the game"; }
        else { scriptButton.Enabled = true; scriptNote.Text = link.ScriptInstalled() ? "an older script is in the game" : "script not in the game yet"; }

    }

    // ---- changes ----------------------------------------------------------------------------------------------------------
    // One undoable change: snapshot, do it, rebuild the rows, send, autosave.
    void Change(Action act) {
        model.Mark();
        act();
        AfterModelChange();
    }

    void AfterModelChange() {
        RebuildRows();
        RequestSend();
        try { model.Autosave(); } catch (IOException) { }
        UpdateStatus();
    }

    // RE2 / RE3 name the fingers l_hand_index_0 ...; Village (and RE7) L_IndexF1 ... L_Thumb3 (2026-10-24)
    static readonly string[] FingerWords = { "index", "middle", "ring", "pinky", "thumb" };
    static bool IsFinger(string n, string side) {
        string l = n.ToLowerInvariant();
        if (l.StartsWith(side + "hand_")) return true;
        foreach (var f in FingerWords) if (l.StartsWith(side + f)) return true;
        return false;
    }

    // a hand: its wrist (Village / RE7 L_Hand, RE2 / RE3 l_arm_wrist) and its fingers
    static bool IsHand(string n, string side) {
        return IsFinger(n, side + "_") || n.StartsWith(side + "_arm_wrist") || n.ToLowerInvariant() == side + "_hand";
    }

    void QuickAdd(Func<string, bool> match, string what) {
        if (model.Skeleton.Names.Count == 0) { status.Text = "no skeleton yet: the game has to be running with a character"; status.ForeColor = Bad; return; }
        if (!model.Skeleton.Names.Any(match)) { status.Text = "this character has no " + what + " joints by those names: add them from the list"; status.ForeColor = Bad; return; }
        Change(() => model.AddMatching(match));
    }

    void AddSelected() {
        var item = allJoints.SelectedItem as JointItem;
        if (item == null) return;
        string name = item.Name;
        Change(() => model.Add(name));
    }

    void FillJointList() {
        string f = filter.Text.Trim().ToLowerInvariant();
        allJoints.BeginUpdate();
        allJoints.Items.Clear();
        foreach (var n in model.Skeleton.Names) {   // the plain words count in the filter too ("index", "thumb", "wrist")
            string d = (JointNames.Describe(n) ?? "").ToLowerInvariant();
            if (f == "" || n.ToLowerInvariant().Contains(f) || d.Contains(f)) allJoints.Items.Add(new JointItem(n));
        }
        allJoints.EndUpdate();
    }

    void FillBuilds() {
        builds.Items.Clear();
        foreach (var b in model.Builds()) builds.Items.Add(b);
        if (builds.Items.Count > 0) builds.SelectedIndex = 0;
    }

    void SaveBuild() {
        string file = model.SaveBuild(buildName.Text);
        if (file == null) { status.Text = "give the build a name first"; status.ForeColor = Bad; return; }
        FillBuilds();
        builds.SelectedItem = Path.GetFileNameWithoutExtension(file);
        status.Text = "saved build " + Path.GetFileNameWithoutExtension(file);
    }

    void DeleteBuild() {
        var name = builds.SelectedItem as string;
        if (name == null) return;
        if (MessageBox.Show(this, "Delete the build \"" + name + "\"?", "Hand Poser", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        model.DeleteBuild(name);
        FillBuilds();
    }

    // ---- rows ---------------------------------------------------------------------------------------------------------------
    string shownSide = "";   // "l" / "r": only that hand's rows are shown; "" = both (before either button was pressed)
    void ShowSide(string side) {
        shownSide = side;
        settings.Set("shown_hand", side);
        if (dockBox != null) dockBox.Side = side;   // the wrist box poses the shown hand too
        status.Text = "showing the " + (side == "l" ? "LEFT" : "RIGHT") + " hand's sliders (the other hand keeps its pose)";
        status.ForeColor = Ink;
    }
    static string SideOfName(string n) {
        string l = n.ToLowerInvariant();
        return l.StartsWith("l_") ? "l" : l.StartsWith("r_") ? "r" : "";
    }
    static string SideOfGroup(string group) {
        string h = JointNames.FingerHeader(group);
        return h.StartsWith("LEFT") ? "l" : h.StartsWith("RIGHT") ? "r" : "";
    }
    bool SideShown(string side) { return shownSide == "" || side == "" || side == shownSide; }
    readonly List<GroupRow> groupRows = new List<GroupRow>();
    void RebuildRows() {
        rowsPanel.SuspendLayout();
        foreach (var r in rows) r.Dispose();
        foreach (var g in groupRows) g.Dispose();
        rows.Clear();
        groupRows.Clear();
        rowsPanel.Controls.Clear();
        foreach (var b in fingerBoxes.Values) b.Dispose();
        fingerBoxes.Clear();
        // 2026-10-27 (Tefa: "group this whole one part, example thumb, in like a separate box that is straight away clear that
        // everything here affects the thumb"): a box per finger, its header, its BEND ALL bar and its joints; other joints after
        var groups = model.Groups();
        // thumb first, then index to pinky, left hand before right
        groups = groups.OrderBy(g => FingerOrder(g.Key)).ToList();
        var inGroup = new HashSet<Row>();
        foreach (var g in groups) foreach (var r in g.Value) inGroup.Add(r);
        var order = new List<Control>();
        foreach (var g in groups) {
            if (!SideShown(SideOfGroup(g.Key))) continue;   // 2026-11-02: the other hand, out of view
            var box = new SkinPanel { Dock = DockStyle.Top, Tint = Color.FromArgb(150, 30, 44, 66), Padding = new Padding(6, 2, 6, 4), Margin = new Padding(0, 0, 0, 6) };
            var head = new SkinLabel { Text = JointNames.FingerHeader(g.Key), Dock = DockStyle.Top, Height = 26, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Ink, Font = new Font("Segoe UI Semibold", 14f) };
            var gap = new Panel { Dock = DockStyle.Top, Height = 6, BackColor = Color.Transparent };
            var gr = new GroupRow(g.Key, g.Value, this);
            groupRows.Add(gr);
            var mine = new List<JointRow>();
            foreach (var r in g.Value) { var jr = new JointRow(r, this, GroupAxis(g.Key)); mine.Add(jr); rows.Add(jr); }
            for (int i = mine.Count - 1; i >= 0; i--) box.Controls.Add(mine[i]);   // docked Top: add in reverse
            box.Controls.Add(gr);
            box.Controls.Add(head);
            int h = box.Padding.Vertical + head.Height + gr.Height;
            foreach (var jr in mine) h += jr.Height;
            box.Height = h;
            fingerBoxes[g.Key] = box;
            nudge.Section(box, "finger:" + g.Key);   // 2026-11-08: the buttons reach the part clicked last (Nudge.cs)
            order.Add(box);
            order.Add(gap);
        }
        foreach (var r in model.Rows) if (!inGroup.Contains(r) && SideShown(SideOfName(r.Name))) { var jr = new JointRow(r, this, null); rows.Add(jr); order.Add(jr); nudge.Section(jr, "joint:" + r.Name); }
        for (int i = order.Count - 1; i >= 0; i--) rowsPanel.Controls.Add(order[i]);
        rowsPanel.ResumeLayout();
    }

    static int FingerOrder(string group) {
        string h = JointNames.FingerHeader(group);
        int side = h.StartsWith("RIGHT") ? 10 : 0;
        string[] f = { "THUMB", "INDEX", "MIDDLE", "RING", "PINKY" };
        for (int i = 0; i < f.Length; i++) if (h.EndsWith(f[i])) return side + i;
        return 100;
    }

    // ---- jump to a finger (the arrows beside LEFT / RIGHT FINGERS) ---------------------------------------------------------
    readonly Dictionary<string, SkinPanel> fingerBoxes = new Dictionary<string, SkinPanel>();
    void JumpMenu(Control at, string side) {
        var menu = new ContextMenuStrip { Font = new Font("Segoe UI", 12f) };
        foreach (var f in new[] { "THUMB", "INDEX", "MIDDLE", "RING", "PINKY" }) {
            string want = side + " " + f;
            menu.Items.Add(want, null, (s, e) => JumpTo(want, side));
        }
        menu.Show(at, new Point(0, at.Height));
    }
    void JumpTo(string header, string side) {
        SkinPanel box = null;
        foreach (var kv in fingerBoxes) if (JointNames.FingerHeader(kv.Key) == header) box = kv.Value;
        if (box == null) {   // that hand's fingers are not in the window yet: add them first
            string pre = side == "LEFT" ? "l_" : "r_";
            QuickAdd(n => IsFinger(n, pre), side.ToLowerInvariant() + " finger");
            foreach (var kv in fingerBoxes) if (JointNames.FingerHeader(kv.Key) == header) box = kv.Value;
        }
        if (box != null) rowsPanel.AutoScrollPosition = new Point(0, box.Top - rowsPanel.AutoScrollPosition.Y);
    }
    // the bend axis changed on a finger's bar: its joints' IN / OUT words move to that axis
    internal void BendAxisChanged(string group, string axis) {
        foreach (var r in rows) if (PoseModel.GroupOf(r.Model.Name) == group) r.SetBendAxis(axis);
    }

    // a finger group's bend slider: while dragged it adds its value to every joint of the finger on the chosen axis, from where
    // they stood when it was taken hold of; let go, it springs back to 0 (a nudge, as often as needed). The joints' own
    // sliders follow live.
    internal void GroupPressed() { model.Mark(); UpdateStatus(); }
    internal void GroupMoved() { foreach (var row in rows) row.PullFromModel(); RequestSend(); }
    internal void GroupReleased() { try { model.Autosave(); } catch (IOException) { } }
    internal string GroupAxis(string group) { return settings.Get("axis." + group, "Z"); }
    internal void SetGroupAxis(string group, string axis) { settings.Set("axis." + group, axis); }

    class GroupRow : SkinPanel {
        readonly string group;
        readonly List<Row> members;
        readonly PoserUi ui;
        readonly FancySlider bend;
        readonly Button axis;
        readonly Label value;
        readonly CheckBox tick;
        double[] bases;

        public GroupRow(string group, List<Row> members, PoserUi ui) {
            this.group = group; this.members = members; this.ui = ui;
            Dock = DockStyle.Top; Height = 40; Tint = Color.FromArgb(185, 40, 64, 96); Padding = new Padding(6, 2, 6, 2);
            var name = new SkinLabel { Text = "BEND ALL", AutoSize = true, ForeColor = Ink, Font = new Font(ui.Font, FontStyle.Bold), Location = new Point(6, 11) };
            axis = ui.Btn("axis " + ui.GroupAxis(group), (s, e) => NextAxis(), 64);
            axis.Height = 26;
            value = new SkinLabel { AutoSize = true, ForeColor = Dim, Location = new Point(0, 11) };
            bend = new FancySlider { Minimum = -900, Maximum = 900, SmallChange = 5, LargeChange = 50, Height = 34, Signed = true };
            BarWords();
            bend.Pressed += () => { ui.GroupPressed(); foreach (var r in members) RowHistory.Mark(ui.poseGun, r); bases = members.Select(r => Axis(r)).ToArray(); };
            bend.Scroll += (o, e) => Apply();
            bend.Released += () => { bases = null; ui.syncing = true; bend.Value = 0; ui.syncing = false; ShowValue(); ui.GroupReleased(); };
            tick = new SkinCheckBox { Width = 28, Height = 28, Checked = ui.IsTicked("group:" + group), Location = new Point(6, 6), Tag = "group:" + group };
            tick.CheckedChanged += (o, e) => ui.SetTicked("group:" + group, tick.Checked);
            name.Location = new Point(40, 11);
            Controls.Add(tick); Controls.Add(name); Controls.Add(axis); Controls.Add(bend); Controls.Add(value);
            Layout += (o, e) => {
                int w = ClientSize.Width;
                axis.Location = new Point(w - axis.Width - 6, 7);
                value.Location = new Point(axis.Left - 70, 11);
                int left = name.Right + 10;
                bend.Location = new Point(left, 5);
                bend.Width = Math.Max(60, value.Left - left - 6);
            };
            ShowValue();
        }
        string AxisName() { return ui.GroupAxis(group); }
        double Axis(Row r) { string a = AxisName(); return a == "X" ? r.X : a == "Y" ? r.Y : r.Z; }
        void NextAxis() {
            string a = AxisName(), next = a == "X" ? "Y" : a == "Y" ? "Z" : "X";
            ui.SetGroupAxis(group, next);
            axis.Text = "axis " + next;
            BarWords();
            ui.BendAxisChanged(group, next);
        }
        int AxisIndex() { return "XYZ".IndexOf(AxisName()); }
        // 2026-11-08: the bar's ends say what its axis does on this finger (HandAxes.cs: Z bends IN / OUT, Y LEFT / RIGHT, X turns)
        void BarWords() {
            string l, r; bool f;
            HandAxes.Ends(members.Count > 0 ? members[0].Name : "", Math.Max(0, AxisIndex()), out l, out r, out f);
            if (l.StartsWith("-")) { l = "IN"; r = "OUT"; }
            bend.LeftText = l; bend.RightText = r; bend.Invalidate();
        }
        void Apply() {
            if (bases == null) return;
            double d = bend.Value / (double)SLIDER_STEPS_PER_DEG;
            string a = AxisName();
            for (int i = 0; i < members.Count; i++) {
                var r = members[i];
                double v = PoseModel.Clamp(bases[i] + d * HandAxes.Sign(r.Name, AxisIndex()));   // the bar's right end is the joint's right end
                if (a == "X") r.X = v; else if (a == "Y") r.Y = v; else r.Z = v;
                r.Known = true; r.Moved = true;
            }
            ShowValue();
            ui.GroupMoved();
        }
        void ShowValue() { value.Text = string.Format("{0:+0.0;-0.0;0.0} deg", bend.Value / (double)SLIDER_STEPS_PER_DEG); }
    }

    // called by a row: a slider is about to move (snapshot + light up the children) / moved / let go
    internal void RowPressed(Row r) {
        model.Mark();
        RowHistory.Mark(poseGun, r);   // 2026-11-14: the row's own step
        var below = model.Skeleton.Descendants(r.Name);
        foreach (var row in rows) row.Lit = below.Contains(row.Model.Name);
        UpdateStatus();
    }
    internal void RowMoved(Row r) { RequestSend(); }
    internal void RowReleased(Row r) {
        foreach (var row in rows) { row.Lit = false; if (row.Model == r) row.PullFromModel(); }   // (2026-11-14: its ↶ lights up)
        try { model.Autosave(); } catch (IOException) { }
    }
    internal void RowReset(Row r) { RowHistory.Mark(poseGun, r); Change(() => model.Set(r.Name, 0, 0, 0)); }
    // 2026-11-14: a row's ↶ / ↷ (RowHistory.cs): only that row, only in the pose it is in
    internal void RowHistoryStep(Row r, bool redo) { double x, y, z; if (RowHistory.Step(poseGun, r, redo, out x, out y, out z)) Change(() => model.Set(r.Name, x, y, z)); }
    internal bool RowCanUndo(Row r) { return RowHistory.CanUndo(poseGun, r.Name); }
    internal bool RowCanRedo(Row r) { return RowHistory.CanRedo(poseGun, r.Name); }
    internal void RowRemove(Row r) { Change(() => model.Remove(r.Name)); }

    // ---- the demo for the test (--demo): add the left fingers, then turn index_0 to 30 degrees ----------------------
    void Demo() {
        if (demoStep == 0 && model.Skeleton.Names.Count > 0) { demoStep = 1; QuickAdd(n => n.StartsWith("l_hand_"), "left finger"); }
        else if (demoStep == 1 && model.Rows.Count > 0 && model.Rows.All(r => r.Known)) {
            demoStep = 2;
            var r = model.Rows[0];
            RowPressed(r); model.Set(r.Name, 30, r.Y, r.Z); RowMoved(r); RowReleased(r);
            foreach (var row in rows) row.PullFromModel();
        }
    }

}
