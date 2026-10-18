using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using Valve.VR;
using Vortice.DirectWrite;

namespace Orbit;

public sealed class Notification
{
    public string Title, Content, Source;
    public float Timeout = 3, Volume = 0.7f;
    public uint[] Icon;                         // premultiplied BGRA, IconSize x IconSize
    public string Glyph;                        // without an Icon: this symbol instead of the bell (a friend: a heart)
    // display state (main thread)
    public double Shown = -1;
    public float Vis;
    public float Y = float.NaN;                 // where it is in the stack now (it glides to its place: NotificationPanel.Update)
    public bool Leaving;
    public Vortice.Direct2D1.ID2D1Bitmap Bitmap;
}

// Receives notifications in XSOverlay's UDP format (port 42069, JSON with messageType 1, title,
// content, timeout, volume, useBase64Icon/icon, sourceApp) - what VRCX and other tools send when
// their "XSOverlay notifications" option is on. Only from this PC (bound to 127.0.0.1).
public sealed class NotificationListener : IDisposable
{
    public const int Port = 42069, IconSize = 96;
    public readonly ConcurrentQueue<Notification> Incoming = new();
    // off / listening / busy - Status says it in the language picked (the log in English)
    int _st;
    public string Status
    {
        get
        {
            // made again only when the state or the language changes (the settings read it on every frame of theirs)
            int st = _st;
            if (_statusText == null || st != _statusFor || Lang.Version != _statusLang)
            {
                _statusFor = st; _statusLang = Lang.Version;
                _statusText = st == 1 ? Lang.F("listening on UDP {0}", Port) : st == 2 ? Lang.F("UDP {0} is busy (XSOverlay running?)", Port) : Lang.T("off");
            }
            return _statusText;
        }
    }
    string _statusText;
    int _statusFor = -1, _statusLang = -1;
    UdpClient _udp;
    Thread _t;

    double _retryAt;
    bool _warned;

    // called every frame: a busy port (XSOverlay owns it) is retried every 10 s, not every frame
    public void SetEnabled(bool on)
    {
        if (on == (_udp != null)) return;
        if (!on) { Stop(); _st = 0; _retryAt = 0; _warned = false; return; }
        double now = Clock.Now;
        if (now < _retryAt) return;
        UdpClient u = null;
        try
        {
            u = new UdpClient(AddressFamily.InterNetwork);
            u.Client.Bind(new IPEndPoint(IPAddress.Loopback, Port));
            _udp = u;
            _warned = false;
            _t = new Thread(Run) { IsBackground = true, Name = "notifications" };
            _t.Start();
            _st = 1;
            Log.Info($"notifications: listening on UDP {Port}");
        }
        catch (SocketException e)
        {
            u?.Dispose();
            _udp = null;
            _retryAt = now + 10;
            _st = 2;
            if (!_warned) { _warned = true; Log.Warn("notifications: " + e.Message); }
        }
    }

    void Run()
    {
        var from = new IPEndPoint(IPAddress.Any, 0);
        var udp = _udp;
        while (udp != null)
        {
            byte[] data;
            try { data = udp.Receive(ref from); }
            catch { break; }
            try
            {
                var n = Parse(data);
                if (n != null) Incoming.Enqueue(n);
            }
            catch (Exception e) { Log.Warn("notification parse: " + e.Message); }
        }
    }

    static readonly Regex Tags = new("<[^>]+>");

    public static Notification Parse(byte[] data)
    {
        using var doc = JsonDocument.Parse(data);
        var r = doc.RootElement;
        string str(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        float num(string k, float d) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? (float)v.GetDouble() : d;
        bool flag(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.True;
        int type = r.TryGetProperty("messageType", out var mt) && mt.ValueKind == JsonValueKind.Number ? mt.GetInt32() : 1;
        if (type != 1) return null;
        var n = new Notification
        {
            Title = Tags.Replace(str("title") ?? "", ""),
            Content = Tags.Replace(str("content") ?? "", ""),
            Source = str("sourceApp"),
            Timeout = Math.Clamp(num("timeout", 3), 1.5f, 15f),
            Volume = Math.Clamp(num("volume", 0.7f), 0, 1),
        };
        string icon = str("icon");
        if (flag("useBase64Icon") && !string.IsNullOrEmpty(icon))
        {
            try
            {
                using var ms = new MemoryStream(Convert.FromBase64String(icon));
                using var img = Image.FromStream(ms);
                n.Icon = MediaInfo.CoverFit(img, IconSize);
            }
            catch { n.Icon = null; }
        }
        if (string.IsNullOrWhiteSpace(n.Title) && string.IsNullOrWhiteSpace(n.Content)) return null;
        return n;
    }

    void Stop()
    {
        var u = _udp;
        _udp = null;
        try { u?.Close(); } catch { }
    }

    public void Dispose() => Stop();
}

// Shows notifications as cards in the lower part of the view (head-locked), up to three stacked.
public sealed partial class NotificationPanel : IDisposable
{
    public const int TW = 960, TH = 640;
    const float CardH = 190, CardGap = 14;
    readonly Painter _p;
    readonly ConfigStore _cfg;
    readonly Surface _surf;
    readonly List<Notification> _cards = new();
    readonly KeySounds _sound = new();
    VrOverlay _ov;
    bool _dirty;
    double _wake = double.PositiveInfinity;      // a style's animation wants its next frame then (AnimWake.cs)
    const float Width = 0.42f, Distance = 0.85f;
    public NotificationHistory History;          // every card shown, for the watch's notifications widget
    float _placedAngle = float.NaN;

    // Head-locked: the first card's centre sits 'angle' degrees below the centre of view, the panel
    // turned to face the eyes. Further cards stack below it.
    void Place(float angleDeg)
    {
        if (angleDeg == _placedAngle) return;
        _placedAngle = angleDeg;
        float a = angleDeg * MathF.PI / 180;
        float metersPerPx = Width / TW;
        float firstCardFromCentre = (TH / 2f - CardH / 2f) * metersPerPx;    // first card is drawn at the top
        // where the first card should be, then the panel centre is that much further "down" the panel
        var cardPos = new Vector3(0, -Distance * MathF.Sin(a), -Distance * MathF.Cos(a));
        var rot = Matrix4x4.CreateRotationX(-a);                  // +Z (the panel's face) points back at the eyes
        var down = Vector3.TransformNormal(-Vector3.UnitY, rot);
        var centre = cardPos + down * firstCardFromCentre;
        _ov.SetRelative(0, rot * Matrix4x4.CreateTranslation(centre));
    }

    public NotificationPanel(Gpu g, Painter p, ConfigStore cfg)
    {
        _p = p; _cfg = cfg;
        _surf = new Surface(g, TW, TH);
    }

    public void CreateOverlay()
    {
        _ov = new VrOverlay("erik.orbit.notifications", "OrbitOverlay Notifications");
        _ov.SetFlag(VROverlayFlags.IsPremultiplied, true);
        _ov.SetSortOrder(180);
        _ov.SetWidth(Width);
        _placedAngle = float.NaN;
        _sound.Warm();
    }

    public void DestroyOverlay()
    {
        _ov?.Dispose(); _ov = null;
        foreach (var c in _cards) c.Bitmap?.Dispose();
        _cards.Clear();
    }

    // a card drawn once while nothing shows (OrbitApp, after connecting): the first real one no longer makes the code ready in its frame
    // (Erik 10-18, the spike line: 7.8 ms). Nothing is shown or sent.
    public void Warm()
    {
        if (_ov == null || _cards.Count > 0) return;
        var n = new Notification { Source = "OrbitOverlay", Title = "OrbitOverlay", Content = "Ready.", Vis = 1, Y = 0 };
        _cards.Add(n);
        try { Draw(); }
        finally
        {
            _cards.Remove(n);
            n.Bitmap?.Dispose();
            _dirty = true;
        }
    }

    public void Update(NotificationListener src, double now, float dt)
    {
        while (src.Incoming.TryDequeue(out var n))
        {
            if (_ov == null) continue;
            History?.Add(n);
            n.Shown = now;
            _cards.Insert(0, n);
            if (_cfg.Data.General.NotificationSound) _sound.PlayChime(n.Volume * 0.6f);
            _dirty = true;
        }
        if (_ov == null) return;
        // more than three: the oldest leaves early
        int alive = 0;
        foreach (var c in _cards) if (!c.Leaving && ++alive > 3) c.Leaving = true;
        for (int i = _cards.Count - 1; i >= 0; i--)
        {
            var c = _cards[i];
            if (!c.Leaving && now - c.Shown > c.Timeout) c.Leaving = true;
            float nv = Motion.Step(c.Vis, !c.Leaving, dt, c.Leaving ? 0.3f : 0.22f);       // Motion off: at once
            if (nv != c.Vis) { c.Vis = nv; _dirty = true; }
            if (c.Leaving && c.Vis <= 0) { c.Bitmap?.Dispose(); _cards.RemoveAt(i); _dirty = true; }
        }
        // the stack, newest on top: a card pushed down by a new one (or moving up into a gap) glides to its place - modern only, the 95
        // windows just are where they are
        float ty = 0;
        foreach (var c in _cards)
        {
            if (float.IsNaN(c.Y)) c.Y = ty;              // a new one: at its place, sliding in from the side (Draw)
            float ny = Motion.Any && !Theme.Classic ? c.Y + (ty - c.Y) * Math.Min(1, dt * 12) : ty;
            if (MathF.Abs(ny - ty) < 0.5f) ny = ty;
            if (ny != c.Y) { c.Y = ny; _dirty = true; }
            ty += CardH + CardGap;
        }
        if (_cards.Count == 0) { _ov.Hide(); return; }
        Place(Math.Clamp(_cfg.Data.General.NotificationAngle, 0f, 40f));
        if (_dirty || now >= _wake)
        {
            Draw();
            _ov.SetTexture(_surf.Tex);
            _dirty = false;
            _wake = AnimWake.Take();
        }
        _ov.Show();
    }

    Surface _canvas95;

    // the 95 style: every card a little 95 window at 1995 size; it pops up and goes away, no fading
    void Draw95()
    {
        _canvas95 ??= new Surface(_p.Gpu, TW / 2, TH / 2);
        _p.BeginPixel(_canvas95.Bitmap, 0x00000000);
        float y = 0;
        foreach (var c in _cards)
        {
            if (c.Vis >= 0.5f) DrawClassicCard(c, y);
            y += (CardH + CardGap) / 2;
            if (y > (TH - CardH) / 2) break;
        }
        _p.End();
        _p.Begin(_surf.Bitmap, 0x00000000);
        _p.ImageNearest(_canvas95.Bitmap, new RectangleF(0, 0, TW, TH));
        _p.End();
    }

    public void Draw()
    {
        if (Theme.Classic) { Draw95(); return; }
        if (Theme.Cyber) { DrawCyber(); return; }        // HUD alerts (NotificationsCyber.cs)
        _p.Begin(_surf.Bitmap, 0x00000000);
        float y = 0;
        foreach (var c in _cards)
        {
            float e = 1 - MathF.Pow(1 - c.Vis, 3);
            var r = new RectangleF(20 + (1 - e) * 40, float.IsNaN(c.Y) ? y : c.Y, TW - 40, CardH);
            uint A(uint col) => Theme.WithAlpha(col, e);
            _p.Fill(r, A(Theme.WithAlpha(Theme.Bg, 0.96f)), 24);
            _p.Stroke(r, A(Theme.WithAlpha(Theme.Accent, 0.6f)), 2, 24);
            var icon = new RectangleF(r.X + 24, r.Y + 24, 96, 96);
            if (c.Icon != null)
            {
                c.Bitmap ??= _p.CreateBitmap(c.Icon, NotificationListener.IconSize, NotificationListener.IconSize);
                _p.Image(c.Bitmap, icon, e);
            }
            else
            {
                _p.Fill(icon, A(Theme.Panel3), 20);
                _p.Icon(c.Glyph ?? "\uEA8F", icon, A(Theme.Accent), 40);    // bell (or its own)
            }
            float tx = icon.Right + 24, tw = r.Right - tx - 24;
            if (!string.IsNullOrEmpty(c.Source))
                _p.Text(c.Source, new RectangleF(tx, r.Y + 18, tw, 26), A(Theme.Dim), 18);
            _p.Text(c.Title, new RectangleF(tx, r.Y + 42, tw, 44), A(Theme.Text), 30, FontWeight.SemiBold);
            _p.Text(c.Content, new RectangleF(tx, r.Y + 90, tw, 84), A(Theme.Text), 23, FontWeight.Normal,
                TextAlignment.Leading, ParagraphAlignment.Near, null, true);
            y += CardH + CardGap;
            if (y > TH - CardH) break;
        }
        _p.End();
    }

    // a 95 message window: navy caption with the sender, the information icon (or the app's own),
    // the title in bold and the text, all in 8 pt MS Sans Serif
    void DrawClassicCard(Notification c, float y)
    {
        if (SkinCard(c, y)) return;         // a style's own card (ScreenHooks.cs)
        var r = new RectangleF(10, y, TW / 2 - 20, CardH / 2);
        _p.Fill(r, Theme.W95Face);
        _p.Bevel(r, Painter.Edge.Window);
        var cap = new RectangleF(r.X + 3, r.Y + 3, r.Width - 6, 18);
        _p.Fill(cap, Theme.W95Navy);
        _p.Text(string.IsNullOrEmpty(c.Source) ? "Notification" : c.Source, new RectangleF(cap.X + 3, cap.Y, cap.Width - 6, cap.Height),
            Theme.W95Hi, Ui.F95, FontWeight.Bold, TextAlignment.Leading, ParagraphAlignment.Center, Theme.RetroFont);
        var icon = new RectangleF(r.X + 14, cap.Bottom + 12, 32, 32);
        if (c.Icon != null)
        {
            c.Bitmap ??= _p.CreateBitmap(c.Icon, NotificationListener.IconSize, NotificationListener.IconSize);
            _p.Image(c.Bitmap, icon, 1);
        }
        else W95Icons.Draw(_p, W95Icons.Id.Info32, icon.X, icon.Y);
        float tx = icon.Right + 14, tw = r.Right - tx - 12;
        _p.Text(c.Title, new RectangleF(tx, cap.Bottom + 10, tw, 15), Theme.W95Dark, Ui.F95, FontWeight.Bold,
            TextAlignment.Leading, ParagraphAlignment.Center, Theme.RetroFont);
        _p.Text(c.Content, new RectangleF(tx, cap.Bottom + 27, tw, r.Bottom - cap.Bottom - 31), Theme.W95Dark, Ui.F95, FontWeight.Normal,
            TextAlignment.Leading, ParagraphAlignment.Near, Theme.RetroFont, true);
    }

    // snapshot tool: draw a sample card
    public void AddForPreview(Notification n) { n.Vis = 1; n.Shown = 0; _cards.Add(n); }
    public Surface Surface => _surf;

    public void Dispose() { DestroyOverlay(); _surf.Dispose(); _sound.Dispose(); _canvas95?.Dispose(); }
}

// The last notifications shown (newest first, 20 at most), for the watch's notifications widget. Main thread only.
public sealed class NotificationHistory
{
    public readonly record struct Item(string Source, string Title, string Content, DateTime At);
    public const int Max = 20;
    readonly List<Item> _items = new();
    public int Version { get; private set; }
    public IReadOnlyList<Item> Items => _items;

    public void Add(Notification n) => Add(n.Source, n.Title, n.Content, DateTime.Now);

    public void Add(string source, string title, string content, DateTime at)
    {
        _items.Insert(0, new Item(source ?? "", title ?? "", content ?? "", at));
        if (_items.Count > Max) _items.RemoveRange(Max, _items.Count - Max);
        Version++;
    }

    public void Clear() { _items.Clear(); Version++; }
}
