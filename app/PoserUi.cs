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
        ClientSize = new Size(600, 760);
        MinimumSize = new Size(520, 560);
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
        var top = new SkinPanel { Dock = DockStyle.Top, Height = 238, Tint = Color.FromArgb(120, Back) };
        Controls.Add(top);

        status = new SkinLabel { Dock = DockStyle.Top, Height = 24, Padding = new Padding(8, 4, 0, 0), ForeColor = Dim };
        var gameRow = Row(32);
        gameRow.Controls.Add(Btn("GAME FOLDER...", (s, e) => PickFolder()));
        scriptButton = Btn("PUT THE SCRIPT INTO THE GAME", (s, e) => InstallScript());
        gameRow.Controls.Add(scriptButton);
        scriptNote = new SkinLabel { AutoSize = true, ForeColor = Dim, Padding = new Padding(4, 6, 0, 0) };
        gameRow.Controls.Add(scriptNote);

        var quick = Row(32);
        quick.Controls.Add(Btn("LEFT FINGERS", (s, e) => QuickAdd(n => IsFinger(n, "l_"), "left finger")));
        quick.Controls.Add(Btn("RIGHT FINGERS", (s, e) => QuickAdd(n => IsFinger(n, "r_"), "right finger")));
        quick.Controls.Add(Btn("LEFT HAND", (s, e) => QuickAdd(n => n.StartsWith("l_arm_wrist") || n.ToLowerInvariant() == "l_hand", "left hand")));
        quick.Controls.Add(Btn("RIGHT HAND", (s, e) => QuickAdd(n => n.StartsWith("r_arm_wrist") || n.ToLowerInvariant() == "r_hand", "right hand")));
        quick.Controls.Add(Btn("CLEAR ALL", (s, e) => Change(() => model.Clear())));

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

        nudgeNote = new SkinLabel { Dock = DockStyle.Top, Height = 24, Padding = new Padding(8, 4, 0, 0), ForeColor = Dim };
        // docked controls stack bottom-up in the order added, so add in reverse
        top.Controls.Add(nudgeNote);
        top.Controls.Add(listHolder);
        top.Controls.Add(filterRow);
        top.Controls.Add(quick);
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
            }
        }
        UpdateStatus();
        if (demo) Demo();
    }

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
        else { status.Text = model.Game + " · " + model.Character + " · connected · " + model.Rows.Count + " held" + (model.LastError != "" ? " · " + model.LastError : ""); status.ForeColor = Good; }
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
        for (int i = model.Rows.Count - 1; i >= 0; i--) {     // docked Top: last added ends up first, so add in reverse
            var row = new JointRow(model.Rows[i], this);
            rows.Add(row);
            rowsPanel.Controls.Add(row);
        }
        rows.Reverse();
        var groups = model.Groups();                           // the finger groups go above the joints
        for (int i = groups.Count - 1; i >= 0; i--) {
            var g = new GroupRow(groups[i].Key, groups[i].Value, this);
            groupRows.Add(g);
            rowsPanel.Controls.Add(g);
        }
        rowsPanel.ResumeLayout();
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
            var name = new SkinLabel { Text = JointNames.GroupTitle(group), AutoSize = true, ForeColor = Ink, Font = new Font(ui.Font, FontStyle.Bold), Location = new Point(6, 11) };
            axis = ui.Btn("axis " + ui.GroupAxis(group), (s, e) => NextAxis(), 64);
            axis.Height = 26;
            value = new SkinLabel { AutoSize = true, ForeColor = Dim, Location = new Point(0, 11) };
            bend = new FancySlider { Minimum = -900, Maximum = 900, SmallChange = 5, LargeChange = 50, Height = 30, Signed = true };
            bend.Pressed += () => { ui.GroupPressed(); bases = members.Select(r => Axis(r)).ToArray(); };
            bend.Scroll += (o, e) => Apply();
            bend.Released += () => { bases = null; ui.syncing = true; bend.Value = 0; ui.syncing = false; ShowValue(); ui.GroupReleased(); };
            tick = new SkinCheckBox { Width = 18, Height = 18, Checked = ui.IsTicked("group:" + group), Location = new Point(6, 12) };
            tick.CheckedChanged += (o, e) => ui.SetTicked("group:" + group, tick.Checked);
            name.Location = new Point(28, 11);
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

        public JointRow(Row model, PoserUi ui) {
            Model = model; this.ui = ui;
            // 2026-10-25 (Tefa: "R_IndexF1 is something that i don't know what it is until i try it"): the plain words on top,
            // the game's name after them, the numbers on a line of their own
            Dock = DockStyle.Top; Height = 80; Tint = Color.FromArgb(160, Panel2); Margin = new Padding(0, 0, 0, 3); Padding = new Padding(6, 2, 6, 2);
            var name = new SkinLabel { Text = JointNames.Title(model.Name), AutoSize = true, Location = new Point(6, 4), ForeColor = Ink, Font = new Font(ui.Font, FontStyle.Bold) };
            values = new SkinLabel { AutoSize = true, Location = new Point(6, 24), ForeColor = Dim };
            var reset = ui.Btn("RESET", (s, e) => ui.RowReset(Model), 56);
            var remove = ui.Btn("✕", (s, e) => ui.RowRemove(Model), 26);
            reset.Height = remove.Height = 22;
            Controls.Add(name); Controls.Add(values); Controls.Add(reset); Controls.Add(remove);
            for (int i = 0; i < 3; i++) {
                var s = new FancySlider { Minimum = (int)(PoseModel.MIN_DEG * SLIDER_STEPS_PER_DEG), Maximum = (int)(PoseModel.MAX_DEG * SLIDER_STEPS_PER_DEG), SmallChange = 5, LargeChange = 50, Height = 30, Signed = true };
                int axis = i;
                s.Pressed += () => ui.RowPressed(Model);
                s.Released += () => ui.RowReleased(Model);
                s.Scroll += (o, e) => { if (ui.syncing) return; Push(); ui.RowMoved(Model); ShowValues(); };
                s.KeyDown += (o, e) => { if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right || e.KeyCode == Keys.Up || e.KeyCode == Keys.Down || e.KeyCode == Keys.PageUp || e.KeyCode == Keys.PageDown) ui.RowPressed(Model); };
                s.KeyUp += (o, e) => ui.RowReleased(Model);
                sliders[axis] = s;
                Controls.Add(s);
                string key = model.Name + ":" + "XYZ"[axis];
                var t = new SkinCheckBox { Width = 18, Height = 18, Checked = ui.IsTicked(key) };
                t.CheckedChanged += (o, e) => ui.SetTicked(key, t.Checked);
                ticks[axis] = t;
                Controls.Add(t);
            }
            Layout += (o, e) => {
                int w = ClientSize.Width;
                remove.Location = new Point(w - remove.Width - 6, 4);
                reset.Location = new Point(remove.Left - reset.Width - 4, 4);
                values.Location = new Point(6, 24);
                int sw = (w - 12) / 3;
                for (int i = 0; i < 3; i++) {
                    ticks[i].Location = new Point(6 + i * sw, 52);
                    sliders[i].Location = new Point(6 + i * sw + 20, 46); sliders[i].Width = sw - 24;
                }
            };
            PullFromModel();
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
            if (Model.Known && Model.RelKnown) own += string.Format("      from the hand  {0:0} / {1:0} / {2:0}", Model.RelX, Model.RelY, Model.RelZ);
            values.Text = own;
        }
    }
}
