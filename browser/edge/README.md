# PejPass for Microsoft Edge

The extension reads the active HTTP(S) tab hostname and requests matching account metadata from the local PejPass desktop app. Passwords and a current TOTP code are retrieved only after the user explicitly selects **Fill login** for a matching account. It can launch the desktop app and submit a master password for local vault verification. It can also generate passwords locally in the popup using the browser's cryptographic random number generator.

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

## Browser API compatibility

The extension's background and popup scripts use `browser-api.js` as a small compatibility facade. It selects the standard `browser` namespace when available and falls back to `chrome` in Chromium-based browsers. Keep browser-specific manifest and packaging work separate from shared form logic; this facade is groundwork, not a complete Firefox package.

## Shared browser-independent logic

The login-form detection and filling implementation is maintained in `browser/shared/injected.js`. The `browser/edge/injected.js` file is the packaged copy required by the unpacked Edge extension; edit the shared source, not the packaged copy.

After changing the shared implementation, run these commands from `browser/edge`:

```powershell
npm run build
npm test
```

The build script synchronizes the shared source into the Edge package. The tests exercise the shared implementation and fail if the packaged Edge copy is out of date. When another browser target is added, the build pipeline can synchronize this same source into that package as well.

## Build and register the local host

From the repository root, run PowerShell with the extension ID copied above:

```powershell
powershell -ExecutionPolicy Bypass -File .\browser\edge\install-dev-host.ps1 -ExtensionId YOUR_32_CHARACTER_EXTENSION_ID
```

The script builds `src/PejPass.Wpf` and `src/PejPass.BrowserHost`, writes a host manifest under the host's Release output directory, and registers that manifest for the current Windows user in Edge. The manifest records the desktop executable's absolute path in `desktopAppPath`; the host uses this configured path when handling `launchApp`, rather than deriving a path relative to the development checkout. It does not require administrator privileges. Restart Edge after registration if it was already running.

To remove the registration later:

```powershell
powershell -ExecutionPolicy Bypass -File .\browser\edge\uninstall-dev-host.ps1
```

## Manual checks

- The password generator supports lengths from 8 to 64 characters, configurable lowercase, uppercase, numeric, and symbol sets, and optional exclusion of ambiguous characters. It ensures at least one character from each selected set. It uses `crypto.getRandomValues`, rejection sampling to avoid modulo bias, and a cryptographic shuffle. The strength indicator estimates entropy from the selected character pool and length; it is an estimate, not a full password-strength analyzer. Generated passwords are not saved automatically.
- **Generate and fill** only fills a visible field identified as a new-password field or a likely registration form. It does not submit the page. Copying a generated password routes through the desktop clipboard service, which clears it after 30 seconds only if the clipboard still contains the same text; the desktop app and updated BrowserHost must be running.
- The popup displays the active hostname on ordinary HTTP(S) pages; restricted browser pages show an unavailable message.
- The extension background service worker owns one persistent Native Messaging port and routes popup requests through it with request IDs. The desktop pushes vault-state changes to BrowserHost over the separate `PejPass.BrowserEvents.v1` named pipe; no status polling occurs while the desktop event pipe is connected. The toolbar icon and open popup react to these events. A 30-second alarm is used only to re-establish a disconnected Native Messaging connection. The toolbar uses the same locked and unlocked ICO assets as the desktop app, with a locked icon as the default. When unlocked, select **Lock vault** to lock the desktop vault immediately; the popup clears account metadata, save-form values, and its generated password. If the desktop app is not running, select **Launch PejPass**; the native host launches the executable configured by the host manifest's absolute `desktopAppPath`. The development registration script points this field at the Release build in the checkout; a production installer should write the actual installed executable path to the same field.
- When the vault is locked, the popup accepts the master password and forwards it through Native Messaging and the current-user-only named pipe to the desktop app. The desktop app verifies it using its existing vault-opening flow. The extension clears the password field after each attempt and does not store the password. This requires an existing vault selected in PejPass and a Release build created by `install-dev-host.ps1`.
- When unlocked, the popup lists entries whose saved URL host and active site share the same registrable domain (eTLD+1). Matching uses the official Public Suffix List bundled with the desktop app, including exact, wildcard, exception, and internationalized-domain rules. This helps prevent an entry for a public suffix or another tenant on shared hosting from being offered to a different tenant. The bundled list is a snapshot and is updated when the application is updated; it is not fetched dynamically at runtime.
- Only entry ID, title, username, and saved URL are sent for lookup. Passwords, TOTP secrets, notes, custom fields, and key material are excluded from lookup responses. A current TOTP code is generated by the desktop app only during an explicit fill request; the raw TOTP secret is never sent to the extension.
- When locked, the extension shows an unlock message and receives no account metadata.
- Selecting **Fill login** requests the chosen credential from the desktop app, revalidates the account ID and active hostname, and checks the main document plus accessible same-site frames. If the page is on a username-first step, it fills a likely visible username field and asks you to continue on the site; select **Fill login** again after the password step appears. When a password field is present, it fills the password and a likely username field in the same form when available. It dispatches focus, input, change, and blur interactions around each value update to improve compatibility with frameworks that validate on blur. The detector traverses open Shadow DOM roots as well as the main document. If a visible one-time-code field is present, it also fills a current TOTP code when the selected entry has a valid TOTP secret. Cross-origin frames are intentionally not filled.
- Filling is always user-triggered. The extension does not submit the form. Review the filled fields and submit the login yourself.
- After a login is successfully saved or updated, the extension clears and hides the temporary save form, including its password field.
- The extension checks the page hostname again immediately before filling and does not fill if the page changed.
- The host accepts version-1 `ping`, `launchApp`, `unlockVault`, `lockVault`, `getVaultStatus`, `getCredentials`, `fillCredential`, `saveCredential`, and `copyToClipboard` requests. Requests are validated by both the host and desktop endpoint.
- Successful credential fill and save operations reset the desktop AutoLock timer. Passive status checks and credential metadata lookups do not reset it.
- The desktop request endpoint uses the current-user-only `PejPass.BrowserStatus.v1` named pipe; BrowserHost serializes requests to this single-instance pipe while allowing correlated native-messaging requests. Vault-state events use a separate persistent `PejPass.BrowserEvents.v1` pipe. The event pipe is current-user-only on both ends, accepts up to four subscribers, and carries only `locked` or `unlocked` status values. Credential lookup is limited to 25 results.
- The extension requests `activeTab`, `alarms`, `nativeMessaging`, `scripting`, `storage`, and `tabs`. The `tabs` permission lets the background service worker read tab URLs to calculate per-tab matching-account badges and clear badges across tabs when the vault locks; this includes badges retained on inactive tabs. The extension does not run a persistent content script or read page contents in the background.
- When the popup opens, PejPass injects a lightweight `probe` into accessible frames to check for a visible login form and whether its password field is non-empty. The result includes form-presence metadata, the page hostname, and a boolean `hasPassword`; it never returns the username or password value.

## Current scope

Autofill supports visible username-like and password inputs in the main page document, open Shadow DOM roots, and accessible same-site frames. Closed Shadow DOM roots cannot be inspected by the extension. For multi-step login pages, fill the username first, continue on the website, then select **Fill login** again to fill the password or a detected one-time-code field. TOTP autofill requires a valid TOTP secret stored with the selected entry. It does not automatically submit forms or handle login fields inside cross-origin frames.
