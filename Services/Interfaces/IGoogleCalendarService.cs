using System.Collections.Generic;
using System.Threading.Tasks;
using WorkActivityPanel.Models;

namespace WorkActivityPanel.Services.Interfaces;

/// <summary>
/// Service responsible for fetching Google Calendar events via iCal feed and triggering meeting alerts.
/// </summary>
public interface IGoogleCalendarService
{
    /// <summary>
    /// The currently configured iCal (.ics) feed URL.
    /// </summary>
    string? ICalUrl { get; }

    /// <summary>
    /// Optional password or token for authenticated iCal feeds.
    /// </summary>
    string? ICalKey { get; }

    /// <summary>
    /// Indicates whether a valid iCal feed URL is configured.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Event triggered when a meeting is approaching (e.g. 5 minutes before).
    /// </summary>
    event EventHandler<CalendarEvent>? UpcomingMeetingDetected;

    /// <summary>
    /// Gets the current filter settings for Granola triggers.
    /// </summary>
    CalendarFilterSettings FilterSettings { get; }

    /// <summary>
    /// Updates the calendar filter settings and persists them.
    /// </summary>
    void UpdateFilterSettings(CalendarFilterSettings settings);

    /// <summary>
    /// Configures and saves the iCal feed URL and optional key.
    /// </summary>
    Task<bool> SetICalCredentialsAsync(string url, string? key = null);

    /// <summary>
    /// Clears the configured iCal URL and key.
    /// </summary>
    Task ClearICalCredentialsAsync();

    /// <summary>
    /// Fetches all calendar events scheduled for today from the iCal feed.
    /// </summary>
    Task<List<CalendarEvent>> GetTodayEventsAsync();

    /// <summary>
    /// Schedules automatic alarms 5 minutes prior to each meeting to ensure Granola is open.
    /// </summary>
    void ScheduleMeetingAlerts(IEnumerable<CalendarEvent> events);

    /// <summary>
    /// Clears any active meeting timers.
    /// </summary>
    void ClearAlerts();
}
