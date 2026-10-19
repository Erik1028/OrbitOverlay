using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Numerics;
using Vortice.DirectWrite;
using T = Orbit.TerminalSkin;

namespace Orbit;

// The watch on a green terminal (the 320 x 220 canvas, shown 2x): the fixed faces as a session on a little CRT - the system's
// banner, the command that drew it, the time in big scanlined digits, the batteries as ASCII bars, F-key buttons and a status
// line with its cursor; the custom face's widgets as boxes with their names on the top line; the pages as virtual consoles;
// the opened views where the shared 95 ones would show a black LCD or a bitmap (the CD player, the graphs, the clock, Minesweeper).
public sealed partial class WristPanel
{
    static T Tsk => T.It;

    // text with the top of its cell at y / with its capitals from capTop down (7 px a dot row)
    void TPut(string s, float x, float y, uint c, int n = 1, bool bold = false) => Tsk.Put(_p, s, x, y, c, n, bold);
    void TCaps(string s, float x, float capTop, uint c, int n = 1, bool bold = false) => Tsk.Put(_p, s, x, capTop - 2 * n, c, n, bold);
    void TCapsC(string s, RectangleF r, float capTop, uint c, int n = 1, bool bold = false) =>
        TCaps(s, r.X + MathF.Floor((r.Width - T.W(s, n, bold)) / 2), capTop, c, n, bold);
    void TCapsR(string s, float right, float capTop, uint c, int n = 1, bool bold = false) =>
        TCaps(s, right - T.W(s, n, bold), capTop, c, n, bold);

    // big letters on the tube: a faint halo around them first (the phosphor's glow, it also softens the scanlines), then the ink
    void TBig(string s, float x, float capTop, int n, uint c = 0)
    {
        if (c == 0) c = T.INK;
        TGlowUnder(s, x, capTop, n);            // what it showed before, fading out in the phosphor
        if (n >= 3)
        {
            uint halo = T.Ph(0.17f);
            TCaps(s, x - 1, capTop, halo, n);
            TCaps(s, x + 1, capTop, halo, n);
            TCaps(s, x, capTop - 1, halo, n);
            TCaps(s, x, capTop + 1, halo, n);
        }
        TCaps(s, x, capTop, c, n);
    }

    // s cut to w with an ellipsis (one cell)
    static string TFit(string s, float w, int n = 1, bool bold = false)
    {
        string sh = Lang.Short(s);
        s = Lang.T(s);                  // cut in the language picked (its short form first, where it has one)
        if (string.IsNullOrEmpty(s) || T.W(s, n, bold) <= w) return s;
        if (sh != null && T.W(s = sh, n, bold) <= w) return s;
        int k = s.Length;
        while (k > 1 && T.W(s[..k] + "\u2026", n, bold) > w) k--;
        if (k > 1 && char.IsHighSurrogate(s[k - 1])) k--;
        return s[..k].TrimEnd() + "\u2026";
    }

    // the biggest of the font's sizes (up to max) at which s fits w (and its capitals h)
    static int TScale(string s, float w, float h, int max, bool bold = false)
    {
        for (int n = max; n > 1; n--) if (T.W(s, n, bold) <= w && 7 * n <= h) return n;
        return 1;
    }

    // [#####-----] with 'cells' cells at x; returns its width
    float TBar(float x, float capTop, int cells, float frac, bool warn)
    {
        cells = Math.Max(1, cells);
        int on = (int)MathF.Round(Math.Clamp(frac, 0, 1) * cells);
        if (on == 0 && frac >= 0.01f) on = 1;
        float x0 = x;
        TCaps("[", x, capTop, T.DIM); x += 6;
        if (on > 0) { TCaps(new string('#', on), x, capTop, warn ? T.HOT : T.INK); x += on * 6; }
        if (cells > on) { TCaps(new string('-', cells - on), x, capTop, T.DIM); x += (cells - on) * 6; }
        TCaps("]", x, capTop, T.DIM);
        return x + 5 - x0;
    }

    // [=======>.......]: where in the track
    float TTrack(float x, float capTop, int cells, float frac)
    {
        cells = Math.Max(2, cells);
        int on = Math.Clamp((int)MathF.Round(Math.Clamp(frac, 0, 1) * cells), 1, cells);
        TCaps("[", x, capTop, T.DIM);
        TCaps(new string('=', on - 1) + ">", x + 6, capTop, T.INK);
        if (cells > on) TCaps(new string('.', cells - on), x + 6 + on * 6, capTop, T.DIM);
        TCaps("]", x + 6 + cells * 6, capTop, T.DIM);
        return (cells + 2) * 6 - 1;
    }

    // a button of the terminal: a line around it (lit when on or held), one or two lines of capitals in it
    bool TKey(string id, RectangleF r, string label, bool on = false, bool enabled = true, string line2 = null)
    {
        bool click = _ui.Clickable(id, r, out bool hover, out bool held) && enabled;
        if (click) TFlash(id);
        bool lit = (on || held || TFlashing(id)) && enabled;
        if (lit) T.Lit(_p, r);
        else { _p.Fill(r, T.BG); T.Box(_p, r, hover && enabled ? T.INK : T.FRAME); }
        uint c = lit ? T.BG : !enabled ? T.DIM : hover ? T.HOT : T.INK;
        if (line2 == null) TCapsC(TFit(label, r.Width - 4), r, r.Y + MathF.Floor((r.Height - 7) / 2), c);
        else
        {
            float top = r.Y + MathF.Floor((r.Height - 18) / 2);
            TCapsC(TFit(label, r.Width - 4), r, top, c);
            TCapsC(TFit(line2, r.Width - 4), r, top + 11, c);
        }
        return click;
    }

    bool TGlyphKey(string id, RectangleF r, string[] glyph, bool enabled)
    {
        bool click = _ui.Clickable(id, r, out bool hover, out bool held) && enabled;
        Tsk.GlyphButton(_p, r, glyph, enabled, held && enabled, hover);
        return click;
    }

    // ================= the fixed faces =================

    partial void FaceTerm(PanelData d, ref bool done)
    {
        done = true;
        TBeginFrame();
        string face = C.Face ?? "full";
        var now = DateTime.Now;
        bool rich = face is "full" or "analog";
        bool media = rich && C.ShowMedia, bats = rich && C.ShowBatteries, perf = rich && C.ShowPerformance;
        bool tools = face != "time", big = face is "minimal" or "time";
        float hClock = big ? 64 : 52, hMedia = media ? 38 : 0, hTools = tools ? 32 : 0, hPerf = perf ? (C.PerfGraph ? 34 : 18) : 0;
        float h = 3 + 30 + hClock + hMedia + hTools + hPerf + 6 + 3;
        float wy = MathF.Floor((TH95 - h) / 2);
        var win = new RectangleF(0, wy, TW95, h);
        _p.Fill(win, T.BG);
        T.Crt(_p, win);
        float x0 = 11, x1 = TW95 - 11, y = wy + 7;

        // the banner and the command that drew this
        TCaps($"ORBIT/OS v{Updater.CurrentText}", x0, y, T.DIM);
        float bx = TW95 - 8 - 16;
        if (TKey("w95.x", new RectangleF(bx, y - 3, 16, 13), "X")) { _dismissReq = true; TOff(); }
        if (TKey("w95.min", new RectangleF(bx - 18, y - 3, 16, 13), "_")) { _dismissReq = true; TOff(); }
        TCapsR("TTY0", bx - 24, y, T.DIM);
        TCaps("> watch --face " + face, x0, y + 13, T.MID);
        y += 30;

        bool h24 = _cfg.Data.Appearance.Clock24h;
        var culture = CultureInfo.CurrentCulture;
        string time = Clk(now, h24 ? "H:mm:ss" : "h:mm:ss", culture);
        string ampm = h24 ? null : now.Hour < 12 ? "AM" : "PM";
        string date = T.Up(Clk(now, "ddd", culture)) + " " + now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        float aw = ampm == null ? 0 : 16;
        if (big)
        {
            int n = TScale(time, x1 - x0 - aw, 48, 6);
            float tw = T.W(time, n) + aw, tx = MathF.Floor((TW95 - tw) / 2), top = y + 2;
            TBig(time, tx, top, n);
            if (ampm != null) TCaps(ampm, tx + T.W(time, n) + 4, top + 7 * n - 7, T.MID);
            string longDate = T.Up(Clk(now, culture.DateTimeFormat.LongDatePattern, culture));
            string ds = T.W(longDate) <= x1 - x0 ? longDate : date;
            TCapsC(ds, new RectangleF(0, 0, TW95, 0), top + 7 * n + 8, T.MID);
        }
        else
        {
            float bw = bats ? 124 : 0, left = x0;
            if (face == "analog")
            {
                TDial(now, x0 + 22, y + 22, 21);
                left = x0 + 52;
                int n = TScale(time, x1 - bw - left - aw - 6, 14, 2);
                TCaps(time, left, y + 6, T.INK, n);
                if (ampm != null) TCaps(ampm, left + T.W(time, n) + 4, y + 6 + 7 * n - 7, T.MID);
                TCaps(date, left, y + 6 + 7 * n + 8, T.MID);
            }
            else
            {
                int n = TScale(time, x1 - bw - x0 - aw - 6, 28, 4);
                TBig(time, x0, y + 2, n);
                if (ampm != null) TCaps(ampm, x0 + T.W(time, n) + 4, y + 2 + 7 * n - 7, T.MID);
                TCaps(date, x0, y + 2 + 7 * n + 8, T.MID);
            }
            if (bats) TBatteryRows(d, new RectangleF(x1 - bw, y, bw, 44), 4);
        }
        y += hClock;

        if (media)
        {
            T.Dashes(_p, x0, x1, y - 4, T.FAINT);
            TMediaLines(d, new RectangleF(x0, y, x1 - x0, 32), "w95m");
            y += hMedia;
        }
        if (tools)
        {
            TToolbar(d, new RectangleF(x0 - 3, y, x1 - x0 + 6, 26));
            y += hTools;
        }
        if (perf)
        {
            if (C.PerfGraph) TGraph(d.Vr, new RectangleF(x0, y, x1 - x0, 28), true);
            else TStatus(d, x0, y + 3, x1 - x0, true);
        }
        TScreenFx(RectangleF.Inflate(win, -3, -3), x0);
        T.Scanlines(_p, RectangleF.Inflate(win, -3, -3));
    }

    // the batteries, one a row: HMD [#####---] 71%
    void TBatteryRows(PanelData d, RectangleF r, int max)
    {
        var all = d.Vr.Batteries;
        if (all.Count == 0)
        {
            TCaps(TFit(d.VrConnected ? "NO BATTERY INFO" : "STEAMVR IS OFF", r.Width), r.X, r.Y + 2, T.DIM);
            return;
        }
        var pick = PickBatteries(all, Math.Min(max, (int)((r.Height + 4) / 11)));
        float lw = 0;
        foreach (int i in pick) lw = MathF.Max(lw, T.W(T.Up(Bat(all[i].label))));
        lw = MathF.Min(lw, 30);
        int cells = (int)((r.Width - lw - 6 - 30) / 6) - 2;
        for (int k = 0; k < pick.Count; k++)
        {
            var b = all[pick[k]];
            float top = r.Y + 2 + k * 11;
            bool low = b.pct <= 0.2f && !b.charging;
            TCaps(TFit(T.Up(Bat(b.label)), lw), r.X, top, T.MID);
            float bx = r.X + lw + 6;
            bx += TBar(bx, top, cells, b.pct, low);
            TCapsR($"{b.pct * 100:0}%", r.Right - 6, top, low ? T.HOT : T.INK);
            if (b.charging) TCaps("+", r.Right - 5, top, T.HOT);
        }
    }

    // \u266A TITLE - ARTIST and the buttons; under them where in the track
    void TMediaLines(PanelData d, RectangleF r, string idp)
    {
        var (has, title, artist, playing) = MediaNow(d);
        string[] ids = { "media.prev", "media.play", "media.next" };
        string[][] glyphs = { PrevGlyph, playing ? PauseGlyph : PlayGlyph, NextGlyph };
        float bw = 26, bx = r.Right - 3 * bw - 4;
        for (int i = 0; i < 3; i++)
            if (TGlyphKey(Ui.Id(idp, i), new RectangleF(bx + i * (bw + 2), r.Y, bw, 18), glyphs[i], has)) Act(ids[i]);
        float tw = bx - 8 - r.X;
        if (!has) { TCaps("\u266A NOTHING PLAYING", r.X, r.Y + 6, T.DIM); return; }
        string line = T.Up(string.IsNullOrEmpty(artist) ? title : $"{title} - {artist}");
        TCaps("\u266A", r.X, r.Y + 6, T.INK);
        TCaps(TFit(line, tw - 12), r.X + 12, r.Y + 6, T.INK);
        var (tok, pos, dur) = Timeline(d);
        float ly = r.Y + 25;
        if (tok && dur > 0)
        {
            string tt = $"{TClock(pos)}/{TClock(dur)}";
            float ttw = T.W(tt);
            TTrack(r.X + 12, ly, (int)((r.Width - 12 - ttw - 8) / 6) - 2, (float)(pos / dur));
            TCapsR(tt, r.Right, ly, T.MID);
        }
        else TCaps(playing ? "PLAYING" : "PAUSED", r.X + 12, ly, T.DIM);
    }

    static string TClock(double s)
    {
        int t = Math.Max(0, (int)s);
        return t >= 3600 ? $"{t / 3600}:{t / 60 % 60:00}:{t % 60:00}" : $"{t / 60:00}:{t % 60:00}";
    }

    // keyboard, monitors, recenter, layout, night, settings: F1-F6
    void TToolbar(PanelData d, RectangleF r)
    {
        string[] ids = { "keyboard", "desktops", "recenter", "layout", "night", "settings" };
        string[] words = { "KEYB", "MON", "CNTR", null, "NIGHT", "SETUP" };
        const int n = 6;
        float bw = MathF.Floor((r.Width - (n - 1) * 2) / n);
        float x = r.X + MathF.Floor((r.Width - (n * bw + (n - 1) * 2)) / 2);
        for (int i = 0; i < n; i++)
        {
            bool on = (i == 0 && d.KeyboardVisible) || (i == 1 && d.DesktopsVisible) || (i == 4 && d.NightMode);
            string word = words[i] ?? T.Up(d.LayoutName ?? "LAYOUT");
            if (TKey(Ui.Id("wt", i), new RectangleF(x + i * (bw + 2), r.Y, bw, r.Height), $"[F{i + 1}]", on, true, word)) Act(ids[i]);
        }
    }

    // 86/90 FPS | REPROJ 4% | GPU 9.3MS | VRAM 9.4/16G _
    void TStatus(PanelData d, float x, float capTop, float w, bool cursor)
    {
        var v = d.Vr;
        float budget = 1000f / Math.Max(1, v.DisplayHz), vram = d.VramUsed;
        bool fpsW = v.AppFps >= 0 && v.AppFps < v.DisplayHz * 0.85f, repW = v.ReprojPct > 8, gpuW = v.GpuMs > budget * 0.85f,
            vrW = vram > 0 && d.VramTotalGB > 0 && vram > d.VramTotalGB * 0.92f;
        (string, bool)[][] forms =
        {
            new[]
            {
                (v.AppFps < 0 ? "-- FPS" : $"{v.AppFps:0}/{v.DisplayHz:0} FPS", fpsW), (v.ReprojPct < 0 ? "REPROJ --" : $"REPROJ {v.ReprojPct:0}%", repW),
                (v.GpuMs < 0 ? "GPU --" : $"GPU {v.GpuMs:0.0}MS", gpuW),
                (vram < 0 ? "VRAM --" : d.VramTotalGB > 0 ? $"VRAM {vram:0.0}/{d.VramTotalGB:0}G" : $"VRAM {vram:0.0}G", vrW),
            },
            new[]
            {
                (v.AppFps < 0 ? "--FPS" : $"{v.AppFps:0}FPS", fpsW), (v.ReprojPct < 0 ? "--" : $"{v.ReprojPct:0}%", repW),
                (v.GpuMs < 0 ? "--" : $"{v.GpuMs:0.0}MS", gpuW), (vram < 0 ? "--" : $"{vram:0.0}G", vrW),
            },
        };
        float cw = cursor ? 8 : 0;
        foreach (var f in forms)
        {
            const float pad = 4;            // around the | between two readings
            float total = (f.Length - 1) * (2 * pad + 5);
            foreach (var (s, _) in f) total += T.W(s);
            if (total + cw > w && f != forms[^1]) continue;
            float cx = x;
            for (int i = 0; i < f.Length; i++)
            {
                if (i > 0) { TCaps("|", cx + pad - 1, capTop, T.FAINT); cx += 2 * pad + 5; }
                TCaps(f[i].Item1, cx, capTop, f[i].Item2 ? T.HOT : T.MID);
                cx += T.W(f[i].Item1);
            }
            if (cursor && cx + 9 <= x + w && T.CursorOn()) _p.Fill(new RectangleF(cx + 4, capTop - 1, 5, 9), T.INK);
            return;
        }
    }

    // the frame times as a bar graph: a column per few frames, the budget a dotted line, late frames the brightest
    void TGraph(VrStats v, RectangleF r, bool label)
    {
        _p.Fill(r, T.BG);
        float budget = 1000f / Math.Max(1, v.DisplayHz), top = budget * 2;
        int n = v.FrameCount;
        const int step = 3;
        int cols = (int)(r.Width / step);
        float by = MathF.Round(r.Bottom - r.Height * (budget / top));
        for (float x = r.X; x < r.Right; x += 3) _p.Fill(new RectangleF(x, by, 1, 1), T.DIM);
        if (n > 0)
            for (int c = 0; c < cols; c++)
            {
                int i0 = n * c / cols, i1 = Math.Max(i0 + 1, n * (c + 1) / cols);
                float ms = 0; int flag = 0;
                for (int i = i0; i < i1 && i < n; i++)
                {
                    int idx = (v.FrameHead - n + i + VrStats.FrameHistory) % VrStats.FrameHistory;
                    ms = MathF.Max(ms, v.FrameMs[idx]);
                    flag = Math.Max(flag, v.FrameFlag[idx]);
                }
                float bh = MathF.Max(2, MathF.Round(Math.Clamp(ms / top, 0.02f, 1) * r.Height / 2) * 2);
                uint col = flag == 2 ? T.HOT : flag == 1 || ms > budget ? T.HOT : T.INK;
                var bar = new RectangleF(r.X + c * step, r.Bottom - bh, step - 1, bh);
                _p.Fill(bar, col);
                for (float yy = bar.Y + 1; yy < bar.Bottom; yy += 2) _p.Fill(new RectangleF(bar.X, yy, bar.Width, 1), flag > 0 || ms > budget ? T.INK : T.LIT2);
            }
        if (!label) return;
        string s = v.AppFps < 0 ? "WAITING FOR FRAMES" : $"{v.AppFps:0} FPS \u00B7 {v.GpuMs:0.0} MS \u00B7 {Math.Max(0, v.ReprojPct):0}%";
        s = TFit(s, r.Width - 6);
        _p.Fill(new RectangleF(r.X, r.Y, T.W(s) + 4, 10), T.BG);
        TCaps(s, r.X + 1, r.Y + 1, T.MID);
    }

    // a round dial: the hours ticked, the hands lines
    void TDial(DateTime now, float cx, float cy, float rad)
    {
        _p.Ring(cx + 0.5f, cy + 0.5f, rad, T.FRAME, 1);
        for (int i = 0; i < 12; i++)
        {
            float a = i / 12f * MathF.Tau - MathF.PI / 2, ca = MathF.Cos(a), sa = MathF.Sin(a);
            float r0 = rad - (i % 3 == 0 ? 6 : 3), r1 = rad - 2;
            _p.Line(cx + 0.5f + r0 * ca, cy + 0.5f + r0 * sa, cx + 0.5f + r1 * ca, cy + 0.5f + r1 * sa, i % 3 == 0 ? T.INK : T.MID, 1);
        }
        float s = now.Second, m = now.Minute + s / 60f, hr = now.Hour % 12 + m / 60f;
        void Hand(float frac, float len, float width, uint c)
        {
            float a = frac * MathF.Tau - MathF.PI / 2;
            _p.Line(cx + 0.5f, cy + 0.5f, cx + 0.5f + len * MathF.Cos(a), cy + 0.5f + len * MathF.Sin(a), c, width);
        }
        Hand(hr / 12f, rad * 0.5f, 2, T.INK);
        Hand(m / 60f, rad * 0.78f, 1, T.INK);
        if (rad >= 18) Hand(s / 60f, rad * 0.84f, 1, T.DIM);
        _p.Fill(new RectangleF(cx - 1, cy - 1, 3, 3), T.INK);
    }

    // ================= the custom face =================

    // what each widget's box says on its top line
    static string TTitle(string id) => id switch
    {
        "time" => "TIME", "date" => "DATE", "calendar" => "CAL", "analog" => "CLOCK", "timer" => "TIMER", "stopwatch" => "CHRONO",
        "session" => "IN VR", "battery" => "BATT", "media" => "PLAYBACK", "volume" => "VOL", "perf" => "PERF", "graph" => "FRAME TIME",
        "fps" => "FPS", "reproj" => "REPR", "gpu" => "GPU", "cputime" => "CPU MS", "vram" => "VRAM", "cpu" => "CPU", "ram" => "RAM", "room" => "ROOM",
        "dock" => "HEXPAD", "minesweeper" => "MINES", "mic" => "MIC", "notifications" => "NOTIFY", "buttons" => "BUTTONS",
        "photo" => "PHOTO", "weather" => "WTHR", "friends" => "WHO", "recap" => "LAST",
        "light" => "LIGHT", "rgb" => "RGB", "memory" => "MEMORY", "temps" => "SENSORS", "avatar" => "AVATAR",
        _ => T.Up(id),
    };

    // the F-key of each button widget, and its word
    static (string key, string word, string wide) TButtonWords(string id) => id switch
    {
        "keyboard" => ("F1", "KEYB", "KEYBOARD"),
        "desktops" => ("F2", "MON", "MONITORS"),
        "recenter" => ("F3", "CNTR", "RECENTER"),
        "layout" => ("F4", null, null),
        "night" => ("F5", "NIGHT", "NIGHT"),
        "settings" => ("F6", "SETUP", "SETTINGS"),
        "status" => ("F7", "STATUS", "STATUS"),
        "theater" => ("F8", "CINEMA", "THEATER"),
        _ => ("F9", T.Up(id), T.Up(id)),
    };

    partial void WidgetTerm(WatchWidget w, WatchWidgets.Kind k, RectangleF r, PanelData d, int id, ref bool done)
    {
        done = true;
        _tCards.Add(r);                     // where the tube shows on a see-through face (CustomTerm)
        var now = DateTime.Now;
        if (k.Button)
        {
            var (key, word, wide) = TButtonWords(k.Id);
            (word, wide) = (Lang.T(word), Lang.T(wide));        // in the language picked (TKey measures what it draws)
            if (word == null) word = wide = T.Up(Lang.T(d.LayoutName ?? "LAYOUT"));
            bool click = w.W >= 2
                ? TKey(Ui.Id("wcb", id), r, $"[{key}] {wide}", IsOn(k.Id, d))
                : TKey(Ui.Id("wcb", id), r, $"[{key}]", IsOn(k.Id, d), true, word);
            if (click) Act(k.Id);
            return;
        }
        var c = Tsk.WidgetFrame(_p, r, TTitle(k.Id));
        switch (k.Id)
        {
            case "time": TTime(c, now, w.H >= 2); break;
            case "date": TDate(c, now, w); break;
            case "calendar": TCalendar(c, w, now); break;
            case "analog":
                TDial(now, MathF.Floor(c.X + c.Width / 2), MathF.Floor(c.Y + c.Height / 2), MathF.Floor(MathF.Min(c.Width, c.Height) / 2) - 3);
                break;
            case "timer":
                if (w.H >= 2) TTimer(c, w, id);
                else
                {
                    double left = TimerLeft(Clock.Now);
                    TValue(c, _timer == TimerState.Done ? "DONE" : Clock2(left),
                        _timer switch { TimerState.Running => "RUN", TimerState.Paused => "PAUSE", TimerState.Done => "!", _ => "SET" }, T.INK);
                }
                break;
            case "stopwatch":
                if (w.W >= 2) TStopwatch(c, w, id);
                else TValue(c, StopwatchText(StopwatchTime(Clock.Now), false), _swRunning ? "RUN" : "STOP", T.INK);
                break;
            case "battery": TBatteries(c, d, w); break;
            case "media": TMediaWidget(c, d, w, id); break;
            case "volume":
                if (w.W == 1)
                {
                    float lvl = Volume.Level;
                    TValue(c, lvl < 0 ? "--" : Volume.Muted ? "MUTE" : $"{lvl * 100:0}", lvl < 0 || Volume.Muted ? null : "%", T.INK);
                }
                else Volume95(c, w, id);
                break;
            case "dock" when !DockShows(d) && d.Dock?.State == HexpadLink.LinkState.Connecting:
                // Hexpad being looked for: a spinner turns after the word
                if (_ui.Clickable(Ui.Id("wdk", id), r, out _, out _)) OpenDetail(w);
                string cw = Lang.T(w.W >= 2 ? "CONNECTING " : "CONN ");
                TCapsC(cw + T.Spinner(), c, MathF.Floor(c.Y + (c.Height - 7) / 2), T.INK);
                break;
            case "dock": Dock95Widget(r, c, d, w, id); break;
            case "room": TRoomWidget(r, c, d, w, id); break;
            case "graph": TGraph(d.Vr, RectangleF.Inflate(c, -1, -1), w.H >= 2 || c.Width >= 140); break;
            case "minesweeper": TMinesWidget(c, w); break;
            case "mic": TMic(r, c, w, id); break;
            case "notifications": TNotes(c, d); break;
            case "buttons": TGroup(c, w, d, id); break;
            case "mixer": Mixer95(r, c, w, id); break;
            case "photo": TPhotoWidget(r, c, w, d, id); break;     // the photo in coloured characters (WristPhoto.Term.cs)
            case "memory": TPhotoWidget(r, c, w, d, id); break;
            case "temps": TTemps(c, d, w); break;                  // `sensors` (WristTemps.cs)
            case "weather": TWeather(c, d, w); break;            // wttr.in's (WristWeather.Term.cs)
            case "friends": TFriends(r, c, w, d); break;         // who (WristSocial.Term.cs)
            case "recap": TRecap(c, w, d); break;                // the session's log (WristSocial.Term.cs)
            case "light": TLight(r, c, w, d, id); break;         // (WristLights.Term.cs)
            case "rgb": TRgb(r, c, w, d, id); break;
            case "avatar": TAvatar(r, c, w, d, id); break;     // (WristAvatar.Term.cs)
            case "perf":
                if (c.Width >= 280) TStatus(d, c.X + 3, MathF.Floor(c.Y + (c.Height - 7) / 2), c.Width - 6, false);
                else TPerfTwo(d, c);
                break;
            case "session":
                // the first hour in minutes ("42" MIN), then hours and minutes (whatever language SessionText writes them in)
                bool firstHour = d.VrSince is { } vs0 && (DateTime.Now - vs0).TotalHours < 1;
                string sv = d.VrSince is { } vs1 ? (firstHour ? Math.Max(0, (int)(DateTime.Now - vs1).TotalMinutes).ToString(CultureInfo.InvariantCulture) : SessionText(d)) : "\u2013";
                TValue(c, sv == "\u2013" ? "--" : sv, firstHour ? "MIN" : w.W >= 2 ? "H:MM IN VR" : "H:MM", T.INK);
                break;
            default:
                var (frac, value, unit, _, tint) = StatInfo(k.Id, d);
                uint color = tint == Theme.Warn || tint == Theme.Bad ? T.HOT : T.INK;
                if (value == "\u2013") value = "--";
                if (w.W >= 2 && (k.Id is "cpu" or "ram" or "vram") && frac >= 0)
                {
                    string line = unit == "GB" ? value + "G" : value;
                    if (k.Id == "vram" && d.VramTotalGB > 0) line = $"{value}/{d.VramTotalGB:0}G";
                    else if (k.Id == "ram" && Stats.RamTotalGB > 0) line = $"{value}/{Stats.RamTotalGB:0}G";
                    float top = MathF.Floor(c.Y + (c.Height - 25) / 2);
                    int n = TScale(line, c.Width - 6, 14, 2);
                    TCaps(line, c.X + 3, top + (n == 1 ? 4 : 0), color, n);
                    TBar(c.X + 3, top + 18, (int)((c.Width - 6) / 6) - 2, frac, color == T.HOT);
                }
                else
                {
                    string cap = k.Id switch
                    {
                        "fps" => d.Vr.DisplayHz > 0 ? $"/{d.Vr.DisplayHz:0}" : "FPS",
                        "reproj" => "FRAMES",
                        "gpu" or "cputime" => "MS",
                        "vram" => d.VramTotalGB > 0 ? $"/{d.VramTotalGB:0}G" : "GB",
                        "cpu" => "LOAD",
                        _ => Stats.RamTotalGB > 0 ? $"/{Stats.RamTotalGB:0}G" : "GB",
                    };
                    TValue(c, value + (unit == "%" ? "%" : ""), cap, color);
                }
                break;
        }
    }

    // a value as big as fits (the font at 2x), its unit under it
    void TValue(RectangleF c, string value, string caption, uint color)
    {
        int n = TScale(value, c.Width - 2, caption != null ? c.Height - 13 : c.Height - 2, 3);
        float vh = 7 * n, total = vh + (caption != null ? 5 + 7 : 0);
        float top = MathF.Floor(c.Y + (c.Height - total) / 2);
        string v = TFit(value, c.Width - 2, n);
        if (n >= 3) TBig(v, c.X + MathF.Floor((c.Width - T.W(v, n)) / 2), top, n, color);
        else TCapsC(v, c, top, color, n, n == 1);
        if (caption != null) TCapsC(TFit(caption, c.Width - 2), c, top + vh + 5, T.DIM);
    }

    // the two lines of the performance widget
    void TPerfTwo(PanelData d, RectangleF c)
    {
        var v = d.Vr;
        float vram = d.VramUsed, budget = 1000f / Math.Max(1, v.DisplayHz);
        string a = v.AppFps < 0 ? "-- FPS" : $"{v.AppFps:0}/{v.DisplayHz:0} FPS", b = v.ReprojPct < 0 ? "-- %" : $"{v.ReprojPct:0}% REPR";
        string e = v.GpuMs < 0 ? "-- MS" : $"{v.GpuMs:0.0} MS", f = vram < 0 ? "-- G" : d.VramTotalGB > 0 ? $"{vram:0.0}/{d.VramTotalGB:0}G" : $"{vram:0.0}G";
        float top = MathF.Floor(c.Y + (c.Height - 18) / 2), mid = c.X + MathF.Floor(c.Width / 2);
        TCaps(TFit(a, mid - c.X - 6), c.X + 3, top, v.AppFps >= 0 && v.AppFps < v.DisplayHz * 0.85f ? T.HOT : T.INK);
        TCaps(TFit(b, c.Right - mid - 4), mid + 2, top, v.ReprojPct > 8 ? T.HOT : T.INK);
        TCaps(TFit(e, mid - c.X - 6), c.X + 3, top + 11, v.GpuMs > budget * 0.85f ? T.HOT : T.MID);
        TCaps(TFit(f, c.Right - mid - 4), mid + 2, top + 11, T.MID);
    }

    void TTime(RectangleF c, DateTime now, bool tall)
    {
        bool h24 = _cfg.Data.Appearance.Clock24h;
        var culture = CultureInfo.CurrentCulture;
        string time = Clk(now, h24 ? "H:mm:ss" : "h:mm:ss", culture);
        string ampm = h24 ? null : now.Hour < 12 ? "AM" : "PM";
        bool withDate = tall && c.Height >= 44;
        float aw = ampm != null ? 16 : 0;
        int n = TScale(time, c.Width - 6 - aw, c.Height - (withDate ? 16 : 4), 6);
        if (n == 1 && T.W(time) + aw > c.Width - 4) { time = Clk(now, h24 ? "H:mm" : "h:mm", culture); n = TScale(time, c.Width - 6 - aw, c.Height - 4, 6); }
        float vh = 7 * n, total = vh + (withDate ? 9 + 7 : 0);
        float top = MathF.Floor(c.Y + (c.Height - total) / 2);
        float tw = T.W(time, n) + aw, x = c.X + MathF.Floor((c.Width - tw) / 2);
        TBig(time, x, top, n);
        if (ampm != null) TCaps(ampm, x + T.W(time, n) + 4, top + vh - 7, T.MID);
        if (!withDate) return;
        TCapsC(TFit(TLongDate(now, c.Width - 6), c.Width - 4), c, top + vh + 9, T.MID);
    }

    // THURSDAY, OCTOBER 15, 2026 - or as much of it as fits, down to the ISO date
    static string TLongDate(DateTime now, float maxW)
    {
        var culture = CultureInfo.CurrentCulture;
        var f = culture.DateTimeFormat;
        foreach (string pattern in new[] { f.LongDatePattern, "dddd, " + f.MonthDayPattern, "ddd " + f.MonthDayPattern })
        {
            string s = T.Up(now.ToString(pattern, culture));
            if (T.W(s) <= maxW) return s;
        }
        return T.Up(now.ToString("ddd", culture)) + " " + now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    void TDate(RectangleF c, DateTime now, WatchWidget w)
    {
        var culture = CultureInfo.CurrentCulture;
        if (w.W == 1)
        {
            TValue(c, now.Day.ToString(culture), T.Up(Clk(now, "ddd", culture)), T.INK);
            return;
        }
        string iso = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string sub = Lang.F("{0} \u00B7 WEEK {1}", T.Up(Clk(now, w.W >= 3 ? "dddd" : "ddd", culture)), ISOWeek.GetWeekOfYear(now));
        int n = TScale(iso, c.Width - 6, c.Height - 13, 3);
        float vh = 7 * n, top = MathF.Floor(c.Y + (c.Height - vh - 12) / 2);
        TCapsC(iso, c, top, T.INK, n);
        TCapsC(TFit(sub, c.Width - 4), c, top + vh + 5, T.DIM);
    }

    // the calendar: a desk calendar's page when small, the month like `cal` prints it when there is room
    void TCalendar(RectangleF c, WatchWidget w, DateTime now)
    {
        var culture = CultureInfo.CurrentCulture;
        if (w.H < 2)
        {
            if (w.W >= 2) TValue(c, T.Up(Clk(now, culture.DateTimeFormat.MonthDayPattern, culture)), T.Up(Clk(now, "dddd", culture)), T.INK);
            else TValue(c, now.Day.ToString(culture), T.Up(Clk(now, "MMM", culture)), T.INK);
            return;
        }
        TMonth(c, new DateTime(now.Year, now.Month, 1), now, false);
    }

    // a month as `cal` prints it: the name over it, the days in columns, today lit
    void TMonth(RectangleF box, DateTime month, DateTime now, bool wide)
    {
        var culture = CultureInfo.CurrentCulture;
        int first = (int)culture.DateTimeFormat.FirstDayOfWeek;
        float cw = MathF.Floor(MathF.Min(wide ? 24 : 18, box.Width / 7));
        float gw = cw * 7, gx = box.X + MathF.Floor((box.Width - gw) / 2);
        int weeks = (((int)month.DayOfWeek - first + 7) % 7 + DateTime.DaysInMonth(month.Year, month.Month) + 6) / 7;
        float rh = MathF.Floor(MathF.Min(wide ? 16 : 11, (box.Height - 22) / (weeks + 0f)));
        float top = box.Y + MathF.Floor((box.Height - (22 + weeks * rh)) / 2);
        TCapsC(T.Up(month.ToString("MMMM yyyy", culture)), box, top, T.INK);
        for (int col = 0; col < 7; col++)
        {
            string n = culture.DateTimeFormat.GetShortestDayName((DayOfWeek)((first + col) % 7));
            n = T.Up(cw >= 15 ? n[..Math.Min(2, n.Length)] : n[..1]);
            TCapsC(n, new RectangleF(gx + col * cw, 0, cw, 0), top + 11, T.DIM);
        }
        int offset = ((int)month.DayOfWeek - first + 7) % 7, days = DateTime.DaysInMonth(month.Year, month.Month);
        for (int dd = 1; dd <= days; dd++)
        {
            int cell = offset + dd - 1;
            var cr = new RectangleF(gx + cell % 7 * cw, top + 22 + cell / 7 * rh, cw, rh);
            bool today = month.Year == now.Year && month.Month == now.Month && dd == now.Day;
            float capTop = cr.Y + MathF.Floor((rh - 7) / 2);
            if (today) T.Lit(_p, new RectangleF(cr.X + 1, capTop - 2, cw - 1, 11));
            string ds = dd.ToString(CultureInfo.InvariantCulture);
            if (cw >= 16) TCapsR(ds, cr.Right - MathF.Floor((cw - 11) / 2), capTop, today ? T.BG : T.INK);
            else
            {
                // narrow columns: the condensed digits (a 132-column line's), so the numbers stay apart
                float dw = T.Narrow.Measure(ds, false), rx = cr.Right - MathF.Floor((cw - 7) / 2);
                T.Narrow.Draw(_p, ds, new RectangleF(rx - dw, capTop - 2, dw + 1, T.CellH), today ? T.BG : T.INK, false, TextAlignment.Leading,
                    ParagraphAlignment.Near, false);
            }
        }
    }

    void TBatteries(RectangleF c, PanelData d, WatchWidget w)
    {
        var bats = d.Vr.Batteries;
        if (bats.Count == 0)
        {
            TCapsC(TFit(d.VrConnected ? "NO INFO" : "VR OFF", c.Width - 4), c, MathF.Floor(c.Y + (c.Height - 7) / 2), T.DIM);
            return;
        }
        if (w.W == 1 && w.H == 1)
        {
            var low = bats[0];
            foreach (var b in bats) if (b.pct < low.pct) low = b;
            TValue(c, $"{low.pct * 100:0}%", T.Up(Bat(low.label)), low.pct > 0.2f || low.charging ? T.INK : T.HOT);
            return;
        }
        int two = (int)((c.Height + 3) / 24);
        if (two >= Math.Min(bats.Count, 2) && w.H >= 2)
        {
            // two lines each: HMD 71% / [#######---]
            var pick = PickBatteries(bats, Math.Max(1, two));
            float top = MathF.Floor(c.Y + (c.Height - (pick.Count * 24 - 6)) / 2);
            int cells = (int)((c.Width - 6) / 6) - 2;
            for (int i = 0; i < pick.Count; i++)
            {
                var b = bats[pick[i]];
                bool low = b.pct <= 0.2f && !b.charging;
                float y = top + i * 24;
                string head = $"{TFit(T.Up(Bat(b.label)), c.Width - 50)} {b.pct * 100:0}%";
                TCaps(head, c.X + 3, y, low ? T.HOT : T.INK);
                if (b.charging) TCaps("+", c.X + 3 + T.W(head) + 6, y, T.HOT);
                TBar(c.X + 3, y + 10, cells, b.pct, low);
            }
            return;
        }
        int rows = Math.Clamp((int)((c.Height + 4) / 11), 1, Math.Min(4, bats.Count));
        float rtop = c.Y + MathF.Floor((c.Height - (rows * 11 - 4)) / 2);
        TBatteryRows(d, new RectangleF(c.X + 3, rtop - 2, c.Width - 4, rows * 11), rows);
    }

    // now playing: the title, the artist and where in the track, the buttons
    void TMediaWidget(RectangleF c, PanelData d, WatchWidget w, int id)
    {
        var (has, title, artist, playing) = MediaNow(d);
        string[] ids = { "media.prev", "media.play", "media.next" };
        string[][] glyphs = { PrevGlyph, playing ? PauseGlyph : PlayGlyph, NextGlyph };
        float x = c.X + 3, iw = c.Width - 6;
        var (tok, pos, dur) = Timeline(d);
        if (w.H >= 2)
        {
            float y = c.Y + 4;
            if (!has) TCaps("\u266A NOTHING PLAYING", x, y, T.DIM);
            else
            {
                int n = TScale(T.Up(title), iw - 14, 14, 2);
                TCaps("\u266A", x, y + (n == 2 ? 4 : 0), T.INK);
                TCaps(TFit(T.Up(title), iw - 14, n, n == 1), x + 12, y, T.INK, n, n == 1);
                y += 7 * n + 6;
                string tt = tok && dur > 0 ? $"{TClock(pos)}/{TClock(dur)}" : playing ? "PLAYING" : "PAUSED";
                TCapsR(tt, c.Right - 3, y, T.DIM);
                TCaps(TFit(T.Up(artist ?? ""), iw - T.W(tt) - 10), x, y, T.DIM);
                y += 12;
            }
            float by = y + 2;
            if (has && tok && dur > 0) TTrack(x, by + 6, (int)((iw - 3 * 28 - 6) / 6) - 2, (float)(pos / dur));
            for (int j = 0; j < 3; j++)
                if (TGlyphKey(Ui.Id("wcm", id * 3 + j), new RectangleF(c.Right - 3 - (3 - j) * 28 + 2, by, 26, 18), glyphs[j], has)) Act(ids[j]);
            if (c.Width >= 150 && c.Bottom - (by + 24) >= 30) Volume95Row(new RectangleF(c.X + 4, by + 24, c.Width - 8, 30), Ui.Id("wcv", id));
            return;
        }
        float top = c.Y + MathF.Floor((c.Height - 32) / 2);
        string line = has ? T.Up(string.IsNullOrEmpty(artist) ? title : $"{title} - {artist}") : "NOTHING PLAYING";
        TCaps("\u266A", x, top + 2, has ? T.INK : T.DIM);
        TCaps(TFit(line, iw - 12), x + 12, top + 2, has ? T.INK : T.DIM);
        int nb = iw >= 86 ? 3 : 1;
        float bx = x;
        for (int j = 0; j < nb; j++)
        {
            int a = nb == 1 ? 1 : j;
            if (TGlyphKey(Ui.Id("wcm", id * 3 + a), new RectangleF(bx, top + 14, 26, 18), glyphs[a], has)) Act(ids[a]);
            bx += 28;
        }
        float rest = c.Right - 3 - bx - 4;
        if (has && tok && dur > 0 && rest >= 48)
        {
            string tt = $"{TClock(pos)}/{TClock(dur)}";
            bool time = rest - T.W(tt) - 6 >= 48;
            int cells = (int)(((time ? rest - T.W(tt) - 6 : rest) + 1) / 6) - 2;
            TTrack(bx + 4, top + 20, cells, (float)(pos / dur));
            if (time) TCapsR(tt, c.Right - 3, top + 20, T.DIM);
        }
    }

    // the timer, two cells high: the time as big as fits, the play key beside it, the time left as a bar, -1 / RESET / +1
    void TTimer(RectangleF c, WatchWidget w, int id)
    {
        double left = TimerLeft(Clock.Now);
        bool idle = _timer == TimerState.Idle, over = _timer == TimerState.Done;
        string txt = over ? "DONE" : Clock2(left);
        if (TGlyphKey(Ui.Id("wtp", id), new RectangleF(c.Right - 30, c.Y + 3, 26, 18), _timer == TimerState.Running ? PauseGlyph : PlayGlyph, true))
            TimerTap();
        float by = c.Bottom - 23;
        int n = TScale(txt, c.Width - 40, MathF.Min(28, by - c.Y - 24), 4);
        TBig(txt, c.X + 4, c.Y + 4 + MathF.Max(0, MathF.Floor((18 - 7 * n) / 2)), n, over ? T.HOT : T.INK);
        float top = c.Y + 4 + MathF.Max(18, 7 * n);
        float frac = idle ? 1 : (float)(left / Math.Max(1, Math.Max(_timerTotal, left)));
        TBar(c.X + 3, MathF.Floor(top + (by - top - 7) / 2), (int)((c.Width - 6) / 6) - 2, frac, over);
        float rw = T.W("RESET") + 10, sw = MathF.Floor((c.Width - 8 - rw - 6) / 2);
        if (TKey(Ui.Id("wtm", id), new RectangleF(c.X + 4, by, sw, 20), "-1")) TimerAdjust(-1);
        if (TKey(Ui.Id("wtr", id), new RectangleF(c.X + 7 + sw, by, c.Width - 14 - 2 * sw, 20), "RESET", false, !idle)) TimerReset();
        if (TKey(Ui.Id("wtq", id), new RectangleF(c.Right - 4 - sw, by, sw, 20), "+1")) TimerAdjust(1);
    }

    // the stopwatch, two cells wide: the time, START / STOP and RESET under it
    void TStopwatch(RectangleF c, WatchWidget w, int id)
    {
        double t = StopwatchTime(Clock.Now);
        string txt = StopwatchText(t, true);
        float by = c.Bottom - 21;
        int n = TScale(txt, c.Width - 8, by - c.Y - 6, 3);
        TBig(txt, c.X + MathF.Floor((c.Width - T.W(txt, n)) / 2), c.Y + MathF.Floor((by - c.Y - 7 * n) / 2), n);
        float bw = MathF.Floor((c.Width - 10) / 2);
        if (TKey(Ui.Id("wsp", id), new RectangleF(c.X + 2, by, bw, 19), _swRunning ? "STOP" : "START", _swRunning)) StopwatchTap();
        if (TKey(Ui.Id("wsr", id), new RectangleF(c.X + 6 + bw, by, bw, 19), "RESET", false, t > 0 && !_swRunning)) StopwatchReset();
    }

    // who is in the room: how many, the friends among them (a tap opens the list)
    void TRoomWidget(RectangleF r, RectangleF c, PanelData d, WatchWidget w, int id)
    {
        if (_ui.Clickable(Ui.Id("wrm", id), r, out _, out _)) OpenDetail(w);
        var (n, f, names) = RoomCount(d);
        if (w.W >= 2)
        {
            float top = MathF.Floor(c.Y + (c.Height - 18) / 2);
            TCaps(TFit(n < 0 ? "NOT IN A WORLD" : Lang.Upper(RoomPanel.PeopleText(n)), c.Width - 6), c.X + 3, top, n < 0 ? T.DIM : T.INK);
            string sub = f > 0 ? Lang.Upper(RoomPanel.FriendsText(f)) + ": " + names : n < 0 ? "" : T.Up(d.Room.World);
            if (!string.IsNullOrEmpty(sub)) TCaps(TFit(sub, c.Width - 6), c.X + 3, top + 11, f > 0 ? T.HOT : T.MID);
            return;
        }
        TValue(c, n < 0 ? "--" : n.ToString(CultureInfo.InvariantCulture), f > 0 ? Lang.Upper(RoomPanel.FriendsText(f)) : "IN ROOM", T.INK);
    }

    // the microphone: a tap mutes it; wide, [X] MUTE and the level as a bar
    void TMic(RectangleF r, RectangleF c, WatchWidget w, int id)
    {
        if (_ui.Clickable(Ui.Id("wmic", id), r, out _, out _)) { Mic.ToggleMute(); _dirty = true; }
        bool has = Mic.Present, muted = has && Mic.Muted;
        if (w.W >= 2 && c.Height >= 34)
        {
            bool m = muted;
            if (_ui.Check95(Ui.Id("wmk", id), c.X + 6, c.Y + 4, "Mute", ref m, has)) { Mic.ToggleMute(); _dirty = true; }
            TBar(c.X + 3, c.Bottom - 11, (int)((c.Width - 6) / 6) - 2, has && !muted ? MathF.Sqrt(MathF.Max(0, Mic.Peak)) : 0, false);
            return;
        }
        TValue(c, !has ? "--" : muted ? "MUTED" : "LIVE", "MIC", muted ? T.HOT : T.INK);
    }

    // the last notifications, newest first: what, and how long ago
    void TNotes(RectangleF c, PanelData d)
    {
        NotesNow(d);
        if (_noteRows.Count == 0)
        {
            TCapsC(TFit("NO MESSAGES", c.Width - 4), c, MathF.Floor(c.Y + (c.Height - 7) / 2), T.DIM);
            return;
        }
        int rows = Math.Max(1, (int)((c.Height - 2) / 11));
        float top = c.Y + MathF.Floor((c.Height - (Math.Min(rows, _noteRows.Count) * 11 - 4)) / 2);
        var now = DateTime.Now;
        for (int i = 0; i < Math.Min(rows, _noteRows.Count); i++)
        {
            var (text, _, at) = _noteRows[i];
            string age = T.Up(Age(at, now));
            float y = top + i * 11, aw = T.W(age);
            TCaps(TFit(text, c.Width - aw - 12), c.X + 3, y, i == 0 ? T.INK : T.MID);
            TCapsR(age, c.Right - 3, y, T.DIM);
        }
    }

    // a group of buttons: F-key boxes in a row (two columns when tall), the ones that are on lit
    void TGroup(RectangleF c, WatchWidget w, PanelData d, int id)
    {
        GroupIdsOf(w);
        int n = _groupIds.Count;
        int cols = w.H >= 2 ? Math.Min(2, n) : n, rows = (n + cols - 1) / cols;
        var a = RectangleF.Inflate(c, -2, -2);
        float bw = MathF.Floor((a.Width - (cols - 1) * 2) / cols), bh = MathF.Floor(MathF.Min(26, (a.Height - (rows - 1) * 2) / rows));
        float y0 = MathF.Floor(a.Y + (a.Height - rows * bh - (rows - 1) * 2) / 2);
        for (int j = 0; j < n; j++)
        {
            string kid = _groupIds[j];
            var (key, word, wide) = TButtonWords(kid);
            (word, wide) = (Lang.T(word), Lang.T(wide));
            if (word == null) word = wide = T.Up(Lang.T(d.LayoutName ?? "LAYOUT"));
            var b = new RectangleF(a.X + j % cols * (bw + 2), y0 + j / cols * (bh + 2), bw, bh);
            float fit = bw - 4;
            string shrt = kid switch
            {
                "keyboard" => "KBD", "desktops" => "MON", "recenter" => "CTR", "night" => "NGT", "settings" => "SET", "status" => "STS",
                "theater" => "CIN", _ => word.Length > 3 ? word[..3] : word,
            };
            shrt = Lang.T(shrt);
            string label = T.W($"[{key}] {wide}") <= fit ? $"[{key}] {wide}" : T.W(wide) <= fit ? wide : T.W(word) <= fit ? word
                : T.W(shrt) <= fit ? shrt : key;
            if (TKey(Ui.Id("wgb", id * 8 + j), b, label, IsOn(kid, d))) Act(kid);
        }
    }

    // ================= the pages: virtual consoles =================

    partial void TaskbarTerm(int count, int cur, ref bool done)
    {
        done = true;
        float y = TH95 - WatchWidgets.Taskbar95 - 2;
        _tCards.Add(new RectangleF(0, y, TW95, TH95 - y));
        _p.Fill(new RectangleF(0, y, TW95, TH95 - y), T.BG);
        T.Dashes(_p, 3, TW95 - 3, y + 1, T.FAINT);
        float bw = MathF.Floor(MathF.Min(104, (TW95 - 6 - (count - 1) * 3) / (float)count));
        for (int i = 0; i < count; i++)
        {
            var b = new RectangleF(3 + i * (bw + 3), y + 4, bw, TH95 - y - 6);
            string name = T.Up(WatchPages.Label(C, i));          // (already in the language picked)
            string label = T.W($"F{i + 1} {name}") <= bw - 6 ? $"F{i + 1} {name}" : TFit($"{i + 1}:{name}", bw - 6);
            if (TKey(Ui.Id("wtb", i), b, label, i == cur)) GoToPage(i);
        }
    }

    // ================= the opened views =================

    partial void DetailTerm(PanelData d, WatchWidgets.Kind k, string kind, RectangleF win, ref bool done)
    {
        if (kind == "mines") { done = true; TMinesDetail(win); return; }
        if (kind == "weather") { done = true; TWeatherDetail(win, d); return; }
        if (kind is "friends" or "recap") { done = true; TSocialDetail(win, d, kind); return; }      // who / the session's log (WristSocial.Term.cs)
        if (kind is not ("media" or "perf" or "system" or "clock" or "notifications")) return;      // the rest: the shared windows, in this skin
        done = true;
        _p.Fill(win, T.BG);
        _p.Bevel(win, Painter.Edge.Window);
        _ui.Caption95(new RectangleF(win.X + 3, win.Y + 3, win.Width - 6, 18), k.Title95);
        if (_ui.CaptionClose95("wd95x", win.Right - 5 - 16, win.Y + 5, true, 6)) { CloseDetail(); return; }
        if (_ui.Button95("wd95ok", new RectangleF(win.Right - 8 - 75, win.Bottom - 8 - 23, 75, 23), "OK", true, true)) { CloseDetail(); return; }
        var c = new RectangleF(win.X + 8, win.Y + 27, win.Width - 16, win.Height - 27 - 36);
        var now = DateTime.Now;
        switch (kind)
        {
            case "media": TMediaDetail(c, d); break;
            case "perf":
                TGraph(d.Vr, new RectangleF(c.X, c.Y, c.Width, c.Height - 20), true);
                TStatus(d, c.X, c.Bottom - 9, c.Width, true);
                break;
            case "system": TSystemDetail(c); break;
            case "clock":
                var left = new RectangleF(c.X, c.Y, 160, c.Height);
                _ui.Group95(left, "Date");
                TMonth(new RectangleF(left.X + 6, left.Y + 14, left.Width - 12, left.Height - 18), new DateTime(now.Year, now.Month, 1), now, false);
                var right = new RectangleF(left.Right + 8, c.Y, c.Right - left.Right - 8, c.Height);
                _ui.Group95(right, "Time");
                float rad = MathF.Floor(MathF.Min(right.Width - 20, right.Height - 50) / 2);
                TDial(now, MathF.Floor(right.X + right.Width / 2), MathF.Floor(right.Y + 16 + rad), rad);
                string time = Clk(now, _cfg.Data.Appearance.Clock24h ? "H:mm:ss" : "h:mm:ss tt", CultureInfo.CurrentCulture);
                TCapsC(time, right, right.Bottom - 16, T.INK);
                break;
            case "notifications": TNotesDetail(c, d); break;
        }
    }

    // The notifications as a log, newest first - "[12:04] VRCX     Friend online - Sam is in ...", what does not fit going on on the
    // line under it (two lines at most, then cut), the newest bright; the stick scrolls it an entry a notch, a bar at the right says
    // where; how many and Clear beside OK
    readonly List<(string a, string b)> _tNoteLines = new();
    NotificationHistory _tNoteLinesOf;
    int _tNoteLinesVer = -1;
    float _tNoteLinesW = -1;

    void TNotesDetail(RectangleF c, PanelData d)
    {
        var rows = NotesOpen(d);
        int n = rows.Count;
        _notesEmptyDrawn = n == 0;
        _notesFirst = n > 0 ? rows[0].Title : null;
        TCaps("> tail notify.log", c.X, c.Y + 2, T.MID);
        const float Line = 11, Gap = 3;
        float y0 = c.Y + 17, avail = c.Bottom - y0;
        if (n == 0)
        {
            TCaps("NO MESSAGES", c.X, y0, T.DIM);
            TCaps("> _", c.X, y0 + Line + Gap, T.INK);
        }
        // the app's column as wide as the widest one (eight letters at most); the text after it, the bar's room kept at the right
        float sw = 6;
        foreach (var e in rows) sw = MathF.Max(sw, T.W(TFit(e.Chip ?? "-", 48)));
        float sx = c.X + T.W("[00:00] "), tx = sx + sw + 12, tw = c.Right - 8 - tx;
        if (!ReferenceEquals(_tNoteLinesOf, d.Notes) || _tNoteLinesVer != (d.Notes?.Version ?? -1) || _tNoteLinesW != tw)
        {
            // each entry's lines, made again when the list changed
            (_tNoteLinesOf, _tNoteLinesVer, _tNoteLinesW) = (d.Notes, d.Notes?.Version ?? -1, tw);
            _tNoteLines.Clear();
            foreach (var e in rows) _tNoteLines.Add(TWrap(e.Title.Length > 0 && e.Text.Length > 0 ? e.Title + " - " + e.Text : e.Title + e.Text, tw));
        }
        float Adv(int i) => (_tNoteLines[i].b.Length > 0 ? 2 : 1) * Line + Gap;
        // how far the stick goes: the last entries filling the log
        int maxTop = 0;
        float used = 0;
        for (int i = n - 1; i >= 0; i--)
        {
            used += Adv(i);
            if (used - Gap - (Line - 7) > avail) { maxTop = i + 1; break; }
        }
        if (_ui.Wheel != 0 && maxTop > 0 && _ui.OverNow(c)) { _notesTop -= Math.Sign(_ui.Wheel); _dirty = true; }
        _notesTop = Math.Clamp(_notesTop, 0, maxTop);
        float y = y0;
        int shown = 0;
        for (int i = _notesTop; i < n; i++)
        {
            var e = rows[i];
            var (a, b2) = _tNoteLines[i];
            if (y + (b2.Length > 0 ? Line : 0) + 7 > c.Bottom) break;
            bool newest = i == 0;
            TCaps("[" + e.At.ToString("HH:mm", CultureInfo.InvariantCulture) + "]", c.X, y, T.DIM);
            TCaps(TFit(e.Chip ?? "-", sw), sx, y, newest ? T.HOT : T.INK);
            TCaps(a, tx, y, newest ? T.HOT : T.INK);
            if (b2.Length > 0) TCaps(b2, tx, y + Line, T.MID);
            y += Adv(i);
            shown++;
        }
        if (maxTop > 0)
        {
            float h = avail + 2, th = MathF.Max(8, MathF.Round(h * shown / n)), ty = MathF.Round(y0 - 2 + (h - th) * _notesTop / maxTop);
            _p.Fill(new RectangleF(c.Right - 3, y0 - 2, 2, h), T.FAINT);
            _p.Fill(new RectangleF(c.Right - 4, ty, 4, th), T.INK);
        }
        var clr = new RectangleF(c.Right - 75 - 6 - 75, c.Bottom + 5, 75, 23);
        if (n > 0) TCaps(TFit(T.Up(NotesCount(n)), clr.X - c.X - 6), c.X, c.Bottom + 13, T.DIM);
        if (_ui.Button95("wn95clr", clr, "&Clear", n > 0)) NotesClear(d);
    }

    // s over two lines of w: as many words as fit on the first, the rest cut on the second
    static (string, string) TWrap(string s, float w)
    {
        s = Lang.T(s);
        if (T.W(s) <= w) return (s, "");
        int cut = -1;
        for (int i = s.IndexOf(' '); i > 0; i = s.IndexOf(' ', i + 1))
        {
            if (T.W(s[..i]) > w) break;
            cut = i;
        }
        if (cut < 0) return (TFit(s, w), "");
        return (s[..cut], TFit(s[(cut + 1)..].TrimStart(), w));
    }

    void TMediaDetail(RectangleF c, PanelData d)
    {
        var (has, title, artist, playing) = MediaNow(d);
        Volume.Sample(Clock.Now);
        var (tok, pos, dur) = Timeline(d);
        float y = c.Y + 4;
        if (!has) TCaps("\u266A NOTHING PLAYING", c.X, y, T.DIM);
        else
        {
            int n = TScale(T.Up(title), c.Width - 16, 14, 2);
            TCaps("\u266A", c.X, y + (n == 2 ? 4 : 0), T.INK);
            TCaps(TFit(T.Up(title), c.Width - 16, n, n == 1), c.X + 14, y, T.INK, n, n == 1);
            y += 7 * n + 7;
            TCaps(TFit(T.Up(artist ?? ""), c.Width - 14), c.X + 14, y, T.MID);
        }
        y = c.Y + 40;
        if (has && tok && dur > 0)
        {
            string tt = $"{TClock(pos)} / {TClock(dur)}";
            TTrack(c.X, y, (int)((c.Width - T.W(tt) - 8) / 6) - 2, (float)(pos / dur));
            TCapsR(tt, c.Right, y, T.MID);
        }
        else TCaps(has ? (playing ? "PLAYING" : "PAUSED") : "", c.X, y, T.DIM);
        string[] ids = { "media.prev", "media.play", "media.next" };
        string[][] glyphs = { PrevGlyph, playing ? PauseGlyph : PlayGlyph, NextGlyph };
        for (int j = 0; j < 3; j++)
            if (TGlyphKey(Ui.Id("wd95m", j), new RectangleF(c.X + j * 34, c.Y + 54, 32, 22), glyphs[j], has)) Act(ids[j]);
        Volume95Row(new RectangleF(c.X, c.Y + 84, c.Width, 34));
    }

    // top: the CPU over the last minute as columns; the memory as a bar
    void TSystemDetail(RectangleF c)
    {
        var g = new RectangleF(c.X, c.Y + 12, c.Width, c.Height - 44);
        TCaps(Stats.Cpu >= 0 ? $"CPU {Stats.Cpu * 100:0}%" : "CPU --", c.X, c.Y + 1, T.INK);
        TCapsR("LAST 60 S", c.Right, c.Y + 1, T.DIM);
        T.Box(_p, g, T.FRAME);
        var gi = RectangleF.Inflate(g, -2, -2);
        for (float gy = gi.Y + 8; gy < gi.Bottom; gy += 10) T.Dashes(_p, gi.X, gi.Right, gy, T.FAINT);
        float step = gi.Width / SystemStats.History;
        for (int i = 0; i < Stats.CpuCount; i++)
        {
            int idx = (Stats.CpuHead - Stats.CpuCount + i + SystemStats.History) % SystemStats.History;
            float h = MathF.Max(1, MathF.Round(Stats.CpuHistory[idx] * gi.Height));
            _p.Fill(new RectangleF(MathF.Floor(gi.Right - (Stats.CpuCount - i) * step), gi.Bottom - h, MathF.Max(1, MathF.Ceiling(step) - 1), h), T.INK);
        }
        float frac = Stats.RamTotalGB > 0 ? Stats.RamUsedGB / Stats.RamTotalGB : 0;
        string mem = Stats.RamTotalGB > 0 ? $"MEM {Stats.RamUsedGB:0.0}/{Stats.RamTotalGB:0}G" : "MEM --";
        TCaps(mem, c.X, c.Bottom - 22, T.INK);
        TBar(c.X, c.Bottom - 10, (int)((c.Width + 1) / 6) - 2, frac, frac > 0.9f);
    }

    // ---- Minesweeper as a text-mode game ----

    // what a square shows: (character, its colour, lit)
    (string ch, uint c, bool lit) TSquare(MinesGame g, int i, int press, int chord)
    {
        bool lost = g.State == MinesGame.Phase.Lost, open = g.IsOpen(i), flag = g.IsFlag(i);
        if (lost)
        {
            if (i == g.Hit) return ("*", T.BG, true);
            if (g.IsMine(i)) return flag ? ("F", T.BG, true) : ("*", T.HOT, false);
            if (flag) return ("X", T.HOT, false);
        }
        if (open) return g.Adj(i) == 0 ? (" ", T.INK, false) : (g.Adj(i).ToString(CultureInfo.InvariantCulture), T.INK, false);
        if (flag) return ("F", T.BG, true);
        return MinesDown(g, i, press, chord) ? (" ", T.INK, false) : ("\u00B7", T.MID, false);
    }

    static string TFace(MinesGame g, bool held) => held ? ":)" : g.State switch
    {
        MinesGame.Phase.Won => "B)",
        MinesGame.Phase.Lost => "X(",
        _ => ":)",
    };

    // the widget: the counters and the board, small (a tap opens it)
    void TMinesWidget(RectangleF c, WatchWidget w)
    {
        var g = _mines[0];
        int secs = (int)Math.Min(999, g.Seconds(Clock.Now));
        string face = TFace(g, false);
        if (g.State is not (MinesGame.Phase.Won or MinesGame.Phase.Lost) && MinesOh(g)) face = ":O";
        if (w.H < 2)
        {
            TValue(c, $"{Math.Max(0, g.MinesLeft):000}", face, T.HOT);
            return;
        }
        const float px = 6, py = 9;
        float bwid = g.W * px, bh = g.H * py;
        bool side = c.Width >= bwid + 60;
        float bx = side ? c.X + 6 : MathF.Floor(c.X + (c.Width - bwid) / 2);
        float by = side ? MathF.Floor(c.Y + (c.Height - bh) / 2) : c.Bottom - bh - 2;
        for (int i = 0; i < g.Cells; i++)
        {
            var (ch, col, lit) = TSquare(g, i, -1, -1);
            float x = bx + i % g.W * px, y = by + i / g.W * py;
            if (lit) _p.Fill(new RectangleF(x, y, 5, 8), T.INK);
            if (ch != " ") TCaps(ch, x, y + 1, col);
        }
        string left = $"*{Math.Max(0, g.MinesLeft):000}", time = $"{secs:000}";
        if (side)
        {
            float sx = bx + bwid + 8, sw = c.Right - 4 - sx;
            var col = new RectangleF(sx, 0, sw, 0);
            float top = MathF.Floor(c.Y + (c.Height - 50) / 2);
            TCapsC(left, col, top, T.HOT);
            TCapsC(face, col, top + 21, T.INK);
            TCapsC(time, col, top + 42, T.HOT);
        }
        else
        {
            TCaps(left, c.X + 3, c.Y + 2, T.HOT);
            TCapsC(face, c, c.Y + 2, T.INK);
            TCapsR(time, c.Right - 3, c.Y + 2, T.HOT);
        }
    }

    // the opened game: the board's squares as big as the 95 one's (16 px), a character each at 2x; beside it the window
    void TMinesDetail(RectangleF win)
    {
        var g = _mines[0];
        double now = Clock.Now;
        const float pitch = 16;
        float bwid = g.W * pitch, bh = g.H * pitch;
        var left = new RectangleF(win.X, win.Y, bwid + 16, win.Height);
        _p.Fill(left, T.BG);
        T.Box(_p, left, T.FRAME);
        var board = new RectangleF(left.X + 8, left.Bottom - 8 - bh, bwid, bh);
        int hover = MinesInput(g, board, pitch, now);
        var (press, prog, chord) = MinesPressInfo(g, now);
        // the counters and the face over the board
        float hy = win.Y + 6;
        var fb = new RectangleF(MathF.Floor(left.X + (left.Width - 30) / 2), hy, 30, 24);
        _mFaceBtn = fb;
        bool newGame = _ui.Clickable("mn95face", fb, out bool faceHover, out bool faceHeld);
        int secs = (int)Math.Min(999, g.Seconds(now));
        TCaps($"{Math.Max(-99, g.MinesLeft):000}", left.X + 8, hy + 5, T.HOT, 2);
        TCapsR($"{secs:000}", left.Right - 8, hy + 5, T.HOT, 2);
        string face = TFace(g, faceHeld);
        if (!faceHeld && !g.Over && MinesOh(g)) face = ":O";
        if (faceHeld) T.Lit(_p, fb);
        else { _p.Fill(fb, T.BG); T.Box(_p, fb, faceHover ? T.INK : T.FRAME); }
        TCapsC(face, fb, fb.Y + 5, faceHeld ? T.BG : T.INK, 2);
        T.Dashes(_p, left.X + 4, left.Right - 4, board.Y - 5, T.FAINT);
        // the squares
        for (int i = 0; i < g.Cells; i++)
        {
            var sq = new RectangleF(board.X + i % g.W * pitch, board.Y + i / g.W * pitch, pitch, pitch);
            var (ch, col, lit) = TSquare(g, i, press, chord);
            bool hidden = ch == "\u00B7";
            if (lit) T.Lit(_p, RectangleF.Inflate(sq, -1, -1));
            else if (hidden) T.Box(_p, RectangleF.Inflate(sq, -1, -1), i == hover && !g.Over ? T.INK : T.FAINT);
            if (i == press && prog > 0)
            {
                // the long press: a bar fills the square from the bottom, the flag comes (or goes)
                float fh = MathF.Round((sq.Height - 4) * Math.Clamp(prog, 0, 1));
                _p.Fill(new RectangleF(sq.X + 2, sq.Bottom - 2 - fh, sq.Width - 4, fh), T.SEL);
            }
            if (hidden) _p.Fill(new RectangleF(sq.X + 7, sq.Y + 7, 2, 2), col);
            else if (ch != " ") TCaps(ch, MathF.Floor(sq.X + (pitch - T.W(ch, 2)) / 2), sq.Y + 1, col, 2);
        }
        if (newGame) MinesNew();
        // the window beside it
        var sw = new RectangleF(left.Right + 2, win.Y, win.Right - left.Right - 2, win.Height);
        _p.Fill(sw, T.BG);
        _p.Bevel(sw, Painter.Edge.Window);
        _ui.Caption95(new RectangleF(sw.X + 3, sw.Y + 3, sw.Width - 6, 18), "Mines");
        _mCloseBtn = new RectangleF(sw.Right - 5 - 16, sw.Y + 5, 16, 14);
        if (_ui.CaptionClose95("wd95x", sw.Right - 5 - 16, sw.Y + 5, true, 6)) { CloseDetail(); return; }
        float x0 = sw.X + 8, w = sw.Width - 16;
        if (_ui.Button95("mn95new", new RectangleF(x0, sw.Y + 30, w, 23), "&New game")) MinesNew();
        var flagBtn = new RectangleF(x0, sw.Y + 59, w, 23);
        _mFlagBtn = flagBtn;
        if (_ui.Button95("mn95flag", flagBtn, "&Flag mode", true, false, _mFlagMode)) { _mFlagMode = !_mFlagMode; _dirty = true; }
        float ty = flagBtn.Bottom + 12;
        string msg = g.State switch
        {
            MinesGame.Phase.Won => "YOU WIN! Tap the face to play again.",
            MinesGame.Phase.Lost => "GAME OVER. Tap the face to play again.",
            MinesGame.Phase.Ready => "Tap a square to start.",
            _ => "Hold a square to flag it.",
        };
        _ui.Text95(msg, new RectangleF(x0, ty, w, 60), T.INK, false, TextAlignment.Leading, true, true);
        int best = MinesBestOf(0);
        TCaps(best > 0 ? Lang.F("BEST {0} S", best) : "BEST --", x0, sw.Bottom - 16, T.DIM);
    }
}
