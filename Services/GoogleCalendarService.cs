using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WorkActivityPanel.Helpers;
using WorkActivityPanel.Models;
using WorkActivityPanel.Services.Interfaces;

namespace WorkActivityPanel.Services;

/// <summary>
/// Service that synchronizes with Google Calendar via private iCal (.ics) feed URL
/// with optional password/token authentication.
/// </summary>
public class GoogleCalendarService : IGoogleCalendarService, IDisposable
{
    private const string ICalUrlSettingKey = "CalendarICalUrl";
    private const string ICalKeySettingKey = "CalendarICalKey";
    private const string CalendarExcludedKeywordsKey = "CalendarExcludedKeywords";
    private const string CalendarIgnoreAllDayEventsKey = "CalendarIgnoreAllDayEvents";
    private const string CalendarRequireMeetingLinkKey = "CalendarRequireMeetingLink";

    private readonly IAppLauncherService _appLauncherService;
    private readonly ILogger<GoogleCalendarService> _logger;
    private readonly ConcurrentBag<Timer> _activeTimers = new();

    private string? _iCalUrl;
    private string? _iCalKey;
    private CalendarFilterSettings _filterSettings = new();

    /// <inheritdoc />
    public string? ICalUrl => _iCalUrl;

    /// <inheritdoc />
    public string? ICalKey => _iCalKey;

    /// <inheritdoc />
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_iCalUrl);

    /// <inheritdoc />
    public CalendarFilterSettings FilterSettings => _filterSettings;

    /// <inheritdoc />
    public event EventHandler<CalendarEvent>? UpcomingMeetingDetected;

    public GoogleCalendarService(
        IAppLauncherService appLauncherService,
        ILogger<GoogleCalendarService> logger)
    {
        _appLauncherService = appLauncherService;
        _logger = logger;

        LoadSavedSettings();
    }

    private void LoadSavedSettings()
    {
        try
        {
            _iCalUrl = LocalSettingsHelper.Get(ICalUrlSettingKey);
            _iCalKey = LocalSettingsHelper.Get(ICalKeySettingKey);

            var excludedKeywords = LocalSettingsHelper.Get(CalendarExcludedKeywordsKey);
            if (excludedKeywords != null)
            {
                _filterSettings.ExcludedKeywords = excludedKeywords;
            }

            var ignoreAllDayStr = LocalSettingsHelper.Get(CalendarIgnoreAllDayEventsKey);
            if (bool.TryParse(ignoreAllDayStr, out var ignoreAllDay))
            {
                _filterSettings.IgnoreAllDayEvents = ignoreAllDay;
            }

            var requireLinkStr = LocalSettingsHelper.Get(CalendarRequireMeetingLinkKey);
            if (bool.TryParse(requireLinkStr, out var requireLink))
            {
                _filterSettings.RequireMeetingLink = requireLink;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load saved calendar settings.");
        }
    }

    /// <inheritdoc />
    public void UpdateFilterSettings(CalendarFilterSettings settings)
    {
        _filterSettings = settings ?? new CalendarFilterSettings();
        LocalSettingsHelper.Set(CalendarExcludedKeywordsKey, _filterSettings.ExcludedKeywords);
        LocalSettingsHelper.Set(CalendarIgnoreAllDayEventsKey, _filterSettings.IgnoreAllDayEvents.ToString());
        LocalSettingsHelper.Set(CalendarRequireMeetingLinkKey, _filterSettings.RequireMeetingLink.ToString());
        _logger.LogInformation("Calendar filter settings updated.");
    }

    private HttpClient CreateConfiguredHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        if (!string.IsNullOrWhiteSpace(_iCalKey))
        {
            // If a key or app password is provided, attach Basic Authorization
            var authBytes = Encoding.UTF8.GetBytes($"calendar:{_iCalKey}");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
        }

        return client;
    }

    /// <inheritdoc />
    public async Task<bool> SetICalCredentialsAsync(string url, string? key = null)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        url = url.Trim();
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url;
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            if (!string.IsNullOrWhiteSpace(key))
            {
                var authBytes = Encoding.UTF8.GetBytes($"calendar:{key.Trim()}");
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
            }

            var response = await client.GetStringAsync(url);
            if (!response.Contains("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("The provided URL did not return a valid iCalendar feed.");
                return false;
            }

            _iCalUrl = url;
            _iCalKey = string.IsNullOrWhiteSpace(key) ? null : key.Trim();

            LocalSettingsHelper.Set(ICalUrlSettingKey, _iCalUrl);
            if (_iCalKey != null)
            {
                LocalSettingsHelper.Set(ICalKeySettingKey, _iCalKey);
            }
            else
            {
                LocalSettingsHelper.Remove(ICalKeySettingKey);
            }

            _logger.LogInformation("iCal feed URL and credentials verified.");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download iCal feed from URL.");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task ClearICalCredentialsAsync()
    {
        _iCalUrl = null;
        _iCalKey = null;
        LocalSettingsHelper.Remove(ICalUrlSettingKey);
        LocalSettingsHelper.Remove(ICalKeySettingKey);
        ClearAlerts();
        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<List<CalendarEvent>> GetTodayEventsAsync()
    {
        var result = new List<CalendarEvent>();

        if (string.IsNullOrWhiteSpace(_iCalUrl))
        {
            _logger.LogInformation("No iCal URL configured for calendar synchronization.");
            return result;
        }

        try
        {
            using var client = CreateConfiguredHttpClient();
            var icsContent = await client.GetStringAsync(_iCalUrl);
            result = ICalParser.ParseEventsForDate(icsContent, DateTime.Today);

            foreach (var ev in result)
            {
                ev.OpensGranola = _filterSettings.ShouldOpenGranola(ev);
            }

            _logger.LogInformation("Parsed {Count} calendar events for today ({GranolaCount} eligible for Granola).", 
                result.Count, result.FindAll(e => e.OpensGranola).Count);

            ScheduleMeetingAlerts(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve and parse iCal events.");
        }

        return result;
    }

    /// <inheritdoc />
    public void ScheduleMeetingAlerts(IEnumerable<CalendarEvent> events)
    {
        ClearAlerts();

        var now = DateTime.Now;

        foreach (var meeting in events)
        {
            // Only schedule alerts if meeting qualifies for Granola auto-open
            if (!meeting.OpensGranola)
            {
                _logger.LogInformation("Skipping Granola auto-launch alert for excluded event: '{Title}'.", meeting.Title);
                continue;
            }

            // Trigger 5 minutes before the meeting start
            var alertTime = meeting.StartTime.AddMinutes(-5);
            var delay = alertTime - now;

            if (delay > TimeSpan.Zero)
            {
                _logger.LogInformation("Scheduling meeting alert for '{Title}' at {AlertTime} (in {DelayMinutes:F1} min).", 
                    meeting.Title, alertTime, delay.TotalMinutes);

                var timer = new Timer(_ =>
                {
                    _logger.LogInformation("Upcoming meeting alert fired for '{Title}'. Ensuring Granola is open...", meeting.Title);
                    
                    // Fire event
                    UpcomingMeetingDetected?.Invoke(this, meeting);

                    // Ensure Granola is running
                    _appLauncherService.EnsureGranolaRunning();

                }, null, delay, Timeout.InfiniteTimeSpan);

                _activeTimers.Add(timer);
            }
            else if (now >= meeting.StartTime.AddMinutes(-5) && now < meeting.StartTime)
            {
                // If meeting is already within the 5-minute window right now
                _logger.LogInformation("Meeting '{Title}' is in less than 5 minutes. Ensuring Granola immediately.", meeting.Title);
                UpcomingMeetingDetected?.Invoke(this, meeting);
                _appLauncherService.EnsureGranolaRunning();
            }
        }
    }

    /// <inheritdoc />
    public void ClearAlerts()
    {
        while (_activeTimers.TryTake(out var timer))
        {
            timer.Dispose();
        }
    }

    public void Dispose()
    {
        ClearAlerts();
    }
}
