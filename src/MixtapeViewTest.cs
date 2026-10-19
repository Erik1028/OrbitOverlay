using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;

namespace Orbit;

// ORBIT_SNAP_ONLY=mixtape --snapshot <dir>: Mixtape in Now playing (Panels/WristMixtape.cs) without Mixtape - a made-up source
// (MixtapeFake: placeholder songs, albums with drawn covers, playlists, a queue, placeholder synced lines): the Now playing cards on
// Mixtape's song in every style (their play goes to Mixtape, not the app), opened - Mixtape's player, its six tabs (and an album / a
// playlist opened) in Modern, Cyber and 95, taps that must reach the source (a song, the queue's cross, a lyrics line); another player
// playing: the card is its song (the app's command), opened the usual view, its Mixtape button Mixtape's player; Mixtape not running:
// the usual card; the layout check of every size; an older face's Mixtape widgets merged into Now playing (WatchPages.MergeMixtape).
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

    public bool NoCovers;            // the layout check: no covers (as the watch's own check runs Now playing without one)

    public override uint[] Cover(string key, int size)
    {
        if (string.IsNullOrEmpty(key) || NoCovers) return null;
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
        var keep = (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Pages, wcfg.Page, wcfg.Background, wrist.Mixtape, data.FakeMedia, wrist.OnAction);
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
                data.FakeMedia = (false, null, null, false);        // no other player: Now playing is Mixtape's song
                string acted = null;
                wrist.OnAction = a => acted = a;
                wcfg.Face = "custom";
                wcfg.Background = "panel";
                wcfg.Grid = null; wcfg.Pages = null; wcfg.Page = 0;
                wcfg.Widgets = new List<WatchWidget>
                {
                    new() { Type = "media", X = 0, Y = 0, W = 4, H = 2 }, new() { Type = "media", X = 4, Y = 0, W = 2, H = 1 },
                    new() { Type = "time", X = 0, Y = 2, W = 3, H = 2 },
                };
                wrist.Settle(); wrist.Draw(data); wrist.Draw(data);
                wrist.Surf.SavePng(gpu, Path.Combine(dir, $"mixtape_face{tag}.png"));
                bool lead = wrist.MixtapeLeadsForTest;
                // the card's play: a toggle to Mixtape, nothing to the app (the retro styles: when their card has the button)
                Vector2? p = null;
                for (int k = 0; k < 24 && p == null; k++) p = wrist.ControlPoint(data, Ui.Id("wcm", k * 3 + 1));
                int n0 = fake.Sent.Count;
                if (p is { } pp) MtTap(wrist, data, pp);
                string toMixtape = fake.Sent.Count > n0 ? fake.Last() : "nothing";
                Want($"mixtape{tag}: Mixtape's song in Now playing {lead} (want True); the card's play sent '{toMixtape}' to Mixtape, '{acted ?? "nothing"}' to the app " +
                     $"(want toggle, nothing){(p == null ? " - no play button found" : "")}",
                    lead && (p == null ? classic : toMixtape == "toggle" && acted == null));
                // opened: Mixtape's player, every tab
                bool opened = wrist.OpenForTest("media") && wrist.DetailKind == "mixtape";
                for (int i = 0; i < 6; i++)
                {
                    wrist.MixtapeTabForTest(i);
                    for (int f = 0; f < 12; f++) { Thread.Sleep(15); wrist.Draw(data); }
                    wrist.Surf.SavePng(gpu, Path.Combine(dir, $"mixtape_tab{i}{tag}.png"));
                    if (classic) break;                  // (the retro styles: one view)
                }
                Want($"mixtape{tag}: Now playing on Mixtape's song opens Mixtape's player {opened} (want True; opened '{wrist.DetailKind}')", opened);
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
                    Want($"mixtape{tag}: a song tapped sent '{played}' (want {wantPlay}), the queue's cross '{removed}' (want queueRemove index=0), " +
                         $"a lyrics line '{seek}' (want seek ms=64000)",
                        played == wantPlay && removed == "queueRemove index=0" && seek == "seek ms=64000");
                }
                wrist.CloseForTest();
                wrist.MixtapeTabForTest(0);
                fake.Hold();                     // (the card's play above started it: paused again)
                // another player plays: its song on the card, its play the app's; opened the usual view - and its Mixtape button Mixtape's player
                data.FakeMedia = (true, "Other Player Song", "Other Player", true);
                wrist.Settle(); wrist.Draw(data); wrist.Draw(data);
                wrist.Surf.SavePng(gpu, Path.Combine(dir, $"mixtape_other{tag}.png"));
                bool other = !wrist.MixtapeLeadsForTest;
                acted = null;
                int n1 = fake.Sent.Count;
                p = null;
                for (int k = 0; k < 24 && p == null; k++) p = wrist.ControlPoint(data, Ui.Id("wcm", k * 3 + 1));
                if (p is { } pp2) MtTap(wrist, data, pp2);
                bool toApp = p == null ? classic : acted == "media.play" && fake.Sent.Count == n1;
                wrist.OpenForTest("media");
                for (int f = 0; f < 4; f++) wrist.Draw(data);
                string k1 = wrist.DetailKind;
                wrist.Surf.SavePng(gpu, Path.Combine(dir, $"mixtape_other_open{tag}.png"));
                var sw = wrist.ControlPoint(data, classic ? "wd95mt" : "wdmt");
                if (sw is { } swp) MtTap(wrist, data, swp);
                for (int f = 0; f < 4; f++) wrist.Draw(data);
                string k2 = wrist.DetailKind;
                // (a style's own window - XP, Aero, Mac, Terminal - has no Mixtape button: the usual view stays)
                bool own = Theme.Retro is RetroStyle.XP or RetroStyle.Aero or RetroStyle.Mac or RetroStyle.Terminal;
                Want($"mixtape{tag}: another player playing - Mixtape's song not shown {other} (want True), its play to the app {toApp} (want True), opened '{k1}' (want media), " +
                     $"its Mixtape button {(sw == null ? "none" : "found")} -> '{k2}' (want {(own ? "media: no button in this style's window" : "mixtape")})",
                    other && toApp && k1 == "media" && (own ? k2 == "media" : sw != null && k2 == "mixtape"));
                wrist.CloseForTest();
                // not running: the usual card (nothing playing)
                data.FakeMedia = (false, null, null, false);
                wrist.Mixtape = new MixtapeFake(connected: false);
                wrist.Settle(); wrist.Draw(data); wrist.Draw(data);
                wrist.Surf.SavePng(gpu, Path.Combine(dir, $"mixtape_off{tag}.png"));
                Want($"mixtape{tag}: Mixtape not running - Now playing its usual self {!wrist.MixtapeLeadsForTest} (want True)", !wrist.MixtapeLeadsForTest);
                wrist.Mixtape = new MixtapeFake { NoCovers = true };
                ((MixtapeFake)wrist.Mixtape).Hold();
                layout += LayoutCheckRun(wrist, data, cfg, painter, gpu, dir, tag.Length > 0 ? tag[1..] : "modern", onlyKind: "media");
            }
            MergeCheck(dir);
        }
        finally
        {
            (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Pages, wcfg.Page, wcfg.Background, wrist.Mixtape, data.FakeMedia, wrist.OnAction) = keep;
            wrist.CloseForTest();
            cfg.Data.Appearance = new AppearanceConfig { Clock24h = true };
            Theme.Apply(cfg.Data.Appearance);
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
        Log.Info("snapshot (mixtape only) written to " + dir);
        return layout > 0 ? 1 : WantsExit(failed0);
    }

    // an older face's Mixtape widgets: Now playing in their place (one row on 6 x 4 and in a folder), gone beside a Now playing; once
    static void MergeCheck(string dir)
    {
        string md = Path.Combine(dir, "merge");
        if (Directory.Exists(md)) Directory.Delete(md, true);
        Directory.CreateDirectory(md);
        string mp = Path.Combine(md, "config.json");
        var old = new Config();
        old.Wrist.Face = "custom";
        old.Wrist.Widgets = new List<WatchWidget>
        {
            new() { Type = "media", X = 0, Y = 2, W = 2, H = 1 }, new() { Type = "mixtape", X = 2, Y = 2, W = 2, H = 1 },
            new() { Type = "folder", X = 4, Y = 3, W = 1, H = 1, Name = "Music", Children = new List<WatchWidget> { new() { Type = "mixtape", X = 0, Y = 0, W = 3, H = 2 } } },
        };
        old.Wrist.Pages = new List<WatchPage>
        {
            new() { Grid = "4x3", Widgets = new List<WatchWidget> { new() { Type = "mixtape", X = 0, Y = 0, W = 2, H = 2 } } },
            new() { Grid = "6x4", Widgets = new List<WatchWidget> { new() { Type = "mixtape", X = 0, Y = 0, W = 3, H = 2 }, new() { Type = "time", X = 3, Y = 0, W = 3, H = 2 } } },
        };
        old.Wrist.PagesVersion = 1;
        old.General.LastVersion = "1.15.1";
        File.WriteAllText(mp, System.Text.Json.JsonSerializer.Serialize(old));
        var s1 = new ConfigStore(mp);
        bool m1 = WatchPages.MergeMixtape(s1);
        s1.SaveIfDue(force: true);
        var s2 = new ConfigStore(mp);
        bool m2 = WatchPages.MergeMixtape(s2);
        var w = s2.Data.Wrist;
        static string Desc(List<WatchWidget> l) => string.Join(" ", l.Select(x => x.Children != null ? $"[{Desc(x.Children)}]" : $"{x.Type}{x.W}x{x.H}@{x.X},{x.Y}"));
        string p1 = Desc(w.Widgets), p2 = Desc(w.Pages[0].Widgets), p3 = Desc(w.Pages[1].Widgets);
        int baks = Directory.GetFiles(md, "config.json.bak-mixtape-*").Length;
        Want($"mixtape: an older face merged {m1} (want True), again {m2} (want False); page 1 '{p1}' (want 'media2x1@0,2 [media3x1@0,0]': the Mixtape one beside " +
             $"Now playing gone, the folder's one row high), the 4 x 3 page '{p2}' (want 'media2x2@0,0'), the 6 x 4 page '{p3}' (want 'media3x1@0,0 time3x2@3,0'), " +
             $"copies of the config {baks} (want 1)",
            m1 && !m2 && p1 == "media2x1@0,2 [media3x1@0,0]" && p2 == "media2x2@0,0" && p3 == "media3x1@0,0 time3x2@3,0" && baks == 1);
    }

    static void MtTap(WristPanel wrist, PanelData data, Vector2 p)
    {
        wrist.PointerMove(Hand.Left, p); wrist.Draw(data);
        wrist.PointerButton(Hand.Left, PtrButton.Primary, true, p); wrist.Draw(data);
        wrist.PointerButton(Hand.Left, PtrButton.Primary, false, p); wrist.Draw(data);
        wrist.PointerLeave(Hand.Left); wrist.Draw(data);
    }
}
