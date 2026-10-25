param()

# Adds persistence of the last known vault state to browser/edge/background.js using
# storage.session, so a restarted service worker (Edge) or event page (Firefox) does not
# forget whether the vault was locked. No secrets are stored: only "locked"/"unlocked"/
# "unavailable" and a boolean for Windows Hello readiness.
#
# Run from the repository root:
#   powershell -ExecutionPolicy Bypass -File .\browser\apply-session-state.ps1

$ErrorActionPreference = "Stop"
$utf8 = [System.Text.UTF8Encoding]::new($false)
$backgroundPath = Join-Path $PSScriptRoot "edge\background.js"
$text = [System.IO.File]::ReadAllText($backgroundPath)

if ($text.Contains("persistState")) {
    Write-Host "background.js already contains persistState; nothing to do."
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

# 1) Helper functions, inserted after the debounce constant.
$anchorConstants = 'const matchRefreshDebounceMs = 300;'
$helpers = @'
const matchRefreshDebounceMs = 300;

// Last known vault state survives service-worker / event-page restarts. No secrets are stored.
const sessionStateArea = globalThis.pejpassBrowserApi.storage?.session;

function persistState() {
  if (!sessionStateArea) return;
  void sessionStateArea.set({
    pejpassState: { status: cachedStatus, helloReady: cachedHelloReady }
  }).catch(() => {});
}

async function restoreState() {
  if (!sessionStateArea) return;
  try {
    const stored = (await sessionStateArea.get("pejpassState"))?.pejpassState;
    if (cachedStatus !== "unknown" || !stored ||
        (stored.status !== "locked" && stored.status !== "unlocked")) return;

    const restored = stored.status;
    cachedStatus = restored;
    cachedHelloReady = typeof stored.helloReady === "boolean" ? stored.helloReady : null;
    void setToolbarVaultIcon(restored === "unlocked");

    // The vault may have changed while the worker was stopped, so confirm with the desktop app.
    try {
      const response = await sendNative({ type: "getVaultStatus", protocolVersion }, 6000);
      if (response?.type === "vaultStatus" && response.app === "PejPass" &&
          response.protocolVersion === protocolVersion &&
          (response.status === "locked" || response.status === "unlocked")) {
        applyStatus(response.status, response.helloReady === true);
      }
    } catch {
      if (cachedStatus === restored) applyStatus("unavailable");
      return;
    }

    if (cachedStatus === "unlocked") {
      const tabs = await globalThis.pejpassBrowserApi.tabs.query({ active: true });
      for (const tab of tabs) {
        if (Number.isInteger(tab.id)) scheduleRefreshMatchCount(tab.id);
      }
    }
  } catch {
    // Persisted state is only an optimisation; the native host remains the source of truth.
  }
}
'@
$text = Replace-Once $text $anchorConstants $helpers

# 2) Persist whenever the cached state changes.
$text = Replace-Once $text 'cachedHelloReady = helloReady;' "cachedHelloReady = helloReady;`n  persistState();"

# 3) Restore at startup, before the native connection is re-established.
$anchorStartup = '// Re-establish the native connection whenever the service worker starts.'
$text = Replace-Once $text $anchorStartup "void restoreState();`n$anchorStartup"

[System.IO.File]::WriteAllText($backgroundPath, $text, $utf8)
Write-Host "Updated $backgroundPath"
Write-Host "Run 'npm test' in browser\edge, then reload the extension in Edge and Firefox."
