# Copilot Input

Version: 1.0

## Product Value
Copilot Input is a Windows voice-to-text assistant for real productivity workflows. It captures your speech, understands the on-screen context, and inserts or rewrites text in the active app.

It is designed for users who frequently type in editors, documents, chat tools, and internal systems, and want faster drafting, rewriting, translation, and reply generation.

## Core Features
- **Hold-to-dictate trigger**: hold `CapsLock` to start, release to submit.
- **Context-aware transcription**: combines microphone input with current window screenshot for better intent understanding.
- **Direct text injection**: writes output into the currently focused input field.
- **Selection-aware rewriting**: when text is selected, the result replaces the selection.
- **Process filtering**: configurable allow/block process list to control where the hook is active.
- **Realtime Azure OpenAI integration**: optimized for low-latency dictation and instruction-following edits.

## Typical Value Scenarios

### 1) Fast drafting
- Speak directly to produce paragraphs, notes, tickets, or commit messages.
- Ideal when typing speed is slower than speaking speed.

Example:
- Voice: “Draft a short status update: integration done, QA tomorrow, release on Friday.”
- Result: a polished status update inserted into the active editor.

### 2) Rewrite selected text
- Select existing text, then give an instruction to improve tone, grammar, structure, or clarity.
- Useful for polishing emails, PR descriptions, and documentation.

Example:
- Selected text: “we fixed most issue and maybe deploy tomrrow”
- Voice: “Make this professional and concise.”
- Result: rewritten professional sentence replacing the selection.

### 3) Translation and localization
- Dictate source text and request target language output.
- Good for bilingual communication and localized drafts.

Example:
- Voice: “Translate to Chinese: Please review the attached proposal by end of day.”
- Result: Chinese translation inserted in place.

### 4) Contextual reply generation
- In chat or ticket systems, dictate intent like “reply politely and ask for logs.”
- Screenshot context helps generate a response aligned with the visible conversation.

## Configuration

Edit `appsettings.json`.

### Required settings
- `AzureOpenAI:Endpoint`
- `AzureOpenAI:ApiKey`
- `AzureOpenAI:DeploymentName`

Example endpoint format:
`wss://<resource>.openai.azure.com/openai/realtime?api-version=2024-10-01-preview&deployment=<deployment-name>`

### Common behavior settings
- `Input:ActivationKeys`: trigger keys (default: `CapsLock`)
- `Input:HoldToDictateThresholdMs`: hold threshold before dictation starts
- `Input:AllowedProcesses`: whitelist of process names (`*` means all)
- `Input:BlockedProcesses`: blacklist of process names

### Reliability recommendations
- Run with administrator privileges if global hook does not work in elevated target apps.
- Keep `AllowedProcesses` aligned with your daily tools to reduce unintended triggers.

## Quick Start
1. Create config from template:
   - `Copy-Item appsettings.template.json appsettings.json`
2. Fill Azure OpenAI credentials in `appsettings.json`.
3. Build and run:
   - `dotnet restore`
   - `dotnet run`

## Build (Release)
- `dotnet build CopilotInput.sln -c Release`

Release output:
- `bin/Release/net8.0-windows/CopilotInput.exe`

## Auto-start (Optional)
1. Build Release.
2. Open startup folder: `Win + R` → `shell:startup`
3. Create a shortcut to `CopilotInput.exe` in that folder.

## Requirements
- Windows
- .NET 8 SDK
- Azure OpenAI resource with realtime deployment