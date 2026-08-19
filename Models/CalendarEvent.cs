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
    /// Gets or sets whether this event qualifies for Granola auto-launching and pre-meeting alerts.
    /// </summary>
    public bool OpensGranola { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the event is an all-day event (e.g. out of office, holidays, full day focus).
    /// </summary>
    public bool IsAllDay { get; set; }

    /// <summary>
    /// Formatted start time string (e.g. 9:30 AM).
    /// </summary>
    public string FormattedStartTime => IsAllDay ? "Todo el día" : StartTime.ToString("h:mm tt");

    /// <summary>
    /// Formatted end time string (e.g. 10:30 AM).
    /// </summary>
    public string FormattedEndTime => IsAllDay ? "Todo el día" : EndTime.ToString("h:mm tt");

    /// <summary>
    /// Gets whether the meeting has already concluded.
    /// </summary>
    public bool IsPast => DateTime.Now > EndTime;

    /// <summary>
    /// Gets whether the meeting is currently in progress.
    /// </summary>
    public bool IsInProgress => DateTime.Now >= StartTime && DateTime.Now <= EndTime;

    /// <summary>
    /// Gets whether the meeting is scheduled for the future.
    /// </summary>
    public bool IsUpcoming => DateTime.Now < StartTime;

    /// <summary>
    /// Gets the dynamic status description text based on the current time and Granola trigger eligibility.
    /// </summary>
    public string StatusText
    {
        get
        {
            var now = DateTime.Now;
            if (now >= StartTime && now <= EndTime)
            {
                return "En curso";
            }
            if (now > EndTime)
            {
                return "Finalizada";
            }
            return OpensGranola ? "Granola se abrirá 5 min antes" : "Solo en calendario";
        }
    }
}
