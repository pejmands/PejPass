param()

# Keeps the toolbar badge steady while BrowserHost answers "app_busy".
# refreshMatchCount clears a tab's badge before it asks the desktop app for matches. If the
# desktop pipe is busy (for example while PejPass waits for the user to confirm a fill), the
# lookup is not answered and the badge used to stay empty. With this patch the count that was
# showing before the refresh is put back.
#
# Run from the repository root (after apply-busy-handling.ps1 and the new BrowserHost build):
#   powershell -ExecutionPolicy Bypass -File .\browser\apply-badge-busy.ps1

$ErrorActionPreference = "Stop"
$utf8 = [System.Text.UTF8Encoding]::new($false)
$backgroundPath = Join-Path $PSScriptRoot "edge\background.js"
$text = [System.IO.File]::ReadAllText($backgroundPath)

if ($text.Contains("previousCount")) {
    Write-Host "background.js already keeps the badge while busy; nothing to do."
    return
}

$crlf = $text.Contains("`r`n")
function Convert-Newlines([string]$value) {
    $value = $value.Replace("`r`n", "`n")
    if ($crlf) { return $value.Replace("`n", "`r`n") }
    return $value
}

function Replace-Once([string]$source, [string]$old, [string]$new) {
    $old = Convert-Newlines $old
    $new = Convert-Newlines $new
    $first = $source.IndexOf($old, [System.StringComparison]::Ordinal)
    if ($first -lt 0) { throw "Anchor not found in background.js: $old" }
    if ($source.IndexOf($old, $first + 1, [System.StringComparison]::Ordinal) -ge 0) {
        throw "Anchor is not unique in background.js: $old"
    }
    return $source.Substring(0, $first) + $new + $source.Substring($first + $old.Length)
}

# 1) Remember the count that is showing before refreshMatchCount clears it.
#    The two-space indentation distinguishes this from the same lines in tabs.onUpdated.
$text = Replace-Once $text (
    "  matchRefreshVersions.set(tabId, version);`n  clearMatchBadge(tabId);") (
    "  matchRefreshVersions.set(tabId, version);`n  const previousCount = matchCounts.get(tabId) || 0;`n  clearMatchBadge(tabId);")

# 2) If the desktop was busy, restore it instead of leaving the badge empty.
$lookup = 'const response = await sendNative({ type: "getCredentials", domain }, 6_000);'
$text = Replace-Once $text $lookup (
    $lookup + "`n" +
    '    if (response?.type === "error" && response.code === "app_busy") {' + "`n" +
    '      // The desktop app is waiting for the user, so this lookup was not answered.' + "`n" +
    '      // Keep the badge that was showing before the refresh.' + "`n" +
    '      if (matchRefreshVersions.get(tabId) === version) setMatchBadge(tabId, previousCount);' + "`n" +
    '      return;' + "`n" +
    '    }')

[System.IO.File]::WriteAllText($backgroundPath, $text, $utf8)
Write-Host "Updated $backgroundPath"
Write-Host "Run 'npm test' in browser\edge, then reload the extension."
