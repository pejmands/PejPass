param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[a-p]{32}$')]
    [string]$ExtensionId,

    [Parameter(Mandatory = $false)]
    [ValidateRange(30, 120)]
    [int]$ReconnectTimeoutSeconds = 50
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$installScript = Join-Path $PSScriptRoot "install-dev-host.ps1"
$registrationPath = "HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\com.pejpass.browserhost"

if (-not (Test-Path -LiteralPath $registrationPath)) {
    throw "The Edge native messaging host is not currently registered. Register the normal host first."
}

$originalManifestPath = (Get-Item -LiteralPath $registrationPath).GetValue("")
if ([string]::IsNullOrWhiteSpace($originalManifestPath) -or
    -not (Test-Path -LiteralPath $originalManifestPath -PathType Leaf)) {
    throw "The current Edge native messaging registration does not point to an existing manifest."
}

$originalManifest = Get-Content -LiteralPath $originalManifestPath -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace([string]$originalManifest.path)) {
    throw "The current Edge manifest does not contain a host executable path."
}

$originalHostPath = [System.IO.Path]::GetFullPath([string]$originalManifest.path)
if (-not (Test-Path -LiteralPath $originalHostPath -PathType Leaf)) {
    throw "The currently registered BrowserHost executable does not exist: $originalHostPath"
}

$testInstallDirectory = Join-Path $env:LOCALAPPDATA (
    "Programs\PejPass-UpdateReconnectTest-" + [Guid]::NewGuid().ToString("N"))
$testHostPath = Join-Path $testInstallDirectory "BrowserHost\PejPass.BrowserHost.exe"
$pausedHostPath = $testHostPath + ".update-paused.manual"

function Get-HostProcessesAtPath([string]$ExecutablePath) {
    $expectedPath = [System.IO.Path]::GetFullPath($ExecutablePath)

    foreach ($process in @(Get-Process -Name "PejPass.BrowserHost" -ErrorAction SilentlyContinue)) {
        try {
            $actualPath = $process.Path
            if (-not [string]::IsNullOrWhiteSpace($actualPath) -and
                [System.String]::Equals(
                    [System.IO.Path]::GetFullPath($actualPath),
                    $expectedPath,
                    [System.StringComparison]::OrdinalIgnoreCase)) {
                $process
            }
        }
        catch {
        }
    }
}

function Stop-HostProcessesAtPath([string]$ExecutablePath) {
    foreach ($process in @(Get-HostProcessesAtPath $ExecutablePath)) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }

    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while (@(Get-HostProcessesAtPath $ExecutablePath).Count -gt 0) {
        if ([DateTime]::UtcNow -ge $deadline) {
            throw "BrowserHost is still running from: $ExecutablePath"
        }

        Start-Sleep -Milliseconds 250
    }
}

function Wait-ForHostAtPath([string]$ExecutablePath, [int]$TimeoutSeconds) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        $process = @(Get-HostProcessesAtPath $ExecutablePath) | Select-Object -First 1
        if ($null -ne $process) {
            return $process
        }

        Start-Sleep -Seconds 1
    }

    throw "Edge did not launch BrowserHost from '$ExecutablePath' within $TimeoutSeconds seconds. Ensure the extension is installed and enabled, then inspect edge://extensions and Edge's extension error details."
}

$testCompleted = $false
$registrationChanged = $false

try {
    Write-Host "Building and preparing an isolated test installation..."
    & $installScript -ExtensionId $ExtensionId -InstallDirectory $testInstallDirectory

    if (-not (Test-Path -LiteralPath $testHostPath -PathType Leaf)) {
        throw "The test BrowserHost executable was not created: $testHostPath"
    }

    $registrationChanged = $true
    Write-Host "Switching Edge's native-host registration to the temporary installation..."
    Stop-HostProcessesAtPath $originalHostPath

    $firstConnection = Wait-ForHostAtPath $testHostPath $ReconnectTimeoutSeconds
    Write-Host "PASS: Edge launched the test BrowserHost (PID $($firstConnection.Id))."

    Write-Host "Simulating the brief period when the BrowserHost executable is replaced..."
    Stop-HostProcessesAtPath $testHostPath

    if (Test-Path -LiteralPath $pausedHostPath) {
        throw "A stale paused test executable already exists: $pausedHostPath"
    }

    Move-Item -LiteralPath $testHostPath -Destination $pausedHostPath
    try {
        Start-Sleep -Seconds 5
    }
    finally {
        if (Test-Path -LiteralPath $pausedHostPath) {
            Move-Item -LiteralPath $pausedHostPath -Destination $testHostPath -Force
        }
    }

    $reconnectedHost = Wait-ForHostAtPath $testHostPath $ReconnectTimeoutSeconds
    Write-Host "PASS: Edge reconnected after the BrowserHost executable was restored (PID $($reconnectedHost.Id))."
    $testCompleted = $true
}
finally {
    if (Test-Path -LiteralPath $pausedHostPath) {
        Stop-HostProcessesAtPath $testHostPath
        if (-not (Test-Path -LiteralPath $testHostPath)) {
            Move-Item -LiteralPath $pausedHostPath -Destination $testHostPath -Force
        }
    }

    if ($registrationChanged) {
        Write-Host "Restoring the original Edge native-host registration..."
        New-Item -Path $registrationPath -Force | Out-Null
        Set-Item -Path $registrationPath -Value $originalManifestPath

        Stop-HostProcessesAtPath $testHostPath
        try {
            $restoredHost = Wait-ForHostAtPath $originalHostPath $ReconnectTimeoutSeconds
            Write-Host "PASS: Edge returned to the original BrowserHost (PID $($restoredHost.Id))."
        }
        catch {
            Write-Warning "The original registration was restored, but automatic reconnection to the original BrowserHost was not observed: $($_.Exception.Message)"
        }
    }

    try {
        if (Test-Path -LiteralPath $testInstallDirectory) {
            Remove-Item -LiteralPath $testInstallDirectory -Recurse -Force
        }
    }
    catch {
        Write-Warning "Could not remove the temporary test installation: $testInstallDirectory. Remove it manually after confirming no BrowserHost process is running from it."
    }
}

if (-not $testCompleted) {
    throw "The BrowserHost reconnect test did not complete successfully."
}

Write-Host "PASS: BrowserHost reconnect validation completed. No installed PejPass payload files were replaced."
