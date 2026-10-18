// NetMonitor (macOS/Linux) – Verlauf: Sitzungen und Paketverluste.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace NetMonitor;

class HistoryWindow : AppWindow
{
    readonly Func<string> flushCurrent;
    string currentId;
    List<HistoryEntry> entries = new();
    HistoryEntry shown;
    List<Sample>[] shownSamples;
    List<LossIncident> rawIncidents = new(), listedIncidents = new();
    int lossVersion;
    bool closed;

    TextBlock lblInfo, header, lblLossStatus;
    Segmented tabs, period;
    Toggle summarize;
    Control sessionsPage, lossPage;
    Table list, detail, lossGrid;
    TimeChart chart;
    Surface chartSurface, timelineSurface;
    DayTimeline timeline;
    KpiTile kpiCount, kpiLost, kpiLongest, kpiAvail;
    Donut donut;

    public HistoryWindow(Func<string> flushCurrent)
    {
        this.flushCurrent = flushCurrent;
        Title = L.P("NetMonitor – Verlauf", "NetMonitor – History");
        Width = 1560; Height = 980;
        MinWidth = 1150; MinHeight = 740;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { RowDefinitions = new RowDefinitions("70,*"), Margin = new Thickness(18, 14, 18, 18) };
        root.Children.Add(BuildHeader());
        sessionsPage = BuildSessionsPage();
        lossPage = BuildLossPage();
        var host = new Panel { Children = { sessionsPage, lossPage } };
        Grid.SetRow(host, 1);
        root.Children.Add(host);
        Content = root;

        ShowTab(0);
        Opened += (_, _) => Reload();
    }

    protected override void OnClosed(EventArgs e) { closed = true; base.OnClosed(e); }

    Control BuildHeader()
    {
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(6, 0, 6, 6) };
        lblInfo = Skin.Label("", 11.5, Skin.Faint);
        var titles = Column(2, Skin.Label(L.P("Verlauf", "History"), 23, Skin.Text, FontWeight.Bold), lblInfo);
        titles.Margin = new Thickness(0, 0, 30, 0);
        bar.Children.Add(titles);

        tabs = new Segmented(L.P("Sitzungen", "Sessions"), L.P("Paketverluste", "Packet loss")) { VerticalAlignment = VerticalAlignment.Center };
        tabs.Changed += (_, _) => ShowTab(tabs.SelectedIndex);
        Grid.SetColumn(tabs, 1);
        bar.Children.Add(tabs);

        var btnRefresh = new PillButton(L.P("Aktualisieren", "Refresh"), Skin.Cyan, true, Glyph.Refresh);
        var btnFolder = new PillButton(L.P("Ordner", "Folder"), Skin.Muted, true, Glyph.Folder);
        var btnDelete = new PillButton(L.P("Verlauf löschen …", "Delete history …"), Skin.Bad, true, Glyph.Delete);
        var btnExport = new PillButton("Export", Skin.Accent, false, Glyph.Download);
        btnRefresh.Click += (_, _) => Reload();
        btnFolder.Click += (_, _) => { Directory.CreateDirectory(Storage.Dir); Sh.Open(Storage.Dir); };
        btnDelete.Click += (_, _) => DeleteHistory();
        btnExport.Click += (_, _) => { if (tabs.SelectedIndex == 0) ExportSession(); else ExportIncidents(); };
        var buttons = Row(8, btnRefresh, btnFolder, btnDelete, btnExport);
        buttons.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(buttons, 2);
        bar.Children.Add(buttons);
        return bar;
    }

    public void SelectTab(int i) => tabs.SelectedIndex = i;

    void ShowTab(int i)
    {
        sessionsPage.IsVisible = i == 0;
        lossPage.IsVisible = i == 1;
        if (i == 1) ReloadLosses();
    }

    // ---------- Reiter „Sitzungen“ ----------

    Control BuildSessionsPage()
    {
        var page = new Grid { RowDefinitions = new RowDefinitions("33*,67*") };
        list = new Table { RowHeight = 36, HeaderHeight = 48 };
        list.SelectionChanged += (_, _) => ShowSelected();
        page.Children.Add(new Surface { Child = list, Padding = new Thickness(12), Margin = new Thickness(6) });

        var inner = new Grid { RowDefinitions = new RowDefinitions("34,Auto,*") };
        header = Skin.Label(L.P("Sitzung auswählen", "Select a session"), 17, Skin.Text, FontWeight.Bold);
        inner.Children.Add(header);
        detail = new Table
        {
            Columns = L.En ? new[] { "Target", "Address", "Avg ping", "Min", "Max", "Jitter", "Sent", "Lost", "Loss" }
                           : new[] { "Ziel", "Adresse", "Ø Ping", "Min", "Max", "Jitter", "Gesendet", "Verloren", "Verlust" },
            Weights = new double[] { 150, 160, 80, 70, 70, 70, 80, 80, 80 },
            HeaderHeight = 34, RowHeight = 32
        };
        Grid.SetRow(detail, 1);
        inner.Children.Add(detail);
        chart = new TimeChart { Margin = new Thickness(0, 12, 0, 0), EmptyText = L.P("Lade Messwerte …", "Loading samples …") };
        Grid.SetRow(chart, 2);
        inner.Children.Add(chart);
        chartSurface = new Surface { Child = inner, Padding = new Thickness(18, 14, 18, 12), Margin = new Thickness(6, 6, 6, 0) };
        Grid.SetRow(chartSurface, 1);
        page.Children.Add(chartSurface);
        return page;
    }

    static string PingText(Stats s) => !s.HasPing ? "–" : s.Avg.ToString("0.0") + " ms  (" + s.Min + "–" + s.Max + ")";

    public void Reload()
    {
        if (closed) return;
        currentId = flushCurrent();
        string selectedId = shown?.Id;
        entries = HistoryEntry.LoadAll();
        double hours = entries.Sum(e => e.Duration.TotalHours);
        lblInfo.Text = entries.Count + L.P(" Sitzung(en) · ", " session(s) · ") + hours.ToString("0.0") +
                       L.P(" h gemessen · ", " h measured · ") + Storage.Display(Storage.Home);

        var cols = new List<string> { L.P("Datum", "Date"), L.P("Zeitraum", "Period"), L.P("Dauer", "Duration"), "Status" };
        var weights = new List<double> { 92, 142, 74, 90 };
        var colors = new List<Color> { Skin.Muted, Skin.Muted, Skin.Muted, Skin.Muted };
        for (int i = 0; i < 5; i++)
        {
            string name = MainWindow.DefaultName(i);
            if (i < 3) name = entries.FirstOrDefault(e => e.Names.Length > i)?.Names[i] ?? name;
            cols.Add(name + L.P("\nØ (Min–Max)", "\navg (min–max)"));
            cols.Add(L.P("Verlust", "Loss"));
            weights.Add(i >= 3 ? 170 : 124);
            weights.Add(68);
            colors.Add(Skin.Targets[i]); colors.Add(Skin.Targets[i]);
        }
        list.Columns = cols.ToArray();
        list.Weights = weights.ToArray();
        list.HeaderColors = colors.ToArray();
        list.EmptyText = L.P("Noch keine Sitzungen – starte im Hauptfenster einen Scan.", "No sessions yet – start a scan in the main window.");
        list.Rows.Clear();
        int selectRow = 0;
        foreach (var e in entries)
        {
            bool live = e.Running && e.Id == currentId;
            var cells = new string[cols.Count];
            var cc = new Color?[cols.Count];
            var bold = new bool[cols.Count];
            cells[0] = e.Start.ToString(L.DayYear);
            cells[1] = e.Start.ToString("HH:mm:ss") + " – " + (e.End.Date != e.Start.Date ? e.End.ToString(L.DateTimeShort) : e.End.ToString("HH:mm:ss"));
            cells[2] = Skin.Duration(e.Duration);
            cells[3] = live ? L.P("● läuft", "● running") : e.Running ? L.P("unvollständig", "incomplete") : L.P("✓ beendet", "✓ finished");
            cc[3] = live ? Skin.Good : e.Running ? Skin.Warn : Skin.Muted;
            for (int i = 0; i < e.Stats.Length && 5 + 2 * i < cells.Length; i++)
            {
                var s = e.Stats[i];
                cells[4 + 2 * i] = (i >= 3 && s.Sent > 0 ? e.Names[i] + ":  " : "") + PingText(s);
                cells[5 + 2 * i] = s.Sent > 0 ? s.LossPct.ToString("0.00") + " %" : "–";
                cc[5 + 2 * i] = Skin.Loss(s.LossPct, s.Sent);
                bold[5 + 2 * i] = true;
            }
            for (int i = 0; i < cells.Length; i++) cells[i] ??= "";
            if (e.Id == selectedId) selectRow = list.Rows.Count;
            list.Rows.Add(new TableRow { Cells = cells, Colors = cc, Bold = bold, Tag = e });
        }
        shown = null;
        if (list.Rows.Count > 0) list.SelectedIndex = selectRow;
        else
        {
            header.Text = L.P("Noch keine Sitzungen – starte im Hauptfenster einen Scan.", "No sessions yet – start a scan in the main window.");
            detail.Rows.Clear(); detail.Height = detail.HeaderHeight + 4; detail.Refresh();
            chart.Series.Clear();
            chart.EmptyText = L.P("Keine Daten", "No data");
            chart.InvalidateVisual();
        }
        list.Refresh();
        if (lossPage.IsVisible) ReloadLosses();
    }

    async void ShowSelected()
    {
        if (list.SelectedTag is not HistoryEntry e || e == shown) return;
        shown = e;
        shownSamples = null;
        header.Text = e.Start.ToString(L.DayLong) + "   ·   " + e.Start.ToString("HH:mm:ss") + " – " +
                      e.End.ToString(e.End.Date != e.Start.Date ? L.DateTimeShort + ":ss" : "HH:mm:ss") +
                      L.P("   ·   Dauer ", "   ·   Duration ") + Skin.Duration(e.Duration) +
                      L.P("   ·   Intervall ", "   ·   Interval ") + e.Interval + " ms";

        detail.Rows.Clear();
        for (int i = 0; i < e.Stats.Length; i++)
        {
            var s = e.Stats[i];
            if (s.Sent == 0 && string.IsNullOrEmpty(e.Addrs[i])) continue;
            detail.Rows.Add(new TableRow
            {
                Cells = new[]
                {
                    "●  " + e.Names[i], e.Addrs[i], s.HasPing ? s.Avg.ToString("0.0") + " ms" : "–", s.HasPing ? s.Min + " ms" : "–",
                    s.HasPing ? s.Max + " ms" : "–", s.JitterCount > 0 ? s.Jitter.ToString("0.0") + " ms" : "–",
                    s.Sent.ToString(), s.Lost.ToString(), s.Sent > 0 ? s.LossPct.ToString("0.00") + " %" : "–"
                },
                Colors = new Color?[] { Skin.Targets[i % Skin.Targets.Length], null, null, null, null, null, null, s.Lost > 0 ? Skin.Bad : null, Skin.Loss(s.LossPct, s.Sent) },
                Bold = new[] { true, false, false, false, false, false, false, false, true }
            });
        }
        detail.Height = detail.HeaderHeight + detail.Rows.Count * detail.RowHeight + 4;
        detail.Refresh();

        chart.Series.Clear();
        chart.EmptyText = L.P("Lade Messwerte …", "Loading samples …");
        chart.InvalidateVisual();
        List<Sample>[] samples;
        try { samples = await Task.Run(e.LoadSamples); } catch { samples = null; }
        if (shown != e || closed) return;
        shownSamples = samples;
        FillChart(e);
    }

    // Lange Sitzungen werden in max. ~800 Zeitabschnitte zusammengefasst (Ø Ping + Verlustanteil).
    void FillChart(HistoryEntry e)
    {
        chart.Series.Clear();
        if (shownSamples == null)
        {
            chart.EmptyText = L.P("Messwerte konnten nicht geladen werden", "Samples could not be loaded");
            chart.InvalidateVisual();
            return;
        }
        var span = e.End - e.Start;
        long bucket = Math.Max(TimeSpan.FromMilliseconds(Math.Max(e.Interval, 200)).Ticks, span.Ticks / 800 + 1);
        for (int i = 0; i < shownSamples.Length; i++)
        {
            var series = new ChartSeries { Name = e.Names[i], Color = Skin.Targets[i % Skin.Targets.Length] };
            var buckets = new SortedDictionary<long, double[]>(); // Summe, ok, gesamt
            foreach (var s in shownSamples[i])
            {
                long b = (s.Time.Ticks - e.Start.Ticks) / bucket;
                if (!buckets.TryGetValue(b, out var acc)) buckets[b] = acc = new double[3];
                acc[2]++;
                if (s.Ok) { acc[0] += s.Rtt; acc[1]++; }
            }
            foreach (var kv in buckets)
            {
                var acc = kv.Value;
                series.Points.Add(new ChartPoint
                {
                    T = e.Start.Ticks + kv.Key * bucket + bucket / 2,
                    Y = acc[1] > 0 ? (float)(acc[0] / acc[1]) : float.NaN,
                    Loss = (float)((acc[2] - acc[1]) / acc[2])
                });
            }
            chart.Series.Add(series);
        }
        chart.MinT = e.Start.Ticks;
        chart.MaxT = Math.Max(e.End.Ticks, e.Start.Ticks + TimeSpan.TicksPerSecond * 10);
        chart.Bands = SpeedStore.Windows().Select(w => (w.Start.Ticks, w.End.Ticks)).ToList();
        chart.IntervalTicks = bucket;
        chart.EmptyText = L.P("Keine Messwerte in dieser Sitzung", "No samples in this session");
        chart.InvalidateVisual();
    }

    async void ExportSession()
    {
        var e = shown;
        if (e == null) return;
        await Exporter.Run(this, "netmonitor_" + e.Id + ".csv", chartSurface, path =>
        {
            var samples = shownSamples ?? e.LoadSamples();
            var meta = new List<string>
            {
                L.P("Sitzung;", "Session;") + e.Start.ToString("yyyy-MM-dd HH:mm:ss") + L.P(";bis;", ";to;") + e.End.ToString("yyyy-MM-dd HH:mm:ss"),
                L.P("Dauer;", "Duration;") + Skin.Duration(e.Duration),
                L.P("Intervall ms;", "Interval ms;") + e.Interval + ";Timeout ms;" + e.Timeout
            };
            CsvExport.Write(path, meta, e.Names, e.Addrs, e.Stats, samples);
        });
    }

    // ---------- Reiter „Paketverluste“ ----------

    Control BuildLossPage()
    {
        var page = new Grid { RowDefinitions = new RowDefinitions("52,140,270,*") };

        period = L.En ? new Segmented("Today", "7 days", "30 days", "All") : new Segmented("Heute", "7 Tage", "30 Tage", "Alles");
        period.SelectedIndex = 1;
        period.Changed += (_, _) => ReloadLosses();
        summarize = new Toggle(L.P("Serien zusammenfassen (Störungen < 10 min Abstand)", "Group series (incidents < 10 min apart)"), true);
        summarize.Changed += (_, _) => FillLossList();
        lblLossStatus = Skin.Label("", 12, Skin.Faint);
        var filter = Row(18, period, summarize, lblLossStatus);
        filter.Margin = new Thickness(6, 4, 0, 0);
        filter.VerticalAlignment = VerticalAlignment.Top;
        page.Children.Add(filter);

        var kpis = new Grid { ColumnDefinitions = new ColumnDefinitions("18*,18*,18*,18*,28*") };
        kpiCount = NewTile(L.P("Störungen", "Incidents"), Glyph.Warning, Skin.Bad);
        kpiLost = NewTile(L.P("Verlorene Pakete", "Lost packets"), Glyph.Packet, Skin.Orange);
        kpiLongest = NewTile(L.P("Längste Störung", "Longest incident"), Glyph.Clock, Skin.Yellow);
        kpiAvail = NewTile(L.P("Verfügbarkeit Internet", "Internet availability"), Glyph.Globe, Skin.Good);
        donut = new Donut { Margin = new Thickness(6) };
        Control[] tiles = { kpiCount, kpiLost, kpiLongest, kpiAvail, donut };
        for (int i = 0; i < tiles.Length; i++) { Grid.SetColumn(tiles[i], i); kpis.Children.Add(tiles[i]); }
        Grid.SetRow(kpis, 1);
        page.Children.Add(kpis);

        var tlInner = new Grid { RowDefinitions = new RowDefinitions("30,*"), ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        tlInner.Children.Add(Skin.Label(L.P("Tagesübersicht", "Daily overview"), 17, Skin.Text, FontWeight.Bold));
        var legend = new CauseLegend { Margin = new Thickness(16, 0, 0, 0) };
        Grid.SetColumn(legend, 1);
        tlInner.Children.Add(legend);
        timeline = new DayTimeline();
        timeline.IncidentClicked += SelectIncident;
        var scroll = new ScrollViewer { Content = timeline, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1);
        Grid.SetColumnSpan(scroll, 2);
        tlInner.Children.Add(scroll);
        timelineSurface = new Surface { Child = tlInner, Padding = new Thickness(16, 12, 12, 10), Margin = new Thickness(6) };
        Grid.SetRow(timelineSurface, 2);
        page.Children.Add(timelineSurface);

        lossGrid = new Table
        {
            Columns = L.En ? new[] { "Date", "Time", "Duration", "Incidents", "Lost packets", "Affected targets (lost packets)", "Likely cause" }
                           : new[] { "Datum", "Uhrzeit", "Dauer", "Störungen", "Verlorene Pakete", "Betroffene Ziele (verlorene Pakete)", "Vermutete Ursache" },
            Weights = new double[] { 70, 95, 65, 60, 70, 230, 170 }
        };
        lossGrid.SelectionChanged += (_, _) =>
        {
            var inc = lossGrid.SelectedTag as LossIncident;
            timeline.SelStart = inc?.Start ?? DateTime.MinValue;
            timeline.SelEnd = inc?.End ?? DateTime.MinValue;
            timeline.InvalidateVisual();
        };
        var gridSurface = new Surface { Child = lossGrid, Padding = new Thickness(12), Margin = new Thickness(6, 6, 6, 0) };
        Grid.SetRow(gridSurface, 3);
        page.Children.Add(gridSurface);
        return page;
    }

    static KpiTile NewTile(string title, Glyph glyph, Color tint) => new() { Title = title, Glyph = glyph, Tint = tint, Margin = new Thickness(6) };

    DateTime PeriodStart() => period.SelectedIndex switch
    {
        0 => DateTime.Today,
        1 => DateTime.Today.AddDays(-6),
        2 => DateTime.Today.AddDays(-29),
        _ => DateTime.MinValue
    };

    async void ReloadLosses()
    {
        int version = ++lossVersion;
        var from = PeriodStart();
        var sessions = entries.Where(e => e.End >= from).ToList();
        lblLossStatus.Text = L.P("Analysiere " + sessions.Count + " Sitzung(en) …", "Analysing " + sessions.Count + " session(s) …");
        List<LossIncident> all;
        try
        {
            all = await Task.Run(() =>
            {
                var r = sessions.SelectMany(LossAnalysis.ForSession).Where(inc => inc.Start >= from).ToList();
                r.Sort((a, b) => a.Start.CompareTo(b.Start));
                return r;
            });
        }
        catch (Exception ex)
        {
            if (version == lossVersion) lblLossStatus.Text = L.P("Analyse fehlgeschlagen: ", "Analysis failed: ") + ex.Message;
            return;
        }
        if (version != lossVersion || closed) return;
        rawIncidents = all;
        lblLossStatus.Text = "";

        // Kennzahlen
        double hours = 0; long sent = 0, lostExt = 0;
        foreach (var e in sessions)
        {
            var a = e.Start < from ? from : e.Start;
            hours += Math.Max(0, (e.End - a).TotalHours);
            for (int i = 0; i < 2 && i < e.Stats.Length; i++)
            {
                if (e.Stats[i].Sent == 0 || e.Stats[i].LossPct > 95) continue;
                sent += e.Stats[i].Sent; lostExt += e.Stats[i].Lost;
            }
        }
        int totalLost = all.Sum(i => i.TotalLost);
        var longest = all.OrderByDescending(i => i.Duration).FirstOrDefault();
        var causes = new int[4];
        foreach (var inc in all) causes[(int)inc.Cause]++;
        kpiCount.Value = all.Count.ToString();
        kpiCount.ValueColor = all.Count == 0 ? Skin.Good : Skin.Text;
        kpiCount.Sub = L.P("in ", "in ") + hours.ToString("0.0") + L.P(" h Messzeit", " h measured") +
            (hours > 0 && all.Count > 0 ? " · " + (all.Count / hours).ToString("0.0") + L.P(" pro h", " per h") : "");
        kpiLost.Value = totalLost.ToString();
        kpiLost.Sub = all.Count > 0 ? L.P("Ø ", "avg ") + ((double)totalLost / all.Count).ToString("0.0") + L.P(" pro Störung", " per incident")
                                    : L.P("keine Verluste", "no loss");
        kpiLongest.Value = longest != null ? Skin.ShortDuration(longest.Duration) : "–";
        kpiLongest.Sub = longest != null ? longest.Start.ToString(L.DateTimeShort + ":ss") : "";
        double avail = sent > 0 ? 100.0 * (sent - lostExt) / sent : -1;
        kpiAvail.Value = avail < 0 ? "–" : avail.ToString("0.00") + " %";
        kpiAvail.ValueColor = avail < 0 ? Skin.Text : Skin.Availability(avail);
        kpiAvail.Sub = L.P("Ping zu Cloudflare + Google", "Ping to Cloudflare + Google");
        donut.Segments.Clear();
        donut.Segments.Add((L.P("Internet / Provider", "Internet / ISP"), causes[(int)LossCause.Internet], Skin.CauseColor(LossCause.Internet)));
        donut.Segments.Add((L.P("Heimnetz / WLAN", "Home network / Wi-Fi"), causes[(int)LossCause.HomeNetwork], Skin.CauseColor(LossCause.HomeNetwork)));
        donut.Segments.Add((L.P("Einzelner Dienst", "Single service"), causes[(int)LossCause.SingleService], Skin.CauseColor(LossCause.SingleService)));
        donut.Segments.Add((L.P("Nur Ziel-Server", "Target server only"), causes[(int)LossCause.TargetServer], Skin.CauseColor(LossCause.TargetServer)));
        foreach (var c in new Control[] { kpiCount, kpiLost, kpiLongest, kpiAvail, donut }) c.InvalidateVisual();

        // Tagesübersicht
        var rows = new Dictionary<DateTime, DayRow>();
        DayRow RowFor(DateTime d) => rows.TryGetValue(d, out var r) ? r : rows[d] = new DayRow { Day = d };
        foreach (var e in sessions)
            for (var d = e.Start.Date; d <= e.End.Date; d = d.AddDays(1))
            {
                if (d < from.Date) continue;
                var a = e.Start > d ? e.Start : d;
                var b = e.End < d.AddDays(1) ? e.End : d.AddDays(1);
                if (b > a) RowFor(d).Coverage.Add((a, b));
            }
        foreach (var inc in all) RowFor(inc.Start.Date).Incidents.Add(inc);
        var dayList = rows.Values.OrderByDescending(r => r.Day).Take(60).ToList();
        timeline.Rows = dayList;
        timeline.InvalidateMeasure();
        timeline.InvalidateVisual();

        FillLossList();
    }

    void FillLossList()
    {
        listedIncidents = summarize.Checked ? LossAnalysis.Group(rawIncidents, TimeSpan.FromMinutes(10)) : new List<LossIncident>(rawIncidents);
        listedIncidents.Reverse();
        lossGrid.Rows.Clear();
        foreach (var inc in listedIncidents)
        {
            lossGrid.Rows.Add(new TableRow
            {
                Cells = new[]
                {
                    inc.Start.ToString(L.Day), inc.Start.ToString("HH:mm:ss") + " – " + inc.End.ToString("HH:mm:ss"), Skin.ShortDuration(inc.Duration),
                    inc.Count > 1 ? L.P("Serie · ", "Series · ") + inc.Count : "1", inc.TotalLost.ToString(), inc.Affected, "●  " + inc.CauseText
                },
                Colors = new Color?[] { null, null, null, inc.Count > 1 ? Skin.Warn : null, inc.TotalLost >= 10 ? Skin.Bad : null, null, Skin.CauseColor(inc.Cause) },
                Bold = new[] { false, false, false, inc.Count > 1, false, false, true },
                Tag = inc, Tooltip = inc.Affected
            });
        }
        lossGrid.Selected.Clear();
        lossGrid.Refresh();
        lossGrid.EmptyText = L.P("Keine Paketverluste im gewählten Zeitraum – alles sauber", "No packet loss in the selected period – all clean");
        if (rawIncidents.Count == 0) lblLossStatus.Text = L.P("Keine Paketverluste im gewählten Zeitraum – alles sauber", "No packet loss in the selected period – all clean");
    }

    void SelectIncident(LossIncident inc)
    {
        int i = listedIncidents.FindIndex(li => inc.Start >= li.Start && inc.Start <= li.End);
        if (i >= 0) lossGrid.SelectedIndex = i;
    }

    async void ExportIncidents()
    {
        if (listedIncidents.Count == 0)
        {
            await MessageDialog.Show(this, L.P("Im gewählten Zeitraum gibt es keine Störungen zum Exportieren.", "There are no incidents to export in the selected period."));
            return;
        }
        await Exporter.Run(this, L.P("paketverluste_", "packet-loss_") + DateTime.Now.ToString("yyyy-MM-dd") + ".csv", timelineSurface,
            path => CsvExport.WriteIncidents(path, listedIncidents));
    }

    // ---------- Löschen ----------

    async void DeleteHistory()
    {
        currentId = flushCurrent();
        var sel = tabs.SelectedIndex == 0 ? list.SelectedTag as HistoryEntry : null;
        var dlg = new DeleteDialog(entries, sel, currentId);
        if (!await dlg.ShowDialog<bool>(this)) return;
        int failed = 0;
        foreach (var e in dlg.ToDelete)
            try { e.Delete(); } catch { failed++; }
        if (failed > 0)
            await MessageDialog.Show(this, failed + L.P(" Sitzung(en) konnten nicht gelöscht werden.", " session(s) could not be deleted."), MsgKind.Warning);
        shown = null;
        Reload();
    }
}
