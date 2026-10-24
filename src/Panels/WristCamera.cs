using System.Drawing;

namespace Orbit;

// The camera button's looks (Erik 10-24: it should know whether VRChat's camera is open - Sys/VrcCamera.cs reads its mode from
// VRChat's OSCQuery, with the avatar's values):
//  - not known (nothing read yet: VRChat starting, OSC off): the button as it always was - a tap takes the photo
//  - open: the mode's name instead of "Camera" (Photo, Stream, Emoji ...) - a tap takes the photo
//  - closed (mode 0): greyed, each style its own "not now" - Bento's dim tile, Cyber's faint cuts, 95's etched caption and embossed
//    picture, XP's grey words and faded icon, 7's flat disabled face, the Mac's dimmed button, the terminal's dim key - and
//    "Open the camera" where a name fits. A tap sends nothing (VrcCamera.Shutter refuses; it never sends Mode, the one thing that
//    could open it does nothing while it is closed) and the button says where for a moment: "Open it in VRChat" (Cyber in its
//    warning red, the terminal its bright ink; the retro buttons only change their words, as their buttons did).
// The words are the table's (drawn through it), nothing made a frame.
public sealed partial class WristPanel
{
    public double CamHintFor = 2.5;          // seconds the hint stays after a tap while closed (--snapshot: held for its picture)
    double _camHintAt = -1;                  // the tap's time (Clock.Now); < 0: no hint
    int _camSeen = int.MinValue;             // the mode at the last Update (CameraChanged)
    int _camDrawn = VrcCamera.Unknown;       // the mode a camera button was drawn with last: a tap on it is a tap on what was seen

    enum CamLook { Normal, Open, Closed, Hint }

    static int CamMode(PanelData d) => d.Camera?.Mode ?? VrcCamera.Unknown;

    // --snapshot: how a camera button is drawn now (Normal, Open, Closed, Hint)
    public string CameraLook(PanelData d) => CamNow(d).ToString();

    CamLook CamNow(PanelData d)
    {
        int m = _camDrawn = CamMode(d);
        if (m == VrcCamera.Unknown) return CamLook.Normal;
        if (m != VrcCamera.Off) return CamLook.Open;
        return _camHintAt >= 0 && Clock.Now - _camHintAt < CamHintFor ? CamLook.Hint : CamLook.Closed;
    }

    static bool CamGrey(CamLook l) => l is CamLook.Closed or CamLook.Hint;

    // the hint where even "In VRChat" does not fit (a retro 1x1): where, in one word - the terminal's too
    const string CamWhere = "VRChat";

    // its words in the modern and the retro styles (Cyber puts them in capitals; the terminal has its own: CamWordsTerm); normal: the
    // kind's own
    static string CamLabel(CamLook look, int mode, bool wide, string normal) => look switch
    {
        CamLook.Open => VrcCamera.ModeName(mode),
        CamLook.Closed => wide ? "Open the camera" : "Closed",
        CamLook.Hint => wide ? "Open it in VRChat" : "In VRChat",
        _ => normal,
    };

    // the terminal's: a key's word under it (a 1x1) and beside it (wide)
    static readonly string[] CamModesTerm = { "CAMERA", "PHOTO", "STREAM", "EMOJI", "MULTILAYER", "PRINT", "DRONE" };
    static (string word, string wide) CamWordsTerm(CamLook look, int mode, string word, string wide) => look switch
    {
        CamLook.Open => (CamModesTerm[mode < CamModesTerm.Length ? mode : 0], CamModesTerm[mode < CamModesTerm.Length ? mode : 0]),
        CamLook.Closed => ("OFF", "CAM OFF"),
        CamLook.Hint => ("VRCHAT", "IN VRCHAT"),
        _ => (word, wide),
    };

    // every Update: the mode VRChat said changed, or the hint is over - the watch draws again (when a camera button is in view)
    bool CameraChanged(PanelData d)
    {
        int m = CamMode(d);
        bool over = _camHintAt >= 0 && (m != VrcCamera.Off || Clock.Now - _camHintAt >= CamHintFor);
        if (over) _camHintAt = -1;
        if (m == _camSeen && !over) return false;
        _camSeen = m;
        return C.Face == "custom" && HasWidget(VrcCamera.Id);
    }

    // a tap on a camera button (Act): drawn closed - the hint, and the tap still goes to the app (VrcCamera.Shutter sends nothing then,
    // and has VRChat asked again: it may have been opened since the last read)
    void CameraTapped()
    {
        if (_camDrawn != VrcCamera.Off) return;
        _camHintAt = Clock.Now;
        _dirty = true;
    }

    // ---- modern: a tile ----

    void CameraTile(int id, WatchWidget w, WatchWidgets.Kind k, RectangleF r, PanelData d)
    {
        var look = CamNow(d);
        bool wide = w.W >= 2;
        string label = CamLabel(look, CamMode(d), wide, TileName(k, wide));
        if (TileButton(Ui.Id("wcb", id), r, k.Icon, label, false, wide, false, look == CamLook.Hint ? 2 : look == CamLook.Closed ? 1 : 0)) Act(k.Id);
    }

    // ---- 95, XP, the Mac: the skin's push button; closed, its disabled one - still taken, for the hint ----

    void CameraButton95(int id, WatchWidget w, WatchWidgets.Kind k, RectangleF r, PanelData d, bool xp)
    {
        var look = CamNow(d);
        bool wide = w.W >= 2, grey = CamGrey(look);
        string name = CamLabel(look, CamMode(d), wide, k.Name);
        if (look == CamLook.Hint && !wide && _ui.Width95(name) > r.Width - 8) name = CamWhere;      // (a 1x1 too narrow for "In VRChat")
        W95Icons.Id? icon = k.Icon95;
        string text;
        if (xp) text = wide || _ui.Width95(name) <= r.Width - 8 ? name : look == CamLook.Normal && _ui.Width95(k.Title95) <= r.Width - 8 ? k.Title95 : null;
        else text = wide ? name : null;
        if (look == CamLook.Hint && text == null) { text = name; icon = null; }       // a 1x1's picture gives way to the words a moment
        // greyed words that would be cut beside the picture (its short form too): the words alone - they say more than it
        if (grey && !xp && icon != null && text != null && Fit(text, r.Width - 30).EndsWith("...", System.StringComparison.Ordinal)) icon = null;
        if (text != null) text = Ui.Esc95(Fit(text, r.Width - (icon != null && !xp ? 30 : 8)));
        bool click = _ui.Clickable(Ui.Id("wcb", id), r, out bool hover, out bool held);
        Theme.RetroSkin.PushButton(_p, r, text, icon, !grey, false, held && !grey, false, hover && !grey);
        if (click) Act(k.Id);
    }
}
