$ErrorActionPreference = "Stop"

$registryPaths = @(
    "HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\com.pejpass.browserhost",
    "HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.pejpass.browserhost"
)
$manifestPaths = @()

foreach ($registryPath in $registryPaths) {
    if (-not (Test-Path -LiteralPath $registryPath)) {
        continue
    }

    $manifestPath = (Get-Item -LiteralPath $registryPath).GetValue("")
    if (-not [string]::IsNullOrWhiteSpace([string]$manifestPath)) {
        $manifestPaths += [string]$manifestPath
    }

    Remove-Item -LiteralPath $registryPath -Recurse -Force
    Write-Host "Removed Chromium native messaging registration: $registryPath"
}

foreach ($manifestPath in @($manifestPaths | Sort-Object -Unique)) {
    $stillRegistered = $false

    foreach ($registryPath in $registryPaths) {
        if (-not (Test-Path -LiteralPath $registryPath)) {
            continue
        }

        $registeredManifest = (Get-Item -LiteralPath $registryPath).GetValue("")
        if ([string]::Equals(
            [string]$registeredManifest,
            [string]$manifestPath,
            [System.StringComparison]::OrdinalIgnoreCase)) {
            $stillRegistered = $true
            break
        }
    }

    if (-not $stillRegistered -and (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        Remove-Item -LiteralPath $manifestPath -Force
    }
}

if ($manifestPaths.Count -eq 0) {
    Write-Host "PejPass native messaging host is not registered for Chromium browsers."
}
else {
    Write-Host "PejPass Chromium native messaging registrations removed."
}
