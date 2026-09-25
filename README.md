# PejPass

**PejPass** is a professional, fully offline password manager for Windows.

Built with **C# / .NET 10**, **WPF**, **CommunityToolkit.Mvvm**, and modern authenticated encryption.

> Your secrets never leave your machine. No accounts. No telemetry. No network.

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
- [x] Custom vault location
- [x] Automatic locking after inactivity
- [x] Windows Hello authentication
- [x] Secure local-only storage

### Credential Management

- [x] Add / edit / delete entries
- [x] Username and password storage
- [x] URL and notes
- [x] Custom fields
- [x] Secret custom fields
- [x] Search across entries and custom fields
- [x] Favorites
- [x] Tags and tag filtering

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

### History & Restore

PejPass keeps entry history snapshots to protect against accidental changes.

Features:

- [x] View previous entry versions
- [x] Compare current data with previous snapshots
- [x] Highlight changed fields
- [x] Restore selected fields only
- [x] Restore complete snapshots
- [x] Delete individual history snapshots
- [x] Delete all history snapshots

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

## User Interface

- Modern WPF interface
- Light / dark theme support
- Custom controls and consistent styling
- Native Windows application
- No WebView dependency

## Project Structure

```
src/
 ├── PejPass.Domain
 ├── PejPass.Application
 ├── PejPass.Infrastructure
 └── PejPass.Wpf
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

The current focus areas include:

- Improving user experience
- Additional import/export options
- More vault management tools
- Further security improvements

## License

MIT

---

**Disclaimer**: PejPass is a personal project under active development.
Always keep encrypted backups of your vault. Use at your own risk.
