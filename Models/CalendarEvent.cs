using System;

namespace WorkActivityPanel.Models;

/// <summary>
/// Represents a simple calendar event.
/// </summary>
public class CalendarEvent
{
    /// <summary>
    /// Gets or sets the unique identifier for the event.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the title of the event.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the start time of the event.
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// Gets or sets the end time of the event.
    /// </summary>
    public DateTime EndTime { get; set; }

    /// <summary>
    /// Gets or sets the optional meeting link URL.
    /// </summary>
    public string? MeetingLink { get; set; }

    /// <summary>
    /// Formatted start time string (e.g. 9:30 AM).
    /// </summary>
    public string FormattedStartTime => StartTime.ToString("h:mm tt");

    /// <summary>
    /// Formatted end time string (e.g. 10:30 AM).
    /// </summary>
    public string FormattedEndTime => EndTime.ToString("h:mm tt");
}
