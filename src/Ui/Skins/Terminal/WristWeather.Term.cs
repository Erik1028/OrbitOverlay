using System;
using System.Drawing;
using T = Orbit.TerminalSkin;

namespace Orbit;

// The weather on the terminal, the way `curl wttr.in` prints it: a little ASCII picture of the sky (13 x 5 characters, drawn for
// Orbit - a sun, a moon, clouds, fog, drizzle, rain, snow, a storm) with the conditions beside it, the temperature with feels like in
// brackets, the wind with its arrow, then the hours and the days as a table. The widget: the box's own lines (the temperature big on a
// 1x1, the picture on a 2x2, the hours on a wide one); opened: the whole report after its command line.
public sealed partial class WristPanel
{
    // the pictures, a row each; the sun / moon rows bright, the rain under a cloud dim
    static readonly string[][] TSky =
    {
        new[] { "    \\ | /    ", "  '-.---.-'  ", " -- (   ) -- ", "  .-'---'-.  ", "    / | \\    " },        // clear
        new[] { "   \\ | /     ", "  -- .-.     ", "    (  .--.  ", "   .-(    ). ", "  (___.__)__)" },         // partly cloudy
        new[] { "             ", "     .--.    ", "  .-(    ).  ", " (___.__)__) ", "             " },         // cloudy
        new[] { "             ", " _ - _ - _ - ", "  _ - _ - _  ", " _ - _ - _ - ", "             " },         // fog
        new[] { "     .--.    ", "  .-(    ).  ", " (___.__)__) ", "   '  '  '   ", "  '  '  '    " },         // drizzle
        new[] { "     .--.    ", "  .-(    ).  ", " (___.__)__) ", "   / / / /   ", "  / / / /    " },         // rain
        new[] { "     .--.    ", "  .-(    ).  ", " (___.__)__) ", "   *  *  *   ", "  *  *  *    " },         // snow
        new[] { "     .--.    ", "  .-(    ).  ", " (___.__)__) ", "   / ⚡ / ⚡   ", "  ⚡ / ⚡ /    " },   // storm
    };
    static readonly string[] TMoon = { "     _.._    ", "   .' .-'  * ", "  /  /       ", "  \\  '-._  . ", "   '.___.'   " };
    static readonly string[] TMoonCloud = { "    _.  *    ", "  .' /       ", "  | (  .--.  ", "   '.(    ). ", "  (___.__)__)" };

    static string[] TSkyOf(WxKind k, bool night) => night && k == WxKind.Clear ? TMoon : night && k == WxKind.Partly ? TMoonCloud : TSky[(int)k];

    // a row's ink: the sun's and the moon's rows bright, rain / snow / bolts under the cloud dim (bolts bright)
    static uint TSkyInk(WxKind k, int row) => k switch
    {
        WxKind.Clear => T.HOT,
        WxKind.Partly => row < 2 ? T.HOT : T.INK,
        WxKind.Storm => row >= 3 ? T.HOT : T.INK,
        WxKind.Drizzle or WxKind.Rain or WxKind.Snow => row >= 3 ? T.MID : T.INK,
        WxKind.Fog => T.MID,
        _ => T.INK,
    };

    // TFit, kept with the weather's other texts (a cut made every frame was garbage every frame)
    string WxTFit(string s, float w, int n = 1)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var key = (s, w, (float)n, -3);
        if (_wxFits.TryGetValue(key, out var done)) return done;
        if (_wxFits.Count > 256) _wxFits.Clear();
        return _wxFits[key] = TFit(s, w, n);
    }

    void TSkyArt(WxKind k, bool night, float x, float y, int rows = 5)
    {
        var art = TSkyOf(k, night);
        for (int i = 0; i < rows && i < art.Length; i++) TPut(art[i], x, y + i * 11, TSkyInk(k, i));
    }

    // c: the box's inside, under its WEATHER line
    void TWeather(RectangleF c, PanelData d, WatchWidget w)
    {
        var t = Wxt(d);
        if (!t.Has)
        {
            TCapsC(WxTFit(Upper(c.Width >= 90 ? t.Msg : t.Short), c.Width - 4), c, MathF.Floor(c.Y + (c.Height - 7) / 2), T.DIM);
            return;
        }
        uint ink = t.Stale ? T.DIM : T.INK;
        string cond = Upper(t.Cond);
        if (c.Width < 70)
        {
            // a 1x1: the temperature big, the conditions under it
            TValue(c, t.Temp, WxTFit(cond, c.Width - 2), ink);
            return;
        }
        if (c.Height >= 70 && c.Width >= 80)
        {
            // the picture, the temperature and the conditions under it (a big one: the details beside the picture)
            TSkyArt(t.Kind, t.Night, c.X + 1, c.Y);
            float y = c.Y + 58;
            TCaps(WxTFit(t.TTempCond, c.Width - 4), c.X + 1, y, ink);
            if (y + 22 <= c.Bottom) TCaps(WxTFit(t.THiWind, c.Width - 4), c.X + 1, y + 11, T.MID);
            if (c.Width >= 150)
            {
                float dx = c.X + 6 * 14 + 4, dw = c.Right - dx;
                TCaps(WxTFit(t.FeelsLine, dw), dx, c.Y + 2, T.MID);
                TCaps(WxTFit(t.TWind, dw), dx, c.Y + 13, T.MID);
                TCaps(WxTFit(t.TUvHum, dw), dx, c.Y + 24, T.MID);
            }
            return;
        }
        // one row: the temperature big, the conditions and the day's highest / lowest beside it (a wide one: the next hours)
        int n = c.Height >= 30 ? 2 : 1;
        float top = MathF.Floor(c.Y + (c.Height - 29) / 2);
        TBig(t.TempU, c.X + 2, top + 2, n, ink);
        float rx = c.X + 2 + T.W(t.TempU, n) + 8;
        float rw = (c.Width >= 150 ? MathF.Min(c.Right, c.X + 112) : c.Right) - rx;
        TCaps(WxTFit(cond, rw - 2), rx, top, T.INK);
        TCaps(WxTFit(t.HiLoSlash, rw - 2), rx, top + 11, T.MID);
        TCaps(WxTFit(t.TWind, c.Right - c.X - 4), c.X + 2, top + 22, T.DIM);
        if (c.Width < 150) return;
        float hx = c.X + 116, cw = 28;
        int hn = Math.Min(t.HourN, (int)((c.Right - hx) / cw));
        for (int i = 0; i < hn; i++)
        {
            float x = hx + i * cw;
            TCaps(WxTFit(Upper(t.HourShort[i]), cw - 4), x, top, i == 0 ? T.HOT : T.DIM);
            TCaps(t.HourT[i], x, top + 11, ink);
        }
    }

    // Opened: a terminal window with the whole report - "$ curl wttr.in/<place>", "Weather report: <place>", the picture with now beside
    // it, the next 12 hours (two lines: the hours, the temperatures), the 6 days in a table
    void TWeatherDetail(RectangleF win, PanelData d)
    {
        var t = Wxt(d);
        _p.Fill(win, T.BG);
        _p.Bevel(win, Painter.Edge.Window);
        _ui.Caption95(new RectangleF(win.X + 3, win.Y + 3, win.Width - 6, 18), "Weather");
        if (_ui.CaptionClose95("wd95x", win.Right - 5 - 16, win.Y + 5, true, 6)) { CloseDetail(); return; }
        var c = new RectangleF(win.X + 8, win.Y + 27, win.Width - 16, win.Height - 27 - 8);
        float y = c.Y;
        TPut(WxTFit(t.Has ? t.TCurl : "$ curl wttr.in", c.Width), c.X, y, T.MID);
        y += 11;
        if (!t.Has)
        {
            TPut(WxTFit(Upper(t.Msg), c.Width), c.X, y + 4, T.HOT);
            if (!string.IsNullOrEmpty(t.Hint)) TPut(WxTFit(t.Hint, c.Width), c.X, y + 15, T.DIM);
            TPut("$ _", c.X, y + 30, T.INK);
            return;
        }
        uint ink = t.Stale ? T.DIM : T.INK;
        TPut(WxTFit(t.TReport, c.Width), c.X, y, T.INK);
        if (t.Stale) TPut(WxTFit(t.Updated, c.Width), c.X, y + 11, T.HOT);
        y += 24;
        // the picture, now beside it
        TSkyArt(t.Kind, t.Night, c.X, y);
        float nx = c.X + 6 * 15, nw = c.Right - nx;
        TPut(WxTFit(t.Cond, nw), nx, y, T.HOT);
        TPut(WxTFit(t.TNow, nw), nx, y + 11, ink);
        TPut(WxTFit(t.TWind, nw), nx, y + 22, ink);
        TPut(WxTFit(t.TUvHumRain, nw), nx, y + 33, ink);
        TPut(WxTFit(t.TSun, nw), nx, y + 44, T.MID);
        y += 60;
        // the hours: two lines, a column of four characters each; a first word wider than its column (Magyar's "Most", cut to "Mo…")
        // takes what it needs from the others - the hours are two characters
        float hcw = MathF.Floor(c.Width / WxHours), lead = t.HourN > 0 ? MathF.Max(0, T.W(t.HourShort[0]) + 4 - hcw) : 0;
        if (lead > 0) hcw = MathF.Floor((c.Width - lead) / WxHours);
        for (int i = 0; i < t.HourN; i++)
        {
            float x = c.X + (i == 0 ? 0 : lead) + i * hcw;
            TPut(WxTFit(t.HourShort[i], i == 0 ? hcw + lead - 2 : hcw - 2), x, y, i == 0 ? T.HOT : T.DIM);
            TPut(WxTFit(t.HourT[i], hcw - 2), x, y + 11, ink);
        }
        y += 26;
        // the days: a table with lines round it and between them
        float tw = c.Width, th = c.Bottom - y, dcw = MathF.Floor(tw / WxDays);
        var box = new RectangleF(c.X, y, dcw * WxDays, th);
        T.Box(_p, box, T.FRAME);
        for (int i = 1; i < WxDays; i++) _p.Fill(new RectangleF(box.X + i * dcw, box.Y + 1, 1, box.Height - 2), T.FRAME);
        for (int i = 0; i < t.DayN; i++)
        {
            float x = box.X + i * dcw + 4, w = dcw - 7;
            TPut(WxTFit(Upper(t.DayName[i]), w), x, y + 3, i == 0 ? T.HOT : T.INK);
            TPut(WxTFit(t.DayWord[i], w), x, y + 14, T.MID);
            TPut(WxTFit(t.DayHiLo[i], w), x, y + 25, ink);
        }
    }
}
