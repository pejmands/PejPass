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

## Architecture

Clean Architecture + full MVVM (CommunityToolkit.Mvvm) + Microsoft.Extensions.DependencyInjection

```
src/
├── PejPass.Domain/          # Entities, Value Objects, Policies, Settings
├── PejPass.Application/     # Use cases, Interfaces, Services
├── PejPass.Infrastructure/  # Crypto, File storage, Clipboard
└── PejPass.Wpf/             # WPF UI + ViewModels
```

## Current Features

- [x] Project skeleton & security design
- [x] Create / Open / Lock vault
- [x] Entry management (Add / Edit / Delete)
- [x] Secure password generator
- [x] Auto-lock on inactivity
- [x] Clipboard auto-clear
- [x] Custom vault path (default: `%LOCALAPPDATA%\PejPass`)
- [x] Search entries
- [ ] Change master password
- [ ] Import / Export
- [ ] Groups / Folders

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
