# PejPass for Microsoft Edge

The extension reads the active HTTP(S) tab hostname and requests matching account metadata from the local PejPass desktop app. Passwords are retrieved only after the user explicitly selects **Fill login** for a matching account.

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
- Only entry ID, title, username, and saved URL are sent for lookup. Passwords, TOTP secrets, notes, custom fields, and key material are excluded from lookup responses.
- When locked, the extension shows an unlock message and receives no account metadata.
- Selecting **Fill login** requests the chosen credential from the desktop app, revalidates the account ID and active hostname, and checks the main document plus same-site frames for a visible password field and a likely username field in the same form when available. Cross-site frames are intentionally not filled.
- Filling is always user-triggered. The extension does not submit the form. Review the filled fields and submit the login yourself.
- The extension checks the page hostname again immediately before filling and does not fill if the page changed.
- The host accepts only version-1 `ping`, `getVaultStatus`, `getCredentials`, and `fillCredential` requests. Requests are validated by both the host and desktop endpoint.
- The desktop endpoint uses a current-user-only named pipe. Credential lookup is limited to 25 results.
- The extension requests only `activeTab`, `nativeMessaging`, and `scripting`; it does not run a persistent content script or read page contents in the background.

## Current scope

Autofill supports visible username-like and password inputs in the main page document. It does not automatically submit forms or handle login fields inside cross-origin frames.
