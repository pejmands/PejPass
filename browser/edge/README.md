# PejPass for Microsoft Edge

The extension reads the active HTTP(S) tab hostname and requests matching account metadata from the local PejPass desktop app. Passwords and a current TOTP code are retrieved only after the user explicitly selects **Fill login** for a matching account. It can submit a master password, or request a Windows Hello unlock, for local vault verification. The password generator requests cryptographically generated passwords and the shared zxcvbn strength assessment from the desktop app when available, with a local cryptographic fallback if the app cannot be reached.

## Requirements

- Microsoft Edge
- .NET 10 SDK
- A local checkout of this repository

## Build and load the extension

From the repository root, build the Edge package:

```powershell
powershell -ExecutionPolicy Bypass -File .\browser\build.ps1 -Browser edge
```

The generated package is `browser\dist\edge`. To load it:

1. Open `edge://extensions` in Edge.
2. Enable **Developer mode**.
3. Select **Load unpacked**.
4. Choose the `browser/dist/edge` directory.
5. Copy the extension ID shown on the extension card.

## Browser API compatibility

The extension's background and popup scripts use `browser-api.js` as a small compatibility facade. It selects the standard `browser` namespace when available and falls back to `chrome` in Chromium-based browsers. Shared form logic is kept separate from browser-specific manifest and packaging configuration. The Firefox package is a complete, buildable extension package, not just groundwork; it reuses the shared popup and form logic while keeping its manifest and packaging configuration browser-specific. Run the build and tests for each browser package before release.

## Differences between Edge and Firefox

These differences come from the browsers, not from PejPass settings. See `browser/firefox/README.md` for Firefox setup.

| Area | Edge | Chrome | Firefox |
|---|---|---|
| Background | MV3 service worker (`importScripts`); the browser can stop it when idle | Same Chromium service-worker runtime | MV3 event page (`background.scripts`); the browser can unload it when idle |
| Native host manifest | `allowed_origins` with the stable Chromium extension ID; `desktopAppPath` is stored in the manifest | Same Chromium host manifest format and extension ID | `allowed_extensions` with the Gecko ID `pejpass@pejmands.github.io`; `desktopAppPath` is stored in `pejpass-host-config.json` next to the host executable |
| Registry key | `HKCU\Software\Microsoft\Edge\NativeMessagingHosts` | `HKCU\Software\Google\Chrome\NativeMessagingHosts` | `HKCU\Software\Mozilla\NativeMessagingHosts` |
| Frames | `location.ancestorOrigins` lets PejPass verify the top-level site from a same-site subdomain frame | Same as Edge | `ancestorOrigins` is not available, so a subdomain frame cannot prove its top-level site and is not filled; main-document fields still work |
| Popup | The popup stays open until it loses focus | Same as Edge | The popup may close earlier; PejPass waits about 1.2 seconds after a successful fill before closing |
| Toolbar icon | PNG, rendered on a canvas | Same renderer | Same renderer |

To survive background restarts, the last known vault state (`locked`, `unlocked`, `unavailable`, plus whether Windows Hello is ready) is kept in `storage.session`. It contains no credentials, account names, or addresses, and it is cleared when the browser session ends. After a restart the background script confirms the state with the desktop app before relying on it.

## About and diagnostics

Select the version number in the popup header to open the About panel. It shows the extension version, browser, desktop connection, vault state, and the last native host error. **Copy diagnostics** copies those values as text; it never includes passwords, account names, or website addresses. Attach that text when reporting a problem, especially one that differs between Edge and Firefox.

## Shared browser-independent logic

The login-form detection and filling implementation is maintained in `browser/shared/injected.js`. The generated `browser/dist/edge/injected.js` file is the packaged copy loaded by Edge. Edit `browser/shared/injected.js`, not the generated output or runtime template.

After changing the shared implementation, run these commands from `browser/edge`:

```powershell
npm run build
npm test
```

The build script creates a clean Edge package under `browser/dist/edge` and copies the shared source into it. `npm test` builds that package first, then tests the generated copy and shared implementation. The Chrome and Firefox packages are generated separately under `browser/dist/chrome` and `browser/dist/firefox`.

## Build and register the local host

From the repository root, run PowerShell with the extension ID copied above:

```powershell
powershell -ExecutionPolicy Bypass -File .\browser\edge\install-dev-host.ps1 -ExtensionId YOUR_32_CHARACTER_EXTENSION_ID
```

The script builds `src/PejPass.Wpf` and `src/PejPass.BrowserHost`, writes a host manifest under the host's Release output directory, and registers that manifest for the current Windows user in Edge. The manifest records the desktop executable's absolute path in `desktopAppPath`; the host uses this configured path when handling `launchApp`, rather than deriving a path relative to the development checkout. It does not require administrator privileges. Restart Edge after registration if it was already running.

To check Edge, Chrome, and Firefox registrations without changing anything, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\browser\check-host-registration.ps1
```

To remove the Edge registration later:

```powershell
powershell -ExecutionPolicy Bypass -File .\browser\edge\uninstall-dev-host.ps1
```

## Manual checks

- The password generator supports lengths from 8 to 64 characters, configurable lowercase, uppercase, numeric, and symbol sets, and optional exclusion of look-alike characters. It ensures at least one character from each selected set. It uses `crypto.getRandomValues`, rejection sampling to avoid modulo bias, and a cryptographic shuffle. The strength indicator estimates entropy from the selected character pool and length; it is an estimate, not a full password-strength analyzer. Generated passwords are not saved automatically.
- **Generate and fill** only fills a visible field identified as a new-password field or a likely registration form. It does not submit the page. Copying a generated password routes through the desktop clipboard service, which clears it after 30 seconds only if the clipboard still contains the same text; the desktop app and updated BrowserHost must be running.
- The popup displays the active hostname on ordinary HTTP(S) pages; restricted browser pages show an unavailable message.
- The extension background service worker owns one persistent Native Messaging port and routes popup requests through it with request IDs. The desktop pushes vault-state changes to BrowserHost over the separate `PejPass.BrowserEvents.v1` named pipe; no status polling occurs while the desktop event pipe is connected. The toolbar icon and open popup react to these events. A 30-second alarm is used only to re-establish a disconnected Native Messaging connection. The toolbar uses the shared PNG-based icon renderer for both locked and unlocked states, with a locked icon as the default. When unlocked, select **Lock vault** to lock the desktop vault immediately; the popup clears account metadata, save-form values, and its generated password.
- If the desktop app is not running, the popup reports that PejPass is not running and shows the unlock controls. Submitting the master password (or a Windows Hello request) makes BrowserHost start the desktop app and retry for about 10 seconds. BrowserHost finds the executable by reading `desktopAppPath` from `pejpass-host-config.json` (Firefox), then from `com.pejpass.browserhost.json` (Edge), both in its own directory; the path must be fully qualified and point to an existing file. The host also accepts a standalone `launchApp` request, but the popup has no button for it. The development registration scripts point this path at the Release build in the checkout; a production installer should write the actual installed executable path.
- When the vault is locked, the popup accepts the master password and forwards it through Native Messaging and the current-user-only named pipe to the desktop app. The desktop app verifies it using its existing vault-opening flow. The extension clears the password field after each attempt and does not store the password. When Windows Hello is ready for the vault, **Unlock with Windows Hello** asks the desktop app to verify the user. This requires an existing vault selected in PejPass and a Release build created by `install-dev-host.ps1`.
- When unlocked, the popup lists entries whose saved URL host and active site share the same registrable domain (eTLD+1). Matching uses the official Public Suffix List bundled with the desktop app, including exact, wildcard, exception, and internationalized-domain rules. This helps prevent an entry for a public suffix or another tenant on shared hosting from being offered to a different tenant. The bundled list is a snapshot and is updated when the application is updated; it is not fetched dynamically at runtime.
- **Find another account (SSO)** searches all saved accounts by title, username, or website. Each search requires confirmation in the PejPass desktop app. Selecting a result from another website fills it with the domain check bypassed for that one user-selected account, and the desktop app asks for confirmation again before releasing the credential.
- Only entry ID, title, username, and saved URL are sent for lookup. Passwords, TOTP secrets, notes, custom fields, and key material are excluded from lookup responses. A current TOTP code is generated by the desktop app only during an explicit fill request; the raw TOTP secret is never sent to the extension.
- When locked, the extension shows an unlock message and receives no account metadata.
- Selecting **Fill login** requests the chosen credential from the desktop app, revalidates the account ID and active hostname, and checks the main document plus accessible same-site frames. If the page is on a username-first step, it fills a likely visible username field and asks you to continue on the site; select **Fill login** again after the password step appears. When a password field is present, it fills the password and a likely username field in the same form when available. It dispatches focus, input, change, and blur interactions around each value update to improve compatibility with frameworks that validate on blur. The detector traverses open Shadow DOM roots as well as the main document. If a visible one-time-code field is present, it also fills a current TOTP code when the selected entry has a valid TOTP secret. Cross-origin frames are intentionally not filled, except that Edge accepts a same-site subdomain frame when `ancestorOrigins` proves the top-level site.
- Filling is always user-triggered. The extension does not submit the form. Review the filled fields and submit the login yourself.
- After a login is successfully saved or updated, the extension clears and hides the temporary save form, including its password field.
- The extension checks the page hostname again immediately before filling and does not fill if the page changed.
- The host accepts version-1 `ping`, `launchApp`, `unlockVault`, `unlockWithHello`, `lockVault`, `getVaultStatus`, `getCredentials`, `searchCredentials`, `fillCredential`, `saveCredential`, and `copyToClipboard` requests. Requests are validated by both the host and desktop endpoint.
- Successful credential fill and save operations reset the desktop AutoLock timer. Passive status checks and credential metadata lookups do not reset it.
- The desktop request endpoint uses the current-user-only `PejPass.BrowserStatus.v1` named pipe; BrowserHost serializes requests to this single-instance pipe while allowing correlated native-messaging requests. Vault-state events use a separate persistent `PejPass.BrowserEvents.v1` pipe. The event pipe is current-user-only on both ends, accepts up to four subscribers, and carries only `locked` or `unlocked` status values. Credential lookup is limited to 25 results. The pipe serves one request at a time. A request that cannot get its turn within 2 seconds (5 seconds for unlock, account search, and fill) is answered with `app_busy` instead of waiting in a queue; this happens while the desktop app is waiting for you to confirm a fill or an account search. The popup reports it as a busy state, not a connection error. Vault-state events use their own pipe and keep flowing while the request pipe is busy.
- When the popup opens, PejPass injects a lightweight `probe` into accessible frames to check for a visible login form and whether its password field is non-empty. The result includes form-presence metadata, the page hostname, and a boolean `hasPassword`; it never returns the username or password value.

## Permissions

| Permission | Why it is needed |
|---|---|
| `nativeMessaging` | Talk to the local PejPass desktop app through BrowserHost |
| `activeTab` | Grants access to the current tab after the user opens the popup, so the extension can inject the form helper for reading and filling |
| `scripting` | Inject the form helper into the active tab and its accessible frames |
| `tabs` | Read tab URLs in the background to compute per-tab matching-account badges, and to clear badges on all tabs when the vault locks |
| `storage` | `storage.local` keeps recently used account IDs for sorting; `storage.session` keeps the last known vault state across background restarts |
| `alarms` | Re-establish a disconnected Native Messaging connection every 30 seconds |

`tabs` exposes tab URLs and titles but not page contents. Removing it would also remove the badge on tabs other than the one where the popup was opened, because `activeTab` only applies after a user gesture. Replacing it with `host_permissions` for all websites would request broader access (page contents) and is not recommended. The extension does not run a persistent content script or read page contents in the background.

## Current scope

Autofill supports visible username-like and password inputs in the main page document, open Shadow DOM roots, and accessible same-site frames. Closed Shadow DOM roots cannot be inspected by the extension. For multi-step login pages, fill the username first, continue on the website, then select **Fill login** again to fill the password or a detected one-time-code field. TOTP autofill requires a valid TOTP secret stored with the selected entry. It does not automatically submit forms or handle login fields inside cross-origin frames (in Firefox this includes same-site subdomain frames).
