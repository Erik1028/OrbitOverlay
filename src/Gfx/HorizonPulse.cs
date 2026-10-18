using System;

namespace Orbit;

// Cyber's neon horizon in theater mode (Erik 10-18: "work more on the Cyber style, mainly theater mode"): a thin neon line all the way
// round the night city's horizon that breathes with what the PC plays, and coming in, a spark running each way round it from the
// screen to behind you, lighting it as it goes. This is the arithmetic and the two textures; Panels/TheaterHorizon.cs puts them on
// their overlays.
//
// THE BREATHING. The level is the output device's own meter (every app together - AudioMixer.OutputPeak, one call a frame while the
// ring shows), in dB: -50 dB is nothing, -6 dB everything, a little steeper towards the top (speech stays low, music and explosions
// lift it). Smoothing matters more than reacting (Erik hates flicker): an envelope that rises in ~45 ms and falls in 0.55 s, a 30 ms
// low-pass after it so a drum hit swells instead of popping (together: up to 63 % in ~80 ms), and no frame moves the brightness by more
// than 0.03 (at 90 Hz) whatever the sound does. The brightness is a floor (never dark - it is the horizon) plus that much of the way
// to a cap (never blinding, never a light show round the film); Motion reduced: a calm still one, no meter read.
//
// THE SWEEP (Motion Full, as the city's lights come on): the sparks' angle from the screen over time - out from behind the screen at a
// steady ~90°/s so you see them leave its edges, faster round your sides, slowing to meet behind you after 1.8 s; they fade in as they
// clear the screen's edge and out as they meet. The ring is lit up to them at the cap and settles into its breathing over a second.
public sealed class HorizonPulse
{
    public const float Floor = 0.32f, Cap = 0.9f, Calm = 0.55f;     // the ring's brightness: silence, the loudest, Motion reduced
    public const float AttackS = 0.045f, ReleaseS = 0.55f, SmoothS = 0.03f;
    public const float LowDb = -50, HighDb = -6;
    public const float MaxStep = 0.03f;                              // the most a 1/90 s frame moves it (2.7 a second)

    float _env, _smooth, _bright = Floor;
    public float Env => _env;                // the envelope (0..1), its low-pass, the brightness now
    public float Smooth => _smooth;
    public float Bright => _bright;

    // theater left and came back: from the floor again (the sweep brings it up)
    public void Reset()
    {
        _env = _smooth = 0;
        _bright = Floor;
    }

    // a meter reading (0..1 linear peak; -1 / 0: nothing) as 0..1
    public static float Map(float peak)
    {
        if (!(peak > 1e-6f)) return 0;
        float x = Math.Clamp((20 * MathF.Log10(peak) - LowDb) / (HighDb - LowDb), 0, 1);
        return x * MathF.Sqrt(x);
    }

    // one frame: the reading, the frame's time; the brightness
    public float Step(float peak, float dt)
    {
        dt = Math.Clamp(dt, 0, 0.1f);
        float x = Map(peak);
        _env += (x - _env) * (1 - MathF.Exp(-dt / (x > _env ? AttackS : ReleaseS)));
        _smooth += (_env - _smooth) * (1 - MathF.Exp(-dt / SmoothS));
        if (x == 0 && _env < 1e-4f && _smooth < 1e-4f) _env = _smooth = 0;     // silence settles exactly: no calls for nothing
        float want = Floor + (Cap - Floor) * _smooth, max = MaxStep * 90 * dt;
        _bright += Math.Clamp(want - _bright, -max, max);
        if (MathF.Abs(want - _bright) < 1e-5f) _bright = want;
        return _bright;
    }

    // ---- the sweep ----
    public const float SweepS = 1.8f, SettleS = 1f;
    // the sparks' way round: (seconds, degrees from the screen, degrees a second there) - a monotone Hermite curve through them
    static readonly float[] KeyT = { 0, 0.35f, 0.75f, 1.25f, SweepS };
    static readonly float[] KeyA = { 0, 30, 64, 130, 180 };
    static readonly float[] KeyV = { 90, 90, 95, 150, 0 };

    // how far round each spark is, t seconds after setting off: 0 (the screen) .. π (behind you), radians
    public static float SweepAngle(float t)
    {
        if (t <= 0) return 0;
        if (t >= SweepS) return MathF.PI;
        int i = 0;
        while (i < KeyT.Length - 2 && t > KeyT[i + 1]) i++;
        float h = KeyT[i + 1] - KeyT[i], s = (t - KeyT[i]) / h, s2 = s * s, s3 = s2 * s;
        float deg = (2 * s3 - 3 * s2 + 1) * KeyA[i] + (s3 - 2 * s2 + s) * h * KeyV[i] + (-2 * s3 + 3 * s2) * KeyA[i + 1] + (s3 - s2) * h * KeyV[i + 1];
        return Math.Clamp(deg, 0, 180) * MathF.PI / 180;
    }

    // the sparks' strength: up as they clear the screen's edge (half: its half-angle seen from you), down as they meet behind you
    public static float SparkAlpha(float t, float angle, float half)
    {
        float fadeIn = Smooth01((angle - 0.5f * half) / (0.5f * half + 3 * MathF.PI / 180));
        float fadeOut = 1 - Smooth01((t - (SweepS - 0.12f)) / 0.5f);
        return t < 0 ? 0 : fadeIn * fadeOut;
    }

    // how much the ring is still "charged" by the sweep: 1 while the sparks run, easing to 0 over SettleS (its breathing then)
    public static float Charge(float t) => t < 0 ? 0 : t < SweepS ? 1 : 1 - CyberFx.EaseInOut((t - SweepS) / SettleS);

    static float Smooth01(float x) { x = Math.Clamp(x, 0, 1); return x * x * (3 - 2 * x); }

    // ---- the textures (straight into the pixels, premultiplied BGRA: soft profiles, no painter rounding) ----

    // the ring: as SteamVR makes its height from the texture (bounds included), 4096 x 64 round the circle is 2.8° above and below the
    // horizon; the same profile in every column - a white-pink filament, a pink tube's glow round it, a wide haze, nothing at the edges
    public const int RingW = 4096, RingH = 64;

    public static float RingRowDeg(float y) => MathF.Atan((0.5f - (y + 0.5f) / RingH) * 2 * MathF.PI * RingH / RingW) * 180 / MathF.PI;

    public static uint[] RingPixels()
    {
        var px = new uint[RingW * RingH];
        for (int y = 0; y < RingH; y++)
        {
            float d = MathF.Abs(RingRowDeg(y)), win = 1 - Smooth01((d - 2.0f) / 0.75f);
            float r = 0, g = 0, b = 0, a = 0;
            Layer(ref r, ref g, ref b, ref a, 0xFF2A6D, 0.28f * win * G(d, 1.1f));
            Layer(ref r, ref g, ref b, ref a, 0xFF4F8E, 0.6f * win * G(d, 0.36f));
            Layer(ref r, ref g, ref b, ref a, 0xFFE3EE, 0.95f * win * G(d, 0.10f));
            uint c = Pack(r, g, b, a);
            for (int x = 0; x < RingW; x++) px[y * RingW + x] = c;
        }
        return px;
    }

    // a spark: a small flat flare, 16° x 4° - a white core, a pink glow and a halo tall enough to hide where the lit arc ends behind it, a
    // long thin streak along the horizon with a cyan one just under it (Cyber's red / cyan split)
    public const int SparkW = 512, SparkH = 128;
    public const float SparkDeg = 16;

    public static uint[] SparkPixels()
    {
        var px = new uint[SparkW * SparkH];
        float hw = SparkDeg / 2, hh = SparkDeg * SparkH / SparkW / 2;
        for (int y = 0; y < SparkH; y++)
        {
            float ay = (0.5f - (y + 0.5f) / SparkH) * 2 * hh, wy = 1 - Smooth01((MathF.Abs(ay) - 0.75f * hh) / (0.25f * hh));
            for (int x = 0; x < SparkW; x++)
            {
                float ax = ((x + 0.5f) / SparkW - 0.5f) * 2 * hw, w = wy * (1 - Smooth01((MathF.Abs(ax) - 0.8f * hw) / (0.2f * hw)));
                float r = 0, g = 0, b = 0, a = 0;
                Layer(ref r, ref g, ref b, ref a, 0xFF2A6D, 0.45f * w * G(ax, 3f) * G(ay, 1.0f));
                Layer(ref r, ref g, ref b, ref a, 0xFFC2D8, 0.85f * w * G(ax, 6f) * G(ay, 0.07f));
                Layer(ref r, ref g, ref b, ref a, 0x48E5F0, 0.40f * w * G(ax, 3.5f) * G(ay + 0.26f, 0.06f));
                Layer(ref r, ref g, ref b, ref a, 0xFF8AB5, 0.85f * w * G(ax, 1.1f) * G(ay, 0.55f));
                Layer(ref r, ref g, ref b, ref a, 0xFFFFFF, w * G(ax, 0.32f) * G(ay, 0.22f));
                px[y * SparkW + x] = Pack(r, g, b, a);
            }
        }
        return px;
    }

    static float G(float d, float sigma) { float q = d / sigma; return MathF.Exp(-0.5f * q * q); }

    // one more layer over what is there (premultiplied "over": the colour never goes past the alpha)
    static void Layer(ref float r, ref float g, ref float b, ref float a, uint rgb, float al)
    {
        al = Math.Clamp(al, 0, 1);
        r = al * ((rgb >> 16) & 0xFF) / 255f + r * (1 - al);
        g = al * ((rgb >> 8) & 0xFF) / 255f + g * (1 - al);
        b = al * (rgb & 0xFF) / 255f + b * (1 - al);
        a = al + a * (1 - al);
    }

    static uint Pack(float r, float g, float b, float a)
    {
        uint A = (uint)Math.Clamp((int)MathF.Round(a * 255), 0, 255);
        uint C(float v) => Math.Min(A, (uint)Math.Clamp((int)MathF.Round(v * 255), 0, 255));
        return A << 24 | C(r) << 16 | C(g) << 8 | C(b);
    }
}
