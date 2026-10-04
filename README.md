# PejPass

**PejPass** is a professional, offline-first password manager for Windows.

Built with **C# / .NET 10**, **WPF**, **CommunityToolkit.Mvvm**, and modern authenticated encryption.

> Your secrets stay on your machine. No accounts. No telemetry. Network is used only when you explicitly ask (for example update check or optional favicons).

## Security Model

| Layer | Technology |
|---|---|
| Key Derivation | **Argon2id** |
| Encryption | **AES-256-GCM** (authenticated encryption) |
| Master Password | Never stored |
| Storage | Local encrypted `.pejpass` vault |
| Memory Handling | Sensitive data cleared after use |
| Clipboard | Automatic clipboard clearing |
| Auto-lock | Configurable inactivity timeout |
| Authentication | Optional Windows Hello unlock |

### Master Password Policy

- Minimum length: **12 characters**
- Requires:
  - Uppercase letter
  - Lowercase letter
  - Digit
  - Special character
- No spaces
- Common password rejection

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
- [x] Password history
- [x] Username history
- [x] Restore previous passwords/usernames
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

Update checks and downloads are **manual only** (from About).

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

- [x] Export entries to a plain-text CSV (Chrome/Edge-style columns)

> Export is for migration convenience — it is **not** an encrypted backup. Prefer copying your `.pejpass` vault file for backups.

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
 └── PejPass.Wpf.Tests
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
