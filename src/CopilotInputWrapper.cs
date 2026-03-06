using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms; 
using Microsoft.Extensions.Configuration;
using CopilotInput.Core;
using CopilotInput.Services;

namespace CopilotInput
{
    public class CopilotInputWrapper
    {
        private readonly KeyHook _keyHook;
        private readonly ScreenshotManager _screenshotManager;
        private readonly AudioRecorder _audioRecorder;
        private readonly AzureOpenAIService _azureOpenAIService;
        private readonly TextInjector _textInjector;
        private readonly StatusTipNotifier _statusTipNotifier;
        private readonly HashSet<string> _allowedProcesses;
        private readonly HashSet<string> _blockedProcesses;
        private readonly int _holdThresholdMs;
        private readonly bool _singleTapCapsLockPassThrough;

        private bool _isRecording;
        private byte[] _lastScreenshot;
        private string _lastActiveWindow;
        private string _lastActiveProcess;
        private CancellationTokenSource _holdCts;
        private bool _isDictating;
        private Task _dictationPrepTask;

        private bool _debugMode;
        private string _logPath;
        private System.IO.FileStream _audioDebugStream;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        public CopilotInputWrapper(IConfiguration config)
        {
            _debugMode = bool.Parse(config["General:DebugMode"] ?? "false");
            _logPath = config["General:LogOutputDirectory"];
            _holdThresholdMs = int.TryParse(config["General:HoldThresholdMs"], out var holdMs) ? holdMs : 180;

            _allowedProcesses = ParseProcessList(config["General:AllowedProcesses"]);
            _blockedProcesses = ParseProcessList(config["General:BlockedProcesses"]);

            var activationKeys = ParseActivationKeys(config["General:ActivationKeys"], config["General:ActivationKey"]);
            _singleTapCapsLockPassThrough = activationKeys.Contains(Keys.Capital);

            if (_debugMode && !string.IsNullOrEmpty(_logPath))
            {
                if (!System.IO.Directory.Exists(_logPath))
                    System.IO.Directory.CreateDirectory(_logPath);
            }

            _keyHook = new KeyHook(activationKeys, ShouldHandleActivationForCurrentApp);
            // Pass debug settings to ScreenshotManager
            _screenshotManager = new ScreenshotManager(_debugMode, _logPath ?? config["Screenshot:SavePath"]);
            
            _audioRecorder = new AudioRecorder();
            _azureOpenAIService = new AzureOpenAIService();
            _textInjector = new TextInjector();
            _statusTipNotifier = new StatusTipNotifier();

            var section = config.GetSection("AzureOpenAI");
            _azureOpenAIService.Initialize(
                section["Endpoint"],
                section["ApiKey"],
                section["DeploymentName"],
                section["SystemInstructions"]
            );

            // Enable service logging
            if (_debugMode && !string.IsNullOrEmpty(_logPath))
            {
                bool verbose = bool.Parse(config["General:Verbose"] ?? "false");
                _azureOpenAIService.EnableDebugLogging(true, _logPath, verbose);
            }

            _audioRecorder.AudioDataAvailable += async (s, audioData) => 
            {
                if (!_isRecording || !_isDictating) return;
                
                // Debug Save
                if (_audioDebugStream != null)
                {
                    _audioDebugStream.Write(audioData, 0, audioData.Length);
                }

                await _azureOpenAIService.SendAudioAppendAsync(audioData);
            };
        }

        public void Start()
        {
            _keyHook.ActivationPressed += OnActivationPressed;
            _keyHook.ActivationReleased += OnActivationReleased;
            try
            {
                _keyHook.InstallHook();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Hook Error] {ex.Message}");
                _statusTipNotifier.ShowError("Keyboard hook install failed.");
                return;
            }

            _statusTipNotifier.ShowReady();
            _ = _azureOpenAIService.ConnectAsync();
            Console.WriteLine("Copilot Input started. Hold Caps Lock for Dictation...");
        }

        public async void Stop()
        {
            _keyHook.UninstallHook();
            await _azureOpenAIService.CloseAsync();
            _statusTipNotifier.Dispose();
        }

        private async void OnActivationPressed(object sender, EventArgs e)
        {
            if (_isRecording) return;

            var activeProcess = GetForegroundProcessName();
            if (!ShouldHandleProcess(activeProcess)) return;

            _isRecording = true;
            _isDictating = false;
            _statusTipNotifier.ShowListening();

            _holdCts = new CancellationTokenSource();
            _dictationPrepTask = PrepareDictationAsync(_holdCts.Token);

            try
            {
                await Task.Delay(_holdThresholdMs, _holdCts.Token);
                
                _isDictating = true;
                _statusTipNotifier.ShowDictating();
                Console.WriteLine("Hold Detected: Starting Dictation...");

                _audioRecorder.StartRecording();

                if (_dictationPrepTask != null)
                {
                    await _dictationPrepTask;
                }
            }
            catch (TaskCanceledException)
            {
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error Starting: {ex.Message}");
                _statusTipNotifier.ShowError("Failed to start dictation.");
                _isRecording = false;
            }
        }

        private async Task PrepareDictationAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            _lastScreenshot = _screenshotManager.CaptureActiveWindow();
            _lastActiveWindow = _screenshotManager.GetActiveWindowTitle();
            _lastActiveProcess = GetForegroundProcessName();

            Console.WriteLine($"Context: {_lastActiveWindow} ({_lastActiveProcess})");

            await _azureOpenAIService.ConnectAsync();
            token.ThrowIfCancellationRequested();

            if (_lastScreenshot != null)
            {
                await _azureOpenAIService.ResetSessionAsync(_lastActiveWindow, _lastScreenshot);
            }

            if (_debugMode && !string.IsNullOrEmpty(_logPath))
            {
                try
                {
                    string audioFile = System.IO.Path.Combine(_logPath, $"audio_{DateTime.Now:yyyyMMdd_HHmmss}.pcm");
                    _audioDebugStream = new System.IO.FileStream(audioFile, System.IO.FileMode.Create);
                    Console.WriteLine($"[Debug] Recording audio to: {audioFile}");
                }
                catch
                {
                }
            }
        }

        private async void OnActivationReleased(object sender, EventArgs e)
        {
            if (!_isRecording) return;
            
            bool wasDictating = _isDictating;

            _isRecording = false;
            _holdCts?.Cancel();
            
            try
            {
                if (!wasDictating)
                {
                    if (_singleTapCapsLockPassThrough)
                    {
                        _textInjector.InjectVirtualKeyPress(Keys.Capital);
                    }

                    _audioDebugStream?.Dispose();
                    _audioDebugStream = null;
                    _statusTipNotifier.ShowReady();
                    return;
                }

                _statusTipNotifier.ShowProcessing();

                var remainingAudio = await _audioRecorder.StopRecordingAsync();
                
                _audioDebugStream?.Dispose();
                _audioDebugStream = null;

                if (remainingAudio != null && remainingAudio.Length > 0)
                {
                    await _azureOpenAIService.SendAudioAppendAsync(remainingAudio);
                }

                string finalText = await _azureOpenAIService.CommitAndGenerateResponseAsync();
                
                if (!string.IsNullOrWhiteSpace(finalText))
                {
                     _textInjector.InjectText(finalText + " ");
                }

                _statusTipNotifier.ShowReady();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error Processing: {ex.Message}");
                _statusTipNotifier.ShowError("Failed to process speech.");
                _statusTipNotifier.ShowReady();
            }
        }

        private static HashSet<string> ParseProcessList(string value)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(value)) return set;

            var parts = value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var cleaned = part.Trim();
                if (!string.IsNullOrEmpty(cleaned))
                {
                    set.Add(cleaned);
                }
            }

            return set;
        }

        private static List<Keys> ParseActivationKeys(string activationKeysConfig, string legacyActivationKeyConfig)
        {
            var keys = new List<Keys>();

            static bool TryParseActivationKey(string value, out Keys key)
            {
                if (value.Equals("CapsLock", StringComparison.OrdinalIgnoreCase) ||
                    value.Equals("Caps", StringComparison.OrdinalIgnoreCase))
                {
                    key = Keys.Capital;
                    return true;
                }

                return Enum.TryParse(value, true, out key);
            }

            if (!string.IsNullOrWhiteSpace(activationKeysConfig))
            {
                var parts = activationKeysConfig.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    if (TryParseActivationKey(part.Trim(), out Keys parsedKey))
                    {
                        keys.Add(parsedKey);
                    }
                }
            }

            if (keys.Count == 0 && !string.IsNullOrWhiteSpace(legacyActivationKeyConfig))
            {
                if (legacyActivationKeyConfig.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                {
                    keys.Add(Keys.LShiftKey);
                    keys.Add(Keys.RShiftKey);
                }
                else if (TryParseActivationKey(legacyActivationKeyConfig, out Keys parsedLegacyKey))
                {
                    keys.Add(parsedLegacyKey);
                }
            }

            if (keys.Count == 0)
            {
                keys.Add(Keys.Capital);
            }

            return keys;
        }

        private bool ShouldHandleActivationForCurrentApp()
        {
            return ShouldHandleProcess(GetForegroundProcessName());
        }

        private bool ShouldHandleProcess(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName)) return true;
            if (_blockedProcesses.Contains(processName)) return false;
            if (_allowedProcesses.Contains("*")) return true;
            if (_allowedProcesses.Count == 0) return true;
            return _allowedProcesses.Contains(processName);
        }

        private static string GetForegroundProcessName()
        {
            try
            {
                var hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return string.Empty;

                GetWindowThreadProcessId(hwnd, out var pid);
                if (pid == 0) return string.Empty;

                using var process = Process.GetProcessById((int)pid);
                return process.ProcessName ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}