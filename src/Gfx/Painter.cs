using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Orbit;

static class Theme
{
    // Surface tones follow the chosen panel colour (Look tab); text colours are fixed. The modern watch draws with its
    // own tokens on top of these (Bento, derived in Apply).
    public static uint Bg = 0xFF15171C;
    public static uint Panel = 0xFF1D2027;
    public static uint Panel2 = 0xFF262A33;
    public static uint Panel3 = 0xFF30353F;
    public static uint Border = 0xFF353B47;
    public static uint OverlayBg = 0xEB15171C;   // wrist / keyboard background, with the chosen opacity
    public const uint Text = 0xFFE9EBEF;
    public const uint Dim = 0xFF9AA2AE;
    public const uint Faint = 0xFF5D6573;
    public static uint Accent = 0xFF45C1FF;      // the config's (Apply; the static constructor applies the default look)
    public static uint OnAccent = 0xFF0B1220;    // text on an accent fill
    public static uint Laser = 0xFF45C1FF;
    public const uint Accent2 = 0xFF6D5CFF;      // indigo (logo)
    public const uint Good = 0xFF4ADE80;
    public const uint Warn = 0xFFFBBF24;
    public const uint Bad = 0xFFF87171;
    public static int Version;                   // bumped on every appearance change

    // ---- the retro styles (Look > Style): Windows 95, XP, 7, Classic Mac, Terminal ----
    // Like Mixtape's: it swaps the whole token layer at once - surfaces, text, accent, corners, typeface -
    // so the call sites need no branch; only the places that draw a SHAPE 1995 drew differently (a raised
    // button, a sunken field, a navy caption, a checkbox) ask Classic. The modern look is untouched.
    // Classic is true for every retro style: they share the 95 code paths and layouts; Retro says which one, RetroSkin draws it
    // (src/Ui/Skins). The W95* colours are the current skin's palette (exactly the 1995 ones in Windows 95).
    public static bool Classic { get; private set; }
    public static RetroStyle Retro { get; private set; }
    public static RetroSkin RetroSkin { get; private set; } = Skins.Win95;    // never null: Windows 95 while Modern is on
    public static uint ClassicLaser => RetroSkin.Laser;   // "same as accent" in the retro styles: navy would vanish in a dark world
    public static uint W95Face => RetroSkin.Face;
    public static uint W95Light => RetroSkin.Light;
    public static uint W95Hi => RetroSkin.Hilight;
    public static uint W95Shadow => RetroSkin.Shadow;
    public static uint W95Dark => RetroSkin.DarkShadow;
    public static uint W95Navy => RetroSkin.Highlight;
    public static uint W95Teal => RetroSkin.Desktop;
    public static uint W95Window => RetroSkin.Window;
    public static string RetroFont => RetroSkin.Font;    // the typeface of every retro text

    // Look > Style "cyber" (CyberHud, src/Gfx/Cyber.cs): a modern style (Classic false - the modern layouts and code paths) with
    // its own token layer, type and cut corners; the watch's widgets have their Cyber designs (WristCyber.cs)
    public static bool Cyber { get; private set; }

    // a panel's outline: 25 % white (Cyber: the olive frame)
    public static uint PanelLine => Cyber ? CyberHud.Frame : 0x40FFFFFF;

    // the modern text colours are constants (used as default arguments); under the skin they are drawn as
    // the 1995 ones - RGB swapped, alpha kept; then the style's own colour map (Win95: none)
    public static uint Skin(uint c)
    {
        if (!Classic) return Cyber ? CyberHud.MapColor(c) : c;
        uint rgb = c & 0xFFFFFF, a = c & 0xFF000000;
        var sk = RetroSkin;
        return sk.MapColor(rgb switch
        {
            Text & 0xFFFFFF => a | (sk.WindowText & 0xFFFFFF),
            Dim & 0xFFFFFF => a | (sk.DimText & 0xFFFFFF),
            Faint & 0xFFFFFF => a | (sk.GrayText & 0xFFFFFF),
            _ => c,          // only the text greys: Good / Warn / Bad are also colours people pick (swatches)
        });
    }

    public static readonly uint[] AccentPresets =
        { 0xFF45C1FF, 0xFF4F8BFF, 0xFF8B5CF6, 0xFFF472B6, 0xFFF87171, 0xFFFB923C, 0xFFFBBF24, 0xFF4ADE80, 0xFF2DD4BF, 0xFFE5E7EB };
    public static readonly uint[] PanelPresets =
        { 0xFF15171C, 0xFF0F172A, 0xFF1E1A2E, 0xFF10201B, 0xFF26201A, 0xFF050506 };
    public static readonly string[] PanelNames = { "Charcoal", "Midnight", "Plum", "Forest", "Mocha", "Black" };

    public static void Apply(AppearanceConfig a)
    {
        Motion.Apply(a);
        Retro = Skins.Parse(a.Style);
        Classic = Retro != RetroStyle.None;
        Cyber = !Classic && a.Style == CyberHud.Id;
        RetroSkin = Skins.For(Retro);
        UserFont = a.Font != null && Array.IndexOf(FontsInstalled, a.Font) >= 0 ? a.Font : null;   // not on this PC: the default
        if (Cyber)
        {
            // its own palette and type: the accent, panel colour and Look > Font are Modern's (the opacity and laser apply)
            CyberHud.Apply(a);
            Version++;
            return;
        }
        if (Classic)
        {
            // one grey window face for every piece of chrome, white wells, black text, navy selection (in the style's palette)
            Bg = Panel = Panel2 = W95Face;
            Panel3 = W95Light;
            Border = W95Shadow;
            OverlayBg = W95Face;
            Accent = W95Navy;
            OnAccent = RetroSkin.HighlightText;
            Laser = a.LaserColor == 0 ? ClassicLaser : 0xFF000000 | (a.LaserColor & 0xFFFFFF);
            Version++;
            return;
        }
        uint pc = 0xFF000000 | (a.PanelColor & 0xFFFFFF);
        Bg = pc;
        Panel = Mix(pc, 0xFFFFFFFF, 0.035f);
        Panel2 = Mix(pc, 0xFFFFFFFF, 0.07f);
        Panel3 = Mix(pc, 0xFFFFFFFF, 0.12f);
        Border = Mix(pc, 0xFFFFFFFF, 0.15f);
        OverlayBg = WithAlpha(pc, Math.Clamp(a.PanelOpacity, 0.3f, 1f));
        Accent = 0xFF000000 | (a.Accent & 0xFFFFFF);
        OnAccent = Luma(Accent) > 0.55f ? 0xFF0B1220 : 0xFFFFFFFF;
        Laser = a.LaserColor == 0 ? Accent : 0xFF000000 | (a.LaserColor & 0xFFFFFF);
        Bento.Derive(pc, Accent);          // the watch's cards, tiles and tracks (modern only)
        Version++;
    }

    // the default look until the config is applied (tools that never apply one draw in it too)
    static Theme() => Apply(new AppearanceConfig());

    public static readonly (string name, uint accent, uint panel)[] ThemePresets =
    {
        ("Classic", 0xFF45C1FF, 0xFF15171C), ("Ocean", 0xFF38BDF8, 0xFF0F172A), ("Bento", 0xFFFF5D93, 0xFF0A0B10),
        ("Forest", 0xFF4ADE80, 0xFF10201B), ("Ember", 0xFFFB923C, 0xFF26201A), ("Mono", 0xFFE5E7EB, 0xFF050506),
    };

    public static uint Hsv(float h, float s, float v)
    {
        h = ((h % 360) + 360) % 360 / 60f;
        float c = v * s, x = c * (1 - MathF.Abs(h % 2 - 1)), m = v - c;
        (float r, float g, float b) = h switch
        {
            < 1 => (c, x, 0f), < 2 => (x, c, 0f), < 3 => (0f, c, x),
            < 4 => (0f, x, c), < 5 => (x, 0f, c), _ => (c, 0f, x),
        };
        uint B(float f) => (uint)Math.Clamp((f + m) * 255 + 0.5f, 0, 255);
        return 0xFF000000 | (B(r) << 16) | (B(g) << 8) | B(b);
    }

    public static float Hue(uint col)
    {
        float r = ((col >> 16) & 0xFF) / 255f, g = ((col >> 8) & 0xFF) / 255f, b = (col & 0xFF) / 255f;
        float max = MathF.Max(r, MathF.Max(g, b)), min = MathF.Min(r, MathF.Min(g, b)), d = max - min;
        if (d < 1e-4f) return 0;
        float h = max == r ? (g - b) / d % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h * 60 + 360) % 360;
    }

    // HSV saturation, 0..1
    public static float Sat(uint col)
    {
        float r = ((col >> 16) & 0xFF) / 255f, g = ((col >> 8) & 0xFF) / 255f, b = (col & 0xFF) / 255f;
        float max = MathF.Max(r, MathF.Max(g, b)), min = MathF.Min(r, MathF.Min(g, b));
        return max < 1e-4f ? 0 : (max - min) / max;
    }

    public static float Luma(uint c) =>
        (0.2126f * ((c >> 16) & 0xFF) + 0.7152f * ((c >> 8) & 0xFF) + 0.0722f * (c & 0xFF)) / 255f;

    // Windows 11 fonts; ResolveFonts falls back to the Windows 10 ones (same icon code points)
    public static string UiFont = "Segoe UI Variable Text";
    public static string DisplayFont = "Segoe UI Variable Display";
    public static string IconFont = "Segoe Fluent Icons";
    public const string IcoWindow = "\uE8A7";      // open in new window
    public const string IcoAdd = "\uE710";
    public const string IcoUndo = "\uE7A7", IcoRedo = "\uE7A6";
    public const string IcoNight = "\uE708";       // crescent moon (quiet hours)
    public const string IcoLayouts = "\uE8A9";     // grid of tiles (view all)
    public const string IcoDownload = "\uE896";
    public const string IcoDelete = "\uE74D";

    public static string ClassicFont = "Microsoft Sans Serif";   // the TrueType twin of MS Sans Serif

    public static void ResolveFonts(Vortice.DirectWrite.IDWriteFactory dw)
    {
        try
        {
            using var fonts = dw.GetSystemFontCollection(false);
            bool has(string f) => fonts.FindFamilyName(f, out _);
            if (!has(UiFont)) UiFont = "Segoe UI";
            if (!has(DisplayFont)) DisplayFont = "Segoe UI";
            if (!has(IconFont)) IconFont = "Segoe MDL2 Assets";
            if (!has(ClassicFont)) ClassicFont = "Tahoma";
            FontsInstalled = Array.FindAll(FontChoices, has);
            CyberHud.ResolveFonts(has);
        }
        catch { }
    }

    // Look > Font: typefaces that come with Windows 10 / 11 and read well small on a watch, offered when installed. The modern
    // style only - the 95 skin keeps its pixel font, icons their icon font. null = the default pair above (Segoe UI Variable).
    public static readonly string[] FontChoices =
        { "Bahnschrift", "Tahoma", "Trebuchet MS", "Calibri", "Candara", "Georgia", "Consolas", "Cascadia Mono", "Comic Sans MS" };
    public static string[] FontsInstalled { get; private set; } = FontChoices;     // narrowed by ResolveFonts
    public static string UserFont { get; private set; }                             // the chosen one (Apply), null = default
    public static string DefaultFontName => UiFont.Replace(" Text", "");

    // the typeface for UI text and big numbers in the current skin (Cyber: Bahnschrift's condensed widths - Look > Font is Modern's)
    public static string Font => Classic ? RetroFont : Cyber ? CyberHud.Text : UserFont ?? UiFont;
    public static string Display => Classic ? RetroFont : Cyber ? CyberHud.Display : UserFont ?? DisplayFont;

    // the watch draws in a typeface other than Segoe UI Variable (Look > Font, or Cyber's): its digits' ink is measured, not assumed
    public static bool OwnFont => UserFont != null || Cyber;

    // Segoe Fluent Icons code points
    public const string IcoKeyboard = "\uE765";
    public const string IcoMonitor = "\uE7F4";
    public const string IcoRecenter = "\uE72C";
    public const string IcoSettings = "\uE713";
    public const string IcoHide = "\uED1A";
    public const string IcoShow = "\uE890";
    public const string IcoWatch = "\uE916";
    public const string IcoGeneral = "\uE80F";
    public const string IcoBack = "\uE750";       // backspace
    public const string IcoEnter = "\uE751";
    public const string IcoShift = "\uE752";
    public const string IcoUp = "\uE70E";
    public const string IcoDown = "\uE70D";
    public const string IcoLeft = "\uE76B";
    public const string IcoRight = "\uE76C";
    public const string IcoWin = "\uE782";
    public const string IcoFolder = "\uE838";
    public const string IcoColor = "\uE790";
    public const string IcoLink = "\uE71B";
    public const string IcoCheck = "\uE73E";

    public static uint WithAlpha(uint c, float a) => ((uint)Math.Clamp(a * ((c >> 24) & 0xFF), 0, 255) << 24) | (c & 0xFFFFFF);

    public static uint Mix(uint a, uint b, float t)
    {
        uint ch(int s) => (uint)Math.Clamp(((a >> s) & 0xFF) * (1 - t) + ((b >> s) & 0xFF) * t, 0, 255);
        return (ch(24) << 24) | (ch(16) << 16) | (ch(8) << 8) | ch(0);
    }
}

// Thin drawing layer over the shared ID2D1DeviceContext with a font cache.
// Coordinates are logical; Begin() sets a scale so the same layout renders sharp at any size.
// One solid brush is recoloured per call: animations produce endless in-between colours, and a
// brush per colour used to pile up D2D objects for as long as the app ran.
public sealed class Painter : IDisposable
{
    readonly Gpu _g;
    ID2D1DeviceContext Dc => _g.D2D;
    ID2D1SolidColorBrush _brush;
    uint _brushColor;
    bool _drawing;
    readonly Dictionary<(string, float, FontWeight, string), float> _widths = new();
    readonly Dictionary<(string, float, FontWeight, TextAlignment, ParagraphAlignment, bool), IDWriteTextFormat> _formats = new();
    // Laid-out text, kept: DrawText lays its string out again on every call (~50 us each, 80 % of a watch frame).
    // Keyed by the format object too, so a format thrown away (the cap below) takes its layouts with it. 'fits': its ink is a pixel
    // or more inside its box - cut to the box (ClipText) it would look the same, so it is drawn without the clip.
    readonly Dictionary<(string, IDWriteTextFormat, float, float), (IDWriteTextLayout lay, bool fits)> _layouts = new();
    public int FormatCount => _formats.Count;
    public int WidthCount => _widths.Count;
    public int LayoutCount => _layouts.Count;
    public int LayoutsMade { get; private set; }        // text laid out so far (the perf tool: none a frame at rest is the goal)
    readonly Stack<int> _clips = new();
    public float Scale { get; private set; } = 1;

    public Painter(Gpu g) { _g = g; }

    public Gpu Gpu => _g;

    // ---- pixel mode: the Windows 95 look, drawn at real 1995 pixel size ----
    // Aliased shapes, monochrome hinted glyphs measured like GDI's - text the way MS Sans Serif sat on a
    // 640 x 480 screen. The canvas is then blown up with whole pixels (ImageNearest).
    public bool PixelMode { get; private set; }
    IDWriteRenderingParams _aliasedText;

    public void BeginPixel(ID2D1Image target, uint clear)
    {
        Begin(target, clear, 1);
        PixelMode = true;
        if (Theme.Classic) Theme.RetroSkin.BeginCanvas(this, target);       // the style's animations: which canvas this is
        Dc.AntialiasMode = AntialiasMode.Aliased;
        if (Theme.Classic && Theme.RetroSkin.SmoothText)
        {
            // a later Windows' grey-smoothed text (the style asks for it); shapes stay aliased
            Dc.TextAntialiasMode = Vortice.Direct2D1.TextAntialiasMode.Grayscale;
            Dc.TextRenderingParams = DefaultText;
            return;
        }
        Dc.TextAntialiasMode = Vortice.Direct2D1.TextAntialiasMode.Aliased;
        _aliasedText ??= _g.DWrite.CreateCustomRenderingParams(1.8f, 0f, 0f, PixelGeometry.Flat, RenderingMode.Aliased);
        Dc.TextRenderingParams = _aliasedText;
    }

    // ---- for the retro skins: shapes the 95 rules (square corners, no gradients in pixel mode) would flatten ----

    // smoothed edges for what follows (a skin's rounded or round shapes on the pixel canvas) until Smooth(false)
    public void Smooth(bool on) => Dc.AntialiasMode = on ? AntialiasMode.PerPrimitive : PixelMode || Theme.Classic ? AntialiasMode.Aliased : AntialiasMode.PerPrimitive;

    // a rounded rectangle whatever the style (Fill / Stroke square it in the retro styles)
    public void FillRound(RectangleF r, uint c, float radius) => Dc.FillRoundedRectangle(new RoundedRectangle(r, radius, radius), Brush(c));
    public void StrokeRound(RectangleF r, uint c, float width, float radius) => Dc.DrawRoundedRectangle(new RoundedRectangle(r, radius, radius), Brush(c), width);

    // a real gradient whatever the style (FillGradient is a flat fill in the retro styles); a -> b along angleDeg, radius > 0 rounded;
    // its colours through the style's MapColor like every brush
    public void GradientRaw(RectangleF r, uint a, uint b, float radius = 0, float angleDeg = 90)
    {
        var brush = Place(GradBrush(Theme.Skin(a), Theme.Skin(b), Theme.Skin(b), 2), r, angleDeg, 1);
        if (radius > 0) Dc.FillRoundedRectangle(new RoundedRectangle(r, radius, radius), brush);
        else Dc.FillRectangle(Raw(r), brush);
    }

    // three stops: a, b half-way, c
    public void GradientRaw(RectangleF r, uint a, uint b, uint c, float radius = 0, float angleDeg = 90)
    {
        var brush = Place(GradBrush(Theme.Skin(a), Theme.Skin(b), Theme.Skin(c), 3), r, angleDeg, 1);
        if (radius > 0) Dc.FillRoundedRectangle(new RoundedRectangle(r, radius, radius), brush);
        else Dc.FillRectangle(Raw(r), brush);
    }

    // a bitmap's alpha as a mask filled with c (PixelFont's glyphs); needs aliased shapes (pixel mode)
    public void FillMask(ID2D1Bitmap mask, RectangleF dst, RectangleF src, uint c) => Dc.FillOpacityMask(mask, Brush(c), Raw(dst), Raw(src));

    // the 50 % dither of 1995 (a latched toolbar button, a scroll bar's track): alternate pixels of two colours
    ID2D1BitmapBrush _checker;
    (uint, uint) _checkerColors;

    public void FillChecker(RectangleF r, uint a = 0xFFFFFFFF, uint b = 0xFFC0C0C0)
    {
        if (_checker == null || _checkerColors != (a, b))
        {
            _checker?.Dispose();
            using var bmp = CreateBitmap(new[] { a, b, b, a }, 2, 2);
            _checker = Dc.CreateBitmapBrush(bmp, new BitmapBrushProperties
            {
                ExtendModeX = ExtendMode.Wrap, ExtendModeY = ExtendMode.Wrap,
                InterpolationMode = Vortice.Direct2D1.BitmapInterpolationMode.NearestNeighbor,
            });
            _checkerColors = (a, b);
        }
        Dc.FillRectangle(Raw(r), _checker);
    }

    // r made see-through again (a style's opening animation on a canvas that was cleared to a colour)
    public void ClearRect(RectangleF r)
    {
        PushClip(r);
        Dc.Clear(ToColor(0));
        PopClip();
    }

    // a little pixel pattern ('k' = a pixel in colour c) - the check mark, the caption buttons' glyphs
    public void Pixels(string[] map, float x, float y, uint c)
    {
        for (int row = 0; row < map.Length; row++)
            for (int col = 0; col < map[row].Length; col++)
                if (map[row][col] == 'k') Fill(new RectangleF(x + col, y + row, 1, 1), c);
    }

    // a bitmap at whole-pixel magnification, no smoothing
    public void ImageNearest(ID2D1Bitmap bmp, RectangleF dst) =>
        Dc.DrawBitmap(bmp, Raw(dst), 1, Vortice.Direct2D1.BitmapInterpolationMode.NearestNeighbor, null);

    public void Begin(ID2D1Image target, uint clear, float scale = 1)
    {
        if (_drawing) End();        // a previous frame threw half-way: close it before starting another
        _drawing = true;
        Scale = scale;
        Dc.Target = _target = target;
        Dc.BeginDraw();
        Dc.Transform = Matrix3x2.CreateScale(scale);
        // 1995 had no anti-aliasing: the skin's bevels and squares stay pixel-sharp
        Dc.AntialiasMode = Theme.Classic ? AntialiasMode.Aliased : AntialiasMode.PerPrimitive;
        PixelMode = false;
        Dc.TextAntialiasMode = Vortice.Direct2D1.TextAntialiasMode.Grayscale;     // what Gpu sets up (1.3.3)
        Dc.TextRenderingParams = DefaultText;
        Dc.Clear(ToColor(clear));
    }

    // The default text rendering (what null gives: the primary monitor's), made once - with null set, Direct2D works it out again at
    // the first text of every frame: 0.4 ms a frame on Erik's PC, a third of a settings frame
    IDWriteRenderingParams _defaultText;
    IDWriteRenderingParams DefaultText => _defaultText ??= _g.DWrite.CreateRenderingParams();

    public void End()
    {
        EndAside();
        _drawing = false;
        while (_fades > 0) PopFade();
        while (_transforms.Count > 0) PopScale();
        while (_clips.Count > 0) PopClip();
        while (_layers.Count > 0) PopRoundedClip();
        var r = Dc.EndDraw();
        Dc.Target = _target = null;
        if (r.Failure) Log.Warn("EndDraw: " + r);
    }

    // ---- a picture of its own in the middle of a frame (the settings' console at rest, kept: SettingsCyber.cs) ----
    // What is drawn until EndAside goes into 'target' (cleared, at this frame's scale); then the frame goes on where it was. Only with
    // nothing pushed (no clip, layer, fade or transform): false - draw as usual.
    ID2D1Image _target;              // what Begin draws into
    bool _aside;
    Matrix3x2 _asideTransform;

    public bool BeginAside(ID2D1Image target)
    {
        if (!_drawing || _aside || _clips.Count > 0 || _layers.Count > 0 || _fades > 0 || _transforms.Count > 0) return false;
        _aside = true;
        _asideTransform = Dc.Transform;
        Dc.Target = target;
        Dc.Clear(ToColor(0));
        return true;
    }

    public void EndAside()
    {
        if (!_aside) return;
        while (_fades > 0) PopFade();
        while (_transforms.Count > 0) PopScale();
        while (_clips.Count > 0) PopClip();
        while (_layers.Count > 0) PopRoundedClip();
        Dc.Target = _target;
        Dc.Transform = _asideTransform;
        _aside = false;
    }

    static Color4 ToColor(uint c) => new(((c >> 16) & 0xFF) / 255f, ((c >> 8) & 0xFF) / 255f, (c & 0xFF) / 255f, ((c >> 24) & 0xFF) / 255f);

    ID2D1SolidColorBrush Brush(uint c) => RawBrush(Theme.Skin(c));

    // the brush in exactly this colour (no style's colour map)
    ID2D1SolidColorBrush RawBrush(uint c)
    {
        if (_brush == null) { _brush = Dc.CreateSolidColorBrush(ToColor(c)); _brushColor = c; }
        else if (c != _brushColor) { _brush.Color = ToColor(c); _brushColor = c; }
        return _brush;
    }

    IDWriteTextFormat Format(string family, float size, FontWeight w, TextAlignment h, ParagraphAlignment v, bool wrap)
    {
        var key = (family, size, w, h, v, wrap);
        if (!_formats.TryGetValue(key, out var f))
        {
            if (_formats.Count > 600)       // fitted / animated sizes: start over rather than grow forever
            {
                DropLayouts();
                foreach (var old in _formats.Values) old.Dispose();
                _formats.Clear();
            }
            CyberHud.Resolve(family, out var real, out var stretch);         // Cyber's Bahnschrift widths; any other family as it is
            f = _g.DWrite.CreateTextFormat(real, null, w, Vortice.DirectWrite.FontStyle.Normal, stretch, size * FontScale(family), "en-us");
            f.TextAlignment = h;
            f.ParagraphAlignment = v;
            f.WordWrapping = wrap ? WordWrapping.Wrap : WordWrapping.NoWrap;
            _formats[key] = f;
        }
        return f;
    }

    // The warm-up's fonts (Sys/BootWarm.cs, on its own thread): each family of the style in the weights the panels draw with, laid out
    // once on the shared factory - the font files opened, the faces made, the shaper and the system fallback started, the glyphs' boxes
    // read - so the watch's first draw finds them loaded (Erik 10-24: its "time" took 75 ms, the run's first text). Static: only the
    // DirectWrite factory, which is thread-safe - nothing of the Painter's (its caches, the D2D context: the main loop's). Asked for as
    // Format asks (the family resolved by the caller); the formats and layouts go into 'keep', the factory keeps a face while it is held.
    static readonly FontWeight[] WarmWeights = { FontWeight.Bold, FontWeight.SemiBold, FontWeight.Normal };
    const string WarmSample = "0123456789:% AMPM SEC UTC+02:00 ABCDEFGHIJKLMNOPQRSTUVWXYZ abcdefghijklmnopqrstuvwxyz " +
        "áéíóöőúüű ÁÉÍÓÖŐÚÜŰ";
    const string WarmIcons = Theme.IcoKeyboard + Theme.IcoMonitor + Theme.IcoRecenter + Theme.IcoSettings + Theme.IcoHide + Theme.IcoShow +
        Theme.IcoWatch + Theme.IcoBack + Theme.IcoEnter + Theme.IcoShift;

    public static int WarmFonts(IDWriteFactory dw, List<(string family, FontStretch stretch, bool icon)> fonts, List<IDisposable> keep)
    {
        int n = 0;
        foreach (var (family, stretch, icon) in fonts)
            foreach (var w in WarmWeights)
            {
                if (icon && w != FontWeight.Normal) continue;          // (icons: one weight)
                var f = dw.CreateTextFormat(family, null, w, Vortice.DirectWrite.FontStyle.Normal, stretch, 20, "en-us");
                keep.Add(f);
                var lay = dw.CreateTextLayout(icon ? WarmIcons : WarmSample, f, 4000, 200);
                keep.Add(lay);
                _ = lay.Metrics;
                _ = lay.OverhangMetrics;
                n++;
            }
        return n;
    }

    static Vortice.RawRectF Raw(RectangleF r) => new(r.Left, r.Top, r.Right, r.Bottom);

    public void Fill(RectangleF r, uint c, float radius = 0)
    {
        if (Theme.Classic) radius = 0;        // square corners in the 95 skin
        else if (Theme.Cyber && radius > 0)
        {
            // Cyber: the corners cut, not rounded
            var (tl, br) = CyberHud.Cuts(r, radius);
            if (br > 0) { FillCut(r, c, tl, 0, br, 0); return; }
            radius = 0;
        }
        if (radius > 0) Dc.FillRoundedRectangle(new RoundedRectangle(r, radius, radius), Brush(c));
        else Dc.FillRectangle(Raw(r), Brush(c));
    }

    public void Stroke(RectangleF r, uint c, float width = 1, float radius = 0)
    {
        if (Theme.Classic) radius = 0;
        else if (Theme.Cyber && radius > 0)
        {
            var (tl, br) = CyberHud.Cuts(r, radius);
            if (br > 0) { StrokeCut(r, c, width, tl, 0, br, 0); return; }
            radius = 0;
        }
        if (radius > 0) Dc.DrawRoundedRectangle(new RoundedRectangle(r, radius, radius), Brush(c), width);
        else Dc.DrawRectangle(Raw(r), Brush(c), width);
    }

    // ---- Cyber: rectangles with cut corners (top-left, top-right, bottom-right, bottom-left; 0 = square) ----
    // Each outline is made once per size and cuts, at (0, 0), and drawn moved into place; past 256 they start over (a card
    // springing to a new size makes one a frame).
    readonly Dictionary<(float, float, float, float, float, float), ID2D1PathGeometry> _cuts = new();

    ID2D1PathGeometry CutGeo(float w, float h, float tl, float tr, float br, float bl)
    {
        var key = (w, h, tl, tr, br, bl);
        if (_cuts.TryGetValue(key, out var geo)) return geo;
        if (_cuts.Count >= 256) DropCuts();
        geo = _g.D2DFactory.CreatePathGeometry();
        using (var sink = geo.Open())
        {
            float m = MathF.Min(w, h) / 2;
            tl = MathF.Min(tl, m); tr = MathF.Min(tr, m); br = MathF.Min(br, m); bl = MathF.Min(bl, m);
            sink.BeginFigure(new Vector2(tl, 0), FigureBegin.Filled);
            sink.AddLine(new Vector2(w - tr, 0));
            if (tr > 0) sink.AddLine(new Vector2(w, tr));
            sink.AddLine(new Vector2(w, h - br));
            if (br > 0) sink.AddLine(new Vector2(w - br, h));
            sink.AddLine(new Vector2(bl, h));
            if (bl > 0) sink.AddLine(new Vector2(0, h - bl));
            sink.AddLine(new Vector2(0, tl));
            sink.EndFigure(FigureEnd.Closed);
            sink.Close();
        }
        _cuts[key] = geo;
        return geo;
    }

    void DropCuts()
    {
        foreach (var g in _cuts.Values) g.Dispose();
        _cuts.Clear();
    }

    public void FillCut(RectangleF r, uint c, float tl, float tr, float br, float bl)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        var geo = CutGeo(r.Width, r.Height, tl, tr, br, bl);
        var old = Dc.Transform;
        Dc.Transform = Matrix3x2.CreateTranslation(r.X, r.Y) * old;
        Dc.FillGeometry(geo, Brush(c));
        Dc.Transform = old;
    }

    public void StrokeCut(RectangleF r, uint c, float width, float tl, float tr, float br, float bl)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        var geo = CutGeo(r.Width, r.Height, tl, tr, br, bl);
        var old = Dc.Transform;
        Dc.Transform = Matrix3x2.CreateTranslation(r.X, r.Y) * old;
        Dc.DrawGeometry(geo, Brush(c), width);
        Dc.Transform = old;
    }

    // 'count' four-point figures (4 * count points) filled as one shape (Cyber's bar segments, hazard stripes)
    public void Quads(Vector2[] pts, int count, uint c)
    {
        if (count > 0) FillShape(pts, 4 * count, c, true);
    }

    // ---- small shapes kept (Cyber's bar segments, hazard stripes, tags, switch blocks) ----
    // The same figure keeps coming back, often at another place (a bar's segments on the next row, the stripes crawling, a tag):
    // made once relative to its first point and drawn moved there - building a path geometry every frame cost more than drawing
    // it. Its points are compared to 1/256 px (the same figure somewhere else differs by rounding); past 256 shapes they start over.
    readonly Dictionary<ShapeKey, (Vector2[] rel, ID2D1PathGeometry geo)> _shapes = new();

    readonly struct ShapeKey : IEquatable<ShapeKey>
    {
        readonly int _n, _hash;

        public ShapeKey(Vector2[] pts, int n, bool quads)
        {
            var h = new HashCode();
            h.Add(n);
            h.Add(quads);
            for (int i = 1; i < n; i++)
            {
                var d = pts[i] - pts[0];
                h.Add(MathF.Round(d.X * 256));
                h.Add(MathF.Round(d.Y * 256));
            }
            (_n, _hash) = (quads ? -n : n, h.ToHashCode());
        }

        public bool Equals(ShapeKey o) => _n == o._n && _hash == o._hash;
        public override bool Equals(object o) => o is ShapeKey k && Equals(k);
        public override int GetHashCode() => _hash;
    }

    // pts[0..n): one figure, or n / 4 four-point ones (quads)
    void FillShape(Vector2[] pts, int n, uint c, bool quads)
    {
        var key = new ShapeKey(pts, n, quads);
        var o = pts[0];
        if (!_shapes.TryGetValue(key, out var sh))
        {
            if (_shapes.Count >= 256) DropShapes();
            var rel = new Vector2[n];
            for (int i = 0; i < n; i++) rel[i] = pts[i] - o;
            _shapes[key] = sh = (rel, ShapeGeo(rel, n, quads));
        }
        else if (!Same(sh.rel, pts, n))
        {
            // another shape with the same hash (rare): this one made for the call
            using var once = ShapeGeo(pts, n, quads);
            Dc.FillGeometry(once, Brush(c));
            return;
        }
        var old = Dc.Transform;
        Dc.Transform = Matrix3x2.CreateTranslation(o) * old;
        Dc.FillGeometry(sh.geo, Brush(c));
        Dc.Transform = old;
    }

    static bool Same(Vector2[] rel, Vector2[] pts, int n)
    {
        if (rel.Length != n) return false;
        for (int i = 1; i < n; i++)
        {
            var d = rel[i] - (pts[i] - pts[0]);
            if (MathF.Abs(d.X) > 1 / 256f || MathF.Abs(d.Y) > 1 / 256f) return false;
        }
        return true;
    }

    void DropShapes()
    {
        foreach (var sh in _shapes.Values) sh.geo.Dispose();
        _shapes.Clear();
    }

    ID2D1PathGeometry ShapeGeo(Vector2[] pts, int n, bool quads)
    {
        var geo = _g.D2DFactory.CreatePathGeometry();
        using (var sink = geo.Open())
        {
            int fig = quads ? 4 : n;
            for (int f = 0; f + fig <= n; f += fig)
            {
                sink.BeginFigure(pts[f], FigureBegin.Filled);
                for (int i = 1; i < fig; i++) sink.AddLine(pts[f + i]);
                sink.EndFigure(FigureEnd.Closed);
            }
            sink.Close();
        }
        return geo;
    }

    // ---- linear gradients (the modern watch's cards: the top light, two-tone fills, a glass sheen) ----
    // One brush per stop set, kept: all thrown away when the look changes; past 64 the one used longest ago goes (the watch, the
    // settings and the editor's style chips share this painter - a many-coloured face must not rebuild them every frame). Its
    // start and end move per call, like the one recoloured solid brush.
    readonly Dictionary<(uint, uint, uint, int), (ID2D1LinearGradientBrush brush, long used)> _grads = new();
    int _gradVersion = -1;
    long _gradTick;
    const int GradCap = 64;
    public int GradientCount => _grads.Count;
    public int GradientsMade { get; private set; }     // brushes made so far (--perfbench: none per frame once warm)

    // c0 -> c1 along angleDeg (0 = left to right, 90 = top to bottom, 45 = top-left to bottom-right) across the rectangle,
    // or done after 'span' of it (c1 from there on)
    public void FillGradient(RectangleF r, uint c0, uint c1, float radius, float angleDeg = 90, float span = 1) =>
        Gradient(r, c0, c1, c1, 2, radius, angleDeg, span);

    // three stops: a, b half-way, c
    public void FillGradient(RectangleF r, uint a, uint b, uint c, float radius, float angleDeg = 90, float span = 1) =>
        Gradient(r, a, b, c, 3, radius, angleDeg, span);

    void Gradient(RectangleF r, uint a, uint b, uint c, int n, float radius, float angleDeg, float span)
    {
        if (Theme.Classic || PixelMode) { Fill(r, a); return; }
        if (a == b && (n == 2 || b == c)) { Fill(r, a, radius); return; }       // flat: the solid brush
        if (Theme.Cyber && radius > 0)
        {
            var (tl, br) = CyberHud.Cuts(r, radius);
            if (br > 0)
            {
                // the cut shape at (0, 0), its gradient laid over it there
                var geo = CutGeo(r.Width, r.Height, tl, 0, br, 0);
                var old = Dc.Transform;
                Dc.Transform = Matrix3x2.CreateTranslation(r.X, r.Y) * old;
                Dc.FillGeometry(geo, Place(GradBrush(a, b, c, n), new RectangleF(0, 0, r.Width, r.Height), angleDeg, span));
                Dc.Transform = old;
                return;
            }
            radius = 0;
        }
        var brush = Place(GradBrush(a, b, c, n), r, angleDeg, span);
        if (radius > 0) Dc.FillRoundedRectangle(new RoundedRectangle(r, radius, radius), brush);
        else Dc.FillRectangle(Raw(r), brush);
    }

    // a disc filled with c0 -> c1 laid over 'area' the way FillGradient lays it there: on that card it matches the pixels under it.
    // replace: it takes the place of what is under it, alpha included (a card that is not opaque, drawn over again, would be a
    // touch more solid there)
    public void CircleGradient(float cx, float cy, float rad, RectangleF area, uint c0, uint c1, float angleDeg = 90, float span = 1,
        bool replace = false)
    {
        if (Theme.Classic || PixelMode) { Circle(cx, cy, rad, c0); return; }
        var e = new Ellipse(new Vector2(cx, cy), rad, rad);
        if (replace) Dc.PrimitiveBlend = PrimitiveBlend.Copy;
        if (c0 == c1) Dc.FillEllipse(e, Brush(c0));
        else Dc.FillEllipse(e, Place(GradBrush(c0, c1, c1, 2), area, angleDeg, span));
        if (replace) Dc.PrimitiveBlend = PrimitiveBlend.SourceOver;
    }

    static ID2D1LinearGradientBrush Place(ID2D1LinearGradientBrush brush, RectangleF r, float angleDeg, float span)
    {
        float t = angleDeg * MathF.PI / 180, dx = MathF.Cos(t), dy = MathF.Sin(t);
        float len = MathF.Abs(r.Width * dx) + MathF.Abs(r.Height * dy);
        var s = new Vector2(r.X + r.Width / 2 - dx * len / 2, r.Y + r.Height / 2 - dy * len / 2);
        brush.StartPoint = s;
        brush.EndPoint = s + new Vector2(dx, dy) * (len * MathF.Max(0.01f, span));
        return brush;
    }

    ID2D1LinearGradientBrush GradBrush(uint a, uint b, uint c, int n)
    {
        if (_gradVersion != Theme.Version) { DropGradients(); _gradVersion = Theme.Version; }
        var key = (a, b, c, n);
        if (_grads.TryGetValue(key, out var e)) { _grads[key] = (e.brush, ++_gradTick); return e.brush; }
        if (_grads.Count >= GradCap)
        {
            // full: the one used longest ago makes room
            var old = key;
            long oldest = long.MaxValue;
            foreach (var (k, v) in _grads) if (v.used < oldest) { oldest = v.used; old = k; }
            _grads[old].brush.Dispose();
            _grads.Remove(old);
        }
        var stops = n == 2 ? new[] { new GradientStop(0, ToColor(a)), new GradientStop(1, ToColor(b)) }
            : new[] { new GradientStop(0, ToColor(a)), new GradientStop(0.5f, ToColor(b)), new GradientStop(1, ToColor(c)) };
        ID2D1LinearGradientBrush br;
        using (var col = Dc.CreateGradientStopCollection(stops, ExtendMode.Clamp))
            br = Dc.CreateLinearGradientBrush(new LinearGradientBrushProperties(Vector2.Zero, Vector2.UnitY), col);
        GradientsMade++;
        _grads[key] = (br, ++_gradTick);
        return br;
    }

    void DropGradients()
    {
        foreach (var g in _grads.Values) g.brush.Dispose();
        _grads.Clear();
    }

    // ---- the squircle: a superellipse, |x/a|^n + |y/b|^n = 1 (the 1x1 analog clock's face, the shape of a phone's widget) ----
    // Its outline is made once per size and exponent, around (0, 0), and drawn moved into place; a few are kept (a card springing
    // to a new size makes one a frame: past 32 they start over). Plain rectangles in the 95 skin.
    readonly Dictionary<(float, float, float), ID2D1PathGeometry> _squircles = new();

    ID2D1PathGeometry Squircle(float w, float h, float n)
    {
        var key = (w, h, n);
        if (_squircles.TryGetValue(key, out var geo)) return geo;
        if (_squircles.Count >= 32) DropSquircles();
        geo = _g.D2DFactory.CreatePathGeometry();
        using (var sink = geo.Open())
        {
            // the parametric form (|cos t|^(2/n), |sin t|^(2/n)): its points crowd where it bends, at the corners
            const int N = 160;
            float a = w / 2, b = h / 2, e = 2 / n;
            Vector2 At(int i)
            {
                float t = i * MathF.Tau / N, c = MathF.Cos(t), s = MathF.Sin(t);
                return new(a * MathF.Sign(c) * MathF.Pow(MathF.Abs(c), e), b * MathF.Sign(s) * MathF.Pow(MathF.Abs(s), e));
            }
            sink.BeginFigure(At(0), FigureBegin.Filled);
            for (int i = 1; i < N; i++) sink.AddLine(At(i));
            sink.EndFigure(FigureEnd.Closed);
            sink.Close();
        }
        _squircles[key] = geo;
        return geo;
    }

    void DropSquircles()
    {
        foreach (var g in _squircles.Values) g.Dispose();
        _squircles.Clear();
    }

    // the squircle filling r: c0 -> c1 laid over r as FillGradient lays it (c0 == c1: flat)
    public void FillSquircle(RectangleF r, uint c0, uint c1, float angleDeg = 90, float span = 1, float n = 4)
    {
        if (Theme.Classic || PixelMode) { Fill(r, c0); return; }
        if (r.Width <= 0 || r.Height <= 0) return;
        var geo = SquircleAt(r, n, out var at, out var old);
        Dc.FillGeometry(geo, c0 == c1 ? Brush(c0) : Place(GradBrush(c0, c1, c1, 2), at, angleDeg, span));
        Dc.Transform = old;
    }

    // its outline, centred on r's squircle (inset r by half the width to keep it inside); three stops: a, b half-way, c - a rim lit
    // from one side
    public void StrokeSquircle(RectangleF r, uint a, uint b, uint c, float width, float angleDeg = 90, float span = 1, float n = 4)
    {
        if (Theme.Classic || PixelMode) { Stroke(r, a, width); return; }
        if (r.Width <= 0 || r.Height <= 0) return;
        var geo = SquircleAt(r, n, out var at, out var old);
        Dc.DrawGeometry(geo, a == b && b == c ? Brush(a) : Place(GradBrush(a, b, c, 3), at, angleDeg, span), width);
        Dc.Transform = old;
    }

    public void StrokeSquircle(RectangleF r, uint c, float width, float n = 4) => StrokeSquircle(r, c, c, c, width, 90, 1, n);

    // r's outline, the transform moved to r's centre (the caller puts 'old' back); 'at' is r in that moved space (where a gradient goes)
    ID2D1PathGeometry SquircleAt(RectangleF r, float n, out RectangleF at, out Matrix3x2 old)
    {
        var geo = Squircle(r.Width, r.Height, n);
        old = Dc.Transform;
        Dc.Transform = Matrix3x2.CreateTranslation(r.X + r.Width / 2, r.Y + r.Height / 2) * old;
        at = new RectangleF(-r.Width / 2, -r.Height / 2, r.Width, r.Height);
        return geo;
    }

    // ---- Windows 95 shapes ----

    // one device pixel - two from 1.5x up - so the 3D edges stay crisp at any scale
    public float Px => MathF.Max(1, MathF.Round(Scale)) / Scale;

    // Page: a property sheet's page (drawn like Window in 1995; a style may draw it as its tab control's body)
    public enum Edge { Raised, Window, Pressed, Sunken, Etched, Page }

    // the two-pixel 3D edges of the era: Raised = a button, Window = a window frame,
    // Pressed = a button held down, Sunken = a text box / list / colour well, Etched = a group box line (the style's skin draws them)
    public void Bevel(RectangleF r, Edge e) => Theme.RetroSkin.Bevel(this, r, e);

    // a 95 push button: raised; held = black frame + flat grey edge; latched (a toggle that is in) =
    // dithered + pushed in; isDefault = the black frame of a dialog's default button. Returns the face.
    public RectangleF ButtonFace95(RectangleF r, bool isDefault, bool down, bool latched, bool hover = false) =>
        Theme.RetroSkin.ButtonFace(this, r, isDefault, down, latched, hover);

    // a groove across: the line between sections of a 95 dialog
    public void EtchedLine(float x1, float x2, float y) => Theme.RetroSkin.EtchedLine(this, x1, x2, y);

    // an active window's navy title bar
    public void Caption(RectangleF r, string text, float size = 13)
    {
        Fill(r, Theme.W95Navy);
        Text(text, new RectangleF(r.X + 6, r.Y, r.Width - 12, r.Height), 0xFFFFFFFF, size, FontWeight.Bold);
    }

    // greyed-out text the 95 way: etched - white, then grey on top one pixel up-left (the style's skin draws it)
    public void DisabledText(string s, RectangleF r, float size, FontWeight w = FontWeight.Normal,
        TextAlignment h = TextAlignment.Leading, ParagraphAlignment v = ParagraphAlignment.Center) =>
        Theme.RetroSkin.DisabledText(this, s, r, size, w, h, v);

    // ---- for the modern watch's widgets: rings, and a scale / fade around a widget while it animates ----

    ID2D1StrokeStyle _roundCaps;

    // The arcs' shapes, kept by where and how far they run (the same place, the same geometry: the same pixels) - a watch at rest draws
    // the same rings every frame, and a path geometry made and thrown away each time was two COM objects of garbage and ~20 us a ring.
    // Past 128 (a ring filling up makes one a frame) they start over.
    readonly Dictionary<(float, float, float, float, float), ID2D1PathGeometry> _arcs = new();

    // a ring segment with round ends, Apple-watch style: 'start' and 'sweep' in turns (0 = 12 o'clock, clockwise)
    public void Arc(float cx, float cy, float r, float start, float sweep, uint c, float width)
    {
        if (sweep <= 0.0005f || r <= 0) return;
        _roundCaps ??= _g.D2DFactory.CreateStrokeStyle(new StrokeStyleProperties
        {
            StartCap = CapStyle.Round, EndCap = CapStyle.Round, LineJoin = LineJoin.Round,
        });
        if (sweep >= 0.9995f) { Dc.DrawEllipse(new Ellipse(new Vector2(cx, cy), r, r), Brush(c), width); return; }
        var key = (cx, cy, r, start, sweep);
        if (!_arcs.TryGetValue(key, out var geo))
        {
            if (_arcs.Count >= 128) DropArcs();
            Vector2 At(float t) => new(cx + r * MathF.Sin(t * MathF.Tau), cy - r * MathF.Cos(t * MathF.Tau));
            geo = _g.D2DFactory.CreatePathGeometry();
            using (var sink = geo.Open())
            {
                sink.BeginFigure(At(start), FigureBegin.Hollow);
                sink.AddArc(new ArcSegment
                {
                    Point = At(start + sweep), Size = new Vortice.Mathematics.Size(r, r), RotationAngle = 0,
                    SweepDirection = SweepDirection.Clockwise, ArcSize = sweep > 0.5f ? ArcSize.Large : ArcSize.Small,
                });
                sink.EndFigure(FigureEnd.Open);
                sink.Close();
            }
            _arcs[key] = geo;
        }
        Dc.DrawGeometry(geo, Brush(c), width, _roundCaps);
    }

    void DropArcs()
    {
        foreach (var g in _arcs.Values) g.Dispose();
        _arcs.Clear();
    }

    // a line with round ends (clock hands)
    public void RoundLine(float x1, float y1, float x2, float y2, uint c, float width)
    {
        _roundCaps ??= _g.D2DFactory.CreateStrokeStyle(new StrokeStyleProperties
        {
            StartCap = CapStyle.Round, EndCap = CapStyle.Round, LineJoin = LineJoin.Round,
        });
        Dc.DrawLine(new Vector2(x1, y1), new Vector2(x2, y2), Brush(c), width, _roundCaps);
    }

    readonly Stack<Matrix3x2> _transforms = new();

    // draw scaled by s around (cx, cy) until PopScale
    public void PushScale(float s, float cx, float cy)
    {
        _transforms.Push(Dc.Transform);
        Dc.Transform = Matrix3x2.CreateScale(s, new Vector2(cx, cy)) * Dc.Transform;
    }

    public void PopScale()
    {
        if (_transforms.Count > 0) Dc.Transform = _transforms.Pop();
    }

    // draw moved by (dx, dy) until PopScale (something sliding or rising into place)
    public void PushOffset(float dx, float dy)
    {
        _transforms.Push(Dc.Transform);
        Dc.Transform = Matrix3x2.CreateTranslation(dx, dy) * Dc.Transform;
    }

    // draw through m, then what is set, until PopScale (the mixer's standing trackbar: a skin's own trackbar, transposed)
    public void PushMatrix(Matrix3x2 m)
    {
        _transforms.Push(Dc.Transform);
        Dc.Transform = m * Dc.Transform;
    }

    int _fades;

    // everything until PopFade at this opacity (a layer: only while something animates)
    public void PushFade(float opacity)
    {
        Dc.PushLayer(new LayerParameters1
        {
            ContentBounds = new Vortice.RawRectF(-100000, -100000, 100000, 100000),
            MaskAntialiasMode = AntialiasMode.PerPrimitive,
            MaskTransform = Matrix3x2.Identity,
            Opacity = Math.Clamp(opacity, 0, 1),
        }, null);
        _fades++;
    }

    public void PopFade()
    {
        if (_fades == 0) return;
        _fades--;
        Dc.PopLayer();
    }

    // the first 'count' points as a line with round joins and ends (the frame graph's line); created per call like Arc
    public void Polyline(Vector2[] pts, int count, uint c, float width)
    {
        if (count < 2) return;
        _roundCaps ??= _g.D2DFactory.CreateStrokeStyle(new StrokeStyleProperties
        {
            StartCap = CapStyle.Round, EndCap = CapStyle.Round, LineJoin = LineJoin.Round,
        });
        using var geo = _g.D2DFactory.CreatePathGeometry();
        using (var sink = geo.Open())
        {
            sink.BeginFigure(pts[0], FigureBegin.Hollow);
            for (int i = 1; i < count; i++) sink.AddLine(pts[i]);
            sink.EndFigure(FigureEnd.Open);
            sink.Close();
        }
        Dc.DrawGeometry(geo, Brush(c), width, _roundCaps);
    }

    // the first 'count' points, filled (the frame graph's area); up to four - a tag, a notch, a switch's block - kept (FillShape)
    public void Polygon(Vector2[] pts, int count, uint c)
    {
        if (count < 3) return;
        if (count <= 4) { FillShape(pts, count, c, false); return; }
        using var geo = _g.D2DFactory.CreatePathGeometry();
        using (var sink = geo.Open())
        {
            sink.BeginFigure(pts[0], FigureBegin.Filled);
            for (int i = 1; i < count; i++) sink.AddLine(pts[i]);
            sink.EndFigure(FigureEnd.Closed);
            sink.Close();
        }
        Dc.FillGeometry(geo, Brush(c));
    }

    readonly Dictionary<(float, float), ID2D1StrokeStyle> _dashes = new();

    // a dashed line (flat ends): 'dash' px drawn, 'gap' px left out (the frame budget)
    public void DashedLine(float x1, float y1, float x2, float y2, uint c, float width, float dash = 4, float gap = 4)
    {
        if (Theme.Classic || PixelMode) { Line(x1, y1, x2, y2, c, width); return; }
        if (!_dashes.TryGetValue((dash, gap), out var st))
            _dashes[(dash, gap)] = st = _g.D2DFactory.CreateStrokeStyle(new StrokeStyleProperties
            {
                StartCap = CapStyle.Flat, EndCap = CapStyle.Flat, DashCap = CapStyle.Flat, DashStyle = DashStyle.Custom,
            }, new[] { dash / MathF.Max(0.5f, width), gap / MathF.Max(0.5f, width) });
        Dc.DrawLine(new Vector2(x1, y1), new Vector2(x2, y2), Brush(c), width, st);
    }

    // a filled polygon (the 95 clock's hands): its shape kept by its points as they are (the same points, the same pixels) - the hands
    // stand still for a minute, drawn twice a second; past 64 shapes they start over
    readonly Dictionary<PointsKey, ID2D1PathGeometry> _polys = new();

    readonly struct PointsKey : IEquatable<PointsKey>
    {
        readonly Vector2[] _pts;
        readonly int _hash;

        public PointsKey(Vector2[] pts)
        {
            _pts = pts;
            var h = new HashCode();
            foreach (var p in pts) { h.Add(p.X); h.Add(p.Y); }
            _hash = h.ToHashCode();
        }

        public bool Equals(PointsKey o)
        {
            if (_pts.Length != o._pts.Length) return false;
            for (int i = 0; i < _pts.Length; i++) if (_pts[i] != o._pts[i]) return false;
            return true;
        }
        public override bool Equals(object o) => o is PointsKey k && Equals(k);
        public override int GetHashCode() => _hash;
    }

    public void Polygon(Vector2[] pts, uint c)
    {
        var key = new PointsKey(pts);
        if (!_polys.TryGetValue(key, out var geo))
        {
            if (_polys.Count >= 64) DropPolys();
            geo = _g.D2DFactory.CreatePathGeometry();
            using (var sink = geo.Open())
            {
                sink.BeginFigure(pts[0], FigureBegin.Filled);
                for (int i = 1; i < pts.Length; i++) sink.AddLine(pts[i]);
                sink.EndFigure(FigureEnd.Closed);
                sink.Close();
            }
            _polys[new PointsKey((Vector2[])pts.Clone())] = geo;      // (its own copy of the points: the caller may reuse the array)
        }
        Dc.FillGeometry(geo, Brush(c));
    }

    void DropPolys()
    {
        foreach (var g in _polys.Values) g.Dispose();
        _polys.Clear();
    }

    public void Line(float x1, float y1, float x2, float y2, uint c, float width = 1) =>
        Dc.DrawLine(new Vector2(x1, y1), new Vector2(x2, y2), Brush(c), width);

    public void Circle(float cx, float cy, float r, uint c) =>
        Dc.FillEllipse(new Ellipse(new Vector2(cx, cy), r, r), Brush(c));

    public void Ring(float cx, float cy, float r, uint c, float width) =>
        Dc.DrawEllipse(new Ellipse(new Vector2(cx, cy), r, r), Brush(c), width);

    // Erik's logo (assets\orbitoverlay.svg, 64 x 64 units, the exe's icon) filling the square r: the dark rounded square,
    // the cyan ring and the purple moon whose dark rim cuts the ring. The same in every style, always smoothed.
    public const uint LogoBg = 0xFF171A24, LogoRing = 0xFF3CC3FF, LogoMoon = 0xFF8B5CF6;
    public void Logo(RectangleF r)
    {
        var aa = Dc.AntialiasMode;
        Dc.AntialiasMode = AntialiasMode.PerPrimitive;
        float u = r.Width / 64f;
        FillRound(r, LogoBg, 16 * u);
        Ring(r.X + 31 * u, r.Y + 34 * u, 17 * u, LogoRing, 6 * u);
        Circle(r.X + 46 * u, r.Y + 19 * u, 8 * u, LogoBg);
        Circle(r.X + 46 * u, r.Y + 19 * u, 5 * u, LogoMoon);
        Dc.AntialiasMode = aa;
    }

    // --snapshot's contrast check: every text drawn (and where, in what colour); NoText leaves all text out (what it sits on)
    public Action<string, RectangleF, uint, float, FontWeight, string> TextSpy;
    public bool NoText;

    public void Text(string s, RectangleF r, uint c, float size, FontWeight w = FontWeight.Normal,
        TextAlignment h = TextAlignment.Leading, ParagraphAlignment v = ParagraphAlignment.Center,
        string family = null, bool wrap = false)
    {
        if (string.IsNullOrEmpty(s) || NoText) return;
        s = Lang.T(s);                       // the language picked in General (English: s itself)
        TextSpy?.Invoke(s, r, c, size, w, family);
        FitSpy?.Invoke(s, r, size, w, family ?? Theme.Font, wrap, h, v, 0);
        FitAll?.Invoke(this, s, r, size, w, family ?? Theme.Font, wrap, h, v, 0);
        FontCheck();
        family ??= Theme.Font;
        // a retro style's own type (RetroSkin.MapFont / PixelFont): a bitmap font, or another family / size
        if (PixelMode && Theme.Classic)
        {
            var sk = Theme.RetroSkin;
            sk.MapFont(ref family, ref size, ref w);
            if (sk.PixelFont(family, size, w) is { } pf) { pf.Draw(this, s, r, c, w >= FontWeight.SemiBold, h, v, wrap); return; }
        }
        var f = Format(family, size, w, h, v, wrap);
        // the 95 skin's GDI-measured pixel text stays exactly as it was
        if (PixelMode)
        {
            // laid out once and kept, as DrawText lays it out on every call (GDI-measured, its box as DrawText reads the rectangle,
            // the transform it is drawn with): the same pixels without the layout work each frame
            float pw = (r.X + r.Width) - r.X, ph = (r.Y + r.Height) - r.Y;
            if (pw < 0 || ph < 0 || !PixelLayouts)
            {
                Dc.DrawText(s, f, new Rect(r.X, r.Y, r.Width, r.Height), Brush(c), DrawTextOptions.Clip, Vortice.DCommon.MeasuringMode.GdiClassic);
                return;
            }
            var t = Dc.Transform;
            var gk = (s, f, pw, ph, t);
            if (!_gdiLayouts.TryGetValue(gk, out var gl))
            {
                if (_gdiLayouts.Count >= 1000) DropGdiLayouts();
                gl = _g.DWrite.CreateGdiCompatibleTextLayout(s, (uint)s.Length, f, pw, ph, 1f, t, false);
                LayoutsMade++;
                _gdiLayouts[gk] = gl;
            }
            Dc.DrawTextLayout(new Vector2(r.X, r.Y), gl, Brush(c), DrawTextOptions.Clip);
            return;
        }
        float lw = MathF.Max(0, r.Width), lh = MathF.Max(0, r.Height);
        var key = (s, f, lw, lh);
        if (!_layouts.TryGetValue(key, out var tl))
        {
            // a ticking clock or a springing card makes new ones all the time: start over rather than grow forever
            if (_layouts.Count >= 1000) DropLayouts();
            var lay = _g.DWrite.CreateTextLayout(s, f, lw, lh);
            LayoutsMade++;
            if (Tabular(family, s)) Figures(lay, s, family, size, w);
            _layouts[key] = tl = (lay, Fits(lay));
        }
        Dc.DrawTextLayout(new Vector2(r.X, r.Y), tl.lay, Brush(c), ClipText && !tl.fits ? DrawTextOptions.Clip : DrawTextOptions.None);
    }

    // Text cut to its box (DrawTextOptions.Clip) costs Direct2D a clip per string - with ~100 strings a third of a settings frame. Text
    // whose ink stays a pixel inside its box loses nothing to the cut: it goes without (the same picture, the cost spared).
    static bool Fits(IDWriteTextLayout lay)
    {
        var o = lay.OverhangMetrics;
        return o.Left <= -1 && o.Top <= -1 && o.Right <= -1 && o.Bottom <= -1;
    }

    // Text cut to its rectangle (DrawTextOptions.Clip): the default. Off, a draw that has made sure its text fits (the modern
    // watch's custom face: cut with "...", sized to fit) saves a clip per string - about a third of a watch frame.
    public bool ClipText { get; set; } = true;

    void DropLayouts()
    {
        foreach (var l in _layouts.Values) l.lay.Dispose();
        _layouts.Clear();
        foreach (var l in _tracked.Values) l.lay.Dispose();
        _tracked.Clear();
        DropGdiLayouts();
    }

    // the retro styles' pixel text (PixelMode), laid out the GDI way: by text, format, box and transform. Off: DrawText as before (the
    // perf tool's check that both give the same pixels)
    public bool PixelLayouts { get; set; } = true;
    readonly Dictionary<(string, IDWriteTextFormat, float, float, Matrix3x2), IDWriteTextLayout> _gdiLayouts = new();

    void DropGdiLayouts()
    {
        foreach (var l in _gdiLayouts.Values) l.Dispose();
        _gdiLayouts.Clear();
    }

    // ---- letter-spaced text (the modern watch's UPPERCASE captions) ----
    readonly Dictionary<(string, IDWriteTextFormat, float, float, float), (IDWriteTextLayout lay, bool fits)> _tracked = new();

    // text with 'tracking' px after every letter but the last (so centred and right-aligned text stays where it belongs);
    // the 95 skin's pixel text has none
    public void TrackedText(string s, RectangleF r, uint c, float size, FontWeight w, float tracking,
        TextAlignment h = TextAlignment.Leading, ParagraphAlignment v = ParagraphAlignment.Center, string family = null)
    {
        if (string.IsNullOrEmpty(s) || NoText) return;
        s = Lang.T(s);
        if (PixelMode || tracking == 0 || s.Length < 2) { Text(s, r, c, size, w, h, v, family); return; }
        TextSpy?.Invoke(s, r, c, size, w, family);
        FitSpy?.Invoke(s, r, size, w, family ?? Theme.Font, false, h, v, tracking);
        FitAll?.Invoke(this, s, r, size, w, family ?? Theme.Font, false, h, v, tracking);
        FontCheck();
        family ??= Theme.Font;
        var f = Format(family, size, w, h, v, false);
        float lw = MathF.Max(0, r.Width), lh = MathF.Max(0, r.Height);
        var key = (s, f, lw, lh, tracking);
        if (!_tracked.TryGetValue(key, out var tl))
        {
            if (_tracked.Count >= 300) { foreach (var l in _tracked.Values) l.lay.Dispose(); _tracked.Clear(); }
            var lay = _g.DWrite.CreateTextLayout(s, f, lw, lh);
            LayoutsMade++;
            using (var l1 = lay.QueryInterface<IDWriteTextLayout1>())
                l1.SetCharacterSpacing(0, tracking, 0, new TextRange(0, (uint)(s.Length - 1)));
            if (Tabular(family, s)) Figures(lay, s, family, size, w, tracking);
            _tracked[key] = tl = (lay, Fits(lay));
        }
        Dc.DrawTextLayout(new Vector2(r.X, r.Y), tl.lay, Brush(c), ClipText && !tl.fits ? DrawTextOptions.Clip : DrawTextOptions.None);
    }

    public float MeasureTracked(string s, float size, FontWeight w, float tracking, string family = null)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        s = Lang.T(s);
        return MeasureWidth(s, size, w, family) + (PixelMode ? 0 : tracking * (s.Length - 1));
    }

    // ---- line metrics: where the baseline sits, so a value and its unit share one ----
    readonly Dictionary<(float, FontWeight, string), (float baseline, float height)> _lines = new();

    // from the top of a line of this text to its baseline, and the line's height (text drawn ParagraphAlignment.Near)
    public (float baseline, float height) TextLine(float size, FontWeight w = FontWeight.Normal, string family = null)
    {
        FontCheck();
        family ??= Theme.Font;
        var key = (size, w, family);
        if (_lines.TryGetValue(key, out var m)) return m;
        var f = Format(family, size, w, TextAlignment.Leading, ParagraphAlignment.Near, false);
        using var layout = _g.DWrite.CreateTextLayout("0", f, 1000, 1000);
        var lm = new LineMetrics[1];
        layout.GetLineMetrics(lm, out _);
        m = (lm[0].Baseline, lm[0].Height);
        if (_lines.Count > 400) _lines.Clear();
        _lines[key] = m;
        return m;
    }

    // ---- Look > Font ----
    // A typeface of the list is drawn no wider than the default one: its size scaled by the default's width over its own (a sample of
    // the watch's and the settings' words, and of clock figures), never above 1 - so every layout made for Segoe UI still fits, and a
    // narrower font keeps its size. Its figures are lining and tabular (OpenType 'lnum' + 'tnum'): a ticking clock or a rolling
    // digit does not jiggle the text around it, and old-style figures do not drop below the line; a font without tabular figures
    // has each digit padded out to the widest one's cell. The default font (and the 95 skin) draws exactly as before.
    IDWriteTypography _figures;
    string _font;                                    // the chosen font the caches were filled with
    readonly Dictionary<(string, FontWeight), float[]> _digitPads = new();
    readonly Dictionary<string, float> _scales = new();
    static readonly string[] Notes = { "since 11:53", "since 23:16", "since 1:23 PM", "September", "Szeptember", "Wednesday", "csütörtök" };

    // --snapshot's fit check: every text drawn - the family it is drawn in, whether it wraps, its alignment and letter spacing
    public Action<string, RectangleF, float, FontWeight, string, bool, TextAlignment, ParagraphAlignment, float> FitSpy;
    // the same for every painter at once (--snapshot's text fit in both languages, ToolsLang.cs)
    public static Action<Painter, string, RectangleF, float, FontWeight, string, bool, TextAlignment, ParagraphAlignment, float> FitAll;

    // the chosen font changed: what was laid out and measured for the last one goes (a pick is rare, the caches are big)
    void FontCheck()
    {
        if (string.Equals(_font, Theme.UserFont, StringComparison.Ordinal)) return;
        _font = Theme.UserFont;
        DropLayouts();
        _widths.Clear();
        _heights.Clear();
        _lines.Clear();
    }

    // 1 for the default font and anything not in the list
    public float FontScale(string family)
    {
        if (family == null || Array.IndexOf(Theme.FontChoices, family) < 0) return 1;
        if (_scales.TryGetValue(family, out float k)) return k;
        const string Words = "Time in VR since 11:53 Wednesday, September 30 Battery Keyboard Desktops Settings Now playing Midnight City";
        const string Clock = "12:58 23:47 100% 86 fps 9.3 ms";
        k = 1;
        foreach (var w in new[] { FontWeight.Normal, FontWeight.SemiBold })
            k = MathF.Min(k, RawWidth(Theme.UiFont, Words, w, false) / RawWidth(family, Words, w, true));
        k = MathF.Min(k, RawWidth(Theme.DisplayFont, Clock, FontWeight.Bold, false) / RawWidth(family, Clock, FontWeight.Bold, true));
        // and each of the tightest notes on its own (a 1x1's "since 11:53" under its value: Segoe UI's narrow 1 makes room there)
        foreach (var note in Notes)
            k = MathF.Min(k, RawWidth(Theme.UiFont, note, FontWeight.SemiBold, false) / RawWidth(family, note, FontWeight.SemiBold, true));
        k = MathF.Floor(k * 100) / 100;
        _scales[family] = k;
        return k;
    }

    // s at 100 px, laid out on its own (no cache, no scale); figures: as a font of the list draws them (lining, tabular, padded)
    float RawWidth(string family, string s, FontWeight w, bool figures)
    {
        using var f = _g.DWrite.CreateTextFormat(family, null, w, Vortice.DirectWrite.FontStyle.Normal, FontStretch.Normal, 100, "en-us");
        using var lay = _g.DWrite.CreateTextLayout(s, f, 100000, 1000);
        float pad = 0;
        if (figures)
        {
            lay.SetTypography(FiguresTypography(), new TextRange(0, (uint)s.Length));
            var pads = DigitPads(family, w);
            if (pads != null) foreach (char ch in s) if ((uint)(ch - '0') <= 9) pad += pads[ch - '0'];
        }
        return lay.Metrics.WidthIncludingTrailingWhitespace + pad;
    }

    IDWriteTypography FiguresTypography()
    {
        if (_figures == null)
        {
            _figures = _g.DWrite.CreateTypography();
            _figures.AddFontFeature(new FontFeature { NameTag = FontFeatureTag.LiningFigures, Parameter = 1 });
            _figures.AddFontFeature(new FontFeature { NameTag = FontFeatureTag.TabularFigures, Parameter = 1 });
        }
        return _figures;
    }

    // a font of the list (the chosen one, or a row of the list itself) with figures in the text
    bool Tabular(string family, string s)
    {
        if (Theme.Classic || Array.IndexOf(Theme.FontChoices, family) < 0) return false;
        foreach (char ch in s) if ((uint)(ch - '0') <= 9) return true;
        return false;
    }

    // the list font's figures on a layout of s (and the padding where it has no tabular ones); 'tracking' = TrackedText's spacing
    void Figures(IDWriteTextLayout lay, string s, string family, float size, FontWeight w, float tracking = 0)
    {
        lay.SetTypography(FiguresTypography(), new TextRange(0, (uint)s.Length));
        var pads = DigitPads(family, w);
        if (pads == null) return;
        float k = size * FontScale(family) / 200;
        using var l1 = lay.QueryInterface<IDWriteTextLayout1>();
        for (int i = 0; i < s.Length; i++)
        {
            int d = s[i] - '0';
            if ((uint)d > 9 || pads[d] <= 0) continue;
            float half = pads[d] * k;
            l1.SetCharacterSpacing(half, half + (i < s.Length - 1 ? tracking : 0), 0, new TextRange((uint)i, 1));
        }
    }

    // per digit, how much narrower than the widest digit it is at 100 px (unscaled, with the figures above); null = all one width
    float[] DigitPads(string family, FontWeight w)
    {
        if (_digitPads.TryGetValue((family, w), out var pads)) return pads;
        using var f = _g.DWrite.CreateTextFormat(family, null, w, Vortice.DirectWrite.FontStyle.Normal, FontStretch.Normal, 100, "en-us");
        var adv = new float[10];
        for (int d = 0; d < 10; d++)
        {
            using var lay = _g.DWrite.CreateTextLayout(((char)('0' + d)).ToString(), f, 10000, 1000);
            lay.SetTypography(FiguresTypography(), new TextRange(0, 1));
            adv[d] = lay.Metrics.WidthIncludingTrailingWhitespace;
        }
        float max = 0, min = float.MaxValue;
        foreach (float a in adv) { max = MathF.Max(max, a); min = MathF.Min(min, a); }
        if (max - min > 0.05f)
        {
            pads = new float[10];
            for (int d = 0; d < 10; d++) pads[d] = max - adv[d];
        }
        _digitPads[(family, w)] = pads;
        return pads;
    }

    // How far digits' ink reaches above and below the baseline, per px of the size asked for, as Text draws them (a list font's scale and
    // figures included): a clock fitting its digits to a box in any font (the clock designs, WristClocks)
    readonly Dictionary<(string, FontWeight), (float above, float below)> _digitInk = new();
    public (float above, float below) DigitInk(FontWeight w, string family = null)
    {
        FontCheck();
        family ??= Theme.Font;
        if (_digitInk.TryGetValue((family, w), out var m)) return m;
        const string s = "0123456789";
        var f = Format(family, 100, w, TextAlignment.Leading, ParagraphAlignment.Near, false);
        using var lay = _g.DWrite.CreateTextLayout(s, f, 10000, 1000);
        if (Tabular(family, s)) Figures(lay, s, family, 100, w);
        var lm = new LineMetrics[1];
        lay.GetLineMetrics(lm, out _);
        var o = lay.OverhangMetrics;          // past the 10000 x 1000 box: its top (ink above it) and bottom
        m = ((lm[0].Baseline + o.Top) / 100, (1000 + o.Bottom - lm[0].Baseline) / 100);
        _digitInk[(family, w)] = m;
        return m;
    }

    // how far the ink of a text sticks out of its box, laid out the way Text / TrackedText lay it out (--snapshot's fit check):
    // the most on any side, in px (0 or less: inside)
    public float Overhang(string s, RectangleF r, float size, FontWeight w, string family, bool wrap, TextAlignment h, ParagraphAlignment v,
        float tracking = 0)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        s = Lang.T(s);
        FontCheck();
        family ??= Theme.Font;
        var f = Format(family, size, w, h, v, wrap);
        using var lay = _g.DWrite.CreateTextLayout(s, f, MathF.Max(0, r.Width), MathF.Max(0, r.Height));
        if (tracking != 0 && s.Length > 1)
            using (var l1 = lay.QueryInterface<IDWriteTextLayout1>())
                l1.SetCharacterSpacing(0, tracking, 0, new TextRange(0, (uint)(s.Length - 1)));
        if (Tabular(family, s)) Figures(lay, s, family, size, w, tracking);
        var o = lay.OverhangMetrics;
        return MathF.Max(MathF.Max(o.Left, o.Right), MathF.Max(o.Top, o.Bottom));
    }

    public void Icon(string glyph, RectangleF r, uint c, float size) =>
        Text(glyph, r, c, size, FontWeight.Normal, TextAlignment.Center, ParagraphAlignment.Center, Theme.IconFont);

    public float MeasureWidth(string s, float size, FontWeight w = FontWeight.Normal, string family = null)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        s = Lang.T(s);
        FontCheck();
        family ??= Theme.Font;
        if (PixelMode && Theme.Classic)
        {
            var sk = Theme.RetroSkin;
            sk.MapFont(ref family, ref size, ref w);
            if (sk.PixelFont(family, size, w) is { } pf) return pf.Measure(s, w >= FontWeight.SemiBold);
        }
        var key = (s, size, w, PixelMode ? PxFamily(family) : family);
        if (_widths.TryGetValue(key, out float cached)) return cached;
        var f = Format(family, size, w, TextAlignment.Leading, ParagraphAlignment.Near, false);
        using var layout = PixelMode
            ? _g.DWrite.CreateGdiCompatibleTextLayout(s, (uint)s.Length, f, 10000, 1000, 1f, null, false)
            : _g.DWrite.CreateTextLayout(s, f, 10000, 1000);
        if (!PixelMode && Tabular(family, s)) Figures(layout, s, family, size, w);
        float width = layout.Metrics.WidthIncludingTrailingWhitespace;
        if (_widths.Count > 2000) _widths.Clear();     // chat text etc. would grow it forever
        _widths[key] = width;
        return width;
    }

    // pixel mode's widths are GDI-measured, kept apart under the family's name with a mark - that name made once per family (it was
    // a new string in every measuring call, many a frame in the retro styles)
    readonly Dictionary<string, string> _pxFamily = new();
    string PxFamily(string family)
    {
        if (!_pxFamily.TryGetValue(family, out var k)) _pxFamily[family] = k = family + "\u0001px";
        return k;
    }

    // the height of wrapped text in a column of this width (laid out once, kept)
    readonly Dictionary<(string, float, FontWeight, float, string), float> _heights = new();
    public float MeasureHeight(string s, float size, FontWeight w, float width, string family = null)
    {
        if (string.IsNullOrEmpty(s) || width <= 0) return 0;
        s = Lang.T(s);
        FontCheck();
        family ??= Theme.Font;
        if (PixelMode && Theme.Classic)
        {
            var sk = Theme.RetroSkin;
            sk.MapFont(ref family, ref size, ref w);
            if (sk.PixelFont(family, size, w) is { } pf) return pf.MeasureHeight(s, width, w >= FontWeight.SemiBold);
        }
        var key = (s, size, w, width, family);
        if (_heights.TryGetValue(key, out float cached)) return cached;
        var f = Format(family, size, w, TextAlignment.Leading, ParagraphAlignment.Near, true);
        using var layout = _g.DWrite.CreateTextLayout(s, f, width, 100000);
        if (Tabular(family, s)) Figures(layout, s, family, size, w);
        float h = layout.Metrics.Height;
        if (_heights.Count > 1000) _heights.Clear();
        _heights[key] = h;
        return h;
    }

    // premultiplied BGRA pixels -> bitmap (album art, icons)
    public unsafe ID2D1Bitmap CreateBitmap(uint[] px, int w, int h)
    {
        fixed (uint* p = px)
            return Dc.CreateBitmap(new SizeI(w, h), (IntPtr)p, (uint)(w * 4),
                new BitmapProperties(new Vortice.DCommon.PixelFormat(Vortice.DXGI.Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied)));
    }

    public void Image(ID2D1Bitmap bmp, RectangleF dst, float opacity = 1) =>
        Dc.DrawBitmap(bmp, Raw(dst), opacity, Vortice.Direct2D1.BitmapInterpolationMode.Linear, null);

    // a part of a bitmap (src, in its pixels) scaled into dst well: a high-quality cubic, which first scales a big picture down
    // (a photo at a third of its size, on the watch: no shimmer, no jagged edges)
    public void ImagePart(ID2D1Bitmap bmp, RectangleF dst, RectangleF src, float opacity = 1) =>
        Dc.DrawBitmap(bmp, Raw(dst), opacity, InterpolationMode.HighQualityCubic, Raw(src), null);

    readonly Stack<ID2D1Geometry> _layers = new();

    // The clips' shapes, kept: the watch clips its dock keys and covers in every frame, at the same places while it rests (a geometry
    // made and thrown away each time before). Past 64 they start over - only between clips, never under one still pushed.
    readonly Dictionary<(RectangleF r, float radius, float tl, float br), ID2D1Geometry> _clipGeos = new();

    // clips everything drawn until PopRoundedClip to a rounded rectangle (antialiased edges)
    public void PushRoundedClip(RectangleF r, float radius)
    {
        var cut = Theme.Cyber ? CyberHud.Cuts(r, radius) : (tl: -1f, br: -1f);
        if (cut.br <= 0) cut = (-1, -1);
        var key = (r, radius, cut.tl, cut.br);
        if (!_clipGeos.TryGetValue(key, out var geo))
        {
            if (_clipGeos.Count >= 64 && _layers.Count == 0) DropClipGeos();
            _clipGeos[key] = geo = ClipGeo(r, radius, cut);
        }
        Dc.PushLayer(new LayerParameters1
        {
            ContentBounds = new Vortice.RawRectF(-100000, -100000, 100000, 100000),
            GeometricMask = geo,
            MaskAntialiasMode = AntialiasMode.PerPrimitive,
            MaskTransform = Matrix3x2.Identity,
            Opacity = 1,
        }, null);
        _layers.Push(geo);
    }

    ID2D1Geometry ClipGeo(RectangleF r, float radius, (float tl, float br) cut)
    {
        ID2D1Geometry geo = _g.D2DFactory.CreateRoundedRectangleGeometry(new RoundedRectangle(r, radius, radius));
        if (cut.br > 0)
        {
            // Cyber: the cut shape (its own geometry, placed)
            geo.Dispose();
            var pg = _g.D2DFactory.CreatePathGeometry();
            using (var sink = pg.Open())
            {
                float tl = MathF.Min(cut.tl, MathF.Min(r.Width, r.Height) / 2), br = MathF.Min(cut.br, MathF.Min(r.Width, r.Height) / 2);
                sink.BeginFigure(new Vector2(r.X + tl, r.Y), FigureBegin.Filled);
                sink.AddLine(new Vector2(r.Right, r.Y));
                sink.AddLine(new Vector2(r.Right, r.Bottom - br));
                sink.AddLine(new Vector2(r.Right - br, r.Bottom));
                sink.AddLine(new Vector2(r.X, r.Bottom));
                sink.AddLine(new Vector2(r.X, r.Y + tl));
                sink.EndFigure(FigureEnd.Closed);
                sink.Close();
            }
            geo = pg;
        }
        return geo;
    }

    void DropClipGeos()
    {
        foreach (var g in _clipGeos.Values) g.Dispose();
        _clipGeos.Clear();
    }

    public void PopRoundedClip()
    {
        if (_layers.Count == 0) return;
        Dc.PopLayer();
        _layers.Pop();          // (the shape stays kept: _clipGeos)
    }

    // ---- rounded corners without a clip layer ----
    // After drawing: what lies outside r's rounded corners made see-through, as PushRoundedClip would have clipped it (the VR
    // settings panel). A clip layer the size of the panel, its mask made anew every frame, cost a fifth of the page itself. Here the
    // layer is drawn once, white, into a little picture of the four corners (each at its own place on the pixel grid), and every frame
    // multiplies the corners by it (DestinationIn): the pixels on the curve get the share of the picture the layer gave them (to the
    // rounding of one 8-bit step). At the frame's own scale, nothing moved or pushed.
    Surface _corners;
    (RectangleF r, float radius, float scale) _cornersFor;

    public void CutCorners(RectangleF r, float radius)
    {
        var t = Dc.Transform;
        float s = Scale;
        int c = (int)MathF.Ceiling(radius * s) + 2;           // a corner's square, in pixels
        int x0 = (int)MathF.Floor(r.X * s), y0 = (int)MathF.Floor(r.Y * s);
        int x1 = (int)MathF.Ceiling(r.Right * s) - c, y1 = (int)MathF.Ceiling(r.Bottom * s) - c;
        Span<(int x, int y)> at = stackalloc (int, int)[] { (x0, y0), (x1, y0), (x0, y1), (x1, y1) };
        if (_corners == null || _cornersFor != (r, radius, s))
        {
            _corners?.Dispose();
            _corners = new Surface(_g, 2 * c, 2 * c);
            _cornersFor = (r, radius, s);
            Dc.Target = _corners.Bitmap;
            Dc.Clear(ToColor(0));
            using var geo = _g.D2DFactory.CreateRoundedRectangleGeometry(new RoundedRectangle(r, radius, radius));
            for (int i = 0; i < 4; i++)
            {
                Dc.Transform = Matrix3x2.Identity;
                Dc.PushAxisAlignedClip(new Vortice.RawRectF(i % 2 * c, i / 2 * c, i % 2 * c + c, i / 2 * c + c), AntialiasMode.Aliased);    // its own square
                Dc.Transform = Matrix3x2.CreateScale(s) * Matrix3x2.CreateTranslation(i % 2 * c - at[i].x, i / 2 * c - at[i].y);
                Dc.PushLayer(new LayerParameters1
                {
                    ContentBounds = new Vortice.RawRectF(-100000, -100000, 100000, 100000),
                    GeometricMask = geo,
                    MaskAntialiasMode = AntialiasMode.PerPrimitive,
                    MaskTransform = Matrix3x2.Identity,
                    Opacity = 1,
                }, null);
                Dc.FillRectangle(Raw(r), RawBrush(0xFFFFFFFF));
                Dc.PopLayer();
                Dc.PopAxisAlignedClip();
            }
            Dc.Target = _target;
        }
        // (DestinationIn works on everything outside the picture too: each corner's square clipped to)
        Dc.Transform = Matrix3x2.Identity;
        for (int i = 0; i < 4; i++)
        {
            Dc.PushAxisAlignedClip(new Vortice.RawRectF(at[i].x, at[i].y, at[i].x + c, at[i].y + c), AntialiasMode.Aliased);
            Dc.DrawImage(_corners.Bitmap, new Vector2(at[i].x, at[i].y), new Vortice.RawRectF(i % 2 * c, i / 2 * c, i % 2 * c + c, i / 2 * c + c),
                InterpolationMode.NearestNeighbor, CompositeMode.DestinationIn);
            Dc.PopAxisAlignedClip();
        }
        Dc.Transform = t;
    }

    public void PushClip(RectangleF r)
    {
        Dc.PushAxisAlignedClip(Raw(r), AntialiasMode.Aliased);
        _clips.Push(1);
    }

    public void PopClip()
    {
        if (_clips.Count == 0) return;
        _clips.Pop();
        Dc.PopAxisAlignedClip();
    }

    public void Dispose()
    {
        _brush?.Dispose();
        _brush = null;
        foreach (var d in _dashes.Values) d.Dispose();
        _dashes.Clear();
        DropGradients();
        DropSquircles();
        DropCuts();
        DropShapes();
        DropArcs();
        DropPolys();
        _corners?.Dispose();
        _corners = null;
        DropClipGeos();
        DropLayouts();
        foreach (var f in _formats.Values) f.Dispose();
        _formats.Clear();
        _figures?.Dispose();
        _figures = null;
        _defaultText?.Dispose();
        _defaultText = null;
    }
}
