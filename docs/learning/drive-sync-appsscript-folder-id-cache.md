# Drive Sync: Apps Script Folder-ID Cache

> **Date:** 2026-09-06
> **Module:** `docs/google-setup-guide.md` (`Code.gs` template, deployed by each user in their own Apps Script project)
> **Status:** Implemented & documented

## Context

The Windows client's own scan/hash fast-path (see [Drive Sync Fast-Path Hash Cache Optimization](drive-sync-fast-path-hash-cache.md)) was already healthy: a live `sync_hashes.json` inspected on this machine had 1,306 entries, zero legacy (unmigrated) entries, and an empty `sync_errors.json`. Unchanged files were being skipped in well under a second. Yet syncs stayed slow even with most of the tree already mirrored in Drive — the bottleneck wasn't the local scan, it was the server side.

## Root Cause

Every uploaded file triggers its own `doPost` execution in the Apps Script bridge. Each execution independently re-walked the destination path from the root folder using `getFoldersByName` (an `O(children)` Drive *search* query) once per path segment — with no memory of folders it had already resolved in a previous call. A file three levels deep in an established tree paid for three Drive searches on every single upload, forever, even though the same three folders had been resolved thousands of times before.

Because `LockService.getScriptLock()` is global to the script (documented in [../external-references/drive-sync-adversarial-stress-test.md](../external-references/drive-sync-adversarial-stress-test.md)), uploads are strictly serial by design — the client can't parallelize its way around this. The only lever left was cutting the per-call cost itself.

## Fix

`Code.gs` now caches resolved folder IDs in `PropertiesService.getScriptProperties()`, keyed by the cumulative path segment (e.g. `folderId:Investigaciones/2026/09`). On each upload:
- A cache hit resolves the folder with a direct `DriveApp.getFolderById(id)` call — an O(1) ID lookup, not a name search.
- A cache miss falls back to the original `getFoldersByName`/`createFolder` logic and populates the cache for next time.
- If a cached ID no longer resolves (the folder was permanently deleted or moved out of the account's reach), the `catch` block treats it as a miss and re-resolves normally — the cache self-heals, no manual invalidation needed.
- `DriveApp.getFolderById` does **not** throw for a folder sitting in Drive's Trash — trashing only sets a flag, so the `catch` above never fires for it. The cache explicitly checks `resolvedFolder.isTrashed()` and treats a trashed folder as a cache miss too; without that check, uploads would keep landing silently inside a trashed folder until Drive's ~30-day auto-purge deleted them for good.

This requires no change to the Windows client (`DriveSyncService.cs` is untouched) and no re-upload of existing files — only redeploying the updated `Code.gs` as a new Apps Script version.

## Why not touch the C# client instead?

The obvious lever — raise `_uploadSemaphore` above 1 concurrent upload — was already tried and explicitly reverted per [../external-references/drive-sync-adversarial-stress-test.md](../external-references/drive-sync-adversarial-stress-test.md): concurrent requests queue behind the same global script lock and start timing out past ~25s, cascading into 500s. That constraint stands; this fix works within it instead of around it.

## Scale note

`PropertiesService` script properties cap at ~500 KB total. Each cached entry here is a short key (`folderId:` + path) plus a ~33-character Drive folder ID — a few thousand distinct folders fit comfortably. Not worth adding eviction for a single user's folder tree; revisit only if a source folder ever grows a genuinely enormous number of distinct subfolders.
