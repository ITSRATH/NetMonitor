// NetMonitor – Farbschema und selbst gezeichnete Steuerelemente.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NetMonitor
{
    static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(9, 13, 24);
        public static readonly Color Surface = Color.FromArgb(16, 23, 40);
        public static readonly Color Surface2 = Color.FromArgb(24, 33, 56);
        public static readonly Color Input = Color.FromArgb(22, 31, 53);
        public static readonly Color Border = Color.FromArgb(36, 48, 78);
        public static readonly Color Grid = Color.FromArgb(34, 45, 72);
        public static readonly Color Text = Color.FromArgb(232, 236, 245);
        public static readonly Color Muted = Color.FromArgb(139, 151, 180);
        public static readonly Color Faint = Color.FromArgb(88, 101, 133);
        public static readonly Color Good = Color.FromArgb(34, 197, 94);
        public static readonly Color Warn = Color.FromArgb(245, 158, 11);
        public static readonly Color Bad = Color.FromArgb(244, 63, 94);
        public static readonly Color Accent = Color.FromArgb(99, 102, 241);
        public static readonly Color Cyan = Color.FromArgb(34, 211, 238);
        public static readonly Color Violet = Color.FromArgb(167, 139, 250);
        public static readonly Color Orange = Color.FromArgb(251, 146, 60);
        public static readonly Color Emerald = Color.FromArgb(52, 211, 153);
        public static readonly Color Pink = Color.FromArgb(244, 114, 182);
        public static readonly Color Yellow = Color.FromArgb(250, 204, 21);
        public static readonly Color[] Targets = { Cyan, Violet, Orange, Emerald, Pink };

        public static float Scale = 1f;
        static string textFamily = "Segoe UI", displayFamily = "Segoe UI", iconFamily = "Segoe MDL2 Assets";
        static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();
        static bool initialized;

        static Theme() { Init(); }

        public static void Init()
        {
            if (initialized) return;
            initialized = true;
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) Scale = g.DpiX / 96f;
            if (Installed("Segoe UI Variable Text")) textFamily = "Segoe UI Variable Text";
            if (Installed("Segoe UI Variable Display")) displayFamily = "Segoe UI Variable Display";
            if (Installed("Segoe Fluent Icons")) iconFamily = "Segoe Fluent Icons";
        }

        static bool Installed(string name)
        {
            using (var f = new Font(name, 10f)) return f.Name == name;
        }

        public static int S(float px) { return (int)Math.Round(px * Scale); }

        public static Font F(float size, FontStyle style = FontStyle.Regular) { return Get(textFamily, size, style); }
        public static Font D(float size, FontStyle style = FontStyle.Bold) { return Get(displayFamily, size, style); }
        public static Font Icon(float size) { return Get(iconFamily, size, FontStyle.Regular); }

        static Font Get(string family, float size, FontStyle style)
        {
            string key = family + "|" + size + "|" + style;
            Font f;
            if (!fonts.TryGetValue(key, out f)) fonts[key] = f = new Font(family, size, style);
            return f;
        }

        public static Color A(Color c, int alpha) { return Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), c); }

        public static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb(a.A,
                (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        public static Color Latency(long ms) { return ms < 50 ? Good : ms < 100 ? Warn : Bad; }

        public static Color Loss(double pct, long sent)
        {
            if (sent == 0) return Text;
            return pct == 0 ? Good : pct < 2 ? Warn : Bad;
        }

        public static Color Availability(double pct) { return pct >= 99.9 ? Good : pct >= 98 ? Warn : Bad; }

        public static Color CauseColor(LossCause c)
        {
            switch (c)
            {
                case LossCause.HomeNetwork: return Orange;
                case LossCause.Internet: return Bad;
                case LossCause.SingleService: return Yellow;
                default: return Pink;
            }
        }

        public static string Duration(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            return string.Format("{0:00}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds);
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

        // ---------- Logo / App-Icon ----------

        public static Bitmap Logo(int size)
        {
            var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                var r = new RectangleF(0, 0, size - 1, size - 1);
                using (var path = Round(r, size * 0.28f))
                using (var b = new LinearGradientBrush(new Rectangle(0, 0, size, size), Accent, Cyan, 45f)) g.FillPath(b, path);
                float u = size / 12f, cy = size / 2f;
                var pulse = new[]
                {
                    new PointF(2 * u, cy), new PointF(4.2f * u, cy), new PointF(5.4f * u, cy - 3.2f * u),
                    new PointF(7f * u, cy + 3.4f * u), new PointF(8.2f * u, cy), new PointF(10 * u, cy)
                };
                using (var pen = new Pen(Color.White, Math.Max(1.5f, size / 17f)) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLines(pen, pulse);
            }
            return bmp;
        }

        static Icon appIcon;
        public static Icon AppIcon
        {
            get
            {
                if (appIcon == null)
                {
                    try
                    {
                        using (var ms = new System.IO.MemoryStream())
                        {
                            WriteIco(ms);
                            ms.Position = 0;
                            appIcon = new Icon(ms);
                        }
                    }
                    catch { appIcon = SystemIcons.Application; }
                }
                return appIcon;
            }
        }

        // Schreibt das Logo als .ico (PNG-Einträge 16–256 px), z. B. für Verknüpfungen.
        public static void WriteIco(System.IO.Stream output)
        {
            int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
            var images = new List<byte[]>();
            foreach (int s in sizes)
                using (var bmp = Logo(s))
                using (var ms = new System.IO.MemoryStream())
                {
                    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    images.Add(ms.ToArray());
                }
            var w = new System.IO.BinaryWriter(output);
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
                w.Write(images[i].Length); w.Write(offset);
                offset += images[i].Length;
            }
            foreach (var img in images) w.Write(img);
            w.Flush();
        }

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Color c, RectangleF r, float radius)
        {
            using (var path = Round(r, radius))
            using (var b = new SolidBrush(c)) g.FillPath(b, path);
        }

        public static void DrawText(Graphics g, string text, Font font, Color color, Rectangle r, TextFormatFlags flags)
        {
            TextRenderer.DrawText(g, text, font, r, color, flags | TextFormatFlags.NoPadding);
        }

        public static void DrawText(Graphics g, string text, Font font, Color color, int x, int y)
        {
            TextRenderer.DrawText(g, text, font, new Point(x, y), color, TextFormatFlags.NoPadding);
        }

        public static Size Measure(string text, Font font)
        {
            return TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding);
        }

        // ---------- Dunkle Fensterrahmen und Bildlaufleisten ----------

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        public static void DarkWindow(Form f)
        {
            f.HandleCreated += delegate
            {
                try
                {
                    int on = 1;
                    DwmSetWindowAttribute(f.Handle, 20, ref on, 4);
                    int caption = ColorTranslator.ToWin32(Bg);
                    DwmSetWindowAttribute(f.Handle, 35, ref caption, 4);
                }
                catch { }
            };
        }

        public static void DarkScroll(Control c)
        {
            c.HandleCreated += delegate { try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { } };
        }

        // Grauer Platzhaltertext in leeren Textfeldern.
        public static void SetPlaceholder(TextBox box, string text)
        {
            box.HandleCreated += delegate { SendMessage(box.Handle, 0x1501, (IntPtr)1, text); };
        }

        public static Label MakeLabel(string text, Font font, Color color, Color back)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = color, BackColor = back, Font = font, UseMnemonic = false };
        }

        public static void StyleGrid(DataGridView g)
        {
            g.BackgroundColor = Surface;
            g.BorderStyle = BorderStyle.None;
            g.GridColor = Border;
            g.EnableHeadersVisualStyles = false;
            g.RowHeadersVisible = false;
            g.AllowUserToAddRows = false;
            g.AllowUserToDeleteRows = false;
            g.AllowUserToResizeRows = false;
            g.ReadOnly = true;
            g.MultiSelect = false;
            g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            g.RowTemplate.Height = S(34);
            g.ColumnHeadersDefaultCellStyle.BackColor = Surface2;
            g.ColumnHeadersDefaultCellStyle.ForeColor = Muted;
            g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Surface2;
            g.ColumnHeadersDefaultCellStyle.SelectionForeColor = Muted;
            g.ColumnHeadersDefaultCellStyle.Font = F(9f, FontStyle.Bold);
            g.ColumnHeadersDefaultCellStyle.Padding = new Padding(S(6), S(8), S(6), S(8));
            g.DefaultCellStyle.BackColor = Surface;
            g.DefaultCellStyle.ForeColor = Text;
            g.DefaultCellStyle.SelectionBackColor = Mix(Surface, Accent, 0.28f);
            g.DefaultCellStyle.SelectionForeColor = Text;
            g.DefaultCellStyle.Font = F(9.5f);
            g.DefaultCellStyle.Padding = new Padding(S(6), 0, S(6), 0);
            DarkScroll(g);
        }
    }

    // Symbole aus „Segoe Fluent Icons“ / „Segoe MDL2 Assets“.
    static class Icons
    {
        public const string Play = "", Stop = "", Refresh = "", History = "",
            Download = "", Delete = "", Folder = "", Sync = "", Warning = "",
            Clock = "", Home = "", Globe = "", Game = "", Search = "",
            Packet = "", Check = "";
    }

    // Abgerundete Fläche mit optionalem Farbakzent oben.
    class Surface : Panel
    {
        public Color Fill = Theme.Surface;
        public Color Accent = Color.Empty;
        public int Radius = 16;

        public Surface()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            if (Width < 4 || Height < 4) return;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.Round(r, Theme.S(Radius)))
            {
                using (var b = new SolidBrush(Fill)) g.FillPath(b, path);
                if (Accent != Color.Empty)
                {
                    g.SetClip(path);
                    int gh = Theme.S(110);
                    using (var b = new LinearGradientBrush(new Rectangle(0, -1, Width, gh + 1), Theme.A(Accent, 40), Theme.A(Accent, 0), 90f))
                        g.FillRectangle(b, 0, 0, Width, gh);
                    using (var b = new LinearGradientBrush(new Rectangle(0, 0, Width + 1, Theme.S(3)), Accent, Theme.A(Accent, 40), 0f))
                        g.FillRectangle(b, 0, 0, Width, Theme.S(3));
                    g.ResetClip();
                }
                using (var pen = new Pen(Theme.Border)) g.DrawPath(pen, path);
            }
            base.OnPaint(e);
        }
    }

    // Abgerundeter Button mit Symbol; „Ghost“ = dezente Variante.
    class PillButton : Control
    {
        public Color Color;
        public bool Ghost;
        string glyph;
        bool hover, down;

        public PillButton(string text, Color color, bool ghost, string glyph)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Color = color; Ghost = ghost; this.glyph = glyph;
            Font = Theme.F(10f, FontStyle.Bold);
            BackColor = Theme.Bg;
            Cursor = Cursors.Hand;
            Margin = new Padding(Theme.S(4), 0, Theme.S(4), 0);
            Height = Theme.S(38);
            Text = text;
        }

        public string Glyph { get { return glyph; } set { glyph = value; AutoWidth(); Invalidate(); } }

        void AutoWidth()
        {
            int w = Theme.Measure(Text ?? "", Font).Width + Theme.S(32);
            if (!string.IsNullOrEmpty(glyph)) w += Theme.S(24);
            Width = w;
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); AutoWidth(); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Color c = Ghost ? Theme.Surface2 : Color;
            if (!Enabled) c = Theme.Mix(c, Theme.Bg, 0.6f);
            else if (down) c = Theme.Mix(c, Color.Black, 0.18f);
            else if (hover) c = Theme.Mix(c, Color.White, Ghost ? 0.07f : 0.12f);
            using (var path = Theme.Round(r, Theme.S(11)))
            {
                if (Ghost)
                {
                    using (var b = new SolidBrush(c)) g.FillPath(b, path);
                    using (var pen = new Pen(hover && Enabled ? Theme.A(Color, 170) : Theme.Border)) g.DrawPath(pen, path);
                }
                else
                {
                    using (var b = new LinearGradientBrush(new Rectangle(0, 0, Width, Height + 1),
                        Theme.Mix(c, Color.White, 0.10f), Theme.Mix(c, Color.Black, 0.12f), 90f))
                        g.FillPath(b, path);
                    using (var pen = new Pen(Theme.A(Color.White, Enabled ? 40 : 10))) g.DrawPath(pen, path);
                }
            }
            Color tc = !Enabled ? Theme.Faint : Ghost ? Theme.Text : Color.White;
            Color ic = !Enabled ? Theme.Faint : Ghost ? Color : Color.White;
            var ts = Theme.Measure(Text, Font);
            int iconW = string.IsNullOrEmpty(glyph) ? 0 : Theme.S(24);
            int x = (Width - ts.Width - iconW) / 2;
            if (iconW > 0)
                Theme.DrawText(g, glyph, Theme.Icon(11f), ic, new Rectangle(x, 0, Theme.S(18), Height),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            Theme.DrawText(g, Text, Font, tc, new Rectangle(x + iconW, 0, ts.Width + 2, Height), TextFormatFlags.VerticalCenter);
        }
    }

    // Textfeld mit abgerundetem Rahmen.
    class InputBox : Panel
    {
        public readonly TextBox Box;
        bool focused;

        public InputBox(string text, string placeholder, Font font)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
            Box = new TextBox { BorderStyle = BorderStyle.None, BackColor = Theme.Input, ForeColor = Theme.Text, Font = font, Text = text };
            Theme.SetPlaceholder(Box, placeholder);
            Box.GotFocus += delegate { focused = true; Invalidate(); };
            Box.LostFocus += delegate { focused = false; Invalidate(); };
            Controls.Add(Box);
            Height = Theme.S(34);
            Cursor = Cursors.IBeam;
        }

        public string Value { get { return Box.Text; } set { Box.Text = value; } }

        public bool ReadOnly
        {
            set { Box.ReadOnly = value; Box.BackColor = Theme.Input; Box.ForeColor = value ? Theme.Muted : Theme.Text; Invalidate(); }
        }

        protected override void OnClick(EventArgs e) { base.OnClick(e); Box.Focus(); }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Box == null) return;
            int h = Box.PreferredHeight;
            Box.SetBounds(Theme.S(11), (Height - h) / 2 + 1, Math.Max(10, Width - Theme.S(22)), h);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.Round(r, Theme.S(9)))
            {
                using (var b = new SolidBrush(Theme.Input)) g.FillPath(b, path);
                using (var pen = new Pen(focused ? Theme.Accent : Theme.Border)) g.DrawPath(pen, path);
            }
        }
    }

    // Dunkles Auswahlfeld (öffnet ein dunkles Menü).
    class DarkSelect : Control
    {
        public readonly List<object> Items = new List<object>();
        public event EventHandler SelectedIndexChanged;
        public event EventHandler DropDownOpening;
        public string Placeholder = "";
        int selected = -1;
        bool hover;

        public DarkSelect()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = Theme.S(34);
            Font = Theme.F(9.5f);
            BackColor = Theme.Surface;
            Cursor = Cursors.Hand;
        }

        public int SelectedIndex
        {
            get { return selected; }
            set
            {
                selected = value;
                Invalidate();
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }

        public object SelectedItem { get { return selected >= 0 && selected < Items.Count ? Items[selected] : null; } }

        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (!Enabled) return;
            if (DropDownOpening != null) DropDownOpening(this, EventArgs.Empty);
            var menu = new ContextMenuStrip { Renderer = new DarkRenderer(), ShowImageMargin = false, Font = Font, BackColor = Theme.Surface2 };
            for (int i = 0; i < Items.Count; i++)
            {
                int idx = i;
                var item = new ToolStripMenuItem(Items[i].ToString()) { AutoSize = false, Width = Math.Max(Width, Theme.S(200)), Height = Theme.S(32) };
                if (i == selected) item.Font = Theme.F(9.5f, FontStyle.Bold);
                item.Click += delegate { SelectedIndex = idx; };
                menu.Items.Add(item);
            }
            if (Items.Count == 0) menu.Items.Add(new ToolStripMenuItem(L.P("(keine Einträge)", "(no entries)")) { Enabled = false });
            menu.Closed += delegate { BeginInvoke((Action)menu.Dispose); };
            menu.Show(this, new Point(0, Height + Theme.S(4)));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.Round(r, Theme.S(9)))
            {
                using (var b = new SolidBrush(hover && Enabled ? Theme.Mix(Theme.Input, Color.White, 0.04f) : Theme.Input)) g.FillPath(b, path);
                using (var pen = new Pen(hover && Enabled ? Theme.Mix(Theme.Border, Color.White, 0.15f) : Theme.Border)) g.DrawPath(pen, path);
            }
            var item = SelectedItem;
            string text = item != null ? item.ToString() : Placeholder;
            Color tc = !Enabled ? Theme.Faint : item != null ? Theme.Text : Theme.Faint;
            Theme.DrawText(g, text, Font, tc, new Rectangle(Theme.S(11), 0, Width - Theme.S(36), Height),
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            int cx = Width - Theme.S(17), cy = Height / 2;
            using (var pen = new Pen(Enabled ? Theme.Muted : Theme.Faint, Theme.S(1.6f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLines(pen, new[] { new Point(cx - Theme.S(4), cy - Theme.S(2)), new Point(cx, cy + Theme.S(2)), new Point(cx + Theme.S(4), cy - Theme.S(2)) });
        }
    }

    class DarkColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Theme.Surface2; } }
        public override Color MenuBorder { get { return Theme.Border; } }
        public override Color MenuItemBorder { get { return Theme.Surface2; } }
        public override Color MenuItemSelected { get { return Theme.Mix(Theme.Surface2, Theme.Accent, 0.35f); } }
        public override Color ImageMarginGradientBegin { get { return Theme.Surface2; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Surface2; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Surface2; } }
        public override Color SeparatorDark { get { return Theme.Border; } }
    }

    class DarkRenderer : ToolStripProfessionalRenderer
    {
        public DarkRenderer() : base(new DarkColors()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Faint;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var r = new Rectangle(Theme.S(4), 1, e.Item.Width - Theme.S(8), e.Item.Height - 2);
            Color c = e.Item.Selected && e.Item.Enabled ? Theme.Mix(Theme.Surface2, Theme.Accent, 0.35f) : Theme.Surface2;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.FillRound(e.Graphics, c, r, Theme.S(6));
        }
    }

    // Segment-Umschalter (Reiter, Zeitraum-Filter).
    class Segmented : Control
    {
        readonly string[] items;
        readonly Rectangle[] rects;
        int selected, hoverIdx = -1;
        public event EventHandler Changed;

        public Segmented(params string[] items)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            this.items = items;
            rects = new Rectangle[items.Length];
            Font = Theme.F(9.5f, FontStyle.Bold);
            BackColor = Theme.Bg;
            Height = Theme.S(38);
            Cursor = Cursors.Hand;
            int x = Theme.S(4);
            for (int i = 0; i < items.Length; i++)
            {
                int w = Theme.Measure(items[i], Font).Width + Theme.S(30);
                rects[i] = new Rectangle(x, Theme.S(4), w, Height - Theme.S(8));
                x += w + Theme.S(2);
            }
            Width = x + Theme.S(2);
        }

        public int SelectedIndex
        {
            get { return selected; }
            set { selected = value; Invalidate(); if (Changed != null) Changed(this, EventArgs.Empty); }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = -1;
            for (int i = 0; i < rects.Length; i++) if (rects[i].Contains(e.Location)) h = i;
            if (h != hoverIdx) { hoverIdx = h; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hoverIdx = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            for (int i = 0; i < rects.Length; i++)
                if (rects[i].Contains(e.Location) && i != selected) SelectedIndex = i;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var path = Theme.Round(r, Theme.S(12)))
            {
                using (var b = new SolidBrush(Theme.Surface)) g.FillPath(b, path);
                using (var pen = new Pen(Theme.Border)) g.DrawPath(pen, path);
            }
            for (int i = 0; i < items.Length; i++)
            {
                var ri = rects[i];
                if (i == selected)
                {
                    using (var path = Theme.Round(ri, Theme.S(9)))
                    using (var b = new LinearGradientBrush(new Rectangle(ri.X, ri.Y, ri.Width, ri.Height + 1),
                        Theme.Mix(Theme.Accent, Color.White, 0.12f), Theme.Mix(Theme.Accent, Color.Black, 0.1f), 90f))
                        g.FillPath(b, path);
                }
                else if (i == hoverIdx) Theme.FillRound(g, Theme.Surface2, ri, Theme.S(9));
                Color tc = i == selected ? Color.White : i == hoverIdx ? Theme.Text : Theme.Muted;
                Theme.DrawText(g, items[i], Font, tc, ri, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }

    // Schalter mit Beschriftung.
    class Toggle : Control
    {
        bool isChecked;
        public event EventHandler Changed;

        public Toggle(string text, bool on)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            isChecked = on;
            Font = Theme.F(9.5f);
            BackColor = Theme.Bg;
            Cursor = Cursors.Hand;
            Text = text;
            Height = Theme.S(38);
            Width = Theme.S(52) + Theme.Measure(text, Font).Width + Theme.S(6);
        }

        public bool Checked
        {
            get { return isChecked; }
            set { isChecked = value; Invalidate(); if (Changed != null) Changed(this, EventArgs.Empty); }
        }

        protected override void OnClick(EventArgs e) { base.OnClick(e); Checked = !Checked; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            int tw = Theme.S(40), th = Theme.S(22);
            var track = new RectangleF(Theme.S(2), (Height - th) / 2f, tw, th);
            Theme.FillRound(g, isChecked ? Theme.Accent : Theme.Surface2, track, th / 2f);
            if (!isChecked) using (var path = Theme.Round(track, th / 2f)) using (var pen = new Pen(Theme.Border)) g.DrawPath(pen, path);
            float k = th - Theme.S(6);
            float kx = isChecked ? track.Right - k - Theme.S(3) : track.X + Theme.S(3);
            using (var b = new SolidBrush(isChecked ? Color.White : Theme.Muted)) g.FillEllipse(b, kx, track.Y + Theme.S(3), k, k);
            Theme.DrawText(g, Text, Font, Theme.Text, new Rectangle(Theme.S(50), 0, Width - Theme.S(50), Height), TextFormatFlags.VerticalCenter);
        }
    }

    // Kennzahl-Kachel.
    class KpiTile : Surface
    {
        public string Title = "", Value = "–", Sub = "", Glyph = Icons.Packet;
        public Color Tint = Theme.Accent, ValueColor = Theme.Text;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            int p = Theme.S(16), ic = Theme.S(34);
            var circle = new Rectangle(p, p, ic, ic);
            Theme.FillRound(g, Theme.A(Tint, 40), circle, Theme.S(10));
            Theme.DrawText(g, Glyph, Theme.Icon(12f), Tint, circle, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            Theme.DrawText(g, Title, Theme.F(9f, FontStyle.Bold), Theme.Muted,
                new Rectangle(p + ic + Theme.S(10), p, Width - p * 2 - ic, ic), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            Theme.DrawText(g, Value, Theme.D(20f), ValueColor,
                new Rectangle(p, p + ic + Theme.S(8), Width - p * 2, Theme.S(36)), TextFormatFlags.EndEllipsis);
            Theme.DrawText(g, Sub, Theme.F(8.5f), Theme.Faint,
                new Rectangle(p, p + ic + Theme.S(46), Width - p * 2, Theme.S(18)), TextFormatFlags.EndEllipsis);
        }
    }

    // Ringdiagramm mit Legende (Ursachen der Störungen).
    class Donut : Surface
    {
        public readonly List<Tuple<string, double, Color>> Segments = new List<Tuple<string, double, Color>>();
        public string Title = L.P("Ursachen", "Causes");

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            int p = Theme.S(16);
            int size = Height - p * 2;
            var ring = new RectangleF(p + Theme.S(6), p + Theme.S(6), size - Theme.S(12), size - Theme.S(12));
            float thick = Theme.S(13);
            double total = 0;
            foreach (var s in Segments) total += s.Item2;
            using (var pen = new Pen(Theme.Surface2, thick)) g.DrawEllipse(pen, ring);
            if (total > 0)
            {
                float start = -90;
                foreach (var s in Segments)
                {
                    if (s.Item2 <= 0) continue;
                    float sweep = (float)(360 * s.Item2 / total);
                    using (var pen = new Pen(s.Item3, thick)) g.DrawArc(pen, ring, start + 1.5f, Math.Max(0.5f, sweep - 3f));
                    start += sweep;
                }
            }
            var center = Rectangle.Round(ring);
            Theme.DrawText(g, total > 0 ? total.ToString("0") : "0", Theme.D(17f), Theme.Text,
                new Rectangle(center.X, center.Y + center.Height / 2 - Theme.S(20), center.Width, Theme.S(26)), TextFormatFlags.HorizontalCenter);
            Theme.DrawText(g, L.P("Störungen", "incidents"), Theme.F(7.5f), Theme.Faint,
                new Rectangle(center.X, center.Y + center.Height / 2 + Theme.S(4), center.Width, Theme.S(14)), TextFormatFlags.HorizontalCenter);

            int lx = p + size + Theme.S(10), ly = p;
            Theme.DrawText(g, Title, Theme.F(9f, FontStyle.Bold), Theme.Muted, lx, ly);
            ly += Theme.S(24);
            foreach (var s in Segments)
            {
                using (var b = new SolidBrush(s.Item3)) g.FillEllipse(b, lx, ly + Theme.S(4), Theme.S(9), Theme.S(9));
                string pct = total > 0 ? (100 * s.Item2 / total).ToString("0") + " %" : "–";
                Theme.DrawText(g, s.Item1, Theme.F(8.5f), s.Item2 > 0 ? Theme.Text : Theme.Faint,
                    new Rectangle(lx + Theme.S(16), ly, Width - lx - Theme.S(16) - p - Theme.S(40), Theme.S(18)), TextFormatFlags.EndEllipsis);
                Theme.DrawText(g, pct, Theme.F(8.5f, FontStyle.Bold), s.Item2 > 0 ? s.Item3 : Theme.Faint,
                    new Rectangle(Width - p - Theme.S(44), ly, Theme.S(44), Theme.S(18)), TextFormatFlags.Right);
                ly += Theme.S(21);
            }
        }
    }

    // ---------- Zeitdiagramm (Ping-Linien mit Farbverlauf + Verlust-Spuren) ----------

    public struct ChartPoint
    {
        public long T;
        public float Y;     // NaN = kein Messwert
        public float Loss;  // 0..1
    }

    public class ChartSeries
    {
        public string Name;
        public Color Color;
        public bool Visible = true;
        public readonly List<ChartPoint> Points = new List<ChartPoint>();
    }

    class TimeChart : Control
    {
        public readonly List<ChartSeries> Series = new List<ChartSeries>();
        public long MinT, MaxT;
        public long IntervalTicks = TimeSpan.TicksPerSecond;
        public string EmptyText = L.P("Noch keine Messdaten – oben auf „Start“ klicken", "No data yet – click “Start” above");
        public string LossTitle = L.P("Paketverlust je Ziel", "Packet loss per target");
        Point mouse;
        bool hover;
        readonly List<KeyValuePair<Rectangle, ChartSeries>> legendHits = new List<KeyValuePair<Rectangle, ChartSeries>>();

        class Agg { public double[] Sum; public int[] Cnt, Pts; public float[] Loss; }

        public TimeChart()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
            Font = Theme.F(8.5f);
        }

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
            if (count < 2) count = 2;
            foreach (var s in secs)
            {
                long st = s * TimeSpan.TicksPerSecond;
                if (range / st <= count) return st;
            }
            return 604800 * TimeSpan.TicksPerSecond;
        }

        protected override void OnMouseMove(MouseEventArgs e) { mouse = e.Location; hover = true; Invalidate(); base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            foreach (var kv in legendHits)
                if (kv.Key.Contains(e.Location)) { kv.Value.Visible = !kv.Value.Visible; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var active = new List<ChartSeries>();
            foreach (var s in Series) if (s.Points.Count > 0) active.Add(s);

            // Legende (klickbar: Linie ein-/ausblenden)
            legendHits.Clear();
            int lx = Theme.S(2);
            foreach (var s in active)
            {
                var sz = Theme.Measure(s.Name, Theme.F(9f));
                var chip = new Rectangle(lx, 0, sz.Width + Theme.S(32), Theme.S(26));
                Theme.FillRound(g, s.Visible ? Theme.A(s.Color, 30) : Theme.Surface2, chip, Theme.S(13));
                using (var path = Theme.Round(new RectangleF(chip.X + 0.5f, chip.Y + 0.5f, chip.Width - 1, chip.Height - 1), Theme.S(13)))
                using (var pen = new Pen(Theme.A(s.Color, s.Visible ? 110 : 35))) g.DrawPath(pen, path);
                using (var b = new SolidBrush(s.Visible ? s.Color : Theme.Faint))
                    g.FillEllipse(b, chip.X + Theme.S(11), chip.Y + chip.Height / 2 - Theme.S(4), Theme.S(8), Theme.S(8));
                Theme.DrawText(g, s.Name, Theme.F(9f), s.Visible ? Theme.Text : Theme.Faint,
                    new Rectangle(chip.X + Theme.S(24), chip.Y, sz.Width + 4, chip.Height), TextFormatFlags.VerticalCenter);
                legendHits.Add(new KeyValuePair<Rectangle, ChartSeries>(chip, s));
                lx += chip.Width + Theme.S(8);
            }

            int laneH = Theme.S(9), laneGap = Theme.S(6);
            int lanesH = active.Count > 0 ? Theme.S(26) + active.Count * (laneH + laneGap) : 0;
            int axisH = Theme.S(22);
            int left = Theme.S(48);
            var plot = new Rectangle(left, Theme.S(42), Width - left - Theme.S(14), Height - Theme.S(42) - lanesH - axisH - Theme.S(4));
            if (plot.Width < 40 || plot.Height < 30) return;

            if (active.Count == 0 || MaxT <= MinT)
            {
                using (var pen = new Pen(Theme.Grid) { DashStyle = DashStyle.Dot })
                    for (int k = 0; k <= 4; k++) { int y = plot.Bottom - k * plot.Height / 4; g.DrawLine(pen, plot.Left, y, plot.Right, y); }
                Theme.DrawText(g, EmptyText, Theme.F(10.5f), Theme.Faint, plot, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            int cols = plot.Width;
            long range = MaxT - MinT;
            var aggs = new Agg[active.Count];
            double max = 0;
            for (int i = 0; i < active.Count; i++)
            {
                aggs[i] = Aggregate(active[i], cols);
                if (!active[i].Visible) continue;
                for (int c = 0; c < cols; c++) if (aggs[i].Cnt[c] > 0) max = Math.Max(max, aggs[i].Sum[c] / aggs[i].Cnt[c]);
            }
            double ymax = NiceCeil(Math.Max(10, max * 1.15));

            // Raster + Y-Beschriftung
            using (var pen = new Pen(Theme.Grid) { DashStyle = DashStyle.Dot })
                for (int k = 0; k <= 4; k++)
                {
                    int y = plot.Bottom - k * plot.Height / 4;
                    g.DrawLine(pen, plot.Left, y, plot.Right, y);
                    string label = (ymax * k / 4).ToString("0") + (k == 4 ? " ms" : "");
                    Theme.DrawText(g, label, Font, Theme.Faint, new Rectangle(0, y - Theme.S(8), plot.Left - Theme.S(8), Theme.S(16)), TextFormatFlags.Right);
                }

            // Zeitachse
            long step = NiceStep(range, Math.Max(2, plot.Width / Theme.S(110)));
            string fmt = step < TimeSpan.TicksPerMinute ? "HH:mm:ss" : range > TimeSpan.TicksPerDay ? L.DateTimeShort : "HH:mm";
            int axisY = Height - axisH + Theme.S(4);
            using (var pen = new Pen(Theme.A(Theme.Grid, 140)) { DashStyle = DashStyle.Dot })
                for (long t = (MinT + step - 1) / step * step; t <= MaxT; t += step)
                {
                    int x = plot.Left + (int)((t - MinT) * (cols - 1) / range);
                    g.DrawLine(pen, x, plot.Top, x, plot.Bottom);
                    Theme.DrawText(g, new DateTime(t).ToString(fmt), Font, Theme.Faint,
                        new Rectangle(x - Theme.S(50), axisY, Theme.S(100), Theme.S(16)), TextFormatFlags.HorizontalCenter);
                }

            // Linien mit Farbverlauf
            int gapCols = Math.Max(3, (int)(IntervalTicks * 3.0 * cols / range));
            g.SetClip(new Rectangle(plot.Left, plot.Top - Theme.S(4), plot.Width + 1, plot.Height + Theme.S(5)));
            for (int i = 0; i < active.Count; i++)
            {
                if (!active[i].Visible) continue;
                var a = aggs[i];
                var color = active[i].Color;
                var seg = new List<PointF>();
                int lastCol = -1000;
                for (int c = 0; c <= cols; c++)
                {
                    bool end = c == cols;
                    if (!end && a.Pts[c] > 0 && a.Cnt[c] == 0) { DrawSegment(g, seg, plot, color); seg.Clear(); lastCol = -1000; continue; }
                    if (end || a.Cnt[c] == 0) { if (end) DrawSegment(g, seg, plot, color); continue; }
                    if (c - lastCol > gapCols && seg.Count > 0) { DrawSegment(g, seg, plot, color); seg.Clear(); }
                    float y = (float)(plot.Bottom - (a.Sum[c] / a.Cnt[c]) / ymax * plot.Height);
                    seg.Add(new PointF(plot.Left + c, y));
                    lastCol = c;
                }
            }
            g.ResetClip();

            // Verlust-Spuren
            int laneTop = plot.Bottom + Theme.S(12);
            Theme.DrawText(g, LossTitle, Theme.F(8.5f, FontStyle.Bold), Theme.Muted, plot.Left, laneTop);
            laneTop += Theme.S(20);
            for (int i = 0; i < active.Count; i++)
            {
                int y = laneTop + i * (laneH + laneGap);
                var lane = new RectangleF(plot.Left, y, plot.Width, laneH);
                Theme.FillRound(g, Theme.Surface2, lane, laneH / 2f);
                using (var b = new SolidBrush(active[i].Visible ? active[i].Color : Theme.Faint))
                    g.FillEllipse(b, plot.Left - Theme.S(16), y + laneH / 2f - Theme.S(4), Theme.S(8), Theme.S(8));
                var a = aggs[i];
                int c0 = -1; float worst = 0;
                for (int c = 0; c <= cols; c++)
                {
                    bool lossHere = c < cols && a.Loss[c] > 0;
                    if (lossHere) { if (c0 < 0) { c0 = c; worst = 0; } worst = Math.Max(worst, a.Loss[c]); continue; }
                    if (c0 >= 0)
                    {
                        float w = Math.Max(Theme.S(4), c - c0);
                        Theme.FillRound(g, Theme.A(Theme.Bad, (int)(120 + 135 * worst)), new RectangleF(plot.Left + c0, y, w, laneH), laneH / 2f);
                        c0 = -1;
                    }
                }
            }
            int lanesBottom = laneTop + active.Count * (laneH + laneGap);

            // Fadenkreuz + Tooltip
            if (hover && mouse.X >= plot.Left && mouse.X < plot.Right && mouse.Y >= plot.Top && mouse.Y <= lanesBottom)
            {
                int c = mouse.X - plot.Left;
                using (var pen = new Pen(Theme.A(Theme.Text, 60))) g.DrawLine(pen, mouse.X, plot.Top, mouse.X, lanesBottom);
                long t = MinT + (long)(c * (double)range / (cols - 1));
                var lines = new List<Tuple<Color, string>>();
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
                            float py = (float)(plot.Bottom - avg / ymax * plot.Height);
                            using (var b = new SolidBrush(active[i].Color)) g.FillEllipse(b, plot.Left + best - Theme.S(4), py - Theme.S(4), Theme.S(8), Theme.S(8));
                            using (var pen = new Pen(Theme.Surface, Theme.S(2))) g.DrawEllipse(pen, plot.Left + best - Theme.S(4), py - Theme.S(4), Theme.S(8), Theme.S(8));
                        }
                        else if (a.Loss[best] > 0) v = L.P("Paket verloren", "packet lost");
                    }
                    lines.Add(Tuple.Create(active[i].Color, active[i].Name + ":  " + v));
                }
                string head = new DateTime(t).ToString(range > TimeSpan.TicksPerDay ? L.DateTimeShort + ":ss" : "HH:mm:ss");
                int tw = Theme.Measure(head, Theme.F(9f, FontStyle.Bold)).Width;
                foreach (var l in lines) tw = Math.Max(tw, Theme.Measure(l.Item2, Theme.F(9f)).Width + Theme.S(18));
                int bw = tw + Theme.S(24), bh = Theme.S(34) + lines.Count * Theme.S(20);
                int bx = mouse.X + Theme.S(16);
                if (bx + bw > Width - Theme.S(4)) bx = mouse.X - bw - Theme.S(16);
                var box = new Rectangle(bx, plot.Top + Theme.S(6), bw, bh);
                Theme.FillRound(g, Theme.A(Theme.Bg, 235), box, Theme.S(10));
                using (var path = Theme.Round(new RectangleF(box.X + 0.5f, box.Y + 0.5f, box.Width - 1, box.Height - 1), Theme.S(10)))
                using (var pen = new Pen(Theme.Border)) g.DrawPath(pen, path);
                Theme.DrawText(g, head, Theme.F(9f, FontStyle.Bold), Theme.Text, box.X + Theme.S(12), box.Y + Theme.S(9));
                int ty = box.Y + Theme.S(31);
                foreach (var l in lines)
                {
                    using (var b = new SolidBrush(l.Item1)) g.FillEllipse(b, box.X + Theme.S(12), ty + Theme.S(4), Theme.S(8), Theme.S(8));
                    Theme.DrawText(g, l.Item2, Theme.F(9f), Theme.Text, box.X + Theme.S(28), ty);
                    ty += Theme.S(20);
                }
            }
        }

        static void DrawSegment(Graphics g, List<PointF> seg, Rectangle plot, Color color)
        {
            if (seg.Count == 0) return;
            if (seg.Count == 1)
            {
                using (var b = new SolidBrush(color)) g.FillEllipse(b, seg[0].X - Theme.S(2.5f), seg[0].Y - Theme.S(2.5f), Theme.S(5), Theme.S(5));
                return;
            }
            using (var path = new GraphicsPath())
            {
                path.AddLines(seg.ToArray());
                path.AddLine(seg[seg.Count - 1].X, seg[seg.Count - 1].Y, seg[seg.Count - 1].X, plot.Bottom);
                path.AddLine(seg[seg.Count - 1].X, plot.Bottom, seg[0].X, plot.Bottom);
                path.CloseFigure();
                using (var b = new LinearGradientBrush(new Rectangle(plot.Left, plot.Top - 1, plot.Width, plot.Height + 2),
                    Theme.A(color, 70), Theme.A(color, 0), 90f))
                    g.FillPath(b, path);
            }
            using (var pen = new Pen(color, Theme.S(2f)) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLines(pen, seg.ToArray());
        }
    }

    // ---------- Tagesübersicht der Paketverluste ----------

    class DayRow
    {
        public DateTime Day;
        public readonly List<KeyValuePair<DateTime, DateTime>> Coverage = new List<KeyValuePair<DateTime, DateTime>>();
        public readonly List<LossIncident> Incidents = new List<LossIncident>();
    }

    class DayTimeline : Control
    {
        public List<DayRow> Rows = new List<DayRow>();
        public DateTime SelStart = DateTime.MinValue, SelEnd = DateTime.MinValue;
        public event Action<LossIncident> IncidentClicked;
        readonly List<KeyValuePair<RectangleF, LossIncident>> hits = new List<KeyValuePair<RectangleF, LossIncident>>();
        Point mouse;
        bool hover;

        public DayTimeline()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
            Font = Theme.F(8.5f);
        }

        int RowH { get { return Theme.S(30); } }
        int HeadH { get { return Theme.S(24); } }
        int LabelW { get { return Theme.S(92); } }

        public int PreferredHeight { get { return HeadH + Math.Max(1, Rows.Count) * RowH + Theme.S(6); } }

        protected override void OnMouseMove(MouseEventArgs e) { mouse = e.Location; hover = true; Invalidate(); base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            var inc = HitTest(e.Location);
            if (inc != null && IncidentClicked != null) IncidentClicked(inc);
        }

        LossIncident HitTest(Point p)
        {
            foreach (var kv in hits)
            {
                var r = kv.Key;
                r.Inflate(Theme.S(3), 0);
                if (r.Contains(p)) return kv.Value;
            }
            return null;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            hits.Clear();
            if (Rows.Count == 0)
            {
                Theme.DrawText(g, L.P("Keine Messungen im gewählten Zeitraum", "No measurements in the selected period"), Theme.F(10f), Theme.Faint, ClientRectangle,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            int x0 = LabelW, w = Width - x0 - Theme.S(10);
            if (w < 50) return;
            // Stundenraster
            for (int h = 0; h <= 24; h += 3)
            {
                int x = x0 + w * h / 24;
                Theme.DrawText(g, h.ToString("00") + ":00", Font, Theme.Faint, new Rectangle(x - Theme.S(30), 0, Theme.S(60), HeadH), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                using (var pen = new Pen(Theme.A(Theme.Grid, 160)) { DashStyle = DashStyle.Dot })
                    g.DrawLine(pen, x, HeadH, x, HeadH + Rows.Count * RowH);
            }
            for (int i = 0; i < Rows.Count; i++)
            {
                var row = Rows[i];
                int y = HeadH + i * RowH;
                Theme.DrawText(g, Theme.DayName(row.Day), Theme.F(9f, FontStyle.Bold), row.Day == DateTime.Today ? Theme.Text : Theme.Muted,
                    new Rectangle(Theme.S(4), y, x0 - Theme.S(8), RowH), TextFormatFlags.VerticalCenter);
                var track = new RectangleF(x0, y + Theme.S(7), w, RowH - Theme.S(14));
                Theme.FillRound(g, Theme.Surface2, track, Theme.S(5));
                foreach (var cv in row.Coverage)
                {
                    float a = (float)(cv.Key - row.Day).TotalHours / 24f, b = (float)(cv.Value - row.Day).TotalHours / 24f;
                    var r = new RectangleF(x0 + w * a, track.Y, Math.Max(Theme.S(2), w * (b - a)), track.Height);
                    Theme.FillRound(g, Theme.A(Theme.Accent, 95), r, Theme.S(4));
                }
                foreach (var inc in row.Incidents)
                {
                    float a = (float)(inc.Start - row.Day).TotalHours / 24f;
                    float iw = Math.Max(Theme.S(4), w * (float)inc.Duration.TotalHours / 24f);
                    var r = new RectangleF(x0 + w * a - iw / 2 + Theme.S(2), track.Y - Theme.S(3), iw, track.Height + Theme.S(6));
                    var col = Theme.CauseColor(inc.Cause);
                    Theme.FillRound(g, col, r, Theme.S(2));
                    bool sel = inc.Start >= SelStart && inc.Start <= SelEnd;
                    if (sel)
                        using (var path = Theme.Round(RectangleF.Inflate(r, Theme.S(2), Theme.S(2)), Theme.S(3)))
                        using (var pen = new Pen(Color.White, Theme.S(1.5f))) g.DrawPath(pen, path);
                    hits.Add(new KeyValuePair<RectangleF, LossIncident>(r, inc));
                }
            }

            if (hover)
            {
                var inc = HitTest(mouse);
                if (inc == null) return;
                var lines = new[]
                {
                    inc.Start.ToString(L.Date + "  HH:mm:ss") + " – " + inc.End.ToString("HH:mm:ss"),
                    L.P("Dauer ", "Duration ") + Theme.ShortDuration(inc.Duration) + "  ·  " + inc.TotalLost + L.P(" Pakete verloren", " packets lost"),
                    inc.CauseText
                };
                int tw = 0;
                foreach (var l in lines) tw = Math.Max(tw, Theme.Measure(l, Theme.F(9f, FontStyle.Bold)).Width);
                int bw = tw + Theme.S(24), bh = Theme.S(72);
                int bx = Math.Min(mouse.X + Theme.S(14), Width - bw - Theme.S(4));
                int by = mouse.Y + Theme.S(16);
                if (by + bh > Height) by = mouse.Y - bh - Theme.S(8);
                var box = new Rectangle(bx, Math.Max(0, by), bw, bh);
                Theme.FillRound(g, Theme.A(Theme.Bg, 240), box, Theme.S(10));
                using (var path = Theme.Round(new RectangleF(box.X + 0.5f, box.Y + 0.5f, box.Width - 1, box.Height - 1), Theme.S(10)))
                using (var pen = new Pen(Theme.CauseColor(inc.Cause))) g.DrawPath(pen, path);
                Theme.DrawText(g, lines[0], Theme.F(9f, FontStyle.Bold), Theme.Text, box.X + Theme.S(12), box.Y + Theme.S(9));
                Theme.DrawText(g, lines[1], Theme.F(9f), Theme.Muted, box.X + Theme.S(12), box.Y + Theme.S(29));
                Theme.DrawText(g, lines[2], Theme.F(9f, FontStyle.Bold), Theme.CauseColor(inc.Cause), box.X + Theme.S(12), box.Y + Theme.S(49));
            }
        }
    }
}
