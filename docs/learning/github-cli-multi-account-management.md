# Learning: Multi-Account Profile Switching via GitHub CLI in Desktop Applications

## Context
Developers frequently balance personal open-source contributions and organization/work repositories on the same machine. Managing multiple GitHub accounts typically requires manually running CLI commands, managing SSH configs, or navigating web browsers.

## Problem & Challenge
For **Work Activity Panel**, we needed a lightweight, zero-latency desktop mechanism to:
1. Detect whether GitHub CLI (`gh`) is installed on the machine across multiple candidate paths.
2. Read the active account and all configured accounts without executing slow subprocesses on every UI load.
3. Allow 1-click switching between accounts from the application dashboard and settings menu.
4. Prevent any credential leakage into application state or logs.

## Solution Architecture: Two-Tier Inspection & Safe Subprocess Delegation

```
┌────────────────────────────────────────────────────────┐
│               Work Activity Panel (WinUI 3)            │
└───────────────────────────┬────────────────────────────┘
                            │
                            ▼
┌────────────────────────────────────────────────────────┐
│                   GitHubAuthService                    │
│                                                        │
│  ┌──────────────────────────────────────────────────┐  │
│  │ Tier 1: Fast YAML Parse (~/.config/gh/hosts.yml) │  │
│  │ - Reads 'user' (active) and 'users' (available)  │  │
│  │ - Zero subprocess overhead (< 1 ms latency)      │  │
│  └──────────────────────────────────────────────────┘  │
│                            │ (Fallback if not found)   │
│                            ▼                           │
│  ┌──────────────────────────────────────────────────┐  │
│  │ Tier 2: CLI Query (gh auth status)               │  │
│  │ - Spawns hidden process (CreateNoWindow = true)  │  │
│  │ - Parses standard output / error streams         │  │
│  └──────────────────────────────────────────────────┘  │
└───────────────────────────┬────────────────────────────┘
                            │ Account Switch Triggered
                            ▼
┌────────────────────────────────────────────────────────┐
│  Execute: gh auth switch -u <user> --hostname github.com│
│  - Asynchronous execution                              │
│  - Fires ActiveAccountChanged event                    │
└────────────────────────────────────────────────────────┘
```

### 1. Fast Direct YAML Parsing
Spawning `gh auth status` takes 150–300 ms because it spawns a full CLI environment and validates tokens. To keep UI startup instantaneous:
- `GitHubAuthService` directly reads `%APPDATA%\GitHub CLI\hosts.yml` (Windows) or `~/.config/gh/hosts.yml` (POSIX).
- Extracts the active username and the list of secondary accounts in `< 1 ms`.

### 2. Multi-Candidate CLI Resolution
To prevent `Win32Exception` when the CLI is installed per-user without modifying system `PATH`:
- The service inspects `%ProgramFiles%\GitHub CLI\gh.exe`, `%LocalAppData%\Programs\GitHub CLI\gh.exe`, and `%USERPROFILE%\bin\gh.exe` before falling back to `where.exe gh`.

### 3. Delegated Security & Zero Token Storage
- Work Activity Panel **never** reads, stores, or handles OAuth tokens or secrets.
- All credential persistence remains encrypted within the official GitHub CLI credential helper (Windows Credential Manager).

## Key Takeaway
When integrating third-party CLI tooling in desktop applications:
- Prefer fast direct configuration reading for telemetry and state display.
- Rely on official CLI subcommands (`gh auth switch`) for state mutations.
- Keep credentials strictly isolated within the tool's native secure store.
