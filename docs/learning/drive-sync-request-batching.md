# Drive Sync Request Batching

> **Date:** 2026-09-10
> **Module:** `Services/DriveSyncService.cs`, `docs/google-setup-guide.md` (`Code.gs` template)
> **Status:** Implemented & tested

## Context

Two earlier optimizations already fixed the *scanning* side of Drive sync: the metadata-first
hash cache ([drive-sync-fast-path-hash-cache.md](drive-sync-fast-path-hash-cache.md)) skips
re-hashing unchanged files, and the Apps Script folder-ID cache
([drive-sync-appsscript-folder-id-cache.md](drive-sync-appsscript-folder-id-cache.md)) skips
re-walking the Drive folder tree on every upload. Neither touches the *upload* itself: syncing
stayed slow whenever many files had actually changed, because each one was still its own
sequential HTTP round trip to Apps Script.

## Root Cause

Every changed file got its own POST, serialized behind a `SemaphoreSlim(1,1)` plus a fixed
`Task.Delay(300)` after each one. Each Apps Script invocation itself costs ~1.5-3s (cold start +
`LockService.waitLock` + base64 decode + Drive write) regardless of file size. For N changed
files, total time was ~N × (2-3s + 0.3s) — no amount of hash caching helps here, since caching
only skips *unchanged* files.

## Fix: Batch Multiple Files Per Request

`DriveSyncService.BuildBatches` groups pending uploads into batches bounded by:
- `MaxBatchFileCount = 8` files, and
- `MaxBatchRawBytes = 9 MB` raw bytes (≈ 12 MB base64-encoded, comfortably under Google's
  undocumented ~25-30 MB HTTP body ceiling for Apps Script Web Apps — see
  [drive-sync-adversarial-stress-test.md](../external-references/drive-sync-adversarial-stress-test.md),
  vulnerability #3).

A file already larger than the byte cap on its own still gets a one-item batch instead of being
dropped (`BuildBatches` only flushes the current batch when it is non-empty).

Each batch is sent as one JSON POST (`{ authToken, files: [{ filename, relativePath, mimeType,
data }, ...] }`) instead of the previous `FormUrlEncodedContent` per-file body. `Code.gs` reads
`e.postData.contents` (JSON body) instead of `e.parameter` (form fields), loops over `files`
under a single `LockService` acquisition, and wraps each file in its own try/catch so one bad
file (locked, quota, odd name) fails only its own entry in the `results` array instead of
aborting the whole batch.

### Why This Doesn't Risk a Google-Side Rejection

This was checked explicitly before shipping, since switching from form-urlencoded to a JSON body
is a real protocol change:
- **JSON POST bodies are a standard, documented way to reach `doPost`** — `e.postData.contents`
  is the raw body regardless of `Content-Type`; there is no server-side rejection of JSON per se.
- **The only real ceiling is total HTTP body size** — Google's undocumented ~25-30 MB limit,
  already known from the adversarial stress test. The batch cap (~12 MB encoded) sits well under
  it, with more headroom than the *old* single-file path had: `MaxFileSizeMb` defaults to 20 MB
  raw (~26.6 MB base64), which was already uncomfortably close to that ceiling for a single large
  file. Batching does not make this worse; the per-file cap is unchanged.
- **No CORS concerns** — the request is server-to-server (`HttpClient`, not a browser), so CORS
  preflight rules never apply.

### One Wire Format, Not Two

`UploadSingleFileAsync` (used by `TestConnectionAsync` and available on the public interface) is
now a thin wrapper that calls the same batch endpoint with a one-item list, instead of maintaining
a separate single-file protocol. This is also why the change is a **breaking wire-protocol
change**: the deployed `Code.gs` and the app version must move together, same as the folder-ID
cache update before it — see the callout in `docs/google-setup-guide.md`.

## Trade-off

Progress reporting granularity changed from one tick per file to one tick per batch (up to 8
files). This is inherent to batching: there is no way to report per-file progress mid-flight of a
single atomic HTTP call. The status message shows "Subiendo lote de N archivos..." instead of one
file name at a time when a batch has more than one file.
