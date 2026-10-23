# PejPass for Firefox

This is the Firefox package for the PejPass desktop companion. It reuses the same popup, native messaging protocol, and shared login-form implementation as the Edge extension. The build creates a ready-to-load package under `browser/dist/firefox`; generated files are not committed.

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

The script builds PejPass and BrowserHost, creates a Firefox-specific host manifest in a separate output directory, and registers it for the current Windows user under `HKCU\Software\Mozilla\NativeMessagingHosts\com.pejpass.browserhost`. The separate manifest and host directory preserve the existing Edge registration and manifest.

Restart Firefox if it was already running. To remove the Firefox registration, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\browser\firefox\uninstall-dev-host.ps1
```

This only removes the Firefox registry entry; it does not remove the Edge registration.

## Validation

From `browser\edge`, run `npm test` to run the browser package tests. Then build Firefox as above and test it in Firefox. Automated tests cannot replace manual testing of native messaging, vault status, credential lookup, login filling, saving, TOTP, and generated-password filling.
