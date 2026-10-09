// NetMonitor – Daten, Speicherung, Auswertung und Spiel-Server-Erkennung.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NetMonitor
{
    public class Sample
    {
        public DateTime Time;
        public bool Ok;
        public long Rtt;
        public string Status;
        public double Loss60;

        public const int WindowSize = 60;

        // Berechnet den gleitenden Verlust über die letzten 60 Messungen (für geladene Sitzungen).
        public static void ComputeLoss60(List<Sample> list)
        {
            var window = new Queue<bool>();
            int lost = 0;
            foreach (var s in list)
            {
                window.Enqueue(s.Ok);
                if (!s.Ok) lost++;
                if (window.Count > WindowSize && !window.Dequeue()) lost--;
                s.Loss60 = 100.0 * lost / window.Count;
            }
        }
    }

    public class Stats
    {
        public long Sent, Received, Min = long.MaxValue, Max, JitterCount;
        public double Sum, JitterSum;
        long lastRtt;
        bool hasLast;

        public void Add(bool ok, long rtt)
        {
            Sent++;
            if (!ok) return;
            Received++;
            Sum += rtt;
            if (rtt < Min) Min = rtt;
            if (rtt > Max) Max = rtt;
            if (hasLast) { JitterSum += Math.Abs(rtt - lastRtt); JitterCount++; }
            lastRtt = rtt; hasLast = true;
        }

        public long Lost { get { return Sent - Received; } }
        public double LossPct { get { return Sent == 0 ? 0 : 100.0 * Lost / Sent; } }
        public double Avg { get { return Received == 0 ? 0 : Sum / Received; } }
        public double Jitter { get { return JitterCount == 0 ? 0 : JitterSum / JitterCount; } }
        public bool HasPing { get { return Received > 0; } }

        public void Save(StringBuilder sb, int i)
        {
            var inv = CultureInfo.InvariantCulture;
            string p = "t" + i + ".";
            sb.Append(p).Append("sent=").Append(Sent).AppendLine();
            sb.Append(p).Append("received=").Append(Received).AppendLine();
            sb.Append(p).Append("min=").Append(Min).AppendLine();
            sb.Append(p).Append("max=").Append(Max).AppendLine();
            sb.Append(p).Append("sum=").Append(Sum.ToString("R", inv)).AppendLine();
            sb.Append(p).Append("jittersum=").Append(JitterSum.ToString("R", inv)).AppendLine();
            sb.Append(p).Append("jittercount=").Append(JitterCount).AppendLine();
        }

        public static Stats Parse(Dictionary<string, string> d, int i)
        {
            var inv = CultureInfo.InvariantCulture;
            string p = "t" + i + ".";
            var s = new Stats();
            s.Sent = long.Parse(d[p + "sent"], inv);
            s.Received = long.Parse(d[p + "received"], inv);
            s.Min = long.Parse(d[p + "min"], inv);
            s.Max = long.Parse(d[p + "max"], inv);
            s.Sum = double.Parse(d[p + "sum"], inv);
            s.JitterSum = double.Parse(d[p + "jittersum"], inv);
            s.JitterCount = long.Parse(d[p + "jittercount"], inv);
            return s;
        }
    }

    // ---------- Speicherung des Verlaufs ----------

    static class Storage
    {
        // Datenordner; über die Umgebungsvariable NETMONITOR_HOME umleitbar (z. B. für Tests oder portable Nutzung).
        public static readonly string Home = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NETMONITOR_HOME"))
            ? Environment.GetEnvironmentVariable("NETMONITOR_HOME")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NetMonitor");
        public static readonly string Dir = Path.Combine(Home, "Verlauf");
        public static readonly Encoding Utf8 = new UTF8Encoding(false);
        public const string TimeFormat = "yyyy-MM-ddTHH:mm:ss.fff";

        public static string Escape(string s)
        {
            return (s ?? "").Replace(";", ",").Replace("\r", " ").Replace("\n", " ");
        }

        static string SettingsPath { get { return Path.Combine(Home, "settings.txt"); } }

        public static Dictionary<string, string> LoadSettings()
        {
            var d = new Dictionary<string, string>();
            try
            {
                if (File.Exists(SettingsPath))
                    foreach (var line in File.ReadAllLines(SettingsPath, Utf8))
                    {
                        int eq = line.IndexOf('=');
                        if (eq > 0) d[line.Substring(0, eq)] = line.Substring(eq + 1);
                    }
            }
            catch { }
            return d;
        }

        public static void SaveSettings(Dictionary<string, string> d)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                var sb = new StringBuilder();
                foreach (var kv in d) sb.AppendLine(kv.Key + "=" + Escape(kv.Value));
                File.WriteAllText(SettingsPath, sb.ToString(), Utf8);
            }
            catch { }
        }
    }

    // Zeichnet eine laufende Sitzung (Start bis Stop) auf: Messwerte werden angehängt, Zusammenfassung überschrieben.
    class SessionRecorder
    {
        public readonly string Id;
        readonly string summaryPath, samplesPath;
        readonly DateTime start;
        readonly int interval, timeout;
        readonly string[] names, addrs;
        public readonly Stats[] Stats;
        readonly List<string> pending = new List<string>();

        public SessionRecorder(string[] names, string[] addrs, int interval, int timeout)
        {
            Directory.CreateDirectory(Storage.Dir);
            start = DateTime.Now;
            string id = start.ToString("yyyy-MM-dd_HH-mm-ss");
            for (int n = 2; File.Exists(Path.Combine(Storage.Dir, id + ".summary")); n++)
                id = start.ToString("yyyy-MM-dd_HH-mm-ss") + "_" + n;
            Id = id;
            summaryPath = Path.Combine(Storage.Dir, Id + ".summary");
            samplesPath = Path.Combine(Storage.Dir, Id + ".samples");
            this.names = names; this.addrs = addrs; this.interval = interval; this.timeout = timeout;
            Stats = new Stats[names.Length];
            for (int i = 0; i < Stats.Length; i++) Stats[i] = new Stats();
            Flush(true);
        }

        public void Add(int index, Sample s)
        {
            Stats[index].Add(s.Ok, s.Rtt);
            pending.Add(s.Time.Ticks.ToString(CultureInfo.InvariantCulture) + ";" + index + ";" +
                        (s.Ok ? s.Rtt : -1) + ";" + (s.Ok ? "" : Storage.Escape(s.Status)));
        }

        public void Flush(bool running)
        {
            if (pending.Count > 0)
            {
                File.AppendAllLines(samplesPath, pending, Storage.Utf8);
                pending.Clear();
            }
            var sb = new StringBuilder();
            sb.AppendLine("start=" + start.ToString(Storage.TimeFormat, CultureInfo.InvariantCulture));
            sb.AppendLine("end=" + DateTime.Now.ToString(Storage.TimeFormat, CultureInfo.InvariantCulture));
            sb.AppendLine("running=" + (running ? "1" : "0"));
            sb.AppendLine("interval=" + interval);
            sb.AppendLine("timeout=" + timeout);
            sb.AppendLine("count=" + names.Length);
            for (int i = 0; i < names.Length; i++)
            {
                sb.AppendLine("t" + i + ".name=" + names[i]);
                sb.AppendLine("t" + i + ".addr=" + addrs[i]);
                Stats[i].Save(sb, i);
            }
            string tmp = summaryPath + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), Storage.Utf8);
            if (File.Exists(summaryPath)) File.Delete(summaryPath);
            File.Move(tmp, summaryPath);
        }

        public void Finish()
        {
            bool any = false;
            foreach (var s in Stats) if (s.Sent > 0) any = true;
            if (any) { Flush(false); return; }
            // Leere Sitzung (sofort gestoppt) nicht im Verlauf behalten.
            if (File.Exists(summaryPath)) File.Delete(summaryPath);
            if (File.Exists(samplesPath)) File.Delete(samplesPath);
        }
    }

    public class HistoryEntry
    {
        public string Id, SummaryPath, SamplesPath;
        public DateTime Start, End;
        public bool Running;
        public int Interval, Timeout;
        public string[] Names, Addrs;
        public Stats[] Stats;

        public TimeSpan Duration { get { return End - Start; } }

        public static List<HistoryEntry> LoadAll()
        {
            var list = new List<HistoryEntry>();
            if (!Directory.Exists(Storage.Dir)) return list;
            foreach (var file in Directory.GetFiles(Storage.Dir, "*.summary"))
            {
                try { list.Add(Load(file)); }
                catch { /* beschädigte Datei überspringen */ }
            }
            list.Sort((a, b) => b.Start.CompareTo(a.Start));
            return list;
        }

        static HistoryEntry Load(string path)
        {
            var d = new Dictionary<string, string>();
            foreach (var line in File.ReadAllLines(path, Storage.Utf8))
            {
                int eq = line.IndexOf('=');
                if (eq > 0) d[line.Substring(0, eq)] = line.Substring(eq + 1);
            }
            var inv = CultureInfo.InvariantCulture;
            var e = new HistoryEntry
            {
                Id = Path.GetFileNameWithoutExtension(path),
                SummaryPath = path,
                SamplesPath = Path.ChangeExtension(path, ".samples"),
                Start = DateTime.ParseExact(d["start"], Storage.TimeFormat, inv),
                End = DateTime.ParseExact(d["end"], Storage.TimeFormat, inv),
                Running = d["running"] == "1",
                Interval = int.Parse(d["interval"], inv),
                Timeout = int.Parse(d["timeout"], inv)
            };
            int n = int.Parse(d["count"], inv);
            e.Names = new string[n]; e.Addrs = new string[n]; e.Stats = new Stats[n];
            for (int i = 0; i < n; i++)
            {
                e.Names[i] = d["t" + i + ".name"];
                e.Addrs[i] = d["t" + i + ".addr"];
                e.Stats[i] = NetMonitor.Stats.Parse(d, i);
            }
            return e;
        }

        public List<Sample>[] LoadSamples()
        {
            var result = new List<Sample>[Names.Length];
            for (int i = 0; i < result.Length; i++) result[i] = new List<Sample>();
            if (File.Exists(SamplesPath))
            {
                foreach (var line in File.ReadLines(SamplesPath, Storage.Utf8))
                {
                    var p = line.Split(';');
                    if (p.Length < 4) continue;
                    int idx;
                    long ticks, rtt;
                    if (!long.TryParse(p[0], out ticks) || !int.TryParse(p[1], out idx) ||
                        !long.TryParse(p[2], out rtt) || idx < 0 || idx >= result.Length) continue;
                    result[idx].Add(new Sample { Time = new DateTime(ticks), Ok = rtt >= 0, Rtt = rtt, Status = rtt >= 0 ? "OK" : p[3] });
                }
            }
            foreach (var list in result)
            {
                list.Sort((a, b) => a.Time.CompareTo(b.Time));
                Sample.ComputeLoss60(list);
            }
            return result;
        }

        public long SamplesLength
        {
            get { try { return File.Exists(SamplesPath) ? new FileInfo(SamplesPath).Length : 0; } catch { return 0; } }
        }

        public void Delete()
        {
            if (File.Exists(SummaryPath)) File.Delete(SummaryPath);
            if (File.Exists(SamplesPath)) File.Delete(SamplesPath);
        }
    }

    // ---------- Auswertung der Paketverluste ----------

    public enum LossCause { HomeNetwork, Internet, SingleService, TargetServer }

    // Eine Störung: zusammenhängende Paketverluste (mehrere Ziele, kurz hintereinander) –
    // oder, zusammengefasst, eine Serie mehrerer Störungen.
    public class LossIncident
    {
        public DateTime Start, End;
        public string SessionId;
        public string[] Names;
        public int[] Lost;
        public int Count = 1;
        public LossCause Cause;

        public TimeSpan Duration { get { return End - Start; } }

        public int TotalLost
        {
            get { int n = 0; foreach (var l in Lost) n += l; return n; }
        }

        public bool Has(int i) { return i < Lost.Length && Lost[i] > 0; }

        public string Affected
        {
            get
            {
                var list = new List<string>();
                for (int i = 0; i < Lost.Length; i++)
                    if (Lost[i] > 0) list.Add(Names[i] + " (" + Lost[i] + ")");
                return string.Join(", ", list);
            }
        }

        public string CauseText
        {
            get
            {
                switch (Cause)
                {
                    case LossCause.HomeNetwork: return L.P("Heimnetz (WLAN / LAN / Router)", "Home network (Wi-Fi / LAN / router)");
                    case LossCause.Internet: return L.P("Internet / Provider", "Internet / ISP");
                    case LossCause.SingleService:
                        for (int i = 0; i < 2; i++) if (Has(i)) return L.P("Einzelner Dienst (", "Single service (") + Names[i] + ")";
                        return L.P("Einzelner Dienst", "Single service");
                    default:
                        for (int i = 3; i < Lost.Length; i++) if (Has(i)) return L.P("Nur Ziel-Server (", "Target server only (") + Names[i] + ")";
                        return L.P("Nur Ziel-Server", "Target server only");
                }
            }
        }

        public void Classify()
        {
            bool home = Has(2), e0 = Has(0), e1 = Has(1), other = Has(3) || Has(4);
            if (home) Cause = LossCause.HomeNetwork;
            else if (e0 && e1) Cause = LossCause.Internet;
            else if ((e0 || e1) && other) Cause = LossCause.Internet;
            else if (e0 || e1) Cause = LossCause.SingleService;
            else Cause = LossCause.TargetServer;
        }
    }

    static class LossAnalysis
    {
        static readonly Dictionary<string, Tuple<long, List<LossIncident>>> cache = new Dictionary<string, Tuple<long, List<LossIncident>>>();
        static readonly object sync = new object();

        // Störungen einer Sitzung; Ziele, die (fast) nie erreichbar waren, zählen nicht als Störung.
        public static List<LossIncident> ForSession(HistoryEntry e)
        {
            long len = e.SamplesLength;
            lock (sync)
            {
                Tuple<long, List<LossIncident>> hit;
                if (cache.TryGetValue(e.Id, out hit) && hit.Item1 == len) return hit.Item2;
            }
            var samples = e.LoadSamples();
            var result = Compute(e, samples);
            lock (sync) cache[e.Id] = Tuple.Create(len, result);
            return result;
        }

        public static List<LossIncident> Compute(HistoryEntry e, List<Sample>[] samples)
        {
            int n = e.Names.Length;
            var lost = new List<KeyValuePair<DateTime, int>>();
            for (int i = 0; i < n && i < samples.Length; i++)
            {
                if (e.Stats[i].Sent == 0 || e.Stats[i].LossPct > 95) continue;
                foreach (var s in samples[i]) if (!s.Ok) lost.Add(new KeyValuePair<DateTime, int>(s.Time, i));
            }
            lost.Sort((a, b) => a.Key.CompareTo(b.Key));
            var gap = TimeSpan.FromMilliseconds(Math.Max(3 * e.Interval, 5000));
            var step = TimeSpan.FromMilliseconds(Math.Max(e.Interval, 200));
            var result = new List<LossIncident>();
            LossIncident cur = null;
            foreach (var kv in lost)
            {
                if (cur == null || kv.Key - cur.End > gap)
                {
                    cur = new LossIncident { Start = kv.Key, End = kv.Key + step, SessionId = e.Id, Names = e.Names, Lost = new int[n] };
                    result.Add(cur);
                }
                else if (kv.Key + step > cur.End) cur.End = kv.Key + step;
                cur.Lost[kv.Value]++;
            }
            foreach (var inc in result) inc.Classify();
            return result;
        }

        // Fasst Störungen, die weniger als "gap" auseinanderliegen, zu Serien zusammen.
        public static List<LossIncident> Group(List<LossIncident> sortedAsc, TimeSpan gap)
        {
            var result = new List<LossIncident>();
            LossIncident cur = null, worst = null;
            foreach (var inc in sortedAsc)
            {
                if (cur == null || inc.Start - cur.End > gap || inc.Lost.Length != cur.Lost.Length)
                {
                    if (cur != null) cur.Cause = worst.Cause;
                    cur = new LossIncident
                    {
                        Start = inc.Start, End = inc.End, SessionId = inc.SessionId, Names = inc.Names,
                        Lost = (int[])inc.Lost.Clone(), Count = inc.Count, Cause = inc.Cause
                    };
                    worst = inc;
                    result.Add(cur);
                    continue;
                }
                if (inc.End > cur.End) cur.End = inc.End;
                for (int i = 0; i < inc.Lost.Length; i++) cur.Lost[i] += inc.Lost[i];
                cur.Count += inc.Count;
                if (inc.TotalLost > worst.TotalLost) worst = inc;
            }
            if (cur != null) cur.Cause = worst.Cause;
            return result;
        }
    }

    static class CsvExport
    {
        public static void Write(string path, List<string> meta, string[] names, string[] addrs,
                                 Stats[] stats, List<Sample>[] samples)
        {
            var ci = CultureInfo.CurrentCulture;
            var sb = new StringBuilder();
            sb.AppendLine("NetMonitor Export");
            sb.AppendLine(L.P("Erstellt;", "Created;") + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            foreach (var m in meta) sb.AppendLine(m);
            sb.AppendLine();
            sb.AppendLine(L.P("ZUSAMMENFASSUNG", "SUMMARY"));
            sb.AppendLine(L.P("Ziel;Adresse;Gesendet;Empfangen;Verloren;Verlust %;Ø Ping ms;Min ms;Max ms;Jitter ms",
                              "Target;Address;Sent;Received;Lost;Loss %;Avg ping ms;Min ms;Max ms;Jitter ms"));
            for (int i = 0; i < names.Length; i++)
            {
                var s = stats[i];
                if (s.Sent == 0 && string.IsNullOrEmpty(addrs[i])) continue;
                sb.AppendLine(string.Join(";", new[]
                {
                    names[i], addrs[i], s.Sent.ToString(), s.Received.ToString(), s.Lost.ToString(),
                    s.LossPct.ToString("0.00", ci),
                    s.HasPing ? s.Avg.ToString("0.00", ci) : "",
                    s.HasPing ? s.Min.ToString() : "",
                    s.HasPing ? s.Max.ToString() : "",
                    s.JitterCount > 0 ? s.Jitter.ToString("0.00", ci) : ""
                }));
            }
            sb.AppendLine();
            sb.AppendLine(L.P("MESSWERTE", "SAMPLES"));
            sb.AppendLine(L.P("Zeitstempel;Ziel;Adresse;Ergebnis;Ping ms;Verlust letzte 60 %;Status",
                              "Timestamp;Target;Address;Result;Ping ms;Loss last 60 %;Status"));

            var all = new List<KeyValuePair<int, Sample>>();
            for (int i = 0; i < samples.Length; i++)
                foreach (var s in samples[i]) all.Add(new KeyValuePair<int, Sample>(i, s));
            all.Sort((a, b) => a.Value.Time.CompareTo(b.Value.Time));
            foreach (var kv in all)
            {
                var s = kv.Value;
                sb.AppendLine(string.Join(";", new[]
                {
                    s.Time.ToString("yyyy-MM-dd HH:mm:ss.fff"), names[kv.Key], addrs[kv.Key],
                    s.Ok ? "OK" : L.P("VERLUST", "LOST"), s.Ok ? s.Rtt.ToString() : "",
                    s.Loss60.ToString("0.0", ci), Storage.Escape(s.Status)
                }));
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        public static void WriteIncidents(string path, List<LossIncident> incidents)
        {
            var sb = new StringBuilder();
            sb.AppendLine(L.P("Datum;Start;Ende;Dauer s;Störungen;Verlorene Pakete;Betroffene Ziele;Vermutete Ursache",
                              "Date;Start;End;Duration s;Incidents;Lost packets;Affected targets;Likely cause"));
            foreach (var inc in incidents)
                sb.AppendLine(string.Join(";", new[]
                {
                    inc.Start.ToString("yyyy-MM-dd"), inc.Start.ToString("HH:mm:ss"), inc.End.ToString("HH:mm:ss"),
                    ((int)Math.Round(inc.Duration.TotalSeconds)).ToString(), inc.Count.ToString(), inc.TotalLost.ToString(),
                    Storage.Escape(inc.Affected), inc.CauseText
                }));
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        // Fragt nach dem Speicherort, schreibt die CSV (+ optional ein PNG des Diagramms) und meldet das Ergebnis.
        public static void ExportWithDialog(IWin32Window owner, string defaultName, Control image, Action<string> writeCsv)
        {
            using (var dlg = new SaveFileDialog
            {
                Filter = L.P("CSV-Datei (*.csv)|*.csv", "CSV file (*.csv)|*.csv"),
                FileName = defaultName,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            })
            {
                if (dlg.ShowDialog(owner) != DialogResult.OK) return;
                try
                {
                    writeCsv(dlg.FileName);
                    string msg = dlg.FileName;
                    if (image != null && image.Width > 0 && image.Height > 0)
                    {
                        string png = Path.ChangeExtension(dlg.FileName, ".png");
                        using (var bmp = new Bitmap(image.Width, image.Height))
                        {
                            image.DrawToBitmap(bmp, new Rectangle(0, 0, image.Width, image.Height));
                            bmp.Save(png, System.Drawing.Imaging.ImageFormat.Png);
                        }
                        msg += "\n" + png;
                    }
                    MessageBox.Show(owner, L.P("Export gespeichert:\n\n", "Export saved:\n\n") + msg, "NetMonitor",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(owner, L.P("Export fehlgeschlagen:\n", "Export failed:\n") + ex.Message, "NetMonitor",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }

    // ---------- Spiel-Server-Erkennung ----------

    // Ein laufendes Programm mit Fenster (Auswahl im Dropdown der Spiel-Karte).
    class ProcItem
    {
        public string Name, Title;

        static readonly HashSet<string> Ignore = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "explorer", "ApplicationFrameHost", "TextInputHost", "SystemSettings", "ShellExperienceHost",
            "SearchHost", "StartMenuExperienceHost", "LockApp", "powershell", "cmd", "conhost", "WindowsTerminal"
        };

        public override string ToString() { return Title + "   (" + Name + ".exe)"; }

        public int[] CurrentPids()
        {
            var list = new List<int>();
            foreach (var p in System.Diagnostics.Process.GetProcessesByName(Name)) { list.Add(p.Id); p.Dispose(); }
            return list.ToArray();
        }

        public static List<ProcItem> ListWindowed()
        {
            var byName = new Dictionary<string, ProcItem>(StringComparer.OrdinalIgnoreCase);
            int own = System.Diagnostics.Process.GetCurrentProcess().Id;
            foreach (var p in System.Diagnostics.Process.GetProcesses())
            {
                try
                {
                    if (p.Id == own || Ignore.Contains(p.ProcessName) || p.MainWindowHandle == IntPtr.Zero) continue;
                    string title = p.MainWindowTitle;
                    if (string.IsNullOrEmpty(title) || byName.ContainsKey(p.ProcessName)) continue;
                    if (title.Length > 45) title = title.Substring(0, 44) + "…";
                    byName[p.ProcessName] = new ProcItem { Name = p.ProcessName, Title = title };
                }
                catch { }
                finally { p.Dispose(); }
            }
            var list = new List<ProcItem>(byName.Values);
            list.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase));
            return list;
        }
    }

    // Eine Gegenstelle, mit der das Spiel während der Analyse kommuniziert hat.
    class Candidate
    {
        public string Proto, Ip;
        public int Port;
        public long PacketsOut, PacketsIn, Bytes;
        public double Rate, Score;
        public bool? Answers;
        public long PingMs;

        // Liest das Ergebnis des Hilfsprozesses und sortiert nach Wahrscheinlichkeit „Spielserver“.
        public static List<Candidate> Load(string file, out string error)
        {
            error = null;
            var result = new List<Candidate>();
            if (!File.Exists(file)) { error = L.P("Die Analyse hat kein Ergebnis geliefert.", "The analysis returned no result."); return result; }
            var lines = File.ReadAllLines(file, Storage.Utf8);
            if (lines.Length == 0 || !lines[0].StartsWith("ok;"))
            {
                error = lines.Length > 0 && lines[0].StartsWith("error;") ? lines[0].Substring(6)
                    : L.P("Unbekanntes Ergebnis der Analyse.", "Unknown analysis result.");
                return result;
            }
            double seconds = double.Parse(lines[0].Split(';')[1], CultureInfo.InvariantCulture);
            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].StartsWith("#")) continue;
                var p = lines[i].Split(';');
                if (p.Length < 6) continue;
                var c = new Candidate
                {
                    Proto = p[0], Ip = p[1], Port = int.Parse(p[2]),
                    PacketsOut = long.Parse(p[3]), PacketsIn = long.Parse(p[4]), Bytes = long.Parse(p[5])
                };
                IPAddress ip;
                if (!IPAddress.TryParse(c.Ip, out ip) || NetTools.IsPrivate(ip)) continue;
                if (c.Port == 53 || c.Port == 80 || c.Port == 443 || c.Port == 853) continue; // DNS / Web / QUIC
                long total = c.PacketsIn + c.PacketsOut;
                if (total < 5) continue;
                c.Rate = total / seconds;
                c.Score = c.Rate * (c.Proto == "UDP" ? 1.0 : 0.3) * (c.PacketsIn > 0 && c.PacketsOut > 0 ? 1.0 : 0.2);
                result.Add(c);
            }
            result.Sort((a, b) => b.Score.CompareTo(a.Score));
            if (result.Count > 6) result.RemoveRange(6, result.Count - 6);
            return result;
        }
    }

    static class NetTools
    {
        // IPv4-Standardgateway (Router) der aktiven Netzwerkverbindung, oder null.
        public static string DefaultGateway()
        {
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var gw in nic.GetIPProperties().GatewayAddresses)
                        if (gw.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !gw.Address.Equals(IPAddress.Any))
                            return gw.Address.ToString();
                }
            }
            catch { }
            return null;
        }

        public static bool IsPrivate(IPAddress ip)
        {
            if (IPAddress.IsLoopback(ip)) return true;
            var b = ip.GetAddressBytes();
            if (b.Length == 4)
                return b[0] == 10 || b[0] == 127 || b[0] == 0 || b[0] >= 224 ||
                       (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) ||
                       (b[0] == 169 && b[1] == 254) || (b[0] == 100 && b[1] >= 64 && b[1] <= 127);
            return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast || (b[0] & 0xFE) == 0xFC;
        }

        public static async Task<long> PingOnce(string ip, int ttl)
        {
            try
            {
                using (var p = new Ping())
                {
                    var r = await p.SendPingAsync(ip, 1000, new byte[32], new PingOptions(ttl, true));
                    return r.Status == IPStatus.Success ? r.RoundtripTime : -1;
                }
            }
            catch { return -1; }
        }

        // Ping-Test mit bis zu 3 Versuchen; liefert die Antwortzeit oder -1.
        public static async Task<long> Test(string ip)
        {
            for (int i = 0; i < 3; i++)
            {
                long ms = await PingOnce(ip, 128);
                if (ms >= 0) return ms;
            }
            return -1;
        }

        // Traceroute: letzter öffentlicher Router vor dem Ziel, der selbst auf Ping antwortet.
        public static async Task<Tuple<string, int>> FindLastHop(string ip, Action<int> progress)
        {
            var hops = new List<Tuple<string, int>>();
            int misses = 0;
            for (int ttl = 1; ttl <= 30; ttl++)
            {
                progress(ttl);
                PingReply r = null;
                try { using (var p = new Ping()) r = await p.SendPingAsync(ip, 1000, new byte[32], new PingOptions(ttl, true)); }
                catch { }
                if (r != null && r.Status == IPStatus.Success) return Tuple.Create(ip, ttl);
                if (r != null && r.Status == IPStatus.TtlExpired && r.Address != null)
                {
                    if (!IsPrivate(r.Address)) hops.Add(Tuple.Create(r.Address.ToString(), ttl));
                    misses = 0;
                }
                else if (++misses >= 4 && hops.Count > 0) break;
            }
            for (int i = hops.Count - 1; i >= 0; i--)
                if (await Test(hops[i].Item1) >= 0) return hops[i];
            return null;
        }
    }

    // Beobachtet über Windows-ETW (Microsoft-Windows-Kernel-Network), mit welchen Gegenstellen ein Prozess
    // TCP/UDP-Pakete austauscht. Läuft im kurz mit Adminrechten gestarteten Hilfsprozess.
    public static class Detector
    {
        static readonly Guid KernelNetwork = new Guid("7DD42A49-5329-4832-8DFD-43D979153A88");
        const string SessionName = "NetMonitorCapture";
        const uint ControlStop = 1;

        [DllImport("advapi32.dll", EntryPoint = "StartTraceW", CharSet = CharSet.Unicode)]
        static extern uint StartTrace(out ulong handle, string name, IntPtr properties);
        [DllImport("advapi32.dll", EntryPoint = "ControlTraceW", CharSet = CharSet.Unicode)]
        static extern uint ControlTrace(ulong handle, string name, IntPtr properties, uint code);
        [DllImport("advapi32.dll")]
        static extern uint EnableTraceEx2(ulong handle, ref Guid provider, uint control, byte level,
                                          ulong matchAny, ulong matchAll, uint timeout, IntPtr parameters);
        [DllImport("advapi32.dll", EntryPoint = "OpenTraceW", SetLastError = true)]
        static extern ulong OpenTrace(IntPtr logfile);
        [DllImport("advapi32.dll")]
        static extern uint ProcessTrace(ulong[] handles, uint count, IntPtr start, IntPtr end);
        [DllImport("advapi32.dll")]
        static extern uint CloseTrace(ulong handle);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        delegate void RecordCallback(IntPtr record);

        public static void Capture(int[] pids, int seconds, string outFile)
        {
            L.Load();
            List<string> lines;
            try { lines = Run(pids, seconds); }
            catch (Exception ex) { lines = new List<string> { "error;" + Storage.Escape(ex.Message) }; }
            File.WriteAllLines(outFile, lines, Storage.Utf8);
        }

        static IntPtr AllocZero(int size)
        {
            IntPtr p = Marshal.AllocHGlobal(size);
            for (int i = 0; i < size; i++) Marshal.WriteByte(p, i, 0);
            return p;
        }

        // EVENT_TRACE_PROPERTIES (x64) für eine Echtzeit-Sitzung; der Sitzungsname folgt direkt dahinter.
        static IntPtr NewProperties()
        {
            const int nameOffset = 120;
            int size = nameOffset + 1024;
            IntPtr p = AllocZero(size);
            Marshal.WriteInt32(p, 0, size);              // Wnode.BufferSize
            Marshal.Copy(Guid.NewGuid().ToByteArray(), 0, p + 24, 16); // Wnode.Guid
            Marshal.WriteInt32(p, 40, 1);                // Wnode.ClientContext = QPC
            Marshal.WriteInt32(p, 44, 0x00020000);       // WNODE_FLAG_TRACED_GUID
            Marshal.WriteInt32(p, 48, 64);               // BufferSize (KB)
            Marshal.WriteInt32(p, 64, 0x100);            // EVENT_TRACE_REAL_TIME_MODE
            Marshal.WriteInt32(p, 68, 1);                // FlushTimer (s)
            Marshal.WriteInt32(p, 116, nameOffset);      // LoggerNameOffset
            return p;
        }

        static List<string> Run(int[] pids, int seconds)
        {
            if (IntPtr.Size != 8) throw new InvalidOperationException(L.P("Die Erkennung benötigt 64-Bit-PowerShell.", "Detection requires 64-bit PowerShell."));
            var pidSet = new HashSet<int>(pids);
            var local = new HashSet<string>();
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                foreach (var a in nic.GetIPProperties().UnicastAddresses) local.Add(a.Address.ToString());
            var stats = new Dictionary<string, long[]>(); // "Proto;IP;Port" -> gesendet, empfangen, Bytes
            long total = 0, matched = 0;

            IntPtr props = NewProperties();
            ulong session;
            uint err = StartTrace(out session, SessionName, props);
            if (err == 183) // Sitzung existiert noch (z. B. nach Absturz) -> beenden und neu starten
            {
                ControlTrace(0, SessionName, props, ControlStop);
                Marshal.FreeHGlobal(props);
                props = NewProperties();
                err = StartTrace(out session, SessionName, props);
            }
            if (err == 5) { Marshal.FreeHGlobal(props); throw new UnauthorizedAccessException(L.P("Adminrechte erforderlich.", "Administrator rights required.")); }
            if (err != 0) { Marshal.FreeHGlobal(props); throw new System.ComponentModel.Win32Exception((int)err); }

            IntPtr logfile = IntPtr.Zero, namePtr = IntPtr.Zero;
            ulong trace = ulong.MaxValue;
            System.Threading.Thread thread = null;
            RecordCallback callback = rec =>
            {
                try
                {
                    var provider = (Guid)Marshal.PtrToStructure(rec + 24, typeof(Guid));
                    if (provider != KernelNetwork) return;
                    int id = (ushort)Marshal.ReadInt16(rec, 40);
                    bool udp, v6, send;
                    switch (id)
                    {
                        case 10: case 11: udp = false; v6 = false; break;
                        case 26: case 27: udp = false; v6 = true; break;
                        case 42: case 43: udp = true; v6 = false; break;
                        case 58: case 59: udp = true; v6 = true; break;
                        default: return;
                    }
                    send = id == 10 || id == 26 || id == 42 || id == 58;
                    total++;
                    int len = (ushort)Marshal.ReadInt16(rec, 86);
                    IntPtr ud = Marshal.ReadIntPtr(rec, 96);
                    int alen = v6 ? 16 : 4;
                    if (ud == IntPtr.Zero || len < 12 + 2 * alen) return;
                    if (!pidSet.Contains(Marshal.ReadInt32(ud, 0))) return;
                    int bytes = Marshal.ReadInt32(ud, 4);
                    var d = new byte[alen]; var s = new byte[alen];
                    Marshal.Copy(ud + 8, d, 0, alen);
                    Marshal.Copy(ud + 8 + alen, s, 0, alen);
                    int po = 8 + 2 * alen; // Ports in Netzwerk-Byte-Reihenfolge
                    int dport = (Marshal.ReadByte(ud, po) << 8) | Marshal.ReadByte(ud, po + 1);
                    int sport = (Marshal.ReadByte(ud, po + 2) << 8) | Marshal.ReadByte(ud, po + 3);
                    var dip = new IPAddress(d); var sip = new IPAddress(s);
                    bool destIsLocal = local.Contains(dip.ToString());
                    string ip = destIsLocal ? sip.ToString() : dip.ToString();
                    int port = destIsLocal ? sport : dport;
                    matched++;
                    string key = (udp ? "UDP" : "TCP") + ";" + ip + ";" + port;
                    long[] acc;
                    if (!stats.TryGetValue(key, out acc)) stats[key] = acc = new long[3];
                    if (send) acc[0]++; else acc[1]++;
                    acc[2] += bytes;
                }
                catch { }
            };
            try
            {
                Guid providerId = KernelNetwork;
                err = EnableTraceEx2(session, ref providerId, 1, 5, ulong.MaxValue, 0, 0, IntPtr.Zero);
                if (err != 0) throw new System.ComponentModel.Win32Exception((int)err);

                // EVENT_TRACE_LOGFILEW (x64, 448 Bytes): LoggerName @8, ProcessTraceMode @28, EventRecordCallback @424
                logfile = AllocZero(448);
                namePtr = Marshal.StringToHGlobalUni(SessionName);
                Marshal.WriteIntPtr(logfile, 8, namePtr);
                Marshal.WriteInt32(logfile, 28, 0x100 | 0x10000000); // REAL_TIME | EVENT_RECORD
                Marshal.WriteIntPtr(logfile, 424, Marshal.GetFunctionPointerForDelegate(callback));
                trace = OpenTrace(logfile);
                if (trace == ulong.MaxValue) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                ulong h = trace;
                thread = new System.Threading.Thread(() => ProcessTrace(new[] { h }, 1, IntPtr.Zero, IntPtr.Zero)) { IsBackground = true };
                thread.Start();
                System.Threading.Thread.Sleep(seconds * 1000);
            }
            finally
            {
                ControlTrace(0, SessionName, props, ControlStop);
                if (thread != null) thread.Join(5000);
                if (trace != ulong.MaxValue) CloseTrace(trace);
                if (thread != null) thread.Join(2000);
                Marshal.FreeHGlobal(props);
                if (logfile != IntPtr.Zero) Marshal.FreeHGlobal(logfile);
                if (namePtr != IntPtr.Zero) Marshal.FreeHGlobal(namePtr);
                GC.KeepAlive(callback);
            }

            var lines = new List<string> { "ok;" + seconds + ";" + total + ";" + matched };
            foreach (var kv in stats)
                lines.Add(kv.Key + ";" + kv.Value[0] + ";" + kv.Value[1] + ";" + kv.Value[2]);
            return lines;
        }
    }

    public static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        public const string Version = "1.0.0";

        // Pfad zu NetMonitor.ps1 – wird für den Hilfsprozess der Spiel-Erkennung benötigt.
        public static string ScriptPath;

        static void Prepare(string scriptPath)
        {
            ScriptPath = scriptPath;
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            try { Application.SetCompatibleTextRenderingDefault(false); } catch (InvalidOperationException) { }
            Theme.Init();
            L.Load();
        }

        [STAThread]
        public static void Run(string scriptPath)
        {
            Prepare(scriptPath);
            Application.Run(new MainForm());
        }

        [STAThread]
        public static void Setup(string scriptPath)
        {
            Prepare(scriptPath);
            Application.Run(new SetupForm(Path.GetDirectoryName(scriptPath)));
        }

        [STAThread]
        public static void Uninstall(string scriptPath)
        {
            Prepare(scriptPath);
            Application.Run(new UninstallForm(Path.GetDirectoryName(scriptPath)));
        }
    }
}
