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
    // Monospace digits for the instrument look; Consolas ships with every Windows.
    public static class MonoFont {
        static string family;
        public static string Family {
            get {
                if (family != null) return family;
                foreach (string name in new[] { "Cascadia Mono", "Cascadia Code", "Consolas" }) {
                    try { using (var f = new FontFamily(name)) { family = f.Name; return family; } } catch (ArgumentException) { }
                }
                family = FontFamily.GenericMonospace.Name;
                return family;
            }
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

    // Sci-fi HUD details shared by cards and the floating monitor.
    public static class Hud {
        public static void Brackets(Graphics g, RectangleF r, Color color, float length, float width) {
            if (color.IsEmpty || color.A == 0 || r.Width < length * 2 || r.Height < length * 2) return;
            using (var pen = new Pen(color, width)) {
                pen.StartCap = pen.EndCap = LineCap.Square;
                float l = r.Left, t = r.Top, rt = r.Right, b = r.Bottom;
                g.DrawLines(pen, new[] { new PointF(l, t + length), new PointF(l, t), new PointF(l + length, t) });
                g.DrawLines(pen, new[] { new PointF(rt - length, t), new PointF(rt, t), new PointF(rt, t + length) });
                g.DrawLines(pen, new[] { new PointF(rt, b - length), new PointF(rt, b), new PointF(rt - length, b) });
                g.DrawLines(pen, new[] { new PointF(l + length, b), new PointF(l, b), new PointF(l, b - length) });
            }
        }
        // Thin glowing accent line fading out to the right.
        public static void TopLine(Graphics g, RectangleF r, Color color, float width) {
            if (color.IsEmpty || color.A == 0 || r.Width < 4) return;
            var line = new RectangleF(r.Left, r.Top, r.Width * 0.55f, Math.Max(1, width));
            using (var brush = new LinearGradientBrush(new RectangleF(line.Left - 1, line.Top, line.Width + 2, line.Height), Color.FromArgb(220, color), Color.FromArgb(0, color), LinearGradientMode.Horizontal))
                g.FillRectangle(brush, line);
        }
        public static Color Dim(Color color, int alpha) { return Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), color); }
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
            BorderColor = Color.FromArgb(52, 55, 64);
            AccentColor = Color.Empty;
        }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            if (Width < 2 || Height < 2) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using (var path = Shapes.Round(bounds, Radius))
            using (var pen = new Pen(BorderColor)) e.Graphics.DrawPath(pen, path);
            if (!AccentColor.IsEmpty) {
                float unit = Math.Max(0.5f, HudUnit);
                e.Graphics.SmoothingMode = SmoothingMode.None;
                Hud.Brackets(e.Graphics, new RectangleF(1, 1, Width - 3, Height - 3), Hud.Dim(AccentColor, 200), 12 * unit, 2 * unit);
                Hud.TopLine(e.Graphics, new RectangleF(Radius + 14 * unit, 1, Width - 2 * Radius - 28 * unit, 2 * unit), AccentColor, 1.5f * unit);
            }
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
        public FloatingForm() { DoubleBuffered = true; Radius = 16; HudUnit = 1f; BorderColor = Color.FromArgb(65, 68, 78); AccentColor = Color.Empty; }
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
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Shapes.Round(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), Radius))
            using (var pen = new Pen(BorderColor)) e.Graphics.DrawPath(pen, path);
            if (!AccentColor.IsEmpty) {
                float unit = Math.Max(0.5f, HudUnit);
                e.Graphics.SmoothingMode = SmoothingMode.None;
                float inset = Math.Max(2f, Radius * 0.3f);
                Hud.Brackets(e.Graphics, new RectangleF(inset, inset, Width - 1 - 2 * inset, Height - 1 - 2 * inset), Hud.Dim(AccentColor, 170), Math.Min(10 * unit, Height / 4f), 1.5f * unit);
            }
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
            FillColor = Color.FromArgb(138, 158, 214);
            TrackColor = Color.FromArgb(52, 55, 64);
            CaptionColor = Color.Empty;
        }
        // 30 segments of 9 degrees with 3-degree gaps; lit segments glow softly.
        const int Segments = 30;
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            float scale = Width / 52f;
            float diameter = 40 * scale;
            float stroke = 3 * scale;
            var ring = new RectangleF((Width - diameter) / 2, 4 * scale, diameter, diameter);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            bool known = Value >= 0 && !Double.IsNaN(Value) && !Double.IsInfinity(Value);
            double lit = known ? Math.Min(100, Value) / 100.0 * Segments : 0;
            float step = 360f / Segments, sweep = step - 3f;
            if (known && Value > 0) {
                // Soft outer glow behind the lit arc.
                using (var glow = new Pen(Hud.Dim(FillColor, Stale ? 25 : 55), stroke * 2.6f))
                    e.Graphics.DrawArc(glow, ring, -90, (float)(Math.Min(100, Value) * 3.6));
            }
            using (var track = new Pen(TrackColor, stroke))
            using (var pen = new Pen(Stale ? Hud.Dim(FillColor, 150) : FillColor, stroke)) {
                for (int i = 0; i < Segments; i++) {
                    float start = -90 + i * step + 1.5f;
                    e.Graphics.DrawArc(track, ring, start, sweep);
                    if (i < Math.Floor(lit)) e.Graphics.DrawArc(pen, ring, start, sweep);
                    else if (i < lit) e.Graphics.DrawArc(pen, ring, start, (float)(sweep * (lit - i)));
                }
            }
            // Inner hairline for an instrument-dial look.
            float inner = stroke * 2.2f;
            using (var hair = new Pen(Hud.Dim(TrackColor, 160), Math.Max(1f, scale * 0.6f)))
                e.Graphics.DrawEllipse(hair, RectangleF.Inflate(ring, -inner, -inner));
            string valueText = ValueText;
            // Bounds already contain the monitor scale. Pixel fonts avoid
            // applying the graphics device's DPI a second time to point fonts.
            using (var valueFont = new Font(MonoFont.Family, 11.5f * scale, FontStyle.Bold, GraphicsUnit.Pixel))
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
            FillColor = Color.FromArgb(138, 158, 214);
            TrackColor = Color.FromArgb(48, 51, 60);
        }
        // Segmented "LED" meter: fixed-width cells with gaps, partial last cell.
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            if (Width < 2 || Height < 2) return;
            e.Graphics.SmoothingMode = SmoothingMode.None;
            float gap = Math.Max(2f, Height / 3f);
            int count = Math.Max(8, (int)Math.Round((Width + gap) / (Height * 1.8f + gap)));
            float cell = (Width - gap * (count - 1)) / count;
            double filled = Math.Max(0, Math.Min(100, Value)) / 100.0 * count;
            using (var track = new SolidBrush(TrackColor))
            using (var fill = new SolidBrush(FillColor))
            using (var shine = new SolidBrush(Color.FromArgb(90, Color.White))) {
                for (int i = 0; i < count; i++) {
                    var r = new RectangleF(i * (cell + gap), 0, cell, Height);
                    e.Graphics.FillRectangle(track, r);
                    if (i >= filled) continue;
                    var lit = new RectangleF(r.Left, 0, (float)(cell * Math.Min(1, filled - i)), Height);
                    e.Graphics.FillRectangle(fill, lit);
                    if (Height >= 4) e.Graphics.FillRectangle(shine, new RectangleF(lit.Left, 0, lit.Width, 1));
                }
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

    // Dark HUD context menu: navy surface, cyan selection and check marks.
    public sealed class HudColorTable : ProfessionalColorTable {
        readonly Color back, border, select;
        public HudColorTable(Color back, Color border, Color select) { this.back = back; this.border = border; this.select = select; UseSystemColors = false; }
        public override Color ToolStripDropDownBackground { get { return back; } }
        public override Color ImageMarginGradientBegin { get { return back; } }
        public override Color ImageMarginGradientMiddle { get { return back; } }
        public override Color ImageMarginGradientEnd { get { return back; } }
        public override Color MenuBorder { get { return border; } }
        public override Color MenuItemBorder { get { return border; } }
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
        readonly Color text, muted, accent;
        public HudMenuRenderer(Color back, Color border, Color select, Color text, Color muted, Color accent) : base(new HudColorTable(back, border, select)) {
            this.text = text; this.muted = muted; this.accent = accent;
            RoundedEdges = false;
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) {
            e.TextColor = e.Item.Enabled ? text : muted;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) {
            e.ArrowColor = accent;
            base.OnRenderArrow(e);
        }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) {
            Rectangle r = e.ImageRectangle;
            if (r.Width <= 0 || r.Height <= 0) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float w = Math.Max(1.5f, r.Height / 8f);
            using (var pen = new Pen(accent, w)) {
                pen.StartCap = pen.EndCap = LineCap.Round;
                e.Graphics.DrawLines(pen, new[] {
                    new PointF(r.Left + r.Width * 0.22f, r.Top + r.Height * 0.52f),
                    new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.72f),
                    new PointF(r.Left + r.Width * 0.78f, r.Top + r.Height * 0.30f) });
            }
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

