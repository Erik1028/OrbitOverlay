using System;
using System.Collections.Generic;
using System.Text;

namespace Orbit;

// The custom face's widgets drawn once while the watch is away (Erik 10-24, the spike lines: battery 10.8 ms, recap 7, an opened widget
// 6 the first time each drew - the connect's warm-ups, OrbitApp.BootTick and WarmAgain, draw only the page in view). After the connect's
// steps, the watch hidden for a second at least, one item a frame (OrbitApp.Tick): each widget of the other pages, then an opened view of
// each kind on the pages (and in their folders) - a few ms each, off-screen into the watch's own surface, never handed to SteamVR: its one
// SetTexture is WristPanel.Update's, right after a real Draw of the whole surface, and the surface is left clear after each item. Again only
// when the layout, the style or the language changed (3 s after the last change), and for the kinds whose data came since (the first
// notification, a world, the friends...). Not the retro styles (their skins keep motion of their own) nor the fixed faces.
// It leaves no trace: drawn with a Ui of its own (the watch's springs, eased values, pressed control and hit areas are not touched) and a
// pointer that is nowhere (nothing hovered, pressed or scrolled: no button acts, no slider sends), the boot held off for the draw (a boot
// held for a look draws an empty face), and what a draw writes in the panel put back after it (WarmKeep): the opened widget (none), the
// scrolls, the notifications' and the photos' "seen", the rolling digits and word fades, Cyber's frame bookkeeping.
// Left as the draw made them, on purpose: the caches (texts, numbers, palettes, rows, ids, the painter's layouts, bitmaps - made from what
// a real draw reads); a cover first taken by a warm-up (a bitmap cannot be put back: the next media card still fades it in); Cyber's value
// flickers and note states - they act only while the watch shows (Look > Motion Full), and the next look after more than 0.4 s away
// settles them before they could (Update: IntroHold / CyberBoot -> CyberSettleFx; if that ever changes, a value could flicker once).
// Views that load, send or keep a state of their own are not opened: the photos (a thumbnail asked for), the mixer and Mixtape's player
// (their wishes), the dock (its input), Minesweeper, a folder, the avatar; Minesweeper's, the mixer's and the avatar's cards are not drawn.
// Read only, and so accepted: the media and volume views ask for the volume (its reader wakes for 1.5 s), a media card on Mixtape's song
// asks the link for its cover (kept there, asked once - the card in view asks the same).
public sealed partial class WristPanel
{
    // an item: a widget's card (View null) or the opened view of that kind, with the list and the grid it is on
    readonly record struct WarmItem(List<WatchWidget> List, GridSpec Grid, WatchWidget W, string View);

    readonly List<WarmItem> _warmItems = new();
    readonly HashSet<string> _warmKinds = new(), _warmSkipped = new();
    int _warmSig, _warmNext = -1;                  // the next item (-1: to be listed, int.MaxValue: done)
    double _warmSigAt = -1;                        // when the layout's signature last changed (-1: never looked)
    int _warmData, _warmDrawn, _warmOnly = -1;     // the data seen (bits, only ever added), the data drawn with, the kinds listed (-1: all)
    bool _warmOff;                                 // it failed outside a draw: not again this run
    long _warmTicks, _warmMax;
    string _warmMaxWhat;
    int _warmCards, _warmViews, _warmFrames;
    Ui _warmUi;
    readonly PointerState _warmPtr = new();        // nowhere: not inside, nothing pressed, no wheel
    readonly List<WatchWidget> _warmOne = new(1);
    // the animation state a draw moves, kept to be put back (made once)
    readonly List<(string key, Roll ro, string from, string to, double start)> _warmRolls = new();
    readonly List<KeyValuePair<int, (int, int, int, int, double)>> _warmFades = new();
    readonly List<(string text, string who, DateTime at)> _warmNoteRows = new();
    int[] _warmIds, _warmIdsLast;

    // OrbitApp.Tick, once a frame after the connect's steps and the 8 s warm-up (never in a frame that ran a step): an item while the
    // watch is away
    public void WarmStep(PanelData d)
    {
        if (_warmOff || Ov == null || Ov.Visible || Alpha > 0.02f) return;
        double now = Clock.Now;
        if (now - _shownAt < 1) return;                 // away a second at least: not a blink, not a glance off its edge
        if (!C.Enabled || C.Face != "custom" || Theme.Classic || _open != null || _closing || _grabbing || EditFolder != null) return;
        try
        {
            _warmData |= WarmData(d);
            int sig = WarmSignature();
            if (sig != _warmSig || _warmSigAt < 0)
            {
                // a new layout / style / language: all of it again, once it has stayed so for 3 s (a slider or an editor drag moves it
                // every frame)
                (_warmSig, _warmSigAt, _warmNext, _warmOnly) = (sig, now, -1, -1);
                return;
            }
            if (now - _warmSigAt < 3) return;
            if (_warmNext == int.MaxValue)
            {
                int fresh = _warmData & ~_warmDrawn;    // data came since (the first notification, a world): only the kinds that show it
                if (fresh == 0) return;
                (_warmNext, _warmOnly) = (-1, fresh);
            }
            if (_warmNext < 0) { WarmBuild(); _warmNext = 0; }
            if (_warmNext >= _warmItems.Count) { WarmDone(); return; }
            WarmDraw(_warmItems[_warmNext++], d);
        }
        catch (Exception e)
        {
            _warmOff = true;
            Log.Warn($"watch warm-up: {e.Message} - off until the next start");
        }
    }

    // the items, in the order they are drawn: the cards page by page, then the views
    void WarmBuild()
    {
        _warmItems.Clear();
        _warmKinds.Clear();
        _warmSkipped.Clear();
        _warmCards = _warmViews = _warmFrames = 0;
        _warmTicks = _warmMax = 0;
        _warmMaxWhat = null;
        _warmDrawn = _warmOnly == -1 ? _warmData : _warmDrawn | _warmOnly;
        int count = WatchPages.Count(C), cur = C.Page;          // (the config as it is: CurPage and PageList can write it)
        // the cards of the other pages (the page in view: drawn by the connect's warm-ups, and with every look) - and of the page in
        // view too when it is for data that came while the watch was away (its cards drew without it)
        for (int p = 0; p < count; p++)
        {
            var list = WarmList(p);
            if ((p == cur && _warmOnly == -1) || list == null) continue;
            var grid = WatchPages.Grid(C, p);
            foreach (var w in list)
            {
                var k = w == null ? null : WatchWidgets.Find(w.Type);
                if (k != null && k.Id is not ("minesweeper" or "mixer" or "avatar") && WarmWanted(k.Id)) _warmItems.Add(new WarmItem(list, grid, w, null));
            }
        }
        // an opened view of each kind, on the pages and in their folders (the first widget of the kind found)
        for (int p = 0; p < count; p++)
        {
            var list = WarmList(p);
            if (list == null) continue;
            var grid = WatchPages.Grid(C, p);
            foreach (var w in list)
            {
                if (w == null) continue;
                WarmView(list, grid, w);
                if (WatchFolders.Is(w) && w.Children != null)
                    foreach (var c in w.Children) if (c != null) WarmView(w.Children, GridSpec.Folder, c);
            }
        }
    }

    void WarmView(List<WatchWidget> list, GridSpec grid, WatchWidget w)
    {
        var k = WatchWidgets.Find(w.Type);
        string v = k == null || k.Button ? null : DetailOf(k.Id);
        if (v == null || !WarmWanted(k.Id)) return;
        if (v is "photo" or "memory" or "mixer" or "dock" or "mines" or "folder" or "avatar") _warmSkipped.Add(v);
        else if (_warmKinds.Add(v)) _warmItems.Add(new WarmItem(list, grid, w, v));
    }

    // a page's list as the config has it (null: none - page 1 never set up, a hand-edited page)
    List<WatchWidget> WarmList(int p) => p <= 0 ? C.Widgets : C.Pages[p - 1]?.Widgets;

    bool WarmWanted(string kind) => _warmOnly == -1 || (WarmBit(kind) & _warmOnly) != 0;

    static int WarmBit(string kind) => kind switch
    {
        "recap" => 1, "friends" => 2, "photo" => 4, "memory" => 8, "weather" => 16, "room" => 32, "notifications" => 64, _ => 0,
    };

    // the data the cards show once it has come (a bit is only ever added: its kinds are drawn with it once)
    static int WarmData(PanelData d)
    {
        int b = 0;
        if (d.Recap != null) b |= 1;
        if (d.Friends is { Online.Count: > 0 }) b |= 2;
        if (d.Photos is { Recent.Count: > 0 } ph && ph.Recent[0].Thumb != null) b |= 4;
        if (d.Memories is { Recent.Count: > 0 } me && me.Recent[0].Thumb != null) b |= 8;
        if (d.Weather is { Status: WxStatus.Ok }) b |= 16;
        if (d.Room is { InRoom: true }) b |= 32;
        if (d.Notes is { Items.Count: > 0 }) b |= 64;
        return b;
    }

    // the layout, the style and the language as one number (nothing allocated: ~50 widgets, a few µs a frame)
    int WarmSignature()
    {
        unchecked
        {
            int count = WatchPages.Count(C);
            int h = ((Theme.Version * 31 + Lang.Version) * 31 + (C.Background?.GetHashCode() ?? 0)) * 31 + count;
            for (int p = 0; p < count; p++)
            {
                string grid = p <= 0 ? C.Grid : C.Pages[p - 1]?.Grid;
                h = h * 31 + (grid?.GetHashCode() ?? 0);
                var list = WarmList(p);
                if (list == null) { h = h * 31 + 1; continue; }
                foreach (var w in list) h = WarmHash(h, w, 0);
            }
            return h;
        }
    }

    static int WarmHash(int h, WatchWidget w, int depth)
    {
        unchecked
        {
            if (w == null) return h * 31 + 2;
            h = ((((h * 31 + (w.Type?.GetHashCode() ?? 0)) * 31 + w.X) * 31 + w.Y) * 31 + w.W) * 31 + w.H;
            h = ((h * 31 + (w.Style?.GetHashCode() ?? 0)) * 31 + (w.Variant?.GetHashCode() ?? 0)) * 31 + (w.Name?.GetHashCode() ?? 0);
            h = (h * 31 + (int)(w.Color ?? 0)) * 31 + (int)(w.Color2 ?? 0);
            if (w.Items != null) foreach (var s in w.Items) h = h * 31 + (s?.GetHashCode() ?? 0);
            if (w.Children != null && depth < 2) foreach (var c in w.Children) h = WarmHash(h, c, depth + 1);
            return h;
        }
    }

    // one item drawn into the watch's surface, every trace of it put back
    void WarmDraw(in WarmItem it, PanelData d)
    {
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var keep = new WarmKeep(this);
        WarmKeepAnims();
        double wake = AnimWake.Take();                  // the asks before it are another panel's: given back after it
        var ui = _ui;
        _warmUi ??= new Ui(_p) { Hud = false };
        _warmUi.ResetAnimations();                      // every spring and eased value at its first call: the item drawn as it ends up
        _ui = _warmUi;
        _introStart = -10;                              // (a boot held for a look would draw an empty face)
        _cyBootAt = -100;
        bool begun = false;
        try
        {
            _rolling = false;
            _ui.Begin(_warmPtr, 1);
            _ui.Inert = true;                           // only a picture: nothing hovered or tapped, no hit areas noted
            _p.Begin(Surf.Bitmap, 0x00000000, TestScale);
            begun = true;
            _p.ClipText = false;                        // (DrawCustom's: every string on the face is cut to fit)
            Bento.Reset();
            if (it.View == null)
            {
                _dockKeys = DockShows(d);
                _warmOne.Clear();
                _warmOne.Add(it.W);
                // past its cascade (t 99), nothing popping in, no page around it
                DrawPage(_warmOne, it.Grid, 0, d, C.Background == "cards", Volume.Level >= 0 && Volume.Muted, Now, 99, false);
            }
            else
            {
                // opened and at rest (DrawDetail at e = 1: no zoom, no rise; Cyber's tag typed, its brackets gone); never Mixtape's player
                (_open, _openMt, _closing, _openStart, _folder, _calOffset) = (it.W, false, false, -10, null, 0);
                _grid = it.Grid;
                _big = false;
                _ui.SpringFrom(OpenKey, 1);
                DrawDetail(d, it.List);
            }
        }
        catch (Exception e)
        {
            Log.Warn($"watch warm-up ({it.View ?? it.W.Type}): {e.Message} - stopped");
            _warmNext = int.MaxValue;
            _warmDrawn = -1;                            // (nor the data's kinds again, until the layout changes)
            _warmUi = null;
        }
        finally
        {
            _ui.End();
            _ui = ui;
            keep.Put(this);
            WarmPutAnims();
            _p.ClipText = true;
            Bento.Reset();
            if (begun)
            {
                _p.End();                               // (D2D draws the batch here)
                _p.Begin(Surf.Bitmap, 0x00000000);      // and the surface is left clear: no lone card stays in it
                _p.End();
            }
            AnimWake.Take();
            if (!double.IsPositiveInfinity(wake)) AnimWake.At(wake);
        }
        long dt = System.Diagnostics.Stopwatch.GetTimestamp() - t0;
        Spikes.Piece(it.View == null ? "watch warm-up: a widget" : "watch warm-up: an opened view", t0);
        _warmTicks += dt;
        _warmFrames++;
        if (it.View == null) _warmCards++; else _warmViews++;
        if (dt > _warmMax) { _warmMax = dt; _warmMaxWhat = it.View ?? it.W.Type; }
    }

    // the lists a draw moves: the rolling digits (their values put back, the ones it added gone), the word clocks' fades, the
    // notification rows (their count is how a new one slides in), the Cyber cards of the frame
    void WarmKeepAnims()
    {
        _warmRolls.Clear();
        foreach (var kv in _rolls) _warmRolls.Add((kv.Key, kv.Value, kv.Value.From, kv.Value.To, kv.Value.Start));
        _warmFades.Clear();
        foreach (var kv in _wordFades) _warmFades.Add(kv);
        _warmNoteRows.Clear();
        _warmNoteRows.AddRange(_noteRows);
        _warmIds ??= new int[_cyIds.Length];
        _warmIdsLast ??= new int[_cyIdsLast.Length];
        Array.Copy(_cyIds, _warmIds, _cyIds.Length);
        Array.Copy(_cyIdsLast, _warmIdsLast, _cyIdsLast.Length);
    }

    void WarmPutAnims()
    {
        if (_rolls.Count != _warmRolls.Count)
        {
            _rolls.Clear();
            foreach (var r in _warmRolls) _rolls[r.key] = r.ro;
        }
        foreach (var r in _warmRolls) { r.ro.From = r.from; r.ro.To = r.to; r.ro.Start = r.start; }
        _wordFades.Clear();
        foreach (var kv in _warmFades) _wordFades[kv.Key] = kv.Value;
        _noteRows.Clear();
        _noteRows.AddRange(_warmNoteRows);
        Array.Copy(_warmIds, _cyIds, _cyIds.Length);
        Array.Copy(_warmIdsLast, _cyIdsLast, _cyIdsLast.Length);
    }

    // all drawn: one line, the warm-up's Ui let go
    void WarmDone()
    {
        _warmNext = int.MaxValue;
        _warmUi = null;
        if (_warmFrames == 0) { _warmItems.Clear(); return; }
        bool all = _warmOnly == -1;
        var sb = new StringBuilder(all ? "watch warm-up: " : "watch warm-up, their data came: ");
        sb.Append(_warmCards).Append(_warmCards == 1 ? " widget" : " widgets").Append(all ? " of the other pages and " : " and ")
          .Append(_warmViews).Append(_warmViews == 1 ? " opened view" : " opened views");
        if (_warmViews > 0)
        {
            sb.Append(" (");
            bool first = true;
            foreach (var it in _warmItems)
            {
                if (it.View == null) continue;
                if (!first) sb.Append(", ");
                sb.Append(it.View);
                first = false;
            }
            sb.Append(')');
        }
        sb.Append($" drawn while the watch was away - {WarmMs(_warmTicks):0.0} ms over {_warmFrames} frames, the slowest {WarmMs(_warmMax):0.0} ms ({_warmMaxWhat})");
        if (_warmSkipped.Count > 0) sb.Append("; not opened: ").Append(string.Join(", ", _warmSkipped)).Append(" (their views load, send or keep a state)");
        Log.Info(sb.ToString());
        _warmItems.Clear();                             // (the widgets and lists it held: let go)
    }

    static double WarmMs(long ticks) => ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

    // What a draw writes in the panel's own fields, put back after the item: the frame's scratch, the intro and the boot, the opened
    // widget, the views' scrolls, the notifications' "seen" (modern and Cyber's inbox: a new one still slides / blinks in at the next
    // look), Cyber's frame bookkeeping, both photo sets (a new photo still pops at the next look).
    readonly struct WarmKeep
    {
        readonly bool dirty, rolling, big, dockKeys, mtLead, closing, openMt, notesEmpty, notesSlide, cyNotesFirst, cyBusy, cyWarnNow, cyWarnPrev, cyClip;
        readonly GridSpec grid;
        readonly float coverFade, roomScroll, roomMax, notesScroll, notesMax, frScroll, frMax, rcScroll, rcMax, cyU;
        readonly double introStart, cyBootAt, openStart, cyInboxAt, cyNoteSeen, cyNoteInAt, cyDue, cyT, cyLoopsUntil, cyMinuteAt;
        readonly WatchWidget open, folder;
        readonly int calOffset, notesTop, notesVersion, cyNotesVersion, cyPage, cyScanId, cyMinute, cyCur, cyIdCount, cyIdCountLast, cyGraphId, cyGraphLast;
        readonly string notesFirst, notesCount, cyNoteNewestTitle, cyTapKey;
        readonly DateTime cyInboxNewest, cyNoteNewest;
        readonly long cyStamp;
        readonly uint cyRed;
        readonly PhotoKeep latest, memory;

        public WarmKeep(WristPanel w)
        {
            (dirty, rolling, grid, big, dockKeys, coverFade, mtLead) = (w._dirty, w._rolling, w._grid, w._big, w._dockKeys, w._coverFade, w._mtLead);
            (introStart, cyBootAt) = (w._introStart, w._cyBootAt);
            (open, closing, openStart, openMt, folder, calOffset) = (w._open, w._closing, w._openStart, w._openMt, w._folder, w._calOffset);
            (roomScroll, roomMax, notesScroll, notesMax, notesTop) = (w._roomScroll, w._roomMax, w._notesScroll, w._notesMax, w._notesTop);
            (frScroll, frMax, rcScroll, rcMax) = (w._frScroll, w._frMax, w._rcScroll, w._rcMax);
            (notesEmpty, notesFirst, notesVersion, notesSlide, notesCount) = (w._notesEmptyDrawn, w._notesFirst, w._notesVersion, w._notesSlide, w._notesCount);
            (cyNotesVersion, cyNotesFirst, cyInboxNewest, cyInboxAt) = (w._cyNotesVersion, w._cyNotesFirst, w._cyInboxNewest, w._cyInboxAt);
            (cyNoteSeen, cyNoteInAt, cyNoteNewest, cyNoteNewestTitle) = (w._cyNoteSeen, w._cyNoteInAt, w._cyNoteNewest, w._cyNoteNewestTitle);
            (cyStamp, cyBusy, cyDue, cyT, cyWarnNow, cyWarnPrev, cyRed) = (w._cyStamp, w._cyBusy, w._cyDue, w._cyT, w._cyWarnNow, w._cyWarnPrev, w._cyRed);
            (cyLoopsUntil, cyPage, cyScanId, cyMinute, cyMinuteAt, cyTapKey) = (w._cyLoopsUntil, w._cyPage, w._cyScanId, w._cyMinute, w._cyMinuteAt, w._cyTapKey);
            (cyCur, cyU, cyClip) = (w._cyCur, w._cyU, w._cyClip);
            (cyIdCount, cyIdCountLast, cyGraphId, cyGraphLast) = (w._cyIdCount, w._cyIdCountLast, w._cyGraphId, w._cyGraphLast);
            latest = new PhotoKeep(w._latest);
            memory = new PhotoKeep(w._memory);
        }

        public void Put(WristPanel w)
        {
            (w._dirty, w._rolling, w._grid, w._big, w._dockKeys, w._coverFade, w._mtLead) = (dirty, rolling, grid, big, dockKeys, coverFade, mtLead);
            (w._introStart, w._cyBootAt) = (introStart, cyBootAt);
            (w._open, w._closing, w._openStart, w._openMt, w._folder, w._calOffset) = (open, closing, openStart, openMt, folder, calOffset);
            (w._roomScroll, w._roomMax, w._notesScroll, w._notesMax, w._notesTop) = (roomScroll, roomMax, notesScroll, notesMax, notesTop);
            (w._frScroll, w._frMax, w._rcScroll, w._rcMax) = (frScroll, frMax, rcScroll, rcMax);
            (w._notesEmptyDrawn, w._notesFirst, w._notesVersion, w._notesSlide, w._notesCount) = (notesEmpty, notesFirst, notesVersion, notesSlide, notesCount);
            (w._cyNotesVersion, w._cyNotesFirst, w._cyInboxNewest, w._cyInboxAt) = (cyNotesVersion, cyNotesFirst, cyInboxNewest, cyInboxAt);
            (w._cyNoteSeen, w._cyNoteInAt, w._cyNoteNewest, w._cyNoteNewestTitle) = (cyNoteSeen, cyNoteInAt, cyNoteNewest, cyNoteNewestTitle);
            (w._cyStamp, w._cyBusy, w._cyDue, w._cyT, w._cyWarnNow, w._cyWarnPrev, w._cyRed) = (cyStamp, cyBusy, cyDue, cyT, cyWarnNow, cyWarnPrev, cyRed);
            (w._cyLoopsUntil, w._cyPage, w._cyScanId, w._cyMinute, w._cyMinuteAt, w._cyTapKey) = (cyLoopsUntil, cyPage, cyScanId, cyMinute, cyMinuteAt, cyTapKey);
            (w._cyCur, w._cyU, w._cyClip) = (cyCur, cyU, cyClip);
            (w._cyIdCount, w._cyIdCountLast, w._cyGraphId, w._cyGraphLast) = (cyIdCount, cyIdCountLast, cyGraphId, cyGraphLast);
            latest.Put(w._latest);
            memory.Put(w._memory);
        }
    }

    // a photo set's snapshot, frame stamp, newest seen, pop and opened photo (and --snapshot's hooks)
    readonly struct PhotoKeep
    {
        readonly PhotoWatcher.Snapshot _snap;
        readonly long _stamp;
        readonly string _newest, _path, _shown;
        readonly double _popAt, _scrollAt;
        readonly int _index;
        readonly float _acc;
        readonly bool _empty;

        public PhotoKeep(PhotoSet s) =>
            (_snap, _stamp, _newest, _popAt, _index, _path, _acc, _scrollAt, _shown, _empty) =
            (s.Snap, s.Stamp, s.NewestPath, s.PopAt, s.Index, s.Path, s.Acc, s.ScrollAt, s.WidgetShown, s.EmptyShown);

        public void Put(PhotoSet s) =>
            (s.Snap, s.Stamp, s.NewestPath, s.PopAt, s.Index, s.Path, s.Acc, s.ScrollAt, s.WidgetShown, s.EmptyShown) =
            (_snap, _stamp, _newest, _popAt, _index, _path, _acc, _scrollAt, _shown, _empty);
    }
}
