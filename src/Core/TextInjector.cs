using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Threading;

namespace CopilotInput.Core
{
    public class TextInjector
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        private const int INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_UNICODE = 0x0004;

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public int type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)]
            public MOUSEINPUT mi;
            [FieldOffset(0)]
            public KEYBDINPUT ki;
            [FieldOffset(0)]
            public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        public void InjectText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            // Normalize line endings to \n
            // NEW METHOD: Clipboard Paste (Ctrl+V) with Backup/Restore
            // Run everything in a single STA thread to manage Clipboard ownership
            Exception threadEx = null;
            Thread staThread = new Thread(() =>
            {
                IDataObject backupData = null;
                try
                {
                    // 1. Backup specific common formats (Text/Image) manually because legacy IDataObject 
                    //    reference might become invalid once we overwrite the clipboard.
                    //    However, simply holding the IDataObject reference is the standard attempt.
                    //    We try to keep the data alive.
                    var rawData = Clipboard.GetDataObject();
                    if (rawData != null)
                    {
                        // Create a fresh DataObject to hold the data in memory (Deep Copy for common types)
                        backupData = new DataObject();
                        // Copy common formats if present
                        if (rawData.GetDataPresent(DataFormats.Text)) backupData.SetData(DataFormats.Text, rawData.GetData(DataFormats.Text));
                        if (rawData.GetDataPresent(DataFormats.UnicodeText)) backupData.SetData(DataFormats.UnicodeText, rawData.GetData(DataFormats.UnicodeText));
                        if (rawData.GetDataPresent(DataFormats.Bitmap)) backupData.SetData(DataFormats.Bitmap, rawData.GetData(DataFormats.Bitmap));
                        if (rawData.GetDataPresent(DataFormats.Html)) backupData.SetData(DataFormats.Html, rawData.GetData(DataFormats.Html));
                        // If it's something else (files, proprietary), we might lose it, but this covers 99% of text/image usage.
                    }
                }
                catch { /* Ignore backup failures */ }

                try
                {
                    // 2. Set Clipboard to new text
                    // Retry logic for clipboard locking
                    bool clipboardSet = false;
                    for (int i = 0; i < 5; i++)
                    {
                        try
                        {
                            Clipboard.SetText(text);
                            clipboardSet = true;
                            break;
                        }
                        catch
                        {
                            Thread.Sleep(50);
                        }
                    }

                    if (!clipboardSet) throw new Exception("Could not lock clipboard.");

                    // 3. Send Ctrl+V
                    var inputs = new List<INPUT>();

                    // Ctrl Down
                    inputs.Add(new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = 0x11, dwFlags = 0 } } });
                    // V Down
                    inputs.Add(new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = 0x56, dwFlags = 0 } } });
                    // V Up
                    inputs.Add(new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = 0x56, dwFlags = KEYEVENTF_KEYUP } } });
                    // Ctrl Up
                    inputs.Add(new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = 0x11, dwFlags = KEYEVENTF_KEYUP } } });

                    SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf(typeof(INPUT)));

                    // 4. Wait for Paste to complete
                    Thread.Sleep(200); 

                    // 5. Restore Clipboard
                    if (backupData != null)
                    {
                        // Retry logic for restore
                        for (int i = 0; i < 5; i++)
                        {
                            try { Clipboard.SetDataObject(backupData, true); break; } catch { Thread.Sleep(50); }
                        }
                    }
                }
                catch (Exception ex)
                {
                    threadEx = ex;
                }
            });
            staThread.SetApartmentState(ApartmentState.STA);
            staThread.Start();
            staThread.Join();

            if (threadEx != null)
            {
                Console.WriteLine($"[Error] Injection Failed: {threadEx.Message}");
            }
        }

        public void InjectSpaceKey()
        {
            var inputs = new List<INPUT>
            {
                new INPUT
                {
                    type = INPUT_KEYBOARD,
                    u = new InputUnion
                    {
                        ki = new KEYBDINPUT { wVk = (ushort)Keys.Space, dwFlags = 0 }
                    }
                },
                new INPUT
                {
                    type = INPUT_KEYBOARD,
                    u = new InputUnion
                    {
                        ki = new KEYBDINPUT { wVk = (ushort)Keys.Space, dwFlags = KEYEVENTF_KEYUP }
                    }
                }
            };

            SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf(typeof(INPUT)));
        }

        public void InjectVirtualKeyPress(Keys key)
        {
            var keyCode = (ushort)key;
            var inputs = new List<INPUT>
            {
                new INPUT
                {
                    type = INPUT_KEYBOARD,
                    u = new InputUnion
                    {
                        ki = new KEYBDINPUT { wVk = keyCode, dwFlags = 0 }
                    }
                },
                new INPUT
                {
                    type = INPUT_KEYBOARD,
                    u = new InputUnion
                    {
                        ki = new KEYBDINPUT { wVk = keyCode, dwFlags = KEYEVENTF_KEYUP }
                    }
                }
            };

            SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf(typeof(INPUT)));
        }
    }
}