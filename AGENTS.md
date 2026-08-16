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
- **Unit Testing:** `xUnit` (2.9.2) & `Moq` (4.20.72)
- **Installer Packaging:** Inno Setup 6 (`installer.iss`)

---

## 2. Directory Structure & Key Components

```
work-activity-panel/
├── App.xaml / App.xaml.cs          # Application entry point, DI host builder, lifecycle & DispatcherQueue
├── MainWindow.xaml / .cs           # Host window with Mica backdrop & System Tray integration
├── MainPage.xaml / .cs             # Dashboard view (schedule, upcoming meetings, quick actions, sync status)
├── SettingsPage.xaml / .cs         # Configuration view (work hours, vacation mode, iCal, paths, sync rules)
├── Models/
│   ├── CalendarEvent.cs            # Meeting model (summary, start/end, meeting URL, status)
│   ├── WorkSchedule.cs             # Work hours, active days, lunch break, vacation state
│   ├── DriveSyncSettings.cs        # Google Apps Script URL, auth token, local folder, filters
│   └── SyncModels.cs               # File metadata, SHA-256 index, upload requests/responses
├── Services/
│   ├── Interfaces/                 # Service contracts (IScheduleService, IAppLauncherService, etc.)
│   ├── ScheduleService.cs          # Timer-based schedule evaluation & vacation mode handler
│   ├── AppLauncherService.cs       # Process launcher (Slack, Granola, browser meeting URLs)
│   ├── GoogleCalendarService.cs    # iCal feed fetcher, parser integration, caching, deduplication
│   └── DriveSyncService.cs         # Streaming SHA-256 hashing, filtering, Google Apps Script HTTP client
├── Helpers/
│   ├── ICalParser.cs               # RFC 5545 parser (unfolding, timezone normalization, meeting links)
│   ├── LocalSettingsHelper.cs      # JSON persistence in %LOCALAPPDATA%\WorkActivityPanel
│   ├── AutostartHelper.cs          # Windows registry startup configuration
│   └── Converters.cs               # XAML value converters (status colors, visibility, date formatting)
├── WorkActivityPanel.Tests/        # xUnit unit test suite for services, models, and parsers
├── docs/                           # Setup guides and architecture documentation
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
# Restore & build the main project
dotnet build WorkActivityPanel.csproj

# Run the application locally
dotnet run --project WorkActivityPanel.csproj
```

### Running Unit Tests
```powershell
# Run the complete unit test suite
dotnet test WorkActivityPanel.Tests\WorkActivityPanel.Tests.csproj
```

### Publishing Releases
```powershell
# Publish self-contained single-file x64 release
dotnet publish WorkActivityPanel.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o bin\Release\net9.0-windows10.0.26100.0\win-x64\publish

# Compile Inno Setup installer (requires Inno Setup 6 installed; outputs to releases/)
iscc installer.iss
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

### 4.4 Google Drive Sync Engine
- Hashing must use streaming SHA-256 (`SHA256.Create()`) to avoid loading large files fully into memory.
- Multi-criteria filtering (extension whitelist/blacklist, system folder exclusions, maximum file size in MB) must be strictly enforced before generating upload requests.
- All HTTP requests to Google Apps Script Web Apps must follow redirects (`HttpClientHandler.AllowAutoRedirect = true`) and carry the configured authentication token in the request header or payload.

### 4.5 Settings & Local Persistence
- Configuration files are stored as serialized JSON under `%LOCALAPPDATA%\WorkActivityPanel\` via `LocalSettingsHelper.cs`.
- Never use hardcoded absolute system paths. Always query system directories via:
  ```csharp
  Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
  ```

---

## 5. Agent Constraints & Guidelines

### 🔒 Security & Privacy
1. **No Path Leaks:** NEVER output or commit absolute user paths (e.g., user profiles or personal home directories). Use relative paths or generic placeholders (e.g., `/path/to/project` or `%LOCALAPPDATA%`).
2. **No Secret Leaks:** NEVER hardcode private iCal URLs, Google Apps Script tokens, credentials, or API keys in source code, documentation, or commit messages.

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
   - Update the feature grid and `translations` object (`es` and `en`) with any new features, improvements, or architecture enhancements introduced in that release.
   - Add/update the direct link to the GitHub Release notes (`https://github.com/AnaCataVC/work-activity-panel/releases/tag/vX.Y.Z`).
3. **Build & Release Packaging:**
   - Execute `dotnet publish` for `win-x64` self-contained single-file.
   - Compile the Inno Setup installer via `iscc installer.iss` into `releases/`.
   - Compress the published output into `releases/WorkActivityPanel-vX.Y.Z-win-x64.zip`.
   - Publish the GitHub Release via `gh release create vX.Y.Z` attaching both binaries and bilingual release notes.

