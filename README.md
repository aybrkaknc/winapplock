# 🔒 WinAppLock

A free and open-source Windows application that restricts access to specific apps, files, and folders using a master password or PIN.

---

## ✨ Features

- **Application Locking** — Add any `.exe` to the protected list; WinAppLock suspends its process on launch and shows a full-screen authentication overlay.
- **Multiple Auth Methods** — Choose between a 4-8 digit PIN or an alphanumeric password secured with Argon2id hashing.
- **Recovery System** — One-time recovery key + optional security question so you never get locked out.
- **Real-Time Protection** — A Windows Service monitors process creation via WMI events and enforces locks even when the UI is closed.
- **Drag & Drop** — Drop `.exe` files directly onto the dashboard to lock them instantly.
- **Dark Theme** — A modern, eye-friendly dark interface built with WPF.
- **Bilingual UI** — Fully localized in Turkish and English with instant language switching (no restart needed).
- **System Tray** — Minimizes to the notification area with quick-access menu.
- **Global Hotkey** — `Ctrl+Alt+L` to lock all protected apps from anywhere.
- **Brute-Force Protection** — Configurable failed-attempt limit and cooldown timer.

---

## 🏗️ Architecture

WinAppLock uses a **3-tier architecture** for maximum security:

```
┌──────────────────────────────────────────────────┐
│ WinAppLock.UI  (WPF)                             │
│ Dashboard · Settings · SetupWizard · LockOverlay │
│ System Tray · Global Hotkey                      │
├──────────────────────────────────────────────────┤
│ WinAppLock.Service  (Windows Service)            │
│ ProcessWatcher · ProcessController · HeartbeatWkr│
│ LockStateManager · PipeServer                    │
├──────────────────────────────────────────────────┤
│ WinAppLock.Core  (Shared Library)                │
│ Models · Security (Argon2id) · AppIdentifier     │
│ AppDatabase (SQLite) · IPC Messages              │
└──────────────────────────────────────────────────┘
```

| Layer | Role |
|-------|------|
| **Core** | Shared models, security primitives (hashing, authentication), SQLite database, IPC message contracts. |
| **Service** | Background Windows Service that watches process creation via WMI, suspends/resumes threads with P/Invoke, and communicates with the UI over Named Pipes. |
| **UI** | WPF desktop application with a dark-themed dashboard, first-run setup wizard, full-screen lock overlay with PIN pad, and a settings panel. |

---

## 🛠️ Tech Stack

| Component | Technology |
|-----------|------------|
| Framework | .NET 8.0 (Windows) |
| UI | WPF (Windows Presentation Foundation) |
| Database | SQLite (via Microsoft.Data.Sqlite) |
| Hashing | Argon2id (via Konscious.Security.Cryptography) |
| IPC | Named Pipes (System.IO.Pipes) |
| Process Control | Win32 P/Invoke (SuspendThread / ResumeThread) |
| Localization | XAML ResourceDictionary + DynamicResource |

---

## 🚀 Getting Started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later
- Windows 10 / 11

### Build

```bash
git clone https://github.com/aybrkaknc/winapplock.git
cd winapplock
dotnet build WinAppLock.sln
```

### Run

```bash
# UI Application
dotnet run --project src/WinAppLock.UI

# Windows Service (requires admin)
dotnet run --project src/WinAppLock.Service
```

> **Note:** The Windows Service needs to be registered via `sc create` or installed through the Inno Setup installer for production use.

---

## 📂 Project Structure

```
WinAppLock/
├── WinAppLock.sln
├── LICENSE                      # GPLv3
├── src/
│   ├── WinAppLock.Core/         # Shared library
│   │   ├── Data/                # SQLite database layer
│   │   ├── Models/              # LockedApp, AppSettings, etc.
│   │   ├── Security/            # Argon2id, PIN/Password auth
│   │   ├── Identification/      # PE header + SHA256 app identity
│   │   └── IPC/                 # Named pipe message contracts
│   ├── WinAppLock.Service/      # Windows Service
│   │   ├── ProcessWatcherWorker.cs
│   │   ├── ProcessController.cs
│   │   └── ...
│   └── WinAppLock.UI/           # WPF Application
│       ├── Themes/              # DarkTheme.xaml, Strings.tr/en.xaml
│       ├── Views/               # SetupWizard, SettingsView, LockOverlay
│       ├── Services/            # LocalizationManager, TrayIcon, Hotkey
│       └── MainWindow.xaml
└── tests/
```

---

## 🌐 Localization

WinAppLock supports **Turkish** and **English** with instant switching.

Language files are located in `src/WinAppLock.UI/Themes/`:

| File | Language |
|------|----------|
| `Strings.tr.xaml` | Türkçe |
| `Strings.en.xaml` | English |

To add a new language, create a `Strings.{code}.xaml` file following the same key structure and register it in `LocalizationManager.cs`.

---

## 📜 License

This project is licensed under the **GNU General Public License v3.0** — see the [LICENSE](LICENSE) file for details.

---

## 🤝 Contributing

Contributions are welcome! Please open an issue first to discuss what you would like to change.

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/my-feature`)
3. Commit your changes (`git commit -m 'feat: add my feature'`)
4. Push to the branch (`git push origin feature/my-feature`)
5. Open a Pull Request
