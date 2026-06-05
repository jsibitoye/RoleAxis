param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$ProjectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectFile = Join-Path $ProjectDir "RoleAxis.InterviewAssistant.csproj"
$InstallerScript = Join-Path $ProjectDir "installer.iss"
$OutputDir = Join-Path $ProjectDir "installer-output"

Write-Host "Publishing RoleAxis Desktop..."
dotnet publish $ProjectFile --configuration $Configuration --runtime $Runtime --self-contained false

$isccCandidates = @(
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
) | Where-Object { $_ -and (Test-Path $_) }

if (-not $isccCandidates) {
    throw "Inno Setup 6 was not found. Install it from https://jrsoftware.org/isinfo.php, then run this script again."
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
Write-Host "Building installer..."
Push-Location $ProjectDir
try {
    & $isccCandidates[0] $InstallerScript
}
finally {
    Pop-Location
}

$installer = Get-ChildItem -Path $OutputDir -Filter "*.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $installer) {
    throw "Installer build finished but no .exe was found in $OutputDir"
}

Write-Host "Installer ready: $($installer.FullName)"
