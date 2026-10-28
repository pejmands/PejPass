# PejPass for Google Chrome

PejPass for Chrome reuses the shared Chromium extension runtime and login-form logic used by the Edge package. It has its own manifest and Native Messaging registration under Chrome's per-user registry key.

## Build the unpacked extension

From the repository root, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\browser\build.ps1 -Browser chrome
```

The package is generated at `browser\dist\chrome`.

## Load the extension

1. Open `chrome://extensions` in Chrome.
2. Enable **Developer mode**.
3. Select **Load unpacked**.
4. Choose the `browser\dist\chrome` directory.
5. Copy the extension ID shown on the extension card.

The manifest retains the Chromium extension key used by the Edge package. This keeps the extension identity stable across Chromium browsers and lets the native host authorize the extension ID.

## Register the native messaging host

From the repository root, run PowerShell with the ID shown on `chrome://extensions`:

```powershell
powershell -ExecutionPolicy Bypass -File .\browser\chrome\install-dev-host.ps1 -ExtensionId YOUR_32_CHARACTER_EXTENSION_ID
```

This builds the desktop app and BrowserHost, copies the Release output to the normal PejPass install directory, and registers the host for the current Windows user under `HKCU\Software\Google\Chrome\NativeMessagingHosts\com.pejpass.browserhost`. It does not require administrator privileges.

Restart Chrome if it was already running. Then open the extension popup and test connection, vault unlock, account matching, explicit fill, save/update, and TOTP.

To verify the host registration:

```powershell
powershell -ExecutionPolicy Bypass -File .\browser\check-host-registration.ps1
```

To remove only Chrome's registration:

```powershell
powershell -ExecutionPolicy Bypass -File .\browser\chrome\uninstall-dev-host.ps1
```

If Edge is also registered, uninstalling Chrome leaves the shared Chromium host manifest in place. The extension does not automatically submit login forms; review and submit the form yourself.
