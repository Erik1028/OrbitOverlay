using System;
using System.Drawing;
using System.Windows.Forms;
using Vortice.Direct2D1;
using Vortice.DXGI;

namespace Orbit;

// Desktop settings window: a DXGI swap chain that the shared SettingsView draws into.
// Closing it only hides it (Orbit keeps running in the tray).
public sealed class SettingsWindow : Form
{
    readonly Gpu _g;
    readonly Painter _p;
    readonly Ui _ui;
    public readonly SettingsView View;
    readonly Action _beforeDraw;
    readonly PointerState _ptr = new();
    IDXGISwapChain1 _swap;
    ID2D1Bitmap1 _target;
    public bool Dirty = true;
    public bool AllowClose;
    double _nextRefresh;        // when the page is next asked whether it shows anything new (SettingsView.LiveSig)
    double _wake = double.PositiveInfinity;    // a style's animation wants its next frame then (AnimWake.cs)
    int _liveSig;

    public SettingsWindow(Gpu g, Painter p, SettingsView view, Action beforeDraw, Icon icon)
    {
        _g = g; _p = p; View = view; _beforeDraw = beforeDraw;
        _ui = new Ui(p);
        View.OnClose95 = Hide;
        View.OnMinimize = () => WindowState = FormWindowState.Minimized;      // the Cyber header's own button (no Windows frame there)
        View.AskFile = (save, name, then) => BeginInvoke(new Action(() => AskFileDialog(save, name, then)));     // the watch editor's Share
        View.AskText = (title, prompt, text, then) => BeginInvoke(new Action(() => AskTextDialog(title, prompt, text, then)));     // the weather's place
        Text = "OrbitOverlay";
        Icon = icon;
        FormBorderStyle = FormBorderStyle.None;      // every style draws its own frame (ApplyStyle)
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(0x15, 0x17, 0x1C);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque, true);
        float s = DeviceDpi / 96f;
        ClientSize = new Size((int)(SettingsView.W * s), (int)(SettingsView.H * s));
    }

    float DpiScale => DeviceDpi / 96f;

    // ---- no Windows 11 frame in any style (Erik, 10-16: "the window frame breaks the design"): the 95 family draws its own caption,
    // which drags the window; Modern and Cyber their header, with their own minimise / close ----
    bool _classic;      // the style the window is laid out for
    bool _cyber;
    new bool StyleChanged => _classic != Theme.Classic || _cyber != (Theme.Cyber && !Theme.Classic);
    int _k = 1;         // whole-pixel enlargement of the 750 x 480 sheet

    int Scale95()
    {
        var wa = Screen.FromControl(this).WorkingArea;
        int k = Math.Max(1, (int)MathF.Floor(DpiScale + 0.5f));    // 100-125 %: 1995 size; 150-225 %: 2 x ...
        while (k > 1 && (SettingsView.W95W * k > wa.Width || SettingsView.W95H * k > wa.Height)) k--;
        return k;
    }

    void ApplyStyle()
    {
        _classic = Theme.Classic;
        _cyber = Theme.Cyber && !_classic;
        var centre = new Point(Left + Width / 2, Top + Height / 2);
        if (_classic)
        {
            _k = Scale95();
            FormBorderStyle = FormBorderStyle.None;
            ClientSize = new Size(SettingsView.W95W * _k, SettingsView.H95() * _k);
            int round = 1;      // DWMWCP_DONOTROUND: 1995 had square corners
            Native.DwmSetWindowAttribute(Handle, 33, ref round, 4);
        }
        else
        {
            FormBorderStyle = FormBorderStyle.None;
            ClientSize = new Size((int)(SettingsView.W * DpiScale), (int)(SettingsView.H * DpiScale));
            int round = _cyber ? 1 : 2;      // Cyber: square (its frame cuts its own corners); Modern: Windows 11's round ones
            Native.DwmSetWindowAttribute(Handle, 33, ref round, 4);
        }
        if (Visible && WindowState == FormWindowState.Normal)
        {
            // same centre, but never past the screen's edges (a taller Modern frame could hide its title bar)
            var wa = Screen.FromPoint(centre).WorkingArea;
            int x = Math.Clamp(centre.X - Width / 2, wa.Left, Math.Max(wa.Left, wa.Right - Width));
            int y = Math.Clamp(centre.Y - Height / 2, wa.Top, Math.Max(wa.Top, wa.Bottom - Height));
            Location = new Point(x, y);
        }
        Dirty = true;
    }

    float InputScale => _classic ? _k : DpiScale;

    // runs inside the first Show, before anything is on screen: start with the right frame and size
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        ApplyStyle();       // the frame's corners too (Modern's are not set before)
    }

    // without a Windows frame (95, Cyber) the taskbar button still minimises / restores it
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.Style |= 0x00020000 | 0x00080000;     // WS_MINIMIZEBOX | WS_SYSMENU
            cp.Style &= ~0x00010000;                // no WS_MAXIMIZEBOX: a double click on the header must not blow the sheet up to the screen
            return cp;
        }
    }

    const int WM_NCHITTEST = 0x84, HTCLIENT = 1, HTCAPTION = 2;
    const int WM_TIMER = 0x113, WM_ENTERMENULOOP = 0x211, WM_EXITMENULOOP = 0x212, WM_ENTERSIZEMOVE = 0x231, WM_EXITSIZEMOVE = 0x232;
    static readonly IntPtr ModalTimer = 0x4F52;

    // One frame of the app's loop. Moving this window by its title bar (or its system menu) runs Windows' own modal loop,
    // and the app's loop stops until the button is let go - in VR the laser lets it go from that loop, so everything froze
    // for good. A timer keeps the frames coming meanwhile.
    public Action ModalTick;

    // The watch editor's export / import: a Save / Open dialog. Asked from inside a frame (the view is drawing then), so it runs a
    // moment later from the message loop, outside the frame; the modal timer keeps VR's frames coming while it is up (ModalTick).
    void AskFileDialog(bool save, string name, Action<string> then)
    {
        string path = null;
        Native.SetTimer(Handle, ModalTimer, 10, IntPtr.Zero);
        try
        {
            try { System.IO.Directory.CreateDirectory(FaceShare.Folder); } catch { }
            using FileDialog d = save ? new SaveFileDialog { FileName = name, OverwritePrompt = true, DefaultExt = "json", Title = Lang.T("Export the watch page") }
                : new OpenFileDialog { CheckFileExists = true, Title = Lang.T("Import a watch page") };
            d.Filter = Lang.T("Watch page (*.json)|*.json|All files (*.*)|*.*");
            d.InitialDirectory = FaceShare.Folder;
            if (d.ShowDialog(this) == DialogResult.OK) path = d.FileName;
        }
        catch (Exception e) { Log.Warn("file dialog: " + e.Message); }
        finally { Native.KillTimer(Handle, ModalTimer); }
        try { then(path); } catch (Exception e) { Log.Warn("file dialog: " + e.Message); }
        Dirty = true;
    }

    // A text the settings ask for (the weather's place: the settings have no text box of their own) - a small Windows dialog, a moment
    // later from the message loop like the file dialogs, the modal timer keeping VR's frames coming. then(null): cancelled.
    void AskTextDialog(string title, string prompt, string text, Action<string> then)
    {
        string result = null;
        Native.SetTimer(Handle, ModalTimer, 10, IntPtr.Zero);
        try
        {
            using var font = new Font("Segoe UI", 9f);
            using var f = new Form
            {
                Text = Lang.T(title), FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false,
                StartPosition = FormStartPosition.CenterParent, AutoScaleMode = AutoScaleMode.Dpi, AutoScaleDimensions = new SizeF(96, 96),
                ClientSize = new Size(420, 132), Font = font,
            };
            var label = new Label { Text = Lang.T(prompt), Left = 12, Top = 10, Width = 396, Height = 36 };
            var box = new TextBox { Text = text ?? "", Left = 12, Top = 52, Width = 396 };
            var ok = new Button { Text = Lang.T("OK"), DialogResult = DialogResult.OK, Left = 252, Top = 92, Width = 75, Height = 26 };
            var cancel = new Button { Text = Lang.T("Cancel"), DialogResult = DialogResult.Cancel, Left = 333, Top = 92, Width = 75, Height = 26 };
            f.Controls.AddRange(new Control[] { label, box, ok, cancel });
            f.AcceptButton = ok;
            f.CancelButton = cancel;
            if (f.ShowDialog(this) == DialogResult.OK) result = box.Text;
        }
        catch (Exception e) { Log.Warn("text dialog: " + e.Message); }
        finally { Native.KillTimer(Handle, ModalTimer); }
        try { then(result); } catch (Exception e) { Log.Warn("text dialog: " + e.Message); }
        Dirty = true;
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_ENTERSIZEMOVE or WM_ENTERMENULOOP: Native.SetTimer(Handle, ModalTimer, 10, IntPtr.Zero); break;
            case WM_EXITSIZEMOVE or WM_EXITMENULOOP: Native.KillTimer(Handle, ModalTimer); break;
            case WM_TIMER when m.WParam == ModalTimer: ModalTick?.Invoke(); return;
        }
        base.WndProc(ref m);
        // the 95 caption bar moves the window like a real one (but not over its ? and X boxes)
        if (m.Msg == WM_NCHITTEST && _classic && (int)m.Result == HTCLIENT)
        {
            var pt = PointToClient(new Point((short)((long)m.LParam & 0xFFFF), (short)(((long)m.LParam >> 16) & 0xFFFF)));
            float x = pt.X / (float)_k, y = pt.Y / (float)_k;
            if (y >= 3 && y < 21 && x >= 3 && x < SettingsView.W95W - 5 - 16 - 2 - 16) m.Result = HTCAPTION;
        }
        // Modern / Cyber: the header drags the window wherever no control is
        else if (m.Msg == WM_NCHITTEST && !_classic && (int)m.Result == HTCLIENT)
        {
            var pt = PointToClient(new Point((short)((long)m.LParam & 0xFFFF), (short)(((long)m.LParam >> 16) & 0xFFFF)));
            float x = pt.X / DpiScale, y = pt.Y / DpiScale;
            if (y < SettingsView.CaptionH && !_ui.OverWidget(x, y)) m.Result = HTCAPTION;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int dark = 1;
        Native.DwmSetWindowAttribute(Handle, 20, ref dark, 4);   // DWMWA_USE_IMMERSIVE_DARK_MODE
        CreateSwapChain();
    }

    // a style change can make WinForms re-create the window: its swap chain goes with it
    protected override void OnHandleDestroyed(EventArgs e)
    {
        _target?.Dispose(); _target = null;
        _swap?.Dispose(); _swap = null;
        base.OnHandleDestroyed(e);
    }

    void CreateSwapChain()
    {
        _target?.Dispose(); _target = null;
        _swap?.Dispose(); _swap = null;
        var desc = new SwapChainDescription1
        {
            Width = (uint)Math.Max(1, ClientSize.Width), Height = (uint)Math.Max(1, ClientSize.Height),
            Format = Format.B8G8R8A8_UNorm, SampleDescription = new SampleDescription(1, 0),
            BufferUsage = Usage.RenderTargetOutput, BufferCount = 2,
            SwapEffect = SwapEffect.FlipDiscard, Scaling = Scaling.Stretch, AlphaMode = Vortice.DXGI.AlphaMode.Ignore,
        };
        _swap = _g.Dxgi.CreateSwapChainForHwnd(_g.Device, Handle, desc, null, null);
        _g.Dxgi.MakeWindowAssociation(Handle, WindowAssociationFlags.IgnoreAll);
    }

    void EnsureTarget()
    {
        if (_target != null) return;
        using var surf = _swap.GetBuffer<IDXGISurface>(0);
        _target = _g.CreateTargetBitmap(surf, false);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        if (_classic) ApplyStyle();
        else ClientSize = new Size((int)(SettingsView.W * DpiScale), (int)(SettingsView.H * DpiScale));
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_swap == null || ClientSize.Width < 1 || ClientSize.Height < 1) return;
        _target?.Dispose(); _target = null;
        _swap.ResizeBuffers(2, (uint)ClientSize.Width, (uint)ClientSize.Height, Format.B8G8R8A8_UNorm, SwapChainFlags.None);
        Dirty = true;
    }

    protected override void OnPaint(PaintEventArgs e) { Dirty = true; }
    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_ptr.Down && (e.Button & MouseButtons.Left) == 0) CancelDrag();     // the up was never delivered
        _ptr.Move(e.X, e.Y);
        if (_ui.NeedsRedrawForMove(_ptr, InputScale)) Dirty = true;
    }

    // Alt+Tab / another window taking the mouse mid-drag: no MouseUp comes, so the slider would stay
    // grabbed. Drop the press without a click.
    void CancelDrag()
    {
        _ptr.Down = false;
        _ptr.PressedLatch = false;
        Dirty = true;
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (_ptr.Down && !Capture) CancelDrag();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (_ptr.Down) CancelDrag();
    }
    protected override void OnMouseLeave(EventArgs e) { if (!_ptr.Down) _ptr.Inside = false; Dirty = true; }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _ptr.Move(e.X, e.Y); _ptr.Press(); Capture = true; Dirty = true;
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _ptr.Move(e.X, e.Y); _ptr.Release(); Capture = false; Dirty = true;
    }
    protected override void OnMouseWheel(MouseEventArgs e) { _ptr.Wheel += e.Delta / 120f; Dirty = true; }
    // typing in the window: the watch editor's palette search (SettingsView.TypeKey)
    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        if (View.TypeKey(e.KeyChar)) { e.Handled = true; Dirty = true; }
        else base.OnKeyPress(e);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!AllowClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    public void ShowAndFocus()
    {
        if (StyleChanged) ApplyStyle();      // the right frame before it appears
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        Dirty = true;
    }

    // Called from the main loop. Redraws on input, when a style's animation asks, and otherwise twice a second - only when a live
    // value shows differently (SettingsView.LiveSig; it drew and presented on every beat whatever it showed).
    public void Tick(double now)
    {
        if (!Visible || WindowState == FormWindowState.Minimized || _swap == null) return;
        if (StyleChanged) ApplyStyle();
        bool draw = Dirty || now >= _wake;
        if (!draw && now >= _nextRefresh)
        {
            _nextRefresh = now + 0.5;
            draw = View.LiveSig() != _liveSig;
        }
        if (!draw) return;
        Dirty = false;
        _nextRefresh = now + 0.5;
        _beforeDraw?.Invoke();
        AnimWake.Take();             // the previews' asks (the watch, the keyboard) are their own panels', not this window's
        EnsureTarget();
        if (_classic) View.Render95(_ui, _ptr, _target, _k, 0, 0, Theme.W95Teal);
        else
        {
            _ui.Begin(_ptr, DpiScale);
            _p.Begin(_target, Theme.Bg, DpiScale);
            View.Draw(_ui);
            _p.End();
            _ui.End();
        }
        _swap.Present(0, PresentFlags.None);
        _liveSig = View.LiveSig();
        _wake = AnimWake.Take();                                       // a style's animation wants its next frame (AnimWake.cs)
        if (_ui.Interacting || _ui.Changed || _ui.Animating || _ui.Pressed || _ui.Released) Dirty = true;
        // 95: busy in the background (the updater) - the cursor is the arrow with the hourglass, as 1995's was
        var cur = _classic && View.Busy95 ? Cursors.AppStarting : Cursors.Default;
        if (Cursor != cur) Cursor = cur;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _target?.Dispose(); _swap?.Dispose(); View.Dispose95(); }
        base.Dispose(disposing);
    }
}
