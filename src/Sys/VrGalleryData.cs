using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Orbit;

// VR Gallery (Erik's photo album app, github.com/Erik1028/VRGallery), read only - the watch's gallery is "VR Gallery on the wrist"
// (Erik 10-24): its library knows each photo's world (from VRCX's metadata, or from VRChat's log by the session it was taken in), the
// people in it, whether it is a favourite and its rating; its settings say whether its local API runs (and on which port, with which
// token) and how long a slide stays in its slideshow. The database is opened read only and immutable (Vrcx.Open: no lock it could trip
// over, nothing written - the newest photos may wait for its next checkpoint, their own files say the rest); its config.json is only
// read. Its API's one action used: "discord" - VR Gallery sends its newest photo to the Discord webhook set up in it (only the newest:
// the API has no way to name a photo), on a tap of the watch's Send.
public static class VrGalleryData
{
    public sealed class Info
    {
        public string World;
        public string[] Players = Array.Empty<string>();
        public bool Favorite;
        public int Rating;
    }

    static string Dir(string slug) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), slug);

    // its library: VRGallery\vrgallery.db (VRChronicle\vrchronicle.db before the rename); null: not installed
    public static string DbPath()
    {
        foreach (var (slug, file) in new[] { ("VRGallery", "vrgallery.db"), ("VRChronicle", "vrchronicle.db") })
        {
            string p = Path.Combine(Dir(slug), file);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    public sealed class Settings
    {
        public bool Api;
        public int Port;
        public string Token;
        public bool Webhook;
        public int SlideSeconds = 5;
        public HashSet<string> Self = new(StringComparer.OrdinalIgnoreCase);
    }

    // its config.json (read only); null: none
    public static Settings ReadSettings()
    {
        try
        {
            foreach (var slug in new[] { "VRGallery", "VRChronicle" })
            {
                string p = Path.Combine(Dir(slug), "config.json");
                if (!File.Exists(p) || new FileInfo(p).Length > 4 << 20) continue;
                using var doc = JsonDocument.Parse(File.ReadAllText(p));
                var r = doc.RootElement;
                if (r.ValueKind != JsonValueKind.Object) return null;
                var s = new Settings
                {
                    Api = r.TryGetProperty("api_enabled", out var a) && a.ValueKind == JsonValueKind.True,
                    Port = r.TryGetProperty("api_port", out var po) && po.ValueKind == JsonValueKind.Number && po.TryGetInt32(out int port) ? port : 8770,
                    Token = r.TryGetProperty("api_token", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null,
                    Webhook = r.TryGetProperty("webhook_url", out var w) && w.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(w.GetString()),
                };
                if (r.TryGetProperty("slideshow_secs", out var ss) && ss.ValueKind == JsonValueKind.Number && ss.TryGetInt32(out int sec))
                    s.SlideSeconds = Math.Clamp(sec, 1, 60);
                if (r.TryGetProperty("self_names", out var sn) && sn.ValueKind == JsonValueKind.Array)
                    foreach (var n in sn.EnumerateArray())
                        if (n.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(n.GetString())) s.Self.Add(n.GetString().Trim());
                if (s.Port is < 1 or > 65535) s.Port = 8770;
                return s;
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { Log.Info("vr gallery: config not read: " + e.Message); }
        return null;
    }

    // the library by file name (VRChat's names are unique; a folder moved keeps them): world, people (without your own names), favourite,
    // rating. On the album's thread; ~2000 photos: tens of ms.
    public static Dictionary<string, Info> Read(string db, HashSet<string> self)
    {
        var map = new Dictionary<string, Info>(StringComparer.OrdinalIgnoreCase);
        IntPtr h = Vrcx.Open(db);
        try
        {
            var ids = new Dictionary<string, Info>();
            foreach (var r in Vrcx.Query(h,
                         "SELECT p.id, p.filename, COALESCE(NULLIF(p.world_name, ''), s.world_name), p.favorite, p.rating FROM photos p " +
                         "LEFT JOIN sessions s ON s.id = p.session_id WHERE COALESCE(p.missing, 0) = 0 AND p.deleted_at IS NULL", 5))
            {
                if (string.IsNullOrEmpty(r[1])) continue;
                var info = new Info
                {
                    World = string.IsNullOrWhiteSpace(r[2]) ? null : r[2].Trim(),
                    Favorite = r[3] is { } f && f != "0" && f.Length > 0,
                    Rating = int.TryParse(r[4], out int rt) ? Math.Clamp(rt, 0, 5) : 0,
                };
                map[r[1]] = info;
                if (r[0] != null) ids[r[0]] = info;
            }
            var people = new Dictionary<Info, List<string>>();
            foreach (var r in Vrcx.Query(h, "SELECT photo_id, name FROM photo_players ORDER BY photo_id, name", 2))
            {
                if (r[0] == null || string.IsNullOrWhiteSpace(r[1]) || !ids.TryGetValue(r[0], out var info) || self.Contains(r[1].Trim())) continue;
                if (!people.TryGetValue(info, out var l)) people[info] = l = new List<string>();
                l.Add(r[1].Trim());
            }
            foreach (var (info, l) in people) info.Players = l.ToArray();
        }
        finally { Vrcx.Close(h); }
        return map;
    }

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };

    // VR Gallery sends its newest photo to its Discord webhook (POST /latest/discord on 127.0.0.1); null: done - else why not (English,
    // the panel says it in its language). Blocking: on a thread of its own.
    public static string SendNewest(Settings s)
    {
        if (s == null) return "VR Gallery is not set up";
        if (!s.Api || string.IsNullOrEmpty(s.Token)) return "VR Gallery's API is off";
        if (!s.Webhook) return "No Discord webhook in VR Gallery";
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{s.Port}/latest/discord?token={Uri.EscapeDataString(s.Token)}");
            using var resp = Http.Send(req);
            if ((int)resp.StatusCode == 403) return "VR Gallery refused the token";
            if (!resp.IsSuccessStatusCode) return "VR Gallery did not send it";
            Log.Info("vr gallery: asked to send its newest photo to Discord");
            return null;
        }
        catch (HttpRequestException) { return "VR Gallery is not running"; }
        catch (TaskCanceledException) { return "VR Gallery did not answer"; }
    }
}
