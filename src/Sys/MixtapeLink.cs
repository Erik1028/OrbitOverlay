using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Orbit;

// Mixtape on the watch (Erik 10-19: "lehetne egy fullos mixtape overlay mint kinyíló widget - a dallistám, lyrics, meg minden főbb
// funkció"): Erik's iPod manager plays the music; its Remote API (docs/mixtape-remote.md, a named pipe) tells what plays, the Up Next
// queue, the song list and playlists, the covers and the lyrics, and takes the buttons. MixtapeLink is the client; the widget
// (Panels/WristMixtape.cs) reads a MixtapeSource - this link, or the tools' made-up one (MixtapeFake).
public enum MixtapeStatus { Off, NotRunning, Connected }

public sealed class MixtapeSong
{
    public string Id = "", Title = "", Artist = "", Album = "", AlbumArtist = "", Cover;
    public int DurationMs, Track, Disc, Year;
    string _duration;
    public string Duration => _duration ??= Time(DurationMs);        // (made once: the lists draw it every frame)
    public static string Time(int ms)
    {
        int s = Math.Max(0, ms / 1000);
        return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
    }
}

public sealed class MixtapePlaylist
{
    public string Key = "", Name = "";
    public MixtapeSong[] Songs = Array.Empty<MixtapeSong>();
}

public sealed class MixtapeAlbum
{
    public string Key = "", Title = "", Artist = "", Cover;
    public int Year;
    public MixtapeSong[] Songs = Array.Empty<MixtapeSong>();
}

// the song list as Mixtape lists it, its albums (grouped here: album artist + album, songs by disc and number) and playlists
public sealed class MixtapeLibrary
{
    public int V;
    public MixtapeSong[] Songs = Array.Empty<MixtapeSong>();
    public MixtapeAlbum[] Albums = Array.Empty<MixtapeAlbum>();
    public MixtapePlaylist[] Playlists = Array.Empty<MixtapePlaylist>();
    public readonly Dictionary<string, MixtapeSong> ById = new(StringComparer.Ordinal);

    public static MixtapeLibrary Build(int v, MixtapeSong[] songs, List<(string key, string name, string[] ids)> playlists)
    {
        var lib = new MixtapeLibrary { V = v, Songs = songs };
        foreach (var s in songs) lib.ById[s.Id] = s;
        lib.Albums = songs
            .Where(s => s.Album.Length > 0)
            .GroupBy(s => ((s.AlbumArtist.Length > 0 ? s.AlbumArtist : s.Artist).ToLowerInvariant(), s.Album.ToLowerInvariant()))
            .Select(g =>
            {
                var list = g.OrderBy(s => s.Disc).ThenBy(s => s.Track).ThenBy(s => s.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
                var first = list[0];
                return new MixtapeAlbum
                {
                    Key = g.Key.Item1 + "\u001F" + g.Key.Item2, Title = first.Album,
                    Artist = first.AlbumArtist.Length > 0 ? first.AlbumArtist : first.Artist, Year = list.Max(x => x.Year),
                    Cover = list.Select(x => x.Cover).FirstOrDefault(c => c != null), Songs = list,
                };
            })
            .OrderBy(a => a.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        lib.Playlists = playlists.Select(p => new MixtapePlaylist
        {
            Key = p.key, Name = p.name,
            Songs = p.ids.Select(id => lib.ById.TryGetValue(id, out var s) ? s : null).Where(s => s != null).ToArray(),
        }).ToArray();
        return lib;
    }
}

public sealed class MixtapeLyrics
{
    public string TrackId = "";
    public bool Synced;
    public int[] Ms = Array.Empty<int>();
    public string[] Lines = Array.Empty<string>();

    // the line sung at ms (synced), -1 before the first
    public int LineAt(int ms)
    {
        if (!Synced) return -1;
        int lo = 0, hi = Ms.Length - 1, found = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (Ms[mid] <= ms) { found = mid; lo = mid + 1; } else hi = mid - 1;
        }
        return found;
    }
}

// What plays: a new object each time the API's state moved (the watch compares MixtapeSource.Version)
public sealed class MixtapeState
{
    public int V, QueueV, LibraryV, LyricsV;
    public bool Playing;
    public MixtapeSong Track;                // null: nothing loaded
    public int Rating;                       // 0..5
    public int PositionMs;
    public double At;                        // Clock.Now when PositionMs was read
    public float Volume = 1;
    public bool Shuffle;
    public string Repeat = "off";            // off / all / one
    public MixtapeSong[] Queue = Array.Empty<MixtapeSong>();

    // where the track is now (moving on from the last reading while it plays)
    public int PositionNow(double now)
    {
        if (Track == null) return 0;
        int p = Playing ? PositionMs + (int)((now - At) * 1000) : PositionMs;
        return Math.Clamp(p, 0, Math.Max(0, Track.DurationMs));
    }

    public MixtapeState Copy() => (MixtapeState)MemberwiseClone();
}

// The widget's side: the link to the real Mixtape, or the tools' made-up one
public abstract class MixtapeSource
{
    public MixtapeStatus Status { get; protected set; }
    public string AppVersion { get; protected set; }
    public string LibraryName { get; protected set; }
    public MixtapeState State { get; protected set; } = new();
    public MixtapeLibrary Library { get; protected set; }
    public MixtapeLyrics Lyrics { get; protected set; }
    public int Version => Volatile.Read(ref _version);        // moves on anything the watch shows
    protected int _version;
    protected void Changed() => Interlocked.Increment(ref _version);

    // set by the app each frame: a page has the widget (the link runs), the watch shows it / it is opened (faster), lyrics wanted
    public bool Active, Watched, WantLyrics, WantLibrary;

    // a cover, made for a size x size square (64 / 128 / 256): null until it has come (it is asked for then; Version moves when it comes)
    public abstract uint[] Cover(string key, int size);

    // the buttons: the state shows the result at once (the next reading confirms it)
    protected abstract void Send(string cmd, params (string name, object value)[] args);

    public void Toggle() { Optimistic(s => s.Playing = !s.Playing); Send("toggle"); }
    public void Next() => Send("next");
    public void Prev() => Send("prev");
    public void Seek(int ms) { Optimistic(s => { s.PositionMs = ms; s.At = global::Orbit.Clock.Now; }); Send("seek", ("ms", ms)); }
    public void SetVolume(float v) { Optimistic(s => s.Volume = v); Send("volume", ("v", Math.Round(v, 3))); }
    public void SetShuffle(bool on) { Optimistic(s => s.Shuffle = on); Send("shuffle", ("on", on)); }
    public void SetRepeat(string mode) { Optimistic(s => s.Repeat = mode); Send("repeat", ("mode", mode)); }
    public void PlaySongs(IReadOnlyList<MixtapeSong> songs, int start) =>
        Send("playSongs", ("ids", songs.Select(s => s.Id).ToArray()), ("start", start));
    public void QueueAdd(IReadOnlyList<MixtapeSong> songs) => Send("queueAdd", ("ids", songs.Select(s => s.Id).ToArray()));
    public void QueueNext(IReadOnlyList<MixtapeSong> songs) => Send("queueNext", ("ids", songs.Select(s => s.Id).ToArray()));
    public void QueueRemove(int i) { Optimistic(s => s.Queue = s.Queue.Where((_, k) => k != i).ToArray()); Send("queueRemove", ("index", i)); }
    public void QueueClear() { Optimistic(s => s.Queue = Array.Empty<MixtapeSong>()); Send("queueClear"); }
    public void QueueJump(int i) => Send("queueJump", ("index", i));
    public void Rate(int stars) { Optimistic(s => s.Rating = stars); Send("rate", ("stars", stars)); }

    void Optimistic(Action<MixtapeState> change)
    {
        var s = State.Copy();
        if (s.Playing) { s.PositionMs = s.PositionNow(global::Orbit.Clock.Now); s.At = global::Orbit.Clock.Now; }
        change(s);
        State = s;
        Changed();
    }

    public static string Pretty(string repeat) => repeat == "one" ? "one" : repeat == "all" ? "all" : "off";
}

// The real one: a thread of its own, started by the first Mixtape widget. Connected: the state 4 times a second while the watch shows it
// (once a second when it does not), the song list when its version moves (and the first time the opened view wants it), the lyrics when
// theirs moves (while they show), covers as they are asked for. Mixtape not running (or its Remote API off): tried again every 2 s.
public sealed class MixtapeLink : MixtapeSource, IDisposable
{
    public const string PipeName = "Mixtape.Remote.v1";
    public string Pipe = PipeName;                    // (--mixtapetest: a pipe of its own)
    readonly AutoResetEvent _wake = new(false);
    readonly ConcurrentQueue<string> _commands = new();
    readonly ConcurrentQueue<(string key, int size)> _coverWants = new();
    readonly object _coverLock = new();
    readonly Dictionary<(string, int), uint[]> _covers = new();
    readonly HashSet<(string, int)> _coverPending = new();
    readonly LinkedList<(string, int)> _coverOrder = new();
    const int MaxCovers = 96;
    Thread _thread;
    volatile bool _stop;
    NamedPipeClientStream _pipe;
    StreamReader _in;
    StreamWriter _out;
    int _nextId;
    double _nextPoll, _nextTry;
    int _lyricsVHad = -1, _libraryVHad = -1;
    bool _wantedLibrary;

    public void Run()
    {
        if (_thread != null) return;
        _thread = new Thread(Loop) { IsBackground = true, Name = "OrbitOverlay Mixtape", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    // the app, every frame (cheap): wake the thread when it is wanted sooner
    public void Pump()
    {
        if (Active && _thread == null) Run();
        if (Active && (WantLibrary && Library == null || Watched)) _wake.Set();
    }

    public override uint[] Cover(string key, int size)
    {
        if (string.IsNullOrEmpty(key)) return null;
        lock (_coverLock)
        {
            if (_covers.TryGetValue((key, size), out var px)) return px;
            if (_coverPending.Add((key, size))) { _coverWants.Enqueue((key, size)); _wake.Set(); }
        }
        return null;
    }

    protected override void Send(string cmd, params (string name, object value)[] args)
    {
        var o = new Dictionary<string, object> { ["cmd"] = cmd };
        foreach (var (n, v) in args) o[n] = v;
        _commands.Enqueue(JsonSerializer.Serialize(o));
        _wake.Set();
    }

    void Loop()
    {
        while (!_stop)
        {
            double now = global::Orbit.Clock.Now;
            try
            {
                if (!Active)
                {
                    Disconnect(MixtapeStatus.Off);
                    _wake.WaitOne(1000);
                    continue;
                }
                if (_pipe == null)
                {
                    if (now < _nextTry) { _wake.WaitOne(Math.Max(50, (int)((_nextTry - now) * 1000))); continue; }
                    if (!Connect()) { _nextTry = global::Orbit.Clock.Now + 2; Disconnect(MixtapeStatus.NotRunning); continue; }
                }
                bool sent = false;
                while (_commands.TryDequeue(out var c))
                {
                    try { Request(c).Dispose(); }
                    catch (MixtapeRefused e) { Log.Info("mixtape: refused - " + e.Message); }
                    sent = true;
                }
                if (sent || now >= _nextPoll)
                {
                    ReadState();
                    _nextPoll = global::Orbit.Clock.Now + (Watched ? 0.25 : 1.0);
                }
                var st = State;
                if (Library == null ? WantLibrary || _wantedLibrary : st.LibraryV != _libraryVHad) ReadLibrary();
                if (WantLyrics && st.LyricsV != _lyricsVHad) ReadLyrics();
                while (_coverWants.TryDequeue(out var cw)) ReadCover(cw.key, cw.size);
                double wait = _nextPoll - global::Orbit.Clock.Now;
                if (wait > 0.005 && _commands.IsEmpty && _coverWants.IsEmpty) _wake.WaitOne((int)(wait * 1000));
            }
            catch (Exception e)
            {
                if (_pipe != null) Log.Info("mixtape: the link closed (" + e.Message + ")");
                Disconnect(MixtapeStatus.NotRunning);
                _nextTry = global::Orbit.Clock.Now + 2;
            }
        }
        Disconnect(MixtapeStatus.Off);
    }

    bool Connect()
    {
        var p = new NamedPipeClientStream(".", Pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
        try { p.Connect(250); }
        catch { p.Dispose(); return false; }
        _pipe = p;
        _in = new StreamReader(p, new UTF8Encoding(false));
        _out = new StreamWriter(p, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        using var hello = Request("{\"cmd\":\"hello\"}");
        var r = hello.RootElement;
        AppVersion = Str(r, "version");
        LibraryName = Str(r, "library");
        _libraryVHad = _lyricsVHad = -1;
        _wantedLibrary = Library != null;            // (a library seen before: fetch it again on reconnect)
        Library = null;
        Lyrics = null;
        Status = MixtapeStatus.Connected;
        Log.Info($"mixtape: connected to Mixtape {AppVersion} ({LibraryName ?? "no library"})");
        Changed();
        return true;
    }

    void Disconnect(MixtapeStatus status)
    {
        if (_pipe != null)
        {
            try { _pipe.Dispose(); } catch { }
            _pipe = null; _in = null; _out = null;
        }
        if (Status != status) { Status = status; Changed(); }
    }

    // one request, its answer (ok: false throws for reads; the commands' answers are just read)
    JsonDocument Request(string json)
    {
        int id = ++_nextId;
        string line = json.Length > 1 && json[0] == '{' ? "{\"id\":" + id + "," + json[1..] : json;
        _out.WriteLine(line);
        var read = _in.ReadLineAsync();
        if (!read.Wait(TimeSpan.FromSeconds(4))) throw new IOException("no answer in 4 s");
        string answer = read.Result ?? throw new IOException("Mixtape closed the link");
        var doc = JsonDocument.Parse(answer);
        if (doc.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False)
        {
            string err = Str(doc.RootElement, "error") ?? "error";
            doc.Dispose();
            throw new MixtapeRefused(err);
        }
        return doc;
    }

    sealed class MixtapeRefused : Exception { public MixtapeRefused(string m) : base(m) { } }

    void ReadState()
    {
        using var doc = Request("{\"cmd\":\"state\"}");
        var r = doc.RootElement;
        var s = new MixtapeState
        {
            V = Int(r, "v"), QueueV = Int(r, "queueV"), LibraryV = Int(r, "libraryV"), LyricsV = Int(r, "lyricsV"),
            Playing = Bool(r, "playing"), PositionMs = Int(r, "positionMs"), At = global::Orbit.Clock.Now,
            Volume = (float)Num(r, "volume", 1), Shuffle = Bool(r, "shuffle"), Repeat = Pretty(Str(r, "repeat") ?? "off"),
        };
        if (r.TryGetProperty("track", out var t) && t.ValueKind == JsonValueKind.Object)
        {
            s.Track = Song(t);
            s.Rating = Int(t, "rating");
        }
        var old = State;
        if (r.TryGetProperty("queue", out var q) && q.ValueKind == JsonValueKind.Array)
            s.Queue = old != null && old.QueueV == s.QueueV && old.Queue.Length == q.GetArrayLength() ? old.Queue : q.EnumerateArray().Select(Song).ToArray();
        bool moved = old == null || old.V != s.V || old.Playing != s.Playing || old.QueueV != s.QueueV || old.LyricsV != s.LyricsV
            || old.LibraryV != s.LibraryV || Math.Abs(old.PositionNow(s.At) - s.PositionMs) > 1500;
        State = s;
        if (moved) Changed();
    }

    void ReadLibrary()
    {
        using var doc = Request("{\"cmd\":\"library\"}");
        var r = doc.RootElement;
        var songs = r.TryGetProperty("songs", out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().Select(Song).ToArray() : Array.Empty<MixtapeSong>();
        var lists = new List<(string, string, string[])>();
        if (r.TryGetProperty("playlists", out var pl) && pl.ValueKind == JsonValueKind.Array)
            foreach (var p in pl.EnumerateArray())
                lists.Add((Str(p, "key") ?? "", Str(p, "name") ?? "", p.TryGetProperty("songs", out var ids) && ids.ValueKind == JsonValueKind.Array
                    ? ids.EnumerateArray().Select(x => x.GetString() ?? "").ToArray() : Array.Empty<string>()));
        int v = Int(r, "v");
        Library = MixtapeLibrary.Build(v, songs, lists);
        _libraryVHad = State.LibraryV;
        _wantedLibrary = false;
        Log.Info($"mixtape: the song list - {songs.Length} songs, {Library.Albums.Length} albums, {lists.Count} playlists");
        Changed();
    }

    void ReadLyrics()
    {
        using var doc = Request("{\"cmd\":\"lyrics\"}");
        var r = doc.RootElement;
        var ly = new MixtapeLyrics { TrackId = Str(r, "id") ?? "", Synced = Bool(r, "synced") };
        if (r.TryGetProperty("lines", out var lines) && lines.ValueKind == JsonValueKind.Array)
        {
            var ms = new List<int>();
            var text = new List<string>();
            foreach (var l in lines.EnumerateArray()) { ms.Add(Int(l, "ms")); text.Add(Str(l, "text") ?? ""); }
            ly.Ms = ms.ToArray();
            ly.Lines = text.ToArray();
        }
        Lyrics = ly;
        _lyricsVHad = State.LyricsV;
        Changed();
    }

    void ReadCover(string key, int size)
    {
        uint[] px = null;
        try
        {
            using var doc = Request(JsonSerializer.Serialize(new Dictionary<string, object> { ["cmd"] = "cover", ["key"] = key, ["size"] = size }));
            var r = doc.RootElement;
            int w = Int(r, "w"), h = Int(r, "h");
            byte[] bytes = Convert.FromBase64String(Str(r, "bgra") ?? "");
            if (w == size && h == size && bytes.Length == size * size * 4)
            {
                px = new uint[size * size];
                Buffer.BlockCopy(bytes, 0, px, 0, bytes.Length);
            }
        }
        catch (MixtapeRefused) { }          // (no such cover: an empty one is kept, not asked again)
        lock (_coverLock)
        {
            _coverPending.Remove((key, size));
            _covers[(key, size)] = px ?? Array.Empty<uint>();
            _coverOrder.AddLast((key, size));
            while (_coverOrder.Count > MaxCovers) { _covers.Remove(_coverOrder.First.Value); _coverOrder.RemoveFirst(); }
        }
        Changed();
    }

    static MixtapeSong Song(JsonElement e) => new()
    {
        Id = Str(e, "id") ?? "", Title = Str(e, "title") ?? "", Artist = Str(e, "artist") ?? "", Album = Str(e, "album") ?? "",
        AlbumArtist = Str(e, "albumArtist") ?? "", Cover = Str(e, "cover"), DurationMs = Int(e, "durationMs"),
        Track = Int(e, "track"), Disc = Int(e, "disc"), Year = Int(e, "year"),
    };

    static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    static int Int(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long l) ? (int)Math.Clamp(l, int.MinValue, int.MaxValue) : 0;
    static double Num(JsonElement e, string name, double def) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : def;
    static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    public void Dispose()
    {
        _stop = true;
        _wake.Set();
        _thread?.Join(1000);
    }
}
