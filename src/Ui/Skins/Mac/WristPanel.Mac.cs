using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Numerics;
using Vortice.DirectWrite;

namespace Orbit;

// The watch in the Classic Mac style (MacSkin.cs). The fixed faces are a Platinum window laid out the Mac way: the close box on
// the left, the collapse box on the right; the time big in a white well with the date under it, the batteries beside it, now
// playing on a placard, a row of push buttons with words on them, the frame timing as the Finder's plain info line (or a graph
// on white). The custom face keeps the 95 grid and taps; the widgets that were 1995 pictures (the CD Player's green LCD, the
// black performance graphs, the teal dial) are drawn as a Mac had them. The opened views are Platinum windows.
public sealed partial class WristPanel
{
    static MacSkin Mac => (MacSkin)Skins.Mac;
    const uint MacWhite = 0xFFFFFFFF, MacBlack = 0xFF000000;

    // ================= the fixed faces =================

    partial void FaceMac(PanelData d, ref bool done)
    {
        done = true;
        MBeginFrame();
        var m = Mac;
        string face = C.Face ?? "full";
        var now = DateTime.Now;
        bool rich = face is "full" or "analog";
        bool media = rich && C.ShowMedia, bats = rich && C.ShowBatteries, perf = rich && C.ShowPerformance;
        bool tools = face != "time", graph = perf && C.PerfGraph;
        bool big = face is "minimal" or "time";
        float top = big ? 80 : 72;
        float h = 22 + top + (media ? 52 : 0) + (tools ? 34 : 0) + (perf ? (graph ? 32 : 20) : 0) + 3;
        float wy = MathF.Floor((TH95 - h) / 2);
        var win = new RectangleF(0, wy, TW95, h);
        if (MZoom(win)) return;                 // zooming out of the middle as it comes, back in after the close box
        float shade = MShade(win);              // rolled up into its title bar after the collapse box
        if (shade < win.Height) _p.PushClip(new RectangleF(win.X, win.Y, win.Width, shade));
        m.MacWindow(_p, win, "Clock");
        if (_ui.CaptionClose95("w95.x", MacSkin.CloseX(win), MacSkin.BoxY(win))) { _dismissReq = true; _mCloseAt = Clock.Now; }
        if (_ui.CaptionMin95("w95.min", MacSkin.CollapseX(win), MacSkin.BoxY(win))) { _dismissReq = true; _mShadeAt = Clock.Now; }   // the window shade

        float y = wy + 22;
        bool h24 = _cfg.Data.Appearance.Clock24h;
        string hm = Clk(now, h24 ? "H:mm" : "h:mm", CultureInfo.CurrentCulture);
        string ampm = h24 ? null : now.Hour < 12 ? "AM" : "PM";

        // the clock in its white well (the batteries beside it on the full face)
        bool side = bats && !big;
        var well = new RectangleF(8, y + 5, side ? 190 : TW95 - 16, top - 8);
        m.Well(_p, well);
        if (big)
        {
            BigTimeMac(hm, ampm, new RectangleF(well.X, well.Y + 3, well.Width, 52), 52, true);
            _ui.Text95(FitDate95(now, well.Width - 12), new RectangleF(well.X, well.Bottom - 17, well.Width, 13), MacBlack, false, TextAlignment.Center);
        }
        else if (face == "analog")
        {
            float rad = MathF.Floor((well.Height - 8) / 2);
            DialMac(now, well.X + 6 + rad, MathF.Floor(well.Y + well.Height / 2), rad);
            float tx = well.X + 14 + 2 * rad, tw = well.Right - 6 - tx;
            string hms = Clk(now, h24 ? "H:mm:ss" : "h:mm:ss", CultureInfo.CurrentCulture);
            float sz = _ui.Width95(hms, false, 26) + (ampm != null ? _ui.Width95(ampm, true) + 4 : 0) <= tw ? 26 : 13;
            BigTimeMac(hms, ampm, new RectangleF(tx, well.Y + 9, tw, 26), sz, false);
            _ui.Text95(FitDate95(now, tw), new RectangleF(tx, well.Y + 40, tw, 13), MacBlack);
        }
        else
        {
            BigTimeMac(hm, ampm, new RectangleF(well.X + 10, well.Y + 3, well.Width - 20, 39), 39, false);
            _ui.Text95(FitDate95(now, well.Width - 20), new RectangleF(well.X + 11, well.Bottom - 18, well.Width - 20, 13), MacBlack);
        }
        if (side) BatteriesMac(d, new RectangleF(well.Right + 8, well.Y, TW95 - 8 - well.Right - 8, well.Height));
        y += top;

        if (media)
        {
            var pl = new RectangleF(8, y + 1, TW95 - 16, 46);
            m.Inset(_p, pl);
            MediaMac(d, RectangleF.Inflate(pl, -5, -5), "wm", 3);
            y += 52;
        }
        else if (bats && big) y += 0;

        if (tools)
        {
            ToolbarMac(d, y + 3);
            y += 34;
        }

        if (perf)
        {
            if (graph) GraphMac(d.Vr, new RectangleF(8, y + 1, TW95 - 16, 28));
            else
            {
                m.EtchedLine(_p, 8, TW95 - 8, y);
                StatusMac(d, new RectangleF(10, y + 3, TW95 - 20, 14));
            }
        }
        if (shade < win.Height)
        {
            _p.PopClip();
            MShadeEdge(win, shade);
        }
    }

    // the time in Chicago blown up (size: 13 a line, 26 twice, 39 three times...), AM / PM after it in plain Chicago
    void BigTimeMac(string time, string ampm, RectangleF r, float size, bool center)
    {
        float tw = _ui.Width95(time, false, size), aw = ampm == null ? 0 : _ui.Width95(ampm, true) + 4;
        float x = center ? r.X + MathF.Round((r.Width - tw - aw) / 2) : r.X;
        _ui.Text95(time, new RectangleF(x, r.Y, tw + 4, r.Height), MacBlack, size: size);
        // the capitals of a blown-up Chicago end at 11/13 of its line: AM / PM sit on that baseline
        if (ampm != null) _ui.Text95(ampm, new RectangleF(x + tw + 4, r.Y + MathF.Round(r.Height * 11 / 13f) - 11, 30, 13), MacBlack, true);
    }

    // the face's buttons: words when they fit (the Mac had no toolbar icons), the picture when not; a button that is on is
    // the highlight colour
    void ToolbarMac(PanelData d, float y)
    {
        string[] ids = { "keyboard", "desktops", "recenter", "layout", "night", "settings" };
        string[] words = { "Keys", "Monitors", "Center", null, "Night", "Settings" };
        const int n = 6;
        float bw = MathF.Floor((TW95 - 10 - (n - 1) * 2) / (float)n);
        float x0 = MathF.Floor((TW95 - (n * bw + (n - 1) * 2)) / 2);
        for (int i = 0; i < n; i++)
        {
            var r = new RectangleF(x0 + i * (bw + 2), y, bw, 26);
            bool on = (i == 0 && d.KeyboardVisible) || (i == 1 && d.DesktopsVisible) || (i == 4 && d.NightMode);
            // the body only (Platinum, the highlight colour when on), then the word: Geneva, Chicago on the one that is on
            if (_ui.Button95(Ui.Id("wt", i), r, null, true, false, on)) Act(ids[i]);
            var face = RectangleF.Inflate(r, -2, -2);
            string word = words[i] ?? d.LayoutName ?? "";
            bool bold = on && _ui.Width95(word, true) <= face.Width - 3;
            _ui.Text95(Fit(word, face.Width - 1, Ui.F95, bold), new RectangleF(face.X, face.Y, face.Width, face.Height), on ? MacWhite : MacBlack, bold,
                TextAlignment.Center);
        }
    }

    // batteries: the name in Chicago, a Platinum progress bar, the percentage
    void BatteriesMac(PanelData d, RectangleF r)
    {
        var bats = d.Vr.Batteries;
        if (bats.Count == 0)
        {
            _ui.Text95(Fit(d.VrConnected ? "No battery info" : "SteamVR is off", r.Width), r, Gry95, false, TextAlignment.Center);
            return;
        }
        var pick = PickBatteries(bats, Math.Min(4, (int)(r.Height / 14)));
        float pitch = MathF.Min(15, MathF.Floor(r.Height / pick.Count));
        float y0 = MathF.Floor(r.Y + (r.Height - pick.Count * pitch) / 2);
        float lw = 20;
        foreach (int i in pick) lw = MathF.Max(lw, _ui.Width95(Bat(bats[i].label), true) + 2);
        lw = MathF.Min(lw, MathF.Floor(r.Width * 0.4f));
        for (int i = 0; i < pick.Count; i++)
        {
            var b = bats[pick[i]];
            float yy = y0 + i * pitch + MathF.Floor((pitch - 13) / 2);
            bool low = b.pct <= 0.2f && !b.charging;
            _ui.Text95(Fit(Bat(b.label), lw - 1, Ui.F95, true), new RectangleF(r.X, yy, lw, 13), MacBlack, true);
            var bar = new RectangleF(r.X + lw + 1, yy + 2, r.Width - lw - 1 - 30, 10);
            if (bar.Width >= 12) _ui.Progress95(bar, b.pct, low ? 0xFF800000 : Theme.W95Navy);
            if (b.charging) _p.Pixels(BoltGlyph, bar.Right + 2, yy + 4, MacBlack);
            _ui.Text95($"{b.pct * 100:0}%", new RectangleF(r.Right - 26, yy, 26, 13), low ? 0xFF800000 : MacBlack, false, TextAlignment.Trailing);
        }
    }

    // now playing: the cover, the title in Chicago and the artist under it, the buttons (n = 3, or 1: play / pause alone) on the right
    void MediaMac(PanelData d, RectangleF r, string key, int n, bool cover = true)
    {
        var (has, title, artist, playing) = MediaNow(d);
        float x = r.X;
        if (cover && r.Height >= 26)
        {
            float s = MathF.Min(r.Height, 40);
            var box = new RectangleF(r.X, MathF.Floor(r.Y + (r.Height - s) / 2), s, s);
            CoverMac(box);
            x = box.Right + 7;
        }
        float bw = 28, bh = 20, gap = 3;
        float bx = r.Right - (n * bw + (n - 1) * gap);
        float tw = bx - 6 - x;
        if (has)
        {
            _ui.Text95(Fit(title, tw, Ui.F95, true), new RectangleF(x, MathF.Floor(r.Y + r.Height / 2) - 14, tw, 14), MacBlack, true);
            _ui.Text95(Fit(artist ?? "", tw), new RectangleF(x, MathF.Floor(r.Y + r.Height / 2), tw, 13), Gry95);
        }
        else _ui.Text95(Fit("Nothing playing", tw), new RectangleF(x, r.Y, tw, r.Height), Gry95);
        string[] ids = { "media.prev", "media.play", "media.next" };
        string[][] glyphs = { PrevGlyph, playing ? PauseGlyph : PlayGlyph, NextGlyph };
        float by = MathF.Floor(r.Y + (r.Height - bh) / 2);
        for (int j = 0; j < n; j++)
        {
            int a = n == 1 ? 1 : j;
            if (_ui.GlyphButton95(Ui.Id(key, a), new RectangleF(bx + j * (bw + gap), by, bw, bh), glyphs[a], has)) Act(ids[a]);
        }
    }

    // the album cover in a black frame (no cover: the Mac's note on white)
    void CoverMac(RectangleF box)
    {
        _p.Fill(box, MacBlack);
        var inner = RectangleF.Inflate(box, -1, -1);
        _p.Fill(inner, MacWhite);
        if (_art != null) _p.Image(_art, inner);
        else W95Icons.Draw(_p, W95Icons.Id.Note, MathF.Floor(inner.X + (inner.Width - 16) / 2), MathF.Floor(inner.Y + (inner.Height - 16) / 2));
    }

    // a clock face as the Mac drew one: white, a black rim, the hour marks, black hands, the second hand in the highlight colour
    void DialMac(DateTime now, float cx, float cy, float r)
    {
        cx = MathF.Floor(cx); cy = MathF.Floor(cy); r = MathF.Floor(r);
        MacSkin.Disc(_p, cx, cy, r, MacWhite, MacBlack, r >= 24 ? 2 : 1);
        for (int i = 0; i < 12; i++)
        {
            bool q = i % 3 == 0;
            if (!q && r < 16) continue;
            float a = i / 12f * MathF.Tau - MathF.PI / 2, ca = MathF.Cos(a), sa = MathF.Sin(a);
            float r1 = r - (r >= 24 ? 4 : 3), r0 = r1 - MathF.Max(2, MathF.Round(r * (q ? 0.16f : 0.09f)));
            if (q)
            {
                // the quarters: little bars on whole pixels, two wide
                float tx = MathF.Round(cx + ca * (r0 + r1) / 2), ty = MathF.Round(cy + sa * (r0 + r1) / 2), len = r1 - r0 + 1;
                bool vert = i % 6 == 0;
                _p.Fill(vert ? new RectangleF(tx, MathF.Round(ty - len / 2), 2, len) : new RectangleF(MathF.Round(tx - len / 2), ty, len, 2), MacBlack);
            }
            else _p.Line(cx + 1 + ca * r0, cy + 1 + sa * r0, cx + 1 + ca * r1, cy + 1 + sa * r1, MacBlack, 1);
        }
        float s = now.Second, mm = now.Minute + s / 60f, hr = now.Hour % 12 + mm / 60f;
        HandMac(cx + 1, cy + 1, hr / 12f, r * 0.52f, MathF.Max(1.5f, r * 0.075f));
        HandMac(cx + 1, cy + 1, mm / 60f, r * 0.8f, MathF.Max(1f, r * 0.05f));
        float sa2 = s / 60f * MathF.Tau - MathF.PI / 2;
        if (r >= 16) _p.Line(cx + 1, cy + 1, cx + 1 + r * 0.84f * MathF.Cos(sa2), cy + 1 + r * 0.84f * MathF.Sin(sa2), Theme.W95Navy, 1);
        _p.Fill(new RectangleF(cx - 1, cy - 1, 4, 4), MacBlack);
    }

    void HandMac(float cx, float cy, float frac, float len, float half)
    {
        float a = frac * MathF.Tau - MathF.PI / 2;
        var dir = new Vector2(MathF.Cos(a), MathF.Sin(a));
        var perp = new Vector2(-dir.Y, dir.X);
        var c = new Vector2(cx, cy);
        _p.Polygon(new[] { c - dir * (len * 0.12f) + perp * half, c + dir * len + perp * (half * 0.45f), c + dir * len - perp * (half * 0.45f),
            c - dir * (len * 0.12f) - perp * half }, MacBlack);
    }

    // the frame timing on white: dotted guides, the frames as a black line, the budget dashed, the reading in Chicago top right
    void GraphMac(VrStats v, RectangleF r)
    {
        var m = Mac;
        m.Well(_p, r);
        var g = RectangleF.Inflate(r, -3, -3);
        float budget = 1000f / Math.Max(1, v.DisplayHz), top = budget * 2;
        for (float gy = g.Y + 4; gy < g.Bottom - 1; gy += 6) MacSkin.DotsH(_p, g.X + (gy - g.Y) % 4, g.Right, gy, MacSkin.Grey3, 4);
        float by = MathF.Round(g.Bottom - 1 - (g.Height - 1) * (budget / top));
        for (float x = g.X; x < g.Right; x += 6) _p.Fill(new RectangleF(x, by, MathF.Min(3, g.Right - x), 1), MacSkin.Grey5);
        int n = v.FrameCount;
        int cols = (int)g.Width;
        float prev = -1;
        for (int col = 0; col < cols; col++)
        {
            // the frames under this column (newest on the right): the slowest of them
            int i0 = (int)((col / (float)cols) * VrStats.FrameHistory), i1 = (int)(((col + 1) / (float)cols) * VrStats.FrameHistory);
            float ms = -1; bool bad = false;
            for (int i = i0; i < Math.Max(i1, i0 + 1); i++)
            {
                int age = VrStats.FrameHistory - 1 - i;           // 0 = the newest
                if (age >= n) continue;
                int idx = (v.FrameHead - 1 - age + VrStats.FrameHistory * 2) % VrStats.FrameHistory;
                ms = MathF.Max(ms, v.FrameMs[idx]);
                if (v.FrameFlag[idx] == 2) bad = true;
            }
            if (ms < 0) { prev = -1; continue; }
            float yy = MathF.Round(g.Bottom - 1 - (g.Height - 1) * Math.Clamp(ms / top, 0.02f, 1));
            float a = prev < 0 ? yy : prev, lo = MathF.Min(a, yy), hi = MathF.Max(a, yy);
            _p.Fill(new RectangleF(g.X + col, lo, 1, hi - lo + 1), bad ? 0xFFCC0000 : MacBlack);
            prev = yy;
        }
        string label = v.AppFps < 0 ? "waiting for frames" : $"{v.AppFps:0} fps · {v.GpuMs:0.0} ms";
        label = Fit(label, g.Width - 8, Ui.F95, true);
        float lw = _ui.Width95(label, true);
        if (g.Height >= 16)
        {
            _p.Fill(new RectangleF(g.Right - lw - 6, g.Y, lw + 6, 13), MacWhite);
            _ui.Text95(label, new RectangleF(g.Right - lw - 3, g.Y, lw + 2, 13), MacBlack, true);
        }
    }

    // the frame timing as a line of words: fps, reprojection, GPU time, video memory (the Finder's "12 items, 1.2 GB available")
    void StatusMac(PanelData d, RectangleF r)
    {
        var v = d.Vr;
        float budget = 1000f / Math.Max(1, v.DisplayHz);
        float vram = d.VramUsed;
        (string full, string brief, uint c)[] items =
        {
            (v.AppFps < 0 ? "-- fps" : $"{v.AppFps:0}/{v.DisplayHz:0} fps", v.AppFps < 0 ? "--" : $"{v.AppFps:0} fps",
                v.AppFps >= 0 && v.AppFps < v.DisplayHz * 0.85f ? 0xFF806000 : MacBlack),
            (v.ReprojPct < 0 ? "-- reproj." : $"{v.ReprojPct:0}% reproj.", v.ReprojPct < 0 ? "--" : $"{v.ReprojPct:0}%",
                v.ReprojPct > 25 ? 0xFF800000 : v.ReprojPct > 8 ? 0xFF806000 : MacBlack),
            (v.GpuMs < 0 ? "-- ms GPU" : $"{v.GpuMs:0.0} ms GPU", v.GpuMs < 0 ? "--" : $"{v.GpuMs:0.0} ms",
                v.GpuMs > budget ? 0xFF800000 : v.GpuMs > budget * 0.85f ? 0xFF806000 : MacBlack),
            (vram < 0 ? "-- GB" : d.VramTotalGB > 0 ? $"{vram:0.0}/{d.VramTotalGB:0} GB" : $"{vram:0.0} GB", vram < 0 ? "--" : $"{vram:0.0} GB",
                vram > 0 && d.VramTotalGB > 0 && vram > d.VramTotalGB * 0.92f ? 0xFF806000 : MacBlack),
        };
        float cw = r.Width / 4;
        for (int i = 0; i < 4; i++)
        {
            string s = _ui.Width95(items[i].full) <= cw - 4 ? items[i].full : Fit(items[i].brief, cw - 4);
            var al = i == 0 ? TextAlignment.Leading : i == 3 ? TextAlignment.Trailing : TextAlignment.Center;
            _ui.Text95(s, new RectangleF(MathF.Floor(r.X + i * cw), r.Y, MathF.Floor(cw), r.Height), items[i].c, false, al);
        }
    }

    // ================= the custom face: the widgets that were 1995 pictures =================

    partial void WidgetMac(WatchWidget w, WatchWidgets.Kind k, RectangleF r, PanelData d, int id, ref bool done)
    {
        if (k.Button) return;
        var m = Mac;
        var now = DateTime.Now;
        switch (k.Id)
        {
            case "time" when w.W >= 2:
                TimeMac(m.WidgetFrame(_p, r, w.H >= 2 ? k.Title95 : null), now, w.H >= 2);
                break;
            case "date" when w.W >= 2:
                DateMac(m.WidgetFrame(_p, r, null), now, w);
                break;
            case "analog":
            {
                var c = m.WidgetFrame(_p, r, w.H >= 2 ? k.Title95 : null);
                DialMac(now, c.X + c.Width / 2, c.Y + c.Height / 2, MathF.Min(c.Width, c.Height) / 2 - 4);
                break;
            }
            case "media":
            {
                var c = m.WidgetFrame(_p, r, w.H >= 2 ? k.Title95 : null);
                MediaWidgetMac(c, d, w, id);
                break;
            }
            case "graph":
            {
                var c = m.WidgetFrame(_p, r, w.H >= 2 ? k.Title95 : null);
                GraphMac(d.Vr, RectangleF.Inflate(c, -1, -1));
                break;
            }
            case "perf":
            {
                var c = m.WidgetFrame(_p, r, null);
                StatusMac(d, new RectangleF(c.X + 4, MathF.Floor(c.Y + (c.Height - 13) / 2), c.Width - 8, 13));
                break;
            }
            case "fps" or "reproj" or "gpu" or "cputime" or "vram" or "cpu" or "ram":
            {
                var (frac, value, unit, _, tint) = StatInfo(k.Id, d);
                if (w.W >= 2 && (k.Id is "cpu" or "ram" or "vram") && frac >= 0) return;      // the 95 bar is the Mac's too
                var c = m.WidgetFrame(_p, r, w.H >= 2 ? k.Title95 : null);
                uint color = tint == Theme.Warn ? 0xFF806000u : tint == Theme.Bad ? 0xFF800000u : MacBlack;
                if (value == "–") value = "--";
                string cap = k.Id switch { "fps" => "fps", "reproj" => "reproj.", "gpu" => "ms GPU", "cputime" => "ms CPU", "vram" => "GB VRAM", "cpu" => "CPU", _ => "GB RAM" };
                if (k.Id is "vram" or "ram" && (unit != "GB" || _ui.Width95(cap) > c.Width - 2)) cap = k.Id == "vram" ? "VRAM" : "RAM";
                ValueMac(c, value, cap, color);
                break;
            }
            case "minesweeper":
                MinesWidgetMac(m.WidgetFrame(_p, r, w.H >= 2 ? k.Title95 : null), w);
                break;
            case "dock" when !DockShows(d) && d.Dock?.State == HexpadLink.LinkState.Connecting:
                MacDockWait(r, m.WidgetFrame(_p, r, w.H >= 2 ? k.Title95 : null), d, w, id);
                break;
            case "photo": PhotoWidgetMac(r, w, d, id); break;           // a Scrapbook window (WristPhoto.Mac.cs)
            case "memory": PhotoWidgetMac(r, w, d, id); break;
            case "session":
            {
                var c = m.WidgetFrame(_p, r, w.H >= 2 ? k.Title95 : null);
                string sv = SessionText(d);
                ValueMac(c, sv == "–" ? "--" : sv, w.W >= 2 ? "time in VR" : "in VR", MacBlack);
                break;
            }
            default:
                return;
        }
        done = true;
    }

    // a number big (Chicago twice when it fits) and its words under it
    void ValueMac(RectangleF c, string value, string caption, uint color)
    {
        bool two = _ui.Width95(value, false, 26) <= c.Width - 4 && c.Height >= 40;
        float vh = two ? 26 : 15, vy = MathF.Floor(c.Y + (c.Height - vh - 13) / 2) + (two ? -1 : 0);
        if (two) _ui.Text95(value, new RectangleF(c.X, vy, c.Width, 26), color, false, TextAlignment.Center, size: 26);
        else _ui.Text95(Fit(value, c.Width - 2, 13, true), new RectangleF(c.X, vy, c.Width, 15), color, true, TextAlignment.Center, size: 13);
        _ui.Text95(Fit(caption, c.Width - 2), new RectangleF(c.X, vy + vh, c.Width, 13), MacBlack, false, TextAlignment.Center);
    }

    // the time in a white well (a titled window), or right on the white card
    void TimeMac(RectangleF c, DateTime now, bool tall)
    {
        var m = Mac;
        var well = c;
        if (tall)
        {
            well = RectangleF.Inflate(c, -2, -2);
            m.Well(_p, well);
            well = RectangleF.Inflate(well, -2, -2);
        }
        bool h24 = _cfg.Data.Appearance.Clock24h;
        string hm = Clk(now, h24 ? "H:mm" : "h:mm", CultureInfo.CurrentCulture);
        string ampm = h24 ? null : now.Hour < 12 ? "AM" : "PM";
        bool withDate = tall && well.Height >= 50;
        float aw = ampm == null ? 0 : _ui.Width95(ampm, true) + 4;
        // the biggest Chicago that fits: 13 x s
        int s = 6;
        while (s > 1 && (_ui.Width95(hm, false, 13 * s) + aw > well.Width - 8 || 13 * s > well.Height - (withDate ? 15 : 0) + 2 * s)) s--;
        float lh = 13 * s;
        float block = lh - 2 * s + (withDate ? 15 : 0);              // the line's empty top rows do not count
        float ty = MathF.Floor(well.Y + (well.Height - block) / 2) - 2 * s + (s == 1 ? 1 : 0);
        BigTimeMac(hm, ampm, new RectangleF(well.X, ty, well.Width, lh), lh, true);
        if (withDate)
            _ui.Text95(FitDate95(now, well.Width - 6), new RectangleF(well.X, ty + lh - s, well.Width, 13), MacBlack, false, TextAlignment.Center);
    }

    // the date: a desk calendar's leaf (the month on the highlight colour, the day in Chicago) and the weekday beside it
    void DateMac(RectangleF c, DateTime now, WatchWidget w)
    {
        var culture = CultureInfo.CurrentCulture;
        float ph = MathF.Min(34, c.Height - 6), pw = 28;
        var leaf = new RectangleF(c.X + 5, MathF.Floor(c.Y + (c.Height - ph) / 2), pw, ph);
        _p.Fill(leaf, MacBlack);
        var li = RectangleF.Inflate(leaf, -1, -1);
        _p.Fill(li, MacWhite);
        var head = new RectangleF(li.X, li.Y, li.Width, 11);
        _p.Fill(head, Theme.W95Navy);
        string mon = Clk(now, "MMM", culture).TrimEnd('.').ToUpper(culture);
        _ui.Text95(Fit(mon, head.Width), new RectangleF(head.X, head.Y - 1, head.Width, 12), MacWhite, false, TextAlignment.Center);
        _ui.Text95(now.Day.ToString(culture), new RectangleF(li.X, head.Bottom, li.Width, li.Bottom - head.Bottom), MacBlack, true, TextAlignment.Center);
        float tx = leaf.Right + 7, tw = c.Right - 4 - tx;
        string wd = Clk(now, "dddd", culture);
        wd = char.ToUpper(wd[0], culture) + wd[1..];
        string rest = w.W >= 4 ? Clk(now, culture.DateTimeFormat.LongDatePattern, culture) : Clk(now, "yyyy", culture);
        if (w.W >= 4 && _ui.Width95(rest) > tw) rest = Clk(now, culture.DateTimeFormat.MonthDayPattern + ", yyyy", culture);
        float my = MathF.Floor(c.Y + c.Height / 2);
        _ui.Text95(Fit(wd, tw, Ui.F95, true), new RectangleF(tx, my - 14, tw, 14), MacBlack, true);
        _ui.Text95(Fit(rest, tw), new RectangleF(tx, my, tw, 13), Gry95);
    }

    // now playing on the custom face: the cover where there is room, the title and artist, the buttons; tall: the volume too
    void MediaWidgetMac(RectangleF c, PanelData d, WatchWidget w, int id)
    {
        var inner = RectangleF.Inflate(c, -3, -3);
        if (w.H >= 2 && inner.Height >= 60)
        {
            var top = new RectangleF(inner.X, inner.Y, inner.Width, MathF.Min(44, inner.Height - 30));
            MediaMac(d, top, Ui.Id("wcm", id), inner.Width >= 200 ? 3 : 1);
            if (inner.Width >= 150 && inner.Bottom - top.Bottom >= 30)
                Volume95Row(new RectangleF(inner.X, inner.Bottom - 30, inner.Width, 30), Ui.Id("wcv", id));
            return;
        }
        int n = inner.Width >= 230 ? 3 : 1;
        MediaMac(d, inner, Ui.Id("wcm", id), n, inner.Width >= 150);
    }

    // Minesweeper on the face: the mines counter and the Happy Mac; bigger, the board in small too
    readonly Dictionary<int, (MinesGame g, int ver, Vortice.Direct2D1.ID2D1Bitmap bmp)> _mMiniMac = new();

    void MinesWidgetMac(RectangleF c, WatchWidget w)
    {
        var g = _mines[0];
        int secs = (int)Math.Min(999, g.Seconds(Clock.Now));
        var face = MacFace(g, MinesFace95(g));
        const float cw = MacSkin.Mines.SmallW, chh = MacSkin.Mines.SmallH;
        if (w.H < 2)
        {
            float y = MathF.Floor(c.Y + (c.Height - 36) / 2);
            _p.ImageNearest(MacSkin.Mines.CounterBmp(_p, g.MinesLeft), new RectangleF(MathF.Floor(c.X + (c.Width - cw) / 2), y, cw, chh));
            _p.ImageNearest(MacSkin.Mines.FaceIconBmp(_p, face), new RectangleF(MathF.Floor(c.X + (c.Width - 17) / 2), y + chh + 4, 17, 17));
            return;
        }
        if (c.Width >= 124)
        {
            float x0 = MathF.Floor(c.X + (c.Width - 92) / 2), y0 = MathF.Floor(c.Y + (c.Height - 78) / 2);
            _p.ImageNearest(MacSkin.Mines.CounterBmp(_p, g.MinesLeft), new RectangleF(x0, y0 + 5, cw, chh));
            _p.ImageNearest(MacSkin.Mines.FaceBmp(_p, face), new RectangleF(x0 + 34, y0, 24, 24));
            _p.ImageNearest(MacSkin.Mines.CounterBmp(_p, secs), new RectangleF(x0 + 64, y0 + 5, cw, chh));
            MiniMac(g, MathF.Floor(c.X + (c.Width - (g.W * 5 + 4)) / 2), y0 + 29, 5);
            return;
        }
        float bx = MathF.Floor(c.X + (c.Width - 76) / 2), by = MathF.Floor(c.Y + (c.Height - 40) / 2);
        MiniMac(g, bx, by, 4);
        float lx = bx + 46, ly = MathF.Floor(c.Y + (c.Height - 59) / 2);
        _p.ImageNearest(MacSkin.Mines.CounterBmp(_p, g.MinesLeft), new RectangleF(lx, ly, cw, chh));
        _p.ImageNearest(MacSkin.Mines.FaceIconBmp(_p, face), new RectangleF(lx + 5, ly + chh + 6, 17, 17));
        _p.ImageNearest(MacSkin.Mines.CounterBmp(_p, secs), new RectangleF(lx, ly + chh + 6 + 17 + 6, cw, chh));
    }

    void MiniMac(MinesGame g, float x, float y, int cs)
    {
        var r = new RectangleF(x, y, g.W * cs + 4, g.H * cs + 4);
        _p.Fill(r, MacBlack);
        _p.Fill(RectangleF.Inflate(r, -1, -1), 0xFFDDDDDD);
        if (!_mMiniMac.TryGetValue(cs, out var e) || e.g != g || e.ver != g.Version)
        {
            e.bmp?.Dispose();
            e = (g, g.Version, _p.CreateBitmap(MacSkin.Mines.Mini(g, cs), g.W * cs, g.H * cs));
            _mMiniMac[cs] = e;
        }
        _p.ImageNearest(e.bmp, new RectangleF(x + 2, y + 2, g.W * cs, g.H * cs));
    }

    // ================= the opened views: Platinum windows =================

    partial void DetailMac(PanelData d, WatchWidgets.Kind k, string kind, RectangleF win, ref bool done)
    {
        done = true;
        if (kind == "mines") { MinesMacDetail(win); return; }
        if (kind == "room") { RoomMacDetail(win, d); return; }
        if (kind == "dock") { DockMacDetail(win, d); return; }
        if (kind is "photo" or "memory") { PhotoMacDetail(win, d); return; }
        var m = Mac;
        m.MacWindow(_p, win, MacSkin.MacTitle(k.Title95));
        _mCloseBtn = new RectangleF(MacSkin.CloseX(win), MacSkin.BoxY(win), 16, 14);
        if (_ui.CaptionClose95("wd95x", MacSkin.CloseX(win), MacSkin.BoxY(win), true, 6)) { CloseDetail(); return; }
        if (_ui.Button95("wd95ok", new RectangleF(win.Right - 8 - 75, win.Bottom - 8 - 23, 75, 23), "OK", true, true)) { CloseDetail(); return; }
        var c = new RectangleF(win.X + 8, win.Y + 28, win.Width - 16, win.Height - 28 - 36);
        var now = DateTime.Now;
        switch (kind)
        {
            case "media": MediaMacDetail(c, d); break;
            case "battery": Battery95Detail(c, d); break;
            case "clock": ClockMacDetail(c, now); break;
            case "calendar": Calendar95Detail(c, now); break;
            case "timer": Timer95Detail(c); break;
            case "stopwatch": Stopwatch95Detail(c); break;
            case "perf":
                GraphMac(d.Vr, new RectangleF(c.X, c.Y, c.Width, c.Height - 24));
                m.EtchedLine(_p, c.X, c.Right, c.Bottom - 19);
                StatusMac(d, new RectangleF(c.X + 2, c.Bottom - 15, c.Width - 4, 14));
                break;
            case "system": SystemMacDetail(c); break;
            case "volume": Volume95Detail(c); break;
            case "mixer": Mixer95Detail(c); break;
            case "notifications": Notes95Detail(c, d); break;
            case "weather": Weather95Detail(c, d); break;
            case "friends": Friends95Detail(c, d); break;
            case "recap": Recap95Detail(c, d); break;
            case "light": Light95Detail(c, d); break;
            case "rgb": Rgb95Detail(c, d); break;
            case "temps": Temps95Detail(c, d); break;
            case "avatar": Avatar95Detail(c, d); break;
        }
    }

    void MediaMacDetail(RectangleF c, PanelData d)
    {
        Volume.Sample(Clock.Now);
        var top = new RectangleF(c.X, c.Y, c.Width, 60);
        Mac.Inset(_p, top);
        MediaMac(d, RectangleF.Inflate(top, -6, -6), "wd95m", 3);
        Volume95Row(new RectangleF(c.X, top.Bottom + 14, c.Width, 34));
    }

    // Date & Time: the month on the left, the clock face and the time in a field on the right
    void ClockMacDetail(RectangleF c, DateTime now)
    {
        var left = new RectangleF(c.X, c.Y, 160, c.Height);
        _ui.Group95(left, "Date");
        Month95(new RectangleF(left.X + 8, left.Y + 18, left.Width - 16, left.Height - 26), now, now);
        var right = new RectangleF(left.Right + 8, c.Y, c.Right - left.Right - 8, c.Height);
        _ui.Group95(right, "Time");
        float rad = MathF.Floor(MathF.Min(right.Width - 24, right.Height - 60) / 2);
        DialMac(now, MathF.Floor(right.X + right.Width / 2), MathF.Floor(right.Y + 20 + rad), rad);
        string time = Clk(now, _cfg.Data.Appearance.Clock24h ? "H:mm:ss" : "h:mm:ss tt", CultureInfo.CurrentCulture);
        _ui.Edit95(new RectangleF(right.X + 10, right.Bottom - 30, right.Width - 20, 21), time);
    }

    // the CPU over the last minute as black bars on white (a Mac performance window), the memory under it
    void SystemMacDetail(RectangleF c)
    {
        var g = new RectangleF(c.X, c.Y, c.Width, c.Height - 44);
        Mac.Well(_p, g);
        var gi = RectangleF.Inflate(g, -3, -3);
        for (float gy = gi.Y + 16; gy < gi.Bottom; gy += 12) MacSkin.DotsH(_p, gi.X, gi.Right, gy, MacSkin.Grey3, 3);
        float step = gi.Width / SystemStats.History;
        for (int i = 0; i < Stats.CpuCount; i++)
        {
            int idx = (Stats.CpuHead - Stats.CpuCount + i + SystemStats.History) % SystemStats.History;
            float hh = MathF.Max(1, MathF.Round(Stats.CpuHistory[idx] * gi.Height));
            _p.Fill(new RectangleF(MathF.Floor(gi.Right - (Stats.CpuCount - i) * step), gi.Bottom - hh, MathF.Max(1, MathF.Ceiling(step) - 1), hh),
                Stats.CpuHistory[idx] > 0.85f ? 0xFFCC0000 : Theme.W95Navy);
        }
        string cpuLabel = Stats.Cpu >= 0 ? $"CPU: {Stats.Cpu * 100:0}%" : "CPU: --";
        float lw = _ui.Width95(cpuLabel, true);
        _p.Fill(new RectangleF(gi.X, gi.Y, lw + 8, 14), MacWhite);
        _ui.Text95(cpuLabel, new RectangleF(gi.X + 3, gi.Y + 1, lw + 4, 13), MacBlack, true);
        float frac = Stats.RamTotalGB > 0 ? Stats.RamUsedGB / Stats.RamTotalGB : 0;
        _ui.Text95(Stats.RamTotalGB > 0 ? Lang.F("Memory: {0:0.0} of {1:0} GB", Stats.RamUsedGB, Stats.RamTotalGB) : "Memory: --", new RectangleF(c.X, c.Bottom - 38, c.Width, 15));
        _ui.Progress95(new RectangleF(c.X, c.Bottom - 20, c.Width, 14), frac, Theme.W95Navy);
    }
}
