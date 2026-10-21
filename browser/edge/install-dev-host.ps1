param(
    [Parameter(Mandatory = $true)]
    [string]$ExtensionId
)

$ErrorActionPreference = "Stop"

if ($ExtensionId -notmatch '^[a-p]{32}$') {
    throw "ExtensionId must be the 32-character ID shown on edge://extensions."
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$projectPath = Join-Path $repoRoot "src\PejPass.BrowserHost\PejPass.BrowserHost.csproj"
$desktopProjectPath = Join-Path $repoRoot "src\PejPass.Wpf\PejPass.Wpf.csproj"
$outputDirectory = Join-Path $repoRoot "src\PejPass.BrowserHost\bin\Release\net10.0-windows"
$hostExecutable = Join-Path $outputDirectory "PejPass.BrowserHost.exe"
$manifestPath = Join-Path $outputDirectory "com.pejpass.browserhost.json"
$registryPath = "HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\com.pejpass.browserhost"

dotnet build $desktopProjectPath --configuration Release -p:Platform=x64
if ($LASTEXITCODE -ne 0) {
    throw "Building the PejPass desktop app failed."
}

dotnet build $projectPath --configuration Release
if ($LASTEXITCODE -ne 0) {
    throw "Building the PejPass native messaging host failed."
}

if (-not (Test-Path $hostExecutable)) {
    throw "The native messaging host executable was not found at: $hostExecutable"
}

$manifest = @{
    name = "com.pejpass.browserhost"
    description = "PejPass local browser communication bridge"
    path = $hostExecutable
    type = "stdio"
    allowed_origins = @("chrome-extension://$ExtensionId/")
} | ConvertTo-Json -Depth 4

[System.IO.File]::WriteAllText(
    $manifestPath,
    $manifest,
    [System.Text.UTF8Encoding]::new($false)
)

New-Item -Path $registryPath -Force | Out-Null
Set-Item -Path $registryPath -Value $manifestPath

Write-Host "PejPass native messaging host registered for Edge."
Write-Host "Manifest: $manifestPath"
Write-Host "Restart Edge if it was already running, then test the PejPass extension popup."
