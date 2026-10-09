// NetMonitor – Installation und Deinstallation (pro Benutzer, ohne Adminrechte).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NetMonitor
{
    static class Installer
    {
        public static readonly string DefaultDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "NetMonitor");
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\NetMonitor";
        static readonly string[] RootFiles = { "NetMonitor.ps1", "NetMonitor.cmd", "README.md", "LICENSE" };

        public static string StartMenuLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "NetMonitor.lnk"); } }
        public static string DesktopLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "NetMonitor.lnk"); } }
        static string PowerShellExe { get { return Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"); } }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool DeleteFile(string path);

        static string Registered(string value)
        {
            using (var k = Registry.CurrentUser.OpenSubKey(UninstallKey))
                return k == null ? null : k.GetValue(value) as string;
        }

        public static string InstalledVersion() { return Registered("DisplayVersion"); }
        public static string InstalledDir() { return Registered("InstallLocation"); }

        public static string LaunchArgs(string dir, string extra)
        {
            return "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + Path.Combine(dir, "NetMonitor.ps1") + "\"" + extra;
        }

        public static void Launch(string dir)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(PowerShellExe, LaunchArgs(dir, ""))
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = dir
            });
        }

        static bool SamePath(string a, string b)
        {
            return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        public static void Install(string source, string target, bool desktop, bool startMenu, Action<int, string> progress)
        {
            if (!File.Exists(Path.Combine(source, "NetMonitor.ps1")) || !Directory.Exists(Path.Combine(source, "src")))
                throw new FileNotFoundException(L.P("Installationsdateien unvollständig (NetMonitor.ps1 oder src fehlt).",
                                                    "Installation files incomplete (NetMonitor.ps1 or src missing)."));
            Directory.CreateDirectory(target);
            if (!SamePath(source, target))
            {
                string srcTarget = Path.Combine(target, "src");
                Directory.CreateDirectory(srcTarget);
                foreach (var old in Directory.GetFiles(srcTarget, "*.cs")) File.Delete(old); // Update: alte Quellen entfernen

                var files = new List<string>();
                foreach (var f in RootFiles) if (File.Exists(Path.Combine(source, f))) files.Add(f);
                foreach (var f in Directory.GetFiles(Path.Combine(source, "src"), "*.cs")) files.Add(Path.Combine("src", Path.GetFileName(f)));
                for (int i = 0; i < files.Count; i++)
                {
                    progress(5 + 55 * i / files.Count, L.P("Kopiere ", "Copying ") + files[i]);
                    string dst = Path.Combine(target, files[i]);
                    File.Copy(Path.Combine(source, files[i]), dst, true);
                    DeleteFile(dst + ":Zone.Identifier"); // „Aus dem Internet“-Markierung entfernen
                }
            }

            progress(65, L.P("Erstelle Symbol …", "Creating icon …"));
            string ico = Path.Combine(target, "NetMonitor.ico");
            using (var fs = File.Create(ico)) Theme.WriteIco(fs);

            progress(75, L.P("Erstelle Verknüpfungen …", "Creating shortcuts …"));
            if (startMenu) CreateShortcut(StartMenuLink, target, ico); else if (File.Exists(StartMenuLink)) File.Delete(StartMenuLink);
            if (desktop) CreateShortcut(DesktopLink, target, ico); else if (File.Exists(DesktopLink)) File.Delete(DesktopLink);

            progress(88, L.P("Registriere in „Apps & Features“ …", "Registering in “Apps & features” …"));
            long size = 0;
            foreach (var f in Directory.GetFiles(target, "*", SearchOption.AllDirectories)) size += new FileInfo(f).Length;
            using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", "NetMonitor");
                k.SetValue("DisplayVersion", Program.Version);
                k.SetValue("Publisher", "NetMonitor");
                k.SetValue("DisplayIcon", ico);
                k.SetValue("InstallLocation", target);
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                k.SetValue("UninstallString", "\"" + PowerShellExe + "\" " + LaunchArgs(target, " -Uninstall"));
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)Math.Max(1, size / 1024), RegistryValueKind.DWord);
            }

            // Gewählte Sprache für die App übernehmen
            var settings = Storage.LoadSettings();
            settings["lang"] = L.Code;
            Storage.SaveSettings(settings);
            progress(100, L.P("Fertig", "Done"));
        }

        static void CreateShortcut(string link, string dir, string ico)
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(type);
            try
            {
                object lnk = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { link });
                var lt = lnk.GetType();
                Action<string, object> set = (name, value) => lt.InvokeMember(name, BindingFlags.SetProperty, null, lnk, new[] { value });
                set("TargetPath", PowerShellExe);
                set("Arguments", LaunchArgs(dir, ""));
                set("WorkingDirectory", dir);
                set("IconLocation", ico + ",0");
                set("Description", "NetMonitor – Ping & packet loss monitor");
                set("WindowStyle", 7);
                lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
                Marshal.FinalReleaseComObject(lnk);
            }
            finally { Marshal.FinalReleaseComObject(shell); }
        }

        // Entfernt Verknüpfungen, Registrierung und – nur im registrierten Installationsordner – die Programmdateien.
        public static bool Uninstall(string dir, bool removeData)
        {
            foreach (var link in new[] { StartMenuLink, DesktopLink })
                try { if (File.Exists(link)) File.Delete(link); } catch { }
            string registered = InstalledDir();
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
            if (removeData)
                try { if (Directory.Exists(Storage.Home)) Directory.Delete(Storage.Home, true); } catch { }

            bool deleteFolder = registered != null && SamePath(registered, dir) && File.Exists(Path.Combine(dir, "NetMonitor.ps1"));
            if (deleteFolder)
            {
                // Ordner erst löschen, wenn dieser Prozess beendet ist
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe",
                    "/c timeout /t 3 /nobreak >nul & rmdir /s /q \"" + dir + "\"")
                {
                    UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetTempPath()
                });
            }
            return deleteFolder;
        }
    }

    // Logo + Titel oben im Installer.
    class SetupHeader : Control
    {
        public string Subtitle = "";

        public SetupHeader()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            Height = Theme.S(72);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int ls = Theme.S(60);
            using (var logo = Theme.Logo(ls)) g.DrawImage(logo, 0, (Height - ls) / 2, ls, ls);
            Theme.DrawText(g, "NetMonitor", Theme.D(21f), Theme.Text, ls + Theme.S(16), Theme.S(6));
            Theme.DrawText(g, Subtitle, Theme.F(9.5f), Theme.Muted, ls + Theme.S(18), Theme.S(44));
        }
    }

    // Schlanker Fortschrittsbalken.
    class ProgressLine : Control
    {
        public int Value;

        public ProgressLine()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            Height = Theme.S(8);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.FillRound(g, Theme.Surface2, new RectangleF(0, 0, Width - 1, Height - 1), Height / 2f);
            float w = (Width - 1) * Math.Max(0, Math.Min(100, Value)) / 100f;
            if (w < 2) return;
            using (var path = Theme.Round(new RectangleF(0, 0, w, Height - 1), Height / 2f))
            using (var b = new LinearGradientBrush(new Rectangle(0, 0, Width, Height), Theme.Accent, Theme.Cyan, 0f)) g.FillPath(b, path);
        }
    }

    public class SetupForm : Form
    {
        readonly string source;
        string targetDir;
        bool desktop = true, startMenu = true, launch = true, done, busy;
        InputBox pathBox;
        Toggle tDesktop, tStart, tLaunch;
        PillButton btnInstall, btnBrowse;
        ProgressLine progress;
        Label status;

        public SetupForm(string source)
        {
            this.source = source;
            targetDir = Installer.InstalledDir() ?? Installer.DefaultDir;
            BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.F(10f);
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Theme.S(640), Theme.S(560));
            Icon = Theme.AppIcon;
            Theme.DarkWindow(this);
            Build();
        }

        void Build()
        {
            SuspendLayout();
            var old = new List<Control>();
            foreach (Control c in Controls) old.Add(c);
            Controls.Clear();
            foreach (var c in old) c.Dispose();

            string installed = Installer.InstalledVersion();
            Text = L.P("NetMonitor installieren", "Install NetMonitor");
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 9, BackColor = Theme.Bg, Padding = new Padding(Theme.S(28), Theme.S(22), Theme.S(28), Theme.S(22)) };
            for (int i = 0; i < 9; i++) root.RowStyles.Add(new RowStyle(i == 6 ? SizeType.Percent : SizeType.AutoSize, 100));

            var head = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Bg, AutoSize = true, Margin = new Padding(0) };
            head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            head.Controls.Add(new SetupHeader { Dock = DockStyle.Fill, Subtitle = "Setup · Version " + Program.Version }, 0, 0);
            var lang = new Segmented("DE", "EN") { Anchor = AnchorStyles.Top | AnchorStyles.Right, Margin = new Padding(0, Theme.S(4), 0, 0) };
            lang.SelectedIndex = L.En ? 1 : 0;
            lang.Changed += delegate { bool en = lang.SelectedIndex == 1; BeginInvoke((Action)(() => { L.Set(en); Build(); })); };
            head.Controls.Add(lang, 1, 0);
            root.Controls.Add(head, 0, 0);

            var intro = Theme.MakeLabel(L.P(
                "Misst dauerhaft Ping und Paketverluste zu Internet, Router und Spielservern – mit Dashboard, Verlauf und Störungsanalyse.\n" +
                "Die Installation erfolgt nur für dein Benutzerkonto und benötigt keine Adminrechte.",
                "Continuously measures ping and packet loss to the internet, your router and game servers – with dashboard, history and incident analysis.\n" +
                "Installs for your user account only and does not need administrator rights."), Theme.F(9.5f), Theme.Muted, Theme.Bg);
            intro.MaximumSize = new Size(Theme.S(580), 0);
            intro.Margin = new Padding(0, Theme.S(18), 0, Theme.S(6));
            root.Controls.Add(intro, 0, 1);

            if (installed != null)
            {
                var upd = Theme.MakeLabel(L.P("Version " + installed + " ist bereits installiert und wird aktualisiert. Verlauf und Einstellungen bleiben erhalten.",
                    "Version " + installed + " is already installed and will be updated. History and settings are kept."), Theme.F(9f, FontStyle.Bold), Theme.Cyan, Theme.Bg);
                upd.MaximumSize = new Size(Theme.S(580), 0);
                upd.Margin = new Padding(0, 0, 0, Theme.S(6));
                root.Controls.Add(upd, 0, 2);
            }

            var pathLabel = Theme.MakeLabel(L.P("Installationsordner", "Install folder"), Theme.F(9f, FontStyle.Bold), Theme.Muted, Theme.Bg);
            pathLabel.Margin = new Padding(0, Theme.S(14), 0, Theme.S(6));
            var pathRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = Theme.Bg, AutoSize = true, Margin = new Padding(0) };
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pathRow.Controls.Add(pathLabel, 0, 0);
            pathBox = new InputBox(targetDir, "", Theme.F(9.5f)) { Dock = DockStyle.Fill, BackColor = Theme.Bg, Margin = new Padding(0, 0, Theme.S(8), 0) };
            btnBrowse = new PillButton(L.P("Ändern …", "Change …"), Theme.Muted, true, Icons.Folder) { Height = Theme.S(34) };
            btnBrowse.Click += delegate
            {
                using (var dlg = new FolderBrowserDialog { SelectedPath = pathBox.Value })
                    if (dlg.ShowDialog(this) == DialogResult.OK) pathBox.Value = Path.Combine(dlg.SelectedPath, "NetMonitor");
            };
            pathRow.Controls.Add(pathBox, 0, 1);
            pathRow.Controls.Add(btnBrowse, 1, 1);
            root.Controls.Add(pathRow, 0, 3);

            var opts = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, BackColor = Theme.Bg, Margin = new Padding(0, Theme.S(16), 0, 0) };
            tStart = new Toggle(L.P("Eintrag im Startmenü", "Start menu entry"), startMenu);
            tDesktop = new Toggle(L.P("Verknüpfung auf dem Desktop", "Desktop shortcut"), desktop);
            tLaunch = new Toggle(L.P("NetMonitor nach der Installation starten", "Launch NetMonitor after installing"), launch);
            tStart.Changed += delegate { startMenu = tStart.Checked; };
            tDesktop.Changed += delegate { desktop = tDesktop.Checked; };
            tLaunch.Changed += delegate { launch = tLaunch.Checked; };
            opts.Controls.AddRange(new Control[] { tStart, tDesktop, tLaunch });
            root.Controls.Add(opts, 0, 4);

            progress = new ProgressLine { Dock = DockStyle.Fill, Margin = new Padding(0, Theme.S(18), 0, Theme.S(6)), Visible = false };
            status = Theme.MakeLabel("", Theme.F(9f), Theme.Faint, Theme.Bg);
            root.Controls.Add(progress, 0, 5);
            root.Controls.Add(status, 0, 7);

            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, WrapContents = false, BackColor = Theme.Bg, Margin = new Padding(0, Theme.S(10), 0, 0) };
            btnInstall = new PillButton(installed != null ? L.P("Aktualisieren", "Update") : L.P("Installieren", "Install"), Theme.Accent, false, Icons.Download);
            var btnCancel = new PillButton(L.P("Abbrechen", "Cancel"), Theme.Muted, true, null);
            btnInstall.Click += delegate { if (done) Finish(); else Install(); };
            btnCancel.Click += delegate { if (!busy) Close(); };
            buttons.Controls.Add(btnInstall); buttons.Controls.Add(btnCancel);
            root.Controls.Add(buttons, 0, 8);
            Controls.Add(root);
            ResumeLayout(true);
        }

        async void Install()
        {
            targetDir = pathBox.Value.Trim();
            if (targetDir.Length == 0) return;
            busy = true;
            btnInstall.Enabled = false; btnBrowse.Enabled = false; pathBox.ReadOnly = true;
            progress.Visible = true;
            Action<int, string> report = (v, text) => BeginInvoke((Action)(() => { progress.Value = v; progress.Invalidate(); status.Text = text; }));
            try
            {
                string dir = targetDir;
                bool d = desktop, s = startMenu;
                await Task.Run(() => Installer.Install(source, dir, d, s, report));
                await Task.Delay(150);
                status.ForeColor = Theme.Good;
                status.Text = L.P("✓ NetMonitor wurde installiert.", "✓ NetMonitor has been installed.") +
                    (startMenu ? L.P(" Du findest es im Startmenü.", " You can find it in the Start menu.") : "");
                done = true;
                btnInstall.Text = L.P("Fertig", "Finish");
                btnInstall.Glyph = Icons.Check;
            }
            catch (Exception ex)
            {
                status.ForeColor = Theme.Bad;
                status.Text = L.P("Installation fehlgeschlagen: ", "Installation failed: ") + ex.Message;
                pathBox.ReadOnly = false;
                btnBrowse.Enabled = true;
            }
            finally
            {
                busy = false;
                btnInstall.Enabled = true;
            }
        }

        void Finish()
        {
            if (launch) try { Installer.Launch(targetDir); } catch { }
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (busy) e.Cancel = true;
            base.OnFormClosing(e);
        }
    }

    public class UninstallForm : Form
    {
        readonly string dir;
        Toggle removeData;
        PillButton ok;
        Label status;
        bool done;

        public UninstallForm(string dir)
        {
            this.dir = dir;
            Text = L.P("NetMonitor deinstallieren", "Uninstall NetMonitor");
            BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.F(10f);
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Theme.S(560), Theme.S(330));
            Icon = Theme.AppIcon;
            Theme.DarkWindow(this);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, BackColor = Theme.Bg, Padding = new Padding(Theme.S(28), Theme.S(22), Theme.S(28), Theme.S(22)) };
            for (int i = 0; i < 5; i++) root.RowStyles.Add(new RowStyle(i == 3 ? SizeType.Percent : SizeType.AutoSize, 100));
            root.Controls.Add(new SetupHeader { Dock = DockStyle.Fill, Subtitle = L.P("Deinstallation", "Uninstall") + " · Version " + Program.Version }, 0, 0);
            var text = Theme.MakeLabel(L.P("Entfernt Programmdateien, Startmenü-Eintrag, Desktop-Verknüpfung und den Eintrag in „Apps & Features“.",
                "Removes the program files, Start menu entry, desktop shortcut and the “Apps & features” entry."), Theme.F(9.5f), Theme.Muted, Theme.Bg);
            text.MaximumSize = new Size(Theme.S(500), 0);
            text.Margin = new Padding(0, Theme.S(18), 0, Theme.S(10));
            root.Controls.Add(text, 0, 1);
            removeData = new Toggle(L.P("Auch Messverlauf und Einstellungen löschen", "Also delete measurement history and settings"), false);
            root.Controls.Add(removeData, 0, 2);
            status = Theme.MakeLabel("", Theme.F(9f, FontStyle.Bold), Theme.Good, Theme.Bg);
            status.Margin = new Padding(0, Theme.S(14), 0, 0);
            root.Controls.Add(status, 0, 3);

            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, WrapContents = false, BackColor = Theme.Bg };
            ok = new PillButton(L.P("Deinstallieren", "Uninstall"), Theme.Bad, false, Icons.Delete);
            var cancel = new PillButton(L.P("Abbrechen", "Cancel"), Theme.Muted, true, null);
            ok.Click += delegate
            {
                if (done) { Close(); return; }
                bool folder = Installer.Uninstall(dir, removeData.Checked);
                status.Text = folder ? L.P("✓ NetMonitor wurde entfernt.", "✓ NetMonitor has been removed.")
                                     : L.P("✓ Verknüpfungen und Registrierung entfernt. Dieser Ordner ist keine Installation und bleibt erhalten.",
                                           "✓ Shortcuts and registration removed. This folder is not an installation and was kept.");
                status.MaximumSize = new Size(Theme.S(500), 0);
                done = true;
                ok.Text = L.P("Schließen", "Close");
                ok.Glyph = Icons.Check;
                ok.Color = Theme.Accent;
                ok.Invalidate();
                cancel.Visible = false;
            };
            cancel.Click += delegate { Close(); };
            buttons.Controls.Add(ok); buttons.Controls.Add(cancel);
            root.Controls.Add(buttons, 0, 4);
            Controls.Add(root);
        }
    }
}
