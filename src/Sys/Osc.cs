using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Orbit;

// Sends the XSOverlay-style avatar parameters to VRChat (UDP 127.0.0.1:9000), which is what
// avatar add-ons such as DXOverlay listen to: isOverlayOpen, isKeyboardOpen, isWristVisible,
// openOverlayCount and the controller/tracker battery floats.
// Values go out on change and are all re-sent every few seconds, so a freshly loaded avatar
// picks them up without us having to listen on VRChat's output port (Hexpad owns 9001).
public sealed class OscSender : IDisposable
{
    readonly UdpClient _udp = new();
    readonly Dictionary<string, Value> _last = new();
    IPEndPoint _ep;
    double _nextFull;
    bool _full;

    // what went out last for a parameter: its type and value, kept without boxing (the app sends 10 times a second)
    readonly record struct Value(char Type, int I, float F);

    public void Send(double now, int port, IEnumerable<(string name, object value)> values, bool force = false)
    {
        Begin(now, port, force);
        foreach (var (name, value) in values)
            switch (value)
            {
                case bool b: Param(name, b); break;
                case int i: Param(name, i); break;
                case float f: Param(name, f); break;
                default: throw new ArgumentException("unsupported OSC type " + value?.GetType());
            }
    }

    // A round of avatar parameters: Begin, then Param for each - sent when it changed since it last went out, all of them every 5 s
    // (force: all of them now)
    public void Begin(double now, int port, bool force = false)
    {
        if (_ep == null || _ep.Port != port) { _ep = new IPEndPoint(IPAddress.Loopback, port); _last.Clear(); }
        _full = force || now >= _nextFull;
        if (_full) _nextFull = now + 5;
    }

    public void Param(string name, bool v) { if (Changed(name, new Value('T', v ? 1 : 0, 0))) Send(name, v); }
    public void Param(string name, int v) { if (Changed(name, new Value('i', v, 0))) Send(name, v); }
    public void Param(string name, float v) { if (Changed(name, new Value('f', 0, v))) Send(name, v); }

    // (a float compares as float.Equals did on the boxed one: a NaN equals a NaN)
    bool Changed(string name, Value v)
    {
        if (!_full && _last.TryGetValue(name, out var old) && old.Type == v.Type && old.I == v.I && old.F.Equals(v.F)) return false;
        _last[name] = v;
        return true;
    }

    void Send(string name, object value)
    {
        var msg = Encode("/avatar/parameters/" + name, value);
        try { _udp.Send(msg, msg.Length, _ep); } catch (Exception e) { Log.Warn("osc: " + e.Message); }
    }

    public static byte[] Encode(string address, object value) => Encode(address, new[] { value });

    // OSC message with any number of string / bool / int / float arguments (strings as UTF-8,
    // which VRChat's chatbox reads fine - accents included).
    public static byte[] Encode(string address, object[] values)
    {
        var buf = new List<byte>(64);
        void Bytes(byte[] b)
        {
            buf.AddRange(b);
            int pad = 4 - b.Length % 4;           // at least one NUL, then align to 4
            for (int i = 0; i < pad; i++) buf.Add(0);
        }
        void BigEndian(byte[] b) { if (BitConverter.IsLittleEndian) Array.Reverse(b); buf.AddRange(b); }

        Bytes(Encoding.UTF8.GetBytes(address));       // (UTF-8: an avatar's parameter may have letters ASCII has not - the avatar toggles)
        var tags = new StringBuilder(",");
        foreach (var v in values)
            tags.Append(v switch
            {
                bool b => b ? 'T' : 'F',
                int => 'i',
                float => 'f',
                string => 's',
                _ => throw new ArgumentException("unsupported OSC type " + v?.GetType()),
            });
        Bytes(Encoding.ASCII.GetBytes(tags.ToString()));
        foreach (var v in values)
            switch (v)
            {
                case int i: BigEndian(BitConverter.GetBytes(i)); break;
                case float f: BigEndian(BitConverter.GetBytes(f)); break;
                case string str: Bytes(Encoding.UTF8.GetBytes(str)); break;
            }
        return buf.ToArray();
    }

    // one-off message to any address (chatbox etc.)
    public void SendMessage(int port, string address, params object[] values)
    {
        if (_ep == null || _ep.Port != port) { _ep = new IPEndPoint(IPAddress.Loopback, port); _last.Clear(); }
        var msg = Encode(address, values);
        try { _udp.Send(msg, msg.Length, _ep); } catch (Exception e) { Log.Warn("osc: " + e.Message); }
    }

    public void Dispose() => _udp.Dispose();
}
