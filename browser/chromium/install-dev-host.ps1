param(
    [Parameter(Mandatory = $true)]
    [string]$ExtensionId,

    [Parameter(Mandatory = $false)]
    [string]$InstallDirectory
)

$ErrorActionPreference = "Stop"

if ($ExtensionId -notmatch '^[a-p]{32}$') {
    throw "ExtensionId must be the 32-character ID shown on edge://extensions or chrome://extensions."
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$projectPath = Join-Path $repoRoot "src\PejPass.BrowserHost\PejPass.BrowserHost.csproj"
$desktopProjectPath = Join-Path $repoRoot "src\PejPass.Wpf\PejPass.Wpf.csproj"

if ([string]::IsNullOrWhiteSpace($InstallDirectory)) {
    $InstallDirectory = Join-Path $env:LOCALAPPDATA "Programs\PejPass"
}

$InstallDirectory = [System.IO.Path]::GetFullPath($InstallDirectory)
$hostDirectory = Join-Path $InstallDirectory "BrowserHost"
$desktopExecutable = Join-Path $InstallDirectory "PejPass.exe"
$hostExecutable = Join-Path $hostDirectory "PejPass.BrowserHost.exe"
$manifestPath = Join-Path $hostDirectory "com.pejpass.browserhost.json"
$registryPaths = @(
    "HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\com.pejpass.browserhost",
    "HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.pejpass.browserhost"
)

dotnet build $desktopProjectPath --configuration Release -p:Platform=x64
if ($LASTEXITCODE -ne 0) {
    throw "Building the PejPass desktop app failed."
}

dotnet build $projectPath --configuration Release
if ($LASTEXITCODE -ne 0) {
    throw "Building the PejPass native messaging host failed."
}

$builtDesktopDirectory = Join-Path $repoRoot "src\PejPass.Wpf\bin\x64\Release\net10.0-windows10.0.19041.0"
$builtDesktopExecutable = Join-Path $builtDesktopDirectory "PejPass.exe"
$builtHostDirectory = Join-Path $repoRoot "src\PejPass.BrowserHost\bin\Release\net10.0-windows"

if (-not (Test-Path $builtDesktopExecutable)) {
    throw "The built PejPass desktop executable was not found: $builtDesktopExecutable"
}

if (-not (Test-Path (Join-Path $builtHostDirectory "PejPass.BrowserHost.exe"))) {
    throw "The built native messaging host executable was not found."
}

New-Item -ItemType Directory -Path $InstallDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $hostDirectory -Force | Out-Null

Copy-Item -Path (Join-Path $builtDesktopDirectory "*") -Destination $InstallDirectory -Recurse -Force
Copy-Item -Path (Join-Path $builtHostDirectory "*") -Destination $hostDirectory -Recurse -Force

if (-not (Test-Path $desktopExecutable)) {
    throw "PejPass was not copied to: $desktopExecutable"
}

if (-not (Test-Path $hostExecutable)) {
    throw "The native messaging host was not copied to: $hostExecutable"
}

$manifest = @{
    name = "com.pejpass.browserhost"
    description = "PejPass local Chromium browser communication bridge"
    path = $hostExecutable
    desktopAppPath = $desktopExecutable
    type = "stdio"
    allowed_origins = @("chrome-extension://$ExtensionId/")
} | ConvertTo-Json -Depth 4

[System.IO.File]::WriteAllText(
    $manifestPath,
    $manifest,
    [System.Text.UTF8Encoding]::new($false)
)

foreach ($registryPath in $registryPaths) {
    New-Item -Path $registryPath -Force | Out-Null
    Set-Item -Path $registryPath -Value $manifestPath
}

Write-Host "PejPass native messaging host registered for Edge and Chrome."
Write-Host "Install directory: $InstallDirectory"
Write-Host "Manifest: $manifestPath"
Write-Host "Restart Edge or Chrome if it was already running, then test the PejPass extension popup."
