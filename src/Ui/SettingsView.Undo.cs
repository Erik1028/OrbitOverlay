using System.Collections.Generic;
using System.Text.Json;

namespace Orbit;

// The watch editor's Undo / Redo (Erik 10-23): the face's layout - every page's widgets, their looks, the folders, the grids, the
// background - as it was before each change, 50 steps. A change is seen after the frame that made it (the layout written out differs
// from the one kept); a drag (a widget moved or resized, a slider) is one step, once it is let go. The page the watch shows, its place
// and size are not part of it - pages turned on the watch are not changes. Both editors (the modern / Cyber one and the 95 family's);
// in the desktop window Ctrl+Z / Ctrl+Y too. The steps stay while the settings live; showing the Wrist tab without the editor starts
// from what is there then (a change made elsewhere is not one of its steps).
public sealed partial class SettingsView
{
    const int UndoMax = 50;
    readonly List<string> _undo = new(), _redo = new();
    string _wnow;                          // the layout as last kept (null: look again first)

    sealed record WatchLayoutState(List<WatchWidget> Widgets, string Background, string Grid, string Name, List<WatchPage> Pages);

    static readonly JsonSerializerOptions UndoJson = new();

    static string LayoutText(WristConfig w) =>
        JsonSerializer.Serialize(new WatchLayoutState(w.Widgets, w.Background, w.Grid, w.Name, w.Pages), UndoJson);

    internal static string LayoutTextOf(WristConfig w) => LayoutText(w);      // --snapshot (UndoTest.cs)

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    // at the end of an editor frame: a change made in it is a step back
    void UndoTrack(Ui ui)
    {
        if (_wdrag != null || ui.DragHeld) return;          // being dragged: one step once it is let go
        string now = LayoutText(C.Wrist);
        if (_wnow == null) { _wnow = now; return; }
        if (now == _wnow) return;
        _undo.Add(_wnow);
        if (_undo.Count > UndoMax) _undo.RemoveAt(0);
        _redo.Clear();
        _wnow = now;
    }

    // the Wrist tab without the editor: what is there now is where the next steps start
    void UndoForget() => _wnow = null;

    public void Undo() => Step(_undo, _redo);
    public void Redo() => Step(_redo, _undo);

    void Step(List<string> from, List<string> to)
    {
        if (from.Count == 0) return;
        string now = LayoutText(C.Wrist);
        string back = from[^1];
        from.RemoveAt(from.Count - 1);
        to.Add(now);
        Restore(back);
        _wnow = back;
        _h.Cfg.MarkDirty();
    }

    // the layout put back (the WristConfig itself stays: the watch and the app hold it); the selection goes, the folder the editor works
    // in stays open when there is one at its place again
    void Restore(string text)
    {
        var s = JsonSerializer.Deserialize<WatchLayoutState>(text, UndoJson);
        var w = C.Wrist;
        int folderAt = _wfolder == null ? -1 : WatchLayout().IndexOf(_wfolder);
        w.Widgets = s.Widgets;
        w.Background = s.Background ?? "panel";
        w.Grid = s.Grid;
        w.Name = s.Name;
        w.Pages = s.Pages;
        _wselW = null;
        _wdrag = null;
        _wcell = (-1, -1);
        _wmsg = null;
        _wfolderAsk = null;
        _wremoveArmed = -10;
        _gridUndo = (-1, null, null);
        var page = WatchLayout();
        _wfolder = folderAt >= 0 && folderAt < page.Count && WatchFolders.Is(page[folderAt]) ? page[folderAt] : null;
    }
}
