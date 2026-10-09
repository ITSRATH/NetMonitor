// NetMonitor – Sprachumschaltung Deutsch / Englisch.
using System;
using System.Globalization;
using System.Threading;

namespace NetMonitor
{
    static class L
    {
        public static bool En { get; private set; }

        // Liefert den Text in der aktuellen Sprache.
        public static string P(string de, string en) { return En ? en : de; }

        public static void Set(bool en)
        {
            En = en;
            var culture = new CultureInfo(en ? "en-GB" : "de-DE");
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
        }

        // Sprache aus den Einstellungen – oder, beim ersten Start, von Windows übernehmen.
        public static void Load()
        {
            string v;
            var d = Storage.LoadSettings();
            Set(d.TryGetValue("lang", out v) ? v == "en" : CultureInfo.InstalledUICulture.TwoLetterISOLanguageName != "de");
        }

        public static string Code { get { return En ? "en" : "de"; } }
        public static string Day { get { return P("ddd dd.MM.", "ddd dd MMM"); } }
        public static string DayYear { get { return P("ddd dd.MM.yy", "ddd dd MMM"); } }
        public static string Date { get { return P("dd.MM.yyyy", "dd MMM yyyy"); } }
        public static string DateTimeShort { get { return P("dd.MM. HH:mm", "dd MMM HH:mm"); } }
        public static string DayLong { get { return P("dddd, dd.MM.yyyy", "dddd, dd MMMM yyyy"); } }
    }
}
