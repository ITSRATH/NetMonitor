// NetMonitor (macOS/Linux) – Messwerte, Statistik, Speicherung und Verlauf.
// Die Dateiformate sind identisch mit der Windows-Version, Verläufe sind also austauschbar.
using System.Globalization;
using System.Text;

namespace NetMonitor;

public class Sample
{
    public DateTime Time;
    public bool Ok;
    public long Rtt;
    public string Status = "";
    public double Loss60;

    public const int WindowSize = 60;

    // Gleitender Verlust über die letzten 60 Messungen (für geladene Sitzungen).
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

    public long Lost => Sent - Received;
    public double LossPct => Sent == 0 ? 0 : 100.0 * Lost / Sent;
    public double Avg => Received == 0 ? 0 : Sum / Received;
    public double Jitter => JitterCount == 0 ? 0 : JitterSum / JitterCount;
    public bool HasPing => Received > 0;

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
        return new Stats
        {
            Sent = long.Parse(d[p + "sent"], inv),
            Received = long.Parse(d[p + "received"], inv),
            Min = long.Parse(d[p + "min"], inv),
            Max = long.Parse(d[p + "max"], inv),
            Sum = double.Parse(d[p + "sum"], inv),
            JitterSum = double.Parse(d[p + "jittersum"], inv),
            JitterCount = long.Parse(d[p + "jittercount"], inv)
        };
    }
}

public struct ChartPoint
{
    public long T;
    public float Y;     // NaN = kein Messwert
    public float Loss;  // 0..1
}

public class ChartSeries
{
    public string Name = "";
    public Avalonia.Media.Color Color;
    public bool Visible = true;
    public readonly List<ChartPoint> Points = new();
}

// ---------- Speicherung ----------

static class Storage
{
    // Datenordner je System; über NETMONITOR_HOME umleitbar (z. B. für Tests oder portable Nutzung).
    public static readonly string Home = ResolveHome();
    public static readonly string Dir = Path.Combine(Home, "Verlauf");
    public static readonly Encoding Utf8 = new UTF8Encoding(false);
    public const string TimeFormat = "yyyy-MM-ddTHH:mm:ss.fff";

    static string ResolveHome()
    {
        string env = Environment.GetEnvironmentVariable("NETMONITOR_HOME");
        if (!string.IsNullOrEmpty(env)) return env;
        string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsMacOS()) return Path.Combine(user, "Library", "Application Support", "NetMonitor");
        if (OperatingSystem.IsLinux())
        {
            string xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            return Path.Combine(string.IsNullOrEmpty(xdg) ? Path.Combine(user, ".local", "share") : xdg, "NetMonitor");
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NetMonitor");
    }

    // Für Anzeigen: Benutzerordner durch ~ ersetzen.
    public static string Display(string path)
    {
        string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return path.StartsWith(user, StringComparison.Ordinal) ? "~" + path.Substring(user.Length) : path;
    }

    public static string Escape(string s) => (s ?? "").Replace(";", ",").Replace("\r", " ").Replace("\n", " ");

    static string SettingsPath => Path.Combine(Home, "settings.txt");

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
            Directory.CreateDirectory(Home);
            var sb = new StringBuilder();
            foreach (var kv in d) sb.AppendLine(kv.Key + "=" + Escape(kv.Value));
            File.WriteAllText(SettingsPath, sb.ToString(), Utf8);
        }
        catch { }
    }

    public static void SaveSetting(string key, string value)
    {
        var d = LoadSettings();
        d[key] = value;
        SaveSettings(d);
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
    readonly List<string> pending = new();

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
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("start=" + start.ToString(Storage.TimeFormat, inv));
        sb.AppendLine("end=" + DateTime.Now.ToString(Storage.TimeFormat, inv));
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
        File.Move(tmp, summaryPath, true);
    }

    public void Finish()
    {
        bool any = Stats.Any(s => s.Sent > 0);
        if (any) { Flush(false); return; }
        // Leere Sitzung (sofort gestoppt) nicht im Verlauf behalten.
        if (File.Exists(summaryPath)) File.Delete(summaryPath);
        if (File.Exists(samplesPath)) File.Delete(samplesPath);
    }
}

public class HistoryEntry
{
    public string Id = "", SummaryPath = "", SamplesPath = "";
    public DateTime Start, End;
    public bool Running;
    public int Interval, Timeout;
    public string[] Names = Array.Empty<string>(), Addrs = Array.Empty<string>();
    public Stats[] Stats = Array.Empty<Stats>();

    public TimeSpan Duration => End - Start;

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
                if (!long.TryParse(p[0], out long ticks) || !int.TryParse(p[1], out int idx) ||
                    !long.TryParse(p[2], out long rtt) || idx < 0 || idx >= result.Length) continue;
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

// Eine Störung: zusammenhängende Paketverluste – oder, zusammengefasst, eine Serie mehrerer Störungen.
public class LossIncident
{
    public DateTime Start, End;
    public string SessionId = "";
    public string[] Names = Array.Empty<string>();
    public int[] Lost = Array.Empty<int>();
    public int Count = 1;
    public LossCause Cause;

    public TimeSpan Duration => End - Start;
    public int TotalLost => Lost.Sum();
    public bool Has(int i) => i < Lost.Length && Lost[i] > 0;

    public string Affected => string.Join(", ", Enumerable.Range(0, Lost.Length).Where(i => Lost[i] > 0).Select(i => Names[i] + " (" + Lost[i] + ")"));

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
    static readonly Dictionary<string, (long Key, List<LossIncident> List)> cache = new();
    static readonly object sync = new();

    public static List<LossIncident> ForSession(HistoryEntry e)
    {
        long key = e.SamplesLength + 1000000000L * SpeedStore.Windows().Count; // neue Speedtests machen den Cache ungültig
        lock (sync)
            if (cache.TryGetValue(e.Id, out var hit) && hit.Key == key) return hit.List;
        var result = Compute(e, e.LoadSamples());
        lock (sync) cache[e.Id] = (key, result);
        return result;
    }

    // Ziele, die (fast) nie erreichbar waren, und Verluste während eines Speedtests zählen nicht als Störung.
    public static List<LossIncident> Compute(HistoryEntry e, List<Sample>[] samples)
    {
        int n = e.Names.Length;
        var lost = new List<(DateTime Time, int Index)>();
        var speedtests = SpeedStore.Windows();
        for (int i = 0; i < n && i < samples.Length; i++)
        {
            if (e.Stats[i].Sent == 0 || e.Stats[i].LossPct > 95) continue;
            foreach (var s in samples[i])
                if (!s.Ok && !speedtests.Any(w => s.Time >= w.Start && s.Time <= w.End)) lost.Add((s.Time, i));
        }
        lost.Sort((a, b) => a.Time.CompareTo(b.Time));
        var gap = TimeSpan.FromMilliseconds(Math.Max(3 * e.Interval, 5000));
        var step = TimeSpan.FromMilliseconds(Math.Max(e.Interval, 200));
        var result = new List<LossIncident>();
        LossIncident cur = null;
        foreach (var (time, index) in lost)
        {
            if (cur == null || time - cur.End > gap)
            {
                cur = new LossIncident { Start = time, End = time + step, SessionId = e.Id, Names = e.Names, Lost = new int[n] };
                result.Add(cur);
            }
            else if (time + step > cur.End) cur.End = time + step;
            cur.Lost[index]++;
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
    public static void Write(string path, List<string> meta, string[] names, string[] addrs, Stats[] stats, List<Sample>[] samples)
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
            sb.AppendLine(string.Join(";", names[i], addrs[i], s.Sent, s.Received, s.Lost, s.LossPct.ToString("0.00", ci),
                s.HasPing ? s.Avg.ToString("0.00", ci) : "", s.HasPing ? s.Min.ToString() : "", s.HasPing ? s.Max.ToString() : "",
                s.JitterCount > 0 ? s.Jitter.ToString("0.00", ci) : ""));
        }
        sb.AppendLine();
        sb.AppendLine(L.P("MESSWERTE", "SAMPLES"));
        sb.AppendLine(L.P("Zeitstempel;Ziel;Adresse;Ergebnis;Ping ms;Verlust letzte 60 %;Status",
                          "Timestamp;Target;Address;Result;Ping ms;Loss last 60 %;Status"));
        var all = new List<(int Index, Sample S)>();
        for (int i = 0; i < samples.Length; i++)
            foreach (var s in samples[i]) all.Add((i, s));
        all.Sort((a, b) => a.S.Time.CompareTo(b.S.Time));
        foreach (var (index, s) in all)
            sb.AppendLine(string.Join(";", s.Time.ToString("yyyy-MM-dd HH:mm:ss.fff"), names[index], addrs[index],
                s.Ok ? "OK" : L.P("VERLUST", "LOST"), s.Ok ? s.Rtt.ToString() : "", s.Loss60.ToString("0.0", ci), Storage.Escape(s.Status)));
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    public static void WriteIncidents(string path, List<LossIncident> incidents)
    {
        var sb = new StringBuilder();
        sb.AppendLine(L.P("Datum;Start;Ende;Dauer s;Störungen;Verlorene Pakete;Betroffene Ziele;Vermutete Ursache",
                          "Date;Start;End;Duration s;Incidents;Lost packets;Affected targets;Likely cause"));
        foreach (var inc in incidents)
            sb.AppendLine(string.Join(";", inc.Start.ToString("yyyy-MM-dd"), inc.Start.ToString("HH:mm:ss"), inc.End.ToString("HH:mm:ss"),
                (int)Math.Round(inc.Duration.TotalSeconds), inc.Count, inc.TotalLost, Storage.Escape(inc.Affected), inc.CauseText));
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }
}
