$ErrorActionPreference = "Stop"

$registryPath = "HKCU:\Software\Mozilla\NativeMessagingHosts\com.pejpass.browserhost"

if (Test-Path -LiteralPath $registryPath) {
    Remove-Item -LiteralPath $registryPath -Recurse -Force
    Write-Host "PejPass native messaging host registration removed for Firefox."
}
else {
    Write-Host "PejPass native messaging host is not registered for Firefox."
}
