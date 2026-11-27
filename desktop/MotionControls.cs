// Animated controls in the "Follow the light" style: wave-bar trend, heartbeat quota line,
// segmented switch, elastic slider and the app's own toast cards. C# 5 only.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace CodexUsage {
    // One shared animation clock (about 33 fps) for every animated control. A control is
    // registered only while it animates and is dropped as soon as it is disposed, so the
    // timer stops when nothing moves. Instant (smoke tests) skips animation entirely:
    // controls then paint their final state.
    public static class Motion {
        static Timer timer;
        static readonly Dictionary<Control, Func<bool>> items = new Dictionary<Control, Func<bool>>();
        public static bool Instant;
        public static int Count { get { return items.Count; } }

        public static double Progress(DateTime start, double milliseconds) {
            if (Instant) return 1;
            return Math.Max(0, Math.Min(1, (DateTime.UtcNow - start).TotalMilliseconds / milliseconds));
        }

        // step() runs every frame; returning false (or the control being disposed) ends it.
        public static void Run(Control owner, Func<bool> step) {
            if (Instant || owner == null || owner.IsDisposed) return;
            if (!items.ContainsKey(owner)) owner.Disposed += OwnerDisposed;
            items[owner] = step;
            if (timer == null) { timer = new Timer { Interval = 30 }; timer.Tick += Tick; }
            if (!timer.Enabled) timer.Start();
        }

        public static void Stop(Control owner) {
            if (owner == null || !items.ContainsKey(owner)) return;
            items.Remove(owner);
            owner.Disposed -= OwnerDisposed;
        }

        static void OwnerDisposed(object sender, EventArgs e) { Stop((Control)sender); }

        static void Tick(object sender, EventArgs e) {
            foreach (var pair in items.ToList()) {
                bool keep;
                try { keep = !pair.Key.IsDisposed && pair.Value(); } catch (Exception) { keep = false; }
                if (!keep) Stop(pair.Key);
            }
            if (items.Count == 0) timer.Stop();
        }
    }

    // Seven-day token trend as wave bars: rounded capsules rising with a slight elastic
    // overshoot one after another, today's bar lit with a glow, the hovered bar brightened.
    // Values are relative to the week's maximum.
    public sealed class UsageSparkline : Control {
        double[] values = new double[0];
        string[] labels = new string[0];
        DateTime? shownAt;
        int hover = -1;
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
        public int HoverIndex { get { return hover; } }
        public void SetData(double[] data, string[] captions) {
            values = data ?? new double[0];
            labels = captions ?? new string[0];
            Invalidate();
        }
        // Bars rise again; called when the panel opens or the chart is switched to, not on
        // the minute-by-minute rebuilds.
        public void Replay() {
            shownAt = DateTime.UtcNow;
            DateTime start = shownAt.Value;
            Motion.Run(this, delegate { Invalidate(); return Motion.Progress(start, 520 + 60 * values.Length) < 1; });
            Invalidate();
        }
        int IndexAt(int x) {
            int count = values.Length;
            if (count == 0 || Width <= 0) return -1;
            return Math.Max(0, Math.Min(count - 1, x * count / Width));
        }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int i = IndexAt(e.X); if (i != hover) { hover = i; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (hover != -1) { hover = -1; Invalidate(); } }

        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            int count = values.Length;
            if (count == 0 || Width < 8 || Height < 12) return;
            Graphics g = e.Graphics;
            float captionHeight = Math.Max(10f, Height * 0.3f);
            float chartHeight = Height - captionHeight - 2;
            float gap = Math.Max(3f, Width / (count * 2.2f));
            float bar = (Width - gap * (count - 1)) / count;
            double max = 0;
            foreach (double v in values) if (!Double.IsNaN(v) && v > max) max = v;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float radius = bar / 2f;
            double elapsed = Motion.Instant || !shownAt.HasValue ? 1e9 : (DateTime.UtcNow - shownAt.Value).TotalMilliseconds;
            using (var track = new SolidBrush(Hud.Dim(TrackColor, 150)))
            using (var font = new Font("Microsoft YaHei UI", Math.Max(7f, captionHeight * 0.78f), FontStyle.Regular, GraphicsUnit.Pixel)) {
                for (int i = 0; i < count; i++) {
                    float x = i * (bar + gap);
                    using (var path = Shapes.Round(new RectangleF(x, 0, bar, chartHeight), radius)) g.FillPath(track, path);
                    double v = values[i];
                    bool today = i == HighlightIndex, lit = today || i == hover;
                    if (max > 0 && v > 0 && !Double.IsNaN(v)) {
                        double grow = Hud.EaseOutBack((elapsed - i * 60) / 520.0);
                        float full = Math.Min(chartHeight, Math.Max(bar + 1, (float)(chartHeight * v / max)));
                        float h = Math.Max(0, Math.Min(chartHeight, (float)(full * grow)));
                        if (h >= 1) {
                            var r = new RectangleF(x, chartHeight - h, bar, h);
                            if (today) using (var glowPath = Shapes.Round(RectangleF.Inflate(r, 2.5f, 2.5f), radius + 2.5f))
                                using (var glow = new SolidBrush(Hud.Dim(BarColor, 55))) g.FillPath(glow, glowPath);
                            using (var path = Shapes.Round(r, Math.Min(radius, h / 2)))
                            using (var fill = new LinearGradientBrush(new RectangleF(r.Left, r.Top - 1, r.Width, r.Height + 2),
                                Hud.Dim(Hud.Mix(BarColor, Color.White, lit ? 0.35 : 0.15), lit ? 255 : 170), Hud.Dim(BarColor, lit ? 200 : 90), LinearGradientMode.Vertical))
                                g.FillPath(fill, path);
                        }
                    }
                    if (i < labels.Length && !string.IsNullOrEmpty(labels[i]))
                        TextRenderer.DrawText(g, labels[i], font, new Rectangle((int)x - 4, (int)(chartHeight + 2), (int)bar + 8, (int)captionHeight),
                            lit ? BarColor : CaptionColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                }
            }
        }
    }

    // Remaining quota of the 5-hour window over the last hours, drawn like a heartbeat
    // monitor: the line traces in, a soft glow underneath, and a pulsing head on the latest read.
    public sealed class HeartbeatChart : Control {
        double[] times = new double[0], levels = new double[0];
        DateTime? shownAt;
        bool running;
        readonly DateTime epoch = DateTime.UtcNow;
        public Color LineColor { get; set; }
        public Color GridColor { get; set; }
        public Color CaptionColor { get; set; }
        public string EmptyText { get; set; }
        public string StartCaption { get; set; }
        public string EndCaption { get; set; }
        public HeartbeatChart() {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            LineColor = Color.FromArgb(64, 156, 255);
            GridColor = Color.FromArgb(58, 58, 62);
            CaptionColor = Color.FromArgb(142, 142, 147);
            EmptyText = "数据不足 · 约 10 分钟后显示";
            StartCaption = "5 小时前"; EndCaption = "现在";
        }
        public int PointCount { get { return times.Length; } }
        // x: 0-1 across the time span; y: remaining percent 0-100.
        public void SetData(double[] x, double[] y) {
            int n = Math.Min(x == null ? 0 : x.Length, y == null ? 0 : y.Length);
            times = new double[n]; levels = new double[n];
            for (int i = 0; i < n; i++) { times[i] = Math.Max(0, Math.Min(1, x[i])); levels[i] = Math.Max(0, Math.Min(100, y[i])); }
            Invalidate();
        }
        // The line traces in again (panel opened, chart switched to).
        public void Replay() { shownAt = DateTime.UtcNow; Invalidate(); }
        public bool Pulsing { get { return running; } }

        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            if (Width < 8 || Height < 12) return;
            if (!running && times.Length >= 2 && !Motion.Instant) {
                // The head pulses while the chart is on screen; the clock lets go once it is hidden.
                running = true;
                Motion.Run(this, delegate { if (!Visible) { running = false; return false; } Invalidate(); return true; });
            }
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float captionHeight = Math.Max(10f, Height * 0.3f);
            float top = 4, bottom = Height - captionHeight - 2, chart = bottom - top;
            using (var grid = new Pen(Hud.Dim(GridColor, 170), 1) { DashStyle = DashStyle.Dot })
                foreach (float f in new[] { 0f, 0.5f, 1f }) g.DrawLine(grid, 0, top + chart * f, Width, top + chart * f);
            using (var font = new Font("Microsoft YaHei UI", Math.Max(7f, captionHeight * 0.78f), FontStyle.Regular, GraphicsUnit.Pixel)) {
                var flags = TextFormatFlags.Top | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
                TextRenderer.DrawText(g, StartCaption, font, new Rectangle(0, (int)bottom + 2, Width / 2, (int)captionHeight), CaptionColor, flags);
                TextRenderer.DrawText(g, EndCaption, font, new Rectangle(Width / 2, (int)bottom + 2, Width / 2, (int)captionHeight), LineColor, flags | TextFormatFlags.Right);
                if (times.Length < 2) {
                    TextRenderer.DrawText(g, EmptyText, font, new Rectangle(0, (int)top, Width, (int)chart), CaptionColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                    return;
                }
            }
            double elapsed = Motion.Instant || !shownAt.HasValue ? 1e9 : (DateTime.UtcNow - shownAt.Value).TotalMilliseconds;
            double reveal = Hud.EaseOut(elapsed / 900.0);
            float pad = 5;
            var pts = new PointF[times.Length];
            for (int i = 0; i < times.Length; i++) pts[i] = new PointF(pad + (float)times[i] * (Width - pad * 2), top + (float)((100 - levels[i]) / 100.0) * chart);
            GraphicsState state = g.Save();
            g.SetClip(new RectangleF(0, 0, pad + (float)reveal * (Width - pad), Height));
            using (var area = new GraphicsPath()) {
                area.AddLines(pts);
                area.AddLine(pts[pts.Length - 1].X, bottom, pts[0].X, bottom);
                area.CloseFigure();
                using (var fill = new LinearGradientBrush(new RectangleF(0, top - 1, Width, chart + 2), Hud.Dim(LineColor, 70), Hud.Dim(LineColor, 0), LinearGradientMode.Vertical)) g.FillPath(fill, area);
            }
            using (var glow = new Pen(Hud.Dim(LineColor, 50), 5) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLines(glow, pts);
            using (var brush = new LinearGradientBrush(new RectangleF(0, 0, Width, Height), Hud.Dim(LineColor, 120), Hud.Mix(LineColor, Color.White, 0.25), LinearGradientMode.Horizontal))
            using (var line = new Pen(brush, 2) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLines(line, pts);
            g.Restore(state);
            if (reveal >= 0.999) {
                PointF head = pts[pts.Length - 1];
                double beat = Motion.Instant ? 0.3 : ((DateTime.UtcNow - epoch).TotalMilliseconds % 1200) / 1200.0;
                float halo = 3 + 7 * (float)beat;
                using (var ring = new SolidBrush(Hud.Dim(LineColor, (int)(130 * (1 - beat))))) g.FillEllipse(ring, head.X - halo, head.Y - halo, halo * 2, halo * 2);
                using (var dot = new SolidBrush(Hud.Mix(LineColor, Color.White, 0.4))) g.FillEllipse(dot, head.X - 3, head.Y - 3, 6, 6);
            }
        }
    }

    // iOS-style segmented control whose selection pill slides with a small elastic settle.
    public sealed class SegmentedSwitch : Control {
        string[] items = new string[0];
        int selected;
        double pillFrom;
        DateTime moveStart = DateTime.MinValue;
        public event EventHandler SelectedIndexChanged;
        public Color TrackColor { get; set; }
        public Color PillColor { get; set; }
        public Color TextColor { get; set; }
        public Color MutedColor { get; set; }
        public SegmentedSwitch() {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            TrackColor = Color.FromArgb(28, 28, 30);
            PillColor = Color.FromArgb(99, 99, 102);
            TextColor = Color.White;
            MutedColor = Color.FromArgb(152, 152, 159);
            AccessibleRole = AccessibleRole.PageTabList;
        }
        public string[] Items { get { return items; } set { items = value ?? new string[0]; selected = Math.Max(0, Math.Min(selected, items.Length - 1)); Invalidate(); } }
        public int SelectedIndex { get { return selected; } set { Select(value, true, true); } }
        // animate: slide the pill; notify: raise SelectedIndexChanged.
        public void Select(int index, bool animate, bool notify) {
            int next = Math.Max(0, Math.Min(items.Length - 1, index));
            if (next == selected) return;
            pillFrom = PillPosition();
            selected = next;
            AccessibleDescription = items.Length > 0 ? items[selected] : "";
            if (animate) {
                moveStart = DateTime.UtcNow;
                DateTime start = moveStart;
                Motion.Run(this, delegate { Invalidate(); return Motion.Progress(start, 300) < 1; });
            } else moveStart = DateTime.MinValue;
            Invalidate();
            if (notify && SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
        }
        // Pill position in segment units (0 = first segment).
        double PillPosition() {
            if (moveStart == DateTime.MinValue) return selected;
            double t = Hud.EaseOutBack(Motion.Progress(moveStart, 300));
            return pillFrom + (selected - pillFrom) * t;
        }
        protected override void OnMouseDown(MouseEventArgs e) {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || items.Length == 0) return;
            Focus();
            SelectedIndex = Math.Max(0, Math.Min(items.Length - 1, e.X * items.Length / Math.Max(1, Width)));
        }
        protected override bool IsInputKey(Keys key) { return key == Keys.Left || key == Keys.Right || base.IsInputKey(key); }
        protected override void OnKeyDown(KeyEventArgs e) {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Left) SelectedIndex = selected - 1;
            if (e.KeyCode == Keys.Right) SelectedIndex = selected + 1;
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e) {
            Graphics g = e.Graphics;
            using (var back = new SolidBrush(Parent != null ? Parent.BackColor : BackColor)) g.FillRectangle(back, ClientRectangle);
            if (items.Length == 0 || Width < 8 || Height < 8) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var track = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using (var path = Shapes.Round(track, track.Height / 2))
            using (var fill = new SolidBrush(TrackColor)) g.FillPath(fill, path);
            float segment = (Width - 4f) / items.Length;
            var pill = new RectangleF(2 + (float)PillPosition() * segment, 2, segment, Height - 4);
            using (var path = Shapes.Round(pill, pill.Height / 2)) {
                using (var fill = new LinearGradientBrush(new RectangleF(pill.Left, pill.Top - 1, pill.Width, pill.Height + 2), Hud.Mix(PillColor, Color.White, 0.12), PillColor, LinearGradientMode.Vertical)) g.FillPath(fill, path);
                using (var rim = new Pen(Color.FromArgb(Focused ? 110 : 45, Color.White))) g.DrawPath(rim, path);
            }
            for (int i = 0; i < items.Length; i++)
                TextRenderer.DrawText(g, items[i], Font, new Rectangle((int)(2 + i * segment), 0, (int)segment, Height), i == selected ? TextColor : MutedColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }

    // Slider with an elastic thumb: it stretches into a pill while dragged, rubber-bands past
    // either end and springs back with a damped wobble on release. TrackBar-compatible API.
    public sealed class ElasticSlider : Control {
        int minimum, maximum = 100, value;
        bool dragging;
        double stretch;          // extra pixels beyond the end while dragging
        double shownPosition = -1; // animated thumb position (0-1)
        DateTime springStart = DateTime.MinValue;
        double springFrom;
        public event EventHandler ValueChanged;
        public int SmallChange { get; set; }
        public int LargeChange { get; set; }
        public Color AccentColor { get; set; }
        public Color TrackColor { get; set; }
        public Color ThumbColor { get; set; }
        public ElasticSlider() {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            SmallChange = 1; LargeChange = 5;
            AccentColor = Color.FromArgb(10, 132, 255);
            TrackColor = Color.FromArgb(58, 58, 62);
            ThumbColor = Color.White;
            AccessibleRole = AccessibleRole.Slider;
            Cursor = Cursors.Hand;
        }
        public int Minimum { get { return minimum; } set { minimum = value; if (maximum < minimum) maximum = minimum; Value = this.value; } }
        public int Maximum { get { return maximum; } set { maximum = Math.Max(minimum, value); Value = this.value; } }
        public int Value {
            get { return value; }
            set {
                int next = Math.Max(minimum, Math.Min(maximum, value));
                if (next == this.value) return;
                double from = Fraction(this.value);
                this.value = next;
                AccessibleDescription = next.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (!dragging) Spring(from);
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }
        public bool Dragging { get { return dragging; } }
        double Fraction(int v) { return maximum == minimum ? 0 : (double)(v - minimum) / (maximum - minimum); }
        float Thumb { get { return Math.Max(8f, Height * 0.42f); } }
        float Left0 { get { return Thumb / 2 + 2; } }
        float Span { get { return Math.Max(1f, Width - Thumb - 4); } }

        void Spring(double fromFraction) {
            springFrom = fromFraction + stretch / Span;
            springStart = DateTime.UtcNow;
            stretch = 0;
            DateTime start = springStart;
            Motion.Run(this, delegate { Invalidate(); return Motion.Progress(start, 650) < 1; });
        }
        // Damped spring toward the value: overshoots slightly, wobbles once, settles.
        double DisplayFraction() {
            double target = Fraction(value);
            if (dragging) return target + stretch / Span;
            if (springStart == DateTime.MinValue) return target;
            double t = Motion.Progress(springStart, 650);
            if (t >= 1) return target;
            double secs = t * 0.65;
            return target + (springFrom - target) * Math.Exp(-7 * secs) * Math.Cos(15 * secs);
        }
        void SetFromX(int x) {
            double raw = (x - Left0) / Span;
            int next = (int)Math.Round(minimum + Math.Max(0, Math.Min(1, raw)) * (maximum - minimum));
            double over = raw < 0 ? raw * Span : raw > 1 ? (raw - 1) * Span : 0;
            stretch = Math.Sign(over) * Math.Min(Thumb * 0.9, Math.Abs(over) * 0.3);
            Value = next;
            Invalidate();
        }
        protected override void OnMouseDown(MouseEventArgs e) {
            base.OnMouseDown(e);
            if (!Enabled || e.Button != MouseButtons.Left) return;
            Focus();
            dragging = true; Capture = true;
            SetFromX(e.X);
        }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (dragging) SetFromX(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) {
            base.OnMouseUp(e);
            if (!dragging) return;
            dragging = false; Capture = false;
            Spring(Fraction(value));
        }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); if (Enabled) Value += Math.Sign(e.Delta) * SmallChange; }
        protected override bool IsInputKey(Keys key) { return key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down || base.IsInputKey(key); }
        protected override void OnKeyDown(KeyEventArgs e) {
            base.OnKeyDown(e);
            if (!Enabled) return;
            switch (e.KeyCode) {
                case Keys.Left: case Keys.Down: Value -= SmallChange; break;
                case Keys.Right: case Keys.Up: Value += SmallChange; break;
                case Keys.PageDown: Value -= LargeChange; break;
                case Keys.PageUp: Value += LargeChange; break;
                case Keys.Home: Value = minimum; break;
                case Keys.End: Value = maximum; break;
            }
        }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e) {
            Graphics g = e.Graphics;
            using (var back = new SolidBrush(Parent != null ? Parent.BackColor : BackColor)) g.FillRectangle(back, ClientRectangle);
            if (Width < 12 || Height < 8) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float thumb = Thumb, trackH = Math.Max(3f, thumb * 0.36f), cy = Height / 2f;
            Color accent = Enabled ? AccentColor : Hud.Mix(AccentColor, TrackColor, 0.6);
            double f = DisplayFraction();
            float cx = Left0 + (float)f * Span;
            var track = new RectangleF(Left0 - trackH / 2, cy - trackH / 2, Span + trackH, trackH);
            using (var path = Shapes.Round(track, trackH / 2))
            using (var fill = new SolidBrush(TrackColor)) g.FillPath(fill, path);
            float litRight = Math.Max(track.Left + trackH, Math.Min(track.Right + Math.Abs((float)stretch), cx + trackH / 2));
            var lit = RectangleF.FromLTRB(track.Left, track.Top, litRight, track.Bottom);
            using (var path = Shapes.Round(lit, trackH / 2))
            using (var fill = new LinearGradientBrush(new RectangleF(lit.Left - 1, lit.Top, lit.Width + 2, lit.Height), Hud.Dim(accent, 150), Hud.Mix(accent, Color.White, 0.2), LinearGradientMode.Horizontal)) g.FillPath(fill, path);
            // Thumb: a circle that stretches into a pill while held, the more so past the ends.
            float w = thumb * (dragging ? 1.45f + (float)Math.Min(0.5, Math.Abs(stretch) / thumb) : 1f), h = thumb * (dragging ? 0.86f : 1f);
            var knob = new RectangleF(cx - w / 2, cy - h / 2, w, h);
            using (var shadowPath = Shapes.Round(RectangleF.Inflate(knob, 2, 2), h / 2 + 2))
            using (var shadow = new SolidBrush(Color.FromArgb(Enabled ? 60 : 20, accent))) g.FillPath(shadow, shadowPath);
            using (var path = Shapes.Round(knob, h / 2))
            using (var fill = new SolidBrush(Enabled ? ThumbColor : Color.FromArgb(150, ThumbColor))) g.FillPath(fill, path);
            if (Focused && ShowFocusCues) using (var path = Shapes.Round(RectangleF.Inflate(knob, 3, 3), h / 2 + 3)) using (var pen = new Pen(Hud.Dim(accent, 170), 1.5f)) g.DrawPath(pen, path);
        }
    }

    // The app's own notification card: dark glass-grey, a light travelling around its rim,
    // fades and slides in, stays about seven seconds (longer while hovered), never takes focus.
    public sealed class GlowToast : Form {
        readonly string title, body;
        readonly Color accent;
        readonly bool warm;
        readonly Font titleFont, bodyFont;
        readonly float unit;
        readonly Timer clock;
        readonly DateTime born = DateTime.UtcNow;
        DateTime? leaving;
        Point slideFrom, slideTo;
        DateTime slideStart = DateTime.MinValue;
        double shown;
        public event EventHandler Clicked;
        public int Lifetime { get; set; }
        public Rectangle TextBounds { get; private set; }

        public GlowToast(string title, string body, Color accent, bool warm, Font titleFont, Font bodyFont, int width, float unit) {
            this.title = title ?? ""; this.body = body ?? ""; this.accent = accent; this.warm = warm;
            this.titleFont = titleFont; this.bodyFont = bodyFont; this.unit = Math.Max(0.5f, unit);
            Lifetime = 7000;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            // Topmost comes from WS_EX_TOPMOST below: the TopMost property would activate the window.
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(44, 44, 46);
            DoubleBuffered = true;
            Cursor = Cursors.Hand;
            Opacity = 0;
            int pad = (int)(16 * this.unit), dot = (int)(10 * this.unit), textLeft = pad + dot + (int)(10 * this.unit);
            int textWidth = Math.Max(40, width - textLeft - pad);
            Size titleSize = TextRenderer.MeasureText(this.title, titleFont, new Size(textWidth, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            Size bodySize = TextRenderer.MeasureText(this.body, bodyFont, new Size(textWidth, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            int gap = (int)(4 * this.unit);
            TextBounds = new Rectangle(textLeft, pad, textWidth, titleSize.Height + gap + bodySize.Height);
            ClientSize = new Size(width, pad * 2 + TextBounds.Height);
            clock = new Timer { Interval = 30 };
            clock.Tick += delegate { Step(); };
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams {
            get {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 | 0x00000080 | 0x00000008; // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST
                return cp;
            }
        }
        protected override void OnShown(EventArgs e) { base.OnShown(e); clock.Start(); }
        protected override void OnSizeChanged(EventArgs e) {
            base.OnSizeChanged(e);
            if (Width < 2 || Height < 2) return;
            using (var path = Shapes.Round(new RectangleF(0, 0, Width, Height), 14 * unit)) { var old = Region; Region = new Region(path); if (old != null) old.Dispose(); }
        }
        protected override void Dispose(bool disposing) { if (disposing) { clock.Stop(); clock.Dispose(); } base.Dispose(disposing); }

        public bool Leaving { get { return leaving.HasValue; } }
        public void Dismiss() { if (!leaving.HasValue) leaving = DateTime.UtcNow; }
        public void SlideTo(Point target) {
            if (Location == target && slideStart == DateTime.MinValue) return;
            slideFrom = Location; slideTo = target; slideStart = DateTime.UtcNow;
            if (Motion.Instant) { Location = target; slideStart = DateTime.MinValue; }
        }

        void Step() {
            DateTime now = DateTime.UtcNow;
            if (slideStart != DateTime.MinValue) {
                double t = Hud.EaseOut((now - slideStart).TotalMilliseconds / 260.0);
                Location = new Point(slideFrom.X + (int)Math.Round((slideTo.X - slideFrom.X) * t), slideFrom.Y + (int)Math.Round((slideTo.Y - slideFrom.Y) * t));
                if (t >= 1) slideStart = DateTime.MinValue;
            }
            bool hovered = Bounds.Contains(Cursor.Position);
            if (!leaving.HasValue && !hovered && (now - born).TotalMilliseconds > Lifetime) leaving = now;
            if (hovered && leaving.HasValue && (now - leaving.Value).TotalMilliseconds < 300) leaving = null;
            double target = leaving.HasValue ? 0 : 1;
            shown += Math.Sign(target - shown) * Math.Min(Math.Abs(target - shown), 0.12);
            Opacity = 0.97 * shown;
            if (leaving.HasValue && shown <= 0.001) { clock.Stop(); Close(); return; }
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e) {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && Clicked != null) Clicked(this, EventArgs.Empty);
            Dismiss();
        }

        protected override void OnPaint(PaintEventArgs e) {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float radius = 14 * unit;
            var bounds = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using (var sheen = new LinearGradientBrush(new RectangleF(0, -1, Width, Height * 0.7f + 2), Color.FromArgb(22, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical))
                g.FillRectangle(sheen, 0, 0, Width, Height * 0.7f);
            Hud.GlassEdge(g, bounds, radius, Color.FromArgb(70, 255, 255, 255), Color.FromArgb(80, 255, 255, 255), unit);
            double age = (DateTime.UtcNow - born).TotalSeconds;
            Hud.LightRim(g, new RectangleF(1, 1, Width - 2, Height - 2), radius, age * 0.45, 0.85, warm ? Hud.WarmLight : Hud.CoolLight, unit);
            int pad = (int)(16 * unit), dot = (int)(10 * unit);
            float pulse = (float)(0.5 + 0.5 * Math.Sin(age * Math.PI * 2 / 1.4));
            var center = new PointF(pad + dot / 2f, pad + titleFont.Height / 2f);
            float halo = dot / 2f + 4 * unit * pulse;
            using (var glow = new SolidBrush(Hud.Dim(accent, (int)(90 * (1 - pulse * 0.5))))) g.FillEllipse(glow, center.X - halo, center.Y - halo, halo * 2, halo * 2);
            using (var fill = new SolidBrush(accent)) g.FillEllipse(fill, center.X - dot / 2f, center.Y - dot / 2f, dot, dot);
            Rectangle text = TextBounds;
            Size titleSize = TextRenderer.MeasureText(title, titleFont, new Size(text.Width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, title, titleFont, new Rectangle(text.Left, text.Top, text.Width, titleSize.Height), Color.FromArgb(245, 245, 247), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            int bodyTop = text.Top + titleSize.Height + (int)(4 * unit);
            TextRenderer.DrawText(g, body, bodyFont, new Rectangle(text.Left, bodyTop, text.Width, text.Bottom - bodyTop + 2), Color.FromArgb(190, 190, 196), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }
    }
}
