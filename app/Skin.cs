// Skin.cs -- the look shared by Hand Poser and Grab Zones (2026-10-25; the same file in both: hand-poser-vr/app/Skin.cs and
// visceral-re8-vr/dev-archive/native/zones-app/app/Skin.cs, keep them the same).
//
//   FancySlider  a slider drawn by us. The stock Windows TrackBar paints itself white for a moment before its dark colours, and
//                Window View photographs the window many times a second (PrintWindow), so the white moment showed in the
//                headset and on the desktop as random flicker (Tefa: "a quick white flicker, that's on the hand poser as well").
//                This one paints in one go into a buffer: a rounded track, a glowing gradient fill (from zero on a signed
//                slider), quarter dots, and a round thumb that swells and glows while held. Click anywhere to jump and drag,
//                the wheel and the arrow keys step, Page Up / Down take big steps, Home / End go to the ends.
//   Backdrop     the window's background: the app's own icon, pixelated and faint, behind everything (Tefa: "make the window
//                background have a pixelated image of the ... icon").
//   SkinPanel    a panel that paints the backdrop at its own place in the window, then its Tint (a see-through colour) over it.
//                Children with a Transparent BackColor show it through.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

static class Backdrop {
    const int PIXELS = 30;          // the icon is shrunk to this many pixels across, then blown up square-edged
    const float STRENGTH = 0.42f;   // how strongly it shows through the dark background (0 = not at all)
    const float FILL = 0.86f;       // its size as a share of the window's smaller side

    static Bitmap tiny;             // the shrunken icon
    static Bitmap page;             // the whole window's background at its current size
    static Color back;
    static Form owner;

    public static void Init(Form form, string iconPath, Color background) {
        owner = form; back = background;
        try {
            using (var big = LargestPicture(iconPath)) {
                tiny = new Bitmap(PIXELS, PIXELS, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(tiny)) {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(big, new Rectangle(0, 0, PIXELS, PIXELS));
                }
            }
        } catch (Exception) { tiny = null; }
        form.Resize += (s, e) => { Rebuild(); form.Invalidate(true); };
        Rebuild();
    }

    // The biggest picture inside an .ico. Its large sizes are stored as PNG, which .NET Framework's Icon class draws blank, so
    // the file's directory is read and a PNG entry is opened as an image; an old-style entry falls back to the Icon class.
    static Bitmap LargestPicture(string path) {
        byte[] f = System.IO.File.ReadAllBytes(path);
        int count = BitConverter.ToUInt16(f, 4), best = -1, bestSide = 0;
        for (int i = 0; i < count; i++) {
            int e = 6 + 16 * i, side = f[e] == 0 ? 256 : f[e];
            if (side > bestSide) { bestSide = side; best = e; }
        }
        if (best < 0) throw new System.IO.InvalidDataException("no pictures in " + path);
        int size = BitConverter.ToInt32(f, best + 8), at = BitConverter.ToInt32(f, best + 12);
        bool png = f[at] == 0x89 && f[at + 1] == 0x50 && f[at + 2] == 0x4E && f[at + 3] == 0x47;
        if (png) using (var ms = new System.IO.MemoryStream(f, at, size)) using (var img = Image.FromStream(ms)) return new Bitmap(img);
        using (var ico = new Icon(path, bestSide, bestSide)) return ico.ToBitmap();
    }

    static void Rebuild() {
        if (owner == null) return;
        Size sz = owner.ClientSize;
        if (sz.Width < 1 || sz.Height < 1) return;
        var b = new Bitmap(sz.Width, sz.Height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(b)) {
            g.Clear(back);
            if (tiny != null) {
                int side = (int)(Math.Min(sz.Width, sz.Height) * FILL);
                side -= side % PIXELS;   // whole pixels, so every square is the same size
                var dst = new Rectangle((sz.Width - side) / 2, (sz.Height - side) / 2, side, side);
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                var cm = new ColorMatrix { Matrix33 = STRENGTH };
                using (var ia = new ImageAttributes()) {
                    ia.SetColorMatrix(cm);
                    g.DrawImage(tiny, dst, 0, 0, PIXELS, PIXELS, GraphicsUnit.Pixel, ia);
                }
            }
        }
        var old = page;
        page = b;
        if (old != null) old.Dispose();
    }

    // paint the part of the background that lies under control c
    public static void PaintUnder(Control c, Graphics g) {
        if (page == null || owner == null || !owner.IsHandleCreated || !c.IsHandleCreated) { g.Clear(back); return; }
        Point off = owner.PointToClient(c.PointToScreen(Point.Empty));
        g.DrawImageUnscaled(page, -off.X, -off.Y);
    }
}

class SkinPanel : Panel {
    Color tint = Color.Transparent;
    public Color Tint { get { return tint; } set { tint = value; Invalidate(true); } }

    public SkinPanel() {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        // a scrolling panel moves its pixels; the backdrop stays put, so repaint after every scroll
        Scroll += (s, e) => Invalidate(true);
        MouseWheel += (s, e) => Invalidate(true);
    }

    protected override void OnPaintBackground(PaintEventArgs e) {
        Backdrop.PaintUnder(this, e.Graphics);
        if (tint.A > 0) using (var br = new SolidBrush(tint)) e.Graphics.FillRectangle(br, ClientRectangle);
    }
}

// a label that never flickers while its text changes four times a second over the backdrop
class SkinLabel : Label {
    public SkinLabel() {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }
}

class FancySlider : Control {
    int min = 0, max = 100, val = 0;
    bool hot, dragging;
    public int SmallChange = 1, LargeChange = 10;
    public bool Signed;   // the fill runs from zero (the middle) to the thumb, and a notch marks zero

    public static readonly Color TrackBack = Color.FromArgb(18, 20, 27), TrackEdge = Color.FromArgb(78, 84, 106);
    public static readonly Color FillA = Color.FromArgb(56, 196, 232), FillB = Color.FromArgb(150, 108, 245);
    public static readonly Color ThumbFace = Color.FromArgb(244, 246, 252), Off = Color.FromArgb(92, 96, 112);

    public event EventHandler ValueChanged;   // any change, by the user or by code
    public event EventHandler Scroll;         // a change by the user only
    public event Action Pressed, Released;    // the mouse took hold / let go

    public FancySlider() {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Height = 32;
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    public int Minimum { get { return min; } set { min = value; if (max < min) max = min; Value = val; Invalidate(); } }
    public int Maximum { get { return max; } set { max = value; if (min > max) min = max; Value = val; Invalidate(); } }
    public int Value {
        get { return val; }
        set { int v = Math.Max(min, Math.Min(max, value)); if (v == val) return; val = v; Invalidate(); if (ValueChanged != null) ValueChanged(this, EventArgs.Empty); }
    }

    void UserSet(int v) {
        int before = val;
        Value = v;
        if (val != before && Scroll != null) Scroll(this, EventArgs.Empty);
    }

    const int PAD = 14, TRACK_H = 8, THUMB_R = 9, THUMB_R_HOT = 11;
    int Left0 { get { return PAD; } }
    int Width0 { get { return Math.Max(1, ClientSize.Width - 2 * PAD); } }
    float XOf(int v) { return Left0 + (max == min ? 0f : (v - min) * (float)Width0 / (max - min)); }
    int ValueAt(int x) { return max == min ? min : min + (int)Math.Round((x - Left0) * (double)(max - min) / Width0); }

    static GraphicsPath Pill(RectangleF r) {
        var p = new GraphicsPath();
        float d = Math.Min(r.Height, r.Width);
        if (d <= 0) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 90, 180);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 180);
        p.CloseFigure();
        return p;
    }

    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float cy = ClientSize.Height / 2f;
        var track = new RectangleF(Left0, cy - TRACK_H / 2f, Width0, TRACK_H);
        bool on = Enabled;

        using (var p = Pill(track)) {
            using (var br = new SolidBrush(TrackBack)) g.FillPath(br, p);
            using (var pen = new Pen(TrackEdge, 1f)) g.DrawPath(pen, p);
        }
        // quarter dots under the track
        using (var dot = new SolidBrush(Color.FromArgb(on ? 110 : 50, TrackEdge)))
            for (int q = 1; q < 4; q++) { float x = Left0 + Width0 * q / 4f; g.FillEllipse(dot, x - 1.5f, cy + TRACK_H / 2f + 3, 3, 3); }

        float tx = XOf(val);
        float from = Signed && min < 0 && max > 0 ? XOf(0) : Left0;
        float a = Math.Min(from, tx), b = Math.Max(from, tx);
        if (b - a >= 1f) {
            var fill = new RectangleF(a, track.Y + 1, b - a, TRACK_H - 2);
            using (var p = Pill(fill)) {
                if (on) {
                    using (var glow = new Pen(Color.FromArgb(hot || dragging ? 90 : 50, FillA), 6f)) g.DrawPath(glow, p);
                    using (var br = new LinearGradientBrush(new RectangleF(Left0 - 1, 0, Width0 + 2, 1), FillA, FillB, LinearGradientMode.Horizontal)) g.FillPath(br, p);
                } else using (var br = new SolidBrush(Off)) g.FillPath(br, p);
            }
        }
        if (Signed && min < 0 && max > 0) {   // the zero notch
            float zx = XOf(0);
            using (var pen = new Pen(Color.FromArgb(on ? 200 : 90, ThumbFace), 2f)) g.DrawLine(pen, zx, track.Y - 4, zx, track.Bottom + 4);
        }

        float r = (hot || dragging) && on ? THUMB_R_HOT : THUMB_R;
        if (on && (hot || dragging || Focused)) {
            using (var halo = new SolidBrush(Color.FromArgb(dragging ? 80 : 45, FillB))) g.FillEllipse(halo, tx - r - 6, cy - r - 6, 2 * r + 12, 2 * r + 12);
        }
        var thumb = new RectangleF(tx - r, cy - r, 2 * r, 2 * r);
        using (var shade = new SolidBrush(Color.FromArgb(90, 0, 0, 0))) g.FillEllipse(shade, thumb.X + 1, thumb.Y + 2, thumb.Width, thumb.Height);
        using (var face = new LinearGradientBrush(thumb, on ? ThumbFace : Off, on ? Color.FromArgb(196, 204, 226) : Color.FromArgb(70, 74, 88), LinearGradientMode.Vertical))
            g.FillEllipse(face, thumb);
        Color ring = on ? (tx - Left0 < Width0 / 2f ? FillA : FillB) : TrackEdge;
        using (var pen = new Pen(ring, 3f)) g.DrawEllipse(pen, thumb.X + 1.5f, thumb.Y + 1.5f, thumb.Width - 3, thumb.Height - 3);
        using (var pip = new SolidBrush(on ? Color.FromArgb(60, 64, 82) : TrackEdge)) g.FillEllipse(pip, tx - 2, cy - 2, 4, 4);
    }

    protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnMouseDown(MouseEventArgs e) {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || !Enabled) return;
        Focus();
        dragging = true;
        Capture = true;
        if (Pressed != null) Pressed();
        UserSet(ValueAt(e.X));
    }
    protected override void OnMouseMove(MouseEventArgs e) {
        base.OnMouseMove(e);
        if (dragging) UserSet(ValueAt(e.X));
    }
    protected override void OnMouseUp(MouseEventArgs e) {
        base.OnMouseUp(e);
        if (!dragging) return;
        dragging = false;
        Capture = false;
        Invalidate();
        if (Released != null) Released();
    }
    protected override void OnMouseCaptureChanged(EventArgs e) {
        base.OnMouseCaptureChanged(e);
        if (dragging && !Capture) { dragging = false; Invalidate(); if (Released != null) Released(); }
    }
    protected override void OnMouseWheel(MouseEventArgs e) {
        base.OnMouseWheel(e);
        if (!Enabled) return;
        if (Pressed != null) Pressed();
        UserSet(val + Math.Sign(e.Delta) * SmallChange);
        if (Released != null) Released();
    }

    protected override bool IsInputKey(Keys k) {
        switch (k & Keys.KeyCode) {
        case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down: case Keys.PageUp: case Keys.PageDown: case Keys.Home: case Keys.End: return true;
        }
        return base.IsInputKey(k);
    }
    protected override void OnKeyDown(KeyEventArgs e) {
        base.OnKeyDown(e);
        if (!Enabled) return;
        switch (e.KeyCode) {
        case Keys.Left: case Keys.Down: UserSet(val - SmallChange); break;
        case Keys.Right: case Keys.Up: UserSet(val + SmallChange); break;
        case Keys.PageDown: UserSet(val - LargeChange); break;
        case Keys.PageUp: UserSet(val + LargeChange); break;
        case Keys.Home: UserSet(min); break;
        case Keys.End: UserSet(max); break;
        }
    }
}
