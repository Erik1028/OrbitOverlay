using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Orbit;

// --mixtapetest: the Mixtape link (Sys/MixtapeLink.cs) against a made-up server on a pipe of its own, speaking docs/mixtape-remote.md -
// hello, the state, the song list and a playlist, a cover, the lyrics read right; the buttons arrive as the protocol says; the server
// gone, the link says so; back, it links again. --mixtapetest live: the real Mixtape's pipe, read only (hello, state, library, lyrics,
// one cover) - nothing is sent that would change anything.
static class MixtapeLinkTest
{
    public static int Run(bool live)
    {
        int failed = 0;
        void Want(string line, bool ok) { if (!ok) failed++; Log.Info($"mixtapetest: {line} {ok} (want True)"); }
        if (live) return Live(Want, () => failed);
        string pipe = "Mixtape.Remote.v1.test" + Environment.ProcessId;
        var got = new List<string>();
        using var stop = new CancellationTokenSource();
        var server = Serve(pipe, got, stop.Token);
        using var link = new MixtapeLink { Pipe = pipe, Active = true, Watched = true, WantLibrary = true, WantLyrics = true };
        link.Pump();
        bool Wait(Func<bool> ok, double s = 4)
        {
            double until = Clock.Now + s;
            while (!ok() && Clock.Now < until) { Thread.Sleep(20); link.Pump(); }
            return ok();
        }
        bool linked = Wait(() => link.Status == MixtapeStatus.Connected && link.Library != null && link.Lyrics != null);
        var st = link.State;
        Want($"linked {linked}, version '{link.AppVersion}' (want 9.9), playing {st.Playing} '{st.Track?.Title}' (want True 'Song B') at {st.PositionMs} ms (want 61000), " +
             $"volume {st.Volume:0.00} (want 0.70), shuffle {st.Shuffle} repeat {st.Repeat} (want True all), queue {st.Queue.Length} (want 2)",
            linked && link.AppVersion == "9.9" && st.Playing && st.Track?.Title == "Song B" && st.PositionMs == 61000 && MathF.Abs(st.Volume - 0.7f) < 0.001f
            && st.Shuffle && st.Repeat == "all" && st.Queue.Length == 2);
        var lib = link.Library;
        Want($"the song list: {lib?.Songs.Length} songs (want 3), {lib?.Albums.Length} albums (want 2: one per album), playlist '{lib?.Playlists.FirstOrDefault()?.Name}' " +
             $"with {lib?.Playlists.FirstOrDefault()?.Songs.Length} songs (want 'Mix' 2; an id not in the list left out)",
            lib != null && lib.Songs.Length == 3 && lib.Albums.Length == 2 && lib.Playlists.Length == 1 && lib.Playlists[0].Name == "Mix" && lib.Playlists[0].Songs.Length == 2);
        var ly = link.Lyrics;
        Want($"the lyrics: synced {ly?.Synced} with {ly?.Lines.Length} lines (want True 3), the line at 5 s {ly?.LineAt(5000)} (want 1)",
            ly != null && ly.Synced && ly.Lines.Length == 3 && ly.LineAt(5000) == 1);
        link.Cover("c1", 64);
        bool cover = Wait(() => link.Cover("c1", 64) is { Length: 64 * 64 });
        var px = link.Cover("c1", 64);
        Want($"a cover came: {cover} (want True), its first pixel {(px is { Length: > 0 } ? px[0].ToString("X8") : "-")} (want FF102030)", cover && px[0] == 0xFF102030);
        link.Toggle();
        link.Seek(1234);
        link.PlaySongs(lib.Songs, 1);
        link.QueueRemove(0);
        link.SetRepeat("one");
        static bool Command(string g) => !g.Contains("\"cmd\":\"state\"") && !g.Contains("\"cmd\":\"hello\"") && !g.Contains("\"cmd\":\"library\"")
                                         && !g.Contains("\"cmd\":\"lyrics\"") && !g.Contains("\"cmd\":\"cover\"");
        bool sent = Wait(() => { lock (got) return got.Count(Command) >= 5; });
        string[] cmds;
        lock (got) cmds = got.Where(g => !g.Contains("\"cmd\":\"state\"") && !g.Contains("\"cmd\":\"hello\"") && !g.Contains("\"cmd\":\"library\"")
                                         && !g.Contains("\"cmd\":\"lyrics\"") && !g.Contains("\"cmd\":\"cover\"")).ToArray();
        bool Has(string cmd, string part) => cmds.Any(c => c.Contains($"\"cmd\":\"{cmd}\"") && c.Contains(part));
        Want($"the buttons arrived {sent}: {string.Join(" | ", cmds)}",
            sent && Has("toggle", "") && Has("seek", "\"ms\":1234") && Has("playSongs", "\"ids\":[\"1\",\"2\",\"3\"]") && Has("playSongs", "\"start\":1")
            && Has("queueRemove", "\"index\":0") && Has("repeat", "\"mode\":\"one\""));
        // the server goes: the link says so; it comes back: linked again
        stop.Cancel();
        server.Join(2000);
        bool down = Wait(() => link.Status == MixtapeStatus.NotRunning, 6);
        using var stop2 = new CancellationTokenSource();
        var server2 = Serve(pipe, got, stop2.Token);
        bool again = Wait(() => link.Status == MixtapeStatus.Connected, 6);
        Want($"the server gone: not running {down}; back: linked again {again}", down && again);
        stop2.Cancel();
        server2.Join(2000);
        Log.Info($"mixtapetest: {(failed == 0 ? "all as wanted" : failed + " NOT as wanted")}");
        return failed == 0 ? 0 : 1;
    }

    static int Live(Action<string, bool> Want, Func<int> failed)
    {
        using var link = new MixtapeLink { Active = true, Watched = true, WantLibrary = true, WantLyrics = true };
        link.Pump();
        double until = Clock.Now + 6;
        while ((link.Status != MixtapeStatus.Connected || link.Library == null || link.Lyrics == null) && Clock.Now < until) { Thread.Sleep(50); link.Pump(); }
        var st = link.State;
        Want($"live: linked {link.Status} to Mixtape {link.AppVersion} ({link.LibraryName}); playing {st.Playing} '{st.Track?.Title}' by '{st.Track?.Artist}' " +
             $"{st.PositionMs / 1000}/{(st.Track?.DurationMs ?? 0) / 1000} s, volume {st.Volume:0.00}, shuffle {st.Shuffle}, repeat {st.Repeat}, queue {st.Queue.Length}",
            link.Status == MixtapeStatus.Connected);
        var lib = link.Library;
        Want($"live: the song list {lib?.Songs.Length} songs, {lib?.Albums.Length} albums, {lib?.Playlists.Length} playlists; lyrics synced {link.Lyrics?.Synced}, {link.Lyrics?.Lines.Length} lines",
            lib != null);
        string key = st.Track?.Cover ?? lib?.Albums.FirstOrDefault(a => a.Cover != null)?.Cover;
        if (key != null)
        {
            link.Cover(key, 128);
            until = Clock.Now + 4;
            while (link.Cover(key, 128) == null && Clock.Now < until) Thread.Sleep(30);
            Want($"live: a cover ({key}) {link.Cover(key, 128)?.Length ?? 0} pixels (want {128 * 128})", link.Cover(key, 128)?.Length == 128 * 128);
        }
        return failed() == 0 ? 0 : 1;
    }

    // the made-up server: one client at a time, the answers of docs/mixtape-remote.md
    static Thread Serve(string pipe, List<string> got, CancellationToken stop)
    {
        var t = new Thread(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                using var s = new NamedPipeServerStream(pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                try { s.WaitForConnectionAsync(stop).Wait(stop); }
                catch { return; }
                var r = new StreamReader(s, new UTF8Encoding(false));
                var w = new StreamWriter(s, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
                try
                {
                    while (!stop.IsCancellationRequested)
                    {
                        var read = r.ReadLineAsync(stop).AsTask();
                        try { read.Wait(stop); } catch { return; }
                        string line = read.Result;
                        if (line == null) break;
                        lock (got) got.Add(line);
                        using var doc = JsonDocument.Parse(line);
                        int id = doc.RootElement.GetProperty("id").GetInt32();
                        string cmd = doc.RootElement.GetProperty("cmd").GetString();
                        w.WriteLine("{\"id\":" + id + ",\"ok\":true" + Answer(cmd) + "}");
                    }
                }
                catch (IOException) { }
            }
        }) { IsBackground = true };
        t.Start();
        return t;
    }

    static string Answer(string cmd)
    {
        string Song(string id, string title, string album, string cover) =>
            $"{{\"id\":\"{id}\",\"title\":\"{title}\",\"artist\":\"Artist\",\"album\":\"{album}\",\"albumArtist\":\"Artist\",\"durationMs\":200000,\"track\":{id},\"disc\":1,\"year\":2001,\"cover\":\"{cover}\"}}";
        switch (cmd)
        {
            case "hello": return ",\"app\":\"Mixtape\",\"version\":\"9.9\",\"api\":1,\"library\":\"Test\"";
            case "state":
                return ",\"v\":3,\"playing\":true,\"track\":{\"id\":\"2\",\"title\":\"Song B\",\"artist\":\"Artist\",\"album\":\"One\",\"durationMs\":200000,\"rating\":3,\"cover\":\"c1\"}," +
                       "\"positionMs\":61000,\"volume\":0.7,\"shuffle\":true,\"repeat\":\"all\",\"queue\":[" + Song("3", "Song C", "Two", "c2") + "," + Song("1", "Song A", "One", "c1") +
                       "],\"queueV\":2,\"libraryV\":1,\"lyricsV\":1";
            case "library":
                return ",\"v\":1,\"songs\":[" + Song("1", "Song A", "One", "c1") + "," + Song("2", "Song B", "One", "c1") + "," + Song("3", "Song C", "Two", "c2") +
                       "],\"playlists\":[{\"key\":\"p\",\"name\":\"Mix\",\"songs\":[\"3\",\"1\",\"404\"]}]";
            case "lyrics": return ",\"id\":\"2\",\"synced\":true,\"lines\":[{\"ms\":0,\"text\":\"one\"},{\"ms\":4000,\"text\":\"two\"},{\"ms\":8000,\"text\":\"three\"}]";
            case "cover":
                var px = new byte[64 * 64 * 4];
                for (int i = 0; i < px.Length; i += 4) { px[i] = 0x30; px[i + 1] = 0x20; px[i + 2] = 0x10; px[i + 3] = 0xFF; }
                return ",\"w\":64,\"h\":64,\"bgra\":\"" + Convert.ToBase64String(px) + "\"";
            default: return "";
        }
    }
}
