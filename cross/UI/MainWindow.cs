// NetMonitor (macOS/Linux) – Hauptfenster (Dashboard).
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace NetMonitor;

class Target
{
    public string Name, Address;
    public Color Color;
    public TargetCard Card;
    public bool Busy;
    public Stats Total = new();
    public readonly Queue<bool> Window = new();
    public readonly List<Sample> Samples = new();
    public readonly ChartSeries Series = new();

    public Target(string name, string address, Color color)
    {
        Name = name; Address = address; Color = color;
        Series.Name = name; Series.Color = color;
    }

    public void Rename(string name) { Name = name; Series.Name = name; }

    public void Reset()
    {
        Total = new Stats();
        Window.Clear(); Samples.Clear(); Series.Points.Clear();
    }

    public void Add(Sample s)
    {
        Total.Add(s.Ok, s.Rtt);
        Window.Enqueue(s.Ok);
        while (Window.Count > Sample.WindowSize) Window.Dequeue();
        s.Loss60 = 100.0 * Window.Count(ok => !ok) / Window.Count;
        Samples.Add(s);
        Series.Points.Add(new ChartPoint { T = s.Time.Ticks, Y = s.Ok ? s.Rtt : float.NaN, Loss = s.Ok ? 0 : 1 });
    }

    public double Loss60 => Samples.Count == 0 ? 0 : Samples[^1].Loss60;
    public bool Active => !string.IsNullOrEmpty(Address);
    // Ein Ziel, das praktisch nie antwortet, zählt nicht als Störung.
    public bool Unreachable => Total.Sent >= 20 && Total.LossPct > 95;
}

// Karte eines Ziels: komplett selbst gezeichnet, nur die Eingabefelder sind echte Steuerelemente.
class TargetCard : Panel
{
    readonly Target target;
    readonly bool editable, game;
    public readonly TextBox NameInput, AddrInput;
    public readonly ComboBox ProcessSelect;
    public readonly PillButton Detect;

    public string StatusText = "", BigText = "–", BigUnit = "ms", Note = "";
    public Color StatusColor = Skin.Muted, BigColor = Skin.Muted, NoteColor = Skin.Faint;
    public double Availability = -1;
    public readonly string[] StatValues = { "–", "–", "–", "–", "–", "–" };
    public readonly Color[] StatColors = { Skin.Text, Skin.Text, Skin.Text, Skin.Text, Skin.Text, Skin.Text };
    double contentTop, noteTop;
    readonly Face face;

    static string[] StatNames => L.En
        ? new[] { "Avg ping", "Min / Max", "Jitter", "Loss total", "Loss (60)", "Sent / lost" }
        : new[] { "Ø Ping", "Min / Max", "Jitter", "Verlust ges.", "Verlust (60)", "Gesendet / Verl." };

    public TargetCard(Target target, bool editable, bool game, string placeholder)
    {
        this.target = target; this.editable = editable; this.game = game;
        Margin = new Thickness(7);
        ClipToBounds = true;
        face = new Face(this);
        Children.Add(face);
        if (editable)
        {
            NameInput = Skin.TextInput(target.Name, game ? L.P("Name des Spiels", "Game name") : L.P("Name, z. B. Game-Server", "Name, e.g. game server"));
            NameInput.FontWeight = FontWeight.Bold;
            NameInput.FontSize = 14;
            NameInput.MaxLength = 30;
            Children.Add(NameInput);
        }
        AddrInput = Skin.TextInput(target.Address, placeholder, true);
        Children.Add(AddrInput);
        if (game)
        {
            ProcessSelect = Skin.Combo(double.NaN);
            ProcessSelect.PlaceholderText = L.P("Spiel auswählen …", "Select game …");
            Detect = new PillButton(L.P("Erkennen", "Detect"), target.Color, false, Glyph.Search) { Height = 34 };
            Children.Add(ProcessSelect);
            Children.Add(Detect);
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var c in Children) c.Measure(new Size(Math.Max(0, availableSize.Width - 32), 40));
        return new Size(double.IsInfinity(availableSize.Width) ? 300 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 470 : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        face.Arrange(new Rect(finalSize));
        double p = 16, w = finalSize.Width - 2 * p, y = 16;
        if (editable) NameInput.Arrange(new Rect(p, y, Math.Max(0, w - 112), 32));
        y += 42;
        if (game)
        {
            double dw = Detect.DesiredSize.Width;
            ProcessSelect.Arrange(new Rect(p, y, Math.Max(0, w - dw - 8), 34));
            Detect.Arrange(new Rect(p + w - dw, y, dw, 34));
            y += 42;
        }
        AddrInput.Arrange(new Rect(p, y, Math.Max(0, w), 34));
        y += 40;
        if (game) { noteTop = y; y += 20; }
        contentTop = y;
        return finalSize;
    }

    public new void InvalidateVisual() => face.InvalidateVisual();

    // Panel.Render ist versiegelt – gezeichnet wird über ein Hintergrund-Element unter den Eingabefeldern.
    sealed class Face : Control
    {
        readonly TargetCard card;
        public Face(TargetCard card) { this.card = card; }
        public override void Render(DrawingContext dc) => card.Paint(dc);
    }

    void Paint(DrawingContext dc)
    {
        Surface.Paint(dc, Bounds.Size, Skin.Surface, target.Color, 16);
        double W = Bounds.Width, H = Bounds.Height, p = 16, w = W - 2 * p;

        // Kopfzeile: Name + Status-Pille
        double pillW = Math.Min(104, Skin.Measure(StatusText, 11.5, FontWeight.Bold).Width + 30);
        if (!editable)
        {
            Skin.Dot(dc, target.Color, new Point(p + 5, 32), 5);
            Skin.TextIn(dc, target.Name, 17, Skin.Text, new Rect(p + 18, 16, w - pillW - 26, 32), FontWeight.Bold);
        }
        var pill = new Rect(W - p - pillW, 20, pillW, 24);
        Skin.Round(dc, Skin.A(StatusColor, 36), pill, 12);
        Skin.Dot(dc, StatusColor, new Point(pill.X + 13.5, pill.Center.Y), 3.5);
        Skin.TextIn(dc, StatusText, 11.5, StatusColor, new Rect(pill.X + 21, pill.Y, pill.Width - 25, pill.Height), FontWeight.Bold);

        if (game) Skin.Draw(dc, Note, 11.5, NoteColor, p, noteTop, FontWeight.Normal, w);

        // Großer Ping-Wert + Verfügbarkeitsring
        double y = contentTop + 2;
        double bigSize = game ? 34 : 40;
        var big = Skin.Measure(BigText, bigSize, FontWeight.Bold);
        Skin.Draw(dc, BigText, bigSize, BigColor, p - 1, y, FontWeight.Bold);
        if (!string.IsNullOrEmpty(BigUnit))
            Skin.Draw(dc, BigUnit, 14, Skin.Muted, p + big.Width + 6, y + big.Height - 24, FontWeight.Bold);
        Skin.Draw(dc, L.P("aktueller Ping", "current ping"), 11, Skin.Faint, p, y + big.Height + 1);

        double rs = game ? 54 : 62;
        var ring = new Rect(W - p - rs + 4, y + 4, rs - 8, rs - 8);
        double rad = ring.Width / 2;
        dc.DrawEllipse(null, Skin.Pen(Skin.Surface2, 6), ring.Center, rad, rad);
        if (Availability >= 0)
            Donut.DrawArc(dc, ring.Center, rad, -90, Math.Min(359.9, Math.Max(1, 360 * Availability / 100)), Skin.Pen(Skin.Availability(Availability), 6, true));
        string av = Availability < 0 ? "–" : Availability >= 99.995 ? "100" : Availability.ToString(Availability >= 99 ? "0.0" : "0");
        Skin.TextIn(dc, av, 13, Skin.Text, ring, FontWeight.Bold, TextAlignment.Center);
        Skin.Draw(dc, "% online", 10, Skin.Faint, ring.X - 10, ring.Bottom + 6, FontWeight.Normal, ring.Width + 20, TextAlignment.Center);

        // Bereiche von unten nach oben: Kennzahlen, Paketleiste, Sparkline (füllt den Rest)
        double statsH = 84, stripH = 34;
        double statsTop = H - p - statsH;
        double stripTop = statsTop - stripH - 4;
        double sparkTop = y + Math.Max(big.Height + 22, rs + 24);
        DrawSparkline(dc, new Rect(p, sparkTop, w, Math.Max(24, stripTop - sparkTop - 10)));
        DrawPacketStrip(dc, new Rect(p, stripTop, w, stripH));

        var names = StatNames;
        double cw = w / 3;
        for (int i = 0; i < 6; i++)
        {
            double cx = p + (i % 3) * cw, cy = statsTop + (i / 3) * 42;
            Skin.Draw(dc, names[i], 11, Skin.Faint, cx, cy, FontWeight.Normal, cw - 4);
            Skin.Draw(dc, StatValues[i], 14, StatColors[i], cx, cy + 16, FontWeight.Bold, cw - 4);
        }
    }

    void DrawSparkline(DrawingContext dc, Rect r)
    {
        Skin.Round(dc, Skin.A(Skin.Bg, 110), r, 10);
        var samples = target.Samples;
        if (!target.Active || samples.Count == 0)
        {
            string hint = target.Active ? L.P("Wartet auf Messwerte …", "Waiting for data …")
                : game ? L.P("Spiel wählen und Server erkennen lassen", "Select a game and detect its server")
                       : L.P("Adresse eintragen, um zu messen", "Enter an address to start measuring");
            Skin.TextIn(dc, hint, 11.5, Skin.Faint, r.Deflate(8), FontWeight.Normal, TextAlignment.Center);
            return;
        }
        const int n = 90;
        int start = Math.Max(0, samples.Count - n);
        long max = 10;
        for (int i = start; i < samples.Count; i++) if (samples[i].Ok) max = Math.Max(max, samples[i].Rtt);
        double ymax = max * 1.25;
        var inner = new Rect(r.X + 6, r.Y + 16, r.Width - 12, r.Height - 22);
        double step = inner.Width / (n - 1);
        var pts = new List<Point>();
        void Flush()
        {
            if (pts.Count >= 2)
            {
                var area = new StreamGeometry();
                using (var ctx = area.Open())
                {
                    ctx.BeginFigure(new Point(pts[0].X, inner.Bottom), true);
                    foreach (var pt in pts) ctx.LineTo(pt);
                    ctx.LineTo(new Point(pts[^1].X, inner.Bottom));
                    ctx.EndFigure(true);
                }
                dc.DrawGeometry(new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, inner.Top, RelativeUnit.Absolute), EndPoint = new RelativePoint(0, inner.Bottom, RelativeUnit.Absolute),
                    GradientStops = { new GradientStop(Skin.A(target.Color, 90), 0), new GradientStop(Skin.A(target.Color, 0), 1) }
                }, null, area);
                var line = new StreamGeometry();
                using (var ctx = line.Open())
                {
                    ctx.BeginFigure(pts[0], false);
                    for (int i = 1; i < pts.Count; i++) ctx.LineTo(pts[i]);
                    ctx.EndFigure(false);
                }
                dc.DrawGeometry(null, Skin.Pen(target.Color, 1.8, true), line);
            }
            else if (pts.Count == 1) Skin.Dot(dc, target.Color, pts[0], 2);
            pts.Clear();
        }
        int offset = n - (samples.Count - start);
        for (int i = start; i < samples.Count; i++)
        {
            double x = inner.X + (offset + i - start) * step;
            var s = samples[i];
            if (!s.Ok) { Flush(); Skin.Dot(dc, Skin.Bad, new Point(x, inner.Bottom), 2.5); continue; }
            pts.Add(new Point(x, inner.Bottom - s.Rtt / ymax * inner.Height));
        }
        Flush();
        Skin.Draw(dc, "max " + max + " ms", 10, Skin.Faint, r.X, r.Y + 3, FontWeight.Normal, r.Width - 8, TextAlignment.Right);
        Skin.Draw(dc, L.P("letzte " + n + " Messungen", "last " + n + " samples"), 10, Skin.Faint, r.X + 8, r.Y + 3);
    }

    void DrawPacketStrip(DrawingContext dc, Rect r)
    {
        var samples = target.Samples;
        const int n = 60;
        int start = Math.Max(0, samples.Count - n);
        int lost = 0;
        for (int i = start; i < samples.Count; i++) if (!samples[i].Ok) lost++;
        Skin.Draw(dc, L.P("Letzte 60 Pakete", "Last 60 packets"), 11, Skin.Faint, r.X, r.Y);
        string right = samples.Count == 0 ? "" : lost == 0 ? L.P("alle angekommen", "all received") : lost + L.P(" verloren", " lost");
        Skin.Draw(dc, right, 11, lost == 0 ? Skin.Good : Skin.Bad, r.X, r.Y, FontWeight.Bold, r.Width, TextAlignment.Right);
        double gap = 2, sw = (r.Width - gap * (n - 1)) / n, y = r.Y + 19, h = 12;
        int offset = n - (samples.Count - start);
        for (int i = 0; i < n; i++)
        {
            int si = start + i - offset;
            Color c = si < start || si >= samples.Count ? Skin.Surface2 : !samples[si].Ok ? Skin.Bad : Skin.A(Skin.Latency(samples[si].Rtt), 200);
            Skin.Round(dc, c, new Rect(r.X + i * (sw + gap), y, sw, h), Math.Min(sw / 2, 2));
        }
    }
}

// Logo, Titel und Statuszeile oben links.
class Brand : Control
{
    public bool Running;
    public TimeSpan Runtime;
    public int Incidents;
    public DateTime LastIncident;
    public ConnInfo Conn;

    public override void Render(DrawingContext dc)
    {
        double ls = 46, ly = (Bounds.Height - ls) / 2;
        Icons.Logo(dc, new Rect(0, ly, ls, ls));
        double tx = ls + 14;
        Skin.Draw(dc, "NetMonitor", 23, Skin.Text, tx, ly - 4, FontWeight.Bold);
        if (Conn != null)
        {
            // Verbindungsart: LAN grün, WLAN gelb (für Messungen weniger geeignet), sonst grau
            string ct = Conn.Short;
            Color cc = Conn.IsLan ? Skin.Good : Conn.Kind == "WLAN" ? Skin.Warn : Skin.Muted;
            Glyph g = Conn.IsLan ? Glyph.Lan : Conn.Kind == "WLAN" ? Glyph.Wifi : Glyph.Globe;
            double cx = tx + Skin.Measure("NetMonitor", 23, FontWeight.Bold).Width + 14;
            var cr = new Rect(cx, ly + 2, Skin.Measure(ct, 11.5, FontWeight.Bold).Width + 38, 22);
            Skin.Round(dc, Skin.A(cc, 36), cr, 11);
            Icons.Draw(dc, g, new Rect(cr.X + 8, cr.Y + 3, 16, 16), cc);
            Skin.TextIn(dc, ct, 11.5, cc, new Rect(cr.X + 28, cr.Y, cr.Width - 28, cr.Height), FontWeight.Bold);
        }
        double y = ly + 28;
        string st = Running ? L.P("Messung läuft", "Measuring") : L.P("Gestoppt", "Stopped");
        Color sc = Running ? Skin.Good : Skin.Muted;
        double pw = Skin.Measure(st, 11.5, FontWeight.Bold).Width + 30;
        var pill = new Rect(tx, y, pw, 20);
        Skin.Round(dc, Skin.A(sc, 40), pill, 10);
        Skin.Dot(dc, sc, new Point(pill.X + 12.5, pill.Center.Y), 3.5);
        Skin.TextIn(dc, st, 11.5, sc, new Rect(pill.X + 21, pill.Y, pw, pill.Height), FontWeight.Bold);
        double x = pill.Right + 12;
        string rt = L.P("Laufzeit ", "Runtime ") + Skin.Duration(Runtime) + "   ·   ";
        Skin.TextIn(dc, rt, 12, Skin.Muted, new Rect(x, y, 400, pill.Height));
        x += Skin.Measure(rt, 12).Width;
        string inc = Incidents == 0 ? L.P("keine Störungen", "no incidents")
            : Incidents + (Incidents == 1 ? L.P(" Störung", " incident") : L.P(" Störungen", " incidents")) +
              L.P("  (zuletzt ", "  (last ") + LastIncident.ToString("HH:mm:ss") + ")";
        Skin.TextIn(dc, inc, 12, Incidents == 0 ? Skin.Good : Skin.Bad, new Rect(x, y, Math.Max(0, Bounds.Width - x), pill.Height), FontWeight.Bold);
    }
}

class MainWindow : AppWindow
{
    readonly List<Target> targets = new();
    readonly DispatcherTimer timer = new();
    readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer refresh = new() { Interval = TimeSpan.FromMilliseconds(500) };
    readonly DispatcherTimer autosave = new() { Interval = TimeSpan.FromSeconds(30) };
    bool running, dirty, applyingGame, bandsDirty = true, closing;
    int session, liveIncidents, connTick;
    DateTime runStart, firstStart = DateTime.MinValue, lastLoss = DateTime.MinValue, lastIncident;
    TimeSpan accumulated = TimeSpan.Zero;
    SessionRecorder recorder;
    HistoryWindow history;
    SpeedtestWindow speedtest;
    int intervalIdx = 1, timeoutIdx = 1, windowIdx = 1;
    string gameNote = "";
    ConnInfo connection;

    Brand brand;
    ComboBox selInterval, selTimeout, selWindow;
    PillButton btnToggle;
    TimeChart chart;
    Surface chartSurface;

    // Ziel 3 = Router, Ziel 4 ist frei benennbar und optional, Ziel 5 wird per Spiel-Erkennung befüllt.
    const int RouterIndex = 2, CustomIndex = 3, GameIndex = 4;
    static readonly int[] Intervals = { 500, 1000, 2000, 5000 };
    static readonly int[] Timeouts = { 500, 1000, 2000, 3000 };

    public static string DefaultName(int i) => i switch
    {
        0 => L.P("Extern 1 · Cloudflare", "External 1 · Cloudflare"),
        1 => L.P("Extern 2 · Google", "External 2 · Google"),
        2 => "Router",
        3 => L.P("Eigenes Ziel", "Custom target"),
        _ => L.P("Spiel-Server", "Game server")
    };

    static bool IsDefaultName(int i, string name)
    {
        bool en = L.En;
        L.Set(false); string de = DefaultName(i);
        L.Set(true); string eng = DefaultName(i);
        L.Set(en);
        return name == de || name == eng || name == "FRITZ!Box";
    }

    public MainWindow()
    {
        Width = 1640; Height = 1000;
        MinWidth = 1180; MinHeight = 760;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (App.ScreenshotTo == null) WindowState = WindowState.Maximized;
        else { Width = 1600; Height = 1000; }

        var settings = Storage.LoadSettings();
        string router = NetTools.DefaultGateway() ?? "";
        targets.Add(new Target(DefaultName(0), "1.1.1.1", Skin.Targets[0]));
        targets.Add(new Target(DefaultName(1), "8.8.8.8", Skin.Targets[1]));
        targets.Add(new Target(DefaultName(2), router, Skin.Targets[2]));
        targets.Add(new Target(DefaultName(CustomIndex), "", Skin.Targets[3]));
        targets.Add(new Target(DefaultName(GameIndex), "", Skin.Targets[4]));
        LoadSettings(settings);

        BuildUi();

        timer.Tick += (_, _) => PingAll();
        clock.Tick += (_, _) => { UpdateBrand(); if (++connTick % 15 == 0) RefreshConnection(); };
        refresh.Tick += (_, _) =>
        {
            if (bandsDirty || SpeedState.Active) { bandsDirty = false; UpdateBands(); dirty = true; }
            if (running || dirty) { dirty = false; UpdateChartRange(); chart.InvalidateVisual(); }
        };
        SpeedState.Finished += OnSpeedtestFinished;
        autosave.Tick += (_, _) => SaveProgress();
        RefreshConnection();
        clock.Start();
        refresh.Start();
    }

    // Baut die komplette Oberfläche (neu) auf – auch beim Sprachwechsel; Messdaten bleiben erhalten.
    void BuildUi()
    {
        Title = L.P("NetMonitor – Ping & Paketverlust", "NetMonitor – Ping & packet loss");
        var root = new Grid { RowDefinitions = new RowDefinitions("72,470,*"), Margin = new Thickness(18, 14, 18, 18) };
        root.Children.Add(BuildHeader());

        var cards = new Avalonia.Controls.Primitives.UniformGrid { Columns = targets.Count, Rows = 1 };
        for (int i = 0; i < targets.Count; i++)
        {
            string placeholder = i == GameIndex ? L.P("wird automatisch erkannt", "detected automatically")
                : i == RouterIndex ? L.P("IP-Adresse des Routers", "Router IP address")
                : L.P("IP-Adresse oder Hostname", "IP address or host name");
            targets[i].Card = new TargetCard(targets[i], i >= CustomIndex, i == GameIndex, placeholder);
            UpdateCard(targets[i], targets[i].Samples.Count > 0 ? targets[i].Samples[^1] : null);
            cards.Children.Add(targets[i].Card);
        }
        var game = targets[GameIndex].Card;
        game.Note = gameNote;
        game.ProcessSelect.DropDownOpened += (_, _) => RefreshProcesses();
        game.Detect.Click += (_, _) => DetectGameServer();
        game.AddrInput.TextChanged += (_, _) => { if (!applyingGame) { gameNote = game.Note = ""; game.InvalidateVisual(); } };
        Grid.SetRow(cards, 1);
        root.Children.Add(cards);
        var chartBox = BuildChart();
        Grid.SetRow(chartBox, 2);
        root.Children.Add(chartBox);
        Content = root;
        UpdateButtons();
        dirty = true;
    }

    Control BuildHeader()
    {
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("480,*,Auto"), Margin = new Thickness(6, 0, 6, 6) };
        brand = new Brand { Incidents = liveIncidents, LastIncident = lastIncident, Conn = connection };
        bar.Children.Add(brand);

        var lang = new Segmented("DE", "EN");
        lang.SelectedIndex = L.En ? 1 : 0;
        // verzögert, weil der Umschalter beim Neuaufbau selbst ersetzt wird
        lang.Changed += (_, _) => { bool en = lang.SelectedIndex == 1; Dispatcher.UIThread.Post(() => SwitchLanguage(en)); };
        selInterval = MakeSelect(new[] { L.P("0,5 s", "0.5 s"), "1 s", "2 s", "5 s" }, intervalIdx);
        selTimeout = MakeSelect(new[] { "500 ms", "1 s", "2 s", "3 s" }, timeoutIdx);
        selInterval.SelectionChanged += (_, _) => intervalIdx = Math.Max(0, selInterval.SelectedIndex);
        selTimeout.SelectionChanged += (_, _) => timeoutIdx = Math.Max(0, selTimeout.SelectedIndex);
        var set = Row(6, lang, Caption(L.P("Intervall", "Interval")), selInterval, Caption("Timeout"), selTimeout);
        set.HorizontalAlignment = HorizontalAlignment.Right;
        set.VerticalAlignment = VerticalAlignment.Center;
        set.Margin = new Thickness(0, 0, 14, 0);
        Grid.SetColumn(set, 1);
        bar.Children.Add(set);

        btnToggle = new PillButton("Start", Skin.Good, false, Glyph.Play);
        var btnReset = new PillButton(L.P("Zurücksetzen", "Reset"), Skin.Muted, true, Glyph.Refresh);
        var btnHistory = new PillButton(L.P("Verlauf", "History"), Skin.Violet, true, Glyph.History);
        var btnSpeed = new PillButton("Speedtest", Skin.Cyan, true, Glyph.Bolt);
        var btnExport = new PillButton("Export", Skin.Accent, false, Glyph.Download);
        btnToggle.Click += (_, _) => { if (running) StopScan(); else StartScan(); };
        btnReset.Click += (_, _) => ResetAll();
        btnHistory.Click += (_, _) => ShowHistory();
        btnSpeed.Click += (_, _) => ShowSpeedtest();
        btnExport.Click += (_, _) => Export();
        var buttons = Row(8, btnToggle, btnReset, btnHistory, btnSpeed, btnExport);
        buttons.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(buttons, 2);
        bar.Children.Add(buttons);
        return bar;
    }

    Control BuildChart()
    {
        var inner = new Grid { RowDefinitions = new RowDefinitions("48,*"), ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        inner.Children.Add(Column(2,
            Skin.Label(L.P("Ping-Verlauf", "Ping history"), 17, Skin.Text, FontWeight.Bold),
            Skin.Label(L.P("Linien = Antwortzeit  ·  rote Markierungen = verlorene Pakete  ·  Legende anklicken zum Ein-/Ausblenden",
                            "Lines = response time  ·  red marks = lost packets  ·  click the legend to show/hide a line"), 11.5, Skin.Faint)));
        selWindow = MakeSelect(L.En ? new[] { "5 minutes", "15 minutes", "1 hour", "6 hours", "All" }
                                    : new[] { "5 Minuten", "15 Minuten", "1 Stunde", "6 Stunden", "Alles" }, windowIdx);
        selWindow.Width = 140;
        selWindow.VerticalAlignment = VerticalAlignment.Top;
        selWindow.SelectionChanged += (_, _) => { windowIdx = Math.Max(0, selWindow.SelectedIndex); dirty = true; };
        Grid.SetColumn(selWindow, 1);
        inner.Children.Add(selWindow);
        chart = new TimeChart { Margin = new Thickness(0, 6, 0, 0), IntervalTicks = IntervalMs * TimeSpan.TicksPerMillisecond };
        foreach (var t in targets) chart.Series.Add(t.Series);
        Grid.SetRow(chart, 1);
        Grid.SetColumnSpan(chart, 2);
        inner.Children.Add(chart);
        chartSurface = new Surface { Child = inner, Margin = new Thickness(7, 10, 7, 0), Padding = new Thickness(20, 14, 18, 12) };
        return chartSurface;
    }

    static TextBlock Caption(string text)
    {
        var l = Skin.Label(text, 12, Skin.Muted);
        l.Margin = new Thickness(8, 0, 0, 0);
        return l;
    }

    static ComboBox MakeSelect(string[] items, int selected)
    {
        var s = Skin.Combo(104);
        foreach (var i in items) s.Items.Add(i);
        s.SelectedIndex = Math.Clamp(selected, 0, items.Length - 1);
        return s;
    }

    static int Setting(Dictionary<string, string> d, string key, int fallback) =>
        d.TryGetValue(key, out var v) && int.TryParse(v, out int n) ? n : fallback;

    static int IndexOf(int[] values, int value, int fallback)
    {
        int i = Array.IndexOf(values, value);
        return i >= 0 ? i : fallback;
    }

    int IntervalMs => Intervals[intervalIdx];
    int TimeoutMs => Timeouts[timeoutIdx];

    // ---------- Sprache ----------

    void SwitchLanguage(bool en)
    {
        if (en == L.En) return;
        // Eingaben übernehmen, die noch nicht per Start bestätigt wurden
        var addrs = targets.Select(t => t.Card.AddrInput.Text ?? "").ToArray();
        var names = targets.Select(t => t.Card.NameInput?.Text).ToArray();
        var proc = targets[GameIndex].Card.ProcessSelect.SelectedItem;
        L.Set(en);
        for (int i = 0; i < targets.Count; i++)
            if (i < CustomIndex || IsDefaultName(i, targets[i].Name)) targets[i].Rename(DefaultName(i));
        bool historyOpen = history != null;
        history?.Close();
        bool speedOpen = speedtest != null && !SpeedState.Active;
        if (speedOpen) speedtest.Close();
        BuildUi();
        for (int i = 0; i < targets.Count; i++)
        {
            targets[i].Card.AddrInput.Text = addrs[i];
            if (names[i] != null) targets[i].Card.NameInput.Text = IsDefaultName(i, names[i]) ? DefaultName(i) : names[i];
        }
        if (proc != null)
        {
            var box = targets[GameIndex].Card.ProcessSelect;
            box.Items.Add(proc);
            box.SelectedIndex = 0;
        }
        SaveSettings();
        if (historyOpen) ShowHistory();
        if (speedOpen) ShowSpeedtest();
        bandsDirty = true;
    }

    // ---------- Einstellungen ----------

    void LoadSettings(Dictionary<string, string> d)
    {
        for (int i = 0; i < targets.Count; i++)
        {
            if (d.TryGetValue("t" + i + ".addr", out var v)) targets[i].Address = v.Trim();
            if (i >= CustomIndex && d.TryGetValue("t" + i + ".name", out v) && v.Trim().Length > 0)
                targets[i].Rename(IsDefaultName(i, v.Trim()) ? DefaultName(i) : v.Trim());
        }
        // Router-Adresse immer neu ermitteln, wenn keine gespeichert ist (z. B. nach einem Netzwechsel)
        if (string.IsNullOrEmpty(targets[RouterIndex].Address)) targets[RouterIndex].Address = NetTools.DefaultGateway() ?? "";
        if (d.TryGetValue("t" + GameIndex + ".note", out var note)) gameNote = note;
        intervalIdx = IndexOf(Intervals, Setting(d, "interval", 1000), 1);
        timeoutIdx = IndexOf(Timeouts, Setting(d, "timeout", 1000), 1);
        windowIdx = Math.Clamp(Setting(d, "window", 1), 0, 4);
    }

    void SaveSettings()
    {
        var d = Storage.LoadSettings(); // weitere Einstellungen (Tarif, Speedtest) erhalten
        d["lang"] = L.Code;
        for (int i = 0; i < targets.Count; i++)
        {
            d["t" + i + ".addr"] = (targets[i].Card.AddrInput.Text ?? "").Trim();
            if (i >= CustomIndex) d["t" + i + ".name"] = (targets[i].Card.NameInput.Text ?? "").Trim();
        }
        d["t" + GameIndex + ".note"] = gameNote;
        d["interval"] = IntervalMs.ToString();
        d["timeout"] = TimeoutMs.ToString();
        d["window"] = windowIdx.ToString();
        Storage.SaveSettings(d);
    }

    string[] Names() => targets.Select(t => t.Name).ToArray();
    string[] Addresses() => targets.Select(t => t.Address).ToArray();

    // ---------- Messung ----------

    // Screenshot-/Testmodus
    public void Start() => StartScan();
    public void Stop() => StopScan();

    async void StartScan()
    {
        if (running) return;
        if (!targets.Any(t => (t.Card.AddrInput.Text ?? "").Trim().Length > 0))
        {
            await MessageDialog.Show(this, L.P("Bitte mindestens eine Adresse eintragen.", "Please enter at least one address."), MsgKind.Warning);
            return;
        }
        for (int i = CustomIndex; i < targets.Count; i++)
        {
            var custom = targets[i];
            string customName = (custom.Card.NameInput.Text ?? "").Trim();
            if (customName.Length == 0) custom.Card.NameInput.Text = customName = DefaultName(i);
            custom.Rename(customName);
        }
        foreach (var t in targets)
        {
            string addr = (t.Card.AddrInput.Text ?? "").Trim();
            if (addr != t.Address) { t.Address = addr; t.Reset(); }
            UpdateCard(t, null);
        }
        SaveSettings();
        string recorderError = null;
        try
        {
            recorder = new SessionRecorder(Names(), Addresses(), IntervalMs, TimeoutMs);
            autosave.Start();
        }
        catch (Exception ex)
        {
            recorder = null;
            recorderError = ex.Message;
        }
        running = true;
        session++;
        runStart = DateTime.Now;
        if (firstStart == DateTime.MinValue) firstStart = runStart;
        timer.Interval = TimeSpan.FromMilliseconds(IntervalMs);
        chart.IntervalTicks = IntervalMs * TimeSpan.TicksPerMillisecond;
        timer.Start();
        PingAll();
        UpdateButtons();
        if (recorderError != null)
            await MessageDialog.Show(this, L.P("Verlauf kann nicht gespeichert werden:\n", "History cannot be saved:\n") + recorderError +
                L.P("\n\nDie Messung läuft trotzdem.", "\n\nMeasuring continues anyway."), MsgKind.Warning);
    }

    void StopScan()
    {
        if (!running) return;
        running = false;
        timer.Stop();
        autosave.Stop();
        accumulated += DateTime.Now - runStart;
        if (recorder != null)
        {
            try { recorder.Finish(); } catch { }
            recorder = null;
        }
        UpdateButtons();
        history?.Reload();
    }

    void SaveProgress()
    {
        try { recorder?.Flush(true); } catch { }
    }

    void ResetAll()
    {
        foreach (var t in targets) { t.Reset(); UpdateCard(t, null); }
        accumulated = TimeSpan.Zero;
        runStart = DateTime.Now;
        firstStart = running ? runStart : DateTime.MinValue;
        liveIncidents = 0;
        lastLoss = DateTime.MinValue;
        dirty = true;
        UpdateButtons();
    }

    void PingAll()
    {
        foreach (var t in targets)
            if (t.Active && !t.Busy) PingOne(t, session);
    }

    async void PingOne(Target t, int mySession)
    {
        t.Busy = true;
        var s = new Sample { Time = DateTime.Now, Rtt = -1 };
        try
        {
            using var p = new Ping();
            var r = await p.SendPingAsync(t.Address, TimeSpan.FromMilliseconds(TimeoutMs), null, new PingOptions(64, true));
            s.Ok = r.Status == IPStatus.Success;
            if (s.Ok) s.Rtt = r.RoundtripTime;
            s.Status = s.Ok ? "OK" : TranslateStatus(r.Status);
        }
        catch (Exception ex)
        {
            Exception inner = ex;
            while (inner.InnerException != null) inner = inner.InnerException;
            s.Status = (inner as SocketException)?.SocketErrorCode switch
            {
                SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain => L.P("Name nicht auflösbar", "Name not resolvable"),
                SocketError.NetworkUnreachable => L.P("Keine Route", "No route") + " (" + (t.Address.Contains(':') ? "IPv6" : "IPv4") + ")",
                SocketError.HostUnreachable => L.P("Host nicht erreichbar", "Host unreachable"),
                _ => L.P("Fehler: ", "Error: ") + inner.Message
            };
        }
        finally { t.Busy = false; }
        if (!running || mySession != session) return;
        t.Add(s);
        recorder?.Add(targets.IndexOf(t), s);
        if (!s.Ok && !t.Unreachable && !SpeedState.Disturbs(s.Time))
        {
            var gap = TimeSpan.FromMilliseconds(Math.Max(3 * IntervalMs, 5000));
            if (s.Time - lastLoss > gap) { liveIncidents++; lastIncident = s.Time; }
            lastLoss = s.Time;
        }
        UpdateCard(t, s);
        dirty = true;
    }

    static string TranslateStatus(IPStatus status) => status switch
    {
        IPStatus.TimedOut => "Timeout",
        IPStatus.DestinationHostUnreachable => L.P("Host nicht erreichbar", "Host unreachable"),
        IPStatus.DestinationNetworkUnreachable => L.P("Netz nicht erreichbar", "Network unreachable"),
        IPStatus.TtlExpired => L.P("TTL abgelaufen", "TTL expired"),
        _ => status.ToString()
    };

    // ---------- Anzeige ----------

    void UpdateCard(Target t, Sample s)
    {
        var c = t.Card;
        var st = t.Total;
        if (s == null)
        {
            c.BigText = "–"; c.BigUnit = "ms"; c.BigColor = Skin.Muted;
            c.StatusText = t.Active ? L.P("Bereit", "Ready") : L.P("Inaktiv", "Inactive"); c.StatusColor = Skin.Muted;
        }
        else if (s.Ok)
        {
            c.BigText = s.Rtt < 1 ? "<1" : s.Rtt.ToString(); c.BigUnit = "ms";
            c.BigColor = Skin.Latency(s.Rtt);
            c.StatusText = "Online"; c.StatusColor = Skin.Good;
        }
        else
        {
            c.BigText = L.P("Verlust", "Lost"); c.BigUnit = ""; c.BigColor = Skin.Bad;
            c.StatusText = s.Status.Length > 14 ? L.P("Fehler", "Error") : s.Status; c.StatusColor = Skin.Bad;
        }
        c.Availability = st.Sent > 0 ? 100 - st.LossPct : -1;
        c.StatValues[0] = st.HasPing ? st.Avg.ToString("0.0") + " ms" : "–";
        c.StatValues[1] = st.HasPing ? st.Min + " / " + st.Max : "–";
        c.StatValues[2] = st.JitterCount > 0 ? st.Jitter.ToString("0.0") + " ms" : "–";
        c.StatValues[3] = st.Sent > 0 ? st.LossPct.ToString("0.00") + " %" : "–";
        c.StatColors[3] = Skin.Loss(st.LossPct, st.Sent);
        c.StatValues[4] = st.Sent > 0 ? t.Loss60.ToString("0.0") + " %" : "–";
        c.StatColors[4] = Skin.Loss(t.Loss60, st.Sent);
        c.StatValues[5] = st.Sent + " / " + st.Lost;
        c.StatColors[5] = st.Lost > 0 ? Skin.Bad : Skin.Text;
        ToolTip.SetTip(c.AddrInput, s != null && !s.Ok ? s.Status : null);
        c.InvalidateVisual();
    }

    void UpdateChartRange()
    {
        long now = DateTime.Now.Ticks;
        chart.MaxT = running ? now : LastPointTime(now);
        chart.MinT = windowIdx switch
        {
            0 => chart.MaxT - TimeSpan.FromMinutes(5).Ticks,
            1 => chart.MaxT - TimeSpan.FromMinutes(15).Ticks,
            2 => chart.MaxT - TimeSpan.FromHours(1).Ticks,
            3 => chart.MaxT - TimeSpan.FromHours(6).Ticks,
            _ => Math.Min(targets.Where(t => t.Series.Points.Count > 0).Select(t => t.Series.Points[0].T).DefaultIfEmpty(chart.MaxT).Min(),
                          chart.MaxT - TimeSpan.FromMinutes(1).Ticks)
        };
    }

    long LastPointTime(long fallback)
    {
        long last = targets.Where(t => t.Series.Points.Count > 0).Select(t => t.Series.Points[^1].T).DefaultIfEmpty(0).Max();
        return last > 0 ? last + TimeSpan.TicksPerSecond : fallback;
    }

    void UpdateButtons()
    {
        btnToggle.Text = running ? "Stop" : "Start";
        btnToggle.Glyph = running ? Glyph.Stop : Glyph.Play;
        btnToggle.Color = running ? Skin.Bad : Skin.Good;
        btnToggle.InvalidateVisual();
        selInterval.IsEnabled = !running;
        selTimeout.IsEnabled = !running;
        foreach (var t in targets)
        {
            t.Card.AddrInput.IsReadOnly = running;
            if (t.Card.NameInput != null) t.Card.NameInput.IsReadOnly = running;
        }
        UpdateBrand();
    }

    TimeSpan TotalRuntime() => accumulated + (running ? DateTime.Now - runStart : TimeSpan.Zero);

    void UpdateBrand()
    {
        brand.Running = running;
        brand.Runtime = TotalRuntime();
        brand.Incidents = liveIncidents;
        brand.LastIncident = lastIncident;
        brand.InvalidateVisual();
    }

    // ---------- Spiel-Server-Erkennung ----------

    void RefreshProcesses()
    {
        var box = targets[GameIndex].Card.ProcessSelect;
        var prev = box.SelectedItem as ProcItem;
        var items = ProcItem.ListNetworked();
        box.Items.Clear();
        int sel = -1;
        for (int i = 0; i < items.Count; i++)
        {
            box.Items.Add(items[i]);
            if (prev != null && string.Equals(items[i].Name, prev.Name, StringComparison.OrdinalIgnoreCase)) sel = i;
        }
        box.SelectedIndex = sel;
    }

    async void DetectGameServer()
    {
        var card = targets[GameIndex].Card;
        if (card.ProcessSelect.SelectedItem is not ProcItem item)
        {
            await MessageDialog.Show(this, L.P("Bitte zuerst das Spiel im Dropdown auswählen.", "Please select the game from the drop-down first."));
            return;
        }
        var pids = item.CurrentPids();
        if (pids.Length == 0)
        {
            await MessageDialog.Show(this, item.Name + L.P(" läuft nicht mehr.", " is no longer running."), MsgKind.Warning);
            RefreshProcesses();
            return;
        }
        string oldNote = card.Note;
        card.Detect.IsEnabled = false;
        card.NoteColor = Skin.Warn;
        card.Note = L.P("Analysiere " + GameDetector.Seconds + " s – bitte im Match bleiben …", "Analysing for " + GameDetector.Seconds + " s – stay in the match …");
        card.InvalidateVisual();
        try
        {
            var candidates = await Task.Run(() => GameDetector.Detect(pids, CancellationToken.None));
            card.Note = oldNote;
            if (candidates.Count == 0)
            {
                await MessageDialog.Show(this, L.P("Bei " + item.Name + " wurde kein Spielserver-Verkehr gefunden.\n\n" +
                    "Die Erkennung funktioniert nur, während du in einem laufenden Match bist (nicht im Menü oder in der Lobby).",
                    "No game server traffic found for " + item.Name + ".\n\n" +
                    "Detection only works while you are in a running match (not in the menu or lobby)."));
                return;
            }
            var dlg = new ServerPickDialog(item.Name, candidates);
            if (!await dlg.ShowDialog<bool>(this)) return;
            ApplyGameTarget(item.Name, dlg.Address, dlg.NoteText);
        }
        catch (Exception ex)
        {
            card.Note = oldNote;
            await MessageDialog.Show(this, L.P("Erkennung fehlgeschlagen:\n", "Detection failed:\n") + ex.Message, MsgKind.Error);
        }
        finally
        {
            card.NoteColor = Skin.Faint;
            card.Detect.IsEnabled = true;
            card.InvalidateVisual();
        }
    }

    // Übernimmt den erkannten Server; eine laufende Messung wird als neue Sitzung fortgesetzt.
    void ApplyGameTarget(string name, string address, string note)
    {
        bool wasRunning = running;
        if (wasRunning) StopScan();
        var card = targets[GameIndex].Card;
        applyingGame = true;
        if (name.Length > 30) name = name.Substring(0, 30);
        card.NameInput.Text = name;
        card.AddrInput.Text = address;
        gameNote = card.Note = note;
        applyingGame = false;
        if (wasRunning) StartScan();
        else
        {
            var t = targets[GameIndex];
            t.Address = address; t.Rename(name);
            t.Reset();
            UpdateCard(t, null);
            SaveSettings();
            dirty = true;
        }
    }

    // ---------- Verlauf / Speedtest ----------

    // Speichert den aktuellen Stand und liefert die Id der laufenden Sitzung (oder null).
    string FlushCurrent()
    {
        SaveProgress();
        return recorder?.Id;
    }

    void ShowHistory()
    {
        if (history == null)
        {
            history = new HistoryWindow(FlushCurrent);
            history.Closed += (_, _) => history = null;
            history.Show(this);
        }
        else
        {
            history.Reload();
            if (history.WindowState == WindowState.Minimized) history.WindowState = WindowState.Normal;
            history.Activate();
        }
    }

    void ShowSpeedtest()
    {
        if (speedtest == null)
        {
            speedtest = new SpeedtestWindow();
            speedtest.Closed += (_, _) => speedtest = null;
            speedtest.Show(this);
        }
        else
        {
            if (speedtest.WindowState == WindowState.Minimized) speedtest.WindowState = WindowState.Normal;
            speedtest.Activate();
        }
    }

    public HistoryWindow OpenHistory() { ShowHistory(); return history; }
    public SpeedtestWindow OpenSpeedtest() { ShowSpeedtest(); return speedtest; }

    async void RefreshConnection()
    {
        ConnInfo c;
        try { c = await Task.Run(NetInfo.Current); } catch { return; }
        if (closing) return;
        connection = c;
        brand.Conn = c;
        brand.InvalidateVisual();
    }

    void OnSpeedtestFinished() => Dispatcher.UIThread.Post(() =>
    {
        bandsDirty = true;
        history?.Reload();
    });

    // Speedtest-Zeiträume im Ping-Diagramm schraffiert markieren.
    void UpdateBands() => chart.Bands = SpeedStore.Windows().Select(w => (w.Start.Ticks, w.End.Ticks)).ToList();

    // ---------- Export ----------

    async void Export()
    {
        if (!targets.Any(t => t.Total.Sent > 0))
        {
            await MessageDialog.Show(this, L.P("Noch keine Messdaten vorhanden.", "No measurement data yet."));
            return;
        }
        await Exporter.Run(this, "netmonitor_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".csv", chartSurface, WriteCsv);
    }

    void WriteCsv(string path)
    {
        var meta = new List<string>
        {
            L.P("Erster Start;", "First start;") + (firstStart == DateTime.MinValue ? "" : firstStart.ToString("yyyy-MM-dd HH:mm:ss")),
            L.P("Laufzeit;", "Runtime;") + Skin.Duration(TotalRuntime()),
            L.P("Intervall ms;", "Interval ms;") + IntervalMs + ";Timeout ms;" + TimeoutMs
        };
        CsvExport.Write(path, meta, Names(), Addresses(), targets.Select(t => t.Total).ToArray(), targets.Select(t => t.Samples).ToArray());
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        closing = true;
        StopScan();
        SaveSettings();
        SpeedState.Finished -= OnSpeedtestFinished;
        timer.Stop(); clock.Stop(); refresh.Stop();
        base.OnClosing(e);
    }
}
