using System;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace Orbit;

// One monitor (or one window, see ForWindow) captured with Windows.Graphics.Capture into our own BGRA texture.
// The frame pool is free-threaded and polled from the main loop, so all D3D work stays on one thread.
public sealed unsafe class MonitorCapture : IDisposable
{
    static readonly Guid IID_IGraphicsCaptureItemInterop = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    static readonly Guid IID_IGraphicsCaptureItem = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    static readonly Guid IID_IDirect3DDxgiInterfaceAccess = new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");
    static readonly Guid IID_ID3D11Texture2D = new("6F15AAF2-D208-4E89-9AB4-489535D34F9C");
    static readonly Guid IID_IDirect3D11CaptureFramePool = new("24EB6D22-1975-422E-82E7-780DBD8DDF24");
    static readonly Guid IID_IClosable = new("30D5A829-7FA4-4026-83BB-D75BAE4EA99E");

    // The frame pool's own COM interface, polled directly: TryGetNextFrame through the projection
    // creates managed wrappers for every frame (garbage the GC has to collect from the VR loop).
    IntPtr _poolAbi;
    ID3D11Texture2D _frameTex;      // one reused wrapper around each frame's texture

    readonly Gpu _g;
    readonly IDirect3DDevice _device;
    GraphicsCaptureItem _item;
    Direct3D11CaptureFramePool _pool;
    GraphicsCaptureSession _session;
    Windows.Graphics.SizeInt32 _size;
    public Surface Target { get; private set; }
    public bool Closed { get; private set; }
    public long Frames;
    bool _cursor = true;

    // while our own laser cursor is drawn on this monitor, leave the (late) captured one out
    public void SetCursorCapture(bool on)
    {
        if (on == _cursor || _session == null) return;
        _cursor = on;
        try { _session.IsCursorCaptureEnabled = on; } catch (Exception e) { Log.Warn("cursor capture: " + e.Message); }
    }

    public MonitorCapture(Gpu g, IDirect3DDevice device, MonitorInfo m) : this(g, device, CreateItem(m.Handle, forWindow: false)) { }

    public static MonitorCapture ForWindow(Gpu g, IDirect3DDevice device, IntPtr hwnd) =>
        new(g, device, CreateItem(hwnd, forWindow: true));

    MonitorCapture(Gpu g, IDirect3DDevice device, GraphicsCaptureItem item)
    {
        _g = g;
        _device = device;
        _item = item;
        _item.Closed += (_, _) => Closed = true;
        _size = _item.Size;
        // a minimised window can report 0 x 0; the first real frame resizes everything
        if (_size.Width < 1 || _size.Height < 1) _size = new Windows.Graphics.SizeInt32 { Width = 16, Height = 16 };
        try
        {
            Target = new Surface(g, _size.Width, _size.Height);
            _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_device, DirectXPixelFormat.B8G8R8A8UIntNormalized, Buffers, _size);
            _session = _pool.CreateCaptureSession(_item);
            _poolAbi = QueryAbi(_pool, IID_IDirect3D11CaptureFramePool);
            try { _session.IsCursorCaptureEnabled = true; } catch (Exception e) { Log.Warn("cursor capture: " + e.Message); }
            try { _session.IsBorderRequired = false; } catch (Exception e) { Log.Warn("capture border can't be disabled: " + e.Message); }
            _session.StartCapture();
        }
        catch
        {
            Dispose();        // the pool holds a raw reference and the target is VRAM: nothing may stay behind
            throw;
        }
    }

    static IntPtr QueryAbi(object o, Guid iid)
    {
        IntPtr unk = MarshalInspectable<object>.FromManaged(o);
        try
        {
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unk, ref iid, out IntPtr p));
            return p;
        }
        finally { Marshal.Release(unk); }
    }

    static void CloseFrame(IntPtr frame)
    {
        Guid iid = IID_IClosable;
        if (Marshal.QueryInterface(frame, ref iid, out IntPtr closable) == 0)
        {
            ((delegate* unmanaged[Stdcall]<IntPtr, int>)(*(IntPtr**)closable)[6])(closable);    // IClosable.Close
            Marshal.Release(closable);
        }
        Marshal.Release(frame);
    }

    public static IDirect3DDevice CreateWinRtDevice(Gpu g)
    {
        using var dxgi = g.Device.QueryInterface<IDXGIDevice>();
        Marshal.ThrowExceptionForHR(Native.CreateDirect3D11DeviceFromDXGIDevice(dxgi.NativePointer, out IntPtr p));
        try { return MarshalInterface<IDirect3DDevice>.FromAbi(p); }
        finally { Marshal.Release(p); }
    }

    static GraphicsCaptureItem CreateItem(IntPtr handle, bool forWindow)
    {
        const string cls = "Windows.Graphics.Capture.GraphicsCaptureItem";
        Marshal.ThrowExceptionForHR(Native.WindowsCreateString(cls, cls.Length, out IntPtr hs));
        try
        {
            Guid iid = IID_IGraphicsCaptureItemInterop;
            Marshal.ThrowExceptionForHR(Native.RoGetActivationFactory(hs, ref iid, out IntPtr factory));
            try
            {
                // IGraphicsCaptureItemInterop: [3] CreateForWindow, [4] CreateForMonitor
                var vtbl = *(IntPtr**)factory;
                var create = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, int>)vtbl[forWindow ? 3 : 4];
                Guid itemIid = IID_IGraphicsCaptureItem;
                IntPtr result;
                Marshal.ThrowExceptionForHR(create(factory, handle, &itemIid, &result));
                try { return GraphicsCaptureItem.FromAbi(result); }
                finally { Marshal.Release(result); }
            }
            finally { Marshal.Release(factory); }
        }
        finally { Native.WindowsDeleteString(hs); }
    }

    static IntPtr TexturePointer(IntPtr surface)
    {
        IntPtr unk = surface;
        {
            Guid accIid = IID_IDirect3DDxgiInterfaceAccess;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unk, ref accIid, out IntPtr access));
            try
            {
                var vtbl = *(IntPtr**)access;
                var get = (delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)vtbl[3];
                Guid texIid = IID_ID3D11Texture2D;
                IntPtr tex;
                Marshal.ThrowExceptionForHR(get(access, &texIid, &tex));
                return tex;
            }
            finally { Marshal.Release(access); }
        }
    }

    // Copies the newest frame (if any) into Target. Returns true when Target changed.
    // IDirect3D11CaptureFramePool: [7] TryGetNextFrame. IDirect3D11CaptureFrame: [6] Surface, [8] ContentSize.
    // The frame copied last is handed back to Windows only at the next poll (Erik 10-19, the spike line: 1.6-4.6 ms polls in VR, 0.18 ms
    // on the bench - closing a frame right after its copy was queued waited for that copy while the GPU was full of the game's frame);
    // one more buffer in the pool so Windows has a free one meanwhile
    const int Buffers = 3;
    IntPtr _held;

    void ReleaseHeld()
    {
        if (_held == IntPtr.Zero) return;
        CloseFrame(_held);
        _held = IntPtr.Zero;
    }

    public bool Poll()
    {
        if (Closed || _pool == null || _poolAbi == IntPtr.Zero) return false;
        IntPtr last = IntPtr.Zero, surface = IntPtr.Zero;
        try
        {
            long p0 = System.Diagnostics.Stopwatch.GetTimestamp();
            var tryNext = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)(*(IntPtr**)_poolAbi)[7];
            for (;;)
            {
                IntPtr f = IntPtr.Zero;
                int hr = tryNext(_poolAbi, &f);
                if (hr < 0 || f == IntPtr.Zero) break;
                if (last != IntPtr.Zero) CloseFrame(last);
                last = f;
            }
            Spikes.Piece("capture: next frame", p0);
            if (last == IntPtr.Zero) return false;

            var fv = *(IntPtr**)last;
            Windows.Graphics.SizeInt32 cs;
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<IntPtr, Windows.Graphics.SizeInt32*, int>)fv[8])(last, &cs));
            if (cs.Width < 1 || cs.Height < 1) return false;
            if (cs.Width != _size.Width || cs.Height != _size.Height)
            {
                // monitor mode changed: rebuild pool + target at the new size, take the next frame
                _size = cs;
                ReleaseHeld();
                _pool.Recreate(_device, DirectXPixelFormat.B8G8R8A8UIntNormalized, Buffers, cs);
                Target.Dispose();
                Target = new Surface(_g, cs.Width, cs.Height);
                return false;
            }

            long c0 = System.Diagnostics.Stopwatch.GetTimestamp();
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)fv[6])(last, &surface));
            IntPtr texPtr = TexturePointer(surface);
            _frameTex ??= new ID3D11Texture2D(IntPtr.Zero);
            _frameTex.NativePointer = texPtr;
            try
            {
                int w = Math.Min(cs.Width, Target.W), h = Math.Min(cs.Height, Target.H);
                _g.Ctx.CopySubresourceRegion(Target.Tex, 0, 0, 0, 0, _frameTex, 0, new Box(0, 0, 0, w, h, 1));
            }
            finally
            {
                _frameTex.NativePointer = IntPtr.Zero;
                Marshal.Release(texPtr);
            }
            Spikes.Piece("capture: copy", c0);
            // this frame kept until the next poll (its copy done by then); the one before handed back now
            long k0 = System.Diagnostics.Stopwatch.GetTimestamp();
            ReleaseHeld();
            _held = last;
            last = IntPtr.Zero;
            Spikes.Piece("capture: frame back to Windows", k0);
            Frames++;
            return true;
        }
        catch (Exception e)
        {
            Log.Warn("capture poll: " + e.Message);
            Closed = true;
            return false;
        }
        finally
        {
            if (surface != IntPtr.Zero) Marshal.Release(surface);
            if (last != IntPtr.Zero) CloseFrame(last);
        }
    }

    public void Dispose()
    {
        try { ReleaseHeld(); } catch { }
        if (_poolAbi != IntPtr.Zero) { Marshal.Release(_poolAbi); _poolAbi = IntPtr.Zero; }
        try { _session?.Dispose(); } catch { }
        try { _pool?.Dispose(); } catch { }
        _session = null; _pool = null; _item = null;
        Target?.Dispose();
        Target = null;
    }
}
