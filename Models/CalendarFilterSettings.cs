using System;
using System.Linq;

namespace WorkActivityPanel.Models;

/// <summary>
/// Settings and evaluation policy for determining whether calendar events should trigger Granola auto-launch.
/// </summary>
public class CalendarFilterSettings
{
    public const string DefaultExcludedKeywords = "[Personal], [Privado], Out of office, Fuera de la oficina, OOO, Vacaciones, Focus time";

    /// <summary>
    /// Gets or sets the comma-separated list of keywords or prefixes that disqualify an event from opening Granola.
    /// </summary>
    public string ExcludedKeywords { get; set; } = DefaultExcludedKeywords;

    /// <summary>
    /// Gets or sets whether all-day events (e.g. OOO, holidays, all-day focus blocks) should be ignored for Granola.
    /// </summary>
    public bool IgnoreAllDayEvents { get; set; } = true;

    /// <summary>
    /// Gets or sets whether Granola should only open if the calendar event contains a valid video conference URL (Meet, Zoom, Teams, Webex).
    /// </summary>
    public bool RequireMeetingLink { get; set; }

    /// <summary>
    /// Evaluates a calendar event against the current filter rules to determine if Granola should be automatically opened.
    /// </summary>
    /// <param name="calendarEvent">The calendar event to evaluate.</param>
    /// <returns>True if Granola should open 5 minutes before the meeting; otherwise false.</returns>
    public bool ShouldOpenGranola(CalendarEvent? calendarEvent)
    {
        if (calendarEvent == null) return false;

        // 1. Check all-day event rule
        if (IgnoreAllDayEvents && calendarEvent.IsAllDay)
        {
            return false;
        }

        // 2. Check video meeting link requirement
        if (RequireMeetingLink && string.IsNullOrWhiteSpace(calendarEvent.MeetingLink))
        {
            return false;
        }

        // 3. Check excluded keywords and prefixes
        if (!string.IsNullOrWhiteSpace(ExcludedKeywords) && !string.IsNullOrWhiteSpace(calendarEvent.Title))
        {
            var keywords = ExcludedKeywords
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(k => k.Trim())
                .Where(k => !string.IsNullOrEmpty(k));

            foreach (var keyword in keywords)
            {
                if (calendarEvent.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }

        return true;
    }
}
