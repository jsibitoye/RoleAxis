# RoleAxis Interview Assistant

Windows desktop assistant for live interview preparation inside the RoleAxis Career workspace.

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
dotnet publish services\career\interview-assistant\RoleAxis.InterviewAssistant.csproj --configuration Release --runtime win-x64 --self-contained false
```

The Inno Setup script reads from `bin\Release\net8.0-windows\win-x64\publish\` and creates `RoleAxis-Interview-Assistant-Setup.exe` in `installer-output\`.
