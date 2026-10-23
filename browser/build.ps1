param(
    [ValidateSet("edge")]
    [string]$Browser = "edge"
)

$ErrorActionPreference = "Stop"

$browserRoot = $PSScriptRoot
$sourcePath = Join-Path $browserRoot "shared\injected.js"
$targetDirectory = Join-Path $browserRoot $Browser
$targetPath = Join-Path $targetDirectory "injected.js"

if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
    throw "Shared injected script was not found: $sourcePath"
}

if (-not (Test-Path -LiteralPath $targetDirectory -PathType Container)) {
    throw "Browser package directory was not found: $targetDirectory"
}

Copy-Item -LiteralPath $sourcePath -Destination $targetPath -Force
Write-Host "Updated $targetPath from the shared source."
