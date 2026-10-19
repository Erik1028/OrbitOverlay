using System;
using System.Drawing;
using System.Globalization;
using Vortice.DirectWrite;

namespace Orbit;

// The watch's opened windows in Windows XP, where XP's own window looked otherwise than 95's: Date and Time Properties (the themed
// month calendar, the round clock), the frame rate and the CPU as Windows Task Manager's Performance page (the usage meter, the
// history graph, the totals), now playing as Windows Media Player (the black info pane, the seek bar, the round transport
// buttons). The rest (the batteries, the timer, the stopwatch, the volume, Minesweeper, the room, Hexpad) are the shared windows
// in the XP skin.
public sealed partial class WristPanel
{
    partial void DetailXp(PanelData d, WatchWidgets.Kind k, string kind, RectangleF win, ref bool done)
    {
        if (kind == "dock") { done = true; DockDetailXp(win, d); return; }
        if (kind is not ("clock" or "calendar" or "perf" or "system" or "media")) return;
        done = true;
        string title = kind switch { "clock" => "Date and Time Properties", "media" => "Windows Media Player", _ => k.Title95 };
        _p.Fill(win, Theme.W95Face);
        _p.Bevel(win, Painter.Edge.Window);
        _ui.Caption95(new RectangleF(win.X + 3, win.Y + 3, win.Width - 6, 18), title, kind == "clock" ? W95Icons.Id.Clock : null);
        if (_ui.CaptionClose95("wd95x", win.Right - 5 - 16, win.Y + 5, true, 6)) { CloseDetail(); return; }
        if (_ui.Button95("wd95ok", new RectangleF(win.Right - 8 - 75, win.Bottom - 8 - 23, 75, 23), "OK", true, true)) { CloseDetail(); return; }
        var c = new RectangleF(win.X + 8, win.Y + 27, win.Width - 16, win.Height - 27 - 36);
        var now = DateTime.Now;
        switch (kind)
        {
            case "clock": ClockDetailXp(c, now); break;
            case "calendar": CalendarDetailXp(c, now); break;
            case "perf": PerfDetailXp(c, d); break;
            case "system": SystemDetailXp(c); break;
            case "media": MediaDetailXp(c, d); break;
        }
    }

    // Hexpad: the shared window; while it connects, XP's marquee under the message (busy, how long unknown)
    void DockDetailXp(RectangleF win, PanelData d)
    {
        Dock95Detail(win, d);
        if (_open == null || _closing || _dockIn == null || DockShown(_dockIn, d) || d.Dock == null) return;
        if (_dockIn.Snap.State != HexpadLink.LinkState.Connecting) return;
        var msg = new RectangleF(win.X + 6, win.Y + 26, win.Width - 12, win.Height - 32);
        ((XpSkin)Skins.Xp).Marquee(_p, new RectangleF(msg.X + 54, msg.Y + 42, MathF.Min(190, msg.Width - 68), 13));
    }

    // ---- Date and Time Properties ----

    void ClockDetailXp(RectangleF c, DateTime now)
    {
        var left = new RectangleF(c.X, c.Y, 160, c.Height);
        _ui.Group95(left, "Date");
        MonthXp(new RectangleF(left.X + 8, left.Y + 18, left.Width - 16, left.Height - 26), now, now);
        var right = new RectangleF(left.Right + 8, c.Y, c.Right - left.Right - 8, c.Height);
        _ui.Group95(right, "Time");
        float rad = MathF.Floor(MathF.Min(right.Width - 20, right.Height - 56) / 2);
        DialXp(now, MathF.Floor(right.X + right.Width / 2) + 0.5f, MathF.Floor(right.Y + 18 + rad) + 0.5f, rad);
        string time = Clk(now, _cfg.Data.Appearance.Clock24h ? "H:mm:ss" : "h:mm:ss tt", CultureInfo.CurrentCulture);
        _ui.Edit95(new RectangleF(right.X + 10, right.Bottom - 30, right.Width - 20, 21), time);
    }

    void CalendarDetailXp(RectangleF c, DateTime now)
    {
        var month = new DateTime(now.Year, now.Month, 1).AddMonths(_calOffset);
        if (_ui.Button95("wd95cp", new RectangleF(c.X, c.Y, 24, 21), "<")) { _calOffset--; _dirty = true; }
        if (_ui.Button95("wd95cn", new RectangleF(c.Right - 24, c.Y, 24, 21), ">")) { _calOffset++; _dirty = true; }
        MonthXp(new RectangleF(c.X + 30, c.Y + 3, c.Width - 60, c.Height - 3), month, now);
    }

    // XP's themed month calendar: the month over a white box with the grey-blue edge, the day initials over a thin rule, today
    // selected in the Luna highlight blue
    void MonthXp(RectangleF box, DateTime month, DateTime now)
    {
        var culture = CultureInfo.CurrentCulture;
        var first1 = new DateTime(month.Year, month.Month, 1);
        _ui.Text95(Capital(first1.ToString("MMMM yyyy", culture)), new RectangleF(box.X, box.Y, box.Width, 15), Blk95, true, TextAlignment.Center);
        var g = new RectangleF(box.X, box.Y + 18, box.Width, box.Height - 18);
        _p.Fill(g, Wht95);
        WellXp(g);
        var gi = RectangleF.Inflate(g, -3, -3);
        int first = (int)culture.DateTimeFormat.FirstDayOfWeek;
        float cw = gi.Width / 7, rh = MathF.Floor(gi.Height / 7);
        for (int col = 0; col < 7; col++)
        {
            string n = culture.DateTimeFormat.GetShortestDayName((DayOfWeek)((first + col) % 7));
            _ui.Text95(n.Substring(0, 1).ToUpper(culture), new RectangleF(gi.X + col * cw, gi.Y, cw, rh), XpBlueText, false, TextAlignment.Center);
        }
        _p.Fill(new RectangleF(gi.X + 2, gi.Y + rh - 1, gi.Width - 4, 1), 0xFFACA899);
        int offset = ((int)first1.DayOfWeek - first + 7) % 7, days = DateTime.DaysInMonth(month.Year, month.Month);
        for (int dd = 1; dd <= days; dd++)
        {
            int cell = offset + dd - 1;
            var cr = new RectangleF(MathF.Floor(gi.X + cell % 7 * cw), gi.Y + (cell / 7 + 1) * rh, MathF.Ceiling(cw), rh);
            bool today = month.Year == now.Year && month.Month == now.Month && dd == now.Day;
            if (today) _p.Fill(RectangleF.Inflate(cr, -1, 0), Theme.W95Navy);
            _ui.Text95(dd.ToString(), cr, today ? Wht95 : Blk95, false, TextAlignment.Center);
        }
    }

    // ---- Windows Task Manager's Performance page ----

    // the frame rate: the usage meter as frames per second of the headset's rate, the frame time history, the totals
    void PerfDetailXp(RectangleF c, PanelData d)
    {
        var v = d.Vr;
        float budget = 1000f / Math.Max(1, v.DisplayHz);
        float topH = c.Height - 50;
        var meter = new RectangleF(c.X, c.Y, 74, topH);
        _ui.Group95(meter, "FPS");
        MeterXp(new RectangleF(meter.X + 8, meter.Y + 16, meter.Width - 16, meter.Height - 23),
            v.AppFps < 0 || v.DisplayHz <= 0 ? -1 : v.AppFps / v.DisplayHz, v.AppFps < 0 ? "--" : $"{v.AppFps:0} fps");
        var hist = new RectangleF(meter.Right + 6, c.Y, c.Right - meter.Right - 6, topH);
        _ui.Group95(hist, "Frame Time History");
        var gr = new RectangleF(hist.X + 8, hist.Y + 16, hist.Width - 16, hist.Height - 23);
        PlotXp(v, RectangleF.Inflate(gr, -1, -1), false);
        WellXp(gr);
        float vram = d.VramUsed;
        uint Lv(bool bad, bool warn) => bad ? XpBad : warn ? XpWarn : Blk95;
        TotalsXp(new RectangleF(c.X, c.Bottom - 46, c.Width, 46), "Totals", new (string, string, uint)[]
        {
            ("Frames/s", v.AppFps < 0 ? "--" : $"{v.AppFps:0} / {v.DisplayHz:0}", Lv(false, v.AppFps >= 0 && v.AppFps < v.DisplayHz * 0.85f)),
            ("GPU time", v.GpuMs < 0 ? "--" : $"{v.GpuMs:0.0} ms", Lv(v.GpuMs > budget, v.GpuMs > budget * 0.85f)),
            ("Reprojected", v.ReprojPct < 0 ? "--" : $"{v.ReprojPct:0}%", Lv(v.ReprojPct > 25, v.ReprojPct > 8)),
            ("Video memory", vram < 0 ? "--" : d.VramTotalGB > 0 ? $"{vram:0.0} / {d.VramTotalGB:0} GB" : $"{vram:0.0} GB",
                Lv(false, vram > 0 && d.VramTotalGB > 0 && vram > d.VramTotalGB * 0.92f)),
        });
    }

    // the CPU: its usage meter and history; the physical memory
    void SystemDetailXp(RectangleF c)
    {
        float topH = c.Height - 50;
        var meter = new RectangleF(c.X, c.Y, 74, topH);
        _ui.Group95(meter, "CPU Usage");
        MeterXp(new RectangleF(meter.X + 8, meter.Y + 16, meter.Width - 16, meter.Height - 23), Stats.Cpu, Stats.Cpu >= 0 ? $"{Stats.Cpu * 100:0} %" : "--");
        var hist = new RectangleF(meter.Right + 6, c.Y, c.Right - meter.Right - 6, topH);
        _ui.Group95(hist, "CPU Usage History");
        var gr = new RectangleF(hist.X + 8, hist.Y + 16, hist.Width - 16, hist.Height - 23);
        var g = RectangleF.Inflate(gr, -1, -1);
        _p.Fill(g, 0xFF000000);
        int n = Stats.CpuCount;
        float step = g.Width / SystemStats.History;
        // the grid moves with the samples (Task Manager's), as long as they come (95's count stopped when the history was full)
        float scroll = XpAeroFx.Moves ? MathF.Floor(CpuSeenXp() * step % 12) : n % 12 * step;
        for (float gx = g.Right - 1 - scroll % 12; gx >= g.X; gx -= 12) _p.Fill(new RectangleF(gx, g.Y, 1, g.Height), TaskGrid);
        for (float gy = g.Bottom - 1; gy >= g.Y; gy -= 10) _p.Fill(new RectangleF(g.X, gy, g.Width, 1), TaskGrid);
        float px = -1, py = 0;
        for (int i = 0; i < n; i++)
        {
            int idx = (Stats.CpuHead - n + i + SystemStats.History) % SystemStats.History;
            float x = MathF.Floor(g.Right - (n - i) * step);
            float y = MathF.Round(g.Bottom - 1 - (g.Height - 2) * Math.Clamp(Stats.CpuHistory[idx], 0, 1));
            if (px >= 0 && x != px) _p.Line(px + 0.5f, py + 0.5f, x + 0.5f, y + 0.5f, TaskLine, 1);
            else if (px < 0) _p.Fill(new RectangleF(x, y, 1, 1), TaskLine);
            px = x; py = y;
        }
        WellXp(gr);
        bool ram = Stats.RamTotalGB > 0;
        float used = Stats.RamUsedGB, total = Stats.RamTotalGB;
        TotalsXp(new RectangleF(c.X, c.Bottom - 46, c.Width, 46), "Physical Memory (GB)", new (string, string, uint)[]
        {
            ("Total", ram ? $"{total:0.0}" : "--", Blk95),
            ("Available", ram ? $"{Math.Max(0, total - used):0.0}" : "--", Blk95),
            ("In use", ram ? $"{used:0.0}" : "--", Blk95),
            ("Usage", ram ? $"{used / total * 100:0}%" : "--", ram && used > total * 0.9f ? XpWarn : Blk95),
        });
    }

    // Task Manager's totals: a group box of name / value pairs in two columns, the values right-aligned
    void TotalsXp(RectangleF g, string title, (string name, string value, uint color)[] items)
    {
        _ui.Group95(g, title);
        float colW = MathF.Floor((g.Width - 16 - 12) / 2);
        for (int i = 0; i < items.Length; i++)
        {
            float x = g.X + 8 + (i / 2) * (colW + 12), y = g.Y + 15 + (i % 2) * 14;
            var (name, value, color) = items[i];
            float vw = _ui.Width95(value) + 2;
            _ui.Text95(Fit(name, colW - vw - 4), new RectangleF(x, y, colW - vw, 13));
            _ui.Text95(value, new RectangleF(x + colW - vw, y, vw, 13), color, false, TextAlignment.Trailing);
        }
    }

    // ---- Windows Media Player ----

    void MediaDetailXp(RectangleF c, PanelData d)
    {
        var (has, title, artist, playing) = MediaNow(d);
        Volume.Sample(Clock.Now);
        var box = new RectangleF(c.X, c.Y, 60, 60);
        var inner = RectangleF.Inflate(box, -1, -1);
        _p.Fill(inner, Wht95);
        if (_art != null) _p.Image(_art, inner);
        else W95Icons.Draw(_p, W95Icons.Id.Note, inner.X + 21, inner.Y + 21);
        WellXp(box);
        // the info pane: Media Player's black, blue-tinted at the top; the title white, the artist pale blue
        var pane = new RectangleF(box.Right + 6, c.Y, c.Right - box.Right - 6, 34);
        RetroSkin.RowsGradient(_p, pane, 0xFF22365E, 0xFF02060E);
        _p.Fill(new RectangleF(pane.X, pane.Y, pane.Width, 1), 0xFF5A6E96);
        _p.Fill(new RectangleF(pane.X, pane.Bottom - 1, pane.Width, 1), 0xFF000000);
        if (!has) _ui.Text95(Fit("Nothing playing", pane.Width - 12), new RectangleF(pane.X + 6, pane.Y + 3, pane.Width - 10, 14), 0xFF8EA4CC);
        else
        {
            _ui.Text95(Fit(title ?? "", pane.Width - 12, Ui.F95, true), new RectangleF(pane.X + 6, pane.Y + 3, pane.Width - 10, 14), 0xFFFFFFFF, true);
            _ui.Text95(Fit(artist ?? "", pane.Width - 12), new RectangleF(pane.X + 6, pane.Y + 17, pane.Width - 10, 14), 0xFF9CC3F5);
        }
        var (tok, pos, dur) = Timeline(d);
        SeekXp(new RectangleF(pane.X, pane.Bottom + 4, pane.Width, 6), has && tok && dur > 0 ? (float)Math.Clamp(pos / dur, 0, 1) : 0);
        // the transport: |<< and >>| round and silver, play / pause bigger and blue
        string[] ids = { "media.prev", "media.play", "media.next" };
        string[][] glyphs = { PrevGlyph, playing ? PauseGlyph : PlayGlyph, NextGlyph };
        float bx = pane.X;
        for (int j = 0; j < 3; j++)
        {
            float s = j == 1 ? 30 : 26;
            var r = new RectangleF(bx, j == 1 ? pane.Bottom + 13 : pane.Bottom + 15, s, s);
            bool click = _ui.Clickable(Ui.Id("wd95m", j), r, out bool hover, out bool held);
            RoundButtonXp(r, glyphs[j], j == 1, has, hover && has, held && has);
            if (click && has) Act(ids[j]);
            bx += s + 3;
        }
        Volume95Row(new RectangleF(c.X, c.Y + 82, c.Width, 34));
    }

    // Media Player's round button: a silver (or, the main one, blue) ball with a darker rim, lighter under the pointer, deeper held
    void RoundButtonXp(RectangleF r, string[] glyph, bool main, bool enabled, bool hover, bool held)
    {
        uint a, b, rim, ink;
        if (main) (a, b, rim, ink) = held ? (0xFF2A62D4, 0xFF5D9BF6, 0xFF0B2F86, 0xFFFFFFFF) : hover ? (0xFF9CC6FF, 0xFF2F6FE8, 0xFF1543B0, 0xFFFFFFFF)
            : (0xFF7DB2FF, 0xFF1F5ED8, 0xFF0E3A9C, 0xFFFFFFFF);
        else (a, b, rim, ink) = held ? (0xFFBFCADC, 0xFFF2F5FA, 0xFF61759A, 0xFF1F2F5A) : hover ? (0xFFFFFFFF, 0xFFD9E4F6, 0xFF6F86B4, 0xFF1F2F5A)
            : (0xFFFDFDFD, 0xFFC9D3E3, 0xFF7A8BAA, 0xFF1F2F5A);
        if (!enabled) (a, b, rim, ink) = (0xFFF4F3EE, 0xFFE2E0D6, 0xFFC9C7BA, 0xFFACA899);
        float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, rad = r.Width / 2;
        _p.Smooth(true);
        _p.Circle(cx, cy, rad, rim);
        _p.GradientRaw(RectangleF.Inflate(r, -1, -1), a, b, rad - 1, 90);
        if (!held) _p.Ring(cx, cy - 0.5f, rad - 2f, Theme.WithAlpha(0xFFFFFFFF, 0.45f), 1);
        _p.Smooth(false);
        float gw = glyph[0].Length, gh = glyph.Length;
        _p.Pixels(glyph, MathF.Floor(cx - gw / 2) + (held ? 1 : 0), MathF.Floor(cy - gh / 2) + (held ? 1 : 0), ink);
    }
}
