using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Orbit;

// Where the Tapo devices on this network are now (Erik 10-23: the router's network moved from 192.168.0.x to 192.168.1.x and the bulb's
// saved address went dead). TP-Link's own discovery: one UDP probe to port 20002 (to every network's broadcast address), the devices
// answer with their address, MAC and kind - the way the Tapo app and python-kasa find them. The probe is a 16-byte header (version 2,
// probe, flags 17, a random serial, a CRC-32 over it all with 0x5A6B7C8D in its place first) and {"params":{"rsa_key": a public key}};
// the answer is the same header and {"result": {"ip", "mac", "device_type", "device_model", ...}}. Nothing is signed in to or changed.
public static class TapoFind
{
    public const int Port = 20002;

    public sealed record Found(string Ip, string Mac, string Type, string Model)
    {
        public bool Bulb => Type?.Contains("BULB", StringComparison.OrdinalIgnoreCase) == true;
        public override string ToString() => $"{Ip} {Mac} {Type} {Model}";
    }

    // the probe out, the answers for `ms` milliseconds (on the caller's thread: the light's own)
    public static List<Found> Find(int ms = 1500)
    {
        var found = new Dictionary<string, Found>();
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0)) { EnableBroadcast = true };
        var probe = Probe();
        foreach (var to in Broadcasts())
            try { udp.Send(probe, probe.Length, new IPEndPoint(to, Port)); }
            catch (SocketException) { }
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (true)
        {
            int left = (int)(until - DateTime.UtcNow).TotalMilliseconds;
            if (left <= 0 || !udp.Client.Poll(left * 1000, SelectMode.SelectRead)) break;
            IPEndPoint from = null;
            byte[] data;
            try { data = udp.Receive(ref from); }
            catch (SocketException) { continue; }
            if (data.Length <= 16 || from == null) continue;
            try
            {
                using var doc = JsonDocument.Parse(data.AsMemory(16));
                if (!doc.RootElement.TryGetProperty("result", out var r) || r.ValueKind != JsonValueKind.Object) continue;
                string S(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                string ip = from.Address.ToString();
                found[ip] = new Found(ip, NormMac(S("mac")), S("device_type"), S("device_model"));
            }
            catch (JsonException) { }
        }
        return found.Values.OrderBy(f => f.Ip).ToList();
    }

    // E4-FA-C4-E3-40-14 whichever way it is written
    public static string NormMac(string mac) => string.IsNullOrWhiteSpace(mac) ? null : mac.Trim().Replace(':', '-').ToUpperInvariant();

    static byte[] Probe()
    {
        using var rsa = RSA.Create(2048);
        string pem = rsa.ExportSubjectPublicKeyInfoPem().Replace("\r", "") + "\n";
        var body = Encoding.ASCII.GetBytes("{\"params\":{\"rsa_key\":\"" + pem.Replace("\n", "\\n") + "\"}}");
        var p = new byte[16 + body.Length];
        p[0] = 2;                                   // version
        p[1] = 0;                                   // message type
        p[2] = 0; p[3] = 1;                         // op code: probe
        p[4] = (byte)(body.Length >> 8); p[5] = (byte)body.Length;
        p[6] = 17;                                  // flags
        p[7] = 0;
        RandomNumberGenerator.Fill(p.AsSpan(8, 4)); // serial
        p[12] = 0x5A; p[13] = 0x6B; p[14] = 0x7C; p[15] = 0x8D;
        body.CopyTo(p, 16);
        uint crc = Crc32(p);
        p[12] = (byte)(crc >> 24); p[13] = (byte)(crc >> 16); p[14] = (byte)(crc >> 8); p[15] = (byte)crc;
        return p;
    }

    // 255.255.255.255 and each network's own broadcast address (Windows sends the first out of one network card only)
    static IEnumerable<IPAddress> Broadcasts()
    {
        var all = new List<IPAddress> { IPAddress.Broadcast };
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var u in nic.GetIPProperties().UnicastAddresses)
                {
                    if (u.Address.AddressFamily != AddressFamily.InterNetwork || u.IPv4Mask == null) continue;
                    var a = u.Address.GetAddressBytes();
                    var m = u.IPv4Mask.GetAddressBytes();
                    if (a[0] == 169 && a[1] == 254) continue;                  // no DHCP answer on that card: nothing there
                    for (int i = 0; i < 4; i++) a[i] = (byte)(a[i] | ~m[i]);
                    var b = new IPAddress(a);
                    if (!all.Contains(b)) all.Add(b);
                }
            }
        }
        catch (NetworkInformationException) { }
        return all;
    }

    static readonly uint[] Table = Enumerable.Range(0, 256).Select(n =>
    {
        uint c = (uint)n;
        for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    static uint Crc32(byte[] b)
    {
        uint c = 0xFFFFFFFFu;
        foreach (byte x in b) c = Table[(c ^ x) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
