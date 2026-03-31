# 🔒 WinAppLock

A free and open-source (GPLv3) Windows application that restricts access to specific apps using a proactive **IFEO Gatekeeper** architecture. Zero-latency, multi-process aware, and secure.

---

## ✨ Features

- **Proactive Interception (IFEO)** — Unlike reactive WMI-based tools, WinAppLock uses Windows *Image File Execution Options* to intercept protected applications *before* they even load into memory. **Zero latency, zero bypass.**
- **Gatekeeper Engine** — A tiny, lightweight stub that negotiates launch verdicts with the Windows Service, ensuring a "Fail-Closed" security policy.
- **Multi-Process Grace Period** — Intelligent "10-second wave" protection that allows multi-process apps like Google Chrome and Discord to launch their background tabs and workers without repeated password prompts.
- **Multiple Auth Methods** — Secure your apps with a 4-8 digit PIN or an alphanumeric password, hashed using the industry-standard **Argon2id**.
- **Real-Time Health Guard** — The background Service periodically verifies IFEO registry integrity and repairs any missing or tampered protection keys (Anti-Tamper).
- **Global Hotkey** — Instantly lock all protected applications using `Ctrl+Alt+L`.
- **"Dark Retro" Windows 98 Aesthetic** — Pixel-perfect recreation of the classic Windows 98 UI, featuring 3D bevel (Sunken/Raised) effects, Tahoma typography (Hi-DPI optimized), and original 8-bit icons.
- **Dynamic Theme Engine** — Fully customizable UI including Title Bar gradients (8-bit vs Smooth), navigation styles (Classic Tabs vs Modern Sidebar), and instant animation toggles.

---

## 🏗️ Architecture

WinAppLock uses a **4-tier architecture** for robust security and performance:

```
┌───────────────────────────────────────────────────────┐
│ WinAppLock.UI (WPF) - "Dark Retro" Edition            │
│ Tabbed Property Sheets · Retro Settings · LockOverlay │
└───────────┬───────────────────────────────────────────┘
            │ Pipe (IPC)
┌───────────▼───────────────────────────────────────────┐
│ WinAppLock.Service (Windows Service - SYSTEM)         │
│ LockStateManager · GatekeeperPipeServer (Duplex)      │
│ HeartbeatWorker (IFEO Guard) · IfeoRegistrar          │
└───────────▲───────────────────────────────────────────┘
            │ Pipe (IPC)
┌───────────┴───────────────────────────────────────────┐
│ WinAppLock.Gatekeeper  (IFEO Debugger Stub)           │
│ "The Interceptor" - Redirects execution to Service    │
└─────────────────────────────────────┬─────────────────┘
                                      │ Refers to
┌─────────────────────────────────────▼─────────────────┐
│ WinAppLock.Core (Shared Library)                      │
│ Models (v0.0.103) · Argon2id Security · AppIdentifier │
│ AppDatabase (SQLite) · IPC Message Contracts          │
└───────────────────────────────────────────────────────┘
```

### Component Roles

| Layer | Role |
|-------|------|
| **Gatekeeper** | The first point of contact. Windows executes this stub instead of the protected app. It asks the Service for a "Verdict" (Allow/Deny). |
| **Service** | The brain of the system. Manages lock states, validates passwords via IPC, and handles IFEO Registry keys. Runs as `SYSTEM`. |
| **UI** | The interaction layer. Handles user configuration and displays the full-screen auth overlay when triggered by the Service. |
| **Core** | Shared logic, Argon2id hashing, SQLite persistence, and IPC message definitions. |

---

## ⚙️ How it Works (IFEO Flow)

1. **User clicks `chrome.exe`.**
2. **Windows Registry** (Image File Execution Options) redirects execution to our **`Gatekeeper.exe`**.
3. **Gatekeeper** connects to the Service via a **Duplex Named Pipe** and asks: *"Can I launch Chrome?"*
4. **Service** checks if Chrome is locked:
   - **If Unlocked/In Grace Period:** Returns `Allow`.
   - **If Locked:** Service signals the **UI** to show the **LockOverlay**.
5. Once the user enters the correct password/PIN, the Service:
   - Temporarily pulls the IFEO key.
   - Tells Gatekeeper to `Allow`.
   - Restores the IFEO key for subsequent launches.

---

## 🛠️ Tech Stack

| Component | Technology |
|-----------|------------|
| Framework | .NET 8.0 (Windows) |
| UI | WPF (Windows Presentation Foundation) |
| Interception | Image File Execution Options (IFEO) Debugger Redirection |
| Database | SQLite (via Microsoft.Data.Sqlite) |
| Hashing | Argon2id (via Konscious.Security.Cryptography) |
| IPC | Duplex Named Pipes (System.IO.Pipes) |
| Localization | XAML ResourceDictionary + DynamicResource |

---

## 🚀 Getting Started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows 10 / 11 (Admin access required for IFEO registration)

### Build & Deploy

```bash
# 1. Clone and Build
git clone https://github.com/aybrkaknc/winapplock.git
dotnet build WinAppLock.sln

# 2. Deploy Gatekeeper (Temporary manual step for test environment)
# Gatekeeper MUST be in this specific path for IFEO paths to work:
xcopy "src/WinAppLock.Gatekeeper/bin/Debug/net8.0-windows/*" "C:/ProgramData/WinAppLock/" /Y
```

### Run (Testing)

Use the provided scripts to start the environment in admin mode:
- `StartTest.bat`: Builds, deploys, and starts both Service and UI.
- `StopTest.bat`: Cleans up processes and restores Registry settings.

---

## 📂 Project Structure

```
WinAppLock/
├── src/
│   ├── WinAppLock.Gatekeeper/   # IFEO Interception Stub
│   ├── WinAppLock.Service/      # Core Logic & Registry Management
│   ├── WinAppLock.UI/           # WPF Interface & Auth Screens
│   └── WinAppLock.Core/         # Shared Library, DB, & Security
└── StartTest.bat                # Automated test environment
```

---

## 📜 License

Licensed under the **GNU General Public License v3.0**. See [LICENSE](LICENSE) for details.
