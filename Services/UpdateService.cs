using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WorkActivityPanel.Models;
using WorkActivityPanel.Services.Interfaces;

namespace WorkActivityPanel.Services;

/// <summary>
/// Service implementation for checking and downloading updates from GitHub Releases.
/// </summary>
public class UpdateService : IUpdateService, IDisposable
{
    private const string GitHubRepoOwner = "AnaCataVC";
    private const string GitHubRepoName = "work-activity-panel";
    private static readonly string LatestReleaseApiUrl = $"https://api.github.com/repos/{GitHubRepoOwner}/{GitHubRepoName}/releases/latest";

    private readonly HttpClient _httpClient;
    private readonly ILogger<UpdateService>? _logger;
    private readonly string _currentVersion;

    /// <inheritdoc />
    public string CurrentAppVersion => _currentVersion;

    public UpdateService(ILogger<UpdateService>? logger = null, HttpClient? httpClient = null)
    {
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _currentVersion = ResolveCurrentVersion();

        ConfigureHttpClient();
    }

    private void ConfigureHttpClient()
    {
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("WorkActivityPanel", _currentVersion));
        }
        if (!_httpClient.DefaultRequestHeaders.Contains("Accept"))
        {
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
        }
    }

    private static string ResolveCurrentVersion()
    {
        try
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            if (version != null)
            {
                return $"{version.Major}.{version.Minor}.{version.Build}";
            }
        }
        catch
        {
            // Fallback
        }
        return "1.3.0";
    }

    /// <inheritdoc />
    public async Task<UpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        var info = new UpdateInfo
        {
            CurrentVersion = _currentVersion
        };

        try
        {
            _logger?.LogInformation("Checking for application updates from {Url}...", LatestReleaseApiUrl);

            using var response = await _httpClient.GetAsync(LatestReleaseApiUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                info.ErrorMessage = $"GitHub API respondió con código: {(int)response.StatusCode} ({response.ReasonPhrase})";
                _logger?.LogWarning("GitHub Releases API returned status {StatusCode}", response.StatusCode);
                return info;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string tagName = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? string.Empty : string.Empty;
            string releaseTitle = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? tagName : tagName;
            string releaseBody = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? string.Empty : string.Empty;
            string htmlUrl = root.TryGetProperty("html_url", out var urlProp) ? urlProp.GetString() ?? string.Empty : string.Empty;

            info.LatestVersion = NormalizeVersionString(tagName);
            info.ReleaseTitle = releaseTitle;
            info.ReleaseNotes = releaseBody;
            info.ReleaseHtmlUrl = htmlUrl;

            // Search for installer asset (*.exe)
            if (root.TryGetProperty("assets", out var assetsProp) && assetsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assetsProp.EnumerateArray())
                {
                    string assetName = asset.TryGetProperty("name", out var aName) ? aName.GetString() ?? string.Empty : string.Empty;
                    string downloadUrl = asset.TryGetProperty("browser_download_url", out var aUrl) ? aUrl.GetString() ?? string.Empty : string.Empty;
                    long size = asset.TryGetProperty("size", out var aSize) ? aSize.GetInt64() : 0;

                    if (assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        info.DownloadUrl = downloadUrl;
                        info.InstallerFileName = assetName;
                        info.InstallerSizeBytes = size;
                        break;
                    }
                }
            }

            info.IsUpdateAvailable = IsNewerVersion(_currentVersion, info.LatestVersion);
            _logger?.LogInformation("Update check result: Current={Current}, Latest={Latest}, Available={Available}",
                _currentVersion, info.LatestVersion, info.IsUpdateAvailable);

            return info;
        }
        catch (Exception ex)
        {
            info.ErrorMessage = $"Error al buscar actualizaciones: {ex.Message}";
            _logger?.LogError(ex, "Failed to check for updates from GitHub Releases.");
            return info;
        }
    }

    /// <inheritdoc />
    public async Task<string> DownloadUpdateAsync(
        string downloadUrl,
        string? fileName = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            throw new ArgumentException("Download URL cannot be empty.", nameof(downloadUrl));
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "WorkActivityPanel_Updates");
        Directory.CreateDirectory(tempDir);

        var safeFileName = string.IsNullOrWhiteSpace(fileName) ? "WorkActivityPanel-Setup-Update.exe" : fileName;
        var destinationPath = Path.Combine(tempDir, safeFileName);

        _logger?.LogInformation("Downloading update installer from {Url} to {Destination}...", downloadUrl, destinationPath);

        using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        var canReportProgress = totalBytes > 0 && progress != null;

        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        var buffer = new byte[81920];
        long totalBytesRead = 0;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            totalBytesRead += bytesRead;

            if (canReportProgress)
            {
                var percentage = (double)totalBytesRead / totalBytes * 100.0;
                progress?.Report(percentage);
            }
        }

        progress?.Report(100.0);
        _logger?.LogInformation("Update installer downloaded successfully ({Bytes} bytes).", totalBytesRead);

        return destinationPath;
    }

    /// <inheritdoc />
    public void LaunchInstaller(string installerPath)
    {
        if (!File.Exists(installerPath))
        {
            throw new FileNotFoundException("Installer executable not found.", installerPath);
        }

        _logger?.LogInformation("Launching update installer {Path}...", installerPath);

        Process.Start(new ProcessStartInfo
        {
            FileName = installerPath,
            UseShellExecute = true
        });
    }

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

        // Fallback component-by-component comparison
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

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
