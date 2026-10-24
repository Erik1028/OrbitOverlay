using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using Vortice.DirectWrite;

namespace Orbit;

// The watch in Windows 7: the fixed faces as a "Date and Time" window on glass (the big Segoe UI Light time, the batteries as
// 7's green bars, Windows Media Player's round buttons, a row of push buttons, a status strip); the custom face as desktop
// gadgets - blue glass tiles with white text, the clock gadget, the calendar's red day, a dark media tile with the blue play orb,
// the frame-time line; the pages as the superbar; an opened widget as a glass window.
public sealed partial class WristPanel
{
    static AeroSkin Ae => (AeroSkin)Skins.Aero;

    const uint InkAero = 0xFF1E395B, SubAero = 0xFF5A6B80, TimeInkAero = 0xFF1C3A60;
    static readonly string[] PrevAero = { "k.....k", "k....kk", "k...kkk", "k..kkkk", "k...kkk", "k....kk", "k.....k" };
    static readonly string[] NextAero = { "k.....k", "kk....k", "kkk...k", "kkkk..k", "kkk...k", "kk....k", "k.....k" };
    static readonly string[] PlayAero = { "kk....", "kkkk..", "kkkkkk", "kkkkkk", "kkkk..", "kk....", };
    static readonly string[] PauseAero = { "kk.kk", "kk.kk", "kk.kk", "kk.kk", "kk.kk", "kk.kk", "kk.kk" };
    static readonly string[] BoltAero = { "...kk", "..kk.", ".kkkk", "kkkk.", "..kk.", ".kk..", ".k..." };

    // text on a baseline (the big time and its seconds share one)
    void BaseAero(string s, float x, float baseY, float w, uint c, float size, FontWeight wt = FontWeight.Normal, string fam = null,
        TextAlignment al = TextAlignment.Leading)
    {
        var (bl, lh) = _p.TextLine(size, wt, fam ?? Ae.Font);
        _p.Text(s, new RectangleF(x, MathF.Round(baseY - bl), w, lh + 4), c, size, wt, al, ParagraphAlignment.Near, fam ?? Ae.Font);
    }

    float WAero(string s, float size, FontWeight wt = FontWeight.Normal, string fam = null) => _p.MeasureWidth(s, size, wt, fam ?? Ae.Font);

    // ================= the fixed faces =================

    partial void FaceAero(PanelData d, ref bool done)
    {
        done = true;
        string face = C.Face ?? "full";
        var now = DateTime.Now;
        bool rich = face is "full" or "analog";
        bool media = rich && C.ShowMedia, bats = rich && C.ShowBatteries, perf = rich && C.ShowPerformance, graph = perf && C.PerfGraph;
        bool tools = face != "time";
        const float Cap = 24;
        float top = face is "minimal" or "time" ? 74 : 66;
        float row = face == "full" ? (media ? 48 : 0) : (media || bats ? 48 : 0);
        float h = Cap + top + row + (tools ? 32 : 0) + (perf ? (graph ? 34 : 22) : 0) + 6;
        float wy = MathF.Floor((TH95 - h) / 2);
        var win = new RectangleF(0, wy, TW95, h);
        var cl = AeroSkin.Window7(_p, win, Cap, 0xFFFFFFFF, shadow: true);
        W95Icons.Draw(_p, W95Icons.Id.Clock, win.X + 7, win.Y + 4);
        Ae.GlowText(_p, "Date and Time", new RectangleF(win.X + 27, win.Y + 3, 150, 18), 0xFF000000, 12);
        if (CaptionsAero(win, "w95")) _dismissReq = true;

        bool h24 = _cfg.Data.Appearance.Clock24h;
        var culture = CultureInfo.CurrentCulture;
        string hm = Clk(now, h24 ? "H:mm" : "h:mm", culture), sec = Clk(now, ":ss", culture);
        string ampm = h24 ? null : now.Hour < 12 ? "AM" : "PM";
        string date = Clk(now, culture.DateTimeFormat.LongDatePattern, culture);
        float y0 = cl.Y;
        switch (face)
        {
            case "analog":
                AeroSkin.Clock7(_p, cl.X + 40, y0 + 33, 29, now);
                TimeAero(hm, sec, ampm, cl.X + 82, y0 + 40, 40, false, cl.Right - 8);
                _p.Text(Fit(date, cl.Right - 84 - 8, 12), new RectangleF(cl.X + 84, y0 + 44, cl.Right - 84 - cl.X - 6, 18), 0xFF3C3C3C, 12,
                    FontWeight.Normal, TextAlignment.Leading, ParagraphAlignment.Center, Ae.Font);
                break;
            case "minimal":
            case "time":
                TimeAero(hm, sec, ampm, cl.X, y0 + 46, 52, true, cl.Right);
                _p.Text(Fit(date, cl.Width - 12, 12), new RectangleF(cl.X, y0 + 50, cl.Width, 18), 0xFF3C3C3C, 12, FontWeight.Normal,
                    TextAlignment.Center, ParagraphAlignment.Center, Ae.Font);
                break;
            default:
                float right = bats ? 196 : cl.Right - 70;
                TimeAero(hm, sec, ampm, cl.X + 10, y0 + 40, 40, false, right - 6);
                _p.Text(Fit(date, right - cl.X - 18, 12), new RectangleF(cl.X + 11, y0 + 44, right - cl.X - 14, 18), 0xFF3C3C3C, 12,
                    FontWeight.Normal, TextAlignment.Leading, ParagraphAlignment.Center, Ae.Font);
                if (bats) BatteriesAero(d, new RectangleF(right, y0 + 6, cl.Right - 8 - right, 56), false);
                else AeroSkin.Clock7(_p, cl.Right - 36, y0 + 33, 29, now);
                break;
        }
        float y = y0 + top;
        if (row > 0)
        {
            if (face == "full") MediaRowAero(d, new RectangleF(cl.X + 6, y, cl.Width - 12, 42));
            else
            {
                if (media) MediaRowAero(d, new RectangleF(cl.X + 6, y, bats ? 190 : cl.Width - 12, 42));
                if (bats)
                {
                    float bx = media ? cl.X + 204 : cl.X + 10;
                    BatteriesAero(d, new RectangleF(bx, y, cl.Right - 8 - bx, 42), false);
                }
            }
            y += row;
        }
        if (tools) { ToolbarAero(d, cl, y); y += 32; }
        if (perf)
        {
            if (graph)
            {
                var g = new RectangleF(cl.X + 6, y, cl.Width - 12, 28);
                AeroSkin.FrameLine(_p, d.Vr, g, false);
                GraphLabelAero(d.Vr, g, 0xFF2CFF4C, true);
            }
            else StatusAero(d, new RectangleF(cl.X, cl.Bottom - 20, cl.Width, 20));
        }
    }

    // the caption buttons of a window: minimise, maximise, close (the first two only with a min / max id); true: closed or minimised
    bool CaptionsAero(RectangleF win, string key, bool minMax = true)
    {
        float x = win.Right - 6 - 43, y = win.Y + 1;
        var rc = new RectangleF(x, y, 43, 18);
        bool closed = _ui.Clickable(key + ".x", RectangleF.Inflate(rc, 0, 3), out bool ch, out bool cd);
        bool min = false;
        if (minMax)
        {
            var rmax = new RectangleF(x - 25, y, 26, 18);
            var rmin = new RectangleF(x - 50, y, 26, 18);
            _ui.Clickable(key + ".max", rmax, out bool xh, out bool xd);
            min = _ui.Clickable(key + ".min", RectangleF.Inflate(rmin, 0, 3), out bool nh, out bool nd);
            AeroSkin.CaptionBox(_p, rmin, CaptionKind.Min, true, nd, nh);
            AeroSkin.CaptionBox(_p, rmax, CaptionKind.Max, true, xd, xh);
        }
        AeroSkin.CaptionBox(_p, rc, CaptionKind.Close, true, cd, ch);
        return closed || min;
    }

    // "19:58" big and light, ":42" smaller and grey on the same baseline, AM / PM after it
    void TimeAero(string hm, string sec, string ampm, float x, float baseY, float size, bool center, float right)
    {
        const string L = AeroSkin.LightFont;
        float ss = MathF.Round(size * 0.5f);
        float w1 = WAero(hm, size, FontWeight.Normal, L), w2 = WAero(sec, ss, FontWeight.Normal, L);
        float w3 = ampm == null ? 0 : WAero(ampm, 13) + 4;
        float total = w1 + w2 + w3;
        if (total > right - x - 4) { size = MathF.Floor(size * (right - x - 4) / total); ss = MathF.Round(size * 0.5f); w1 = WAero(hm, size, FontWeight.Normal, L); w2 = WAero(sec, ss, FontWeight.Normal, L); total = w1 + w2 + w3; }
        if (center) x = MathF.Round(x + (right - x - total) / 2);
        BaseAero(hm, x, baseY, w1 + 6, TimeInkAero, size, FontWeight.Normal, L);
        BaseAero(sec, x + w1, baseY, w2 + 6, 0xFF8496AE, ss, FontWeight.Normal, L);
        if (ampm != null) BaseAero(ampm, x + w1 + w2 + 4, baseY, w3 + 4, 0xFF8496AE, 13);
    }

    // the batteries: name, 7's bar (green, yellow when low-ish, red when low, blue while charging), the percentage
    void BatteriesAero(PanelData d, RectangleF r, bool glass)
    {
        // a tall tile: the battery meter gadget's rows - the name and the percentage on a line, the bar under them
        int nb = Math.Min(4, d.Vr.Batteries.Count);
        if (glass && nb > 0 && r.Width >= 80 && r.Height / nb >= 23) { BatteryRowsAero(d, r, nb); return; }
        int shown = 0, max = Math.Max(1, Math.Min(4, (int)(r.Height / 13)));
        float pitch = MathF.Min(14, MathF.Floor(r.Height / Math.Max(1, Math.Min(max, d.Vr.Batteries.Count))));
        float y0 = r.Y + MathF.Floor((r.Height - Math.Min(max, d.Vr.Batteries.Count) * pitch) / 2);
        uint ink = glass ? 0xFFFFFFFF : 0xFF2D2D2D;
        foreach (var b in d.Vr.Batteries)
        {
            if (shown >= max) break;
            float y = y0 + shown * pitch;
            _p.Text(Bat(b.label), new RectangleF(r.X, y - 2, 32, pitch + 4), ink, 11, FontWeight.Normal, TextAlignment.Leading, ParagraphAlignment.Center, Ae.Font);
            var bar = new RectangleF(r.X + 32, y + MathF.Floor((pitch - 9) / 2), r.Width - 32 - 32, 9);
            uint[] col = b.charging ? AeroSkin.BarBlue : b.pct <= 0.2f ? AeroSkin.BarRed : b.pct <= 0.35f ? AeroSkin.BarYellow : AeroSkin.BarGreen;
            if (bar.Width >= 10) AeroSkin.Bar7(_p, bar, b.pct, col);
            uint pc = b.charging ? (glass ? 0xFFBFE2FF : 0xFF2B83D6) : b.pct <= 0.2f ? (glass ? 0xFFFFA48F : 0xFFC42B1C) : ink;
            if (b.charging) _p.Pixels(BoltAero, bar.Right + 2, y + MathF.Floor((pitch - 7) / 2), pc);
            _p.Text($"{b.pct * 100:0}%", new RectangleF(bar.Right + 2, y - 2, 30, pitch + 4), pc, 11, FontWeight.Normal, TextAlignment.Trailing,
                ParagraphAlignment.Center, Ae.Font);
            shown++;
        }
        if (shown == 0)
            _p.Text(d.VrConnected ? "No battery info" : "SteamVR is off", new RectangleF(r.X, r.Y, r.Width, 16), glass ? 0xFFC6DCF4 : 0xFF6D6D6D, 11,
                FontWeight.Normal, TextAlignment.Leading, ParagraphAlignment.Center, Ae.Font);
    }

    // the long names where a row has the room (the short ones of the 95 meter otherwise)
    static string[] BatteryNamesAero(string label) => label switch
    {
        "L" => new[] { "Left controller", "Left", "L" },
        "R" => new[] { "Right controller", "Right", "R" },
        _ when label.StartsWith("Trk×", StringComparison.Ordinal) => new[] { Lang.F("{0} trackers (lowest)", label[4..]), Lang.F("{0} trackers", label[4..]), label },
        _ => new[] { label },
    };

    void BatteryRowsAero(PanelData d, RectangleF r, int n)
    {
        float pitch = MathF.Min(30, MathF.Floor(r.Height / n));
        float y = r.Y + MathF.Floor((r.Height - n * pitch) / 2) + MathF.Floor((pitch - 23) / 2);
        for (int i = 0; i < n; i++, y += pitch)
        {
            var b = d.Vr.Batteries[i];
            string pct = $"{b.pct * 100:0}%";
            uint pc = b.charging ? 0xFFBFE2FF : b.pct <= 0.2f ? 0xFFFFA48F : 0xFFFFFFFF;
            float pw = WAero(pct, 11) + 2, bw = b.charging ? 7 : 0;
            float nw = r.Width - pw - bw - 6;
            string name = Array.Find(BatteryNamesAero(b.label), x => WAero(x, 11) <= nw) ?? Fit(Bat(b.label), nw);
            _p.Text(name, new RectangleF(r.X, y - 2, nw + 2, 16), 0xFFE8F1FC, 11, FontWeight.Normal, TextAlignment.Leading, ParagraphAlignment.Center, Ae.Font);
            if (b.charging) _p.Pixels(BoltAero, MathF.Round(r.X + WAero(name, 11) + 4), y + 2, 0xFFFFD866);
            _p.Text(pct, new RectangleF(r.Right - pw - 2, y - 2, pw + 2, 16), pc, 11, FontWeight.Normal, TextAlignment.Trailing, ParagraphAlignment.Center, Ae.Font);
            uint[] col = b.charging ? AeroSkin.BarBlue : b.pct <= 0.2f ? AeroSkin.BarRed : b.pct <= 0.35f ? AeroSkin.BarYellow : AeroSkin.BarGreen;
            AeroSkin.Bar7(_p, new RectangleF(r.X, y + 13, r.Width, 9), b.pct, col);
        }
    }

    // Windows Media Player's now playing: the cover, the title and artist, |<< (play) >>| as round glass buttons
    void MediaRowAero(PanelData d, RectangleF box)
    {
        var (has, title, artist, playing) = MediaNow(d);
        _p.Smooth(true);
        _p.FillRound(box, 0xFFC5D7EE, 4);
        _p.Smooth(false);
        _p.GradientRaw(RectangleF.Inflate(box, -1, -1), 0xFFF7FAFE, 0xFFE4EEFA, 3);
        var art = new RectangleF(box.X + 5, box.Y + 5, 32, 32);
        ArtAero(art);
        float next = box.Right - 15, play = next - 26, prev = play - 26;
        float tx = art.Right + 8, tw = prev - 14 - tx;
        if (has)
        {
            _p.Text(Fit(title, tw, 12, true), new RectangleF(tx, box.Y + 3, tw + 4, 20), InkAero, 12, FontWeight.Bold, TextAlignment.Leading,
                ParagraphAlignment.Center, Ae.Font);
            _p.Text(Fit(artist ?? "", tw, 11), new RectangleF(tx, box.Y + 20, tw + 4, 18), 0xFF6D6D6D, 11, FontWeight.Normal, TextAlignment.Leading,
                ParagraphAlignment.Center, Ae.Font);
        }
        else _p.Text("Nothing playing", new RectangleF(tx, box.Y, tw + 4, box.Height), 0xFF6D6D6D, 11, FontWeight.Normal, TextAlignment.Leading,
            ParagraphAlignment.Center, Ae.Font);
        MediaOrbsAero(prev, play, next, box.Y + box.Height / 2, has, playing, "wm", 0);
    }

    void MediaOrbsAero(float prev, float play, float next, float cy, bool has, bool playing, string key, int baseId, bool onlyPlay = false)
    {
        string[] ids = { "media.prev", "media.play", "media.next" };
        float[] xs = { prev, play, next };
        for (int i = 0; i < 3; i++)
        {
            if (onlyPlay && i != 1) continue;
            float rad = i == 1 ? 13 : 10;
            var hit = new RectangleF(xs[i] - 13, cy - 13, 26, 26);
            bool click = _ui.Clickable(Ui.Id(key, baseId + i), hit, out bool hover, out bool held) && has;
            string[] g = i == 0 ? PrevAero : i == 2 ? NextAero : playing ? PauseAero : PlayAero;
            AeroSkin.Orb(_p, xs[i], cy, rad, i == 1, hover && has, held && has, g, has);
            if (click) Act(ids[i]);
        }
    }

    // the cover in a thin frame with a shadow (a note on 7's blue when there is none)
    void ArtAero(RectangleF art)
    {
        _p.Fill(new RectangleF(art.X + 1, art.Y + 1, art.Width, art.Height), Theme.WithAlpha(0xFF000000, 0.18f));
        _p.Fill(RectangleF.Inflate(art, 1, 1), 0xFFB4C3D6);
        if (_art != null) _p.Image(_art, art);
        else
        {
            _p.GradientRaw(art, 0xFF7DB4EC, 0xFF2B6CC4);
            W95Icons.Draw(_p, W95Icons.Id.Note, MathF.Floor(art.X + (art.Width - 16) / 2), MathF.Floor(art.Y + (art.Height - 16) / 2));
        }
    }

    // keyboard, monitors, recenter, the layout profile, night mode, settings: 7's push buttons with their names
    void ToolbarAero(PanelData d, RectangleF cl, float y)
    {
        string[] ids = { "keyboard", "desktops", "recenter", "layout", "night", "settings" };
        string[] names = { "Keyboard", "Monitors", "Center", null, "Night", "Settings" };
        const int n = 6;
        float bw = MathF.Floor((cl.Width - 8 - (n - 1) * 2) / n);
        float x0 = cl.X + MathF.Floor((cl.Width - (n * bw + (n - 1) * 2)) / 2);
        for (int i = 0; i < n; i++)
        {
            var r = new RectangleF(x0 + i * (bw + 2), y + 2, bw, 26);
            bool on = (i == 0 && d.KeyboardVisible) || (i == 1 && d.DesktopsVisible) || (i == 4 && d.NightMode);
            bool click = _ui.Clickable(Ui.Id("wt", i), r, out bool hover, out bool held);
            Ae.ButtonFace(_p, r, false, held, on, hover);
            // Segoe UI 9 pt, or a point smaller where a name would not fit
            string label = names[i] ?? d.LayoutName;
            float size = WAero(label, 11) <= bw - 5 ? 11 : 10;
            _p.Text(Fit(label, bw - 5, size), new RectangleF(r.X + 1, r.Y - 2, r.Width - 2, r.Height + 4), 0xFF000000, size, FontWeight.Normal,
                TextAlignment.Center, ParagraphAlignment.Center, Ae.Font);
            if (click) Act(ids[i]);
        }
    }

    // the status strip along the bottom of the window: frame rate, reprojection, GPU time, video memory
    void StatusAero(PanelData d, RectangleF r)
    {
        var v = d.Vr;
        _p.GradientRaw(r, 0xFFF3F6FA, 0xFFE3E9F1);
        _p.Fill(new RectangleF(r.X, r.Y, r.Width, 1), 0xFFD0DAE6);
        float budget = 1000f / Math.Max(1, v.DisplayHz), vram = d.VramUsed;
        const uint ok = 0xFF4C4C4C, warn = 0xFF9A6A00, bad = 0xFFC42B1C;
        (string, string, uint)[] panes =
        {
            (v.AppFps < 0 ? "-- fps" : $"{v.AppFps:0}/{v.DisplayHz:0} fps", v.AppFps < 0 ? "--" : $"{v.AppFps:0} fps",
                v.AppFps >= 0 && v.AppFps < v.DisplayHz * 0.85f ? warn : ok),
            (v.ReprojPct < 0 ? Lang.T("-- reprojection") : Lang.F("{0:0}% reprojection", v.ReprojPct), v.ReprojPct < 0 ? "--" : $"{v.ReprojPct:0}% reproj.",
                v.ReprojPct > 25 ? bad : v.ReprojPct > 8 ? warn : ok),
            (v.GpuMs < 0 ? "-- ms GPU" : $"{v.GpuMs:0.0} ms GPU", v.GpuMs < 0 ? "--" : $"{v.GpuMs:0.0} ms",
                v.GpuMs > budget ? bad : v.GpuMs > budget * 0.85f ? warn : ok),
            (vram < 0 ? "-- GB VRAM" : d.VramTotalGB > 0 ? $"{vram:0.0}/{d.VramTotalGB:0} GB VRAM" : $"{vram:0.0} GB VRAM", vram < 0 ? "--" : $"{vram:0.0} GB",
                vram > 0 && d.VramTotalGB > 0 && vram > d.VramTotalGB * 0.92f ? warn : ok),
        };
        float pw = r.Width / 4;
        for (int i = 0; i < 4; i++)
        {
            var pr = new RectangleF(MathF.Round(r.X + i * pw), r.Y + 1, MathF.Round(pw), r.Height - 1);
            if (i > 0) _p.Fill(new RectangleF(pr.X, pr.Y + 4, 1, pr.Height - 8), 0xFFC9D3DF);
            string s = WAero(panes[i].Item1, 11) <= pr.Width - 6 ? panes[i].Item1 : Fit(panes[i].Item2, pr.Width - 6);
            _p.Text(s, new RectangleF(pr.X + 2, pr.Y - 2, pr.Width - 4, pr.Height + 4), panes[i].Item3, 11, FontWeight.Normal, TextAlignment.Center,
                ParagraphAlignment.Center, Ae.Font);
        }
    }

    void GraphLabelAero(VrStats v, RectangleF g, uint c, bool box)
    {
        string label = v.AppFps < 0 ? "waiting for frames" : $"{v.AppFps:0} fps  {v.GpuMs:0.0} ms GPU  {Math.Max(0, v.ReprojPct):0}% reproj.";
        if (v.AppFps >= 0 && WAero(label, 11) > g.Width - 12) label = $"{v.AppFps:0} fps  {v.GpuMs:0.0} ms  {Math.Max(0, v.ReprojPct):0}%";
        label = Fit(label, g.Width - 12);
        if (box) _p.Fill(new RectangleF(g.X + 2, g.Y + 2, MathF.Min(WAero(label, 11) + 6, g.Width - 4), 13), 0xFF000000);
        _p.Text(label, new RectangleF(g.X + 5, g.Y, g.Width - 8, 17), c, 11, FontWeight.Normal, TextAlignment.Leading, ParagraphAlignment.Center, Ae.Font);
    }

    // ================= the custom face: gadgets =================

    partial void WidgetAero(WatchWidget w, WatchWidgets.Kind k, RectangleF r, PanelData d, int id, ref bool done)
    {
        done = true;
        var sk = Ae;
        if (k.Button) { ButtonAero(id, w, k, r, d); return; }
        var now = DateTime.Now;
        switch (k.Id)
        {
            case "media": MediaTileAero(r, d, w, id); return;
            case "photo": PhotoTileAero(r, w, d, id); return;          // the Slide Show gadget (WristPhoto.Aero.cs)
            case "memory": PhotoTileAero(r, w, d, id); return;
            case "weather": WeatherTileAero(r, d, w); return;          // the weather gadget (WristWeather.Aero.cs)
            case "analog":
                AeroSkin.Tile(_p, r, 0xFF68ABEC, 0xFF3C7FCC);
                float rad = MathF.Floor(MathF.Min(r.Width, r.Height) / 2) - 6;
                AeroSkin.Clock7(_p, MathF.Floor(r.X + r.Width / 2), MathF.Floor(r.Y + r.Height / 2), rad, now);
                return;
            case "calendar" when w.H >= 2 && r.Width >= 140: MonthTileAero(r, now); return;
            case "calendar":
            case "date":
                DayTileAero(r, w, now);
                return;
            case "graph":
            {
                var c0 = sk.WidgetFrame(_p, r, null);
                var v = d.Vr;
                bool head = c0.Height >= 34;
                var g = head ? new RectangleF(c0.X + 2, c0.Y + 15, c0.Width - 4, c0.Height - 17) : RectangleF.Inflate(c0, -2, -2);
                _p.Fill(g, Theme.WithAlpha(0xFF0B2E5C, 0.30f));
                AeroSkin.FrameLine(_p, v, g, true);
                if (head)
                {
                    _p.Text("Frame time", new RectangleF(c0.X + 3, c0.Y - 1, c0.Width / 2, 16), 0xFFDCEBFB, 11, FontWeight.Normal,
                        TextAlignment.Leading, ParagraphAlignment.Center, sk.Font);
                    string fr = v.AppFps < 0 ? "--" : $"{v.AppFps:0} fps · {v.GpuMs:0.0} ms";
                    if (WAero(fr, 11) > c0.Width / 2 - 4 && v.AppFps >= 0) fr = $"{v.AppFps:0} fps";
                    _p.Text(Fit(fr, c0.Width / 2 - 4), new RectangleF(c0.X + c0.Width / 2, c0.Y - 1, c0.Width / 2 - 3, 16), 0xFFDCEBFB, 11,
                        FontWeight.Normal, TextAlignment.Trailing, ParagraphAlignment.Center, sk.Font);
                }
                else GraphLabelAero(v, g, 0xFFFFFFFF, false);
                return;
            }
        }
        string title = k.Id is "time" or "date" or "battery" or "session" or "fps" or "reproj" or "gpu" or "cputime" or "vram" or "cpu" or "ram" ? null
            : w.H >= 2 ? k.Title95 : null;
        var c = sk.WidgetFrame(_p, r, title);
        sk.Glass = true;
        try
        {
            switch (k.Id)
            {
                case "time" when w.W == 1: ValueAero(c, Clk(now, _cfg.Data.Appearance.Clock24h ? "H:mm" : "h:mm", CultureInfo.CurrentCulture),
                    _cfg.Data.Appearance.Clock24h ? Clk(now, "ddd d", CultureInfo.CurrentCulture) : now.Hour < 12 ? "AM" : "PM", 0xFFFFFFFF); break;
                case "time": TimeTileAero(c, now, w.H >= 2); break;
                case "date":
                    string ds = FitDate95(now, c.Width - 6);
                    float dsz = w.W >= 4 && _ui.Width95(ds, false, 13) <= c.Width - 10 ? 13 : Ui.F95;
                    _ui.Text95(ds, c, 0xFF000000, false, TextAlignment.Center, size: dsz);
                    break;
                case "timer": Timer95(c, w, id); break;
                case "stopwatch": Stopwatch95(c, w, id); break;
                case "battery":
                    if (w.W == 1 && w.H == 1 || d.Vr.Batteries.Count == 0) Battery95(c, d, w);
                    else BatteriesAero(d, RectangleF.Inflate(c, -3, -1), true);
                    break;
                case "dock": Dock95Widget(r, c, d, w, id); break;
                case "room": Room95Widget(r, c, d, w, id); break;
                case "volume": Volume95(c, w, id); break;
                case "minesweeper": MinesTileAero(c, w); break;
                case "mic": Mic95(r, c, w, id); break;
                case "notifications": NotesAero(c, d); break;
                case "buttons": Group95(c, w, d, id); break;
                case "mixer": Mixer95(r, c, w, id); break;
                case "friends": Friends95(c, w, d); break;           // the shared bodies, white on the glass
                case "recap": Recap95(c, w, d); break;
                case "light": Light95(r, c, w, d, id); break;
                case "rgb": Rgb95(r, c, w, d, id); break;
                case "temps": Temps95(c, d, w, true); break;
                case "avatar": Avatar95(r, c, w, d, id); break;
                case "slimevr": Slime95(c, w, d, true, id); break;     // white on the glass
                case "perf": Status95(d, new RectangleF(c.X, MathF.Floor(c.Y + (c.Height - 24) / 2), c.Width, 24)); break;
                case "session":
                    string sv = SessionText(d);
                    ValueAero(c, sv == "–" ? "--" : sv, w.W >= 2 ? "time in VR" : "in VR", 0xFFFFFFFF);
                    break;
                default:
                    var (frac, value, unit, _, tint) = StatInfo(k.Id, d);
                    uint color = tint == Theme.Warn ? 0xFFFFD866u : tint == Theme.Bad ? 0xFFFFA48Fu : 0xFFFFFFFF;
                    if (value == "–") value = "--";
                    string cap = k.Id switch
                    {
                        "fps" => d.Vr.AppFps >= 0 ? $"fps / {d.Vr.DisplayHz:0}" : "fps", "reproj" => Lang.T("reprojection"), "gpu" => "ms GPU",
                        "cputime" => "ms CPU", "vram" => "GB VRAM", "cpu" => "CPU", _ => "RAM",
                    };
                    if (WAero(cap, 11) > c.Width - 2)
                        cap = k.Id switch { "fps" => "fps", "reproj" => "reproj.", "gpu" => "GPU ms", "cputime" => "CPU ms", "vram" => "VRAM", _ => cap };
                    if (w.W >= 2 && (k.Id is "cpu" or "ram" or "vram") && frac >= 0)
                    {
                        _p.Text(Fit($"{Lang.T(cap)}: {value}{(unit == "GB" && k.Id != "vram" ? " GB" : "")}", c.Width - 8), new RectangleF(c.X + 4, c.Y + 2, c.Width - 8, 18),
                            color, 11, FontWeight.Normal, TextAlignment.Leading, ParagraphAlignment.Center, sk.Font);
                        AeroSkin.Bar7(_p, new RectangleF(c.X + 4, c.Y + 22, c.Width - 8, 12), frac,
                            tint == Theme.Bad ? AeroSkin.BarRed : tint == Theme.Warn ? AeroSkin.BarYellow : AeroSkin.BarGreen);
                    }
                    else ValueAero(c, value, cap, color);
                    break;
            }
        }
        finally { sk.Glass = false; }
    }

    // a gadget's reading: the value big and light, what it is small under it
    void ValueAero(RectangleF c, string value, string caption, uint color)
    {
        const string L = AeroSkin.LightFont;
        float size = 24;
        float vw = WAero(value, size, FontWeight.Normal, L);
        if (vw > c.Width - 4) size = MathF.Max(13, MathF.Floor(size * (c.Width - 4) / vw));
        float capH = c.Height >= 40 ? 14 : 0;
        float vh = MathF.Round(size * 1.05f);
        float y = MathF.Floor(c.Y + (c.Height - vh - capH) / 2);
        _p.Text(Fit(value, c.Width - 2, size), new RectangleF(c.X, y - 3, c.Width, vh + 6), color, size, FontWeight.Normal, TextAlignment.Center,
            ParagraphAlignment.Center, size >= 18 ? L : Ae.Font);
        if (capH > 0)
            _p.Text(Fit(caption, c.Width - 2), new RectangleF(c.X, y + vh - 2, c.Width, capH + 4), 0xFFD5E6F8, 11, FontWeight.Normal,
                TextAlignment.Center, ParagraphAlignment.Center, Ae.Font);
    }

    void TimeTileAero(RectangleF c, DateTime now, bool tall)
    {
        bool h24 = _cfg.Data.Appearance.Clock24h;
        var culture = CultureInfo.CurrentCulture;
        string time = Clk(now, h24 ? "H:mm" : "h:mm", culture);
        string ampm = h24 ? null : now.Hour < 12 ? "AM" : "PM";
        bool withDate = tall && c.Height >= 44;
        const string L = AeroSkin.LightFont;
        float aw = ampm != null ? WAero(ampm, 13) + 4 : 0;
        float size = MathF.Floor(MathF.Min(c.Height * (withDate ? 0.62f : 0.80f), 100 * (c.Width - 10 - aw) / WAero(time, 100, FontWeight.Normal, L)));
        size = Math.Max(12, size);
        var (above, _) = _p.DigitInk(FontWeight.Normal, L);
        float ink = above * size, dateH = withDate ? 16 : 0;
        float baseY = MathF.Round(c.Y + (c.Height - ink - dateH) / 2 + ink);
        float tw = WAero(time, size, FontWeight.Normal, L), x = MathF.Round(c.X + (c.Width - tw - aw) / 2);
        BaseAero(time, x, baseY, tw + 6, 0xFFFFFFFF, size, FontWeight.Normal, L);
        if (ampm != null) BaseAero(ampm, x + tw + 4, baseY, aw + 4, 0xFFD5E6F8, 13);
        if (withDate)
            _p.Text(FitDate95(now, c.Width - 6), new RectangleF(c.X, baseY + 1, c.Width, 16), 0xFFD5E6F8, 11, FontWeight.Normal, TextAlignment.Center,
                ParagraphAlignment.Center, Ae.Font);
    }

    // the calendar gadget: the day on red (the weekday over it), the month and the year on the white page beside it
    void DayTileAero(RectangleF r, WatchWidget w, DateTime now)
    {
        var culture = CultureInfo.CurrentCulture;
        _p.Smooth(true);
        _p.FillRound(r, 0xFF8E2A20, 4);
        _p.Smooth(false);
        var i = RectangleF.Inflate(r, -1, -1);
        float redW = r.Width >= 90 ? MathF.Min(MathF.Round(i.Width * 0.38f), 64) : i.Width;
        var red = new RectangleF(i.X, i.Y, redW, i.Height);
        _p.PushRoundedClip(i, 3);
        _p.GradientRaw(red, 0xFFE8574B, 0xFFCF3A2E, 0xFFB52A20);
        _p.Fill(new RectangleF(red.X, red.Y, red.Width, MathF.Round(red.Height * 0.45f)), Theme.WithAlpha(0xFFFFFFFF, 0.10f));
        if (redW < i.Width)
            _p.GradientRaw(new RectangleF(red.Right, i.Y, i.Width - redW, i.Height), 0xFFFFFFFF, 0xFFF1F4F8);
        _p.PopRoundedClip();
        string wd = Clk(now, "ddd", culture).ToUpper(culture).TrimEnd('.');
        float ds = MathF.Min(26, MathF.Floor(red.Height * 0.48f));
        float blockH = 13 + ds * 1.05f, y = MathF.Floor(red.Y + (red.Height - blockH) / 2);
        _p.Text(wd, new RectangleF(red.X, y - 2, red.Width, 16), 0xFFFFE3DF, 11, FontWeight.Normal, TextAlignment.Center, ParagraphAlignment.Center, Ae.Font);
        _p.Text(now.Day.ToString(culture), new RectangleF(red.X, y + 10, red.Width, ds * 1.3f), 0xFFFFFFFF, ds, FontWeight.Bold, TextAlignment.Center,
            ParagraphAlignment.Center, Ae.Font);
        if (redW >= i.Width) return;
        var page = new RectangleF(red.Right + 6, i.Y, i.Right - red.Right - 10, i.Height);
        string month = Clk(now, "MMMM", culture);
        float ms = WAero(month, 16) <= page.Width ? 16 : 13;
        float py = MathF.Floor(page.Y + (page.Height - 34) / 2);
        _p.Text(Fit(month, page.Width, ms), new RectangleF(page.X, py - 2, page.Width + 4, 24), AeroSkin.HeadBlue, ms, FontWeight.Normal,
            TextAlignment.Leading, ParagraphAlignment.Center, Ae.Font);
        _p.Text(now.Year.ToString(culture), new RectangleF(page.X, py + 19, page.Width, 16), 0xFF8A94A0, 11, FontWeight.Normal, TextAlignment.Leading,
            ParagraphAlignment.Center, Ae.Font);
    }

    // a big calendar: the month on a white page under a red band, today in 7's blue
    void MonthTileAero(RectangleF r, DateTime now)
    {
        var culture = CultureInfo.CurrentCulture;
        _p.Smooth(true);
        _p.FillRound(r, 0xFF7E8EA3, 4);
        _p.Smooth(false);
        var i = RectangleF.Inflate(r, -1, -1);
        _p.PushRoundedClip(i, 3);
        _p.GradientRaw(i, 0xFFFFFFFF, 0xFFEEF2F7);
        var band = new RectangleF(i.X, i.Y, i.Width, 18);
        _p.GradientRaw(band, 0xFFE8574B, 0xFFC4342A);
        _p.PopRoundedClip();
        _p.Text(Capital(Clk(now, "MMMM yyyy", culture)), band, 0xFFFFFFFF, 11, FontWeight.Bold, TextAlignment.Center, ParagraphAlignment.Center, Ae.Font);
        var g = new RectangleF(i.X + 3, band.Bottom + 1, i.Width - 6, i.Bottom - band.Bottom - 3);
        int first = (int)culture.DateTimeFormat.FirstDayOfWeek;
        float cw = g.Width / 7, rh = MathF.Floor(g.Height / 7);
        for (int col = 0; col < 7; col++)
        {
            string n = culture.DateTimeFormat.GetShortestDayName((DayOfWeek)((first + col) % 7));
            _p.Text(n[..1].ToUpper(culture), new RectangleF(g.X + col * cw, g.Y - 1, cw, rh + 2), 0xFF6D7B8C, 11, FontWeight.Normal,
                TextAlignment.Center, ParagraphAlignment.Center, Ae.Font);
        }
        var day1 = new DateTime(now.Year, now.Month, 1);
        int offset = ((int)day1.DayOfWeek - first + 7) % 7, days = DateTime.DaysInMonth(now.Year, now.Month);
        for (int dd = 1; dd <= days; dd++)
        {
            int cell = offset + dd - 1;
            var cr = new RectangleF(MathF.Floor(g.X + cell % 7 * cw), g.Y + (cell / 7 + 1) * rh, MathF.Ceiling(cw), rh);
            bool today = dd == now.Day;
            if (today)
            {
                _p.Smooth(true);
                _p.FillRound(RectangleF.Inflate(cr, 0, 0), 0xFF3399FF, 2);
                _p.Smooth(false);
            }
            _p.Text(dd.ToString(culture), new RectangleF(cr.X, cr.Y - 2, cr.Width, cr.Height + 4), today ? 0xFFFFFFFF : 0xFF1E1E1E, 11, FontWeight.Normal,
                TextAlignment.Center, ParagraphAlignment.Center, Ae.Font);
        }
    }

    // now playing as a dark gadget: the cover, the title and artist in white, the blue play orb (and |<< >>| when wide)
    void MediaTileAero(RectangleF r, PanelData d, WatchWidget w, int id)
    {
        var (has, title, artist, playing) = MediaNow(d);
        AeroSkin.Tile(_p, r, 0xFF1B2C46, 0xFF0B1424);
        var c = RectangleF.Inflate(r, -4, -4);
        bool tall = w.H >= 2 && c.Height >= 70;
        float artS = tall ? MathF.Min(56, c.Height - 36) : MathF.Min(c.Height - 4, 40);
        var art = new RectangleF(c.X + 2, tall ? c.Y + 2 : MathF.Floor(c.Y + (c.Height - artS) / 2), artS, artS);
        bool showArt = c.Width >= 120;
        if (showArt) ArtAero(art);
        bool three = c.Width >= 170;
        float cy = tall ? c.Bottom - 16 : c.Y + c.Height / 2;
        float playX = tall ? c.X + c.Width / 2 : c.Right - 16, prevX = playX - 26, nextX = playX + 26;
        if (!tall && three) { nextX = c.Right - 13; playX = nextX - 26; prevX = playX - 26; }
        float tx = showArt ? art.Right + 8 : c.X + 4;
        float tr = tall ? c.Right - 4 : (three ? prevX - 14 : playX - 18);
        float tw = MathF.Max(10, tr - tx);
        float ty = tall ? art.Y + 6 : MathF.Floor(c.Y + c.Height / 2 - 17);
        if (has)
        {
            _p.Text(Fit(title, tw, 12, true), new RectangleF(tx, ty, tw + 4, 18), 0xFFFFFFFF, 12, FontWeight.Bold, TextAlignment.Leading,
                ParagraphAlignment.Center, Ae.Font);
            _p.Text(Fit(artist ?? "", tw), new RectangleF(tx, ty + 16, tw + 4, 18), 0xFF9FB2CA, 11, FontWeight.Normal, TextAlignment.Leading,
                ParagraphAlignment.Center, Ae.Font);
        }
        else _p.Text(Fit("Nothing playing", tw), new RectangleF(tx, ty + 6, tw + 4, 18), 0xFF9FB2CA, 11, FontWeight.Normal, TextAlignment.Leading,
            ParagraphAlignment.Center, Ae.Font);
        MediaOrbsAero(prevX, playX, nextX, cy, has, playing, "wcm", id * 3, !(three || tall));
    }

    // the last notifications on a white list: what, how long ago
    void NotesAero(RectangleF c, PanelData d)
    {
        NotesNow(d);
        var box = new RectangleF(c.X + 1, c.Y + 1, c.Width - 2, c.Height - 2);
        _p.Fill(box, 0xFFFFFFFF);
        AeroSkin.Field(_p, box, false);
        var inner = RectangleF.Inflate(box, -4, -2);
        bool g = Ae.Glass;
        Ae.Glass = false;
        if (_noteRows.Count == 0) { _ui.Text95(Fit("(no notifications)", inner.Width), inner, 0xFF6D6D6D, false, TextAlignment.Center); Ae.Glass = g; return; }
        int rows = Math.Max(1, (int)(inner.Height / 14));
        var now = DateTime.Now;
        for (int i = 0; i < Math.Min(rows, _noteRows.Count); i++)
        {
            var (text, _, at) = _noteRows[i];
            float y = inner.Y + i * 14;
            string age = Age(at, now);
            float aw = _ui.Width95(age) + 4;
            _ui.Text95(Fit(text, inner.Width - aw - 4), new RectangleF(inner.X, y, inner.Width - aw, 14), 0xFF000000);
            _ui.Text95(age, new RectangleF(inner.Right - aw, y, aw, 14), 0xFF6D6D6D, false, TextAlignment.Trailing);
        }
        Ae.Glass = g;
    }

    // ================= the pages: 7's superbar =================

    partial void TaskbarAero(int count, int cur, ref bool done)
    {
        done = true;
        float y = TH95 - WatchWidgets.Taskbar95 - 2;
        var bar = new RectangleF(0, y, TW95, TH95 - y);
        _p.GradientRaw(bar, 0xFF5B7EA8, 0xFF2B4A72, 0xFF14294A);
        _p.Fill(new RectangleF(0, y, TW95, MathF.Round(bar.Height * 0.45f)), Theme.WithAlpha(0xFFFFFFFF, 0.10f));
        _p.Fill(new RectangleF(0, y, TW95, 1), 0xFF9DB9DB);
        _p.Fill(new RectangleF(0, y + 1, TW95, 1), Theme.WithAlpha(0xFFFFFFFF, 0.25f));
        float bw = MathF.Floor(MathF.Min(104, (TW95 - 6 - (count - 1) * 3) / (float)count));
        for (int i = 0; i < count; i++)
        {
            var b = new RectangleF(3 + i * (bw + 3), y + 4, bw, TH95 - y - 6);
            bool click = _ui.Clickable(Ui.Id("wtb", i), b, out bool hover, out bool held);
            bool on = i == cur;
            // the superbar's hot-track: the button lights up (in quickly, away slowly) and a glow follows the pointer along it, in
            // the pointer's (the laser's) colour where 7 took the program icon's
            float h = XpAeroFx.Fade(XpAeroFx.Task, b, hover && !held, AeroSkin.GlowIn, AeroSkin.GlowOut);
            if (hover && i < _glowX7.Length) _glowX7[i] = Math.Clamp(_ui.Mouse.X, b.X + 4, b.Right - 4);
            if (on || held || h >= 1) ButtonLit7(b, on, held);
            else
            {
                _p.Smooth(true);
                _p.StrokeRound(new RectangleF(b.X + 0.5f, b.Y + 0.5f, b.Width - 1, b.Height - 1), Theme.WithAlpha(0xFFFFFFFF, 0.22f), 1, 3);
                _p.Smooth(false);
                if (h > 0)
                {
                    _p.PushFade(h);
                    ButtonLit7(b, false, false);
                    _p.PopFade();
                }
            }
            if (h > 0 && !held && i < _glowX7.Length)
            {
                // the glow: soft rings round the pointer's place, at the button's foot, clipped to the button
                var gi = RectangleF.Inflate(b, -1, -1);
                float gx = XpAeroFx.Ease(XpAeroFx.Glow, b, _glowX7[i], 0.08f), gy = gi.Bottom;
                uint lc = 0xFF000000 | (Theme.Laser & 0xFFFFFF);
                _p.PushRoundedClip(gi, 2);
                _p.Smooth(true);
                for (int ring = 0; ring < 4; ring++)
                    _p.Circle(gx, gy, gi.Height * (1.15f - ring * 0.22f), Theme.WithAlpha(Theme.Mix(lc, 0xFFFFFFFF, 0.35f), 0.16f * h));
                _p.Smooth(false);
                _p.PopRoundedClip();
            }
            string name = Fit(WatchPages.Label(C, i), bw - 10);
            Ae.GlowLight(_p, name, b);
            if (click) GoToPage(i);
        }
    }

    // the superbar's page buttons: where the pointer glow is on each (the last place, while it fades away)
    readonly float[] _glowX7 = new float[16];

    // a lit superbar button: the page in view (a light line at its foot), held (deeper), or under the pointer
    void ButtonLit7(RectangleF b, bool on, bool held)
    {
        _p.Smooth(true);
        _p.FillRound(b, Theme.WithAlpha(0xFF0B1A30, 0.65f), 3);
        _p.Smooth(false);
        var inner = RectangleF.Inflate(b, -1, -1);
        uint top = held ? 0xFF4D7FBF : on ? 0xFF7FA9DA : 0xFF6F95C2, bottom = held ? 0xFF2B5C9C : on ? 0xFF3F6EA8 : 0xFF3D628F;
        _p.GradientRaw(inner, top, bottom, 2);
        _p.Fill(new RectangleF(inner.X + 1, inner.Y, inner.Width - 2, MathF.Round(inner.Height * 0.45f)), Theme.WithAlpha(0xFFFFFFFF, 0.18f));
        if (on) _p.Fill(new RectangleF(inner.X + 4, inner.Bottom - 1, inner.Width - 8, 1), 0xFFBFE4FF);
    }

    // ================= an opened widget: a glass window =================


    partial void DetailAero(PanelData d, WatchWidgets.Kind k, string kind, RectangleF win, ref bool done)
    {
        done = true;
        // 7's window animation: opening, the window grows from 90 % and fades in; closing, the other way (ZoomSeconds; the shared
        // zoom draws nothing of its own in 7 - LiveZoom). On its way it takes no taps (as 95's window is not there yet: a second
        // tap on the tile does not land on the window's buttons).
        double zs = Theme.RetroSkin.ZoomSeconds;
        float e = 1;
        if (zs > 0 && Theme.RetroSkin.LiveZoom)
        {
            float t = (float)Math.Clamp((Clock.Now - _openStart) / zs, 0, 1);
            e = _closing ? (1 - t) * (1 - t) : 1 - (1 - t) * (1 - t) * (1 - t);
        }
        bool fx = e < 1, inert = _ui.Inert;
        if (fx)
        {
            _p.PushFade(e);
            _p.PushScale(0.9f + 0.1f * e, win.X + win.Width / 2, win.Y + win.Height / 2);
            _ui.Inert = true;
        }
        if (kind == "room") Room95Detail(win, d);           // their 95 windows, in 7's skin
        else if (kind == "dock") DockDetailAero(win, d);
        else if (kind == "mines") MinesDetailAero(win);
        else if (kind is "photo" or "memory") PhotoDetailAero(win, d);       // Windows Photo Viewer (WristPhoto.Aero.cs)
        else if (kind == "weather") WeatherFlyoutAero(win, d);
        else WindowAero(d, k, kind, win);
        if (fx)
        {
            _p.PopScale();
            _p.PopFade();
            _ui.Inert = inert;
        }
    }

    // Hexpad: the shared window in 7's skin; while it connects, 7's marquee under the message (busy, how long unknown)
    void DockDetailAero(RectangleF win, PanelData d)
    {
        Dock95Detail(win, d);
        if (_open == null || _closing || _dockIn == null || DockShown(_dockIn, d) || d.Dock == null) return;
        if (_dockIn.Snap.State != HexpadLink.LinkState.Connecting) return;
        var msg = new RectangleF(win.X + 6, win.Y + 26, win.Width - 12, win.Height - 32);
        AeroSkin.Marquee7(_p, new RectangleF(msg.X + 54, msg.Y + 42, MathF.Min(190, msg.Width - 68), 13));
    }

    // an opened widget's own 7 window
    void WindowAero(PanelData d, WatchWidgets.Kind k, string kind, RectangleF win)
    {
        var cl = AeroSkin.Window7(_p, win, 24, 0xFFF0F0F0);
        Ae.GlowText(_p, TitleAero(k, kind), new RectangleF(win.X + 9, win.Y + 3, win.Width - 70, 18), 0xFF000000, 12);
        var rc = new RectangleF(win.Right - 6 - 43, win.Y + 1, 43, 18);
        bool x = _ui.Clickable("wd95x", RectangleF.Inflate(rc, 4, 4), out bool xh, out bool xd);
        AeroSkin.CaptionBox(_p, rc, CaptionKind.Close, true, xd, xh);
        if (x) { CloseDetail(); return; }
        if (_ui.Button95("wd95ok", new RectangleF(win.Right - 10 - 75, win.Bottom - 10 - 23, 75, 23), "OK", true, true)) { CloseDetail(); return; }
        var c = new RectangleF(win.X + 10, win.Y + 30, win.Width - 20, win.Height - 30 - 38);
        var now = DateTime.Now;
        switch (kind)
        {
            case "media": MediaDetailAero(c, d); break;
            case "battery": Battery95Detail(c, d); break;
            case "clock":
            {
                var left = new RectangleF(c.X, c.Y, 160, c.Height);
                _ui.Group95(left, "Date");
                Month95(new RectangleF(left.X + 8, left.Y + 18, left.Width - 16, left.Height - 26), now, now);
                var right = new RectangleF(left.Right + 8, c.Y, c.Right - left.Right - 8, c.Height);
                _ui.Group95(right, "Time");
                float rad = MathF.Floor(MathF.Min(right.Width - 24, right.Height - 58) / 2);
                AeroSkin.Clock7(_p, MathF.Floor(right.X + right.Width / 2), MathF.Floor(right.Y + 20 + rad), rad, now);
                string time = Clk(now, _cfg.Data.Appearance.Clock24h ? "H:mm:ss" : "h:mm:ss tt", CultureInfo.CurrentCulture);
                _ui.Edit95(new RectangleF(right.X + 10, right.Bottom - 30, right.Width - 20, 21), time);
                break;
            }
            case "calendar": Calendar95Detail(c, now); break;
            case "timer": Timer95Detail(c); break;
            case "stopwatch": Stopwatch95Detail(c); break;
            case "perf":
            {
                var g = new RectangleF(c.X, c.Y, c.Width, c.Height - 28);
                AeroSkin.FrameLine(_p, d.Vr, g, false);
                GraphLabelAero(d.Vr, g, 0xFF2CFF4C, true);
                StatusAero(d, new RectangleF(c.X, c.Bottom - 22, c.Width, 22));
                break;
            }
            case "system": System95Detail(c); break;
            case "volume": Volume95Detail(c); break;
            case "mixer": Mixer95Detail(c); break;
            case "notifications": Notes95Detail(c, d); break;
            case "friends": Friends95Detail(c, d); break;
            case "recap": Recap95Detail(c, d); break;
            case "light": Light95Detail(c, d); break;
            case "rgb": Rgb95Detail(c, d); break;
            case "temps": Temps95Detail(c, d); break;
            case "avatar": Avatar95Detail(c, d); break;
            case "slimevr": Slime95Detail(c, d); break;
        }
    }

    // the programs of 2009 these windows stand for (95's CD Player, Power Meter, Volume Control...)
    static string TitleAero(WatchWidgets.Kind k, string kind) => kind switch
    {
        "media" => "Windows Media Player",
        "battery" => "Battery Meter",
        "clock" => "Date and Time",
        "volume" => "Volume Mixer",
        "system" => k.Id == "ram" ? "Resource Monitor" : "Task Manager",
        _ => k.Title95,
    };

    // a button on the face: 7's push button with its picture, the name under it (beside it when the button is wide)
    void ButtonAero(int id, WatchWidget w, WatchWidgets.Kind k, RectangleF r, PanelData d)
    {
        bool layout = k.Id == "layout";
        bool click = _ui.Clickable(Ui.Id("wcb", id), r, out bool hover, out bool held);
        Ae.ButtonFace(_p, r, false, held, !layout && IsOn(k.Id, d), hover);
        var icon = k.Id == "desktops" && !d.DesktopsVisible ? W95Icons.Id.ComputerOff : k.Icon95;
        string name = layout ? d.LayoutName : w.W >= 2 ? k.Name : k.Title95;
        float o = held ? 1 : 0;
        if (layout && w.W >= 2)
        {
            _p.Text(Fit(name, r.Width - 10), new RectangleF(r.X + 4 + o, r.Y - 2 + o, r.Width - 8, r.Height + 4), 0xFF000000, 11, FontWeight.Normal,
                TextAlignment.Center, ParagraphAlignment.Center, Ae.Font);
        }
        else if (w.W >= 2)
        {
            float tw = MathF.Min(WAero(name, 11), r.Width - 34), x = MathF.Round(r.X + (r.Width - 16 - 5 - tw) / 2);
            W95Icons.Draw(_p, icon, x + o, MathF.Floor(r.Y + (r.Height - 16) / 2) + o);
            _p.Text(Fit(name, tw + 1), new RectangleF(x + 21 + o, r.Y - 2 + o, tw + 4, r.Height + 4), 0xFF000000, 11, FontWeight.Normal,
                TextAlignment.Leading, ParagraphAlignment.Center, Ae.Font);
        }
        else
        {
            float size = WAero(name, 11) <= r.Width - 6 ? 11 : WAero(name, 10) <= r.Width - 6 ? 10 : 0;
            bool both = size > 0 && r.Height >= 36;
            float iy = both ? MathF.Floor(r.Y + (r.Height - 31) / 2) : MathF.Floor(r.Y + (r.Height - 16) / 2);
            W95Icons.Draw(_p, icon, MathF.Floor(r.X + (r.Width - 16) / 2) + o, iy + o);
            if (both)
                _p.Text(name, new RectangleF(r.X + 1 + o, iy + 15 + o, r.Width - 2, 18), 0xFF1E2B3A, size, FontWeight.Normal, TextAlignment.Center,
                    ParagraphAlignment.Center, Ae.Font);
        }
        if (click) Act(k.Id);
    }

    // Windows Media Player: the cover big, the title and artist, the round buttons, the volume
    void MediaDetailAero(RectangleF c, PanelData d)
    {
        var (has, title, artist, playing) = MediaNow(d);
        Volume.Sample(Clock.Now);
        var art = new RectangleF(c.X + 1, c.Y + 1, 64, 64);
        ArtAero(art);
        float tx = art.Right + 10, tw = c.Right - tx;
        if (has)
        {
            _p.Text(Fit(title, tw, 15, true), new RectangleF(tx, c.Y, tw + 4, 22), InkAero, 15, FontWeight.Bold, TextAlignment.Leading,
                ParagraphAlignment.Center, Ae.Font);
            _p.Text(Fit(artist ?? "", tw, 12), new RectangleF(tx, c.Y + 20, tw + 4, 18), 0xFF5A5A5A, 12, FontWeight.Normal, TextAlignment.Leading,
                ParagraphAlignment.Center, Ae.Font);
        }
        else _p.Text("Nothing playing", new RectangleF(tx, c.Y, tw + 4, 22), 0xFF6D6D6D, 13, FontWeight.Normal, TextAlignment.Leading,
            ParagraphAlignment.Center, Ae.Font);
        float cy = c.Y + 52, play = tx + 40;
        MediaOrbsAero(play - 28, play, play + 28, cy, has, playing, "wd95m", 0);
        Volume95Row(new RectangleF(c.X, art.Bottom + 12, c.Width, 34));
    }
}
