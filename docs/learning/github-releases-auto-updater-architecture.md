# Learning: Zero-Infrastructure Auto-Updater Architecture via GitHub Releases

## Context
Desktop applications frequently require update mechanisms to keep users on the latest version. For open-source or indie projects, managing dedicated update servers, cloud buckets, or complex CI/CD infrastructure can add excessive maintenance overhead.

## Problem & Challenge
We needed a lightweight, secure, and reliable auto-update mechanism for **Work Activity Panel** that could:
1. Detect new releases published to GitHub automatically without blocking UI startup.
2. Accurately compare semantic versions across varying tag formats (e.g., `v1.2.0` vs `1.1.0`).
3. Download the standalone installer (`.exe`) with real-time percentage progress reporting.
4. Launch the in-place installer and seamlessly update the application.

## Solution Architecture: `UpdateService` + GitHub API

```
┌──────────────────────────────┐
│  Work Activity Panel Client  │
└──────────────┬───────────────┘
               │ 1. GET /repos/{owner}/{repo}/releases/latest
               ▼
┌──────────────────────────────┐
│   GitHub REST API Endpoint   │
└──────────────┬───────────────┘
               │ 2. JSON Payload (tag_name, assets: [{ name, browser_download_url }])
               ▼
┌──────────────────────────────┐
│  Version Evaluation Engine   │
│  NormalizeVersionString()    │
│  IsNewerVersion()            │
└──────────────┬───────────────┘
               │ 3. If Update Available:
               ▼
┌──────────────────────────────┐
│  Streaming Chunk Downloader  │
│  - Stream to %TEMP%          │
│  - IProgress<double> update  │
│  - Launch Inno Setup (.exe)  │
└──────────────────────────────┘
```

### 1. Robust Semantic Version Normalization
GitHub release tags often contain prefixes like `v` or whitespace. The comparison logic handles both standard `System.Version` parsing and segment-by-segment comparisons:
```csharp
public static string NormalizeVersionString(string rawVersion)
{
    if (string.IsNullOrWhiteSpace(rawVersion)) return "0.0.0";
    var cleaned = rawVersion.Trim();
    if (cleaned.StartsWith("v", StringComparison.OrdinalIgnoreCase))
    {
        cleaned = cleaned[1..];
    }
    return cleaned;
}

public static bool IsNewerVersion(string currentVersion, string latestVersion)
{
    var curNormalized = NormalizeVersionString(currentVersion);
    var latNormalized = NormalizeVersionString(latestVersion);

    if (Version.TryParse(curNormalized, out var curVer) && Version.TryParse(latNormalized, out var latVer))
    {
        return latVer > curVer;
    }

    var curParts = curNormalized.Split('.');
    var latParts = latNormalized.Split('.');
    int maxParts = Math.Max(curParts.Length, latParts.Length);

    for (int i = 0; i < maxParts; i++)
    {
        int curNum = i < curParts.Length && int.TryParse(curParts[i], out var c) ? c : 0;
        int latNum = i < latParts.Length && int.TryParse(latParts[i], out var l) ? l : 0;

        if (latNum > curNum) return true;
        if (latNum < curNum) return false;
    }

    return false;
}
```

### 2. Streaming Chunk Download with Progress
Downloading large installers directly to `%TEMP%` using 80 KB buffers prevents memory spikes while keeping UI progress indicators accurate:
```csharp
using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
response.EnsureSuccessStatusCode();

var totalBytes = response.Content.Headers.ContentLength ?? -1L;
await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

var buffer = new byte[81920];
long totalBytesRead = 0;
int bytesRead;

while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
{
    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
    totalBytesRead += bytesRead;
    if (totalBytes > 0 && progress != null)
    {
        progress.Report((double)totalBytesRead / totalBytes * 100.0);
    }
}
```

## Key Takeaway
For desktop applications distributed via GitHub, the public GitHub Releases API provides a zero-maintenance, highly reliable auto-update distribution backend. Coupling streaming HTTP downloads with Inno Setup's in-place installation creates a frictionless 1-click update experience.
