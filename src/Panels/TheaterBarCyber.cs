using System;
using System.Drawing;
using Vortice.DirectWrite;

namespace Orbit;

// The bar under a screen in the Cyber style: a HUD strip - the olive frame with its cut corners (the cuts yellow); the screens as
// plates (the one playing a cyan block), Theater as the yellow plate, the settings on a dark well with a small SCREEN code, the
// keyboard plate lit cyan while it is shown, Exit edged red. The same places and ids as the modern bar (DrawModern), so the hits,
// the stick's steps and the tests are the same. Motion: it comes in as a strip opening from its middle - the frame first, the
// brackets riding its two ends, then what is on it (the fade's own frames; Reduced too).
public sealed partial class TheaterBar
{
    static readonly string CyExit = ((char)0xE711).ToString(), CyPlay = ((char)0xE8B2).ToString();

    void DrawCyber()
    {
        _ui.Begin(_ptr, 1);
        _p.Begin(_surf.Bitmap, 0x00000000);
        var all = new RectangleF(0, 0, _tw, _th);
        // opening: from the middle out
        float k = CyberFx.Any && _vis < 1 ? CyberFx.EaseOut(_vis) : 1;
        bool wiping = k < 0.999f;
        float ww = MathF.Max(40, _tw * k);
        var wipe = new RectangleF((_tw - ww) / 2, 0, ww, _th);
        if (wiping) _p.PushClip(wipe);
        CyberHud.ConsoleFrame(_p, all, Theme.OverlayBg, 14);
        float x = Pad, ch = _th - 2 * Pad;
        ITheaterScreen pick = null;
        bool go = false, exit = false;
        if (_theater)
        {
            for (int i = 0; i < _choices.Count; i++, x += ChipW + Gap)
            {
                var r = new RectangleF(x, Pad, ChipW, ch);
                var s = _choices[i];
                bool on = s == _current;
                string id = Ui.Id("thb", i);
                if (_ui.Clickable(id, r, out bool hover, out bool held) && !on) pick = s;
                float a = _ui.Anim(Ui.Id(id, "#a"), on ? 1 : 0, 14), hv = _ui.Anim(Ui.Id(id, "#h"), hover ? 1 : 0, 18), pr = _ui.Anim(Ui.Id(id, "#p"), held ? 1 : 0, 30);
                r = RectangleF.Inflate(r, -ChipW * 0.02f * pr, -ch * 0.04f * pr);
                Plate(r, a, hv, CyberHud.Cyan, CyberHud.CyanDim);
                uint fg = Theme.Mix(CyberHud.White, CyberHud.Ink, a), ic = Theme.Mix(CyberHud.Cyan, CyberHud.Ink, a);
                _p.Icon(s is WindowPanel ? Theme.IcoWindow : Theme.IcoMonitor, new RectangleF(r.X + 14, r.Y, 30, r.Height), ic, 19);
                string label = Ellipsize(Label(i), r.Width - 66, 17, FontWeight.SemiBold);
                var (t, ts) = _ui.HudFit(label, r.Width - 66, 17);
                _p.Text(t, new RectangleF(r.X + 50, r.Y, r.Width - 58, r.Height), fg, ts, FontWeight.SemiBold);
                CyberHud.MonoAt(_p, Ui.Id("SCR-0", i + 1), r.Right - 50, r.Y + 15, 44, Theme.Mix(CyberHud.Faint, CyberHud.Ink, a), 10, FontWeight.Bold);
            }
            x = MediaCyber(x, ch);          // what plays: the readout and the transport (TheaterBarMedia.cs; nothing without a session)
        }
        else
        {
            var r = new RectangleF(x, Pad, GoW, ch);
            go = _ui.Clickable("thb.go", r, out bool hover, out bool held);
            float hv = _ui.Anim("thb.go#h", hover ? 1 : 0, 18), pr = _ui.Anim("thb.go#p", held ? 1 : 0, 30);
            r = RectangleF.Inflate(r, -GoW * 0.02f * pr, -ch * 0.04f * pr);
            Plate(r, 1, hv, CyberHud.Yellow, CyberHud.Yellow);
            _p.Icon(CyPlay, new RectangleF(r.X + 16, r.Y, 30, r.Height), CyberHud.Ink, 19);
            _p.Text("THEATER", new RectangleF(r.X + 54, r.Y, r.Width - 62, r.Height), CyberHud.Ink, 19, FontWeight.Bold);
            x += GoW + Gap;
        }
        // the settings, on a dark well
        var g = new RectangleF(x, Pad, SliderW + Gap + SliderW + Gap + CurveW, ch);
        _p.FillCut(g, CyberHud.Page, 0, 0, 10, 0);
        _p.StrokeCut(RectangleF.Inflate(g, -0.6f, -0.6f), CyberHud.Line, 1.25f, 0, 0, 9.7f, 0);
        CyberHud.Brackets(_p, g, CyberHud.Frame, 8, 3, 1.5f);
        bool changed = false;
        float dim = C.Dim;
        if (_ui.Slider("thb.dim", new RectangleF(g.X + 14, Pad + 2, SliderW - 14, ch - 4), "Dim", ref dim, 0, 0.9f, v => $"{v * 100:0} %"))
        { C.Dim = dim; changed = true; }
        float size = SizeNow;
        if (_ui.Slider("thb.size", new RectangleF(g.X + SliderW + Gap, Pad + 2, SliderW, ch - 4), "Size", ref size, Theater.MinWidth, Theater.MaxWidth, v => $"{v:0.0} m"))
        { C.Width = size; changed = true; }
        bool curved = C.Curved;
        if (_ui.Toggle("thb.curve", new RectangleF(g.X + 2 * (SliderW + Gap), Pad, CurveW - 10, ch), "Curved", ref curved))
        { C.Curved = curved; changed = true; }
        x = g.Right + Gap;
        bool kbOn = KbOn;
        var kr = new RectangleF(x, Pad, ch, ch);
        bool kb = _ui.Clickable("thb.kb", kr, out bool kh, out bool kheld);
        float ka = _ui.Anim("thb.kb#a", kbOn ? 1 : 0, 14), khv = _ui.Anim("thb.kb#h", kh ? 1 : 0, 18), kp = _ui.Anim("thb.kb#p", kheld ? 1 : 0, 30);
        var kb2 = RectangleF.Inflate(kr, -ch * 0.04f * kp, -ch * 0.04f * kp);
        Plate(kb2, ka, khv, CyberHud.Cyan, CyberHud.CyanDim);
        _p.Icon(Theme.IcoKeyboard, kb2, Theme.Mix(CyberHud.Cyan, CyberHud.Ink, ka), 20);
        x += ch + Gap;
        if (_theater)
        {
            var xr = new RectangleF(x, Pad, ch, ch);
            exit = _ui.Clickable("thb.x", xr, out bool xh, out bool xheld);
            float xa = _ui.Anim("thb.x#h", xh ? 1 : 0, 18), xp = _ui.Anim("thb.x#p", xheld ? 1 : 0, 30);
            var xb = RectangleF.Inflate(xr, -ch * 0.04f * xp, -ch * 0.04f * xp);
            _p.FillCut(xb, Theme.Mix(Theme.Mix(CyberHud.DangerFill, CyberHud.Card3, 0.3f * xa), CyberHud.Red, xp), 0, 0, 10, 0);
            _p.StrokeCut(RectangleF.Inflate(xb, -0.6f, -0.6f), Theme.Mix(CyberHud.DangerEdge, CyberHud.Red, xa), 1.25f, 0, 0, 9.7f, 0);
            _p.Icon(CyExit, xb, Theme.Mix(CyberHud.Red, CyberHud.Ink, xp), 18);
        }
        if (wiping)
        {
            _p.PopClip();
            // the brackets riding the strip's two ends
            CyberHud.Brackets(_p, new RectangleF(wipe.X - 2, 2, 20, _th - 4), CyberHud.Yellow, 12, 0, 3, topRight: false);
            CyberHud.Brackets(_p, new RectangleF(wipe.Right - 18, 2, 20, _th - 4), CyberHud.Yellow, 12, 0, 3, bottomLeft: false);
            _p.Fill(new RectangleF(wipe.X, _th / 2 - 1, 2, 2), CyberHud.Cyan);
            AnimWake.At(Clock.Now);
        }
        _p.End();
        MediaAfter();
        _ui.End();
        Act(pick, go, exit, changed, kb);
    }

    // a plate with its bottom-right corner cut: dark (a = 0) to 'on' (a = 1), its edge lighting with the pointer
    void Plate(RectangleF r, float a, float hv, uint on, uint edge)
    {
        uint bg = Theme.Mix(Theme.Mix(CyberHud.Card2, CyberHud.Card3, hv), on, a);
        _p.FillCut(r, bg, 0, 0, 10, 0);
        if (a < 0.99f) _p.StrokeCut(RectangleF.Inflate(r, -0.6f, -0.6f), Theme.WithAlpha(Theme.Mix(edge, CyberHud.Cyan, hv), 1 - a), 1.25f, 0, 0, 9.7f, 0);
        if (hv > 0.01f) CyberHud.Brackets(_p, r, Theme.WithAlpha(a > 0.5f ? CyberHud.White : CyberHud.Cyan, hv), 8, 4 - 5 * (1 - hv), 2);
    }
}
