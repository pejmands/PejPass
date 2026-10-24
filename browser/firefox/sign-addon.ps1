param()

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($env:WEB_EXT_API_KEY) -or
    [string]::IsNullOrWhiteSpace($env:WEB_EXT_API_SECRET)) {
    throw @"
Set WEB_EXT_API_KEY and WEB_EXT_API_SECRET to your AMO API credentials before running this script.
Create credentials from your Firefox Add-ons developer account. Do not commit or share the secret.
"@
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$buildScript = Join-Path $repoRoot "browser\build.ps1"
$sourceDirectory = Join-Path $repoRoot "browser\dist\firefox"
$artifactsDirectory = Join-Path $repoRoot "browser\dist\signed-firefox"

& powershell -NoProfile -ExecutionPolicy Bypass -File $buildScript -Browser firefox
if ($LASTEXITCODE -ne 0) {
    throw "Building the Firefox extension package failed."
}

New-Item -ItemType Directory -Path $artifactsDirectory -Force | Out-Null

Push-Location $repoRoot
try {
    npx --yes web-ext sign `
        --source-dir $sourceDirectory `
        --artifacts-dir $artifactsDirectory `
        --channel unlisted `
        --api-key $env:WEB_EXT_API_KEY `
        --api-secret $env:WEB_EXT_API_SECRET

    if ($LASTEXITCODE -ne 0) {
        throw "Mozilla signing failed. Review the web-ext output above."
    }
}
finally {
    Pop-Location
}

Write-Host "Signed Firefox extension packages are in: $artifactsDirectory"
