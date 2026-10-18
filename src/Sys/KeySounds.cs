using System;
using System.IO;
using System.Runtime.InteropServices;
using Vortice.Multimedia;
using Vortice.XAudio2;

namespace Orbit;

// Key sounds for the VR keyboard, synthesised at start (no sound files) and played through
// XAudio2 (low latency; quick presses can overlap).
//
// One plain click per key press, like the Virtual Desktop keyboard: a ~3 ms band-limited noise
// tick, no tones and no release sound. "muted" is the same tick with the highs rolled off.
// Four noise variants so fast typing doesn't sound mechanical.
public sealed class KeySounds : IDisposable
{
    const int Rate = 44100;
    sealed class Clip { public IntPtr Data; public int Bytes; }

    IXAudio2 _xa;
    IXAudio2MasteringVoice _master;
    readonly IXAudio2SourceVoice[] _voices = new IXAudio2SourceVoice[8];
    int _nextVoice, _nextVariant;
    Clip[] _down = Array.Empty<Clip>(), _up = Array.Empty<Clip>();
    Clip _downBig, _upBig;
    string _style;
    float _volume = -1;
    bool _failed;

    // (Re)opens the audio device - call when the keyboard opens, so a headset that became the
    // default output after Orbit started (Virtual Desktop) is picked up.
    public void Reopen()
    {
        if (_initializing) return;
        // only when the default device changed since it was opened: closing and reopening on every show left the first keys silent
        if (_xa != null && _openedFor == AudioMixer.DefaultChanges) return;
        _openedFor = AudioMixer.DefaultChanges;
        CloseAudio();
        _failed = false;
        Warm();
    }

    volatile bool _initializing;
    int _openedFor = -1;

    // Opens XAudio2 on a worker thread; a click requested before it is ready is skipped
    // (better than a 60 ms hitch in the laser loop).
    public void Warm()
    {
        if (_xa != null || _failed || _initializing) return;
        _initializing = true;
        new System.Threading.Thread(() => { try { Open(); } finally { _initializing = false; } }) { IsBackground = true }.Start();
    }

    bool EnsureAudio()
    {
        if (_xa != null) return true;
        Warm();
        return false;
    }

    bool Open()
    {
        if (_xa != null) return true;
        if (_failed) return false;
        IXAudio2 xa = null;
        try
        {
            // build everything first and publish _xa last: the render thread checks _xa to play
            XAudio2.XAudio2Create(out xa).CheckError();
            _master = xa.CreateMasteringVoice(0, 0, AudioStreamCategory.GameEffects, null, null);
            var fmt = new WaveFormat(Rate, 16, 1);
            for (int i = 0; i < _voices.Length; i++) _voices[i] = xa.CreateSourceVoice(fmt, false);
            System.Threading.Thread.MemoryBarrier();
            _xa = xa;
            return true;
        }
        catch (Exception e)
        {
            Log.Warn("audio: " + e.Message);
            // the render thread never saw these objects (_xa is still null): tear down locally
            for (int i = 0; i < _voices.Length; i++) { try { _voices[i]?.DestroyVoice(); } catch { } _voices[i] = null; }
            try { _master?.DestroyVoice(); } catch { }
            _master = null;
            try { xa?.Dispose(); } catch { }
            _failed = true;
            return false;
        }
    }

    Clip _chime;
    float _chimeVol = -1;

    // notification chime: two soft bell notes (B5, E6)
    public void PlayChime(float volume)
    {
        if (volume <= 0.001f || !EnsureAudio()) return;
        if (_chime == null || Math.Abs(volume - _chimeVol) > 0.001f)
        {
            foreach (var v in _voices) { v?.Stop(PlayFlags.None, 0); v?.FlushSourceBuffers(); }
            if (_chime != null) Marshal.FreeHGlobal(_chime.Data);
            int n = (int)(Rate * 0.55);
            var s = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float a = Note(t, 987.8f), b = t >= 0.09f ? Note(t - 0.09f, 1318.5f) : 0;
                s[i] = a * 0.8f + b;
            }
            _chime = ToClip(s, volume);
            _chimeVol = volume;
        }
        Submit(_chime);
    }

    static float Note(float t, float f) =>
        Math.Min(1, t / 0.004f) * MathF.Exp(-t / 0.16f) * (MathF.Sin(2 * MathF.PI * f * t) + 0.25f * MathF.Sin(4 * MathF.PI * f * t));

    void Submit(Clip c)
    {
        IXAudio2SourceVoice v = null;
        for (int i = 0; i < _voices.Length && v == null; i++)
        {
            var cand = _voices[(_nextVoice + i) % _voices.Length];
            if (cand.State.BuffersQueued == 0) { v = cand; _nextVoice = (_nextVoice + i + 1) % _voices.Length; }
        }
        if (v == null)
        {
            v = _voices[_nextVoice];
            _nextVoice = (_nextVoice + 1) % _voices.Length;
            v.Stop(PlayFlags.None, 0);
            v.FlushSourceBuffers();
        }
        try
        {
            v.SubmitSourceBuffer(new AudioBuffer(c.Data, (uint)c.Bytes, BufferFlags.EndOfStream), null);
            v.Start();
        }
        catch (Exception e) { Log.Warn("audio play: " + e.Message); }
    }

    public void Play(bool big, bool release, string style, float volume)
    {
        if (release || volume <= 0.001f || !EnsureAudio()) return;
        style = style == "muted" ? "muted" : "click";
        if (style != _style || Math.Abs(volume - _volume) > 0.001f) Build(style, volume);
        Clip c = big ? (release ? _upBig : _downBig)
                     : (release ? _up : _down)[_nextVariant++ % _down.Length];
        // a free voice, or the oldest one
        IXAudio2SourceVoice v = null;
        for (int i = 0; i < _voices.Length && v == null; i++)
        {
            var cand = _voices[(_nextVoice + i) % _voices.Length];
            if (cand.State.BuffersQueued == 0) { v = cand; _nextVoice = (_nextVoice + i + 1) % _voices.Length; }
        }
        if (v == null)
        {
            v = _voices[_nextVoice];
            _nextVoice = (_nextVoice + 1) % _voices.Length;
            v.Stop(PlayFlags.None, 0);
            v.FlushSourceBuffers();
        }
        try
        {
            v.SubmitSourceBuffer(new AudioBuffer(c.Data, (uint)c.Bytes, BufferFlags.EndOfStream), null);
            v.Start();
        }
        catch (Exception e) { Log.Warn("audio play: " + e.Message); }
    }

    void Build(string style, float volume)
    {
        foreach (var v in _voices) { v?.Stop(PlayFlags.None, 0); v?.FlushSourceBuffers(); }
        FreeClips();
        _style = style;
        _volume = volume;
        var rng = new Random(11);
        float[] pitches = { 1.0f, 1.05f, 0.96f, 1.02f };
        _down = new Clip[4]; _up = new Clip[4];
        for (int i = 0; i < 4; i++)
        {
            _down[i] = ToClip(Synth(style, pitches[i], false, false, rng), volume);
            _up[i] = ToClip(Synth(style, pitches[i] * 1.12f, true, false, rng), volume * 0.45f);
        }
        _downBig = ToClip(Synth(style, 0.82f, false, true, rng), volume);
        _upBig = ToClip(Synth(style, 0.9f, true, true, rng), volume * 0.45f);
    }

    static float[] Synth(string style, float pitch, bool release, bool big, Random rng)
    {
        int n = (int)(Rate * 0.012);
        var s = new float[n];
        // band-pass the noise: one-pole high-pass (~900 Hz) then one-pole low-pass
        float lpCut = (style == "muted" ? 2600f : 6500f) * (big ? 0.8f : 1f) * pitch;
        float hpA = MathF.Exp(-2 * MathF.PI * 900f / Rate), lpA = 1 - MathF.Exp(-2 * MathF.PI * lpCut / Rate);
        float hpPrevIn = 0, hp = 0, lp = 0;
        float decay = big ? 0.0011f : 0.0008f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float x = (float)(rng.NextDouble() * 2 - 1) * MathF.Exp(-t / decay);
            hp = hpA * (hp + x - hpPrevIn);
            hpPrevIn = x;
            lp += lpA * (hp - lp);
            s[i] = lp;
        }
        return s;
    }

    static float[] Normalised(float[] s, float volume)
    {
        float peak = 1e-6f;
        foreach (var v in s) peak = Math.Max(peak, Math.Abs(v));
        float gain = Math.Clamp(volume, 0, 1) * 0.85f / peak;
        var o = new float[s.Length];
        int fade = Rate * 3 / 1000;
        for (int i = 0; i < s.Length; i++)
            o[i] = s[i] * gain * (i > s.Length - fade ? (s.Length - i) / (float)fade : 1);
        return o;
    }

    static Clip ToClip(float[] s, float volume)
    {
        var n = Normalised(s, volume);
        var pcm = new short[n.Length];
        for (int i = 0; i < n.Length; i++) pcm[i] = (short)Math.Clamp(n[i] * 32767, -32768, 32767);
        var c = new Clip { Bytes = pcm.Length * 2 };
        c.Data = Marshal.AllocHGlobal(c.Bytes);      // XAudio2 reads it while playing; freed on rebuild
        Marshal.Copy(pcm, 0, c.Data, pcm.Length);
        return c;
    }

    void FreeClips()
    {
        foreach (var c in _down) if (c != null) Marshal.FreeHGlobal(c.Data);
        foreach (var c in _up) if (c != null) Marshal.FreeHGlobal(c.Data);
        if (_downBig != null) Marshal.FreeHGlobal(_downBig.Data);
        if (_upBig != null) Marshal.FreeHGlobal(_upBig.Data);
        _down = _up = Array.Empty<Clip>();
        _downBig = _upBig = null;
    }

    void CloseAudio()
    {
        for (int i = 0; i < _voices.Length; i++) { try { _voices[i]?.DestroyVoice(); } catch { } _voices[i] = null; }
        try { _master?.DestroyVoice(); } catch { }
        _master = null;
        _xa?.Dispose();
        _xa = null;
        _style = null;                 // clips are rebuilt (and their memory freed) with the new voices
        FreeClips();
        if (_chime != null) { Marshal.FreeHGlobal(_chime.Data); _chime = null; }
    }

    public void Dispose()
    {
        // let a background Open finish before tearing down
        for (int i = 0; i < 50 && _initializing; i++) System.Threading.Thread.Sleep(10);
        CloseAudio();
    }

    // used by the snapshot tool to check the generated sounds
    public static void Export(string dir)
    {
        foreach (var style in new[] { "click", "muted" })
        {
            var rng = new Random(11);
            Wav(Path.Combine(dir, $"key_{style}.wav"), Normalised(Synth(style, 1, false, false, rng), 0.8f));
            Wav(Path.Combine(dir, $"key_{style}_big.wav"), Normalised(Synth(style, 0.82f, false, true, rng), 0.8f));
        }
    }

    static void Wav(string path, float[] s)
    {
        using var w = new BinaryWriter(File.Create(path));
        int dataLen = s.Length * 2;
        w.Write("RIFF"u8.ToArray()); w.Write(36 + dataLen); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(dataLen);
        foreach (var v in s) w.Write((short)Math.Clamp(v * 32767, -32768, 32767));
    }
}
