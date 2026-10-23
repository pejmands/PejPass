param()

$ErrorActionPreference = "Stop"

$extensionId = "pejpass@pejmands.github.io"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$projectPath = Join-Path $repoRoot "src\PejPass.BrowserHost\PejPass.BrowserHost.csproj"
$desktopProjectPath = Join-Path $repoRoot "src\PejPass.Wpf\PejPass.Wpf.csproj"
$desktopExecutable = Join-Path $repoRoot "src\PejPass.Wpf\bin\x64\Release\net10.0-windows10.0.19041.0\PejPass.exe"
$outputDirectory = Join-Path $repoRoot "src\PejPass.BrowserHost\bin\Release\net10.0-windows"
$firefoxHostDirectory = Join-Path $outputDirectory "Firefox"
$hostExecutable = Join-Path $firefoxHostDirectory "PejPass.BrowserHost.exe"
$manifestPath = Join-Path $firefoxHostDirectory "com.pejpass.browserhost.json"
$registryPath = "HKCU:\Software\Mozilla\NativeMessagingHosts\com.pejpass.browserhost"

dotnet build $desktopProjectPath --configuration Release -p:Platform=x64
if ($LASTEXITCODE -ne 0) {
    throw "Building the PejPass desktop app failed."
}

dotnet build $projectPath --configuration Release
if ($LASTEXITCODE -ne 0) {
    throw "Building the PejPass native messaging host failed."
}

if (-not (Test-Path -LiteralPath $desktopExecutable -PathType Leaf)) {
    throw "The PejPass desktop executable was not found at: $desktopExecutable"
}

if (-not (Test-Path -LiteralPath $outputDirectory -PathType Container)) {
    throw "The native messaging host output directory was not found at: $outputDirectory"
}

New-Item -ItemType Directory -Path $firefoxHostDirectory -Force | Out-Null
Get-ChildItem -LiteralPath $outputDirectory -File |
    Where-Object { $_.Name -ne "com.pejpass.browserhost.json" } |
    Copy-Item -Destination $firefoxHostDirectory -Force

if (-not (Test-Path -LiteralPath $hostExecutable -PathType Leaf)) {
    throw "The Firefox native messaging host executable was not found at: $hostExecutable"
}

$manifest = @{
    name = "com.pejpass.browserhost"
    description = "PejPass local browser communication bridge for Firefox"
    path = $hostExecutable
    type = "stdio"
    allowed_extensions = @($extensionId)
} | ConvertTo-Json -Depth 4

[System.IO.File]::WriteAllText(
    $manifestPath,
    $manifest,
    [System.Text.UTF8Encoding]::new($false)
)

$configPath = Join-Path $firefoxHostDirectory "pejpass-host-config.json"
$config = @{
    desktopAppPath = $desktopExecutable
} | ConvertTo-Json -Depth 2

[System.IO.File]::WriteAllText(
    $configPath,
    $config,
    [System.Text.UTF8Encoding]::new($false)
)

New-Item -Path $registryPath -Force | Out-Null
Set-Item -Path $registryPath -Value $manifestPath

Write-Host "PejPass native messaging host registered for Firefox."
Write-Host "Manifest: $manifestPath"
Write-Host "Restart Firefox if it was already running, then test the PejPass extension."
