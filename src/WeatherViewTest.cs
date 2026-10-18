using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;

namespace Orbit;

// --snapshot's part for the weather widget (Sys/Weather.cs, WristWeather*.cs) - never the network and never anyone's real place: a
// made-up forecast in Open-Meteo's own shape for a made-up town ("Orbitville, Made-up Land"; its hours and days start at this hour, so
// the strips show), made-up geocoding answers, a copy of nothing real for Hexpad's config.
//  - the data (once): the parser on the sample answer, the cache written and read back, a failed fetch keeping the last report as
//    stale, nothing kept and offline, no place here or in Hexpad ("Set a location"), Hexpad's place taken from a made-up Hexpad config,
//    a place the geocoding does not know; °F.
//  - each style: a tap on the card opens it, its X closes it; the watch at rest and opened makes no garbage; the pictures
//    wrist_weather_<case><tag>.png (every size on a 6 x 4 and a 4 x 3 face, opened, night, stale, none) - and at the end of the run
//    weather_preview.png (every style's sizes and opened side by side), weather_states.png, qa/weather_glyphs.png.
static class WeatherViewTools
{
    public const string Town = "Orbitville", Land = "Made-up Land";

    // ---- the made-up answers ----

    // the forecast: partly cloudy by day now, the next 36 hours through every kind of weather, 7 days; times on this PC's clock
    public static string SampleJson(DateTime hour, int nowCode = 2, bool day = true, double temp = 18.4)
    {
        var inv = CultureInfo.InvariantCulture;
        int off = (int)TimeZoneInfo.Local.GetUtcOffset(hour).TotalSeconds;
        var sb = new StringBuilder();
        sb.Append("{\"latitude\":12.34,\"longitude\":56.78,\"generationtime_ms\":0.5,\"utc_offset_seconds\":").Append(off)
          .Append(",\"timezone\":\"Etc/Made-up\",\"timezone_abbreviation\":\"MUT\",\"elevation\":120.0,");
        sb.Append("\"current_units\":{\"time\":\"iso8601\",\"temperature_2m\":\"°C\"},");
        sb.Append("\"current\":{\"time\":\"").Append(hour.ToString("yyyy-MM-dd'T'HH:mm", inv)).Append("\",\"interval\":900,\"temperature_2m\":")
          .Append(temp.ToString(inv)).Append(",\"relative_humidity_2m\":62,\"apparent_temperature\":").Append((temp - 1.5).ToString(inv))
          .Append(",\"precipitation\":0.00,\"weather_code\":").Append(nowCode).Append(",\"wind_speed_10m\":11.3,\"wind_direction_10m\":225,")
          .Append("\"uv_index\":3.2,\"visibility\":24000.00,\"is_day\":").Append(day ? 1 : 0).Append("},");
        int[] codes = { 2, 2, 1, 0, 0, 3, 3, 45, 51, 61, 63, 80, 95, 71, 73, 2, 1, 0, 0, 0, 2, 3, 61, 61, 3, 2, 1, 0, 0, 1, 2, 3, 3, 2, 1, 0 };
        codes[0] = nowCode;                                  // the hour it is now: the weather now
        sb.Append("\"hourly\":{\"time\":[");
        for (int i = 0; i < 36; i++) sb.Append(i > 0 ? "," : "").Append('"').Append(hour.AddHours(i).ToString("yyyy-MM-dd'T'HH:mm", inv)).Append('"');
        sb.Append("],\"temperature_2m\":[");
        for (int i = 0; i < 36; i++) sb.Append(i > 0 ? "," : "").Append((temp - 3.5 * Math.Sin(i / 24.0 * 2 * Math.PI) - i * 0.06).ToString("0.0", inv));
        sb.Append("],\"weather_code\":[").Append(string.Join(",", codes)).Append("],\"is_day\":[");
        for (int i = 0; i < 36; i++) { int h = hour.AddHours(i).Hour; sb.Append(i > 0 ? "," : "").Append((i == 0 ? day : h >= 7 && h < 18) ? 1 : 0); }
        sb.Append("],\"precipitation_probability\":[");
        for (int i = 0; i < 36; i++) sb.Append(i > 0 ? "," : "").Append(codes[i] >= 51 ? 70 : 5);
        sb.Append("]},");
        var d0 = hour.Date;
        sb.Append("\"daily\":{\"time\":[").Append(string.Join(",", Enumerable.Range(0, 7).Select(i => $"\"{d0.AddDays(i):yyyy-MM-dd}\"")))
          .Append("],\"weather_code\":[2,61,0,95,73,45,3],\"temperature_2m_max\":[21.3,16.8,23.1,19.4,2.6,11.2,14.9],")
          .Append("\"temperature_2m_min\":[9.7,8.1,10.4,12.2,-3.8,4.6,6.0],\"uv_index_max\":[4.1,2.0,5.6,3.3,1.1,1.8,2.7],")
          .Append("\"precipitation_probability_max\":[10,80,0,90,70,20,15],\"sunrise\":[")
          .Append(string.Join(",", Enumerable.Range(0, 7).Select(i => $"\"{d0.AddDays(i):yyyy-MM-dd}T07:{6 + i:00}\"")))
          .Append("],\"sunset\":[").Append(string.Join(",", Enumerable.Range(0, 7).Select(i => $"\"{d0.AddDays(i):yyyy-MM-dd}T18:{12 - i:00}\"")))
          .Append("]}}");
        return sb.ToString();
    }

    public const string GeoJson = "{\"results\":[{\"id\":1,\"name\":\"Orbitville\",\"latitude\":12.34,\"longitude\":56.78,\"country\":\"Made-up Land\",\"admin1\":\"Nowhere\"}]}";
    public const string GeoNone = "{\"generationtime_ms\":0.3}";

    static DateTime ThisHour { get { var n = DateTime.Now; return new DateTime(n.Year, n.Month, n.Day, n.Hour, 0, 0); } }

    // a report as the watch gets it (stale: an old one; place: the made-up town's)
    public static WeatherReport Report(int code = 2, bool day = true, bool stale = false, double temp = 18.4)
    {
        var r = Weather.Parse(SampleJson(ThisHour, code, day, temp));
        r.Place = Town;
        r.Region = Land;
        r.Query = Town;
        r.Fetched = DateTime.UtcNow.AddMinutes(stale ? -95 : -4);
        r.Stale = stale;
        r.Version = 1000 + code * 10 + (day ? 1 : 0) + (stale ? 5 : 0);
        return r;
    }

    public static WeatherReport None(WxStatus s, string query = "") => new() { Status = s, Query = query, Version = 2000 + (int)s };

    // ---- the data ----

    public static void DataChecks(WristPanel wrist, PanelData data, ConfigStore cfg, string dir)
    {
        var inv = CultureInfo.InvariantCulture;
        // the parser on the sample answer
        var r = Weather.Parse(SampleJson(ThisHour));
        bool parsed = MathF.Abs(r.Temp - 18.4f) < 0.01f && r.Kind == WxKind.Partly && r.Day && r.Hours.Length == 36 && r.Days.Length == 7 &&
                      r.Days[0].Rise.Hour == 7 && r.Days[0].Rise.Minute == 6 && MathF.Abs(r.Days[4].Lo + 3.8f) < 0.01f && r.Hours[12].Code == 95 &&
                      MathF.Abs(r.WindDir - 225) < 0.01f;
        bool bad = false;
        try { Weather.Parse("{\"latitude\":1}"); } catch (FormatException) { bad = true; }
        Tools.Want($"weather parse: a sample Open-Meteo answer reads 18.4 °C, partly cloudy by day, 36 hours, 7 days, sunrise 07:06 {parsed}; " +
                   $"an answer without the weather now is refused {bad} -", parsed && bad);
        var place = Weather.ParsePlace(GeoJson);
        bool geo = place is { Name: Town, Region: Land } p0 && Math.Abs(p0.Lat - 12.34) < 1e-6 && Weather.ParsePlace(GeoNone) == null;
        bool coords = Weather.TryCoords(" 12.5 , -45.25", out double la, out double lo) && la == 12.5 && lo == -45.25 && !Weather.TryCoords("Orbitville", out _, out _) &&
                      !Weather.TryCoords("95,10", out _, out _);
        Tools.Want($"weather place: the geocoding's answer read {geo}, \"lat,lon\" taken as numbers {coords} -", geo && coords);

        // the cache: written and read back, only for the same place
        string wd = Path.Combine(dir, "weather_test");
        if (Directory.Exists(wd)) Directory.Delete(wd, true);
        Directory.CreateDirectory(wd);
        string cache = Path.Combine(wd, "weather_cache.json"), json = SampleJson(ThisHour);
        var fetched = DateTime.UtcNow.AddMinutes(-7);
        Weather.SaveCache(cache, Town, place.Value, fetched, json);
        var back = Weather.LoadCache(cache, " orbitville ");
        var rb = back is { } b0 ? Weather.Parse(b0.forecast) : null;
        bool round = back is { } b && b.place == place.Value && Math.Abs((b.fetched - fetched).TotalSeconds) < 1 && rb != null &&
                     rb.Temp == r.Temp && rb.Hours.Length == r.Hours.Length && rb.Days[3].Code == r.Days[3].Code;
        bool other = Weather.LoadCache(cache, "Somewhere Else") == null;
        Tools.Want($"weather cache: written and read back the same report {round}, another place's cache not used {other} -", round && other);

        // the thread, on made-up answers
        var asked = new List<string>();
        bool offline = false;
        string Answer(string url)
        {
            lock (asked) asked.Add(url);
            if (offline) throw new System.Net.Http.HttpRequestException("offline (made up)");
            if (url.Contains("geocoding")) return url.Contains("Nowhere") ? GeoNone : GeoJson;
            return SampleJson(ThisHour);
        }
        WeatherReport Wait(WeatherService s, Func<WeatherReport, bool> ok)
        {
            for (int i = 0; i < 100 && !ok(s.Report); i++) Thread.Sleep(30);
            return s.Report;
        }
        string emptyHex = Path.Combine(wd, "hexpad_none");
        Directory.CreateDirectory(emptyHex);

        // a kept report, then the fetch fails: it stays, marked stale
        using (var s = new WeatherService { CachePath = cache, HexpadDir = emptyHex, Get = Answer })
        {
            offline = true;
            Weather.SaveCache(cache, Town, place.Value, DateTime.UtcNow.AddMinutes(-20), json);
            s.SetLocation(Town);
            s.Active = true;
            var st = Wait(s, x => x.HasData && x.Stale);
            data.Weather = st;
            bool kept = st.HasData && st.Place == Town && MathF.Abs(st.Temp - 18.4f) < 0.01f;
            bool shows = wrist.WeatherStaleShown(data);
            Tools.Want($"weather stale: a fetch that fails keeps the last report {kept}, marked stale {st.Stale}, the watch shows it so {shows} -", kept && st.Stale && shows);
        }
        // nothing kept and no answer: why
        File.Delete(cache);
        using (var s = new WeatherService { CachePath = cache, HexpadDir = emptyHex, Get = Answer })
        {
            offline = true;
            s.SetLocation(Town);
            s.Active = true;
            var st = Wait(s, x => x.Status == WxStatus.Offline);
            data.Weather = st;
            string said = wrist.WeatherShown(data);
            Tools.Want($"weather offline: nothing kept and no answer says '{said}' (want No connection)", said == "No connection");
        }
        // no place here or in Hexpad
        using (var s = new WeatherService { CachePath = cache, HexpadDir = emptyHex, Get = Answer })
        {
            offline = false;
            s.SetLocation("");
            s.Active = true;
            var st = Wait(s, x => x.Status == WxStatus.NoLocation);
            data.Weather = st;
            lock (asked) asked.Clear();
            Thread.Sleep(100);
            bool quiet;
            lock (asked) quiet = asked.Count == 0;
            string said = wrist.WeatherShown(data);
            Tools.Want($"weather no place: none here or in Hexpad says '{said}' (want Set a location), nothing asked of the network {quiet} -", said == "Set a location" && quiet);
        }
        // Hexpad's place (a made-up Hexpad config and cache, read only): taken, and not looked up again
        string hex = Path.Combine(wd, "hexpad");
        Directory.CreateDirectory(hex);
        File.WriteAllText(Path.Combine(hex, "config.json"), "{\"brightness\":70,\"weather\":\"Starfall Bay\",\"weather_units\":\"c\",\"pages\":[]}");
        File.WriteAllText(Path.Combine(hex, "weather_cache.json"), "{\"place\":\"Starfall Bay\",\"loc\":[10.5,20.25,\"Starfall Bay\"],\"state\":null}");
        string hexCfg0 = File.ReadAllText(Path.Combine(hex, "config.json"));
        File.Delete(cache);
        using (var s = new WeatherService { CachePath = cache, HexpadDir = hex, Get = Answer })
        {
            lock (asked) asked.Clear();
            s.SetLocation(null);
            s.Active = true;
            var st = Wait(s, x => x.HasData);
            bool fromHex = st.FromHexpad && st.Query == "Starfall Bay" && st.Place == "Starfall Bay" && st.HasData;
            bool noGeo, atHex;
            lock (asked)
            {
                noGeo = asked.Count > 0 && !asked.Any(u => u.Contains("geocoding"));
                atHex = asked.Any(u => u.Contains("latitude=10.5&longitude=20.25"));
            }
            bool untouched = File.ReadAllText(Path.Combine(hex, "config.json")) == hexCfg0;
            Tools.Want($"weather Hexpad: no place set - Hexpad's ('Starfall Bay', a made-up config) used {fromHex}, where its own cache found it, not looked up " +
                       $"again {noGeo && atHex}, Hexpad's file unchanged {untouched} -", fromHex && noGeo && atHex && untouched);
            // the setting wins over Hexpad's
            lock (asked) asked.Clear();
            s.SetLocation(Town);
            var st2 = Wait(s, x => x.HasData && x.Place == Town);
            bool looked;
            lock (asked) looked = asked.Any(u => u.Contains("geocoding"));
            Tools.Want($"weather place: the setting wins over Hexpad's {st2.Place == Town && !st2.FromHexpad}, looked up by its name {looked} -",
                       st2.Place == Town && !st2.FromHexpad && looked);
            // a place the geocoding does not know
            s.SetLocation("Nowhere At All");
            var st3 = Wait(s, x => x.Status == WxStatus.NotFound);
            data.Weather = st3;
            string said = wrist.WeatherShown(data);
            Tools.Want($"weather place: one the geocoding does not know says '{said}' (want Place not found)", said == "Place not found");
        }
        // °F: the same report in the other units
        var wx0 = cfg.Data.Weather;
        data.Weather = Report();
        string c0 = wrist.WeatherShown(data);
        cfg.Data.Weather = new WeatherConfig { Units = "f" };
        string f0 = wrist.WeatherShown(data);
        cfg.Data.Weather = wx0;
        Tools.Want($"weather units: 18.4 °C shows {c0} (want 18°), in °F {f0} (want 65°)", c0 == "18°" && f0 == "65°");
        int hoursN = wrist.WeatherHoursShown(data), daysN = wrist.WeatherDaysShown(data);
        Tools.Want($"weather data: the next hours shown {hoursN} (want 12), days {daysN} (want 6)", hoursN == 12 && daysN == 6);
        // the config: nothing written for the defaults, a place / units read back
        var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
        string plain = System.Text.Json.JsonSerializer.Serialize(new Config(), opts);
        var set = new Config { Weather = new WeatherConfig { Location = Town, Units = "f" } };
        var rt = System.Text.Json.JsonSerializer.Deserialize<Config>(System.Text.Json.JsonSerializer.Serialize(set, opts), opts);
        Tools.Want($"weather config: a config without it writes nothing {!plain.Contains("\"Weather\"")}, a place and °F read back " +
                   $"{rt.Weather?.Location == Town && rt.Weather?.Units == "f"} -", !plain.Contains("\"Weather\"") && rt.Weather?.Location == Town && rt.Weather?.Units == "f");
        data.Weather = Report();
        try { Directory.Delete(wd, true); } catch { }
    }

    // ---- each style ----

    static void Draw2(WristPanel wrist, PanelData data) { wrist.Draw(data); wrist.Draw(data); }

    static void Tap(WristPanel wrist, PanelData data, Vector2 p)
    {
        wrist.PointerMove(Hand.Left, p); wrist.Draw(data);
        wrist.PointerButton(Hand.Left, PtrButton.Primary, true, p); wrist.Draw(data);
        wrist.PointerButton(Hand.Left, PtrButton.Primary, false, p); wrist.Draw(data);
        wrist.PointerLeave(Hand.Left); wrist.Draw(data);
    }

    static void Settle(WristPanel wrist, PanelData data)
    {
        for (int k = 0; k < 11; k++) { Thread.Sleep(40); wrist.Draw(data); }
    }

    static Vector2 Middle(System.Drawing.RectangleF c)
    {
        float s = Theme.Classic ? 2 : 1;
        return new Vector2((c.X + c.Width / 2) * s, (c.Y + c.Height / 2) * s);
    }

    static WatchWidget W(int x, int y, int w, int h, string type = "weather") => new() { Type = type, X = x, Y = y, W = w, H = h };

    // every size on the 6 x 4 grid, with a few others round them
    static List<WatchWidget> Face64() => new()
    {
        W(0, 0, 2, 2), W(2, 0, 4, 1), W(2, 1, 2, 1), W(4, 1, 1, 1), W(5, 1, 1, 1, "battery"),
        W(0, 2, 3, 2, "time"), W(3, 2, 3, 1, "media"),
        W(3, 3, 1, 1, "keyboard"), W(4, 3, 1, 1, "desktops"), W(5, 3, 1, 1, "night"),
    };

    // the 4 x 3 page's: L, M, S, the wide one
    static List<WatchWidget> Face43() => new() { W(0, 0, 2, 2), W(2, 0, 2, 1), W(2, 1, 1, 1), W(3, 1, 1, 1, "fps"), W(0, 2, 4, 1) };

    public static void Run(WristPanel wrist, PanelData data, ConfigStore cfg, Gpu gpu, string dir, string tag)
    {
        var wcfg = cfg.Data.Wrist;
        var keep = (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Pages, wcfg.Page, wcfg.Background);
        var wx0 = data.Weather;
        try
        {
            wcfg.Face = "custom";
            wcfg.Background = "panel";
            wcfg.Grid = null;
            wcfg.Pages = null;
            wcfg.Page = 0;
            data.Weather = Report();
            Checks(wrist, data, wcfg, tag);
            Pictures(wrist, data, wcfg, gpu, dir, tag);
        }
        finally
        {
            wrist.CloseForTest();
            wrist.PointerLeave(Hand.Left);
            data.Weather = wx0;
            (wcfg.Face, wcfg.Widgets, wcfg.Grid, wcfg.Pages, wcfg.Page, wcfg.Background) = keep;
        }
    }

    static void Checks(WristPanel wrist, PanelData data, WristConfig wcfg, string tag)
    {
        bool classic = Theme.Classic;
        string xId = classic ? "wd95x" : "wclose";
        // a tap on the card opens it; its X closes it
        wcfg.Widgets = new List<WatchWidget> { W(0, 2, 2, 1) };
        wrist.Settle();
        Draw2(wrist, data);
        Tap(wrist, data, Middle(WatchWidgets.CellRect(0, 2, 2, 1, classic)));
        Settle(wrist, data);
        bool opened = wrist.IsOpen && wrist.DetailKind == "weather";
        var x = wrist.ControlPoint(data, xId);
        if (x is { } xp) Tap(wrist, data, xp);
        bool shut = x != null && !wrist.IsOpen;
        Settle(wrist, data);
        wrist.CloseForTest();
        Tools.Want($"weather view{tag}: a tap on the card opened it {opened}, its X closed it {shut && !wrist.DetailShown} -", opened && shut && !wrist.DetailShown);
        // no garbage a frame: the weather sizes at rest, then opened
        wcfg.Widgets = new List<WatchWidget> { W(0, 0, 2, 2), W(2, 0, 4, 1), W(2, 1, 2, 1), W(4, 1, 1, 1) };
        long Bytes()
        {
            using var appOnly = Tools.AppAllocs();
            wrist.Settle();
            for (int i = 0; i < 4; i++) wrist.Draw(data);
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++) wrist.Draw(data);
            return (GC.GetAllocatedBytesForCurrentThread() - a0) / 20;
        }
        long rest = Bytes();
        if (Environment.GetEnvironmentVariable("ORBIT_PERF_TYPES") == "1") Tools.WeatherAllocTypes("weather" + tag, () => wrist.Draw(data));
        wrist.OpenForTest("weather");
        long open = Bytes();
        if (Environment.GetEnvironmentVariable("ORBIT_PERF_TYPES") == "1") Tools.WeatherAllocTypes("weather opened" + tag, () => wrist.Draw(data));
        wrist.CloseForTest();
        wcfg.Widgets = new List<WatchWidget>();
        long empty = Bytes();
        wcfg.Widgets = new List<WatchWidget> { W(0, 0, 2, 2, "battery"), W(2, 0, 4, 1, "media"), W(2, 1, 2, 1, "volume"), W(4, 1, 1, 1, "fps") };
        long other = Bytes();
        wrist.OpenForTest("volume");
        long otherOpen = Bytes();
        if (Environment.GetEnvironmentVariable("ORBIT_PERF_TYPES") == "1") Tools.WeatherAllocTypes("volume opened" + tag, () => wrist.Draw(data));
        wrist.CloseForTest();
        if (Environment.GetEnvironmentVariable("ORBIT_PERF_TYPES") == "1") Tools.WeatherAllocTypes("others" + tag, () => wrist.Draw(data));
        Tools.Want($"weather view{tag}: a frame of its sizes at rest makes {rest} B of garbage (want 0); opened {open} B - its window's frame, as every opened view's " +
                   $"(the volume opened {otherOpen} B; a face of the batteries, now playing, the volume and the frame rate {other} B, an empty one {empty} B) -", rest == 0);
    }

    static void Pictures(WristPanel wrist, PanelData data, WristConfig wcfg, Gpu gpu, string dir, string tag)
    {
        void Shot(string name, List<WatchWidget> face, WeatherReport r, bool open, string grid = null)
        {
            data.Weather = r;
            wcfg.Grid = grid;
            wcfg.Widgets = face;
            wrist.Settle();
            if (open && !wrist.OpenForTest("weather")) Log.Warn("weather view: no weather widget to open");
            Draw2(wrist, data);
            wrist.Surf.SavePng(gpu, Path.Combine(dir, $"wrist_weather_{name}{tag}.png"));
            wrist.CloseForTest();
        }
        Shot("face", Face64(), Report(), false);
        Shot("face43", Face43(), Report(), false, "4x3");
        Shot("open", Face64(), Report(), true);
        Shot("night", Face64(), Report(0, false, false, 9.6), false);
        Shot("rain", Face64(), Report(63, true, false, 11.2), false);
        Shot("stale", Face64(), Report(95, true, true), false);
        Shot("stale_open", Face64(), Report(95, true, true), true);
        Shot("none", Face64(), None(WxStatus.NoLocation), false);
        Shot("none_open", Face64(), None(WxStatus.NoLocation), true);
        wcfg.Grid = null;
        Log.Info($"weather view{tag}: 9 pictures (wrist_weather_*{tag}.png)");
    }

    // ---- the settings: the Wrist tab's rows (Modern, Cyber), the 95 family's dialog ----

    public static void SettingsChecks(SettingsView view, Ui ui, Surface surf, PointerState ptr, Painter painter, Gpu gpu, ConfigStore cfg, WristPanel wrist,
        PanelData data, string dir)
    {
        var keep = (cfg.Data.Appearance, cfg.Data.Weather, view.Tab, view.AskText);
        var face0 = cfg.Data.Wrist.Face;
        try
        {
            data.Weather = Report();
            cfg.Data.Wrist.Face = "custom";
            view.Tab = SettingsView.TabWrist;
            void Look(string style) { cfg.Data.Appearance = new AppearanceConfig { Style = style, Clock24h = true }; Theme.Apply(cfg.Data.Appearance); }
            void FrameM() { wrist.Draw(data); ui.Begin(ptr, 1.5f); painter.Begin(surf.Bitmap, Theme.Bg, 1.5f); view.Draw(ui); painter.End(); ui.End(); }
            void ClickM(float x, float y) { ptr.Move(x * 1.5f, y * 1.5f); FrameM(); ptr.Press(); FrameM(); ptr.Release(); FrameM(); }
            // the desktop window: Change asks for a text (here a made-up answer), the units are a switch
            string answer = Town;
            int asked = 0;
            view.AskText = (title, prompt, text, then) => { asked++; then(answer); };
            Look("modern");
            ui.ResetAnimations();
            float y0 = 92 + 372f * WristPanel.TH / WristPanel.TW + 180;           // WeatherRows' top (under the watch's own rows)
            ClickM(612 + (356 - 112) / 2f, y0 + 24 + 19);
            bool set = asked == 1 && cfg.Data.Weather?.Location == Town;
            ClickM(612 + 356 - 25, y0 + 24 + 19);
            bool fahrenheit = cfg.Data.Weather?.Units == "f";
            ptr.Leave();
            ui.ResetAnimations();
            FrameM();
            surf.SavePng(gpu, Path.Combine(dir, "settings_weather.png"));
            answer = "  ";
            ClickM(612 + (356 - 112) / 2f, y0 + 24 + 19);
            bool cleared = cfg.Data.Weather?.Location == null;
            ClickM(612 + 356 - 75, y0 + 24 + 19);
            bool none = cfg.Data.Weather == null && !cfg.Serialized().Contains("\"Weather\"");
            Tools.Want($"weather settings: Change asks the desktop window and keeps the place {set}, °F picked {fahrenheit}; " +
                       $"an empty answer brings Hexpad's place back {cleared}, °C again writes nothing {none} -", set && fahrenheit && cleared && none);
            // VR (no text box): the button is shown only; Cyber; the 95 family's dialog
            view.AskText = null;
            cfg.Data.Weather = new WeatherConfig { Location = Town };
            foreach (var (style, tag) in new[] { (CyberHud.Id, "_cyber"), ("classic", "_95"), ("xp", "_xp"), ("aero", "_aero"), ("mac", "_mac"), ("terminal", "_term") })
            {
                Look(style);
                ui.ResetAnimations();
                ptr.Leave();
                if (Theme.Classic)
                {
                    view.WeatherDialogOpen = true;
                    wrist.Draw(data);
                    view.Render95(ui, ptr, surf.Bitmap, 2, 0, 0, Theme.W95Teal);
                    view.Render95(ui, ptr, surf.Bitmap, 2, 0, 0, Theme.W95Teal);
                    view.WeatherDialogOpen = false;
                }
                else { FrameM(); FrameM(); }
                surf.SavePng(gpu, Path.Combine(dir, $"settings_weather{tag}.png"));
            }
            Log.Info("weather settings: 7 pictures (settings_weather*.png)");
        }
        finally
        {
            ptr.Leave();
            (cfg.Data.Appearance, cfg.Data.Weather, view.Tab, view.AskText) = keep;
            Theme.Apply(cfg.Data.Appearance);
            cfg.Data.Wrist.Face = face0;
            view.WeatherDialogOpen = false;
        }
    }

    // ---- the sheets: every style side by side ----

    static readonly (string tag, string name)[] Styles =
    {
        ("", "Modern"), ("_cyber", "Cyber"), ("_95", "Windows 95"), ("_xp", "Windows XP"), ("_aero", "Windows 7"), ("_mac", "Mac"), ("_term", "Terminal"),
    };

    public static void Sheets(string dir, Painter painter, Gpu gpu)
    {
        Sheet(Path.Combine(dir, "weather_preview.png"), dir, new[] { "face", "face43", "open" });
        Sheet(Path.Combine(dir, "weather_states.png"), dir, new[] { "night", "rain", "stale_open", "none_open" });
        Glyphs(Path.Combine(dir, "qa", "weather_glyphs.png"), painter, gpu);
    }

    static void Sheet(string path, string dir, string[] cases)
    {
        const int Cw = 400, Ch = 275, Gap = 10, Label = 22, Side = 110;
        using var bmp = new System.Drawing.Bitmap(Side + cases.Length * (Cw + Gap) + Gap, Styles.Length * (Ch + Label + Gap) + Gap + Label);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.Clear(System.Drawing.Color.FromArgb(255, 38, 40, 46));
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        using var font = new System.Drawing.Font("Segoe UI", 10f);
        using var bold = new System.Drawing.Font("Segoe UI", 11f, System.Drawing.FontStyle.Bold);
        for (int j = 0; j < cases.Length; j++) g.DrawString(cases[j], font, System.Drawing.Brushes.Gainsboro, Side + Gap + j * (Cw + Gap), 4);
        int n = 0;
        for (int i = 0; i < Styles.Length; i++)
        {
            int y = Label + Gap + i * (Ch + Label + Gap);
            g.DrawString(Styles[i].name, bold, System.Drawing.Brushes.White, 8, y + Ch / 2 - 8);
            for (int j = 0; j < cases.Length; j++)
            {
                string f = Path.Combine(dir, $"wrist_weather_{cases[j]}{Styles[i].tag}.png");
                if (!File.Exists(f)) continue;
                using var src = new System.Drawing.Bitmap(f);
                g.DrawImage(src, new System.Drawing.Rectangle(Side + Gap + j * (Cw + Gap), y, Cw, Ch));
                n++;
            }
        }
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        Log.Info($"weather: {Path.GetFileName(path)} - {n} pictures of {Styles.Length * cases.Length}");
    }

    // every picture by day and by night: in colour, in a look's text colour, in Cyber's, and as the 95 / Mac pixel art at 32 and 16
    static void Glyphs(string path, Painter p, Gpu gpu)
    {
        const int S = 64, Pad = 12;
        using var surf = new Surface(gpu, 16 * (S + Pad) + Pad, 5 * (S + Pad) + Pad);
        p.Begin(surf.Bitmap, 0xFF1A1C22);
        var mono = WeatherGlyphs.Mono(0xFFF2F3F7, 0xFF9EA4B6);
        for (int k = 0; k < 8; k++)
            for (int n = 0; n < 2; n++)
            {
                int col = k * 2 + n;
                float x = Pad + col * (S + Pad);
                WeatherGlyphs.Draw(p, (WxKind)k, n == 1, new System.Drawing.RectangleF(x, Pad, S, S), WeatherGlyphs.Colour);
                WeatherGlyphs.Draw(p, (WxKind)k, n == 1, new System.Drawing.RectangleF(x, Pad + (S + Pad), S, S), mono);
                p.Fill(new System.Drawing.RectangleF(x - 4, Pad + 2 * (S + Pad) - 4, S + 8, S + 8), CyberHud.Card);
                WeatherGlyphs.Draw(p, (WxKind)k, n == 1, new System.Drawing.RectangleF(x, Pad + 2 * (S + Pad), S, S), WeatherGlyphs.Cyber);
                p.Fill(new System.Drawing.RectangleF(x - 4, Pad + 3 * (S + Pad) - 4, S + 8, 2 * (S + Pad)), 0xFFC0C0C0);
                WeatherGlyphs.Pixel(p, (WxKind)k, n == 1, x, Pad + 3 * (S + Pad), 32);
                WeatherGlyphs.Pixel(p, (WxKind)k, n == 1, x + 40, Pad + 3 * (S + Pad) + 16, 16);
                p.Fill(new System.Drawing.RectangleF(x, Pad + 4 * (S + Pad), 32, 32), 0xFFFFFFFF);
                WeatherGlyphs.Pixel(p, (WxKind)k, n == 1, x, Pad + 4 * (S + Pad), 32);
            }
        p.End();
        surf.SavePng(gpu, path);
    }
}

static partial class Tools
{
    // ORBIT_SNAP_ONLY=weather: the weather's data checks, its views' checks and pictures and its layout check (its sizes and opened only)
    // in every style, the sheets, in seconds
    internal static void WeatherAllocTypes(string what, Action draw) => AllocTypes(what, draw, null);

    static int WeatherOnly(WristPanel wrist, PanelData data, ConfigStore cfg, Painter painter, Gpu gpu, string dir)
    {
        int failed0 = WantsFailed, layout = 0;
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = SnapCulture;
        data.Weather = WeatherViewTools.Report();
        WeatherViewTools.DataChecks(wrist, data, cfg, dir);
        foreach (var (style, tag) in new[] { ("modern", ""), ("classic", "_95"), ("xp", "_xp"), ("aero", "_aero"), ("mac", "_mac"), ("terminal", "_term"), (CyberHud.Id, "_cyber") })
        {
            cfg.Data.Appearance = new AppearanceConfig { Style = style, Clock24h = true };
            Theme.Apply(cfg.Data.Appearance);
            WeatherViewTools.Run(wrist, data, cfg, gpu, dir, tag);
            string f0 = cfg.Data.Wrist.Face;
            layout += LayoutCheckRun(wrist, data, cfg, painter, gpu, dir, tag.Length > 0 ? tag[1..] : "modern", onlyKind: "weather");
            cfg.Data.Wrist.Face = f0;
            cfg.Data.Wrist.Widgets = null;
        }
        cfg.Data.Appearance = new AppearanceConfig { Clock24h = true };
        Theme.Apply(cfg.Data.Appearance);
        WeatherViewTools.Sheets(dir, painter, gpu);
        var host = new FakeHost { Cfg = cfg, GpuName = gpu.AdapterName, MonitorList = new List<MonitorInfo>(), Data = data, Wrist = wrist };
        using (var surf = new Surface(gpu, 1500, 960))
            WeatherViewTools.SettingsChecks(new SettingsView(host), new Ui(painter), surf, new PointerState(), painter, gpu, cfg, wrist, data, dir);
        CultureInfo.CurrentCulture = culture;
        Log.Info("snapshot (weather only) written to " + dir);
        return layout > 0 ? 1 : WantsExit(failed0);
    }
}
