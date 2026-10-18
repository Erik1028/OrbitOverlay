using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Vortice.DirectWrite;

namespace Orbit;

// The watch in the Windows 95 style: a "Date/Time" window at real 1995 pixel size (320 x 220, shown 2x).
// Big digital time and the long date, an analog dial with dotted marks and bevelled teal hands, now
// playing like the CD Player, batteries as block bars, a toolbar, frame timing in the status bar.
public sealed partial class WristPanel
{
    public const int TW95 = TW / 2, TH95 = TH / 2;
    public Surface Canvas95 { get; private set; }
    bool _dismissReq, _dismissed;      // its _ or X: hidden until you look away (or a few seconds)
    double _dismissUntil;

    const uint Blk95 = 0xFF000000, Wht95 = 0xFFFFFFFF, Gry95 = 0xFF808080, Teal95 = 0xFF008080, Deep95 = 0xFF004040;

    static readonly string[] PlayGlyph = { "k...", "kk..", "kkk.", "kkkk", "kkk.", "kk..", "k..." };
    static readonly string[] PauseGlyph = { "kk.kk", "kk.kk", "kk.kk", "kk.kk", "kk.kk", "kk.kk", "kk.kk" };
    static readonly string[] BarGlyph = { "k", "k", "k", "k", "k", "k", "k" };
    static readonly string[] PrevGlyph = Glue(BarGlyph, Mirror(PlayGlyph), Mirror(PlayGlyph));
    static readonly string[] NextGlyph = Glue(PlayGlyph, PlayGlyph, BarGlyph);
    static readonly string[] BoltGlyph = { "..kk", ".kk.", "kkkk", ".kk.", "kk.." };

    static string[] Mirror(string[] g) => g.Select(r => new string(r.Reverse().ToArray())).ToArray();
    static string[] Glue(params string[][] parts) =>
        Enumerable.Range(0, parts[0].Length).Select(i => string.Concat(parts.Select(p => p[i]))).ToArray();

    void Draw95(PanelData d)
    {
        Canvas95 ??= new Surface(_p.Gpu, TW95, TH95);
        _ui.Begin(_ptr, 2);
        SwipeGuard();
        _ui.Canvas95 = new SizeF(TW95, TH95);
        _p.BeginPixel(Canvas95.Bitmap, 0x00000000);
        if (C.Face == "custom") Custom95(d);
        else Window95(d);
        _p.End();
        _p.Begin(Surf.Bitmap, 0x00000000);
        _p.ImageNearest(Canvas95.Bitmap, new RectangleF(0, 0, TW, TH));
        _p.End();
        _ui.End();
    }

    void Window95(PanelData d)
    {
        if (SkinFace(d)) return;                  // a style's own face (ScreenHooks.cs)
        string face = C.Face ?? "full";
        var now = Now;                        // (the clock; a tool's fixed moment)
        bool rich = face is "full" or "analog";
        bool media = rich && C.ShowMedia, bats = rich && C.ShowBatteries, perf = rich && C.ShowPerformance;
        bool row2 = media || bats, tools = face != "time";
        float h = 22 + 74 + (row2 ? 56 : 0) + (tools ? 34 : 0) + (perf ? 28 : 0) + 4;
        float wy = MathF.Floor((TH95 - h) / 2);
        var win = new RectangleF(0, wy, TW95, h);
        _p.Fill(win, Theme.W95Face);
        _p.Bevel(win, Painter.Edge.Window);
        _ui.Caption95(new RectangleF(3, wy + 3, TW95 - 6, 18), "Date/Time");
        float bx = TW95 - 5 - 16;
        if (_ui.CaptionClose95("w95.x", bx, wy + 5)) _dismissReq = true;
        _ui.CaptionMax95("w95.max", bx - 2 - 16, wy + 5, false);
        if (_ui.CaptionMin95("w95.min", bx - 2 - 32, wy + 5)) _dismissReq = true;

        // the clock part: like the reference Date/Time window
        float ty = wy + 22;
        bool h24 = _cfg.Data.Appearance.Clock24h;
        string time = Clk(now, h24 ? "H:mm:ss" : "h:mm:ss", CultureInfo.CurrentCulture);
        string ampm = h24 ? null : now.Hour < 12 ? "AM" : "PM";
        var culture = CultureInfo.CurrentCulture;
        string date = Clk(now, culture.DateTimeFormat.LongDatePattern, culture);
        if (face == "analog")
        {
            Dial95(now, 50, ty + 37, 32);
            BigTime95(time, ampm, new RectangleF(98, ty + 6, 214, 38), 30, false);
            _ui.Text95(Fit(date, 210, 13), new RectangleF(100, ty + 46, 212, 16), Blk95, size: 13);
        }
        else if (face == "minimal")
        {
            BigTime95(time, ampm, new RectangleF(4, ty + 2, TW95 - 8, 52), 48, true);
            _ui.Text95(Fit(date, 300, 13), new RectangleF(4, ty + 54, TW95 - 8, 16), Blk95, false, TextAlignment.Center, size: 13);
        }
        else
        {
            BigTime95(time, ampm, new RectangleF(12, ty + 4, 200, 46), 44, false);
            _ui.Text95(Fit(date, 196, 13), new RectangleF(14, ty + 50, 200, 16), Blk95, size: 13);
            Dial95(now, TW95 - 52, ty + 37, 32);
        }
        float y = ty + 74;

        // now playing (CD Player) and batteries (Power Meter)
        if (row2)
        {
            _p.EtchedLine(6, TW95 - 6, y + 2);
            float ry = y + 6;
            if (media) Media95(d, new RectangleF(8, ry, bats ? 190 : TW95 - 16, 48));
            if (bats) Batteries95(d, new RectangleF(media ? 206 : 8, ry, media ? TW95 - 8 - 206 : TW95 - 16, 48));
            y += 56;
        }

        // the toolbar
        if (tools)
        {
            _p.EtchedLine(6, TW95 - 6, y + 2);
            Toolbar95(d, y + 6);
            y += 34;
        }

        // frame timing: a status bar, or the black graph of the era's performance tools
        if (perf)
        {
            var r = new RectangleF(6, y + 2, TW95 - 12, 24);
            if (C.PerfGraph) Graph95(d.Vr, r);
            else Status95(d, r);
        }
    }

    // a text cut to its room, the answer kept by text and room (a cut text was made again, many of them, on every frame of the 95
    // face: a widget's title, a song's line); the language or the look changing (another retro style, another font) throws the kept
    // ones out
    readonly Dictionary<(string s, float w, float size, bool bold, int lang, int look), string> _fits = new();

    string Fit(string s, float w, float size = Ui.F95, bool bold = false)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var key = (s, w, size, bold, Lang.Version, Theme.Version);
        if (_fits.TryGetValue(key, out var kept)) return kept;
        if (_fits.Count > 512) _fits.Clear();
        return _fits[key] = FitNow(s, w, size, bold);
    }

    string FitNow(string s, float w, float size, bool bold)
    {
        string sh = Lang.Short(s);
        s = Lang.T(s);                     // cut in the language picked (its short form first, where it has one)
        if (string.IsNullOrEmpty(s) || _ui.Width95(s, bold, size) <= w) return s;
        if (sh != null && _ui.Width95(s = sh, bold, size) <= w) return s;
        int n = s.Length;
        while (n > 1 && _ui.Width95(s[..n] + "...", bold, size) > w) n--;
        if (n > 1 && char.IsHighSurrogate(s[n - 1])) n--;
        return s[..n].TrimEnd() + "...";
    }

    void BigTime95(string time, string ampm, RectangleF r, float size, bool center)
    {
        float tw = _ui.Width95(time, false, size), aw = ampm == null ? 0 : _ui.Width95(ampm) + 4;
        float x = center ? r.X + MathF.Round((r.Width - tw - aw) / 2) : r.X;
        _ui.Text95(time, new RectangleF(x, r.Y, tw + 4, r.Height), Blk95, size: size);
        if (ampm != null) _ui.Text95(ampm, new RectangleF(x + tw + 4, r.Bottom - 18, 30, 13));
    }

    // dotted marks (teal squares on the hours), bevelled teal hands, a thin second hand
    void Dial95(DateTime now, float cx, float cy, float r)
    {
        for (int i = 0; i < 60; i++)
        {
            float a = i / 60f * MathF.Tau - MathF.PI / 2;
            float x = MathF.Round(cx + r * MathF.Cos(a)), y = MathF.Round(cy + r * MathF.Sin(a));
            if (i % 5 == 0)
            {
                _p.Fill(new RectangleF(x - 1, y - 1, 3, 3), Teal95);
                _p.Fill(new RectangleF(x - 1, y - 1, 2, 1), 0xFF00FFFF);
                _p.Fill(new RectangleF(x - 1, y, 1, 1), 0xFF00FFFF);
                _p.Fill(new RectangleF(x + 1, y - 1, 1, 3), Deep95);
                _p.Fill(new RectangleF(x - 1, y + 1, 2, 1), Deep95);
            }
            else _p.Fill(new RectangleF(x, y, 1, 1), Gry95);
        }
        float s = now.Second, m = now.Minute + s / 60f, hr = now.Hour % 12 + m / 60f;
        Hand95(cx, cy, hr / 12f, r * 0.55f, 4);
        Hand95(cx, cy, m / 60f, r * 0.82f, 3);
        float sa = s / 60f * MathF.Tau - MathF.PI / 2;
        _p.Line(cx + 0.5f, cy + 0.5f, cx + 0.5f + r * 0.86f * MathF.Cos(sa), cy + 0.5f + r * 0.86f * MathF.Sin(sa), Blk95, 1);
        _p.Fill(new RectangleF(cx - 1, cy - 1, 3, 3), Blk95);
    }

    readonly Vector2[] _hand95 = new Vector2[4];

    void Hand95(float cx, float cy, float frac, float len, float half)
    {
        float a = frac * MathF.Tau - MathF.PI / 2;
        var dir = new Vector2(MathF.Cos(a), MathF.Sin(a));
        var perp = new Vector2(-dir.Y, dir.X);
        var c = new Vector2(cx + 0.5f, cy + 0.5f);
        var tip = c + dir * len;
        var tail = c - dir * (len * 0.14f);
        var l = c + dir * (len * 0.24f) + perp * half;
        var rr = c + dir * (len * 0.24f) - perp * half;
        var pts = _hand95;                   // (the one array: no garbage a frame)
        pts[0] = tail; pts[1] = l; pts[2] = tip; pts[3] = rr;
        _p.Polygon(pts, Teal95);
        // light from the top left: the edges facing it white, the others dark
        void Edge(Vector2 p0, Vector2 p1)
        {
            var e = p1 - p0;
            bool lit = Vector2.Dot(new Vector2(-e.Y, e.X), new Vector2(-1, -1)) > 0;
            _p.Line(p0.X, p0.Y, p1.X, p1.Y, lit ? Wht95 : Deep95, 1);
        }
        Edge(tail, l); Edge(l, tip); Edge(tip, rr); Edge(rr, tail);
    }

    void Media95(PanelData d, RectangleF r)
    {
        bool has; string title, artist; bool playing; uint[] art = null; int ver = -2;
        if (d.FakeMedia is { } f) (has, title, artist, playing) = f;
        else if (d.Media != null) (has, title, artist, playing, art, ver) = d.Media.Snapshot();
        else { has = false; title = artist = null; playing = false; }
        if (ver != _artVersion)
        {
            _artVersion = ver;
            _art?.Dispose();
            _art = art != null ? _p.CreateBitmap(art, MediaInfo.ArtSize, MediaInfo.ArtSize) : null;
        }
        var box = new RectangleF(r.X, r.Y, 36, 36);
        var inner = RectangleF.Inflate(box, -2, -2);
        _p.Fill(inner, Wht95);
        if (_art != null) _p.Image(_art, inner);
        else W95Icons.Draw(_p, W95Icons.Id.Note, inner.X + 8, inner.Y + 8);
        _p.Bevel(box, Painter.Edge.Sunken);
        float tx = r.X + 42, tw = r.Right - tx;
        if (has)
        {
            _ui.Text95(Fit(title, tw, Ui.F95, true), new RectangleF(tx, r.Y, tw, 14), Blk95, true);
            _ui.Text95(Fit(artist ?? "", tw), new RectangleF(tx, r.Y + 14, tw, 13));
        }
        else _ui.Text95("Nothing playing", new RectangleF(tx, r.Y, tw, 14), Gry95);
        var glyphs = MediaGlyphs(playing);
        for (int i = 0; i < 3; i++)
        {
            var b = new RectangleF(tx + i * 30, r.Y + 30, 28, 18);
            if (_ui.GlyphButton95(Ui.Id("wm", i), b, glyphs[i], has)) OnAction?.Invoke(MediaIds[i]);
        }
    }

    void Batteries95(PanelData d, RectangleF r)
    {
        int shown = 0;
        foreach (var b in d.Vr.Batteries)
        {
            if (shown >= 4) break;
            float y = r.Y + shown * 12;
            _ui.Text95(Bat(b.label), new RectangleF(r.X, y, 34, 12));
            var bar = new RectangleF(r.X + 36, y + 1, r.Width - 36 - 32, 10);
            _ui.Progress95(bar, b.pct, b.pct > 0.2f ? Theme.W95Navy : 0xFF800000);
            if (b.charging) _p.Pixels(BoltGlyph, bar.Right + 3, y + 3, Blk95);
            _ui.Text95(Pct(ShownPct(b.pct)), new RectangleF(bar.Right + 2, y, 30, 12), Blk95, false, TextAlignment.Trailing);      // (the "0" format's rounding, made once)
            shown++;
        }
        if (shown == 0) _ui.Text95(d.VrConnected ? "No battery info" : "SteamVR is off", new RectangleF(r.X, r.Y, r.Width, 12), Gry95);
    }

    // keyboard, monitors, recenter, layout profile (its name), night mode, settings
    static readonly string[] ToolbarIds = { "keyboard", "desktops", "recenter", "layout", "night", "settings" };
    readonly W95Icons.Id?[] _toolbarIcons = { W95Icons.Id.Keyboard, null, W95Icons.Id.Center, null, W95Icons.Id.Moon, W95Icons.Id.Gear };
    readonly string[][] _mediaGlyphs = new string[3][];

    // |<<  > (or ||)  >>| - the array kept, its middle set for this frame
    string[][] MediaGlyphs(bool playing)
    {
        _mediaGlyphs[0] = PrevGlyph;
        _mediaGlyphs[1] = playing ? PauseGlyph : PlayGlyph;
        _mediaGlyphs[2] = NextGlyph;
        return _mediaGlyphs;
    }

    void Toolbar95(PanelData d, float y)
    {
        var ids = ToolbarIds;
        var icons = _toolbarIcons;
        icons[1] = d.DesktopsVisible ? W95Icons.Id.Computer : W95Icons.Id.ComputerOff;
        const int n = 6;
        float bw = MathF.Floor((TW95 - 16 - (n - 1) * 2) / (float)n);
        float x0 = MathF.Floor((TW95 - (n * bw + (n - 1) * 2)) / 2);
        for (int i = 0; i < n; i++)
        {
            var r = new RectangleF(x0 + i * (bw + 2), y, bw, 26);
            bool on = (i == 0 && d.KeyboardVisible) || (i == 1 && d.DesktopsVisible) || (i == 4 && d.NightMode);
            bool click = icons[i] is { } ic
                ? _ui.Button95(Ui.Id("wt", i), r, null, true, false, on, ic)
                : _ui.Button95(Ui.Id("wt", i), r, Ui.Esc95(Fit(d.LayoutName, bw - 6)), true, false, false);
            if (click) OnAction?.Invoke(ids[i]);
        }
    }

    // the status bar's texts, made again only when a number they show changes (eight strings a frame before: 2.4 KB a draw)
    readonly string[] _statusText = new string[4], _statusShort = new string[4];
    (float fps, float hz, float rp, float ms, float vram, float total, int lang) _statusFor = (-2, -2, -2, -2, -2, -2, -1);

    void Status95(PanelData d, RectangleF r)
    {
        var v = d.Vr;
        float budget = 1000f / Math.Max(1, v.DisplayHz);
        float vram = d.VramUsed;
        var key = (v.AppFps, v.DisplayHz, v.ReprojPct, v.GpuMs, vram, d.VramTotalGB, Lang.Version);
        if (key != _statusFor)
        {
            _statusFor = key;
            _statusText[0] = v.AppFps < 0 ? "-- fps" : $"{v.AppFps:0}/{v.DisplayHz:0} fps";
            _statusText[1] = v.ReprojPct < 0 ? Lang.T("-- reproj.") : Lang.F("{0:0}% reproj.", v.ReprojPct);
            _statusText[2] = v.GpuMs < 0 ? "-- ms GPU" : $"{v.GpuMs:0.0} ms GPU";
            _statusText[3] = vram < 0 ? "-- GB" : d.VramTotalGB > 0 ? $"{vram:0.0}/{d.VramTotalGB:0} GB" : $"{vram:0.0} GB";
            // narrow panes drop the words: "86 fps", "4%", "9.3 ms", "9.4 GB"
            _statusShort[0] = v.AppFps < 0 ? "--" : $"{v.AppFps:0} fps";
            _statusShort[1] = v.ReprojPct < 0 ? "--" : $"{v.ReprojPct:0}%";
            _statusShort[2] = v.GpuMs < 0 ? "--" : $"{v.GpuMs:0.0} ms";
            _statusShort[3] = vram < 0 ? "--" : $"{vram:0.0} GB";
        }
        var colors = _statusColor;          // (the array kept: no garbage a frame)
        colors[0] = v.AppFps >= 0 && v.AppFps < v.DisplayHz * 0.85f ? 0xFF806000 : Blk95;
        colors[1] = v.ReprojPct > 25 ? 0xFF800000 : v.ReprojPct > 8 ? 0xFF806000 : Blk95;
        colors[2] = v.GpuMs > budget ? 0xFF800000 : v.GpuMs > budget * 0.85f ? 0xFF806000 : Blk95;
        colors[3] = vram > 0 && d.VramTotalGB > 0 && vram > d.VramTotalGB * 0.92f ? 0xFF806000 : Blk95;
        float pw = MathF.Floor((r.Width - 3 * 2) / 4);
        for (int i = 0; i < 4; i++)
        {
            var pr = new RectangleF(r.X + i * (pw + 2), r.Y + 3, i == 3 ? r.Right - (r.X + i * (pw + 2)) : pw, 18);
            _ui.Pane95(pr, null);
            string s = _ui.Width95(_statusText[i]) <= pr.Width - 7 ? _statusText[i] : Fit(_statusShort[i], pr.Width - 7);
            _ui.Text95(s, new RectangleF(pr.X + 4, pr.Y, pr.Width - 6, pr.Height), colors[i]);
        }
    }
    readonly uint[] _statusColor = new uint[4];

    // the graph's label, made again only when its numbers, the room it has, the language or the look (its font) change
    string _graphLabel;
    (float fps, float ms, float rp, float w, int lang, int look) _graphLabelFor = (-2, -2, -2, -2, -1, -1);

    string GraphLabel95(VrStats v, float w)
    {
        var key = (v.AppFps, v.GpuMs, v.ReprojPct, w, Lang.Version, Theme.Version);
        if (_graphLabel != null && key == _graphLabelFor) return _graphLabel;
        _graphLabelFor = key;
        string label = v.AppFps < 0 ? "waiting for frames" : Lang.F("{0:0} fps  {1:0.0} ms GPU  {2:0}% reproj.", v.AppFps, v.GpuMs, Math.Max(0, v.ReprojPct));
        if (v.AppFps >= 0 && _ui.Width95(label) > w)
            label = $"{v.AppFps:0} fps  {v.GpuMs:0.0} ms  {Math.Max(0, v.ReprojPct):0}%";
        return _graphLabel = Fit(label, w);                  // a narrow graph widget
    }

    // last ~5 s of frames on black: green bars, yellow over budget / reprojected, red dropped
    void Graph95(VrStats v, RectangleF r)
    {
        const uint green = 0xFF00FF00, dim = 0xFF008000, warn = 0xFFFFFF00, bad = 0xFFFF0000;
        _p.Fill(r, Blk95);
        _p.Bevel(r, Painter.Edge.Sunken);
        var inner = RectangleF.Inflate(r, -2, -2);
        for (float gx = inner.X + 6; gx < inner.Right; gx += 12) _p.Fill(new RectangleF(gx, inner.Y, 1, inner.Height), dim);
        for (float gy = inner.Y + 5; gy < inner.Bottom; gy += 6) _p.Fill(new RectangleF(inner.X, gy, inner.Width, 1), dim);
        float budget = 1000f / Math.Max(1, v.DisplayHz), top = budget * 2;
        int n = v.FrameCount;
        float step = inner.Width / VrStats.FrameHistory;
        for (int i = 0; i < n; i++)
        {
            int idx = (v.FrameHead - n + i + VrStats.FrameHistory) % VrStats.FrameHistory;
            float ms = v.FrameMs[idx];
            byte flag = v.FrameFlag[idx];
            float bh = MathF.Max(1, MathF.Round(Math.Clamp(ms / top, 0.02f, 1) * inner.Height));
            uint col = flag == 2 ? bad : flag == 1 || ms > budget ? warn : green;
            float x = MathF.Floor(inner.Right - (n - i) * step);
            _p.Fill(new RectangleF(x, inner.Bottom - bh, MathF.Max(1, MathF.Ceiling(step)), bh), col);
        }
        float by = MathF.Round(inner.Bottom - inner.Height * (budget / top));
        _p.Fill(new RectangleF(inner.X, by, inner.Width, 1), Wht95);
        string label = GraphLabel95(v, inner.Width - 12);
        _p.Fill(new RectangleF(inner.X + 2, inner.Y + 1, MathF.Min(_ui.Width95(label) + 4, inner.Width - 4), 12), Blk95);
        _ui.Text95(label, new RectangleF(inner.X + 4, inner.Y + 1, inner.Width - 8, 12), green);
    }
}
