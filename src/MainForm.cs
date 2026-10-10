// NetMonitor – Hauptfenster (Dashboard).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NetMonitor
{
    class Target
    {
        public string Name, Address;
        public Color Color;
        public TargetCard Card;
        public bool Busy;
        public Stats Total = new Stats();
        public readonly Queue<bool> Window = new Queue<bool>();
        public readonly List<Sample> Samples = new List<Sample>();
        public readonly ChartSeries Series = new ChartSeries();

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
            int lost = 0;
            foreach (bool ok in Window) if (!ok) lost++;
            s.Loss60 = 100.0 * lost / Window.Count;
            Samples.Add(s);
            Series.Points.Add(new ChartPoint { T = s.Time.Ticks, Y = s.Ok ? s.Rtt : float.NaN, Loss = s.Ok ? 0 : 1 });
        }

        public double Loss60 { get { return Samples.Count == 0 ? 0 : Samples[Samples.Count - 1].Loss60; } }
        public bool Active { get { return !string.IsNullOrEmpty(Address); } }
        // Ein Ziel, das praktisch nie antwortet, zählt nicht als Störung.
        public bool Unreachable { get { return Total.Sent >= 20 && Total.LossPct > 95; } }
    }

    // Karte eines Ziels: komplett selbst gezeichnet, nur die Eingabefelder sind echte Steuerelemente.
    class TargetCard : Surface
    {
        readonly Target target;
        readonly bool editable, game;
        public readonly InputBox NameInput, AddrInput;
        public readonly DarkSelect ProcessSelect;
        public readonly PillButton Detect;

        public string StatusText = "", BigText = "–", BigUnit = "ms", Note = "";
        public Color StatusColor = Theme.Muted, BigColor = Theme.Muted, NoteColor = Theme.Faint;
        public double Availability = -1;
        public readonly string[] StatValues = { "–", "–", "–", "–", "–", "–" };
        public readonly Color[] StatColors = { Theme.Text, Theme.Text, Theme.Text, Theme.Text, Theme.Text, Theme.Text };
        int contentTop, noteTop;

        static string[] StatNames
        {
            get
            {
                return L.En
                    ? new[] { "Avg ping", "Min / Max", "Jitter", "Loss total", "Loss (60)", "Sent / lost" }
                    : new[] { "Ø Ping", "Min / Max", "Jitter", "Verlust ges.", "Verlust (60)", "Gesendet / Verl." };
            }
        }

        public TargetCard(Target target, bool editable, bool game, string placeholder)
        {
            this.target = target; this.editable = editable; this.game = game;
            Accent = target.Color;
            Margin = new Padding(Theme.S(7));
            Dock = DockStyle.Fill;
            if (editable)
            {
                NameInput = new InputBox(target.Name, game ? L.P("Name des Spiels", "Game name") : L.P("Name, z. B. Game-Server", "Name, e.g. game server"),
                    Theme.F(10.5f, FontStyle.Bold));
                NameInput.Box.MaxLength = 30;
                Controls.Add(NameInput);
            }
            AddrInput = new InputBox(target.Address, placeholder, new Font("Consolas", 10f));
            Controls.Add(AddrInput);
            if (game)
            {
                ProcessSelect = new DarkSelect { Placeholder = L.P("Spiel auswählen …", "Select game …") };
                Detect = new PillButton(L.P("Erkennen", "Detect"), target.Color, false, Icons.Search) { BackColor = Theme.Surface };
                Detect.Height = Theme.S(34);
                Controls.Add(ProcessSelect);
                Controls.Add(Detect);
            }
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (AddrInput == null || (game && Detect == null)) return; // noch im Konstruktor
            int p = Theme.S(16), w = Width - 2 * p, y = Theme.S(16);
            if (editable) NameInput.SetBounds(p, y, w - Theme.S(112), Theme.S(32));
            y += Theme.S(42);
            if (game)
            {
                ProcessSelect.SetBounds(p, y, w - Detect.Width - Theme.S(8), Theme.S(34));
                Detect.Location = new Point(p + w - Detect.Width, y);
                y += Theme.S(42);
            }
            AddrInput.SetBounds(p, y, w, Theme.S(34));
            y += Theme.S(40);
            if (game)
            {
                noteTop = y;
                y += Theme.S(20);
            }
            contentTop = y;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int p = Theme.S(16), w = Width - 2 * p;

            // Kopfzeile: Name + Status-Pille
            var stFont = Theme.F(8.5f, FontStyle.Bold);
            int pillW = Math.Min(Theme.S(104), Theme.Measure(StatusText, stFont).Width + Theme.S(30));
            if (!editable)
            {
                using (var b = new SolidBrush(target.Color)) g.FillEllipse(b, p, Theme.S(27), Theme.S(10), Theme.S(10));
                Theme.DrawText(g, target.Name, Theme.D(12.5f), Theme.Text,
                    new Rectangle(p + Theme.S(18), Theme.S(16), w - pillW - Theme.S(26), Theme.S(32)), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
            var pill = new Rectangle(Width - p - pillW, Theme.S(20), pillW, Theme.S(24));
            Theme.FillRound(g, Theme.A(StatusColor, 36), pill, Theme.S(12));
            using (var b = new SolidBrush(StatusColor)) g.FillEllipse(b, pill.X + Theme.S(10), pill.Y + pill.Height / 2 - Theme.S(3), Theme.S(7), Theme.S(7));
            Theme.DrawText(g, StatusText, stFont, StatusColor, new Rectangle(pill.X + Theme.S(21), pill.Y, pill.Width - Theme.S(25), pill.Height),
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            if (game)
                Theme.DrawText(g, Note, Theme.F(8.5f), NoteColor, new Rectangle(p, noteTop, w, Theme.S(18)), TextFormatFlags.EndEllipsis);

            // Großer Ping-Wert + Verfügbarkeitsring
            int y = contentTop + Theme.S(2);
            var bigFont = Theme.D(game ? 26f : 30f);
            var bs = Theme.Measure(BigText, bigFont);
            Theme.DrawText(g, BigText, bigFont, BigColor, p - Theme.S(2), y);
            if (!string.IsNullOrEmpty(BigUnit))
            {
                float inkW = g.MeasureString(BigText, bigFont, PointF.Empty, StringFormat.GenericTypographic).Width;
                Theme.DrawText(g, BigUnit, Theme.F(11f, FontStyle.Bold), Theme.Muted, p + (int)inkW + Theme.S(6), y + bs.Height - Theme.S(27));
            }
            Theme.DrawText(g, L.P("aktueller Ping", "current ping"), Theme.F(8f), Theme.Faint, p, y + bs.Height + Theme.S(2));

            int rs = Theme.S(game ? 54 : 62);
            var ring = new RectangleF(Width - p - rs + Theme.S(4), y + Theme.S(4), rs - Theme.S(8), rs - Theme.S(8));
            using (var pen = new Pen(Theme.Surface2, Theme.S(6))) g.DrawEllipse(pen, ring);
            if (Availability >= 0)
            {
                var rc = Theme.Availability(Availability);
                using (var pen = new Pen(rc, Theme.S(6)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(pen, ring, -90, (float)Math.Max(1, 360 * Availability / 100));
            }
            string av = Availability < 0 ? "–" : Availability >= 99.995 ? "100" : Availability.ToString(Availability >= 99 ? "0.0" : "0");
            Theme.DrawText(g, av, Theme.F(9.5f, FontStyle.Bold), Theme.Text, Rectangle.Round(ring), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            Theme.DrawText(g, "% online", Theme.F(7.5f), Theme.Faint,
                new Rectangle((int)ring.X - Theme.S(10), (int)ring.Bottom + Theme.S(6), (int)ring.Width + Theme.S(20), Theme.S(14)), TextFormatFlags.HorizontalCenter);

            // Bereiche von unten nach oben: Kennzahlen, Paketleiste, Sparkline (füllt den Rest)
            int statsH = Theme.S(84), stripH = Theme.S(34);
            int statsTop = Height - p - statsH;
            int stripTop = statsTop - stripH - Theme.S(4);
            int sparkTop = y + Math.Max(bs.Height + Theme.S(22), rs + Theme.S(24));
            var spark = new Rectangle(p, sparkTop, w, Math.Max(Theme.S(24), stripTop - sparkTop - Theme.S(10)));
            DrawSparkline(g, spark);
            DrawPacketStrip(g, new Rectangle(p, stripTop, w, stripH));

            var names = StatNames;
            int cw = w / 3;
            for (int i = 0; i < 6; i++)
            {
                int cx = p + (i % 3) * cw, cy = statsTop + (i / 3) * Theme.S(42);
                Theme.DrawText(g, names[i], Theme.F(8f), Theme.Faint, new Rectangle(cx, cy, cw - Theme.S(4), Theme.S(16)), TextFormatFlags.EndEllipsis);
                Theme.DrawText(g, StatValues[i], Theme.F(10.5f, FontStyle.Bold), StatColors[i],
                    new Rectangle(cx, cy + Theme.S(16), cw - Theme.S(4), Theme.S(22)), TextFormatFlags.EndEllipsis);
            }
        }

        void DrawSparkline(Graphics g, Rectangle r)
        {
            Theme.FillRound(g, Theme.A(Theme.Bg, 110), r, Theme.S(10));
            var samples = target.Samples;
            if (!target.Active || samples.Count == 0)
            {
                string hint = target.Active ? L.P("Wartet auf Messwerte …", "Waiting for data …")
                    : game ? L.P("Spiel wählen und Server erkennen lassen", "Select a game and detect its server")
                           : L.P("Adresse eintragen, um zu messen", "Enter an address to start measuring");
                Theme.DrawText(g, hint, Theme.F(8.5f), Theme.Faint, r, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                return;
            }
            const int n = 90;
            int start = Math.Max(0, samples.Count - n);
            long max = 10;
            for (int i = start; i < samples.Count; i++) if (samples[i].Ok) max = Math.Max(max, samples[i].Rtt);
            float ymax = max * 1.25f;
            var inner = new RectangleF(r.X + Theme.S(6), r.Y + Theme.S(16), r.Width - Theme.S(12), r.Height - Theme.S(22));
            float step = inner.Width / (n - 1);
            var pts = new List<PointF>();
            Action flush = () =>
            {
                if (pts.Count >= 2)
                {
                    using (var path = new GraphicsPath())
                    {
                        path.AddLines(pts.ToArray());
                        path.AddLine(pts[pts.Count - 1].X, pts[pts.Count - 1].Y, pts[pts.Count - 1].X, inner.Bottom);
                        path.AddLine(pts[pts.Count - 1].X, inner.Bottom, pts[0].X, inner.Bottom);
                        path.CloseFigure();
                        using (var b = new LinearGradientBrush(new RectangleF(inner.X, inner.Y - 1, inner.Width, inner.Height + 2),
                            Theme.A(target.Color, 90), Theme.A(target.Color, 0), 90f)) g.FillPath(b, path);
                    }
                    using (var pen = new Pen(target.Color, Theme.S(1.8f)) { LineJoin = LineJoin.Round }) g.DrawLines(pen, pts.ToArray());
                }
                else if (pts.Count == 1)
                    using (var b = new SolidBrush(target.Color)) g.FillEllipse(b, pts[0].X - 2, pts[0].Y - 2, 4, 4);
                pts.Clear();
            };
            int offset = n - (samples.Count - start);
            for (int i = start; i < samples.Count; i++)
            {
                float x = inner.X + (offset + i - start) * step;
                var s = samples[i];
                if (!s.Ok)
                {
                    flush();
                    using (var b = new SolidBrush(Theme.Bad)) g.FillEllipse(b, x - Theme.S(2.5f), inner.Bottom - Theme.S(2.5f), Theme.S(5), Theme.S(5));
                    continue;
                }
                pts.Add(new PointF(x, inner.Bottom - s.Rtt / ymax * inner.Height));
            }
            flush();
            Theme.DrawText(g, "max " + max + " ms", Theme.F(7.5f), Theme.Faint, new Rectangle(r.X, r.Y + Theme.S(3), r.Width - Theme.S(8), Theme.S(14)), TextFormatFlags.Right);
            Theme.DrawText(g, L.P("letzte " + n + " Messungen", "last " + n + " samples"), Theme.F(7.5f), Theme.Faint, r.X + Theme.S(8), r.Y + Theme.S(3));
        }

        void DrawPacketStrip(Graphics g, Rectangle r)
        {
            var samples = target.Samples;
            const int n = 60;
            int start = Math.Max(0, samples.Count - n);
            int lost = 0;
            for (int i = start; i < samples.Count; i++) if (!samples[i].Ok) lost++;
            Theme.DrawText(g, L.P("Letzte 60 Pakete", "Last 60 packets"), Theme.F(8f), Theme.Faint, r.X, r.Y);
            string right = samples.Count == 0 ? "" : lost == 0 ? L.P("alle angekommen", "all received") : lost + L.P(" verloren", " lost");
            Theme.DrawText(g, right, Theme.F(8f, FontStyle.Bold), lost == 0 ? Theme.Good : Theme.Bad,
                new Rectangle(r.X, r.Y, r.Width, Theme.S(16)), TextFormatFlags.Right);
            float gap = Theme.S(2);
            float sw = (r.Width - gap * (n - 1)) / n;
            float y = r.Y + Theme.S(19), h = Theme.S(12);
            int offset = n - (samples.Count - start);
            for (int i = 0; i < n; i++)
            {
                int si = start + i - offset;
                Color c;
                if (si < start || si >= samples.Count) c = Theme.Surface2;
                else if (!samples[si].Ok) c = Theme.Bad;
                else c = Theme.A(Theme.Latency(samples[si].Rtt), 200);
                Theme.FillRound(g, c, new RectangleF(r.X + i * (sw + gap), y, sw, h), Math.Min(sw / 2, Theme.S(2)));
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

        public Brand()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int ls = Theme.S(46);
            int ly = (Height - ls) / 2;
            using (var logo = Theme.Logo(ls)) g.DrawImage(logo, 0, ly, ls, ls);

            int tx = ls + Theme.S(14);
            Theme.DrawText(g, "NetMonitor", Theme.D(17f), Theme.Text, tx, ly - Theme.S(3));
            if (Conn != null)
            {
                // Verbindungsart: LAN grün, WLAN gelb (für Messungen weniger geeignet), sonst grau
                string ct = Conn.Short;
                Color cc = Conn.IsLan ? Theme.Good : Conn.Kind == "WLAN" ? Theme.Warn : Theme.Muted;
                string glyph = Conn.IsLan ? "" : Conn.Kind == "WLAN" ? "" : Icons.Globe;
                var cf = Theme.F(8.5f, FontStyle.Bold);
                int cx = tx + Theme.Measure("NetMonitor", Theme.D(17f)).Width + Theme.S(14);
                var cr = new Rectangle(cx, ly + Theme.S(2), Theme.Measure(ct, cf).Width + Theme.S(36), Theme.S(22));
                Theme.FillRound(g, Theme.A(cc, 36), cr, Theme.S(11));
                Theme.DrawText(g, glyph, Theme.Icon(9f), cc, new Rectangle(cr.X + Theme.S(9), cr.Y, Theme.S(16), cr.Height), TextFormatFlags.VerticalCenter);
                Theme.DrawText(g, ct, cf, cc, new Rectangle(cr.X + Theme.S(27), cr.Y, cr.Width, cr.Height), TextFormatFlags.VerticalCenter);
            }
            int y = ly + Theme.S(28);
            var stFont = Theme.F(8.5f, FontStyle.Bold);
            string st = Running ? L.P("Messung läuft", "Measuring") : L.P("Gestoppt", "Stopped");
            Color sc = Running ? Theme.Good : Theme.Muted;
            int pw = Theme.Measure(st, stFont).Width + Theme.S(28);
            var pill = new Rectangle(tx, y, pw, Theme.S(20));
            Theme.FillRound(g, Theme.A(sc, 40), pill, Theme.S(10));
            using (var b = new SolidBrush(sc)) g.FillEllipse(b, pill.X + Theme.S(9), pill.Y + pill.Height / 2 - Theme.S(3), Theme.S(7), Theme.S(7));
            Theme.DrawText(g, st, stFont, sc, new Rectangle(pill.X + Theme.S(20), pill.Y, pw, pill.Height), TextFormatFlags.VerticalCenter);
            int x = pill.Right + Theme.S(12);
            string rt = L.P("Laufzeit ", "Runtime ") + Theme.Duration(Runtime) + "   ·   ";
            Theme.DrawText(g, rt, Theme.F(9f), Theme.Muted, new Rectangle(x, y, Width, pill.Height), TextFormatFlags.VerticalCenter);
            x += Theme.Measure(rt, Theme.F(9f)).Width;
            string inc = Incidents == 0 ? L.P("keine Störungen", "no incidents")
                : Incidents + (Incidents == 1 ? L.P(" Störung", " incident") : L.P(" Störungen", " incidents")) +
                  L.P("  (zuletzt ", "  (last ") + LastIncident.ToString("HH:mm:ss") + ")";
            Theme.DrawText(g, inc, Theme.F(9f, FontStyle.Bold), Incidents == 0 ? Theme.Good : Theme.Bad,
                new Rectangle(x, y, Width - x, pill.Height), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    public class MainForm : Form
    {
        readonly List<Target> targets = new List<Target>();
        readonly Timer timer = new Timer();
        readonly Timer clock = new Timer { Interval = 1000 };
        readonly Timer refresh = new Timer { Interval = 500 };
        readonly Timer autosave = new Timer { Interval = 30000 };
        bool running, dirty, applyingGame;
        int session, liveIncidents;
        DateTime runStart, firstStart = DateTime.MinValue, lastLoss = DateTime.MinValue, lastIncident;
        TimeSpan accumulated = TimeSpan.Zero;
        SessionRecorder recorder;
        HistoryForm history;
        int intervalIdx = 1, timeoutIdx = 1, windowIdx = 1;
        string gameNote = "";

        Brand brand;
        DarkSelect selInterval, selTimeout, selWindow;
        PillButton btnToggle, btnReset, btnHistory, btnSpeed, btnExport;
        SpeedtestForm speedtest;
        bool bandsDirty = true;
        TimeChart chart;
        Surface chartSurface;
        static readonly byte[] payload = new byte[32];

        // Ziel 3 = Router, Ziel 4 ist frei benennbar und optional, Ziel 5 wird per Spiel-Erkennung befüllt.
        const int RouterIndex = 2;
        const int CustomIndex = 3;
        const int GameIndex = 4;
        static readonly int[] Intervals = { 500, 1000, 2000, 5000 };
        static readonly int[] Timeouts = { 500, 1000, 2000, 3000 };
        const int CaptureSeconds = 12;

        public static string DefaultName(int i)
        {
            switch (i)
            {
                case 0: return L.P("Extern 1 · Cloudflare", "External 1 · Cloudflare");
                case 1: return L.P("Extern 2 · Google", "External 2 · Google");
                case 2: return "Router";
                case 3: return L.P("Eigenes Ziel", "Custom target");
                default: return L.P("Spiel-Server", "Game server");
            }
        }

        static bool IsDefaultName(int i, string name)
        {
            bool en = L.En;
            L.Set(false); string de = DefaultName(i);
            L.Set(true); string eng = DefaultName(i);
            L.Set(en);
            return name == de || name == eng || name == "FRITZ!Box";
        }

        public MainForm()
        {
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.F(9.5f);
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(Theme.S(1640), Theme.S(1000));
            MinimumSize = new Size(Theme.S(1300), Theme.S(820));
            WindowState = FormWindowState.Maximized;
            DoubleBuffered = true;
            Icon = Theme.AppIcon;
            Theme.DarkWindow(this);

            var settings = Storage.LoadSettings();
            string router = NetTools.DefaultGateway() ?? "";
            targets.Add(new Target(DefaultName(0), "1.1.1.1", Theme.Targets[0]));
            targets.Add(new Target(DefaultName(1), "8.8.8.8", Theme.Targets[1]));
            targets.Add(new Target(DefaultName(2), router, Theme.Targets[2]));
            targets.Add(new Target(DefaultName(CustomIndex), "", Theme.Targets[3]));
            targets.Add(new Target(DefaultName(GameIndex), "", Theme.Targets[4]));
            LoadSettings(settings);

            BuildUi();

            timer.Tick += delegate { PingAll(); };
            clock.Tick += delegate { UpdateBrand(); if (++connTick % 15 == 0) RefreshConnection(); };
            RefreshConnection();
            refresh.Tick += delegate
            {
                if (bandsDirty || SpeedState.Active) { bandsDirty = false; UpdateBands(); dirty = true; }
                if (running || dirty) { dirty = false; UpdateChartRange(); chart.Invalidate(); }
            };
            SpeedState.Finished += OnSpeedtestFinished;
            autosave.Tick += delegate { SaveProgress(); };
            clock.Start();
            refresh.Start();
        }

        // Baut die komplette Oberfläche (neu) auf – auch beim Sprachwechsel; Messdaten bleiben erhalten.
        void BuildUi()
        {
            SuspendLayout();
            var old = new List<Control>();
            foreach (Control c in Controls) old.Add(c);
            Controls.Clear();
            foreach (var c in old) c.Dispose();

            Text = L.P("NetMonitor – Ping & Paketverlust", "NetMonitor – Ping & packet loss");
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Theme.Bg,
                Padding = new Padding(Theme.S(18), Theme.S(14), Theme.S(18), Theme.S(18)) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(72)));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(470)));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.Controls.Add(BuildHeader(), 0, 0);

            var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = targets.Count, RowCount = 1, Margin = new Padding(0), BackColor = Theme.Bg };
            for (int i = 0; i < targets.Count; i++)
            {
                cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / targets.Count));
                string placeholder = i == GameIndex ? L.P("wird automatisch erkannt", "detected automatically")
                    : i == RouterIndex ? L.P("IP-Adresse des Routers", "Router IP address")
                    : L.P("IP-Adresse oder Hostname", "IP address or host name");
                targets[i].Card = new TargetCard(targets[i], i >= CustomIndex, i == GameIndex, placeholder);
                UpdateCard(targets[i], targets[i].Samples.Count > 0 ? targets[i].Samples[targets[i].Samples.Count - 1] : null);
                cards.Controls.Add(targets[i].Card, i, 0);
            }
            var game = targets[GameIndex].Card;
            game.Note = gameNote;
            game.ProcessSelect.DropDownOpening += delegate { RefreshProcesses(); };
            game.Detect.Click += delegate { DetectGameServer(); };
            game.AddrInput.Box.TextChanged += delegate { if (!applyingGame) { gameNote = game.Note = ""; game.Invalidate(); } };
            root.Controls.Add(cards, 0, 1);
            root.Controls.Add(BuildChart(), 0, 2);
            Controls.Add(root);
            ResumeLayout(true);
            UpdateButtons();
            dirty = true;
        }

        Control BuildHeader()
        {
            var bar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Theme.Bg, Margin = new Padding(Theme.S(6), 0, Theme.S(6), Theme.S(6)) };
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(480)));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            brand = new Brand { Dock = DockStyle.Fill, Margin = new Padding(0), Incidents = liveIncidents, LastIncident = lastIncident, Conn = connection };
            bar.Controls.Add(brand, 0, 0);

            var set = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, BackColor = Theme.Bg, Margin = new Padding(0, Theme.S(14), Theme.S(12), 0) };
            var lang = new Segmented("DE", "EN") { Margin = new Padding(0, Theme.S(-2), Theme.S(8), 0) };
            lang.SelectedIndex = L.En ? 1 : 0;
            // verzögert, weil der Umschalter beim Neuaufbau selbst ersetzt wird
            lang.Changed += delegate { bool en = lang.SelectedIndex == 1; BeginInvoke((Action)(() => SwitchLanguage(en))); };
            selInterval = MakeSelect(new[] { L.P("0,5 s", "0.5 s"), "1 s", "2 s", "5 s" }, intervalIdx);
            selTimeout = MakeSelect(new[] { "500 ms", "1 s", "2 s", "3 s" }, timeoutIdx);
            selInterval.SelectedIndexChanged += delegate { intervalIdx = selInterval.SelectedIndex; };
            selTimeout.SelectedIndexChanged += delegate { timeoutIdx = selTimeout.SelectedIndex; };
            set.Controls.Add(lang);
            set.Controls.Add(Caption(L.P("Intervall", "Interval"))); set.Controls.Add(selInterval);
            set.Controls.Add(Caption("Timeout")); set.Controls.Add(selTimeout);
            bar.Controls.Add(set, 1, 0);

            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, BackColor = Theme.Bg, Margin = new Padding(0, Theme.S(14), 0, 0) };
            btnToggle = new PillButton("Start", Theme.Good, false, Icons.Play);
            btnReset = new PillButton(L.P("Zurücksetzen", "Reset"), Theme.Muted, true, Icons.Refresh);
            btnHistory = new PillButton(L.P("Verlauf", "History"), Theme.Violet, true, Icons.History);
            btnSpeed = new PillButton("Speedtest", Theme.Cyan, true, Icons.Bolt);
            btnExport = new PillButton("Export", Theme.Accent, false, Icons.Download);
            btnToggle.Click += delegate { if (running) StopScan(); else StartScan(); };
            btnReset.Click += delegate { ResetAll(); };
            btnHistory.Click += delegate { ShowHistory(); };
            btnSpeed.Click += delegate { ShowSpeedtest(); };
            btnExport.Click += delegate { Export(); };
            buttons.Controls.AddRange(new Control[] { btnToggle, btnReset, btnHistory, btnSpeed, btnExport });
            bar.Controls.Add(buttons, 2, 0);
            return bar;
        }

        Control BuildChart()
        {
            chartSurface = new Surface { Dock = DockStyle.Fill, Margin = new Padding(Theme.S(7), Theme.S(10), Theme.S(7), 0),
                Padding = new Padding(Theme.S(20), Theme.S(14), Theme.S(18), Theme.S(12)) };
            var inner = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = Theme.Surface };
            inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            inner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            inner.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(48)));
            inner.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var titles = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, BackColor = Theme.Surface, Margin = new Padding(0) };
            titles.Controls.Add(Theme.MakeLabel(L.P("Ping-Verlauf", "Ping history"), Theme.D(13f), Theme.Text, Theme.Surface));
            titles.Controls.Add(Theme.MakeLabel(L.P(
                "Linien = Antwortzeit  ·  rote Markierungen = verlorene Pakete  ·  Legende anklicken zum Ein-/Ausblenden",
                "Lines = response time  ·  red marks = lost packets  ·  click the legend to show/hide a line"),
                Theme.F(8.5f), Theme.Faint, Theme.Surface));
            inner.Controls.Add(titles, 0, 0);
            selWindow = MakeSelect(L.En ? new[] { "5 minutes", "15 minutes", "1 hour", "6 hours", "All" }
                                        : new[] { "5 Minuten", "15 Minuten", "1 Stunde", "6 Stunden", "Alles" }, windowIdx);
            selWindow.BackColor = Theme.Surface;
            selWindow.Width = Theme.S(140);
            selWindow.Margin = new Padding(0, Theme.S(4), 0, 0);
            selWindow.SelectedIndexChanged += delegate { windowIdx = selWindow.SelectedIndex; dirty = true; };
            inner.Controls.Add(selWindow, 1, 0);
            chart = new TimeChart { Dock = DockStyle.Fill, Margin = new Padding(0, Theme.S(6), 0, 0) };
            chart.IntervalTicks = IntervalMs * TimeSpan.TicksPerMillisecond;
            foreach (var t in targets) chart.Series.Add(t.Series);
            inner.Controls.Add(chart, 0, 1);
            inner.SetColumnSpan(chart, 2);
            chartSurface.Controls.Add(inner);
            return chartSurface;
        }

        static Label Caption(string text)
        {
            var l = Theme.MakeLabel(text, Theme.F(9f), Theme.Muted, Theme.Bg);
            l.Margin = new Padding(Theme.S(10), Theme.S(9), Theme.S(2), 0);
            return l;
        }

        static DarkSelect MakeSelect(string[] items, int selected)
        {
            var s = new DarkSelect { Width = Theme.S(104), BackColor = Theme.Bg, Margin = new Padding(Theme.S(4), 0, Theme.S(4), 0) };
            foreach (var i in items) s.Items.Add(i);
            s.SelectedIndex = Math.Max(0, Math.Min(items.Length - 1, selected));
            return s;
        }

        static int Setting(Dictionary<string, string> d, string key, int fallback)
        {
            string v; int n;
            return d.TryGetValue(key, out v) && int.TryParse(v, out n) ? n : fallback;
        }

        static int IndexOf(int[] values, int value, int fallback)
        {
            int i = Array.IndexOf(values, value);
            return i >= 0 ? i : fallback;
        }

        int IntervalMs { get { return Intervals[intervalIdx]; } }
        int TimeoutMs { get { return Timeouts[timeoutIdx]; } }

        // ---------- Sprache ----------

        void SwitchLanguage(bool en)
        {
            if (en == L.En) return;
            // Eingaben übernehmen, die noch nicht per Start bestätigt wurden
            var addrs = new string[targets.Count];
            var names = new string[targets.Count];
            for (int i = 0; i < targets.Count; i++)
            {
                addrs[i] = targets[i].Card.AddrInput.Value;
                names[i] = targets[i].Card.NameInput != null ? targets[i].Card.NameInput.Value : null;
            }
            var proc = targets[GameIndex].Card.ProcessSelect.SelectedItem;
            L.Set(en);
            for (int i = 0; i < targets.Count; i++)
            {
                if (i < CustomIndex) targets[i].Rename(DefaultName(i));
                else if (IsDefaultName(i, targets[i].Name)) targets[i].Rename(DefaultName(i));
            }
            bool historyOpen = history != null && !history.IsDisposed;
            if (historyOpen) history.Close();
            bool speedOpen = speedtest != null && !speedtest.IsDisposed && !SpeedState.Active;
            if (speedOpen) speedtest.Close();
            BuildUi();
            for (int i = 0; i < targets.Count; i++)
            {
                targets[i].Card.AddrInput.Value = addrs[i];
                if (names[i] != null) targets[i].Card.NameInput.Value = IsDefaultName(i, names[i]) ? DefaultName(i) : names[i];
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
            string v;
            for (int i = 0; i < targets.Count; i++)
            {
                if (d.TryGetValue("t" + i + ".addr", out v)) targets[i].Address = v.Trim();
                if (i >= CustomIndex && d.TryGetValue("t" + i + ".name", out v) && v.Trim().Length > 0)
                    targets[i].Rename(IsDefaultName(i, v.Trim()) ? DefaultName(i) : v.Trim());
            }
            if (d.TryGetValue("t" + GameIndex + ".note", out v)) gameNote = v;
            intervalIdx = IndexOf(Intervals, Setting(d, "interval", 1000), 1);
            timeoutIdx = IndexOf(Timeouts, Setting(d, "timeout", 1000), 1);
            windowIdx = Math.Max(0, Math.Min(4, Setting(d, "window", 1)));
        }

        void SaveSettings()
        {
            var d = Storage.LoadSettings(); // weitere Einstellungen (Tarif, Speedtest) erhalten
            d["lang"] = L.Code;
            for (int i = 0; i < targets.Count; i++)
            {
                d["t" + i + ".addr"] = targets[i].Card.AddrInput.Value.Trim();
                if (i >= CustomIndex) d["t" + i + ".name"] = targets[i].Card.NameInput.Value.Trim();
            }
            d["t" + GameIndex + ".note"] = gameNote;
            d["interval"] = IntervalMs.ToString();
            d["timeout"] = TimeoutMs.ToString();
            d["window"] = windowIdx.ToString();
            Storage.SaveSettings(d);
        }

        string[] Names()
        {
            var n = new string[targets.Count];
            for (int i = 0; i < n.Length; i++) n[i] = targets[i].Name;
            return n;
        }

        string[] Addresses()
        {
            var a = new string[targets.Count];
            for (int i = 0; i < a.Length; i++) a[i] = targets[i].Address;
            return a;
        }

        // ---------- Messung ----------

        void StartScan()
        {
            if (running) return;
            bool anyActive = false;
            foreach (var t in targets) if (t.Card.AddrInput.Value.Trim().Length > 0) anyActive = true;
            if (!anyActive)
            {
                MessageBox.Show(this, L.P("Bitte mindestens eine Adresse eintragen.", "Please enter at least one address."), Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            for (int i = CustomIndex; i < targets.Count; i++)
            {
                var custom = targets[i];
                string customName = custom.Card.NameInput.Value.Trim();
                if (customName.Length == 0) custom.Card.NameInput.Value = customName = DefaultName(i);
                custom.Rename(customName);
            }
            foreach (var t in targets)
            {
                string addr = t.Card.AddrInput.Value.Trim();
                if (addr != t.Address)
                {
                    t.Address = addr;
                    t.Reset();
                }
                UpdateCard(t, null);
            }
            SaveSettings();
            try
            {
                recorder = new SessionRecorder(Names(), Addresses(), IntervalMs, TimeoutMs);
                autosave.Start();
            }
            catch (Exception ex)
            {
                recorder = null;
                MessageBox.Show(this, L.P("Verlauf kann nicht gespeichert werden:\n", "History cannot be saved:\n") + ex.Message +
                    L.P("\n\nDie Messung läuft trotzdem.", "\n\nMeasuring continues anyway."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            running = true;
            session++;
            runStart = DateTime.Now;
            if (firstStart == DateTime.MinValue) firstStart = runStart;
            timer.Interval = IntervalMs;
            chart.IntervalTicks = IntervalMs * TimeSpan.TicksPerMillisecond;
            timer.Start();
            PingAll();
            UpdateButtons();
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
            if (history != null && !history.IsDisposed) history.Reload();
        }

        void SaveProgress()
        {
            if (recorder == null) return;
            try { recorder.Flush(true); } catch { }
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
                using (var p = new Ping())
                {
                    var r = await p.SendPingAsync(t.Address, TimeoutMs, payload, new PingOptions(64, true));
                    s.Ok = r.Status == IPStatus.Success;
                    if (s.Ok) s.Rtt = r.RoundtripTime;
                    s.Status = s.Ok ? "OK" : TranslateStatus(r.Status);
                }
            }
            catch (Exception ex)
            {
                Exception inner = ex;
                while (inner.InnerException != null) inner = inner.InnerException;
                var we = inner as System.ComponentModel.Win32Exception;
                int code = we != null ? we.NativeErrorCode : 0;
                if (code == 1231 || code == 10051)
                    s.Status = L.P("Keine Route", "No route") + " (" + (t.Address.Contains(":") ? "IPv6" : "IPv4") + ")";
                else if (code == 1232 || code == 10065)
                    s.Status = L.P("Host nicht erreichbar", "Host unreachable");
                else if (code == 11001)
                    s.Status = L.P("Name nicht auflösbar", "Name not resolvable");
                else
                    s.Status = L.P("Fehler: ", "Error: ") + inner.Message;
            }
            finally
            {
                t.Busy = false;
            }
            if (!running || mySession != session) return;
            t.Add(s);
            if (recorder != null) recorder.Add(targets.IndexOf(t), s);
            if (!s.Ok && !t.Unreachable && !SpeedState.Disturbs(s.Time))
            {
                var gap = TimeSpan.FromMilliseconds(Math.Max(3 * IntervalMs, 5000));
                if (s.Time - lastLoss > gap) { liveIncidents++; lastIncident = s.Time; }
                lastLoss = s.Time;
                brand.Incidents = liveIncidents;
                brand.LastIncident = lastIncident;
            }
            UpdateCard(t, s);
            dirty = true;
        }

        static string TranslateStatus(IPStatus status)
        {
            switch (status)
            {
                case IPStatus.TimedOut: return "Timeout";
                case IPStatus.DestinationHostUnreachable: return L.P("Host nicht erreichbar", "Host unreachable");
                case IPStatus.DestinationNetworkUnreachable: return L.P("Netz nicht erreichbar", "Network unreachable");
                case IPStatus.TtlExpired: return L.P("TTL abgelaufen", "TTL expired");
                default: return status.ToString();
            }
        }

        // ---------- Anzeige ----------

        void UpdateCard(Target t, Sample s)
        {
            var c = t.Card;
            var st = t.Total;
            if (s == null)
            {
                c.BigText = "–"; c.BigUnit = "ms"; c.BigColor = Theme.Muted;
                c.StatusText = t.Active ? L.P("Bereit", "Ready") : L.P("Inaktiv", "Inactive"); c.StatusColor = Theme.Muted;
            }
            else if (s.Ok)
            {
                c.BigText = s.Rtt < 1 ? "<1" : s.Rtt.ToString(); c.BigUnit = "ms";
                c.BigColor = Theme.Latency(s.Rtt);
                c.StatusText = "Online"; c.StatusColor = Theme.Good;
            }
            else
            {
                c.BigText = L.P("Verlust", "Lost"); c.BigUnit = ""; c.BigColor = Theme.Bad;
                c.StatusText = s.Status.Length > 14 ? L.P("Fehler", "Error") : s.Status; c.StatusColor = Theme.Bad;
            }
            c.Availability = st.Sent > 0 ? 100 - st.LossPct : -1;
            c.StatValues[0] = st.HasPing ? st.Avg.ToString("0.0") + " ms" : "–";
            c.StatValues[1] = st.HasPing ? st.Min + " / " + st.Max : "–";
            c.StatValues[2] = st.JitterCount > 0 ? st.Jitter.ToString("0.0") + " ms" : "–";
            c.StatValues[3] = st.Sent > 0 ? st.LossPct.ToString("0.00") + " %" : "–";
            c.StatColors[3] = Theme.Loss(st.LossPct, st.Sent);
            c.StatValues[4] = st.Sent > 0 ? t.Loss60.ToString("0.0") + " %" : "–";
            c.StatColors[4] = Theme.Loss(t.Loss60, st.Sent);
            c.StatValues[5] = st.Sent + " / " + st.Lost;
            c.StatColors[5] = st.Lost > 0 ? Theme.Bad : Theme.Text;
            c.Invalidate();
        }

        void UpdateChartRange()
        {
            long now = DateTime.Now.Ticks;
            chart.MaxT = running ? now : LastPointTime(now);
            switch (windowIdx)
            {
                case 0: chart.MinT = chart.MaxT - TimeSpan.FromMinutes(5).Ticks; break;
                case 1: chart.MinT = chart.MaxT - TimeSpan.FromMinutes(15).Ticks; break;
                case 2: chart.MinT = chart.MaxT - TimeSpan.FromHours(1).Ticks; break;
                case 3: chart.MinT = chart.MaxT - TimeSpan.FromHours(6).Ticks; break;
                default:
                    long first = chart.MaxT;
                    foreach (var t in targets) if (t.Series.Points.Count > 0) first = Math.Min(first, t.Series.Points[0].T);
                    chart.MinT = Math.Min(first, chart.MaxT - TimeSpan.FromMinutes(1).Ticks);
                    break;
            }
        }

        long LastPointTime(long fallback)
        {
            long last = 0;
            foreach (var t in targets) if (t.Series.Points.Count > 0) last = Math.Max(last, t.Series.Points[t.Series.Points.Count - 1].T);
            return last > 0 ? last + TimeSpan.TicksPerSecond : fallback;
        }

        void UpdateButtons()
        {
            btnToggle.Text = running ? "Stop" : "Start";
            btnToggle.Glyph = running ? Icons.Stop : Icons.Play;
            btnToggle.Color = running ? Theme.Bad : Theme.Good;
            btnToggle.Invalidate();
            selInterval.Enabled = !running;
            selTimeout.Enabled = !running;
            foreach (var t in targets)
            {
                t.Card.AddrInput.ReadOnly = running;
                if (t.Card.NameInput != null) t.Card.NameInput.ReadOnly = running;
            }
            UpdateBrand();
        }

        TimeSpan TotalRuntime()
        {
            return accumulated + (running ? DateTime.Now - runStart : TimeSpan.Zero);
        }

        void UpdateBrand()
        {
            brand.Running = running;
            brand.Runtime = TotalRuntime();
            brand.Incidents = liveIncidents;
            brand.LastIncident = lastIncident;
            brand.Invalidate();
        }

        // ---------- Spiel-Server-Erkennung ----------

        void RefreshProcesses()
        {
            var box = targets[GameIndex].Card.ProcessSelect;
            var prev = box.SelectedItem as ProcItem;
            var items = ProcItem.ListWindowed();
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
            var item = card.ProcessSelect.SelectedItem as ProcItem;
            if (item == null)
            {
                MessageBox.Show(this, L.P("Bitte zuerst das Spiel im Dropdown auswählen.", "Please select the game from the drop-down first."),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var pids = item.CurrentPids();
            if (pids.Length == 0)
            {
                MessageBox.Show(this, item.Title + L.P(" läuft nicht mehr.", " is no longer running."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                RefreshProcesses();
                return;
            }
            if (string.IsNullOrEmpty(Program.ScriptPath))
            {
                MessageBox.Show(this, L.P("Die Erkennung funktioniert nur, wenn NetMonitor normal gestartet wurde.",
                    "Detection only works when NetMonitor was started normally."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string outFile = Path.Combine(Path.GetTempPath(), "netmonitor_capture_" + Guid.NewGuid().ToString("N") + ".txt");
            string oldNote = card.Note;
            card.Detect.Enabled = false;
            card.NoteColor = Theme.Warn;
            card.Note = L.P("Analysiere " + CaptureSeconds + " s – bitte im Match bleiben …", "Analysing for " + CaptureSeconds + " s – stay in the match …");
            card.Invalidate();
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe",
                    "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + Program.ScriptPath + "\"" +
                    " -CapturePids " + string.Join(",", Array.ConvertAll(pids, x => x.ToString())) + " -Seconds " + CaptureSeconds + " -Out \"" + outFile + "\"")
                {
                    Verb = "runas", UseShellExecute = true, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                };
                System.Diagnostics.Process helper;
                try { helper = System.Diagnostics.Process.Start(psi); }
                catch (System.ComponentModel.Win32Exception ex)
                {
                    if (ex.NativeErrorCode != 1223) throw;
                    card.Note = oldNote;
                    MessageBox.Show(this, L.P("Für die Erkennung werden einmalig Adminrechte benötigt (Windows-Netzwerkprotokoll).",
                        "Detection needs administrator rights once (Windows network tracing)."), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                bool exited = await Task.Run(() => helper.WaitForExit((CaptureSeconds + 60) * 1000));
                if (!exited) { try { helper.Kill(); } catch { } throw new TimeoutException(L.P("Die Analyse hat nicht geantwortet.", "The analysis did not respond.")); }

                string error;
                var candidates = Candidate.Load(outFile, out error);
                if (error != null) throw new Exception(error);
                card.Note = oldNote;
                if (candidates.Count == 0)
                {
                    MessageBox.Show(this, L.P("Bei " + item.Title + " wurde kein Spielserver-Verkehr gefunden.\n\n" +
                        "Die Erkennung funktioniert nur, während du in einem laufenden Match bist (nicht im Menü oder in der Lobby).",
                        "No game server traffic found for " + item.Title + ".\n\n" +
                        "Detection only works while you are in a running match (not in the menu or lobby)."),
                        Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                using (var dlg = new ServerPickDialog(item.Title, candidates))
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    ApplyGameTarget(item.Title, dlg.Address, dlg.NoteText);
                }
            }
            catch (Exception ex)
            {
                card.Note = oldNote;
                MessageBox.Show(this, L.P("Erkennung fehlgeschlagen:\n", "Detection failed:\n") + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                card.NoteColor = Theme.Faint;
                card.Detect.Enabled = true;
                card.Invalidate();
                try { if (File.Exists(outFile)) File.Delete(outFile); } catch { }
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
            card.NameInput.Value = name;
            card.AddrInput.Value = address;
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

        // ---------- Verlauf ----------

        // Speichert den aktuellen Stand und liefert die Id der laufenden Sitzung (oder null).
        string FlushCurrent()
        {
            SaveProgress();
            return recorder == null ? null : recorder.Id;
        }

        void ShowHistory()
        {
            if (history == null || history.IsDisposed)
            {
                history = new HistoryForm(FlushCurrent);
                history.Show(this);
            }
            else
            {
                history.Reload();
                if (history.WindowState == FormWindowState.Minimized) history.WindowState = FormWindowState.Normal;
                history.Activate();
            }
        }

        // ---------- Verbindungsart ----------

        ConnInfo connection;
        int connTick;

        async void RefreshConnection()
        {
            ConnInfo c;
            try { c = await Task.Run(() => NetInfo.Current()); } catch { return; }
            if (IsDisposed) return;
            connection = c;
            brand.Conn = c;
            brand.Invalidate();
        }

        // ---------- Speedtest ----------

        void ShowSpeedtest()
        {
            if (speedtest == null || speedtest.IsDisposed)
            {
                speedtest = new SpeedtestForm();
                speedtest.Show(this);
            }
            else
            {
                if (speedtest.WindowState == FormWindowState.Minimized) speedtest.WindowState = FormWindowState.Normal;
                speedtest.Activate();
            }
        }

        void OnSpeedtestFinished()
        {
            bandsDirty = true;
            if (history != null && !history.IsDisposed) history.Reload();
        }

        // Speedtest-Zeiträume im Ping-Diagramm schraffiert markieren.
        void UpdateBands()
        {
            var bands = new List<KeyValuePair<long, long>>();
            foreach (var w in SpeedStore.Windows()) bands.Add(new KeyValuePair<long, long>(w.Key.Ticks, w.Value.Ticks));
            chart.Bands = bands;
        }

        // ---------- Export ----------

        void Export()
        {
            bool hasData = false;
            foreach (var t in targets) if (t.Total.Sent > 0) hasData = true;
            if (!hasData)
            {
                MessageBox.Show(this, L.P("Noch keine Messdaten vorhanden.", "No measurement data yet."), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            CsvExport.ExportWithDialog(this, "netmonitor_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".csv",
                chartSurface, WriteCsv);
        }

        void WriteCsv(string path)
        {
            var meta = new List<string>
            {
                L.P("Erster Start;", "First start;") + (firstStart == DateTime.MinValue ? "" : firstStart.ToString("yyyy-MM-dd HH:mm:ss")),
                L.P("Laufzeit;", "Runtime;") + Theme.Duration(TotalRuntime()),
                L.P("Intervall ms;", "Interval ms;") + IntervalMs + ";Timeout ms;" + TimeoutMs
            };
            var stats = new Stats[targets.Count];
            var samples = new List<Sample>[targets.Count];
            for (int i = 0; i < targets.Count; i++) { stats[i] = targets[i].Total; samples[i] = targets[i].Samples; }
            CsvExport.Write(path, meta, Names(), Addresses(), stats, samples);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            StopScan();
            SaveSettings();
            SpeedState.Finished -= OnSpeedtestFinished;
            timer.Stop();
            clock.Stop();
            refresh.Stop();
            base.OnFormClosing(e);
        }
    }
}
