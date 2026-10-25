param()

# Applies the small fixes that accompany the new popup UI.
# Run from the repository root:
#   powershell -ExecutionPolicy Bypass -File .\browser\apply-small-fixes.ps1

$ErrorActionPreference = "Stop"
$utf8 = [System.Text.UTF8Encoding]::new($false)
$browserRoot = $PSScriptRoot

function Edit-File {
    param([string]$Path, [string]$Old, [string]$New, [switch]$All)
    $text = [System.IO.File]::ReadAllText($Path)
    if (-not $text.Contains($Old)) {
        Write-Warning "Pattern not found (already applied?) in $Path : $Old"
        return
    }
    $text = $text.Replace($Old, $New)
    [System.IO.File]::WriteAllText($Path, $text, $utf8)
    Write-Host "Updated $Path"
}

# 1) Regex bug: split(/\\s+/) splits on the literal text "\s", not on whitespace.
Edit-File (Join-Path $browserRoot "shared\injected.js") 'split(/\\s+/)' 'split(/\s+/)'

# 2) Refresh button label now matches the shorter HTML label.
$popupJs = Join-Path $browserRoot "edge\popup.js"
Edit-File $popupJs 'refreshLoginsButton.textContent = "Refresh status & accounts";' 'refreshLoginsButton.textContent = "Refresh";'

# 3) Keep the popup open a little longer so the result message can be read (Firefox closes it quickly).
Edit-File $popupJs 'window.setTimeout(() => window.close(), 350);' 'window.setTimeout(() => window.close(), 1200);'

# 4) Use PNG toolbar icons in both manifests (Firefox handles .ico poorly).
$icon = '"default_icon": { "16": "icons/PejPass16.png", "48": "icons/PejPass48.png" }'
foreach ($manifest in @("edge\manifest.json", "firefox\manifest.json")) {
    Edit-File (Join-Path $browserRoot $manifest) '"default_icon": "icons/PejPassLocked.ico"' $icon
}

# 5) Sync the shared injected.js into the Edge package.
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $browserRoot "build.ps1") -Browser edge
if ($LASTEXITCODE -ne 0) { throw "build.ps1 failed." }

Write-Host "Done. Run 'npm test' in browser\edge, then reload the extension in Edge and Firefox."
