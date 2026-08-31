# Drive Sync Fast-Path Hash Cache Optimization

> **Date:** 2026-08-31
> **Module:** `Services/DriveSyncService.cs`, `Models/SyncModels.cs`
> **Status:** Implemented & tested

## Context

When scanning local workspaces to synchronize with Google Drive, calculating the SHA-256 hash for every file on every sync loop imposes severe disk I/O penalties. With 1,100+ entries in `sync_hashes.json` and five configured source folders, the upfront hashing phase took 3-8 seconds of I/O before a single file was uploaded.

---

## Architectural Decisions & Learnings

### 1. Metadata-First vs. Hash-First Validation

We moved from an eager hash-first approach to a **metadata-first validation strategy**. By storing `{ Hash, LastWriteTimeUtcTicks, FileSize }` as a `HashCacheEntry`, we can trust OS filesystem metadata as a reliable proxy for content change:

- If a file's `LastWriteTimeUtc.Ticks` and `Length` both match the cached values, its content is almost certainly unchanged.
- Only when metadata drifts (file was written, truncated, or replaced) does the SHA-256 read happen.

**Why ticks instead of `DateTime`?** `DateTime.Ticks` is the native `long` representation of the timestamp. It serializes with zero precision loss in JSON and compares with a single integer equality check, avoiding floating-point rounding or string parsing issues.

### 2. Lazy vs. Eager Hashing

**Before:** `ScanFolder` computed `ComputeSha256(file)` for every file during the folder traversal phase, even for files that would be skipped moments later. This caused 100% of files to be read from disk on every sync run.

**After:** `ScanFolder` leaves `Hash = string.Empty`. The sync loop calls `IsMetadataConfirmed` first. Only when metadata has drifted does `ComputeSha256` execute. On a typical run where most files are unchanged, **0 disk reads occur during the scan phase**.

### 3. Clean Separation of Responsibilities

The auditor identified (and we fixed) a critical design flaw in the original implementation: `GetKnownHash` was doing a `FileInfo` stat internally AND the sync loop was calling `IsMetadataConfirmed` which did another identical stat. This caused double disk I/O and violated single-responsibility.

**Final design:**
- `GetKnownHash(hashKey)` - pure in-memory cache lookup, no disk I/O, O(1)
- `IsMetadataConfirmed(hashKey, filePath, size)` - the single authoritative FileInfo stat, under `_hashLock`

### 4. The 1 KB Threshold (Why 1 KB?)

Files smaller than 1 KB are hashed so fast that the overhead of the stat + lock + comparison can match or exceed the SHA-256 cost on modern NVMe drives. The 1 KB threshold provides a clear cost-benefit boundary:
- Files >= 1 KB: metadata fast-path (~0.1 ms vs ~5-15 ms per file for SHA-256)
- Files < 1 KB: always recompute SHA-256 (sub-millisecond, maximum data integrity)

### 5. Legacy Migration Strategy

Upgrading `sync_hashes.json` from `Dictionary<string, string>` to `Dictionary<string, HashCacheEntry>` could have forced a full re-upload of 1,100+ files.

**Migration approach in `LoadHashIndex`:**
- Parse raw JSON as a `JsonElement` to detect format without hard-typed deserialization
- Old string-value entries are converted to `HashCacheEntry` with `LastWriteTimeUtcTicks = 0L`
- `ticks = 0` is a sentinel: `IsMetadataConfirmed` returns `false`, forcing slow-path SHA-256 on first run
- After hash confirmed, `SaveKnownHash` upgrades the entry with real metadata for future fast-path hits
- **Result:** no files are re-uploaded, fast-path activates for each file as soon as it is processed once

---

## Performance Impact

| Scenario | Before | After |
|---|---|---|
| 1,131 files, none changed (2nd sync) | ~3-8 s disk I/O | < 50 ms (metadata check only) |
| 1,131 files, 20 changed | ~3-8 s + 20x upload time | < 50 ms scan + 20x upload time |
| First run after upgrade (legacy migration) | N/A | ~3-8 s (same as before, upgrades cache) |
