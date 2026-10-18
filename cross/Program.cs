// NetMonitor (macOS/Linux) – Einstiegspunkt.
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace NetMonitor;

class App : Application
{
    public static string ExportIconsTo;

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (ExportIconsTo != null)
            {
                // Für die Paketierung: Logo als PNG in mehreren Größen (Linux-Icon, macOS-iconset)
                Directory.CreateDirectory(ExportIconsTo);
                foreach (int px in new[] { 16, 32, 48, 64, 128, 256, 512, 1024 })
                    using (var bmp = LogoView.Render(px)) Png.Save(bmp, Path.Combine(ExportIconsTo, "netmonitor-" + px + ".png"));
                desktop.Shutdown();
                return;
            }
            L.Load();
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;
            var main = new MainWindow();
            desktop.MainWindow = main;
            if (ScreenshotTo != null) Capture(main, desktop);
        }
        base.OnFrameworkInitializationCompleted();
    }

    public static string ScreenshotTo;

    // Für Dokumentation und Tests: Haupt-, Verlaufs- und Speedtest-Fenster als PNG speichern und beenden.
    static async void Capture(MainWindow main, IClassicDesktopStyleApplicationLifetime desktop)
    {
        Directory.CreateDirectory(ScreenshotTo);
        await Task.Delay(1500);
        main.Start();
        await Task.Delay(12000);
        Save(main, "dashboard.png");
        main.Stop();
        var history = main.OpenHistory();
        await Task.Delay(3000);
        Save(history, "sessions.png");
        history.SelectTab(1);
        await Task.Delay(3000);
        Save(history, "packet-loss.png");
        history.Close();
        var speed = main.OpenSpeedtest();
        await Task.Delay(2500);
        Save(speed, "speedtest.png");
        desktop.Shutdown();
    }

    static void Save(Avalonia.Controls.Window w, string name)
    {
        try
        {
            double s = w.RenderScaling;
            using var bmp = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)(w.Bounds.Width * s), (int)(w.Bounds.Height * s)), new Vector(96 * s, 96 * s));
            bmp.Render(w);
            Png.Save(bmp, Path.Combine(ScreenshotTo, name));
        }
        catch (Exception ex) { Console.Error.WriteLine(name + ": " + ex.Message); }
    }
}

static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        int i = Array.IndexOf(args, "--export-icons");
        if (i >= 0) App.ExportIconsTo = i + 1 < args.Length ? args[i + 1] : "icons";
        int s = Array.IndexOf(args, "--screenshot");
        if (s >= 0) App.ScreenshotTo = s + 1 < args.Length ? args[s + 1] : "screenshots";
        if (args.Contains("--version")) { Console.WriteLine("NetMonitor " + AppInfo.Version); return 0; }
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .With(new FontManagerOptions { DefaultFamilyName = "fonts:Inter#Inter" })
        .LogToTrace();
}
