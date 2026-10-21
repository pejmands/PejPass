# PejPass for Microsoft Edge

This directory contains the PejPass browser extension. The extension does not read credentials or page contents. The current native messaging integration only sends a harmless ping to a small local .NET host and checks its response.

## Requirements

- Microsoft Edge
- .NET 10 SDK
- A local checkout of this repository

## Load the extension

1. Open `edge://extensions` in Edge.
2. Enable **Developer mode**.
3. Select **Load unpacked**.
4. Choose the `browser/edge` directory.
5. Copy the extension ID shown on the extension card.

## Build and register the local host

From the repository root, run PowerShell with the extension ID copied above:

```powershell
powershell -ExecutionPolicy Bypass -File .\browser\edge\install-dev-host.ps1 -ExtensionId YOUR_32_CHARACTER_EXTENSION_ID
```

The script builds `src/PejPass.BrowserHost`, writes a host manifest under its Release output directory, and registers that manifest for the current Windows user in Edge. It does not require administrator privileges. Restart Edge after registration if it was already running.

To remove the registration later:

```powershell
powershell -ExecutionPolicy Bypass -File .\browser\edge\uninstall-dev-host.ps1
```

If Edge assigns a different ID after you move or reload the unpacked extension from a different location, run the installer again with the current ID.

## Manual checks

- The popup displays its version and the active tab hostname on ordinary HTTP(S) pages.
- Restricted pages such as `edge://extensions` show an unavailable-page message.
- With the host registered, the popup shows **PejPass desktop bridge connected**.
- Without the host installed, the popup shows a clear disconnected status.
- The host accepts only a version-1 `ping` message and returns a `pong` response.
- The extension requests only `activeTab` and `nativeMessaging`.
- No credentials, vault data, or page contents are read, sent, or changed.

## Current scope

This is a connectivity proof only. It does not connect to the PejPass vault, expose lock state, match credentials, or fill forms.
