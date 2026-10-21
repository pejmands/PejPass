# PejPass for Microsoft Edge

The extension reads only the active HTTP(S) tab hostname and requests matching account metadata from the local PejPass desktop app. Passwords, key material, vault contents, and page contents are not sent to the extension.

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

## Manual checks

- The popup displays the active hostname on ordinary HTTP(S) pages; restricted browser pages show an unavailable message.
- The popup reports the live desktop connection and vault lock state.
- When unlocked, the popup lists entries whose saved URL host is the active site host or a parent domain of it. Matching is case-insensitive and does not match lookalike sibling domains.
- Only entry ID, title, username, and saved URL are sent to the extension. Passwords, TOTP secrets, notes, custom fields, and key material are excluded.
- When locked, the extension shows an unlock message and receives no account metadata.
- The host accepts only version-1 `ping`, `getVaultStatus`, and `getCredentials` requests. Requests are validated by both the host and desktop endpoint.
- The desktop endpoint uses a current-user-only named pipe. Credential lookup is limited to 25 results.
- The extension requests only `activeTab` and `nativeMessaging`; it does not read page contents or inject scripts.

## Current scope

This change lists matching account metadata only. Password copy and form filling are not implemented.
