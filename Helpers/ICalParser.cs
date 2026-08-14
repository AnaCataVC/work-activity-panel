using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using WorkActivityPanel.Models;

namespace WorkActivityPanel.Helpers;

/// <summary>
/// Lightweight, fast RFC 5545 iCalendar (.ics) parser for extracting events and meeting links.
/// </summary>
public static partial class ICalParser
{
    [GeneratedRegex(@"https?:\/\/(?:[a-zA-Z0-9\-]+\.)?(?:meet\.google\.com|zoom\.us|teams\.microsoft\.com|teams\.live\.com|webex\.com)\/[^\s<""'>]+", RegexOptions.IgnoreCase)]
    private static partial Regex MeetingLinkRegex();

    /// <summary>
    /// Unfolds multi-line strings in iCalendar format where lines beginning with space or tab continue the previous line.
    /// </summary>
    public static List<string> UnfoldLines(string icsContent)
    {
        var result = new List<string>();
        using var reader = new StringReader(icsContent);
        string? currentLine = null;

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t'))
            {
                if (currentLine != null)
                {
                    currentLine += line[1..];
                }
            }
            else
            {
                if (currentLine != null)
                {
                    result.Add(currentLine);
                }
                currentLine = line;
            }
        }

        if (currentLine != null)
        {
            result.Add(currentLine);
        }

        return result;
    }

    /// <summary>
    /// Parses an iCalendar string and returns all valid events occurring on the specified date,
    /// ignoring cancelled events and deduplicating recurring or modified instances.
    /// </summary>
    public static List<CalendarEvent> ParseEventsForDate(string icsContent, DateTime targetDate)
    {
        var eventMap = new Dictionary<string, CalendarEvent>();
        var lines = UnfoldLines(icsContent);

        bool inEvent = false;
        string id = "";
        string summary = "";
        string location = "";
        string description = "";
        string status = "";
        DateTime? dtStart = null;
        DateTime? dtEnd = null;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.Equals("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                inEvent = true;
                id = "";
                summary = "Reunión";
                location = "";
                description = "";
                status = "";
                dtStart = null;
                dtEnd = null;
                continue;
            }

            if (trimmed.Equals("END:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                if (inEvent && dtStart.HasValue)
                {
                    // Ignore cancelled events
                    if (status.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase))
                    {
                        inEvent = false;
                        continue;
                    }

                    var start = dtStart.Value;
                    var end = dtEnd ?? start.AddHours(1);

                    // Check if event is on targetDate
                    if (start.Date == targetDate.Date)
                    {
                        var meetingLink = ExtractMeetingLink(location, description);
                        
                        // Deduplication key: Prefer UID, fallback to Title+StartTime
                        string key = !string.IsNullOrWhiteSpace(id) 
                            ? id 
                            : $"{summary}_{start:yyyyMMddHHmm}";

                        eventMap[key] = new CalendarEvent
                        {
                            Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString() : id,
                            Title = summary,
                            StartTime = start,
                            EndTime = end,
                            MeetingLink = meetingLink
                        };
                    }
                }
                inEvent = false;
                continue;
            }

            if (!inEvent) continue;

            int colonIdx = line.IndexOf(':');
            if (colonIdx <= 0) continue;

            string keyPart = line[..colonIdx].Trim();
            string valPart = line[(colonIdx + 1)..].Trim();

            if (keyPart.StartsWith("UID", StringComparison.OrdinalIgnoreCase))
            {
                id = valPart;
            }
            else if (keyPart.StartsWith("SUMMARY", StringComparison.OrdinalIgnoreCase))
            {
                summary = UnescapeText(valPart);
            }
            else if (keyPart.StartsWith("LOCATION", StringComparison.OrdinalIgnoreCase))
            {
                location = UnescapeText(valPart);
            }
            else if (keyPart.StartsWith("DESCRIPTION", StringComparison.OrdinalIgnoreCase))
            {
                description = UnescapeText(valPart);
            }
            else if (keyPart.StartsWith("STATUS", StringComparison.OrdinalIgnoreCase))
            {
                status = valPart;
            }
            else if (keyPart.StartsWith("DTSTART", StringComparison.OrdinalIgnoreCase))
            {
                dtStart = ParseDateTime(valPart);
            }
            else if (keyPart.StartsWith("DTEND", StringComparison.OrdinalIgnoreCase))
            {
                dtEnd = ParseDateTime(valPart);
            }
        }

        var events = eventMap.Values.ToList();
        events.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
        return events;
    }

    /// <summary>
    /// Parses an iCalendar date/time string (UTC or local).
    /// </summary>
    public static DateTime? ParseDateTime(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        // Formats: 20260814T143000Z, 20260814T143000, 20260814
        string[] formats =
        {
            "yyyyMMddTHHmmssZ",
            "yyyyMMddTHHmmss",
            "yyyyMMddTHHmmZ",
            "yyyyMMddTHHmm",
            "yyyyMMdd"
        };

        if (DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture,
            value.EndsWith('Z') ? DateTimeStyles.AssumeUniversal : DateTimeStyles.AssumeLocal, out var dt))
        {
            return dt.ToLocalTime();
        }

        return null;
    }

    /// <summary>
    /// Unescapes RFC 5545 escaped characters like \, \;, \n.
    /// </summary>
    public static string UnescapeText(string text)
    {
        return text
            .Replace("\\n", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("\\N", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("\\,", ",")
            .Replace("\\;", ";")
            .Replace("\\\\", "\\");
    }

    /// <summary>
    /// Extracts a Google Meet, Zoom, Teams, or Webex URL from location or description text.
    /// </summary>
    public static string? ExtractMeetingLink(string location, string description)
    {
        var combined = $"{location}\n{description}";
        var match = MeetingLinkRegex().Match(combined);
        return match.Success ? match.Value : null;
    }
}
