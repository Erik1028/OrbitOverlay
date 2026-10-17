using System;
using System.Drawing;
using System.Numerics;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Orbit;

// Theater mode's Ambilight (Erik 10-18 picked it): a soft glow round the big screen in the colours of its picture's edges, on the dark
// wall behind it. This is the sampling and the glow's texture - no VR here, so --snapshot drives it with made-up pictures (AmbilightTest.cs);
// the overlay that shows it behind the screen is Theater's (TheaterAmbilight.cs).
//  - Sampling, on the GPU: the picture is drawn into a 32 x 18 grid with Direct2D's high-quality cubic (it first halves a big picture
//    down like mipmaps, so every pixel counts and fine text does not shimmer), the grid copied into one of three staging textures and
//    read a frame or more later without waiting (DoNotWait: not done yet = the next frame). No full-size picture ever comes to the CPU.
//  - The edge: the outer ring of cells (with a little of the next ring in), 100 colours round the screen - a little more saturated,
//    their brightness capped (a white picture must not wash the dimming out), eased towards each new reading over ~0.4 s, so a cut
//    does not flash and nothing flickers.
//  - The glow: a small texture (112 on its long side) round a hole of the screen's shape. Each texel takes the edge colours near it
//    along the edge, blurred more the farther out it is, fading to nothing at its rim - no blocks, no seams, round corners. The blur is
//    worked out once per shape (weights along the edge at 6 distances, 4 of them a texel), so a frame of easing is ~0.03 ms into the
//    same pixel array and one UpdateSubresource; at rest nothing is done at all. Deep behind the (opaque) screen nothing is drawn.
//  - The easing is not a Look > Motion animation: it is what keeps a cut from flashing, so it stays with Motion off too.
public sealed class Ambilight : IDisposable
{
    public const int GridW = 32, GridH = 18;
    public const int Edge = 2 * (GridW + GridH);    // the edge colours: top (left to right), right (down), bottom (left to right), left (down)
    const int Stages = 3;
    const int Long = 112;                           // the glow texture's long side
    const int Bins = 128, Levels = 6;               // the edge blurred: 128 places round it, at 6 distances out
    public const float MarginK = 0.25f;             // the glow reaches this x sqrt(w h) past the screen on every side (16:9: 1.38x as wide)
    const float CornerK = 0.35f;                    // the corners' share of the way round (of the margin): their colours turn over there
    const float Sigma0 = 1.0f, SigmaK = 0.8f;       // the blur along the edge: a cell's width at the screen, growing as it goes out
    const float Sat = 1.25f;                        // a little more colourful than the average of a cell
    public const float LumCap = 0.5f, MaxCap = 0.85f, Peak = 0.8f;   // the brightness cap; the glow's strength at the screen's edge
    public const float Tau = 0.14f;                 // easing time constant: 95 % of a change in ~0.42 s

    readonly Gpu _g;
    readonly Painter _p;
    Surface _grid;
    readonly ID3D11Texture2D[] _stage = new ID3D11Texture2D[Stages];
    readonly long[] _stageAt = { -1, -1, -1 };      // the frame a stage was filled in (-1: empty)
    readonly int[] _tries = new int[Stages];
    int _write;
    long _frame;
    readonly uint[] _cells = new uint[GridW * GridH];
    readonly Vector3[] _target = new Vector3[Edge], _eased = new Vector3[Edge];
    bool _have, _snap = true;

    public Ambilight(Gpu g, Painter p) { _g = g; _p = p; }

    // for the checks and the perf lines
    public int Sampled { get; private set; }        // pictures sent to the GPU
    public int Reads { get; private set; }          // readings that came back
    public int Waits { get; private set; }          // a stage not done yet when looked at (DoNotWait): taken a frame later
    public int Composed { get; private set; }       // glow textures written
    public bool HasReading => _have;
    public Vector3 Target(int k) => _target[k];
    public Vector3 Eased(int k) => _eased[k];
    public uint Cell(int x, int y) => _cells[y * GridW + x];

    // the edge colour k's sample: top 0..31, right 32..49, bottom 50..81, left 82..99
    public static int Top(int i) => i;
    public static int Right(int j) => GridW + j;
    public static int Bottom(int i) => GridW + GridH + i;
    public static int Left(int j) => 2 * GridW + GridH + j;

    // ---- sampling ----

    // the picture shrunk into the grid and copied for reading, on the GPU (nothing waits for it)
    public void Sample(Surface pic)
    {
        if (pic?.Bitmap == null) return;
        if (_grid == null)
        {
            _grid = new Surface(_g, GridW, GridH);
            for (int i = 0; i < Stages; i++)
                _stage[i] = _g.Device.CreateTexture2D(new Texture2DDescription
                {
                    Width = GridW, Height = GridH, MipLevels = 1, ArraySize = 1,
                    Format = Format.B8G8R8A8_UNorm, SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Staging, CPUAccessFlags = CpuAccessFlags.Read,
                });
        }
        _p.Begin(_grid.Bitmap, 0x00000000);
        _p.ImagePart(pic.Bitmap, new RectangleF(0, 0, GridW, GridH), new RectangleF(0, 0, pic.W, pic.H));
        _p.End();
        _g.Ctx.CopyResource(_stage[_write], _grid.Tex);     // (the main loop's flush at the end of the frame sends it: done by the next)
        _stageAt[_write] = _frame;
        _tries[_write] = 0;
        _write = (_write + 1) % Stages;
        Sampled++;
    }

    // once a frame: a reading that has come back becomes the new target; true when there was one
    public bool Poll()
    {
        long now = _frame++;                         // (a picture sampled this frame is looked at from the next one on)
        // the newest stage that is a frame old or more (an older one still waiting is dropped: the newer is better)
        int best = -1;
        for (int i = 0; i < Stages; i++)
            if (_stageAt[i] >= 0 && now - _stageAt[i] >= 1 && (best < 0 || _stageAt[i] > _stageAt[best])) best = i;
        if (best < 0) return false;
        var r = _g.Ctx.Map(_stage[best], 0, MapMode.Read, Vortice.Direct3D11.MapFlags.DoNotWait, out MappedSubresource map);
        if (r.Failure)
        {
            Waits++;
            if (++_tries[best] > 30) _stageAt[best] = -1;      // never came back (device trouble): let it go
            return false;
        }
        unsafe
        {
            try
            {
                for (int y = 0; y < GridH; y++)
                {
                    uint* row = (uint*)((byte*)map.DataPointer + y * map.RowPitch);
                    for (int x = 0; x < GridW; x++) _cells[y * GridW + x] = row[x];
                }
            }
            finally { _g.Ctx.Unmap(_stage[best], 0); }
        }
        for (int i = 0; i < Stages; i++) if (_stageAt[i] >= 0 && _stageAt[i] <= _stageAt[best]) _stageAt[i] = -1;
        Reading();
        Reads++;
        return true;
    }

    static Vector3 Rgb(uint c) => new Vector3((c >> 16) & 0xFF, (c >> 8) & 0xFF, c & 0xFF) * (1 / 255f);

    // the edge's colours from the grid: each a cell of the outer ring with a little of the one inside it
    void Reading()
    {
        for (int i = 0; i < GridW; i++)
        {
            _target[Top(i)] = Grade(Mix(i, 0, i, 1));
            _target[Bottom(i)] = Grade(Mix(i, GridH - 1, i, GridH - 2));
        }
        for (int j = 0; j < GridH; j++)
        {
            _target[Left(j)] = Grade(Mix(0, j, 1, j));
            _target[Right(j)] = Grade(Mix(GridW - 1, j, GridW - 2, j));
        }
        // the first reading (or the glow not showing yet): no easing from nothing; the same colours again: nothing to do
        if ((!_have || _snap) && (!_have || !Settled())) { Array.Copy(_target, _eased, Edge); _changed = true; }
        _have = true;
    }

    Vector3 Mix(int ox, int oy, int ix, int iy) => 0.65f * Rgb(_cells[oy * GridW + ox]) + 0.35f * Rgb(_cells[iy * GridW + ix]);

    static readonly Vector3 LumW = new(0.2126f, 0.7152f, 0.0722f);

    // a little more colourful, then the cap: no brighter than LumCap (white: half-grey light) and no channel over MaxCap
    public static Vector3 Grade(Vector3 c)
    {
        float lum = Vector3.Dot(c, LumW);
        c = Vector3.Clamp(new Vector3(lum) + (c - new Vector3(lum)) * Sat, Vector3.Zero, Vector3.One);
        lum = Vector3.Dot(c, LumW);
        if (lum > LumCap) c *= LumCap / lum;
        float mx = MathF.Max(c.X, MathF.Max(c.Y, c.Z));
        if (mx > MaxCap) c *= MaxCap / mx;
        return c;
    }

    // ---- easing ----

    bool _changed;

    // while the glow is not showing (or barely): the next reading is taken as it is, not eased into from the last one
    public void Snap(bool on)
    {
        _snap = on;
        if (on && _have && !Settled()) { Array.Copy(_target, _eased, Edge); _changed = true; }
    }

    bool Settled()
    {
        for (int k = 0; k < Edge; k++) if (_target[k] != _eased[k]) return false;
        return true;
    }

    // the colours a frame closer to the reading; true when the glow has to be written again (Compose)
    public bool Step(float dt)
    {
        if (!_have) return false;
        bool moved = _changed;
        _changed = false;
        float k = 1 - MathF.Exp(-Math.Max(0, dt) / Tau);
        const float Done = 0.5f / 255;
        for (int i = 0; i < Edge; i++)
        {
            var t = _target[i];
            var e = _eased[i];
            if (e == t) continue;
            var d = t - e;
            if (MathF.Abs(d.X) < Done && MathF.Abs(d.Y) < Done && MathF.Abs(d.Z) < Done) e = t;     // close enough: the last step, then rest
            else e += d * k;
            _eased[i] = e;
            moved = true;
        }
        return moved;
    }

    // ---- the glow: its shape, its texture ----

    public Surface Glow { get; private set; }
    public int TW { get; private set; }
    public int TH { get; private set; }
    public RectangleF Hole { get; private set; }        // the screen, in the glow's texels
    float _aspect = -1;
    uint[] _px;
    Vector4[] _blur;                                     // [level * Bins + bin]: the edge blurred along itself at that distance
    readonly Vector4[] _eased4 = new Vector4[Edge];
    int[] _wStart, _wIdx;                                // per level and bin: its weights over the edge colours
    float[] _wVal;
    int[] _tPx, _tIdx;                                   // the texels drawn, and for each the 4 blurred colours it is made of ...
    float[] _tW;                                         //  ... and how much of each (its strength in it)
    const float Inside = 3;                              // texels kept behind the screen's edge (looked at from the side)

    // the glow's shape for a screen this wide for its height; true when the texture is a new one. Rebuilt (~1 ms) only when that is
    // 2 % off: a window resized in theater mode does not rebuild it every frame (the hole is hidden behind the screen anyway)
    public bool Shape(float aspect)
    {
        aspect = Math.Clamp(float.IsFinite(aspect) ? aspect : 16 / 9f, 0.2f, 6f);
        if (Glow != null && MathF.Abs(aspect - _aspect) < 0.02f * _aspect) return false;
        _aspect = aspect;
        float w = 1, h = 1 / aspect, m = MarginK * MathF.Sqrt(w * h), gw = w + 2 * m, gh = h + 2 * m;
        int tw, th;
        if (gw >= gh) { tw = Long; th = Math.Max(16, (int)MathF.Round(Long * gh / gw)); }
        else { th = Long; tw = Math.Max(16, (int)MathF.Round(Long * gw / gh)); }
        float s = tw / gw;                               // texels a unit (square texels: the overlay's height is the texture's)
        float iw = w * s, ih = h * s, mt = m * s, x0 = (tw - iw) / 2, y0 = (th - ih) / 2;
        float a = CornerK * mt, len = 2 * iw + 2 * ih + 4 * a, binLen = len / Bins;
        Hole = new RectangleF(x0, y0, iw, ih);

        // where each edge colour sits along the way round (clockwise from the top-left corner; a corner's arc between the sides)
        var su = new float[Edge];
        for (int i = 0; i < GridW; i++)
        {
            su[Top(i)] = (i + 0.5f) / GridW * iw;
            su[Bottom(i)] = iw + 2 * a + ih + iw - (i + 0.5f) / GridW * iw;
        }
        for (int j = 0; j < GridH; j++)
        {
            su[Right(j)] = iw + a + (j + 0.5f) / GridH * ih;
            su[Left(j)] = 2 * iw + 3 * a + ih + ih - (j + 0.5f) / GridH * ih;
        }
        // the weights: a Gaussian along the edge, a cell wide at the screen and wider farther out
        float cell = (iw / GridW + ih / GridH) / 2;
        var idx = new System.Collections.Generic.List<int>(Levels * Bins * 24);
        var val = new System.Collections.Generic.List<float>(Levels * Bins * 24);
        var start = new int[Levels * Bins + 1];
        for (int l = 0; l < Levels; l++)
        {
            float sigma = Sigma0 * cell + SigmaK * (l / (float)(Levels - 1)) * mt, reach = 2.5f * sigma;
            for (int b = 0; b < Bins; b++)
            {
                start[l * Bins + b] = idx.Count;
                float u = (b + 0.5f) * binLen, sum = 0;
                int from = idx.Count;
                for (int k = 0; k < Edge; k++)
                {
                    float du = MathF.Abs(u - su[k]);
                    du = MathF.Min(du, len - du);          // round the corner where the way round closes
                    if (du > reach) continue;
                    float g = MathF.Exp(-du * du / (2 * sigma * sigma));
                    idx.Add(k); val.Add(g); sum += g;
                }
                for (int t = from; t < val.Count; t++) val[t] /= sum;
            }
        }
        start[Levels * Bins] = idx.Count;
        _wStart = start; _wIdx = idx.ToArray(); _wVal = val.ToArray();

        // every texel that shows: its distance out from the screen, where along the edge it is, how strong - as the 4 blurred edge
        // colours it is made of (2 places along the edge x 2 distances out) and their weights, its strength folded in. Past the rim
        // and deep behind the screen (it is opaque there; a strip just inside is kept for looking past its edge) nothing is drawn.
        int n = tw * th;
        var tPx = new System.Collections.Generic.List<int>(n);
        var tIdx = new System.Collections.Generic.List<int>(n * 4);
        var tW = new System.Collections.Generic.List<float>(n * 4);
        for (int py = 0; py < th; py++)
            for (int px = 0; px < tw; px++)
            {
                int i = py * tw + px;
                float X = px + 0.5f, Y = py + 0.5f;
                float dl = x0 - X, dr = X - (x0 + iw), du = y0 - Y, dd = Y - (y0 + ih);
                float dx = MathF.Max(0, MathF.Max(dl, dr)), dy = MathF.Max(0, MathF.Max(du, dd));
                float d = MathF.Sqrt(dx * dx + dy * dy), alpha = Falloff(d / mt);
                if (alpha <= 0 || MathF.Max(MathF.Max(dl, dr), MathF.Max(du, dd)) < -Inside) continue;
                float u;
                const float Q = MathF.PI / 2;
                if (dx <= 0 && dy <= 0)
                {
                    // inside (behind the screen): the nearest side's
                    float m0 = MathF.Min(MathF.Min(-dl, -dr), MathF.Min(-du, -dd));
                    u = m0 == -du ? X - x0 : m0 == -dr ? iw + a + (Y - y0) : m0 == -dd ? iw + 2 * a + ih + (x0 + iw - X) : 2 * iw + 3 * a + ih + (y0 + ih - Y);
                }
                else if (dx <= 0) u = du > 0 ? X - x0 : iw + 2 * a + ih + (x0 + iw - X);
                else if (dy <= 0) u = dr > 0 ? iw + a + (Y - y0) : 2 * iw + 3 * a + ih + (y0 + ih - Y);
                else if (du > 0 && dr > 0) u = iw + a * MathF.Atan2(dr, du) / Q;                          // top right: from the top round to the right
                else if (dd > 0 && dr > 0) u = iw + a + ih + a * MathF.Atan2(dd, dr) / Q;                 // bottom right
                else if (dd > 0) u = 2 * iw + 2 * a + ih + a * MathF.Atan2(dl, dd) / Q;                   // bottom left
                else u = 2 * iw + 3 * a + 2 * ih + a * MathF.Atan2(du, dl) / Q;                          // top left
                float fb = u / binLen - 0.5f;
                int b0 = (int)MathF.Floor(fb);
                float f = fb - b0;
                b0 = ((b0 % Bins) + Bins) % Bins;
                int b1 = b0 + 1 == Bins ? 0 : b0 + 1;
                float fl = Math.Clamp(d / mt, 0, 1) * (Levels - 1);
                int l0 = Math.Min((int)fl, Levels - 2);
                float g = fl - l0, k = alpha * Peak * 255;
                tPx.Add(i);
                tIdx.Add(l0 * Bins + b0); tW.Add((1 - f) * (1 - g) * k);
                tIdx.Add(l0 * Bins + b1); tW.Add(f * (1 - g) * k);
                tIdx.Add((l0 + 1) * Bins + b0); tW.Add((1 - f) * g * k);
                tIdx.Add((l0 + 1) * Bins + b1); tW.Add(f * g * k);
            }
        _tPx = tPx.ToArray(); _tIdx = tIdx.ToArray(); _tW = tW.ToArray();
        Retire();
        Retired = Glow;                                  // an overlay may still show it: freed once it has the new one (DropRetired)
        Glow = new Surface(_g, tw, th);
        TW = tw; TH = th;
        _px = new uint[n];
        _blur = new Vector4[Levels * Bins];
        _changed = true;
        return true;
    }

    // how strong the glow is t of the way out (0 at the screen, 1 at its rim): light falling off from the screen's edge, a long soft
    // tail, reaching nothing smoothly (no rim to see)
    public static float Falloff(float t)
    {
        if (t >= 1) return 0;
        if (t <= 0) return 1;
        float r = 1 - t;
        return r * r * (1 + t);
    }

    // the glow's texture from the eased colours, in place
    public unsafe void Compose()
    {
        if (Glow == null || !_have) return;
        // (pointers and Vector4s: no bounds checks, one SIMD multiply-add a colour - this runs every frame while the colours ease)
        for (int k = 0; k < Edge; k++) _eased4[k] = new Vector4(_eased[k], 0);
        int n = _tPx.Length;
        fixed (Vector4* blur = _blur, eased = _eased4)
        fixed (int* ws = _wStart, wi = _wIdx, at = _tPx, idx = _tIdx)
        fixed (float* wv = _wVal, w = _tW)
        fixed (uint* px = _px)
        {
            for (int i = 0, bins = Levels * Bins; i < bins; i++)
            {
                var c = Vector4.Zero;
                for (int t = ws[i], e = ws[i + 1]; t < e; t++) c += wv[t] * eased[wi[t]];
                blur[i] = c;
            }
            var half = new Vector4(0.5f);
            var top = new Vector4(255.49f);
            int* q = idx;
            float* qw = w;
            for (int i = 0; i < n; i++, q += 4, qw += 4)
            {
                var c = qw[0] * blur[q[0]] + qw[1] * blur[q[1]] + qw[2] * blur[q[2]] + qw[3] * blur[q[3]];
                c = Vector4.Min(c + half, top);
                // premultiplied, its alpha its strongest channel: light on the wall (black adds nothing, it does not darken)
                uint r = (uint)c.X, g = (uint)c.Y, b = (uint)c.Z;
                uint al = Math.Max(r, Math.Max(g, b));
                px[at[i]] = al << 24 | r << 16 | g << 8 | b;
            }
        }
        _g.Ctx.UpdateSubresource(_px, Glow.Tex, 0, (uint)TW * 4, 0, null);
        Composed++;
    }

    public uint[] Pixels => _px;

    // the last shape's texture, kept until the overlay shows the new one
    public Surface Retired { get; private set; }
    public void Retire() { Retired?.Dispose(); Retired = null; }

    public void Dispose()
    {
        _grid?.Dispose(); _grid = null;
        for (int i = 0; i < Stages; i++) { _stage[i]?.Dispose(); _stage[i] = null; _stageAt[i] = -1; }
        Retire();
        Glow?.Dispose(); Glow = null;
        _aspect = -1;
    }
}
