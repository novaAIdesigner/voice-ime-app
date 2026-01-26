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

### Basic Operation
- **Tap Space**: Types a normal space character.
- **Hold Space**: Starts dictation. Release to finish.

### Capabilities

The app sends a **screenshot** of your active window to the AI along with your voice. This allows two powerful modes of interaction:

#### 1. Direct Dictation
Simply speak what you want to type. Use this for writing new content.
- *Voice*: "Hello World"
- *Output*: "Hello World"

#### 2. Context-Aware Editing & Refinement
You can **select text** (highlight it with your mouse) before holding Space. The AI will see the selection and can modify it based on your instructions. The generated text will automatically replace your selection.

**Examples:**

*   **Refining Text**:
    1. Select a rough draft sentence: *"i dont wanna go today"*
    2. Hold Space and say: *"Make this formal and polite."*
    3. Result: *"I regret to inform you that I will be unable to attend today."*

*   **Translation**:
    1. Hold Space and say: *"Put it in Chinese. I dont like to go today"*
    2. Result: The text is replaced with the Chinese translation.

*   **Replying to Messages**:
    1. Even without selecting, if you are in a chat app (like Teams or Discord), the AI sees the chat history from the screenshot.
    2. Hold Space and say: *"Reply that I'm looking into it."*
    3. Result: *"Thanks for the update, I'm looking into it right now."* (Contextually formatted)

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