using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;

namespace Orbit;

// The dock's controls, for whoever shows them - the floating Hexpad panel (DockPanel) and the watch's opened dock (WristDock.cs):
// what the laser presses and drags, what that sends to Hexpad (through the app's one HexpadLink), and the values shown meanwhile.
// A view puts where its controls are (Hit, Cards, Track: in its texture's pixels) while it draws and hands the laser in; each view
// has its own, so a key held on the watch is not one held on the panel. Keys and buttons go down and up (Hexpad's hold and double
// tap work); what could end the VR session (lock, sleep, empty the bin) asks first (Confirm). A slider Hexpad can read and set
// ("abs") sets the value as it is dragged; any other turn is a "jog" that turns the knob a detent per bit of drag. The stick over a
// knob moves it too. Without a link (--snapshot) nothing goes anywhere: what would have been sent is written to Sent.
public sealed class DockInput
{
    public enum Slide { None, Abs, Jog }

    readonly HexpadLink _link;
    readonly ConfigStore _cfg;
    public HexpadLink.Snapshot Snap = new();
    public string Hover, Pressed, Confirm;
    public readonly Dictionary<string, RectangleF> Hit = new();     // control -> rect in texture pixels ("enc1:s" = its slider)
    public readonly Dictionary<string, RectangleF> Cards = new();   // knob -> its whole row (the stick over it moves the slider)
    public readonly Dictionary<string, RectangleF> Track = new();   // knob -> where its thumb's centre runs (texture pixels)
    public bool Dirty;                                              // the picture changed: the view draws again (TakeDirty)
    public readonly List<string> Sent = new();                      // no link: what would have gone to Hexpad

    string _scrollId;
    float _scrollAcc;
    double _wokeAt = -10;

    // the slider being dragged
    public string Drag;
    string _dragKind;
    bool _dragAbs;
    float _dragGrab;            // abs: where on the thumb it was taken (px from its centre); jog: where the drag began
    public float JogOff;        // jog: how far it is dragged (px)
    public int JogSent;         // jog: detents sent in this drag
    // abs: the value we set is shown instead of Hexpad's while dragging and for a moment after (its read-back lags)
    readonly Dictionary<string, float> _local = new();
    public readonly Dictionary<string, double> HoldUntil = new();
    readonly Dictionary<string, (double at, string kind)> _stickEnd = new();   // the stick moved it: final then

    const double Hold = 0.6, StickFinal = 0.4;
    const float StickStep = 0.02f;

    public DockInput(HexpadLink link, ConfigStore cfg) { _link = link; _cfg = cfg; }

    DockConfig C => _cfg.Data.Dock;

    public bool TakeDirty()
    {
        bool d = Dirty;
        Dirty = false;
        return d;
    }

    // ---- to Hexpad (or to Sent) ----

    void Input(string id, bool down) { if (_link != null) _link.Input(id, down); else Sent.Add($"{id} {(down ? "down" : "up")}"); }
    void Set(string id, string kind, float v, bool final) { if (_link != null) _link.Set(id, kind, v, final); else Sent.Add($"{id} set {v:0.00}{(final ? " final" : "")}"); }
    void Jog(string id, int n) { if (_link != null) _link.Jog(id, n); else Sent.Add($"{id} jog {n}"); }
    void Wake() { if (_link != null) _link.Wake(); else Sent.Add("wake"); }

    // the stick's final value once it stops; Hexpad's own value again once a hold runs out
    public void Timers(double now)
    {
        if (_stickEnd.Count > 0)
            foreach (var id in new List<string>(_stickEnd.Keys))
            {
                var (at, kind) = _stickEnd[id];
                if (now < at) continue;
                _stickEnd.Remove(id);
                if (_local.TryGetValue(id, out float v)) Set(id, kind, v, true);
                HoldUntil[id] = now + Hold;
            }
        if (HoldUntil.Count > 0)
            foreach (var id in new List<string>(HoldUntil.Keys))
                if (now >= HoldUntil[id] && Drag != id && !_stickEnd.ContainsKey(id))
                {
                    HoldUntil.Remove(id);
                    _local.Remove(id);
                    Dirty = true;
                }
    }

    // ---- the laser ----

    public bool Live => Snap.State == HexpadLink.LinkState.Ok;
    bool CanPress => Live && C.Input && Confirm == null;
    public bool Busy => Pressed != null || Drag != null;

    // a view starts drawing: where its controls were is forgotten (it puts them again)
    public void BeginLayout()
    {
        Hit.Clear();
        Cards.Clear();
        Track.Clear();
    }

    string HitTest(Vector2 px)
    {
        foreach (var (id, r) in Hit)
            if (r.Contains(px.X, px.Y)) return id;
        return null;
    }

    string CardAt(Vector2 px)
    {
        foreach (var (id, r) in Cards)
            if (r.Contains(px.X, px.Y)) return id;
        return null;
    }

    static string Base(string id) => id != null && id.Contains(':') ? id[..id.IndexOf(':')] : id;
    public static string S(string id) => Ui.Id(id, ":s");          // a knob's slider (the push is the knob's own id)

    // what a knob's slider does now
    public Slide TypeOf(string id)
    {
        var c = Ctl(id);
        if (c == null || string.IsNullOrEmpty(c.Slider)) return Slide.None;
        if (c.Slider == "abs") return Snap.Api >= 2 && !string.IsNullOrEmpty(c.Kind) ? Slide.Abs : Slide.Jog;
        return Slide.Jog;
    }

    public bool Off(string id) => Snap.Values.TryGetValue(id, out var v) && v.Off;

    // an abs slider's value: ours while dragging and just after, else Hexpad's (null = it does not know)
    public float? AbsValue(string id, double now)
    {
        if ((Drag == id || HoldUntil.TryGetValue(id, out var t) && now < t) && _local.TryGetValue(id, out float mine)) return mine;
        return Snap.Values.TryGetValue(id, out var v) ? v.V : null;
    }

    // drag this far for one detent; pages and tracks are whole steps, so they want a longer pull
    public float Detent(string id) => Ctl(id)?.Kind is "page" or "media" ? 60 : 24;

    void Press(string id)
    {
        if (Snap.Controls.TryGetValue(id, out var c) && c.Danger)
        {
            Confirm = id;               // nothing is sent until it is confirmed
            return;
        }
        Input(id, true);
        Pressed = id;
    }

    // the laser lets go (or leaves, or the view goes): the key comes up, a drag ends, the stick's value is final
    public void ReleasePress()
    {
        if (Pressed != null) Input(Pressed, false);
        Pressed = null;
        EndDrag();
        if (_stickEnd.Count > 0)
        {
            foreach (var (id, (_, kind)) in _stickEnd)
                if (_local.TryGetValue(id, out float v)) Set(id, kind, v, true);
            foreach (var id in _stickEnd.Keys) HoldUntil[id] = Clock.Now + Hold;
            _stickEnd.Clear();
        }
    }

    void BeginDrag(string id, Vector2 px)
    {
        var t = TypeOf(id);
        if (t == Slide.None || t == Slide.Abs && Off(id) || !Track.TryGetValue(id, out var tr)) return;
        Drag = id;
        _dragKind = Ctl(id)?.Kind;          // a page change mid-drag must not turn a volume drag into something else
        _dragAbs = t == Slide.Abs;
        _stickEnd.Remove(id);
        if (_dragAbs)
        {
            // taken by the thumb it keeps where it was held; anywhere else on the track it jumps there
            float? cur = AbsValue(id, Clock.Now);
            float grab = Theme.Classic ? 14 : 24;
            _dragGrab = 0;
            if (cur is float v && MathF.Abs(px.X - (tr.X + tr.Width * v)) <= grab) _dragGrab = px.X - (tr.X + tr.Width * v);
        }
        else
        {
            _dragGrab = px.X;
            JogOff = 0;
            JogSent = 0;
        }
        DragTo(px);
    }

    void DragTo(Vector2 px)
    {
        if (Drag == null || !Track.TryGetValue(Drag, out var tr)) return;
        if (_dragAbs)
        {
            float v = Math.Clamp((px.X - _dragGrab - tr.X) / tr.Width, 0, 1);
            _local[Drag] = v;
            HoldUntil[Drag] = double.MaxValue;
            Set(Drag, _dragKind, v, false);
        }
        else
        {
            JogOff = px.X - _dragGrab;
            int n = (int)(JogOff / Detent(Drag));         // towards zero: a detent per full step of drag
            if (n != JogSent) { Jog(Drag, n - JogSent); JogSent = n; }
        }
        Dirty = true;
    }

    void EndDrag()
    {
        if (Drag == null) return;
        if (_dragAbs)
        {
            if (_local.TryGetValue(Drag, out float v)) Set(Drag, _dragKind, v, true);
            HoldUntil[Drag] = Clock.Now + Hold;
        }
        Drag = null;
        JogOff = 0;
        Dirty = true;
    }

    // the laser moved over the view (texture px): a drag follows it, the hover moves; the dock's idle screen swallows the first
    // press, so it is woken as soon as the laser arrives
    public void Move(Vector2 px)
    {
        if (Drag != null) DragTo(px);
        string hov = Drag != null ? S(Drag) : CanPress ? HitTest(px) : null;
        if (hov != Hover) { Hover = hov; Dirty = true; }
        if (Snap.Ambient && C.Input && Clock.Now - _wokeAt > 2)
        {
            _wokeAt = Clock.Now;
            Wake();
        }
    }

    public void Leave()
    {
        ReleasePress();
        Hover = null;
        Dirty = true;
    }

    // the trigger: a key or a button goes down (up on Up), a slider's track starts a drag
    public void Down(Vector2 px)
    {
        if (CanPress && HitTest(px) is { } id)
        {
            if (id.EndsWith(":s")) BeginDrag(Base(id), px);
            else Press(id);
        }
        Dirty = true;
    }

    public void Up() { ReleasePress(); Dirty = true; }

    // the stick over a knob's row: an abs slider moves 2 % per notch (its final value once the stick rests),
    // a jog one turns a detent per notch
    public void Scroll(Vector2 px, float notches)
    {
        if (!CanPress || Drag != null) return;
        string id = CardAt(px);
        if (id == null) return;
        var t = TypeOf(id);
        if (t == Slide.Abs)
        {
            if (Off(id) || AbsValue(id, Clock.Now) is not float cur) return;
            string kind = Ctl(id).Kind;
            float v = Math.Clamp(cur + StickStep * notches, 0, 1);
            _local[id] = v;
            HoldUntil[id] = double.MaxValue;
            _stickEnd[id] = (Clock.Now + StickFinal, kind);
            Set(id, kind, v, false);
            Dirty = true;
            return;
        }
        if (t != Slide.Jog) return;
        if (_scrollId != id) { _scrollId = id; _scrollAcc = 0; }
        _scrollAcc += notches;
        int n = 0;
        while (_scrollAcc >= 1) { n++; _scrollAcc -= 1; }
        while (_scrollAcc <= -1) { n--; _scrollAcc += 1; }
        if (n != 0) Jog(id, n);
    }

    // the question for a dangerous key / push: yes sends it (down and up), no forgets it
    public void ConfirmYes()
    {
        if (Confirm == null) return;
        Input(Confirm, true);
        Input(Confirm, false);
        Confirm = null;
        Dirty = true;
    }

    public void ConfirmNo() { Confirm = null; Dirty = true; }

    // without VR (--docktest, --snapshot): a pointer over a control, a pressed one, a pending confirmation, a slider being dragged
    // (drag = the knob; dragAt = the value for an abs slider, the pull in texture px for a jog one). Only for drawing: nothing is
    // sent. After the drawing: AfterPose.
    public void Pose(HexpadLink.Snapshot s, string hover = null, string pressed = null, string confirm = null, string drag = null, float dragAt = 0)
    {
        Snap = s; Hover = hover; Pressed = pressed; Confirm = confirm;
        _local.Clear(); HoldUntil.Clear(); _stickEnd.Clear();
        Drag = null; JogOff = 0; JogSent = 0;
        if (drag != null)
        {
            Drag = drag;
            _dragAbs = TypeOf(drag) == Slide.Abs;
            _dragKind = Ctl(drag)?.Kind;
            if (_dragAbs) { _local[drag] = Math.Clamp(dragAt, 0, 1); HoldUntil[drag] = double.MaxValue; }
            else { JogOff = dragAt; JogSent = (int)(dragAt / Detent(drag)); }
            Hover ??= S(drag);
        }
    }

    public void AfterPose()
    {
        Drag = null; JogOff = 0; JogSent = 0;
        _local.Clear(); HoldUntil.Clear();
    }

    // ---- what the views write ----

    // where on the dock: made again only when the page or the folder changes (a new string every frame before)
    string _where;
    (bool inFolder, string name, int page, int pages) _whereFor;

    public string Where()
    {
        var s = Snap;
        var key = s.InFolder ? (true, s.FolderName, s.FolderPage, s.FolderPages) : (false, s.PageName, s.Page, s.Pages);
        if (_where != null && key == _whereFor) return _where;
        _whereFor = key;
        if (s.InFolder)
            return _where = s.FolderPages > 1 ? $"{s.FolderName} · {s.FolderPage + 1}/{s.FolderPages}" : s.FolderName;
        return _where = $"{s.PageName} · {s.Page + 1}/{s.Pages}";
    }

    // "42%" for 0..100, made once (the sliders' values, a frame each while the dock is shown)
    static readonly string[] PctText = MakePct();
    static string[] MakePct()
    {
        var a = new string[101];
        for (int i = 0; i <= 100; i++) a[i] = i + "%";
        return a;
    }

    public string StateLine() => Snap.State switch
    {
        HexpadLink.LinkState.NotRunning => "Hexpad is not running",
        HexpadLink.LinkState.OldHexpad => "Update Hexpad to see the dock here",
        HexpadLink.LinkState.Unplugged => "The dock is unplugged",
        HexpadLink.LinkState.ScreenOff => "The dock's screen is off",
        _ => "Connecting to Hexpad...",
    };

    public HexpadLink.Control Ctl(string id) => id != null && Snap.Controls.TryGetValue(id, out var c) ? c : null;

    // "Volume: mute" -> "Mute": a knob's group / caption already says what it is
    public static string Short(string summary)
    {
        if (string.IsNullOrEmpty(summary)) return "";
        int i = summary.IndexOf(": ", StringComparison.Ordinal);
        string s = i >= 0 ? summary[(i + 2)..] : summary;
        return s.Length > 0 ? char.ToUpperInvariant(s[0]) + s[1..] : s;
    }

    public bool Flash(string id, double now) => now - Snap.EchoAt < 0.4 && Snap.Echo == id;

    // a knob's name: what it turns ("Volume"), else where it sits on the dock
    public static string KnobName(string id, HexpadLink.Control c) =>
        !string.IsNullOrEmpty(c?.Label) ? c.Label : id switch { "enc1" => "Big knob", "enc0" => "Left knob", _ => "Right knob" };

    public static string PushLabel(HexpadLink.Control c) => string.IsNullOrEmpty(c?.Push) ? "" : Short(c.Push);

    // the value beside a slider's name: "42%" (abs), the detents of this pull (jog), or why the track rests
    public string ValueText(string id, Slide t, double now)
    {
        var c = Ctl(id);
        if (t == Slide.Abs)
        {
            Snap.Values.TryGetValue(id, out var x);
            if (x?.Off == true) return "Off";
            float? v = AbsValue(id, now);
            if (v == null) return "--";
            bool mine = Drag == id || HoldUntil.ContainsKey(id);
            if (x?.Muted == true && !mine) return "Muted";
            float n = MathF.Round(v.Value * Math.Max(1, c?.Max ?? 100));
            return n >= 0 && n <= 100 ? PctText[(int)n] : $"{n}%";
        }
        if (t == Slide.Jog)
            return Drag != id ? "" : JogSent > 0 ? $"+{JogSent}" : JogSent < 0 ? $"−{-JogSent}" : "0";
        return "";
    }

    // a key says what it does; a knob's push is its own action
    public string ConfirmLabel()
    {
        var c = Ctl(Confirm);
        string label = Confirm.StartsWith("enc") && !string.IsNullOrEmpty(c?.Push) ? c.Push : c?.Label;
        return string.IsNullOrEmpty(label) ? Confirm : label;
    }
}
