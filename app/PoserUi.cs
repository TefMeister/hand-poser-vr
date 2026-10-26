// PoserUi.cs -- the Hand Poser window. Top: game and character, the game folder and script buttons, the quick-add
// buttons (LEFT FINGERS, RIGHT FINGERS, LEFT HAND, RIGHT HAND), a filter box with the whole skeleton to add any one
// joint. Middle: one row per held joint with X / Y / Z sliders in degrees, RESET and a remove cross; while a slider is
// dragged, the rows of the joints it carries along light up amber. Bottom: UNDO, REDO, the pose-build name with SAVE
// BUILD, the saved builds with LOAD and DELETE. Every change goes to the game at once and into the autosave.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

class PoserUi : Form {
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

    Label status, scriptNote;
    Button scriptButton, undoButton, redoButton;
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
        ClientSize = new Size(900, 1480);   // 2026-10-27: one hand's five fingers fit without scrolling
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
        if (!GameLink.LooksLikeGame(folder)) folder = GameLink.FindRunningGame();
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
        var top = new SkinPanel { Dock = DockStyle.Top, Height = 276, Tint = Color.FromArgb(120, Back) };
        Controls.Add(top);

        status = new SkinLabel { Dock = DockStyle.Top, Height = 24, Padding = new Padding(8, 4, 0, 0), ForeColor = Dim };
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
        perSpotBox = new SkinCheckBox { Text = "AND EACH HAND SPOT (start sphere, landing spot, slide, magazine)", AutoSize = true, Checked = perSpot, ForeColor = Ink, Padding = new Padding(4, 4, 0, 0) };
        perSpotBox.CheckedChanged += (s, e) => { perSpot = perSpotBox.Checked; settings.Set("per_spot", perSpot ? "1" : "0"); GunCheck(); };
        gunRow.Controls.Add(perSpotBox);

        var quick = Row(32);
        quick.Controls.Add(Btn("LEFT FINGERS", (s, e) => QuickAdd(n => IsFinger(n, "l_"), "left finger")));
        quick.Controls.Add(Btn("▼", (s, e) => JumpMenu((Control)s, "LEFT"), 34));   // 2026-10-27: jump to a finger
        quick.Controls.Add(Btn("RIGHT FINGERS", (s, e) => QuickAdd(n => IsFinger(n, "r_"), "right finger")));
        quick.Controls.Add(Btn("▼", (s, e) => JumpMenu((Control)s, "RIGHT"), 34));
        // 2026-10-25 (Tefa: "i cannot rotate my hands, they only follow motion controllers ... i did change both left and right
        // hand values"): these add the WRIST, and a held wrist is turned the same way every frame, so the controller can no
        // longer turn that hand. Named for what they add, and the row says so
        quick.Controls.Add(Btn("LEFT WRIST", (s, e) => QuickAdd(n => n.StartsWith("l_arm_wrist") || n.ToLowerInvariant() == "l_hand", "left wrist")));
        quick.Controls.Add(Btn("RIGHT WRIST", (s, e) => QuickAdd(n => n.StartsWith("r_arm_wrist") || n.ToLowerInvariant() == "r_hand", "right wrist")));
        // (2026-10-27, Tefa: "please remove clear all from it")

        var filterRow = Row(30);
        filterRow.Controls.Add(new SkinLabel { Text = "any joint:", AutoSize = true, ForeColor = Dim, Padding = new Padding(0, 6, 0, 0) });
        filter = new TextBox { Width = 200, BackColor = Panel2, ForeColor = Ink, BorderStyle = BorderStyle.FixedSingle };
        filter.TextChanged += (s, e) => FillJointList();
        filterRow.Controls.Add(filter);
        filterRow.Controls.Add(Btn("ADD", (s, e) => AddSelected()));
        allJoints = new ListBox { Dock = DockStyle.Top, Height = 84, BackColor = Panel2, ForeColor = Ink, BorderStyle = BorderStyle.None, IntegralHeight = false };
        allJoints.DoubleClick += (s, e) => AddSelected();
        var listHolder = new Panel { Dock = DockStyle.Top, Height = 88, Padding = new Padding(8, 0, 8, 4), BackColor = Color.Transparent };
        listHolder.Controls.Add(allJoints);

        // 2026-10-27 (Tefa: "add a clear all tick marks button"): beside the line about the ticked sliders
        var nudgeRow = Row(34);
        nudgeRow.Controls.Add(Btn("CLEAR ALL TICKS", (s, e) => ClearTicks()));
        nudgeNote = new SkinLabel { AutoSize = true, Padding = new Padding(4, 6, 0, 0), ForeColor = Dim };
        nudgeRow.Controls.Add(nudgeNote);
        // docked controls stack bottom-up in the order added, so add in reverse
        top.Controls.Add(nudgeRow);
        top.Controls.Add(listHolder);
        top.Controls.Add(filterRow);
        top.Controls.Add(quick);
        top.Controls.Add(gunRow);
        top.Controls.Add(gameRow);
        top.Controls.Add(status);
    }

    void BuildBottom() {
        var bottom = new SkinPanel { Dock = DockStyle.Bottom, Height = 70, Tint = Color.FromArgb(120, Back) };
        Controls.Add(bottom);
        var one = Row(32);
        undoButton = Btn("UNDO", (s, e) => { model.Undo(); AfterModelChange(); });
        redoButton = Btn("REDO", (s, e) => { model.Redo(); AfterModelChange(); });
        one.Controls.Add(undoButton); one.Controls.Add(redoButton);
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
        if (started) model.Mark();   // one undo step per press (its repeats included)
        nudgeSaving = true;
        // every slider to step, once each, whether ticked on its own or through its finger's bar
        var targets = new HashSet<string>();
        foreach (var r in model.Rows) foreach (var a in new[] { "X", "Y", "Z" }) if (ticked.Contains(r.Name + ":" + a)) targets.Add(r.Name + ":" + a);
        foreach (var g in model.Groups()) {
            if (!ticked.Contains("group:" + g.Key)) continue;
            string axis = GroupAxis(g.Key);
            foreach (var r in g.Value) targets.Add(r.Name + ":" + axis);
        }
        foreach (var r in model.Rows) {
            if (!r.Known) continue;
            if (targets.Contains(r.Name + ":X")) r.X = PoseModel.Clamp(r.X + step);
            if (targets.Contains(r.Name + ":Y")) r.Y = PoseModel.Clamp(r.Y + step);
            if (targets.Contains(r.Name + ":Z")) r.Z = PoseModel.Clamp(r.Z + step);
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
            if ((DateTime.Now - lastFind).TotalMilliseconds > FIND_GAME_EVERY_MS) {
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
        string g = model.Gun ?? "";
        if (perSpot && !string.IsNullOrEmpty(model.Spot)) g += " @ " + model.Spot;   // a spot keeps its own pose too
        if (poseGun == g) { model.GunPoseFile = model.GunFile(g); return; }
        string file = model.GunFile(g);
        if (File.Exists(file)) {   // this gun has its own pose: it comes back (the previous gun's is already saved)
            model.Mark();
            if (model.LoadFile(file)) { poseGun = g; model.GunPoseFile = file; AfterModelChange(); return; }
        }
        poseGun = g;               // a gun never posed: it starts from the pose in hand, kept from now on as its own
        model.GunPoseFile = file;
        try { model.Autosave(); } catch (IOException) { }
        UpdateStatus();
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
        if (link.Folder == null) { status.Text = "no game found yet: start an RE game with REFramework, or pick its folder"; status.ForeColor = Bad; }
        else if (!alive) { status.Text = Path.GetFileName(link.Folder) + " · waiting for the script in the game"; status.ForeColor = Dim; }
        else if (!model.Connected) { status.Text = model.Game + " · script running, no character yet (load into the game)"; status.ForeColor = Dim; }
        else { status.Text = model.Game + " · " + model.Character + " · connected · " + model.Rows.Count + " held" + (perGun && poseGun != null ? " · pose of: " + GunWords(poseGun) : "") + (model.LastError != "" ? " · " + model.LastError : ""); status.ForeColor = Good; }
        if (link.Folder == null) { scriptButton.Enabled = false; scriptNote.Text = ""; }
        else if (link.ScriptUpToDate()) { scriptButton.Enabled = false; scriptNote.Text = "script is in the game"; }
        else { scriptButton.Enabled = true; scriptNote.Text = link.ScriptInstalled() ? "an older script is in the game" : "script not in the game yet"; }
        undoButton.Enabled = model.CanUndo; redoButton.Enabled = model.CanRedo;
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
            order.Add(box);
            order.Add(gap);
        }
        foreach (var r in model.Rows) if (!inGroup.Contains(r)) { var jr = new JointRow(r, this, null); rows.Add(jr); order.Add(jr); }
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
            bend = new FancySlider { Minimum = -900, Maximum = 900, SmallChange = 5, LargeChange = 50, Height = 34, Signed = true, LeftText = "IN", RightText = "OUT" };
            bend.Pressed += () => { ui.GroupPressed(); bases = members.Select(r => Axis(r)).ToArray(); };
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
            ui.BendAxisChanged(group, next);
        }
        void Apply() {
            if (bases == null) return;
            double d = bend.Value / (double)SLIDER_STEPS_PER_DEG;
            string a = AxisName();
            for (int i = 0; i < members.Count; i++) {
                var r = members[i];
                double v = PoseModel.Clamp(bases[i] + d);
                if (a == "X") r.X = v; else if (a == "Y") r.Y = v; else r.Z = v;
                r.Known = true;
            }
            ShowValue();
            ui.GroupMoved();
        }
        void ShowValue() { value.Text = string.Format("{0:+0.0;-0.0;0.0} deg", bend.Value / (double)SLIDER_STEPS_PER_DEG); }
    }

    // called by a row: a slider is about to move (snapshot + light up the children) / moved / let go
    internal void RowPressed(Row r) {
        model.Mark();
        var below = model.Skeleton.Descendants(r.Name);
        foreach (var row in rows) row.Lit = below.Contains(row.Model.Name);
        UpdateStatus();
    }
    internal void RowMoved(Row r) { RequestSend(); }
    internal void RowReleased(Row r) {
        foreach (var row in rows) row.Lit = false;
        try { model.Autosave(); } catch (IOException) { }
    }
    internal void RowReset(Row r) { Change(() => model.Set(r.Name, 0, 0, 0)); }
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

    // ---- one joint: name, values, three sliders, RESET, remove ------------------------------------------------------------
    class JointRow : SkinPanel {
        public readonly Row Model;
        readonly PoserUi ui;
        readonly Label values;
        readonly FancySlider[] sliders = new FancySlider[3];
        readonly CheckBox[] ticks = new CheckBox[3];   // 2026-10-25: ticked = the controller buttons step this slider (Nudge.cs)
        bool lit;

        public bool Lit { get { return lit; } set { if (lit != value) { lit = value; Tint = lit ? Color.FromArgb(215, PoserUi.Lit) : Color.FromArgb(160, Panel2); } } }

        readonly bool compact;   // 2026-10-27: inside a finger box: one short line (KNUCKLE BASE / MIDDLE / TIP), the bend axis IN / OUT

        public JointRow(Row model, PoserUi ui, string bendAxis) {
            Model = model; this.ui = ui;
            compact = bendAxis != null;
            // 2026-10-25 (Tefa: "R_IndexF1 is something that i don't know what it is until i try it"): the plain words on top,
            // the game's name after them, the numbers on a line of their own. In a finger box: the short name, numbers under it
            Dock = DockStyle.Top; Height = compact ? 42 : 80; Tint = Color.FromArgb(compact ? 110 : 160, Panel2); Margin = new Padding(0, 0, 0, 3); Padding = new Padding(6, 2, 6, 2);
            var name = new SkinLabel { Text = compact ? JointNames.ShortPart(model.Name) : JointNames.Title(model.Name), AutoSize = true, Location = new Point(6, 4), ForeColor = Ink, Font = new Font(ui.Font.FontFamily, compact ? 10f : ui.Font.Size, FontStyle.Bold) };
            values = new SkinLabel { AutoSize = true, Location = new Point(6, compact ? 22 : 24), ForeColor = Dim, Font = new Font(ui.Font.FontFamily, compact ? 7.5f : ui.Font.Size) };
            var reset = ui.Btn("RESET", (s, e) => ui.RowReset(Model), 56);
            var remove = ui.Btn("✕", (s, e) => ui.RowRemove(Model), 26);
            reset.Height = remove.Height = 22;
            Controls.Add(name); Controls.Add(values); Controls.Add(reset); Controls.Add(remove);
            for (int i = 0; i < 3; i++) {
                var s = new FancySlider { Minimum = (int)(PoseModel.MIN_DEG * SLIDER_STEPS_PER_DEG), Maximum = (int)(PoseModel.MAX_DEG * SLIDER_STEPS_PER_DEG), SmallChange = 5, LargeChange = 50, Height = compact ? 38 : 30, Signed = true };
                int axis = i;
                s.Pressed += () => ui.RowPressed(Model);
                s.Released += () => ui.RowReleased(Model);
                s.Scroll += (o, e) => { if (ui.syncing) return; Push(); ui.RowMoved(Model); ShowValues(); };
                s.KeyDown += (o, e) => { if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right || e.KeyCode == Keys.Up || e.KeyCode == Keys.Down || e.KeyCode == Keys.PageUp || e.KeyCode == Keys.PageDown) ui.RowPressed(Model); };
                s.KeyUp += (o, e) => ui.RowReleased(Model);
                sliders[axis] = s;
                Controls.Add(s);
                string key = model.Name + ":" + "XYZ"[axis];
                var t = new SkinCheckBox { Width = 28, Height = 28, Checked = ui.IsTicked(key), Tag = key };
                t.CheckedChanged += (o, e) => ui.SetTicked(key, t.Checked);
                ticks[axis] = t;
                Controls.Add(t);
            }
            SetBendAxis(bendAxis);
            Layout += (o, e) => {
                int w = ClientSize.Width;
                if (compact) {   // name | three tick + slider columns | RESET ✕
                    remove.Location = new Point(w - remove.Width - 6, 10);
                    reset.Location = new Point(remove.Left - reset.Width - 4, 10);
                    int left = 150, sw = Math.Max(90, (reset.Left - 8 - left) / 3);
                    for (int i = 0; i < 3; i++) {
                        ticks[i].Location = new Point(left + i * sw, 7);
                        sliders[i].Location = new Point(left + i * sw + 32, 2); sliders[i].Width = sw - 38;
                    }
                    return;
                }
                remove.Location = new Point(w - remove.Width - 6, 4);
                reset.Location = new Point(remove.Left - reset.Width - 4, 4);
                values.Location = new Point(6, 24);
                int sw2 = (w - 12) / 3;
                for (int i = 0; i < 3; i++) {
                    ticks[i].Location = new Point(6 + i * sw2, 47);
                    sliders[i].Location = new Point(6 + i * sw2 + 32, 46); sliders[i].Width = sw2 - 36;
                }
            };
            PullFromModel();
        }

        // the finger's bend axis says IN (left) / OUT (right); the other two their axis letter at each end
        public void SetBendAxis(string axis) {
            for (int i = 0; i < 3; i++) {
                string a = "XYZ"[i].ToString();
                if (axis != null && a == axis) { sliders[i].LeftText = "IN"; sliders[i].RightText = "OUT"; }
                else if (axis != null) { sliders[i].LeftText = "-" + a; sliders[i].RightText = "+" + a; }
                sliders[i].Invalidate();
            }
        }

        void Push() {
            Model.X = sliders[0].Value / (double)SLIDER_STEPS_PER_DEG;
            Model.Y = sliders[1].Value / (double)SLIDER_STEPS_PER_DEG;
            Model.Z = sliders[2].Value / (double)SLIDER_STEPS_PER_DEG;
            Model.Known = true;
        }

        public void PullFromModel() {
            ui.syncing = true;
            foreach (var s in sliders) s.Enabled = Model.Known;
            if (Model.Known) {
                sliders[0].Value = Clamp((int)Math.Round(Model.X * SLIDER_STEPS_PER_DEG));
                sliders[1].Value = Clamp((int)Math.Round(Model.Y * SLIDER_STEPS_PER_DEG));
                sliders[2].Value = Clamp((int)Math.Round(Model.Z * SLIDER_STEPS_PER_DEG));
            }
            ui.syncing = false;
            ShowValues();
        }

        int Clamp(int v) { return Math.Max(sliders[0].Minimum, Math.Min(sliders[0].Maximum, v)); }

        void ShowValues() {
            string own = Model.Known ? string.Format("X {0,7:0.0}   Y {1,7:0.0}   Z {2,7:0.0}", Model.X, Model.Y, Model.Z) : "reading from the game...";
            // 2026-10-25: the joint as seen from the hand, live: it changes when a joint above it turns
            if (compact) own = Model.Known ? string.Format("X {0:0.0}  Y {1:0.0}  Z {2:0.0}", Model.X, Model.Y, Model.Z) : "reading...";
            else if (Model.Known && Model.RelKnown) own += string.Format("      from the hand  {0:0} / {1:0} / {2:0}", Model.RelX, Model.RelY, Model.RelZ);
            values.Text = own;
        }
    }
}
