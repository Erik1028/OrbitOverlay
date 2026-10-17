using System;

namespace Orbit;

// The retro styles' theater backdrops (TheaterBackdrop.cs has the why and the rules). Era-true and our own: drawn from numbers, the
// look of the time - its palettes, its pixels, its dithering - never a copied wallpaper, screensaver or logo (Erik's rule for the
// retro styles: authentic era pixels, not a recolour). Each keeps the screen's surroundings the darkest and calmest part.
public sealed partial class Theater
{
    // ---- Windows 95: Starfield ----
    // Black space and crisp star pixels in the 16-colour palette's greys (dark grey, silver, white), the way the screensaver flies: small
    // and dim round the screen - the vanishing point, far away - bigger and whiter further out, as if they were passing you.
    sealed class StarfieldArt : Art
    {
        readonly float[] _vig = Columns(az => Gauss(az, 85));

        public override void Row(int y, float el, float[] a, float[] r, float[] g, float[] b)
        {
            float v = Gauss(el, 45);
            for (int x = 0; x < BackW; x++) a[x] = 0.80f + 0.12f * _vig[x] * v;      // black: no colour at all
        }

        public override void Sprites(uint[] px)
        {
            var rnd = new Random(1995);
            float s = MathF.Sin(FadeTo * MathF.PI / 180);
            for (int i = 0; i < 1700; i++)
            {
                float az = (float)rnd.NextDouble() * 360 - 180;
                float el = MathF.Asin(((float)rnd.NextDouble() * 2 - 1) * s) * 180 / MathF.PI;       // even over the sphere
                double pick = rnd.NextDouble(), big = rnd.NextDouble();
                if (MathF.Abs(az) < 31 && MathF.Abs(el) < 17) continue;             // behind the screen
                // how far out from the screen: 0 near it .. 1 beside / behind you
                float d = MathF.Acos(MathF.Cos(el * MathF.PI / 180) * MathF.Cos(az * MathF.PI / 180)) * 180 / MathF.PI;
                float near = Math.Clamp((d - 28) / 80, 0, 1);
                int w = big < 0.04 + 0.16 * near ? 2 : 1;
                // the palette's greys: 0x80 far away, 0xC0, 0xFF the close ones; round the screen only the dim ones
                float v = pick < 0.55 - 0.35 * near ? 0x80 / 255f : pick < 0.88 - 0.25 * near ? 0xC0 / 255f : 1;
                if (d < 40) v = Math.Min(v, 0xC0 / 255f) * (0.55f + 0.45f * (d - 25) / 15);
                Spot(px, az, el, w, v, v, v);
            }
        }
    }

    // ---- Windows XP: a Luna-blue night over rolling hills ----
    // XP's blue gone to night: a deep blue sky darkening upwards, a few faint stars, a crescent moon off to the side (Luna was the theme's
    // name) with its halo; two ranges of soft green hills round you, the far one bluer and lit along its top by the moon.
    sealed class HillsArt : Art
    {
        const float MoonAz = -82, MoonEl = 30, MoonR = 2.1f;
        readonly float[] _vig = Columns(az => Gauss(az, 85));
        // the hills' tops (deg): sums of whole waves round the circle (no seam behind you), gentle like the theme's hills
        readonly float[] _far = Columns(az =>
        {
            float t = az * MathF.PI / 180;
            return 4.6f + 2.3f * MathF.Sin(2 * t + 0.7f) + 1.5f * MathF.Sin(3 * t + 2.2f) + 0.5f * MathF.Sin(7 * t + 0.4f);
        });
        readonly float[] _near = Columns(az =>
        {
            float t = az * MathF.PI / 180;
            return 0.6f + 2.7f * MathF.Sin(t + 1.9f) + 1.7f * MathF.Sin(4 * t + 0.3f) + 0.6f * MathF.Sin(9 * t + 2.6f);
        });

        public override void Row(int y, float el, float[] a, float[] r, float[] g, float[] b)
        {
            float th = TexH(el), v = Gauss(el, 45);
            float up = Smooth01(el / 50);
            // the sky: deep Luna blue just over the hills, night navy up high
            float sr = 0.050f + (0.010f - 0.050f) * up, sg = 0.120f + (0.024f - 0.120f) * up, sb = 0.330f + (0.085f - 0.330f) * up;
            bool moonRow = MathF.Abs(el - MoonEl) < 16;
            for (int x = 0; x < BackW; x++)
            {
                float az = ColAz(x);
                a[x] = 0.84f + 0.10f * _vig[x] * v;
                float cr = sr, cg = sg, cb = sb;
                if (moonRow)
                {
                    float dx = (az - MoonAz) * MathF.Cos(el * MathF.PI / 180), dy = el - MoonEl, d = MathF.Sqrt(dx * dx + dy * dy);
                    float halo = 0.10f * Gauss(d, 8) + 0.06f * Gauss(d, 3.5f);
                    cr += 0.45f * halo; cg += 0.58f * halo; cb += 0.85f * halo;
                    if (d < MoonR + 1)
                    {
                        float ex = dx - 0.95f, ey = dy - 0.45f;                         // the dark of the crescent: the same disc, moved
                        float lit = Cover(MoonR - d, th) * (1 - Cover(MoonR * 0.96f - MathF.Sqrt(ex * ex + ey * ey), th));
                        cr += (0.93f - cr) * lit; cg += (0.95f - cg) * lit; cb += (1f - cb) * lit;
                    }
                }
                // the far hills: blue-green, a moonlit rim along the top
                float hf = _far[x] - el;
                if (hf > -1)
                {
                    float cf = Cover(hf, th), rim = 0.15f * Gauss(hf - 0.35f, 0.6f) * cf, fall = 1 - 0.35f * Smooth01(hf / 6);
                    cr += (0.050f * fall + rim * 0.8f - cr) * cf; cg += (0.135f * fall + rim - cg) * cf; cb += (0.150f * fall + rim * 1.2f - cb) * cf;
                }
                // the near hills: dark meadow green, darker down towards you
                float hn = _near[x] - el;
                if (hn > -1)
                {
                    float cn = Cover(hn, th), deep = 1 - 0.7f * Smooth01(hn / 22), rim = 0.06f * Gauss(hn - 0.3f, 0.5f) * cn;
                    cr += (0.032f * deep + rim * 0.6f - cr) * cn; cg += (0.118f * deep + rim - cg) * cn; cb += (0.050f * deep + rim * 0.9f - cb) * cn;
                }
                r[x] = cr; g[x] = cg; b[x] = cb;
            }
        }

        public override void Sprites(uint[] px)
        {
            var rnd = new Random(2001);
            for (int i = 0; i < 320; i++)
            {
                float az = (float)rnd.NextDouble() * 360 - 180, el = 9 + 66 * MathF.Pow((float)rnd.NextDouble(), 0.8f);
                float k = 0.25f + 0.55f * (float)rnd.NextDouble();
                if (MathF.Abs(az - MoonAz) < 14 && MathF.Abs(el - MoonEl) < 12) continue;   // not in the moon's glow
                if (MathF.Abs(az) < 40 && el < 26) k *= 0.5f;                               // calm round the screen
                Spot(px, az, el, 1, 0.80f, 0.86f, 1f, k);
            }
        }
    }

    // ---- Windows 7: dark glassy light ribbons ----
    // A deep blue dark with a few broad ribbons of light curving round you - cyan, azure, a little green, like an aurora seen through
    // glass: a soft glow, a brighter body and a thin bright rim along their top edge. Faint round the screen, they show off to the sides.
    sealed class AuroraArt : Art
    {
        readonly float[] _vig = Columns(az => Gauss(az, 85)), _calm = Columns(az => Gauss(az, 55));
        // centre = e0 + a1 sin(az + p1) + a2 sin(2 az + p2) (deg), width (deg), colour, where along it shows (env: 0..1 by azimuth)
        static readonly (float e0, float a1, float p1, float a2, float p2, float w, float r, float g, float b, float ep, float ea)[] Ribbons =
        {
            (16, 10, 0.4f, 4, 1.7f, 3.4f, 0.20f, 0.75f, 1.00f, 1.1f, 0.60f),
            (24, 13, 2.6f, 5, 0.2f, 5.0f, 0.30f, 0.50f, 1.00f, 3.0f, 0.65f),
            (11, 7, 4.4f, 3, 2.9f, 2.8f, 0.15f, 0.95f, 0.68f, 4.6f, 0.60f),
            (35, 9, 1.3f, 6, 4.1f, 6.5f, 0.55f, 0.42f, 1.00f, 0.2f, 0.45f),
        };
        readonly float[][] _c, _k, _env;

        public AuroraArt()
        {
            int n = Ribbons.Length;
            _c = new float[n][]; _k = new float[n][]; _env = new float[n][];
            for (int i = 0; i < n; i++)
            {
                var rb = Ribbons[i];
                _c[i] = Columns(az => Centre(rb, az));
                // across the ribbon, not straight down (it slopes): the vertical distance times cos of its slope
                _k[i] = Columns(az =>
                {
                    float s = Centre(rb, az + 0.5f) - Centre(rb, az - 0.5f);
                    return 1 / MathF.Sqrt(1 + s * s);
                });
                _env[i] = Columns(az =>
                {
                    float e = 0.5f + 0.5f * MathF.Sin(az * MathF.PI / 180 + rb.ep);
                    return rb.ea * e * e * (3 - 2 * e);
                });
            }
        }

        static float Centre((float e0, float a1, float p1, float a2, float p2, float w, float r, float g, float b, float ep, float ea) rb, float az)
        {
            float t = az * MathF.PI / 180;
            return rb.e0 + rb.a1 * MathF.Sin(t + rb.p1) + rb.a2 * MathF.Sin(2 * t + rb.p2);
        }

        public override void Row(int y, float el, float[] a, float[] r, float[] g, float[] b)
        {
            float v = Gauss(el, 45), up = Smooth01(MathF.Abs(el) / 45), calmEl = Gauss(el, 30);
            float sr = 0.028f + (0.006f - 0.028f) * up, sg = 0.075f + (0.016f - 0.075f) * up, sb = 0.190f + (0.050f - 0.190f) * up;
            float low = 1 - 0.4f * Smooth01(-el / 4);               // a little darker below the horizon (no edge at it)
            sr *= low; sg *= low; sb *= 0.25f + 0.75f * low;
            for (int x = 0; x < BackW; x++)
            {
                a[x] = 0.84f + 0.10f * _vig[x] * v;
                float cr = sr, cg = sg, cb = sb, calm = 1 - 0.7f * _calm[x] * calmEl;
                for (int i = 0; i < Ribbons.Length; i++)
                {
                    var rb = Ribbons[i];
                    float d = (el - _c[i][x]) * _k[i][x];
                    if (MathF.Abs(d) > rb.w * 3.2f) continue;
                    float glow = 0.13f * Gauss(d + 0.2f * rb.w, 1.7f * rb.w), body = 0.40f * Gauss(d, 0.7f * rb.w);
                    float rim = 0.5f * Gauss(d - 0.85f * rb.w, 0.4f);                    // the glass's bright edge
                    float l = (glow + body) * _env[i][x] * calm, e = rim * _env[i][x] * calm;
                    cr += rb.r * l + 0.75f * e; cg += rb.g * l + 0.88f * e; cb += rb.b * l + e;
                }
                r[x] = cr; g[x] = cg; b[x] = cb;
            }
        }
    }

    // ---- Classic Mac: a calm dark desktop ----
    // The Mac's own way of drawing: 1-bit patterns and dithering, every pixel one of a few greys. A dark slate desktop pattern for the
    // sky, a Platinum-grey haze over the horizon in ordered dither (no in-between greys) with a brighter Platinum line on it, Platinum
    // pinstripes below, pixel stars (dots and four-pointed ones) and a pixel crescent moon off to the right. One pattern pixel is 2 x 2
    // texels at the horizon and square seen from you up high too (its rows are Mercator's: they grow taller with the elevation exactly
    // as much as the circle round you narrows).
    sealed class PlatinumArt : Art
    {
        const float Cell = 360f / (BackW / 2);       // a pattern pixel (deg): a whole number of them round the circle (no seam)
        const float CellRad = Cell * MathF.PI / 180;
        const int MoonCx = (int)((78 + 180) / Cell), MoonCy = 46;        // the moon's middle (pattern pixels: 78° right, about 31° up)
        static readonly int[] Bayer =
        {
            0, 32, 8, 40, 2, 34, 10, 42, 48, 16, 56, 24, 50, 18, 58, 26, 12, 44, 4, 36, 14, 46, 6, 38, 60, 28, 52, 20, 62, 30, 54, 22,
            3, 35, 11, 43, 1, 33, 9, 41, 51, 19, 59, 27, 49, 17, 57, 25, 15, 47, 7, 39, 13, 45, 5, 37, 63, 31, 55, 23, 61, 29, 53, 21,
        };
        readonly float[] _vig = Columns(az => Gauss(az, 85)), _calm = Columns(az => Gauss(az, 50));

        // a pattern pixel's own number 0..1 (the stars: always the same ones)
        static float Hash(int cx, int cy)
        {
            uint h = (uint)cx * 374761393u + (uint)cy * 668265263u;
            h = (h ^ (h >> 13)) * 1274126177u;
            return (h ^ (h >> 16)) / 4294967296f;
        }
        static int MercRow(float el) => (int)MathF.Floor(MathF.Log(MathF.Tan(MathF.PI / 4 + el * MathF.PI / 360)) / CellRad);
        static readonly int Cy12 = MercRow(12), Cy24 = MercRow(24), Cx40 = (int)(40 / Cell);
        // a star's centre: a dot (1) or a four-pointed one (2), up from 12°, never round the screen
        static int Star(int cx, int cy)
        {
            if (cy < Cy12 || (Math.Abs(cx - BackW / 4) < Cx40 && cy < Cy24)) return 0;
            float h = Hash(cx, cy);
            return h < 0.0010f ? 2 : h < 0.0055f ? 1 : 0;
        }

        public override void Row(int y, float el, float[] a, float[] r, float[] g, float[] b)
        {
            float v = Gauss(el, 45), calmEl = Gauss(el, 28);
            int cy = MercRow(el);
            // the haze's strength at this height and the line's: each dithered in, in one grey
            float haze = el >= 0 ? 0.50f * Gauss(el, 15) : 0.40f * Gauss(el, 6);
            float line = 0.75f * Gauss(el, 2.2f);
            bool moonRow = Math.Abs(cy - MoonCy) <= 5, starRow = el > 10;
            for (int x = 0; x < BackW; x++)
            {
                a[x] = 0.84f + 0.10f * _vig[x] * v;
                int cx = x >> 1;
                float th = Bayer[(cy & 7) * 8 + (cx & 7)] / 64f + 0.5f / 64, calm = 1 - 0.5f * _calm[x] * calmEl;
                float cr, cg, cb;
                if (el >= 0)
                {
                    // the desk pattern: a lighter dot every 4 pixels, every other row shifted - the quiet "dots" kind
                    bool dot = ((cx & 3) == 0 && (cy & 3) == 0) || ((cx & 3) == 2 && (cy & 3) == 2);
                    (cr, cg, cb) = dot ? (0.105f, 0.115f, 0.165f) : (0.068f, 0.076f, 0.118f);
                }
                else
                    (cr, cg, cb) = (cy & 1) == 0 ? (0.052f, 0.052f, 0.064f) : (0.080f, 0.080f, 0.094f);      // the pinstripes
                if (haze * calm > th) (cr, cg, cb) = el >= 0 ? (0.200f, 0.200f, 0.235f) : (0.140f, 0.140f, 0.160f);
                if (line * calm > th) (cr, cg, cb) = (0.330f, 0.330f, 0.365f);
                if (starRow)
                {
                    int st = Star(cx, cy);
                    bool lit = st > 0 || Star(cx - 1, cy) == 2 || Star(cx + 1, cy) == 2 || Star(cx, cy - 1) == 2 || Star(cx, cy + 1) == 2;
                    if (lit) (cr, cg, cb) = st == 2 ? (0.86f, 0.86f, 0.90f) : st == 1 ? (0.62f, 0.62f, 0.68f) : (0.48f, 0.48f, 0.54f);
                }
                if (moonRow)
                {
                    // the crescent: a disc of 4.3 pixels less the same disc moved up and right
                    int di = cx - MoonCx, dj = cy - MoonCy;
                    if (di * di + dj * dj <= 18.5f && (di - 1.7f) * (di - 1.7f) + (dj - 1.1f) * (dj - 1.1f) > 13.5f)
                        (cr, cg, cb) = (0.86f, 0.86f, 0.90f);
                }
                r[x] = cr; g[x] = cg; b[x] = cb;
            }
        }
    }

    // ---- Terminal: green phosphor ----
    // The black glass of a green screen with vector graphics on it: two ranges of jagged mountains drawn in glowing phosphor lines round
    // you and the horizon line under them, a faint phosphor bloom along the horizon, and the tube's scanlines through all the glow.
    sealed class PhosphorArt : Art
    {
        readonly float[] _vig = Columns(az => Gauss(az, 85)), _calm = Columns(az => Gauss(az, 55));
        readonly float[] _m1, _k1, _m2, _k2, _r2, _q2;

        public PhosphorArt()
        {
            (_m1, _k1, _, _) = Range(new Random(1977), 24, 15, 7, 16);
            (_m2, _k2, _r2, _q2) = Range(new Random(1982), 16, 9, 9, 19);
        }

        // a mountain range's line round the circle: peaks (straight-sided tents, the way vectors drew them) standing on the horizon; the
        // top of all of them at each column and its across-the-line factor (1 / sqrt(1 + slope²)); and each peak's ridge, a line from its
        // top down its right flank to the foot (NaN where there is none) with its factor
        static (float[] m, float[] k, float[] r, float[] q) Range(Random rnd, int peaks, float hMax, float wMin, float wMax)
        {
            var p = new (float az, float h, float w)[peaks];
            for (int i = 0; i < peaks; i++)
                p[i] = (i * 360f / peaks + (float)rnd.NextDouble() * 9, hMax * (0.35f + 0.65f * (float)rnd.NextDouble()),
                        wMin + (wMax - wMin) * (float)rnd.NextDouble());
            float Top(float az)
            {
                float m = 0;
                foreach (var q in p)
                {
                    float d = MathF.Abs(((az - q.az) % 360 + 540) % 360 - 180);           // round the circle
                    m = MathF.Max(m, q.h * (1 - d / q.w));
                }
                return m;
            }
            float Ridge(float az)
            {
                foreach (var q in p)
                {
                    float d = ((az - q.az) % 360 + 540) % 360 - 180, run = 0.45f * q.w;
                    if (d < 0 || d > run) continue;
                    float e = q.h * (1 - d / run);
                    if (e < Top(az) - 0.4f) return e;            // inside the outline only
                }
                return float.NaN;
            }
            float[] Factor(float[] m)
            {
                var k = new float[BackW];
                for (int x = 0; x < BackW; x++)
                {
                    float a = m[(x + 1) % BackW], b = m[(x + BackW - 1) % BackW];
                    float s = float.IsNaN(a) || float.IsNaN(b) ? 0 : (a - b) / (2 * 360f / BackW);
                    k[x] = 1 / MathF.Sqrt(1 + s * s);
                }
                return k;
            }
            var mm = Columns(Top);
            var rr = Columns(Ridge);
            return (mm, Factor(mm), rr, Factor(rr));
        }

        public override void Row(int y, float el, float[] a, float[] r, float[] g, float[] b)
        {
            float v = Gauss(el, 45), calmEl = Gauss(el, 26);
            float bloom = 0.10f * Gauss(el, 14) + 0.06f * Gauss(el, 4);
            float horizon = 0.36f * Gauss(el, 0.36f) + 0.08f * Gauss(el, 1.4f);
            float scan = (y & 1) == 0 ? 1 : 0.55f;                 // the tube's scanlines: every other row dimmer
            bool lines = el > -4 && el < 22;                        // (the mountains' lines and their glow are all in here)
            for (int x = 0; x < BackW; x++)
            {
                a[x] = 0.86f + 0.10f * _vig[x] * v;
                float glass = 1 - 0.55f * _calm[x] * calmEl;
                if (!lines)
                {
                    float lb = bloom * glass * scan;
                    r[x] = 0.008f + 0.20f * lb; g[x] = 0.035f + lb; b[x] = 0.018f + 0.40f * lb;
                    continue;
                }
                // the lines: the near range, the far one dimmer and only above it (vectors hid what was behind), the horizon under them;
                // where two meet, the brighter one (no doubled glow)
                float d1 = (el - _m1[x]) * _k1[x], d2 = (el - _m2[x]) * _k2[x];
                float l2 = 0.48f * Gauss(d2, 0.36f) + 0.10f * Gauss(d2, 1.4f);
                if (!float.IsNaN(_r2[x]))                                                   // a ridge inside the near range
                {
                    float d3 = (el - _r2[x]) * _q2[x];
                    l2 = MathF.Max(l2, 0.26f * Gauss(d3, 0.36f) + 0.05f * Gauss(d3, 1.4f));
                }
                float l1 = el > _m2[x] + 0.3f ? 0.30f * Gauss(d1, 0.36f) + 0.06f * Gauss(d1, 1.4f) : 0;
                float line = MathF.Max(l2, MathF.Max(l1, horizon));
                float l = (bloom + line) * glass * scan;
                // the glass's black-green, the phosphor's green; hot where the lines are brightest
                float hot = MathF.Max(0, l - 0.32f);
                r[x] = 0.008f + 0.20f * l + 0.5f * hot; g[x] = 0.035f + 1.00f * l; b[x] = 0.018f + 0.40f * l + 0.4f * hot;
            }
        }

        public override void Sprites(uint[] px)
        {
            var rnd = new Random(1978);
            for (int i = 0; i < 140; i++)
            {
                float az = (float)rnd.NextDouble() * 360 - 180, el = 14 + 55 * (float)rnd.NextDouble();
                if (MathF.Abs(az) < 40 && el < 24) continue;
                Spot(px, az, el, 1, 0.20f, 1f, 0.40f, 0.25f + 0.35f * (float)rnd.NextDouble());
            }
        }
    }
}
