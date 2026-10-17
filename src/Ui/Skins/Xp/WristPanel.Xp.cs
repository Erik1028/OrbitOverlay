using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Numerics;
using Vortice.DirectWrite;

namespace Orbit;

// The watch in Windows XP: the fixed face is a "Date and Time Properties" dialog with group boxes (the time with its round clock,
// the batteries as green-block bars, now playing with its seek bar, a row of big toolbar buttons, a status bar with a size grip);
// the custom face is a desktop of little XP windows (blue tool-window bars, the cream face, big navy numbers), the pages along an
// XP taskbar.
public sealed partial class WristPanel
{
    static XpSkin Xp => (XpSkin)Skins.Xp;
    const uint XpNavy = XpSkin.Navy, XpBlueText = 0xFF0046D5, XpGrey = 0xFF6D6D6D, XpWarn = 0xFFB35A00, XpBad = 0xFFC0261C;

    // ================= the fixed face =================

    partial void FaceXp(PanelData d, ref bool done)
    {
        done = true;
        XpSkin.CornerBack = 0;
        string face = C.Face ?? "full";
        var now = DateTime.Now;
        bool rich = face is "full" or "analog";
        bool media = rich && C.ShowMedia, bats = rich && C.ShowBatteries, perf = rich && C.ShowPerformance;
        bool tools = face != "time", graph = perf && C.PerfGraph;
        float timeH = face is "full" or "analog" ? 70 : 76;
        float h = 21 + 4 + timeH + (media ? 4 + 46 : 0) + (tools ? 4 + 32 : 0) + (perf ? graph ? 4 + 28 + 4 : 3 + 18 : 4) + 3;
        float wy = MathF.Floor((TH95 - h) / 2);
        var win = new RectangleF(0, wy, TW95, h);
        _p.Fill(win, Theme.W95Face);
        _p.Bevel(win, Painter.Edge.Window);
        _ui.Caption95(new RectangleF(3, wy + 3, TW95 - 6, 18), "Date and Time Properties", W95Icons.Id.Clock);
        float bx = TW95 - 5 - 16;
        if (_ui.CaptionClose95("w95.x", bx, wy + 5)) _dismissReq = true;
        _ui.CaptionMax95("w95.max", bx - 2 - 16, wy + 5, false);
        if (_ui.CaptionMin95("w95.min", bx - 2 - 32, wy + 5)) _dismissReq = true;

        float y = wy + 25;
        // the time (and the batteries beside it)
        float tw = bats ? TW95 - 12 - 112 - 6 : TW95 - 12;
        TimeGroupXp(face, now, new RectangleF(6, y, tw, timeH));
        if (bats) BatteriesXp(d, new RectangleF(6 + tw + 6, y, 112, timeH));
        y += timeH + 4;
        if (media)
        {
            MediaGroupXp(d, new RectangleF(6, y, TW95 - 12, 46));
            y += 50;
        }
        if (tools)
        {
            ToolbarXp(d, y);
            y += 36;
        }
        if (perf)
        {
            if (graph)
            {
                var gr = new RectangleF(6, y, TW95 - 12, 28);
                PlotXp(d.Vr, RectangleF.Inflate(gr, -1, -1), true);
                WellXp(gr);
                y += 32;
            }
            else StatusBarXp(d, new RectangleF(3, y, TW95 - 6, 18));
        }
    }

    // "Time": the round clock, the big navy time with its seconds, the long date
    void TimeGroupXp(string face, DateTime now, RectangleF g)
    {
        _ui.Group95(g, "Time");
        bool h24 = _cfg.Data.Appearance.Clock24h;
        var culture = CultureInfo.CurrentCulture;
        string hm = Clk(now, h24 ? "H:mm" : "h:mm", culture), sec = Clk(now, ":ss", culture);
        string ampm = h24 ? null : now.Hour < 12 ? "AM" : "PM";
        var inner = new RectangleF(g.X + 8, g.Y + 14, g.Width - 16, g.Height - 18);
        bool dial = face is "full" or "analog";
        float R = face == "analog" ? MathF.Floor(inner.Height / 2) : MathF.Min(25, MathF.Floor(inner.Height / 2));
        float tx = inner.X;
        if (dial)
        {
            DialXp(now, inner.X + R + 0.5f, MathF.Floor(inner.Y + inner.Height / 2) + 0.5f, R);
            tx = inner.X + 2 * R + 10;
        }
        float room = inner.Right - tx;
        bool center = !dial;
        float max = face == "analog" ? 24 : dial ? 34 : 44;
        BigTimeXp(hm, sec, ampm, new RectangleF(tx, inner.Y, room, inner.Height - 15), max, center);
        string date = FitDate95(now, room);
        _ui.Text95(date, new RectangleF(tx, inner.Bottom - 15, room, 14), Blk95, false, center ? TextAlignment.Center : TextAlignment.Leading);
    }

    // the time in XP's big type: bold navy hours and minutes, the seconds (and AM / PM) small beside them on the same baseline
    void BigTimeXp(string hm, string sec, string ampm, RectangleF r, float max, bool center)
    {
        float secSize(float s) => MathF.Max(11, MathF.Floor(s * 0.5f));
        float Width(float s) => _ui.Width95(hm, true, s) + _ui.Width95(sec, true, secSize(s)) + (ampm != null ? 3 + _ui.Width95(ampm, true, Ui.F95) : 0);
        float size = max;
        while (size > 13 && (Width(size) > r.Width - 2 || size * 1.2f > r.Height)) size--;
        float w = Width(size);
        float x = MathF.Round(center ? r.X + (r.Width - w) / 2 : r.X);
        var (base1, line1) = _p.TextLine(size, FontWeight.Bold, Theme.Font);
        float by = MathF.Round(r.Y + (r.Height + line1 * 0.62f) / 2);      // the baseline: the digits' middle in the box
        float hw = _ui.Width95(hm, true, size);
        Xp.TextAt(_p, hm, new RectangleF(x, by - base1, hw + 4, line1), XpNavy, size, FontWeight.Bold, TextAlignment.Leading, ParagraphAlignment.Near);
        float ss = secSize(size);
        var (base2, line2) = _p.TextLine(ss, FontWeight.Bold, Theme.Font);
        float sw = _ui.Width95(sec, true, ss);
        Xp.TextAt(_p, sec, new RectangleF(x + hw, by - base2, sw + 4, line2), XpNavy, ss, FontWeight.Bold, TextAlignment.Leading, ParagraphAlignment.Near);
        if (ampm != null)
        {
            var (base3, line3) = _p.TextLine(Ui.F95, FontWeight.Bold, Theme.Font);
            _p.Text(ampm, new RectangleF(x + hw + sw + 3, by - base3, 30, line3), XpNavy, Ui.F95, FontWeight.Bold, TextAlignment.Leading,
                ParagraphAlignment.Near);
        }
    }

    // the clock of XP's Date and Time Properties, round and smooth: a grey-blue rim, a white face, navy marks and hands, a red
    // second hand, the blue boss
    void DialXp(DateTime now, float cx, float cy, float R)
    {
        if (R < 6) return;
        _p.Smooth(true);
        _p.Circle(cx, cy, R, 0xFF8A98B2);
        _p.Circle(cx, cy, R - 1, 0xFFDCE2EC);
        float fr = MathF.Max(3, R * 0.86f);
        _p.Circle(cx, cy, fr, 0xFFB4C0D6);
        _p.Circle(cx, cy, fr - 1, 0xFFFFFFFF);
        for (int i = 0; i < 12; i++)
        {
            float a = i / 12f * MathF.Tau;
            var dir = new Vector2(MathF.Sin(a), -MathF.Cos(a));
            if (i % 3 == 0)
            {
                var p0 = new Vector2(cx, cy) + dir * (fr * 0.70f);
                var p1 = new Vector2(cx, cy) + dir * (fr * 0.88f);
                _p.Line(p0.X, p0.Y, p1.X, p1.Y, 0xFF27459A, MathF.Max(1.5f, R * 0.075f));
            }
            else
            {
                var pt = new Vector2(cx, cy) + dir * (fr * 0.82f);
                _p.Circle(pt.X, pt.Y, MathF.Max(0.6f, R * 0.03f), 0xFF8D9BBA);
            }
        }
        float s = now.Second, m = now.Minute + s / 60f, hr = now.Hour % 12 + m / 60f;
        void Hand(float frac, float len, float width, uint c, float tail = 0.1f)
        {
            float a = frac * MathF.Tau;
            var dir = new Vector2(MathF.Sin(a), -MathF.Cos(a));
            var c0 = new Vector2(cx, cy) - dir * (len * tail);
            var c1 = new Vector2(cx, cy) + dir * len;
            _p.RoundLine(c0.X, c0.Y, c1.X, c1.Y, c, width);
        }
        Hand(hr / 12f, fr * 0.52f, MathF.Max(2, R * 0.11f), 0xFF1B2A4E);
        Hand(m / 60f, fr * 0.78f, MathF.Max(1.5f, R * 0.075f), 0xFF1B2A4E);
        if (R >= 14) Hand(s / 60f, fr * 0.84f, MathF.Max(0.9f, R * 0.035f), 0xFFE0502A, 0.18f);
        _p.Circle(cx, cy, MathF.Max(1.5f, R * 0.09f), 0xFF2C5BC2);
        _p.Smooth(false);
    }

    // "Batteries": each one a row - its name, a green-block bar, its percentage
    void BatteriesXp(PanelData d, RectangleF g)
    {
        _ui.Group95(g, "Batteries");
        var inner = new RectangleF(g.X + 7, g.Y + 15, g.Width - 14, g.Height - 19);
        var bats = d.Vr.Batteries;
        if (bats.Count == 0)
        {
            _ui.Text95(Fit(d.VrConnected ? "No battery info" : "SteamVR is off", inner.Width), new RectangleF(inner.X, inner.Y, inner.Width, 14), Gry95);
            return;
        }
        int n = Math.Min(4, bats.Count);
        float pitch = MathF.Floor(inner.Height / 4);
        for (int i = 0; i < n; i++)
        {
            var b = bats[i];
            float y = inner.Y + i * pitch;
            bool low = b.pct <= 0.2f && !b.charging;
            _ui.Text95(Bat(b.label), new RectangleF(inner.X, y, 30, 12));
            var bar = new RectangleF(inner.X + 31, y + 2, inner.Width - 31 - 29, 9);
            _ui.Progress95(bar, b.pct, low ? 0xFF800000 : Theme.W95Navy);
            if (b.charging) _p.Pixels(BoltGlyph, bar.Right + 2, y + 3, 0xFF1F2F5A);
            _ui.Text95($"{b.pct * 100:0}%", new RectangleF(bar.Right + 2, y, inner.Right - bar.Right - 2, 12), low ? XpBad : Blk95, false,
                TextAlignment.Trailing);
        }
    }

    // "Now playing": the cover, the title and artist, Media Player's blue seek bar, |<  ||  >|
    void MediaGroupXp(PanelData d, RectangleF g)
    {
        _ui.Group95(g, "Now playing");
        var (has, title, artist, playing) = MediaNow(d);
        var box = new RectangleF(g.X + 8, g.Y + 13, 30, 30);
        var inner = RectangleF.Inflate(box, -1, -1);
        _p.Fill(inner, Wht95);
        if (_art != null) _p.Image(_art, inner);
        else W95Icons.Draw(_p, W95Icons.Id.Note, inner.X + 6, inner.Y + 6);
        _p.Bevel(box, Painter.Edge.Sunken);
        string[] ids = { "media.prev", "media.play", "media.next" };
        string[][] glyphs = { PrevGlyph, playing ? PauseGlyph : PlayGlyph, NextGlyph };
        float bx = g.Right - 8 - (3 * 24 + 4);
        for (int i = 0; i < 3; i++)
            if (_ui.GlyphButton95(Ui.Id("wm", i), new RectangleF(bx + i * 26, g.Y + 18, 24, 20), glyphs[i], has)) OnAction?.Invoke(ids[i]);
        float tx = box.Right + 6, tw = bx - 8 - tx;
        if (!has) { _ui.Text95("Nothing playing", new RectangleF(tx, g.Y + 14, tw, 13), Gry95); return; }
        float t1 = _ui.Width95(Fit(title, tw, Ui.F95, true), true);
        _ui.Text95(Fit(title, tw, Ui.F95, true), new RectangleF(tx, g.Y + 14, tw, 13), Blk95, true);
        if (!string.IsNullOrEmpty(artist) && t1 + 30 < tw)
            _ui.Text95(Fit(" - " + artist, tw - t1), new RectangleF(tx + t1, g.Y + 14, tw - t1, 13), XpGrey);
        var (tok, pos, dur) = Timeline(d);
        SeekXp(new RectangleF(tx, g.Y + 32, tw, 7), tok && dur > 0 ? (float)Math.Clamp(pos / dur, 0, 1) : 0);
    }

    // Windows Media Player's seek bar: a white rounded trough, the played part a blue gradient
    void SeekXp(RectangleF r, float t)
    {
        _p.Fill(RectangleF.Inflate(r, -1, -1), 0xFFFFFFFF);
        var e = new RectangleF(r.X, r.Y, r.Width, r.Height);
        _p.Fill(new RectangleF(e.X + 1, e.Y, e.Width - 2, 1), 0xFF8E9DB6);
        _p.Fill(new RectangleF(e.X + 1, e.Bottom - 1, e.Width - 2, 1), 0xFFB8C3D6);
        _p.Fill(new RectangleF(e.X, e.Y + 1, 1, e.Height - 2), 0xFF8E9DB6);
        _p.Fill(new RectangleF(e.Right - 1, e.Y + 1, 1, e.Height - 2), 0xFFB8C3D6);
        float w = MathF.Round((r.Width - 2) * t);
        if (w >= 1) RetroSkin.RowsGradient(_p, new RectangleF(r.X + 1, r.Y + 1, w, r.Height - 2), 0xFF8DB8F7, 0xFF2D63D6);
    }

    // the toolbar: big buttons, each its picture over its name
    void ToolbarXp(PanelData d, float y)
    {
        string[] ids = { "keyboard", "desktops", "recenter", "layout", "night", "settings" };
        W95Icons.Id[] icons =
        {
            W95Icons.Id.Keyboard, d.DesktopsVisible ? W95Icons.Id.Computer : W95Icons.Id.ComputerOff, W95Icons.Id.Center,
            W95Icons.Id.Layouts, W95Icons.Id.Moon, W95Icons.Id.Gear,
        };
        // six across the window, 2 px apart: wide enough for "Keyboard" under its picture
        const int n = 6;
        float bw = MathF.Floor((TW95 - 8 - (n - 1) * 2) / (float)n);
        float x0 = MathF.Floor((TW95 - (n * bw + (n - 1) * 2)) / 2);
        for (int i = 0; i < n; i++)
        {
            var r = new RectangleF(x0 + i * (bw + 2), y, bw, 32);
            bool on = (i == 0 && d.KeyboardVisible) || (i == 1 && d.DesktopsVisible) || (i == 4 && d.NightMode);
            string label = i switch { 0 => "Keyboard", 1 => "Monitors", 2 => "Recenter", 3 => d.LayoutName, 4 => "Night", _ => "Settings" };
            if (_ui.Button95(Ui.Id("wt", i), r, Ui.Esc95(Fit(label, bw - 4)), true, false, on, icons[i])) OnAction?.Invoke(ids[i]);
        }
    }

    // the status bar along the window's bottom: frame timing in panes, the size grip in the corner
    void StatusBarXp(PanelData d, RectangleF r)
    {
        _p.Fill(new RectangleF(r.X, r.Y, r.Width, 1), 0xFFD8D4C6);
        Status95(d, new RectangleF(r.X + 2, r.Y - 2, r.Width - 16, 24));
        Grip(r.Right - 12, r.Bottom - 12);
    }

    // XP's size grip: dots in a triangle
    void Grip(float x, float y)
    {
        for (int row = 0; row < 3; row++)
            for (int col = 2 - row; col < 3; col++)
            {
                _p.Fill(new RectangleF(x + col * 4 + 1, y + row * 4 + 1, 2, 2), 0xFFFFFFFF);
                _p.Fill(new RectangleF(x + col * 4, y + row * 4, 2, 2), 0xFFB8B4A4);
            }
    }

    // ================= the custom face =================

    partial void CustomXp(PanelData d, ref bool done)
    {
        // the windows' corners over the desktop (or over nothing, on the cards background)
        XpSkin.CornerBack = C.Background != "cards" ? XpSkin.DesktopBlue : 0;
        XpSkin.WidgetBack = XpSkin.CornerBack;
    }

    // which widgets get a title bar when they are one row high (the others need the row for themselves)
    static bool TitleOneRow(string id, int w) => id switch
    {
        "time" or "date" or "calendar" or "session" or "fps" or "reproj" or "gpu" or "vram" or "cpu" or "ram" or "perf" or "graph" => true,
        "timer" or "battery" or "volume" or "stopwatch" => w == 1,
        "media" => w >= 3,
        _ => false,
    };

    // the bar's title: the 95 window's name, a short one on a narrow window (XP's own names where its window was another one)
    static string XpTitle(WatchWidgets.Kind k, int w) => k.Id == "media" ? "Now Playing" : w >= 2 ? k.Title95 : k.Id switch
    {
        "fps" => "FPS", "reproj" => "Reproj.", "gpu" => "GPU", "vram" => "VRAM", "cpu" => "CPU", "ram" => "RAM", "session" => "In VR",
        "time" => "Time", "date" => "Date", "calendar" => "Date", "timer" => "Timer", "stopwatch" => "Watch", "battery" => "Power",
        "volume" => "Volume", "room" => "Room", "dock" => "Hexpad", "minesweeper" => "Mines", "mic" => "Mic", _ => k.Title95,
    };

    partial void WidgetXp(WatchWidget w, WatchWidgets.Kind k, RectangleF r, PanelData d, int id, ref bool done)
    {
        done = true;
        if (k.Button) { ButtonWidgetXp(id, w, k, r, d); return; }
        if (k.Id is "photo" or "memory") { PhotoWidgetXp(r, w, d, id); return; }      // My Pictures' thumbnail (WristPhoto.Xp.cs)
        string title = w.H >= 2 || TitleOneRow(k.Id, w.W) ? XpTitle(k, w.W) : null;
        var c = Theme.RetroSkin.WidgetFrame(_p, r, title);
        var now = DateTime.Now;
        switch (k.Id)
        {
            case "time" when w.W == 1: ValueXp(c, Clk(now, _cfg.Data.Appearance.Clock24h ? "H:mm" : "h:mm", CultureInfo.CurrentCulture),
                    _cfg.Data.Appearance.Clock24h ? Clk(now, "ddd d", CultureInfo.CurrentCulture) : now.Hour < 12 ? "AM" : "PM", XpNavy); break;
            case "time": TimeWidgetXp(c, now, w.H >= 2); break;
            case "date" when w.W == 1: Calendar95(c, w, now); break;
            case "date": DateWidgetXp(c, now, w); break;
            case "calendar": Calendar95(c, w, now); break;
            case "analog":
                DialXp(now, MathF.Floor(c.X + c.Width / 2) + 0.5f, MathF.Floor(c.Y + c.Height / 2) + 0.5f, MathF.Floor(MathF.Min(c.Width, c.Height) / 2) - 4);
                break;
            case "timer": Timer95(c, w, id); break;
            case "stopwatch": Stopwatch95(c, w, id); break;
            case "battery" when w.H >= 2: BatteryWidgetXp(c, d); break;
            case "battery": Battery95(c, d, w); break;
            case "media": MediaWidgetXp(c, d, w, id); break;
            case "dock": Dock95Widget(r, c, d, w, id); break;
            case "room": Room95Widget(r, c, d, w, id); break;
            case "volume": Volume95(c, w, id); break;
            case "graph": GraphXp(d.Vr, c, r, title); break;
            case "minesweeper": Mines95Widget(c, w); break;
            case "mic": Mic95(r, c, w, id); break;
            case "notifications": Notes95(c, d); break;
            case "buttons": Group95(c, w, d, id); break;
            case "mixer": Mixer95(r, c, w, id); break;
            case "weather": Weather95(c, d, w); break;
            case "friends": Friends95(c, w, d); break;
            case "recap": Recap95(c, w, d); break;
            case "light": Light95(r, c, w, d, id); break;
            case "rgb": Rgb95(r, c, w, d, id); break;
            case "temps": Temps95(c, d, w); break;
            case "avatar": Avatar95(r, c, w, d, id); break;
            case "perf": Status95(d, new RectangleF(c.X, MathF.Floor(c.Y + (c.Height - 24) / 2), c.Width, 24)); break;
            case "session":
                string sv = SessionText(d);
                ValueXp(c, sv == "–" ? "--" : sv, w.W >= 2 ? "time in VR" : "in VR", XpNavy);
                break;
            default: StatWidgetXp(c, k, w, d); break;
        }
    }

    // a reading: the number big and navy (amber / red when it matters), what it is small and grey under it
    void ValueXp(RectangleF c, string value, string caption, uint color)
    {
        bool cap = !string.IsNullOrEmpty(caption) && c.Height >= 26;
        float room = c.Height - (cap ? 13 : 0);
        float size = MathF.Min(MathF.Floor(room * 0.8f), 24);
        while (size > 11 && _ui.Width95(value, true, size) > c.Width - 4) size--;
        float vh = MathF.Ceiling(size * 1.15f);
        float vy = MathF.Floor(c.Y + (c.Height - vh - (cap ? 12 : 0)) / 2);
        Xp.TextAt(_p, Fit(value, c.Width - 2, size, true), new RectangleF(c.X, vy, c.Width, vh), color, size, FontWeight.Bold, TextAlignment.Center);
        if (cap) _ui.Text95(Fit(caption, c.Width - 2), new RectangleF(c.X, vy + vh, c.Width, 12), XpGrey, false, TextAlignment.Center);
    }

    void StatWidgetXp(RectangleF c, WatchWidgets.Kind k, WatchWidget w, PanelData d)
    {
        var (frac, value, unit, _, lv) = Reading(k.Id, d);
        uint color = lv == StatLevel.Bad ? XpBad : lv == StatLevel.Warn ? XpWarn : XpNavy;
        if (value == "–") value = "--";
        var v = d.Vr;
        string cap = k.Id switch
        {
            "fps" => v.DisplayHz > 0 ? $"/ {v.DisplayHz:0}" : "fps",
            "reproj" => "frames",
            "gpu" => "ms GPU",
            "vram" => d.VramTotalGB > 0 ? $"/ {d.VramTotalGB:0} GB" : "GB",
            "cpu" => "load",
            _ => Stats.RamTotalGB > 0 ? $"/ {Stats.RamTotalGB:0} GB" : "GB",
        };
        if (w.W >= 2 && (k.Id is "cpu" or "ram" or "vram") && frac >= 0)
        {
            // Task Manager's way: the number, the bar under it
            string s = Fit($"{value}{(unit == "GB" ? " GB" : "")} {Lang.T(cap)}", c.Width - 8);
            _ui.Text95(s, new RectangleF(c.X + 4, c.Y + 2, c.Width - 8, 13), color == XpNavy ? Blk95 : color, true);
            _ui.Progress95(new RectangleF(c.X + 4, c.Y + 17, c.Width - 8, MathF.Min(12, c.Height - 19)), frac, color == XpBad ? 0xFF800000 : Theme.W95Navy);
            return;
        }
        if (k.Id == "fps" && w.W >= 2) cap = v.DisplayHz > 0 ? Lang.F("fps of {0:0} Hz", v.DisplayHz) : "fps";
        ValueXp(c, value, cap, color);
    }

    // the time: navy hours and minutes as big as fit (the seconds small), the date under them on a tall window
    void TimeWidgetXp(RectangleF c, DateTime now, bool tall)
    {
        bool h24 = _cfg.Data.Appearance.Clock24h;
        var culture = CultureInfo.CurrentCulture;
        string hm = Clk(now, h24 ? "H:mm" : "h:mm", culture), sec = Clk(now, ":ss", culture);
        string ampm = h24 ? null : now.Hour < 12 ? "AM" : "PM";
        bool withDate = tall && c.Height >= 44;
        var tr = withDate ? new RectangleF(c.X, c.Y, c.Width, c.Height - 15) : c;
        BigTimeXp(hm, tall ? sec : "", ampm, RectangleF.Inflate(tr, -2, 0), 60, true);
        if (withDate)
            _ui.Text95(FitDate95(now, c.Width - 6), new RectangleF(c.X, c.Bottom - 15, c.Width, 13), Blk95, false, TextAlignment.Center);
    }

    // the date: the weekday blue and small, the day bold under it
    void DateWidgetXp(RectangleF c, DateTime now, WatchWidget w)
    {
        var culture = CultureInfo.CurrentCulture;
        string wd = Clk(now, "dddd", culture);
        string day = Clk(now, culture.DateTimeFormat.MonthDayPattern, culture);
        if (c.Height < 30 || w.W >= 4)
        {
            // one line: "Thursday, October 15"
            string s = FitDate95(now, c.Width - 8);
            float size = w.W >= 4 && _ui.Width95(s, true, 13) <= c.Width - 10 ? 13 : Ui.F95;
            _ui.Text95(s, c, Blk95, true, TextAlignment.Center, size: size);
            return;
        }
        float ds = 16;
        while (ds > 11 && _ui.Width95(day, true, ds) > c.Width - 8) ds--;
        float y = MathF.Floor(c.Y + (c.Height - 13 - ds * 1.2f) / 2);
        _ui.Text95(Fit(wd, c.Width - 8), new RectangleF(c.X + 4, y, c.Width - 8, 13), XpBlueText);
        _ui.Text95(Fit(day, c.Width - 8, ds, true), new RectangleF(c.X + 4, y + 13, c.Width - 8, MathF.Ceiling(ds * 1.2f)), Blk95, true, size: ds);
    }

    // the batteries on a tall window: the name and its percentage on a line, a full-width green bar under it
    void BatteryWidgetXp(RectangleF c, PanelData d)
    {
        var bats = d.Vr.Batteries;
        if (bats.Count == 0)
        {
            _ui.Text95(Fit(d.VrConnected ? "No battery info" : "SteamVR is off", c.Width - 4), c, Gry95, false, TextAlignment.Center);
            return;
        }
        int rows = Math.Max(1, Math.Min(4, (int)((c.Height - 2) / 25)));
        var pick = PickBatteries(bats, rows);
        float pitch = MathF.Floor((c.Height - 2) / Math.Max(rows, pick.Count));
        float y0 = c.Y + 2 + MathF.Floor((c.Height - 2 - pick.Count * pitch) / 2);
        for (int i = 0; i < pick.Count; i++)
        {
            var b = bats[pick[i]];
            float y = y0 + i * pitch;
            bool low = b.pct <= 0.2f && !b.charging;
            string pct = $"{b.pct * 100:0}%";
            float pw = _ui.Width95(pct, true) + 2;
            _ui.Text95(Fit(b.charging ? Bat(b.label) + " +" : Bat(b.label), c.Width - 8 - pw - 4), new RectangleF(c.X + 4, y, c.Width - 8 - pw, 12));
            _ui.Text95(pct, new RectangleF(c.Right - 4 - pw, y, pw, 12), low ? XpBad : b.charging ? XpBlueText : Blk95, true, TextAlignment.Trailing);
            _ui.Progress95(new RectangleF(c.X + 4, y + 13, c.Width - 8, 10), b.pct, low ? 0xFF800000 : Theme.W95Navy);
        }
    }

    // now playing: the cover, the title bold, the artist grey, the seek bar, |<  ||  >| (the volume under it on a big window)
    void MediaWidgetXp(RectangleF c, PanelData d, WatchWidget w, int id)
    {
        var (has, title, artist, playing) = MediaNow(d);
        float x = c.X + 2;
        bool tall = w.H >= 2 && c.Height >= 60;
        float art = tall ? MathF.Min(MathF.Floor(c.Height * 0.55f), 44) : MathF.Min(c.Height - 6, 36);
        if (w.W >= 3 || tall)
        {
            var box = new RectangleF(x, c.Y + 3, art, art);
            var inner = RectangleF.Inflate(box, -1, -1);
            _p.Fill(inner, Wht95);
            if (_art != null) _p.Image(_art, inner);
            else W95Icons.Draw(_p, W95Icons.Id.Note, MathF.Floor(inner.X + (inner.Width - 16) / 2), MathF.Floor(inner.Y + (inner.Height - 16) / 2));
            _p.Bevel(box, Painter.Edge.Sunken);
            x = box.Right + 5;
        }
        string[] ids = { "media.prev", "media.play", "media.next" };
        string[][] glyphs = { PrevGlyph, playing ? PauseGlyph : PlayGlyph, NextGlyph };
        // the buttons: under the text on a tall window, at the right on a low one
        int nb = (tall ? c.Right - x : c.Width) >= 150 ? 3 : 1;
        float bw = 24, bh = 18, total = nb * bw + (nb - 1) * 2;
        float bx = tall ? x : c.Right - 2 - total, by = tall ? c.Y + 3 + art - bh : MathF.Floor(c.Y + (c.Height - bh) / 2);
        for (int j = 0; j < nb; j++)
        {
            int a = nb == 1 ? 1 : j;
            if (_ui.GlyphButton95(Ui.Id("wcm", id * 3 + a), new RectangleF(bx + j * (bw + 2), by, bw, bh), glyphs[a], has)) OnAction?.Invoke(ids[a]);
        }
        float tr = tall ? c.Right - 2 : bx - 5, tw = tr - x;
        if (tw >= 20)
        {
            float ty = c.Y + 3;
            if (!tall) ty = MathF.Floor(c.Y + (c.Height - 26) / 2);
            if (!has) _ui.Text95(Fit("Nothing playing", tw), new RectangleF(x, ty, tw, 13), Gry95);
            else
            {
                _ui.Text95(Fit(title ?? "", tw, Ui.F95, true), new RectangleF(x, ty, tw, 13), Blk95, true);
                _ui.Text95(Fit(artist ?? "", tw), new RectangleF(x, ty + 13, tw, 13), XpGrey);
            }
            if (tall && has && by - (ty + 27) >= 6)
            {
                var (tok, pos, dur) = Timeline(d);
                SeekXp(new RectangleF(x, ty + 29, tw, 6), tok && dur > 0 ? (float)Math.Clamp(pos / dur, 0, 1) : 0);
            }
        }
        if (tall && c.Width >= 150 && c.Bottom - (c.Y + art + 6) >= 30)
            Volume95Row(new RectangleF(c.X + 4, c.Y + art + 6, c.Width - 8, 30), Ui.Id("wcv", id));
    }

    // Task Manager's performance graph: black, the green grid, the frame times as a bright green line, the budget dashed yellow;
    // the numbers on the window's bar (right of its title) when they fit
    void GraphXp(VrStats v, RectangleF c, RectangleF r, string title)
    {
        PlotXp(v, c, false);
        if (title == null) return;
        string label = v.AppFps < 0 ? "" : $"{v.AppFps:0} fps · {v.GpuMs:0.0} ms";
        float tw = _ui.Width95(title, true), lw = _ui.Width95(label);
        if (label.Length > 0 && 4 + tw + 10 + lw <= r.Width - 8)
            _p.Text(label, new RectangleF(r.Right - 5 - lw, r.Y + 1, lw + 2, XpSkin.WidgetBar), 0xFFD8E6FF, Ui.F95, FontWeight.Normal,
                TextAlignment.Leading, ParagraphAlignment.Center, Theme.Font);
    }

    // Task Manager's history graph: black, the dark green grid scrolling with the frames, the frame times as a bright green line
    // (a dropped frame's step red), the frame budget dashed yellow; label: the numbers in green at its top left, as Task Manager
    // writes nothing over its graphs but the watch has no room beside them
    const uint TaskGrid = 0xFF008040, TaskLine = 0xFF00FF00;

    // the samples that came in so far (the histories only count up to their length): Task Manager's grid moves with them
    int _xpFrameHead = -1, _xpCpuHead = -1;
    long _xpFrames, _xpCpu;

    long FramesSeenXp(VrStats v)
    {
        if (_xpFrameHead >= 0) _xpFrames += (v.FrameHead - _xpFrameHead + VrStats.FrameHistory) % VrStats.FrameHistory;
        _xpFrameHead = v.FrameHead;
        return _xpFrames;
    }

    long CpuSeenXp()
    {
        if (_xpCpuHead >= 0) _xpCpu += (Stats.CpuHead - _xpCpuHead + SystemStats.History) % SystemStats.History;
        _xpCpuHead = Stats.CpuHead;
        return _xpCpu;
    }

    void PlotXp(VrStats v, RectangleF c, bool label)
    {
        _p.Fill(c, 0xFF000000);
        var g = RectangleF.Inflate(c, -1, -1);
        float step = g.Width / VrStats.FrameHistory;
        // the grid scrolls with the frames, as Task Manager's with its samples
        float scroll = XpAeroFx.Moves ? MathF.Floor(FramesSeenXp(v) * step % 12) : v.FrameCount / 2 % 12;
        for (float gx = g.Right - 1 - scroll; gx >= g.X; gx -= 12) _p.Fill(new RectangleF(gx, g.Y, 1, g.Height), TaskGrid);
        for (float gy = g.Bottom - 1; gy >= g.Y; gy -= 10) _p.Fill(new RectangleF(g.X, gy, g.Width, 1), TaskGrid);
        float budget = 1000f / Math.Max(1, v.DisplayHz), top = budget * 2;
        int n = v.FrameCount;
        float by = MathF.Round(g.Bottom - 1 - (g.Height - 2) * (budget / top));
        for (float dx = g.X; dx < g.Right; dx += 6) _p.Fill(new RectangleF(dx, by, MathF.Min(3, g.Right - dx), 1), 0xFFFFE000);
        float px = -1, py = 0;
        for (int i = 0; i < n; i++)
        {
            int idx = (v.FrameHead - n + i + VrStats.FrameHistory) % VrStats.FrameHistory;
            float ms = v.FrameMs[idx];
            float x = MathF.Floor(g.Right - (n - i) * step);
            float y = MathF.Round(g.Bottom - 1 - (g.Height - 2) * Math.Clamp(ms / top, 0.02f, 1));
            if (px >= 0 && x != px) _p.Line(px + 0.5f, py + 0.5f, x + 0.5f, y + 0.5f, v.FrameFlag[idx] == 2 ? 0xFFFF3030 : TaskLine, 1);
            else if (px < 0) _p.Fill(new RectangleF(x, y, 1, 1), TaskLine);
            px = x; py = y;
        }
        if (!label) return;
        string s = v.AppFps < 0 ? "waiting for frames" : $"{v.AppFps:0} fps  {v.GpuMs:0.0} ms GPU  {Math.Max(0, v.ReprojPct):0}% reproj.";
        if (v.AppFps >= 0 && _ui.Width95(s) > g.Width - 12) s = $"{v.AppFps:0} fps  {v.GpuMs:0.0} ms  {Math.Max(0, v.ReprojPct):0}%";
        s = Fit(s, g.Width - 12);
        _p.Fill(new RectangleF(g.X + 2, g.Y + 1, MathF.Min(_ui.Width95(s) + 4, g.Width - 4), 12), 0xFF000000);
        _ui.Text95(s, new RectangleF(g.X + 4, g.Y + 1, g.Width - 8, 12), TaskLine);
    }

    // a graph in its sunken well (Task Manager's: the 1 px grey-blue edge of a themed control)
    void WellXp(RectangleF r)
    {
        _p.Fill(new RectangleF(r.X + 1, r.Y, r.Width - 2, 1), 0xFF7F9DB9);
        _p.Fill(new RectangleF(r.X + 1, r.Bottom - 1, r.Width - 2, 1), 0xFF7F9DB9);
        _p.Fill(new RectangleF(r.X, r.Y + 1, 1, r.Height - 2), 0xFF7F9DB9);
        _p.Fill(new RectangleF(r.Right - 1, r.Y + 1, 1, r.Height - 2), 0xFF7F9DB9);
    }

    // Task Manager's usage meter: a black box, two columns of green segments (lit up to the reading), the reading under them
    void MeterXp(RectangleF r, float frac, string text)
    {
        _p.Fill(r, 0xFF000000);
        WellXp(r);
        var inner = new RectangleF(r.X + 6, r.Y + 5, r.Width - 12, r.Height - 5 - 17);
        int rows = (int)((inner.Height + 1) / 3);
        int lit = frac < 0 ? 0 : (int)MathF.Round(Math.Clamp(frac, 0, 1) * rows);
        float cw = MathF.Floor((inner.Width - 1) / 2);
        for (int i = 0; i < rows; i++)
        {
            float y = inner.Bottom - 2 - i * 3;
            uint col = i < lit ? TaskLine : TaskGrid;
            _p.Fill(new RectangleF(inner.X, y, cw, 2), col);
            _p.Fill(new RectangleF(inner.X + cw + 1, y, cw, 2), col);
        }
        _ui.Text95(Fit(text, r.Width - 4), new RectangleF(r.X, r.Bottom - 16, r.Width, 13), TaskLine, false, TextAlignment.Center);
    }

    // a button widget: an XP button, its picture over its name when it is big enough
    void ButtonWidgetXp(int id, WatchWidget w, WatchWidgets.Kind k, RectangleF r, PanelData d)
    {
        bool click;
        if (k.Id == "layout")
            click = _ui.Button95(Ui.Id("wcb", id), r, Ui.Esc95(Fit(d.LayoutName, r.Width - 8)), true, false, false,
                r.Height >= 40 ? W95Icons.Id.Layouts : null);
        else
        {
            var icon = k.Id == "desktops" && !d.DesktopsVisible ? W95Icons.Id.ComputerOff : k.Icon95;
            string name = w.W >= 2 || _ui.Width95(k.Name) <= r.Width - 8 ? k.Name : _ui.Width95(k.Title95) <= r.Width - 8 ? k.Title95 : null;
            click = _ui.Button95(Ui.Id("wcb", id), r, name == null ? null : Ui.Esc95(Fit(name, r.Width - 8)), true, false, IsOn(k.Id, d), icon);
        }
        if (click) OnAction?.Invoke(k.Id);
    }

    // ================= the pages' taskbar =================

    // XP's taskbar: the blue bar with its light top edge, a button per page (the one in view pressed in, darker)
    partial void TaskbarXp(int count, int cur, ref bool done)
    {
        done = true;
        float y = TH95 - WatchWidgets.Taskbar95 - 2;
        var bar = new RectangleF(0, y, TW95, TH95 - y);
        uint[] rows = { 0xFF0C2E8A, 0xFF5D96F5, 0xFF3F7DED, 0xFF316FE6, 0xFF2A66E0 };
        for (int i = 0; i < rows.Length; i++) _p.Fill(new RectangleF(0, y + i, TW95, 1), rows[i]);
        RetroSkin.RowsGradient(_p, new RectangleF(0, y + rows.Length, TW95, bar.Height - rows.Length), 0xFF2560DB, 0xFF1A49B8);
        float bw = MathF.Floor(MathF.Min(104, (TW95 - 6 - (count - 1) * 3) / (float)count));
        for (int i = 0; i < count; i++)
        {
            var b = new RectangleF(3 + i * (bw + 3), y + 4, bw, TH95 - y - 6);
            bool click = _ui.Clickable(Ui.Id("wtb", i), b, out bool hover, out bool held);
            bool on = i == cur;
            TaskButtonXp(b, Fit(WatchPages.Label(C, i), bw - 12), on, hover, held);
            if (click) GoToPage(i);
        }
    }

    // a taskbar button: rounded, lighter blue with white text; pressed in (the page in view) a deep blue
    void TaskButtonXp(RectangleF b, string name, bool on, bool hover, bool held)
    {
        uint top, bottom, edge;
        // the lighter blue under the pointer glows up and away (a tenth of a second in, a little longer out)
        float h = XpAeroFx.Fade(XpAeroFx.Task, b, hover && !on && !held, XpSkin.HotIn, 0.25f);
        if (on || held) (top, bottom, edge) = (0xFF1A43A9, 0xFF2156C8, 0xFF0E2E80);
        else if (h >= 1) (top, bottom, edge) = (0xFF6FA7FF, 0xFF4B8BF5, 0xFF2A5BCB);
        else if (h <= 0) (top, bottom, edge) = (0xFF5A97FA, 0xFF3A7BEE, 0xFF234FB8);
        else (top, bottom, edge) = (Theme.Mix(0xFF5A97FA, 0xFF6FA7FF, h), Theme.Mix(0xFF3A7BEE, 0xFF4B8BF5, h), Theme.Mix(0xFF234FB8, 0xFF2A5BCB, h));
        var inner = RectangleF.Inflate(b, -1, -1);
        RetroSkin.RowsGradient(_p, inner, top, bottom);
        _p.Fill(new RectangleF(b.X + 1, b.Y, b.Width - 2, 1), edge);
        _p.Fill(new RectangleF(b.X + 1, b.Bottom - 1, b.Width - 2, 1), edge);
        _p.Fill(new RectangleF(b.X, b.Y + 1, 1, b.Height - 2), edge);
        _p.Fill(new RectangleF(b.Right - 1, b.Y + 1, 1, b.Height - 2), edge);
        if (on) _p.Fill(new RectangleF(inner.X, inner.Y, inner.Width, 1), 0xFF10308A);           // pressed in: the shadow on top
        else _p.Fill(new RectangleF(inner.X, inner.Y, inner.Width, 1), 0xFF8EBBFF);              // raised: the light on top
        _p.Text(name, new RectangleF(b.X + 6, b.Y, b.Width - 8, b.Height), 0xFFFFFFFF, Ui.F95, on ? FontWeight.Bold : FontWeight.Normal,
            TextAlignment.Leading, ParagraphAlignment.Center, Theme.Font);
    }
}
