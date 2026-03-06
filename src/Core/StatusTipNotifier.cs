using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CopilotInput.Core
{
    public sealed class StatusTipNotifier : IDisposable
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GUITHREADINFO
        {
            public int cbSize;
            public int flags;
            public IntPtr hwndActive;
            public IntPtr hwndFocus;
            public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner;
            public IntPtr hwndMoveSize;
            public IntPtr hwndCaret;
            public RECT rcCaret;
        }

        private enum OverlayState
        {
            Ready,
            Listening,
            Dictating,
            Processing,
            Error
        }

        private readonly Control _uiControl;
        private readonly NotifyIcon _notifyIcon;
        private readonly CursorGlowForm _cursorGlowForm;
        private readonly WindowOutlineForm _windowOutlineForm;
        private readonly Timer _animationTimer;

        private OverlayState _state;
        private int _frame;
        private bool _disposed;
        private Point _lastAnchorPoint;

        public StatusTipNotifier()
        {
            _uiControl = new Control();
            _uiControl.CreateControl();

            _notifyIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Visible = true,
                Text = "Copilot Input: Starting"
            };

            _cursorGlowForm = new CursorGlowForm();
            _windowOutlineForm = new WindowOutlineForm();

            _animationTimer = new Timer { Interval = 50 };
            _animationTimer.Tick += (_, __) => AnimateOverlay();
        }

        public void ShowReady() => Post(() =>
        {
            if (_disposed) return;
            _notifyIcon.Text = "Copilot Input: Ready (Hold Caps Lock)";
            SetState(OverlayState.Ready);
        });

        public void ShowListening() => Post(() =>
        {
            if (_disposed) return;
            _notifyIcon.Text = "Copilot Input: Listening...";
            SetState(OverlayState.Listening);
        });

        public void ShowDictating() => Post(() =>
        {
            if (_disposed) return;
            _notifyIcon.Text = "Copilot Input: Dictating";
            SetState(OverlayState.Dictating);
        });

        public void ShowProcessing() => Post(() =>
        {
            if (_disposed) return;
            _notifyIcon.Text = "Copilot Input: Processing speech...";
            SetState(OverlayState.Processing);
        });

        public void ShowError(string message) => Post(() =>
        {
            if (_disposed) return;
            _notifyIcon.Text = "Copilot Input: Error";
            SetState(OverlayState.Error);
        });

        private void SetState(OverlayState state)
        {
            _state = state;
            _frame = 0;

            if (_state == OverlayState.Ready)
            {
                _animationTimer.Stop();
                _cursorGlowForm.Hide();
                _windowOutlineForm.Hide();
                return;
            }

            if (!_animationTimer.Enabled)
            {
                _animationTimer.Start();
            }

            AnimateOverlay();
        }

        private void AnimateOverlay()
        {
            if (_disposed || _state == OverlayState.Ready)
            {
                return;
            }

            _frame++;

            var anchorPoint = GetOverlayAnchorPoint();
            var dotCenter = new Point(anchorPoint.X + 5, anchorPoint.Y + 10);
            var pulse = 0.5 + (Math.Sin(_frame * 0.18) * 0.5);

            var palette = new[]
            {
                Color.FromArgb(238, 80, 145),
                Color.FromArgb(138, 80, 216),
                Color.FromArgb(25, 159, 215)
            };

            double loopPosition = (_frame * 0.035) % 3.0;
            int segmentIndex = (int)loopPosition;
            double segmentT = loopPosition - segmentIndex;

            var segmentStart = palette[segmentIndex];
            var segmentEnd = palette[(segmentIndex + 1) % palette.Length];
            var activeColor = LerpColor(segmentStart, segmentEnd, segmentT);

            int dotBaseSize;
            int borderThickness;
            int borderAlphaBase;

            switch (_state)
            {
                case OverlayState.Listening:
                    dotBaseSize = 16;
                    borderThickness = 2;
                    borderAlphaBase = 108;
                    break;
                case OverlayState.Dictating:
                    dotBaseSize = 18;
                    borderThickness = 3;
                    borderAlphaBase = 138;
                    break;
                case OverlayState.Processing:
                    dotBaseSize = 17;
                    borderThickness = 2;
                    borderAlphaBase = 122;
                    break;
                default:
                    dotBaseSize = 18;
                    borderThickness = 3;
                    borderAlphaBase = 146;
                    break;
            }

                var outlineAlpha = borderAlphaBase + (int)(pulse * 80);
                var borderStart = Color.FromArgb(Math.Clamp(outlineAlpha, 96, 235), activeColor);
                var borderEnd = Color.FromArgb(Math.Clamp((int)(outlineAlpha * 0.78), 72, 210), segmentEnd);

            var dotSize = dotBaseSize + (int)(pulse * 4); // 16~22 (by state)
                var dotColor = Color.FromArgb(Math.Clamp(borderStart.A + 18, 130, 245), activeColor);

            _cursorGlowForm.UpdateGlow(
                dotCenter,
                dotColor,
                dotSize
            );

            var hwnd = GetForegroundWindow();
            if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out var rect))
            {
                var width = rect.Right - rect.Left;
                var height = rect.Bottom - rect.Top;
                if (width > 40 && height > 40)
                {
                    var bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
                    bounds.Inflate(3, 3);
                    _windowOutlineForm.UpdateOutline(bounds, borderStart, borderEnd, borderThickness, segmentT);
                }
                else
                {
                    _windowOutlineForm.Hide();
                }
            }
            else
            {
                _windowOutlineForm.Hide();
            }
        }

        private Point GetOverlayAnchorPoint()
        {
            if (TryGetCaretScreenPoint(out var caretPoint))
            {
                _lastAnchorPoint = caretPoint;
                return caretPoint;
            }

            if (_lastAnchorPoint != Point.Empty)
            {
                return _lastAnchorPoint;
            }

            var fallbackPoint = TryGetForegroundWindowCenter(out var windowCenter)
                ? windowCenter
                : Cursor.Position;

            _lastAnchorPoint = fallbackPoint;
            return fallbackPoint;
        }

        private bool TryGetCaretScreenPoint(out Point caretPoint)
        {
            caretPoint = Point.Empty;

            try
            {
                var foreground = GetForegroundWindow();
                if (foreground == IntPtr.Zero)
                {
                    return false;
                }

                uint threadId = GetWindowThreadProcessId(foreground, out _);
                if (threadId == 0)
                {
                    return false;
                }

                var info = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
                if (!GetGUIThreadInfo(threadId, ref info) || info.hwndCaret == IntPtr.Zero)
                {
                    return false;
                }

                var pt = new POINT
                {
                    X = info.rcCaret.Left,
                    Y = info.rcCaret.Bottom
                };

                if (!ClientToScreen(info.hwndCaret, ref pt))
                {
                    return false;
                }

                caretPoint = new Point(pt.X, pt.Y);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static Color LerpColor(Color from, Color to, double t)
        {
            t = Math.Clamp(t, 0.0, 1.0);

            int a = (int)(from.A + ((to.A - from.A) * t));
            int r = (int)(from.R + ((to.R - from.R) * t));
            int g = (int)(from.G + ((to.G - from.G) * t));
            int b = (int)(from.B + ((to.B - from.B) * t));
            return Color.FromArgb(a, r, g, b);
        }

        private bool TryGetForegroundWindowCenter(out Point center)
        {
            center = Point.Empty;

            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            if (!GetWindowRect(hwnd, out var rect))
            {
                return false;
            }

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0)
            {
                return false;
            }

            center = new Point(rect.Left + (width / 2), rect.Top + (height / 2));
            return true;
        }

        private void Post(Action action)
        {
            if (_disposed) return;

            if (_uiControl.IsHandleCreated)
            {
                _uiControl.BeginInvoke(action);
            }
            else
            {
                action();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_uiControl.IsHandleCreated)
            {
                _uiControl.Invoke(new Action(() =>
                {
                    _animationTimer.Stop();
                    _animationTimer.Dispose();
                    _cursorGlowForm.Dispose();
                    _windowOutlineForm.Dispose();
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                    _uiControl.Dispose();
                }));
            }
            else
            {
                _animationTimer.Stop();
                _animationTimer.Dispose();
                _cursorGlowForm.Dispose();
                _windowOutlineForm.Dispose();
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _uiControl.Dispose();
            }
        }

        private abstract class OverlayFormBase : Form
        {
            protected override bool ShowWithoutActivation => true;

            protected override CreateParams CreateParams
            {
                get
                {
                    const int WS_EX_TOOLWINDOW = 0x00000080;
                    const int WS_EX_LAYERED = 0x00080000;
                    const int WS_EX_TRANSPARENT = 0x00000020;
                    const int WS_EX_NOACTIVATE = 0x08000000;

                    var cp = base.CreateParams;
                    cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;
                    return cp;
                }
            }

            protected OverlayFormBase()
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                TopMost = true;
                StartPosition = FormStartPosition.Manual;
                BackColor = Color.Black;
                TransparencyKey = Color.Black;
                DoubleBuffered = true;
            }
        }

        private sealed class CursorGlowForm : OverlayFormBase
        {
            private Color _fillColor = Color.FromArgb(230, 96, 122, 255);

            public void UpdateGlow(Point dotCenter, Color fillColor, int size)
            {
                _fillColor = fillColor;

                Bounds = new Rectangle(dotCenter.X - (size / 2), dotCenter.Y - (size / 2), size, size);

                if (!Visible)
                {
                    Show();
                }

                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);

                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var area = new Rectangle(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));

                using var brush = new SolidBrush(_fillColor);
                e.Graphics.FillEllipse(brush, area);
            }
        }

        private sealed class WindowOutlineForm : OverlayFormBase
        {
            private Color _gradientStartColor = Color.FromArgb(160, 96, 122, 255);
            private Color _gradientEndColor = Color.FromArgb(120, 130, 160, 255);
            private float _gradientAngle = 45f;
            private int _thickness = 3;

            public void UpdateOutline(Rectangle bounds, Color gradientStartColor, Color gradientEndColor, int thickness, double flow)
            {
                _gradientStartColor = gradientStartColor;
                _gradientEndColor = gradientEndColor;
                _gradientAngle = 20f + (float)(flow * 140f);
                _thickness = Math.Max(2, thickness);
                Bounds = bounds;

                if (!Visible)
                {
                    Show();
                }

                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);

                if (Width <= 0 || Height <= 0)
                {
                    return;
                }

                e.Graphics.SmoothingMode = SmoothingMode.None;
                var inset = _thickness / 2f;
                var rect = new RectangleF(inset, inset, Width - _thickness, Height - _thickness);

                using var brush = new LinearGradientBrush(new Rectangle(0, 0, Width, Height), _gradientStartColor, _gradientEndColor, _gradientAngle);
                using var pen = new Pen(brush, _thickness);
                e.Graphics.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
            }
        }
    }
}