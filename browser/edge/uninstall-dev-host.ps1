$ErrorActionPreference = "Stop"

$registryPath = "HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\com.pejpass.browserhost"
$chromeRegistryPath = "HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.pejpass.browserhost"

if (Test-Path -LiteralPath $registryPath) {
    $manifestPath = (Get-Item -LiteralPath $registryPath).GetValue("")
    Remove-Item -LiteralPath $registryPath -Recurse -Force

    if ($manifestPath -and
        -not (Test-Path -LiteralPath $chromeRegistryPath) -and
        (Test-Path -LiteralPath $manifestPath)) {
        Remove-Item -LiteralPath $manifestPath -Force
    }

    Write-Host "PejPass native messaging host registration removed from Edge."
}
else {
    Write-Host "PejPass native messaging host is not registered for Edge."
}
