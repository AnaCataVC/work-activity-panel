# Work Activity Panel - Agent Guide 🤖

Welcome to the **Work Activity Panel** codebase! This document provides essential architectural context, development conventions, workflow standards, build commands, and security guidelines for AI agents and human contributors interacting with this repository.

---

## 1. Project Overview & Architecture

**Work Activity Panel** is a native Windows 11 desktop productivity application built with **WinUI 3** and **.NET 9**. It automates daily work routines, manages schedule-aware application launching, synchronizes private Google Calendar feeds, and backs up local work files to Google Drive using a lightweight Google Apps Script bridge.

### Key Technology Stack
- **Framework & UI:** WinUI 3 with Fluent Design & Mica backdrop (Windows App SDK 2.4.0)
- **Runtime:** .NET 9 (`net9.0-windows10.0.26100.0`, unpackaged desktop app: `<WindowsPackageType>None</WindowsPackageType>`)
- **MVVM Pattern:** `CommunityToolkit.Mvvm` (8.4.0) with source generators (`[ObservableProperty]`, `[RelayCommand]`)
- **Dependency Injection & Hosting:** `Microsoft.Extensions.Hosting` & `Microsoft.Extensions.DependencyInjection` (9.0.2)
- **System Tray Integration:** `H.NotifyIcon.WinUI` (2.1.4)
- **Unit Testing:** `xUnit` (2.5.3) & `Moq` (4.20.72)
- **Installer Packaging:** Inno Setup 6 (`installer.iss`)

---

## 2. Directory Structure & Key Components

```
work-activity-panel/
├── App.xaml / App.xaml.cs          # Application entry point, DI host builder, lifecycle & DispatcherQueue
├── MainWindow.xaml / .cs           # Host window with Mica backdrop & System Tray integration
├── MainPage.xaml / .cs             # Dashboard view (schedule, upcoming meetings, quick actions, sync status)
├── SettingsPage.xaml / .cs         # Configuration view (work hours, vacation mode, iCal, paths, sync rules)
├── ViewModels/
│   ├── DashboardViewModel.cs       # Dashboard state, clock timer, update banner, account switcher, meeting reminders
│   └── SettingsViewModel.cs        # Settings persistence, schedule configuration, iCal/Drive settings, autostart
├── Models/
│   ├── CalendarEvent.cs            # Meeting model (summary, start/end, meeting URL, status)
│   ├── ClaudeMaintenanceModels.cs  # Store reports, retention settings, and cleanup results for local Claude caches
│   ├── DriveSyncSettings.cs        # Google Apps Script URL, auth token, local folder, filters
│   ├── GitHubAccountInfo.cs        # GitHub CLI active/available accounts data model
│   ├── SyncModels.cs               # File metadata, SHA-256 index, upload requests/responses
│   ├── UpdateInfo.cs               # GitHub Releases update check & download metadata
│   └── WorkSchedule.cs             # Work hours, active days, lunch break, vacation state
├── Services/
│   ├── Interfaces/                 # Service contracts (IScheduleService, IClaudeMaintenanceService, etc.)
│   ├── AppLauncherService.cs       # Process launcher (Slack, Granola, browser meeting URLs)
│   ├── ClaudeConfigDiscovery.cs    # Untracked CLAUDE.md & references discovery, git batching & secret filtering
│   ├── ClaudeMaintenanceService.cs # On-demand disk footprint analysis, stale transcript pruning, and desktop session archiving
│   ├── DriveSyncService.cs         # Streaming SHA-256 hashing, filtering, Google Apps Script HTTP client
│   ├── GitHubAuthService.cs        # GitHub CLI integration (hosts.yml parsing, gh auth switch)
│   ├── GoogleCalendarService.cs    # iCal feed fetcher, parser integration, caching, deduplication
│   ├── ScheduleService.cs          # Timer-based schedule evaluation & vacation mode handler
│   └── UpdateService.cs            # GitHub Releases API check, streaming download & Inno installer trigger
├── Helpers/
│   ├── AutostartHelper.cs          # Windows registry startup configuration
│   ├── Converters.cs               # XAML value converters (status colors, visibility, date formatting)
│   ├── ICalParser.cs               # RFC 5545 parser (unfolding, timezone normalization, meeting links)
│   └── LocalSettingsHelper.cs      # JSON persistence in %LOCALAPPDATA%\WorkActivityPanel
├── WorkActivityPanel.Tests/        # xUnit unit test suite for services, models, parsers, and Claude maintenance
├── docs/                           # Setup guides, architecture, and learning documentation
│   ├── README.md                   # Documentation catalog and architectural index
│   └── learning/                   # Engineering learnings & post-implementation case studies
├── Assets/                         # Icons, multi-resolution assets, AppIcon.ico
├── installer.iss                   # Inno Setup 6 standalone installer script
└── releases/                       # Directory for compiled release installers (gitignored)
```

---

## 3. Development Commands (PowerShell)

> [!IMPORTANT]
> When executing commands in PowerShell on Windows, **never** chain commands with `&&` or `||`. Use `;` or run commands as separate steps.

### Building & Running
```powershell
# Restore & build the main project (use & "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" if bare dotnet lacks SDK)
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" build WorkActivityPanel.csproj

# Run the application locally
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" run --project WorkActivityPanel.csproj
```

### Running Unit Tests
```powershell
# Run the complete unit test suite
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" test WorkActivityPanel.Tests\WorkActivityPanel.Tests.csproj
```

### Publishing Releases
```powershell
# Publish self-contained multi-file payload for Inno Setup installer
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" publish WorkActivityPanel.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o releases\WorkActivityPanel-win-x64

# Compile Inno Setup installer (requires Inno Setup 6 installed; outputs to releases/)
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer.iss

# Compress standalone zip
Compress-Archive -Path "releases\WorkActivityPanel-win-x64\*" -DestinationPath "releases\WorkActivityPanel-vX.Y.Z-win-x64.zip" -Force
```

---

## 4. Architectural Rules & Best Practices

### 4.1 Dependency Injection & Service Registration
- All singleton services and view models must be registered in `App.xaml.cs` via `Host.CreateDefaultBuilder()`.
- Services must be decoupled using interfaces located in `Services/Interfaces/`.
- Access services dynamically where constructor injection isn't directly supported using `App.GetService<T>()`.

### 4.2 MVVM and UI Thread Safety
- **Clean MVVM:** View models must inherit from `ObservableObject`. Avoid business logic or direct service polling in code-behind files (`.xaml.cs`).
- **Source Generators:** Utilize `[ObservableProperty]` and `[RelayCommand]` from `CommunityToolkit.Mvvm`.
- **UI Thread Dispatching:** Background timers, file I/O, or HTTP callbacks that update view models bound to the UI must dispatch through the UI thread:
  ```csharp
  App.DispatcherQueue.TryEnqueue(() =>
  {
      StatusMessage = "Sync completed";
  });
  ```

### 4.3 Calendar Engine & RFC 5545 Parsing
- `ICalParser.cs` handles raw `.ics` data. All line-unfolding (CRLF + space/tab), timezone adjustments (`DTSTART`, `DTEND`), cancellation status checks (`STATUS:CANCELLED`), and video conference URL extraction (Google Meet, Zoom, Teams, Webex) must maintain backwards-compatible unit test coverage in `WorkActivityPanel.Tests/ICalParserTests.cs`.

### 4.4 Google Drive Sync & AI Context Discovery Engine
- Hashing must use streaming SHA-256 (`SHA256.Create()`) to avoid loading large files fully into memory.
- Multi-criteria filtering (extension whitelist/blacklist, system folder exclusions, maximum file size in MB) must be strictly enforced before generating upload requests.
- All HTTP requests to Google Apps Script Web Apps must follow redirects (`HttpClientHandler.AllowAutoRedirect = true`) and carry the configured authentication token in the request header or payload.
- **Unversioned AI Context Discovery (`ClaudeConfigDiscovery.cs`):**
  - **Discovery Scope:** Breadth-first walk discovering instruction anchors (`CLAUDE.md` and `.claude/CLAUDE.md`) and reference documentation (`references/**/*.md` and `.claude/references/**/*.md`) within the configured depth limit (`ClaudeMarkdownScanDepth`). All Claude CLI/Desktop internal session directories (`.claude/projects/**/memory`, `.claude/plans`, `.claude/security`, `.claude/cache`, `.claude/plugins`) are strictly excluded.
  - **Excluded Directories:** Always skip dependency and build caches (`node_modules`, `.git`, `.vs`, `bin`, `obj`, `venv`, `.venv`, `__pycache__`, `AppData`, `dist`, `build`, `.obsidian`, `.trash`, `.idea`, and directories starting with `_backup_` or `backup_`).
  - **Batched Git Verification:** To avoid high process-spawning overhead, group candidate files by repository root via `git rev-parse --show-toplevel`, and execute batched queries (`git ls-files -- <batch>`) in chunks of 50 files. Only files not tracked in Git or existing outside any repository are queued for sync.
  - **Multi-Layer Secret Filtering:**
    - Reject files matching sensitive name keywords: `id_rsa`, `id_ed25519`, `credentials`, `auth_token`, `api_key`.
    - Inspect the first 64 KB of candidate files with compiled regex signatures for live infrastructure credentials: SSH private keys (`-----BEGIN ... PRIVATE KEY-----`), AWS access keys (`\bAKIA[0-9A-Z]{16}\b`), GitHub PATs (`\bghp_[A-Za-z0-9_]{36}\b`), and Slack tokens (`\bxox[baprs]-[0-9a-zA-Z]{10,48}\b`).

### 4.5 Settings & Local Persistence
- Configuration files are stored as serialized JSON under `%LOCALAPPDATA%\WorkActivityPanel\` via `LocalSettingsHelper.cs`.
- Never use hardcoded absolute system paths. Always query system directories via:
  ```csharp
  Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
  ```
- **Unit Test Persistence Isolation:** Unit tests MUST NEVER write to or mutate the user's live `%LOCALAPPDATA%\WorkActivityPanel\Data\settings.json`. Static persistence helpers must support scoped temporary paths (`LocalSettingsHelper.SettingsFilePath`) and always clean up on completion.
- **xUnit Static Helper Parallelization:** Disable parallel test execution in `WorkActivityPanel.Tests/TestAssemblyConfig.cs` (`[assembly: CollectionBehavior(DisableTestParallelization = true)]`) to eliminate race conditions across shared static helpers.

### 4.6 Performance & Resource Profiling Guardrails
- **Memory Baseline:** WinUI 3 + DirectComposition + CoreCLR .NET 9 operates with a baseline of ~160 MB Private Commit and ~280 MB Working Set (including ~115 MB of shared OS/GPU DLLs). This is normal, healthy, and expected.
- **No Fake Working Set Trimming:** NEVER call `EmptyWorkingSet()` or `SetProcessWorkingSetSize(-1, -1)` when minimizing to the system tray. This forces physical memory to pagefile on disk, causing hard page faults and a 200–500 ms UI freeze upon reopening.
- **No Forced GC on Tray Hide:** Do not call `GC.Collect()` upon window hide, as it fragments CLR heap segments for negligible memory gains (~2 MB) and triggers thread pauses.
- **Event-Driven Timers:** Keep background services non-polling. Use coalesced `DispatcherTimer` intervals (>= 1 minute) for UI clocks and one-shot `System.Threading.Timer` for schedule/meeting transitions.

### 4.7 GitHub CLI Multi-Account Management
- `GitHubAuthService.cs` integrates directly with GitHub CLI (`gh`).
- Parsing prioritizes local YAML configuration (`%APPDATA%\GitHub CLI\hosts.yml` or `~/.config/gh/hosts.yml`) for sub-millisecond status lookups, with fallback to CLI invocation (`gh auth status`).
- Account switching (`gh auth switch -u <user>`) must always be executed asynchronously with hidden console windows (`CreateNoWindow = true`, `UseShellExecute = false`).

### 4.8 In-App Auto-Updates via GitHub Releases
- `UpdateService.cs` checks the public GitHub Releases API endpoint asynchronously on startup without delaying UI initialization.
- Installer downloads use chunked HTTP streams (80 KB buffers) directly into `%TEMP%` accompanied by `IProgress<double>` progress updates.
- In-place installer launches must spawn the downloaded `.exe` with appropriate arguments and exit cleanly.

### 4.9 Desktop Icon Assets & Shell Cache Reliability
- `Assets/AppIcon.ico` must include native uncompressed 32-bit DIB bitmaps (BITMAPINFOHEADER + BGRA) for resolutions <= 128x128 and PNG for 256x256 to ensure full compatibility with `AppWindow.SetIcon()`, `H.NotifyIcon`, and Inno Setup shortcut binding.
- After updating icon assets or executable binaries locally, ensure any running instances are terminated before overwriting, and restart `explorer.exe` (`Stop-Process -Name explorer -Force`) if the taskbar icon cache does not flush immediately.

### 4.10 Local Claude Sessions & Transcripts Maintenance Architecture
- **Contract & Separation of Concerns:** Defined via `IClaudeMaintenanceService` and implemented in `ClaudeMaintenanceService.cs`, using models in `ClaudeMaintenanceModels.cs` (`ClaudeMaintenanceSettings`, `ClaudeStoreReport`, `ClaudeMaintenanceReport`, `ClaudeCleanupResult`).
- **Target Stores & Dynamic Path Discovery:**
  - Transcripts Store: `%USERPROFILE%\.claude\projects\` (recursive `*.jsonl`).
  - Desktop Sessions Store: `%APPDATA%\Claude\claude-code-sessions\` (`*.json`).
  - Strict privacy rule: NEVER hardcode or log absolute user directory paths. Always resolve dynamically via `Environment.SpecialFolder.UserProfile` and `Environment.SpecialFolder.ApplicationData`.
- **Core Invariants & Safety Guardrails:**
  1. **Strictly Manual / On-Demand (Zero Side-Effects):** Operations never run on automatic timers, background recurring intervals, or as side effects of scanning. Every cleanup action requires an explicit user trigger in the UI.
  2. **Active Session 24-Hour Grace Guard:** In `DeleteStaleTranscriptsAsync`, any transcript file modified within the last 24 hours (`ActiveSessionGrace = TimeSpan.FromHours(24)`) is unconditionally preserved, regardless of configured retention threshold (even if `TranscriptRetentionDays = 0`), protecting currently active or recently resumed sessions.
  3. **Process Guard for Desktop Session Archival:** In `ArchiveStaleSessionsAsync`, execution is strictly refused (`result.Skipped = true`) if the `claude` process is running (`_isClaudeRunning()`). Claude Desktop maintains session objects in memory and would overwrite on-disk changes on flush/exit. Transcripts deletion remains safe while Claude is running due to the 24h grace guard.
  4. **Irreversible Permanent Deletion via UI Confirmation Dialog:** Transcripts deletion is irreversible and actually reclaims physical disk space (`ReclaimsDiskSpace = true`). It MUST be guarded by an explicit user confirmation dialog (`ContentDialog`) in the UI that explains data loss before execution.
  5. **Session Archiving without Disk Space Reclaim:** Archiving desktop sessions isolates the first 1,000 characters (`SessionHeaderChars`) and flips `"isArchived": false` to `"isArchived": true`. This removes sessions from the Claude Desktop session list but reclaims zero bytes on disk (`ReclaimsDiskSpace = false`), which is clearly reported to the user. Original `LastWriteTime` timestamps are preserved and file updates use atomic `.tmp` swapping.

---

## 5. Agent Constraints & Guidelines

### 🔒 Security, Privacy & Documentation Hygiene
1. **No Path Leaks:** NEVER output or commit absolute user paths (e.g., user profiles or personal home directories like `C:\Users\...`). Use relative paths or generic placeholders (e.g., `/path/to/project`, `%LOCALAPPDATA%`, or `~/Work`).
2. **No Secret Leaks:** NEVER hardcode private iCal URLs, Google Apps Script tokens, credentials, or API keys in source code, documentation, or commit messages.
3. **Documentation Deduplication & Canonical Links:** Keep `README.md` concise and high-level by linking to dedicated canonical guides in `docs/` (e.g., `docs/performance-and-resource-profiling.md`, `docs/google-setup-guide.md`) rather than duplicating large technical documentation blocks across multiple files.

### 🚀 Code Style & Conventions
1. **Language:** All source code, identifiers, comments, documentation, and commit messages MUST be in **English**.
2. **Commit Standards:** Use Conventional Commits (`feat: ...`, `fix: ...`, `docs: ...`, `refactor: ...`, `test: ...`, `chore: ...`). Commit messages grouping multiple changes must summarize all included modifications.
3. **Clean Code & KISS:** Keep functions small, modular, and single-responsibility. Centralize reusable logic in `Helpers/`.
4. **Artifact Cleanliness:** Binary deliverables and installer outputs MUST always be placed in the `releases/` directory, which is excluded from git tracking.
5. **Testing Verification:** Whenever modifying business logic, parsers, or services, always add or update corresponding unit tests in `WorkActivityPanel.Tests/`.

### 🌐 Release & Landing Page Synchronization Protocol
Whenever a new release or version tag (e.g., `vX.Y.Z`) is planned and published, agents MUST execute the following mandatory synchronization steps:
1. **Installer Configuration (`installer.iss`):**
   - Update `#define MyAppVersion "X.Y.Z"` and `OutputBaseFilename=WorkActivityPanel-Setup-vX.Y.Z`.
2. **Landing Page Synchronization (`index.html`):**
   - Update all download links in `index.html` to point to the official GitHub Release assets: `https://github.com/AnaCataVC/work-activity-panel/releases/download/vX.Y.Z/WorkActivityPanel-Setup-vX.Y.Z.exe` and `WorkActivityPanel-vX.Y.Z-win-x64.zip`.
   - Update version badges and strings in the navigation and download sections to reflect the new release.
   - **Full UI Mockup Parity:** Update the interactive mockup in `index.html` to reflect ALL new sections, cards, and header badges with live JS simulation and full bilingual dictionary entries (`es` and `en`).
   - **Microsoft Defender SmartScreen Notice:** Ensure the download section contains the explanatory callout for the SmartScreen prompt ("Más información" -> "Ejecutar de todas formas").
   - Update the feature grid and `translations` object (`es` and `en`) with any new features, improvements, or architecture enhancements introduced in that release.
   - Add/update the direct link to the GitHub Release notes (`https://github.com/AnaCataVC/work-activity-panel/releases/tag/vX.Y.Z`).
3. **Build & Release Packaging:**
   - Execute `dotnet publish WorkActivityPanel.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o releases\WorkActivityPanel-win-x64`.
   - Compile the Inno Setup installer via `& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer.iss` into `releases/`.
   - Compress the published output from `releases\WorkActivityPanel-win-x64` into `releases/WorkActivityPanel-vX.Y.Z-win-x64.zip`.
   - Publish the GitHub Release via `gh release create vX.Y.Z` attaching both binaries and bilingual release notes.
4. **Release In-Place Update Protocol (Without Version Bump):**
   - When updating an already published release (e.g. critical fixes in `vX.Y.Z`):
     1. Stage and commit changes to `main` and push to `origin main`.
     2. Move the existing git tag: `git tag -fa vX.Y.Z -m "Release vX.Y.Z: Updated build with fixes"` and `git push origin vX.Y.Z --force`.
     3. Re-upload compiled binaries with `--clobber`: `gh release upload vX.Y.Z releases\WorkActivityPanel-Setup-vX.Y.Z.exe releases\WorkActivityPanel-vX.Y.Z-win-x64.zip --clobber`.
     4. Verify that `index.html` download buttons and GitHub release notes stay synchronized.


