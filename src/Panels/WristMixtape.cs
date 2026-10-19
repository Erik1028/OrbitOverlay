using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Vortice.Direct2D1;
using Vortice.DirectWrite;

namespace Orbit;

// Mixtape in Now playing (Erik 10-19: "lehetne egy fullos mixtape overlay mint kinyíló widget - a dallistám, lyrics, meg minden főbb
// funkció"; the same day: "a mixtape es now playing widgetet ossze kellene olvasztani" - its own widget went into Now playing).
// Erik's iPod manager plays the music on the PC; its Remote API (docs/mixtape-remote.md, Sys/MixtapeLink.cs) gives what plays, the Up
// Next queue, the song list, the albums and playlists, the covers and the lyrics, and takes the buttons.
// The card is Now playing's own (WristCustom.MediaNow reads Mixtape while it leads - see "in Now playing" below).
// Opened ("mixtape", the opened kind of Now playing on Mixtape), six tabs in its header: Playing (the cover big, the song, the time line to seek on, shuffle / previous / play / next / repeat,
// the volume), Up next (the queue: a tap plays that one, the cross takes it out, Clear), Songs (the whole song list: a tap plays it and
// lines up the ones after it; Shuffle all), Albums (a grid of covers; a tap opens the album: Play, Shuffle, Queue, its songs), Playlists
// (the library's own; opened like an album) and Lyrics (the playing song's, synced ones follow the song with the line being sung bright
// - a tap on a line jumps there; the stick scrolls them, the song takes over again 4 s later). The stick scrolls every list; a long list
// also has a bar on its right to drag.
// Modern and Cyber draw the same layout in their own colours (Bento's tokens follow the style; Cyber's cover box, brackets, mono
// labels and the yellow line); the retro styles show the song and its buttons and the queue as a 1995 list (Mixtape95Detail).
public sealed partial class WristPanel
{
    public const string MixtapeKindIcon = "";            // Segoe's album: the widget in the palette
    const string MtNotRunning = "Mixtape is not running";

    public MixtapeSource Mixtape;                               // the app's link (or the tools' made-up one); null: none
    int _mtVersion = -1;
    enum MtTab { Playing, Queue, Songs, Albums, Playlists, Lyrics }
    static readonly string[] MtTabIcons = { "", "", "", "", "", "" };
    static readonly string[] MtTabNames = { "Playing", "Up next", "Songs", "Albums", "Playlists", "Lyrics" };
    MtTab _mtTab;
    MixtapeAlbum _mtAlbum;                                      // an album opened in the Albums tab
    MixtapePlaylist _mtList;                                    // a playlist opened in the Playlists tab
    readonly float[] _mtScroll = new float[8];                  // each list's scroll (px): the tabs, the album and the playlist opened
    double _mtLyricsHandUntil;                                  // the stick scrolled the lyrics: the song does not move them until then
    float _mtLyricsY = float.NaN;

    bool MixtapeOn => C.Face == "custom" && (HasWidget("media") || DetailIs("mixtape"));

    // the tools: a tab of the opened view (and its first album / playlist opened)
    public void MixtapeTabForTest(int tab, bool openFirst = false)
    {
        _mtTab = (MtTab)tab;
        _mtAlbum = openFirst && _mtTab == MtTab.Albums ? Mixtape?.Library?.Albums.FirstOrDefault() : null;
        _mtList = openFirst && _mtTab == MtTab.Playlists ? Mixtape?.Library?.Playlists.FirstOrDefault() : null;
        _mtLyricsHandUntil = 0;
        _mtLyricsY = float.NaN;
        _dirty = true;
    }

    // something new to show (WristPanel.Update): the state, the list, a cover, the lyrics
    bool MixtapeChanged()
    {
        if (!MixtapeOn || Mixtape == null) return false;
        int v = Mixtape.Version;
        if (v == _mtVersion) return false;
        _mtVersion = v;
        return true;
    }

    // the link's wishes, each frame the watch is updated (the song list only once a tab needs it, the lyrics while their tab shows)
    void MixtapeFrame()
    {
        var m = Mixtape;
        if (m == null) return;
        bool open = DetailIs("mixtape");
        m.WantLibrary = open && _mtTab is MtTab.Songs or MtTab.Albums or MtTab.Playlists;
        m.WantLyrics = open && _mtTab == MtTab.Lyrics || _mtTab == MtTab.Lyrics && HasWidget("media");
        if (open && m.State.Playing) AnimWake.At(Clock.Now + 0.25);      // the time line and the lyrics move while it plays
    }

    // ---------------- the covers: a bitmap per (key, size), the last 72 kept ----------------
    readonly Dictionary<(string, int), ID2D1Bitmap> _mtCovers = new();
    readonly LinkedList<(string, int)> _mtCoverOrder = new();

    ID2D1Bitmap MtCover(string key, int size)
    {
        if (string.IsNullOrEmpty(key) || Mixtape == null) return null;
        if (_mtCovers.TryGetValue((key, size), out var b)) return b;
        var px = Mixtape.Cover(key, size);
        if (px == null) return null;                            // (asked for: it comes with a later frame)
        b = px.Length == size * size ? _p.CreateBitmap(px, size, size) : null;
        _mtCovers[(key, size)] = b;
        _mtCoverOrder.AddLast((key, size));
        while (_mtCoverOrder.Count > 72)
        {
            var old = _mtCoverOrder.First.Value;
            _mtCoverOrder.RemoveFirst();
            if (_mtCovers.Remove(old, out var ob)) ob?.Dispose();
        }
        return b;
    }

    void MtDispose()
    {
        foreach (var b in _mtCovers.Values) b?.Dispose();
        _mtCovers.Clear();
        _mtCoverOrder.Clear();
    }

    // a cover in a box: Modern rounded on its tile, Cyber in its dark-red box; none - the note
    void MtCoverBox(RectangleF box, string key, int size, float radius)
    {
        var bmp = MtCover(key, size);
        if (Theme.Cyber)
        {
            _p.Fill(box, CyberHud.Card2);
            if (bmp != null) { _p.PushClip(box); _p.Image(bmp, RectangleF.Inflate(box, -2, -2)); _p.PopClip(); }
            else _p.Icon("", box, CyberHud.Faint, MathF.Min(box.Width, box.Height) * 0.36f);
            return;
        }
        _p.Fill(box, Bento.Tile, radius);
        if (bmp != null)
        {
            _p.PushRoundedClip(box, radius);
            _p.Image(bmp, box);
            _p.PopRoundedClip();
        }
        else _p.Icon("", box, Sub, MathF.Min(box.Width, box.Height) * 0.36f);
    }

    // ---------------- in Now playing (Erik 10-19: "a mixtape es now playing widgetet ossze kellene olvasztani") ----------------
    // Now playing shows Mixtape's song when Mixtape plays, or is paused with no other player playing (the Windows media session's
    // player: a browser, Spotify...); nothing playing anywhere and Mixtape linked: Mixtape too (opened: its library, to start one). Then
    // the card's buttons are Mixtape's (Act), its cover is Mixtape's (MediaNow), and a tap opens Mixtape's whole player.
    const int MtCardCover = 128;
    bool _mtLead;                    // the Now playing drawn last showed Mixtape's song
    bool _openMt;                    // the Now playing opened is Mixtape's player (chosen as it opened, or by its Mixtape button)
    bool MixtapeUp => Mixtape is { Status: MixtapeStatus.Connected };
    public bool MixtapeLeadsForTest => _mtLead;

    bool MixtapeLeads(bool has, string title, bool playing)
    {
        if (!MixtapeUp) return false;
        var st = Mixtape.State;
        if (st.Track != null && st.Playing) return true;
        if (playing && !(st.Track != null && title == st.Track.Title)) return false;     // another player plays (Mixtape's own session: its song)
        return st.Track != null || !has;
    }

    // the opened view of a widget's kind: Now playing opened on Mixtape - Mixtape's player
    string OpenKindOf(string type)
    {
        string k = DetailOf(type);
        return k == "media" && _openMt && MixtapeUp ? "mixtape" : k;
    }

    // every button of the watch reaches the app through here (OrbitApp.WristAction); Now playing's three go to Mixtape while it shows
    // Mixtape's song
    void Act(string id)
    {
        if (id is "media.prev" or "media.play" or "media.next" && _mtLead && Mixtape is { Status: MixtapeStatus.Connected } m)
        {
            if (id == "media.play") m.Toggle(); else if (id == "media.next") m.Next(); else m.Prev();
            _dirty = true;
            return;
        }
        OnAction?.Invoke(id);
    }

    // ---------------- opened ----------------

    void MixtapeDetail(RectangleF E, PanelData d)
    {
        var m = Mixtape;
        // the header: the name, then the tabs
        float tx = E.X + 24;
        if (Theme.Cyber) { CyberDetailTag(E, Upper("Mixtape")); tx += _p.MeasureWidth(Upper("Mixtape"), 18, FontWeight.Bold) + 44; }
        else
        {
            _p.Text("Mixtape", new RectangleF(tx, E.Y + 16, 160, 40), Bento.Text, 24, FontWeight.SemiBold);
            tx += _p.MeasureWidth("Mixtape", 24, FontWeight.SemiBold) + 18;
        }
        for (int i = 0; i < MtTabIcons.Length; i++)
        {
            var b = new RectangleF(tx + i * 52, E.Y + 14, 46, 46);
            if (Press(Ui.Id("wmtt", i), b, MtTabIcons[i], null, (int)_mtTab == i, m != null, 20))
            {
                if ((int)_mtTab == i) { _mtAlbum = null; _mtList = null; }        // its own tab again: back to its top
                _mtTab = (MtTab)i;
                _dirty = true;
            }
        }
        var c = new RectangleF(E.X + 24, E.Y + 74, E.Width - 48, E.Height - 92);
        if (m == null || m.Status != MixtapeStatus.Connected) { MtMessage(c, MtNotRunning, "Start Mixtape 0.23 or newer on the PC (Settings › Safety › Control from other apps: on)."); return; }
        switch (_mtTab)
        {
            case MtTab.Playing: MtPlaying(c, m); break;
            case MtTab.Queue: MtQueue(c, m); break;
            case MtTab.Songs: MtSongs(c, m); break;
            case MtTab.Albums: MtAlbums(c, m); break;
            case MtTab.Playlists: MtPlaylists(c, m); break;
            case MtTab.Lyrics: MtLyrics(c, m); break;
        }
    }

    void MtMessage(RectangleF c, string title, string note)
    {
        float cy = c.Y + c.Height / 2;
        if (Theme.Cyber)
        {
            TextAt(Ellipsize(Upper(title), c.Width, 24, FontWeight.Bold), c.X, cy - 6, c.Width, CyG, 24, FontWeight.Bold, TextAlignment.Center);
            if (note != null) CyMono(Ellipsize(Upper(note), c.Width, CyNote, FontWeight.SemiBold, CyberHud.Mono), c.X, cy + 24, c.Width, CyberHud.Faint, CyNote, FontWeight.SemiBold, TextAlignment.Center);
            return;
        }
        TextAt(Ellipsize(title, c.Width, 22, FontWeight.SemiBold), c.X, cy - 6, c.Width, Sub, 22, FontWeight.SemiBold, TextAlignment.Center);
        if (note != null) TextAt(Ellipsize(note, c.Width, SmallText, FontWeight.Normal), c.X, cy + 24, c.Width, Bento.Faint, SmallText, FontWeight.Normal, TextAlignment.Center);
    }

    // ---- Playing ----

    string _mtPosText = "", _mtDurText = "";
    int _mtPosSec = -1, _mtDurSec = -1;

    void MtPlaying(RectangleF c, MixtapeSource m)
    {
        var st = m.State;
        var t = st.Track;
        float s = MathF.Min(c.Height, MathF.Min(250, c.Width * 0.42f));
        var cover = new RectangleF(c.X, c.Y + (c.Height - s) / 2, s, s);
        MtCoverBox(cover, t?.Cover, 256, 20);
        if (Theme.Cyber) CyberHud.Brackets(_p, RectangleF.Inflate(cover, 5, 5), CyY, 14, 0, 2);
        float x = cover.Right + 28, w = c.Right - x, y = c.Y + 6;
        if (t == null) { MtMessage(new RectangleF(x, c.Y, w, c.Height * 0.6f), NotPlaying, null); }
        else
        {
            // the song over two lines, the artist, the album
            string title = Theme.Cyber ? Upper(t.Title) : t.Title;
            float ts = Theme.Cyber ? 26 : 25;
            var (l1, l2) = TwoLines(title, w, ts, Theme.Cyber ? FontWeight.Bold : FontWeight.SemiBold);
            TextAt(l1, x, y + 26, w + 2, Bento.Text, ts, Theme.Cyber ? FontWeight.Bold : FontWeight.SemiBold);
            if (l2.Length > 0) { y += 30; TextAt(l2, x, y + 26, w + 2, Bento.Text, ts, Theme.Cyber ? FontWeight.Bold : FontWeight.SemiBold); }
            y += 52;
            if (Theme.Cyber) CyMono(Ellipsize(Upper(t.Artist), w, CyNote + 2, FontWeight.SemiBold, CyberHud.Mono), x, y, w + 2, CyC, CyNote + 2);
            else TextAt(Ellipsize(t.Artist, w, 19, FontWeight.SemiBold), x, y, w + 2, Sub, 19, FontWeight.SemiBold);
            if (t.Album.Length > 0) TextAt(Ellipsize(t.Album, w, SmallText, FontWeight.Normal), x, y + 24, w + 2, Bento.Faint, SmallText, FontWeight.Normal);
        }
        // the time line: drag or tap it to go there; the time so far and the length under it
        float ly = c.Y + 150;
        int pos = st.PositionNow(Clock.Now), dur = t?.DurationMs ?? 0;
        var bar = new RectangleF(x, ly, w, 10);
        Slider("wmtpos", bar, dur > 0 ? pos / (float)dur : 0, Bento.Accent, t != null && dur > 0, v => m.Seek((int)(v * dur)));
        if (pos / 1000 != _mtPosSec) { _mtPosSec = pos / 1000; _mtPosText = MixtapeSong.Time(pos); }
        if (dur / 1000 != _mtDurSec) { _mtDurSec = dur / 1000; _mtDurText = MixtapeSong.Time(dur); }
        uint tc = Theme.Cyber ? CyG : Sub;
        TextAt(_mtPosText, x, ly + 34, 80, tc, 15, FontWeight.SemiBold);
        TextAt(_mtDurText, x + w - 80, ly + 34, 80, tc, 15, FontWeight.SemiBold, TextAlignment.Trailing);
        // shuffle, previous, play, next, repeat
        const float big = 60, sm = 48, tiny = 40, gap = 14;
        float total = tiny * 2 + sm * 2 + big + gap * 4, bx = x + MathF.Max(0, (w - total) / 2), by = c.Y + 196;
        float cy = by + big / 2;
        if (Press("wmtsh", new RectangleF(bx, cy - tiny / 2, tiny, tiny), "", null, st.Shuffle, true, 16, true)) m.SetShuffle(!st.Shuffle);
        bx += tiny + gap;
        if (Press("wmtpr", new RectangleF(bx, cy - sm / 2, sm, sm), "", null, false, t != null, 18, true)) m.Prev();
        bx += sm + gap;
        if (Press("wmtpl", new RectangleF(bx, by, big, big), st.Playing ? PauseFill : PlayFill, null, true, t != null, 24, true)) m.Toggle();
        bx += big + gap;
        if (Press("wmtnx", new RectangleF(bx, cy - sm / 2, sm, sm), "", null, false, t != null, 18, true)) m.Next();
        bx += sm + gap;
        string rep = st.Repeat;
        if (Press("wmtrp", new RectangleF(bx, cy - tiny / 2, tiny, tiny), rep == "one" ? "" : "", null, rep != "off", true, 16, true))
            m.SetRepeat(rep == "off" ? "all" : rep == "all" ? "one" : "off");
        // the volume: Mixtape's own
        float vy = c.Bottom - 30;
        _p.Icon("", new RectangleF(x, vy - 12, 28, 28), tc, 18);
        Slider("wmtvol", new RectangleF(x + 40, vy - 4, w - 40, 10), st.Volume, Bento.Accent, true, v => m.SetVolume(v));
    }

    // ---- the lists: rows that scroll (the stick, the bar on the right), only the visible ones drawn ----

    // a list's frame: its scroll clamped and moved by the stick, the bar dragged; returns the first row's y and the row width
    (float top, float width) MtListFrame(int slot, RectangleF L, int count, float rowH)
    {
        float content = count * rowH, max = MathF.Max(0, content - L.Height);
        ref float scroll = ref _mtScroll[slot];
        if (_ui.Wheel != 0 && !_ui.Inert && _ui.OverNow(L)) { scroll -= _ui.Wheel * rowH; _dirty = true; }
        if (max > 0)
        {
            // the bar: a 22 px wide strip to drag (a long song list in a few moves)
            var strip = new RectangleF(L.Right - 22, L.Y, 22, L.Height);
            _ui.Clickable(Ui.Id("wmtsb", slot), strip, out bool hover, out bool held);
            if (held) { scroll = Math.Clamp((_ui.Mouse.Y - L.Y) / L.Height, 0, 1) * max; _dirty = true; }
            scroll = Math.Clamp(scroll, 0, max);
            float bh = MathF.Max(28, L.Height * L.Height / content), bty = L.Y + (L.Height - bh) * (scroll / max);
            uint bc = Theme.Cyber ? (held || hover ? CyC : CyberHud.Track) : Theme.WithAlpha(Bento.Text, held || hover ? 0.55f : 0.3f);
            _p.Fill(new RectangleF(L.Right - 5, bty, 5, bh), bc, Theme.Cyber ? 0 : 2.5f);
            return (L.Y - scroll, L.Width - 28);
        }
        scroll = 0;
        return (L.Y, L.Width);
    }

    // one row's hit (clipped to the list) and its hover / press shade; true on a tap
    bool MtRow(string id, RectangleF row, RectangleF L)
    {
        var hit = RectangleF.Intersect(row, L);
        if (hit.Height < 4) return false;
        bool tap = _ui.Clickable(id, hit, out bool hover, out bool held);
        if (hover || held)
        {
            if (Theme.Cyber) { _p.Fill(row, held ? CyberHud.Card3 : CyberHud.Card2); _p.Fill(new RectangleF(row.X, row.Y, 3, row.Height), CyY); }
            else _p.Fill(row, held ? Bento.TileHover : Theme.WithAlpha(Bento.TileHover, 0.6f), 12);
        }
        return tap && !_ui.Inert;
    }

    // a song's row: the cover (when asked), the title, artist · album, the length; the one playing marked
    void MtSongRow(RectangleF row, MixtapeSong s, bool cover, bool playing, int number = 0)
    {
        float x = row.X + 10, right = row.Right - 10;
        if (cover)
        {
            MtCoverBox(new RectangleF(x, row.Y + (row.Height - 40) / 2, 40, 40), s.Cover, 64, 8);
            x += 52;
        }
        else if (number > 0)
        {
            TextAt(number.ToString(), x, row.Y + row.Height / 2 + 6, 30, Bento.Faint, 16, FontWeight.SemiBold);
            x += 34;
        }
        string dur = s.Duration;
        float dw = _p.MeasureWidth(dur, 15, FontWeight.SemiBold);
        TextAt(dur, right - dw, row.Y + row.Height / 2 + 6, dw + 2, Theme.Cyber ? CyG : Sub, 15, FontWeight.SemiBold);
        float tw = right - dw - 14 - x;
        uint tcol = playing ? (Theme.Cyber ? CyY : Bento.Accent) : Bento.Text;
        if (playing) { _p.Icon("", new RectangleF(x, row.Y, 18, row.Height), tcol, 14); x += 22; tw -= 22; }
        string sub = s.Album.Length > 0 && row.Height >= 48 ? s.Artist + " · " + s.Album : s.Artist;
        TextAt(Ellipsize(s.Title, tw, 18, FontWeight.SemiBold), x, row.Y + row.Height / 2 - 2, tw + 2, tcol, 18, FontWeight.SemiBold);
        TextAt(Ellipsize(sub, tw, 15, FontWeight.Normal), x, row.Y + row.Height / 2 + 18, tw + 2, Theme.Cyber ? CyG : Sub, 15, FontWeight.Normal);
    }

    // a list's heading line: what it is (and how many) on the left, buttons on the right (Press labels)
    void MtHeading(RectangleF c, string text)
    {
        if (Theme.Cyber) CyMono(Ellipsize(Upper(text), c.Width * 0.5f, CyNote, FontWeight.SemiBold, CyberHud.Mono), c.X, c.Y + 22, c.Width * 0.5f, CyG);
        else TextAt(Ellipsize(text, c.Width * 0.5f, SmallText, FontWeight.SemiBold), c.X, c.Y + 22, c.Width * 0.5f, Sub, SmallText, FontWeight.SemiBold);
    }

    // ---- Up next ----

    void MtQueue(RectangleF c, MixtapeSource m)
    {
        var q = m.State.Queue;
        MtHeading(c, q.Length == 0 ? "Up next is empty" : Lang.F("Up next · {0}", q.Length));
        if (q.Length > 0 && Press("wmtqc", new RectangleF(c.Right - 110, c.Y, 110, 34), null, "Clear", false, true)) m.QueueClear();
        if (q.Length == 0) return;
        var L = new RectangleF(c.X, c.Y + 42, c.Width, c.Height - 42);
        const float rh = 52;
        var (top, rw) = MtListFrame(0, L, q.Length, rh);
        _p.PushClip(L);
        for (int i = 0; i < q.Length; i++)
        {
            var row = new RectangleF(L.X, top + i * rh, rw, rh - 4);
            if (row.Bottom < L.Y || row.Y > L.Bottom) continue;
            // the cross on the right takes it out (before the row, so it wins)
            var x = new RectangleF(row.Right - 44, row.Y + (row.Height - 36) / 2, 36, 36);
            bool remove = RectangleF.Intersect(x, L).Height > 30 && Press(Ui.Id("wmtqx", i), x, "", null, false, true, 14, true);
            if (MtRow(Ui.Id("wmtq", i), new RectangleF(row.X, row.Y, row.Width - 50, row.Height), L)) m.QueueJump(i);
            MtSongRow(new RectangleF(row.X, row.Y, row.Width - 50, row.Height), q[i], true, false);
            if (remove) m.QueueRemove(i);
        }
        _p.PopClip();
    }

    // ---- Songs ----

    void MtSongs(RectangleF c, MixtapeSource m)
    {
        var lib = m.Library;
        if (lib == null) { MtMessage(c, "Loading the song list…", null); return; }
        MtHeading(c, Lang.F("{0} songs", lib.Songs.Length));
        if (lib.Songs.Length > 0 && Press("wmtsa", new RectangleF(c.Right - 160, c.Y, 160, 34), "", "Shuffle all", false, true, 16))
        {
            m.SetShuffle(true);
            m.PlaySongs(lib.Songs, Random.Shared.Next(lib.Songs.Length));
        }
        var L = new RectangleF(c.X, c.Y + 42, c.Width, c.Height - 42);
        MtSongList(2, L, lib.Songs, m, true);
    }

    // a song list (the Songs tab, an album, a playlist): a tap plays it and lines up the rest of the list after it
    void MtSongList(int slot, RectangleF L, MixtapeSong[] songs, MixtapeSource m, bool covers, bool numbers = false)
    {
        float rh = covers ? 54 : 46;
        var (top, rw) = MtListFrame(slot, L, songs.Length, rh);
        string playingId = m.State.Track?.Id;
        int first = Math.Max(0, (int)((L.Y - top) / rh)), last = Math.Min(songs.Length - 1, (int)((L.Bottom - top) / rh));
        _p.PushClip(L);
        for (int i = first; i <= last; i++)
        {
            var row = new RectangleF(L.X, top + i * rh, rw, rh - 4);
            if (MtRow(Ui.Id(Ui.Id("wmts", slot), i), row, L)) m.PlaySongs(songs, i);
            var s = songs[i];
            MtSongRow(row, s, covers, s.Id == playingId, numbers ? (s.Track > 0 ? s.Track : i + 1) : 0);
        }
        _p.PopClip();
    }

    // ---- Albums ----

    void MtAlbums(RectangleF c, MixtapeSource m)
    {
        var lib = m.Library;
        if (lib == null) { MtMessage(c, "Loading the song list…", null); return; }
        if (_mtAlbum != null) { MtAlbumPage(c, m, _mtAlbum.Title, _mtAlbum.Artist + (_mtAlbum.Year > 0 ? " · " + _mtAlbum.Year : ""), _mtAlbum.Cover, _mtAlbum.Songs, 5); return; }
        var albums = lib.Albums;
        MtHeading(c, Lang.F("{0} albums", albums.Length));
        var L = new RectangleF(c.X, c.Y + 42, c.Width, c.Height - 42);
        const int cols = 5;
        const float gap = 14;
        float tile = (L.Width - 28 - gap * (cols - 1)) / cols, rh = tile + 50;
        int rows = (albums.Length + cols - 1) / cols;
        var (top, _) = MtListFrame(3, L, rows, rh);
        _p.PushClip(L);
        for (int i = 0; i < albums.Length; i++)
        {
            float ry = top + i / cols * rh;
            if (ry + rh < L.Y || ry > L.Bottom) continue;
            var a = albums[i];
            var cell = new RectangleF(L.X + i % cols * (tile + gap), ry, tile, rh - 6);
            if (MtRow(Ui.Id("wmta", i), cell, L)) { _mtAlbum = a; _mtScroll[5] = 0; _dirty = true; }
            MtCoverBox(new RectangleF(cell.X, cell.Y, tile, tile), a.Cover, 128, 12);
            TextAt(Ellipsize(a.Title, tile, 15, FontWeight.SemiBold), cell.X, cell.Y + tile + 20, tile + 2, Bento.Text, 15, FontWeight.SemiBold);
            TextAt(Ellipsize(a.Artist, tile, 13, FontWeight.Normal), cell.X, cell.Y + tile + 38, tile + 2, Theme.Cyber ? CyG : Sub, 13, FontWeight.Normal);
        }
        _p.PopClip();
    }

    // an album / a playlist opened: back, its cover (a playlist: its icon), name and note, Play / Shuffle / Queue, its songs
    void MtAlbumPage(RectangleF c, MixtapeSource m, string name, string note, string cover, MixtapeSong[] songs, int slot)
    {
        if (Press("wmtbk", new RectangleF(c.X, c.Y, 40, 40), Theme.IcoLeft, null, false, true, 16, true)) { _mtAlbum = null; _mtList = null; _dirty = true; return; }
        float x = c.X + 52;
        const float cs = 92;
        if (slot == 5) MtCoverBox(new RectangleF(x, c.Y, cs, cs), cover, 128, 12);
        else { _p.Fill(new RectangleF(x, c.Y, cs, cs), Theme.Cyber ? CyberHud.Card2 : Bento.Tile, Theme.Cyber ? 0 : 12); _p.Icon("", new RectangleF(x, c.Y, cs, cs), Theme.Cyber ? CyC : Bento.Accent, 34); }
        float tx = x + cs + 18, tw = c.Right - tx;
        TextAt(Ellipsize(Theme.Cyber ? Upper(name) : name, tw, 22, FontWeight.SemiBold), tx, c.Y + 24, tw + 2, Bento.Text, 22, FontWeight.SemiBold);
        TextAt(Ellipsize(note + (note.Length > 0 ? " · " : "") + Lang.F("{0} songs", songs.Length), tw, 16, FontWeight.Normal), tx, c.Y + 48, tw + 2,
            Theme.Cyber ? CyG : Sub, 16, FontWeight.Normal);
        float by = c.Y + 58, bw = MathF.Min(120, (tw - 20) / 3);
        bool any = songs.Length > 0;
        if (Press(Ui.Id("wmtap", slot), new RectangleF(tx, by, bw, 34), PlayFill, "Play", true, any, 14)) m.PlaySongs(songs, 0);
        if (Press(Ui.Id("wmtas", slot), new RectangleF(tx + bw + 10, by, bw, 34), "", "Shuffle", false, any, 14))
        {
            m.SetShuffle(true);
            m.PlaySongs(songs, Random.Shared.Next(Math.Max(1, songs.Length)));
        }
        if (Press(Ui.Id("wmtaq", slot), new RectangleF(tx + 2 * (bw + 10), by, bw, 34), "", "Queue", false, any, 14)) m.QueueAdd(songs);
        var L = new RectangleF(c.X, c.Y + cs + 12, c.Width, c.Height - cs - 12);
        MtSongList(slot, L, songs, m, slot != 5, slot == 5);
    }

    // ---- Playlists ----

    void MtPlaylists(RectangleF c, MixtapeSource m)
    {
        var lib = m.Library;
        if (lib == null) { MtMessage(c, "Loading the song list…", null); return; }
        if (_mtList != null) { MtAlbumPage(c, m, _mtList.Name, "", null, _mtList.Songs, 6); return; }
        var lists = lib.Playlists;
        if (lists.Length == 0) { MtMessage(c, "No playlists", "Make one in Mixtape - it shows up here."); return; }
        MtHeading(c, Lang.F("{0} playlists", lists.Length));
        var L = new RectangleF(c.X, c.Y + 42, c.Width, c.Height - 42);
        const float rh = 56;
        var (top, rw) = MtListFrame(4, L, lists.Length, rh);
        _p.PushClip(L);
        for (int i = 0; i < lists.Length; i++)
        {
            var row = new RectangleF(L.X, top + i * rh, rw, rh - 4);
            if (row.Bottom < L.Y || row.Y > L.Bottom) continue;
            var p = lists[i];
            if (MtRow(Ui.Id("wmtp", i), row, L)) { _mtList = p; _mtScroll[6] = 0; _dirty = true; }
            var ic = new RectangleF(row.X + 10, row.Y + (row.Height - 40) / 2, 40, 40);
            _p.Fill(ic, Theme.Cyber ? CyberHud.Card2 : Bento.Tile, Theme.Cyber ? 0 : 8);
            _p.Icon("", ic, Theme.Cyber ? CyC : Bento.Accent, 18);
            float x = ic.Right + 12, tw = row.Right - 10 - x;
            TextAt(Ellipsize(p.Name, tw, 18, FontWeight.SemiBold), x, row.Y + row.Height / 2 - 2, tw + 2, Bento.Text, 18, FontWeight.SemiBold);
            TextAt(Lang.F("{0} songs", p.Songs.Length), x, row.Y + row.Height / 2 + 18, tw + 2, Theme.Cyber ? CyG : Sub, 15, FontWeight.Normal);
        }
        _p.PopClip();
    }

    // ---- Lyrics ----

    void MtLyrics(RectangleF c, MixtapeSource m)
    {
        var st = m.State;
        var ly = m.Lyrics;
        if (st.Track == null) { MtMessage(c, NotPlaying, null); return; }
        if (ly == null || ly.TrackId != st.Track.Id) { MtMessage(c, "Looking for the lyrics…", null); return; }
        if (ly.Lines.Length == 0) { MtMessage(c, "No lyrics for this song", "Mixtape shows them when the song has them (or after it found them)."); return; }
        const float lh = 40;
        int pos = st.PositionNow(Clock.Now), cur = ly.LineAt(pos);
        var L = new RectangleF(c.X, c.Y, c.Width, c.Height);
        float content = ly.Lines.Length * lh, max = MathF.Max(0, content - L.Height);
        double now = Clock.Now;
        if (_ui.Wheel != 0 && !_ui.Inert)
        {
            _mtLyricsY = (float.IsNaN(_mtLyricsY) ? 0 : _mtLyricsY) - _ui.Wheel * lh;
            _mtLyricsHandUntil = now + 4;
            _dirty = true;
        }
        if (ly.Synced && now >= _mtLyricsHandUntil && cur >= 0)
        {
            // the line sung stays at about 40 % of the height (eased there)
            float want = cur * lh - L.Height * 0.4f + lh / 2;
            _mtLyricsY = float.IsNaN(_mtLyricsY) ? want : _mtLyricsY + (want - _mtLyricsY) * 0.25f;
            if (MathF.Abs(want - _mtLyricsY) > 0.5f) _ui.Animating = true;
        }
        if (float.IsNaN(_mtLyricsY)) _mtLyricsY = 0;
        _mtLyricsY = Math.Clamp(_mtLyricsY, 0, max);
        _p.PushClip(L);
        int first = Math.Max(0, (int)(_mtLyricsY / lh)), last = Math.Min(ly.Lines.Length - 1, (int)((_mtLyricsY + L.Height) / lh));
        for (int i = first; i <= last; i++)
        {
            var row = new RectangleF(L.X, L.Y + i * lh - _mtLyricsY, L.Width, lh);
            bool sung = ly.Synced && i == cur, past = ly.Synced && i < cur;
            if (ly.Synced && MtRow(Ui.Id("wmtl", i), row, L)) { m.Seek(ly.Ms[i]); _mtLyricsHandUntil = 0; }
            string line = ly.Lines[i];
            float size = sung ? 23 : 19;
            uint col = !ly.Synced ? Bento.Text : sung ? (Theme.Cyber ? CyY : Bento.Text) : past ? Bento.Faint : (Theme.Cyber ? CyG : Sub);
            TextAt(Ellipsize(line, L.Width - 20, size, sung ? FontWeight.Bold : FontWeight.SemiBold), L.X + 10, row.Y + lh / 2 + size * 0.36f,
                L.Width - 20, col, size, sung ? FontWeight.Bold : FontWeight.SemiBold, TextAlignment.Center);
        }
        _p.PopClip();
    }

    // ================= the retro styles opened: the song, its buttons, the queue as a list =================

    void Mixtape95Detail(RectangleF c)
    {
        var m = Mixtape;
        var st = m?.State;
        if (m == null || m.Status != MixtapeStatus.Connected) { _ui.Text95(MtNotRunning, new RectangleF(c.X, c.Y + 40, c.Width, 13), Gry95, false, TextAlignment.Center); return; }
        var t = st.Track;
        _ui.Text95(Fit(t?.Title ?? NotPlaying, c.Width - 4, Ui.F95, true), new RectangleF(c.X + 2, c.Y + 2, c.Width - 4, 13), Blk95, true);
        if (t != null) _ui.Text95(Fit(t.Artist + (t.Album.Length > 0 ? " - " + t.Album : ""), c.Width - 4), new RectangleF(c.X + 2, c.Y + 17, c.Width - 4, 13), Gry95);
        float by = c.Y + 34;
        if (_ui.Button95("wmt95p", new RectangleF(c.X + 2, by, 30, 22), "|<", t != null)) m.Prev();
        if (_ui.Button95("wmt95t", new RectangleF(c.X + 36, by, 40, 22), st.Playing ? "||" : ">", t != null, true)) m.Toggle();
        if (_ui.Button95("wmt95n", new RectangleF(c.X + 80, by, 30, 22), ">|", t != null)) m.Next();
        if (_ui.Button95("wmt95s", new RectangleF(c.X + 118, by, 60, 22), "Shuffle", true, false, st.Shuffle)) m.SetShuffle(!st.Shuffle);
        var q = st.Queue;
        _ui.Text95(q.Length == 0 ? "Up next is empty" : Lang.F("Up next · {0}", q.Length), new RectangleF(c.X + 2, by + 30, c.Width - 4, 13), Blk95);
        if (q.Length == 0) return;
        var lr = new RectangleF(c.X + 2, by + 46, c.Width - 4, c.Bottom - by - 48);
        int hit = _ui.List95("wmt95q", lr, q.Length, -1, (i, rr, on) =>
            _ui.Text95(Fit(q[i].Title + " - " + q[i].Artist, rr.Width - 4), new RectangleF(rr.X + 2, rr.Y, rr.Width - 4, rr.Height), on ? Wht95 : Blk95));
        if (hit >= 0) m.QueueJump(hit);
    }
}
