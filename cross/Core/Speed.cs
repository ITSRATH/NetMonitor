// NetMonitor (macOS/Linux) – Speedtest: Cloudflare, Speedtest.net (Ookla CLI), offizielle Messstellen.
using System.Diagnostics;
using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NetMonitor;

public class SpeedResult
{
    public DateTime Time;
    public string Provider = "", Server = "", Url = "";
    public double Down = double.NaN, Up = double.NaN, Ping = double.NaN, Jitter = double.NaN, LoadedPing = double.NaN, Loss = double.NaN;
    public double DurationS;
    public long Bytes;
    public bool Suspicious;   // Leitung wurde nicht ausgelastet – Ergebnis vermutlich zu niedrig
    public string Conn = "";  // LAN / WLAN / MOBILE / VPN zum Zeitpunkt der Messung

    public string ConnText => Conn switch
    {
        "WLAN" => L.P("WLAN", "Wi-Fi"),
        "MOBILE" => L.P("Mobilfunk", "Mobile"),
        "" or "?" => "–",
        _ => Conn
    };

    static string N(double v) => double.IsNaN(v) ? "" : v.ToString("0.###", CultureInfo.InvariantCulture);
    static double P(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : double.NaN;

    public string ToLine() => string.Join(";", Time.Ticks.ToString(CultureInfo.InvariantCulture), Storage.Escape(Provider), Storage.Escape(Server),
        N(Down), N(Up), N(Ping), N(Jitter), N(LoadedPing), N(Loss), N(DurationS), Bytes.ToString(CultureInfo.InvariantCulture),
        Storage.Escape(Url), Suspicious ? "1" : "", Conn);

    public static SpeedResult Parse(string line)
    {
        var p = line.Split(';');
        if (p.Length < 12 || !long.TryParse(p[0], out long ticks)) return null;
        long.TryParse(p[10], out long bytes);
        return new SpeedResult
        {
            Time = new DateTime(ticks), Provider = p[1], Server = p[2], Down = P(p[3]), Up = P(p[4]), Ping = P(p[5]),
            Jitter = P(p[6]), LoadedPing = P(p[7]), Loss = P(p[8]), DurationS = P(p[9]), Bytes = bytes, Url = p[11],
            Suspicious = p.Length > 12 && p[12] == "1", Conn = p.Length > 13 ? p[13] : ""
        };
    }

    public static string Fmt(double v) => double.IsNaN(v) ? "–" : v.ToString(v >= 100 ? "0" : "0.0");
    public static string FmtMs(double v) => double.IsNaN(v) ? "–" : v.ToString("0") + " ms";
}

static class SpeedStore
{
    static string FilePath => Path.Combine(Storage.Home, "speedtests.csv");
    static readonly object sync = new();

    public static List<SpeedResult> Load()
    {
        var list = new List<SpeedResult>();
        lock (sync)
        {
            try
            {
                if (File.Exists(FilePath))
                    foreach (var line in File.ReadAllLines(FilePath, Storage.Utf8))
                        if (SpeedResult.Parse(line) is { } r) list.Add(r);
            }
            catch { }
        }
        list.Sort((a, b) => a.Time.CompareTo(b.Time));
        return list;
    }

    public static void Save(List<SpeedResult> list)
    {
        lock (sync)
        {
            Directory.CreateDirectory(Storage.Home);
            File.WriteAllLines(FilePath, list.Select(r => r.ToLine()), Storage.Utf8);
        }
    }

    public static void Append(SpeedResult r)
    {
        lock (sync)
        {
            Directory.CreateDirectory(Storage.Home);
            File.AppendAllLines(FilePath, new[] { r.ToLine() }, Storage.Utf8);
        }
    }

    // Zeiträume, in denen ein Speedtest die Leitung ausgelastet hat (werden in der Verlust-Analyse ignoriert).
    public static List<(DateTime Start, DateTime End)> Windows()
    {
        var w = Load().Where(r => r.DurationS > 0).Select(r => (r.Time.AddSeconds(-1), r.Time.AddSeconds(r.DurationS + 3))).ToList();
        if (SpeedState.Active) w.Add((SpeedState.Since.AddSeconds(-1), DateTime.Now.AddSeconds(3)));
        return w;
    }
}

// Läuft gerade ein Speedtest? (Der Ping-Monitor wertet Verluste in dieser Zeit nicht als Störung.)
static class SpeedState
{
    public static bool Active;
    public static DateTime Since, LastEnd = DateTime.MinValue;
    public static event Action Finished;

    public static void Begin() { Active = true; Since = DateTime.Now; }

    public static void End()
    {
        Active = false;
        LastEnd = DateTime.Now;
        Finished?.Invoke();
    }

    public static bool Disturbs(DateTime t) => Active || (t - LastEnd).TotalSeconds < 3;
}

// Fortschritt: Phase 0 = Latenz, 1 = Download, 2 = Upload; Wert (ms bzw. Mbit/s); Gesamtfortschritt 0..1
delegate void SpeedProgress(int phase, double value, double fraction, string server);

// Messung gegen speed.cloudflare.com (dieselben Endpunkte wie Cloudflares eigener Speedtest).
static class CloudflareTest
{
    const string Base = "https://speed.cloudflare.com";
    const int Seconds = 8;

    static readonly Dictionary<string, string> Colos = new()
    {
        ["FRA"] = "Frankfurt", ["MUC"] = "München", ["HAM"] = "Hamburg", ["DUS"] = "Düsseldorf", ["BER"] = "Berlin",
        ["STR"] = "Stuttgart", ["VIE"] = "Wien", ["ZRH"] = "Zürich", ["AMS"] = "Amsterdam", ["LHR"] = "London",
        ["CDG"] = "Paris", ["PRG"] = "Prag", ["WAW"] = "Warschau", ["CPH"] = "Kopenhagen"
    };

    static double Median(List<double> v)
    {
        if (v.Count == 0) return double.NaN;
        var s = v.OrderBy(x => x).ToList();
        return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
    }

    static double Jitter(List<double> v)
    {
        if (v.Count < 2) return double.NaN;
        double sum = 0;
        for (int i = 1; i < v.Count; i++) sum += Math.Abs(v[i] - v[i - 1]);
        return sum / (v.Count - 1);
    }

    public static async Task<SpeedResult> Run(CancellationToken ct, SpeedProgress report)
    {
        var result = new SpeedResult { Time = DateTime.Now, Provider = "Cloudflare" };
        var total = Stopwatch.StartNew();
        using var handler = new SocketsHttpHandler { MaxConnectionsPerServer = 32, PooledConnectionLifetime = TimeSpan.FromMinutes(2) };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.Add("User-Agent", "NetMonitor/" + AppInfo.Version);
        string colo = "";
        try
        {
            foreach (var line in (await http.GetStringAsync(Base + "/cdn-cgi/trace", ct)).Split('\n'))
                if (line.StartsWith("colo=")) colo = line.Substring(5).Trim();
        }
        catch { }
        result.Server = "Cloudflare " + (Colos.TryGetValue(colo, out var city) ? city + " (" + colo + ")" : colo);

        // Latenz im Leerlauf (erste Anfrage enthält den Verbindungsaufbau und zählt nicht)
        var lat = new List<double>();
        for (int i = 0; i < 13; i++)
        {
            ct.ThrowIfCancellationRequested();
            var t = Stopwatch.StartNew();
            using (await http.GetAsync(Base + "/__down?bytes=0", ct)) { }
            if (i > 0) lat.Add(t.Elapsed.TotalMilliseconds);
            report(0, Median(lat), 0.1 * (i + 1) / 13, result.Server);
        }
        result.Ping = Median(lat);
        result.Jitter = Jitter(lat);

        var loaded = new List<double>();
        long used = 0;
        result.Down = await Transfer(http, false, ct, loaded, (v, f) => report(1, v, 0.1 + 0.45 * f, result.Server), b => used += b);
        lock (loaded) result.LoadedPing = Median(loaded);
        result.Up = await Transfer(http, true, ct, null, (v, f) => report(2, v, 0.55 + 0.45 * f, result.Server), b => used += b);
        result.Bytes = used;
        result.DurationS = total.Elapsed.TotalSeconds;
        return result;
    }

    // Misst den Durchsatz über mehrere parallele Verbindungen; die erste Anlaufphase wird nicht gewertet.
    static async Task<double> Transfer(HttpClient http, bool upload, CancellationToken outer, List<double> loaded,
                                       Action<double, double> live, Action<long> usedBytes)
    {
        long bytes = 0;
        double lastDone = 0;
        var sync = new object();
        byte[] payload = null;
        if (upload) { payload = new byte[2 * 1024 * 1024]; Random.Shared.NextBytes(payload); }
        var sw = Stopwatch.StartNew();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(outer);
        var tasks = new List<Task>();
        for (int w = 0; w < 6; w++)
            tasks.Add(Task.Run(async () =>
            {
                var buf = new byte[65536];
                while (!cts.IsCancellationRequested)
                {
                    try
                    {
                        if (upload)
                        {
                            using var content = new ByteArrayContent(payload);
                            using (await http.PostAsync(Base + "/__up", content, cts.Token)) { }
                            lock (sync) { bytes += payload.Length; lastDone = sw.Elapsed.TotalSeconds; }
                        }
                        else
                        {
                            using var resp = await http.GetAsync(Base + "/__down?bytes=50000000", HttpCompletionOption.ResponseHeadersRead, cts.Token);
                            using var s = await resp.Content.ReadAsStreamAsync(cts.Token);
                            int n;
                            while ((n = await s.ReadAsync(buf, cts.Token)) > 0)
                                lock (sync) { bytes += n; lastDone = sw.Elapsed.TotalSeconds; }
                        }
                    }
                    catch
                    {
                        if (cts.IsCancellationRequested) break;
                        await Task.Delay(200);
                    }
                }
            }));
        var pinger = loaded == null ? Task.CompletedTask : Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    var t = Stopwatch.StartNew();
                    using (await http.GetAsync(Base + "/__down?bytes=0", cts.Token)) { }
                    if (sw.Elapsed.TotalSeconds > 1.5) lock (loaded) loaded.Add(t.Elapsed.TotalMilliseconds);
                    await Task.Delay(300, cts.Token);
                }
                catch { if (cts.IsCancellationRequested) break; }
            }
        });

        const double warmup = 1.5;
        double warmTime = -1; long warmBytes = 0;
        var window = new Queue<(double T, long B)>();
        while (sw.Elapsed.TotalSeconds < Seconds)
        {
            await Task.Delay(200, outer);
            double t = sw.Elapsed.TotalSeconds;
            long b;
            lock (sync) b = bytes;
            window.Enqueue((t, b));
            while (window.Count > 2 && t - window.Peek().T > 1.2) window.Dequeue();
            var first = window.Peek();
            double rate = t > first.T ? (b - first.B) * 8 / 1e6 / (t - first.T) : 0;
            if (warmTime < 0 && t >= warmup) { warmTime = t; warmBytes = b; }
            live(rate, t / Seconds);
        }
        cts.Cancel();
        long endBytes; double endTime;
        lock (sync) { endBytes = bytes; endTime = upload ? lastDone : sw.Elapsed.TotalSeconds; }
        try { await Task.WhenAll(tasks); } catch { }
        try { await pinger; } catch { }
        usedBytes(endBytes);
        if (warmTime < 0 || endTime <= warmTime) return endBytes * 8 / 1e6 / Math.Max(0.1, sw.Elapsed.TotalSeconds);
        return (endBytes - warmBytes) * 8 / 1e6 / (endTime - warmTime);
    }
}

// Speedtest.net über die offizielle Ookla Speedtest CLI.
static class OoklaTest
{
    public const string Terms = "https://www.speedtest.net/about/terms";
    public const string Privacy = "https://www.speedtest.net/about/privacy";
    public static int LastServer;

    // Offizielle Pakete von Ookla (Version 1.2.0), Prüfsummen beim Erstellen dieser Version festgehalten.
    static (string Url, string Sha256)? Package()
    {
        const string b = "https://install.speedtest.net/app/cli/ookla-speedtest-1.2.0-";
        if (OperatingSystem.IsMacOS()) return (b + "macosx-universal.tgz", "c9f8192149ebc88f8699998cecab1ce144144045907ece6f53cf50877f4de66f");
        if (OperatingSystem.IsLinux())
            return RuntimeInformation.OSArchitecture == Architecture.Arm64
                ? (b + "linux-aarch64.tgz", "3953d231da3783e2bf8904b6dd72767c5c6e533e163d3742fd0437affa431bd3")
                : (b + "linux-x86_64.tgz", "5690596c54ff9bed63fa3732f818a05dbc2db19ad36ed68f21ca5f64d5cfeeb7");
        return null;
    }

    public static string ManagedPath => Path.Combine(Storage.Home, "tools", "ookla", OperatingSystem.IsWindows() ? "speedtest.exe" : "speedtest");

    // Lädt die CLI herunter, prüft die Prüfsumme und entpackt sie in den NetMonitor-Datenordner.
    public static async Task<string> Download()
    {
        var pkg = Package() ?? throw new PlatformNotSupportedException();
        using var http = new HttpClient();
        byte[] data = await http.GetByteArrayAsync(pkg.Url);
        string hash = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        if (hash != pkg.Sha256)
            throw new Exception(L.P("Die Prüfsumme der heruntergeladenen Datei stimmt nicht – Download verworfen.",
                                    "The checksum of the downloaded file does not match – download discarded."));
        string dir = Path.GetDirectoryName(ManagedPath);
        Directory.CreateDirectory(dir);
        using (var gz = new GZipStream(new MemoryStream(data), CompressionMode.Decompress))
        using (var tar = new TarReader(gz))
        {
            TarEntry entry;
            while ((entry = tar.GetNextEntry()) != null)
            {
                string name = Path.GetFileName(entry.Name);
                if (entry.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile && (name == "speedtest" || name == "speedtest.md"))
                    entry.ExtractToFile(Path.Combine(dir, name), true);
            }
        }
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(ManagedPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                              UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        var settings = Storage.LoadSettings();
        settings["ookla.path"] = ManagedPath;
        settings["ookla.accepted"] = "1";
        Storage.SaveSettings(settings);
        return ManagedPath;
    }

    public static string Find(string configured)
    {
        if (!string.IsNullOrEmpty(configured) && File.Exists(configured)) return configured;
        if (File.Exists(ManagedPath)) return ManagedPath;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Concat(new[] { "/opt/homebrew/bin", "/usr/local/bin", "/usr/bin" }))
        {
            if (dir.Length == 0) continue;
            string c = Path.Combine(dir, "speedtest");
            // Achtung: das Python-Paket „speedtest-cli“ heißt ebenfalls „speedtest“ – nur die Ookla-Version unterstützt --format=jsonl
            if (File.Exists(c) && Sh.Run(c, "--version", 3000).Contains("Ookla")) return c;
        }
        return null;
    }

    public class Server
    {
        public int Id;
        public string Name = "";
        public override string ToString() => Name;
    }

    static double D(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : double.NaN;
    static string S(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : "";
    static JsonElement O(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) ? v : default;

    // Nächstgelegene Speedtest.net-Server (kostet keine nennenswerten Daten).
    public static List<Server> ListServers(string exe)
    {
        var list = new List<Server>();
        try
        {
            using var doc = JsonDocument.Parse(Sh.Run(exe, "-L --format=json --accept-license --accept-gdpr", 20000));
            foreach (var s in doc.RootElement.GetProperty("servers").EnumerateArray())
                list.Add(new Server { Id = (int)D(s, "id"), Name = (S(s, "name") + " · " + S(s, "location")).Trim() });
        }
        catch { }
        return list;
    }

    public static SpeedResult Run(string exe, CancellationToken ct, SpeedProgress report, int serverId)
    {
        var psi = new ProcessStartInfo(exe, "--format=jsonl --progress=yes --accept-license --accept-gdpr" + (serverId > 0 ? " --server-id=" + serverId : ""))
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        var total = Stopwatch.StartNew();
        using var p = Process.Start(psi);
        using var reg = ct.Register(() => { try { if (!p.HasExited) p.Kill(true); } catch { } });
        var errTask = p.StandardError.ReadToEndAsync();
        SpeedResult result = null;
        string server = "Speedtest.net", error = null;
        int used = 0;
        string line;
        while ((line = p.StandardOutput.ReadLine()) != null)
        {
            JsonDocument doc;
            try { doc = JsonDocument.Parse(line); } catch { continue; }
            using (doc)
            {
                var o = doc.RootElement;
                switch (S(o, "type"))
                {
                    case "testStart":
                        var srv = O(o, "server");
                        server = (S(srv, "name") + " " + S(srv, "location")).Trim();
                        used = (int)D(srv, "id");
                        report(0, double.NaN, 0.02, server);
                        break;
                    case "ping":
                        report(0, D(O(o, "ping"), "latency"), 0.1 * Math.Max(0, D(O(o, "ping"), "progress")), server);
                        break;
                    case "download":
                        report(1, D(O(o, "download"), "bandwidth") * 8 / 1e6, 0.1 + 0.45 * Math.Max(0, D(O(o, "download"), "progress")), server);
                        break;
                    case "upload":
                        report(2, D(O(o, "upload"), "bandwidth") * 8 / 1e6, 0.55 + 0.45 * Math.Max(0, D(O(o, "upload"), "progress")), server);
                        break;
                    case "log":
                        if (S(o, "level") == "error") error = S(o, "message");
                        break;
                    case "result":
                        var down = O(o, "download");
                        var up = O(o, "upload");
                        result = new SpeedResult
                        {
                            Provider = "Speedtest.net", Server = server,
                            Ping = D(O(o, "ping"), "latency"), Jitter = D(O(o, "ping"), "jitter"),
                            Down = D(down, "bandwidth") * 8 / 1e6, Up = D(up, "bandwidth") * 8 / 1e6,
                            LoadedPing = D(O(down, "latency"), "iqm"), Loss = D(o, "packetLoss"),
                            Bytes = (long)(Math.Max(0, D(down, "bytes")) + Math.Max(0, D(up, "bytes"))),
                            Url = S(O(o, "result"), "url")
                        };
                        break;
                }
            }
        }
        p.WaitForExit();
        string stderr = errTask.Result;
        ct.ThrowIfCancellationRequested();
        if (result == null)
            throw new Exception(error ?? (stderr.Trim().Length > 0 ? stderr.Trim() : L.P("Speedtest.net hat kein Ergebnis geliefert.", "Speedtest.net returned no result.")));
        result.Time = DateTime.Now.AddSeconds(-total.Elapsed.TotalSeconds);
        result.DurationS = total.Elapsed.TotalSeconds;
        LastServer = used;
        return result;
    }
}

// Offizielle Messungen der Regulierungsbehörden (nur Länder mit eigener Messung).
class OfficialTest
{
    public string Code = "", NameDe = "", NameEn = "", Regulator = "", Title = "", Web = "";
    public int Level; // 0 = rechtsverbindlich, 1 = offizieller Test der Behörde, 2 = keine offizielle Messung
    public string TextDe = "", TextEn = "";

    public string Name => L.P(NameDe, NameEn);
    public string Text => L.P(TextDe, TextEn);
    public override string ToString() => Name;

    public static readonly List<OfficialTest> All = new()
    {
        new OfficialTest
        {
            Code = "DE", NameDe = "Deutschland", NameEn = "Germany", Regulator = "Bundesnetzagentur", Title = "Breitbandmessung", Level = 0,
            Web = "https://breitbandmessung.de/test",
            TextDe = "Die einzige rechtsverbindliche Messung in Deutschland. Weicht deine Geschwindigkeit erheblich vom Vertrag ab, kannst du mit dem " +
                     "Messprotokoll der Desktop-App (30 Messungen an 3 Kalendertagen) mindern oder kündigen. Die Desktop-App gibt es für Windows, macOS und Linux.",
            TextEn = "The only legally binding measurement in Germany. If your speed deviates significantly from your contract, the protocol of the " +
                     "desktop app (30 measurements on 3 calendar days) entitles you to reduce payment or cancel. The desktop app is available for Windows, macOS and Linux."
        },
        new OfficialTest
        {
            Code = "AT", NameDe = "Österreich", NameEn = "Austria", Regulator = "RTR", Title = "RTR-Netztest", Level = 1, Web = "https://www.netztest.at/",
            TextDe = "Offizieller Test der österreichischen Regulierungsbehörde RTR. Misst neben der Geschwindigkeit auch Qualitätsmerkmale wie DNS, Ports " +
                     "und VoIP; eine zertifizierte Messung ist möglich. Die Ergebnisse werden als Open Data veröffentlicht.",
            TextEn = "Official test of the Austrian regulator RTR. Besides speed it checks quality parameters such as DNS, ports and VoIP; a certified " +
                     "measurement is available. Results are published as open data."
        },
        new OfficialTest
        {
            Code = "CH", NameDe = "Schweiz", NameEn = "Switzerland", Regulator = "BAKOM / OFCOM", Title = "networktest.ch", Level = 1, Web = "https://www.networktest.ch/",
            TextDe = "Nach der Fernmeldedienstverordnung müssen Schweizer Anbieter ihrer Kundschaft eine Qualitätsmessung ermöglichen. networktest.ch " +
                     "wird gemeinsam von Swisscom, Sunrise und Salt betrieben, das BAKOM hat die Einführung begleitet.",
            TextEn = "Under the Swiss telecommunications services ordinance, providers must let customers measure their connection quality. " +
                     "networktest.ch is run jointly by Swisscom, Sunrise and Salt; OFCOM accompanied its introduction."
        },
        new OfficialTest
        {
            Code = "LU", NameDe = "Luxemburg", NameEn = "Luxembourg", Regulator = "ILR", Title = "checkmynet.lu", Level = 1, Web = "https://checkmynet.lu/",
            TextDe = "Offizielles Messwerkzeug der Luxemburger Regulierungsbehörde ILR. Die Messserver stehen am nationalen Internetknoten LU-CIX; " +
                     "die Daten sind Open Data.",
            TextEn = "Official measurement tool of Luxembourg's regulator ILR. The test servers are located at the national internet exchange LU-CIX; " +
                     "the data is published as open data."
        },
        new OfficialTest
        {
            Code = "", NameDe = "Anderes Land", NameEn = "Other country", Level = 2,
            TextDe = "Für dieses Land gibt es keine eigene Messung der Regulierungsbehörde. Cloudflare oder Speedtest.net reichen völlig aus – " +
                     "trage deinen Tarif ein, dann dokumentiert NetMonitor Abweichungen für Gespräche mit deinem Anbieter.",
            TextEn = "There is no measurement run by the regulator for this country. Cloudflare or Speedtest.net are fully sufficient – " +
                     "enter your plan and NetMonitor documents any shortfall for discussions with your provider."
        }
    };

    public static OfficialTest ForCode(string code) => All.FirstOrDefault(t => t.Code == code) ?? All[^1];

    public static string DefaultCode()
    {
        try { return ForCode(RegionInfo.CurrentRegion.TwoLetterISORegionName).Code; } catch { return ""; }
    }
}

static class AppInfo
{
    public static string Version => typeof(AppInfo).Assembly.GetName().Version is { } v ? v.ToString(3) : "1.3.0";
}
