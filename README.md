# PejPass

**PejPass** is a professional, fully offline password manager for Windows.

Built with **C# / .NET 10**, **WPF**, and modern cryptography.

> Your secrets never leave your machine. No accounts. No telemetry. No network.

## Security Model

| Layer              | Technology                          |
|--------------------|-------------------------------------|
| Key Derivation     | **Argon2id** (64 MiB, 3 iterations) |
| Encryption         | **AES-256-GCM** (authenticated)     |
| Master Password    | Never stored (not even hashed)      |
| Storage            | Local encrypted `.pejpass` file     |
| Memory Protection  | Secure zeroization after use        |
| Clipboard          | Auto-clear after 30 seconds         |
| Auto-lock          | Configurable inactivity timeout     |

### Master Password Policy (~75% strict)

- Minimum length: **12 characters**
- Must contain: uppercase, lowercase, digit, special character
- No spaces
- Basic common-password rejection

## Features

- [x] Create / Open / Lock vault
- [x] Entry management (Add / Edit / Delete)
- [x] Custom fields (user-defined name/value pairs)
- [x] Secure password generator
- [x] Auto-lock on inactivity
- [x] Clipboard auto-clear
- [x] Custom vault path (default: `%LOCALAPPDATA%\PejPass`)
- [x] Search (including custom fields)
- [x] **Import from browser CSV** (Chrome, Edge, Firefox)
- [ ] Change master password
- [ ] Export
- [ ] Groups / Folders

## Browser Import

1. In Chrome / Edge: Settings → Passwords → Export passwords → save as CSV
2. In Firefox: about:logins → ••• → Export Logins
3. In PejPass click **Import** and select the CSV file

Supported columns (auto-detected):
`name` / `title`, `url`, `username`, `password`, `notes`

## Requirements

- Windows 10/11
- .NET 10 SDK

## Getting Started

```bash
git clone https://github.com/pejmands/PejPass.git
cd PejPass
dotnet restore
dotnet build
dotnet run --project src/PejPass.Wpf
```

## License

MIT

---

**Disclaimer**: This is a personal project under active development. Use at your own risk. Always keep encrypted backups of your vault.
