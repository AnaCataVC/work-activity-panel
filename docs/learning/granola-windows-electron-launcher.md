# Learning: Resolving and Launching Electron Apps with Scoped Names on Windows

## Context
In **Work Activity Panel**, the application is responsible for automatically launching companion tools like **Granola** before scheduled calendar meetings and **Slack** at the start of the workday.

## Problem & Challenge
Initial attempts to launch Granola assumed a standard `%LocalAppData%\Granola\Granola.exe` path. However, Granola failed to open because:
1. **Scoped Package Directory Structure**: Electron applications configured with npm package scopes (e.g., `@granolaelectron`) install the binary under `%LocalAppData%\Programs\@granolaelectron\Granola.exe` instead of `%LocalAppData%\Granola\Granola.exe` or `%ProgramFiles%`.
2. **Missing System PATH**: Modern per-user Electron applications do not automatically add themselves to the global system `PATH`, causing standard `Process.Start("Granola.exe")` calls to throw `Win32Exception` (file not found).

## Solution: Multi-Tier Discovery and Launch Strategy
To ensure resilience across different machines, packaging variations, and installations:

1. **Multi-Path Candidate Resolution**:
   Check well-known directories in priority order:
   - `%LocalAppData%\Programs\@granolaelectron\Granola.exe` (Scoped Electron default)
   - `%LocalAppData%\Programs\Granola\Granola.exe` (Standard Electron default)
   - `%LocalAppData%\Granola\Granola.exe` (Squirrel / legacy installer)
   - `%ProgramFiles%\Granola\Granola.exe` (Machine-wide installation)
   - `%ProgramFiles(x86)%\Granola\Granola.exe` (32-bit compatibility)

2. **Protocol Handler Fallback (`granola:`)**:
   Granola registers a custom URI scheme in Windows (`HKCU\Software\Classes\granola\shell\open\command`). Launching `granola:` with `UseShellExecute = true` leverages Windows Shell association to launch the application if binary path resolution misses a non-standard directory.

3. **System PATH Fallback**:
   As a last resort, invoke `Granola.exe` via Windows Shell execution.

## Key Takeaway
When developing native Windows tools that interact with third-party Electron applications:
- Always account for `@scope` directory prefixes under `%LocalAppData%\Programs\`.
- Combine explicit candidate path checks with Windows URI protocol schemes (`UseShellExecute = true`) for robust app launching.
