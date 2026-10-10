// NetMonitor – manueller Speedtest (Cloudflare, Speedtest.net/Ookla, Breitbandmessung) mit Tarifvergleich.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NetMonitor
{
    public class SpeedResult
    {
        public DateTime Time;
        public string Provider = "", Server = "", Url = "";
        public double Down = double.NaN, Up = double.NaN, Ping = double.NaN, Jitter = double.NaN, LoadedPing = double.NaN, Loss = double.NaN;
        public double DurationS;
        public long Bytes;
        // Leitung wurde während der Messung offensichtlich nicht ausgelastet – Ergebnis vermutlich zu niedrig.
        public bool Suspicious;
        public string Conn = ""; // LAN / WLAN / MOBILE / VPN zum Zeitpunkt der Messung

        public string ConnText
        {
            get { return Conn == "WLAN" ? L.P("WLAN", "Wi-Fi") : Conn == "MOBILE" ? L.P("Mobilfunk", "Mobile") : Conn == "?" || Conn == "" ? "–" : Conn; }
        }

        static string N(double v) { return double.IsNaN(v) ? "" : v.ToString("0.###", CultureInfo.InvariantCulture); }
        static double P(string s) { double v; return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : double.NaN; }

        public string ToLine()
        {
            return string.Join(";", new[]
            {
                Time.Ticks.ToString(CultureInfo.InvariantCulture), Storage.Escape(Provider), Storage.Escape(Server),
                N(Down), N(Up), N(Ping), N(Jitter), N(LoadedPing), N(Loss), N(DurationS), Bytes.ToString(CultureInfo.InvariantCulture), Storage.Escape(Url),
                Suspicious ? "1" : "", Conn
            });
        }

        public static SpeedResult Parse(string line)
        {
            var p = line.Split(';');
            long ticks;
            if (p.Length < 12 || !long.TryParse(p[0], out ticks)) return null;
            long bytes;
            long.TryParse(p[10], out bytes);
            return new SpeedResult
            {
                Time = new DateTime(ticks), Provider = p[1], Server = p[2], Down = P(p[3]), Up = P(p[4]), Ping = P(p[5]),
                Jitter = P(p[6]), LoadedPing = P(p[7]), Loss = P(p[8]), DurationS = P(p[9]), Bytes = bytes, Url = p[11],
                Suspicious = p.Length > 12 && p[12] == "1",
                Conn = p.Length > 13 ? p[13] : ""
            };
        }
    }

    // Speedtest-Ergebnisse in %APPDATA%\NetMonitor\speedtests.csv
    static class SpeedStore
    {
        static string FilePath { get { return Path.Combine(Storage.Home, "speedtests.csv"); } }
        static readonly object sync = new object();

        public static List<SpeedResult> Load()
        {
            var list = new List<SpeedResult>();
            lock (sync)
            {
                try
                {
                    if (File.Exists(FilePath))
                        foreach (var line in File.ReadAllLines(FilePath, Storage.Utf8))
                        {
                            var r = SpeedResult.Parse(line);
                            if (r != null) list.Add(r);
                        }
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
                var lines = new List<string>();
                foreach (var r in list) lines.Add(r.ToLine());
                File.WriteAllLines(FilePath, lines, Storage.Utf8);
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
        public static List<KeyValuePair<DateTime, DateTime>> Windows()
        {
            var w = new List<KeyValuePair<DateTime, DateTime>>();
            foreach (var r in Load())
                if (r.DurationS > 0) w.Add(new KeyValuePair<DateTime, DateTime>(r.Time.AddSeconds(-1), r.Time.AddSeconds(r.DurationS + 3)));
            if (SpeedState.Active) w.Add(new KeyValuePair<DateTime, DateTime>(SpeedState.Since.AddSeconds(-1), DateTime.Now.AddSeconds(3)));
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
            if (Finished != null) Finished();
        }

        public static bool Disturbs(DateTime t) { return Active || (t - LastEnd).TotalSeconds < 3; }
    }

    // Fortschritt: Phase 0 = Latenz, 1 = Download, 2 = Upload; Wert (ms bzw. Mbit/s); Gesamtfortschritt 0..1
    delegate void SpeedProgress(int phase, double value, double fraction, string server);

    // Messung gegen speed.cloudflare.com (dieselben Endpunkte wie Cloudflares eigener Speedtest).
    static class CloudflareTest
    {
        const string Base = "https://speed.cloudflare.com";
        const int Seconds = 8;

        class Counter { public long Bytes; public double LastDone; }

        static readonly Dictionary<string, string> Colos = new Dictionary<string, string>
        {
            { "FRA", "Frankfurt" }, { "MUC", "München" }, { "HAM", "Hamburg" }, { "DUS", "Düsseldorf" }, { "BER", "Berlin" },
            { "STR", "Stuttgart" }, { "VIE", "Wien" }, { "ZRH", "Zürich" }, { "AMS", "Amsterdam" }, { "LHR", "London" },
            { "CDG", "Paris" }, { "PRG", "Prag" }, { "WAW", "Warschau" }, { "CPH", "Kopenhagen" }
        };

        static double Median(List<double> v)
        {
            if (v.Count == 0) return double.NaN;
            var s = new List<double>(v);
            s.Sort();
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
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            ServicePointManager.DefaultConnectionLimit = Math.Max(ServicePointManager.DefaultConnectionLimit, 32);
            ServicePointManager.Expect100Continue = false;
            var result = new SpeedResult { Time = DateTime.Now, Provider = "Cloudflare" };
            var total = Stopwatch.StartNew();
            using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) })
            {
                http.DefaultRequestHeaders.Add("User-Agent", "NetMonitor/" + Program.Version);
                string colo = "";
                try
                {
                    foreach (var line in (await http.GetStringAsync(Base + "/cdn-cgi/trace")).Split('\n'))
                        if (line.StartsWith("colo=")) colo = line.Substring(5).Trim();
                }
                catch { }
                string city;
                result.Server = "Cloudflare " + (Colos.TryGetValue(colo, out city) ? city + " (" + colo + ")" : colo);

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
                result.LoadedPing = Median(loaded);
                result.Up = await Transfer(http, true, ct, null, (v, f) => report(2, v, 0.55 + 0.45 * f, result.Server), b => used += b);
                result.Bytes = used;
            }
            result.DurationS = total.Elapsed.TotalSeconds;
            return result;
        }

        // Misst den Durchsatz über mehrere parallele Verbindungen; die erste Anlaufphase wird nicht gewertet.
        static async Task<double> Transfer(HttpClient http, bool upload, CancellationToken outer, List<double> loaded,
                                           Action<double, double> live, Action<long> usedBytes)
        {
            var counter = new Counter();
            var sync = new object();
            int workers = upload ? 6 : 6;
            byte[] payload = null;
            if (upload) { payload = new byte[2 * 1024 * 1024]; new Random().NextBytes(payload); }
            var sw = Stopwatch.StartNew();
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(outer))
            {
                var tasks = new List<Task>();
                for (int w = 0; w < workers; w++)
                    tasks.Add(Task.Run(async () =>
                    {
                        var buf = new byte[65536];
                        while (!cts.IsCancellationRequested)
                        {
                            bool failed = false;
                            try
                            {
                                if (upload)
                                {
                                    using (var content = new ByteArrayContent(payload))
                                    using (await http.PostAsync(Base + "/__up", content, cts.Token)) { }
                                    lock (sync) { counter.Bytes += payload.Length; counter.LastDone = sw.Elapsed.TotalSeconds; }
                                }
                                else
                                {
                                    using (var resp = await http.GetAsync(Base + "/__down?bytes=50000000", HttpCompletionOption.ResponseHeadersRead, cts.Token))
                                    using (var s = await resp.Content.ReadAsStreamAsync())
                                    {
                                        int n;
                                        while ((n = await s.ReadAsync(buf, 0, buf.Length, cts.Token)) > 0)
                                            lock (sync) { counter.Bytes += n; counter.LastDone = sw.Elapsed.TotalSeconds; }
                                    }
                                }
                            }
                            catch { failed = true; }
                            if (cts.IsCancellationRequested) break;
                            if (failed) await Task.Delay(200); // kurzer Abstand nach einem Verbindungsfehler
                        }
                    }));
                Task pinger = Task.FromResult(0);
                if (loaded != null)
                    pinger = Task.Run(async () =>
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
                var window = new Queue<KeyValuePair<double, long>>();
                while (sw.Elapsed.TotalSeconds < Seconds)
                {
                    await Task.Delay(200, outer);
                    double t = sw.Elapsed.TotalSeconds;
                    long b;
                    lock (sync) b = counter.Bytes;
                    window.Enqueue(new KeyValuePair<double, long>(t, b));
                    while (window.Count > 2 && t - window.Peek().Key > 1.2) window.Dequeue();
                    var first = window.Peek();
                    double rate = t > first.Key ? (b - first.Value) * 8 / 1e6 / (t - first.Key) : 0;
                    if (warmTime < 0 && t >= warmup) { warmTime = t; warmBytes = b; }
                    live(rate, t / Seconds);
                }
                cts.Cancel();
                long endBytes; double endTime;
                lock (sync) { endBytes = counter.Bytes; endTime = upload ? counter.LastDone : sw.Elapsed.TotalSeconds; }
                try { await Task.WhenAll(tasks); } catch { }
                try { await pinger; } catch { }
                usedBytes(endBytes);
                if (warmTime < 0 || endTime <= warmTime) return endBytes * 8 / 1e6 / Math.Max(0.1, sw.Elapsed.TotalSeconds);
                return (endBytes - warmBytes) * 8 / 1e6 / (endTime - warmTime);
            }
        }
    }

    // Speedtest.net über die offizielle Ookla Speedtest CLI (muss separat installiert werden).
    static class OoklaTest
    {
        public const string DownloadPage = "https://www.speedtest.net/apps/cli";
        public const string Terms = "https://www.speedtest.net/about/terms";
        public const string Privacy = "https://www.speedtest.net/about/privacy";

        // Offizielles Paket von Ookla (Version und Prüfsumme wie im winget-Verzeichnis „Ookla.Speedtest.CLI“)
        const string ZipUrl = "https://install.speedtest.net/app/cli/ookla-speedtest-1.2.0-win64.zip";
        const string ZipSha256 = "13e3d888b845d301a556419e31f14ab9bff57e3f06089ef2fd3bdc9ba6841efa";

        public static string ManagedPath { get { return Path.Combine(Storage.Home, "tools", "ookla", "speedtest.exe"); } }

        // Lädt die CLI herunter, prüft die Prüfsumme und entpackt sie in den NetMonitor-Datenordner.
        public static string Download()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            byte[] zip;
            using (var web = new WebClient()) zip = web.DownloadData(ZipUrl);
            string hash;
            using (var sha = System.Security.Cryptography.SHA256.Create())
                hash = BitConverter.ToString(sha.ComputeHash(zip)).Replace("-", "").ToLowerInvariant();
            if (hash != ZipSha256)
                throw new Exception(L.P("Die Prüfsumme der heruntergeladenen Datei stimmt nicht – Download verworfen.",
                                        "The checksum of the downloaded file does not match – download discarded."));
            string dir = Path.GetDirectoryName(ManagedPath);
            Directory.CreateDirectory(dir);
            using (var ms = new MemoryStream(zip))
            using (var archive = new System.IO.Compression.ZipArchive(ms))
                foreach (var entry in archive.Entries)
                {
                    if (entry.Name != "speedtest.exe" && entry.Name != "speedtest.md") continue;
                    using (var src = entry.Open())
                    using (var dst = File.Create(Path.Combine(dir, entry.Name))) src.CopyTo(dst);
                }
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
            var candidates = new List<string>();
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
                if (dir.Trim().Length > 0) candidates.Add(Path.Combine(dir.Trim(), "speedtest.exe"));
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            candidates.Add(Path.Combine(local, @"Microsoft\WinGet\Links\speedtest.exe"));
            try
            {
                string pkgs = Path.Combine(local, @"Microsoft\WinGet\Packages");
                if (Directory.Exists(pkgs))
                    foreach (var d in Directory.GetDirectories(pkgs, "Ookla.Speedtest.CLI*")) candidates.Add(Path.Combine(d, "speedtest.exe"));
            }
            catch { }
            foreach (var c in candidates)
                try { if (File.Exists(c)) return c; } catch { }
            return null;
        }

        static double D(Dictionary<string, object> o, string key)
        {
            object v;
            return o != null && o.TryGetValue(key, out v) && v != null ? Convert.ToDouble(v, CultureInfo.InvariantCulture) : double.NaN;
        }

        static Dictionary<string, object> Sub(Dictionary<string, object> o, string key)
        {
            object v;
            return o != null && o.TryGetValue(key, out v) ? v as Dictionary<string, object> : null;
        }

        static string S(Dictionary<string, object> o, string key)
        {
            object v;
            return o != null && o.TryGetValue(key, out v) && v != null ? v.ToString() : "";
        }

        // Id des zuletzt verwendeten Servers (für die automatische Wiederholung mit einem anderen Server)
        [ThreadStatic] public static int usedServer;
        public static int LastServer;

        public class Server
        {
            public int Id;
            public string Name;
            public override string ToString() { return Name; }
        }

        // Nächstgelegene Speedtest.net-Server (kostet keine nennenswerten Daten).
        public static List<Server> ListServers(string exe)
        {
            var list = new List<Server>();
            var psi = new ProcessStartInfo(exe, "-L --format=json --accept-license --accept-gdpr")
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8
            };
            using (var p = Process.Start(psi))
            {
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(20000);
                var o = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string, object>>(output);
                object servers;
                if (o != null && o.TryGetValue("servers", out servers) && servers is System.Collections.IEnumerable)
                    foreach (var item in (System.Collections.IEnumerable)servers)
                    {
                        var s = item as Dictionary<string, object>;
                        if (s == null) continue;
                        list.Add(new Server { Id = (int)D(s, "id"), Name = (S(s, "name") + " · " + S(s, "location")).Trim() });
                    }
            }
            return list;
        }

        // Läuft auf einem Hintergrund-Thread (Task.Run) und liest die Ausgabe der CLI synchron, damit sie nie warten muss.
        public static SpeedResult Run(string exe, CancellationToken ct, SpeedProgress report, int serverId)
        {
            var psi = new ProcessStartInfo(exe, "--format=jsonl --progress=yes --accept-license --accept-gdpr" + (serverId > 0 ? " --server-id=" + serverId : ""))
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            var total = Stopwatch.StartNew();
            using (var p = Process.Start(psi))
            using (ct.Register(() => { try { if (!p.HasExited) p.Kill(); } catch { } }))
            {
                // Ohne sichtbares Fenster würde Windows 11 die CLI drosseln (EcoQoS) – das verfälscht den Durchsatz.
                PowerThrottling.Disable(p.Handle);
                try { p.PriorityClass = ProcessPriorityClass.AboveNormal; } catch { }
                var errTask = p.StandardError.ReadToEndAsync();
                var json = new System.Web.Script.Serialization.JavaScriptSerializer();
                SpeedResult result = null;
                string server = "Speedtest.net", error = null;
                usedServer = 0;
                string line;
                while ((line = p.StandardOutput.ReadLine()) != null)
                {
                    Dictionary<string, object> o;
                    try { o = json.Deserialize<Dictionary<string, object>>(line); } catch { continue; }
                    if (o == null) continue;
                    switch (S(o, "type"))
                    {
                        case "testStart":
                            var srv = Sub(o, "server");
                            server = (S(srv, "name") + " " + S(srv, "location")).Trim();
                            usedServer = (int)D(srv, "id");
                            report(0, double.NaN, 0.02, server);
                            break;
                        case "ping":
                            var pg = Sub(o, "ping");
                            report(0, D(pg, "latency"), 0.1 * Math.Max(0, D(pg, "progress")), server);
                            break;
                        case "download":
                            var dl = Sub(o, "download");
                            report(1, D(dl, "bandwidth") * 8 / 1e6, 0.1 + 0.45 * Math.Max(0, D(dl, "progress")), server);
                            break;
                        case "upload":
                            var ul = Sub(o, "upload");
                            report(2, D(ul, "bandwidth") * 8 / 1e6, 0.55 + 0.45 * Math.Max(0, D(ul, "progress")), server);
                            break;
                        case "log":
                            if (S(o, "level") == "error") error = S(o, "message");
                            break;
                        case "result":
                            var ping = Sub(o, "ping");
                            var down = Sub(o, "download");
                            var up = Sub(o, "upload");
                            var latency = Sub(down, "latency");
                            result = new SpeedResult
                            {
                                Time = DateTime.Now, Provider = "Speedtest.net", Server = server,
                                Ping = D(ping, "latency"), Jitter = D(ping, "jitter"),
                                Down = D(down, "bandwidth") * 8 / 1e6, Up = D(up, "bandwidth") * 8 / 1e6,
                                LoadedPing = D(latency, "iqm"), Loss = D(o, "packetLoss"),
                                Bytes = (long)(Math.Max(0, D(down, "bytes")) + Math.Max(0, D(up, "bytes"))),
                                Url = S(Sub(o, "result"), "url")
                            };
                            break;
                    }
                }
                p.WaitForExit();
                string stderr = errTask.Result;
                ct.ThrowIfCancellationRequested();
                if (result == null)
                    throw new Exception(error ?? (stderr.Trim().Length > 0 ? stderr.Trim() : L.P("Speedtest.net hat kein Ergebnis geliefert.", "Speedtest.net returned no result.")));
                result.Time = DateTime.Now.AddSeconds(-total.Elapsed.TotalSeconds);
                result.DurationS = total.Elapsed.TotalSeconds;
                LastServer = usedServer;
                return result;
            }
        }
    }

    // Schaltet die Energiespar-Drosselung (EcoQoS / Power Throttling) von Windows für einen Prozess ab.
    static class PowerThrottling
    {
        [StructLayout(LayoutKind.Sequential)]
        struct State { public uint Version, ControlMask, StateMask; }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetProcessInformation(IntPtr process, int infoClass, ref State info, int size);
        [DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentProcess();

        public static void Disable(IntPtr process)
        {
            try
            {
                // ProcessPowerThrottling = 4; EXECUTION_SPEED (1) | IGNORE_TIMER_RESOLUTION (4) kontrollieren, aber nicht aktivieren
                var s = new State { Version = 1, ControlMask = 1 | 4, StateMask = 0 };
                SetProcessInformation(process, 4, ref s, Marshal.SizeOf(typeof(State)));
            }
            catch { }
        }

        public static void DisableForSelf() { Disable(GetCurrentProcess()); }
    }

    // Offizielle bzw. von der Regulierungsbehörde getragene Messungen je Land (deutsch- und englischsprachiger Raum).
    class OfficialTest
    {
        public string Code, NameDe, NameEn, Regulator, Title, Web, App;
        public int Level; // 0 = rechtsverbindlich, 1 = offizieller Test der Behörde, 2 = kein offizieller Desktop-/Browser-Test
        public string TextDe, TextEn;

        public string Name { get { return L.P(NameDe, NameEn); } }
        public string Text { get { return L.P(TextDe, TextEn); } }
        public override string ToString() { return Name; }

        public static readonly List<OfficialTest> All = new List<OfficialTest>
        {
            new OfficialTest
            {
                Code = "DE", NameDe = "Deutschland", NameEn = "Germany", Regulator = "Bundesnetzagentur", Title = "Breitbandmessung", Level = 0,
                Web = "https://breitbandmessung.de/test", App = "https://breitbandmessung.de/desktop-app",
                TextDe = "Die einzige rechtsverbindliche Messung in Deutschland. Weicht deine Geschwindigkeit erheblich vom Vertrag ab, kannst du mit dem " +
                         "Messprotokoll der Desktop-App (30 Messungen an 3 Kalendertagen) mindern oder kündigen.",
                TextEn = "The only legally binding measurement in Germany. If your speed deviates significantly from your contract, the protocol of the " +
                         "desktop app (30 measurements on 3 calendar days) entitles you to reduce payment or cancel."
            },
            new OfficialTest
            {
                Code = "AT", NameDe = "Österreich", NameEn = "Austria", Regulator = "RTR", Title = "RTR-Netztest", Level = 1,
                Web = "https://www.netztest.at/",
                TextDe = "Offizieller Test der österreichischen Regulierungsbehörde RTR. Misst neben der Geschwindigkeit auch Qualitätsmerkmale wie DNS, Ports " +
                         "und VoIP; eine zertifizierte Messung ist möglich. Die Ergebnisse werden als Open Data veröffentlicht.",
                TextEn = "Official test of the Austrian regulator RTR. Besides speed it checks quality parameters such as DNS, ports and VoIP; a certified " +
                         "measurement is available. Results are published as open data."
            },
            new OfficialTest
            {
                Code = "CH", NameDe = "Schweiz", NameEn = "Switzerland", Regulator = "BAKOM / OFCOM", Title = "networktest.ch", Level = 1,
                Web = "https://www.networktest.ch/",
                TextDe = "Nach der Fernmeldedienstverordnung müssen Schweizer Anbieter ihrer Kundschaft eine Qualitätsmessung ermöglichen. networktest.ch " +
                         "wird gemeinsam von Swisscom, Sunrise und Salt betrieben, das BAKOM hat die Einführung begleitet.",
                TextEn = "Under the Swiss telecommunications services ordinance, providers must let customers measure their connection quality. " +
                         "networktest.ch is run jointly by Swisscom, Sunrise and Salt; OFCOM accompanied its introduction."
            },
            new OfficialTest
            {
                Code = "LU", NameDe = "Luxemburg", NameEn = "Luxembourg", Regulator = "ILR", Title = "checkmynet.lu", Level = 1,
                Web = "https://checkmynet.lu/",
                TextDe = "Offizielles Messwerkzeug der Luxemburger Regulierungsbehörde ILR. Die Messserver stehen am nationalen Internetknoten LU-CIX; " +
                         "die Daten sind Open Data.",
                TextEn = "Official measurement tool of Luxembourg's regulator ILR. The test servers are located at the national internet exchange LU-CIX; " +
                         "the data is published as open data."
            },
            new OfficialTest
            {
                Code = "", NameDe = "Anderes Land", NameEn = "Other country", Regulator = "", Title = "", Level = 2, Web = "",
                TextDe = "Für dieses Land gibt es keine eigene Messung der Regulierungsbehörde. Cloudflare oder Speedtest.net reichen völlig aus – " +
                         "trage deinen Tarif ein, dann dokumentiert NetMonitor Abweichungen für Gespräche mit deinem Anbieter.",
                TextEn = "There is no measurement run by the regulator for this country. Cloudflare or Speedtest.net are fully sufficient – " +
                         "enter your plan and NetMonitor documents any shortfall for discussions with your provider."
            }
        };

        public static OfficialTest ForCode(string code)
        {
            foreach (var t in All) if (t.Code == code) return t;
            return All[All.Count - 1];
        }

        public static string DefaultCode()
        {
            try { return ForCode(RegionInfo.CurrentRegion.TwoLetterISORegionName).Code; } catch { return ""; }
        }
    }

    // Offizielle Breitbandmessung der Bundesnetzagentur (läuft nur in deren App / im Browser).
    static class Breitband
    {
        public const string BrowserTest = "https://breitbandmessung.de/test";
        public const string DesktopApp = "https://breitbandmessung.de/desktop-app";

        public static string FindDesktopApp()
        {
            var roots = new[]
            {
                Tuple.Create(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
                Tuple.Create(Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
                Tuple.Create(Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall")
            };
            foreach (var root in roots)
            {
                try
                {
                    using (var key = root.Item1.OpenSubKey(root.Item2))
                    {
                        if (key == null) continue;
                        foreach (var name in key.GetSubKeyNames())
                            using (var sub = key.OpenSubKey(name))
                            {
                                var display = sub == null ? null : sub.GetValue("DisplayName") as string;
                                if (display == null || display.IndexOf("Breitbandmessung", StringComparison.OrdinalIgnoreCase) < 0) continue;
                                var icon = (sub.GetValue("DisplayIcon") as string ?? "").Split(',')[0].Trim('"');
                                if (icon.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(icon)) return icon;
                                var dir = sub.GetValue("InstallLocation") as string;
                                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                                    foreach (var exe in Directory.GetFiles(dir, "*.exe"))
                                        if (Path.GetFileName(exe).IndexOf("Breitband", StringComparison.OrdinalIgnoreCase) >= 0) return exe;
                            }
                    }
                }
                catch { }
            }
            return null;
        }
    }

    // ---------- Anzeige ----------

    // Tachometer für Download / Upload mit Tarif-Markierung.
    class Gauge : Control
    {
        public string Title = "";
        public Color Color = Theme.Cyan;
        public double Tariff;
        public string Sub = "";
        double target = double.NaN, shown;
        readonly System.Windows.Forms.Timer anim = new System.Windows.Forms.Timer { Interval = 30 };

        public Gauge()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
            anim.Tick += delegate
            {
                double t = double.IsNaN(target) ? 0 : target;
                shown += (t - shown) * 0.22;
                if (Math.Abs(t - shown) < 0.05) { shown = t; anim.Stop(); }
                Invalidate();
            };
        }

        public double Value
        {
            get { return target; }
            set { target = value; anim.Start(); }
        }

        protected override void Dispose(bool disposing) { if (disposing) anim.Dispose(); base.Dispose(disposing); }

        double Scale()
        {
            double m = Math.Max(50, Math.Max(Tariff * 1.15, (double.IsNaN(target) ? 0 : target) * 1.15));
            double[] steps = { 50, 100, 150, 200, 250, 300, 400, 500, 750, 1000, 1250, 1500, 2000, 2500, 5000, 10000 };
            foreach (var s in steps) if (m <= s) return s;
            return Math.Ceiling(m / 1000) * 1000;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int size = Math.Min(Width - Theme.S(20), (int)((Height - Theme.S(10)) * 1.25));
            if (size < 60) return;
            float thick = Theme.S(16);
            var rect = new RectangleF((Width - size) / 2f + thick / 2, Theme.S(10) + thick / 2, size - thick, size - thick);
            const float start = 150, sweep = 240;
            double max = Scale();
            using (var pen = new Pen(Theme.Surface2, thick) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawArc(pen, rect, start, sweep);
            double v = Math.Max(0, Math.Min(max, shown));
            if (v > 0.01)
                using (var brush = new LinearGradientBrush(Rectangle.Round(RectangleF.Inflate(rect, thick, thick)), Theme.Mix(Color, Theme.Accent, 0.4f), Color, 0f))
                using (var pen = new Pen(brush, thick) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(pen, rect, start, (float)(sweep * v / max));

            float cx = rect.X + rect.Width / 2, cy = rect.Y + rect.Height / 2, r = rect.Width / 2;
            for (int i = 0; i <= 4; i++)
            {
                double a = (start + sweep * i / 4.0) * Math.PI / 180;
                float tx = cx + (float)Math.Cos(a) * (r - thick - Theme.S(10)), ty = cy + (float)Math.Sin(a) * (r - thick - Theme.S(10));
                string label = (max * i / 4).ToString("0");
                var sz = Theme.Measure(label, Theme.F(7.5f));
                Theme.DrawText(g, label, Theme.F(7.5f), Theme.Faint, (int)(tx - sz.Width / 2f), (int)(ty - sz.Height / 2f));
            }
            if (Tariff > 0 && Tariff <= max)
            {
                double a = (start + sweep * Tariff / max) * Math.PI / 180;
                using (var pen = new Pen(Color.White, Theme.S(2.5f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(pen, cx + (float)Math.Cos(a) * (r - thick / 2 - Theme.S(4)), cy + (float)Math.Sin(a) * (r - thick / 2 - Theme.S(4)),
                                    cx + (float)Math.Cos(a) * (r + thick / 2 + Theme.S(4)), cy + (float)Math.Sin(a) * (r + thick / 2 + Theme.S(4)));
                float lx = cx + (float)Math.Cos(a) * (r + thick / 2 + Theme.S(14)), ly = cy + (float)Math.Sin(a) * (r + thick / 2 + Theme.S(14));
                Theme.DrawText(g, L.P("Tarif", "plan"), Theme.F(7.5f, FontStyle.Bold), Theme.Muted, (int)lx - Theme.S(12), (int)ly - Theme.S(7));
            }

            Theme.DrawText(g, Title, Theme.F(9.5f, FontStyle.Bold), Color, new Rectangle(0, (int)(cy - Theme.S(52)), Width, Theme.S(20)), TextFormatFlags.HorizontalCenter);
            string val = double.IsNaN(target) ? "–" : shown.ToString(shown >= 100 ? "0" : "0.0");
            Theme.DrawText(g, val, Theme.D(30f), Theme.Text, new Rectangle(0, (int)(cy - Theme.S(32)), Width, Theme.S(54)), TextFormatFlags.HorizontalCenter);
            Theme.DrawText(g, "Mbit/s", Theme.F(9f), Theme.Muted, new Rectangle(0, (int)(cy + Theme.S(22)), Width, Theme.S(18)), TextFormatFlags.HorizontalCenter);
            Theme.DrawText(g, Sub, Theme.F(9f, FontStyle.Bold), SubColor, new Rectangle(0, (int)(cy + Theme.S(44)), Width, Theme.S(18)), TextFormatFlags.HorizontalCenter);
        }

        public Color SubColor = Theme.Muted;
    }

    // Balkendiagramm der letzten Speedtests (Download/Upload) mit Tarif-Linien.
    class SpeedHistory : Control
    {
        public List<SpeedResult> Items = new List<SpeedResult>();
        public double TariffDown, TariffUp;
        Point mouse;
        bool hover;
        readonly List<KeyValuePair<RectangleF, SpeedResult>> hits = new List<KeyValuePair<RectangleF, SpeedResult>>();

        public SpeedHistory()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
        }

        protected override void OnMouseMove(MouseEventArgs e) { mouse = e.Location; hover = true; Invalidate(); base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            hits.Clear();
            var list = new List<SpeedResult>();
            foreach (var r in Items) if (!double.IsNaN(r.Down) || !double.IsNaN(r.Up)) list.Add(r);
            if (list.Count > 24) list.RemoveRange(0, list.Count - 24);
            var plot = new Rectangle(Theme.S(52), Theme.S(26), Width - Theme.S(66), Height - Theme.S(56));
            if (plot.Width < 50 || plot.Height < 30) return;
            if (list.Count == 0)
            {
                Theme.DrawText(g, L.P("Noch keine Speedtests – oben „Test starten“ klicken", "No speed tests yet – click “Start test” above"),
                    Theme.F(10f), Theme.Faint, plot, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            double max = Math.Max(TariffDown, TariffUp);
            foreach (var r in list) { if (!double.IsNaN(r.Down)) max = Math.Max(max, r.Down); if (!double.IsNaN(r.Up)) max = Math.Max(max, r.Up); }
            double[] steps = { 50, 100, 150, 200, 250, 300, 400, 500, 750, 1000, 1500, 2000, 2500, 5000, 10000 };
            double ymax = steps[steps.Length - 1];
            foreach (var s in steps) if (max * 1.1 <= s) { ymax = s; break; }
            using (var pen = new Pen(Theme.Grid) { DashStyle = DashStyle.Dot })
                for (int k = 0; k <= 4; k++)
                {
                    int y = plot.Bottom - k * plot.Height / 4;
                    g.DrawLine(pen, plot.Left, y, plot.Right, y);
                    Theme.DrawText(g, (ymax * k / 4).ToString("0"), Theme.F(8f), Theme.Faint, new Rectangle(0, y - Theme.S(8), plot.Left - Theme.S(8), Theme.S(16)), TextFormatFlags.Right);
                }
            Theme.DrawText(g, "Mbit/s", Theme.F(7.5f), Theme.Faint, 0, 0);
            float slot = (float)plot.Width / list.Count;
            float bw = Math.Max(Theme.S(4), Math.Min(Theme.S(22), slot * 0.32f));
            for (int i = 0; i < list.Count; i++)
            {
                var r = list[i];
                float x = plot.Left + i * slot + slot / 2;
                var area = new RectangleF(plot.Left + i * slot, plot.Top, slot, plot.Height);
                if (hover && area.Contains(mouse)) Theme.FillRound(g, Theme.A(Theme.Text, 12), area, Theme.S(6));
                if (!double.IsNaN(r.Down))
                {
                    float h = (float)(r.Down / ymax * plot.Height);
                    Theme.FillRound(g, r.Suspicious ? Theme.A(Theme.Cyan, 70) : Theme.Cyan, new RectangleF(x - bw - 1, plot.Bottom - h, bw, h), Math.Min(bw / 2, Theme.S(4)));
                    if (r.Suspicious)
                        Theme.DrawText(g, "⚠", Theme.F(8f, FontStyle.Bold), Theme.Warn, (int)(x - bw), (int)(plot.Bottom - h) - Theme.S(16));
                }
                if (!double.IsNaN(r.Up))
                {
                    float h = (float)(r.Up / ymax * plot.Height);
                    Theme.FillRound(g, r.Suspicious ? Theme.A(Theme.Violet, 70) : Theme.Violet, new RectangleF(x + 1, plot.Bottom - h, bw, h), Math.Min(bw / 2, Theme.S(4)));
                }
                if (list.Count <= 12 || i % Math.Max(1, list.Count / 8) == 0)
                    Theme.DrawText(g, r.Time.ToString(list.Count <= 6 ? L.DateTimeShort : "dd.MM."), Theme.F(7.5f), Theme.Faint,
                        new Rectangle((int)(x - slot / 2 - Theme.S(20)), plot.Bottom + Theme.S(6), (int)slot + Theme.S(40), Theme.S(16)), TextFormatFlags.HorizontalCenter);
                hits.Add(new KeyValuePair<RectangleF, SpeedResult>(area, r));
            }
            Action<double, Color, string> tariffLine = (v, c, label) =>
            {
                if (v <= 0) return;
                float y = (float)(plot.Bottom - v / ymax * plot.Height);
                using (var pen = new Pen(Theme.A(c, 200), Theme.S(1.5f)) { DashStyle = DashStyle.Dash }) g.DrawLine(pen, plot.Left, y, plot.Right, y);
                Theme.DrawText(g, label, Theme.F(7.5f, FontStyle.Bold), c, new Rectangle(plot.Left, (int)y - Theme.S(16), plot.Width - Theme.S(4), Theme.S(14)), TextFormatFlags.Right);
            };
            tariffLine(TariffDown, Theme.Cyan, L.P("Tarif Download ", "Plan download ") + TariffDown.ToString("0"));
            tariffLine(TariffUp, Theme.Violet, L.P("Tarif Upload ", "Plan upload ") + TariffUp.ToString("0"));

            if (!hover) return;
            foreach (var kv in hits)
            {
                if (!kv.Key.Contains(mouse)) continue;
                var r = kv.Value;
                var lines = new[]
                {
                    r.Time.ToString(L.Date + "  HH:mm") + "  ·  " + r.Provider,
                    "↓ " + Fmt(r.Down) + "   ↑ " + Fmt(r.Up) + "  Mbit/s",
                    "Ping " + FmtMs(r.Ping) + L.P("   ·   unter Last ", "   ·   loaded ") + FmtMs(r.LoadedPing)
                };
                int tw = 0;
                foreach (var l in lines) tw = Math.Max(tw, Theme.Measure(l, Theme.F(9f, FontStyle.Bold)).Width);
                var box = new Rectangle(Math.Min(mouse.X + Theme.S(14), Width - tw - Theme.S(30)), Theme.S(8), tw + Theme.S(24), Theme.S(72));
                Theme.FillRound(g, Theme.A(Theme.Bg, 240), box, Theme.S(10));
                using (var path = Theme.Round(new RectangleF(box.X + 0.5f, box.Y + 0.5f, box.Width - 1, box.Height - 1), Theme.S(10)))
                using (var pen = new Pen(Theme.Border)) g.DrawPath(pen, path);
                for (int i = 0; i < lines.Length; i++)
                    Theme.DrawText(g, lines[i], Theme.F(9f, i == 0 ? FontStyle.Bold : FontStyle.Regular), i == 0 ? Theme.Text : Theme.Muted,
                        box.X + Theme.S(12), box.Y + Theme.S(9) + i * Theme.S(20));
            }
        }

        public static string Fmt(double v) { return double.IsNaN(v) ? "–" : v.ToString(v >= 100 ? "0" : "0.0"); }
        public static string FmtMs(double v) { return double.IsNaN(v) ? "–" : v.ToString("0") + " ms"; }
    }

    // Hinweisleiste zur Verbindungsart (LAN / WLAN …) im Speedtest-Fenster.
    class ConnBanner : Surface
    {
        string glyph = Icons.Globe, message = "";
        Color tint = Theme.Muted;

        public ConnBanner() { Radius = 12; }

        public void Set(string glyph, Color tint, string message)
        {
            this.glyph = glyph; this.tint = tint; this.message = message;
            Fill = Theme.Mix(Theme.Surface, tint, 0.10f);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            int ic = Theme.S(28);
            var circle = new Rectangle(Theme.S(12), (Height - ic) / 2, ic, ic);
            Theme.FillRound(g, Theme.A(tint, 50), circle, ic / 2f);
            Theme.DrawText(g, glyph, Theme.Icon(10.5f), tint, circle, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            Theme.DrawText(g, message, Theme.F(9f), Theme.Text, new Rectangle(Theme.S(52), Theme.S(2), Width - Theme.S(64), Height - Theme.S(4)),
                TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        }
    }

    public class SpeedtestForm : Form
    {
        ConnBanner connBanner;
        ConnInfo conn;
        readonly System.Windows.Forms.Timer connTimer = new System.Windows.Forms.Timer { Interval = 15000 };

        async void RefreshConnection()
        {
            ConnInfo c;
            try { c = await Task.Run(() => NetInfo.Current()); } catch { return; }
            if (IsDisposed) return;
            conn = c;
            UpdateConnBanner();
        }

        string ConnKind() { return conn != null ? conn.Kind : "?"; }

        // Erklärt, wie gut sich die aktuelle Verbindung für (offizielle) Messungen eignet.
        void UpdateConnBanner()
        {
            if (connBanner == null) return;
            if (conn == null) { connBanner.Set(Icons.Globe, Theme.Muted, L.P("Ermittle Verbindungsart …", "Detecting connection type …")); return; }
            switch (conn.Kind)
            {
                case "WLAN":
                    string details = (conn.Ssid.Length > 0 ? "„" + conn.Ssid + "“" : "") +
                                     (conn.Signal >= 0 ? ", Signal " + conn.Signal + " %" : "") + (conn.Band.Length > 0 ? ", " + conn.Band : "");
                    connBanner.Set("", Theme.Warn, L.P(
                        "Du bist per WLAN verbunden (" + details.TrimStart(',', ' ') + "). WLAN kann Messungen deutlich ausbremsen – für aussagekräftige " +
                        "Ergebnisse und besonders für offizielle Messungen bitte per LAN-Kabel direkt am Router messen.",
                        "You are connected via Wi-Fi (" + details.Replace("„", "\"").Replace("“", "\"").TrimStart(',', ' ') + "). Wi-Fi can slow measurements down " +
                        "considerably – for meaningful results and especially for official measurements, connect via LAN cable directly to the router."));
                    break;
                case "LAN":
                    if (conn.LinkMbps > 0 && tariffDown > 0 && conn.LinkMbps < tariffDown)
                        connBanner.Set("", Theme.Warn, L.P(
                            "LAN-Verbindung, aber der Netzwerkanschluss arbeitet nur mit " + ConnInfo.FormatLink(conn.LinkMbps) + " – weniger als dein Tarif (" +
                            tariffDown.ToString("0") + " Mbit/s). Kabel (mind. Cat 5e) und Port am Router/Switch prüfen.",
                            "LAN connection, but the network port only runs at " + ConnInfo.FormatLink(conn.LinkMbps) + " – less than your plan (" +
                            tariffDown.ToString("0") + " Mbit/s). Check the cable (at least Cat 5e) and the router/switch port."));
                    else
                        connBanner.Set("", Theme.Good, L.P(
                            "Per LAN-Kabel verbunden" + (conn.LinkMbps > 0 ? " (" + ConnInfo.FormatLink(conn.LinkMbps) + ")" : "") + " – ideal für aussagekräftige und offizielle Messungen.",
                            "Connected via LAN cable" + (conn.LinkMbps > 0 ? " (" + ConnInfo.FormatLink(conn.LinkMbps) + ")" : "") + " – ideal for meaningful and official measurements."));
                    break;
                case "MOBILE":
                    connBanner.Set(Icons.Globe, Theme.Warn, L.P(
                        "Mobilfunkverbindung – die Messung zeigt dein Mobilfunknetz, nicht deinen Festnetzanschluss.",
                        "Mobile connection – the measurement shows your mobile network, not your fixed-line connection."));
                    break;
                case "VPN":
                    connBanner.Set(Icons.Globe, Theme.Warn, L.P(
                        "Verbindung über VPN bzw. einen virtuellen Adapter – gemessen wird die VPN-Strecke. Für Messungen deines Anschlusses das VPN trennen.",
                        "Connected through a VPN or virtual adapter – the VPN route is measured. Disconnect the VPN to measure your own connection."));
                    break;
                default:
                    connBanner.Set(Icons.Globe, Theme.Muted, L.P("Verbindungsart konnte nicht ermittelt werden.", "The connection type could not be determined."));
                    break;
            }
        }

        string countryCode;
        DarkSelect serverSelect;
        List<OoklaTest.Server> servers;
        bool loadingServers;
        Label officialTitle, officialBadge, officialText;
        FlowLayoutPanel officialButtons, officialEntry;
        int provider;
        double tariffDown, tariffUp;
        string ooklaPath;
        bool ooklaAccepted;
        CancellationTokenSource cts;
        List<SpeedResult> results;

        Segmented providerSelect;
        Panel testPanel, bnaPanel, ooklaMissing;
        Gauge gDown, gUp;
        KpiTile kPing, kLoaded, kJitter, kServer;
        PillButton btnStart;
        ProgressLine progress;
        Label status, providerInfo, tariffDownText, tariffUpText, tariffHint;
        InputBox inTariffDown, inTariffUp, inManDown, inManUp, inManPing;
        SpeedHistory history;
        DataGridView grid;

        public SpeedtestForm()
        {
            var d = Storage.LoadSettings();
            string v;
            if (d.TryGetValue("speed.provider", out v)) int.TryParse(v, out provider);
            provider = Math.Max(0, Math.Min(2, provider));
            if (d.TryGetValue("tariff.down", out v)) double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out tariffDown);
            if (d.TryGetValue("tariff.up", out v)) double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out tariffUp);
            d.TryGetValue("ookla.path", out ooklaPath);
            ooklaAccepted = d.TryGetValue("ookla.accepted", out v) && v == "1";
            countryCode = d.TryGetValue("speed.country", out v) ? OfficialTest.ForCode(v).Code : OfficialTest.DefaultCode();
            results = SpeedStore.Load();
            FlagOldResults();

            Text = "NetMonitor – Speedtest";
            BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.F(9.5f);
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(Theme.S(1320), Theme.S(1010));
            MinimumSize = new Size(Theme.S(1100), Theme.S(820));
            Icon = Theme.AppIcon;
            Theme.DarkWindow(this);
            Build();
            ShowProvider();
            UpdateTariff();
            FillResults();
            UpdateConnBanner();
            RefreshConnection();
            connTimer.Tick += delegate { RefreshConnection(); };
            connTimer.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) connTimer.Dispose();
            base.Dispose(disposing);
        }

        void SaveSetting(string key, string value)
        {
            var d = Storage.LoadSettings();
            d[key] = value;
            Storage.SaveSettings(d);
        }

        void Build()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Theme.Bg,
                Padding = new Padding(Theme.S(18), Theme.S(14), Theme.S(18), Theme.S(18)) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(70)));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(54)));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(420)));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            connBanner = new ConnBanner { Dock = DockStyle.Fill, Margin = new Padding(Theme.S(6), Theme.S(2), Theme.S(6), Theme.S(4)) };
            root.Controls.Add(connBanner, 0, 1);

            // Kopfzeile
            var head = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Theme.Bg, Margin = new Padding(Theme.S(6), 0, Theme.S(6), 0) };
            head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var titles = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, BackColor = Theme.Bg };
            titles.Controls.Add(Theme.MakeLabel("Speedtest", Theme.D(17f), Theme.Text, Theme.Bg));
            providerInfo = Theme.MakeLabel("", Theme.F(8.5f), Theme.Faint, Theme.Bg);
            titles.Controls.Add(providerInfo);
            head.Controls.Add(titles, 0, 0);
            providerSelect = new Segmented("Cloudflare", "Speedtest.net", L.P("Offizielle Messung", "Official test")) { Anchor = AnchorStyles.Right, Margin = new Padding(0, Theme.S(8), 0, 0) };
            providerSelect.SelectedIndex = provider;
            providerSelect.Changed += delegate { provider = providerSelect.SelectedIndex; SaveSetting("speed.provider", provider.ToString()); ShowProvider(); };
            head.Controls.Add(providerSelect, 1, 0);
            root.Controls.Add(head, 0, 0);

            // Mitte: Messung (links) + Tarif (rechts)
            var mid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Theme.Bg, Margin = new Padding(0) };
            mid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));
            mid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
            var left = new Surface { Dock = DockStyle.Fill, Margin = new Padding(Theme.S(6)), Padding = new Padding(Theme.S(16)) };
            testPanel = BuildTestPanel();
            bnaPanel = BuildBnaPanel();
            left.Controls.Add(testPanel);
            left.Controls.Add(bnaPanel);
            mid.Controls.Add(left, 0, 0);
            mid.Controls.Add(BuildTariffPanel(), 1, 0);
            root.Controls.Add(mid, 0, 2);

            // Unten: Verlauf
            var bottom = new Surface { Dock = DockStyle.Fill, Margin = new Padding(Theme.S(6), Theme.S(6), Theme.S(6), 0), Padding = new Padding(Theme.S(16), Theme.S(12), Theme.S(16), Theme.S(12)) };
            var bl = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = Theme.Surface };
            bl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            bl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            bl.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(40)));
            bl.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            bl.Controls.Add(Theme.MakeLabel(L.P("Bisherige Speedtests", "Previous speed tests"), Theme.D(12.5f), Theme.Text, Theme.Surface), 0, 0);
            var actions = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, WrapContents = false, BackColor = Theme.Surface, Margin = new Padding(0) };
            var btnExport = new PillButton("Export", Theme.Accent, false, Icons.Download) { BackColor = Theme.Surface, Height = Theme.S(34) };
            var btnDelete = new PillButton(L.P("Auswahl löschen", "Delete selected"), Theme.Bad, true, Icons.Delete) { BackColor = Theme.Surface, Height = Theme.S(34) };
            btnExport.Click += delegate { ExportResults(); };
            btnDelete.Click += delegate { DeleteSelected(); };
            actions.Controls.Add(btnExport); actions.Controls.Add(btnDelete);
            bl.Controls.Add(actions, 1, 0);
            history = new SpeedHistory { Dock = DockStyle.Fill, Margin = new Padding(0, 0, Theme.S(12), 0) };
            bl.Controls.Add(history, 0, 1);
            grid = new DataGridView { Dock = DockStyle.Fill };
            Theme.StyleGrid(grid);
            grid.MultiSelect = true;
            var cols = L.En
                ? new[] { "Date", "Provider", "Download\nMbit/s", "Upload\nMbit/s", "Ping", "Loaded", "% of plan", "Connection" }
                : new[] { "Datum", "Anbieter", "Download\nMbit/s", "Upload\nMbit/s", "Ping", "Unter Last", "% Tarif", "Verbindung" };
            foreach (var c in cols) grid.Columns.Add(c, c);
            grid.Columns[0].FillWeight = 120;
            grid.Columns[1].FillWeight = 110;
            grid.Columns[7].FillWeight = 115;
            grid.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                var r = grid.Rows[e.RowIndex].Tag as SpeedResult;
                if (r != null && r.Url.StartsWith("https://")) Process.Start(r.Url);
            };
            bl.Controls.Add(grid, 1, 1);
            bottom.Controls.Add(bl);
            root.Controls.Add(bottom, 0, 3);
            Controls.Add(root);
        }

        Panel BuildTestPanel()
        {
            var p = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, BackColor = Theme.Surface, Margin = new Padding(0) };
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            p.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            p.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(96)));
            p.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(22)));
            p.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(48)));
            gDown = new Gauge { Dock = DockStyle.Fill, Title = "Download", Color = Theme.Cyan };
            gUp = new Gauge { Dock = DockStyle.Fill, Title = "Upload", Color = Theme.Violet };
            p.Controls.Add(gDown, 0, 0);
            p.Controls.Add(gUp, 1, 0);

            var kpis = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, BackColor = Theme.Surface, Margin = new Padding(0) };
            for (int i = 0; i < 4; i++) kpis.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, i == 3 ? 34 : 22));
            kPing = MiniTile("Ping", Theme.Good);
            kLoaded = MiniTile(L.P("Ping unter Last", "Loaded ping"), Theme.Warn);
            kJitter = MiniTile("Jitter", Theme.Cyan);
            kServer = MiniTile("Server", Theme.Accent);
            kpis.Controls.Add(kPing, 0, 0); kpis.Controls.Add(kLoaded, 1, 0); kpis.Controls.Add(kJitter, 2, 0); kpis.Controls.Add(kServer, 3, 0);
            p.Controls.Add(kpis, 0, 1);
            p.SetColumnSpan(kpis, 2);

            progress = new ProgressLine { Dock = DockStyle.Fill, BackColor = Theme.Surface, Margin = new Padding(0, Theme.S(8), 0, Theme.S(6)) };
            p.Controls.Add(progress, 0, 2);
            p.SetColumnSpan(progress, 2);

            var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Theme.Surface, Margin = new Padding(0) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            status = Theme.MakeLabel("", Theme.F(9f), Theme.Faint, Theme.Surface);
            status.Margin = new Padding(0, Theme.S(12), 0, 0);
            status.AutoEllipsis = true;
            row.Controls.Add(status, 0, 0);
            var btns = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Surface, Margin = new Padding(0) };
            ooklaMissing = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Surface, Margin = new Padding(0) };
            var btnGet = new PillButton(L.P("Ookla CLI installieren", "Install Ookla CLI"), Theme.Accent, false, Icons.Download) { BackColor = Theme.Surface };
            var btnPath = new PillButton(L.P("Pfad wählen …", "Choose path …"), Theme.Muted, true, Icons.Folder) { BackColor = Theme.Surface };
            btnGet.Click += delegate { InstallOokla(btnGet); };
            btnPath.Click += delegate { ChooseOokla(); };
            ooklaMissing.Controls.Add(btnGet); ooklaMissing.Controls.Add(btnPath);
            btnStart = new PillButton(L.P("Test starten", "Start test"), Theme.Good, false, Icons.Play) { BackColor = Theme.Surface };
            btnStart.Click += delegate { if (cts != null) cts.Cancel(); else StartTest(); };
            serverSelect = new DarkSelect { Width = Theme.S(250), BackColor = Theme.Surface, Margin = new Padding(0, Theme.S(2), Theme.S(8), 0), Placeholder = L.P("Server …", "Server …") };
            serverSelect.Items.Add(L.P("Server: automatisch", "Server: automatic"));
            serverSelect.SelectedIndex = 0;
            serverSelect.SelectedIndexChanged += delegate
            {
                var s = serverSelect.SelectedItem as OoklaTest.Server;
                SaveSetting("ookla.server", s != null ? s.Id.ToString() : "0");
            };
            btns.Controls.Add(ooklaMissing); btns.Controls.Add(serverSelect); btns.Controls.Add(btnStart);
            row.Controls.Add(btns, 1, 0);
            p.Controls.Add(row, 0, 3);
            p.SetColumnSpan(row, 2);
            return p;
        }

        static KpiTile MiniTile(string title, Color tint)
        {
            return new KpiTile { Title = title, Glyph = Icons.Clock, Tint = tint, Dock = DockStyle.Fill, Margin = new Padding(Theme.S(4)), Fill = Theme.Surface2, BackColor = Theme.Surface, Compact = true };
        }

        OfficialTest Official { get { return OfficialTest.ForCode(countryCode); } }

        Panel BuildBnaPanel()
        {
            var p = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6, BackColor = Theme.Surface, Margin = new Padding(0) };
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            p.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (int i = 0; i < 6; i++) p.RowStyles.Add(new RowStyle(i == 5 ? SizeType.Percent : SizeType.AutoSize, 100));
            officialTitle = Theme.MakeLabel("", Theme.D(14f), Theme.Text, Theme.Surface);
            p.Controls.Add(officialTitle, 0, 0);
            var country = new DarkSelect { Width = Theme.S(210), BackColor = Theme.Surface, Margin = new Padding(0) };
            foreach (var t in OfficialTest.All) country.Items.Add(t);
            country.SelectedIndex = OfficialTest.All.IndexOf(Official);
            country.SelectedIndexChanged += delegate
            {
                countryCode = ((OfficialTest)country.SelectedItem).Code;
                SaveSetting("speed.country", countryCode);
                FillOfficial();
                ShowProvider();
            };
            p.Controls.Add(country, 1, 0);
            officialBadge = new Label { AutoSize = true, Font = Theme.F(8.5f, FontStyle.Bold), UseMnemonic = false, Margin = new Padding(Theme.S(2), Theme.S(6), 0, 0), Padding = new Padding(Theme.S(8), Theme.S(3), Theme.S(8), Theme.S(3)) };
            p.Controls.Add(officialBadge, 0, 1);
            officialText = Theme.MakeLabel("", Theme.F(9.5f), Theme.Muted, Theme.Surface);
            officialText.MaximumSize = new Size(Theme.S(740), 0);
            officialText.Margin = new Padding(0, Theme.S(10), 0, Theme.S(14));
            p.Controls.Add(officialText, 0, 2);
            p.SetColumnSpan(officialText, 2);

            officialButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Surface, Margin = new Padding(0, 0, 0, Theme.S(16)) };
            p.Controls.Add(officialButtons, 0, 3);
            p.SetColumnSpan(officialButtons, 2);

            var entryBox = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, BackColor = Theme.Surface, Margin = new Padding(0) };
            entryBox.Controls.Add(Theme.MakeLabel(L.P("Ergebnis eintragen (zum Vergleich mit den anderen Messungen)", "Enter result (to compare with the other measurements)"),
                Theme.F(9f, FontStyle.Bold), Theme.Muted, Theme.Surface));
            var entry = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Surface, Margin = new Padding(0, Theme.S(6), 0, 0) };
            inManDown = SmallInput("");
            inManUp = SmallInput("");
            inManPing = SmallInput("");
            var btnSave = new PillButton(L.P("Speichern", "Save"), Theme.Good, false, Icons.Check) { BackColor = Theme.Surface, Height = Theme.S(34) };
            btnSave.Click += delegate { SaveManual(); };
            btnSave.Margin = new Padding(0, Theme.S(20), 0, 0);
            entry.Controls.AddRange(new Control[] { Labeled("Download (Mbit/s)", inManDown), Labeled("Upload (Mbit/s)", inManUp), Labeled("Ping (ms)", inManPing), btnSave });
            entryBox.Controls.Add(entry);
            p.Controls.Add(entryBox, 0, 4);
            p.SetColumnSpan(entryBox, 2);
            officialEntry = entryBox;
            FillOfficial();
            return p;
        }

        // Inhalt für das gewählte Land: Einstufung, Beschreibung und passende Buttons.
        void FillOfficial()
        {
            var t = Official;
            officialTitle.Text = t.Web.Length == 0 ? L.P("Keine offizielle Messung", "No official measurement") : t.Title + "  ·  " + t.Regulator;
            officialBadge.Text = t.Level == 0 ? L.P("RECHTSVERBINDLICH", "LEGALLY BINDING")
                               : t.Level == 1 ? L.P("OFFIZIELLER TEST DER BEHÖRDE", "OFFICIAL REGULATOR TEST")
                               : L.P("CLOUDFLARE / SPEEDTEST.NET VERWENDEN", "USE CLOUDFLARE / SPEEDTEST.NET");
            if (officialEntry != null) officialEntry.Visible = t.Level < 2;
            var c = t.Level == 0 ? Theme.Good : t.Level == 1 ? Theme.Cyan : Theme.Warn;
            officialBadge.ForeColor = c;
            officialBadge.BackColor = Theme.Mix(Theme.Surface, c, 0.18f);
            officialText.Text = t.Text + (t.Level < 2
                ? L.P("\n\nDie Messung läuft im Browser bzw. in der App der Behörde – NetMonitor öffnet sie für dich. " +
                      "Wichtig: per LAN-Kabel direkt am Router messen (kein WLAN), den Ping-Monitor stoppen und andere Geräte vom Netz nehmen.",
                      "\n\nThe measurement runs in the regulator's website or app – NetMonitor opens it for you. " +
                      "Important: measure via LAN cable directly at the router (no Wi-Fi), stop the ping monitor and disconnect other devices.") +
                  (t.Code == "DE" ? L.P(" Für das Nachweisverfahren der Desktop-App ist eine LAN-Verbindung Voraussetzung.",
                                        " A LAN connection is required for the desktop app's verification procedure.") : "")
                : "");

            foreach (Control old in officialButtons.Controls) old.Dispose();
            officialButtons.Controls.Clear();
            if (t.Code == "DE")
            {
                string app = Breitband.FindDesktopApp();
                var btnApp = new PillButton(app != null ? L.P("Desktop-App starten", "Launch desktop app") : L.P("Desktop-App herunterladen", "Get desktop app"),
                    Theme.Accent, false, app != null ? Icons.Play : Icons.Download) { BackColor = Theme.Surface };
                btnApp.Click += delegate { Process.Start(app ?? t.App); };
                officialButtons.Controls.Add(btnApp);
            }
            if (t.Web.Length == 0)
            {
                // Kein offizieller Test: direkt zu den eingebauten Messungen wechseln
                var btnCf = new PillButton(L.P("Cloudflare-Test", "Cloudflare test"), Theme.Accent, false, Icons.Bolt) { BackColor = Theme.Surface };
                btnCf.Click += delegate { providerSelect.SelectedIndex = 0; };
                var btnOokla = new PillButton("Speedtest.net", Theme.Cyan, true, Icons.Bolt) { BackColor = Theme.Surface };
                btnOokla.Click += delegate { providerSelect.SelectedIndex = 1; };
                officialButtons.Controls.Add(btnCf);
                officialButtons.Controls.Add(btnOokla);
                return;
            }
            var btnWeb = new PillButton(L.P("Messung im Browser öffnen", "Open test in browser"),
                t.Code == "DE" ? Theme.Cyan : Theme.Accent, t.Code == "DE", Icons.Globe) { BackColor = Theme.Surface };
            btnWeb.Click += delegate { Process.Start(t.Web); };
            officialButtons.Controls.Add(btnWeb);
        }

        static InputBox SmallInput(string placeholder)
        {
            return new InputBox("", "", Theme.F(10f)) { Width = Theme.S(150), Margin = new Padding(0, 0, Theme.S(8), 0) };
        }

        // Eingabefeld mit Beschriftung darüber.
        static Control Labeled(string caption, InputBox box)
        {
            var p = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, BackColor = Theme.Surface, Margin = new Padding(0, 0, Theme.S(8), 0) };
            var l = Theme.MakeLabel(caption, Theme.F(8.5f), Theme.Faint, Theme.Surface);
            l.Margin = new Padding(Theme.S(2), 0, 0, Theme.S(4));
            box.Margin = new Padding(0);
            p.Controls.Add(l);
            p.Controls.Add(box);
            return p;
        }

        Control BuildTariffPanel()
        {
            var s = new Surface { Dock = DockStyle.Fill, Margin = new Padding(Theme.S(6)), Padding = new Padding(Theme.S(18), Theme.S(14), Theme.S(18), Theme.S(14)) };
            var p = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, BackColor = Theme.Surface };
            for (int i = 0; i < 7; i++) p.RowStyles.Add(new RowStyle(i == 6 ? SizeType.Percent : SizeType.AutoSize, 100));
            p.Controls.Add(Theme.MakeLabel(L.P("Gebuchter Tarif", "Your internet plan"), Theme.D(13f), Theme.Text, Theme.Surface), 0, 0);
            var sub = Theme.MakeLabel(L.P("Maximalgeschwindigkeit laut Vertrag", "Maximum speed according to your contract"), Theme.F(8.5f), Theme.Faint, Theme.Surface);
            sub.Margin = new Padding(Theme.S(2), 0, 0, Theme.S(12));
            p.Controls.Add(sub, 0, 1);
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Surface, Margin = new Padding(0, 0, 0, Theme.S(16)) };
            inTariffDown = SmallInput("Download Mbit/s");
            inTariffUp = SmallInput("Upload Mbit/s");
            inTariffDown.Width = inTariffUp.Width = Theme.S(140);
            if (tariffDown > 0) inTariffDown.Value = tariffDown.ToString("0.##");
            if (tariffUp > 0) inTariffUp.Value = tariffUp.ToString("0.##");
            inTariffDown.Box.TextChanged += delegate { ReadTariff(); };
            inTariffUp.Box.TextChanged += delegate { ReadTariff(); };
            row.Controls.Add(Labeled("Download (Mbit/s)", inTariffDown)); row.Controls.Add(Labeled("Upload (Mbit/s)", inTariffUp));
            p.Controls.Add(row, 0, 2);
            tariffDownText = Theme.MakeLabel("", Theme.F(10.5f, FontStyle.Bold), Theme.Text, Theme.Surface);
            tariffUpText = Theme.MakeLabel("", Theme.F(10.5f, FontStyle.Bold), Theme.Text, Theme.Surface);
            tariffUpText.Margin = new Padding(Theme.S(3), Theme.S(6), 0, Theme.S(14));
            p.Controls.Add(tariffDownText, 0, 3);
            p.Controls.Add(tariffUpText, 0, 4);
            tariffHint = Theme.MakeLabel("", Theme.F(9f), Theme.Muted, Theme.Surface);
            tariffHint.MaximumSize = new Size(Theme.S(400), 0);
            p.Controls.Add(tariffHint, 0, 5);
            s.Controls.Add(p);
            return s;
        }

        static double ParseNumber(string s)
        {
            double v;
            s = (s ?? "").Trim().Replace(',', '.');
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v > 0 ? v : 0;
        }

        void ReadTariff()
        {
            tariffDown = ParseNumber(inTariffDown.Value);
            tariffUp = ParseNumber(inTariffUp.Value);
            var d = Storage.LoadSettings();
            d["tariff.down"] = tariffDown.ToString(CultureInfo.InvariantCulture);
            d["tariff.up"] = tariffUp.ToString(CultureInfo.InvariantCulture);
            Storage.SaveSettings(d);
            UpdateTariff();
            FillResults();
        }

        static Color PctColor(double pct) { return pct >= 90 ? Theme.Good : pct >= 70 ? Theme.Warn : Theme.Bad; }

        // Vergleicht den letzten Messwert mit dem Tarif und schätzt den Durchschnitt der letzten Messungen ein.
        void UpdateTariff()
        {
            gDown.Tariff = tariffDown; gUp.Tariff = tariffUp;
            SpeedResult last = null;
            for (int i = results.Count - 1; i >= 0 && last == null; i--) if (!double.IsNaN(results[i].Down) && !results[i].Suspicious) last = results[i];
            Action<Label, Gauge, string, double, double> show = (label, gauge, name, measured, tariff) =>
            {
                if (tariff <= 0 || double.IsNaN(measured))
                {
                    label.Text = name + ": " + (tariff > 0 ? L.P("noch keine Messung", "no measurement yet") : L.P("Tarif eintragen", "enter your plan"));
                    label.ForeColor = Theme.Muted;
                    gauge.Sub = ""; gauge.Invalidate();
                    return;
                }
                double pct = 100 * measured / tariff;
                label.Text = name + ": " + SpeedHistory.Fmt(measured) + L.P(" von ", " of ") + tariff.ToString("0.##") + " Mbit/s  (" + pct.ToString("0") + " %)";
                label.ForeColor = PctColor(pct);
                gauge.Sub = pct.ToString("0") + L.P(" % des Tarifs", " % of plan");
                gauge.SubColor = PctColor(pct);
                gauge.Invalidate();
            };
            show(tariffDownText, gDown, "Download", last != null ? last.Down : double.NaN, tariffDown);
            show(tariffUpText, gUp, "Upload", last != null ? last.Up : double.NaN, tariffUp);

            int n = 0, below = 0;
            for (int i = results.Count - 1; i >= 0 && n < 10; i--)
            {
                if (double.IsNaN(results[i].Down) || results[i].Suspicious || tariffDown <= 0) continue;
                n++;
                if (results[i].Down < 0.9 * tariffDown) below++;
            }
            tariffHint.Text = tariffDown <= 0
                ? L.P("Trage die Werte aus deinem Vertrag ein, dann zeigt NetMonitor, wie viel davon tatsächlich ankommt.",
                      "Enter the values from your contract and NetMonitor shows how much of it you actually get.")
                : n == 0 ? ""
                : L.P(below + " von " + n + " der letzten Messungen lagen unter 90 % des Tarifs.",
                      below + " of the last " + n + " measurements were below 90 % of your plan.") +
                  (below * 2 > n ? L.P("\n\nDas spricht für eine dauerhafte Abweichung. Für Ansprüche gegenüber dem Anbieter zählt nur das Protokoll der Breitbandmessung (Reiter oben).",
                                       "\n\nThis suggests a persistent shortfall. For claims against your ISP only the official broadband measurement counts (tab above).") : "");
            tariffHint.ForeColor = n > 0 && below * 2 > n ? Theme.Warn : Theme.Muted;
            history.TariffDown = tariffDown; history.TariffUp = tariffUp;
            history.Invalidate();
            UpdateConnBanner();
        }

        void ShowProvider()
        {
            testPanel.Visible = provider != 2;
            bnaPanel.Visible = provider == 2;
            bool ookla = provider == 1;
            bool missing = ookla && OoklaTest.Find(ooklaPath) == null;
            ooklaMissing.Visible = missing;
            serverSelect.Visible = ookla && !missing;
            btnStart.Enabled = !missing;
            if (ookla && !missing) LoadServers();
            providerInfo.Text = provider == 0
                ? L.P("speed.cloudflare.com · 6 parallele Verbindungen · je 8 s Download und Upload · misst auch die Latenz unter Last",
                      "speed.cloudflare.com · 6 parallel connections · 8 s download and upload each · also measures latency under load")
                : provider == 1
                    ? L.P("Offizielle Ookla Speedtest CLI · nächstgelegener Speedtest.net-Server", "Official Ookla Speedtest CLI · nearest Speedtest.net server")
                    : Official.Web.Length == 0 ? L.P("Offizielle Messstellen: Deutschland, Österreich, Schweiz, Luxemburg", "Official tests: Germany, Austria, Switzerland, Luxembourg")
                    : L.P("Offizielle Messstelle für ", "Official measurement for ") + Official.Name + " · " + Official.Regulator;
            status.ForeColor = Theme.Faint;
            status.Text = missing
                ? L.P("Benötigt die kostenlose Ookla Speedtest CLI.", "Requires the free Ookla Speedtest CLI.")
                : L.P("Die Messung lastet die Leitung kurz voll aus – währenddessen erkannte Paketverluste werden nicht als Störung gewertet.",
                      "The test saturates your connection briefly – packet loss during the test is not counted as an incident.");
        }

        // Lädt die nächstgelegenen Speedtest.net-Server in die Auswahl (einmalig, im Hintergrund).
        async void LoadServers()
        {
            if (servers != null || loadingServers) return;
            loadingServers = true;
            string exe = OoklaTest.Find(ooklaPath);
            try { servers = await Task.Run(() => OoklaTest.ListServers(exe)); }
            catch { servers = new List<OoklaTest.Server>(); }
            loadingServers = false;
            if (IsDisposed) return;
            string v;
            int saved = 0;
            if (Storage.LoadSettings().TryGetValue("ookla.server", out v)) int.TryParse(v, out saved);
            int sel = 0;
            foreach (var s in servers)
            {
                serverSelect.Items.Add(s);
                if (s.Id == saved) sel = serverSelect.Items.Count - 1;
            }
            serverSelect.SelectedIndex = sel;
        }

        int SelectedServerId()
        {
            var s = serverSelect.SelectedItem as OoklaTest.Server;
            return s != null ? s.Id : 0;
        }

        bool AskOoklaTerms()
        {
            if (ooklaAccepted) return true;
            var answer = MessageBox.Show(this, L.P(
                "Speedtest.net wird von Ookla betrieben. Mit der Nutzung akzeptierst du deren Nutzungsbedingungen und Datenschutzerklärung:\n\n" +
                OoklaTest.Terms + "\n" + OoklaTest.Privacy + "\n\nAkzeptieren und fortfahren?",
                "Speedtest.net is operated by Ookla. By using it you accept their terms of use and privacy policy:\n\n" +
                OoklaTest.Terms + "\n" + OoklaTest.Privacy + "\n\nAccept and continue?"), "Speedtest.net", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (answer != DialogResult.Yes) return false;
            ooklaAccepted = true;
            SaveSetting("ookla.accepted", "1");
            return true;
        }

        // Lädt die offizielle Ookla CLI herunter und hinterlegt sie automatisch.
        async void InstallOokla(PillButton button)
        {
            if (!AskOoklaTerms()) return;
            button.Enabled = false;
            status.ForeColor = Theme.Muted;
            status.Text = L.P("Lade Ookla Speedtest CLI …", "Downloading Ookla Speedtest CLI …");
            try
            {
                ooklaPath = await Task.Run(() => OoklaTest.Download());
                servers = null;
                ShowProvider();
                status.ForeColor = Theme.Good;
                status.Text = L.P("✓ Ookla Speedtest CLI installiert – bereit für den Test.", "✓ Ookla Speedtest CLI installed – ready to test.");
            }
            catch (Exception ex)
            {
                status.ForeColor = Theme.Bad;
                status.Text = L.P("Download fehlgeschlagen: ", "Download failed: ") + (ex.InnerException ?? ex).Message;
            }
            finally { button.Enabled = true; }
        }

        void ChooseOokla()
        {
            using (var dlg = new OpenFileDialog { Filter = "speedtest.exe|speedtest.exe", Title = "Ookla Speedtest CLI" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                ooklaPath = dlg.FileName;
                SaveSetting("ookla.path", ooklaPath);
                ShowProvider();
            }
        }

        async void StartTest()
        {
            string exe = null;
            if (provider == 1)
            {
                exe = OoklaTest.Find(ooklaPath);
                if (exe == null) { ShowProvider(); return; }
                if (!AskOoklaTerms()) return;
            }

            cts = new CancellationTokenSource();
            providerSelect.Enabled = false;
            btnStart.Text = L.P("Abbrechen", "Cancel"); btnStart.Glyph = Icons.Stop; btnStart.Color = Theme.Bad; btnStart.Invalidate();
            gDown.Value = double.NaN; gUp.Value = double.NaN;
            gDown.Sub = gUp.Sub = "";
            foreach (var k in new[] { kPing, kLoaded, kJitter, kServer }) { k.Value = "–"; k.Invalidate(); }
            status.ForeColor = Theme.Muted;
            progress.Value = 0; progress.Invalidate();
            SpeedState.Begin();
            SpeedProgress report = (phase, value, fraction, server) => BeginInvoke((Action)(() =>
            {
                progress.Value = (int)(100 * fraction); progress.Invalidate();
                if (!string.IsNullOrEmpty(server)) { kServer.Value = server; kServer.Invalidate(); }
                if (phase == 0) { status.Text = L.P("Messe Latenz …", "Measuring latency …"); if (!double.IsNaN(value)) { kPing.Value = SpeedHistory.FmtMs(value); kPing.Invalidate(); } }
                if (phase == 1) { status.Text = L.P("Messe Download …", "Measuring download …"); gDown.Value = value; }
                if (phase == 2) { status.Text = L.P("Messe Upload …", "Measuring upload …"); gUp.Value = value; }
            }));
            bool askRetry = false;
            try
            {
                var token = cts.Token;
                int serverId = provider == 1 ? SelectedServerId() : 0;
                SpeedResult r = provider == 1
                    ? await Task.Run(() => OoklaTest.Run(exe, token, report, serverId))
                    : await Task.Run(() => CloudflareTest.Run(token, report));
                r.Suspicious = IsSuspicious(r);
                r.Conn = ConnKind();
                if (r.Suspicious && provider == 1)
                {
                    // Unplausibel: Ergebnis markiert speichern und automatisch mit einem anderen nahen Server wiederholen
                    SpeedStore.Append(r);
                    results.Add(r);
                    FillResults();
                    if (servers == null || servers.Count == 0)
                        try { servers = await Task.Run(() => OoklaTest.ListServers(exe)); } catch { servers = new List<OoklaTest.Server>(); }
                    OoklaTest.Server alt = null;
                    foreach (var s in servers) if (s.Id != OoklaTest.LastServer) { alt = s; break; }
                    status.ForeColor = Theme.Warn;
                    status.Text = L.P("⚠ Unplausibel (", "⚠ Implausible (") + SpeedHistory.Fmt(r.Down) +
                        L.P(" Mbit/s) – wiederhole automatisch", " Mbit/s) – repeating automatically") + (alt != null ? " · " + alt.Name : "") + " …";
                    gDown.Value = double.NaN; gUp.Value = double.NaN;
                    int altId = alt != null ? alt.Id : 0;
                    r = await Task.Run(() => OoklaTest.Run(exe, token, report, altId));
                    r.Suspicious = IsSuspicious(r);
                    r.Conn = ConnKind();
                }
                askRetry = r.Suspicious;
                SpeedStore.Append(r);
                results.Add(r);
                ShowResult(r);
                status.ForeColor = Theme.Good;
                status.Text = L.P("✓ Fertig", "✓ Done") + " · " + r.Server +
                    (r.Bytes > 0 ? L.P(" · Datenverbrauch ", " · data used ") + (r.Bytes / 1e6).ToString("0") + " MB" : "") +
                    (r.Url.Length > 0 ? L.P(" · Doppelklick in der Liste öffnet das Ergebnis", " · double-click the list entry to open the result") : "");
                if (r.Suspicious)
                {
                    status.ForeColor = Theme.Warn;
                    status.Text = L.P("⚠ Leitung wurde nicht ausgelastet – Ergebnis vermutlich zu niedrig", "⚠ Connection was not saturated – result is probably too low");
                }
                FillResults();
            }
            catch (Exception ex)
            {
                status.ForeColor = cts.IsCancellationRequested ? Theme.Muted : Theme.Bad;
                status.Text = cts.IsCancellationRequested ? L.P("Abgebrochen", "Cancelled") : L.P("Fehler: ", "Error: ") + (ex.InnerException ?? ex).Message;
                gDown.Value = double.NaN; gUp.Value = double.NaN;
            }
            finally
            {
                SpeedState.End();
                cts.Dispose();
                cts = null;
                providerSelect.Enabled = true;
                btnStart.Text = L.P("Test starten", "Start test"); btnStart.Glyph = Icons.Play; btnStart.Color = Theme.Good; btnStart.Invalidate();
                progress.Value = 100; progress.Invalidate();
            }
            if (askRetry && !IsDisposed)
            {
                var answer = MessageBox.Show(this, L.P(
                    "Die Latenz ist während der Messung kaum gestiegen – die Leitung wurde also nicht ausgelastet. Das Ergebnis liegt deutlich unter " +
                    "deinem Tarif bzw. deinen bisherigen Messungen und ist vermutlich zu niedrig (z. B. ausgelasteter Server oder parallele Downloads).\n\n" +
                    "Das Ergebnis ist markiert und fließt nicht in die Tarifbewertung ein. Test jetzt wiederholen?",
                    "Latency barely increased during the test – the connection was not saturated. The result is far below your plan or previous " +
                    "measurements and is probably too low (e.g. a busy server or parallel downloads).\n\n" +
                    "The result is flagged and excluded from the plan evaluation. Repeat the test now?"),
                    "Speedtest", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer == DialogResult.Yes) StartTest();
            }
        }

        // Ältere Messungen (vor Version 1.1.1) einmalig mit denselben Kriterien prüfen.
        void FlagOldResults()
        {
            bool changed = false;
            foreach (var r in results)
            {
                if (r.Suspicious || r.Bytes == 0 || double.IsNaN(r.Down) || double.IsNaN(r.Ping) || double.IsNaN(r.LoadedPing)) continue;
                if (r.LoadedPing - r.Ping >= 5) continue;
                double reference = tariffDown;
                foreach (var o in results)
                    if (o != r && !double.IsNaN(o.Down) && Math.Abs((o.Time - r.Time).TotalDays) < 30 &&
                        (double.IsNaN(o.LoadedPing) || double.IsNaN(o.Ping) || o.LoadedPing - o.Ping >= 5))
                        reference = Math.Max(reference, o.Down);
                if (reference > 0 && r.Down < 0.5 * reference) { r.Suspicious = true; changed = true; }
            }
            if (changed) try { SpeedStore.Save(results); } catch { }
        }

        bool IsSuspicious(SpeedResult r)
        {
            if (double.IsNaN(r.Down) || double.IsNaN(r.Ping) || double.IsNaN(r.LoadedPing)) return false;
            if (r.LoadedPing - r.Ping >= 5) return false; // Leitung war ausgelastet
            double reference = tariffDown;
            foreach (var old in results)
                if (!old.Suspicious && !double.IsNaN(old.Down) && (DateTime.Now - old.Time).TotalDays < 30) reference = Math.Max(reference, old.Down);
            return reference > 0 && r.Down < 0.5 * reference;
        }

        void ShowResult(SpeedResult r)
        {
            gDown.Value = r.Down; gUp.Value = r.Up;
            kPing.Value = SpeedHistory.FmtMs(r.Ping);
            kLoaded.Value = SpeedHistory.FmtMs(r.LoadedPing);
            kLoaded.Sub = !double.IsNaN(r.LoadedPing) && !double.IsNaN(r.Ping)
                ? "+" + Math.Max(0, r.LoadedPing - r.Ping).ToString("0") + L.P(" ms unter Last", " ms under load") : "";
            kLoaded.ValueColor = !double.IsNaN(r.LoadedPing) && r.LoadedPing - r.Ping > 60 ? Theme.Warn : Theme.Text;
            kJitter.Value = SpeedHistory.FmtMs(r.Jitter);
            kServer.Value = r.Server;
            foreach (var k in new[] { kPing, kLoaded, kJitter, kServer }) k.Invalidate();
            UpdateTariff();
        }

        void SaveManual()
        {
            double down = ParseNumber(inManDown.Value), up = ParseNumber(inManUp.Value), ping = ParseNumber(inManPing.Value);
            if (down <= 0 && up <= 0)
            {
                MessageBox.Show(this, L.P("Bitte mindestens Download oder Upload eintragen.", "Please enter at least download or upload."), Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var r = new SpeedResult
            {
                Time = DateTime.Now, Provider = Official.Title, Server = L.P("manuell eingetragen", "entered manually") + " · " + Official.Regulator, Conn = ConnKind(),
                Down = down > 0 ? down : double.NaN, Up = up > 0 ? up : double.NaN, Ping = ping > 0 ? ping : double.NaN
            };
            SpeedStore.Append(r);
            results.Add(r);
            inManDown.Value = inManUp.Value = inManPing.Value = "";
            UpdateTariff();
            FillResults();
        }

        void FillResults()
        {
            history.Items = results;
            history.Invalidate();
            grid.Rows.Clear();
            for (int i = results.Count - 1; i >= 0; i--)
            {
                var r = results[i];
                double pct = tariffDown > 0 && !double.IsNaN(r.Down) ? 100 * r.Down / tariffDown : double.NaN;
                int row = grid.Rows.Add(r.Time.ToString(L.DateTimeShort), r.Provider, SpeedHistory.Fmt(r.Down), SpeedHistory.Fmt(r.Up),
                    SpeedHistory.FmtMs(r.Ping), SpeedHistory.FmtMs(r.LoadedPing), double.IsNaN(pct) ? "–" : pct.ToString("0") + " %", r.ConnText);
                grid.Rows[row].Cells[7].Style.ForeColor = r.Conn == "LAN" ? Theme.Good : r.Conn == "" || r.Conn == "?" ? Theme.Faint : Theme.Warn;
                grid.Rows[row].Tag = r;
                grid.Rows[row].Cells[2].Style.ForeColor = Theme.Cyan;
                grid.Rows[row].Cells[3].Style.ForeColor = Theme.Violet;
                if (r.Suspicious)
                {
                    grid.Rows[row].Cells[1].Value = "⚠ " + r.Provider;
                    grid.Rows[row].Cells[1].Style.ForeColor = Theme.Warn;
                    grid.Rows[row].Cells[1].ToolTipText = L.P("Leitung nicht ausgelastet – Ergebnis vermutlich zu niedrig, zählt nicht für die Tarifbewertung",
                                                              "Connection not saturated – result probably too low, excluded from the plan evaluation");
                    grid.Rows[row].Cells[2].Style.ForeColor = Theme.Faint;
                }
                if (!double.IsNaN(pct))
                {
                    grid.Rows[row].Cells[6].Style.ForeColor = PctColor(pct);
                    grid.Rows[row].Cells[6].Style.Font = Theme.F(9.5f, FontStyle.Bold);
                }
            }
            grid.ClearSelection();
        }

        void DeleteSelected()
        {
            if (grid.SelectedRows.Count == 0) return;
            var answer = MessageBox.Show(this, grid.SelectedRows.Count + L.P(" Speedtest(s) löschen?", " speed test(s) delete?"), Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;
            foreach (DataGridViewRow row in grid.SelectedRows) results.Remove(row.Tag as SpeedResult);
            try { SpeedStore.Save(results); } catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
            UpdateTariff();
            FillResults();
        }

        void ExportResults()
        {
            if (results.Count == 0) return;
            CsvExport.ExportWithDialog(this, "speedtests_" + DateTime.Now.ToString("yyyy-MM-dd") + ".csv", history, path =>
            {
                var ci = CultureInfo.CurrentCulture;
                var sb = new StringBuilder();
                sb.AppendLine(L.P("Zeit;Anbieter;Server;Download Mbit/s;Upload Mbit/s;Ping ms;Jitter ms;Ping unter Last ms;Paketverlust %;% Tarif Download;Ergebnis-Link;Verbindung;Unplausibel",
                                  "Time;Provider;Server;Download Mbit/s;Upload Mbit/s;Ping ms;Jitter ms;Loaded ping ms;Packet loss %;% of plan download;Result link;Connection;Implausible"));
                Func<double, string> f = v => double.IsNaN(v) ? "" : v.ToString("0.0", ci);
                foreach (var r in results)
                    sb.AppendLine(string.Join(";", new[]
                    {
                        r.Time.ToString("yyyy-MM-dd HH:mm:ss"), r.Provider, Storage.Escape(r.Server), f(r.Down), f(r.Up), f(r.Ping), f(r.Jitter),
                        f(r.LoadedPing), f(r.Loss), tariffDown > 0 && !double.IsNaN(r.Down) ? f(100 * r.Down / tariffDown) : "", r.Url,
                        r.ConnText, r.Suspicious ? L.P("ja", "yes") : ""
                    }));
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            });
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (cts != null) cts.Cancel();
            base.OnFormClosing(e);
        }
    }
}
