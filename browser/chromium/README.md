# PejPass for Chromium (Microsoft Edge and Google Chrome)

The Chromium extension uses the same source and stable extension ID in Edge and Chrome. There is one maintained package for both browsers; Firefox has its own package.

## Build the extension

From the repository root, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\browser\build.ps1 -Browser chromium
```

The unpacked package is generated at `browser\dist\chromium`.

## Load it in Edge or Chrome

1. Open `edge://extensions` in Edge or `chrome://extensions` in Chrome.
2. Enable **Developer mode**.
3. Select **Load unpacked**.
4. Choose `browser\dist\chromium`.
5. Copy the extension ID shown on the extension card. The manifest contains a stable key, so Edge and Chrome should show the same Chromium extension ID.

## Register the native messaging host

From the repository root, run PowerShell with the extension ID:

```powershell
powershell -ExecutionPolicy Bypass -File .\browser\chromium\install-dev-host.ps1 -ExtensionId YOUR_32_CHARACTER_EXTENSION_ID
```

This builds the PejPass desktop app and BrowserHost, copies their Release output into `%LOCALAPPDATA%\Programs\PejPass`, and writes a shared host manifest. It registers that same manifest for the current Windows user under both `HKCU\Software\Microsoft\Edge\NativeMessagingHosts` and `HKCU\Software\Google\Chrome\NativeMessagingHosts`. Administrator privileges are not required.

**Important:** the browser and BrowserHost must run in the same Windows environment and user context. If Edge or Chrome runs inside Windows Sandbox or another isolated environment, install PejPass and register BrowserHost inside that environment too. A registration on the Windows host is not visible inside the sandbox.

To remove both Chromium registrations later, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\browser\chromium\uninstall-dev-host.ps1
```

To inspect the current Chromium and Firefox registrations without changing anything, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\browser\check-host-registration.ps1
```

## Tests and shared logic

The login-form detection and filling implementation is maintained in `browser\shared\injected.js`. Do not edit generated files under `browser\dist`.

From `browser\chromium`, run:

```powershell
npm test
```

The `pretest` step builds `browser\dist\chromium` before running the Node tests. This package can then be loaded in either Edge or Chrome. Restart the selected browser after changing Native Messaging registration or BrowserHost binaries. The extension never automatically submits a login form; review and submit the form yourself.
