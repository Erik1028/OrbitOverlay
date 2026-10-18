using System;
using System.Collections.Generic;
using System.Numerics;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Orbit;

// Cyber's night city comes alive (Erik 10-18: "work more on the Cyber style, mainly theater mode") - calmly, and for next to nothing:
//  - windows: every few seconds one to three lit ones fade out and as many dark ones fade in, beside the screen where they are seen -
//    someone lives there (a gentle fade, never a blink);
//  - the neon signs: now and then (20-40 s apart, never two at once) ONE stutters like a failing tube - two partial dips, never below
//    45 %, never a strobe (2.5 dips a second at most);
//  - the antennas' red lights blink softly: on about a second in a 2 s beat, each its own, never quite off;
//  - a few new soft stars low over the skyline (where real ones twinkle most) twinkle very slightly - on the band's own empty sky, so
//    the big wall is never touched (its 1 px stars are left alone: a 1 px dot changing brightness is the shimmer the city had);
//  - two flying cars (a warm white head on a short streak, a red tail light) and a cyan drone glide slowly along the skyline off to
//    the sides, out of the haze and away again.
// How: the band texture is drawn once as before. Its lamps (every window - lit or dark - sign, antenna light, and the new stars) are
// recorded while it is drawn; the band is drawn once more without them and without the street haze (on a texture of its own), and the
// rows with lamps of both copied for reading: a frame later a thread of its own reads the patches out (nothing waits) and keeps them
// (half a MB). Then 5 times a second at most, only the lamps whose level changed get their few texels put together again on the CPU -
// the band under them, every lamp over that patch in drawing order (a later building's body still covers what it covered), the haze
// over all - and written with an UpdateSubresource each; the band is given to SteamVR again once. That is Direct2D's arithmetic, but the
// GPU rounds its blend its own way (a level off here and there): a patch whose lamps are all as drawn is put back from the drawn copy,
// so the city without life is the very texels it was drawn with (--snapshot checks it). The cars are tiny overlays of their own (soft
// textures drawn once), moved with SetAbsolute every frame in the city's level frame - no texture work a frame.
// Only while the city shows with Motion Full (CyberFx.Full): otherwise - Reduced, Off, theater gone, another style - the lamps go back
// to the city as drawn, the cars away, and nothing at all is done a frame. Coming in, the lamps start as drawn and the cars and stars
// fade in with the lights (the power-up stays as it was).
public sealed partial class Theater
{
    // ---- the lamps (DrawSkyline records them) ----
    struct Lamp
    {
        public byte Kind;                    // KWin / KSign / KAnt / KStar
        public short X, Y, W, H;             // its patch on the band: all of its fills
        public int F0, Fn;                   // its fills (_fills)
        public short CX0, CY0, CX1, CY1;     // the part of it a building drawn after it covers (left out of its fills); none: CX1 <= CX0
        public int House;                    // the building it belongs to (-1: none, a star)
        public float Static;                 // its level in the city as drawn: 1 lit, 0 dark (a dark window, a star)
        public float Lv, Vel;                // its level now, and how far it fades a tick (a window turning on / off)
        public byte Shown;                   // the level (in LevelSteps) its patch was last put together with
        public byte Phase;                   // antennas: where in the beat it starts; stars: their twinkle
        public bool Cpu;                     // its patch holds texels put together here (not the drawn ones: may be a level off)
        public int Base;                     // its patch of the band under the lamps (_lampBase) and as drawn (_lampDrawn)
        public int O0, On;                   // the lamps over its patch in drawing order, itself among them (_over)
    }

    // a rectangle of colour C (the style's brush colour, rgb) at alpha A, as Pm gives it to the painter
    struct LampRect { public short X, Y, W, H; public uint C; public float A; }

    const byte KWin = 0, KSign = 1, KAnt = 2, KStar = 3;
    const int LevelSteps = 32;               // a lamp's level is written in 1/32 steps
    const float LifeTick = 0.2f;             // the band's lamps change 5 times a second at most
    const int StarCount = 8;
    static readonly float[] Beat = { 1, 1, 1, 1, 0.8f, 0.45f, 0.25f, 0.25f, 0.45f, 0.8f };   // an antenna light, a tick each (2 s)
    static readonly float[][] Stutter = { new[] { 0.5f, 0.85f, 0.45f, 1 }, new[] { 0.6f, 0.95f, 0.5f, 0.8f, 1 } };     // a sign's dips

    readonly List<Lamp> _recL = new();
    readonly List<LampRect> _recF = new();
    readonly List<(int x, int top, int w, int bottom)> _recHouses = new();
    bool _recOn, _bare;                      // recording (the city's own draw) / drawing the band without its lamps and haze
    int _lampAt;                             // the fills of the lamp being recorded start here
    readonly uint[] _haze = new uint[BandH]; // the street haze over each row (its brush colour and alpha; 0: none)
    Lamp[] _lamps = Array.Empty<Lamp>();
    LampRect[] _fills = Array.Empty<LampRect>();
    int[] _over = Array.Empty<int>();
    int[] _wins = Array.Empty<int>(), _signs = Array.Empty<int>(), _ants = Array.Empty<int>(), _stars = Array.Empty<int>();
    uint[] _lampBase, _lampDrawn;            // the band under the lamps and as drawn, patch by patch (read back once)
    uint[] _patch = Array.Empty<uint>();     // one patch being put together
    int _baseLen, _lampY0, _lampY1, _covered2;

    // ---- city life ----
    bool _lifeOn, _lifeDirty;                // live now / a lamp in the texture is not as the city was drawn
    int _lifeState;                          // the band under the lamps: 0 not yet, 1 being copied, 3 being read out, 2 here
    Surface _lifeBare, _lifeSky;             // the band drawn without its lamps (until read back) / the band the lamps' levels are in
    ID3D11Texture2D _lifeStage;
    bool _lifeMapped;
    System.Threading.Thread _lifeJob;        // reads the patches out of the mapped copy (a few ms: the frames go on meanwhile)
    int _lifeCopied, _lifeWaits;
    float _lifeAcc, _lifeIn;                 // time to the next tick / the stars and cars fading in (0..1)
    long _tick, _nextWin, _nextBuzz, _buzzAt;
    int _buzz = -1, _buzzKind;               // the sign stuttering (-1: none) and its pattern
    uint _rng = 1018;
    float _bandA;                            // the band's alpha this frame (CyberWall): the cars fade with it
    public uint LifeSeed;                    // --snapshot: the same city every run (0: a new one each time theater starts)

    // what it did (--snapshot's checks; the log)
    public int LifeTicks, LifeSends, LifePatches, LifeBuzzes, LifeWinOn, LifeWinOff;
    public long LifeTexels;
    public double LifeTickMs, LifeTickMaxMs, LifeBuildMs, LampsEndMs;
    public int LifeMaxPatches, LifeMaxTexels;

    // ---- recording, while the band is drawn ----

    void LampsBegin(bool bare)
    {
        _bare = bare;
        _recOn = !bare;
        if (!_recOn) return;
        _recL.Clear();
        _recF.Clear();
        _recHouses.Clear();
        Array.Clear(_haze);
    }

    void LampHouse(int x, int top, int w, int bottom) { if (_recOn) _recHouses.Add((x, top, w, bottom)); }

    // a window: lit (as drawn) or dark (recorded only: city life may light it) - its faint halo and its pane
    void LampWindow(int wx, int wy, uint c, float al, bool lit)
    {
        _lampAt = _recF.Count;
        LampFill(wx - 1, wy - 1, 5, 6, c, al * 0.22f, lit);
        LampFill(wx, wy, 3, 4, c, al, lit);
        LampDone(KWin, lit ? 1 : 0);
    }

    // a neon sign down a building's side: its wide glow, its glow, the tube
    void LampSign(int sx, int sy, int sl, uint n)
    {
        _lampAt = _recF.Count;
        LampFill(sx - 3, sy - 3, 9, sl + 6, n, 0.12f, true);
        LampFill(sx - 1, sy - 1, 5, sl + 2, n, 0.22f, true);
        LampFill(sx, sy, 3, sl, n, 0.95f, true);
        LampDone(KSign, 1);
    }

    // an antenna's red light (its mast is part of the building)
    void LampAntenna(int ax, int ty)
    {
        _lampAt = _recF.Count;
        LampFill(ax - 2, ty - 2, 6, 5, 0xFFFF3B4F, 0.25f, true);
        LampFill(ax - 1, ty - 1, 4, 3, 0xFFFF3B4F, 1, true);
        LampDone(KAnt, 1);
    }

    // the street haze: two rows over everything (city life puts it back over a lamp it changes)
    void LampHaze(int y, int w, uint c, float a)
    {
        uint pm = Pm(c, a);
        if (!_bare) _p.Fill(new System.Drawing.RectangleF(0, y, w, 2), pm);
        if (_recOn) _haze[y] = _haze[y + 1] = Theme.Skin(pm);
    }

    void LampFill(int x, int y, int w, int h, uint c, float a, bool draw)
    {
        if (draw && !_bare) _p.Fill(new System.Drawing.RectangleF(x, y, w, h), Pm(c, a));
        if (_recOn) _recF.Add(new LampRect { X = (short)x, Y = (short)y, W = (short)w, H = (short)h, C = Theme.Skin(c | 0xFF000000) & 0xFFFFFF, A = a });
    }

    void LampDone(byte kind, float level)
    {
        if (!_recOn) return;
        _recL.Add(new Lamp { Kind = kind, F0 = _lampAt, Fn = _recF.Count - _lampAt, House = _recHouses.Count - 1, Static = level });
    }

    // the band is drawn: the new stars, then each lamp's patch, what covers it, what lies over it, its place in the read-back pixels
    void LampsEnd(int bw, int bh, float sunX, float sunR)
    {
        if (!_recOn) return;
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        _recOn = false;
        int mid = bh / 2;
        // the stars: low over the skyline (14.5-20° up, above every roof and antenna), nowhere near the sun, spread all the way round
        for (int i = 0; i < StarCount; i++)
        {
            float u = (i + 0.2f + 0.6f * CyberFx.Hash(i, 31)) / StarCount;
            int x = (int)(u * bw);
            float dx = MathF.Abs(x - sunX);
            dx = MathF.Min(dx, bw - dx);
            if (dx < sunR + 110) x = (int)((x + (sunR + 220)) % bw);
            int y = (int)MathF.Round(mid - BandRow(14.5f + 5.5f * CyberFx.Hash(i, 47)) * mid);
            uint c = CyberFx.Hash(i, 53) < 0.35f ? 0xFF9FE9FF : 0xFFE8E0F0;
            float a = 0.55f + 0.25f * CyberFx.Hash(i, 59);
            _lampAt = _recF.Count;
            _recOn = true;
            LampFill(x - 1, y, 4, 2, c, a * 0.3f, false);
            LampFill(x, y - 1, 2, 4, c, a * 0.3f, false);
            LampFill(x, y, 2, 2, c, a, false);
            _recL.Add(new Lamp { Kind = KStar, F0 = _lampAt, Fn = 3, House = -1, Static = 0, Phase = (byte)(255 * CyberFx.Hash(i, 61)) });
            _recOn = false;
        }
        _fills = _recF.ToArray();
        var lamps = _recL.ToArray();
        int n = lamps.Length, y0 = bh, y1 = 0, area = 0, big = 0;
        _covered2 = 0;
        for (int i = 0; i < n; i++)
        {
            ref var L = ref lamps[i];
            int l = bw, t = bh, r = 0, b = 0;
            for (int f = L.F0; f < L.F0 + L.Fn; f++)
            {
                var q = _fills[f];
                l = Math.Min(l, q.X); t = Math.Min(t, q.Y); r = Math.Max(r, q.X + q.W); b = Math.Max(b, q.Y + q.H);
            }
            l = Math.Max(0, l); t = Math.Max(0, t); r = Math.Min(bw, r); b = Math.Min(bh, b);
            L.X = (short)l; L.Y = (short)t; L.W = (short)Math.Max(1, r - l); L.H = (short)Math.Max(1, b - t);
            // a building drawn after this lamp's own covers what of it lies on its body (a sign's glow reaches one texel past its side)
            for (int k = L.House + 1; L.House >= 0 && k < _recHouses.Count && _recHouses[k].x < r; k++)
            {
                var hs = _recHouses[k];
                int cx0 = Math.Max(l, hs.x), cy0 = Math.Max(t, hs.top), cx1 = Math.Min(r, hs.x + hs.w), cy1 = Math.Min(b, hs.bottom);
                if (cx1 <= cx0 || cy1 <= cy0) continue;
                if (L.CX1 > L.CX0) { _covered2++; continue; }        // (never: a patch is at most a texel into the next one)
                L.CX0 = (short)cx0; L.CY0 = (short)cy0; L.CX1 = (short)cx1; L.CY1 = (short)cy1;
            }
            L.Base = area;
            area += L.W * L.H;
            big = Math.Max(big, L.W * L.H);
            y0 = Math.Min(y0, t); y1 = Math.Max(y1, b);
            L.Lv = L.Static;
            L.Shown = (byte)(L.Static * LevelSteps);
            if (L.Kind == KAnt) L.Phase = (byte)(Beat.Length * CyberFx.Hash(l, 7));
        }
        // the lamps over each patch (patches are small: a lamp sorted by its left edge only meets the few just before and after it)
        var byX = new int[n];
        for (int i = 0; i < n; i++) byX[i] = i;
        Array.Sort(byX, (a, b) => lamps[a].X != lamps[b].X ? lamps[a].X.CompareTo(lamps[b].X) : a.CompareTo(b));
        var over = new List<int>(n * 2);
        var near = new List<int>(16);
        int widest = 1;
        foreach (var L in lamps) widest = Math.Max(widest, L.W);
        for (int i = 0; i < n; i++)
        {
            ref var L = ref lamps[i];
            near.Clear();
            int lo = 0, hi = n;
            while (lo < hi) { int m = (lo + hi) / 2; if (lamps[byX[m]].X < L.X - widest) lo = m + 1; else hi = m; }
            for (int k = lo; k < n && lamps[byX[k]].X < L.X + L.W; k++)
            {
                int j = byX[k];
                ref var J = ref lamps[j];
                if (J.X + J.W > L.X && J.Y < L.Y + L.H && J.Y + J.H > L.Y) near.Add(j);
            }
            near.Sort();
            L.O0 = over.Count;
            L.On = near.Count;
            over.AddRange(near);
        }
        _over = over.ToArray();
        // the lamps of each kind (city life picks from them)
        int Count(byte k) { int c = 0; foreach (var L in lamps) if (L.Kind == k) c++; return c; }
        int[] Of(byte k) { var a = new int[Count(k)]; int c = 0; for (int i = 0; i < n; i++) if (lamps[i].Kind == k) a[c++] = i; return a; }
        _wins = Of(KWin); _signs = Of(KSign); _ants = Of(KAnt); _stars = Of(KStar);
        if (_lampBase != null && (area != _baseLen || y0 != _lampY0 || y1 != _lampY1)) { LifeFree(); _lampBase = _lampDrawn = null; _lifeState = 0; }
        _lamps = lamps;
        _baseLen = area;
        _lampY0 = y0;
        _lampY1 = y1;
        if (_patch.Length < big) _patch = new uint[big];
        _lifeDirty = false;                  // the band was just drawn as it is
        _lifeSky = _sky;
        _buzz = -1;
        _recL.Clear(); _recF.Clear();
        LampsEndMs = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
    }

    // ---- the band under the lamps: drawn once without them and read back with the band as drawn (a frame later: nothing waits) ----

    // true once it is here
    bool LifeBase()
    {
        if (_lifeState == 2) return true;
        if (_lamps.Length == 0) return false;
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_lifeState == 0)
        {
            LifeFree();
            _lifeBare = new Surface(_g, BandW, BandH);
            DrawSkyline(_lifeBare);
            // the rows with lamps: without them on top, the band as drawn under it (it is that while there is no base)
            int rows = _lampY1 - _lampY0;
            _lifeStage = _g.Device.CreateTexture2D(new Texture2DDescription
            {
                Width = BandW, Height = (uint)(2 * rows), MipLevels = 1, ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm, SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Staging, CPUAccessFlags = CpuAccessFlags.Read,
            });
            _g.Ctx.CopySubresourceRegion(_lifeStage, 0, 0, 0, 0, _lifeBare.Tex, 0, new Box(0, _lampY0, 0, BandW, _lampY1, 1));
            _g.Ctx.CopySubresourceRegion(_lifeStage, 0, 0, (uint)rows, 0, _sky.Tex, 0, new Box(0, _lampY0, 0, BandW, _lampY1, 1));
            _lifeState = 1;
            _lifeWaits = 0;
            LifeBuildMs = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            return false;
        }
        if (_lifeState == 1)
        {
            var r = _g.Ctx.Map(_lifeStage, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.DoNotWait, out MappedSubresource map);
            if (r.Failure)
            {
                if (++_lifeWaits > 300) { Log.Warn("theater city life: the band never came back for reading"); LifeFree(); _lifeState = 0; }
                return false;
            }
            // reading the patches out touches every page of the copy (~4 ms): a thread of its own does it while the copy stays mapped
            // (nothing else uses it), the frames go on
            _lifeMapped = true;
            _lampBase ??= new uint[_baseLen];
            _lampDrawn ??= new uint[_baseLen];
            LampsReadJob(map.DataPointer, map.RowPitch);
            _lifeState = 3;
            LifeBuildMs += System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            return false;
        }
        if (System.Threading.Volatile.Read(ref _lifeCopied) == 0) { _lifeWaits++; return false; }
        LifeFree();
        _lifeState = 2;
        LifeBuildMs += System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        Log.Info($"theater: the night city's {_lamps.Length} lamps ready ({_wins.Length} windows, {_signs.Length} signs, {_ants.Length} antennas, " +
                 $"{_stars.Length} stars; {_baseLen * 8 / 1024} KB) in {LifeBuildMs:0.0} ms of work, {_lifeWaits} frame(s) waited");
        return true;
    }

    // (its own method: its closure is made only here, not on every call of LifeBase)
    void LampsReadJob(IntPtr data, long pitch)
    {
        var (lamps, bse, drawn, y0, rows) = (_lamps, _lampBase, _lampDrawn, _lampY0, _lampY1 - _lampY0);
        _lifeCopied = 0;
        _lifeJob = new System.Threading.Thread(() =>
        {
            try { LampsRead(lamps, bse, drawn, y0, rows, data, pitch); }
            catch (Exception e) { Log.Error("theater city life: " + e); }
            System.Threading.Volatile.Write(ref _lifeCopied, 1);
        }) { IsBackground = true, Name = "theater city lamps" };
        _lifeJob.Start();
    }

    // the lamps' patches out of the copy: the band without them on top, as drawn under it
    static unsafe void LampsRead(Lamp[] lamps, uint[] bse, uint[] drawn, int y0, int rows, IntPtr data, long pitch)
    {
        foreach (var L in lamps)
            for (int y = 0; y < L.H; y++)
            {
                uint* row = (uint*)((byte*)data + (L.Y + y - y0) * pitch) + L.X;
                new ReadOnlySpan<uint>(row, L.W).CopyTo(bse.AsSpan(L.Base + y * L.W, L.W));
                row = (uint*)((byte*)data + (L.Y + y - y0 + rows) * pitch) + L.X;
                new ReadOnlySpan<uint>(row, L.W).CopyTo(drawn.AsSpan(L.Base + y * L.W, L.W));
            }
    }

    void LifeFree()
    {
        _lifeJob?.Join();                    // (only ever a few ms: it is let go when done)
        _lifeJob = null;
        if (_lifeMapped) { _g.Ctx.Unmap(_lifeStage, 0); _lifeMapped = false; }
        _lifeStage?.Dispose(); _lifeStage = null;
        _lifeBare?.Dispose(); _lifeBare = null;
    }

    // ---- a frame ----

    // after the wall (Theater.Update): the city's life while it shows (no SteamVR - --snapshot - drives it itself)
    void UpdateCityLife(float dt)
    {
        if (_dim == null) return;
        CityLife(dt, _band != null && _band.Visible);
    }

    void CityLife(float dt, bool shown)
    {
        bool live = shown && _cityOn && _sky != null && CyberFx.Full;
        if (!live)
        {
            if (_lifeOn || _lifeDirty || _carsUp) CityLifeOff();
            return;
        }
        if (!_lifeOn) LifeStart();
        _lifeIn = Math.Min(1, _lifeIn + dt / 2);
        if (_sky != _lifeSky) LampsAsDrawn();            // a new band texture (the style was away): it is the city as drawn
        if (LifeBase())
        {
            _lifeAcc += dt;
            if (_lifeAcc >= LifeTick)
            {
                _lifeAcc = Math.Min(_lifeAcc - LifeTick, LifeTick);      // a hitch: one tick, not a burst
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                int wrote = LifeTick1();
                double ms = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                LifeTickMs += ms;
                LifeTickMaxMs = Math.Max(LifeTickMaxMs, ms);
                if (wrote > 0) Send();
            }
        }
        Cars(dt);
    }

    void LifeStart()
    {
        _lifeOn = true;
        _rng = LifeSeed != 0 ? LifeSeed : (uint)Environment.TickCount | 1;
        _tick = 0;
        _lifeAcc = 0;
        _lifeIn = 0;
        _nextWin = 6 + RndInt(8);
        _nextBuzz = 40 + RndInt(60);         // the first stutter 8-20 s in
        _buzz = -1;
        foreach (int i in _signs) _lamps[i].Lv = 1;     // (one caught half way through a stutter when SteamVR went: lit)
        CarsStart();
    }

    // the city as it was drawn again, the cars away (once), then nothing
    void CityLifeOff()
    {
        if (_lifeDirty && _sky != null && _sky == _lifeSky && _lifeState == 2)
        {
            for (int i = 0; i < _lamps.Length; i++) { _lamps[i].Lv = _lamps[i].Static; _lamps[i].Vel = 0; }
            int wrote = 0;
            for (int i = 0; i < _lamps.Length; i++) if (LampQ(i) != _lamps[i].Shown || _lamps[i].Cpu) { LampWrite(i); wrote++; }
            if (wrote > 0) Send();
        }
        LampsAsDrawn();
        if (_lifeState == 1 || _lifeState == 3) { LifeFree(); _lifeState = 0; }      // (not read back yet: read it next time)
        _lifeOn = false;
        _buzz = -1;
        CarsOff();
    }

    // the lamps' levels as the city was drawn (the texture is, or has just become, that)
    void LampsAsDrawn()
    {
        for (int i = 0; i < _lamps.Length; i++)
        {
            ref var L = ref _lamps[i];
            L.Lv = L.Static; L.Vel = 0; L.Shown = (byte)(L.Static * LevelSteps); L.Cpu = false;
        }
        _lifeDirty = false;
        _lifeSky = _sky;
    }

    void Send()
    {
        _band?.SetTexture(_sky.Tex);         // the band given to SteamVR again: once a tick, only when a lamp changed
        LifeSends++;
    }

    // ---- a tick: the lamps' levels, then the patches of those that changed ----
    int LifeTick1()
    {
        _tick++;
        LifeTicks++;
        // windows: one to three lit ones go dark and as many dark ones light up, every 2.4-4.8 s, where they can be seen
        if (_tick >= _nextWin)
        {
            _nextWin = _tick + 12 + RndInt(13);
            int k = 1 + RndInt(3);
            for (int j = 0; j < k; j++)
            {
                int off = PickLamp(_wins, 1), on = PickLamp(_wins, 0);
                if (off < 0 || on < 0) break;
                _lamps[off].Vel = -1 / 5f;       // out over a second
                _lamps[on].Vel = 1 / 3f;         // on over 0.6 s
                LifeWinOff++; LifeWinOn++;
            }
        }
        // the signs: now and then one stutters
        if (_buzz < 0 && _tick >= _nextBuzz && _signs.Length > 0)
        {
            _buzz = PickLamp(_signs, 2);
            _buzzAt = _tick;
            _buzzKind = RndInt(Stutter.Length);
            LifeBuzzes++;
        }
        if (_buzz >= 0)
        {
            int s = (int)(_tick - _buzzAt);
            var pat = Stutter[_buzzKind];
            _lamps[_buzz].Lv = s < pat.Length ? pat[s] : 1;
            if (s >= pat.Length) { _buzz = -1; _nextBuzz = _tick + 100 + RndInt(101); }
        }
        foreach (int i in _ants)
        {
            ref var L = ref _lamps[i];
            L.Lv = 1 + (Beat[(int)((_tick + L.Phase) % Beat.Length)] - 1) * _lifeIn;
        }
        foreach (int i in _stars)
        {
            ref var L = ref _lamps[i];
            float t = _tick * LifeTick, ph = L.Phase / 255f * 6.283f;
            float tw = 0.6f * MathF.Sin(t * 6.283f / 3.7f + ph) + 0.4f * MathF.Sin(t * 6.283f / 5.3f + 2.1f * ph);
            L.Lv = (0.8f + 0.2f * tw) * _lifeIn;
        }
        foreach (int i in _wins)
        {
            ref var L = ref _lamps[i];
            if (L.Vel == 0) continue;
            L.Lv = Math.Clamp(L.Lv + L.Vel, 0, 1);
            if (L.Lv <= 0 || L.Lv >= 1) L.Vel = 0;
        }
        int wrote = 0, texels = 0;
        for (int i = 0; i < _lamps.Length; i++)
            if (LampQ(i) != _lamps[i].Shown) { texels += LampWrite(i); wrote++; }
        LifePatches += wrote;
        LifeTexels += texels;
        LifeMaxPatches = Math.Max(LifeMaxPatches, wrote);
        LifeMaxTexels = Math.Max(LifeMaxTexels, texels);
        return wrote;
    }

    int LampQ(int i) => (int)MathF.Round(Math.Clamp(_lamps[i].Lv, 0, 1) * LevelSteps);

    // a lamp's patch at the lamps' levels now, written into the band: with every lamp over it as drawn, the patch as drawn; else put
    // together - the band under it, every lamp over it in drawing order, the haze over all (the arithmetic Direct2D does for a filled
    // rectangle). cpu: put together whatever the levels (--snapshot). Its texels
    int LampWrite(int i, bool cpu = false)
    {
        ref var L = ref _lamps[i];
        int w = L.W, h = L.H, n = w * h;
        var px = _patch.AsSpan(0, n);
        bool drawn = !cpu;
        for (int k = L.O0; k < L.O0 + L.On && drawn; k++) drawn = LampQ(_over[k]) == (int)(_lamps[_over[k]].Static * LevelSteps);
        if (drawn) _lampDrawn.AsSpan(L.Base, n).CopyTo(px);
        else
        {
            _lampBase.AsSpan(L.Base, n).CopyTo(px);
            for (int k = L.O0; k < L.O0 + L.On; k++) LampDraw(_over[k], L.X, L.Y, w, h, px);
            for (int y = 0; y < h; y++)
            {
                uint hz = _haze[L.Y + y];
                if (hz != 0) for (int x = 0; x < w; x++) px[y * w + x] = Over(px[y * w + x], hz & 0xFFFFFF, hz >> 24);
            }
        }
        _g.Ctx.UpdateSubresource<uint>((ReadOnlySpan<uint>)px, _sky.Tex, 0, (uint)(w * 4), 0, new Box(L.X, L.Y, 0, L.X + w, L.Y + h, 1));
        L.Shown = (byte)LampQ(i);
        L.Cpu = !drawn;
        if (!drawn) _lifeDirty = true;
        return n;
    }

    void LampDraw(int j, int px0, int py0, int pw, int ph, Span<uint> px)
    {
        ref var J = ref _lamps[j];
        int q = LampQ(j);
        if (q <= 0) return;
        float lv = q / (float)LevelSteps;
        for (int f = J.F0; f < J.F0 + J.Fn; f++)
        {
            var F = _fills[f];
            uint a = (uint)Math.Clamp(F.A * lv * 255, 0, 255);           // Pm's alpha (Theme.WithAlpha)
            if (a == 0) continue;
            int x0 = Math.Max(F.X, px0), y0 = Math.Max(F.Y, py0), x1 = Math.Min(F.X + F.W, px0 + pw), y1 = Math.Min(F.Y + F.H, py0 + ph);
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    if (x >= J.CX0 && x < J.CX1 && y >= J.CY0 && y < J.CY1) continue;      // a later building's body is over it
                    int o = (y - py0) * pw + x - px0;
                    px[o] = Over(px[o], F.C, a);
                }
        }
    }

    // straight colour c at alpha a (0..255) over a premultiplied texel, rounded like the GPU
    static uint Over(uint d, uint c, uint a)
    {
        float A = a / 255f, k = 1 - A;
        uint Ch(int s) => (uint)(((c >> s) & 0xFF) * A + ((d >> s) & 0xFF) * k + 0.5f);
        uint al = (uint)(a + (d >> 24) * k + 0.5f);
        return Math.Min(255u, al) << 24 | Math.Min(255u, Ch(16)) << 16 | Math.Min(255u, Ch(8)) << 8 | Math.Min(255u, Ch(0));
    }

    // a lamp of the list (mode 0: a dark window at rest, 1: a lit one at rest, 2: any), beside the screen where it is seen if one of a
    // few tries finds one there; -1: none
    int PickLamp(int[] list, int mode)
    {
        if (list.Length == 0) return -1;
        float edge = FrontDeg(false);
        int any = -1;
        for (int k = 0; k < 16; k++)
        {
            int i = list[RndInt(list.Length)];
            ref var L = ref _lamps[i];
            bool ok = mode == 2 || (L.Vel == 0 && (mode == 1 ? L.Lv >= 1 : L.Lv <= 0));
            if (!ok) continue;
            float az = MathF.Abs(LampAz(i));
            if (az >= edge && az <= edge + 75) return i;
            if (any < 0) any = i;
        }
        return any;
    }

    // a lamp's azimuth (degrees: 0 the screen, + to its right)
    float LampAz(int i) => ((_lamps[i].X + _lamps[i].W / 2f) / BandW - 0.5f) * 360;

    // how far either side of the screen's middle it reaches (degrees), with its Ambilight glow (glow)
    float FrontDeg(bool glow)
    {
        if (_view == null) return 35;
        var ts = _screen?.TextureSize ?? new Vector2(16, 9);
        float w = _view.Size, h = w * ts.Y / Math.Max(1, ts.X);
        var c = _level.Translation;
        var p = _view.Place.Translation;
        float d = MathF.Max(0.5f, MathF.Sqrt((p.X - c.X) * (p.X - c.X) + (p.Z - c.Z) * (p.Z - c.Z)));
        float half = w / 2 + (glow ? Ambilight.MarginK * MathF.Sqrt(w * h) : 0);
        return MathF.Min(150, half / d * 180 / MathF.PI);
    }

    uint Rnd() { _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5; return _rng; }
    float Rnd01() => (Rnd() & 0xFFFFFF) / 16777216f;
    int RndInt(int n) => (int)(Rnd() % (uint)n);

    // ---- the flying cars and the drone ----
    // Tiny overlays of their own in the city's level frame, nearer than the band (0.05-0.25 m: nearer ones bigger, brighter, quicker) and
    // well behind the screen. Sort order 3, over the band by sort AND distance: at sort 2 only the distance would put a car over the band's
    // buildings, and the band's distance is its middle's, in front of you - leaning towards one side a car there could come out farther and
    // drop behind the buildings and back (a flicker). They never go near the screen and its glow (the glow's sort order is the same; the
    // screens' are 106+), so nothing else can cross them.
    struct Car
    {
        public bool Drone;
        public float T, Dur;                 // seconds into the trip (< 0: waiting for it), its length
        public float Az, Dir, Speed;         // where it starts (degrees, + right of the screen), which way, degrees a second
        public float El, Climb;              // degrees up, degrees a second
        public float Depth;                  // 0 far .. 1 near
        public float Phase;
    }

    const int CarCount = 3;                  // two cars and a drone
    const float CarDeg = 2.6f, DroneDeg = 0.8f;   // their textures' width seen from you (near; far ones 65 % of it)
    const int CarTexW = 52, CarTexH = 18, DroneTexW = 16;
    readonly Car[] _car = new Car[CarCount];
    readonly VrOverlay[] _carOv = new VrOverlay[CarCount];
    Surface _carTex, _carTexL, _droneTex;    // a car going right, going left; the drone
    readonly Surface[] _carSet = new Surface[CarCount];     // the texture each car's overlay has
    bool _carsUp, _carsFailed;
    public readonly Matrix4x4[] CarWorld = new Matrix4x4[CarCount];          // where each one is this frame, its alpha, width, radius
    public readonly float[] CarAlpha = new float[CarCount], CarWidth = new float[CarCount], CarRadius = new float[CarCount];
    public readonly float[] CarAz = new float[CarCount], CarEl = new float[CarCount];
    public readonly bool[] CarFlip = new bool[CarCount];

    void CarsStart()
    {
        for (int i = 0; i < CarCount; i++)
        {
            _car[i].Drone = i == CarCount - 1;
            NewTrip(ref _car[i]);
            _car[i].T = -(1 + 6 * i + 2 * Rnd01());      // they come one after another
        }
    }

    void NewTrip(ref Car c)
    {
        float f = FrontDeg(true) + (c.Drone ? DroneDeg : CarDeg) / 2 + 2;     // clear of the screen and its glow
        c.Depth = Rnd01();
        float span = c.Drone ? 22 + 16 * Rnd01() : 34 + 26 * Rnd01();
        c.Speed = c.Drone ? 0.7f + 0.5f * Rnd01() : (1.5f + 1.5f * Rnd01()) * (0.6f + 0.4f * c.Depth);
        float side = Rnd() % 2 == 0 ? -1 : 1, a0 = f + (175 - f) * Rnd01();
        bool away = Rnd() % 2 == 0 || a0 - span < f;                       // towards the screen only if it ends clear of it
        c.Az = side * a0;
        c.Dir = away ? side : -side;
        c.El = c.Drone ? 3 + 8 * Rnd01() : 1.8f + 7 * Rnd01();
        c.Climb = (Rnd01() - 0.5f) * 0.08f;
        c.Dur = span / c.Speed;
        c.Phase = Rnd01() * 6.283f;
        c.T = 0;
    }

    void Cars(float dt)
    {
        if (_dim != null && _carOv[0] == null && !_carsFailed) CarsCreate();
        _carsUp = true;
        for (int i = 0; i < CarCount; i++)
        {
            ref var c = ref _car[i];
            c.T += dt;
            if (c.T >= c.Dur)
            {
                NewTrip(ref c);
                c.T = -(c.Drone ? 8 + 12 * Rnd01() : 4 + 10 * Rnd01());
            }
            float a = 0, t = Math.Max(0, c.T);
            if (c.T > 0)
            {
                // out of the haze and away again (3 s each way), nearer ones brighter; the drone's light breathes slowly
                a = Smooth01(c.T / 3) * Smooth01((c.Dur - c.T) / 3) * (0.55f + 0.45f * c.Depth) * _bandA * _lifeIn;
                if (c.Drone) a *= 0.82f + 0.18f * MathF.Sin(c.Phase + c.T * 2.86f);
            }
            float az = c.Az + c.Dir * c.Speed * t, el = c.El + c.Climb * t + (c.Drone ? 0.15f * MathF.Sin(c.Phase + t * 1.7f) : 0);
            az -= 360 * MathF.Floor((az + 180) / 360);
            float r = _ringR - BandIn - 0.05f - 0.2f * c.Depth;
            float wdt = r * (c.Drone ? DroneDeg : CarDeg) * (0.65f + 0.35f * c.Depth) * MathF.PI / 180;
            var m = Matrix4x4.CreateTranslation(0, r * MathF.Tan(el * MathF.PI / 180), -r) * Matrix4x4.CreateRotationY(-az * MathF.PI / 180) * _level;
            CarWorld[i] = m; CarAlpha[i] = a; CarWidth[i] = wdt; CarRadius[i] = r; CarAz[i] = az; CarEl[i] = el; CarFlip[i] = c.Dir < 0;
            var ov = _carOv[i];
            if (ov == null) continue;
            if (a <= 0.004f) { ov.Hide(); continue; }
            var tex = c.Drone ? _droneTex : c.Dir < 0 ? _carTexL : _carTex;          // its head the way it goes
            if (_carSet[i] != tex) { ov.SetTexture(tex.Tex); _carSet[i] = tex; }     // (once a trip at most)
            ov.SetWidth(wdt);
            ov.SetAbsolute(m);
            ov.SetAlpha(Math.Min(1, a));
            ov.Show();
        }
    }

    void CarsOff()
    {
        foreach (var ov in _carOv) ov?.Hide();
        Array.Clear(CarAlpha);
        _carsUp = false;
    }

    // made in the middle of a frame: SteamVR refusing them (too many overlays) must not throw out of every frame - the city goes on without
    void CarsCreate()
    {
        try
        {
            CarTextures();
            for (int i = 0; i < CarCount; i++)
            {
                var ov = new VrOverlay($"erik.orbit.theater.car{i}", $"OrbitOverlay theater car {i}");
                _carOv[i] = ov;
                ov.SetSortOrder(3);
                ov.SetFlag(Valve.VR.VROverlayFlags.IsPremultiplied, true);
                _carSet[i] = null;
            }
        }
        catch (Exception e)
        {
            Log.Warn("theater car overlays: " + e.Message);
            DestroyCityLife();
            _carsFailed = true;
        }
    }

    // ---- the cars' textures: soft lights, drawn once (about 20 texels a degree, like the headset: nothing to shimmer as they move) ----
    void CarTextures()
    {
        if (_carTex == null)
        {
            var px = PaintCar();
            _carTex = new Surface(_g, CarTexW, CarTexH);
            _g.Ctx.UpdateSubresource(px, _carTex.Tex, 0, CarTexW * 4, 0, null);
            for (int y = 0; y < CarTexH; y++) Array.Reverse(px, y * CarTexW, CarTexW);
            _carTexL = new Surface(_g, CarTexW, CarTexH);
            _g.Ctx.UpdateSubresource(px, _carTexL.Tex, 0, CarTexW * 4, 0, null);
        }
        if (_droneTex == null)
        {
            _droneTex = new Surface(_g, DroneTexW, DroneTexW);
            _g.Ctx.UpdateSubresource(PaintDrone(), _droneTex.Tex, 0, DroneTexW * 4, 0, null);
        }
    }

    // a flying car seen from the side, going right: a warm white head light on a short streak that reddens back to the red tail light
    public static uint[] PaintCar()
    {
        var t = new float[CarTexW * CarTexH * 4];
        const float cy = CarTexH / 2f, hx = 42, tx = 15;
        for (int y = 0; y < CarTexH; y++)
            for (int x = 0; x < CarTexW; x++)
            {
                float px = x + 0.5f, py = y + 0.5f, dy = py - cy;
                int i = y * CarTexW + x;
                float u = Math.Clamp((px - 9) / (hx - 9), 0, 1);                  // the streak: 0 its back .. 1 the head
                if (px < hx) Lay(t, i, 1, 0.18f + 0.7f * u, 0.28f + 0.44f * u, 0.55f * MathF.Pow(u, 1.5f) * Soft(dy * dy, 0.9f));
                float dt2 = (px - tx) * (px - tx) + dy * dy, dh2 = (px - hx) * (px - hx) + dy * dy;
                Lay(t, i, 1, 0.16f, 0.24f, 0.3f * Soft(dt2, 2.5f));
                Lay(t, i, 1, 0.16f, 0.24f, Soft(dt2, 1.15f));
                Lay(t, i, 1, 0.92f, 0.78f, 0.45f * Soft(dh2, 3));
                Lay(t, i, 1, 0.97f, 0.9f, Soft(dh2, 1.3f));
            }
        return Bgra(t);
    }

    // the drone: a cyan light with its glow
    public static uint[] PaintDrone()
    {
        var t = new float[DroneTexW * DroneTexW * 4];
        const float c = DroneTexW / 2f;
        for (int y = 0; y < DroneTexW; y++)
            for (int x = 0; x < DroneTexW; x++)
            {
                float d2 = (x + 0.5f - c) * (x + 0.5f - c) + (y + 0.5f - c) * (y + 0.5f - c);
                int i = y * DroneTexW + x;
                Lay(t, i, 0.35f, 0.9f, 1, 0.38f * Soft(d2, 2.6f));
                Lay(t, i, 0.62f, 0.97f, 1, Soft(d2, 1.2f));
            }
        return Bgra(t);
    }

    static float Soft(float d2, float s) => MathF.Exp(-d2 / (2 * s * s));

    // straight colour (r, g, b) at alpha a over the premultiplied texel i
    static void Lay(float[] t, int i, float r, float g, float b, float a)
    {
        float k = 1 - a;
        t[i * 4] = r * a + t[i * 4] * k;
        t[i * 4 + 1] = g * a + t[i * 4 + 1] * k;
        t[i * 4 + 2] = b * a + t[i * 4 + 2] * k;
        t[i * 4 + 3] = a + t[i * 4 + 3] * k;
    }

    static uint[] Bgra(float[] t)
    {
        var px = new uint[t.Length / 4];
        for (int i = 0; i < px.Length; i++)
        {
            uint B(float v) => (uint)Math.Clamp((int)MathF.Round(v * 255), 0, 255);
            uint a = B(t[i * 4 + 3]);
            px[i] = a << 24 | Math.Min(a, B(t[i * 4])) << 16 | Math.Min(a, B(t[i * 4 + 1])) << 8 | Math.Min(a, B(t[i * 4 + 2]));
        }
        return px;
    }

    // ---- going ----

    // the cars' overlays (SteamVR going away; Theater.DestroyOverlay)
    void DestroyCityLife()
    {
        for (int i = 0; i < CarCount; i++) { _carOv[i]?.Dispose(); _carOv[i] = null; _carSet[i] = null; }
        _carsFailed = false;
        _carsUp = false;
        _lifeOn = false;                     // (the band's lamps are put back as drawn the next frame it shows without life)
    }

    void DisposeCityLife()
    {
        DestroyCityLife();
        LifeFree();
        _lifeState = 0;
        _lampBase = _lampDrawn = null;
        _carTex?.Dispose(); _carTex = null;
        _carTexL?.Dispose(); _carTexL = null;
        _droneTex?.Dispose(); _droneTex = null;
    }

    // ---- --snapshot (CityLifeTest.cs) ----

    // a frame of the city as in VR with the band showing at bandA (0: hidden) - without SteamVR: the cars' places worked out, not sent
    public void CityFrameForTest(float dt, float bandA)
    {
        if (_sky == null) SkylineForTest();
        _cityOn = true;
        if (_view != null)
        {
            var c = _level.Translation;
            _ringR = MathF.Max(Vector3.Distance(_view.Place.Translation, c), Vector3.Distance(_view.World.Translation, c)) + 0.6f;
        }
        _bandA = bandA;
        CityLife(dt, bandA > 0.004f);
    }

    public Surface BandForTest => _sky;
    public float BandRadiusForTest => _ringR - BandIn;
    public float FrontDegForTest(bool glow) => FrontDeg(glow);
    public Matrix4x4 LevelForTest => _level;
    public bool LifeOnForTest => _lifeOn;
    public bool LifeReadyForTest => _lifeState == 2;
    public bool LifeDirtyForTest => _lifeDirty;
    public int LampCount => _lamps.Length;
    public (int wins, int lit, int signs, int ants, int stars) LampKinds
    {
        get
        {
            int lit = 0;
            foreach (int i in _wins) if (_lamps[i].Static > 0) lit++;
            return (_wins.Length, lit, _signs.Length, _ants.Length, _stars.Length);
        }
    }
    public int LampsCoveredTwice => _covered2;
    public int LampsCovered { get { int n = 0; foreach (var L in _lamps) if (L.CX1 > L.CX0) n++; return n; } }
    public int LampBaseBytes => _baseLen * 4;
    public int BuzzingForTest => _buzz;
    public int LitNowForTest { get { int n = 0; foreach (int i in _wins) if (_lamps[i].Lv >= 0.5f) n++; return n; } }
    public float LampLevelForTest(int i) => _lamps[i].Lv;
    public int[] SignsForTest => _signs;
    public int[] AntennasForTest => _ants;
    public int[] StarsForTest => _stars;
    public int[] WindowsForTest => _wins;
    public System.Drawing.Rectangle LampRectForTest(int i) => new(_lamps[i].X, _lamps[i].Y, _lamps[i].W, _lamps[i].H);
    public int LampPatchMax { get { int m = 0; foreach (var L in _lamps) m = Math.Max(m, L.W * L.H); return m; } }

    // every lamp's patch written at its level - put together on the CPU (cpu), or as city life does it
    public void WriteAllLampsForTest(bool cpu)
    {
        for (int i = 0; i < _lamps.Length; i++) LampWrite(i, cpu);
    }

    // what drawing the band costs on the CPU (Direct2D's calls; the GPU idle before): as drawn, and without its lamps (city life's base)
    public double SkylineDrawMsForTest(bool bare)
    {
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        if (bare) { using var s = new Surface(_g, BandW, BandH); t0 = System.Diagnostics.Stopwatch.GetTimestamp(); DrawSkyline(s); }
        else DrawSkyline();
        return System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
    }

    // one sign made to stutter from the next tick on
    public void BuzzForTest(int sign)
    {
        _buzz = sign;
        _buzzAt = _tick + 1;
        _buzzKind = 0;
    }

    public uint[] CarPixelsForTest(bool drone) => drone ? PaintDrone() : PaintCar();
    // the cars' textures as SteamVR gets them: going right, going left, the drone
    public (Surface right, Surface left, Surface drone) CarTexturesForTest() { CarTextures(); return (_carTex, _carTexL, _droneTex); }
    public static (int w, int h, float deg) CarTexForTest(bool drone) => drone ? (DroneTexW, DroneTexW, DroneDeg) : (CarTexW, CarTexH, CarDeg);
}
