# Google Drive Sync Resilience & Error Reporting Architecture

## 1. Context & Problem Statement
In desktop productivity tools synchronizing files to cloud destinations (specifically Google Apps Script Web App endpoints), high file counts (1,000+ files) encounter rate limits (HTTP 429), gateway timeouts (HTTP 504 / 30s limits), transient network errors (HTTP 500/503), payload size boundaries in Base64 encoding, and local OS file locks (Windows `SharingViolationException` / `IOException`).

Without fine-grained error tracking and adaptive resilience:
1. Failed files are excluded from the hash index (`sync_hashes.json`), forcing repeated evaluation on subsequent sync passes.
2. The user receives only high-level status ("Completado con 42 errores"), with zero visibility into which files failed, why they failed, or how to retry them.
3. Rapid sequential POST loops trigger Google Cloud rate-limiting cascades.

---

## 2. Technical Investigation & Best Practices

### 2.1 Exponential Backoff with Jitter for Rate-Limited Endpoints
When communicating with Google Apps Script Web Apps:
- Standard exponential backoff: $Delay = BaseDelay \times 2^{retryCount} + Jitter$.
- Base delay: 500ms - 1000ms.
- Max retries: 3 attempts.
- Rate limit headers / HTTP status codes triggering backoff: `429 Too Many Requests`, `500 Internal Server Error`, `503 Service Unavailable`, `HttpRequestException` (timeout).
- Normal inter-request throttle: 100ms - 200ms sleep between successful uploads prevents burst rate triggers.

### 2.2 Granular Error Categorization Model
Errors during desktop file sync divide into 4 concrete categories:
1. **`FileLocked`**: Local Windows file in use by Word, Excel, IDE, or background process (`IOException` sharing violation / unauthorized).
2. **`Oversized`**: File exceeds memory / Base64 upload limits for web app endpoints.
3. **`RateLimitOrTimeout`**: Google Apps Script quota reached or 30-second execution window expired (`429`, `503`, `TaskCanceledException`).
4. **`NetworkOrServer`**: Connectivity drops, DNS errors, or invalid server responses.

### 2.3 Incremental Scanning Fast-Path
Calculating SHA-256 over 1,000+ files on every sync cycle creates unnecessary disk I/O.
- Store `{ Hash, LastWriteTimeUtc, FileSize }` in `sync_hashes.json`.
- If `FileInfo.LastWriteTimeUtc == cached.LastWriteTimeUtc` AND `FileInfo.Length == cached.FileSize`, skip SHA-256 computation entirely during the collection phase.

### 2.4 User-Facing Error Diagnostics UI in WinUI 3
- Native `ContentDialog` or expandable flyout detailing failed files:
  - File Name and relative destination.
  - Reason / Category badge.
  - Action buttons: "Reintentar solo archivos fallidos" (`RetryFailedFilesAsync`), "Copiar reporte", and "Cerrar".
- Interactive link button in Dashboard card when `Errors > 0`: `Ver X archivos no sincronizados`.
