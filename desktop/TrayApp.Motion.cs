// "Follow the light" motion for the floating monitor and the app's own toast cards. C# 5 only.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using CodexUsage;

namespace CodexUsageDesktop {
    public sealed partial class TrayApp {
        public System.Windows.Forms.Timer MotionTimer;
        public bool ManualRefresh;
        public double HoverGlow;
        public readonly List<GlowToast> Toasts = new List<GlowToast>();
        readonly DateTime motionEpoch = DateTime.UtcNow;
        DateTime? sweepStart;
        bool sweepWarm;
        readonly HashSet<string> lowQuota = new HashSet<string>();
        public const double LowQuotaPercent = 10;
        public const double SweepSeconds = 1.6;

        // The clock only runs while something moves: pointer over the monitor, a refresh in
        // progress, first-load skeletons, or a light sweep. Otherwise the monitor is static.
        public void KickMotion() {
            if (Smoke || MotionTimer == null || Exiting) return;
            if (!MotionTimer.Enabled) MotionTimer.Start();
        }

        // One light sweep around the monitor: cool after a manual refresh, warm when a quota
        // newly drops to 10% or below.
        public void TriggerSweep(bool warm) {
            sweepStart = DateTime.UtcNow;
            sweepWarm = warm;
            KickMotion();
        }

        public bool SweepActive { get { return sweepStart.HasValue; } }

        // Tracks quotas at or below the low threshold; returns true when one newly crosses it.
        public bool NoteLowQuota(string key, double? value, bool stale) {
            bool low = value.HasValue && value.Value <= LowQuotaPercent && !stale;
            if (!low) { lowQuota.Remove(key); return false; }
            return lowQuota.Add(key);
        }

        static double Approach(double current, double target, double step) {
            if (Math.Abs(target - current) <= step) return target;
            return current + Math.Sign(target - current) * step;
        }

        public void StepMotion() { StepMotion(Ball != null && Ball.Visible && !ballMouseDown.HasValue && Ball.Bounds.Contains(Cursor.Position)); }

        public void StepMotion(bool hovering) {
            if (Ball == null || Ball.IsDisposed) return;
            DateTime now = DateTime.UtcNow;
            double clock = (now - motionEpoch).TotalSeconds;
            bool visible = Ball.Visible;
            HoverGlow = Approach(HoverGlow, hovering && visible ? 1 : 0, 0.16);
            double sweep = 0;
            if (sweepStart.HasValue) {
                double t = (now - sweepStart.Value).TotalSeconds / SweepSeconds;
                if (t >= 1 || !visible) sweepStart = null; else sweep = Math.Sin(Math.PI * t);
            }
            bool breathing = RefreshPending && visible;
            double breath = breathing ? 0.16 + 0.12 * Math.Sin(clock * Math.PI * 2 / 1.6) : 0;
            Ball.GlowPhase = (clock * 0.42) % 1.0;
            Ball.GlowLevel = Math.Max(HoverGlow * 0.85, Math.Max(sweep, breath));
            Ball.GlowWarm = sweepWarm && sweepStart.HasValue && sweep >= HoverGlow * 0.85;
            bool loadingLive = RefreshPending || (InitialTimer != null && InitialTimer.Enabled);
            bool anyRing = false, anyLoading = false;
            foreach (QuotaRing ring in RingCells.Values) {
                bool over = hovering && Settings.RingMode && ring.Visible && ring.RectangleToScreen(ring.ClientRectangle).Contains(Cursor.Position);
                ring.HoverLevel = Approach(ring.HoverLevel, over ? 1 : 0, 0.2);
                ring.Phase = Ball.GlowPhase;
                ring.Clock = clock;
                ring.Breathing = breathing;
                if (ring.HoverLevel > 0) anyRing = true;
                if (ring.Loading && ring.Visible && loadingLive) anyLoading = true;
            }
            foreach (SplitQuotaLabel label in CompactCells.Values) {
                label.Clock = clock;
                if (label.Loading && label.Visible && loadingLive) anyLoading = true;
            }
            if (visible) Ball.RequestRender();
            bool active = visible && (hovering || HoverGlow > 0 || sweepStart.HasValue || breathing || anyRing || anyLoading);
            if (!active) {
                Ball.GlowLevel = 0;
                foreach (QuotaRing ring in RingCells.Values) { ring.HoverLevel = 0; ring.Breathing = false; }
                if (visible) Ball.RequestRender();
                if (MotionTimer != null) MotionTimer.Stop();
            }
        }

        // ---------------------------------------------------------------- toasts
        public GlowToast ShowToast(string title, string text, bool warning) {
            var toast = new GlowToast(title, text, warning ? Warning : MenuSelect, warning, UiFont(10, true, false), UiFont(9), U(330), (float)(Popup.DisplayDpi / 96.0));
            toast.Clicked += delegate { Popup.Show(); Popup.Activate(); };
            toast.FormClosed += delegate {
                Toasts.Remove(toast);
                BeginInvokeSafe(delegate { toast.Dispose(); });
                StackToasts(null);
            };
            Toasts.Add(toast);
            // Keep at most three on screen; the oldest leaves first.
            foreach (GlowToast old in Toasts.Where(t => !t.Leaving).Reverse().Skip(3).ToList()) old.Dismiss();
            StackToasts(toast);
            toast.Show();
            return toast;
        }

        void BeginInvokeSafe(MethodInvoker action) {
            try { if (Popup != null && Popup.IsHandleCreated && !Popup.IsDisposed) Popup.BeginInvoke(action); else action(); } catch (Exception) { }
        }

        // Newest at the bottom-right of the monitor's screen, older ones stacked above. The
        // column moves left of the floating monitor when the two would overlap.
        public void StackToasts(GlowToast fresh) {
            if (Toasts.Count == 0) return;
            Rectangle anchor = Ball != null && !Ball.IsDisposed ? new Rectangle(RestLocation ?? Ball.Location, Ball.Size) : Rectangle.Empty;
            Rectangle area = anchor.IsEmpty ? Screen.PrimaryScreen.WorkingArea : Screen.FromRectangle(anchor).WorkingArea;
            int margin = U(16), width = Toasts.Max(t => t.Width);
            int right = area.Right - margin;
            if (Ball != null && Ball.Visible && anchor.IntersectsWith(new Rectangle(right - width, area.Top, width, area.Height)))
                right = Math.Max(area.Left + width + margin, anchor.Left - U(12));
            int bottom = area.Bottom - margin;
            for (int i = Toasts.Count - 1; i >= 0; i--) {
                GlowToast toast = Toasts[i];
                var target = new Point(right - toast.Width, Math.Max(area.Top + margin, bottom - toast.Height));
                if (toast == fresh) toast.Location = new Point(target.X + U(28), target.Y);
                toast.SlideTo(target);
                bottom = target.Y - U(10);
            }
        }
    }
}
