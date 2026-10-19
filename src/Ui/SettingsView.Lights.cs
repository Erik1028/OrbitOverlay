using System;
using System.Drawing;
using Vortice.DirectWrite;

namespace Orbit;

// The light widget's settings (Sys/RoomLights.cs): which bulb - its IP address, typed in the desktop window (the settings have no text box
// of their own: a small Windows dialog asks for it; in VR it is shown only) - and how: through Lumos, or straight to the bulb ("Go
// direct", Erik 10-17: Hexpad does it without Lumos; 10-19 the app goes straight by itself, OrbitApp.TapoAuto). Going direct takes the
// account Hexpad or Lumos already keeps - neither has one: the desktop window asks for it - and keeps only the bulb's key made from it,
// DPAPI-protected (TapoDirect.cs); "Use Lumos" forgets it (and is remembered). In the watch editor, when a light widget is selected:
// Modern / Cyber a row over its look, the 95 family a line in the Selected widget box. The RGB widget needs none (Prisma's pipe has one
// name).
public sealed partial class SettingsView
{
    // the desktop window asks; the answer is the setting (empty: none)
    void AskBulb()
    {
        AskText?.Invoke("Light bulb", "The bulb's IP address, as the Tapo app or Lumos shows it (e.g. 192.168.0.20).",
            C.Lights?.Bulb ?? "", text =>
            {
                if (text == null) return;
                text = text.Trim();
                if (text.Length == 0 && C.Lights?.Key == null) C.Lights = null;
                else (C.Lights ??= new LightsConfig()).Bulb = text.Length == 0 ? null : text;     // (the key stays)
                _h.Cfg.MarkDirty();
            });
    }

    string _bulbNote;                       // why "Go direct" did not (shown where the state is, until the next try)
    public Action<Action<string>> AskAccount;       // the desktop window asks for the Tapo account: the protected key (null: cancelled)

    // straight to the bulb with the key from Hexpad's / Lumos's account - neither has one: typed in the desktop window's dialog - or back
    // through Lumos (remembered: the app then does not go straight by itself, OrbitApp.TapoAuto)
    void ToggleDirect()
    {
        _bulbNote = null;
        if (C.Lights?.Key != null)
        {
            C.Lights.Key = null;
            C.Lights.ViaLumos = true;
            _h.Cfg.MarkDirty();
            return;
        }
        var kept = TapoAccount.Import(out _);
        if (kept != null) { Direct(kept); return; }
        if (AskAccount == null) { _bulbNote = Lang.T("No Tapo account: type it in the desktop window"); return; }
        AskAccount(k => { if (k != null) Direct(k); });
    }

    void Direct(string kept)
    {
        var l = C.Lights ??= new LightsConfig();
        l.Key = kept;
        l.ViaLumos = false;
        _h.Cfg.MarkDirty();
    }

    bool DirectOn => C.Lights?.Key != null;

    // the setting as its button / box shows it
    string BulbShown => C.Lights?.Bulb is { Length: > 0 } b ? b : Lang.T("(not set)");

    // how Lumos answers about it (the state the watch has)
    string BulbFound()
    {
        if (_bulbNote != null) return _bulbNote;
        if (C.Lights?.Bulb is not { Length: > 0 }) return AskText != null ? "" : Lang.T("Type it in the desktop window");
        var s = _h.Data?.Lamp?.State;
        return (s?.Status ?? LinkStatus.Off) switch
        {
            LinkStatus.Ready => Lang.T(DirectOn ? "Direct" : "Lumos answers"),
            LinkStatus.NoApp => Lang.T("Lumos isn't running"),
            LinkStatus.Error => DirectOn && s.Message is { Length: > 0 } m ? Lang.T(m) : Lang.T("Bulb not reachable"),
            LinkStatus.Looking => Lang.T("Looking up..."),
            _ => "",
        };
    }

    // ---- Modern / Cyber: the editor's row for a selected light widget (x, y: its top-left; 64 px tall) ----

    void BulbRow(Ui ui, float x, float y, float w)
    {
        ui.Section(new RectangleF(x, y, w, 22), "Bulb");
        ui.P.Text(BulbFound(), new RectangleF(x + 60, y, w - 60, 22), _bulbNote != null ? Theme.Warn : Theme.Dim, 12, FontWeight.SemiBold, TextAlignment.Trailing);
        const float dw = 118;
        if (ui.Button("we.bulb", new RectangleF(x, y + 24, w - dw - 6, 34), BulbShown, "", false, AskText != null, 13)) AskBulb();
        if (ui.Button("we.bulbdirect", new RectangleF(x + w - dw, y + 24, dw, 34), DirectOn ? "Use Lumos" : "Go direct", null, false, true, 12.5f)) ToggleDirect();
    }

    // ---- the 95 family: a line in the editor's Selected widget box ----

    void Bulb95Row(Ui ui, float x, float y)
    {
        Lbl(ui, x, y, "&Bulb:");
        ui.Edit95(new RectangleF(x + 42, y, 130, RowH), _bulbNote ?? BulbShown);
        if (ui.Button95("we95.bulbdirect", new RectangleF(566, y - 1, 75, 23), DirectOn ? "&Use Lumos" : "&Go direct")) ToggleDirect();
        if (ui.Button95("we95.bulb", new RectangleF(647, y - 1, 75, 23), "C&hange...", AskText != null)) AskBulb();
    }
}
