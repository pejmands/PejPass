$ErrorActionPreference = "Stop"

$registryPath = "HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\com.pejpass.browserhost"

if (Test-Path $registryPath) {
    $manifestPath = (Get-Item -Path $registryPath).GetValue("")
    Remove-Item -Path $registryPath -Recurse -Force

    if ($manifestPath -and (Test-Path $manifestPath)) {
        Remove-Item -Path $manifestPath -Force
    }

    Write-Host "PejPass native messaging host registration removed from Edge."
}
else {
    Write-Host "PejPass native messaging host is not registered for Edge."
}
