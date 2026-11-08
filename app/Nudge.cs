// Nudge.cs -- the controller buttons step the ticked sliders (2026-10-25, Tefa: "add tick boxes to each customizable hand part,
// so that when ticked, i don't have to actually slide the sliders, i can use buttons to + and - sliders, maybe X is -1, Y is -5
// and holding LG makes X and Y repeat press instantly. A and B and RG to do the same respectively for + on the sliders").
//
// The buttons come from Window View (github.com/TefMeister/window-view-vr), whose OpenXR layer reads the controllers while its
// CONTROL mode is on (left stick click: the game's hands are frozen then, so the buttons reach nobody else). Its shared block is
// read here, and while any box is ticked this app writes a claim into it, so Window View leaves A, Y and the grips alone
// (no right-click, no recentre, no carrying) for as long as the claim is fresh.
//   X = -1 degree, Y = -5, A = +1, B = +5 on every ticked slider (a ticked finger bar: on all its joints, on its axis).
//   Held left grip: X and Y repeat at once and keep repeating while held; held right grip: the same for A and B.
//
// 2026-11-08 (Tefa: "have this active only on the window that i clicked on last ... just clicking on the window should be
// enough, without needing to actually check a box"): every helper window with this file (Hand Poser, Grip Zones, Gun Poser,
// Animation & Timing) shares one small block, Local\FlatToVR_SliderOwner_v1 {int pid, int unused, ulong beat}. A click
// anywhere in a helper window (the real mouse, or Window View's clicks, which it posts straight to the window under the
// pointer and so pass through this window's own message queue) makes that window the OWNER: only the owner's ticked sliders
// step and only the owner claims the buttons from Window View. The owner keeps its beat fresh while it runs and lets go when
// it closes; with no fresh owner (nothing clicked yet, or the owner gone) every helper steps, as before.
using System;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Windows.Forms;

class Nudge : IDisposable {
    // Window View's shared block (window-view-vr shared.h, header v2 of 636 bytes)
    const string MAPPING = @"Local\FlatToVR_WindowView_v2";
    const int HEADER = 636, CTRL_MODE = 536, SQUEEZE = 576, BUTTONS = 584, LSQUEEZE = 620, BUTTON_CLAIM = 628;
    const int B_A = 1, B_Y = 2, B_B = 4, B_X = 8;
    const float GRIP_DOWN = 0.6f;
    public const int REPEAT_MS = 60;   // held grip: a step every 60 ms (about 16 a second), the first at once
    MemoryMappedFile file;
    MemoryMappedViewAccessor view;
    int lastOpenTry = -100000, lastButtons;
    readonly int[] lastStep = new int[4];   // per button: when its last repeat fired
    public bool Linked { get { return view != null; } }

    const string OWNER_MAPPING = @"Local\FlatToVR_SliderOwner_v1";
    const int OWNER_SIZE = 16, OWNER_PID = 0, OWNER_BEAT = 8, OWNER_STALE_MS = 3000;
    MemoryMappedFile ownerFile;
    MemoryMappedViewAccessor ownerView;
    readonly int myPid = Process.GetCurrentProcess().Id;
    public bool Owner { get; private set; }   // this window answers the buttons now (the last helper clicked, or no fresh owner)

    public Nudge() { Application.AddMessageFilter(new ClickWatch(this)); }

    class ClickWatch : IMessageFilter {   // any mouse button down in this app's windows (client area or title bar)
        readonly Nudge n;
        public ClickWatch(Nudge n) { this.n = n; }
        public bool PreFilterMessage(ref Message m) {
            int k = m.Msg;
            if (k == 0x201 || k == 0x203 || k == 0x204 || k == 0x207 || k == 0xA1 || k == 0xA4) n.TakeOwnership();
            return false;
        }
    }
    bool OpenOwner() {
        if (ownerView != null) return true;
        try {
            ownerFile = MemoryMappedFile.CreateOrOpen(OWNER_MAPPING, OWNER_SIZE);
            ownerView = ownerFile.CreateViewAccessor(0, OWNER_SIZE);
            return true;
        } catch (Exception) { CloseOwner(); return false; }
    }
    void CloseOwner() {
        if (ownerView != null) { ownerView.Dispose(); ownerView = null; }
        if (ownerFile != null) { ownerFile.Dispose(); ownerFile = null; }
    }
    public void TakeOwnership() {
        if (!OpenOwner()) return;
        try { ownerView.Write(OWNER_PID, myPid); ownerView.Write(OWNER_BEAT, GetTickCount64()); } catch (Exception) { CloseOwner(); }
    }
    bool IsOwner() {   // also keeps the owner's beat fresh
        if (!OpenOwner()) return true;
        try {
            int pid = ownerView.ReadInt32(OWNER_PID);
            ulong beat = ownerView.ReadUInt64(OWNER_BEAT), now = GetTickCount64();
            if (pid == myPid) { ownerView.Write(OWNER_BEAT, now); return true; }
            return pid == 0 || now - beat > OWNER_STALE_MS;
        } catch (Exception) { CloseOwner(); return true; }
    }

    [DllImport("kernel32.dll")] static extern ulong GetTickCount64();

    bool Open() {
        if (view != null) return true;
        if (Environment.TickCount - lastOpenTry < 2000) return false;
        lastOpenTry = Environment.TickCount;
        try {
            file = MemoryMappedFile.OpenExisting(MAPPING, MemoryMappedFileRights.ReadWrite);
            view = file.CreateViewAccessor(0, HEADER);
            return true;
        } catch (Exception) { Close(); return false; }   // Window View not running, or an older one
    }
    void Close() {
        if (view != null) { view.Dispose(); view = null; }
        if (file != null) { file.Dispose(); file = null; }
    }

    // once a tick (about every 15 ms): returns the steps (in degrees) to apply now, 0 = none; `anyTicked` claims the buttons
    public double Poll(bool anyTicked, out bool pressStarted) {
        pressStarted = false;
        Owner = IsOwner();
        if (!Open()) return 0;
        int buttons; float rg, lg; bool ctrl;
        try {
            ctrl = view.ReadInt32(CTRL_MODE) != 0;
            buttons = view.ReadInt32(BUTTONS);
            rg = view.ReadSingle(SQUEEZE);
            lg = view.ReadSingle(LSQUEEZE);
            if (anyTicked && ctrl && Owner) view.Write(BUTTON_CLAIM, GetTickCount64());
        } catch (Exception) { Close(); return 0; }
        if (!anyTicked || !ctrl || !Owner) { lastButtons = buttons; return 0; }
        double total = 0;
        int now = Environment.TickCount;
        int[] bits = { B_X, B_Y, B_A, B_B };
        double[] steps = { -1, -5, +1, +5 };
        for (int i = 0; i < 4; i++) {
            bool down = (buttons & bits[i]) != 0, was = (lastButtons & bits[i]) != 0;
            bool repeat = (i < 2 ? lg : rg) > GRIP_DOWN;   // X / Y repeat with the left grip, A / B with the right
            if (down && !was) { total += steps[i]; lastStep[i] = now; pressStarted = true; }
            else if (down && repeat && now - lastStep[i] >= REPEAT_MS) { total += steps[i]; lastStep[i] = now; }
        }
        lastButtons = buttons;
        return total;
    }

    public void Dispose() {
        try { if (OpenOwner() && ownerView.ReadInt32(OWNER_PID) == myPid) ownerView.Write(OWNER_PID, 0); } catch (Exception) { }   // let go: the others answer again
        CloseOwner();
        Close();
    }
}
