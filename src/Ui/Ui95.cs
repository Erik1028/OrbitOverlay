using System;
using System.Collections.Generic;
using System.Drawing;
using Vortice.DirectWrite;

namespace Orbit;

// The Windows 95 controls, at their real 1995 metrics: 8 pt MS Sans Serif (11 px), 75 x 23 push buttons,
// 13 x 13 check boxes, 12 x 12 option buttons, etched group boxes, trackbars with a pointed thumb,
// block progress bars, property sheet tabs, list boxes with scroll bars, navy captions. Coordinates are
// pixels of the small 95 canvas (Painter.BeginPixel); the canvas is enlarged afterwards with whole pixels.
// What they do (hit areas, values, the open list) is here, the same in every retro style; how they look is the style's skin
// (Theme.RetroSkin: Win95Skin and the styles derived from it).
public sealed partial class Ui
{
    public const float F95 = 11;                       // 8 pt
    const uint Blk = 0xFF000000, Navy = 0xFF000080;    // the defaults of the colour arguments below

    static RetroSkin Sk => Theme.RetroSkin;

    // ---- text ----

    public void Text95(string s, RectangleF r, uint c = Blk, bool bold = false, TextAlignment al = TextAlignment.Leading,
        bool enabled = true, bool wrap = false, float size = F95) =>
        Sk.Text(P, s, r, c, bold, al, enabled, wrap, size);

    public float Width95(string s, bool bold = false, float size = F95) => Sk.TextWidth(P, s, bold, size);

    // ---- access keys: "&Size:" shows "Size:" with the S underlined (Alt+S in 1995); "&&" is a plain & ----

    static readonly Dictionary<string, (string text, int key)> _amp = new();

    public static (string text, int key) Amp(string s)
    {
        s = Lang.T(s);                  // the language picked first: the access key is its own
        if (string.IsNullOrEmpty(s) || s.IndexOf('&') < 0) return (s, -1);
        if (_amp.TryGetValue(s, out var v)) return v;
        var sb = new System.Text.StringBuilder(s.Length);
        int key = -1;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '&' && i + 1 < s.Length)
            {
                i++;
                if (s[i] != '&' && key < 0) key = sb.Length;
            }
            sb.Append(s[i]);
        }
        if (_amp.Count > 500) _amp.Clear();
        return _amp[s] = (sb.ToString(), key);
    }

    // a label's text before its access key and the key's letter (the skins measure them to underline it), made once per text
    static readonly Dictionary<string, (string pre, string ch)> _ampParts = new();

    public static (string pre, string ch) AmpParts(string t, int key)
    {
        if (_ampParts.TryGetValue(t, out var v) && v.pre.Length == key) return v;
        if (_ampParts.Count > 500) _ampParts.Clear();
        return _ampParts[t] = (t[..key], t.Substring(key, 1));
    }

    // data (a window title, a song) can hold a real '&': escape it before it goes into a label
    public static string Esc95(string s) => s?.Replace("&", "&&");

    public float LabelWidth95(string s, bool bold = false) => Width95(Amp(s).text, bold);

    public void Label95(string s, RectangleF r, bool enabled = true, TextAlignment al = TextAlignment.Leading, uint c = Blk,
        bool bold = false) =>
        Sk.Label(P, s, r, enabled, al, c, bold);

    // ---- push button ----

    // isDefault: the black outline of a dialog's default button; latched: a toggle that is in
    public bool Button95(string id, RectangleF r, string text, bool enabled = true, bool isDefault = false, bool latched = false,
        W95Icons.Id? icon = null)
    {
        bool click = Behave(id, r, out bool hover, out bool held) && enabled;
        Sk.PushButton(P, r, text, icon, enabled, isDefault, held && enabled, latched, hover);
        return click;
    }

    // a push button with a little pixel picture (the CD Player's |<<  >  >>|)
    public bool GlyphButton95(string id, RectangleF r, string[] glyph, bool enabled = true)
    {
        bool click = Behave(id, r, out bool hover, out bool held) && enabled;
        Sk.GlyphButton(P, r, glyph, enabled, held && enabled, hover);
        return click;
    }

    // ---- check box / option button ----

    public bool Check95(string id, float x, float y, string text, ref bool value, bool enabled = true)
    {
        var hit = new RectangleF(x, y - 2, 19 + LabelWidth95(text) + 4, 17);
        bool click = Behave(id, hit, out bool hover, out bool held) && enabled;
        if (click) { value = !value; Changed = true; }
        Sk.CheckBox(P, x, y, text, value, enabled, held, hover);
        return click;
    }

    public bool Radio95(string id, float x, float y, string text, bool on, bool enabled = true)
    {
        var hit = new RectangleF(x, y - 2, 18 + LabelWidth95(text) + 4, 16);
        bool click = Behave(id, hit, out bool hover, out bool held) && enabled;
        if (click && !on) Changed = true;
        Sk.Radio(P, x, y, text, on, enabled, held, hover);
        return click && enabled;
    }

    // a row of option buttons; returns true when the choice changed
    public bool Radios95(string id, float x, float y, string[] options, ref int index, bool enabled = true, float gap = 14)
    {
        bool changed = false;
        float cx = x;
        for (int i = 0; i < options.Length; i++)
        {
            if (Radio95(K(id, Seg[Math.Min(i, Seg.Length - 1)]), cx, y, options[i], index == i, enabled) && index != i)
            {
                index = i;
                changed = true;
            }
            cx += 18 + LabelWidth95(options[i]) + gap;
        }
        return changed;
    }

    // ---- group box, etched line ----

    public void Group95(RectangleF r, string title) => Sk.GroupBox(P, r, title);

    // ---- trackbar ----

    public bool Track95(string id, RectangleF r, ref float value, float min, float max, bool enabled = true, int ticks = 11)
    {
        Behave(id, r, out bool hover, out bool held);
        if (held) DragHeld = true;
        var ch = new RectangleF(r.X + 6, r.Y + 8, r.Width - 12, 4);
        bool changed = false;
        if (enabled && (held || (_active == id && Released)))
        {
            float t = Math.Clamp((Mouse.X - ch.X) / ch.Width, 0, 1);
            float nv = min + t * (max - min);
            if (Math.Abs(nv - value) > 1e-5f) { value = nv; changed = true; }
        }
        if (enabled && hover && Wheel != 0 && !held)
        {
            value = Math.Clamp(value + Math.Sign(Wheel) * (max - min) / 50f, min, max);
            changed = true;
        }
        if (changed) Changed = true;
        float t2 = Math.Clamp((value - min) / (max - min), 0, 1);
        Sk.Trackbar(P, r, t2, enabled, ticks, hover, held);
        return changed;
    }

    // ---- progress bar (blocks) ----

    public void Progress95(RectangleF r, float frac, uint block = Navy) => Sk.Progress(P, r, frac, block);

    // ---- property sheet tabs + page ----

    public bool Tabs95(string id, float x, float y, string[] tabs, ref int sel)
    {
        bool changed = false;
        float cx = x;
        var rects = new RectangleF[tabs.Length];
        var hot = new bool[tabs.Length];
        for (int i = 0; i < tabs.Length; i++)
        {
            float w = LabelWidth95(tabs[i]) + 14;
            rects[i] = new RectangleF(cx, y, w, 18);
            if (Behave(K(id, Seg[Math.Min(i, Seg.Length - 1)]), rects[i], out hot[i], out _) && sel != i) { sel = i; changed = true; }
            cx += w;
        }
        for (int i = 0; i < tabs.Length; i++)
            if (i != sel) Sk.Tab(P, rects[i], tabs[i], false, hot[i]);
        var s = rects[sel];
        Sk.Tab(P, new RectangleF(s.X - 2, s.Y - 2, s.Width + 4, s.Height + 4), tabs[sel], true, hot[sel]);
        return changed;
    }

    // ---- list box with a vertical scroll bar ----

    readonly Dictionary<string, int> _scroll95 = new();

    // a list's first row shown (the watch's opened notifications start from the top; --snapshot reads it)
    public int ListTop95(string id) => _scroll95.TryGetValue(id, out int top) ? top : 0;
    public void ListScrollTo95(string id, int top) => _scroll95[id] = Math.Max(0, top);

    // a row's part of its id ("/3"), made once: a "/" + i per row per frame was a list's garbage
    static readonly List<string> _rowParts = new();
    static string RowPart(int i)
    {
        while (_rowParts.Count <= i) _rowParts.Add("/" + _rowParts.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return _rowParts[i];
    }

    // returns the item clicked this frame (or -1); draw(index, row rect, selected) paints a row
    public int List95(string id, RectangleF r, int count, int selected, Action<int, RectangleF, bool> draw, float rowH = 16)
    {
        Sk.ListBox(P, r);
        var inner = RectangleF.Inflate(r, -2, -2);
        int visible = Math.Max(1, (int)(inner.Height / rowH));
        bool bar = count > visible;
        _scroll95.TryGetValue(id, out int top);
        top = Math.Clamp(top, 0, Math.Max(0, count - visible));
        float rowsW = inner.Width - (bar ? 16 : 0);
        int clicked = -1;
        if (bar && Hover(inner) && Wheel != 0) top = Math.Clamp(top - Math.Sign(Wheel) * 3, 0, count - visible);
        // where the rows are drawn from: the style may glide there (smooth scrolling); the hit areas are the rows' own
        float pos = bar ? Sk.ListScroll(P, r, top) : top;
        for (int i = 0; i < visible && top + i < count; i++)
        {
            int idx = top + i;
            var row = new RectangleF(inner.X, inner.Y + i * rowH, rowsW, rowH);
            if (Behave(K(id, RowPart(i)), row, out _, out _)) clicked = idx;
            if (pos != top) continue;
            if (idx == selected) Sk.ListSelection(P, row);
            draw(idx, row, idx == selected);
        }
        if (pos != top)
        {
            P.PushClip(new RectangleF(inner.X, inner.Y, rowsW, inner.Height));
            int first = Math.Max(0, (int)MathF.Floor(pos));
            for (int idx = first; idx < count && idx <= first + visible; idx++)
            {
                var row = new RectangleF(inner.X, inner.Y + MathF.Round((idx - pos) * rowH), rowsW, rowH);
                if (idx == selected) Sk.ListSelection(P, row);
                draw(idx, row, idx == selected);
            }
            P.PopClip();
        }
        if (bar)
        {
            var sb = new RectangleF(inner.Right - 16, inner.Y, 16, inner.Height);
            var up = new RectangleF(sb.X, sb.Y, 16, 16);
            var dn = new RectangleF(sb.X, sb.Bottom - 16, 16, 16);
            if (Behave(K(id, "#up"), up, out _, out bool upHeld)) top = Math.Max(0, top - 1);
            if (Behave(K(id, "#dn"), dn, out _, out bool dnHeld)) top = Math.Min(count - visible, top + 1);
            float track = sb.Height - 32;
            float th = Math.Max(8, track * visible / count);
            float ty = sb.Y + 16 + (track - th) * top / Math.Max(1, count - visible);
            var thumb = new RectangleF(sb.X, MathF.Round(ty), 16, MathF.Round(th));
            Sk.ScrollBar(P, sb, up, dn, thumb, upHeld, dnHeld);
        }
        _scroll95[id] = top;
        return clicked;
    }

    // ---- window parts ----

    public void Caption95(RectangleF r, string title, W95Icons.Id? icon = null, bool active = true) => Sk.Caption(P, r, title, icon, active);

    // the boxes in a caption (16 x 14): close, help, minimise, maximise
    // pad: a bigger, invisible target around the 16x14 button (a laser in VR)
    bool CaptionButton95(string id, float x, float y, CaptionKind kind, bool enabled, float pad = 0)
    {
        var r = new RectangleF(x, y, 16, 14);
        bool click = Behave(id, RectangleF.Inflate(r, pad, pad), out bool hover, out bool held) && enabled;
        Sk.CaptionButton(P, r, kind, enabled, held && enabled, hover);
        return click;
    }

    public bool CaptionClose95(string id, float x, float y, bool enabled = true, float pad = 0) => CaptionButton95(id, x, y, CaptionKind.Close, enabled, pad);
    public bool CaptionHelp95(string id, float x, float y, bool enabled = true) => CaptionButton95(id, x, y, CaptionKind.Help, enabled);
    public bool CaptionMin95(string id, float x, float y, bool enabled = true) => CaptionButton95(id, x, y, CaptionKind.Min, enabled);
    public bool CaptionMax95(string id, float x, float y, bool enabled = true) => CaptionButton95(id, x, y, CaptionKind.Max, enabled);

    // ---- text box (read-only here: Orbit types with its own keyboard, not into its settings) ----

    public void Edit95(RectangleF r, string text, bool enabled = true) => Sk.EditBox(P, r, text, enabled);

    // ---- drop-down list: the field, the arrow button, and the list that opens under it ----

    string _drop;                     // the open one
    bool _dropSeen, _inPopup;         // drawn this frame (it closes when its tab goes away); drawing the list
    RectangleF _dropField, _dropList;
    string[] _dropItems;
    int _dropHot, _dropPick = -1;
    public SizeF Canvas95 = new(100000, 100000);   // so a list near the bottom opens upwards

    void BeginPopups95()
    {
        if (!Theme.Classic) { _drop = null; _dropPick = -1; _dropSeen = false; return; }   // Modern never has one
        _dropSeen = false;
        if (_drop != null && Pressed && !_dropList.Contains(Mouse.X, Mouse.Y) && !_dropField.Contains(Mouse.X, Mouse.Y))
        {
            _drop = null;             // a click elsewhere only closes the list, like 1995
            _dropPick = -1;
            Pressed = false;
        }
    }

    public bool Combo95(string id, RectangleF r, string[] items, ref int index, bool enabled = true)
    {
        bool changed = false;
        if (_drop == id)
        {
            if (!enabled) _drop = null;
            else
            {
                _dropSeen = true;
                _dropField = r;
                if (_dropPick >= 0)
                {
                    Sk.MenuPicked(this, _dropList, _dropItems, _dropPick);         // a style's closing menu (the Mac's blink)
                    if (_dropPick != index && _dropPick < items.Length) { index = _dropPick; changed = Changed = true; }
                    _drop = null;
                    _dropPick = -1;
                }
            }
        }
        bool click = Behave(id, r, out bool hover, out bool held) && enabled;
        if (click)
        {
            if (_drop == id) _drop = null;
            else
            {
                _drop = id; _dropSeen = true; _dropField = r; _dropItems = items; _dropHot = index; _dropPick = -1;
                float h = items.Length * Sk.DropRowH + 2;
                float y = r.Bottom + h <= Canvas95.Height ? r.Bottom : r.Y - h;
                _dropList = new RectangleF(r.X, y, r.Width, h);
            }
        }
        bool open = _drop == id, down = held && enabled;
        string t = index >= 0 && index < items.Length ? items[index] : "";
        Sk.ComboBox(P, r, t, enabled, down, open, hover);
        return changed;
    }

    // the open list goes over everything: the page calls this after drawing itself
    public void Popups95()
    {
        if (_drop == null) { Sk.MenuClosing(this); return; }
        if (!_dropSeen) { _drop = null; return; }
        var r = _dropList;
        float rh = Sk.DropRowH;
        _inPopup = true;
        for (int i = 0; i < _dropItems.Length; i++)
        {
            var row = new RectangleF(r.X + 1, r.Y + 1 + i * rh, r.Width - 2, rh);
            if (!Hover(row)) continue;
            _dropHot = i;                                   // the bar follows the pointer
            if (Released) { _dropPick = i; Animating = true; }   // taken by the combo in the next frame
        }
        _inPopup = false;
        Sk.DropList(P, r, _dropItems, _dropHot, rh);
    }

    // status bar panes: thin sunken fields
    public void Pane95(RectangleF r, string text, uint dot = 0) => Sk.StatusPane(P, r, text, dot);

    // a menu bar's item, its access key underlined
    public void Menu95(float x, float y, string text) => Sk.MenuItem(P, x, y, text);
}
