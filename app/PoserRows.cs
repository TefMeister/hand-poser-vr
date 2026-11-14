// PoserRows.cs -- one joint row of the Hand Poser window: name, values, three sliders, its own undo / redo, RESET, remove. Moved
// out of PoserUi.cs unchanged on 2026-11-14 (the 800-line rule), when the row got its own undo and redo (RowHistory.cs).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

partial class PoserUi {
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
                        int c = HandAxes.Column(Model.Name, i);   // 2026-11-09: the thumb's tip shows its curl on the right, as the fingers
                        ticks[i].Location = new Point(left + c * sw, 7);
                        sliders[i].Location = new Point(left + c * sw + 32, 2); sliders[i].Width = sw - 38;
                    }
                    return;
                }
                remove.Location = new Point(w - remove.Width - 6, 4);
                reset.Location = new Point(remove.Left - reset.Width - 4, 4);
                values.Location = new Point(6, 24);
                int sw2 = (w - 12) / 3;
                for (int i = 0; i < 3; i++) {
                    int c = HandAxes.Column(Model.Name, i);
                    ticks[i].Location = new Point(6 + c * sw2, 47);
                    sliders[i].Location = new Point(6 + c * sw2 + 32, 46); sliders[i].Width = sw2 - 36;
                }
            };
            PullFromModel();
        }

        // 2026-11-08 (Tefa: left end = in / left / turn left, right end = out / right / turn right, on every slider): each axis
        // says what it does on this joint, measured in the game (HandAxes.cs), and shows its number turned round where + would
        // sit on the wrong end. The stored angles keep their meaning. (Was: IN / OUT on the finger's bend axis, -X / +X on the rest.)
        public void SetBendAxis(string axis) {
            for (int i = 0; i < 3; i++) {
                string l, r; bool f;
                HandAxes.Ends(Model.Name, i, out l, out r, out f);
                sliders[i].LeftText = l; sliders[i].RightText = r;
                sliders[i].Invalidate();
            }
        }
        double Sign(int axis) { return HandAxes.Sign(Model.Name, axis); }

        void Push() {
            Model.X = Sign(0) * sliders[0].Value / (double)SLIDER_STEPS_PER_DEG;
            Model.Y = Sign(1) * sliders[1].Value / (double)SLIDER_STEPS_PER_DEG;
            Model.Z = Sign(2) * sliders[2].Value / (double)SLIDER_STEPS_PER_DEG;
            Model.Known = true; Model.Moved = true;
        }

        public void PullFromModel() {
            ui.syncing = true;
            foreach (var s in sliders) s.Enabled = Model.Known;
            if (Model.Known) {
                sliders[0].Value = Clamp((int)Math.Round(Sign(0) * Model.X * SLIDER_STEPS_PER_DEG));
                sliders[1].Value = Clamp((int)Math.Round(Sign(1) * Model.Y * SLIDER_STEPS_PER_DEG));
                sliders[2].Value = Clamp((int)Math.Round(Sign(2) * Model.Z * SLIDER_STEPS_PER_DEG));
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
