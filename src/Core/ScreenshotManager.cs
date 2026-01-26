using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace VoiceImeApp.Core
{
    public class ScreenshotManager
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private string _savePath;
        private bool _debugMode;

        public ScreenshotManager(bool debugMode, string savePath)
        {
            _debugMode = debugMode;
            _savePath = savePath;
            if (_debugMode && !string.IsNullOrEmpty(_savePath) && !System.IO.Directory.Exists(_savePath))
            {
                try { System.IO.Directory.CreateDirectory(_savePath); } catch {}
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        public string GetActiveWindowTitle()
        {
            IntPtr handle = GetForegroundWindow();
            if (handle == IntPtr.Zero) return "Unknown";

            const int nChars = 256;
            var buff = new System.Text.StringBuilder(nChars);
            if (GetWindowText(handle, buff, nChars) > 0)
            {
                return buff.ToString();
            }
            return "Unknown Application";
        }

        public byte[] CaptureActiveWindow()
        {
            IntPtr handle = GetForegroundWindow();
            if (handle == IntPtr.Zero) return null;

            if (GetWindowRect(handle, out RECT rect))
            {
                int width = rect.Right - rect.Left;
                int height = rect.Bottom - rect.Top;

                if (width <= 0 || height <= 0) return null;

                using (var bitmap = new Bitmap(width, height))
                {
                    using (var g = Graphics.FromImage(bitmap))
                    {
                        g.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(width, height));
                    }

                    if (_debugMode && !string.IsNullOrEmpty(_savePath))
                    {
                        try
                        {
                            string filename = System.IO.Path.Combine(_savePath, $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                            bitmap.Save(filename, ImageFormat.Png);
                            Console.WriteLine($"[Debug] Screenshot saved to: {filename}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to save screenshot: {ex.Message}");
                        }
                    }
                    
                    using (var stream = new System.IO.MemoryStream())
                    {
                        bitmap.Save(stream, ImageFormat.Png);
                        return stream.ToArray();
                    }
                }
            }
            return null;
        }
    }
}