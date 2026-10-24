using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectWrite;
using Vortice.DXGI;
using AlphaMode = Vortice.DCommon.AlphaMode;
using PixelFormat = Vortice.DCommon.PixelFormat;

namespace Orbit;

// One D3D11 device (on the adapter SteamVR renders with) + Direct2D/DirectWrite on top of it.
// Everything that draws runs on the main thread; the immediate context is not shared.
public sealed class Gpu : IDisposable
{
    public readonly ID3D11Device Device;
    public readonly ID3D11DeviceContext Ctx;
    public readonly ID2D1Factory1 D2DFactory;
    public readonly ID2D1Device D2DDevice;
    public readonly ID2D1DeviceContext D2D;
    public readonly IDWriteFactory DWrite;
    public readonly IDXGIFactory2 Dxgi;
    public readonly string AdapterName;
    public readonly long AdapterLuid;
    public readonly ulong DedicatedVram;

    public Gpu(int adapterIndex)
    {
        Dxgi = DXGI.CreateDXGIFactory1<IDXGIFactory2>();
        IDXGIAdapter1 adapter = null;
        if (adapterIndex >= 0 && Dxgi.EnumAdapters1((uint)adapterIndex, out var a).Success)
            adapter = a;
        // ORBIT_SNAP_WARP=1 (the tools while SteamVR / VRChat run): Windows' software renderer - the snapshot's pictures without the GPU
        if (adapter == null && Environment.GetEnvironmentVariable("ORBIT_SNAP_WARP") == "1")
            for (uint i = 0; Dxgi.EnumAdapters1(i, out var c).Success; i++)
            {
                if ((c.Description1.Flags & AdapterFlags.Software) != 0) { adapter = c; break; }
                c.Dispose();
            }
        if (adapter == null)
        {
            // first hardware adapter
            for (uint i = 0; Dxgi.EnumAdapters1(i, out var c).Success; i++)
            {
                if ((c.Description1.Flags & AdapterFlags.Software) == 0) { adapter = c; break; }
                c.Dispose();
            }
        }
        if (adapter == null) throw new InvalidOperationException("No GPU adapter found");

        var desc = adapter.Description1;
        AdapterName = desc.Description;
        AdapterLuid = desc.Luid;
        DedicatedVram = (ulong)desc.DedicatedVideoMemory;

        var levels = new[] { Vortice.Direct3D.FeatureLevel.Level_11_1, Vortice.Direct3D.FeatureLevel.Level_11_0 };
        D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport, levels, out Device, out Ctx).CheckError();
        adapter.Dispose();

        D2DFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>(Vortice.Direct2D1.FactoryType.SingleThreaded);
        using (var dxgiDevice = Device.QueryInterface<IDXGIDevice>())
            D2DDevice = D2DFactory.CreateDevice(dxgiDevice);
        D2D = D2DDevice.CreateDeviceContext(DeviceContextOptions.None);
        D2D.TextAntialiasMode = Vortice.Direct2D1.TextAntialiasMode.Grayscale;
        DWrite = Vortice.DirectWrite.DWrite.DWriteCreateFactory<IDWriteFactory>(Vortice.DirectWrite.FactoryType.Shared);
        Theme.ResolveFonts(DWrite);
        Log.Info($"GPU: {AdapterName}, VRAM {DedicatedVram / (1024 * 1024)} MB");
    }

    public ID2D1Bitmap1 CreateTargetBitmap(IDXGISurface surface, bool drawable = true, Format format = Format.B8G8R8A8_UNorm)
    {
        var props = new BitmapProperties1(new PixelFormat(format, AlphaMode.Premultiplied), 96, 96,
            drawable ? BitmapOptions.Target : BitmapOptions.Target | BitmapOptions.CannotDraw);
        return D2D.CreateBitmapFromDxgiSurface(surface, props);
    }

    public void Dispose()
    {
        D2D?.Dispose(); D2DDevice?.Dispose(); D2DFactory?.Dispose(); DWrite?.Dispose();
        Ctx?.ClearState(); Ctx?.Flush(); Ctx?.Dispose(); Device?.Dispose(); Dxgi?.Dispose();
    }
}

// A BGRA texture that Direct2D can draw into and SteamVR can show.
public sealed class Surface : IDisposable
{
    public readonly int W, H;
    public readonly ID3D11Texture2D Tex;
    public readonly ID2D1Bitmap1 Bitmap;

    // every surface alive and its bytes (made, not disposed yet): the perf log's mem: line; a release logs what this dropped by
    // (OrbitApp.GpuRelease.cs). Interlocked: a tool may make one off the main thread.
    public static int LiveCount;
    public static long LiveBytes;
    public long Bytes => (long)W * H * 4;            // (both formats: 32 bits a texel)
    bool _gone;                                      // (disposed: a second Dispose - SystemCursor, the bar's retired one - counts nothing)

    // RGBA is only for the dashboard thumbnail: the web dashboard reads it as RGBA whatever the format says.
    public Surface(Gpu gpu, int w, int h, bool rgba = false)
    {
        W = w; H = h;
        var fmt = rgba ? Format.R8G8B8A8_UNorm : Format.B8G8R8A8_UNorm;
        Tex = gpu.Device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)w, Height = (uint)h, MipLevels = 1, ArraySize = 1,
            Format = fmt, SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default, BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
        });
        using var surf = Tex.QueryInterface<IDXGISurface>();
        Bitmap = gpu.CreateTargetBitmap(surf, true, fmt);
        System.Threading.Interlocked.Increment(ref LiveCount);
        System.Threading.Interlocked.Add(ref LiveBytes, Bytes);
    }

    // Reads the texture back and writes a PNG (alpha un-premultiplied). Used by --snapshot.
    public static Action<string> Saved;          // --snapshot: a picture was written (ToolsLang.cs's text fit names its finds by it)

    public unsafe void SavePng(Gpu gpu, string path, bool premultiplied = true)
    {
        Saved?.Invoke(path);
        using var staging = gpu.Device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)W, Height = (uint)H, MipLevels = 1, ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm, SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging, CPUAccessFlags = CpuAccessFlags.Read,
        });
        gpu.Ctx.CopyResource(staging, Tex);
        var map = gpu.Ctx.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            using var bmp = new Bitmap(W, H, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var bd = bmp.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            for (int y = 0; y < H; y++)
            {
                byte* src = (byte*)map.DataPointer + y * map.RowPitch;
                byte* dst = (byte*)bd.Scan0 + y * bd.Stride;
                for (int x = 0; x < W; x++)
                {
                    byte b = src[0], g = src[1], r = src[2], al = src[3];
                    if (premultiplied && al > 0 && al < 255)
                    {
                        b = (byte)Math.Min(255, b * 255 / al);
                        g = (byte)Math.Min(255, g * 255 / al);
                        r = (byte)Math.Min(255, r * 255 / al);
                    }
                    dst[0] = b; dst[1] = g; dst[2] = r; dst[3] = al;
                    src += 4; dst += 4;
                }
            }
            bmp.UnlockBits(bd);
            bmp.Save(path, ImageFormat.Png);
        }
        finally { gpu.Ctx.Unmap(staging, 0); }
    }

    // The texture's pixels as they are (premultiplied BGRA: the picture over black) - --snapshot's checks, without a PNG
    public unsafe uint[] ReadPixels(Gpu gpu)
    {
        using var staging = gpu.Device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)W, Height = (uint)H, MipLevels = 1, ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm, SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging, CPUAccessFlags = CpuAccessFlags.Read,
        });
        gpu.Ctx.CopyResource(staging, Tex);
        var map = gpu.Ctx.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            var px = new uint[W * H];
            for (int y = 0; y < H; y++)
            {
                uint* src = (uint*)((byte*)map.DataPointer + y * map.RowPitch);
                for (int x = 0; x < W; x++) px[y * W + x] = src[x];
            }
            return px;
        }
        finally { gpu.Ctx.Unmap(staging, 0); }
    }

    public void Dispose()
    {
        if (!_gone)
        {
            _gone = true;
            System.Threading.Interlocked.Decrement(ref LiveCount);
            System.Threading.Interlocked.Add(ref LiveBytes, -Bytes);
        }
        Bitmap?.Dispose(); Tex?.Dispose();
    }
}
