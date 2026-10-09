// NetMonitor – Verlauf (Sitzungen + Paketverluste), Löschdialog und Spielserver-Auswahl.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NetMonitor
{
    public class HistoryForm : Form
    {
        readonly Func<string> flushCurrent;
        string currentId;
        List<HistoryEntry> entries = new List<HistoryEntry>();
        HistoryEntry shown;
        List<Sample>[] shownSamples;
        List<LossIncident> rawIncidents = new List<LossIncident>(), listedIncidents = new List<LossIncident>();
        int lossVersion;

        Label lblInfo, header, lblLossStatus;
        Segmented tabs, period;
        Toggle summarize;
        Panel sessionsPage, lossPage;
        TableLayoutPanel detailLayout;
        DataGridView list, detail, lossGrid;
        TimeChart chart;
        Surface chartSurface, timelineSurface;
        DayTimeline timeline;
        Panel timelineScroll;
        KpiTile kpiCount, kpiLost, kpiLongest, kpiAvail;
        Donut donut;

        public HistoryForm(Func<string> flushCurrent)
        {
            this.flushCurrent = flushCurrent;
            Text = L.P("NetMonitor – Verlauf", "NetMonitor – History");
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.F(9.5f);
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(Theme.S(1560), Theme.S(980));
            MinimumSize = new Size(Theme.S(1200), Theme.S(760));
            Icon = Theme.AppIcon;
            Theme.DarkWindow(this);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Theme.Bg,
                Padding = new Padding(Theme.S(18), Theme.S(14), Theme.S(18), Theme.S(18)) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(70)));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.Controls.Add(BuildHeader(), 0, 0);

            var host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Margin = new Padding(0) };
            sessionsPage = BuildSessionsPage();
            lossPage = BuildLossPage();
            host.Controls.Add(sessionsPage);
            host.Controls.Add(lossPage);
            root.Controls.Add(host, 0, 1);
            Controls.Add(root);

            ShowTab(0);
            Load += delegate { Reload(); };
        }

        Control BuildHeader()
        {
            var bar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Theme.Bg, Margin = new Padding(Theme.S(6), 0, Theme.S(6), Theme.S(6)) };
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var titles = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, BackColor = Theme.Bg, Margin = new Padding(0, 0, Theme.S(30), 0) };
            titles.Controls.Add(Theme.MakeLabel(L.P("Verlauf", "History"), Theme.D(17f), Theme.Text, Theme.Bg));
            lblInfo = Theme.MakeLabel("", Theme.F(8.5f), Theme.Faint, Theme.Bg);
            titles.Controls.Add(lblInfo);
            bar.Controls.Add(titles, 0, 0);

            tabs = new Segmented(L.P("Sitzungen", "Sessions"), L.P("Paketverluste", "Packet loss")) { Anchor = AnchorStyles.Left, Margin = new Padding(0, Theme.S(8), 0, 0) };
            tabs.Changed += delegate { ShowTab(tabs.SelectedIndex); };
            bar.Controls.Add(tabs, 1, 0);

            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, BackColor = Theme.Bg, Margin = new Padding(0, Theme.S(8), 0, 0) };
            var btnRefresh = new PillButton(L.P("Aktualisieren", "Refresh"), Theme.Cyan, true, Icons.Sync);
            var btnFolder = new PillButton(L.P("Ordner", "Folder"), Theme.Muted, true, Icons.Folder);
            var btnDelete = new PillButton(L.P("Verlauf löschen …", "Delete history …"), Theme.Bad, true, Icons.Delete);
            var btnExport = new PillButton("Export", Theme.Accent, false, Icons.Download);
            btnRefresh.Click += delegate { Reload(); };
            btnFolder.Click += delegate
            {
                Directory.CreateDirectory(Storage.Dir);
                System.Diagnostics.Process.Start("explorer.exe", "\"" + Storage.Dir + "\"");
            };
            btnDelete.Click += delegate { DeleteHistory(); };
            btnExport.Click += delegate { if (tabs.SelectedIndex == 0) ExportSession(); else ExportIncidents(); };
            buttons.Controls.AddRange(new Control[] { btnRefresh, btnFolder, btnDelete, btnExport });
            bar.Controls.Add(buttons, 2, 0);
            return bar;
        }

        void ShowTab(int i)
        {
            sessionsPage.Visible = i == 0;
            lossPage.Visible = i == 1;
            if (i == 1) ReloadLosses();
        }

        static string DataFolderText()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (Storage.Home.StartsWith(appData, StringComparison.OrdinalIgnoreCase)) return "%APPDATA%" + Storage.Home.Substring(appData.Length);
            if (Storage.Home.StartsWith(profile, StringComparison.OrdinalIgnoreCase)) return "%USERPROFILE%" + Storage.Home.Substring(profile.Length);
            return Storage.Home;
        }

        // ---------- Reiter „Sitzungen“ ----------

        Panel BuildSessionsPage()
        {
            var page = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Theme.Bg, Margin = new Padding(0) };
            page.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
            page.RowStyles.Add(new RowStyle(SizeType.Percent, 67));

            var listSurface = new Surface { Dock = DockStyle.Fill, Padding = new Padding(Theme.S(12)), Margin = new Padding(Theme.S(6), Theme.S(6), Theme.S(6), Theme.S(6)) };
            list = new DataGridView { Dock = DockStyle.Fill };
            Theme.StyleGrid(list);
            list.SelectionChanged += delegate { ShowSelected(); };
            listSurface.Controls.Add(list);
            page.Controls.Add(listSurface, 0, 0);

            chartSurface = new Surface { Dock = DockStyle.Fill, Padding = new Padding(Theme.S(18), Theme.S(14), Theme.S(18), Theme.S(12)), Margin = new Padding(Theme.S(6), Theme.S(6), Theme.S(6), 0) };
            var inner = detailLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Theme.Surface };
            inner.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(34)));
            inner.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(214)));
            inner.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            header = Theme.MakeLabel(L.P("Sitzung auswählen", "Select a session"), Theme.D(12.5f), Theme.Text, Theme.Surface);
            inner.Controls.Add(header, 0, 0);
            detail = new DataGridView { Dock = DockStyle.Fill, Margin = new Padding(0) };
            Theme.StyleGrid(detail);
            var cols = L.En
                ? new[] { "Target", "Address", "Avg ping", "Min", "Max", "Jitter", "Sent", "Lost", "Loss" }
                : new[] { "Ziel", "Adresse", "Ø Ping", "Min", "Max", "Jitter", "Gesendet", "Verloren", "Verlust" };
            foreach (var c in cols) detail.Columns.Add(c, c);
            detail.Columns[1].FillWeight = 160;
            inner.Controls.Add(detail, 0, 1);
            chart = new TimeChart { Dock = DockStyle.Fill, Margin = new Padding(0, Theme.S(12), 0, 0), EmptyText = L.P("Lade Messwerte …", "Loading samples …") };
            inner.Controls.Add(chart, 0, 2);
            chartSurface.Controls.Add(inner);
            page.Controls.Add(chartSurface, 0, 1);
            return page;
        }

        static string PingText(Stats s)
        {
            if (!s.HasPing) return "–";
            return s.Avg.ToString("0.0") + " ms  (" + s.Min + "–" + s.Max + ")";
        }

        public void Reload()
        {
            currentId = flushCurrent();
            string selectedId = shown != null ? shown.Id : null;
            entries = HistoryEntry.LoadAll();
            double hours = 0;
            foreach (var e in entries) hours += e.Duration.TotalHours;
            lblInfo.Text = entries.Count + L.P(" Sitzung(en) · ", " session(s) · ") + hours.ToString("0.0") +
                           L.P(" h gemessen · ", " h measured · ") + DataFolderText();

            list.SuspendLayout();
            list.Rows.Clear();
            list.Columns.Clear();
            list.Columns.Add("date", L.P("Datum", "Date"));
            list.Columns.Add("range", L.P("Zeitraum", "Period"));
            list.Columns.Add("dur", L.P("Dauer", "Duration"));
            list.Columns.Add("state", "Status");
            var names = new string[5];
            for (int i = 0; i < names.Length; i++)
            {
                names[i] = MainForm.DefaultName(i);
                if (i < 3)
                    foreach (var e in entries)
                        if (e.Names.Length > i) { names[i] = e.Names[i]; break; }
                var pc = list.Columns.Add("p" + i, names[i] + L.P("\nØ (Min–Max)", "\navg (min–max)"));
                var lc = list.Columns.Add("l" + i, L.P("Verlust", "Loss"));
                list.Columns[pc].FillWeight = i >= 3 ? 170 : 124;
                list.Columns[lc].FillWeight = 68;
                list.Columns[pc].HeaderCell.Style.ForeColor = Theme.Targets[i % Theme.Targets.Length];
                list.Columns[lc].HeaderCell.Style.ForeColor = Theme.Targets[i % Theme.Targets.Length];
            }
            list.Columns[0].FillWeight = 92;
            list.Columns[1].FillWeight = 142;
            list.Columns[2].FillWeight = 74;
            list.Columns[3].FillWeight = 90;
            foreach (DataGridViewColumn c in list.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;

            int selectRow = 0;
            foreach (var e in entries)
            {
                var row = new DataGridViewRow();
                row.CreateCells(list);
                bool live = e.Running && e.Id == currentId;
                row.Cells[0].Value = e.Start.ToString(L.DayYear);
                row.Cells[1].Value = e.Start.ToString("HH:mm:ss") + " – " +
                    (e.End.Date != e.Start.Date ? e.End.ToString(L.DateTimeShort) : e.End.ToString("HH:mm:ss"));
                row.Cells[2].Value = Theme.Duration(e.Duration);
                row.Cells[3].Value = live ? L.P("● läuft", "● running") : e.Running ? L.P("unvollständig", "incomplete") : L.P("✓ beendet", "✓ finished");
                row.Cells[3].Style.ForeColor = live ? Theme.Good : e.Running ? Theme.Warn : Theme.Muted;
                for (int i = 0; i < e.Stats.Length && 4 + 2 * i + 1 < row.Cells.Count; i++)
                {
                    var s = e.Stats[i];
                    row.Cells[4 + 2 * i].Value = (i >= 3 && s.Sent > 0 ? e.Names[i] + ":  " : "") + PingText(s);
                    row.Cells[5 + 2 * i].Value = s.Sent > 0 ? s.LossPct.ToString("0.00") + " %" : "–";
                    row.Cells[5 + 2 * i].Style.ForeColor = Theme.Loss(s.LossPct, s.Sent);
                    row.Cells[5 + 2 * i].Style.Font = Theme.F(9.5f, FontStyle.Bold);
                }
                row.Tag = e;
                if (e.Id == selectedId) selectRow = list.Rows.Count;
                list.Rows.Add(row);
            }
            list.ResumeLayout();

            shown = null;
            if (list.Rows.Count > 0)
            {
                list.ClearSelection();
                list.Rows[selectRow].Selected = true;
                list.CurrentCell = list.Rows[selectRow].Cells[0];
                ShowSelected();
            }
            else
            {
                header.Text = L.P("Noch keine Sitzungen – starte im Hauptfenster einen Scan.", "No sessions yet – start a scan in the main window.");
                detail.Rows.Clear();
                chart.Series.Clear();
                chart.EmptyText = L.P("Keine Daten", "No data");
                chart.Invalidate();
            }
            if (lossPage.Visible) ReloadLosses();
        }

        HistoryEntry Selected()
        {
            return list.SelectedRows.Count == 0 ? null : list.SelectedRows[0].Tag as HistoryEntry;
        }

        async void ShowSelected()
        {
            var e = Selected();
            if (e == null || e == shown) return;
            shown = e;
            shownSamples = null;

            header.Text = e.Start.ToString(L.DayLong) + "   ·   " + e.Start.ToString("HH:mm:ss") + " – " +
                          e.End.ToString(e.End.Date != e.Start.Date ? L.DateTimeShort + ":ss" : "HH:mm:ss") +
                          L.P("   ·   Dauer ", "   ·   Duration ") + Theme.Duration(e.Duration) +
                          L.P("   ·   Intervall ", "   ·   Interval ") + e.Interval + " ms";

            detail.Rows.Clear();
            for (int i = 0; i < e.Stats.Length; i++)
            {
                var s = e.Stats[i];
                if (s.Sent == 0 && string.IsNullOrEmpty(e.Addrs[i])) continue;
                int r = detail.Rows.Add("●  " + e.Names[i], e.Addrs[i],
                    s.HasPing ? s.Avg.ToString("0.0") + " ms" : "–",
                    s.HasPing ? s.Min + " ms" : "–",
                    s.HasPing ? s.Max + " ms" : "–",
                    s.JitterCount > 0 ? s.Jitter.ToString("0.0") + " ms" : "–",
                    s.Sent, s.Lost,
                    s.Sent > 0 ? s.LossPct.ToString("0.00") + " %" : "–");
                detail.Rows[r].Cells[0].Style.ForeColor = Theme.Targets[i % Theme.Targets.Length];
                detail.Rows[r].Cells[7].Style.ForeColor = s.Lost > 0 ? Theme.Bad : Theme.Text;
                detail.Rows[r].Cells[8].Style.ForeColor = Theme.Loss(s.LossPct, s.Sent);
                detail.Rows[r].Cells[8].Style.Font = Theme.F(9.5f, FontStyle.Bold);
            }
            detail.ClearSelection();
            detailLayout.RowStyles[1].Height = detail.ColumnHeadersHeight + detail.Rows.Count * detail.RowTemplate.Height + Theme.S(4);

            chart.Series.Clear();
            chart.EmptyText = L.P("Lade Messwerte …", "Loading samples …");
            chart.Invalidate();
            List<Sample>[] samples;
            try { samples = await Task.Run(() => e.LoadSamples()); }
            catch { samples = null; }
            if (shown != e) return;
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
                chart.Invalidate();
                return;
            }
            var span = e.End - e.Start;
            long bucket = Math.Max(TimeSpan.FromMilliseconds(Math.Max(e.Interval, 200)).Ticks, span.Ticks / 800 + 1);
            for (int i = 0; i < shownSamples.Length; i++)
            {
                var series = new ChartSeries { Name = e.Names[i], Color = Theme.Targets[i % Theme.Targets.Length] };
                var buckets = new SortedDictionary<long, double[]>(); // Summe, ok, gesamt
                foreach (var s in shownSamples[i])
                {
                    long b = (s.Time.Ticks - e.Start.Ticks) / bucket;
                    double[] acc;
                    if (!buckets.TryGetValue(b, out acc)) buckets[b] = acc = new double[3];
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
            chart.IntervalTicks = bucket;
            chart.EmptyText = L.P("Keine Messwerte in dieser Sitzung", "No samples in this session");
            chart.Invalidate();
        }

        void ExportSession()
        {
            var e = shown;
            if (e == null) return;
            CsvExport.ExportWithDialog(this, "netmonitor_" + e.Id + ".csv", chartSurface, path =>
            {
                var samples = shownSamples ?? e.LoadSamples();
                var meta = new List<string>
                {
                    L.P("Sitzung;", "Session;") + e.Start.ToString("yyyy-MM-dd HH:mm:ss") + L.P(";bis;", ";to;") + e.End.ToString("yyyy-MM-dd HH:mm:ss"),
                    L.P("Dauer;", "Duration;") + Theme.Duration(e.Duration),
                    L.P("Intervall ms;", "Interval ms;") + e.Interval + ";Timeout ms;" + e.Timeout
                };
                CsvExport.Write(path, meta, e.Names, e.Addrs, e.Stats, samples);
            });
        }

        // ---------- Reiter „Paketverluste“ ----------

        Panel BuildLossPage()
        {
            var page = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Theme.Bg, Margin = new Padding(0) };
            page.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(52)));
            page.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(140)));
            page.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(270)));
            page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var filter = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = Theme.Bg, Margin = new Padding(Theme.S(6), Theme.S(4), 0, 0) };
            period = L.En ? new Segmented("Today", "7 days", "30 days", "All") : new Segmented("Heute", "7 Tage", "30 Tage", "Alles");
            period.SelectedIndex = 1;
            period.Changed += delegate { ReloadLosses(); };
            summarize = new Toggle(L.P("Serien zusammenfassen (Störungen < 10 min Abstand)", "Group series (incidents < 10 min apart)"), true)
                { Margin = new Padding(Theme.S(18), 0, 0, 0) };
            summarize.Changed += delegate { FillLossList(); };
            lblLossStatus = Theme.MakeLabel("", Theme.F(9f), Theme.Faint, Theme.Bg);
            lblLossStatus.Margin = new Padding(Theme.S(18), Theme.S(11), 0, 0);
            filter.Controls.AddRange(new Control[] { period, summarize, lblLossStatus });
            page.Controls.Add(filter, 0, 0);

            var kpis = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, BackColor = Theme.Bg, Margin = new Padding(0) };
            for (int i = 0; i < 4; i++) kpis.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18));
            kpis.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
            kpiCount = NewTile(L.P("Störungen", "Incidents"), Icons.Warning, Theme.Bad);
            kpiLost = NewTile(L.P("Verlorene Pakete", "Lost packets"), Icons.Packet, Theme.Orange);
            kpiLongest = NewTile(L.P("Längste Störung", "Longest incident"), Icons.Clock, Theme.Yellow);
            kpiAvail = NewTile(L.P("Verfügbarkeit Internet", "Internet availability"), Icons.Globe, Theme.Good);
            donut = new Donut { Dock = DockStyle.Fill, Margin = new Padding(Theme.S(6)) };
            kpis.Controls.Add(kpiCount, 0, 0); kpis.Controls.Add(kpiLost, 1, 0); kpis.Controls.Add(kpiLongest, 2, 0);
            kpis.Controls.Add(kpiAvail, 3, 0); kpis.Controls.Add(donut, 4, 0);
            page.Controls.Add(kpis, 0, 1);

            timelineSurface = new Surface { Dock = DockStyle.Fill, Padding = new Padding(Theme.S(16), Theme.S(12), Theme.S(12), Theme.S(10)), Margin = new Padding(Theme.S(6)) };
            var tlInner = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = Theme.Surface };
            tlInner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tlInner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tlInner.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(30)));
            tlInner.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            tlInner.Controls.Add(Theme.MakeLabel(L.P("Tagesübersicht", "Daily overview"), Theme.D(12.5f), Theme.Text, Theme.Surface), 0, 0);
            tlInner.Controls.Add(new CauseLegend { Dock = DockStyle.Fill, Margin = new Padding(Theme.S(16), 0, 0, 0) }, 1, 0);
            timelineScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Surface, Margin = new Padding(0) };
            Theme.DarkScroll(timelineScroll);
            timeline = new DayTimeline { Dock = DockStyle.Top };
            timeline.IncidentClicked += SelectIncident;
            timelineScroll.Controls.Add(timeline);
            tlInner.Controls.Add(timelineScroll, 0, 1);
            tlInner.SetColumnSpan(timelineScroll, 2);
            timelineSurface.Controls.Add(tlInner);
            page.Controls.Add(timelineSurface, 0, 2);

            var gridSurface = new Surface { Dock = DockStyle.Fill, Padding = new Padding(Theme.S(12)), Margin = new Padding(Theme.S(6), Theme.S(6), Theme.S(6), 0) };
            lossGrid = new DataGridView { Dock = DockStyle.Fill };
            Theme.StyleGrid(lossGrid);
            var cols = L.En
                ? new[] { "Date", "Time", "Duration", "Incidents", "Lost packets", "Affected targets (lost packets)", "Likely cause" }
                : new[] { "Datum", "Uhrzeit", "Dauer", "Störungen", "Verlorene Pakete", "Betroffene Ziele (verlorene Pakete)", "Vermutete Ursache" };
            foreach (var c in cols) lossGrid.Columns.Add(c, c);
            lossGrid.Columns[0].FillWeight = 70;
            lossGrid.Columns[1].FillWeight = 95;
            lossGrid.Columns[2].FillWeight = 65;
            lossGrid.Columns[3].FillWeight = 60;
            lossGrid.Columns[4].FillWeight = 70;
            lossGrid.Columns[5].FillWeight = 230;
            lossGrid.Columns[6].FillWeight = 170;
            lossGrid.SelectionChanged += delegate
            {
                var inc = lossGrid.SelectedRows.Count > 0 ? lossGrid.SelectedRows[0].Tag as LossIncident : null;
                timeline.SelStart = inc != null ? inc.Start : DateTime.MinValue;
                timeline.SelEnd = inc != null ? inc.End : DateTime.MinValue;
                timeline.Invalidate();
            };
            gridSurface.Controls.Add(lossGrid);
            page.Controls.Add(gridSurface, 0, 3);
            return page;
        }

        static KpiTile NewTile(string title, string glyph, Color tint)
        {
            return new KpiTile { Title = title, Glyph = glyph, Tint = tint, Dock = DockStyle.Fill, Margin = new Padding(Theme.S(6)) };
        }

        DateTime PeriodStart()
        {
            switch (period.SelectedIndex)
            {
                case 0: return DateTime.Today;
                case 1: return DateTime.Today.AddDays(-6);
                case 2: return DateTime.Today.AddDays(-29);
                default: return DateTime.MinValue;
            }
        }

        async void ReloadLosses()
        {
            int version = ++lossVersion;
            var from = PeriodStart();
            var sessions = new List<HistoryEntry>();
            foreach (var e in entries) if (e.End >= from) sessions.Add(e);
            lblLossStatus.Text = L.P("Analysiere " + sessions.Count + " Sitzung(en) …", "Analysing " + sessions.Count + " session(s) …");
            List<LossIncident> all;
            try
            {
                all = await Task.Run(() =>
                {
                    var r = new List<LossIncident>();
                    foreach (var e in sessions)
                        foreach (var inc in LossAnalysis.ForSession(e))
                            if (inc.Start >= from) r.Add(inc);
                    r.Sort((a, b) => a.Start.CompareTo(b.Start));
                    return r;
                });
            }
            catch (Exception ex)
            {
                if (version == lossVersion) lblLossStatus.Text = L.P("Analyse fehlgeschlagen: ", "Analysis failed: ") + ex.Message;
                return;
            }
            if (version != lossVersion || IsDisposed) return;
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
            int totalLost = 0; LossIncident longest = null;
            var causes = new int[4];
            foreach (var inc in all)
            {
                totalLost += inc.TotalLost;
                causes[(int)inc.Cause]++;
                if (longest == null || inc.Duration > longest.Duration) longest = inc;
            }
            kpiCount.Value = all.Count.ToString();
            kpiCount.ValueColor = all.Count == 0 ? Theme.Good : Theme.Text;
            kpiCount.Sub = L.P("in ", "in ") + hours.ToString("0.0") + L.P(" h Messzeit", " h measured") +
                (hours > 0 && all.Count > 0 ? " · " + (all.Count / hours).ToString("0.0") + L.P(" pro h", " per h") : "");
            kpiLost.Value = totalLost.ToString();
            kpiLost.Sub = all.Count > 0 ? L.P("Ø ", "avg ") + ((double)totalLost / all.Count).ToString("0.0") + L.P(" pro Störung", " per incident")
                                        : L.P("keine Verluste", "no loss");
            kpiLongest.Value = longest != null ? Theme.ShortDuration(longest.Duration) : "–";
            kpiLongest.Sub = longest != null ? longest.Start.ToString(L.DateTimeShort + ":ss") : "";
            double avail = sent > 0 ? 100.0 * (sent - lostExt) / sent : -1;
            kpiAvail.Value = avail < 0 ? "–" : avail.ToString("0.00") + " %";
            kpiAvail.ValueColor = avail < 0 ? Theme.Text : Theme.Availability(avail);
            kpiAvail.Sub = L.P("Ping zu Cloudflare + Google", "Ping to Cloudflare + Google");
            donut.Title = L.P("Ursachen", "Causes");
            donut.Segments.Clear();
            donut.Segments.Add(Tuple.Create(L.P("Internet / Provider", "Internet / ISP"), (double)causes[(int)LossCause.Internet], Theme.CauseColor(LossCause.Internet)));
            donut.Segments.Add(Tuple.Create(L.P("Heimnetz / WLAN", "Home network / Wi-Fi"), (double)causes[(int)LossCause.HomeNetwork], Theme.CauseColor(LossCause.HomeNetwork)));
            donut.Segments.Add(Tuple.Create(L.P("Einzelner Dienst", "Single service"), (double)causes[(int)LossCause.SingleService], Theme.CauseColor(LossCause.SingleService)));
            donut.Segments.Add(Tuple.Create(L.P("Nur Ziel-Server", "Target server only"), (double)causes[(int)LossCause.TargetServer], Theme.CauseColor(LossCause.TargetServer)));
            foreach (Control c in new Control[] { kpiCount, kpiLost, kpiLongest, kpiAvail, donut }) c.Invalidate();

            // Tagesübersicht
            var rows = new Dictionary<DateTime, DayRow>();
            Func<DateTime, DayRow> rowFor = d =>
            {
                DayRow r;
                if (!rows.TryGetValue(d, out r)) rows[d] = r = new DayRow { Day = d };
                return r;
            };
            foreach (var e in sessions)
                for (var d = e.Start.Date; d <= e.End.Date; d = d.AddDays(1))
                {
                    if (d < from.Date) continue;
                    var a = e.Start > d ? e.Start : d;
                    var b = e.End < d.AddDays(1) ? e.End : d.AddDays(1);
                    if (b > a) rowFor(d).Coverage.Add(new KeyValuePair<DateTime, DateTime>(a, b));
                }
            foreach (var inc in all) rowFor(inc.Start.Date).Incidents.Add(inc);
            var dayList = new List<DayRow>(rows.Values);
            dayList.Sort((a, b) => b.Day.CompareTo(a.Day));
            if (dayList.Count > 60) dayList.RemoveRange(60, dayList.Count - 60);
            timeline.Rows = dayList;
            timeline.Height = timeline.PreferredHeight;
            timeline.Invalidate();

            FillLossList();
        }

        void FillLossList()
        {
            listedIncidents = summarize.Checked ? LossAnalysis.Group(rawIncidents, TimeSpan.FromMinutes(10)) : new List<LossIncident>(rawIncidents);
            listedIncidents.Reverse();
            lossGrid.Rows.Clear();
            foreach (var inc in listedIncidents)
            {
                string time = inc.Start.ToString("HH:mm:ss") + " – " + inc.End.ToString("HH:mm:ss");
                int r = lossGrid.Rows.Add(inc.Start.ToString(L.Day), time, Theme.ShortDuration(inc.Duration),
                    inc.Count > 1 ? L.P("Serie · ", "Series · ") + inc.Count : "1", inc.TotalLost, inc.Affected, "●  " + inc.CauseText);
                var row = lossGrid.Rows[r];
                row.Tag = inc;
                row.Cells[6].Style.ForeColor = Theme.CauseColor(inc.Cause);
                row.Cells[6].Style.Font = Theme.F(9.5f, FontStyle.Bold);
                if (inc.Count > 1)
                {
                    row.Cells[3].Style.ForeColor = Theme.Warn;
                    row.Cells[3].Style.Font = Theme.F(9.5f, FontStyle.Bold);
                }
                row.Cells[4].Style.ForeColor = inc.TotalLost >= 10 ? Theme.Bad : Theme.Text;
            }
            lossGrid.ClearSelection();
            if (rawIncidents.Count == 0)
                lblLossStatus.Text = L.P("Keine Paketverluste im gewählten Zeitraum – alles sauber", "No packet loss in the selected period – all clean");
        }

        void SelectIncident(LossIncident inc)
        {
            foreach (DataGridViewRow row in lossGrid.Rows)
            {
                var li = row.Tag as LossIncident;
                if (li == null || inc.Start < li.Start || inc.Start > li.End) continue;
                lossGrid.ClearSelection();
                row.Selected = true;
                lossGrid.FirstDisplayedScrollingRowIndex = row.Index;
                return;
            }
        }

        void ExportIncidents()
        {
            if (listedIncidents.Count == 0)
            {
                MessageBox.Show(this, L.P("Im gewählten Zeitraum gibt es keine Störungen zum Exportieren.", "There are no incidents to export in the selected period."),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            CsvExport.ExportWithDialog(this, L.P("paketverluste_", "packet-loss_") + DateTime.Now.ToString("yyyy-MM-dd") + ".csv", timelineSurface,
                path => CsvExport.WriteIncidents(path, listedIncidents));
        }

        // ---------- Löschen ----------

        void DeleteHistory()
        {
            currentId = flushCurrent();
            var sel = tabs.SelectedIndex == 0 ? Selected() : null;
            using (var dlg = new DeleteDialog(entries, sel, currentId))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                int failed = 0;
                foreach (var e in dlg.ToDelete)
                {
                    try { e.Delete(); } catch { failed++; }
                }
                if (failed > 0)
                    MessageBox.Show(this, failed + L.P(" Sitzung(en) konnten nicht gelöscht werden.", " session(s) could not be deleted."),
                        Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            shown = null;
            Reload();
        }
    }

    // Farblegende der Störungs-Ursachen.
    class CauseLegend : Control
    {
        public CauseLegend()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var items = new[]
            {
                Tuple.Create(L.P("gemessen", "measured"), Theme.A(Theme.Accent, 120)),
                Tuple.Create(L.P("Internet / Provider", "Internet / ISP"), Theme.CauseColor(LossCause.Internet)),
                Tuple.Create(L.P("Heimnetz / WLAN", "Home network / Wi-Fi"), Theme.CauseColor(LossCause.HomeNetwork)),
                Tuple.Create(L.P("Einzelner Dienst", "Single service"), Theme.CauseColor(LossCause.SingleService)),
                Tuple.Create(L.P("Nur Ziel-Server", "Target server only"), Theme.CauseColor(LossCause.TargetServer))
            };
            int x = 0;
            foreach (var it in items)
            {
                Theme.FillRound(g, it.Item2, new RectangleF(x, Height / 2 - Theme.S(5), Theme.S(14), Theme.S(10)), Theme.S(3));
                Theme.DrawText(g, it.Item1, Theme.F(8.5f), Theme.Muted, new Rectangle(x + Theme.S(20), 0, Theme.S(200), Height), TextFormatFlags.VerticalCenter);
                x += Theme.S(32) + Theme.Measure(it.Item1, Theme.F(8.5f)).Width;
            }
        }
    }

    // Auswahl, welcher Teil des Verlaufs gelöscht wird.
    class DeleteDialog : Form
    {
        public readonly List<HistoryEntry> ToDelete = new List<HistoryEntry>();
        readonly List<HistoryEntry> entries;
        readonly HistoryEntry selected;
        readonly string runningId;
        readonly RadioButton[] options;
        readonly Label count;
        readonly PillButton ok;

        public DeleteDialog(List<HistoryEntry> entries, HistoryEntry selected, string runningId)
        {
            this.entries = entries; this.selected = selected; this.runningId = runningId;
            Text = L.P("Verlauf löschen", "Delete history");
            BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.F(10f);
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(Theme.S(540), Theme.S(380));
            Icon = Theme.AppIcon;
            Theme.DarkWindow(this);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Theme.Bg, Padding = new Padding(Theme.S(22)) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var head = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, BackColor = Theme.Bg };
            head.Controls.Add(Theme.MakeLabel(L.P("Verlauf löschen", "Delete history"), Theme.D(15f), Theme.Text, Theme.Bg));
            head.Controls.Add(Theme.MakeLabel(L.P("Gelöschte Sitzungen inkl. aller Messwerte lassen sich nicht wiederherstellen.",
                "Deleted sessions and all their samples cannot be restored."), Theme.F(9f), Theme.Faint, Theme.Bg));
            root.Controls.Add(head, 0, 0);

            var opts = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, Dock = DockStyle.Fill, WrapContents = false, BackColor = Theme.Bg, Padding = new Padding(0, Theme.S(14), 0, 0) };
            string[] labels =
            {
                selected != null ? L.P("Ausgewählte Sitzung (", "Selected session (") + selected.Start.ToString(L.Date + " HH:mm") + ")"
                                 : L.P("Ausgewählte Sitzung (keine ausgewählt)", "Selected session (none selected)"),
                L.P("Alles älter als 7 Tage", "Everything older than 7 days"),
                L.P("Alles älter als 30 Tage", "Everything older than 30 days"),
                L.P("Gesamten Verlauf", "Entire history")
            };
            options = new RadioButton[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                options[i] = new RadioButton
                {
                    Text = labels[i], AutoSize = true, ForeColor = Theme.Text, BackColor = Theme.Bg, Font = Theme.F(10.5f),
                    Margin = new Padding(0, Theme.S(6), 0, Theme.S(6)), Cursor = Cursors.Hand
                };
                options[i].CheckedChanged += delegate { UpdateCount(); };
                opts.Controls.Add(options[i]);
            }
            options[0].Enabled = selected != null && !(selected.Running && selected.Id == runningId);
            options[options[0].Enabled ? 0 : 1].Checked = true;
            root.Controls.Add(opts, 0, 1);

            count = Theme.MakeLabel("", Theme.F(9.5f, FontStyle.Bold), Theme.Warn, Theme.Bg);
            count.Margin = new Padding(0, 0, 0, Theme.S(12));
            root.Controls.Add(count, 0, 2);

            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, WrapContents = false, BackColor = Theme.Bg };
            ok = new PillButton(L.P("Endgültig löschen", "Delete permanently"), Theme.Bad, false, Icons.Delete);
            var cancel = new PillButton(L.P("Abbrechen", "Cancel"), Theme.Muted, true, null);
            ok.Click += delegate { if (ToDelete.Count > 0) DialogResult = DialogResult.OK; };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; };
            buttons.Controls.Add(ok); buttons.Controls.Add(cancel);
            root.Controls.Add(buttons, 0, 3);
            Controls.Add(root);
            UpdateCount();
        }

        void UpdateCount()
        {
            if (count == null) return; // noch im Konstruktor
            ToDelete.Clear();
            int choice = Array.FindIndex(options, o => o.Checked);
            DateTime cutoff = choice == 1 ? DateTime.Today.AddDays(-7) : choice == 2 ? DateTime.Today.AddDays(-30) : DateTime.MaxValue;
            foreach (var e in entries)
            {
                if (e.Running && e.Id == runningId) continue; // laufende Sitzung nie löschen
                if (choice == 0 && e != selected) continue;
                if ((choice == 1 || choice == 2) && e.End >= cutoff) continue;
                ToDelete.Add(e);
            }
            count.Text = ToDelete.Count == 0 ? L.P("Keine passenden Sitzungen.", "No matching sessions.")
                                             : ToDelete.Count + L.P(" Sitzung(en) werden gelöscht.", " session(s) will be deleted.");
            if (ok != null) ok.Enabled = ToDelete.Count > 0;
        }
    }

    // Zeigt die erkannten Gegenstellen; der wahrscheinlichste Spielserver ist vorausgewählt.
    class ServerPickDialog : Form
    {
        readonly List<Candidate> candidates;
        readonly DataGridView grid;
        readonly Label info;
        readonly PillButton ok;
        public string Address, NoteText;

        public ServerPickDialog(string game, List<Candidate> candidates)
        {
            this.candidates = candidates;
            Text = L.P("Spielserver erkannt", "Game server detected");
            BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.F(9.5f);
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(Theme.S(920), Theme.S(440));
            Icon = Theme.AppIcon;
            Theme.DarkWindow(this);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Theme.Bg, Padding = new Padding(Theme.S(20)) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var headBox = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, BackColor = Theme.Bg, Margin = new Padding(0, 0, 0, Theme.S(10)) };
            headBox.Controls.Add(Theme.MakeLabel(L.P("Netzwerkverkehr von ", "Network traffic of ") + game, Theme.D(15f), Theme.Text, Theme.Bg));
            headBox.Controls.Add(Theme.MakeLabel(L.P("Der wahrscheinlichste Spielserver (dauerhafter UDP-Verkehr in beide Richtungen) ist vorausgewählt.",
                "The most likely game server (steady UDP traffic in both directions) is preselected."), Theme.F(9f), Theme.Faint, Theme.Bg));
            root.Controls.Add(headBox, 0, 0);

            var surface = new Surface { Dock = DockStyle.Fill, Padding = new Padding(Theme.S(10)), Margin = new Padding(0) };
            grid = new DataGridView { Dock = DockStyle.Fill };
            Theme.StyleGrid(grid);
            var cols = L.En
                ? new[] { "Address", "Port", "Protocol", "Packets/s", "Data", "Ping", "Assessment" }
                : new[] { "Adresse", "Port", "Protokoll", "Pakete/s", "Daten", "Ping", "Einschätzung" };
            foreach (var c in cols) grid.Columns.Add(c, c);
            grid.Columns[0].FillWeight = 160;
            grid.Columns[6].FillWeight = 140;
            for (int i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                int r = grid.Rows.Add(c.Ip, c.Port, c.Proto, c.Rate.ToString("0.0"),
                    (c.Bytes / 1024.0).ToString("0") + " KB", L.P("teste …", "testing …"),
                    i == 0 ? L.P("★ wahrscheinlich", "★ likely") : c.Proto == "UDP" && c.Rate >= 10 ? L.P("möglich", "possible") : L.P("eher Nebenverkehr", "probably side traffic"));
                grid.Rows[r].Tag = c;
                grid.Rows[r].Cells[6].Style.ForeColor = i == 0 ? Theme.Good : Theme.Muted;
                if (i == 0) grid.Rows[r].Cells[6].Style.Font = Theme.F(9.5f, FontStyle.Bold);
            }
            surface.Controls.Add(grid);
            root.Controls.Add(surface, 0, 1);

            info = Theme.MakeLabel(L.P("Antwortet der Server nicht auf Ping, wird automatisch der letzte erreichbare Router davor gemessen.",
                "If the server does not answer pings, the last reachable router in front of it is measured instead."), Theme.F(9f), Theme.Faint, Theme.Bg);
            info.Margin = new Padding(0, Theme.S(12), 0, Theme.S(8));
            root.Controls.Add(info, 0, 2);

            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, WrapContents = false, BackColor = Theme.Bg };
            ok = new PillButton(L.P("Übernehmen", "Apply"), Theme.Pink, false, Icons.Check);
            var cancel = new PillButton(L.P("Abbrechen", "Cancel"), Theme.Muted, true, null);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; };
            ok.Click += delegate { Apply(); };
            buttons.Controls.Add(ok); buttons.Controls.Add(cancel);
            root.Controls.Add(buttons, 0, 3);
            Controls.Add(root);

            Shown += delegate { TestAll(); };
        }

        async void TestAll()
        {
            for (int i = 0; i < candidates.Count && !IsDisposed; i++)
            {
                var c = candidates[i];
                if (c.Answers == null) await TestOne(c);
                if (IsDisposed) return;
                var cell = grid.Rows[i].Cells[5];
                cell.Value = c.Answers == true ? c.PingMs + " ms" : L.P("keine Antwort", "no reply");
                cell.Style.ForeColor = c.Answers == true ? Theme.Good : Theme.Warn;
            }
        }

        static async Task TestOne(Candidate c)
        {
            long ms = await NetTools.Test(c.Ip);
            c.PingMs = ms;
            c.Answers = ms >= 0;
        }

        async void Apply()
        {
            if (grid.SelectedRows.Count == 0) return;
            var c = (Candidate)grid.SelectedRows[0].Tag;
            ok.Enabled = false; grid.Enabled = false;
            if (c.Answers == null) { info.Text = L.P("Teste Ping …", "Testing ping …"); await TestOne(c); }
            if (IsDisposed) return;
            if (c.Answers == true)
            {
                Address = c.Ip;
                NoteText = L.P("Spielserver direkt · ", "Game server direct · ") + c.Proto + " Port " + c.Port;
            }
            else
            {
                info.ForeColor = Theme.Warn;
                var hop = await NetTools.FindLastHop(c.Ip, n =>
                {
                    if (!IsDisposed) info.Text = L.P("Server blockt Ping – suche Router davor … (Hop ", "Server blocks ping – looking for the router in front … (hop ") + n + ")";
                });
                if (IsDisposed) return;
                if (hop != null && hop.Item1 != c.Ip)
                {
                    Address = hop.Item1;
                    NoteText = L.P("Server " + c.Ip + " blockt Ping → Router davor (Hop " + hop.Item2 + ")",
                                   "Server " + c.Ip + " blocks ping → router in front (hop " + hop.Item2 + ")");
                }
                else
                {
                    Address = c.Ip;
                    NoteText = hop != null ? L.P("Spielserver direkt · ", "Game server direct · ") + c.Proto + " Port " + c.Port
                                           : L.P("Server antwortet nicht auf Ping – Werte unzuverlässig", "Server does not answer pings – values unreliable");
                }
            }
            DialogResult = DialogResult.OK;
        }
    }
}
