using System;
using System.Drawing;
using System.Globalization;
using Vortice.DirectWrite;

namespace Orbit;

// The opened views in the Cyber style that are the HUD's own designs, not the Modern layout in Cyber's colours (Erik 10-16: the
// opened clock more cyberpunk). Made of the face's Cyber parts (WristCyber.cs, WristCyberFaces.cs), so they belong to the preset
// faces' and the custom face's family.
//  - The clock (time, date, analog, time in VR): the CLOCK tag with the week and the day of the year beside it; the yellow time card
//    (CyberTime's hero: TIME, the zone, the digits with their split and slice, SEC, the date tag, the hazard stripes) and the HUD
//    dial in its segmented bezel (the hour so far) beside it; under them plates with cut corners - DATE (the date card's lines), DAY
//    (how much of today has gone, a segment an hour - the hours in VR cyan - and what is left of it), IN VR (how long, since when)
//    and TIMER (what is left, its segments) while they are on.
//  - Now playing: the cover in its dark-red box (the cyan equalizer without one), the title in the condensed capitals, the artist
//    and where in the track in monospace over the yellow line, the transport, the volume as segments.
//  - The frame numbers: the four readings as the face's 1x1 cards on plates, the frames under them as its FRAMES card, the last
//    frames' average, worst and dropped as readouts. The CPU and the memory: the face's wide cards, the last minute of CPU load.
//  - Every opened view's slider (the volume's): segments with a cyan mark, the mixer's.
// Motion as on the face: the time card glitches on a new minute (Full); at rest only the seconds it shows draw it again.
public sealed partial class WristPanel
{
    // c: the opened card's inside (DrawDetail's; its tag and X are over it)
    void CyberClockDetail(RectangleF c, PanelData d, DateTime now)
    {
        var E = new RectangleF(c.X - 24, c.Y - 74, c.Width + 48, c.Height + 96);
        var culture = CultureInfo.CurrentCulture;
        // beside the CLOCK tag: the week, the day of the year
        float nx = E.X + 24 + CyberHud.TagWidth(_p, Upper("Clock"), 18) + 14, nr = E.Right - 22 - 46 - 14;
        string wk = Lang.F("Week {0} · day {1} of {2}", ISOWeek.GetWeekOfYear(now), now.DayOfYear, DateTime.IsLeapYear(now.Year) ? 366 : 365).ToUpper(culture);
        if (CyMonoW(wk) <= nr - nx) CyMono(wk, nx, E.Y + 23 + (26 + CyberHud.CapH(_p, CyNote)) / 2, nr - nx, CyG);
        // the time card and the dial
        const float topH = 194;
        var hero = new RectangleF(c.X, c.Y, MathF.Min(350, c.Width - topH - 16), topH);
        CyberTimeCard(hero);
        CyberTime(hero, _open, now, true);
        var box = new RectangleF(hero.Right + MathF.Max(16, (c.Right - hero.Right - topH) / 2), c.Y, topH, topH);
        float ra = topH / 2, bw = MathF.Max(4, MathF.Round(ra * 0.065f)), aw = MathF.Max(2, MathF.Round(ra * 0.03f * 2) / 2);
        CfBezel(box.X + ra, box.Y + ra, ra - 0.5f - bw, ra - 0.5f, now);
        CyberDialFace(box.X + ra, box.Y + ra, (ra - bw - 5 - aw / 2) / 1.07f, now, aw);
        CyberHud.Brackets(_p, RectangleF.Inflate(box, 6, 0), CyY, 12, 0, 2);
        // the plates: DATE, DAY, and IN VR / TIMER while they are on
        bool vr = d.VrSince != null, timing = _timer != TimerState.Idle;
        int n = 2 + (vr ? 1 : 0) + (timing ? 1 : 0), k = 0;
        float py = c.Y + topH + 14, gap = 12, pw = (c.Width - (n - 1) * gap) / n;
        RectangleF Next() => new(MathF.Round(c.X + k++ * (pw + gap)), py, MathF.Round(pw), c.Bottom - py);
        var dp = Next();
        CyPlate(dp, "Date", null, 0);
        CyDateBody(dp.X + 14, dp.Right - 14, dp.Y + 36, dp.Bottom - 14, dp.Width < 160, now);
        CyDayPlate(Next(), d, now, culture);
        if (vr) CyVrPlate(Next(), d.VrSince.Value, culture);
        if (timing) CyTimerPlate(Next());
    }

    // a plate under the clock: the raised fill with its corners cut, its tag at the top-left, a note at the top-right (monospace;
    // shortNote: its shorter form, where the whole does not fit)
    void CyPlate(RectangleF p, string tag, string note, uint noteC, string shortNote = null)
    {
        CyberHud.CardShape(_p, p, CyberHud.Card2, CyberHud.Edge);
        float tw = CyberHud.Tag(_p, p.X + CyTagX, p.Y + CyTagY, Upper(tag));
        if (note == null) return;
        float left = p.X + CyTagX + tw + 10, right = p.Right - 12;
        if (right - left < 30) return;
        string s = CyMonoW(note) <= right - left ? note : shortNote != null && CyMonoW(shortNote) <= right - left ? shortNote
            : Ellipsize(note, right - left, CyNote, FontWeight.SemiBold, CyberHud.Mono);
        CyMono(s, right, p.Y + CyBase, right - left, noteC, CyNote, FontWeight.SemiBold, TextAlignment.Trailing);
    }

    // DAY: how much of today has gone, big; a segment an hour under it (two a segment on a narrow plate) - yellow, the hours in VR
    // cyan; what is left of it as the note
    void CyDayPlate(RectangleF p, PanelData d, DateTime now, CultureInfo culture)
    {
        var left = TimeSpan.FromDays(1) - now.TimeOfDay;
        CyPlate(p, "Day", Lang.F("{0} left", $"{(int)left.TotalHours}:{left.Minutes:00}").ToUpper(culture), CyG);
        float f = (float)(now.TimeOfDay.TotalHours / 24);
        float x = p.X + 14, w = p.Width - 28;
        string pct = Pct((int)(f * 100));
        CyValue(x, p.Bottom - 30, pct, CyFit(pct, w, 34), CyW);
        var bar = new RectangleF(x, p.Bottom - 18, w, 6);
        int segs = p.Width >= 170 ? 24 : 12;
        if (d.VrSince is { } since && since < now)
        {
            float s0 = since.Date == now.Date ? (float)(since.TimeOfDay.TotalHours / 24) : 0;
            CyberHud.Segments(_p, bar, f, CyC, CyberHud.Track, segs, 2);
            if (s0 > 0) CyberHud.Segments(_p, bar, s0, CyY, 0, segs, 2);
        }
        else CyberHud.Segments(_p, bar, f, CyY, CyberHud.Track, segs, 2);
    }

    // IN VR: how long (to the second: the seconds the view draws anyway), since when as the note
    void CyVrPlate(RectangleF p, DateTime since, CultureInfo culture)
    {
        string at = ShortClock(since);
        CyPlate(p, "In VR", Lang.F("since {0}", at).ToUpper(culture), CyG, at.ToUpper(culture));
        var span = Now - since;
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        string v = $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}";
        CyValue(p.X + 14, p.Bottom - 30, v, CyFit(v, p.Width - 28, 34), CyC);
    }

    // TIMER: what is left (DONE in red once it is up), its segments; running / paused as the note
    void CyTimerPlate(RectangleF p)
    {
        double left = TimerLeft(Clock.Now);
        bool done = _timer == TimerState.Done;
        string note = _timer switch { TimerState.Running => "running", TimerState.Paused => "paused", _ => "time is up" };
        CyPlate(p, "Timer", Upper(note), done ? CyR : _timer == TimerState.Running ? CyC : CyG);
        string v = done ? Upper("Done") : Clock2(left);
        uint c = done ? CyWarn() : _timer == TimerState.Paused ? CyG : CyY;
        CyValue(p.X + 14, p.Bottom - 30, v, CyFit(v, p.Width - 28, 34), c);
        float frac = done ? 0 : (float)(left / Math.Max(1, Math.Max(_timerTotal, left)));
        CyberHud.Segments(_p, new RectangleF(p.X + 14, p.Bottom - 18, p.Width - 28, 6), frac, c, CyberHud.Track, 10);
    }

    // ---- now playing ----

    void CyberMediaDetail(RectangleF c, PanelData d)
    {
        var (has, title, artist, playing) = MediaNow(d);
        var (tok, pos, dur) = Timeline(d);
        tok &= has && dur > 0;
        Volume.Sample(Clock.Now);
        var culture = CultureInfo.CurrentCulture;
        float s = MathF.Min(c.Height, c.Width * 0.44f);
        var cover = new RectangleF(c.X, c.Y + (c.Height - s) / 2, s, s);
        CyCover(cover);
        CyberHud.Brackets(_p, RectangleF.Inflate(cover, 5, 5), CyY, 14, 0, 2);
        float x = cover.Right + 30, w = c.Right - x;
        // the state, the title over two lines, the artist and the time in the track over its line
        CyMono(Upper(!has ? "Idle" : playing ? "Playing" : "paused"), x, c.Y + 12, w, has && playing ? CyC : CyG);
        float ty = c.Y + 50;
        if (!has) TextAt(Ellipsize(Upper(NotPlaying), w, 26, FontWeight.Bold), x, ty, w + 2, CyG, 26, FontWeight.Bold);
        else
        {
            var (l1, l2) = TwoLines(Upper(title ?? ""), w, 28, FontWeight.Bold);
            TextAt(l1, x, ty, w + 2, CyW, 28, FontWeight.Bold);
            if (l2.Length > 0) { ty += 32; TextAt(l2, x, ty, w + 2, CyW, 28, FontWeight.Bold); }
            CyMediaSub(x, ty + 26, w, artist, tok, pos, dur);
            if (tok) CyProgress(new RectangleF(x, ty + 40, w, 3), pos / dur);
        }
        // previous, the yellow play square, next
        float gap = 12, big = MathF.Min(64, (w - 2 * gap) * 0.4f), small = MathF.Min(52, (w - 2 * gap - big) / 2), by = c.Y + 146;
        if (Press("wdm0", new RectangleF(x, by + (big - small) / 2, small, small), "\uE892", null, false, has, 20, true)) Act(MediaIds[0]);
        if (Press("wdm1", new RectangleF(x + small + gap, by, big, big), playing ? PauseFill : PlayFill, null, true, has, 26, true)) Act(MediaIds[1]);
        if (Press("wdm2", new RectangleF(x + small + gap + big + gap, by + (big - small) / 2, small, small), "\uE893", null, false, has, 20, true))
            Act(MediaIds[2]);
        // the volume: its speaker (a tap mutes it, red), what it is, the segments
        float lvl = Volume.Level;
        bool ok = lvl >= 0, muted = Volume.Muted;
        float vy = c.Bottom - 56;
        if (Press("wdmv", new RectangleF(x, vy, 52, 52), muted ? MuteGlyph : "\uE767", null, muted, ok, 20, true, accentFill: false, fill: Bento.Bad))
            Volume.ToggleMute();
        var bar = new RectangleF(x + 72, vy + 19, w - 72 - 22, 14);
        string vt = ok ? (muted ? Upper("Muted") : Lang.F("Volume {0}", VolumePct(lvl)).ToUpper(culture)) : Upper("No output device");
        CyMono(Ellipsize(vt, bar.Width, CyNote, FontWeight.SemiBold, CyberHud.Mono), bar.X, vy + 4, bar.Width, ok && muted ? CyR : CyG);
        Slider("wdmb", bar, lvl, muted ? Bento.Faint : Bento.Accent, ok, Volume.Set);
    }

    // a slider of the opened views (the volume's): segments lit to the level, a cyan mark standing over them (the mixer's)
    void CySlider(RectangleF bar, float v, uint col, bool enabled, bool hot)
    {
        var b = new RectangleF(bar.X, bar.Y + bar.Height / 2 - 4, bar.Width, 8);
        CyberHud.Segments(_p, b, enabled ? v : 0, !enabled ? CyberHud.Faint : hot ? CyC : col, CyberHud.Track);
        if (enabled) _p.Fill(new RectangleF(MathF.Round(bar.X + bar.Width * v) - 1.5f, b.Y - 6, 3, 20), CyC);
    }

    // ---- the frame numbers ----

    void CyberPerfDetail(RectangleF c, PanelData d)
    {
        // the four readings: the face's 1x1 cards (the tag, the value with its unit, five segments) on plates
        const float gap = 10, ph = 108;
        float pw = (c.Width - 3 * gap) / 4;
        for (int j = 0; j < 4; j++)
        {
            var p = new RectangleF(MathF.Round(c.X + j * (pw + gap)), c.Y, MathF.Round(pw), ph);
            CyberHud.CardShape(_p, p, CyberHud.Card2, CyberHud.Edge);
            CyberStat(p, PerfIds[j], d, Ui.Id("wdp", j));
        }
        // the frames: the FRAMES card's bars and budget, the last frames' average, worst and dropped along its bottom
        var g = new RectangleF(c.X, c.Y + ph + 12, c.Width, c.Bottom - c.Y - ph - 12);
        CyberHud.CardShape(_p, g, CyberHud.Card2, CyberHud.Edge);
        CyberGraph(new RectangleF(g.X, g.Y, g.Width, MathF.Min(148, g.Height - 50)), _open, d);
        var v = d.Vr;
        int n = v.FrameCount, dropped = 0;
        float sum = 0, worst = 0;
        for (int i = 0; i < n; i++)
        {
            int idx = (v.FrameHead - n + i + VrStats.FrameHistory) % VrStats.FrameHistory;
            float ms = v.FrameMs[idx];
            sum += ms;
            worst = MathF.Max(worst, ms);
            if (v.FrameFlag[idx] == 2) dropped++;
        }
        float budget = 1000f / Math.Max(1, v.DisplayHz), x = g.X + 14, col = (g.Width - 28) / 3, vb = g.Bottom - 30, lb = g.Bottom - 12;
        void Col(float cx, string val, string unit, string label, bool warn)
        {
            float vw = CyValue(cx, vb, val, MathF.Min(30, CyFit(val, col - 50, 30)), n == 0 ? CyberHud.Faint : warn ? CyWarn() : CyW);
            if (unit != null && n > 0) CyMono(unit, cx + vw + 4, vb, 40, CyG);
            CyMono(Upper(label), cx, lb, col - 6, CyG);
        }
        Col(x, n > 0 ? (sum / n).ToString("0.0", CultureInfo.CurrentCulture) : "–", "MS", "Average", n > 0 && sum / n > budget);
        Col(x + col, n > 0 ? worst.ToString("0.0", CultureInfo.CurrentCulture) : "–", "MS", "Worst", worst > budget * 1.5f);
        Col(x + 2 * col, n > 0 ? dropped.ToString(CultureInfo.CurrentCulture) : "–", null, "Dropped", dropped > 0);
    }

    // ---- the CPU and the memory ----

    void CyberSystemDetail(RectangleF c, PanelData d)
    {
        // the two readings as the face's wide cards (the tag, its note, the value, ten segments) on plates
        const float gap = 12, ph = 108;
        float pw = (c.Width - gap) / 2;
        var a = new RectangleF(c.X, c.Y, MathF.Round(pw), ph);
        var r = new RectangleF(MathF.Round(c.X + pw + gap), c.Y, MathF.Round(pw), ph);
        CyberHud.CardShape(_p, a, CyberHud.Card2, CyberHud.Edge);
        CyberWide(a, "cpu", d, "wdsc");
        CyberHud.CardShape(_p, r, CyberHud.Card2, CyberHud.Edge);
        CyberWide(r, "ram", d, "wdsr");
        // the last minute of CPU load: yellow bars, red where it was high; its average and peak as the note
        float avg = 0, peak = 0;
        for (int i = 0; i < Stats.CpuCount; i++)
        {
            float v = Stats.CpuHistory[(Stats.CpuHead - Stats.CpuCount + i + SystemStats.History) % SystemStats.History];
            avg += v / Stats.CpuCount;
            peak = MathF.Max(peak, v);
        }
        var g = new RectangleF(c.X, c.Y + ph + 12, c.Width, c.Bottom - c.Y - ph - 12);
        var culture = CultureInfo.CurrentCulture;
        string note = Stats.CpuCount > 0 ? (Lang.F("avg {0:0}%", avg * 100) + " · " + Lang.F("peak {0:0}%", peak * 100)).ToUpper(culture) : null;
        CyPlate(g, "CPU, last minute", note, CyG);
        var b = RectangleF.FromLTRB(g.X + 14, g.Y + 40, g.Right - 14, g.Bottom - 14);
        _p.Fill(new RectangleF(b.X, b.Bottom, b.Width, 1), CyberHud.Line);
        float step = b.Width / SystemStats.History;
        for (int i = 0; i < Stats.CpuCount; i++)
        {
            int idx = (Stats.CpuHead - Stats.CpuCount + i + SystemStats.History) % SystemStats.History;
            float v = Stats.CpuHistory[idx], h = MathF.Max(2, v * b.Height);
            float bx = b.Right - (Stats.CpuCount - i) * step;
            _p.Fill(new RectangleF(bx + 1, b.Bottom - h, MathF.Max(2, step - 3), h), CpuLevel(v) == StatLevel.Ok ? CyY : CyR);
        }
    }

    // --snapshot: the timer as the TIMER plate shows it - started for that many minutes (0: back to idle)
    public void TimerForTest(int minutes)
    {
        if (minutes > 0) TimerPreset(minutes);
        else TimerReset();
    }
}
