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

## Key Takeaway
When developing unpackaged Windows desktop applications packaged with Inno Setup:
* Clearly decouple **Application Binaries** (`{localappdata}\Programs\<App>`) from **User Data** (`{localappdata}\<App>\Data`).
* Always register autostart keys with `Flags: uninsdeletevalue` to prevent broken startup entries after uninstallation.
* Explicitly configure post-uninstall directory cleanup (`DelTree`) to prevent credential leakage and orphaned data files on clean removals while keeping updates seamless.
