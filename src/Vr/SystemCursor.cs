using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Orbit;

// The current Windows mouse cursor (arrow, I-beam, hand, resize...) as a texture, so the laser can draw it straight onto a monitor
// panel instead of waiting for it to come back through the desktop capture (that round trip is ~30-60 ms).
// Each shape is drawn once into a texture of its own and kept (Erik 10-18, the lasers' spikes: every change of shape - arrow to I-beam
// to hand as the beam crosses a page - drew 256 x 256 twice through GDI and uploaded it, a millisecond or two on the main loop); a
// shape seen before is a texture swap. OrbitApp asks only while a laser points at a panel.
public sealed class SystemCursor : IDisposable
{
    public const int Size = 256;      // the largest Windows pointer at 100 % (pointer size 15); 64 clipped big ones
    const int MaxShapes = 16;         // (an app making a new cursor each time: the kept ones start over, the one shown stays)
    public Surface Surf { get; private set; }
    public Vector2 Hotspot { get; private set; } = new(0, 0);
    public int Version { get; private set; }
    public int Drawn { get; private set; }          // shapes drawn (the bench: a shape seen before draws nothing)
    readonly Gpu _g;
    readonly Surface _blank;
    readonly Dictionary<IntPtr, (Surface surf, Vector2 hot)> _shapes = new();
    IntPtr _last;
    double _nextCheck;

    [StructLayout(LayoutKind.Sequential)]
    struct CURSORINFO { public int cbSize, flags; public IntPtr hCursor; public Native.POINT pt; }
    [StructLayout(LayoutKind.Sequential)]
    struct ICONINFO { public bool fIcon; public int xHotspot, yHotspot; public IntPtr hbmMask, hbmColor; }
    [StructLayout(LayoutKind.Sequential)]
    struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public short bmPlanes, bmBitsPixel; public IntPtr bmBits; }
    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight; public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [DllImport("user32.dll")] static extern bool GetCursorInfo(ref CURSORINFO ci);
    [DllImport("user32.dll")] static extern bool GetIconInfo(IntPtr h, out ICONINFO ii);
    [DllImport("user32.dll")] static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr icon, int w, int h, int step, IntPtr brush, int flags);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bi, int usage, out IntPtr bits, IntPtr section, int offset);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern int GetObject(IntPtr h, int size, out BITMAP bm);

    public SystemCursor(Gpu g)
    {
        _g = g;
        Surf = _blank = new Surface(g, Size, Size);
    }

    // Cheap to call every frame; a shape not seen before is drawn once.
    public void Update(double now)
    {
        if (now < _nextCheck) return;
        _nextCheck = now + 0.03;
        var ci = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
        if (!GetCursorInfo(ref ci) || ci.hCursor == IntPtr.Zero || ci.hCursor == _last) return;
        Use(ci.hCursor);
    }

    // this cursor shows from now on (the bench calls it with the system's own cursors)
    public void Use(IntPtr cursor)
    {
        _last = cursor;
        try
        {
            if (!_shapes.TryGetValue(cursor, out var s))
            {
                if (_shapes.Count >= MaxShapes)
                {
                    foreach (var kept in _shapes.Values) if (kept.surf != Surf) kept.surf.Dispose();
                    var shown = Surf;
                    _shapes.Clear();
                    if (shown != _blank) _shapes[IntPtr.Zero] = (shown, Hotspot);      // (still on the overlay: freed with the rest later)
                }
                var surf = new Surface(_g, Size, Size);
                s = (surf, Render(cursor, surf));
                _shapes[cursor] = s;
                Drawn++;
            }
            Surf = s.surf;
            Hotspot = s.hot;
            Version++;
        }
        catch (Exception e) { Log.Warn("cursor: " + e.Message); }
    }

    // Drawn twice (on black and on white): the difference gives the alpha, which also handles the old monochrome cursors that GDI
    // draws with XOR masks. Only the cursor's own size is worked on (32 x 32 at the usual pointer size; the rest of the texture clear).
    Vector2 Render(IntPtr cursor, Surface into)
    {
        var hot = Vector2.Zero;
        int w = Size, h = Size;
        if (GetIconInfo(cursor, out var ii))
        {
            hot = new Vector2(ii.xHotspot, ii.yHotspot);
            if (ii.hbmColor != IntPtr.Zero && GetObject(ii.hbmColor, Marshal.SizeOf<BITMAP>(), out var bc) != 0) { w = bc.bmWidth; h = bc.bmHeight; }
            else if (ii.hbmMask != IntPtr.Zero && GetObject(ii.hbmMask, Marshal.SizeOf<BITMAP>(), out var bm) != 0) { w = bm.bmWidth; h = bm.bmHeight / 2; }    // (mask: AND over XOR)
            if (ii.hbmMask != IntPtr.Zero) DeleteObject(ii.hbmMask);
            if (ii.hbmColor != IntPtr.Zero) DeleteObject(ii.hbmColor);
        }
        w = Math.Clamp(w, 1, Size);
        h = Math.Clamp(h, 1, Size);
        Draw(cursor, 0x00000000, _black, w, h);
        Draw(cursor, 0x00FFFFFF, _white, w, h);
        var px = _px;
        Array.Clear(px);
        for (int y = 0; y < h; y++)
            for (int x = 0, i = y * Size; x < w; x++, i++)
            {
                uint b = _black[i], wh = _white[i];
                int a = 255 - Math.Clamp((int)((wh >> 8) & 0xFF) - (int)((b >> 8) & 0xFF), 0, 255);   // green channel
                // premultiplied colour = the image on black
                px[i] = ((uint)a << 24) | (b & 0x00FFFFFF);
            }
        _g.Ctx.UpdateSubresource(px, into.Tex, 0, Size * 4, 0, null);
        return hot;
    }

    // reused for every cursor shape (they were 3 x 16 KB of garbage per change)
    readonly uint[] _black = new uint[Size * Size], _white = new uint[Size * Size], _px = new uint[Size * Size];

    static unsafe void Draw(IntPtr cursor, uint background, uint[] r, int w, int h)
    {
        var bi = new BITMAPINFOHEADER { biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = Size, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
        IntPtr bmp = CreateDIBSection(dc, ref bi, 0, out IntPtr bits, IntPtr.Zero, 0);
        IntPtr old = SelectObject(dc, bmp);
        try
        {
            uint* p = (uint*)bits;
            int n = Size * h;
            for (int i = 0; i < n; i++) p[i] = background;
            DrawIconEx(dc, 0, 0, cursor, 0, 0, 0, IntPtr.Zero, 3 /* DI_NORMAL */);
            for (int i = 0; i < n; i++) r[i] = p[i];
        }
        finally
        {
            SelectObject(dc, old);
            DeleteObject(bmp);
            DeleteDC(dc);
        }
    }

    public void Dispose()
    {
        foreach (var s in _shapes.Values) if (s.surf != _blank) s.surf.Dispose();
        _shapes.Clear();
        _blank.Dispose();
    }
}
