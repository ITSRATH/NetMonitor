// NetMonitor (macOS/Linux) – Farbschema, Schrift, Zeichenhilfen und selbst gezeichnete Symbole.
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace NetMonitor;

static class Skin
{
    public static readonly Color Bg = Color.FromRgb(9, 13, 24);
    public static readonly Color Surface = Color.FromRgb(16, 23, 40);
    public static readonly Color Surface2 = Color.FromRgb(24, 33, 56);
    public static readonly Color Input = Color.FromRgb(22, 31, 53);
    public static readonly Color Border = Color.FromRgb(36, 48, 78);
    public static readonly Color Grid = Color.FromRgb(34, 45, 72);
    public static readonly Color Text = Color.FromRgb(232, 236, 245);
    public static readonly Color Muted = Color.FromRgb(139, 151, 180);
    public static readonly Color Faint = Color.FromRgb(88, 101, 133);
    public static readonly Color Good = Color.FromRgb(34, 197, 94);
    public static readonly Color Warn = Color.FromRgb(245, 158, 11);
    public static readonly Color Bad = Color.FromRgb(244, 63, 94);
    public static readonly Color Accent = Color.FromRgb(99, 102, 241);
    public static readonly Color Cyan = Color.FromRgb(34, 211, 238);
    public static readonly Color Violet = Color.FromRgb(167, 139, 250);
    public static readonly Color Orange = Color.FromRgb(251, 146, 60);
    public static readonly Color Emerald = Color.FromRgb(52, 211, 153);
    public static readonly Color Pink = Color.FromRgb(244, 114, 182);
    public static readonly Color Yellow = Color.FromRgb(250, 204, 21);
    public static readonly Color White = Colors.White;
    public static readonly Color[] Targets = { Cyan, Violet, Orange, Emerald, Pink };

    public static readonly FontFamily Font = new("fonts:Inter#Inter");
    public static readonly FontFamily Mono = new("Menlo, DejaVu Sans Mono, Consolas, monospace");

    static readonly Dictionary<Color, IBrush> brushes = new();
    public static IBrush B(Color c)
    {
        if (!brushes.TryGetValue(c, out var b)) brushes[c] = b = new ImmutableSolidColorBrush(c);
        return b;
    }

    public static Color A(Color c, int alpha) => Color.FromArgb((byte)Math.Clamp(alpha, 0, 255), c.R, c.G, c.B);

    public static Color Mix(Color a, Color b, double t) => Color.FromArgb(a.A,
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    public static IPen Pen(Color c, double width = 1, bool round = false, IDashStyle dash = null) =>
        new ImmutablePen(new ImmutableSolidColorBrush(c), width, dash as ImmutableDashStyle, round ? PenLineCap.Round : PenLineCap.Flat, round ? PenLineJoin.Round : PenLineJoin.Miter);

    public static readonly IDashStyle Dotted = new ImmutableDashStyle(new double[] { 1, 3 }, 0);
    public static readonly IDashStyle Dashed = new ImmutableDashStyle(new double[] { 5, 4 }, 0);

    public static IBrush Vertical(Color top, Color bottom) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(top, 0), new GradientStop(bottom, 1) }
    };

    public static IBrush Horizontal(Color left, Color right) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        GradientStops = { new GradientStop(left, 0), new GradientStop(right, 1) }
    };

    public static IBrush Diagonal(Color a, Color b) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(a, 0), new GradientStop(b, 1) }
    };

    // ---------- Text ----------

    public static FormattedText Fmt(string text, double size, Color color, FontWeight weight = FontWeight.Normal,
                                    double maxWidth = 0, TextAlignment align = TextAlignment.Left, bool mono = false)
    {
        var ft = new FormattedText(text ?? "", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(mono ? Mono : Font, FontStyle.Normal, weight), size, B(color));
        if (maxWidth > 0)
        {
            ft.MaxTextWidth = maxWidth;
            ft.Trimming = TextTrimming.CharacterEllipsis;
            ft.TextAlignment = align;
            ft.MaxLineCount = 1;
        }
        return ft;
    }

    public static void Draw(DrawingContext dc, string text, double size, Color color, double x, double y,
                            FontWeight weight = FontWeight.Normal, double maxWidth = 0, TextAlignment align = TextAlignment.Left)
    {
        if (string.IsNullOrEmpty(text)) return;
        dc.DrawText(Fmt(text, size, color, weight, maxWidth, align), new Point(x, y));
    }

    // Text vertikal mittig in einem Rechteck.
    public static void TextIn(DrawingContext dc, string text, double size, Color color, Rect r,
                              FontWeight weight = FontWeight.Normal, TextAlignment align = TextAlignment.Left)
    {
        if (string.IsNullOrEmpty(text) || r.Width <= 2) return;
        var ft = Fmt(text, size, color, weight, r.Width, align);
        dc.DrawText(ft, new Point(r.X, r.Y + (r.Height - ft.Height) / 2));
    }

    // Mehrzeiliger Text mit Umbruch.
    public static double Wrapped(DrawingContext dc, string text, double size, Color color, Rect r, FontWeight weight = FontWeight.Normal)
    {
        var ft = new FormattedText(text ?? "", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(Font, FontStyle.Normal, weight), size, B(color))
        {
            MaxTextWidth = Math.Max(10, r.Width), MaxTextHeight = Math.Max(10, r.Height), Trimming = TextTrimming.CharacterEllipsis
        };
        dc.DrawText(ft, new Point(r.X, r.Y + Math.Max(0, (r.Height - ft.Height) / 2)));
        return ft.Height;
    }

    public static Size Measure(string text, double size, FontWeight weight = FontWeight.Normal)
    {
        var ft = Fmt(text, size, Text, weight);
        return new Size(ft.WidthIncludingTrailingWhitespace, ft.Height);
    }

    public static void Round(DrawingContext dc, Color fill, Rect r, double radius, Color? stroke = null)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        dc.DrawRectangle(B(fill), stroke.HasValue ? Pen(stroke.Value) : null, new RoundedRect(r, Math.Min(radius, Math.Min(r.Width, r.Height) / 2)));
    }

    public static void Dot(DrawingContext dc, Color c, Point center, double radius) => dc.DrawEllipse(B(c), null, center, radius, radius);

    // ---------- Fachliche Farben und Formate ----------

    public static Color Latency(long ms) => ms < 50 ? Good : ms < 100 ? Warn : Bad;
    public static Color Loss(double pct, long sent) => sent == 0 ? Text : pct == 0 ? Good : pct < 2 ? Warn : Bad;
    public static Color Availability(double pct) => pct >= 99.9 ? Good : pct >= 98 ? Warn : Bad;

    public static Color CauseColor(LossCause c) => c switch
    {
        LossCause.HomeNetwork => Orange,
        LossCause.Internet => Bad,
        LossCause.SingleService => Yellow,
        _ => Pink
    };

    public static string Duration(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";
    }

    public static string ShortDuration(TimeSpan t)
    {
        if (t.TotalSeconds < 60) return Math.Max(1, (int)Math.Round(t.TotalSeconds)) + " s";
        if (t.TotalMinutes < 60) return (int)t.TotalMinutes + " min " + t.Seconds + " s";
        return (int)t.TotalHours + " h " + t.Minutes + " min";
    }

    public static string DayName(DateTime day)
    {
        if (day == DateTime.Today) return L.P("Heute", "Today");
        if (day == DateTime.Today.AddDays(-1)) return L.P("Gestern", "Yesterday");
        return day.ToString(L.Day);
    }

    // ---------- Standard-Steuerelemente im App-Stil ----------

    public static TextBlock Label(string text, double size, Color color, FontWeight weight = FontWeight.Normal) => new()
    {
        Text = text, FontSize = size, Foreground = B(color), FontWeight = weight, FontFamily = Font, TextWrapping = TextWrapping.NoWrap,
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
    };

    public static TextBlock Paragraph(string text, double size, Color color, double maxWidth = double.PositiveInfinity) => new()
    {
        Text = text, FontSize = size, Foreground = B(color), FontFamily = Font, TextWrapping = TextWrapping.Wrap, MaxWidth = maxWidth, LineHeight = size * 1.45
    };

    public static TextBox TextInput(string text, string watermark, bool mono = false) => new()
    {
        Text = text, PlaceholderText = watermark, FontSize = 13, FontFamily = mono ? Mono : Font, Foreground = B(Text), Background = B(Input),
        BorderBrush = B(Border), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Height = 34,
        Padding = new Thickness(10, 7), VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center
    };

    public static ComboBox Combo(double width) => new()
    {
        Width = width, Height = 34, FontSize = 13, FontFamily = Font, Foreground = B(Text), Background = B(Input),
        BorderBrush = B(Border), CornerRadius = new CornerRadius(9), Padding = new Thickness(10, 4)
    };
}

public enum Glyph { None, Play, Stop, Refresh, History, Download, Bolt, Delete, Folder, Search, Check, Wifi, Lan, Globe, Warning, Clock, Packet }

// Einfache Vektor-Symbole (keine Symbolschrift nötig – sieht auf allen Systemen gleich aus).
static class Icons
{
    public static void Draw(DrawingContext dc, Glyph g, Rect r, Color c)
    {
        double s = Math.Min(r.Width, r.Height);
        var o = new Point(r.X + (r.Width - s) / 2, r.Y + (r.Height - s) / 2);
        Point P(double x, double y) => new(o.X + x * s, o.Y + y * s);
        var pen = Skin.Pen(c, Math.Max(1.4, s / 11), true);
        var fill = Skin.B(c);
        void Line(double x1, double y1, double x2, double y2) => dc.DrawLine(pen, P(x1, y1), P(x2, y2));
        void Poly(bool filled, bool closed, params double[] xy)
        {
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(P(xy[0], xy[1]), filled);
                for (int i = 2; i < xy.Length; i += 2) ctx.LineTo(P(xy[i], xy[i + 1]));
                ctx.EndFigure(closed);
            }
            dc.DrawGeometry(filled ? fill : null, filled ? null : pen, geo);
        }
        void Arc(double cx, double cy, double rad, double fromDeg, double toDeg)
        {
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                double a0 = fromDeg * Math.PI / 180, a1 = toDeg * Math.PI / 180;
                ctx.BeginFigure(P(cx + rad * Math.Cos(a0), cy + rad * Math.Sin(a0)), false);
                ctx.ArcTo(P(cx + rad * Math.Cos(a1), cy + rad * Math.Sin(a1)), new Size(rad * s, rad * s), 0,
                          Math.Abs(toDeg - fromDeg) > 180, SweepDirection.Clockwise);
                ctx.EndFigure(false);
            }
            dc.DrawGeometry(null, pen, geo);
        }
        void Circle(double cx, double cy, double rad) => dc.DrawEllipse(null, pen, P(cx, cy), rad * s, rad * s);

        switch (g)
        {
            case Glyph.Play: Poly(true, true, 0.28, 0.18, 0.82, 0.5, 0.28, 0.82); break;
            case Glyph.Stop: dc.DrawRectangle(fill, null, new RoundedRect(new Rect(P(0.24, 0.24), P(0.76, 0.76)), s * 0.06)); break;
            case Glyph.Refresh:
            case Glyph.History:
                Arc(0.5, 0.5, 0.33, -60, 230);
                Poly(false, false, 0.62, 0.12, 0.68, 0.22, 0.56, 0.27);
                if (g == Glyph.History) { Line(0.5, 0.33, 0.5, 0.52); Line(0.5, 0.52, 0.62, 0.6); }
                break;
            case Glyph.Download:
                Line(0.5, 0.15, 0.5, 0.62); Poly(false, false, 0.3, 0.44, 0.5, 0.64, 0.7, 0.44); Line(0.2, 0.84, 0.8, 0.84);
                break;
            case Glyph.Bolt: Poly(true, true, 0.56, 0.08, 0.24, 0.56, 0.47, 0.56, 0.42, 0.92, 0.76, 0.42, 0.53, 0.42); break;
            case Glyph.Delete:
                Line(0.18, 0.26, 0.82, 0.26); Poly(false, false, 0.38, 0.26, 0.42, 0.14, 0.58, 0.14, 0.62, 0.26);
                Poly(false, true, 0.26, 0.26, 0.31, 0.86, 0.69, 0.86, 0.74, 0.26); Line(0.44, 0.4, 0.44, 0.72); Line(0.56, 0.4, 0.56, 0.72);
                break;
            case Glyph.Folder: Poly(false, true, 0.12, 0.24, 0.42, 0.24, 0.5, 0.34, 0.88, 0.34, 0.88, 0.8, 0.12, 0.8); break;
            case Glyph.Search: Circle(0.43, 0.43, 0.25); Line(0.62, 0.62, 0.84, 0.84); break;
            case Glyph.Check: Poly(false, false, 0.2, 0.52, 0.42, 0.74, 0.82, 0.28); break;
            case Glyph.Wifi:
                Arc(0.5, 0.78, 0.6, 225, 315); Arc(0.5, 0.78, 0.41, 225, 315); Arc(0.5, 0.78, 0.22, 225, 315);
                dc.DrawEllipse(fill, null, P(0.5, 0.78), s * 0.06, s * 0.06);
                break;
            case Glyph.Lan:
                Poly(false, true, 0.22, 0.34, 0.78, 0.34, 0.78, 0.78, 0.22, 0.78);
                Poly(false, false, 0.38, 0.34, 0.38, 0.2, 0.62, 0.2, 0.62, 0.34);
                Line(0.36, 0.56, 0.36, 0.68); Line(0.5, 0.56, 0.5, 0.68); Line(0.64, 0.56, 0.64, 0.68);
                break;
            case Glyph.Globe:
                Circle(0.5, 0.5, 0.36); dc.DrawEllipse(null, pen, P(0.5, 0.5), s * 0.15, s * 0.36); Line(0.14, 0.5, 0.86, 0.5);
                break;
            case Glyph.Warning:
                Poly(false, true, 0.5, 0.12, 0.9, 0.84, 0.1, 0.84); Line(0.5, 0.38, 0.5, 0.6);
                dc.DrawEllipse(fill, null, P(0.5, 0.72), s * 0.045, s * 0.045);
                break;
            case Glyph.Clock: Circle(0.5, 0.5, 0.36); Line(0.5, 0.28, 0.5, 0.5); Line(0.5, 0.5, 0.66, 0.6); break;
            case Glyph.Packet:
                Poly(false, true, 0.5, 0.12, 0.84, 0.3, 0.84, 0.7, 0.5, 0.88, 0.16, 0.7, 0.16, 0.3);
                Poly(false, false, 0.16, 0.3, 0.5, 0.48, 0.84, 0.3); Line(0.5, 0.48, 0.5, 0.88);
                break;
        }
    }

    // NetMonitor-Logo: abgerundetes Quadrat mit Verlauf und Puls-Linie.
    public static void Logo(DrawingContext dc, Rect r)
    {
        dc.DrawRectangle(Skin.Diagonal(Skin.Accent, Skin.Cyan), null, new RoundedRect(r, r.Width * 0.28));
        double u = r.Width / 12, cy = r.Y + r.Height / 2;
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(r.X + 2 * u, cy), false);
            ctx.LineTo(new Point(r.X + 4.2 * u, cy));
            ctx.LineTo(new Point(r.X + 5.4 * u, cy - 3.2 * u));
            ctx.LineTo(new Point(r.X + 7 * u, cy + 3.4 * u));
            ctx.LineTo(new Point(r.X + 8.2 * u, cy));
            ctx.LineTo(new Point(r.X + 10 * u, cy));
            ctx.EndFigure(false);
        }
        dc.DrawGeometry(null, Skin.Pen(Colors.White, Math.Max(1.5, r.Width / 17), true), geo);
    }
}
