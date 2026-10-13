# PejPass

**PejPass** is a professional, offline-first password manager for Windows.

Built with **C# / .NET 10**, **WPF**, **CommunityToolkit.Mvvm**, and modern authenticated encryption.

> Your secrets stay on your machine. No accounts. No telemetry. Network access is optional: update availability can be checked at startup when enabled in Settings, or manually from About; optional favicon fetching is off by default.

## Security Model

| Layer | Technology |
|---|---|
| Key Derivation | **Argon2id** |
| Encryption | **AES-256-GCM** (authenticated encryption) |
| Master Password | Never stored |
| Storage | Local encrypted `.pejpass` vault |
| Memory Handling | Best-effort zeroing of owned sensitive byte buffers |
| Clipboard | Automatic clipboard clearing |
| Auto-lock | Configurable inactivity timeout |
| Authentication | Optional Windows Hello unlock |

Best-effort buffer clearing does **not** guarantee that every plaintext copy is removed from process memory. Managed strings and copies held by other components may remain until reclaimed by the runtime.

### Master Password Policy

- Minimum length: **12 characters**
- Maximum length: **256 characters** (the estimator's evaluation limit)
- Requires the maximum zxcvbn score of **4** ("Very strong")
- Rejects common and predictable patterns using zxcvbn's bundled dictionaries and pattern matching
- Spaces are allowed, so strong multi-word passphrases can be used
- The bundled word dictionaries are English-focused, so common non-English phrases may be overrated.

## Features

### Vault Management

- [x] Create encrypted vault
- [x] Open / lock vault
- [x] Custom vault location and recent vault list
- [x] Automatic locking after inactivity
- [x] Windows Hello authentication
- [x] Change master password
- [x] Secure local-only storage
- [x] Soft-delete trash with restore / permanent delete

### Credential Management

- [x] Add / edit / delete entries
- [x] Username and password storage
- [x] URL and notes
- [x] Custom fields
- [x] Secret custom fields
- [x] Search across entries and custom fields
- [x] Favorites
- [x] Tags and tag filtering
- [x] Entry sort modes (favorites stay on top)

### Password Features

- [x] Secure password generator
- [x] Vault health analysis

### Two-Factor Authentication

- [x] TOTP support
- [x] Generate time-based verification codes
- [x] Store TOTP secrets securely
- [x] Import TOTP from QR / otpauth URI (where available)

### History & Restore

PejPass keeps entry history snapshots to protect against accidental changes.

- [x] View previous entry versions
- [x] Compare current data with previous snapshots
- [x] Highlight changed fields
- [x] Restore selected fields only
- [x] Restore complete snapshots
- [x] Delete individual history snapshots
- [x] Delete all history snapshots

### Updates & What’s New

Update availability is checked in the background at startup only when **Check for updates at startup** is enabled in Settings (off by default). You can also check manually from About. Updates are never installed automatically: downloading and installing a portable update requires user confirmation.

- [x] Check for updates via remote `update.json` manifest
- [x] Download & install portable update (with user confirmation)
- [x] Open download link in the browser
- [x] What’s New changelog (cache-first, multi-version notes)
- [x] Show What’s New once after a successful update

Version and release date shown in About come from the running `PejPass.exe` metadata.

## Import & Export

### Browser Import

Supported browsers:

- Chrome
- Microsoft Edge
- Firefox

Steps:

1. Export passwords from your browser as CSV
2. Open PejPass
3. Import the CSV file

Supported columns:

```
name / title
url
username
password
notes
```

### CSV Export

- [x] Export entries as browser-compatible CSV (Chrome/Edge-style columns)
- [x] Export entries in PejPass-specific CSV format

Both CSV formats are **unencrypted plain text** intended for data portability, not secure backups. Anyone who can read an exported CSV may be able to access its credentials. Export only when needed, store the file securely, and delete it when finished.

### Encrypted Backup

Use **Backup** from the title-bar menu (shortcut: **Ctrl+B**) to export an encrypted `.pejpass` vault backup. Prefer this over CSV export for routine backups. Store backup copies separately from the active vault and keep your master password safe.

## User Interface

- Modern WPF interface with custom window chrome
- Light / dark / system theme
- Font size and zoom
- Keyboard shortcuts reference in Settings
- Optional online favicon fetching (off by default)
- About, Settings, and What’s New available from the title-bar menu (including before unlock, with appearance-only settings on the login screen)
- Native Windows application
- No WebView dependency

## Project Structure

```
src/
 ├── PejPass.Domain
 ├── PejPass.Application
 ├── PejPass.Infrastructure
 └── PejPass.Wpf

tests/
 ├── PejPass.Domain.Tests
 ├── PejPass.Infrastructure.Tests
 ├── PejPass.Wpf.Tests
 └── PejPass.UpdateSigner.Tests

tools/
 └── PejPass.UpdateSigner
```

## Requirements

- Windows 10 / Windows 11
- .NET 10 SDK

## Getting Started

```bash
git clone https://github.com/pejmands/PejPass.git

cd PejPass

dotnet restore
dotnet build
dotnet run --project src/PejPass.Wpf
```

## Development Status

PejPass is an actively developed personal project.

Recent focus areas include:

- About / update / What’s New experience
- Appearance and pre-login settings
- Vault trash and recovery flows
- Further UX and security polish

## License

MIT

---

**Disclaimer**: PejPass is a personal project under active development.
Always keep encrypted backups of your vault. Use at your own risk.
