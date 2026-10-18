// NetMonitor (macOS/Linux) – Netzwerk-Hilfen: Gateway, Ping/Traceroute, Verbindungsart und Spielserver-Erkennung.
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace NetMonitor;

// Führt ein Systemprogramm aus und liefert dessen Ausgabe (leer bei Fehler oder Zeitüberschreitung).
static class Sh
{
    public static string Run(string file, string args, int timeoutMs = 5000)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args)
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
            };
            psi.Environment["LC_ALL"] = "C"; // englische, stabile Ausgabe
            using var p = Process.Start(psi);
            var output = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs)) { try { p.Kill(true); } catch { } return ""; }
            return output.Result;
        }
        catch { return ""; }
    }

    public static bool Exists(string file)
    {
        if (file.Contains('/')) return File.Exists(file);
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            if (dir.Length > 0 && File.Exists(Path.Combine(dir, file))) return true;
        return false;
    }

    // Öffnet eine Webseite oder einen Ordner mit dem Standardprogramm des Systems.
    public static void Open(string target)
    {
        try
        {
            if (OperatingSystem.IsLinux()) Process.Start(new ProcessStartInfo("xdg-open", "\"" + target + "\"") { UseShellExecute = false });
            else if (OperatingSystem.IsMacOS()) Process.Start(new ProcessStartInfo("open", "\"" + target + "\"") { UseShellExecute = false });
            else Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch { }
    }
}

static class NetTools
{
    // IPv4-Standardgateway (Router) der aktiven Netzwerkverbindung, oder null.
    public static string DefaultGateway()
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                foreach (var line in File.ReadLines("/proc/net/route").Skip(1))
                {
                    var p = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length > 2 && p[1] == "00000000" && p[2] != "00000000")
                        return new IPAddress(BitConverter.GetBytes(uint.Parse(p[2], NumberStyles.HexNumber))).ToString();
                }
            }
            else if (OperatingSystem.IsMacOS())
            {
                var m = Regex.Match(Sh.Run("route", "-n get default"), @"gateway:\s*([0-9.]+)");
                if (m.Success) return m.Groups[1].Value;
            }
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var gw in nic.GetIPProperties().GatewayAddresses)
                    if (gw.Address.AddressFamily == AddressFamily.InterNetwork && !gw.Address.Equals(IPAddress.Any)) return gw.Address.ToString();
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
            using var p = new Ping();
            var r = await p.SendPingAsync(ip, TimeSpan.FromSeconds(1), null, new PingOptions(ttl, true));
            return r.Status == IPStatus.Success ? r.RoundtripTime : -1;
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
    public static async Task<(string Ip, int Hop)?> FindLastHop(string ip, Action<int> progress)
    {
        var hops = new List<(string, int)>();
        int misses = 0;
        for (int ttl = 1; ttl <= 30; ttl++)
        {
            progress(ttl);
            PingReply r = null;
            try { using var p = new Ping(); r = await p.SendPingAsync(ip, TimeSpan.FromSeconds(1), null, new PingOptions(ttl, true)); } catch { }
            if (r != null && r.Status == IPStatus.Success) return (ip, ttl);
            if (r != null && r.Status == IPStatus.TtlExpired && r.Address != null)
            {
                if (!IsPrivate(r.Address)) hops.Add((r.Address.ToString(), ttl));
                misses = 0;
            }
            else if (++misses >= 4 && hops.Count > 0) break;
        }
        for (int i = hops.Count - 1; i >= 0; i--)
            if (await Test(hops[i].Item1) >= 0) return hops[i];
        return null;
    }
}

// ---------- Verbindungsart ----------

public class ConnInfo
{
    public string Kind = "?";      // LAN, WLAN, MOBILE, VPN, ?
    public string Adapter = "", Ssid = "", Band = "";
    public int Signal = -1;        // WLAN-Signal in %
    public long LinkMbps;          // ausgehandelte Link-Geschwindigkeit

    public bool IsLan => Kind == "LAN";

    public string Short => Kind switch
    {
        "LAN" => "LAN" + (LinkMbps > 0 ? " · " + FormatLink(LinkMbps) : ""),
        "WLAN" => L.P("WLAN", "Wi-Fi") + (Signal >= 0 ? " · " + Signal + " %" : ""),
        "MOBILE" => L.P("Mobilfunk", "Mobile"),
        "VPN" => "VPN",
        _ => L.P("Verbindung unbekannt", "Connection unknown")
    };

    public static string FormatLink(long mbps) => mbps >= 1000 ? (mbps / 1000.0).ToString("0.#") + " Gbit/s" : mbps + " Mbit/s";
}

static class NetInfo
{
    static ConnInfo cached;
    static DateTime cachedAt;

    static bool LooksVirtual(string name) =>
        Regex.IsMatch(name, @"^(tun|tap|wg|utun|ppp|ipsec|zt|tailscale|docker|veth|br-|virbr|vmnet|llw|awdl|bridge)", RegexOptions.IgnoreCase);

    // Ermittelt die Verbindung über die Schnittstelle mit Standardroute (zwischengespeichert für 15 s).
    public static ConnInfo Current()
    {
        if (cached != null && (DateTime.Now - cachedAt).TotalSeconds < 15) return cached;
        ConnInfo info;
        try { info = OperatingSystem.IsMacOS() ? ReadMac() : OperatingSystem.IsLinux() ? ReadLinux() : new ConnInfo(); }
        catch { info = new ConnInfo(); }
        cached = info;
        cachedAt = DateTime.Now;
        return info;
    }

    static ConnInfo ReadLinux()
    {
        var info = new ConnInfo();
        string iface = null;
        foreach (var line in File.ReadLines("/proc/net/route").Skip(1))
        {
            var p = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length > 1 && p[1] == "00000000") { iface = p[0]; if (!LooksVirtual(iface)) break; }
        }
        if (iface == null) return info;
        info.Adapter = iface;
        string sys = "/sys/class/net/" + iface;
        if (LooksVirtual(iface)) info.Kind = "VPN";
        else if (iface.StartsWith("wwan") || iface.StartsWith("rmnet")) info.Kind = "MOBILE";
        else if (Directory.Exists(sys + "/wireless") || Directory.Exists(sys + "/phy80211")) info.Kind = "WLAN";
        else info.Kind = "LAN";

        if (info.Kind == "LAN")
        {
            try { if (long.TryParse(File.ReadAllText(sys + "/speed").Trim(), out long s) && s > 0) info.LinkMbps = s; } catch { }
        }
        else if (info.Kind == "WLAN")
        {
            // nmcli: "yes:MeinNetz:78" – sonst iw: "SSID: MeinNetz", "signal: -55 dBm"
            foreach (var line in Sh.Run("nmcli", "-t -f ACTIVE,SSID,SIGNAL,FREQ dev wifi").Split('\n'))
            {
                if (!line.StartsWith("yes:")) continue;
                var p = line.Split(':');
                if (p.Length >= 3) { info.Ssid = p[1]; int.TryParse(p[2], out info.Signal); }
                if (p.Length >= 4) info.Band = p[3].StartsWith("5") ? "5 GHz" : p[3].StartsWith("2") ? "2,4 GHz" : p[3].StartsWith("6") ? "6 GHz" : "";
                break;
            }
            if (info.Signal < 0)
            {
                string iw = Sh.Run("iw", "dev " + iface + " link");
                var ssid = Regex.Match(iw, @"SSID:\s*(.+)");
                var dbm = Regex.Match(iw, @"signal:\s*(-?\d+)\s*dBm");
                if (ssid.Success) info.Ssid = ssid.Groups[1].Value.Trim();
                if (dbm.Success) info.Signal = Math.Clamp(2 * (int.Parse(dbm.Groups[1].Value) + 100), 0, 100);
            }
        }
        return info;
    }

    static ConnInfo ReadMac()
    {
        var info = new ConnInfo();
        var m = Regex.Match(Sh.Run("route", "-n get default"), @"interface:\s*(\S+)");
        if (!m.Success) return info;
        string iface = m.Groups[1].Value;
        info.Adapter = iface;
        if (LooksVirtual(iface)) { info.Kind = "VPN"; return info; }

        // „Hardware Port: Wi-Fi / Device: en0“
        string ports = Sh.Run("networksetup", "-listallhardwareports");
        var port = Regex.Match(ports, @"Hardware Port:\s*(.+)\r?\nDevice:\s*" + Regex.Escape(iface) + @"\b");
        string kind = port.Success ? port.Groups[1].Value : "";
        info.Kind = Regex.IsMatch(kind, "Wi-?Fi|AirPort", RegexOptions.IgnoreCase) ? "WLAN"
                  : Regex.IsMatch(kind, "iPhone|Modem|WWAN", RegexOptions.IgnoreCase) ? "MOBILE" : "LAN";

        if (info.Kind == "LAN")
        {
            // „media: autoselect (1000baseT <full-duplex>)“
            var media = Regex.Match(Sh.Run("ifconfig", iface), @"media:.*?\((\d+)(G?)base", RegexOptions.IgnoreCase);
            if (media.Success) info.LinkMbps = long.Parse(media.Groups[1].Value) * (media.Groups[2].Value.Length > 0 ? 1000 : 1);
        }
        else if (info.Kind == "WLAN")
        {
            string sp = Sh.Run("system_profiler", "SPAirPortDataType", 15000);
            var current = Regex.Match(sp, @"Current Network Information:\s*\n\s*(.+?):\s*\n(.*?)(\n\s*Other Local|\z)", RegexOptions.Singleline);
            if (current.Success)
            {
                string name = current.Groups[1].Value.Trim();
                if (!name.Contains("redacted", StringComparison.OrdinalIgnoreCase)) info.Ssid = name;
                var sn = Regex.Match(current.Groups[2].Value, @"Signal / Noise:\s*(-?\d+)\s*dBm");
                if (sn.Success) info.Signal = Math.Clamp(2 * (int.Parse(sn.Groups[1].Value) + 100), 0, 100);
                var ch = Regex.Match(current.Groups[2].Value, @"Channel:\s*\d+\s*\((\d+)GHz");
                if (ch.Success) info.Band = ch.Groups[1].Value == "2" ? "2,4 GHz" : ch.Groups[1].Value + " GHz";
            }
        }
        return info;
    }
}

// ---------- Spiel-Server-Erkennung ----------

// Ein laufendes Programm mit Netzwerkverbindungen (Auswahl im Dropdown der Spiel-Karte).
class ProcItem
{
    public string Name = "";
    public int[] Pids = Array.Empty<int>();
    public override string ToString() => Name;

    static readonly HashSet<string> Ignore = new(StringComparer.OrdinalIgnoreCase)
    {
        "NetMonitor", "systemd-resolved", "systemd-networkd", "NetworkManager", "avahi-daemon", "cupsd", "chronyd", "sshd",
        "mDNSResponder", "rapportd", "identityservicesd", "apsd", "trustd", "nsurlsessiond", "syslogd", "netbiosd", "configd", "speedtest"
    };

    public int[] CurrentPids()
    {
        var list = new List<int>();
        foreach (var pid in Pids)
            try { using var p = Process.GetProcessById(pid); if (!p.HasExited) list.Add(pid); } catch { }
        return list.ToArray();
    }

    // Programme, die gerade Netzwerkverbindungen offen haben.
    public static List<ProcItem> ListNetworked()
    {
        var map = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
        void Add(string name, int pid)
        {
            if (string.IsNullOrEmpty(name) || Ignore.Contains(name) || pid == Environment.ProcessId) return;
            if (!map.TryGetValue(name, out var set)) map[name] = set = new HashSet<int>();
            set.Add(pid);
        }
        if (OperatingSystem.IsLinux())
        {
            foreach (Match m in Regex.Matches(Sh.Run("ss", "-H -tunap"), "\\(\"([^\"]+)\",pid=(\\d+)"))
                Add(m.Groups[1].Value, int.Parse(m.Groups[2].Value));
        }
        else if (OperatingSystem.IsMacOS())
        {
            // lsof -F: Zeilen „p1234“ (PID) und „cName“ (Programm)
            int pid = 0;
            foreach (var line in Sh.Run("lsof", "-i -n -P -F pc", 10000).Split('\n'))
            {
                if (line.StartsWith('p')) int.TryParse(line.AsSpan(1), out pid);
                else if (line.StartsWith('c')) Add(line.Substring(1).Trim(), pid);
            }
        }
        var list = map.Select(kv => new ProcItem { Name = kv.Key, Pids = kv.Value.ToArray() }).ToList();
        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return list;
    }
}

// Eine Gegenstelle, mit der das Spiel während der Analyse kommuniziert hat.
class Candidate
{
    public string Proto = "", Ip = "";
    public int Port;
    public double Rate = -1;       // Pakete/s bzw. KB/s, -1 = unbekannt
    public string RateUnit = "";
    public double Score;
    public bool? Answers;
    public long PingMs;
}

static class GameDetector
{
    public const int Seconds = 10;

    static bool Relevant(string ip, int port)
    {
        if (!IPAddress.TryParse(ip.Trim('[', ']'), out var addr) || NetTools.IsPrivate(addr)) return false;
        return port != 53 && port != 80 && port != 443 && port != 853; // DNS / Web / QUIC
    }

    static (string Ip, int Port) SplitEndpoint(string ep)
    {
        int colon = ep.LastIndexOf(':');
        if (colon <= 0) return ("", 0);
        string ip = ep.Substring(0, colon).Trim('[', ']');
        int pct = ip.IndexOf('%');
        if (pct > 0) ip = ip.Substring(0, pct);
        int.TryParse(ep.AsSpan(colon + 1), out int port);
        return (ip, port);
    }

    // Beobachtet die Verbindungen des Spiels und sortiert die Gegenstellen nach Wahrscheinlichkeit „Spielserver“.
    public static List<Candidate> Detect(int[] pids, CancellationToken ct)
    {
        var result = OperatingSystem.IsMacOS() ? DetectMac(pids, ct) : DetectLinux(pids, ct);
        result.Sort((a, b) => b.Score.CompareTo(a.Score));
        if (result.Count > 6) result.RemoveRange(6, result.Count - 6);
        return result;
    }

    // Linux: „ss“ zeigt die verbundenen Sockets des Spiels (ohne Root). Datenraten liefert ss nur für TCP,
    // daher zählt, wie dauerhaft eine Verbindung während der Beobachtung besteht.
    static List<Candidate> DetectLinux(int[] pids, CancellationToken ct)
    {
        var pidSet = new HashSet<int>(pids);
        var seen = new Dictionary<string, (Candidate C, int Count)>();
        int rounds = 0;
        var end = DateTime.Now.AddSeconds(Seconds);
        while (DateTime.Now < end && !ct.IsCancellationRequested)
        {
            rounds++;
            foreach (var line in Sh.Run("ss", "-H -tunp").Split('\n'))
            {
                var m = Regex.Match(line, @"^(udp|tcp)\s+\S+\s+\d+\s+\d+\s+(\S+)\s+(\S+)\s+.*pid=(\d+)");
                if (!m.Success || !pidSet.Contains(int.Parse(m.Groups[4].Value))) continue;
                var (ip, port) = SplitEndpoint(m.Groups[3].Value);
                if (!Relevant(ip, port)) continue;
                string key = m.Groups[1].Value + ";" + ip + ";" + port;
                if (seen.TryGetValue(key, out var v)) seen[key] = (v.C, v.Count + 1);
                else seen[key] = (new Candidate { Proto = m.Groups[1].Value.ToUpperInvariant(), Ip = ip, Port = port }, 1);
            }
            Thread.Sleep(1000);
        }
        foreach (var v in seen.Values)
            v.C.Score = (double)v.Count / Math.Max(1, rounds) * (v.C.Proto == "UDP" ? 1.0 : 0.3);
        return seen.Values.Select(v => v.C).ToList();
    }

    // macOS: „nettop“ liefert je Verbindung die übertragenen Bytes (Delta-Modus über das Beobachtungsintervall).
    static List<Candidate> DetectMac(int[] pids, CancellationToken ct)
    {
        var map = new Dictionary<string, Candidate>();
        foreach (int pid in pids.Take(3))
        {
            if (ct.IsCancellationRequested) break;
            string output = Sh.Run("nettop", "-L 2 -d -x -n -s " + Seconds + " -J bytes_in,bytes_out -p " + pid, (Seconds + 10) * 1000);
            var lines = output.Split('\n');
            // Nur die zweite Stichprobe (Delta über das Intervall) auswerten
            int header = Array.FindLastIndex(lines, l => l.Contains("bytes_in"));
            foreach (var line in lines.Skip(Math.Max(0, header)))
            {
                var m = Regex.Match(line, @"(udp|tcp)[46]\s+(\S+)<->(\S+?)\s*,\s*(\d+)\s*,\s*(\d+)", RegexOptions.IgnoreCase);
                if (!m.Success) continue;
                var (ip, port) = SplitEndpoint(NormalizeEndpoint(m.Groups[3].Value));
                if (!Relevant(ip, port)) continue;
                double kbps = (long.Parse(m.Groups[4].Value) + long.Parse(m.Groups[5].Value)) / 1024.0 / Seconds;
                string key = m.Groups[1].Value + ";" + ip + ";" + port;
                if (!map.TryGetValue(key, out var c)) map[key] = c = new Candidate { Proto = m.Groups[1].Value.ToUpperInvariant(), Ip = ip, Port = port, Rate = 0, RateUnit = "KB/s" };
                c.Rate += kbps;
                c.Score = c.Rate * (c.Proto == "UDP" ? 1.0 : 0.3);
            }
        }
        return map.Values.ToList();
    }

    // nettop schreibt IPv4-Endpunkte teils als „1.2.3.4.27015“ – dann ist der letzte Punkt der Port-Trenner.
    static string NormalizeEndpoint(string ep)
    {
        if (ep.Contains(':')) return ep;
        int i = ep.LastIndexOf('.');
        return i < 0 ? ep : ep.Substring(0, i) + ":" + ep.Substring(i + 1);
    }
}
