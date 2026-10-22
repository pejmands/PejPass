$ErrorActionPreference = "Stop"

$registryPath = "HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\com.pejpass.browserhost"

if (Test-Path $registryPath) {
    Remove-Item -LiteralPath $registryPath -Recurse -Force
    Write-Host "PejPass native messaging host registration removed."
}
else {
    Write-Host "PejPass native messaging host is not registered."
}
