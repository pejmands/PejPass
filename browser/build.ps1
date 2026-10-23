param(
    [ValidateSet("edge", "firefox")]
    [string]$Browser = "edge"
)

$ErrorActionPreference = "Stop"

$browserRoot = $PSScriptRoot
$sourcePath = Join-Path $browserRoot "shared\injected.js"

if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
    throw "Shared injected script was not found: $sourcePath"
}

if ($Browser -eq "edge") {
    $targetDirectory = Join-Path $browserRoot "edge"
    if (-not (Test-Path -LiteralPath $targetDirectory -PathType Container)) {
        throw "Edge package directory was not found: $targetDirectory"
    }

    $targetPath = Join-Path $targetDirectory "injected.js"
    Copy-Item -LiteralPath $sourcePath -Destination $targetPath -Force
    Write-Host "Updated $targetPath from the shared source."
    return
}

$edgeDirectory = Join-Path $browserRoot "edge"
$firefoxSourceDirectory = Join-Path $browserRoot "firefox"
$targetDirectory = Join-Path $browserRoot "dist\firefox"

if (-not (Test-Path -LiteralPath $edgeDirectory -PathType Container)) {
    throw "Edge runtime package directory was not found: $edgeDirectory"
}

if (-not (Test-Path -LiteralPath (Join-Path $firefoxSourceDirectory "manifest.json") -PathType Leaf)) {
    throw "Firefox manifest template was not found in: $firefoxSourceDirectory"
}

if (Test-Path -LiteralPath $targetDirectory) {
    Remove-Item -LiteralPath $targetDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null

$excludedDirectories = @("tests", "node_modules")
$excludedFiles = @(
    "manifest.json",
    "README.md",
    "package.json",
    "package-lock.json",
    "install-dev-host.ps1",
    "uninstall-dev-host.ps1"
)

Get-ChildItem -LiteralPath $edgeDirectory | ForEach-Object {
    if ($_.PSIsContainer) {
        if ($excludedDirectories -notcontains $_.Name) {
            Copy-Item -LiteralPath $_.FullName -Destination $targetDirectory -Recurse -Force
        }
    }
    elseif ($excludedFiles -notcontains $_.Name) {
        Copy-Item -LiteralPath $_.FullName -Destination $targetDirectory -Force
    }
}

Copy-Item -LiteralPath (Join-Path $firefoxSourceDirectory "manifest.json") -Destination (Join-Path $targetDirectory "manifest.json") -Force
Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $targetDirectory "injected.js") -Force

$backgroundPath = Join-Path $targetDirectory "background.js"
$backgroundSource = [System.IO.File]::ReadAllText($backgroundPath)
$workerImport = 'importScripts("browser-api.js", "injected.js");'
if (-not $backgroundSource.StartsWith($workerImport)) {
    throw "The Edge background script no longer has the expected importScripts header."
}

$firefoxBackground = $backgroundSource.Substring($workerImport.Length).TrimStart([char]13, [char]10)
[System.IO.File]::WriteAllText(
    $backgroundPath,
    $firefoxBackground,
    [System.Text.UTF8Encoding]::new($false)
)

Write-Host "Built Firefox extension package at $targetDirectory."
