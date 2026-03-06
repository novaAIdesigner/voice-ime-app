using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Threading;

namespace CopilotInput.Core
{
    public class KeyHook
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;

        private LowLevelKeyboardProc _proc;
        private IntPtr _hookID = IntPtr.Zero;
        private int _activationIsDown;
        private readonly HashSet<int> _activationVkCodes;
        private readonly HashSet<int> _pressedActivationVkCodes;
        private readonly object _pressedKeysLock = new object();
        private readonly Func<bool> _shouldHandleActivation;

        public event EventHandler ActivationPressed;
        public event EventHandler ActivationReleased;

        public KeyHook(IEnumerable<Keys> activationKeys, Func<bool> shouldHandleActivation = null)
        {
            _proc = HookCallback;
            _activationVkCodes = new HashSet<int>();
            _pressedActivationVkCodes = new HashSet<int>();

            foreach (var key in activationKeys)
            {
                _activationVkCodes.Add((int)key);
            }

            if (_activationVkCodes.Count == 0)
            {
                _activationVkCodes.Add((int)Keys.RShiftKey);
            }

            _shouldHandleActivation = shouldHandleActivation;
        }

        public void InstallHook()
        {
            _hookID = SetHook(_proc);
            if (_hookID == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to install keyboard hook.");
            }
        }

        public void UninstallHook()
        {
            UnhookWindowsHookEx(_hookID);
        }

        private IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            IntPtr hookId = IntPtr.Zero;

            try
            {
                using (Process curProcess = Process.GetCurrentProcess())
                using (ProcessModule curModule = curProcess.MainModule)
                {
                    IntPtr moduleHandle = IntPtr.Zero;
                    if (curModule != null)
                    {
                        moduleHandle = GetModuleHandle(curModule.ModuleName);
                    }

                    hookId = SetWindowsHookEx(WH_KEYBOARD_LL, proc, moduleHandle, 0);
                }
            }
            catch
            {
            }

            if (hookId == IntPtr.Zero)
            {
                hookId = SetWindowsHookEx(WH_KEYBOARD_LL, proc, IntPtr.Zero, 0);
            }

            return hookId;
        }

        #pragma warning disable CS0649
        private struct KBDLLHOOKSTRUCT
        {
            public int vkCode;
            public int scanCode;
            public int flags;
            public int time;
            public IntPtr dwExtraInfo;
        }
        #pragma warning restore CS0649

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                KBDLLHOOKSTRUCT kbStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                
                // Check if the event is injected (bit 4)
                if ((kbStruct.flags & 0x10) != 0)
                {
                    return CallNextHookEx(_hookID, nCode, wParam, lParam);
                }

                if (_activationVkCodes.Contains(kbStruct.vkCode))
                {
                    bool shouldHandle = _shouldHandleActivation?.Invoke() ?? true;
                    bool isKeyDown = wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN;
                    bool isKeyUp = wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP;

                    if (!shouldHandle)
                    {
                        if (isKeyUp)
                        {
                            lock (_pressedKeysLock)
                            {
                                _pressedActivationVkCodes.Remove(kbStruct.vkCode);
                                if (_pressedActivationVkCodes.Count == 0)
                                {
                                    Interlocked.Exchange(ref _activationIsDown, 0);
                                }
                            }
                        }
                        return CallNextHookEx(_hookID, nCode, wParam, lParam);
                    }

                    if (isKeyDown)
                    {
                        bool firstDown = false;
                        lock (_pressedKeysLock)
                        {
                            if (_pressedActivationVkCodes.Add(kbStruct.vkCode) && _pressedActivationVkCodes.Count == 1)
                            {
                                firstDown = true;
                            }
                        }

                        if (firstDown && Interlocked.Exchange(ref _activationIsDown, 1) == 0)
                        {
                            ActivationPressed?.Invoke(this, EventArgs.Empty);
                        }
                        return (IntPtr)1;
                    }

                    if (isKeyUp)
                    {
                        bool allReleased = false;
                        lock (_pressedKeysLock)
                        {
                            _pressedActivationVkCodes.Remove(kbStruct.vkCode);
                            allReleased = _pressedActivationVkCodes.Count == 0;
                        }

                        if (allReleased && Interlocked.Exchange(ref _activationIsDown, 0) == 1)
                        {
                            ActivationReleased?.Invoke(this, EventArgs.Empty);
                        }
                        return (IntPtr)1;
                    }
                }
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
    }
}