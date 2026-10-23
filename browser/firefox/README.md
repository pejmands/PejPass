# PejPass for Firefox

This is the Firefox package for the PejPass desktop companion. It reuses the same popup, native messaging protocol, and shared login-form implementation as the Edge extension. The build creates a ready-to-load package under `browser/dist/firefox`; generated files are not committed.

## Build the extension

From the repository root, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\browser\build.ps1 -Browser firefox
```

In Firefox, open `about:debugging#/runtime/this-firefox`, select **Load Temporary Add-on…**, and choose `browser\dist\firefox\manifest.json`.

The Firefox add-on uses the fixed ID `pejpass@pejmands.github.io` for native messaging registration. This development package is loaded temporarily; Firefox may remove it when the browser closes.

## Register the native messaging host on Windows

From the repository root, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\browser\firefox\install-dev-host.ps1
```

The script builds PejPass and BrowserHost, creates a Firefox-specific host manifest in a separate output directory, and registers it for the current Windows user under `HKCU\Software\Mozilla\NativeMessagingHosts\com.pejpass.browserhost`. The separate manifest and host directory preserve the existing Edge registration and manifest.

Restart Firefox if it was already running. To remove the Firefox registration, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\browser\firefox\uninstall-dev-host.ps1
```

This only removes the Firefox registry entry; it does not remove the Edge registration.

## Validation

From `browser\edge`, run `npm test` to run the browser package tests. Then build Firefox as above and test it in Firefox. Automated tests cannot replace manual testing of native messaging, vault status, credential lookup, login filling, saving, TOTP, and generated-password filling.

This is a development package, not a signed AMO release.
