using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using T = Orbit.TerminalSkin;

namespace Orbit;

// SlimeVR on the terminal (the data and the shared words: WristSlimeVr.cs): a status tool's output. The box (SLIMEVR on its top line)
// says how many are up and the lowest battery; one row high, a bar per tracker drawn in the tube's ink with its two letters; taller, a
// line each - "LT [#####---] 41%". Opened, `slimevr status`: TRACKER, BATT, RSSI, PING, STATUS in columns, the low and the troubled
// bright; under it the three resets as F-keys - [Y] YAW, [F] FULL, [M] MOUNT - a first tap lights it "SURE?", the second sends it, the
// server's countdown on the prompt line. Nothing made a frame at rest: the lines are made with the snapshot, the bars' runs once.
// Lying down (WristSlimeVr.cs): the box's first line a lit key, "LYING? [Y] YAW"; opened, the [Y] YAW key lit with LYING? under it and
// the hint on the prompt line.
// The Body side (Erik 10-24; WristSlimeBody.cs): [F1] TRACKERS / [F2] BODY keys on the command's line; `slimevr pose --live` draws the
// figure as thin vector lines in the tube's ink (faint where no tracker is, bright for trouble) and the legs in columns - BONE, TWIST,
// SIDE ("L THIGH  +32 OUT  6", bright from 15°); keys for the groups ([A] ALL, [L] LEGS, [<] LEFT, [>] RIGHT, [U] UPPER - lit when
// picked) over [Y] YAW and [F] FULL. The guide: `slimevr mount --step` (or --pose) with a stick figure of characters, the phase lines
// and a [######----] bar. The prompt at rest: "> YAW RESET 12 MIN AGO". The tracker list has a LEFT column (its battery's time left).
public sealed partial class WristPanel
{
    // the box's first line ("5/6 UP  LOW 23%", shorter "5/6 23%"); the opened view's prompt line at rest; lying: the key's words
    string _svT1 = "", _svT1Short = "", _svTPrompt = "", _svTLieKey = "", _svTLie = "";
    static readonly string[] SvTKeys = { "[Y] YAW", "[F] FULL", "[M] MOUNT" };
    static readonly string[] _hashes = Runs('#'), _dashes = Runs('-');

    static string[] Runs(char ch)
    {
        var a = new string[61];
        for (int i = 0; i < a.Length; i++) a[i] = new string(ch, i);
        return a;
    }

    // made with the rows (SvNow)
    void SvTermTexts(CultureInfo culture)
    {
        int n = _svRows.Count;
        _svT1 = n == 0 ? "" : (_svLow >= 0 ? Lang.F("{0} up  low {1}", _svCount, _svRows[_svLow].Pct) : Lang.F("{0} up", _svCount)).ToUpper(culture);
        _svT1Short = n == 0 ? "" : _svLow >= 0 ? _svCount + " " + _svRows[_svLow].Pct : _svCount;
        _svTPrompt = "> " + (n > 0 ? _svOfN : Lang.T(_svMsg ?? "No trackers")).ToUpper(culture);
        _svTLieKey = Lang.T("LYING?");
        _svTLie = _svTLieKey + " " + Lang.T(SvTKeys[0]);
    }

    // a text cut to w in the terminal's font, kept with the other cut texts
    string SvTFit(string s, float w, int n = 1)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var key = (s, w, (float)n, Vortice.DirectWrite.FontWeight.Normal, "term");
        if (_svFits.TryGetValue(key, out var done)) return done;
        if (_svFits.Count > 512) _svFits.Clear();
        return _svFits[key] = TFit(s, w, n);
    }

    // [#####-----] with 'cells' cells, its runs made once (TBar makes them a frame); returns its width
    float SvTBar(float x, float capTop, int cells, float frac, bool warn)
    {
        cells = Math.Clamp(cells, 1, 60);
        int on = frac < 0 ? 0 : (int)MathF.Round(Math.Clamp(frac, 0, 1) * cells);
        if (on == 0 && frac >= 0.01f) on = 1;
        float x0 = x;
        TCaps("[", x, capTop, T.DIM); x += 6;
        if (on > 0) { TCaps(_hashes[on], x, capTop, warn ? T.HOT : T.INK); x += on * 6; }
        if (cells > on) { TCaps(_dashes[cells - on], x, capTop, T.DIM); x += (cells - on) * 6; }
        TCaps("]", x, capTop, T.DIM);
        return x + 5 - x0;
    }

    // c: the box's inside, under its SLIMEVR line
    void TSlime(RectangleF c, WatchWidget w, PanelData d, int id = 0)
    {
        SvNow(d);
        int n = _svRows.Count;
        if (w.W == 1 && w.H == 1 || c.Width < 70)
        {
            var e = _svLow >= 0 ? _svRows[_svLow] : null;
            bool lie1 = SvLying(d);                 // lying: YAW? under the value
            if (lie1) _svHints++;
            TValue(c, e != null ? e.Pct : n > 0 ? _svCount : "--", lie1 ? _svCyYaw : T.Up(Lang.T(e != null ? _svCount : n > 0 ? _svOnNote[1] : _svNote[1])),
                e != null && e.Lv != StatLevel.Ok ? T.HOT : n > 0 ? T.INK : T.DIM);
            return;
        }
        if (n == 0)
        {
            // why (a narrow box: "SLIMEVR" over "NOT RUNNING"), what would help under it
            string msg = T.Up(Lang.T(_svMsg ?? "No trackers")), hint = c.Height >= 40 && !string.IsNullOrEmpty(_svHint) ? T.Up(Lang.T(_svHint)) : null;
            if (T.W(msg) > c.Width - 6) (msg, hint) = ("SLIMEVR", T.Up(Lang.T(_svNote[0])));
            float my = MathF.Floor(c.Y + (c.Height - (hint != null ? 18 : 7)) / 2);
            TCaps(SvTFit(msg, c.Width - 6), c.X + 3, my, T.DIM);
            if (hint != null) TCaps(SvTFit(hint, c.Width - 6), c.X + 3, my + 11, T.FAINT);
            return;
        }
        if (SvLying(d))
        {
            // lying: the Yaw reset first - a lit key over the lines ("SURE?" after a first tap, then the server's countdown)
            _svHints++;
            var (label, asking, counting) = SvButton(0, d, true);
            string key = asking ? Lang.T("SURE?") : !counting && label == Lang.T(SvResetNames95[0]) ? _svTLie : T.Up(label);
            if (TKey(Ui.Id("wsvl", id), new RectangleF(c.X + 2, c.Y + 1, c.Width - 4, 13), SvTFit(key, c.Width - 8), true, SvCan(d))) SvResetTap(0, d, view: false);
            c = new RectangleF(c.X, c.Y + 16, c.Width, c.Height - 16);
            if (c.Height < 24) return;
        }
        if (c.Height < 40)
        {
            // the count and the lowest; under it a bar per tracker (the beam's ink, bright when low) over its two letters
            float top = c.Y + 2;
            TCaps(SvTFit(T.W(_svT1) <= c.Width - 6 ? _svT1 : _svT1Short, c.Width - 6), c.X + 3, top, T.INK);
            int fit = Math.Max(1, (int)((c.Width - 6) / 14));
            var pick = SvPick(Math.Min(fit, 12));
            int m = pick.Count;
            float pitch = (c.Width - 6) / m, by = top + 11, bh = MathF.Max(4, c.Bottom - 9 - by - 2);
            for (int j = 0; j < m; j++)
            {
                var e = _svRows[pick[j]];
                float cx = c.X + 3 + pitch * (j + 0.5f), bx = MathF.Floor(cx - 2);
                _p.Fill(new RectangleF(bx, by, 4, bh), T.FAINT);
                float h = !e.On || e.Frac < 0 ? 0 : MathF.Max(1, MathF.Round(bh * e.Frac));
                if (h > 0) _p.Fill(new RectangleF(bx, by + bh - h, 4, h), e.Lv == StatLevel.Ok ? T.INK : T.HOT);
                string s = T.Up(e.Short);
                if (pitch >= 13) TCaps(s, MathF.Floor(cx - T.W(s) / 2), c.Bottom - 8, e.On ? T.MID : T.DIM);
            }
            return;
        }
        // taller: a line each, as many as fit (the emptiest first when not all do)
        var all = SvPick(Math.Min(12, (int)((c.Height + 4) / 11)));
        float rtop = c.Y + MathF.Floor((c.Height - (all.Count * 11 - 4)) / 2);
        int cells = (int)((c.Width - 6 - 12 - 6 - 24 - 6) / 6) - 2;
        for (int i = 0; i < all.Count; i++)
        {
            var e = _svRows[all[i]];
            float y = rtop + i * 11;
            bool low = e.On && e.Battery >= 0 && e.Lv != StatLevel.Ok;
            TCaps(T.Up(e.Short), c.X + 3, y, e.On ? T.MID : T.DIM);
            SvTBar(c.X + 3 + 18, y, cells, e.On ? e.Frac : -1, low);
            TCapsR(T.Up(e.Value), c.Right - 3, y, !e.On ? T.DIM : low ? T.HOT : T.INK);
        }
    }

    // Opened: a terminal window - `slimevr status` in columns, the prompt line, the three resets as keys; the stick scrolls the lines
    int _svTop;
    void TSlimeDetail(RectangleF win, PanelData d)
    {
        SvNow(d);
        _svBodyDrawn = false;                  // (TSlimeBody sets it again)
        _p.Fill(win, T.BG);
        _p.Bevel(win, Painter.Edge.Window);
        _ui.Caption95(new RectangleF(win.X + 3, win.Y + 3, win.Width - 6, 18), "SlimeVR");
        if (_ui.CaptionClose95("wd95x", win.Right - 5 - 16, win.Y + 5, true, 6)) { CloseDetail(); return; }
        var c = new RectangleF(win.X + 8, win.Y + 27, win.Width - 16, win.Height - 27 - 8);
        int n = _svRows.Count;
        var guide = SvGuideNow(d, out int gk);
        bool body = _svBody && n > 0 && guide == Guide.None;
        // [F1] TRACKERS / [F2] BODY at the right of the command's line (the command cut short where they leave it no room)
        float cmdW = c.Width;
        if (n > 0)
        {
            string k2 = SvTFit(Lang.T("[F2] BODY"), 90), k1 = SvTFit(Lang.T("[F1] TRACKERS"), 100);
            float w2 = T.W(k2) + 8, w1 = T.W(k1) + 8;
            if (TKey("wsvtab1", new RectangleF(c.Right - w2, c.Y - 3, w2, 13), k2, _svBody)) SvSetBody(true);
            if (TKey("wsvtab0", new RectangleF(c.Right - w2 - 4 - w1, c.Y - 3, w1, 13), k1, !_svBody)) SvSetBody(false);
            cmdW = c.Width - w2 - 4 - w1 - 6;
        }
        TPut(SvTFit(guide != Guide.None ? SvTGuideCmd(d, guide) : body ? "$ slimevr pose --live" : "$ slimevr status", cmdW), c.X, c.Y, T.MID);
        float y0 = c.Y + 13, keyY = c.Bottom - 22, promptY = keyY - 13;
        if (guide != Guide.None) TSvGuide(new RectangleF(c.X, y0, c.Width, promptY - 2 - y0), d, guide, gk);
        else if (body) { TSlimeBody(c, d); return; }
        else if (n == 0)
        {
            TPut(SvTFit(T.Up(Lang.T(_svMsg ?? "No trackers")), c.Width), c.X, y0 + 4, T.HOT);
            if (!string.IsNullOrEmpty(_svHint)) TPut(SvTFit(Lang.T(_svHint), c.Width), c.X, y0 + 15, T.DIM);
        }
        else
        {
            // the columns: TRACKER, BATT (a + after it while charging), LEFT (its time left), RSSI (dBm), PING, STATUS
            float bx = c.X + MathF.Floor(c.Width * 0.3f), lx = bx + 40, rx = lx + 32, px = rx + 34, sx = px + 40;
            TPut(T.Up(Lang.T("Tracker")), c.X, y0, T.DIM);
            TPut("BATT", bx, y0, T.DIM);
            TPut(Lang.C("slimevr", "LEFT"), lx, y0, T.DIM);
            TPut("RSSI", rx, y0, T.DIM);
            TPut("PING", px, y0, T.DIM);
            TPut(T.Up(Lang.T("Status")), sx, y0, T.DIM);
            float ly = y0 + 12;
            int rows = Math.Max(1, (int)((promptY - 2 - ly) / 11)), maxTop = Math.Max(0, n - rows);
            if (_ui.Wheel != 0 && maxTop > 0 && _ui.OverNow(c)) { _svTop -= Math.Sign(_ui.Wheel); _dirty = true; }
            _svTop = Math.Clamp(_svTop, 0, maxTop);
            for (int i = _svTop; i < Math.Min(n, _svTop + rows); i++)
            {
                var e = _svRows[i];
                float y = ly + (i - _svTop) * 11;
                bool low = e.On && e.Battery >= 0 && e.Lv != StatLevel.Ok;
                uint ink = e.On ? T.INK : T.DIM;
                TPut(SvTFit(e.NameUp, bx - c.X - 6), c.X, y, ink);
                string v = T.Up(e.Value);
                TPut(v, bx, y, !e.On ? T.DIM : low ? T.HOT : T.INK);
                if (e.Charging && e.On) TPut("+", bx + T.W(v) + 1, y, T.HOT);
                TPut(e.LeftShort.Length > 0 ? SvTFit(T.Up(e.LeftShort), rx - lx - 4) : "-", lx, y, e.LeftShort.Length > 0 ? ink : T.DIM);
                TPut(e.SigFrac >= 0 ? e.Rssi : "-", rx, y, e.SigLv == StatLevel.Ok ? ink : T.HOT);
                TPut(e.Ping.Length > 0 ? SvTFit(T.Up(e.Ping), sx - px - 4) : "-", px, y, T.MID);
                TPut(SvTFit(T.Up(e.Status), c.Right - sx), sx, y, e.StLv == StatLevel.Ok ? ink : T.HOT);
            }
            if (maxTop > 0)
            {
                float h = rows * 11, th = MathF.Max(8, MathF.Round(h * rows / n)), ty = MathF.Round(ly + (h - th) * _svTop / maxTop);
                _p.Fill(new RectangleF(c.Right - 3, ly, 2, h), T.FAINT);
                _p.Fill(new RectangleF(c.Right - 4, ty, 4, th), T.INK);
            }
        }
        // the prompt: what is asked or counted, else (lying) the hint, else the last reset, else how many are up
        bool lie = SvLying(d);
        if (lie) _svHints++;
        TPut(SvTFit(SvTPromptNow(d, lie), c.Width), c.X, promptY, SvArmed95() || lie ? T.HOT : T.INK);
        // the keys: [Y] YAW, [F] FULL, [M] MOUNT - "SURE?" while one asks, the countdown while the server counts; lying, [Y] YAW lit
        // with LYING? under it
        bool can = SvCan(d);
        float kw = MathF.Floor((c.Width - 8) / 3);
        for (int k = 0; k < 3; k++)
        {
            var (label, asking, counting) = SvButton(k, d, true);
            bool hi = ((k == 0 && lie) || SvGuideLit(k, d)) && !asking && !counting;
            string line2 = asking ? Lang.T("SURE?") : counting ? T.Up(label) : hi ? (k == 0 && lie ? _svTLieKey : Lang.T("NOW")) : null;
            if (TKey(SvIds[k], new RectangleF(c.X + k * (kw + 4), keyY, kw, 22), SvTKeys[k], asking || counting || hi, can, line2)) SvResetTap(k, d);
        }
    }

    // the prompt's words now: what is asked or counted (the status line's), lying's hint, the last reset, how many are up
    string SvTPromptNow(PanelData d, bool lie)
    {
        bool counting = d.SlimeVr is { ResetState: 0 } s && s.ResetKind >= 0 && (s.ResetLeftMs / 1000.0 - (Clock.Now - s.ResetAt) > 0 || (s.ResetKind == 2 && SvStep(d)));
        if (SvArmed95() || counting) return SvTPrompt(SvStatus95(d)[0]);
        if (lie) return SvTPrompt(Lang.T(LieHint));
        return _svRows.Count > 0 && SvLast(d) is { } last ? SvTPrompt(last) : _svTPrompt;
    }

    // the guide's command line
    string SvTGuideCmd(PanelData d, Guide g) => g == Guide.FullFirst ? "$ slimevr reset --full" : SvStep(d) ? "$ slimevr mount --step" : "$ slimevr mount --pose";

    // the stick figures of characters: standing straight, the step (an arrow ahead), the ski pose, on its toes
    static readonly string[][] SvTFigs =
    {
        new[] { "  O  ", " /|\\ ", "  |  ", " / \\ ", " | | " },
        new[] { "  O     ", " /|\\ -->", "  |     ", " / \\    ", "/   \\   " },
        new[] { "   O  ", "  /|_ ", "  \\   ", "  /\\  ", " /  \\ " },
        new[] { "   O  ", "  /|_ ", "  \\   ", "  /\\  ", "  ' ' " },
    };

    // The guide on the tube: the figure at the left; the title, the line, the big word (the phase, the countdown), a [####----] bar, the
    // note; [C] CANCEL (v22) while a mounting reset runs
    void TSvGuide(RectangleF G, PanelData d, Guide g, int kind)
    {
        var (title, line, big, lv, note, frac) = SvGuideText(d, g, kind);
        var fig = SvTFigs[SvGuideFigure(d, g)];
        for (int i = 0; i < fig.Length; i++) TPut(fig[i], G.X + 2, G.Y + 4 + i * 11, lv == 2 ? T.HOT : T.INK);
        float x = G.X + 62, w = G.Right - x;
        TPut(SvTFit(T.Up(Lang.T(title)), w), x, G.Y + 4, T.DIM);
        TPut(SvTFit(T.Up(Lang.T(line)), w), x, G.Y + 15, lv == 2 && big == null ? T.HOT : T.INK);
        float y = G.Y + 30;
        if (big != null)
        {
            string b = T.Up(Lang.T(big));
            if (b.Length == 1) { TBig(b, x, y, 3, lv == 2 ? T.HOT : T.INK); y += 26; }
            else { TPut(SvTFit(SvTPrompt(Lang.T(big)), w), x, y, lv == 2 ? T.HOT : T.INK); y += 13; }
        }
        if (frac >= 0 && lv != 2) { SvTBar(x, y + 2, Math.Min(20, (int)(w / 6) - 2), frac, false); y += 13; }
        if (note != null) TPut(SvTFit(T.Up(Lang.T(note)), w), x, y + 2, T.DIM);
        if (SvCanCancel(d, g) && TKey("wsvcancel", new RectangleF(G.Right - 66, G.Bottom - 14, 66, 13), Lang.T("[C] CANCEL"))) SvCancel();
    }

    // The Body side on the tube: the figures (front, side) as thin lines, the legs in columns, the prompt, the groups' keys over the two
    // resets' keys
    static readonly string[] SvTGroupKeys = { "[A] ALL", "[L] LEGS", "[<] LEFT", "[>] RIGHT", "[U] UPPER" };
    static readonly string[] SvTBodyKeys = { "[Y] YAW", "[F] FULL" };
    static readonly string[] SvTBones = { "L THIGH", "L SHIN", "R THIGH", "R SHIN" };

    void TSlimeBody(RectangleF c, PanelData d)
    {
        _svBodyDrawn = true;
        var p = SvPose(d);
        float y0 = c.Y + 13, keyY = c.Bottom - 22, groupY = keyY - 16, promptY = groupY - 13;
        var box = new RectangleF(c.X, y0, 118, promptY - 3 - y0);
        T.Box(_p, box, T.FRAME);
        var fa = new RectangleF(box.X + 2, box.Y + 2, 52, box.Height - 4);
        var sa = new RectangleF(box.X + 56, box.Y + 2, box.Width - 58, MathF.Min(box.Height - 4, box.Width - 50));
        if (p == null) TPut(SvTFit(T.Up(Lang.T(SvNoPose)), box.Width - 6), box.X + 4, box.Y + box.Height / 2 - 4, T.DIM);
        else
        {
            var ink = new SvInk { Ok = T.INK, Warn = T.HOT, Bad = T.HOT, Sleep = T.DIM, Faint = T.FAINT, Sel = T.HOT, W = 1, WSel = 2, WFaint = 1, Labels = 4 };
            SvFigure(fa, p, false, ink, true);
            SvFigure(sa, p, true, ink, false);
        }
        // BONE TWIST SIDE
        float x = box.Right + 8, tx = x + 48, dx = tx + 26, sx = dx + 26;
        TPut(Lang.T("BONE"), x, y0 + 2, T.DIM);
        TPut(Lang.T("TWIST"), tx, y0 + 2, T.DIM);
        TPut(Lang.T("SIDE"), sx, y0 + 2, T.DIM);
        for (int i = 0; i < 4; i++)
        {
            float y = y0 + 15 + i * 11;
            TPut(SvTFit(Lang.T(SvTBones[i]), tx - x - 4), x, y, T.MID);
            if (p == null || float.IsNaN(p.Twist[i])) { TPut("-", tx, y, T.DIM); continue; }
            bool left = i < 2;
            float t = p.Twist[i];
            bool hot = MathF.Max(MathF.Abs(t), MathF.Abs(p.Side[i])) >= 15;
            uint ink = hot ? T.HOT : T.INK;
            TPut(t >= 0 ? SvDeg("+{0}", t) : SvDeg("-{0}", t), tx, y, ink);
            if (MathF.Abs(t) >= 0.5f) TPut(Lang.T((left ? t > 0 : t < 0) ? "OUT" : "IN"), dx, y, ink);
            TPut(SvDeg("{0}", p.Side[i]), sx, y, ink);
        }
        string note = p == null ? null : p.Rolled ? T.Up(Lang.T(SvRollHints[0])) : p.Lying ? T.Up(Lang.T("Lying")) : !float.IsNaN(p.HipHead) ? SvDeg("Hip {0}° from head", p.HipHead, true) : null;
        if (note != null) TPut(SvTFit(note, c.Right - x), x, y0 + 15 + 4 * 11 + 6, p.Rolled ? T.HOT : T.DIM);
        bool lie = SvLying(d) && p is not { Rolled: true };        // (on a side: the note says to lie on the back - no LYING? [Y] YAW over it)
        TPut(SvTFit(SvTPromptNow(d, lie), c.Width), c.X, promptY, SvArmed95() || lie ? T.HOT : T.INK);
        // the groups (lit when picked), the two resets
        float words = 0;
        for (int g = 0; g < SlimeGroups.Count; g++) words += T.W(Lang.T(SvTGroupKeys[g])) + 6;
        float extra = MathF.Max(0, MathF.Floor((c.Width - 4 * 4 - words) / SlimeGroups.Count)), gx = c.X;
        for (int g = 0; g < SlimeGroups.Count; g++)
        {
            string label = Lang.T(SvTGroupKeys[g]);
            float gw = T.W(label) + 6 + extra;
            if (TKey(SvGroupIds[g], new RectangleF(gx, groupY, gw, 13), label, _svGroup == g)) SvSetGroup(g);
            gx += gw + 4;
        }
        bool can = SvCan(d);
        float kw = MathF.Floor((c.Width - 4) / 2);
        for (int k = 0; k < 2; k++)
        {
            var (label, asking, counting) = SvButton(k, d, true);
            bool hi = ((k == 0 && lie) || SvGuideLit(k, d)) && !asking && !counting;
            string line2 = asking ? Lang.T("SURE?") : counting ? T.Up(label) : hi ? _svTLieKey : null;
            if (TKey(SvBodyIds[k], new RectangleF(c.X + k * (kw + 4), keyY, kw, 22), SvTBodyKeys[k], asking || counting || hi, can, line2)) SvResetTap(k, d, _svGroup);
        }
    }

    // a prompt line of a text (the status line's words in capitals), made once
    readonly Dictionary<string, string> _svTPrompts = new();
    string SvTPrompt(string s)
    {
        if (!_svTPrompts.TryGetValue(s, out var t))
        {
            if (_svTPrompts.Count > 64) _svTPrompts.Clear();
            _svTPrompts[s] = t = "> " + s.ToUpper(CultureInfo.CurrentCulture);
        }
        return t;
    }
}
