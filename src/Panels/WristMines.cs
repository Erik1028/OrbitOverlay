using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using Vortice.Direct2D1;
using Vortice.DirectWrite;

namespace Orbit;

// The Minesweeper widget. On the face: the mines left, the time, the face (the game's state) and a small picture of the
// board - a tap opens it, and the opened view is where you play (squares as big as fit; on a 95 px card they would be too
// small for a laser). One trigger: a tap opens a square (or chords a number); held 0.4 s it puts a flag there or takes it
// away (a ring fills meanwhile); in flag mode a tap flags. The games live in memory while Orbit runs (the best times in
// the config). Modern: Bento's tiles and tokens; Windows 95: the real Minesweeper's pixels (MineArt).
public sealed partial class WristPanel
{
    // one game per board: Beginner 9 x 9 with 10 mines, Intermediate 16 x 16 with 40 (modern only: the 95 window has room
    // for Beginner's 16 px squares, the 95 style plays that one)
    readonly MinesGame[] _mines = { new(9, 9, 10), new(16, 16, 40) };
    int _mLevel;
    bool _mFlagMode;
    // the press on the board: its square, when it began, whether the long press already flagged it
    int _mPress = -1;
    bool _mPressing, _mPressDone;
    double _mPressAt;
    const double MinesHold = 0.4, MinesHoldShow = 0.1;       // the long press; its ring shows from 0.1 s (a tap shows none)
    // the board and the buttons as last drawn (texture px; 95: canvas px) - for the tests' taps
    RectangleF _mBoard, _mFlagBtn, _mFaceBtn;
    float _mPitch = 1;
    readonly Dictionary<int, (MinesGame g, int ver, ID2D1Bitmap bmp)> _mMini = new();

    bool MinesTicking => _mines[0].State == MinesGame.Phase.Playing || _mines[1].State == MinesGame.Phase.Playing;

    static readonly string FlagGlyph = char.ConvertFromUtf32(0xE7C1);
    static readonly string[] MineDigit = { "", "1", "2", "3", "4", "5", "6", "7", "8" };
    // the classic hues for the dark card: 1 blue, 2 green, 3 red, 4 indigo, 5 maroon as a light red, 6 teal (7 white, 8 grey)
    static readonly uint[] MineHue = { 0, 0xFF5AA9FF, 0xFF3DDC84, 0xFFFF5A5A, 0xFF8C93FF, 0xFFFF9C85, 0xFF2DD4BF, 0, 0 };
    const uint MiniOpen = 0x47000000;                         // an open square on the face: a dark film over the card

    static uint MineHueOf(int n) => n == 7 ? Bento.Text : n == 8 ? Bento.Sub : MineHue[n];

    // a number's colour on what it sits on: the hue, lifted (or darkened) until it reads (4.5:1)
    static uint MineNumColor(int n, uint under)
    {
        uint c = MineHueOf(n), to = Bento.Lum(under) < 0.18f ? 0xFFFFFFFF : 0xFF000000;
        for (int i = 0; i < 16 && Bento.Contrast(c, under) < 4.5f; i++) c = Theme.Mix(c, to, 0.08f);
        return c;
    }

    static string MinesTime(double seconds)
    {
        int s = Math.Min(5999, (int)seconds);
        return $"{s / 60}:{s % 60:00}";
    }

    static string MinesLeftText(MinesGame g) => g.MinesLeft.ToString(System.Globalization.CultureInfo.InvariantCulture);

    int MinesBestOf(int level) => C.MinesBest is { } b && b.Length > level ? b[level] : 0;

    void MinesNew()
    {
        (Theme.Classic ? _mines[0] : _mines[_mLevel]).Reset();
        _mPressing = false;
        _mPress = -1;
        _dirty = true;
    }

    void MinesLevelSet(int level)
    {
        _mLevel = level;
        _mPressing = false;
        _mPress = -1;
        _dirty = true;
    }

    // the board's pointer (both styles): a press on a square; held 0.4 s it puts a flag there or takes it away (once); let go
    // on the same square (a little laser wobble allowed) it opens it or chords - or flags it in flag mode. Returns the square
    // under the laser (-1: none).
    int MinesInput(MinesGame g, RectangleF board, float pitch, double now)
    {
        _mBoard = board;
        _mPitch = pitch;
        bool click = _ui.Clickable(Theme.Classic ? "mn95b" : "mnb", board, out bool hover, out bool held);
        // a hot area per square: the hover mark follows the laser (a redraw when it gets onto another square)
        for (int i = 0; i < g.Cells; i++) _ui.Hover(new RectangleF(board.X + i % g.W * pitch, board.Y + i / g.W * pitch, pitch, pitch));
        int under = hover ? MinesCellAt(g, _ui.Mouse) : -1;
        if ((held || click) && !_mPressing)
        {
            _mPressing = true;
            _mPress = under;
            _mPressAt = now;
            _mPressDone = false;
        }
        if (!_mPressing) return under;
        if (!held && !click)
        {
            // let go outside the board: nothing happens
            _mPressing = false;
            _mPress = -1;
            return under;
        }
        bool near = _mPress >= 0 && MinesNear(_mPress, g, _ui.Mouse);
        if (held && !_mPressDone && near && now - _mPressAt >= MinesHold && !g.Over && !g.IsOpen(_mPress))
        {
            g.ToggleFlag(_mPress);
            _mPressDone = true;
            _dirty = true;
            Act("mines.flag");                  // a tick in the laser's hand: the flag is there, let go
        }
        if (click)
        {
            if (!_mPressDone && near) MinesAct(g, _mPress, now);
            _mPressing = false;
            _mPress = -1;
        }
        else if (!_mPressDone) _ui.Animating = true;           // the long press's ring fills: keep drawing
        return under;
    }

    int MinesCellAt(MinesGame g, Vector2 m)
    {
        int cx = (int)MathF.Floor((m.X - _mBoard.X) / _mPitch), cy = (int)MathF.Floor((m.Y - _mBoard.Y) / _mPitch);
        return cx >= 0 && cy >= 0 && cx < g.W && cy < g.H ? cy * g.W + cx : -1;
    }

    // still on the pressed square (a third of a square of wobble allowed)
    bool MinesNear(int i, MinesGame g, Vector2 m)
    {
        float s = _mPitch, k = s * 0.3f;
        var r = new RectangleF(_mBoard.X + i % g.W * s - k, _mBoard.Y + i / g.W * s - k, s + 2 * k, s + 2 * k);
        return r.Contains(m.X, m.Y);
    }

    void MinesAct(MinesGame g, int i, double now)
    {
        if (i < 0 || g.Over) return;
        bool changed = _mFlagMode && !g.IsOpen(i) ? g.ToggleFlag(i) : g.Click(i, now);
        if (!changed) return;
        _dirty = true;
        if (g.State == MinesGame.Phase.Lost) Act("mines.boom");
        if (g.State != MinesGame.Phase.Won) return;
        // a best time (per board) in the config
        int level = g == _mines[1] ? 1 : 0, s = Math.Max(1, (int)g.Seconds(now));
        var best = C.MinesBest;
        if (best == null || best.Length < 2) { var nb = new int[2]; if (best != null) Array.Copy(best, nb, best.Length); C.MinesBest = best = nb; }
        if (best[level] <= 0 || s < best[level])
        {
            best[level] = s;
            _cfg.MarkDirty();
        }
    }

    // what the press shows now: the square held down (-1: none), how far its long press is (0..1), the number being chorded
    // (its hidden neighbours go down too; -1: none)
    (int press, float prog, int chord) MinesPressInfo(MinesGame g, double now)
    {
        if (!_mPressing || _mPressDone || _mPress < 0 || g.Over || !MinesNear(_mPress, g, _ui.Mouse)) return (-1, 0, -1);
        if (g.IsOpen(_mPress)) return (-1, 0, g.Adj(_mPress) > 0 ? _mPress : -1);
        float p = (float)Math.Clamp((now - _mPressAt - MinesHoldShow) / (MinesHold - MinesHoldShow), 0, 1);
        return (_mPress, p, -1);
    }

    // held down: the pressed square (not in flag mode, not a flag), or a hidden neighbour of the number being chorded
    bool MinesDown(MinesGame g, int i, int press, int chord)
    {
        if (g.IsOpen(i) || g.IsFlag(i)) return false;
        if (i == press) return !_mFlagMode;
        if (chord < 0) return false;
        int dx = Math.Abs(i % g.W - chord % g.W), dy = Math.Abs(i / g.W - chord / g.W);
        return dx <= 1 && dy <= 1;
    }

    bool MinesOh(MinesGame g) => _mPressing && !_mPressDone && !g.Over && _mPress >= 0;

    // ================= Modern =================

    // the face: its colour (green when won, red when lost - the text colour on a coloured card)
    static uint MinesFaceColor(MinesGame g, uint normal) => g.State switch
    {
        MinesGame.Phase.Won => Bento.Badge ? normal : Bento.Ok,
        MinesGame.Phase.Lost => Bento.Badge ? normal : Bento.BadInk,
        _ => normal,
    };

    // the smiley as lines: a ring, the eyes, the mouth - 'o' while a square is held, X eyes and a frown when lost, sunglasses when won
    void SmileyM(float cx, float cy, float rad, MinesGame.Phase st, bool oh, uint c, float lw)
    {
        _p.Ring(cx, cy, rad, c, lw);
        float ex = rad * 0.36f, ey = cy - rad * 0.2f, er = MathF.Max(1.4f, rad * 0.11f);
        if (st == MinesGame.Phase.Lost)
        {
            float s = rad * 0.13f;
            foreach (float sx in new[] { cx - ex, cx + ex })
            {
                _p.RoundLine(sx - s, ey - s, sx + s, ey + s, c, lw * 0.8f);
                _p.RoundLine(sx - s, ey + s, sx + s, ey - s, c, lw * 0.8f);
            }
            _p.Arc(cx, cy + rad * 0.66f, rad * 0.4f, 0.85f, 0.3f, c, lw);
            return;
        }
        if (st == MinesGame.Phase.Won)
        {
            float gw = rad * 0.5f, gh = rad * 0.32f;
            _p.Fill(new RectangleF(cx - ex - gw / 2, ey - gh / 2, gw, gh), c, gh * 0.45f);
            _p.Fill(new RectangleF(cx + ex - gw / 2, ey - gh / 2, gw, gh), c, gh * 0.45f);
            _p.Line(cx - ex - gw / 2 - rad * 0.12f, ey - gh * 0.3f, cx + ex + gw / 2 + rad * 0.12f, ey - gh * 0.3f, c, MathF.Max(1.2f, lw * 0.8f));
            _p.Arc(cx, cy, rad * 0.55f, 0.35f, 0.3f, c, lw);
            return;
        }
        _p.Circle(cx - ex, ey, er, c);
        _p.Circle(cx + ex, ey, er, c);
        if (oh) _p.Ring(cx, cy + rad * 0.36f, rad * 0.16f, c, lw);
        else _p.Arc(cx, cy, rad * 0.55f, 0.35f, 0.3f, c, lw);
    }

    // a flag in a square: a pole and its base, the pennant in the accent
    void FlagM(RectangleF c, uint pole, uint flag)
    {
        float s = c.Width, px = c.X + s * 0.38f, top = c.Y + s * 0.18f, bot = c.Y + s * 0.8f, lw = MathF.Max(1.6f, s * 0.07f);
        _p.Polygon(new[] { new Vector2(px, top), new Vector2(px + s * 0.4f, top + s * 0.16f), new Vector2(px, top + s * 0.32f) }, flag);
        _p.RoundLine(px, top, px, bot, pole, lw);
        _p.Fill(new RectangleF(px - s * 0.17f, bot - lw * 0.5f, s * 0.34f, lw), pole, lw / 2);
    }

    // a mine: a ball with four spikes and a glint
    void MineM(float cx, float cy, float s, uint body, uint shine)
    {
        float r = s * 0.22f, sp = s * 0.34f, d = sp * 0.72f, lw = MathF.Max(1.5f, s * 0.07f);
        _p.RoundLine(cx - sp, cy, cx + sp, cy, body, lw);
        _p.RoundLine(cx, cy - sp, cx, cy + sp, body, lw);
        _p.RoundLine(cx - d, cy - d, cx + d, cy + d, body, lw);
        _p.RoundLine(cx - d, cy + d, cx + d, cy - d, body, lw);
        _p.Circle(cx, cy, r, body);
        _p.Circle(cx - r * 0.35f, cy - r * 0.35f, r * 0.3f, shine);
    }

    // The widget, a 1x1 first (a tap grows it into the game). Bigger if wanted: a 2x2 is the game's header (the mines left,
    // the face, the time) over a picture of the board; a 3x2 the board with the numbers beside it
    void MinesOrbit(RectangleF r, WatchWidget w, int id)
    {
        var g = _mines[_mLevel];
        float pad = PadX(r), iw = r.Width - 2 * pad, x = r.X + pad;
        string left = MinesLeftText(g), time = MinesTime(g.Seconds(Clock.Now));
        uint lc = g.MinesLeft < 0 ? Bento.Level(StatLevel.Warn, Bento.Text) : Bento.Text;
        if (w.W == 1 || w.H == 1)
        {
            // the small one (a tap grows it into the game): the face and the time in the caption row, the mines left big with a
            // tiny picture of the board beside it
            const float fr = 9;
            SmileyM(x + fr, r.Y + 19, fr, g.State, false, MinesFaceColor(g, Bento.CapText), 1.8f);
            TextAt(time, x + 2 * fr + 6, r.Y + CapBase + 2, iw - 2 * fr - 6, Bento.Cap, SmallText, FontWeight.SemiBold, TextAlignment.Trailing);
            float hp = MathF.Floor(34f / g.H * 2) / 2, bs = hp * g.W, bh = hp * g.H;
            MinesMini(g, new RectangleF(r.Right - pad - bs, MathF.Round(r.Y + 61 - bh / 2), bs, bh), hp);
            ValueText(x, r.Y + SBase, left, MathF.Max(17, FitSize(left, iw - bs - 6, Hero, FontWeight.Bold, Theme.Display)), lc, null);
            return;
        }
        if (w.W >= 3)
        {
            float pitch = MathF.Floor((r.Height - 28) / g.H);
            var b = new RectangleF(r.X + 14, MathF.Round(r.Y + (r.Height - pitch * g.H) / 2), pitch * g.W, pitch * g.H);
            MinesMini(g, b, pitch);
            float cx0 = b.Right + 16, cw = r.Right - pad - cx0;
            CapText("MINES", cx0, r.Y + CapBase, cw, Bento.Cap);
            ValueText(cx0, r.Y + 62, left, MathF.Max(20, FitSize(left, cw, 32, FontWeight.Bold, Theme.Display)), lc, null);
            CapText("TIME", cx0, r.Y + 96, cw, Bento.Cap);
            ValueText(cx0, r.Y + 133, time, MathF.Max(20, FitSize(time, cw, 32, FontWeight.Bold, Theme.Display)), Bento.Text, null);
            SmileyM(cx0 + 15, r.Y + 170, 15, g.State, false, MinesFaceColor(g, Bento.Text), 2.2f);
            return;
        }
        // 2x2: a flag and the mines left, the face in the middle, the time at the right; the board under them
        float hy = r.Y + 37, side = (iw - 34) / 2;
        FlagM(new RectangleF(x - 3, r.Y + 13, 24, 24), Bento.Text, Bento.Accent);
        TextAt(left, x + 22, hy, side - 22, lc, MathF.Max(17, FitSize(left, side - 24, 26, FontWeight.Bold, Theme.Display)), FontWeight.Bold,
            TextAlignment.Leading, Theme.Display);
        SmileyM(r.X + r.Width / 2, r.Y + 28, 12, g.State, false, MinesFaceColor(g, Bento.Text), 2f);
        TextAt(time, r.Right - pad - side, hy, side, Bento.Text, MathF.Max(17, FitSize(time, side, 26, FontWeight.Bold, Theme.Display)), FontWeight.Bold,
            TextAlignment.Trailing, Theme.Display);
        float pt = MathF.Floor(MathF.Min(iw, r.Bottom - 14 - (r.Y + 52)) / g.H);
        MinesMini(g, new RectangleF(MathF.Round(r.X + (r.Width - pt * g.W) / 2), r.Bottom - 14 - pt * g.H, pt * g.W, pt * g.H), pt);
    }

    // the board as a picture: raised tiles still hidden, open squares lower (a dark film) with a dot in their number's colour,
    // little flags, the mines red when lost (the one that went off on red)
    void MinesMini(MinesGame g, RectangleF b, float pitch)
    {
        float gap = pitch >= 12 ? 2 : 1, cs = pitch - gap, rad = MathF.Max(1.5f, MathF.Round(cs * 0.22f));
        bool lost = g.State == MinesGame.Phase.Lost;
        if (pitch < 6)
        {
            // a hint of it (the 1x1): squares only - the hidden ones lighter than a tile (they are tiny), open ones dark, a flag's
            // in the accent, a mine red
            uint hidden = Theme.Mix(Bento.Tile, Bento.Text, 0.2f);
            for (int i = 0; i < g.Cells; i++)
            {
                var c = new RectangleF(b.X + i % g.W * pitch, b.Y + i / g.W * pitch, pitch - 0.8f, pitch - 0.8f);
                bool mine = lost && g.IsMine(i) && !g.IsFlag(i);
                _p.Fill(c, mine || i == g.Hit ? Bento.Bad : g.IsFlag(i) ? Bento.Accent : g.IsOpen(i) ? MiniOpen : hidden, 0.8f);
            }
            return;
        }
        for (int i = 0; i < g.Cells; i++)
        {
            var c = new RectangleF(b.X + i % g.W * pitch + gap / 2, b.Y + i / g.W * pitch + gap / 2, cs, cs);
            float cx = c.X + cs / 2, cy = c.Y + cs / 2;
            bool open = g.IsOpen(i), flag = g.IsFlag(i), mine = lost && g.IsMine(i) && !flag;
            if (open || mine)
            {
                bool hit = i == g.Hit;
                _p.Fill(c, hit ? Bento.Bad : MiniOpen, rad);
                if (mine || hit) _p.Circle(cx, cy, cs * 0.27f, hit ? Bento.Dark : Bento.BadInk);
                else if (g.Adj(i) > 0) _p.Circle(cx, cy, cs * 0.2f, MineHueOf(g.Adj(i)));
                continue;
            }
            _p.Fill(c, Bento.Tile, rad);
            if (!flag) continue;
            float pw = MathF.Max(1, cs * 0.12f), fx = cx - cs * 0.2f;
            _p.Fill(new RectangleF(fx, c.Y + cs * 0.2f, pw, cs * 0.62f), Bento.Text);
            _p.Fill(new RectangleF(fx + pw, c.Y + cs * 0.2f, cs * 0.42f, cs * 0.3f), Bento.Accent);
        }
    }

    // The opened view: the board on the left as big as the card allows (9 x 9: 41 px squares, 16 x 16: 24 px), on the right the
    // name, the mines left and the time, the face (a new game), flag mode, the board size and how to flag
    void MinesDetail(RectangleF E)
    {
        var g = _mines[_mLevel];
        double now = Clock.Now;
        const float Area = 384;
        float pitch = g.W > 9 ? 24 : 41, bw = pitch * g.W, bh = pitch * g.H;
        var board = new RectangleF(E.X + 18 + MathF.Round((Area - bw) / 2), E.Y + MathF.Round((E.Height - bh) / 2), bw, bh);
        MinesBoardM(g, board, pitch, now);
        float x = E.X + 18 + Area + 20, w = E.Right - 18 - x;
        const string Name = "Minesweeper";
        float ts = MathF.Max(SmallText, FitSize(Name, w - 58, 24, FontWeight.SemiBold, null));
        _p.Text(Ellipsize(Name, w - 58, ts, FontWeight.SemiBold), new RectangleF(x, E.Y + 16, w - 56, 40), Bento.Text, ts, FontWeight.SemiBold);
        // the mines left (amber when there are more flags than mines) and the time
        float cy = E.Y + 78, half = (w - 10) / 2;
        string left = MinesLeftText(g), time = MinesTime(g.Seconds(now));
        CapText("MINES", x, cy + 15, half, Sub);
        CapText("TIME", x + half + 10, cy + 15, half, Sub);
        TextAt(left, x, cy + 54, half, g.MinesLeft < 0 ? Bento.WarnInk : Bento.Text, MathF.Max(SmallText, FitSize(left, half, 34, FontWeight.Bold, Theme.Display)),
            FontWeight.Bold, TextAlignment.Leading, Theme.Display);
        TextAt(time, x + half + 10, cy + 54, half, Bento.Text, MathF.Max(SmallText, FitSize(time, half, 34, FontWeight.Bold, Theme.Display)),
            FontWeight.Bold, TextAlignment.Leading, Theme.Display);
        // the face: a new game; beside it how the game is and the best time
        const float fd = 60;
        var fr = new RectangleF(x, E.Y + 150, fd, fd);
        _mFaceBtn = fr;
        if (Press("mnface", fr, null, null, false, true, 24, true)) MinesNew();
        SmileyM(fr.X + fd / 2, fr.Y + fd / 2, fd * 0.34f, g.State, MinesOh(g), MinesFaceColor(g, Bento.Text), 2.6f);
        float tx = fr.Right + 14, tw = x + w - tx;
        string st = g.State switch
        {
            MinesGame.Phase.Ready => "Ready", MinesGame.Phase.Playing => Lang.C("mines", "Playing"), MinesGame.Phase.Won => "Cleared!", _ => "Boom!",
        };
        uint sc = g.State == MinesGame.Phase.Won ? Bento.Ok : g.State == MinesGame.Phase.Lost ? Bento.BadInk : Bento.Text;
        TextAt(Ellipsize(st, tw, 20, FontWeight.SemiBold), tx, fr.Y + 26, tw, sc, 20, FontWeight.SemiBold);
        int best = MinesBestOf(_mLevel);
        bool record = g.State == MinesGame.Phase.Won && best > 0 && best == Math.Max(1, (int)g.Seconds(now));
        string bt = record ? "New best!" : best > 0 ? Lang.F("Best {0}", MinesTime(best)) : "No best yet";
        TextAt(Ellipsize(bt, tw, SmallText, FontWeight.Normal), tx, fr.Y + 50, tw, Sub, SmallText, FontWeight.Normal);
        // flag mode: a tap flags (a held square flags in either mode)
        var fb = new RectangleF(x, E.Y + 230, w, 52);
        _mFlagBtn = fb;
        if (Press("mnflag", fb, FlagGlyph, "Flag mode", _mFlagMode, true, 20)) { _mFlagMode = !_mFlagMode; _dirty = true; }
        // the board's size (each keeps its own game)
        float sy = E.Y + 298, bwid = (w - 10) / 2;
        CapText("BOARD", x, sy + 15, w, Sub);
        if (Press("mnl0", new RectangleF(x, sy + 26, bwid, 44), null, "9 × 9", _mLevel == 0) && _mLevel != 0) MinesLevelSet(0);
        if (Press("mnl1", new RectangleF(x + bwid + 10, sy + 26, bwid, 44), null, "16 × 16", _mLevel == 1) && _mLevel != 1) MinesLevelSet(1);
        const string Hint = "Hold to place a flag";
        TextAt(Ellipsize(Hint, w, SmallText, FontWeight.Normal), x, E.Y + 396, w, Sub, SmallText, FontWeight.Normal);
    }

    // the board, big: hidden squares raised tiles (lighter under the laser), open ones lower and flat with their number in its
    // colour; a held square goes down; a long press draws a ring filling round its square, the flag fading in (or out)
    void MinesBoardM(MinesGame g, RectangleF board, float pitch, double now)
    {
        int hover = MinesInput(g, board, pitch, now);
        var (press, prog, chord) = MinesPressInfo(g, now);
        float gap = pitch >= 32 ? 3 : 2, cs = pitch - gap, rad = MathF.Max(3, MathF.Round(cs * 0.18f));
        uint open = Theme.Mix(Bento.Card, 0xFF000000, 0.3f), openHover = Theme.Mix(open, Bento.Text, 0.06f);
        uint raised = Theme.Mix(Bento.Tile, Bento.Text, 0.07f);
        float ns = MathF.Max(SmallText, MathF.Round(cs * 0.58f));
        bool lost = g.State == MinesGame.Phase.Lost;
        for (int i = 0; i < g.Cells; i++)
        {
            var c = new RectangleF(board.X + i % g.W * pitch + gap / 2, board.Y + i / g.W * pitch + gap / 2, cs, cs);
            float cx = c.X + cs / 2, cy = c.Y + cs / 2;
            bool isOpen = g.IsOpen(i), flag = g.IsFlag(i), mine = lost && g.IsMine(i) && !flag;
            if (isOpen || mine)
            {
                bool hit = i == g.Hit;
                _p.Fill(c, hit ? Bento.Bad : i == hover && !g.Over && g.Adj(i) > 0 ? openHover : open, rad);
                if (mine || hit)
                    MineM(cx, cy, cs, hit ? Bento.Dark : Bento.BadInk, Theme.Mix(hit ? Bento.Dark : Bento.BadInk, 0xFFFFFFFF, 0.55f));
                else if (g.Adj(i) > 0)
                    _p.Text(MineDigit[g.Adj(i)], c, MineNumColor(g.Adj(i), open), ns, FontWeight.Bold, TextAlignment.Center, ParagraphAlignment.Center,
                        Theme.Display);
                continue;
            }
            if (MinesDown(g, i, press, chord)) _p.Fill(c, open, rad);
            else if (i == hover && !g.Over) _p.Fill(c, Bento.TileHover, rad);
            else _p.FillGradient(c, raised, Bento.Tile, rad, 90);
            var fc = new RectangleF(c.X + cs * 0.1f, c.Y + cs * 0.1f, cs * 0.8f, cs * 0.8f);
            if (i == press && prog > 0)
            {
                // the long press: the ring fills, the flag comes (or goes)
                float a = flag ? 1 - prog : prog;
                FlagM(fc, Theme.WithAlpha(Bento.Text, a), Theme.WithAlpha(Bento.Accent, a));
                _p.Arc(cx, cy, cs * 0.44f, 0, prog, Bento.Accent, MathF.Max(2.5f, cs * 0.08f));
                continue;
            }
            if (!flag) continue;
            FlagM(fc, Bento.Text, Bento.Accent);
            if (lost && !g.IsMine(i))
            {
                // a flag where there was no mine: crossed out
                float k = cs * 0.24f;
                _p.RoundLine(c.X + k, c.Y + k, c.Right - k, c.Bottom - k, Bento.BadInk, MathF.Max(2, cs * 0.08f));
                _p.RoundLine(c.X + k, c.Bottom - k, c.Right - k, c.Y + k, Bento.BadInk, MathF.Max(2, cs * 0.08f));
            }
        }
    }

    // ================= Windows 95 =================

    // n rings of light top-left / dark bottom-right meeting on the diagonal (Minesweeper's 3 px edges; MineArt.Edge in pixels)
    void Edge95(RectangleF r, int n, uint tl, uint br)
    {
        for (int i = 0; i < n; i++)
        {
            float l = r.X + i, t = r.Y + i, rr = r.Right - 1 - i, b = r.Bottom - 1 - i;
            _p.Fill(new RectangleF(l, t, rr - l, 1), tl);
            _p.Fill(new RectangleF(l, t, 1, b - t), tl);
            _p.Fill(new RectangleF(l, b, rr - l + 1, 1), br);
            _p.Fill(new RectangleF(rr, t, 1, b - t + 1), br);
        }
    }

    // a counter: three red seven-segment digits on black in a 1 px sunken box (41 x 25); below zero "-" and two digits
    void Led95(float x, float y, int value)
    {
        Edge95(new RectangleF(x, y, 41, 25), 1, Gry95, Wht95);
        string s = MineArt.LedText(value);
        for (int k = 0; k < 3; k++) _p.ImageNearest(MineArt.LedBmp(_p, s[k]), new RectangleF(x + 1 + 13 * k, y + 1, 13, 23));
    }

    MineArt.Face MinesFace95(MinesGame g) => g.State switch
    {
        MinesGame.Phase.Won => MineArt.Face.Cool,
        MinesGame.Phase.Lost => MineArt.Face.Dead,
        _ => MinesOh(g) ? MineArt.Face.Oh : MineArt.Face.Smile,
    };

    // the small board picture: cs px squares in a 2 px sunken frame, made again only when the game changes
    void Mini95(MinesGame g, float x, float y, int cs)
    {
        Edge95(new RectangleF(x, y, g.W * cs + 4, g.H * cs + 4), 2, Gry95, Wht95);
        if (!_mMini.TryGetValue(cs, out var e) || e.g != g || e.ver != g.Version)
        {
            e.bmp?.Dispose();
            e = (g, g.Version, _p.CreateBitmap(MineArt.Mini(g, cs), g.W * cs, g.H * cs));
            _mMini[cs] = e;
        }
        _p.ImageNearest(e.bmp, new RectangleF(x + 2, y + 2, g.W * cs, g.H * cs));
    }

    // the widget in its little 95 window: a 1x1 the mines counter over the face; a 2x2 the board (4 px squares) with the
    // counters and the face beside it; a 3x2 the real window's layout - the counters with the face between them over the board
    void Mines95Widget(RectangleF c, WatchWidget w)
    {
        var g = _mines[0];
        int secs = (int)Math.Min(999, g.Seconds(Clock.Now));
        var face = MinesFace95(g);
        if (w.H < 2)
        {
            float y = MathF.Floor(c.Y + (c.Height - 44) / 2);
            Led95(MathF.Floor(c.X + (c.Width - 41) / 2), y, g.MinesLeft);
            _p.ImageNearest(MineArt.FaceBmp(_p, face), new RectangleF(MathF.Floor(c.X + (c.Width - 17) / 2), y + 27, 17, 17));
            return;
        }
        if (c.Width >= 124)
        {
            float x0 = MathF.Floor(c.X + (c.Width - 120) / 2), y0 = MathF.Floor(c.Y + (c.Height - 78) / 2);
            Led95(x0, y0, g.MinesLeft);
            Edge95(new RectangleF(x0 + 47, y0, 26, 26), 1, Gry95, Gry95);
            _p.ImageNearest(MineArt.ButtonBmp(_p, face, false), new RectangleF(x0 + 48, y0 + 1, 24, 24));
            Led95(x0 + 79, y0, secs);
            Mini95(g, MathF.Floor(c.X + (c.Width - (g.W * 5 + 4)) / 2), y0 + 29, 5);
            return;
        }
        float bx = MathF.Floor(c.X + (c.Width - 87) / 2), by = MathF.Floor(c.Y + (c.Height - 40) / 2);
        Mini95(g, bx, by, 4);
        float lx = bx + 46, ly = MathF.Floor(c.Y + (c.Height - 76) / 2);
        Led95(lx, ly, g.MinesLeft);
        _p.ImageNearest(MineArt.FaceBmp(_p, face), new RectangleF(lx + 12, ly + 29, 17, 17));
        Led95(lx, ly + 51, secs);
    }

    MineArt.Sq Square95(MinesGame g, int i, int press, int chord)
    {
        bool open = g.IsOpen(i), flag = g.IsFlag(i);
        if (g.State == MinesGame.Phase.Lost)
        {
            if (i == g.Hit) return MineArt.Sq.Hit;
            if (g.IsMine(i)) return flag ? MineArt.Sq.Flag : MineArt.Sq.Mine;
            if (flag) return MineArt.Sq.Wrong;
        }
        if (open) return g.Adj(i) == 0 ? MineArt.Sq.Open : MineArt.Sq.N1 + g.Adj(i) - 1;
        if (flag) return MineArt.Sq.Flag;
        return MinesDown(g, i, press, chord) ? MineArt.Sq.Open : MineArt.Sq.Hidden;
    }

    // The opened view: winmine.exe's window inside, pixel for pixel at its real metrics (MineArt.Client: 168 x 211 for 9 x 9) -
    // the whole of it fits the watch's 212 px, its title bar and menu do not: they go to a narrow window beside it (the name
    // and X, New game, Flag mode, how the game is, the best time)
    RectangleF _mCloseBtn;
    ID2D1Bitmap _mClientBmp;
    object _mClientKey;

    void Mines95Detail(RectangleF win)
    {
        var g = _mines[0];
        double now = Clock.Now;
        int cw = MineArt.ClientW(g.W), ch = MineArt.ClientH(g.H);
        var cl = new RectangleF(win.X, win.Y + MathF.Floor((win.Height - ch) / 2), cw, ch);
        var board = new RectangleF(cl.X + MineArt.BoardX, cl.Y + MineArt.BoardY, g.W * 16, g.H * 16);
        MinesInput(g, board, 16, now);
        var (press, prog, chord) = MinesPressInfo(g, now);
        var fb = new RectangleF(cl.X + MineArt.FaceX(g.W) - 1, cl.Y + MineArt.FaceY - 1, 26, 26);       // the face with its outline
        _mFaceBtn = fb;
        bool newGame = _ui.Clickable("mn95face", fb, out _, out bool faceHeld);
        int secs = (int)Math.Min(999, g.Seconds(now));
        var face = faceHeld ? MineArt.Face.Smile : MinesFace95(g);
        // a long press: its square pushed in (not a flag's, not in flag mode), the flag rising out of it - or sinking into it
        bool rise = press >= 0 && prog > 0, had = rise && g.IsFlag(press);
        int rows = rise ? (int)MathF.Round((had ? 1 - prog : prog) * (MineArt.FlagBottom - MineArt.FlagTop)) : 0;
        var key = (g, g.Version, secs, face, faceHeld, press, chord, rows, rise, _mFlagMode);
        if (_mClientBmp == null || !key.Equals(_mClientKey))
        {
            var px = MineArt.Client(g, secs, face, faceHeld,
                i => rise && i == press ? (had || _mFlagMode ? MineArt.Sq.Hidden : MineArt.Sq.Open) : Square95(g, i, press, chord), press, rows);
            _mClientBmp?.Dispose();
            _mClientBmp = _p.CreateBitmap(px, cw, ch);
            _mClientKey = key;
        }
        _p.ImageNearest(_mClientBmp, cl);
        // the watch's 212 px against winmine's 211: the row left under it is the desktop (or a window's grey on cards)
        if (cl.Bottom < win.Bottom)
            _p.Fill(new RectangleF(cl.X, cl.Bottom, cw, win.Bottom - cl.Bottom), C.Background == "cards" ? Theme.W95Face : Theme.W95Teal);
        if (newGame) MinesNew();
        // the chrome beside it
        var sw = new RectangleF(cl.Right, win.Y, win.Right - cl.Right, win.Height);
        _p.Fill(sw, Theme.W95Face);
        _p.Bevel(sw, Painter.Edge.Window);
        _ui.Caption95(new RectangleF(sw.X + 3, sw.Y + 3, sw.Width - 6, 18), "Minesweeper", W95Icons.Id.Mine);
        _mCloseBtn = new RectangleF(sw.Right - 5 - 16, sw.Y + 5, 16, 14);
        if (_ui.CaptionClose95("wd95x", sw.Right - 5 - 16, sw.Y + 5, true, 6)) { CloseDetail(); return; }
        float x0 = sw.X + 8, w = sw.Width - 16;
        if (_ui.Button95("mn95new", new RectangleF(x0, sw.Y + 30, w, 23), "&New game")) MinesNew();
        var flagBtn = new RectangleF(x0, sw.Y + 59, w, 23);
        _mFlagBtn = flagBtn;
        if (_ui.Button95("mn95flag", flagBtn, "&Flag mode", true, false, _mFlagMode, W95Icons.Id.Flag)) { _mFlagMode = !_mFlagMode; _dirty = true; }
        float ty = flagBtn.Bottom + 10;
        switch (g.State)
        {
            case MinesGame.Phase.Won:
                _ui.Text95("You win!", new RectangleF(x0, ty, w, 15), Blk95, true);
                _ui.Text95("Click the face to play again.", new RectangleF(x0, ty + 16, w, 30), Blk95, false, TextAlignment.Leading, true, true);
                break;
            case MinesGame.Phase.Lost:
                _ui.Text95("Game over.", new RectangleF(x0, ty, w, 15), Blk95, true);
                _ui.Text95("Click the face to play again.", new RectangleF(x0, ty + 16, w, 30), Blk95, false, TextAlignment.Leading, true, true);
                break;
            default:
                _ui.Text95(g.State == MinesGame.Phase.Ready ? "Click a square to start." : "Hold a square to flag it.", new RectangleF(x0, ty, w, 30),
                    Blk95, false, TextAlignment.Leading, true, true);
                break;
        }
        int best = MinesBestOf(0);
        _ui.Text95(best > 0 ? Lang.F("Best time: {0} s", best) : "Best time: none", new RectangleF(x0, sw.Bottom - 8 - 15, w, 15));
    }

    // ---- --snapshot's hooks ----

    public MinesGame MinesGameAt(int level) => _mines[level];
    public void MinesSetGame(int level, MinesGame g) { _mines[level] = g; _dirty = true; }
    public int MinesLevel { get => _mLevel; set => MinesLevelSet(Math.Clamp(value, 0, 1)); }
    public bool MinesFlagMode => _mFlagMode;

    public void MinesResetAll()
    {
        _mines[0] = new MinesGame(9, 9, 10);
        _mines[1] = new MinesGame(16, 16, 40);
        _mLevel = 0;
        _mFlagMode = false;
        _mPressing = false;
        _mPress = -1;
    }

    // where to point on the watch texture (the 95 canvas is shown 2x): a square of the opened board, the flag button, the face
    Vector2 MinesPoint(float x, float y) => Theme.Classic ? new Vector2(x * 2, y * 2) : new Vector2(x, y);
    public Vector2 MinesSquarePoint(int i, int w) =>
        MinesPoint(_mBoard.X + (i % w + 0.5f) * _mPitch, _mBoard.Y + (i / w + 0.5f) * _mPitch);
    public Vector2 MinesFlagButtonPoint => MinesPoint(_mFlagBtn.X + _mFlagBtn.Width / 2, _mFlagBtn.Y + _mFlagBtn.Height / 2);
    public Vector2 MinesFacePoint => MinesPoint(_mFaceBtn.X + _mFaceBtn.Width / 2, _mFaceBtn.Y + _mFaceBtn.Height / 2);
    // the opened view's X (modern: the opened views' own, top right)
    public Vector2 MinesClosePoint
    {
        get
        {
            if (Theme.Classic) return MinesPoint(_mCloseBtn.X + _mCloseBtn.Width / 2, _mCloseBtn.Y + _mCloseBtn.Height / 2);
            var t = WatchWidgets.CellRect(0, 0, WatchWidgets.Cols, WatchWidgets.Rows, false);
            return new Vector2(t.Right - 22 - 23, t.Y + 14 + 23);
        }
    }
    public bool MinesShown => _open != null;            // opened, or still shrinking back into its tile
}
