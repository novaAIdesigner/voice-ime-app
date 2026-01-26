# Voice IME Application

## Overview
The Voice IME Application is a tool that allows users to input text using voice commands. It captures audio input, processes it through the Azure OpenAI API for transcription, and injects the transcribed text into the active input field. The application also supports taking screenshots of the current window.

## Features
- Voice input for text transcription
- Screenshot capture of the current window
- Interaction with Azure OpenAI API (Whisper)
- Global keyboard hook for SPACE key (Hold to dictate, Tap to space)

## Configuration
1. Open `appsettings.json`.
2. Set your Azure OpenAI credentials for the **Realtime API**:
   - `Endpoint`: Your Realtime API connection string.  
     Example: `wss://<resource>.openai.azure.com/openai/realtime?api-version=2024-10-01-preview&deployment=gpt-4o-realtime-preview`
   - `ApiKey`: Your API Key.

## Usage
1. Run the application (Administrator privileges may be required).
2. Hold **Space** to dictate:
   - Captures active window screen & title.
   - Connects to Realtime API.
   - Streams audio.
3. Release **Space**:
   - Stops recording.
   - The model (gpt-4o-realtime) processes audio + image context.
   - Resulting text is injected into the active window.

## Requirements
- Windows OS
- .NET 8.0 SDK
- Azure OpenAI Service with **gpt-4o-realtime-preview** deployment.

## Getting Started

1. **Clone the repository**
   ```bash
   git clone <repository-url>
   cd voice-ime-app
   ```

2. **Configure Settings**  
   Create `appsettings.json` from the template:
   ```powershell
   Copy-Item appsettings.template.json appsettings.json
   ```
   Open `appsettings.json` and fill in your `ApiKey` and `Endpoint`.

3. **Build and Run**
   ```powershell
   dotnet restore
   dotnet run
   ```

## Auto-Start Configuration (Optional)
To start the app automatically when Windows starts:

> **Recommendation**: Set `"DebugMode": false` in `appsettings.json` before enabling auto-start to prevent large log files from filling up your disk.

1. Build the application (ensure you have an `.exe` generated):
   ```powershell
   dotnet build --configuration Release
   ```
2. Locate the executable (e.g., `bin\Release\net8.0-windows\VoiceImeApp.exe`).
3. Press `Win + R`, type `shell:startup`, and press **Enter** to open the Startup folder.
4. Create a **Shortcut** to `VoiceImeApp.exe` and place it in this folder.

## Project Structure
```
voice-ime-app
├── src
│   ├── Core
│   │   ├── KeyHook.cs          # Global Keyboard Hook (Space Key)
│   │   ├── ScreenshotManager.cs# Active Window Capture
│   │   └── TextInjector.cs     # SendInput (Unicode)
│   ├── Services
│   │   ├── AudioRecorder.cs    # NAudio Recording
│   │   └── AzureOpenAIService.cs # OpenAI Whisper API Client
│   ├── MainImeWrapper.cs       # Logic Orchestration
│   └── Program.cs              # Entry Point
├── appsettings.json
└── VoiceImeApp.csproj
```