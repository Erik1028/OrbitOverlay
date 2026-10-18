using System;
using System.Drawing;
using Vortice.DirectWrite;

namespace Orbit;

// The bar under the big screen as a media console, in the Cyber style (Erik 10-18: "work more on the Cyber style, mainly theater
// mode"). While a media session plays or is paused - the Media widget's own reader (Sys/MediaInfo.cs) - a NOW PLAYING // readout sits
// after the screens: the title in the condensed capitals (and the artist in monospace), the time, a thin yellow line of where it is in
// the track; then the transport: play / pause on the yellow plate between -10 s and +10 s (previous / next when the player can't jump
// about). Play / pause, previous and next are the Media widget's own commands (OnMedia: OrbitApp.WristAction). The stick on the readout
// or the transport steps 10 s a notch - sent once it rests, the line and the time showing where it will go - or, without jumps, a track
// at a time. A title too long for its line scrolls slowly (Look > Motion Full), else it is cut with "…".
// Cost: the readout is a little picture of its own - when only it changes (the title scrolling, the time's second, the line's pixel)
// just it is drawn again into _read and copied into the bar's texture; the rest of the bar is not touched. Nothing is read or drawn
// while the bar is away. No session: the part is not there at all (the bar as before); it goes 3 s after the session does (a track
// change's empty moment must not make the bar jump). Modern and the retro styles: their bars as before.
public sealed partial class TheaterBar
{
    // what plays, as the bar reads it (OrbitApp: Read(MediaInfo); the tools: made up)
    public struct BarMedia
    {
        public bool Has, Playing, Timeline, CanSeek;      // Timeline: Pos and Dur (seconds) are the player's
        public string Title, Artist;
        public double Pos, Dur;
    }

    public Func<BarMedia> ReadMedia;             // null: no media part
    public Action<string> OnMedia;               // "media.play" / "media.prev" / "media.next": the Media widget's commands
    public Action<double> OnSeekTo;              // to so many seconds into the track (MediaInfo.SeekTo)

    // the app's reading: the Media widget's reader, as it is (no allocation: it is asked 4 times a second while the bar shows)
    public static BarMedia Read(MediaInfo m)
    {
        var (has, title, artist, playing, _, _) = m.Snapshot();
        var (tok, pos, dur) = m.Timeline();
        return new BarMedia { Has = has, Title = title, Artist = artist, Playing = playing, Timeline = tok, Pos = pos, Dur = dur, CanSeek = m.CanSeek() };
    }

    // the part's sizes: the readout, the two jump / track plates and play between them (width is precious: the bar is ~5 % of the
    // screen's distance tall, so every 84 px of it is as wide as the bar is tall)
    const int MReadW = 252, MJumpW = 50, MPlayW = 64, MGap = 6, LineW = MReadW - 28, LineX = 14, LineY = 22, LineH = 26;
    const float TitleSize = 21, CodeSize = 10, ArtistSize = 11;
    const double MediaGone = 3, ReadEvery = 0.25, StickRest = 0.35, TrackStep = 0.6, PlayHold = 1.2;
    const float MqSpeed = 26, MqHold = 1.8f, MqGap = 56;      // the marquee: px a second, the pause at its start, the gap round the loop
    static readonly string CyPlayFill = "\uF5B0", CyPauseFill = "\uF8AE", CyRewind = "\uEB9E", CyForward = "\uEB9D",
        CyPrev = "\uE892", CyNext = "\uE893";

    BarMedia _m;                     // what the console shows: the last reading that had a session
    bool _mediaOn, _mHas;            // _mediaOn: the part is laid out; _mHas: a session now (else the last one, going)
    double _mReadAt, _mReadT, _mGoneAt, _mTick = -10, _mNow, _mPlayHold, _trackAt, _mqT0;
    double _seekPend, _seekSendAt, _seekTo, _seekToT, _seekShowUntil;    // a jump on its way / just sent (shown until the player agrees)
    string _mCmd;                    // a press of this draw, sent after it
    RectangleF _readAt;              // where the readout is on the bar (the last full draw)
    float _readHv;
    Surface _read, _line;            // the readout / its title line drawn alone (then copied into the bar's texture)
    bool _readDirty, _lineDirty;
    (int sec, int px, int tgt, int dur) _readKey;
    int _lineOff;                    // the marquee's step

    bool MediaPart => _theater && _mediaOn && Theme.Cyber;
    int MediaKey => MediaPart ? 50000 : 0;                // the layout key's part: with / without it
    int MediaW => MediaPart ? MReadW + MGap + MJumpW + MGap + MPlayW + MGap + MJumpW + Gap : 0;
    bool SeekMode => _m.CanSeek && _m.Timeline && _m.Dur >= 20;

    // every frame while the bar shows in theater mode (TheaterBar.Update): what plays read 4 times a second, a jump sent once the stick
    // rests, the readout's own update asked for when what it shows has moved on
    void MediaTick(double now)
    {
        bool fresh = now - _mTick > 0.5;           // the bar was away (or theater mode just started): what plays now, the title from its start
        _mTick = _mNow = now;
        if (fresh) _seekPend = 0;                  // (a jump the stick asked for just before the bar went is not sent when it comes back)
        if (ReadMedia == null || !Theme.Cyber) { _mediaOn = _mHas = false; return; }
        if (fresh || now >= _mReadAt)
        {
            _mReadAt = now + ReadEvery;
            var m = ReadMedia();
            if (m.Has)
            {
                if (now < _mPlayHold && m.Playing != _m.Playing) m.Playing = _m.Playing;     // a press just flipped it: the player's answer is coming
                bool text = !_mHas || !string.Equals(m.Title, _m.Title, StringComparison.Ordinal) || !string.Equals(m.Artist, _m.Artist, StringComparison.Ordinal);
                bool look = !_mHas || m.Playing != _m.Playing || m.CanSeek != _m.CanSeek || m.Timeline != _m.Timeline || (m.Dur >= 20) != (_m.Dur >= 20);
                // a jump just sent: shown where it went until the player says it is there (or 2 s on: then what it says)
                if (now < _seekShowUntil && Math.Abs(m.Pos - (_seekTo + (m.Playing ? now - _seekToT : 0))) < 2.5) _seekShowUntil = 0;
                _m = m;
                _mReadT = now;
                _mHas = true;
                if (text || fresh) { MediaText(); _mqT0 = now; }
                if (text || look) _dirty = true;
                if (fresh || _owner == null) _mediaOn = true;
            }
            else
            {
                if (_mHas) { _mHas = false; _mGoneAt = now; _dirty = true; _seekPend = 0; }
                if (fresh) _mediaOn = false;
            }
        }
        // gone for a while: the part goes (not under the laser - the bar's buttons must not slide away while you point at them)
        if (_mediaOn && !_mHas && now - _mGoneAt >= MediaGone && _owner == null) _mediaOn = false;
        if (_mHas && _mTextFor != Theme.Version * 1000 + Lang.Version) { MediaText(); _dirty = true; }
        // a jump: once the stick rests (a press: at once), to where the readout shows plus what was asked
        if (_seekPend != 0 && now >= _seekSendAt)
        {
            double to = Math.Clamp(ShownPos(now) + _seekPend, 0, Math.Max(0, _m.Dur - 1));
            _seekPend = 0;
            if (_mHas && SeekMode)
            {
                _seekTo = to; _seekToT = now; _seekShowUntil = now + 2;
                OnSeekTo?.Invoke(to);
                _mReadAt = Math.Min(_mReadAt, now + 0.4);
            }
        }
        if (!MediaPart) return;
        // what the readout shows now; another picture of it only when that moved on
        double pos = ShownPos(now);
        bool tl = _m.Timeline && _m.Dur > 0;
        int tgt = _seekPend != 0 && tl ? (int)Math.Clamp(pos + _seekPend, 0, _m.Dur) * 1000 + LinePx(pos + _seekPend) : -1;
        var key = (tl ? (int)pos : -1, tl ? LinePx(pos) : -1, tgt, tl ? (int)Math.Round(_m.Dur) : -1);
        if (key != _readKey) { _readKey = key; _readDirty = true; }
        int off = MarqueeOffset(now);
        if (off != _lineOff) { _lineOff = off; _lineDirty = true; }      // a scrolling step: the title line alone
    }

    // where in the track the readout shows: the reading run on while it plays, or a jump just sent
    double ShownPos(double now)
    {
        double run = _m.Playing && _mHas ? 1 : 0;
        double p = now < _seekShowUntil ? _seekTo + run * (now - _seekToT) : _m.Pos + run * (now - _mReadT);
        return Math.Clamp(p, 0, Math.Max(0, _m.Dur));
    }

    // where on the line a moment of the track is, in whole px (what is drawn and what says it moved on are the same number)
    int LinePx(double pos) => _m.Dur > 0 ? (int)MathF.Round(LineW * (float)Math.Clamp(pos / _m.Dur, 0, 1)) : 0;

    void SeekBy(double seconds, double rest)
    {
        _seekPend += seconds;
        _seekSendAt = _mNow + rest;
    }

    // ---------------- the words: made when the song, the look or the language changes ----------------

    string _mT, _mA, _mTFit, _mAFit;                 // the title and artist in capitals; cut to fit (not scrolling)
    float _mTW, _mAW, _mSepW, _mqLen;
    bool _mq;                                        // too long for its line: it scrolls (Look > Motion Full)
    int _mTextFor = -1;

    void MediaText()
    {
        _mTextFor = Theme.Version * 1000 + Lang.Version;
        _mT = string.IsNullOrEmpty(_m.Title) ? "" : Lang.Upper(_m.Title);
        _mA = string.IsNullOrEmpty(_m.Artist) ? "" : Lang.Upper(_m.Artist);
        _mTW = _p.MeasureWidth(_mT, TitleSize, FontWeight.Bold, CyberHud.Display);
        _mAW = _mA.Length > 0 ? CyberHud.MonoWidth(_p, _mA, ArtistSize) : 0;
        _mSepW = CyberHud.MonoWidth(_p, "//", ArtistSize) + 16;
        float all = _mTW + (_mA.Length > 0 ? _mSepW + _mAW : 0);
        _mq = all > LineW;
        _mqLen = MathF.Ceiling(all + MqGap);
        // cut: the title first; the artist in what is left (not at all in a sliver)
        if (_mTW > LineW) { _mTFit = Cut(_mT, LineW, TitleSize, FontWeight.Bold, CyberHud.Display); _mAFit = null; }
        else
        {
            _mTFit = _mT;
            float left = LineW - _mTW - _mSepW;
            _mAFit = _mA.Length > 0 && left >= 40 ? Cut(_mA, left, ArtistSize, FontWeight.SemiBold, CyberHud.Mono) : null;
        }
    }

    // s cut with "…" to maxW (halving the way there: a long title is measured a few times, once per song)
    string Cut(string s, float maxW, float size, FontWeight wt, string fam)
    {
        if (_p.MeasureWidth(s, size, wt, fam) <= maxW) return s;
        int lo = 1, hi = s.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (_p.MeasureWidth(s[..mid].TrimEnd() + "…", size, wt, fam) <= maxW) lo = mid;
            else hi = mid - 1;
        }
        int n = lo;
        if (n > 1 && char.IsHighSurrogate(s[n - 1])) n--;
        return s[..n].TrimEnd() + "…";
    }

    int MarqueeOffset(double now)
    {
        if (!_mq || !CyberFx.Full) return 0;
        double cyc = MqHold + _mqLen / MqSpeed, ph = (now - _mqT0) % cyc;
        return ph < MqHold ? 0 : Math.Min((int)_mqLen, (int)((ph - MqHold) * MqSpeed));
    }

    // "2:14 / 4:03" ("1:02:14 / 2:14:00" for a film), made when a second changes
    int _tSec = -1, _tDur = -1;
    string _tText;

    string TimeText(int sec, int dur)
    {
        if (sec != _tSec || dur != _tDur) { _tSec = sec; _tDur = dur; _tText = $"{Hms(sec, dur)} / {Hms(dur, dur)}"; }
        return _tText;
    }

    static string Hms(int s, int of) => of >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";

    // ---------------- drawing ----------------

    // the bar's full draw (TheaterBarCyber.cs): after the screens - the readout and the transport; the stick's steps on them. Returns
    // where the settings go.
    float MediaCyber(float x, float ch)
    {
        if (!MediaPart) return x;
        var rr = new RectangleF(x, Pad, MReadW, ch);
        _readAt = rr;
        _ui.Clickable("thb.mread", rr, out bool rh, out _);
        _readHv = _ui.Anim("thb.mread#h", rh && _mHas ? 1 : 0, 18);
        DrawReadout(rr.X, rr.Y);
        x += MReadW + MGap;
        bool seek = SeekMode, on = _mHas;
        var b0 = new RectangleF(x, Pad, MJumpW, ch);
        var b1 = new RectangleF(b0.Right + MGap, Pad, MPlayW, ch);
        var b2 = new RectangleF(b1.Right + MGap, Pad, MJumpW, ch);
        if (Transport("thb.mb", b0, seek ? CyRewind : CyPrev, seek ? "10" : null, false, on, ch))
        {
            if (seek) SeekBy(-10, 0);
            else _mCmd = "media.prev";
        }
        if (Transport("thb.mp", b1, _m.Playing && on ? CyPauseFill : CyPlayFill, null, true, on, ch))
        {
            _mCmd = "media.play";
            _m.Playing = !_m.Playing;                // at once on the bar; the player's answer follows
            _mPlayHold = _mNow + PlayHold;
            _readKey = default;
        }
        if (Transport("thb.mf", b2, seek ? CyForward : CyNext, seek ? "10" : null, false, on, ch))
        {
            if (seek) SeekBy(10, 0);
            else _mCmd = "media.next";
        }
        // the stick on any of it: 10 s a notch (sent once it rests), else a track at a time
        if (on && _ui.Wheel != 0 && _ui.OverNow(new RectangleF(rr.X, Pad, b2.Right - rr.X, ch)))
        {
            int n = (int)_ui.Wheel;
            if (seek) SeekBy(10 * n, StickRest);
            else if (n != 0 && _mNow >= _trackAt) { _mCmd = n > 0 ? "media.next" : "media.prev"; _trackAt = _mNow + TrackStep; }
        }
        return b2.Right + Gap;
    }

    // a transport plate: the jumps and the tracks cyan-edged (the "10" under the jump's arrows), play / pause the yellow one
    bool Transport(string id, RectangleF r, string glyph, string sub, bool main, bool enabled, float ch)
    {
        bool click = _ui.Clickable(id, r, out bool hover, out bool held) && enabled;
        float hv = _ui.Anim(Ui.Id(id, "#h"), hover && enabled ? 1 : 0, 18), pr = _ui.Anim(Ui.Id(id, "#p"), held && enabled ? 1 : 0, 30);
        var b = RectangleF.Inflate(r, -r.Width * 0.04f * pr, -ch * 0.04f * pr);
        if (main && enabled) Plate(b, 1, hv, CyberHud.Yellow, CyberHud.Yellow);
        else Plate(b, 0, hv, CyberHud.Cyan, enabled ? CyberHud.CyanDim : CyberHud.Line);
        uint ic = !enabled ? CyberHud.Faint : main ? CyberHud.Ink : CyberHud.Cyan;
        if (sub == null) _p.Icon(glyph, b, ic, main ? 22 : 18);
        else
        {
            _p.Icon(glyph, new RectangleF(b.X, b.Y + 6, b.Width, b.Height * 0.5f), ic, 17);
            CyberHud.MonoAt(_p, sub, b.X, b.Y + b.Height - 13, b.Width, enabled ? CyberHud.White : CyberHud.Faint, 11, FontWeight.Bold, TextAlignment.Center);
        }
        return click;
    }

    // after the full draw: a press's command
    void MediaAfter()
    {
        _readDirty = _lineDirty = false;             // the full draw drew the readout as it is
        if (_mCmd == null) return;
        string c = _mCmd;
        _mCmd = null;
        OnMedia?.Invoke(c);
    }

    // The readout - a dark well with the brackets (cyan while the laser is on it): NOW PLAYING // (cyan) or PAUSED // (yellow) and the
    // time; the title line; the line of where it is (yellow, its tick; a jump on its way: a cyan tick there, the time in cyan). Drawn
    // with its top-left at ox, oy: in place on the bar, or into its own picture (at 0, 0) for the update of it alone.
    void DrawReadout(float ox, float oy)
    {
        var g = new RectangleF(ox, oy, MReadW, _th - 2 * Pad);
        _p.FillCut(g, CyberHud.Page, 0, 0, 10, 0);
        _p.StrokeCut(RectangleF.Inflate(g, -0.6f, -0.6f), Theme.Mix(CyberHud.Line, CyberHud.CyanDim, _readHv), 1.25f, 0, 0, 9.7f, 0);
        CyberHud.Brackets(_p, g, Theme.Mix(CyberHud.Frame, CyberHud.Cyan, _readHv), 8, 3, 1.5f);
        float x0 = ox + 14, x1 = ox + MReadW - 14;
        bool playing = _m.Playing && _mHas;
        double pos = ShownPos(_mNow);
        bool tl = _m.Timeline && _m.Dur > 0;
        bool pend = _seekPend != 0 && tl;
        // the top row
        string code = playing || !_mHas ? "NOW PLAYING" : "PAUSED";
        uint cc = !_mHas ? CyberHud.Faint : playing ? CyberHud.Cyan : CyberHud.Yellow;
        float by = oy + 18;
        float cw = CyberHud.MonoWidth(_p, code, CodeSize, FontWeight.Bold);
        CyberHud.MonoAt(_p, code, x0, by, cw + 4, cc, CodeSize, FontWeight.Bold);
        float sx = x0 + cw + 6, sw = CyberHud.MonoWidth(_p, "//", CodeSize, FontWeight.Bold);
        CyberHud.MonoAt(_p, "//", sx, by, sw + 4, cc, CodeSize, FontWeight.Bold);
        if (tl)
        {
            double shown = pend ? Math.Clamp(pos + _seekPend, 0, _m.Dur) : pos;
            string t = TimeText((int)shown, (int)Math.Round(_m.Dur));
            float tw = CyberHud.MonoWidth(_p, t, CodeSize);
            if (x1 - tw >= sx + sw + 10)
                CyberHud.MonoAt(_p, t, x1 - tw - 2, by, tw + 4, !_mHas ? CyberHud.Faint : pend ? CyberHud.Cyan : CyberHud.Grey, CodeSize, FontWeight.SemiBold,
                    TextAlignment.Trailing);
        }
        DrawTitleLine(ox + LineX, oy + LineY);
        // where it is in the track
        if (tl)
        {
            var bar = new RectangleF(x0, oy + 51, LineW, 3);
            float fx = LinePx(pos);
            _p.Fill(bar, CyberHud.Track);
            uint lc = _mHas ? CyberHud.Yellow : CyberHud.Faint;
            _p.Fill(new RectangleF(bar.X, bar.Y, fx, bar.Height), lc);
            _p.Fill(new RectangleF(bar.X + fx - 1, bar.Y - 3, 2, bar.Height + 6), lc);
            if (pend)
            {
                float tx = LinePx(pos + _seekPend);
                float a = MathF.Min(fx, tx), w = MathF.Abs(tx - fx);
                _p.Fill(new RectangleF(bar.X + a, bar.Y, w, bar.Height), Theme.WithAlpha(CyberHud.Cyan, 0.45f));
                _p.Fill(new RectangleF(bar.X + tx - 1, bar.Y - 4, 2, bar.Height + 8), CyberHud.Cyan);
            }
        }
    }

    // The title line, its top-left at lx, ly (in the readout, or at 0, 0 in its own picture for a scrolling step): the title and the
    // artist - scrolling (the words going under the well's dark at its ends, not off a hard edge), or cut to fit
    void DrawTitleLine(float lx, float ly)
    {
        var line = new RectangleF(lx, ly, LineW, LineH);
        float tby = ly + 19;
        bool playing = _m.Playing && _mHas;
        uint tc = !_mHas ? CyberHud.Faint : playing ? CyberHud.White : Theme.Mix(CyberHud.White, CyberHud.Grey, 0.4f);
        if (_mq && CyberFx.Full)
        {
            int off = _lineOff;
            _p.PushClip(line);
            TitleRun(lx - off, tby, tc);
            if (lx - off + _mqLen < line.Right) TitleRun(lx - off + _mqLen, tby, tc);
            _p.PopClip();
            for (int i = 0; i < 3; i++)
            {
                uint fade = Theme.WithAlpha(CyberHud.Page, 0.8f - 0.25f * i);
                _p.Fill(new RectangleF(line.Right - 3 * (i + 1), line.Y, 3, line.Height), fade);
                if (off > 0) _p.Fill(new RectangleF(line.X + 3 * i, line.Y, 3, line.Height), fade);
            }
        }
        else
        {
            TitleText(_mTFit, lx, tby, tc);
            if (_mAFit != null) ArtistRun(lx + _mTW, tby);
        }
    }

    // the title, its baseline at by
    void TitleText(string s, float x, float by, uint c)
    {
        if (string.IsNullOrEmpty(s)) return;
        var (b, h) = _p.TextLine(TitleSize, FontWeight.Bold, CyberHud.Display);
        _p.Text(s, new RectangleF(x, by - b, _mTW + 8, h), c, TitleSize, FontWeight.Bold, TextAlignment.Leading, ParagraphAlignment.Near, CyberHud.Display);
    }

    // after the title: // and the artist (cut to what is left, unless it scrolls), on the title's baseline
    void ArtistRun(float x, float by)
    {
        string a = _mq && CyberFx.Full ? _mA : _mAFit;
        if (string.IsNullOrEmpty(a)) return;
        CyberHud.MonoAt(_p, "//", x + 8, by, _mSepW, CyberHud.Faint, ArtistSize, FontWeight.Bold);
        CyberHud.MonoAt(_p, a, x + _mSepW, by, _mAW + 8, _mHas ? CyberHud.Grey : CyberHud.Faint, ArtistSize, FontWeight.SemiBold);
    }

    void TitleRun(float x, float by, uint c)
    {
        TitleText(_mT, x, by, c);
        ArtistRun(x + _mTW, by);
    }

    // The readout alone drawn again and copied into the bar's texture (TheaterBar.Update, when nothing else changed): the rest of the bar
    // stays as it was. Its own picture starts as the bar's ground there (the console's fill) so the copy is the same pixels as drawing it
    // in place. A scrolling step alone: only the title line (on the well's dark, which is opaque). True: it was.
    bool MediaPartUpdate()
    {
        if (!(_readDirty || _lineDirty) || !MediaPart || _surf == null || _readAt.Width <= 0) return false;
        if (_readDirty)
        {
            int rh = _th - 2 * Pad;
            if (_read == null || _read.H != rh) { _read?.Dispose(); _read = new Surface(_g, MReadW, rh); }
            _p.Begin(_read.Bitmap, 0x00000000);
            _p.Fill(new RectangleF(0, 0, MReadW, rh), Theme.OverlayBg);
            DrawReadout(0, 0);
            _p.End();
            _g.Ctx.CopySubresourceRegion(_surf.Tex, 0, (uint)_readAt.X, (uint)_readAt.Y, 0, _read.Tex, 0, null);
        }
        else
        {
            _line ??= new Surface(_g, LineW, LineH);
            _p.Begin(_line.Bitmap, 0x00000000);
            _p.Fill(new RectangleF(0, 0, LineW, LineH), CyberHud.Page);
            DrawTitleLine(0, 0);
            _p.End();
            _g.Ctx.CopySubresourceRegion(_surf.Tex, 0, (uint)_readAt.X + LineX, (uint)_readAt.Y + LineY, 0, _line.Tex, 0, null);
        }
        _readDirty = _lineDirty = false;
        return true;
    }

    // ---------------- --snapshot (ORBIT_SNAP_ONLY=mediabar, MediaBarTest.cs) ----------------
    public void MediaTickForTest(double now) => MediaTick(now);
    public bool MediaPartForTest() { _readDirty = true; return MediaPartUpdate(); }
    public bool MediaLineForTest() { _readDirty = false; _lineDirty = true; return MediaPartUpdate(); }
    public bool MediaStepForTest() => MediaPartUpdate();         // as Update does: whatever moved on (nothing: false)
    public bool MediaPartPendingForTest => (_readDirty || _lineDirty) && MediaPart;
    public bool ReadoutPendingForTest => _readDirty && MediaPart;
    public RectangleF ReadoutForTest => _readAt;
    public Ui UiForTest => _ui;
    public int WidthForTest => _tw;
    public bool MediaShownForTest => MediaPart;
    public bool MarqueeForTest => _mq;
}
