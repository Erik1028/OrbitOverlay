using System.Collections.Generic;
using Vortice.Direct2D1;

namespace Orbit;

// The Windows 95 look's icons: pixel art in the sixteen VGA colours, one letter per pixel (the same way
// Mixtape's are made - several are Mixtape's own). Turned into bitmaps once per painter; always drawn at
// whole-pixel sizes, never smoothed.
public static class W95Icons
{
    public enum Id { Orbit, Clock, Moon, Gear, Center, Info32, Computer, ComputerOff, Folder, Note, RadioOff, RadioOn, Window, Layouts, Keyboard, Palette, Delete, Download, Dock, People, Chat, Theater, Mine, Flag, Hourglass, Camera }

    // k black  w white  s silver  g grey  n navy  b blue  t teal  c cyan  r red  m maroon
    // y yellow o olive  e green   l lime  p purple f fuchsia   . transparent
    static uint Pal(char c) => c switch
    {
        'k' => 0xFF000000, 'w' => 0xFFFFFFFF, 's' => 0xFFC0C0C0, 'g' => 0xFF808080, 'n' => 0xFF000080,
        'b' => 0xFF0000FF, 't' => 0xFF008080, 'c' => 0xFF00FFFF, 'r' => 0xFFFF0000, 'm' => 0xFF800000,
        'y' => 0xFFFFFF00, 'o' => 0xFF808000, 'e' => 0xFF008000, 'l' => 0xFF00FF00, 'p' => 0xFF800080,
        'f' => 0xFFFF00FF, _ => 0,
    };

    static string[] Map(Id id) => id switch
    {
        Id.People => new[]
        {
            "................",
            "................",
            ".....kkk........",
            "....kyyyk.kkk...",
            "....kyyyk.kyyk..",
            "....kyyyk.kyyyk.",
            ".....kkk..kyyyk.",
            "...kkbbbkk.kkk..",
            "..kbbbbbbbk.kkk.",
            "..kbbbbbbbkkrrrk",
            "..kbbbbbbbkrrrrk",
            "..kbbbbbbbkrrrrk",
            "..kkkkkkkkkkkkkk",
            "................",
            "................",
            "................",
        },
        Id.Chat => new[]
        {
            "................",
            "..kkkkkkkkkkkk..",
            ".kwwwwwwwwwwwwk.",
            ".kwkkkkwkkkkwwk.",
            ".kwwwwwwwwwwwwk.",
            ".kwkkkwkkkkkwwk.",
            ".kwwwwwwwwwwwwk.",
            ".kwkkkkkwwwwwwk.",
            ".kwwwwwwwwwwwwk.",
            "..kkkwwkkkkkkk..",
            "....kwk.........",
            "....kk..........",
            "....k...........",
            "................",
            "................",
            "................",
        },
        Id.Theater => new[]
        {
            "................",
            "kkkkkkkkkkkkkkkk",
            "krrmkkkkkkkkmrrk",
            "krrmkcccccckmrrk",
            "krrmkccccyckmrrk",
            "krrmkcccccckmrrk",
            "krrmkcceccckmrrk",
            "krrmkceeecckmrrk",
            "krrmkeeeeeekmrrk",
            "krrmkkkkkkkkmrrk",
            "krrm........mrrk",
            "krrrm......mrrrk",
            "kkkkk......kkkkk",
            "................",
            "................",
            "................",
        },
        Id.Dock => new[]
        {
            "................",
            "................",
            "................",
            "kkkkkkkkkkkkkkkk",
            "kggggggggggggggk",
            "kgbbgrrgllggkkgk",
            "kgbbgrrgllgkwwkk",
            "kggggggggggkwwkk",
            "kgyygppgccggkkgk",
            "kgyygppgccggggk.",
            "kggggggggggkkgkk",
            "kgkkgkkgkkgkwkwk",
            "kggggggggggkkgkk",
            "kkkkkkkkkkkkkkkk",
            "................",
            "................",
        },
        Id.Orbit => new[]       // Erik's logo (assets\orbitoverlay.svg) in the sixteen colours
        {
            ".kkkkkkkkkkkkkk.",
            "kkkkkkkkkkkkkkkk",
            "kkkkkkkkkkkkkkkk",
            "kkkkkkkkkkkpfpkk",
            "kkkkkkcccckfffkk",
            "kkkkcccccckpfpkk",
            "kkkkcckkkkkkkkkk",
            "kkkcckkkkkkcckkk",
            "kkkcckkkkkkcckkk",
            "kkkcckkkkkkcckkk",
            "kkkcckkkkkkcckkk",
            "kkkkcckkkkcckkkk",
            "kkkkcccccccckkkk",
            "kkkkkkcccckkkkkk",
            "kkkkkkkkkkkkkkkk",
            ".kkkkkkkkkkkkkk.",
        },
        Id.Clock => new[]
        {
            "................",
            "....kkkkkkkk....",
            "...kkwwwkwwkk...",
            "..kkwgwwwwgwkk..",
            ".kkwwwwwkwwwwkk.",
            ".kwgwwwwkwwwgwk.",
            ".kwwwwwwkwwwwwk.",
            ".kwwwwwmkwwwwwk.",
            ".kkwwwwwnnwwwkk.",
            ".kwwwwwwwnnwwwk.",
            ".kwgwwwwwwnwgwk.",
            ".kkwwwwwwwwwwkk.",
            "..kkwgwwwwgwkk..",
            "...kkwwwkwwkk...",
            "....kkkkkkkk....",
            "................",
        },
        Id.Moon => new[]
        {
            "................",
            "................",
            "....kk..........",
            "...kyyk.........",
            "..kyyk..........",
            ".kyyyk..........",
            ".kyyyk..........",
            ".kyyyk..........",
            ".kyyyyk.........",
            ".kyyyykk........",
            ".kyyyyyykkkk....",
            "..kyyyyyyyyyk...",
            "...kyyyyyyyk....",
            "....kkyyykk.....",
            "......kkk.......",
            "................",
        },
        Id.Gear => new[]
        {
            ".......kk.......",
            "......kggk......",
            "...kk.kggk.kk...",
            "..kggkkkkkkggk..",
            "..kgksssssskgk..",
            "...ksswwggssk...",
            ".kkkswgkkggskkk.",
            "kggksgkwwkgskggk",
            "kggksgkwwkgskggk",
            ".kkksggkkggskkk.",
            "...kssggggssk...",
            "..kgksssssskgk..",
            "..kggkkkkkkggk..",
            "...kk.kggk.kk...",
            "......kggk......",
            ".......kk.......",
        },
        Id.Center => new[]
        {
            "........k.......",
            "........k.......",
            ".......nk.......",
            ".....nn.knn.....",
            "....n...k..n....",
            "...n....k...n...",
            "...n........n...",
            "..n....rr....n..",
            "kkkkkk.rr..kkkkk",
            "...n........n...",
            "...n........n...",
            "....n...k..n....",
            ".....nn.knn.....",
            ".......nk.......",
            "........k.......",
            "........k.......",
        },
        Id.Info32 => new[]
        {
            "................................",
            "............kkkkkk..............",
            "..........kkwwwwwwkk............",
            "........kkwwwwwwwwwwkk..........",
            ".......kwwwwwwwwwwwwwwk.........",
            "......kwwwwwwwwbwwwwwwwk........",
            ".....kwwwwwwwwbbbwwwwwwwk.......",
            "....kwwwwwwwwbbbbbwwwwwwwk......",
            "....kwwwwwwwwwbbbwwwwwwwwk......",
            "...kwwwwwwwwwwwbwwwwwwwwwwk.....",
            "...kwwwwwwwwwwwwwwwwwwwwwwk.....",
            "..kwwwwwwwwwwwwwwwwwwwwwwwwk....",
            "..kwwwwwwwwbbbbbbbwwwwwwwwwk....",
            "..kwwwwwwwwwwbbbbbwwwwwwwwwk....",
            "..kwwwwwwwwwwbbbbbwwwwwwwwwk....",
            "..kwwwwwwwwwwbbbbbwwwwwwwwwk....",
            "..kwwwwwwwwwwbbbbbwwwwwwwwwk....",
            "...kwwwwwwwwwbbbbbwwwwwwwwk.....",
            "...kwwwwwwwwwbbbbbwwwwwwwwk.....",
            "....kwwwwwwwwbbbbbwwwwwwwk......",
            "....kwwwwwwwwbbbbbwwwwwwwk......",
            ".....kwwwwwwwbbbbbwwwwwwk.......",
            "......kwwwwbbbbbbbbbwwwk........",
            "......kwwwwwwwwwwwwwwwk.........",
            "......kwwwwwwwwwwwwwkk..........",
            "......kwwwwwwwwwwwkk............",
            "......kwwwwwkkkkkk..............",
            "......kwwwwk....................",
            "......kwwkk.....................",
            "......kwk.......................",
            ".....kwk........................",
            "......k.........................",
        },
        Id.Computer => new[]
        {
            "................",
            "..kkkkkkkkkkkk..",
            "..kssssssssssk..",
            "..kskkkkkkkksk..",
            "..ksktttttcksk..",
            "..kskttttttksk..",
            "..kskttttttksk..",
            "..kskttttttksk..",
            "..kskkkkkkkksk..",
            "..kssssssssesk..",
            "..kkkkkkkkkkkk..",
            ".....kssssk.....",
            "...kkkkkkkkkk...",
            "...kssssssssk...",
            "...kkkkkkkkkk...",
            "................",
        },
        Id.ComputerOff => new[]
        {
            "................",
            "..kkkkkkkkkkkk..",
            "..kssssssssssk..",
            "..kskkkkkkkksk..",
            "..kskkkkkkkksk..",
            "..kskkkkkkkksk..",
            "..kskkkkkkkksk..",
            "..kskkkkkkkksk..",
            "..kskkkkkkkksk..",
            "..kssssssssesk..",
            "..kkkkkkkkkkkk..",
            ".....kssssk.....",
            "...kkkkkkkkkk...",
            "...kssssssssk...",
            "...kkkkkkkkkk...",
            "................",
        },
        Id.Folder => new[]
        {
            "................",
            "................",
            "..kkkkk.........",
            ".kwyyyyk........",
            ".kyyyyyykkkkkkk.",
            ".kwwwwwwwwwwwwok",
            ".kwyyyyyyyyyyyok",
            ".kwyyyyyyyyyyyok",
            ".kwyyyyyyyyyyyok",
            ".kwyyyyyyyyyyyok",
            ".kwyyyyyyyyyyyok",
            ".kwyyyyyyyyyyyok",
            ".koooooooooooooo",
            ".kkkkkkkkkkkkkkk",
            "................",
            "................",
        },
        Id.Note => new[]
        {
            "................",
            "......nnnnnnnn..",
            "......nnnnnnnn..",
            "......n......n..",
            "......n......n..",
            "......n......n..",
            "......n......n..",
            "......n......n..",
            "......n......n..",
            "......n......n..",
            "...nnnn...nnnn..",
            "..nnnnn..nnnnn..",
            "..nnnnn..nnnnn..",
            "...nnn....nnn...",
            "................",
            "................",
        },
        Id.RadioOff => new[]
        {
            "....gggg....",
            "..ggkkkkgg..",
            ".gkkwwwwkkw.",
            ".gkwwwwwwsw.",
            "gkwwwwwwwwsw",
            "gkwwwwwwwwsw",
            "gkwwwwwwwwsw",
            "gkwwwwwwwwsw",
            ".gkwwwwwwsw.",
            ".wsswwwwssw.",
            "..wwssssww..",
            "....wwww....",
        },
        Id.RadioOn => new[]
        {
            "....gggg....",
            "..ggkkkkgg..",
            ".gkkwwwwkkw.",
            ".gkwwwwwwsw.",
            "gkwwwkkwwwsw",
            "gkwwkkkkwwsw",
            "gkwwkkkkwwsw",
            "gkwwwkkwwwsw",
            ".gkwwwwwwsw.",
            ".wsswwwwssw.",
            "..wwssssww..",
            "....wwww....",
        },
        Id.Window => new[]
        {
            "................",
            ".kkkkkkkkkkkkkk.",
            ".knnnnnnnnnnnnk.",
            ".knnnnnnnnwkwnk.",
            ".kkkkkkkkkkkkkk.",
            ".kwwwwwwwwwwwwk.",
            ".kwwwwwwwwwwwwk.",
            ".kwwwwwwwwwwwwk.",
            ".kwwwwwwwwwwwwk.",
            ".kwwwwwwwwwwwwk.",
            ".kwwwwwwwwwwwwk.",
            ".kwwwwwwwwwwwwk.",
            ".kwwwwwwwwwwwwk.",
            ".kkkkkkkkkkkkkk.",
            "................",
            "................",
        },
        Id.Layouts => new[]
        {
            "................",
            "kkkkkkkkkk......",
            "knnnnnnnnk......",
            "kkkkkkkkkk......",
            "kwwwwwwwwk......",
            "kwwwwkkkkkkkkkkk",
            "kwwwwknnnnnnnnnk",
            "kwwwwkkkkkkkkkkk",
            "kwwwwkwwwwwwwwwk",
            "kkkkkkwwwwwwwwwk",
            ".....kwwwwwwwwwk",
            ".....kwwwwwwwwwk",
            ".....kwwwwwwwwwk",
            ".....kkkkkkkkkkk",
            "................",
            "................",
        },
        Id.Keyboard => new[]
        {
            "................",
            "................",
            "................",
            "kkkkkkkkkkkkkkkk",
            "kwwwwwwwwwwwwwsk",
            "kwkskskskskskgsk",
            "kwssssssssssssgk",
            "kwskskskskskssgk",
            "kwssssssssssssgk",
            "kwsskskskskskggk",
            "kwssssssssssssgk",
            "kwssskkkkkkksssk",
            "kwsssssssssssssk",
            "kggggggggggggggk",
            "kkkkkkkkkkkkkkkk",
            "................",
        },
        Id.Palette => new[]
        {
            "................",
            "....kkkkkkk.....",
            "..kkwwwwwwwkk...",
            ".kwwrrwwwwbbwk..",
            ".kwwrrwwwwbbwwk.",
            "kwwwwwwwwwwwwwk.",
            "kwyywwwkkwwwwwwk",
            "kwyywwk..kwwllwk",
            "kwwwwwk..kwwllwk",
            "kwwwwwwkkwwwwwwk",
            ".kwwwwwwwwwwwwk.",
            ".kweewwwwwwffwk.",
            "..kweewwwwwffk..",
            "...kkwwwwwwkk...",
            ".....kkkkkk.....",
            "................",
        },
        Id.Delete => new[]
        {
            "................",
            "................",
            "..rr........rr..",
            "..rrr......rrr..",
            "...rrr....rrr...",
            "....rrr..rrr....",
            ".....rrrrrr.....",
            "......rrrr......",
            "......rrrr......",
            ".....rrrrrr.....",
            "....rrr..rrr....",
            "...rrr....rrr...",
            "..rrr......rrr..",
            "..rr........rr..",
            "................",
            "................",
        },
        Id.Download => new[]
        {
            "................",
            "......kkkk......",
            "......klek......",
            "......klek......",
            "......klek......",
            "......klek......",
            "...kkkklekkkk...",
            "....kllllleek...",
            ".....kllleek....",
            "......klek......",
            ".......kk.......",
            "................",
            ".kkkkkkkkkkkkkk.",
            ".kwwwwwwwwwwwgk.",
            ".kkkkkkkkkkkkkk.",
            "................",
        },
        Id.Hourglass => new[]               // busy: the 1995 wait cursor's hourglass (still - 95's did not turn)
        {
            "..kkkkkkkkkkkk..",
            "..kwwwwwwwwwwk..",
            "..kkkkkkkkkkkk..",
            "...kwwwwwwwwk...",
            "...kwkkkkkkwk...",
            "....kwkkkkwk....",
            ".....kwkkwk.....",
            "......kwwk......",
            "......kwwk......",
            ".....kwwkwk.....",
            "....kwwwkwwk....",
            "...kwwkkkkwwk...",
            "...kwkkkkkkwk...",
            "..kkkkkkkkkkkk..",
            "..kwwwwwwwwwwk..",
            "..kkkkkkkkkkkk..",
        },
        Id.Camera => new[]                  // the watch's camera button: a silver camera, its prism hump and shutter release, the blue lens
        {
            "................",
            "................",
            ".....kkkkk......",
            "....kwwwwsk.kk..",
            ".kkkkkkkkkkkkkk.",
            ".kwwwwkkkkkwwwk.",
            ".kwsskncnnnksgk.",
            ".kwsknccnnnnkgk.",
            ".kwskncnnnnnkgk.",
            ".kwsknnnnnnnkgk.",
            ".kwsknnnnnbnkgk.",
            ".kwssknnnbnksgk.",
            ".kggggkkkkkgggk.",
            ".kkkkkkkkkkkkkk.",
            "................",
            "................",
        },
        Id.Mine => MineArt.Mine16,          // Minesweeper's own pictures (Mines.cs)
        Id.Flag => MineArt.Flag16,
        _ => new[] { "." },
    };

    static readonly Dictionary<(Painter, Id), ID2D1Bitmap> _cache = new();

    public static ID2D1Bitmap Get(Painter p, Id id)
    {
        if (_cache.TryGetValue((p, id), out var b)) return b;
        var m = Map(id);
        int h = m.Length, w = m[0].Length;
        var px = new uint[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = Pal(m[y][x]);
        b = p.CreateBitmap(px, w, h);
        _cache[(p, id)] = b;
        return b;
    }

    // (read from a table filled once: Map builds the picture's rows anew on every call - every icon of every frame did)
    static readonly int[] _sizes = new int[(int)Id.Camera + 1];
    public static int Size(Id id)
    {
        int i = (int)id;
        if ((uint)i >= (uint)_sizes.Length) return Map(id).Length;
        if (_sizes[i] == 0) _sizes[i] = Map(id).Length;
        return _sizes[i];
    }

    // at its own size (or a whole multiple), top-left at x, y
    public static void Draw(Painter p, Id id, float x, float y, int scale = 1)
    {
        if (Theme.Classic && Theme.RetroSkin.Icon(p, id, x, y, scale)) return;      // the style's own picture
        int n = Size(id) * scale;
        p.ImageNearest(Get(p, id), new System.Drawing.RectangleF(x, y, n, n));
    }
}
