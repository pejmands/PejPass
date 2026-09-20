# PejPass

**PejPass** is a professional, fully offline password manager for Windows.

Built with **C# / .NET 10**, **WPF**, and modern cryptography.

> Your secrets never leave your machine. No accounts. No telemetry. No network.

## Security Model

| Layer              | Technology                          |
|--------------------|-------------------------------------|
| Key Derivation     | **Argon2id** (memory-hard)          |
| Encryption         | **AES-256-GCM** (authenticated)     |
| Master Password    | Never stored (not even hashed)      |
| Storage            | Local encrypted vault file only     |
| Memory Protection  | Secure zeroization after use        |

### Master Password Policy (strict)

- Minimum length: **12 characters**
- Must contain: uppercase, lowercase, digit, special character
- No spaces
- Basic common-password rejection

## Architecture

Clean Architecture + full MVVM (CommunityToolkit.Mvvm)

```
src/
├── PejPass.Domain/          # Entities, Value Objects, Policies
├── PejPass.Application/     # Use cases, Interfaces, Services
├── PejPass.Infrastructure/  # Crypto, File storage, Clipboard
└── PejPass.Wpf/             # WPF UI + ViewModels
```

## Features (Roadmap)

- [x] Project skeleton & security design
- [ ] Create / Open / Lock vault
- [ ] Entry management (CRUD)
- [ ] Secure password generator
- [ ] Auto-lock on inactivity
- [ ] Clipboard auto-clear
- [ ] Custom vault path (default: `%LOCALAPPDATA%\PejPass`)
- [ ] Change master password
- [ ] Search & tags

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

MIT (to be confirmed)

---

**Disclaimer**: This is a personal project under active development. Use at your own risk. Always keep backups of your vault.
