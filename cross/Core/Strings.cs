// NetMonitor (macOS/Linux) – Sprachumschaltung Deutsch / Englisch.
using System.Globalization;

namespace NetMonitor;

static class L
{
    public static bool En { get; private set; }

    // Liefert den Text in der aktuellen Sprache.
    public static string P(string de, string en) => En ? en : de;

    public static void Set(bool en)
    {
        En = en;
        var culture = new CultureInfo(en ? "en-GB" : "de-DE");
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    // Sprache aus den Einstellungen – oder, beim ersten Start, vom System übernehmen.
    public static void Load()
    {
        var d = Storage.LoadSettings();
        Set(d.TryGetValue("lang", out var v) ? v == "en" : CultureInfo.InstalledUICulture.TwoLetterISOLanguageName != "de");
    }

    public static string Code => En ? "en" : "de";
    public static string Day => P("ddd dd.MM.", "ddd dd MMM");
    public static string DayYear => P("ddd dd.MM.yy", "ddd dd MMM");
    public static string Date => P("dd.MM.yyyy", "dd MMM yyyy");
    public static string DateTimeShort => P("dd.MM. HH:mm", "dd MMM HH:mm");
    public static string DayLong => P("dddd, dd.MM.yyyy", "dddd, dd MMMM yyyy");
}
