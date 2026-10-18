// NetMonitor (macOS/Linux) – Diagramme: Ping-Verlauf, Tagesübersicht, Tachometer, Speedtest-Verlauf.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace NetMonitor;

// Zeitdiagramm: Ping-Linien mit Farbverlauf, Verlust-Spuren je Ziel, klickbare Legende und Tooltip.
class TimeChart : Control
{
    public readonly List<ChartSeries> Series = new();
    public long MinT, MaxT;
    public long IntervalTicks = TimeSpan.TicksPerSecond;
    public string EmptyText = L.P("Noch keine Messdaten – oben auf „Start“ klicken", "No data yet – click “Start” above");
    public string LossTitle = L.P("Paketverlust je Ziel", "Packet loss per target");
    public List<(long Start, long End)> Bands = new();
    Point mouse;
    bool hover;
    readonly List<(Rect Rect, ChartSeries Series)> legendHits = new();

    class Agg { public double[] Sum; public int[] Cnt, Pts; public float[] Loss; }

    public TimeChart() { ClipToBounds = true; }

    static int LowerBound(List<ChartPoint> pts, long t)
    {
        int lo = 0, hi = pts.Count;
        while (lo < hi) { int mid = (lo + hi) / 2; if (pts[mid].T < t) lo = mid + 1; else hi = mid; }
        return lo;
    }

    Agg Aggregate(ChartSeries s, int cols)
    {
        var a = new Agg { Sum = new double[cols], Cnt = new int[cols], Pts = new int[cols], Loss = new float[cols] };
        var pts = s.Points;
        double k = (cols - 1) / (double)(MaxT - MinT);
        for (int i = LowerBound(pts, MinT); i < pts.Count; i++)
        {
            var p = pts[i];
            if (p.T > MaxT) break;
            int c = (int)((p.T - MinT) * k);
            if (c < 0 || c >= cols) continue;
            a.Pts[c]++;
            if (!float.IsNaN(p.Y)) { a.Sum[c] += p.Y; a.Cnt[c]++; }
            if (p.Loss > a.Loss[c]) a.Loss[c] = p.Loss;
        }
        return a;
    }

    static double NiceCeil(double v)
    {
        double[] steps = { 10, 20, 25, 40, 50, 60, 80, 100, 120, 150, 200, 250, 300, 400, 500, 750, 1000, 1500, 2000, 3000, 5000 };
        foreach (var s in steps) if (v <= s) return s;
        return Math.Ceiling(v / 1000) * 1000;
    }

    static long NiceStep(long range, int count)
    {
        long[] secs = { 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600, 7200, 10800, 21600, 43200, 86400, 172800, 604800 };
        count = Math.Max(2, count);
        foreach (var s in secs) if (range / (s * TimeSpan.TicksPerSecond) <= count) return s * TimeSpan.TicksPerSecond;
        return 604800 * TimeSpan.TicksPerSecond;
    }

    protected override void OnPointerMoved(PointerEventArgs e) { mouse = e.GetPosition(this); hover = true; InvalidateVisual(); base.OnPointerMoved(e); }
    protected override void OnPointerExited(PointerEventArgs e) { hover = false; InvalidateVisual(); base.OnPointerExited(e); }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var p = e.GetPosition(this);
        foreach (var (rect, s) in legendHits)
            if (rect.Contains(p)) { s.Visible = !s.Visible; InvalidateVisual(); }
    }

    public override void Render(DrawingContext dc)
    {
        double W = Bounds.Width, H = Bounds.Height;
        var active = Series.Where(s => s.Points.Count > 0).ToList();

        // Legende
        legendHits.Clear();
        double lx = 2;
        foreach (var s in active)
        {
            double tw = Skin.Measure(s.Name, 12).Width;
            var chip = new Rect(lx, 0, tw + 32, 26);
            dc.DrawRectangle(Skin.B(s.Visible ? Skin.A(s.Color, 30) : Skin.Surface2), Skin.Pen(Skin.A(s.Color, s.Visible ? 110 : 35)), new RoundedRect(chip.Deflate(0.5), 13));
            Skin.Dot(dc, s.Visible ? s.Color : Skin.Faint, new Point(chip.X + 15, chip.Y + 13), 4);
            Skin.TextIn(dc, s.Name, 12, s.Visible ? Skin.Text : Skin.Faint, new Rect(chip.X + 24, 0, tw + 6, 26));
            legendHits.Add((chip, s));
            lx += chip.Width + 8;
        }

        double laneH = 9, laneGap = 6;
        double lanesH = active.Count > 0 ? 26 + active.Count * (laneH + laneGap) : 0;
        double axisH = 22, left = 48;
        var plot = new Rect(left, 42, Math.Max(0, W - left - 14), Math.Max(0, H - 42 - lanesH - axisH - 4));
        if (plot.Width < 40 || plot.Height < 30) return;
        var gridPen = Skin.Pen(Skin.Grid, 1, false, Skin.Dotted);

        if (active.Count == 0 || MaxT <= MinT)
        {
            for (int k = 0; k <= 4; k++) { double y = plot.Bottom - k * plot.Height / 4; dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y)); }
            Skin.TextIn(dc, EmptyText, 14, Skin.Faint, plot, FontWeight.Normal, TextAlignment.Center);
            return;
        }

        int cols = (int)plot.Width;
        long range = MaxT - MinT;
        var aggs = active.Select(s => Aggregate(s, cols)).ToArray();
        double max = 0;
        for (int i = 0; i < active.Count; i++)
        {
            if (!active[i].Visible) continue;
            for (int c = 0; c < cols; c++) if (aggs[i].Cnt[c] > 0) max = Math.Max(max, aggs[i].Sum[c] / aggs[i].Cnt[c]);
        }
        double ymax = NiceCeil(Math.Max(10, max * 1.15));

        for (int k = 0; k <= 4; k++)
        {
            double y = plot.Bottom - k * plot.Height / 4;
            dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            Skin.Draw(dc, (ymax * k / 4).ToString("0") + (k == 4 ? " ms" : ""), 11, Skin.Faint, 0, y - 8, FontWeight.Normal, plot.Left - 8, TextAlignment.Right);
        }

        long step = NiceStep(range, Math.Max(2, (int)(plot.Width / 110)));
        string fmt = step < TimeSpan.TicksPerMinute ? "HH:mm:ss" : range > TimeSpan.TicksPerDay ? L.DateTimeShort : "HH:mm";
        double axisY = H - axisH + 4;
        var vgrid = Skin.Pen(Skin.A(Skin.Grid, 140), 1, false, Skin.Dotted);
        for (long t = (MinT + step - 1) / step * step; t <= MaxT; t += step)
        {
            double x = plot.Left + (t - MinT) * (double)(cols - 1) / range;
            dc.DrawLine(vgrid, new Point(x, plot.Top), new Point(x, plot.Bottom));
            Skin.Draw(dc, new DateTime(t).ToString(fmt), 11, Skin.Faint, x - 50, axisY, FontWeight.Normal, 100, TextAlignment.Center);
        }

        // Speedtest-Zeiträume
        foreach (var (bs, be) in Bands)
        {
            if (be < MinT || bs > MaxT) continue;
            double x0 = plot.Left + (Math.Max(bs, MinT) - MinT) * (double)(cols - 1) / range;
            double x1 = plot.Left + (Math.Min(be, MaxT) - MinT) * (double)(cols - 1) / range;
            var r = new Rect(x0, plot.Top, Math.Max(3, x1 - x0), plot.Height);
            dc.DrawRectangle(Skin.B(Skin.A(Skin.Accent, 30)), null, r);
            using (dc.PushClip(r))
                for (double hx = r.X - r.Height; hx < r.Right; hx += 8)
                    dc.DrawLine(Skin.Pen(Skin.A(Skin.Accent, 60)), new Point(hx, r.Bottom), new Point(hx + r.Height, r.Top));
            if (r.Width > 60) Skin.Draw(dc, "Speedtest", 10, Skin.Accent, r.X + 4, plot.Top + 2, FontWeight.Bold);
        }

        // Linien mit Farbverlauf
        int gapCols = Math.Max(3, (int)(IntervalTicks * 3.0 * cols / range));
        using (dc.PushClip(new Rect(plot.Left, plot.Top - 4, plot.Width + 1, plot.Height + 5)))
        {
            for (int i = 0; i < active.Count; i++)
            {
                if (!active[i].Visible) continue;
                var a = aggs[i];
                var seg = new List<Point>();
                int lastCol = -1000;
                for (int c = 0; c <= cols; c++)
                {
                    bool end = c == cols;
                    if (!end && a.Pts[c] > 0 && a.Cnt[c] == 0) { DrawSegment(dc, seg, plot, active[i].Color); seg.Clear(); lastCol = -1000; continue; }
                    if (end || a.Cnt[c] == 0) { if (end) DrawSegment(dc, seg, plot, active[i].Color); continue; }
                    if (c - lastCol > gapCols && seg.Count > 0) { DrawSegment(dc, seg, plot, active[i].Color); seg.Clear(); }
                    seg.Add(new Point(plot.Left + c, plot.Bottom - (a.Sum[c] / a.Cnt[c]) / ymax * plot.Height));
                    lastCol = c;
                }
            }
        }

        // Verlust-Spuren
        double laneTop = plot.Bottom + 12;
        Skin.Draw(dc, LossTitle, 11, Skin.Muted, plot.Left, laneTop, FontWeight.Bold);
        laneTop += 20;
        for (int i = 0; i < active.Count; i++)
        {
            double y = laneTop + i * (laneH + laneGap);
            Skin.Round(dc, Skin.Surface2, new Rect(plot.Left, y, plot.Width, laneH), laneH / 2);
            Skin.Dot(dc, active[i].Visible ? active[i].Color : Skin.Faint, new Point(plot.Left - 12, y + laneH / 2), 4);
            var a = aggs[i];
            int c0 = -1; float worst = 0;
            for (int c = 0; c <= cols; c++)
            {
                bool lossHere = c < cols && a.Loss[c] > 0;
                if (lossHere) { if (c0 < 0) { c0 = c; worst = 0; } worst = Math.Max(worst, a.Loss[c]); continue; }
                if (c0 >= 0)
                {
                    Skin.Round(dc, Skin.A(Skin.Bad, (int)(120 + 135 * worst)), new Rect(plot.Left + c0, y, Math.Max(4, c - c0), laneH), laneH / 2);
                    c0 = -1;
                }
            }
        }
        double lanesBottom = laneTop + active.Count * (laneH + laneGap);

        // Fadenkreuz + Tooltip
        if (hover && mouse.X >= plot.Left && mouse.X < plot.Right && mouse.Y >= plot.Top && mouse.Y <= lanesBottom)
        {
            int c = (int)(mouse.X - plot.Left);
            dc.DrawLine(Skin.Pen(Skin.A(Skin.Text, 60)), new Point(mouse.X, plot.Top), new Point(mouse.X, lanesBottom));
            long t = MinT + (long)(c * (double)range / (cols - 1));
            var lines = new List<(Color, string)>();
            for (int i = 0; i < active.Count; i++)
            {
                if (!active[i].Visible) continue;
                var a = aggs[i];
                int best = -1;
                for (int d = 0; d <= 4 && best < 0; d++)
                {
                    if (c - d >= 0 && a.Pts[c - d] > 0) best = c - d;
                    else if (c + d < cols && a.Pts[c + d] > 0) best = c + d;
                }
                string v = "–";
                if (best >= 0)
                {
                    if (a.Cnt[best] > 0)
                    {
                        double avg = a.Sum[best] / a.Cnt[best];
                        v = avg.ToString(avg < 10 ? "0.0" : "0") + " ms" + (a.Loss[best] > 0 ? L.P("  ·  Verlust", "  ·  loss") : "");
                        var pt = new Point(plot.Left + best, plot.Bottom - avg / ymax * plot.Height);
                        dc.DrawEllipse(Skin.B(active[i].Color), Skin.Pen(Skin.Surface, 2), pt, 4, 4);
                    }
                    else if (a.Loss[best] > 0) v = L.P("Paket verloren", "packet lost");
                }
                lines.Add((active[i].Color, active[i].Name + ":  " + v));
            }
            string head = new DateTime(t).ToString(range > TimeSpan.TicksPerDay ? L.DateTimeShort + ":ss" : "HH:mm:ss");
            double tw = Skin.Measure(head, 12, FontWeight.Bold).Width;
            foreach (var (_, l) in lines) tw = Math.Max(tw, Skin.Measure(l, 12).Width + 18);
            double bw = tw + 24, bh = 34 + lines.Count * 20;
            double bx = mouse.X + 16;
            if (bx + bw > W - 4) bx = mouse.X - bw - 16;
            var box = new Rect(bx, plot.Top + 6, bw, bh);
            dc.DrawRectangle(Skin.B(Skin.A(Skin.Bg, 235)), Skin.Pen(Skin.Border), new RoundedRect(box, 10));
            Skin.Draw(dc, head, 12, Skin.Text, box.X + 12, box.Y + 9, FontWeight.Bold);
            double ty = box.Y + 31;
            foreach (var (color, l) in lines)
            {
                Skin.Dot(dc, color, new Point(box.X + 16, ty + 8), 4);
                Skin.Draw(dc, l, 12, Skin.Text, box.X + 28, ty);
                ty += 20;
            }
        }
    }

    static void DrawSegment(DrawingContext dc, List<Point> seg, Rect plot, Color color)
    {
        if (seg.Count == 0) return;
        if (seg.Count == 1) { Skin.Dot(dc, color, seg[0], 2.5); return; }
        var area = new StreamGeometry();
        using (var ctx = area.Open())
        {
            ctx.BeginFigure(new Point(seg[0].X, plot.Bottom), true);
            foreach (var p in seg) ctx.LineTo(p);
            ctx.LineTo(new Point(seg[^1].X, plot.Bottom));
            ctx.EndFigure(true);
        }
        dc.DrawGeometry(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, plot.Top, RelativeUnit.Absolute), EndPoint = new RelativePoint(0, plot.Bottom, RelativeUnit.Absolute),
            GradientStops = { new GradientStop(Skin.A(color, 70), 0), new GradientStop(Skin.A(color, 0), 1) }
        }, null, area);
        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            ctx.BeginFigure(seg[0], false);
            for (int i = 1; i < seg.Count; i++) ctx.LineTo(seg[i]);
            ctx.EndFigure(false);
        }
        dc.DrawGeometry(null, Skin.Pen(color, 2, true), line);
    }
}

// ---------- Tagesübersicht der Paketverluste ----------

class DayRow
{
    public DateTime Day;
    public readonly List<(DateTime Start, DateTime End)> Coverage = new();
    public readonly List<LossIncident> Incidents = new();
}

class DayTimeline : Control
{
    public List<DayRow> Rows = new();
    public DateTime SelStart = DateTime.MinValue, SelEnd = DateTime.MinValue;
    public event Action<LossIncident> IncidentClicked;
    readonly List<(Rect Rect, LossIncident Incident)> hits = new();
    Point mouse;
    bool hover;
    const double RowH = 30, HeadH = 24, LabelW = 96;

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 600 : availableSize.Width, HeadH + Math.Max(1, Rows.Count) * RowH + 6);

    protected override void OnPointerMoved(PointerEventArgs e) { mouse = e.GetPosition(this); hover = true; InvalidateVisual(); base.OnPointerMoved(e); }
    protected override void OnPointerExited(PointerEventArgs e) { hover = false; InvalidateVisual(); base.OnPointerExited(e); }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var inc = HitTest(e.GetPosition(this));
        if (inc != null) IncidentClicked?.Invoke(inc);
    }

    LossIncident HitTest(Point p) => hits.FirstOrDefault(h => h.Rect.Inflate(new Thickness(3, 0)).Contains(p)).Incident;

    public override void Render(DrawingContext dc)
    {
        hits.Clear();
        double W = Bounds.Width;
        if (Rows.Count == 0)
        {
            Skin.TextIn(dc, L.P("Keine Messungen im gewählten Zeitraum", "No measurements in the selected period"), 13, Skin.Faint,
                new Rect(Bounds.Size), FontWeight.Normal, TextAlignment.Center);
            return;
        }
        double x0 = LabelW, w = W - x0 - 10;
        if (w < 50) return;
        var grid = Skin.Pen(Skin.A(Skin.Grid, 160), 1, false, Skin.Dotted);
        for (int h = 0; h <= 24; h += 3)
        {
            double x = x0 + w * h / 24;
            Skin.TextIn(dc, h.ToString("00") + ":00", 11, Skin.Faint, new Rect(x - 30, 0, 60, HeadH), FontWeight.Normal, TextAlignment.Center);
            dc.DrawLine(grid, new Point(x, HeadH), new Point(x, HeadH + Rows.Count * RowH));
        }
        for (int i = 0; i < Rows.Count; i++)
        {
            var row = Rows[i];
            double y = HeadH + i * RowH;
            Skin.TextIn(dc, Skin.DayName(row.Day), 12, row.Day == DateTime.Today ? Skin.Text : Skin.Muted, new Rect(4, y, x0 - 8, RowH), FontWeight.Bold);
            var track = new Rect(x0, y + 7, w, RowH - 14);
            Skin.Round(dc, Skin.Surface2, track, 5);
            foreach (var (a, b) in row.Coverage)
            {
                double fa = (a - row.Day).TotalHours / 24, fb = (b - row.Day).TotalHours / 24;
                Skin.Round(dc, Skin.A(Skin.Accent, 95), new Rect(x0 + w * fa, track.Y, Math.Max(2, w * (fb - fa)), track.Height), 4);
            }
            foreach (var inc in row.Incidents)
            {
                double fa = (inc.Start - row.Day).TotalHours / 24;
                double iw = Math.Max(4, w * inc.Duration.TotalHours / 24);
                var r = new Rect(x0 + w * fa - iw / 2 + 2, track.Y - 3, iw, track.Height + 6);
                Skin.Round(dc, Skin.CauseColor(inc.Cause), r, 2);
                if (inc.Start >= SelStart && inc.Start <= SelEnd)
                    dc.DrawRectangle(null, Skin.Pen(Colors.White, 1.5), new RoundedRect(r.Inflate(2), 3));
                hits.Add((r, inc));
            }
        }

        if (hover && HitTest(mouse) is { } hit)
        {
            var lines = new[]
            {
                hit.Start.ToString(L.Date + "  HH:mm:ss") + " – " + hit.End.ToString("HH:mm:ss"),
                L.P("Dauer ", "Duration ") + Skin.ShortDuration(hit.Duration) + "  ·  " + hit.TotalLost + L.P(" Pakete verloren", " packets lost"),
                hit.CauseText
            };
            double tw = lines.Max(l => Skin.Measure(l, 12, FontWeight.Bold).Width);
            double bw = tw + 24, bh = 72;
            double bx = Math.Min(mouse.X + 14, W - bw - 4), by = mouse.Y + 16;
            if (by + bh > Bounds.Height) by = mouse.Y - bh - 8;
            var box = new Rect(bx, Math.Max(0, by), bw, bh);
            var cc = Skin.CauseColor(hit.Cause);
            dc.DrawRectangle(Skin.B(Skin.A(Skin.Bg, 240)), Skin.Pen(cc), new RoundedRect(box, 10));
            Skin.Draw(dc, lines[0], 12, Skin.Text, box.X + 12, box.Y + 9, FontWeight.Bold);
            Skin.Draw(dc, lines[1], 12, Skin.Muted, box.X + 12, box.Y + 29);
            Skin.Draw(dc, lines[2], 12, cc, box.X + 12, box.Y + 49, FontWeight.Bold);
        }
    }
}

// ---------- Speedtest ----------

// Tachometer für Download / Upload mit Tarif-Markierung.
class Gauge : Control
{
    public string Title = "";
    public Color Color = Skin.Cyan;
    public double Tariff;
    public string Sub = "";
    public Color SubColor = Skin.Muted;
    double target = double.NaN, shown;
    readonly DispatcherTimer anim = new() { Interval = TimeSpan.FromMilliseconds(30) };

    public Gauge()
    {
        anim.Tick += (_, _) =>
        {
            double t = double.IsNaN(target) ? 0 : target;
            shown += (t - shown) * 0.22;
            if (Math.Abs(t - shown) < 0.05) { shown = t; anim.Stop(); }
            InvalidateVisual();
        };
    }

    public double Value
    {
        get => target;
        set { target = value; anim.Start(); }
    }

    double Scale()
    {
        double m = Math.Max(50, Math.Max(Tariff * 1.15, (double.IsNaN(target) ? 0 : target) * 1.15));
        double[] steps = { 50, 100, 150, 200, 250, 300, 400, 500, 750, 1000, 1250, 1500, 2000, 2500, 5000, 10000 };
        foreach (var s in steps) if (m <= s) return s;
        return Math.Ceiling(m / 1000) * 1000;
    }

    public override void Render(DrawingContext dc)
    {
        double size = Math.Min(Bounds.Width - 20, (Bounds.Height - 10) * 1.25);
        if (size < 60) return;
        double thick = 16;
        var c = new Point(Bounds.Width / 2, 10 + size / 2);
        double r = size / 2 - thick / 2;
        const double start = 150, sweep = 240;
        double max = Scale();
        Donut.DrawArc(dc, c, r, start, sweep, Skin.Pen(Skin.Surface2, thick, true));
        double v = Math.Clamp(shown, 0, max);
        if (v > 0.01)
            Donut.DrawArc(dc, c, r, start, sweep * v / max, new Pen(Skin.Horizontal(Skin.Mix(Color, Skin.Accent, 0.4), Color), thick, lineCap: PenLineCap.Round));
        for (int i = 0; i <= 4; i++)
        {
            double a = (start + sweep * i / 4.0) * Math.PI / 180;
            double tx = c.X + Math.Cos(a) * (r - thick - 10), ty = c.Y + Math.Sin(a) * (r - thick - 10);
            Skin.Draw(dc, (max * i / 4).ToString("0"), 10, Skin.Faint, tx - 20, ty - 7, FontWeight.Normal, 40, TextAlignment.Center);
        }
        if (Tariff > 0 && Tariff <= max)
        {
            double a = (start + sweep * Tariff / max) * Math.PI / 180;
            dc.DrawLine(Skin.Pen(Colors.White, 2.5, true),
                new Point(c.X + Math.Cos(a) * (r - thick / 2 - 4), c.Y + Math.Sin(a) * (r - thick / 2 - 4)),
                new Point(c.X + Math.Cos(a) * (r + thick / 2 + 4), c.Y + Math.Sin(a) * (r + thick / 2 + 4)));
            Skin.Draw(dc, L.P("Tarif", "plan"), 10, Skin.Muted, c.X + Math.Cos(a) * (r + thick / 2 + 16) - 12, c.Y + Math.Sin(a) * (r + thick / 2 + 16) - 7, FontWeight.Bold);
        }
        Skin.Draw(dc, Title, 13, Color, 0, c.Y - 52, FontWeight.Bold, Bounds.Width, TextAlignment.Center);
        string val = double.IsNaN(target) ? "–" : shown.ToString(shown >= 100 ? "0" : "0.0");
        Skin.Draw(dc, val, 40, Skin.Text, 0, c.Y - 34, FontWeight.Bold, Bounds.Width, TextAlignment.Center);
        Skin.Draw(dc, "Mbit/s", 12, Skin.Muted, 0, c.Y + 22, FontWeight.Normal, Bounds.Width, TextAlignment.Center);
        Skin.Draw(dc, Sub, 12, SubColor, 0, c.Y + 44, FontWeight.Bold, Bounds.Width, TextAlignment.Center);
    }
}

// Balkendiagramm der letzten Speedtests (Download/Upload) mit Tarif-Linien.
class SpeedHistory : Control
{
    public List<SpeedResult> Items = new();
    public double TariffDown, TariffUp;
    Point mouse;
    bool hover;
    readonly List<(Rect Rect, SpeedResult Result)> hits = new();

    protected override void OnPointerMoved(PointerEventArgs e) { mouse = e.GetPosition(this); hover = true; InvalidateVisual(); base.OnPointerMoved(e); }
    protected override void OnPointerExited(PointerEventArgs e) { hover = false; InvalidateVisual(); base.OnPointerExited(e); }

    public override void Render(DrawingContext dc)
    {
        hits.Clear();
        var list = Items.Where(r => !double.IsNaN(r.Down) || !double.IsNaN(r.Up)).ToList();
        if (list.Count > 24) list.RemoveRange(0, list.Count - 24);
        var plot = new Rect(52, 26, Math.Max(0, Bounds.Width - 66), Math.Max(0, Bounds.Height - 56));
        if (plot.Width < 50 || plot.Height < 30) return;
        if (list.Count == 0)
        {
            Skin.TextIn(dc, L.P("Noch keine Speedtests – oben „Test starten“ klicken", "No speed tests yet – click “Start test” above"), 13, Skin.Faint, plot, FontWeight.Normal, TextAlignment.Center);
            return;
        }
        double max = Math.Max(TariffDown, TariffUp);
        foreach (var r in list) { if (!double.IsNaN(r.Down)) max = Math.Max(max, r.Down); if (!double.IsNaN(r.Up)) max = Math.Max(max, r.Up); }
        double[] steps = { 50, 100, 150, 200, 250, 300, 400, 500, 750, 1000, 1500, 2000, 2500, 5000, 10000 };
        double ymax = steps.FirstOrDefault(s => max * 1.1 <= s, 10000);
        var gridPen = Skin.Pen(Skin.Grid, 1, false, Skin.Dotted);
        for (int k = 0; k <= 4; k++)
        {
            double y = plot.Bottom - k * plot.Height / 4;
            dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            Skin.Draw(dc, (ymax * k / 4).ToString("0"), 11, Skin.Faint, 0, y - 8, FontWeight.Normal, plot.Left - 8, TextAlignment.Right);
        }
        Skin.Draw(dc, "Mbit/s", 10, Skin.Faint, 0, 0);
        double slot = plot.Width / list.Count, bw = Math.Clamp(slot * 0.32, 4, 22);
        for (int i = 0; i < list.Count; i++)
        {
            var r = list[i];
            double x = plot.Left + i * slot + slot / 2;
            var area = new Rect(plot.Left + i * slot, plot.Top, slot, plot.Height);
            if (hover && area.Contains(mouse)) Skin.Round(dc, Skin.A(Skin.Text, 12), area, 6);
            if (!double.IsNaN(r.Down))
            {
                double h = r.Down / ymax * plot.Height;
                Skin.Round(dc, r.Suspicious ? Skin.A(Skin.Cyan, 70) : Skin.Cyan, new Rect(x - bw - 1, plot.Bottom - h, bw, h), Math.Min(bw / 2, 4));
                if (r.Suspicious) Icons.Draw(dc, Glyph.Warning, new Rect(x - bw, plot.Bottom - h - 16, 13, 13), Skin.Warn);
            }
            if (!double.IsNaN(r.Up))
            {
                double h = r.Up / ymax * plot.Height;
                Skin.Round(dc, r.Suspicious ? Skin.A(Skin.Violet, 70) : Skin.Violet, new Rect(x + 1, plot.Bottom - h, bw, h), Math.Min(bw / 2, 4));
            }
            if (list.Count <= 12 || i % Math.Max(1, list.Count / 8) == 0)
                Skin.Draw(dc, r.Time.ToString(list.Count <= 6 ? L.DateTimeShort : "dd.MM."), 10, Skin.Faint, x - slot / 2 - 20, plot.Bottom + 6, FontWeight.Normal, slot + 40, TextAlignment.Center);
            hits.Add((area, r));
        }
        void TariffLine(double v, Color c, string label)
        {
            if (v <= 0) return;
            double y = plot.Bottom - v / ymax * plot.Height;
            dc.DrawLine(Skin.Pen(Skin.A(c, 200), 1.5, false, Skin.Dashed), new Point(plot.Left, y), new Point(plot.Right, y));
            Skin.Draw(dc, label, 10, c, plot.Left, y - 15, FontWeight.Bold, plot.Width - 4, TextAlignment.Right);
        }
        TariffLine(TariffDown, Skin.Cyan, L.P("Tarif Download ", "Plan download ") + TariffDown.ToString("0"));
        TariffLine(TariffUp, Skin.Violet, L.P("Tarif Upload ", "Plan upload ") + TariffUp.ToString("0"));

        if (!hover) return;
        var hit = hits.FirstOrDefault(h => h.Rect.Contains(mouse)).Result;
        if (hit == null) return;
        var lines = new[]
        {
            hit.Time.ToString(L.Date + "  HH:mm") + "  ·  " + hit.Provider,
            "↓ " + SpeedResult.Fmt(hit.Down) + "   ↑ " + SpeedResult.Fmt(hit.Up) + "  Mbit/s",
            "Ping " + SpeedResult.FmtMs(hit.Ping) + L.P("   ·   unter Last ", "   ·   loaded ") + SpeedResult.FmtMs(hit.LoadedPing)
        };
        double tw = lines.Max(l => Skin.Measure(l, 12, FontWeight.Bold).Width);
        var box = new Rect(Math.Min(mouse.X + 14, Bounds.Width - tw - 30), 8, tw + 24, 72);
        dc.DrawRectangle(Skin.B(Skin.A(Skin.Bg, 240)), Skin.Pen(Skin.Border), new RoundedRect(box, 10));
        for (int i = 0; i < lines.Length; i++)
            Skin.Draw(dc, lines[i], 12, i == 0 ? Skin.Text : Skin.Muted, box.X + 12, box.Y + 9 + i * 20, i == 0 ? FontWeight.Bold : FontWeight.Normal);
    }
}
