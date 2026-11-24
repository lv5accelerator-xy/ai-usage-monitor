using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CodexUsage {
    public static class QuotaFormat {
        public static string Percent(double value) {
            if (value < 0 || Double.IsNaN(value) || Double.IsInfinity(value)) return "—";
            if (value > 0 && value < 1) return "<1%";
            return Math.Round(Math.Min(100, value), 0, MidpointRounding.AwayFromZero).ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";
        }
    }
    // Figures use Segoe UI Semibold (tabular digits by default), the closest Windows
    // counterpart to SF Pro's numerals; older systems fall back to Segoe UI or Tahoma.
    public static class NumberFont {
        static string family;
        public static string Family {
            get {
                if (family != null) return family;
                foreach (string name in new[] { "Segoe UI Semibold", "Segoe UI", "Tahoma" }) {
                    try { using (var f = new FontFamily(name)) { family = f.Name; return family; } } catch (ArgumentException) { }
                }
                family = FontFamily.GenericSansSerif.Name;
                return family;
            }
        }
        // The Semibold family already carries the weight; synthetic bold on top looks heavy.
        public static FontStyle Style(bool bold) {
            if (Family.EndsWith("Semibold", StringComparison.OrdinalIgnoreCase)) return FontStyle.Regular;
            return bold ? FontStyle.Bold : FontStyle.Regular;
        }
    }

    // Native window chrome helpers. Real DWM blur-behind is deliberately not used: WinForms
    // paints labels and backgrounds with GDI, which leaves the alpha channel at 0, so blur
    // turned those pixels see-through (white cards, haloed text; seen in v2.2.0). The glass
    // look comes from a uniformly translucent layered window instead. Calls are best-effort.
    public static class Glass {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        static void Attribute(IntPtr handle, int attribute, int value) {
            try { DwmSetWindowAttribute(handle, attribute, ref value, 4); } catch (Exception) { }
        }

        // Dark title bar whose caption blends into the window background (Windows 11).
        public static void DarkChrome(IntPtr handle, Color caption, Color border) {
            if (handle == IntPtr.Zero) return;
            Attribute(handle, 20, 1); // DWMWA_USE_IMMERSIVE_DARK_MODE
            Attribute(handle, 19, 1); // same attribute on Windows 10 1809-1909
            Attribute(handle, 35, ColorTranslator.ToWin32(caption)); // DWMWA_CAPTION_COLOR
            Attribute(handle, 34, ColorTranslator.ToWin32(border)); // DWMWA_BORDER_COLOR
        }

        // Windows 11 rounds the window (and its shadow) itself; earlier systems ignore this.
        public static void RoundCorners(IntPtr handle, bool small) {
            if (handle != IntPtr.Zero) Attribute(handle, 33, small ? 3 : 2); // DWMWA_WINDOW_CORNER_PREFERENCE
        }

        public static void DarkScrollBar(Control control) {
            try { if (control != null && control.IsHandleCreated) SetWindowTheme(control.Handle, "DarkMode_Explorer", null); } catch (Exception) { }
        }
    }
    public static class NativeDisplay {
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        public static void EnableDpi() {
            try { if (SetThreadDpiAwarenessContext(new IntPtr(-4)) != IntPtr.Zero) return; } catch (EntryPointNotFoundException) { }
            try { SetProcessDPIAware(); } catch { }
        }
    }

    public static class Shapes {
        public static GraphicsPath Round(RectangleF bounds, float radius) {
            var path = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            if (d <= 0) return path;
            path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    // Glass details shared by cards and the floating monitor.
    public static class Hud {
        // Hairline outline plus a specular highlight along the top edge, the way light
        // catches the rim of a pane of frosted glass. Drawn over an opaque fill.
        public static void GlassEdge(Graphics g, RectangleF bounds, float radius, Color border, Color highlight, float unit) {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Shapes.Round(bounds, radius))
            using (var pen = new Pen(border, Math.Max(1f, unit * 0.75f))) g.DrawPath(pen, path);
            if (highlight.IsEmpty || highlight.A == 0 || bounds.Width < radius * 2 + 8) return;
            float y = bounds.Top + Math.Max(1f, unit);
            var line = new RectangleF(bounds.Left + radius * 0.6f, y, bounds.Width - radius * 1.2f, Math.Max(1f, unit * 0.75f));
            using (var brush = new LinearGradientBrush(new RectangleF(line.Left - 1, line.Top, line.Width + 2, line.Height + 1), Color.Transparent, Color.Transparent, LinearGradientMode.Horizontal)) {
                var blend = new ColorBlend {
                    Colors = new[] { Dim(highlight, 0), highlight, highlight, Dim(highlight, 0) },
                    Positions = new[] { 0f, 0.25f, 0.75f, 1f }
                };
                brush.InterpolationColors = blend;
                g.FillRectangle(brush, line);
            }
        }
        public static Color Dim(Color color, int alpha) { return Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), color); }
        public static Color Mix(Color a, Color b, double t) {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb((int)Math.Round(a.R + (b.R - a.R) * t), (int)Math.Round(a.G + (b.G - a.G) * t), (int)Math.Round(a.B + (b.B - a.B) * t));
        }
    }

    public class Surface : Panel {
        public int Radius { get; set; }
        public Color BorderColor { get; set; }
        public Color AccentColor { get; set; }
        public float HudUnit { get; set; }
        public Surface() {
            HudUnit = 1f;
            DoubleBuffered = true;
            ResizeRedraw = true;
            Radius = 12;
            BorderColor = Color.FromArgb(58, 58, 62);
            HighlightColor = Color.FromArgb(70, 255, 255, 255);
            AccentColor = Color.Empty;
        }
        public Color HighlightColor { get; set; }
        protected override void OnPaintBackground(PaintEventArgs e) {
            // Corners outside the rounded region belong to the parent surface.
            if (Parent != null) using (var back = new SolidBrush(Parent.BackColor)) e.Graphics.FillRectangle(back, ClientRectangle);
            if (Width < 2 || Height < 2) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Shapes.Round(new RectangleF(0, 0, Width, Height), Radius))
            using (var fill = new SolidBrush(BackColor)) e.Graphics.FillPath(fill, path);
        }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            if (Width < 2 || Height < 2) return;
            float unit = Math.Max(0.5f, HudUnit);
            Hud.GlassEdge(e.Graphics, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), Radius, BorderColor, HighlightColor, unit);
        }
        protected override void OnSizeChanged(EventArgs e) {
            base.OnSizeChanged(e);
            if (Width < 2 || Height < 2) return;
            using (var path = Shapes.Round(new RectangleF(0, 0, Width, Height), Radius)) {
                var old = Region;
                Region = new Region(path);
                if (old != null) old.Dispose();
            }
        }
    }

    public class FloatingForm : DpiForm {
        int radius = 16;
        public int Radius { get { return radius; } set { radius = Math.Max(1, value); UpdateRegion(); Invalidate(); } }
        public Color BorderColor { get; set; }
        public Color AccentColor { get; set; }
        public float HudUnit { get; set; }
        bool glassEnabled;
        public Color HighlightColor { get; set; }
        public FloatingForm() { DoubleBuffered = true; Radius = 16; HudUnit = 1f; BorderColor = Color.FromArgb(74, 74, 80); HighlightColor = Color.FromArgb(90, 255, 255, 255); AccentColor = Color.Empty; }
        // Whether the owner shows this window translucent (see TrayApp.UpdateMonitorOpacity).
        public bool GlassEnabled { get { return glassEnabled; } set { glassEnabled = value; } }
        protected override void CreateHandle() {
            // Windows expands a new top-level window to its minimum tracking
            // width. Restore the borderless client size before its first frame.
            Size requestedSize = ClientSize;
            base.CreateHandle();
            if (FormBorderStyle == FormBorderStyle.None && ClientSize != requestedSize)
                ClientSize = requestedSize;
        }
        protected override void OnSizeChanged(EventArgs e) {
            base.OnSizeChanged(e);
            UpdateRegion();
        }
        void UpdateRegion() {
            if (Width < 2 || Height < 2) return;
            using (var path = Shapes.Round(new RectangleF(0, 0, Width, Height), Radius)) {
                var old = Region;
                Region = new Region(path);
                if (old != null) old.Dispose();
            }
        }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            Hud.GlassEdge(e.Graphics, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), Radius, BorderColor, HighlightColor, Math.Max(0.5f, HudUnit));
        }
    }

    public class QuotaRing : Control {
        public double Value { get; set; }
        public string Caption { get; set; }
        public bool Stale { get; set; }
        public Color FillColor { get; set; }
        public Color TrackColor { get; set; }
        public Color CaptionColor { get; set; }
        public string ValueText { get { return QuotaFormat.Percent(Value) + (Stale ? "!" : ""); } }
        public QuotaRing() {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Value = -1;
            Caption = "";
            FillColor = Color.FromArgb(64, 156, 255);
            TrackColor = Color.FromArgb(58, 58, 62);
            CaptionColor = Color.Empty;
        }
        // Activity-ring style: a translucent full track with a smooth round-capped arc.
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            float scale = Width / 52f;
            float diameter = 38 * scale;
            float stroke = 4.5f * scale;
            var ring = new RectangleF((Width - diameter) / 2, 5 * scale, diameter, diameter);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            bool known = Value >= 0 && !Double.IsNaN(Value) && !Double.IsInfinity(Value);
            Color fill = Stale ? Hud.Dim(FillColor, 150) : FillColor;
            using (var track = new Pen(TrackColor, stroke)) e.Graphics.DrawEllipse(track, ring);
            if (known && Value > 0) {
                float sweep = (float)(Math.Min(100, Value) * 3.6);
                using (var pen = new Pen(fill, stroke)) {
                    pen.StartCap = pen.EndCap = LineCap.Round;
                    if (sweep >= 359.5f) e.Graphics.DrawEllipse(pen, ring);
                    else e.Graphics.DrawArc(pen, ring, -90, Math.Max(0.5f, sweep));
                }
            }
            string valueText = ValueText;
            // Bounds already contain the monitor scale. Pixel fonts avoid
            // applying the graphics device's DPI a second time to point fonts.
            using (var valueFont = new Font(NumberFont.Family, 10.5f * scale, NumberFont.Style(true), GraphicsUnit.Pixel))
            using (var captionFont = new Font("Microsoft YaHei UI", (28f / 3) * scale, FontStyle.Regular, GraphicsUnit.Pixel)) {
                TextRenderer.DrawText(e.Graphics, valueText, valueFont, Rectangle.Round(ring), ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                string[] lines = (Caption ?? "").Split(new char[] { '\n' }, 2);
                var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
                for (int i = 0; i < lines.Length; i++)
                    TextRenderer.DrawText(e.Graphics, lines[i], captionFont,
                        new Rectangle(0, (int)((46 + 16 * i) * scale), Width, (int)(16 * scale)), CaptionColor.IsEmpty ? ForeColor : CaptionColor, flags);
            }
        }
    }

    public class QuotaBar : Control {
        public double Value { get; set; }
        public Color FillColor { get; set; }
        public Color TrackColor { get; set; }
        public QuotaBar() {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FillColor = Color.FromArgb(64, 156, 255);
            TrackColor = Color.FromArgb(58, 58, 62);
        }
        // Capsule meter: rounded track with a rounded fill and a soft top sheen.
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            if (Width < 2 || Height < 2) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float h = Height, r = h / 2f;
            using (var back = new SolidBrush(Parent != null ? Parent.BackColor : BackColor)) e.Graphics.FillRectangle(back, ClientRectangle);
            using (var trackPath = Shapes.Round(new RectangleF(0, 0, Width, h), r))
            using (var track = new SolidBrush(TrackColor)) e.Graphics.FillPath(track, trackPath);
            double value = Math.Max(0, Math.Min(100, Value));
            if (value <= 0) return;
            // Keep a visible pill even for tiny values.
            float w = Math.Max(h, (float)(Width * value / 100.0));
            var lit = new RectangleF(0, 0, w, h);
            using (var path = Shapes.Round(lit, r)) {
                using (var fill = new LinearGradientBrush(new RectangleF(0, 0, Math.Max(2, w), h), Hud.Mix(FillColor, Color.White, 0.18), FillColor, LinearGradientMode.Horizontal))
                    e.Graphics.FillPath(fill, path);
                if (h >= 4) using (var shine = new LinearGradientBrush(new RectangleF(0, 0, w, h + 1), Color.FromArgb(70, Color.White), Color.FromArgb(0, Color.White), LinearGradientMode.Vertical))
                    e.Graphics.FillPath(shine, path);
            }
        }
    }

    // Native window integration shared by script and packaged UI.
    public static class DesktopIntegration {
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr handle, out RECT rect);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr handle);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr handle, System.Text.StringBuilder name, int count);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr handle, out uint pid);
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        public static bool IsFullScreen(Rectangle window, Rectangle monitor) {
            return window.Left <= monitor.Left && window.Top <= monitor.Top && window.Right >= monitor.Right && window.Bottom >= monitor.Bottom;
        }
        public static bool FullScreenOn(Form form) {
            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero || foreground == GetShellWindow() || IsIconic(foreground)) return false;
            uint pid; GetWindowThreadProcessId(foreground, out pid);
            if (pid == System.Diagnostics.Process.GetCurrentProcess().Id) return false;
            var name = new System.Text.StringBuilder(256); GetClassName(foreground, name, 256);
            if (name.ToString() == "Progman" || name.ToString() == "WorkerW") return false;
            RECT rect; if (!GetWindowRect(foreground, out rect)) return false;
            return IsFullScreen(Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom), Screen.FromControl(form).Bounds);
        }
        public static Rectangle Clamp(Rectangle window, Rectangle area) {
            int width = Math.Min(window.Width, area.Width), height = Math.Min(window.Height, area.Height);
            return new Rectangle(Math.Max(area.Left, Math.Min(window.X, area.Right - width)), Math.Max(area.Top, Math.Min(window.Y, area.Bottom - height)), width, height);
        }
        public static void KeepVisible(Form form) {
            var screens = Screen.AllScreens;
            var areas = new Rectangle[screens.Length];
            for (int i = 0; i < screens.Length; i++) areas[i] = screens[i].WorkingArea;
            var bounds = RestorePlacement(form.Bounds, areas);
            if (form.Bounds != bounds) form.Bounds = bounds;
        }
        public static Rectangle RestorePlacement(Rectangle window, Rectangle[] areas) {
            if (areas == null || areas.Length == 0) return window;
            Rectangle best = areas[0];
            long bestOverlap = -1;
            double bestDistance = Double.MaxValue;
            foreach (Rectangle area in areas) {
                if (area.Width <= 0 || area.Height <= 0) continue;
                Rectangle overlap = Rectangle.Intersect(window, area);
                long overlapSize = (long)Math.Max(0, overlap.Width) * Math.Max(0, overlap.Height);
                double cx = window.X + window.Width / 2.0, cy = window.Y + window.Height / 2.0;
                double dx = Math.Max(area.Left - cx, Math.Max(0, cx - area.Right));
                double dy = Math.Max(area.Top - cy, Math.Max(0, cy - area.Bottom));
                double distance = dx * dx + dy * dy;
                if (overlapSize > bestOverlap || (overlapSize == bestOverlap && distance < bestDistance)) {
                    best = area; bestOverlap = overlapSize; bestDistance = distance;
                }
            }
            return Clamp(window, best);
        }
        public static string StartupCommand(string exe) {
            if (exe.IndexOf('"') >= 0 || !System.IO.Path.IsPathRooted(exe)) throw new ArgumentException("Invalid executable path.");
            return "\"" + exe + "\"";
        }
        public static bool IsStartupEnabled(string exe) {
            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                return key != null && string.Equals(key.GetValue("CodexUsage") as string, StartupCommand(exe), StringComparison.OrdinalIgnoreCase);
        }
        public static void SetStartup(string exe, bool enabled) {
            using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) {
                if (enabled) key.SetValue("CodexUsage", StartupCommand(exe));
                else key.DeleteValue("CodexUsage", false);
            }
        }
    }

    public class DpiForm : Form {
        public int DisplayDpi { get; set; }
        public event EventHandler ScaleChanged;
        public event EventHandler DisplayEnvironmentChanged;
        public bool LastChangeWasResume { get; private set; }
        public DpiForm() { DisplayDpi = 96; AutoScaleMode = AutoScaleMode.None; }
        protected override void OnHandleCreated(EventArgs e) {
            base.OnHandleCreated(e);
            // Every app window is dark; match the title bar and use Windows 11 rounded corners.
            Glass.DarkChrome(Handle, BackColor, Color.FromArgb(58, 58, 62));
            Glass.RoundCorners(Handle, false);
        }
        static Padding ResizePadding(Padding p, float factor) { return new Padding((int)Math.Round(p.Left * factor), (int)Math.Round(p.Top * factor), (int)Math.Round(p.Right * factor), (int)Math.Round(p.Bottom * factor)); }
        readonly System.Collections.Generic.Dictionary<string, Font> dpiFonts = new System.Collections.Generic.Dictionary<string, Font>();
        void ScaleTree(Control control, float factor) {
            control.SuspendLayout();
            try {
                foreach (Control child in control.Controls) ScaleTree(child, factor);
                control.Bounds = new Rectangle((int)Math.Round(control.Left * factor), (int)Math.Round(control.Top * factor), (int)Math.Round(control.Width * factor), (int)Math.Round(control.Height * factor));
                control.Padding = ResizePadding(control.Padding, factor);
                control.Margin = ResizePadding(control.Margin, factor);
                float size = (float)Math.Round(control.Font.Size * factor, 3);
                string key = control.Font.FontFamily.Name + "/" + size.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/" + control.Font.Style + "/" + control.Font.Unit;
                Font font;
                if (!dpiFonts.TryGetValue(key, out font)) { font = new Font(control.Font.FontFamily, size, control.Font.Style, control.Font.Unit); dpiFonts[key] = font; }
                control.Font = font;
                var table = control as TableLayoutPanel;
                if (table != null) {
                    foreach (RowStyle row in table.RowStyles) if (row.SizeType == SizeType.Absolute) row.Height *= factor;
                    foreach (ColumnStyle column in table.ColumnStyles) if (column.SizeType == SizeType.Absolute) column.Width *= factor;
                }
            } finally { control.ResumeLayout(false); }
        }
        public void ApplyDpi(int dpi, Rectangle suggested) {
            if (dpi < 48 || dpi > 768) return;
            float factor = (float)dpi / Math.Max(48, DisplayDpi);
            SuspendLayout();
            try {
                foreach (Control child in Controls) ScaleTree(child, factor);
                Padding = ResizePadding(Padding, factor);
                MinimumSize = new Size((int)Math.Round(MinimumSize.Width * factor), (int)Math.Round(MinimumSize.Height * factor));
                DisplayDpi = dpi;
                Bounds = suggested;
                PerformLayout();
            } finally { ResumeLayout(true); }
            if (ScaleChanged != null) ScaleChanged(this, EventArgs.Empty);
            Invalidate(true);
        }
        public void ApplyZoom(float factor) {
            if (factor <= 0 || Single.IsNaN(factor) || Single.IsInfinity(factor)) throw new ArgumentOutOfRangeException("factor");
            SuspendLayout();
            try {
                foreach (Control child in Controls) ScaleTree(child, factor);
                Padding = ResizePadding(Padding, factor);
            } finally { ResumeLayout(true); }
            Invalidate(true);
        }
        public void NotifyDisplayEnvironmentChanged(bool resumed) {
            LastChangeWasResume = resumed;
            if (DisplayEnvironmentChanged != null) DisplayEnvironmentChanged(this, EventArgs.Empty);
        }
        protected override void Dispose(bool disposing) {
            base.Dispose(disposing);
            if (disposing) { foreach (var font in dpiFonts.Values) font.Dispose(); dpiFonts.Clear(); }
        }
        protected override void WndProc(ref Message message) {
            if (message.Msg == 0x02E0) { // WM_DPICHANGED, per-window suggested physical bounds.
                var rect = (DesktopIntegration.RECT)Marshal.PtrToStructure(message.LParam, typeof(DesktopIntegration.RECT));
                ApplyDpi((int)(message.WParam.ToInt64() & 0xffff), Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom));
                message.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref message);
            if (message.Msg == 0x007E) NotifyDisplayEnvironmentChanged(false); // WM_DISPLAYCHANGE
            if (message.Msg == 0x0218 && (message.WParam.ToInt32() == 0x0007 || message.WParam.ToInt32() == 0x0012))
                NotifyDisplayEnvironmentChanged(true); // WM_POWERBROADCAST: resume
        }
    }

    // Seven-day token trend: bottom-aligned bars, today's bar highlighted with a glow,
    // day captions underneath. Values are relative to the week's maximum.
    public sealed class UsageSparkline : Control {
        double[] values = new double[0];
        string[] labels = new string[0];
        public Color BarColor { get; set; }
        public Color TrackColor { get; set; }
        public Color CaptionColor { get; set; }
        public int HighlightIndex { get; set; }
        public UsageSparkline() {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BarColor = Color.FromArgb(64, 156, 255);
            TrackColor = Color.FromArgb(58, 58, 62);
            CaptionColor = Color.FromArgb(142, 142, 147);
            HighlightIndex = -1;
        }
        public double[] Values { get { return values; } }
        public string[] Labels { get { return labels; } }
        public void SetData(double[] data, string[] captions) {
            values = data ?? new double[0];
            labels = captions ?? new string[0];
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            int count = values.Length;
            if (count == 0 || Width < 8 || Height < 12) return;
            float captionHeight = Math.Max(10f, Height * 0.3f);
            float chartHeight = Height - captionHeight - 2;
            float gap = Math.Max(3f, Width / (count * 2.2f));
            float bar = (Width - gap * (count - 1)) / count;
            double max = 0;
            foreach (double v in values) if (!Double.IsNaN(v) && v > max) max = v;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float radius = Math.Min(bar / 2f, 4f);
            using (var track = new SolidBrush(TrackColor))
            using (var dim = new SolidBrush(Hud.Dim(BarColor, 110)))
            using (var bright = new SolidBrush(BarColor))
            using (var font = new Font("Microsoft YaHei UI", Math.Max(7f, captionHeight * 0.78f), FontStyle.Regular, GraphicsUnit.Pixel)) {
                for (int i = 0; i < count; i++) {
                    float x = i * (bar + gap);
                    using (var path = Shapes.Round(new RectangleF(x, 0, bar, chartHeight), radius)) e.Graphics.FillPath(track, path);
                    double v = values[i];
                    if (max > 0 && v > 0 && !Double.IsNaN(v)) {
                        float h = Math.Min(chartHeight, Math.Max(radius * 2 + 1, (float)(chartHeight * v / max)));
                        bool today = i == HighlightIndex;
                        using (var path = Shapes.Round(new RectangleF(x, chartHeight - h, bar, h), radius)) e.Graphics.FillPath(today ? bright : dim, path);
                    }
                    if (i < labels.Length && !string.IsNullOrEmpty(labels[i]))
                        TextRenderer.DrawText(e.Graphics, labels[i], font, new Rectangle((int)x - 4, (int)(chartHeight + 2), (int)bar + 8, (int)captionHeight),
                            i == HighlightIndex ? BarColor : CaptionColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                }
            }
        }
    }

    // macOS-style dark menu: glass-grey surface, rounded blue selection with white text.
    public sealed class HudColorTable : ProfessionalColorTable {
        readonly Color back, border, select;
        public HudColorTable(Color back, Color border, Color select) { this.back = back; this.border = border; this.select = select; UseSystemColors = false; }
        public override Color ToolStripDropDownBackground { get { return back; } }
        public override Color ImageMarginGradientBegin { get { return back; } }
        public override Color ImageMarginGradientMiddle { get { return back; } }
        public override Color ImageMarginGradientEnd { get { return back; } }
        public override Color MenuBorder { get { return border; } }
        public override Color MenuItemBorder { get { return select; } }
        public override Color MenuItemSelected { get { return select; } }
        public override Color MenuItemSelectedGradientBegin { get { return select; } }
        public override Color MenuItemSelectedGradientEnd { get { return select; } }
        public override Color MenuItemPressedGradientBegin { get { return select; } }
        public override Color MenuItemPressedGradientEnd { get { return select; } }
        public override Color SeparatorDark { get { return border; } }
        public override Color SeparatorLight { get { return back; } }
        public override Color CheckBackground { get { return back; } }
        public override Color CheckSelectedBackground { get { return select; } }
        public override Color CheckPressedBackground { get { return select; } }
    }
    public sealed class HudMenuRenderer : ToolStripProfessionalRenderer {
        readonly Color border, select, text, muted, accent;
        public HudMenuRenderer(Color back, Color border, Color select, Color text, Color muted, Color accent) : base(new HudColorTable(back, border, select)) {
            this.border = border; this.select = select; this.text = text; this.muted = muted; this.accent = accent;
            RoundedEdges = false;
        }
        static bool Highlighted(ToolStripItem item) { return item.Enabled && (item.Selected || item.Pressed); }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) {
            if (!Highlighted(e.Item)) return;
            var r = new RectangleF(4, 1, e.Item.Width - 8, e.Item.Height - 2);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Shapes.Round(r, Math.Min(6f, r.Height / 2f)))
            using (var brush = new SolidBrush(select)) e.Graphics.FillPath(brush, path);
        }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e) {
            int y = e.Item.Height / 2;
            using (var pen = new Pen(border)) e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 10, y);
        }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) {
            if (e.ToolStrip is ToolStripDropDown) {
                using (var pen = new Pen(border)) e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
                return;
            }
            base.OnRenderToolStripBorder(e);
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) {
            e.TextColor = !e.Item.Enabled ? muted : Highlighted(e.Item) ? Color.White : text;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) {
            e.ArrowColor = e.Item != null && Highlighted(e.Item) ? Color.White : muted;
            base.OnRenderArrow(e);
        }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) {
            Rectangle r = e.ImageRectangle;
            if (r.Width <= 0 || r.Height <= 0) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float w = Math.Max(1.5f, r.Height / 8f);
            using (var pen = new Pen(Highlighted(e.Item) ? Color.White : accent, w)) {
                pen.StartCap = pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                e.Graphics.DrawLines(pen, new[] {
                    new PointF(r.Left + r.Width * 0.22f, r.Top + r.Height * 0.52f),
                    new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.72f),
                    new PointF(r.Left + r.Width * 0.78f, r.Top + r.Height * 0.30f) });
            }
        }
    }

    // Capsule button in the style of a macOS / iOS tinted button.
    public sealed class PillButton : Button {
        bool hover, down;
        public Color FillColor { get; set; }
        public Color HoverColor { get; set; }
        public Color PressedColor { get; set; }
        public Color DisabledTextColor { get; set; }
        public PillButton() {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            FillColor = Color.FromArgb(58, 58, 62);
            HoverColor = Color.FromArgb(72, 72, 76);
            PressedColor = Color.FromArgb(86, 86, 92);
            DisabledTextColor = Color.FromArgb(110, 110, 115);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { hover = down = false; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaint(PaintEventArgs e) {
            Graphics g = e.Graphics;
            using (var back = new SolidBrush(Parent != null ? Parent.BackColor : BackColor)) g.FillRectangle(back, ClientRectangle);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(1, 1, Width - 2, Height - 2);
            Color fill = !Enabled ? Hud.Mix(FillColor, Parent != null ? Parent.BackColor : BackColor, 0.4) : down ? PressedColor : hover ? HoverColor : FillColor;
            using (var path = Shapes.Round(r, r.Height / 2f)) {
                using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
                using (var rim = new Pen(Color.FromArgb(Enabled ? 40 : 18, Color.White))) g.DrawPath(rim, path);
            }
            if (Focused && ShowFocusCues) using (var focus = new Pen(Hud.Dim(ForeColor, 160), 1.5f)) using (var path = Shapes.Round(RectangleF.Inflate(r, -2, -2), (r.Height - 4) / 2f)) g.DrawPath(focus, path);
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, Enabled ? ForeColor : DisabledTextColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    public sealed class SplitQuotaLabel : Control {
        public string AccountName { get; set; }
        public string FiveText { get; set; }
        public string LongText { get; set; }
        public Color FiveColor { get; set; }
        public Color LongColor { get; set; }
        public Color SeparatorColor { get; set; }
        public SplitQuotaLabel() { DoubleBuffered = true; ResizeRedraw = true; AccountName = ""; FiveText = LongText = "—"; FiveColor = LongColor = Color.White; SeparatorColor = Color.Gray; }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            int separatorWidth = TextRenderer.MeasureText(e.Graphics, "/", Font, new Size(Int32.MaxValue, Height), flags).Width + Math.Max(4, Font.Height / 6);
            int nameWidth = Width * 32 / 100, valueWidth = Math.Max(0, (Width - nameWidth - separatorWidth) / 2);
            TextRenderer.DrawText(e.Graphics, AccountName, Font, new Rectangle(0, 0, nameWidth, Height), ForeColor, flags);
            TextRenderer.DrawText(e.Graphics, FiveText, Font, new Rectangle(nameWidth, 0, valueWidth, Height), FiveColor, flags);
            TextRenderer.DrawText(e.Graphics, "/", Font, new Rectangle(nameWidth + valueWidth, 0, separatorWidth, Height), SeparatorColor, flags);
            TextRenderer.DrawText(e.Graphics, LongText, Font, new Rectangle(nameWidth + valueWidth + separatorWidth, 0, valueWidth, Height), LongColor, flags);
        }
    }
}

