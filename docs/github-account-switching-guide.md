# GitHub Account Switching & CLI Integration Guide 🐙

## Overview

**Work Activity Panel** provides a native, one-click interface for switching between multiple GitHub accounts (such as personal and organization/work accounts) without terminal friction.

This feature integrates directly with the official [GitHub CLI](https://cli.github.com/) (`gh`), reading local authentication states and executing fast account switches with zero background polling overhead.

---

## 🏗️ Architecture & Interaction Model

```
┌────────────────────────────────────────────────────────┐
│               Work Activity Panel (WinUI 3)            │
│  ┌───────────────────────┐   ┌──────────────────────┐  │
│  │   Dashboard Header    │   │  Settings Page Menu  │  │
│  │  [AnaCataVC] [Switch] │   │  Dropdown Selection  │  │
│  └───────────┬───────────┘   └──────────┬───────────┘  │
└──────────────┼──────────────────────────┼──────────────┘
               │                          │
               ▼                          ▼
┌────────────────────────────────────────────────────────┐
│                   GitHubAuthService                    │
│  1. Fast Parse: ~/.config/gh/hosts.yml                 │
│  2. Fallback CLI: gh auth status                       │
│  3. Account Switch: gh auth switch -u <username>       │
└──────────────────────────┬─────────────────────────────┘
                           │ Process.Start (Hidden Window)
                           ▼
┌────────────────────────────────────────────────────────┐
│                   GitHub CLI (gh.exe)                  │
│       Updates OAuth Token & Active Context             │
└────────────────────────────────────────────────────────┘
```

---

## ⚡ Key Technical Capabilities

### 1. Two-Tier Account Discovery
To ensure sub-millisecond startup times without spawning sub-processes on every frame:
1. **Direct YAML Configuration Parsing (Fast Tier):**
   `GitHubAuthService` inspects GitHub CLI configuration files in standard user paths:
   - `%APPDATA%\GitHub CLI\hosts.yml` (Windows default)
   - `~/.config/gh/hosts.yml` (Cross-platform/POSIX fallback)
   
   The service parses the `github.com` block, extracting `user` (active account) and all entries under `users:` (available accounts).
2. **CLI Fallback (`gh auth status`):**
   If the config file is not found or formatted non-standardly, the service executes `gh auth status` with `CreateNoWindow = true` and parses stdout/stderr to discover authenticated sessions.

### 2. Multi-Candidate CLI Resolution
The service dynamically locates `gh.exe` across known system locations:
- Standard `%ProgramFiles%\GitHub CLI\gh.exe`
- 32-bit `%ProgramFiles(x86)%\GitHub CLI\gh.exe`
- Per-user `%LocalAppData%\Programs\GitHub CLI\gh.exe`
- Per-user `%USERPROFILE%\bin\gh.exe`
- System `PATH` environment resolution via `where.exe gh`

### 3. Non-Blocking Account Switching
When an account switch is triggered from the Dashboard or Settings page:
- Executes `gh auth switch -u <username> --hostname github.com` asynchronously.
- Refreshes account state and notifies the UI via `ActiveAccountChanged` event.
- Updates both the top header chip and settings view models instantaneously without requiring an application restart.

---

## 🔧 Prerequisites & Setup

### 1. Install GitHub CLI
If not already installed, install GitHub CLI on Windows:
```powershell
winget install --id GitHub.cli
```

### 2. Authenticate Multiple Accounts
Authenticate your accounts using the GitHub CLI:
```powershell
# Authenticate your primary/personal account
gh auth login -h github.com

# Authenticate your secondary/work account
gh auth login -h github.com
```

Once logged in, verify available accounts:
```powershell
gh auth status
```

### 3. Using Work Activity Panel
1. Open **Work Activity Panel**.
2. Notice the GitHub account badge in the top-right header of the Dashboard.
3. Click **Cambiar Cuenta** (Switch Account) or navigate to **Settings $\rightarrow$ GitHub Account** to toggle between authenticated accounts seamlessly.

---

## 🔒 Security & Privacy Considerations

- **No Secret Storage:** Work Activity Panel does not store, cache, or intercept your GitHub OAuth tokens, SSH keys, or Personal Access Tokens (PAT).
- **Delegated Authentication:** All token persistence and encryption remain strictly within the official GitHub CLI credential helper and OS credential vault.
- **Local Isolation:** Process execution runs locally on your machine with `UseShellExecute = false` and `CreateNoWindow = true`.
