// NetMonitor (macOS/Linux) – Speedtest-Fenster: Cloudflare, Speedtest.net und offizielle Messstellen.
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace NetMonitor;

class SpeedtestWindow : AppWindow
{
    Banner connBanner;
    ConnInfo conn;
    readonly DispatcherTimer connTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    string countryCode;
    ComboBox serverSelect;
    List<OoklaTest.Server> servers;
    bool loadingServers, closed;
    TextBlock officialTitle, officialBadge, officialText;
    Border officialBadgeBox;
    StackPanel officialButtons, officialEntry;
    int provider;
    double tariffDown, tariffUp;
    string ooklaPath;
    bool ooklaAccepted;
    CancellationTokenSource cts;
    List<SpeedResult> results;

    Segmented providerSelect;
    Control testPanel, bnaPanel;
    StackPanel ooklaMissing;
    Gauge gDown, gUp;
    KpiTile kPing, kLoaded, kJitter, kServer;
    PillButton btnStart;
    ProgressLine progress;
    TextBlock status, providerInfo, tariffDownText, tariffUpText, tariffHint;
    TextBox inTariffDown, inTariffUp, inManDown, inManUp, inManPing;
    SpeedHistory history;
    Table grid;

    public SpeedtestWindow()
    {
        var d = Storage.LoadSettings();
        if (d.TryGetValue("speed.provider", out var v)) int.TryParse(v, out provider);
        provider = Math.Clamp(provider, 0, 2);
        if (d.TryGetValue("tariff.down", out v)) double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out tariffDown);
        if (d.TryGetValue("tariff.up", out v)) double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out tariffUp);
        d.TryGetValue("ookla.path", out ooklaPath);
        ooklaAccepted = d.TryGetValue("ookla.accepted", out v) && v == "1";
        countryCode = d.TryGetValue("speed.country", out v) ? OfficialTest.ForCode(v).Code : OfficialTest.DefaultCode();
        results = SpeedStore.Load();
        FlagOldResults();

        Title = "NetMonitor – Speedtest";
        Width = 1320; Height = 1010;
        MinWidth = 1100; MinHeight = 800;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Build();
        ShowProvider();
        UpdateTariff();
        FillResults();
        UpdateConnBanner();
        RefreshConnection();
        connTimer.Tick += (_, _) => RefreshConnection();
        connTimer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        closed = true;
        connTimer.Stop();
        cts?.Cancel();
        base.OnClosed(e);
    }

    async void RefreshConnection()
    {
        ConnInfo c;
        try { c = await Task.Run(NetInfo.Current); } catch { return; }
        if (closed) return;
        conn = c;
        UpdateConnBanner();
    }

    string ConnKind() => conn?.Kind ?? "?";

    // Erklärt, wie gut sich die aktuelle Verbindung für (offizielle) Messungen eignet.
    void UpdateConnBanner()
    {
        if (connBanner == null) return;
        if (conn == null) { connBanner.Set(Glyph.Globe, Skin.Muted, L.P("Ermittle Verbindungsart …", "Detecting connection type …")); return; }
        switch (conn.Kind)
        {
            case "WLAN":
                string details = ((conn.Ssid.Length > 0 ? "\"" + conn.Ssid + "\"" : "") +
                                  (conn.Signal >= 0 ? ", Signal " + conn.Signal + " %" : "") + (conn.Band.Length > 0 ? ", " + conn.Band : "")).TrimStart(',', ' ');
                connBanner.Set(Glyph.Wifi, Skin.Warn, L.P(
                    "Du bist per WLAN verbunden" + (details.Length > 0 ? " (" + details + ")" : "") + ". WLAN kann Messungen deutlich ausbremsen – für aussagekräftige " +
                    "Ergebnisse und besonders für offizielle Messungen bitte per LAN-Kabel direkt am Router messen.",
                    "You are connected via Wi-Fi" + (details.Length > 0 ? " (" + details + ")" : "") + ". Wi-Fi can slow measurements down " +
                    "considerably – for meaningful results and especially for official measurements, connect via LAN cable directly to the router."));
                break;
            case "LAN":
                if (conn.LinkMbps > 0 && tariffDown > 0 && conn.LinkMbps < tariffDown)
                    connBanner.Set(Glyph.Lan, Skin.Warn, L.P(
                        "LAN-Verbindung, aber der Netzwerkanschluss arbeitet nur mit " + ConnInfo.FormatLink(conn.LinkMbps) + " – weniger als dein Tarif (" +
                        tariffDown.ToString("0") + " Mbit/s). Kabel (mind. Cat 5e) und Port am Router/Switch prüfen.",
                        "LAN connection, but the network port only runs at " + ConnInfo.FormatLink(conn.LinkMbps) + " – less than your plan (" +
                        tariffDown.ToString("0") + " Mbit/s). Check the cable (at least Cat 5e) and the router/switch port."));
                else
                    connBanner.Set(Glyph.Lan, Skin.Good, L.P(
                        "Per LAN-Kabel verbunden" + (conn.LinkMbps > 0 ? " (" + ConnInfo.FormatLink(conn.LinkMbps) + ")" : "") + " – ideal für aussagekräftige und offizielle Messungen.",
                        "Connected via LAN cable" + (conn.LinkMbps > 0 ? " (" + ConnInfo.FormatLink(conn.LinkMbps) + ")" : "") + " – ideal for meaningful and official measurements."));
                break;
            case "MOBILE":
                connBanner.Set(Glyph.Globe, Skin.Warn, L.P(
                    "Mobilfunkverbindung – die Messung zeigt dein Mobilfunknetz, nicht deinen Festnetzanschluss.",
                    "Mobile connection – the measurement shows your mobile network, not your fixed-line connection."));
                break;
            case "VPN":
                connBanner.Set(Glyph.Globe, Skin.Warn, L.P(
                    "Verbindung über VPN bzw. einen virtuellen Adapter – gemessen wird die VPN-Strecke. Für Messungen deines Anschlusses das VPN trennen.",
                    "Connected through a VPN or virtual adapter – the VPN route is measured. Disconnect the VPN to measure your own connection."));
                break;
            default:
                connBanner.Set(Glyph.Globe, Skin.Muted, L.P("Verbindungsart konnte nicht ermittelt werden.", "The connection type could not be determined."));
                break;
        }
    }

    void Build()
    {
        var root = new Grid { RowDefinitions = new RowDefinitions("70,54,420,*"), Margin = new Thickness(18, 14, 18, 18) };

        // Kopfzeile
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(6, 0, 6, 0) };
        providerInfo = Skin.Label("", 11.5, Skin.Faint);
        head.Children.Add(Column(2, Skin.Label("Speedtest", 23, Skin.Text, FontWeight.Bold), providerInfo));
        providerSelect = new Segmented("Cloudflare", "Speedtest.net", L.P("Offizielle Messung", "Official test")) { VerticalAlignment = VerticalAlignment.Center };
        providerSelect.SelectedIndex = provider;
        providerSelect.Changed += (_, _) => { provider = providerSelect.SelectedIndex; Storage.SaveSetting("speed.provider", provider.ToString()); ShowProvider(); };
        Grid.SetColumn(providerSelect, 1);
        head.Children.Add(providerSelect);
        root.Children.Add(head);

        connBanner = new Banner { Margin = new Thickness(6, 2, 6, 4) };
        Grid.SetRow(connBanner, 1);
        root.Children.Add(connBanner);

        // Mitte: Messung (links) + Tarif (rechts)
        var mid = new Grid { ColumnDefinitions = new ColumnDefinitions("64*,36*") };
        testPanel = BuildTestPanel();
        bnaPanel = BuildBnaPanel();
        mid.Children.Add(new Surface { Child = new Panel { Children = { testPanel, bnaPanel } }, Margin = new Thickness(6), Padding = new Thickness(16) });
        var tariff = BuildTariffPanel();
        Grid.SetColumn(tariff, 1);
        mid.Children.Add(tariff);
        Grid.SetRow(mid, 2);
        root.Children.Add(mid);

        // Unten: Verlauf
        var bl = new Grid { ColumnDefinitions = new ColumnDefinitions("45*,55*"), RowDefinitions = new RowDefinitions("40,*") };
        bl.Children.Add(Skin.Label(L.P("Bisherige Speedtests", "Previous speed tests"), 17, Skin.Text, FontWeight.Bold));
        var btnExport = new PillButton("Export", Skin.Accent, false, Glyph.Download) { Height = 34 };
        var btnDelete = new PillButton(L.P("Auswahl löschen", "Delete selected"), Skin.Bad, true, Glyph.Delete) { Height = 34 };
        btnExport.Click += (_, _) => ExportResults();
        btnDelete.Click += (_, _) => DeleteSelected();
        var actions = Row(8, btnDelete, btnExport);
        actions.HorizontalAlignment = HorizontalAlignment.Right;
        actions.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(actions, 1);
        bl.Children.Add(actions);
        history = new SpeedHistory { Margin = new Thickness(0, 0, 12, 0) };
        Grid.SetRow(history, 1);
        bl.Children.Add(history);
        grid = new Table
        {
            Columns = L.En ? new[] { "Date", "Provider", "Download\nMbit/s", "Upload\nMbit/s", "Ping", "Loaded", "% of plan", "Connection" }
                           : new[] { "Datum", "Anbieter", "Download\nMbit/s", "Upload\nMbit/s", "Ping", "Unter Last", "% Tarif", "Verbindung" },
            Weights = new double[] { 120, 110, 80, 80, 70, 80, 70, 100 },
            HeaderColors = new[] { Skin.Muted, Skin.Muted, Skin.Cyan, Skin.Violet, Skin.Muted, Skin.Muted, Skin.Muted, Skin.Muted },
            MultiSelect = true,
            EmptyText = L.P("Noch keine Messungen", "No measurements yet")
        };
        grid.RowDoubleClicked += (_, i) => { if (grid.Rows[i].Tag is SpeedResult r && r.Url.StartsWith("https://")) Sh.Open(r.Url); };
        Grid.SetRow(grid, 1);
        Grid.SetColumn(grid, 1);
        bl.Children.Add(grid);
        var bottom = new Surface { Child = bl, Margin = new Thickness(6, 6, 6, 0), Padding = new Thickness(16, 12, 16, 12) };
        Grid.SetRow(bottom, 3);
        root.Children.Add(bottom);
        Content = root;
    }

    Control BuildTestPanel()
    {
        var p = new Grid { RowDefinitions = new RowDefinitions("*,96,22,48"), ColumnDefinitions = new ColumnDefinitions("*,*") };
        gDown = new Gauge { Title = "Download", Color = Skin.Cyan };
        gUp = new Gauge { Title = "Upload", Color = Skin.Violet };
        p.Children.Add(gDown);
        Grid.SetColumn(gUp, 1);
        p.Children.Add(gUp);

        var kpis = new Grid { ColumnDefinitions = new ColumnDefinitions("22*,22*,22*,34*") };
        kPing = MiniTile("Ping", Skin.Good);
        kLoaded = MiniTile(L.P("Ping unter Last", "Loaded ping"), Skin.Warn);
        kJitter = MiniTile("Jitter", Skin.Cyan);
        kServer = MiniTile("Server", Skin.Accent);
        KpiTile[] tiles = { kPing, kLoaded, kJitter, kServer };
        for (int i = 0; i < 4; i++) { Grid.SetColumn(tiles[i], i); kpis.Children.Add(tiles[i]); }
        Grid.SetRow(kpis, 1);
        Grid.SetColumnSpan(kpis, 2);
        p.Children.Add(kpis);

        progress = new ProgressLine { Margin = new Thickness(0, 8, 0, 6) };
        Grid.SetRow(progress, 2);
        Grid.SetColumnSpan(progress, 2);
        p.Children.Add(progress);

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        status = Skin.Label("", 12, Skin.Faint);
        status.TextTrimming = TextTrimming.CharacterEllipsis;
        row.Children.Add(status);
        var btnGet = new PillButton(L.P("Ookla CLI installieren", "Install Ookla CLI"), Skin.Accent, false, Glyph.Download);
        var btnPath = new PillButton(L.P("Pfad wählen …", "Choose path …"), Skin.Muted, true, Glyph.Folder);
        btnGet.Click += (_, _) => InstallOokla(btnGet);
        btnPath.Click += (_, _) => ChooseOokla();
        ooklaMissing = Row(8, btnGet, btnPath);
        btnStart = new PillButton(L.P("Test starten", "Start test"), Skin.Good, false, Glyph.Play);
        btnStart.Click += (_, _) => { if (cts != null) cts.Cancel(); else StartTest(); };
        serverSelect = Skin.Combo(250);
        serverSelect.Items.Add(L.P("Server: automatisch", "Server: automatic"));
        serverSelect.SelectedIndex = 0;
        serverSelect.SelectionChanged += (_, _) =>
        {
            if (serverSelect.SelectedIndex < 0) return;
            Storage.SaveSetting("ookla.server", serverSelect.SelectedItem is OoklaTest.Server s ? s.Id.ToString() : "0");
        };
        var btns = Row(8, ooklaMissing, serverSelect, btnStart);
        btns.Margin = new Thickness(12, 0, 0, 0);
        btns.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(btns, 1);
        row.Children.Add(btns);
        Grid.SetRow(row, 3);
        Grid.SetColumnSpan(row, 2);
        p.Children.Add(row);
        return p;
    }

    static KpiTile MiniTile(string title, Color tint) => new() { Title = title, Tint = tint, Margin = new Thickness(4), Fill = Skin.Surface2, Compact = true, Radius = 12 };

    OfficialTest Official => OfficialTest.ForCode(countryCode);

    Control BuildBnaPanel()
    {
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        officialTitle = Skin.Label("", 19, Skin.Text, FontWeight.Bold);
        head.Children.Add(officialTitle);
        var country = Skin.Combo(210);
        foreach (var t in OfficialTest.All) country.Items.Add(t);
        country.SelectedIndex = OfficialTest.All.IndexOf(Official);
        country.SelectionChanged += (_, _) =>
        {
            if (country.SelectedItem is not OfficialTest t) return;
            countryCode = t.Code;
            Storage.SaveSetting("speed.country", countryCode);
            FillOfficial();
            ShowProvider();
        };
        Grid.SetColumn(country, 1);
        head.Children.Add(country);

        officialBadge = Skin.Label("", 11.5, Skin.Good, FontWeight.Bold);
        officialBadgeBox = new Border { Child = officialBadge, CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 3), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2, 8, 0, 0) };
        officialText = Skin.Paragraph("", 13, Skin.Muted, 760);
        officialText.Margin = new Thickness(0, 10, 0, 14);
        officialButtons = Row(8);
        officialButtons.Margin = new Thickness(0, 0, 0, 16);

        inManDown = SmallInput();
        inManUp = SmallInput();
        inManPing = SmallInput();
        var btnSave = new PillButton(L.P("Speichern", "Save"), Skin.Good, false, Glyph.Check) { Height = 34, VerticalAlignment = VerticalAlignment.Bottom };
        btnSave.Click += (_, _) => SaveManual();
        officialEntry = Column(6,
            Skin.Label(L.P("Ergebnis eintragen (zum Vergleich mit den anderen Messungen)", "Enter result (to compare with the other measurements)"), 12, Skin.Muted, FontWeight.Bold),
            Row(8, Labeled("Download (Mbit/s)", inManDown), Labeled("Upload (Mbit/s)", inManUp), Labeled("Ping (ms)", inManPing), btnSave));
        var p = Column(0, head, officialBadgeBox, officialText, officialButtons, officialEntry);
        FillOfficial();
        return new ScrollViewer { Content = p };
    }

    // Inhalt für das gewählte Land: Einstufung, Beschreibung und passende Buttons.
    void FillOfficial()
    {
        var t = Official;
        officialTitle.Text = t.Web.Length == 0 ? L.P("Keine offizielle Messung", "No official measurement") : t.Title + "  ·  " + t.Regulator;
        officialBadge.Text = t.Level == 0 ? L.P("RECHTSVERBINDLICH", "LEGALLY BINDING")
                           : t.Level == 1 ? L.P("OFFIZIELLER TEST DER BEHÖRDE", "OFFICIAL REGULATOR TEST")
                           : L.P("CLOUDFLARE / SPEEDTEST.NET VERWENDEN", "USE CLOUDFLARE / SPEEDTEST.NET");
        officialEntry.IsVisible = t.Level < 2;
        var c = t.Level == 0 ? Skin.Good : t.Level == 1 ? Skin.Cyan : Skin.Warn;
        officialBadge.Foreground = Skin.B(c);
        officialBadgeBox.Background = Skin.B(Skin.Mix(Skin.Surface, c, 0.18));
        officialText.Text = t.Text + (t.Level < 2
            ? L.P("\n\nDie Messung läuft im Browser bzw. in der App der Behörde – NetMonitor öffnet sie für dich. " +
                  "Wichtig: per LAN-Kabel direkt am Router messen (kein WLAN), den Ping-Monitor stoppen und andere Geräte vom Netz nehmen.",
                  "\n\nThe measurement runs in the regulator's website or app – NetMonitor opens it for you. " +
                  "Important: measure via LAN cable directly at the router (no Wi-Fi), stop the ping monitor and disconnect other devices.") +
              (t.Code == "DE" ? L.P(" Für das Nachweisverfahren der Desktop-App ist eine LAN-Verbindung Voraussetzung.",
                                    " A LAN connection is required for the desktop app's verification procedure.") : "")
            : "");

        officialButtons.Children.Clear();
        if (t.Code == "DE")
        {
            var btnApp = new PillButton(L.P("Desktop-App herunterladen", "Get desktop app"), Skin.Accent, false, Glyph.Download);
            btnApp.Click += (_, _) => Sh.Open("https://breitbandmessung.de/desktop-app");
            officialButtons.Children.Add(btnApp);
        }
        if (t.Web.Length == 0)
        {
            // Kein offizieller Test: direkt zu den eingebauten Messungen wechseln
            var btnCf = new PillButton(L.P("Cloudflare-Test", "Cloudflare test"), Skin.Accent, false, Glyph.Bolt);
            btnCf.Click += (_, _) => providerSelect.SelectedIndex = 0;
            var btnOokla = new PillButton("Speedtest.net", Skin.Cyan, true, Glyph.Bolt);
            btnOokla.Click += (_, _) => providerSelect.SelectedIndex = 1;
            officialButtons.Children.Add(btnCf);
            officialButtons.Children.Add(btnOokla);
            return;
        }
        var btnWeb = new PillButton(L.P("Messung im Browser öffnen", "Open test in browser"), t.Code == "DE" ? Skin.Cyan : Skin.Accent, t.Code == "DE", Glyph.Globe);
        btnWeb.Click += (_, _) => Sh.Open(t.Web);
        officialButtons.Children.Add(btnWeb);
    }

    static TextBox SmallInput() { var b = Skin.TextInput("", ""); b.Width = 150; return b; }

    // Eingabefeld mit Beschriftung darüber.
    static Control Labeled(string caption, TextBox box)
    {
        var l = Skin.Label(caption, 11.5, Skin.Faint);
        l.Margin = new Thickness(2, 0, 0, 0);
        return Column(4, l, box);
    }

    Control BuildTariffPanel()
    {
        inTariffDown = SmallInput();
        inTariffUp = SmallInput();
        inTariffDown.Width = inTariffUp.Width = 140;
        if (tariffDown > 0) inTariffDown.Text = tariffDown.ToString("0.##");
        if (tariffUp > 0) inTariffUp.Text = tariffUp.ToString("0.##");
        inTariffDown.TextChanged += (_, _) => ReadTariff();
        inTariffUp.TextChanged += (_, _) => ReadTariff();
        tariffDownText = Skin.Label("", 14, Skin.Text, FontWeight.Bold);
        tariffUpText = Skin.Label("", 14, Skin.Text, FontWeight.Bold);
        tariffHint = Skin.Paragraph("", 12, Skin.Muted, 400);
        tariffHint.Margin = new Thickness(0, 14, 0, 0);
        var sub = Skin.Label(L.P("Maximalgeschwindigkeit laut Vertrag", "Maximum speed according to your contract"), 11.5, Skin.Faint);
        sub.Margin = new Thickness(2, 0, 0, 12);
        var inputs = Row(8, Labeled("Download (Mbit/s)", inTariffDown), Labeled("Upload (Mbit/s)", inTariffUp));
        inputs.Margin = new Thickness(0, 0, 0, 16);
        var p = Column(4, Skin.Label(L.P("Gebuchter Tarif", "Your internet plan"), 17, Skin.Text, FontWeight.Bold), sub, inputs, tariffDownText, tariffUpText, tariffHint);
        return new Surface { Child = new ScrollViewer { Content = p }, Margin = new Thickness(6), Padding = new Thickness(18, 14, 18, 14) };
    }

    static double ParseNumber(string s)
    {
        s = (s ?? "").Trim().Replace(',', '.');
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v > 0 ? v : 0;
    }

    void ReadTariff()
    {
        if (tariffHint == null) return; // noch im Aufbau
        tariffDown = ParseNumber(inTariffDown.Text);
        tariffUp = ParseNumber(inTariffUp.Text);
        var d = Storage.LoadSettings();
        d["tariff.down"] = tariffDown.ToString(CultureInfo.InvariantCulture);
        d["tariff.up"] = tariffUp.ToString(CultureInfo.InvariantCulture);
        Storage.SaveSettings(d);
        UpdateTariff();
        FillResults();
    }

    static Color PctColor(double pct) => pct >= 90 ? Skin.Good : pct >= 70 ? Skin.Warn : Skin.Bad;

    // Vergleicht den letzten Messwert mit dem Tarif und schätzt den Durchschnitt der letzten Messungen ein.
    void UpdateTariff()
    {
        gDown.Tariff = tariffDown; gUp.Tariff = tariffUp;
        var last = results.LastOrDefault(r => !double.IsNaN(r.Down) && !r.Suspicious);
        void Show(TextBlock label, Gauge gauge, string name, double measured, double tariff)
        {
            if (tariff <= 0 || double.IsNaN(measured))
            {
                label.Text = name + ": " + (tariff > 0 ? L.P("noch keine Messung", "no measurement yet") : L.P("Tarif eintragen", "enter your plan"));
                label.Foreground = Skin.B(Skin.Muted);
                gauge.Sub = ""; gauge.InvalidateVisual();
                return;
            }
            double pct = 100 * measured / tariff;
            label.Text = name + ": " + SpeedResult.Fmt(measured) + L.P(" von ", " of ") + tariff.ToString("0.##") + " Mbit/s  (" + pct.ToString("0") + " %)";
            label.Foreground = Skin.B(PctColor(pct));
            gauge.Sub = pct.ToString("0") + L.P(" % des Tarifs", " % of plan");
            gauge.SubColor = PctColor(pct);
            gauge.InvalidateVisual();
        }
        Show(tariffDownText, gDown, "Download", last?.Down ?? double.NaN, tariffDown);
        Show(tariffUpText, gUp, "Upload", last?.Up ?? double.NaN, tariffUp);

        var recent = tariffDown <= 0 ? new List<SpeedResult>()
            : results.Where(r => !double.IsNaN(r.Down) && !r.Suspicious).Reverse().Take(10).ToList();
        int n = recent.Count, below = recent.Count(r => r.Down < 0.9 * tariffDown);
        tariffHint.Text = tariffDown <= 0
            ? L.P("Trage die Werte aus deinem Vertrag ein, dann zeigt NetMonitor, wie viel davon tatsächlich ankommt.",
                  "Enter the values from your contract and NetMonitor shows how much of it you actually get.")
            : n == 0 ? ""
            : L.P(below + " von " + n + " der letzten Messungen lagen unter 90 % des Tarifs.",
                  below + " of the last " + n + " measurements were below 90 % of your plan.") +
              (below * 2 > n ? L.P("\n\nDas spricht für eine dauerhafte Abweichung. Für Ansprüche gegenüber dem Anbieter zählt nur die offizielle Messung (Reiter oben).",
                                   "\n\nThis suggests a persistent shortfall. For claims against your ISP only the official measurement counts (tab above).") : "");
        tariffHint.Foreground = Skin.B(n > 0 && below * 2 > n ? Skin.Warn : Skin.Muted);
        history.TariffDown = tariffDown; history.TariffUp = tariffUp;
        history.InvalidateVisual();
        UpdateConnBanner();
    }

    void ShowProvider()
    {
        testPanel.IsVisible = provider != 2;
        bnaPanel.IsVisible = provider == 2;
        bool ookla = provider == 1;
        bool missing = ookla && OoklaTest.Find(ooklaPath) == null;
        ooklaMissing.IsVisible = missing;
        serverSelect.IsVisible = ookla && !missing;
        btnStart.IsEnabled = !missing;
        if (ookla && !missing) LoadServers();
        providerInfo.Text = provider == 0
            ? L.P("speed.cloudflare.com · 6 parallele Verbindungen · je 8 s Download und Upload · misst auch die Latenz unter Last",
                  "speed.cloudflare.com · 6 parallel connections · 8 s download and upload each · also measures latency under load")
            : provider == 1
                ? L.P("Offizielle Ookla Speedtest CLI · nächstgelegener Speedtest.net-Server", "Official Ookla Speedtest CLI · nearest Speedtest.net server")
                : Official.Web.Length == 0 ? L.P("Offizielle Messstellen: Deutschland, Österreich, Schweiz, Luxemburg", "Official tests: Germany, Austria, Switzerland, Luxembourg")
                : L.P("Offizielle Messstelle für ", "Official measurement for ") + Official.Name + " · " + Official.Regulator;
        SetStatus(Skin.Faint, missing
            ? L.P("Benötigt die kostenlose Ookla Speedtest CLI.", "Requires the free Ookla Speedtest CLI.")
            : L.P("Die Messung lastet die Leitung kurz voll aus – währenddessen erkannte Paketverluste werden nicht als Störung gewertet.",
                  "The test saturates your connection briefly – packet loss during the test is not counted as an incident."));
    }

    void SetStatus(Color c, string text)
    {
        status.Foreground = Skin.B(c);
        status.Text = text;
        ToolTip.SetTip(status, text);
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
        if (closed) return;
        int saved = 0;
        if (Storage.LoadSettings().TryGetValue("ookla.server", out var v)) int.TryParse(v, out saved);
        int sel = 0;
        while (serverSelect.Items.Count > 1) serverSelect.Items.RemoveAt(1);
        foreach (var s in servers)
        {
            serverSelect.Items.Add(s);
            if (s.Id == saved) sel = serverSelect.Items.Count - 1;
        }
        serverSelect.SelectedIndex = sel;
    }

    int SelectedServerId() => serverSelect.SelectedItem is OoklaTest.Server s ? s.Id : 0;

    async Task<bool> AskOoklaTerms()
    {
        if (ooklaAccepted) return true;
        bool yes = await MessageDialog.Ask(this, L.P(
            "Speedtest.net wird von Ookla betrieben. Mit der Nutzung akzeptierst du deren Nutzungsbedingungen und Datenschutzerklärung:\n\n" +
            OoklaTest.Terms + "\n" + OoklaTest.Privacy + "\n\nAkzeptieren und fortfahren?",
            "Speedtest.net is operated by Ookla. By using it you accept their terms of use and privacy policy:\n\n" +
            OoklaTest.Terms + "\n" + OoklaTest.Privacy + "\n\nAccept and continue?"),
            L.P("Akzeptieren", "Accept"), L.P("Abbrechen", "Cancel"), MsgKind.Question, "Speedtest.net");
        if (!yes) return false;
        ooklaAccepted = true;
        Storage.SaveSetting("ookla.accepted", "1");
        return true;
    }

    // Lädt die offizielle Ookla CLI herunter (Prüfsumme wird geprüft) und hinterlegt sie automatisch.
    async void InstallOokla(PillButton button)
    {
        if (!await AskOoklaTerms()) return;
        button.IsEnabled = false;
        SetStatus(Skin.Muted, L.P("Lade Ookla Speedtest CLI …", "Downloading Ookla Speedtest CLI …"));
        try
        {
            ooklaPath = await Task.Run(OoklaTest.Download);
            servers = null;
            ShowProvider();
            SetStatus(Skin.Good, L.P("✓ Ookla Speedtest CLI installiert – bereit für den Test.", "✓ Ookla Speedtest CLI installed – ready to test."));
        }
        catch (Exception ex)
        {
            SetStatus(Skin.Bad, L.P("Download fehlgeschlagen: ", "Download failed: ") + (ex.InnerException ?? ex).Message);
        }
        finally { button.IsEnabled = true; }
    }

    async void ChooseOokla()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Ookla Speedtest CLI", AllowMultiple = false });
        string path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (path == null) return;
        if (!Sh.Run(path, "--version", 3000).Contains("Ookla"))
        {
            await MessageDialog.Show(this, L.P("Diese Datei ist nicht die Ookla Speedtest CLI.", "This file is not the Ookla Speedtest CLI."), MsgKind.Warning);
            return;
        }
        ooklaPath = path;
        Storage.SaveSetting("ookla.path", ooklaPath);
        servers = null;
        ShowProvider();
    }

    async void StartTest()
    {
        string exe = null;
        if (provider == 1)
        {
            exe = OoklaTest.Find(ooklaPath);
            if (exe == null) { ShowProvider(); return; }
            if (!await AskOoklaTerms()) return;
        }

        cts = new CancellationTokenSource();
        providerSelect.IsEnabled = false;
        btnStart.Text = L.P("Abbrechen", "Cancel"); btnStart.Glyph = Glyph.Stop; btnStart.Color = Skin.Bad;
        gDown.Value = double.NaN; gUp.Value = double.NaN;
        gDown.Sub = gUp.Sub = "";
        foreach (var k in new[] { kPing, kLoaded, kJitter, kServer }) { k.Value = "–"; k.Sub = ""; k.InvalidateVisual(); }
        progress.Value = 0; progress.InvalidateVisual();
        SetStatus(Skin.Muted, "");
        SpeedState.Begin();
        SpeedProgress report = (phase, value, fraction, server) => Dispatcher.UIThread.Post(() =>
        {
            if (closed) return;
            progress.Value = 100 * fraction; progress.InvalidateVisual();
            if (!string.IsNullOrEmpty(server)) { kServer.Value = server; kServer.InvalidateVisual(); }
            if (phase == 0) { status.Text = L.P("Messe Latenz …", "Measuring latency …"); if (!double.IsNaN(value)) { kPing.Value = SpeedResult.FmtMs(value); kPing.InvalidateVisual(); } }
            if (phase == 1) { status.Text = L.P("Messe Download …", "Measuring download …"); gDown.Value = value; }
            if (phase == 2) { status.Text = L.P("Messe Upload …", "Measuring upload …"); gUp.Value = value; }
        });
        bool askRetry = false;
        var token = cts.Token;
        try
        {
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
                var alt = servers.FirstOrDefault(s => s.Id != OoklaTest.LastServer);
                SetStatus(Skin.Warn, L.P("⚠ Unplausibel (", "⚠ Implausible (") + SpeedResult.Fmt(r.Down) +
                    L.P(" Mbit/s) – wiederhole automatisch", " Mbit/s) – repeating automatically") + (alt != null ? " · " + alt.Name : "") + " …");
                gDown.Value = double.NaN; gUp.Value = double.NaN;
                int altId = alt?.Id ?? 0;
                r = await Task.Run(() => OoklaTest.Run(exe, token, report, altId));
                r.Suspicious = IsSuspicious(r);
                r.Conn = ConnKind();
            }
            askRetry = r.Suspicious;
            SpeedStore.Append(r);
            results.Add(r);
            ShowResult(r);
            SetStatus(Skin.Good, L.P("✓ Fertig", "✓ Done") + " · " + r.Server +
                (r.Bytes > 0 ? L.P(" · Datenverbrauch ", " · data used ") + (r.Bytes / 1e6).ToString("0") + " MB" : "") +
                (r.Url.Length > 0 ? L.P(" · Doppelklick in der Liste öffnet das Ergebnis", " · double-click the list entry to open the result") : ""));
            if (r.Suspicious)
                SetStatus(Skin.Warn, L.P("⚠ Leitung wurde nicht ausgelastet – Ergebnis vermutlich zu niedrig", "⚠ Connection was not saturated – result is probably too low"));
            FillResults();
        }
        catch (Exception ex)
        {
            if (!closed)
            {
                SetStatus(token.IsCancellationRequested ? Skin.Muted : Skin.Bad,
                    token.IsCancellationRequested ? L.P("Abgebrochen", "Cancelled") : L.P("Fehler: ", "Error: ") + (ex.InnerException ?? ex).Message);
                gDown.Value = double.NaN; gUp.Value = double.NaN;
            }
        }
        finally
        {
            SpeedState.End();
            cts.Dispose();
            cts = null;
            providerSelect.IsEnabled = true;
            btnStart.Text = L.P("Test starten", "Start test"); btnStart.Glyph = Glyph.Play; btnStart.Color = Skin.Good;
            progress.Value = 100; progress.InvalidateVisual();
        }
        if (askRetry && !closed)
        {
            bool again = await MessageDialog.Ask(this, L.P(
                "Die Latenz ist während der Messung kaum gestiegen – die Leitung wurde also nicht ausgelastet. Das Ergebnis liegt deutlich unter " +
                "deinem Tarif bzw. deinen bisherigen Messungen und ist vermutlich zu niedrig (z. B. ausgelasteter Server oder parallele Downloads).\n\n" +
                "Das Ergebnis ist markiert und fließt nicht in die Tarifbewertung ein. Test jetzt wiederholen?",
                "Latency barely increased during the test – the connection was not saturated. The result is far below your plan or previous " +
                "measurements and is probably too low (e.g. a busy server or parallel downloads).\n\n" +
                "The result is flagged and excluded from the plan evaluation. Repeat the test now?"),
                L.P("Wiederholen", "Repeat"), L.P("Nein", "No"), MsgKind.Warning, "Speedtest");
            if (again) StartTest();
        }
    }

    // Ältere Messungen einmalig mit denselben Kriterien prüfen.
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
        kPing.Value = SpeedResult.FmtMs(r.Ping);
        kLoaded.Value = SpeedResult.FmtMs(r.LoadedPing);
        kLoaded.Sub = !double.IsNaN(r.LoadedPing) && !double.IsNaN(r.Ping)
            ? "+" + Math.Max(0, r.LoadedPing - r.Ping).ToString("0") + L.P(" ms unter Last", " ms under load") : "";
        kLoaded.ValueColor = !double.IsNaN(r.LoadedPing) && r.LoadedPing - r.Ping > 60 ? Skin.Warn : Skin.Text;
        kJitter.Value = SpeedResult.FmtMs(r.Jitter);
        kServer.Value = r.Server;
        foreach (var k in new[] { kPing, kLoaded, kJitter, kServer }) k.InvalidateVisual();
        UpdateTariff();
    }

    async void SaveManual()
    {
        double down = ParseNumber(inManDown.Text), up = ParseNumber(inManUp.Text), ping = ParseNumber(inManPing.Text);
        if (down <= 0 && up <= 0)
        {
            await MessageDialog.Show(this, L.P("Bitte mindestens Download oder Upload eintragen.", "Please enter at least download or upload."));
            return;
        }
        var r = new SpeedResult
        {
            Time = DateTime.Now, Provider = Official.Title, Server = L.P("manuell eingetragen", "entered manually") + " · " + Official.Regulator, Conn = ConnKind(),
            Down = down > 0 ? down : double.NaN, Up = up > 0 ? up : double.NaN, Ping = ping > 0 ? ping : double.NaN
        };
        SpeedStore.Append(r);
        results.Add(r);
        inManDown.Text = inManUp.Text = inManPing.Text = "";
        UpdateTariff();
        FillResults();
    }

    void FillResults()
    {
        history.Items = results;
        history.InvalidateVisual();
        grid.Rows.Clear();
        for (int i = results.Count - 1; i >= 0; i--)
        {
            var r = results[i];
            double pct = tariffDown > 0 && !double.IsNaN(r.Down) ? 100 * r.Down / tariffDown : double.NaN;
            grid.Rows.Add(new TableRow
            {
                Cells = new[]
                {
                    r.Time.ToString(L.DateTimeShort), (r.Suspicious ? "⚠ " : "") + r.Provider, SpeedResult.Fmt(r.Down), SpeedResult.Fmt(r.Up),
                    SpeedResult.FmtMs(r.Ping), SpeedResult.FmtMs(r.LoadedPing), double.IsNaN(pct) ? "–" : pct.ToString("0") + " %", r.ConnText
                },
                Colors = new Color?[]
                {
                    null, r.Suspicious ? Skin.Warn : null, r.Suspicious ? Skin.Faint : Skin.Cyan, Skin.Violet, null, null,
                    double.IsNaN(pct) ? null : PctColor(pct), r.Conn == "LAN" ? Skin.Good : r.Conn == "" || r.Conn == "?" ? Skin.Faint : Skin.Warn
                },
                Bold = new[] { false, false, false, false, false, false, !double.IsNaN(pct), false },
                Tag = r,
                Tooltip = r.Suspicious ? L.P("Leitung nicht ausgelastet – Ergebnis vermutlich zu niedrig, zählt nicht für die Tarifbewertung",
                                             "Connection not saturated – result probably too low, excluded from the plan evaluation") : r.Server
            });
        }
        grid.Selected.Clear();
        grid.Refresh();
    }

    async void DeleteSelected()
    {
        if (grid.Selected.Count == 0) return;
        var chosen = grid.Selected.Select(i => grid.Rows[i].Tag as SpeedResult).ToList();
        if (!await MessageDialog.Ask(this, chosen.Count + L.P(" Speedtest(s) löschen?", " speed test(s) delete?"),
                L.P("Löschen", "Delete"), L.P("Abbrechen", "Cancel"), MsgKind.Warning)) return;
        foreach (var r in chosen) results.Remove(r);
        try { SpeedStore.Save(results); } catch (Exception ex) { await MessageDialog.Show(this, ex.Message, MsgKind.Error); }
        UpdateTariff();
        FillResults();
    }

    async void ExportResults()
    {
        if (results.Count == 0) return;
        await Exporter.Run(this, "speedtests_" + DateTime.Now.ToString("yyyy-MM-dd") + ".csv", history, path =>
        {
            var ci = CultureInfo.CurrentCulture;
            var sb = new StringBuilder();
            sb.AppendLine(L.P("Zeit;Anbieter;Server;Download Mbit/s;Upload Mbit/s;Ping ms;Jitter ms;Ping unter Last ms;Paketverlust %;% Tarif Download;Ergebnis-Link;Verbindung;Unplausibel",
                              "Time;Provider;Server;Download Mbit/s;Upload Mbit/s;Ping ms;Jitter ms;Loaded ping ms;Packet loss %;% of plan download;Result link;Connection;Implausible"));
            string F(double v) => double.IsNaN(v) ? "" : v.ToString("0.0", ci);
            foreach (var r in results)
                sb.AppendLine(string.Join(";", r.Time.ToString("yyyy-MM-dd HH:mm:ss"), r.Provider, Storage.Escape(r.Server), F(r.Down), F(r.Up), F(r.Ping), F(r.Jitter),
                    F(r.LoadedPing), F(r.Loss), tariffDown > 0 && !double.IsNaN(r.Down) ? F(100 * r.Down / tariffDown) : "", r.Url,
                    r.ConnText, r.Suspicious ? L.P("ja", "yes") : ""));
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        });
    }
}
