# RoleAxis Desktop

Windows desktop command center for Interview Assistant, Meeting Assistant, Presentation Assistant, Local Vault Agent, and Evidence Scanner.

## Requirements

- Windows
- .NET 8 SDK
- An OpenAI API key

## Run Locally

Prefer an environment variable for local development:

```powershell
$env:OPENAI_API_KEY = "sk-..."
dotnet run --project services\career\interview-assistant\RoleAxis.InterviewAssistant.csproj
```

You can also copy `config.example.json` to `config.json` beside the project or published executable. `config.json` is intentionally ignored by git because it can contain a real API key.

## Build

```powershell
dotnet build services\career\interview-assistant\RoleAxis.InterviewAssistant.csproj --configuration Release
```

## Publish For Installer

```powershell
services\career\interview-assistant\build-installer.ps1
```

The installer script publishes `RoleAxis.Desktop.exe`, runs Inno Setup, and creates `RoleAxis-Desktop-Setup.exe` in `installer-output\`.
