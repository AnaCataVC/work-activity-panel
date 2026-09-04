# Work Activity Panel — Documentation Catalog 📚

Welcome to the **Work Activity Panel** documentation catalog. This directory contains comprehensive technical guides, architecture specifications, and engineering learning postmortems for developers, contributors, and end-users.

---

## 🧭 Documentation Index

### 🚀 Setup & Integration Guides

Practical step-by-step guides for connecting external services and configuring workspace tooling:

- **[Google Drive Backup Setup Guide](google-setup-guide.md)**
  - Architecture of the lightweight Google Apps Script Web App bridge.
  - Step-by-step instructions to create, deploy, and connect a private Google Apps Script endpoint.
  - Base64 payload streaming and Drive folder hierarchy reconstruction.

- **[GitHub Account Switching & CLI Integration Guide](github-account-switching-guide.md)**
  - Multi-account management architecture integrating with GitHub CLI (`gh`).
  - Cross-platform configuration discovery (`~/.config/gh/hosts.yml` and `%APPDATA%\GitHub CLI\hosts.yml`).
  - Workflow for seamless switching between personal and organization profiles without Git credential conflicts.

---

### 🏛️ Architecture & System Profiling

Detailed technical deep-dives into core performance, memory footprint, and subsystem topology:

- **[Resource Consumption & Performance Profiling Guide](performance-and-resource-profiling.md)**
  - Real-world telemetry benchmarks: 0.0% idle CPU and ~160 MB Private Commit memory.
  - Working Set vs. Private Commit anatomical memory breakdown in WinUI 3 + .NET 9.
  - Thread topology, non-polling timer design, and analysis of why legacy working set trimming hacks degrade UX.

---

### 💡 Engineering Learnings & Case Studies (`docs/learning/`)

Architectural postmortems capturing hard-earned lessons, pitfalls, and design patterns discovered during implementation:

1. **[Zero-Infrastructure Auto-Updater Architecture](learning/github-releases-auto-updater-architecture.md)**
   - Leveraging public GitHub Releases API for update checking, chunk streaming downloads, and Inno Setup in-place installations.
2. **[Resolving & Launching Electron Apps with Scoped Names on Windows](learning/granola-windows-electron-launcher.md)**
   - Multi-tier discovery strategy for scoped Electron packages (e.g., `@granolaelectron`) and custom URI protocol fallback.
3. **[Handling H.NotifyIcon System Tray Context Menus in WinUI 3](learning/hnotifyicon-winui3-context-menus.md)**
   - Navigating Win32 native `PopupMenu` message loops vs. WinUI XAML event bubbling with strongly typed `IRelayCommand`.
4. **[Inno Setup Lifecycle, Data Persistence & Clean Uninstallation](learning/inno-setup-persistence-and-clean-uninstall.md)**
   - Decoupling application binaries from local user data (`%LOCALAPPDATA%`) and ensuring clean autostart registry removal.
5. **[Memory Anatomy & Performance Profiling in WinUI 3 + .NET 9](learning/winui3-dotnet9-memory-and-performance-profiling.md)**
   - Deep dive into DirectComposition swapchains, CoreCLR heap behavior, and the detrimental effects of `EmptyWorkingSet`.
6. **[WinUI 3 InfoBar Layout, Visibility & ActionButton Constraints](learning/winui3-infobar-layout-and-actionbutton.md)**
   - Handling `WMC0015` compilation constraints on `InfoBar.ActionButton` and layout spacing optimization.
7. **[Multi-Account Profile Switching via GitHub CLI](learning/github-cli-multi-account-management.md)**
   - Two-tier inspection (`hosts.yml` YAML parser + `gh auth status` fallback) and safe delegated account switching.
8. **[Lightweight RFC 5545 iCalendar Parsing & Meeting Link Extraction](learning/rfc5545-icalendar-parsing-and-meeting-extraction.md)**
   - Line unfolding, timezone normalization, cancellation filtering, and multi-platform meeting link extraction.
9. **[WinUI 3 Async Offloading, ContentDialog Lifecycle & Concurrency Hardening](learning/winui3-async-offloading-and-contentdialog-concurrency.md)**
   - Zero-wait `SemaphoreSlim` dialog anti-collision, `Task.Run` UI offloading, `Interlocked` atomic sync guards, and safe `DispatcherQueue` UI thread marshalling.
10. **[Drive Sync Fast-Path Hash Cache Optimization](learning/drive-sync-fast-path-hash-cache.md)**
    - Disk I/O reduction using metadata-first validation (`LastWriteTimeUtcTicks` + `FileSize`), lazy SHA-256 hashing, 1 KB fast-path threshold, seamless JSON schema migration, and thread-safe `IsMetadataConfirmed` design.
11. **[Calendar Event Mutation Detection & In-Place Collection Reconciliation](learning/calendar-event-mutation-and-inplace-reconciliation.md)**
    - Resolving stale meeting displays when event times are rescheduled in iCal feeds (`UID` immutability trap), value-based equality matching, flicker-free in-place WinUI 3 collection reconciliation, and anti-cache HTTP headers.

---

## 🛠️ Contributor & Agent Guidelines

For development workflows, build commands, coding conventions, and agent guidelines, refer to:
- **[AGENTS.md](../AGENTS.md)**: Agent guide, architectural rules, testing guidelines, and release packaging protocols.
