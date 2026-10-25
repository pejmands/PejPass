param()

# Teaches the popup about the new BrowserHost error code "app_busy".
# BrowserHost now answers app_busy instead of queueing a request for a long time when the
# single desktop pipe is occupied (for example while PejPass desktop waits for the user to
# confirm a fill or an account search).
#
# Run from the repository root:
#   powershell -ExecutionPolicy Bypass -File .\browser\apply-busy-handling.ps1

$ErrorActionPreference = "Stop"
$utf8 = [System.Text.UTF8Encoding]::new($false)
$popupPath = Join-Path $PSScriptRoot "edge\popup.js"
$text = [System.IO.File]::ReadAllText($popupPath)

if ($text.Contains("busyMessage")) {
    Write-Host "popup.js already handles app_busy; nothing to do."
    return
}

$crlf = $text.Contains("`r`n")
$nl = if ($crlf) { "`r`n" } else { "`n" }

function Replace-Once([string]$source, [string]$old, [string]$new) {
    $first = $source.IndexOf($old, [System.StringComparison]::Ordinal)
    if ($first -lt 0) { throw "Anchor not found in popup.js: $old" }
    if ($source.IndexOf($old, $first + 1, [System.StringComparison]::Ordinal) -ge 0) {
        throw "Anchor is not unique in popup.js: $old"
    }
    return $source.Substring(0, $first) + $new + $source.Substring($first + $old.Length)
}

# 1) One shared message.
$text = Replace-Once $text 'let activeTabId = null;' (
    'let activeTabId = null;' + $nl +
    'const busyMessage = "PejPass is waiting for you to respond to a request in the desktop app. Respond there, then try again.";')

# 2) Add app_busy to every error-message table except the fill-outcome table (it starts with "filled:").
$pattern = 'const messages = \{(?!\s*filled:)'
$count = [regex]::Matches($text, $pattern).Count
if ($count -ne 7) { Write-Warning "Expected 7 error-message tables, found $count. Review popup.js after patching." }
$text = [regex]::Replace($text, $pattern, ('const messages = {' + $nl + '        app_busy: busyMessage,'))

# 3) Status check: a busy desktop is connected, not broken.
$text = Replace-Once $text 'setConnectionStatus(false, "Connection error");' (
    'if (response?.code === "app_busy") {' + $nl +
    '          setConnectionStatus(true, "Desktop connected");' + $nl +
    '          showStatusError("PejPass is busy.", busyMessage);' + $nl +
    '          return "unknown";' + $nl +
    '        }' + $nl +
    '        setConnectionStatus(false, "Connection error");')

# 4) Lock vault.
$text = Replace-Once $text ': "PejPass returned an invalid vault-lock response.";' `
    ': response?.code === "app_busy" ? busyMessage : "PejPass returned an invalid vault-lock response.";'

[System.IO.File]::WriteAllText($popupPath, $text, $utf8)
Write-Host "Updated $popupPath ($count error-message tables)."
Write-Host "Rebuild BrowserHost, run 'npm test' in browser\edge, reload the extension."
