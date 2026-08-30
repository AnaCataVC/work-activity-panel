# Learning: Inno Setup Lifecycle, Data Persistence & Clean Uninstallation

## Context
**Work Activity Panel** is an unpackaged Windows 11 desktop application (.NET 9 + WinUI 3) distributed as an Inno Setup standalone executable. It stores user configuration, synchronization hashes, and scheduling state under `%LOCALAPPDATA%\WorkActivityPanel\Data\` and startup registration under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

## Problem & Challenge
By default, Inno Setup only deletes files created in the application destination folder (`{app}`). This led to two critical problems:
1. **Orphaned User Data and "Phantom Settings" on Clean Reinstalls**: When a user uninstalled the application, `%LOCALAPPDATA%\WorkActivityPanel` remained intact on disk. Reinstalling weeks later unexpectedly loaded stale settings, tokens, and outdated paths.
2. **Broken Windows Autostart Registry Entries**: If autostart was enabled, uninstalling the app left a dangling entry in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\WorkActivityPanel` pointing to a non-existent binary path.
3. **Risk of Data Loss on Updates**: In-place version upgrades (e.g., v1.1.0 -> v1.2.0) needed to preserve 100% of user schedules, iCal feeds, Drive sync rules, and SHA-256 hash indices without resetting configurations.

## Solution: Lifecycle Decoupling in Inno Setup

To standardize state retention across installation, update, and uninstallation:

### 1. In-Place Update Preservation
Inno Setup relies on a persistent `AppId` (`{{D9A8374E-57B2-4A2D-A3D8-5B1D2F7A8E9C}}`). When a new installer version runs:
* Running processes are closed automatically via `taskkill`.
* Application binaries under `{app}` (`%LOCALAPPDATA%\Programs\WorkActivityPanel`) are replaced.
* User data under `%LOCALAPPDATA%\WorkActivityPanel\Data\` is completely untouched.

### 2. Automatic Registry Cleanup on Uninstallation
Added the `[Registry]` section with the `uninsdeletevalue` flag:
```pascal
[Registry]
; Clean up Autostart entry from Current User registry upon uninstallation
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "{#MyAppName}"; Flags: uninsdeletevalue
```

### 3. Total Data Directory Cleanup on Uninstallation
Added declarative cleanup directives and Pascal code execution upon uninstall completion:
```pascal
[UninstallDelete]
; Clean up runtime user data, settings, and sync hashes upon full uninstallation
Type: filesandordirs; Name: "{localappdata}\WorkActivityPanel\Data"
Type: dirifempty; Name: "{localappdata}\WorkActivityPanel"

[Code]
// Clean up user data directory upon uninstall completion
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DelTree(ExpandConstant('{localappdata}\WorkActivityPanel'), True, True, True);
  end;
end;
```

## 4. Preventing Ghost Binaries & Directory Hijacking (`UsePreviousAppDir`)

### Problem
Inno Setup enables `UsePreviousAppDir=yes` by default. When an installer or automated test script executes targeting a temporary folder (e.g. `%LOCALAPPDATA%\Temp\TestWAPInstall`), Inno Setup saves that custom path to the registry under the application `AppId`. Subsequent silent updates or standard installs read the registry key and continue deploying binaries to the temporary folder instead of `{localappdata}\Programs\WorkActivityPanel`.

This causes **Ghost Binaries**:
1. Old versions (e.g. v1.5.0) remain frozen in `{localappdata}\Programs\WorkActivityPanel`.
2. Windows Startup keys or shortcuts may launch the old orphaned executable on boot.
3. Windows "Installed Apps" list only displays a single entry pointing to the temporary test path, masking the existence of the older binary.

### Solution
In `installer.iss`:
* Set `DisableDirPage=yes` and `UsePreviousAppDir=no` in `[Setup]` to enforce deterministic, immutable installation to `{localappdata}\Programs\WorkActivityPanel`.
* Never allow temporary test runs to reuse the production `AppId` without explicitly overriding or cleaning up the registry state.

```ini
[Setup]
DisableProgramGroupPage=yes
DisableDirPage=yes
UsePreviousAppDir=no
```

## Key Takeaway
When developing unpackaged Windows desktop applications packaged with Inno Setup:
* Clearly decouple **Application Binaries** (`{localappdata}\Programs\<App>`) from **User Data** (`{localappdata}\<App>\Data`).
* Always register autostart keys with `Flags: uninsdeletevalue` to prevent broken startup entries after uninstallation.
* Enforce `UsePreviousAppDir=no` and `DisableDirPage=yes` for user-level per-user desktop apps to guarantee every update and installation targets the canonical application directory without directory drift or ghost binaries.
* Explicitly configure post-uninstall directory cleanup (`DelTree`) to prevent credential leakage and orphaned data files on clean removals while keeping updates seamless.
