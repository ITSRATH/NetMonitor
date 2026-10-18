// NetMonitor (macOS/Linux) – selbst gezeichnete Steuerelemente.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace NetMonitor;

// Abgerundete Fläche mit optionalem Farbakzent oben; nimmt ein Kind-Element auf.
class Surface : Decorator
{
    public Color Fill = Skin.Surface;
    public Color? Accent;
    public double Radius = 16;

    public Surface() { ClipToBounds = false; }

    public override void Render(DrawingContext dc) => Paint(dc, Bounds.Size, Fill, Accent, Radius);

    // Auch von Panels genutzt, die selbst wie eine Fläche aussehen sollen (z. B. die Ziel-Karten).
    public static void Paint(DrawingContext dc, Size size, Color fill, Color? accent, double radius)
    {
        var r = new Rect(size);
        if (r.Width < 4 || r.Height < 4) return;
        var rr = new RoundedRect(r.Deflate(0.5), radius);
        dc.DrawRectangle(Skin.B(fill), null, rr);
        if (accent is { } a)
        {
            using (dc.PushClip(rr))
            {
                dc.DrawRectangle(Skin.Vertical(Skin.A(a, 40), Skin.A(a, 0)), null, new Rect(0, 0, r.Width, Math.Min(110, r.Height)));
                dc.DrawRectangle(Skin.Horizontal(a, Skin.A(a, 40)), null, new Rect(0, 0, r.Width, 3));
            }
        }
        dc.DrawRectangle(null, Skin.Pen(Skin.Border), rr);
    }
}

// Abgerundeter Button mit Symbol; „Ghost“ = dezente Variante.
class PillButton : Control
{
    string text;
    Glyph glyph;
    public Color Color;
    public bool Ghost;
    bool hover, down;
    public event EventHandler Click;

    public PillButton(string text, Color color, bool ghost = false, Glyph glyph = Glyph.None)
    {
        this.text = text; Color = color; Ghost = ghost; this.glyph = glyph;
        Height = 38;
        Cursor = new Cursor(StandardCursorType.Hand);
        Focusable = true;
    }

    public string Text { get => text; set { text = value; InvalidateMeasure(); InvalidateVisual(); } }
    public Glyph Glyph { get => glyph; set { glyph = value; InvalidateMeasure(); InvalidateVisual(); } }

    protected override Size MeasureOverride(Size availableSize) =>
        new(Skin.Measure(text, 13.5, FontWeight.Bold).Width + 32 + (glyph != Glyph.None ? 24 : 0), Height);

    protected override void OnPointerEntered(PointerEventArgs e) { hover = true; InvalidateVisual(); base.OnPointerEntered(e); }
    protected override void OnPointerExited(PointerEventArgs e) { hover = down = false; InvalidateVisual(); base.OnPointerExited(e); }
    protected override void OnPointerPressed(PointerPressedEventArgs e) { down = true; InvalidateVisual(); e.Handled = true; base.OnPointerPressed(e); }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        bool click = down && new Rect(Bounds.Size).Contains(e.GetPosition(this));
        down = false;
        InvalidateVisual();
        base.OnPointerReleased(e);
        if (click && IsEffectivelyEnabled) Click?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsEnabledProperty) InvalidateVisual();
    }

    public override void Render(DrawingContext dc)
    {
        var r = new Rect(Bounds.Size).Deflate(0.5);
        bool enabled = IsEffectivelyEnabled;
        Color c = Ghost ? Skin.Surface2 : Color;
        if (!enabled) c = Skin.Mix(c, Skin.Bg, 0.6);
        else if (down) c = Skin.Mix(c, Colors.Black, 0.18);
        else if (hover) c = Skin.Mix(c, Colors.White, Ghost ? 0.07 : 0.12);
        var rr = new RoundedRect(r, 11);
        if (Ghost)
            dc.DrawRectangle(Skin.B(c), Skin.Pen(hover && enabled ? Skin.A(Color, 170) : Skin.Border), rr);
        else
            dc.DrawRectangle(Skin.Vertical(Skin.Mix(c, Colors.White, 0.10), Skin.Mix(c, Colors.Black, 0.12)),
                             Skin.Pen(Skin.A(Colors.White, enabled ? 40 : 10)), rr);
        Color tc = !enabled ? Skin.Faint : Ghost ? Skin.Text : Colors.White;
        Color ic = !enabled ? Skin.Faint : Ghost ? Color : Colors.White;
        double tw = Skin.Measure(text, 13.5, FontWeight.Bold).Width;
        double iconW = glyph == Glyph.None ? 0 : 24;
        double x = (r.Width - tw - iconW) / 2;
        if (iconW > 0) Icons.Draw(dc, glyph, new Rect(x, (r.Height - 17) / 2, 17, 17), ic);
        Skin.TextIn(dc, text, 13.5, tc, new Rect(x + iconW, 0, tw + 4, r.Height), FontWeight.Bold);
    }
}

// Segment-Umschalter (Reiter, Zeitraum, Sprache).
class Segmented : Control
{
    readonly string[] items;
    readonly List<Rect> rects = new();
    int selected, hoverIdx = -1;
    public event EventHandler Changed;

    public Segmented(params string[] items)
    {
        this.items = items;
        Height = 38;
        Cursor = new Cursor(StandardCursorType.Hand);
        double x = 4;
        foreach (var it in items)
        {
            double w = Skin.Measure(it, 13, FontWeight.Bold).Width + 30;
            rects.Add(new Rect(x, 4, w, Height - 8));
            x += w + 2;
        }
        Width = x + 2;
    }

    public int SelectedIndex
    {
        get => selected;
        set { selected = value; InvalidateVisual(); Changed?.Invoke(this, EventArgs.Empty); }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        int h = rects.FindIndex(r => r.Contains(p));
        if (h != hoverIdx) { hoverIdx = h; InvalidateVisual(); }
        base.OnPointerMoved(e);
    }

    protected override void OnPointerExited(PointerEventArgs e) { hoverIdx = -1; InvalidateVisual(); base.OnPointerExited(e); }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsEffectivelyEnabled) return;
        var p = e.GetPosition(this);
        int i = rects.FindIndex(r => r.Contains(p));
        if (i >= 0 && i != selected) SelectedIndex = i;
    }

    public override void Render(DrawingContext dc)
    {
        dc.DrawRectangle(Skin.B(Skin.Surface), Skin.Pen(Skin.Border), new RoundedRect(new Rect(Bounds.Size).Deflate(0.5), 12));
        for (int i = 0; i < items.Length; i++)
        {
            var ri = rects[i];
            if (i == selected)
                dc.DrawRectangle(Skin.Vertical(Skin.Mix(Skin.Accent, Colors.White, 0.12), Skin.Mix(Skin.Accent, Colors.Black, 0.1)), null, new RoundedRect(ri, 9));
            else if (i == hoverIdx) Skin.Round(dc, Skin.Surface2, ri, 9);
            Color tc = i == selected ? Colors.White : i == hoverIdx ? Skin.Text : Skin.Muted;
            Skin.TextIn(dc, items[i], 13, tc, ri, FontWeight.Bold, TextAlignment.Center);
        }
    }
}

// Schalter mit Beschriftung.
class Toggle : Control
{
    readonly string text;
    bool isChecked;
    public event EventHandler Changed;

    public Toggle(string text, bool on)
    {
        this.text = text; isChecked = on;
        Height = 34;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public bool Checked
    {
        get => isChecked;
        set { isChecked = value; InvalidateVisual(); Changed?.Invoke(this, EventArgs.Empty); }
    }

    protected override Size MeasureOverride(Size availableSize) => new(52 + Skin.Measure(text, 13).Width + 6, Height);
    protected override void OnPointerPressed(PointerPressedEventArgs e) { base.OnPointerPressed(e); Checked = !Checked; }

    public override void Render(DrawingContext dc)
    {
        double tw = 40, th = 22;
        var track = new Rect(2, (Bounds.Height - th) / 2, tw, th);
        dc.DrawRectangle(Skin.B(isChecked ? Skin.Accent : Skin.Surface2), isChecked ? null : Skin.Pen(Skin.Border), new RoundedRect(track, th / 2));
        double k = th - 6, kx = isChecked ? track.Right - k - 3 : track.X + 3;
        dc.DrawEllipse(Skin.B(isChecked ? Colors.White : Skin.Muted), null, new Point(kx + k / 2, track.Y + 3 + k / 2), k / 2, k / 2);
        Skin.TextIn(dc, text, 13, Skin.Text, new Rect(50, 0, Bounds.Width - 50, Bounds.Height));
    }
}

// Kennzahl-Kachel.
class KpiTile : Surface
{
    public string Title = "", Value = "–", Sub = "";
    public Glyph Glyph = Glyph.Packet;
    public Color Tint = Skin.Accent, ValueColor = Skin.Text;
    public bool Compact;

    public override void Render(DrawingContext dc)
    {
        base.Render(dc);
        double w = Bounds.Width;
        if (Compact)
        {
            double cp = 12;
            Skin.Dot(dc, Tint, new Point(cp + 4, cp + 9), 4);
            Skin.Draw(dc, Title, 11.5, Skin.Muted, cp + 14, cp + 1, FontWeight.Bold, w - cp * 2 - 14);
            Skin.TextIn(dc, Value, Value.Length > 14 ? 13 : 20, ValueColor, new Rect(cp, cp + 22, w - cp * 2, 28), FontWeight.Bold);
            Skin.Draw(dc, Sub, 10.5, Skin.Faint, cp, cp + 52, FontWeight.Normal, w - cp * 2);
            return;
        }
        double p = 16, ic = 34;
        var circle = new Rect(p, p, ic, ic);
        Skin.Round(dc, Skin.A(Tint, 40), circle, 10);
        Icons.Draw(dc, Glyph, circle.Deflate(9), Tint);
        Skin.TextIn(dc, Title, 12, Skin.Muted, new Rect(p + ic + 10, p, w - p * 2 - ic, ic), FontWeight.Bold);
        Skin.Draw(dc, Value, 27, ValueColor, p, p + ic + 6, FontWeight.Bold, w - p * 2);
        Skin.Draw(dc, Sub, 11.5, Skin.Faint, p, p + ic + 46, FontWeight.Normal, w - p * 2);
    }
}

// Ringdiagramm mit Legende (Ursachen der Störungen).
class Donut : Surface
{
    public readonly List<(string Label, double Value, Color Color)> Segments = new();
    public string Title = L.P("Ursachen", "Causes");

    public override void Render(DrawingContext dc)
    {
        base.Render(dc);
        double p = 16, size = Bounds.Height - p * 2;
        if (size < 30) return;
        var center = new Point(p + size / 2, p + size / 2);
        double rad = size / 2 - 12, thick = 13;
        double total = Segments.Sum(s => s.Value);
        dc.DrawEllipse(null, Skin.Pen(Skin.Surface2, thick), center, rad, rad);
        if (total > 0)
        {
            double start = -90;
            foreach (var s in Segments)
            {
                if (s.Value <= 0) continue;
                double sweep = 360 * s.Value / total;
                DrawArc(dc, center, rad, start + 1.5, Math.Max(0.5, sweep - 3), Skin.Pen(s.Color, thick));
                start += sweep;
            }
        }
        Skin.Draw(dc, total.ToString("0"), 23, Skin.Text, center.X - rad, center.Y - 20, FontWeight.Bold, rad * 2, TextAlignment.Center);
        Skin.Draw(dc, L.P("Störungen", "incidents"), 10, Skin.Faint, center.X - rad, center.Y + 6, FontWeight.Normal, rad * 2, TextAlignment.Center);

        double lx = p + size + 10, ly = p, w = Bounds.Width;
        Skin.Draw(dc, Title, 12, Skin.Muted, lx, ly, FontWeight.Bold);
        ly += 24;
        foreach (var s in Segments)
        {
            Skin.Dot(dc, s.Color, new Point(lx + 4.5, ly + 8), 4.5);
            string pct = total > 0 ? (100 * s.Value / total).ToString("0") + " %" : "–";
            Skin.Draw(dc, s.Label, 11.5, s.Value > 0 ? Skin.Text : Skin.Faint, lx + 16, ly, FontWeight.Normal, w - lx - 16 - p - 44);
            Skin.Draw(dc, pct, 11.5, s.Value > 0 ? s.Color : Skin.Faint, w - p - 44, ly, FontWeight.Bold, 44, TextAlignment.Right);
            ly += 21;
        }
    }

    public static void DrawArc(DrawingContext dc, Point c, double rad, double startDeg, double sweepDeg, IPen pen)
    {
        double a0 = startDeg * Math.PI / 180, a1 = (startDeg + sweepDeg) * Math.PI / 180;
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(c.X + rad * Math.Cos(a0), c.Y + rad * Math.Sin(a0)), false);
            ctx.ArcTo(new Point(c.X + rad * Math.Cos(a1), c.Y + rad * Math.Sin(a1)), new Size(rad, rad), 0, sweepDeg > 180, SweepDirection.Clockwise);
            ctx.EndFigure(false);
        }
        dc.DrawGeometry(null, pen, geo);
    }
}

// Schlanker Fortschrittsbalken.
class ProgressLine : Control
{
    public double Value;
    public ProgressLine() { Height = 8; }

    public override void Render(DrawingContext dc)
    {
        var r = new Rect(Bounds.Size);
        Skin.Round(dc, Skin.Surface2, r, r.Height / 2);
        double w = r.Width * Math.Clamp(Value, 0, 100) / 100;
        if (w > 2) dc.DrawRectangle(Skin.Horizontal(Skin.Accent, Skin.Cyan), null, new RoundedRect(new Rect(0, 0, w, r.Height), r.Height / 2));
    }
}

// Tabelle mit fester Kopfzeile, Auswahl und eigenem Scrollen.
class TableRow
{
    public string[] Cells = Array.Empty<string>();
    public Color?[] Colors;
    public bool[] Bold;
    public object Tag;
    public string Tooltip;
}

class Table : Control
{
    public string[] Columns = Array.Empty<string>();
    public double[] Weights = Array.Empty<double>();
    public Color[] HeaderColors;
    public readonly List<TableRow> Rows = new();
    public double RowHeight = 34, HeaderHeight = 44;
    public bool MultiSelect;
    public readonly HashSet<int> Selected = new();
    public string EmptyText = "";
    double scroll;
    int hoverRow = -1;
    public event EventHandler SelectionChanged;
    public event EventHandler<int> RowDoubleClicked;

    public Table() { ClipToBounds = true; Focusable = true; }

    public int SelectedIndex
    {
        get => Selected.Count == 0 ? -1 : Selected.Min();
        set { Selected.Clear(); if (value >= 0 && value < Rows.Count) Selected.Add(value); EnsureVisible(value); InvalidateVisual(); SelectionChanged?.Invoke(this, EventArgs.Empty); }
    }

    public object SelectedTag => SelectedIndex >= 0 ? Rows[SelectedIndex].Tag : null;

    public void Refresh() { scroll = Math.Clamp(scroll, 0, MaxScroll); InvalidateVisual(); }

    double MaxScroll => Math.Max(0, Rows.Count * RowHeight - (Bounds.Height - HeaderHeight));

    public void EnsureVisible(int row)
    {
        if (row < 0) return;
        double top = row * RowHeight, view = Bounds.Height - HeaderHeight;
        if (top < scroll) scroll = top;
        else if (top + RowHeight > scroll + view) scroll = top + RowHeight - view;
        scroll = Math.Clamp(scroll, 0, MaxScroll);
    }

    int RowAt(Point p) => p.Y < HeaderHeight ? -1 : (int)((p.Y - HeaderHeight + scroll) / RowHeight) is var i && i < Rows.Count ? i : -1;

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        scroll = Math.Clamp(scroll - e.Delta.Y * RowHeight * 3, 0, MaxScroll);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        int r = RowAt(e.GetPosition(this));
        if (r != hoverRow) { hoverRow = r; InvalidateVisual(); ToolTip.SetTip(this, r >= 0 ? Rows[r].Tooltip : null); }
        base.OnPointerMoved(e);
    }

    protected override void OnPointerExited(PointerEventArgs e) { hoverRow = -1; InvalidateVisual(); base.OnPointerExited(e); }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        int r = RowAt(e.GetPosition(this));
        if (r < 0) return;
        if (e.ClickCount == 2) { RowDoubleClicked?.Invoke(this, r); return; }
        bool multi = MultiSelect && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta));
        if (multi) { if (!Selected.Remove(r)) Selected.Add(r); }
        else { Selected.Clear(); Selected.Add(r); }
        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public override void Render(DrawingContext dc)
    {
        var size = Bounds.Size;
        dc.DrawRectangle(Skin.B(Skin.Surface), null, new Rect(size));
        double total = Weights.Sum();
        var x = new double[Columns.Length + 1];
        for (int i = 0; i < Columns.Length; i++) x[i + 1] = x[i] + size.Width * Weights[i] / total;

        using (dc.PushClip(new Rect(0, HeaderHeight, size.Width, Math.Max(0, size.Height - HeaderHeight))))
        {
            int first = (int)(scroll / RowHeight);
            for (int r = first; r < Rows.Count; r++)
            {
                double y = HeaderHeight + r * RowHeight - scroll;
                if (y > size.Height) break;
                var row = Rows[r];
                if (Selected.Contains(r)) dc.DrawRectangle(Skin.B(Skin.Mix(Skin.Surface, Skin.Accent, 0.28)), null, new Rect(0, y, size.Width, RowHeight));
                else if (r == hoverRow) dc.DrawRectangle(Skin.B(Skin.Mix(Skin.Surface, Skin.Text, 0.04)), null, new Rect(0, y, size.Width, RowHeight));
                dc.DrawLine(Skin.Pen(Skin.Border), new Point(0, y + RowHeight - 0.5), new Point(size.Width, y + RowHeight - 0.5));
                for (int c = 0; c < Columns.Length && c < row.Cells.Length; c++)
                {
                    Color col = row.Colors != null && c < row.Colors.Length && row.Colors[c].HasValue ? row.Colors[c].Value : Skin.Text;
                    bool bold = row.Bold != null && c < row.Bold.Length && row.Bold[c];
                    Skin.TextIn(dc, row.Cells[c], 12.5, col, new Rect(x[c] + 10, y, x[c + 1] - x[c] - 14, RowHeight), bold ? FontWeight.Bold : FontWeight.Normal);
                }
            }
        }
        if (Rows.Count == 0 && EmptyText.Length > 0)
            Skin.TextIn(dc, EmptyText, 13, Skin.Faint, new Rect(0, HeaderHeight, size.Width, Math.Max(40, size.Height - HeaderHeight)), FontWeight.Normal, TextAlignment.Center);

        dc.DrawRectangle(Skin.B(Skin.Surface2), null, new RoundedRect(new Rect(0, 0, size.Width, HeaderHeight), 6, 6, 0, 0));
        for (int c = 0; c < Columns.Length; c++)
        {
            Color hc = HeaderColors != null && c < HeaderColors.Length ? HeaderColors[c] : Skin.Muted;
            var lines = Columns[c].Split('\n');
            double lh = 15, y0 = (HeaderHeight - lines.Length * lh) / 2;
            for (int i = 0; i < lines.Length; i++)
                Skin.Draw(dc, lines[i], 11.5, hc, x[c] + 10, y0 + i * lh, FontWeight.Bold, x[c + 1] - x[c] - 14);
        }
        // Bildlaufleiste
        double content = Rows.Count * RowHeight, view = size.Height - HeaderHeight;
        if (content > view && view > 20)
        {
            double h = Math.Max(24, view * view / content), y = HeaderHeight + (view - h) * scroll / MaxScroll;
            Skin.Round(dc, Skin.A(Skin.Muted, 90), new Rect(size.Width - 6, y + 2, 4, h - 4), 2);
        }
    }
}

// Hinweisleiste mit Symbol (z. B. Verbindungsart im Speedtest-Fenster).
class Banner : Surface
{
    Glyph glyph = Glyph.Globe;
    Color tint = Skin.Muted;
    string message = "";

    public Banner() { Radius = 12; }

    public void Set(Glyph glyph, Color tint, string message)
    {
        this.glyph = glyph; this.tint = tint; this.message = message;
        Fill = Skin.Mix(Skin.Surface, tint, 0.10);
        InvalidateVisual();
    }

    public override void Render(DrawingContext dc)
    {
        base.Render(dc);
        double ic = 28;
        var circle = new Rect(12, (Bounds.Height - ic) / 2, ic, ic);
        dc.DrawEllipse(Skin.B(Skin.A(tint, 50)), null, circle.Center, ic / 2, ic / 2);
        Icons.Draw(dc, glyph, circle.Deflate(6), tint);
        Skin.Wrapped(dc, message, 12, Skin.Text, new Rect(52, 2, Bounds.Width - 64, Bounds.Height - 4));
    }
}

// Farblegende der Störungs-Ursachen.
class CauseLegend : Control
{
    public CauseLegend() { Height = 30; }

    public override void Render(DrawingContext dc)
    {
        var items = new[]
        {
            (L.P("gemessen", "measured"), Skin.A(Skin.Accent, 120)),
            (L.P("Internet / Provider", "Internet / ISP"), Skin.CauseColor(LossCause.Internet)),
            (L.P("Heimnetz / WLAN", "Home network / Wi-Fi"), Skin.CauseColor(LossCause.HomeNetwork)),
            (L.P("Einzelner Dienst", "Single service"), Skin.CauseColor(LossCause.SingleService)),
            (L.P("Nur Ziel-Server", "Target server only"), Skin.CauseColor(LossCause.TargetServer))
        };
        double x = 0;
        foreach (var (label, color) in items)
        {
            Skin.Round(dc, color, new Rect(x, Bounds.Height / 2 - 5, 14, 10), 3);
            Skin.TextIn(dc, label, 11.5, Skin.Muted, new Rect(x + 20, 0, 220, Bounds.Height));
            x += 32 + Skin.Measure(label, 11.5).Width;
        }
    }
}
