using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms; 
using Microsoft.Extensions.Configuration;
using VoiceImeApp.Core;
using VoiceImeApp.Services;

namespace VoiceImeApp
{
    public class MainImeWrapper
    {
        private readonly KeyHook _keyHook;
        private readonly ScreenshotManager _screenshotManager;
        private readonly AudioRecorder _audioRecorder;
        private readonly AzureOpenAIService _azureOpenAIService;
        private readonly TextInjector _textInjector;

        private bool _isRecording;
        private DateTime _pressStartTime;
        private byte[] _lastScreenshot;
        private string _lastActiveWindow;
        private CancellationTokenSource _holdCts;
        private bool _isDictating;
        private List<byte> _audioBuffer = new List<byte>();
        private object _bufferLock = new object();

        private bool _debugMode;
        private string _logPath;
        private System.IO.FileStream _audioDebugStream;

        public MainImeWrapper(IConfiguration config)
        {
            _debugMode = bool.Parse(config["General:DebugMode"] ?? "false");
            _logPath = config["General:LogOutputDirectory"];

            if (_debugMode && !string.IsNullOrEmpty(_logPath))
            {
                if (!System.IO.Directory.Exists(_logPath))
                    System.IO.Directory.CreateDirectory(_logPath);
            }

            _keyHook = new KeyHook();
            // Pass debug settings to ScreenshotManager
            _screenshotManager = new ScreenshotManager(_debugMode, _logPath ?? config["Screenshot:SavePath"]);
            
            _audioRecorder = new AudioRecorder();
            _azureOpenAIService = new AzureOpenAIService();
            _textInjector = new TextInjector();

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
                if (!_isRecording) return;
                
                // Debug Save
                if (_audioDebugStream != null)
                {
                    _audioDebugStream.Write(audioData, 0, audioData.Length);
                }

                if (_isDictating)
                {
                    // Directly send
                     await _azureOpenAIService.SendAudioAppendAsync(audioData);
                }
                else
                {
                    // Buffer until we decide it is a dictation
                    lock(_bufferLock)
                    {
                        _audioBuffer.AddRange(audioData);
                    }
                }
            };
        }

        public void Start()
        {
            _keyHook.SpacePressed += OnSpacePressed;
            _keyHook.SpaceReleased += OnSpaceReleased;
            _keyHook.InstallHook();
            Console.WriteLine("IME Started. Press SPACE (Short) for Space, Hold for Dictation...");
        }

        public async void Stop()
        {
            _keyHook.UninstallHook();
            await _azureOpenAIService.CloseAsync();
        }

        private async void OnSpacePressed(object sender, EventArgs e)
        {
            if (_isRecording) return;
            _isRecording = true;
            _isDictating = false;
            _pressStartTime = DateTime.Now;
            lock(_bufferLock) _audioBuffer.Clear();

            _holdCts = new CancellationTokenSource();

            // Start Audio immediately to catch phrases, but buffer it
            _audioRecorder.StartRecording();

            try
            {
                // Wait for hold duration (e.g. 300ms)
                await Task.Delay(300, _holdCts.Token);
                
                // If we get here, it is a HOLD
                _isDictating = true;
                Console.WriteLine("Hold Detected: Starting Dictation Workflow...");

                // Start Debug Audio File (Deferred)
                if (_debugMode && !string.IsNullOrEmpty(_logPath))
                {
                    try
                    {
                        string audioFile = System.IO.Path.Combine(_logPath, $"audio_{DateTime.Now:yyyyMMdd_HHmmss}.pcm");
                        _audioDebugStream = new System.IO.FileStream(audioFile, System.IO.FileMode.Create);
                        Console.WriteLine($"[Debug] Recording audio to: {audioFile}");
                        
                        lock(_bufferLock) 
                        {
                            if (_audioBuffer.Count > 0)
                            {
                                var existing = _audioBuffer.ToArray();
                                _audioDebugStream.Write(existing, 0, existing.Length);
                            }
                        }
                    }
                    catch { }
                }

                // 1. Capture Screenshot
                _lastScreenshot = _screenshotManager.CaptureActiveWindow();
                _lastActiveWindow = _screenshotManager.GetActiveWindowTitle();
                Console.WriteLine($"Context: {_lastActiveWindow}");

                // 2. Connect
                await _azureOpenAIService.ConnectAsync();
                await _azureOpenAIService.ResetSessionAsync(_lastActiveWindow, _lastScreenshot);

                // 3. Flush Buffer
                byte[] buffered;
                lock(_bufferLock) buffered = _audioBuffer.ToArray();
                if (buffered.Length > 0)
                {
                    await _azureOpenAIService.SendAudioAppendAsync(buffered);
                }
            }
            catch (TaskCanceledException)
            {
                // Released before timeout -> Short press
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error Starting: {ex.Message}");
                _isRecording = false;
            }
        }

        private async void OnSpaceReleased(object sender, EventArgs e)
        {
            if (!_isRecording) return;
            _isRecording = false;

            // Stop waiting for hold timeout
            _holdCts?.Cancel();
            
            // Close debug stream
            _audioDebugStream?.Dispose();
            _audioDebugStream = null;

            try
            {
                // Stop Mic 
                var remainingAudio = await _audioRecorder.StopRecordingAsync();
                
                if (!_isDictating)
                {
                    // Short Press - Just type space
                    Console.WriteLine("Short Press: Space");
                    _textInjector.InjectText(" ");
                    return;
                }

                // If Dictating, send remainder and commit
                if (remainingAudio != null && remainingAudio.Length > 0)
                {
                    await _azureOpenAIService.SendAudioAppendAsync(remainingAudio);
                }

                Console.WriteLine("Generating Realtime Response...");
                string finalText = await _azureOpenAIService.CommitAndGenerateResponseAsync();
                
                Console.WriteLine($"Final Inferred Text: {finalText}");

                if (!string.IsNullOrWhiteSpace(finalText))
                {
                     _textInjector.InjectText(finalText + " ");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error Processing: {ex.Message}");
            }
        }
    }
}