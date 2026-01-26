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
- .NET 6.0 SDK
- Azure OpenAI Service with **gpt-4o-realtime-preview** deployment.

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