using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace CopilotInput.Services
{
    public class AzureOpenAIService
    {
        private string _endpoint;
        private string _apiKey;
        private string _deploymentName;
        private ClientWebSocket _ws;
        private bool _isConnected;
        private ConcurrentQueue<string> _responseQueue = new ConcurrentQueue<string>();
        private Task _receiveTask;
        private CancellationTokenSource _cts;

        private bool _debugMode;
        private string _logPath;
        private string _sessionLogFile;
        private bool _verboseMode; 
        private string _systemInstructions;

        public void Initialize(string endpoint, string apiKey, string deploymentName, string systemInstructions)
        {
            // Realtime API endpoint format: wss://{resource}.openai.azure.com/openai/realtime?api-version=2024-10-01-preview&deployment={deployment}
            _endpoint = endpoint.Replace("https://", "wss://").Replace("http://", "ws://");
            _apiKey = apiKey;
            _deploymentName = deploymentName;
            _systemInstructions = systemInstructions;
        }

        public void EnableDebugLogging(bool enabled, string path, bool verbose = false)
        {
            _debugMode = enabled;
            _logPath = path;
            _verboseMode = verbose;
        }
        
        private void LogDebug(string type, string content)
        {
            if (!_debugMode || string.IsNullOrEmpty(_logPath)) return;
            try 
            {
                if (string.IsNullOrEmpty(_sessionLogFile))
                {
                    _sessionLogFile = Path.Combine(_logPath, $"gpt_session_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                }
                File.AppendAllText(_sessionLogFile, $"[{DateTime.Now:HH:mm:ss.fff}] [{type}] {content}\n");
            }
            catch { }
        }

        public async Task ConnectAsync()
        {
            if (_isConnected && _ws?.State == WebSocketState.Open) return;

            _ws = new ClientWebSocket();
            _ws.Options.SetRequestHeader("api-key", _apiKey);
            _ws.Options.SetRequestHeader("OpenAI-Beta", "realtime=v1");

            Uri uri = new Uri(_endpoint);
            Console.WriteLine($"[Realtime] Connecting to: {uri}");
            
            await _ws.ConnectAsync(uri, CancellationToken.None);
            Console.WriteLine("[Realtime] Connected.");
            _isConnected = true;
            
            _cts = new CancellationTokenSource();
            _receiveTask = Task.Run(ReceiveLoop);
        }

        public async Task ResetSessionAsync(string windowTitle, byte[] screenshot)
        {
            // 1. Session Update (configure for Text output, Manual Turn Detection, Instructions including screenshot reference)
            var sessionUpdate = new
            {
                type = "session.update",
                session = new
                {
                    instructions = _systemInstructions,
                    modalities = new[] { "text" },
                    input_audio_format = "pcm16",
                    turn_detection = (object)null // Manual interaction
                }
            };
            await SendJsonAsync(sessionUpdate);

            // 2. Add Context Item (Screenshot + Window Title)
            var imageBase64 = Convert.ToBase64String(screenshot);
            var itemCreate = new
            {
                type = "conversation.item.create",
                item = new
                {
                    type = "message",
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = $"[Active Window: {windowTitle}]" },
                         new {
                             type = "image_url",
                             image_url = new {
                                 url = $"data:image/png;base64,{imageBase64}" 
                             }
                         }
                    }
                }
            };
            
            object logObj = null;
            if (!_verboseMode)
            {
                 logObj = new
                 {
                    type = "conversation.item.create",
                    item = new
                    {
                        type = "message",
                        role = "user",
                        content = new object[]
                        {
                            new { type = "text", text = $"[Active Window: {windowTitle}]" },
                            new { type = "image_url", image_url = new { url = "data:image/png;base64,[BASE64_IMAGE_OMITTED]" } }
                        }
                    }
                 };
            }
            await SendJsonAsync(itemCreate, logObj);
        }

        public async Task SendAudioAppendAsync(byte[] audioData)
        {
            if (!_isConnected) return;
            var base64 = Convert.ToBase64String(audioData);
            var msg = new
            {
                type = "input_audio_buffer.append",
                audio = base64
            };
            
            object logObj = null;
            if (!_verboseMode)
            {
                logObj = new 
                {
                    type = "input_audio_buffer.append",
                    audio = "[BASE64_AUDIO_OMITTED]"
                };
            }
            await SendJsonAsync(msg, logObj);
        }

        public async Task<string> CommitAndGenerateResponseAsync()
        {
            // Drain queue
            while (_responseQueue.TryDequeue(out _)) { }

            // Commit Buffer
            await SendJsonAsync(new { type = "input_audio_buffer.commit" });
            
            // Request Response
            await SendJsonAsync(new { type = "response.create" });

            string finalResponse = "";
            bool isDone = false;
            
            // Simple timeout based wait loop (not production robust)
            DateTime start = DateTime.Now;
            while (!isDone && (DateTime.Now - start).TotalSeconds < 15)
            {
                if (_responseQueue.TryDequeue(out var eventJson))
                {
                    try 
                    {
                        using (var doc = JsonDocument.Parse(eventJson))
                        {
                            var type = doc.RootElement.GetProperty("type").GetString();
                            if (type == "response.text.delta")
                            {
                                if (doc.RootElement.TryGetProperty("delta", out var delta))
                                    finalResponse += delta.GetString();
                            }
                            else if (type == "response.done")
                            {
                                isDone = true;
                                if (doc.RootElement.TryGetProperty("response", out var respObj))
                                {
                                    if (respObj.TryGetProperty("usage", out var usageObj))
                                    {
                                        int inTokens = 0, outTokens = 0;
                                        if (usageObj.TryGetProperty("input_tokens", out var it)) inTokens = it.GetInt32();
                                        if (usageObj.TryGetProperty("output_tokens", out var ot)) outTokens = ot.GetInt32();
                                        Console.WriteLine($"[Token Usage] Input: {inTokens}, Output: {outTokens}");
                                    }
                                }
                            }
                            else if (type == "error")
                            {
                                isDone = true;
                            }
                        }
                    } catch {}
                }
                else
                {
                    await Task.Delay(10);
                }
            }
            return finalResponse;
        }

        private async Task SendJsonAsync(object data, object logData = null)
        {
            if (_ws.State != WebSocketState.Open) return;
            var json = JsonSerializer.Serialize(data);
            
            if (logData != null)
                LogDebug("SEND", JsonSerializer.Serialize(logData));
            else
                LogDebug("SEND", json);

            var buffer = Encoding.UTF8.GetBytes(json);
            await _ws.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        private async Task ReceiveLoop()
        {
            var buffer = new byte[1024 * 64]; 
            while (_ws.State == WebSocketState.Open && !_cts.IsCancellationRequested)
            {
                try
                {
                    var ms = new MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                        if (result.MessageType == WebSocketMessageType.Close) 
                            break;
                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close) break;

                    ms.Seek(0, SeekOrigin.Begin);
                    using (var reader = new StreamReader(ms, Encoding.UTF8))
                    {
                        var json = await reader.ReadToEndAsync();
                        LogDebug("RECV", json);
                        _responseQueue.Enqueue(json);
                        
                        // Optional: Log errors immediately
                        if (json.Contains("\"type\": \"error\""))
                        {
                            Console.WriteLine($"[Realtime Error]: {json}");
                        }
                    }
                }
                catch (Exception ex)
                {
                     Console.WriteLine($"[WebSocket Receive Error]: {ex.Message}");
                     break; 
                }
            }
        }
        
        public async Task CloseAsync()
        {
            _cts?.Cancel();
            if (_ws != null)
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            _isConnected = false;
        }
    }
}