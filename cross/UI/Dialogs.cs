// NetMonitor (macOS/Linux) – Fensterbasis, Meldungen, Export, Löschdialog und Spielserver-Auswahl.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace NetMonitor;

// Gemeinsame Grundeinstellungen aller Fenster (dunkler Hintergrund, Schrift, Symbol).
class AppWindow : Window
{
    static WindowIcon icon;

    public AppWindow()
    {
        Background = Skin.B(Skin.Bg);
        FontFamily = Skin.Font;
        Foreground = Skin.B(Skin.Text);
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        try { Icon = icon ??= CreateIcon(); } catch { }
    }

    static WindowIcon CreateIcon()
    {
        using var bmp = LogoView.Render(128);
        var ms = new MemoryStream();
        Png.Save(bmp, ms);
        ms.Position = 0;
        return new WindowIcon(ms);
    }

    // Zeile mit Kindern nebeneinander.
    public static StackPanel Row(double spacing, params Control[] children)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing };
        foreach (var c in children) p.Children.Add(c);
        return p;
    }

    public static StackPanel Column(double spacing, params Control[] children)
    {
        var p = new StackPanel { Orientation = Orientation.Vertical, Spacing = spacing };
        foreach (var c in children) p.Children.Add(c);
        return p;
    }
}

// PNG speichern. Die neue Überladung mit BitmapEncoderOptions bringt hier keinen Vorteil.
static class Png
{
#pragma warning disable CS0618
    public static void Save(Bitmap bmp, string path) => bmp.Save(path);
    public static void Save(Bitmap bmp, Stream stream) => bmp.Save(stream);
#pragma warning restore CS0618
}

// Das Logo als eigenes Element (für Fenstersymbol und Icon-Export).
class LogoView : Control
{
    public override void Render(DrawingContext dc) => Icons.Logo(dc, new Rect(Bounds.Size));

    public static RenderTargetBitmap Render(int px)
    {
        var view = new LogoView { Width = px, Height = px };
        view.Measure(new Size(px, px));
        view.Arrange(new Rect(0, 0, px, px));
        var bmp = new RenderTargetBitmap(new PixelSize(px, px), new Vector(96, 96));
        bmp.Render(view);
        return bmp;
    }
}

enum MsgKind { Info, Warning, Error, Question }

// Einfache Meldung im App-Stil (Avalonia bringt keine MessageBox mit).
class MessageDialog : AppWindow
{
    MessageDialog(string title, string text, MsgKind kind, string ok, string cancel)
    {
        Title = title;
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Color tint = kind switch { MsgKind.Error => Skin.Bad, MsgKind.Warning => Skin.Warn, MsgKind.Question => Skin.Accent, _ => Skin.Cyan };
        Glyph glyph = kind switch { MsgKind.Error or MsgKind.Warning => Glyph.Warning, MsgKind.Question => Glyph.Check, _ => Glyph.Globe };

        var badge = new GlyphBadge(glyph, tint) { Width = 40, Height = 40, VerticalAlignment = VerticalAlignment.Top };
        var body = new SelectableTextBlock
        {
            Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13.5, FontFamily = Skin.Font, Foreground = Skin.B(Skin.Text), LineHeight = 20
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        if (cancel != null)
        {
            var c = new PillButton(cancel, Skin.Muted, true);
            c.Click += (_, _) => Close(false);
            buttons.Children.Add(c);
        }
        var o = new PillButton(ok, kind == MsgKind.Error ? Skin.Bad : Skin.Accent, false, kind == MsgKind.Question ? Glyph.Check : Glyph.None);
        o.Click += (_, _) => Close(true);
        buttons.Children.Add(o);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), Margin = new Thickness(22) };
        grid.Children.Add(badge);
        Grid.SetColumn(body, 1);
        body.Margin = new Thickness(16, 2, 0, 0);
        grid.Children.Add(body);
        Grid.SetRow(buttons, 1);
        Grid.SetColumnSpan(buttons, 2);
        buttons.Margin = new Thickness(0, 22, 0, 0);
        grid.Children.Add(buttons);
        Content = grid;
    }

    public static async Task Show(Window owner, string text, MsgKind kind = MsgKind.Info, string title = "NetMonitor")
    {
        var d = new MessageDialog(title, text, kind, "OK", null);
        if (owner != null && owner.IsVisible) await d.ShowDialog(owner); else d.Show();
    }

    public static async Task<bool> Ask(Window owner, string text, string yes, string no, MsgKind kind = MsgKind.Question, string title = "NetMonitor")
    {
        var d = new MessageDialog(title, text, kind, yes, no);
        return await d.ShowDialog<bool>(owner);
    }
}

// Rundes Symbol mit getönter Fläche.
class GlyphBadge : Control
{
    readonly Glyph glyph;
    readonly Color tint;
    public GlyphBadge(Glyph glyph, Color tint) { this.glyph = glyph; this.tint = tint; }

    public override void Render(DrawingContext dc)
    {
        var r = new Rect(Bounds.Size);
        dc.DrawEllipse(Skin.B(Skin.A(tint, 50)), null, r.Center, r.Width / 2, r.Height / 2);
        Icons.Draw(dc, glyph, r.Deflate(r.Width * 0.24), tint);
    }
}

static class Exporter
{
    // Fragt nach dem Speicherort, schreibt die CSV (+ ein PNG des Diagramms) und meldet das Ergebnis.
    public static async Task Run(Window owner, string defaultName, Control image, Action<string> writeCsv)
    {
        IStorageFolder docs = null;
        try { docs = await owner.StorageProvider.TryGetWellKnownFolderAsync(WellKnownFolder.Documents); } catch { }
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export",
            SuggestedFileName = defaultName,
            DefaultExtension = "csv",
            SuggestedStartLocation = docs,
            FileTypeChoices = new[] { new FilePickerFileType(L.P("CSV-Datei", "CSV file")) { Patterns = new[] { "*.csv" } } }
        });
        if (file == null) return;
        string path = file.TryGetLocalPath();
        if (path == null)
        {
            await MessageDialog.Show(owner, L.P("Dieser Speicherort wird nicht unterstützt.", "This location is not supported."), MsgKind.Error);
            return;
        }
        try
        {
            writeCsv(path);
            string msg = Storage.Display(path);
            if (image != null && image.Bounds.Width > 0 && image.Bounds.Height > 0)
            {
                string png = Path.ChangeExtension(path, ".png");
                double scale = owner.RenderScaling;
                var size = new PixelSize((int)(image.Bounds.Width * scale), (int)(image.Bounds.Height * scale));
                using var bmp = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
                using (var ctx = bmp.CreateDrawingContext())
                    ctx.FillRectangle(Skin.B(Skin.Bg), new Rect(image.Bounds.Size));
                bmp.Render(image);
                Png.Save(bmp, png);
                msg += "\n" + Storage.Display(png);
            }
            await MessageDialog.Show(owner, L.P("Export gespeichert:\n\n", "Export saved:\n\n") + msg);
        }
        catch (Exception ex)
        {
            await MessageDialog.Show(owner, L.P("Export fehlgeschlagen:\n", "Export failed:\n") + ex.Message, MsgKind.Error);
        }
    }
}

// Auswahl, welcher Teil des Verlaufs gelöscht wird.
class DeleteDialog : AppWindow
{
    public readonly List<HistoryEntry> ToDelete = new();
    readonly List<HistoryEntry> entries;
    readonly HistoryEntry selected;
    readonly string runningId;
    readonly RadioButton[] options;
    readonly TextBlock count;
    readonly PillButton ok;

    public DeleteDialog(List<HistoryEntry> entries, HistoryEntry selected, string runningId)
    {
        this.entries = entries; this.selected = selected; this.runningId = runningId;
        Title = L.P("Verlauf löschen", "Delete history");
        Width = 540;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        string[] labels =
        {
            selected != null ? L.P("Ausgewählte Sitzung (", "Selected session (") + selected.Start.ToString(L.Date + " HH:mm") + ")"
                             : L.P("Ausgewählte Sitzung (keine ausgewählt)", "Selected session (none selected)"),
            L.P("Alles älter als 7 Tage", "Everything older than 7 days"),
            L.P("Alles älter als 30 Tage", "Everything older than 30 days"),
            L.P("Gesamten Verlauf", "Entire history")
        };
        var opts = new StackPanel { Spacing = 6, Margin = new Thickness(0, 18, 0, 12) };
        options = new RadioButton[labels.Length];
        for (int i = 0; i < labels.Length; i++)
        {
            options[i] = new RadioButton { Content = labels[i], GroupName = "del", FontSize = 14, Foreground = Skin.B(Skin.Text) };
            options[i].IsCheckedChanged += (_, _) => UpdateCount();
            opts.Children.Add(options[i]);
        }
        options[0].IsEnabled = selected != null && !(selected.Running && selected.Id == runningId);

        count = Skin.Label("", 13, Skin.Warn, FontWeight.Bold);
        ok = new PillButton(L.P("Endgültig löschen", "Delete permanently"), Skin.Bad, false, Glyph.Delete);
        var cancel = new PillButton(L.P("Abbrechen", "Cancel"), Skin.Muted, true);
        ok.Click += (_, _) => { if (ToDelete.Count > 0) Close(true); };
        cancel.Click += (_, _) => Close(false);
        var buttons = Row(8, cancel, ok);
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        buttons.Margin = new Thickness(0, 18, 0, 0);

        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Children =
            {
                Skin.Label(L.P("Verlauf löschen", "Delete history"), 20, Skin.Text, FontWeight.Bold),
                Skin.Label(L.P("Gelöschte Sitzungen inkl. aller Messwerte lassen sich nicht wiederherstellen.",
                                "Deleted sessions and all their samples cannot be restored."), 12, Skin.Faint),
                opts, count, buttons
            }
        };
        options[options[0].IsEnabled ? 0 : 1].IsChecked = true;
        UpdateCount();
    }

    void UpdateCount()
    {
        if (count == null) return; // noch im Konstruktor
        ToDelete.Clear();
        int choice = Array.FindIndex(options, o => o.IsChecked == true);
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
        ok.IsEnabled = ToDelete.Count > 0;
    }
}

// Zeigt die erkannten Gegenstellen; der wahrscheinlichste Spielserver ist vorausgewählt.
class ServerPickDialog : AppWindow
{
    readonly List<Candidate> candidates;
    readonly Table grid;
    readonly TextBlock info;
    readonly PillButton ok;
    public string Address, NoteText;

    public ServerPickDialog(string game, List<Candidate> candidates)
    {
        this.candidates = candidates;
        Title = L.P("Spielserver erkannt", "Game server detected");
        Width = 920; Height = 470;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        grid = new Table
        {
            Columns = L.En ? new[] { "Address", "Port", "Protocol", "Activity", "Ping", "Assessment" }
                           : new[] { "Adresse", "Port", "Protokoll", "Aktivität", "Ping", "Einschätzung" },
            Weights = new double[] { 160, 60, 70, 110, 80, 140 }
        };
        for (int i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            string activity = c.Rate >= 0 ? c.Rate.ToString("0.0") + " " + c.RateUnit
                : (100 * c.Score / (c.Proto == "UDP" ? 1.0 : 0.3)).ToString("0") + L.P(" % der Zeit", " % of the time");
            bool likely = i == 0, possible = c.Proto == "UDP" && c.Score >= 0.5;
            grid.Rows.Add(new TableRow
            {
                Cells = new[] { c.Ip, c.Port.ToString(), c.Proto, activity, L.P("teste …", "testing …"),
                                likely ? L.P("★ wahrscheinlich", "★ likely") : possible ? L.P("möglich", "possible") : L.P("eher Nebenverkehr", "probably side traffic") },
                Colors = new Color?[] { null, null, null, null, Skin.Muted, likely ? Skin.Good : Skin.Muted },
                Bold = new[] { false, false, false, false, false, likely },
                Tag = c
            });
        }
        grid.SelectedIndex = 0;

        info = Skin.Paragraph(L.P("Antwortet der Server nicht auf Ping, wird automatisch der letzte erreichbare Router davor gemessen.",
            "If the server does not answer pings, the last reachable router in front of it is measured instead."), 12, Skin.Faint);
        ok = new PillButton(L.P("Übernehmen", "Apply"), Skin.Pink, false, Glyph.Check);
        var cancel = new PillButton(L.P("Abbrechen", "Cancel"), Skin.Muted, true);
        cancel.Click += (_, _) => Close(false);
        ok.Click += (_, _) => Apply();

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(22) };
        var head = Column(2,
            Skin.Label(L.P("Netzwerkverkehr von ", "Network traffic of ") + game, 20, Skin.Text, FontWeight.Bold),
            Skin.Label(L.P("Der wahrscheinlichste Spielserver (dauerhafter UDP-Verkehr) ist vorausgewählt.",
                            "The most likely game server (steady UDP traffic) is preselected."), 12, Skin.Faint));
        head.Margin = new Thickness(0, 0, 0, 12);
        root.Children.Add(head);
        var surface = new Surface { Child = grid, Padding = new Thickness(10) };
        Grid.SetRow(surface, 1);
        root.Children.Add(surface);
        info.Margin = new Thickness(0, 12, 0, 8);
        Grid.SetRow(info, 2);
        root.Children.Add(info);
        var buttons = Row(8, cancel, ok);
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetRow(buttons, 3);
        root.Children.Add(buttons);
        Content = root;

        Opened += (_, _) => TestAll();
    }

    bool closed;
    protected override void OnClosed(EventArgs e) { closed = true; base.OnClosed(e); }

    async void TestAll()
    {
        for (int i = 0; i < candidates.Count && !closed; i++)
        {
            var c = candidates[i];
            if (c.Answers == null) await TestOne(c);
            if (closed) return;
            grid.Rows[i].Cells[4] = c.Answers == true ? c.PingMs + " ms" : L.P("keine Antwort", "no reply");
            grid.Rows[i].Colors[4] = c.Answers == true ? Skin.Good : Skin.Warn;
            grid.Refresh();
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
        if (grid.SelectedTag is not Candidate c) return;
        ok.IsEnabled = false; grid.IsEnabled = false;
        if (c.Answers == null) { info.Text = L.P("Teste Ping …", "Testing ping …"); await TestOne(c); }
        if (closed) return;
        if (c.Answers == true)
        {
            Address = c.Ip;
            NoteText = L.P("Spielserver direkt · ", "Game server direct · ") + c.Proto + " Port " + c.Port;
        }
        else
        {
            info.Foreground = Skin.B(Skin.Warn);
            var hop = await NetTools.FindLastHop(c.Ip, n =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (!closed) info.Text = L.P("Server blockt Ping – suche Router davor … (Hop ", "Server blocks ping – looking for the router in front … (hop ") + n + ")";
                }));
            if (closed) return;
            if (hop is { } h && h.Ip != c.Ip)
            {
                Address = h.Ip;
                NoteText = L.P("Server " + c.Ip + " blockt Ping → Router davor (Hop " + h.Hop + ")",
                               "Server " + c.Ip + " blocks ping → router in front (hop " + h.Hop + ")");
            }
            else
            {
                Address = c.Ip;
                NoteText = hop != null ? L.P("Spielserver direkt · ", "Game server direct · ") + c.Proto + " Port " + c.Port
                                       : L.P("Server antwortet nicht auf Ping – Werte unzuverlässig", "Server does not answer pings – values unreliable");
            }
        }
        Close(true);
    }
}
