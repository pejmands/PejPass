# PejPass for Firefox

This is the Firefox package for the PejPass desktop companion. It reuses the same popup, native messaging protocol, and shared login-form implementation as the Chromium extension. The build creates a ready-to-load package under `browser/dist/firefox`; generated files are not committed.

## Build the extension for temporary development use

From the repository root, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\browser\build.ps1 -Browser firefox
```

In Firefox, open `about:debugging#/runtime/this-firefox`, select **Load Temporary Add-on…**, and choose `browser\dist\firefox\manifest.json`.

This temporary installation is for development only. Firefox removes temporary add-ons when the browser closes.

## Create a signed XPI for permanent installation

Standard Firefox builds require Mozilla-signed extensions. PejPass can be distributed privately as an **unlisted** add-on: Mozilla signs it, but it is not publicly listed on AMO. The manifest declares the stable ID `pejpass@pejmands.github.io`, which must remain unchanged for native messaging compatibility.

1. Sign in to the [Firefox Add-ons Developer Hub](https://addons.mozilla.org/developers/).
2. Create API credentials from [AMO API credentials](https://addons.mozilla.org/developers/addon/api/key/). Keep the secret private.
3. In PowerShell at the repository root, set the credentials for the current session:

   ```powershell
   $env:WEB_EXT_API_KEY = "your-amo-api-key"
   $env:WEB_EXT_API_SECRET = "your-amo-api-secret"
   ```

4. Run the signing script:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\browser\firefox\sign-addon.ps1
   ```

   The script rebuilds the Firefox package and calls `web-ext sign` for the unlisted distribution channel. It places the signed `.xpi` output under `browser\dist\signed-firefox`. The first submission may be subject to Mozilla's review; signing is controlled by Mozilla and cannot be completed without your AMO credentials.

5. In Firefox, open `about:addons`, select the gear menu, choose **Install Add-on From File…**, and select the signed `.xpi`. Confirm the installation.

Do not use the unsigned package in `browser\dist\firefox` for permanent installation. Never commit AMO API credentials or paste them into chat.

## Register the native messaging host on Windows

From the repository root, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\browser\firefox\install-dev-host.ps1
```

The script builds PejPass and BrowserHost, creates a Firefox-specific host manifest in a separate output directory, and registers it for the current Windows user under `HKCU\Software\Mozilla\NativeMessagingHosts\com.pejpass.browserhost`. The separate manifest and host directory preserve the existing Chromium registrations and manifest.

The Firefox manifest does not contain the desktop executable path. The script writes it to `pejpass-host-config.json` next to the Firefox copy of the host executable instead. BrowserHost reads that file first and falls back to `com.pejpass.browserhost.json` in its own directory (the Chromium case), so both browser families resolve the path correctly. The script copies only the files in the host output directory, not subdirectories.

Restart Firefox if it was already running. To remove the Firefox registration, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\browser\firefox\uninstall-dev-host.ps1
```

This only removes the Firefox registry entry; it does not remove either Chromium registration.

To check both registrations without changing anything, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\browser\check-host-registration.ps1
```

## Known differences from Chromium (Edge and Chrome)

- **Frames:** Firefox does not provide `location.ancestorOrigins`. PejPass cannot prove the top-level site from inside a cross-origin frame, so it does not fill fields in such frames, including same-site subdomain frames that Chromium browsers accept. Fields in the main document and open Shadow DOM roots work normally.
- **Background page:** Firefox runs the background script as an event page that the browser can unload when idle, while Chromium browsers run a service worker. PejPass stores the last known vault state in `storage.session` and confirms it with the desktop app after a restart.
- **Popup lifetime:** Firefox may close the popup sooner than Edge. PejPass delays closing the popup by about 1.2 seconds after a successful fill so the result message can be read.
- **Controls:** Firefox draws form controls differently. The popup styles `select` elements itself to keep them consistent.
- **Native host configuration:** `desktopAppPath` lives in `pejpass-host-config.json` (Firefox) instead of the host manifest (Chromium).

Use **About** (select the version number in the popup header) and **Copy diagnostics** to compare the state of both browsers when behavior differs.

## Validation

From `browser\chromium`, run `npm test` to run the browser package tests. Then build Firefox as above and test it in Firefox. Automated tests cannot replace manual testing of native messaging, vault status, credential lookup, login filling, saving, TOTP, and generated-password filling.

Also test unlocking in Firefox with the desktop app closed: BrowserHost should start the desktop app and complete the unlock. This depends on `pejpass-host-config.json` being next to the Firefox copy of the host executable; `check-host-registration.ps1` verifies it.
