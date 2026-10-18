using System;
using System.Numerics;
using Valve.VR;
using Vortice.Direct3D11;

namespace Orbit;

// Wrapper around one SteamVR overlay handle. Setters skip IPC when nothing changed.
// Every call that reaches SteamVR is counted (Calls), and so is every one a setter spared: a perf tool can say "N OpenVR calls a frame"
// (ORBIT_SNAP_ONLY=theaterperf, ToolsTheaterPerf.cs). Fake: no SteamVR at all - the overlays exist, every call is counted, none is sent
// (the tools: Erik may be in VR while they run, and a tool must never make an overlay he can see).
public sealed class VrOverlay : IDisposable
{
    public readonly ulong Handle;
    public readonly string Key;
    static CVROverlay Ov => OpenVR.Overlay;

    // the tools: overlays without SteamVR (set before any is made; never in the app)
    public static bool Fake;
    static ulong _fakeHandle = 0x7000;

    // What the overlays ask of SteamVR: the calls sent, by kind, and the ones the setters spared because nothing had changed (SetTexture
    // is never spared: the picture behind the same handle may be new - TextureSame counts the ones with the handle it already had).
    // Plain counters (an int each): the app pays nothing for them. Sizes: the textures' bytes are summed too (a GetDesc each) - the tools.
    public static class Calls
    {
        public const int Texture = 0, Transform = 1, Width = 2, Alpha = 3, Curvature = 4, Bounds = 5, Show = 6, Hide = 7, Color = 8, Sort = 9,
            MouseScale = 10, Aspect = 11, Flag = 12, Kinds = 13;
        public static readonly string[] Names = { "SetTexture", "SetTransform", "SetWidth", "SetAlpha", "SetCurvature", "SetTextureBounds", "Show", "Hide",
            "SetColor", "SetSortOrder", "SetMouseScale", "SetTexelAspect", "SetFlag" };
        public static readonly int[] Sent = new int[Kinds], Spared = new int[Kinds];
        public static int TextureSame;           // SetTexture with the handle the overlay already showed
        public static bool Sizes;                // sum the textures' bytes (TextureBytes)
        public static long TextureBytes;

        public static int SentTotal { get { int n = 0; foreach (int x in Sent) n += x; return n; } }
        public static int SparedTotal { get { int n = 0; foreach (int x in Spared) n += x; return n; } }

        public static void Reset()
        {
            Array.Clear(Sent);
            Array.Clear(Spared);
            TextureSame = 0;
            TextureBytes = 0;
        }
    }

    bool _visible;
    float _width = -1, _alpha = -1, _curv = -1, _aspect = -1;
    Matrix4x4 _lastM;
    uint _lastDevice = uint.MaxValue - 1;
    bool _hasTransform;
    double _nextWarn;
    IntPtr _lastTex;

    public bool Visible => _visible;
    public IntPtr TextureForTest => _lastTex;        // (the checks: the texture it was given last)
    public float Alpha => Math.Max(0, _alpha);       // as last set (SteamVR's default is 1; -1 here: never set)
    // (the checks and the headset-view renderer, CyberFloorTest.cs: what SteamVR was told last - sort order, place, part of the texture)
    public uint SortOrderForTest => _sort;
    public Matrix4x4 TransformForTest => _lastM;
    public (float u0, float v0, float u1, float v1) BoundsForTest => (_u0, _v0, _u1, _v1);

    // Night mode: every overlay Orbit shows in VR is multiplied by this (1 = normal brightness).
    // The SteamVR dashboard tab is left alone (it is not created through the public constructor).
    static readonly System.Collections.Generic.List<VrOverlay> _dimmed = new();
    static float _dim = 1;

    public static void SetDim(float d)
    {
        d = Math.Clamp(d, 0.05f, 1f);
        if (d == _dim) return;
        _dim = d;
        foreach (var o in _dimmed) o.PushColor();
    }

    public VrOverlay(string key, string name)
    {
        Key = key;
        ulong h = 0;
        if (Fake) h = ++_fakeHandle;
        else
        {
            var e = Ov.CreateOverlay(key, name, ref h);
            if (e != EVROverlayError.None) throw new InvalidOperationException($"CreateOverlay({key}) failed: {e}");
        }
        Handle = h;
        _dimmed.Add(this);
        PushColor();
    }

    // Dashboard overlays come as a pair; the thumbnail handle is exposed separately.
    VrOverlay(ulong handle, string key) { Handle = handle; Key = key; }

    public static (VrOverlay main, VrOverlay thumb) CreateDashboard(string key, string name)
    {
        ulong main = 0, thumb = 0;
        var e = Ov.CreateDashboardOverlay(key, name, ref main, ref thumb);
        if (e != EVROverlayError.None) throw new InvalidOperationException($"CreateDashboardOverlay failed: {e}");
        return (new VrOverlay(main, key), new VrOverlay(thumb, key + ".thumb"));
    }

    public void SetTexture(ID3D11Texture2D tex)
    {
        Calls.Sent[Calls.Texture]++;
        if (tex.NativePointer == _lastTex) Calls.TextureSame++;
        _lastTex = tex.NativePointer;
        if (Calls.Sizes)
        {
            var d = tex.Description;
            Calls.TextureBytes += (long)d.Width * d.Height * 4;        // (every texture Orbit hands over is 32-bit)
        }
        if (Fake) return;
        var t = new Texture_t { handle = tex.NativePointer, eType = ETextureType.DirectX, eColorSpace = EColorSpace.Auto };
        var e = Ov.SetOverlayTexture(Handle, ref t);
        if (e != EVROverlayError.None && Clock.Now >= _nextWarn)
        {
            _nextWarn = Clock.Now + 10;
            Log.Warn($"SetOverlayTexture({Key}): {e}");
        }
    }

    // ComputeOverlayIntersection takes the overlay's aspect from the mouse scale, so keep it = texture size.
    float _msW, _msH;
    public void SetMouseScale(float w, float h)
    {
        if (w == _msW && h == _msH) { Calls.Spared[Calls.MouseScale]++; return; }
        _msW = w; _msH = h;
        Calls.Sent[Calls.MouseScale]++;
        if (Fake) return;
        var v = new HmdVector2_t { v0 = w, v1 = h };
        Ov.SetOverlayMouseScale(Handle, ref v);
    }

    Vector3 _color = Vector3.One, _tint = Vector3.One;

    // multiplies the texture colour (grab / snap feedback); night mode dims on top of it
    public void SetColor(float r, float g, float b)
    {
        _tint = new Vector3(r, g, b);
        PushColor();
    }

    void PushColor()
    {
        var c = _dimmed.Contains(this) ? _tint * _dim : _tint;
        if (c == _color) { Calls.Spared[Calls.Color]++; return; }
        _color = c;
        Calls.Sent[Calls.Color]++;
        if (!Fake) Ov.SetOverlayColor(Handle, c.X, c.Y, c.Z);
    }

    public void SetWidth(float w)
    {
        if (w == _width) { Calls.Spared[Calls.Width]++; return; }
        _width = w;
        Calls.Sent[Calls.Width]++;
        if (!Fake) Ov.SetOverlayWidthInMeters(Handle, w);
    }
    public float Width => _width > 0 ? _width : 1f;            // as last set (SteamVR's default is 1 m)
    public float Curvature => Math.Max(0, _curv);
    public void SetAlpha(float a)
    {
        if (a == _alpha) { Calls.Spared[Calls.Alpha]++; return; }
        _alpha = a;
        Calls.Sent[Calls.Alpha]++;
        if (!Fake) Ov.SetOverlayAlpha(Handle, a);
    }
    public void SetCurvature(float c)
    {
        if (c == _curv) { Calls.Spared[Calls.Curvature]++; return; }
        _curv = c;
        Calls.Sent[Calls.Curvature]++;
        if (!Fake) Ov.SetOverlayCurvature(Handle, c);
    }
    public void SetTexelAspect(float a)
    {
        if (a == _aspect) { Calls.Spared[Calls.Aspect]++; return; }
        _aspect = a;
        Calls.Sent[Calls.Aspect]++;
        if (!Fake) Ov.SetOverlayTexelAspect(Handle, a);
    }
    uint _sort;
    public void SetSortOrder(uint o)
    {
        if (o == _sort) { Calls.Spared[Calls.Sort]++; return; }
        _sort = o;
        Calls.Sent[Calls.Sort]++;
        if (!Fake) Ov.SetOverlaySortOrder(Handle, o);
    }
    public void SetFlag(VROverlayFlags f, bool on)
    {
        Calls.Sent[Calls.Flag]++;
        if (!Fake) Ov.SetOverlayFlag(Handle, f, on);
    }

    // the part of the texture shown (uv, top-left origin; the whole of it by default) - Cyber's unfold (CyberScreenFx)
    float _u0, _v0, _u1 = 1, _v1 = 1;
    public void SetTextureBounds(float u0, float v0, float u1, float v1)
    {
        if (u0 == _u0 && v0 == _v0 && u1 == _u1 && v1 == _v1) { Calls.Spared[Calls.Bounds]++; return; }
        (_u0, _v0, _u1, _v1) = (u0, v0, u1, v1);
        Calls.Sent[Calls.Bounds]++;
        if (Fake) return;
        var b = new VRTextureBounds_t { uMin = u0, vMin = v0, uMax = u1, vMax = v1 };
        Ov.SetOverlayTextureBounds(Handle, ref b);
    }

    // put another overlay where this one is (the same absolute or device-relative transform; Cyber's screen frame)
    public void CopyTransformTo(VrOverlay o)
    {
        if (!_hasTransform) return;
        if (_lastDevice == uint.MaxValue) o.SetAbsolute(_lastM);
        else o.SetRelative(_lastDevice, _lastM);
    }

    public void Show()
    {
        if (_visible) { Calls.Spared[Calls.Show]++; return; }
        _visible = true;
        ShownAt = Clock.Now;
        Calls.Sent[Calls.Show]++;
        if (!Fake) Ov.ShowOverlay(Handle);
    }
    // when it last came into view (Clock.Now; a style's opening animation starts from it)
    public double ShownAt { get; private set; } = -10;
    public void Hide()
    {
        if (!_visible) { Calls.Spared[Calls.Hide]++; return; }
        _visible = false;
        Calls.Sent[Calls.Hide]++;
        if (!Fake) Ov.HideOverlay(Handle);
    }
    public void SetVisible(bool v) { if (v) Show(); else Hide(); }

    public void SetAbsolute(in Matrix4x4 world)
    {
        if (_hasTransform && _lastDevice == uint.MaxValue && world == _lastM) { Calls.Spared[Calls.Transform]++; return; }
        Calls.Sent[Calls.Transform]++;
        if (!Fake)
        {
            var m = MathUtil.ToHmd(world);
            Ov.SetOverlayTransformAbsolute(Handle, ETrackingUniverseOrigin.TrackingUniverseStanding, ref m);
        }
        _lastM = world; _lastDevice = uint.MaxValue; _hasTransform = true;
    }

    public void SetRelative(uint device, in Matrix4x4 rel)
    {
        if (_hasTransform && _lastDevice == device && rel == _lastM) { Calls.Spared[Calls.Transform]++; return; }
        Calls.Sent[Calls.Transform]++;
        if (!Fake)
        {
            var m = MathUtil.ToHmd(rel);
            Ov.SetOverlayTransformTrackedDeviceRelative(Handle, device, ref m);
        }
        _lastM = rel; _lastDevice = device; _hasTransform = true;
    }

    // Ray test in standing space. uv is returned with a TOP-left origin (OpenVR reports GL-style, bottom-left).
    public bool Raycast(Vector3 origin, Vector3 dir, out float dist, out Vector2 uv) =>
        Raycast(origin, dir, out dist, out uv, out _, out _);

    public bool Raycast(Vector3 origin, Vector3 dir, out float dist, out Vector2 uv, out Vector3 point, out Vector3 normal)
    {
        if (Fake) { dist = 0; uv = default; point = default; normal = default; return false; }
        var p = new VROverlayIntersectionParams_t
        {
            vSource = new HmdVector3_t { v0 = origin.X, v1 = origin.Y, v2 = origin.Z },
            vDirection = new HmdVector3_t { v0 = dir.X, v1 = dir.Y, v2 = dir.Z },
            eOrigin = ETrackingUniverseOrigin.TrackingUniverseStanding,
        };
        var r = new VROverlayIntersectionResults_t();
        bool hit = Ov.ComputeOverlayIntersection(Handle, ref p, ref r);
        dist = r.fDistance;
        uv = new Vector2(r.vUVs.v0, UvFlipV ? 1 - r.vUVs.v1 : r.vUVs.v1);
        point = new Vector3(r.vPoint.v0, r.vPoint.v1, r.vPoint.v2);
        normal = new Vector3(r.vNormal.v0, r.vNormal.v1, r.vNormal.v2);
        return hit && dist > 0;
    }

    // ---- the same test in-process (LocalRaycast), from what we last told SteamVR about this overlay ----
    public bool IsRelative => _hasTransform && _lastDevice != uint.MaxValue;

    public bool TryWorld(VrSession vr, out Matrix4x4 world)
    {
        world = _lastM;
        if (!_hasTransform) return false;
        if (_lastDevice == uint.MaxValue) return true;
        if (_lastDevice >= vr.DeviceValid.Length || !vr.DeviceValid[_lastDevice]) return false;
        world = _lastM * vr.Device[_lastDevice];
        return true;
    }

    public bool RaycastLocal(VrSession vr, Vector3 origin, Vector3 dir, out float dist, out Vector2 uv, out Vector3 point, out Vector3 normal)
    {
        dist = 0; uv = default; point = default; normal = default;
        if (_msW <= 0 || _msH <= 0 || !TryWorld(vr, out var world)) return false;
        float w = _width > 0 ? _width : 1f;          // SteamVR's default width
        float c = _curv > 0 ? _curv : 0f;
        return LocalRaycast.Intersect(world, w, c, _msW, _msH, origin, dir, out dist, out uv, out point, out normal);
    }

    // like RaycastLocal, but a ray past the edge lands on the nearest point of the edge (pointer capture)
    public bool ProjectLocal(VrSession vr, Vector3 origin, Vector3 dir, out float dist, out Vector2 uv, out Vector3 point, out Vector3 normal)
    {
        dist = 0; uv = default; point = default; normal = default;
        if (_msW <= 0 || _msH <= 0 || !TryWorld(vr, out var world)) return false;
        float w = _width > 0 ? _width : 1f;
        float c = _curv > 0 ? _curv : 0f;
        return LocalRaycast.Intersect(world, w, c, _msW, _msH, origin, dir, out dist, out uv, out point, out normal, clampToEdge: true);
    }

    // Verified by --selftest; kept switchable in case a runtime reports top-left UVs.
    public static bool UvFlipV = true;

    public void Dispose()
    {
        _dimmed.Remove(this);
        if (Fake) return;
        try { Ov?.DestroyOverlay(Handle); } catch { }
    }
}
