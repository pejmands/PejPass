$ErrorActionPreference = "Stop"

$registryPath = "HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.pejpass.browserhost"
$edgeRegistryPath = "HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\com.pejpass.browserhost"

if (Test-Path -LiteralPath $registryPath) {
    $manifestPath = (Get-Item -LiteralPath $registryPath).GetValue("")
    Remove-Item -LiteralPath $registryPath -Recurse -Force

    if ($manifestPath -and
        -not (Test-Path -LiteralPath $edgeRegistryPath) -and
        (Test-Path -LiteralPath $manifestPath)) {
        Remove-Item -LiteralPath $manifestPath -Force
    }

    Write-Host "PejPass native messaging host registration removed from Chrome."
}
else {
    Write-Host "PejPass native messaging host is not registered for Chrome."
}
