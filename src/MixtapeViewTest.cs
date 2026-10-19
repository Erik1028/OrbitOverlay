using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;

namespace Orbit;

// ORBIT_SNAP_ONLY=mixtape --snapshot <dir>: the Mixtape widget (Panels/WristMixtape.cs) without Mixtape - a made-up source (MixtapeFake:
// placeholder songs, albums with drawn covers, playlists, a queue, placeholder synced lines): the cards in every style, the opened view's
// six tabs (and an album / a playlist opened) in Modern, Cyber and 95, taps that must reach the source (play, a song, the queue's cross,
// a lyrics line), the layout check of every size.
public sealed class MixtapeFake : MixtapeSource
{
    public readonly List<string> Sent = new();
    readonly Dictionary<(string, int), uint[]> _covers = new();

    public MixtapeFake(bool connected = true)
    {
        Status = connected ? MixtapeStatus.Connected : MixtapeStatus.NotRunning;
        AppVersion = "0.23.0";
        LibraryName = "Test iPod";
        var songs = new List<MixtapeSong>();
        string[] albums = { "Album One", "Album Two", "Album Three", "Album Four", "Album Five", "Album Six", "Album Seven" };
        string[] artists = { "Artist A", "Artist B", "Artist C", "Artist A", "Artist D", "Artist E", "Artist B" };
        int n = 0;
        for (int a = 0; a < albums.Length; a++)
            for (int t = 1; t <= 6 + a % 3; t++)
            {
                n++;
                songs.Add(new MixtapeSong
                {
                    Id = n.ToString(), Title = $"Test Song {n:00}", Artist = artists[a], Album = albums[a], AlbumArtist = artists[a],
                    Cover = "cover" + a, DurationMs = 150_000 + n * 7_300 % 120_000, Track = t, Disc = 1, Year = 2001 + a,
                });
            }
        var lists = new List<(string, string, string[])>
        {
            ("p1", "Placeholder list A", songs.Where((_, i) => i % 3 == 0).Select(s => s.Id).ToArray()),
            ("p2", "Placeholder list B", songs.Take(9).Select(s => s.Id).ToArray()),
        };
        Library = MixtapeLibrary.Build(1, songs.ToArray(), lists);
        var playing = songs[3];
        State = new MixtapeState
        {
            V = 1, Playing = true, Track = playing, Rating = 4, PositionMs = 61_000, At = global::Orbit.Clock.Now, Volume = 0.72f,
            Shuffle = false, Repeat = "all", Queue = songs.Skip(4).Take(7).ToArray(),
        };
        // placeholder lines (never real lyrics): synced every 4 s, the song at 61 s = line 15
        Lyrics = new MixtapeLyrics
        {
            TrackId = playing.Id, Synced = true,
            Ms = Enumerable.Range(0, 30).Select(i => i * 4000).ToArray(),
            Lines = Enumerable.Range(0, 30).Select(i => $"Placeholder line {i + 1}").ToArray(),
        };
    }

    // a still: the clock does not move the position (pictures stay the same run to run)
    public void Hold() { var s = State.Copy(); s.Playing = false; State = s; }

    public override uint[] Cover(string key, int size)
    {
        if (string.IsNullOrEmpty(key)) return null;
        if (_covers.TryGetValue((key, size), out var px)) return px;
        // a made-up cover: a diagonal two-colour gradient with a ring, the colours from the key
        uint mixed = unchecked((uint)key.Aggregate(17, (x, ch) => x * 31 + ch) * 2654435761u);
        int h = (int)(mixed >> 1);
        uint c0 = Hsv(h % 360, 0.65f, 0.85f), c1 = Hsv((h / 7 + 140) % 360, 0.7f, 0.35f);
        px = new uint[size * size];
        float cx = size / 2f, r0 = size * 0.32f, r1 = size * 0.36f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float t = (x + y) / (2f * size);
                uint c = Theme.Mix(c0, c1, t);
                float d = MathF.Sqrt((x - cx) * (x - cx) + (y - cx) * (y - cx));
                if (d > r0 && d < r1) c = Theme.Mix(c, 0xFFFFFFFF, 0.55f);
                px[y * size + x] = c | 0xFF000000;
            }
        _covers[(key, size)] = px;
        return px;
    }

    static uint Hsv(float hdeg, float s, float v)
    {
        float c = v * s, x = c * (1 - MathF.Abs(hdeg / 60 % 2 - 1)), m = v - c;
        var (r, g, b) = hdeg < 60 ? (c, x, 0f) : hdeg < 120 ? (x, c, 0f) : hdeg < 180 ? (0f, c, x) : hdeg < 240 ? (0f, x, c) : hdeg < 300 ? (x, 0f, c) : (c, 0f, x);
        return 0xFF000000 | ((uint)((r + m) * 255) << 16) | ((uint)((g + m) * 255) << 8) | (uint)((b + m) * 255);
    }

    protected override void Send(string cmd, params (string name, object value)[] args)
    {
        string a = string.Join(" ", args.Select(x => x.value is string[] ids ? $"{x.name}={ids.Length}" : $"{x.name}={x.value}"));
        lock (Sent) Sent.Add(a.Length > 0 ? cmd + " " + a : cmd);
        Changed();
    }

    public string Last() { lock (Sent) return Sent.Count == 0 ? "" : Sent[^1]; }
}

static partial class Tools
{
    static int MixtapeOnly(WristPanel wrist, PanelData data, ConfigStore cfg, Painter painter, Gpu gpu, string dir)
    {
        int failed0 = WantsFailed, layout = 0;
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = SnapCulture;
        var wcfg = cfg.Data.Wrist;
        var keep = (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Pages, wcfg.Page, wcfg.Background, wrist.Mixtape);
        try
        {
            foreach (var (style, tag) in new[] { ("modern", ""), (CyberHud.Id, "_cyber"), ("classic", "_95"), ("xp", "_xp"), ("aero", "_aero"), ("mac", "_mac"), ("terminal", "_term") })
            {
                cfg.Data.Appearance = new AppearanceConfig { Style = style, Clock24h = true };
                Theme.Apply(cfg.Data.Appearance);
                bool classic = Theme.Classic;
                var fake = new MixtapeFake();
                fake.Hold();
                wrist.Mixtape = fake;
                wcfg.Face = "custom";
                wcfg.Background = "panel";
                wcfg.Grid = null; wcfg.Pages = null; wcfg.Page = 0;
                wcfg.Widgets = new List<WatchWidget>
                {
                    new() { Type = "mixtape", X = 0, Y = 0, W = 3, H = 2 }, new() { Type = "mixtape", X = 3, Y = 0, W = 3, H = 1 },
                    new() { Type = "mixtape", X = 3, Y = 1, W = 2, H = 1 }, new() { Type = "time", X = 0, Y = 2, W = 3, H = 2 },
                };
                wrist.Settle(); wrist.Draw(data); wrist.Draw(data);
                wrist.Surf.SavePng(gpu, Path.Combine(dir, $"mixtape_face{tag}.png"));
                // the card's play: a toggle sent (modern / Cyber: the 3x2's big disc)
                if (!classic)
                {
                    Vector2? p = null;
                    for (int k = 0; k < 24 && p == null; k++) p = wrist.ControlPoint(data, Ui.Id("wmt", k * 3 + 1));
                    int n0 = fake.Sent.Count;
                    if (p is { } pp) MtTap(wrist, data, pp);
                    Want($"mixtape{tag}: the card's play button sent '{(fake.Sent.Count > n0 ? fake.Last() : "nothing")}' (want toggle)", p != null && fake.Last() == "toggle");
                }
                // opened: every tab
                bool opened = wrist.OpenForTest("mixtape");
                for (int i = 0; i < 6; i++)
                {
                    wrist.MixtapeTabForTest(i);
                    for (int f = 0; f < 12; f++) { Thread.Sleep(15); wrist.Draw(data); }
                    wrist.Surf.SavePng(gpu, Path.Combine(dir, $"mixtape_tab{i}{tag}.png"));
                    if (classic) break;                  // (the retro styles: one view)
                }
                if (!classic)
                {
                    wrist.MixtapeTabForTest(3, openFirst: true);
                    for (int f = 0; f < 6; f++) wrist.Draw(data);
                    wrist.Surf.SavePng(gpu, Path.Combine(dir, $"mixtape_album{tag}.png"));
                    wrist.MixtapeTabForTest(4, openFirst: true);
                    for (int f = 0; f < 6; f++) wrist.Draw(data);
                    wrist.Surf.SavePng(gpu, Path.Combine(dir, $"mixtape_playlist{tag}.png"));
                    // taps: a song in the Songs tab plays the list from it; the queue's first cross takes it out; a lyrics line seeks
                    wrist.MixtapeTabForTest(2);
                    for (int f = 0; f < 4; f++) wrist.Draw(data);
                    var song = wrist.ControlPoint(data, Ui.Id(Ui.Id("wmts", 2), 1));
                    if (song is { } sp) MtTap(wrist, data, sp);
                    string played = fake.Last();
                    wrist.MixtapeTabForTest(1);
                    for (int f = 0; f < 4; f++) wrist.Draw(data);
                    var cross = wrist.ControlPoint(data, Ui.Id("wmtqx", 0));
                    if (cross is { } cp) MtTap(wrist, data, cp);
                    string removed = fake.Last();
                    wrist.MixtapeTabForTest(5);
                    for (int f = 0; f < 4; f++) wrist.Draw(data);
                    var line = wrist.ControlPoint(data, Ui.Id("wmtl", 16));
                    if (line is { } lp) MtTap(wrist, data, lp);
                    string seek = fake.Last();
                    string wantPlay = $"playSongs ids={fake.Library.Songs.Length} start=1";
                    Want($"mixtape{tag}: opened {opened}; a song tapped sent '{played}' (want {wantPlay}), the queue's cross '{removed}' (want queueRemove index=0), " +
                         $"a lyrics line '{seek}' (want seek ms=64000)",
                        opened && played == wantPlay && removed == "queueRemove index=0" && seek == "seek ms=64000");
                }
                wrist.CloseForTest();
                // not running: the card and the opened view say so
                wrist.Mixtape = new MixtapeFake(connected: false);
                wrist.Settle(); wrist.Draw(data); wrist.Draw(data);
                wrist.Surf.SavePng(gpu, Path.Combine(dir, $"mixtape_off{tag}.png"));
                wrist.Mixtape = new MixtapeFake();
                ((MixtapeFake)wrist.Mixtape).Hold();
                layout += LayoutCheckRun(wrist, data, cfg, painter, gpu, dir, tag.Length > 0 ? tag[1..] : "modern", onlyKind: "mixtape");
            }
        }
        finally
        {
            (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Pages, wcfg.Page, wcfg.Background, wrist.Mixtape) = keep;
            wrist.CloseForTest();
            cfg.Data.Appearance = new AppearanceConfig { Clock24h = true };
            Theme.Apply(cfg.Data.Appearance);
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
        Log.Info("snapshot (mixtape only) written to " + dir);
        return layout > 0 ? 1 : WantsExit(failed0);
    }

    static void MtTap(WristPanel wrist, PanelData data, Vector2 p)
    {
        wrist.PointerMove(Hand.Left, p); wrist.Draw(data);
        wrist.PointerButton(Hand.Left, PtrButton.Primary, true, p); wrist.Draw(data);
        wrist.PointerButton(Hand.Left, PtrButton.Primary, false, p); wrist.Draw(data);
        wrist.PointerLeave(Hand.Left); wrist.Draw(data);
    }
}
