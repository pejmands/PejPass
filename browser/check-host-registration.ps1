param()

# Read-only check of the PejPass native messaging registration for Chromium browsers (Edge and Chrome) and Firefox.
# It verifies the registry entry, the host manifest, the host executable, the allowed
# extension identity, and where the desktop executable path (used by launchApp) comes from.
#
#   powershell -ExecutionPolicy Bypass -File .\browser\check-host-registration.ps1

$ErrorActionPreference = "Stop"
$script:problems = 0

function Report([bool]$ok, [string]$message) {
    if ($ok) { Write-Host "  [ok]   $message" }
    else { Write-Host "  [FAIL] $message" -ForegroundColor Red; $script:problems++ }
}

$targets = @(
    @{ Browser = "Edge";    Key = "HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\com.pejpass.browserhost"; AllowedProperty = "allowed_origins" },
    @{ Browser = "Chrome";  Key = "HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.pejpass.browserhost"; AllowedProperty = "allowed_origins" },
    @{ Browser = "Firefox"; Key = "HKCU:\Software\Mozilla\NativeMessagingHosts\com.pejpass.browserhost";        AllowedProperty = "allowed_extensions" }
)

foreach ($target in $targets) {
    Write-Host ""
    Write-Host "== $($target.Browser) =="

    if (-not (Test-Path -LiteralPath $target.Key)) {
        Write-Host "  Not registered. Skipping."
        continue
    }

    $manifestPath = (Get-Item -LiteralPath $target.Key).GetValue("")
    Report (-not [string]::IsNullOrWhiteSpace($manifestPath)) "Registry default value points to a manifest: $manifestPath"
    if ([string]::IsNullOrWhiteSpace($manifestPath)) { continue }

    $manifestExists = Test-Path -LiteralPath $manifestPath -PathType Leaf
    Report $manifestExists "Manifest file exists"
    if (-not $manifestExists) { continue }

    try {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    }
    catch {
        Report $false "Manifest is valid JSON ($($_.Exception.Message))"
        continue
    }

    Report ($manifest.name -eq "com.pejpass.browserhost") "Manifest name is com.pejpass.browserhost"
    Report ($manifest.type -eq "stdio") "Manifest type is stdio"

    $hostExecutable = [string]$manifest.path
    $hostExists = (-not [string]::IsNullOrWhiteSpace($hostExecutable)) -and
        (Test-Path -LiteralPath $hostExecutable -PathType Leaf)
    Report $hostExists "Host executable exists: $hostExecutable"

    $allowed = @($manifest.($target.AllowedProperty))
    Report ($allowed.Count -gt 0) "$($target.AllowedProperty) lists: $($allowed -join ', ')"
    if ($target.Browser -eq "Firefox") {
        Report ($allowed -contains "pejpass@pejmands.github.io") "Firefox allows the stable Gecko ID"
    }

    # BrowserHost resolves the desktop executable from pejpass-host-config.json (Firefox)
    # or com.pejpass.browserhost.json (Chromium browsers), both next to the host executable.
    $desktopPath = $null
    $source = $null
    if ($hostExists) {
        $hostDirectory = Split-Path -Parent $hostExecutable
        foreach ($candidate in @("pejpass-host-config.json", "com.pejpass.browserhost.json")) {
            $candidatePath = Join-Path $hostDirectory $candidate
            if (-not (Test-Path -LiteralPath $candidatePath -PathType Leaf)) { continue }
            try {
                $candidateJson = Get-Content -LiteralPath $candidatePath -Raw | ConvertFrom-Json
            }
            catch {
                Report $false "$candidate is valid JSON"
                continue
            }
            $value = [string]$candidateJson.desktopAppPath
            if (-not [string]::IsNullOrWhiteSpace($value) -and
                [System.IO.Path]::IsPathRooted($value) -and
                (Test-Path -LiteralPath $value -PathType Leaf)) {
                $desktopPath = $value
                $source = $candidate
                break
            }
        }
    }

    if ($desktopPath) {
        Report $true "BrowserHost resolves the desktop executable from ${source}: $desktopPath"
    }
    else {
        Report $false "BrowserHost cannot resolve the desktop executable (no valid desktopAppPath in pejpass-host-config.json or com.pejpass.browserhost.json next to the host executable)"
    }
}

Write-Host ""
if ($script:problems -eq 0) { Write-Host "All checks passed." }
else { Write-Host "$($script:problems) problem(s) found. Re-run the matching install-dev-host.ps1." -ForegroundColor Yellow }
