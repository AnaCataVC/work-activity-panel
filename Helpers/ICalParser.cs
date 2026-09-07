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
    private sealed class RawVEvent
    {
        public string Id { get; set; } = "";
        public string Summary { get; set; } = "Reunión";
        public string Location { get; set; } = "";
        public string Description { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTime? DtStart { get; set; }
        public DateTime? DtEnd { get; set; }
        public bool IsAllDay { get; set; }
        public string? RRule { get; set; }
        public DateTime? RecurrenceId { get; set; }
        public List<DateTime> ExDates { get; } = new();
    }

    /// <summary>
    /// Parses an iCalendar string and returns all valid events occurring on the specified date,
    /// evaluating recurring series (RRULE), single events, exceptions (RECURRENCE-ID), and exclusions (EXDATE),
    /// while ignoring cancelled events and deduplicating instances.
    /// </summary>
    public static List<CalendarEvent> ParseEventsForDate(string icsContent, DateTime targetDate)
    {
        var lines = UnfoldLines(icsContent);
        var rawEvents = new List<RawVEvent>();

        RawVEvent? currentEvent = null;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.Equals("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                currentEvent = new RawVEvent();
                continue;
            }

            if (trimmed.Equals("END:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                if (currentEvent != null)
                {
                    rawEvents.Add(currentEvent);
                }
                currentEvent = null;
                continue;
            }

            if (currentEvent == null) continue;

            int colonIdx = line.IndexOf(':');
            if (colonIdx <= 0) continue;

            string keyPart = line[..colonIdx].Trim();
            string valPart = line[(colonIdx + 1)..].Trim();

            if (keyPart.StartsWith("UID", StringComparison.OrdinalIgnoreCase))
            {
                currentEvent.Id = valPart;
            }
            else if (keyPart.StartsWith("SUMMARY", StringComparison.OrdinalIgnoreCase))
            {
                currentEvent.Summary = UnescapeText(valPart);
            }
            else if (keyPart.StartsWith("LOCATION", StringComparison.OrdinalIgnoreCase))
            {
                currentEvent.Location = UnescapeText(valPart);
            }
            else if (keyPart.StartsWith("DESCRIPTION", StringComparison.OrdinalIgnoreCase))
            {
                currentEvent.Description = UnescapeText(valPart);
            }
            else if (keyPart.StartsWith("STATUS", StringComparison.OrdinalIgnoreCase))
            {
                currentEvent.Status = valPart;
            }
            else if (keyPart.StartsWith("RRULE", StringComparison.OrdinalIgnoreCase))
            {
                currentEvent.RRule = valPart;
            }
            else if (keyPart.StartsWith("RECURRENCE-ID", StringComparison.OrdinalIgnoreCase))
            {
                currentEvent.RecurrenceId = ParseDateTime(valPart);
            }
            else if (keyPart.StartsWith("EXDATE", StringComparison.OrdinalIgnoreCase))
            {
                var tokens = valPart.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var token in tokens)
                {
                    var exDt = ParseDateTime(token);
                    if (exDt.HasValue)
                    {
                        currentEvent.ExDates.Add(exDt.Value);
                    }
                }
            }
            else if (keyPart.StartsWith("DTSTART", StringComparison.OrdinalIgnoreCase))
            {
                if (keyPart.Contains("VALUE=DATE", StringComparison.OrdinalIgnoreCase) || valPart.Length == 8)
                {
                    currentEvent.IsAllDay = true;
                }
                currentEvent.DtStart = ParseDateTime(valPart);
            }
            else if (keyPart.StartsWith("DTEND", StringComparison.OrdinalIgnoreCase))
            {
                if (keyPart.Contains("VALUE=DATE", StringComparison.OrdinalIgnoreCase) || valPart.Length == 8)
                {
                    currentEvent.IsAllDay = true;
                }
                currentEvent.DtEnd = ParseDateTime(valPart);
            }
        }

        // Pass 1: Build set of overridden/cancelled occurrences
        var overriddenOccurrences = new HashSet<(string Uid, DateTime Date)>();
        foreach (var ev in rawEvents)
        {
            if (string.IsNullOrWhiteSpace(ev.Id)) continue;

            foreach (var ex in ev.ExDates)
            {
                overriddenOccurrences.Add((ev.Id, ex.Date));
            }

            if (ev.RecurrenceId.HasValue)
            {
                overriddenOccurrences.Add((ev.Id, ev.RecurrenceId.Value.Date));
            }
        }

        // Pass 2: Evaluate events for targetDate
        var eventMap = new Dictionary<string, CalendarEvent>();

        foreach (var ev in rawEvents)
        {
            if (!ev.DtStart.HasValue) continue;

            // 1. Instance override (RECURRENCE-ID)
            if (ev.RecurrenceId.HasValue)
            {
                if (ev.Status.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var start = ev.DtStart.Value;
                var end = ev.DtEnd ?? (ev.IsAllDay ? start.AddDays(1) : start.AddHours(1));

                if (!ev.IsAllDay && start.TimeOfDay == TimeSpan.Zero && (end - start).TotalHours >= 23)
                {
                    ev.IsAllDay = true;
                }

                if (start.Date == targetDate.Date || (ev.IsAllDay && start.Date <= targetDate.Date && end.Date > targetDate.Date))
                {
                    var meetingLink = ExtractMeetingLink(ev.Location, ev.Description);
                    string key = !string.IsNullOrWhiteSpace(ev.Id)
                        ? ev.Id
                        : $"{ev.Summary}_{start:yyyyMMddHHmm}";

                    eventMap[key] = new CalendarEvent
                    {
                        Id = string.IsNullOrWhiteSpace(ev.Id) ? Guid.NewGuid().ToString() : ev.Id,
                        Title = ev.Summary,
                        StartTime = start,
                        EndTime = end,
                        MeetingLink = meetingLink,
                        IsAllDay = ev.IsAllDay
                    };
                }
                continue;
            }

            // 2. Master recurring event (RRULE)
            if (!string.IsNullOrWhiteSpace(ev.RRule))
            {
                if (ev.Status.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(ev.Id) && overriddenOccurrences.Contains((ev.Id, targetDate.Date)))
                {
                    continue;
                }

                if (MatchesRecurrenceRule(ev.RRule, ev.DtStart.Value, targetDate))
                {
                    var start = ev.IsAllDay ? targetDate.Date : targetDate.Date + ev.DtStart.Value.TimeOfDay;
                    var duration = (ev.DtEnd ?? (ev.IsAllDay ? ev.DtStart.Value.AddDays(1) : ev.DtStart.Value.AddHours(1))) - ev.DtStart.Value;
                    var end = ev.IsAllDay ? targetDate.Date.AddDays(1) : start + duration;

                    bool isAllDay = ev.IsAllDay;
                    if (!isAllDay && start.TimeOfDay == TimeSpan.Zero && (end - start).TotalHours >= 23)
                    {
                        isAllDay = true;
                    }

                    var meetingLink = ExtractMeetingLink(ev.Location, ev.Description);
                    string key = !string.IsNullOrWhiteSpace(ev.Id)
                        ? ev.Id
                        : $"{ev.Summary}_{start:yyyyMMddHHmm}";

                    if (!eventMap.ContainsKey(key))
                    {
                        eventMap[key] = new CalendarEvent
                        {
                            Id = string.IsNullOrWhiteSpace(ev.Id) ? Guid.NewGuid().ToString() : ev.Id,
                            Title = ev.Summary,
                            StartTime = start,
                            EndTime = end,
                            MeetingLink = meetingLink,
                            IsAllDay = isAllDay
                        };
                    }
                }
                continue;
            }

            // 3. Normal single non-recurring event
            if (ev.Status.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var singleStart = ev.DtStart.Value;
            var singleEnd = ev.DtEnd ?? (ev.IsAllDay ? singleStart.AddDays(1) : singleStart.AddHours(1));

            if (!ev.IsAllDay && singleStart.TimeOfDay == TimeSpan.Zero && (singleEnd - singleStart).TotalHours >= 23)
            {
                ev.IsAllDay = true;
            }

            if (singleStart.Date == targetDate.Date || (ev.IsAllDay && singleStart.Date <= targetDate.Date && singleEnd.Date > targetDate.Date))
            {
                var meetingLink = ExtractMeetingLink(ev.Location, ev.Description);
                string key = !string.IsNullOrWhiteSpace(ev.Id)
                    ? ev.Id
                    : $"{ev.Summary}_{singleStart:yyyyMMddHHmm}";

                eventMap[key] = new CalendarEvent
                {
                    Id = string.IsNullOrWhiteSpace(ev.Id) ? Guid.NewGuid().ToString() : ev.Id,
                    Title = ev.Summary,
                    StartTime = singleStart,
                    EndTime = singleEnd,
                    MeetingLink = meetingLink,
                    IsAllDay = ev.IsAllDay
                };
            }
        }

        var events = eventMap.Values.ToList();
        events.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
        return events;
    }

    /// <summary>
    /// Evaluates whether an RFC 5545 RRULE generates an occurrence on targetDate.
    /// </summary>
    public static bool MatchesRecurrenceRule(string rrule, DateTime dtStart, DateTime targetDate)
    {
        if (string.IsNullOrWhiteSpace(rrule)) return false;

        // An event cannot occur before its series start date
        if (targetDate.Date < dtStart.Date) return false;

        var parts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ruleSegments = rrule.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var segment in ruleSegments)
        {
            int eqIdx = segment.IndexOf('=');
            if (eqIdx > 0)
            {
                parts[segment[..eqIdx].Trim()] = segment[(eqIdx + 1)..].Trim();
            }
        }

        if (!parts.TryGetValue("FREQ", out var freq))
        {
            return false;
        }

        // 1. UNTIL check
        if (parts.TryGetValue("UNTIL", out var untilStr))
        {
            var untilDt = ParseDateTime(untilStr);
            if (untilDt.HasValue && targetDate.Date > untilDt.Value.Date)
            {
                return false;
            }
        }

        // 2. INTERVAL check
        int interval = 1;
        if (parts.TryGetValue("INTERVAL", out var intervalStr) && int.TryParse(intervalStr, out var parsedInterval) && parsedInterval > 0)
        {
            interval = parsedInterval;
        }

        // 3. WKST check
        DayOfWeek wkst = DayOfWeek.Monday;
        if (parts.TryGetValue("WKST", out var wkstStr))
        {
            var parsedWkst = ParseDayOfWeek(wkstStr);
            if (parsedWkst.HasValue)
            {
                wkst = parsedWkst.Value;
            }
        }

        // 4. Frequency evaluation
        switch (freq.ToUpperInvariant())
        {
            case "DAILY":
            {
                int dayDiff = (targetDate.Date - dtStart.Date).Days;
                if (dayDiff % interval != 0) return false;

                if (parts.TryGetValue("BYDAY", out var byDayStr))
                {
                    var allowedDays = byDayStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(ParseDayOfWeek)
                        .Where(d => d.HasValue)
                        .Select(d => d!.Value)
                        .ToHashSet();

                    if (!allowedDays.Contains(targetDate.DayOfWeek)) return false;
                }

                if (parts.TryGetValue("COUNT", out var countStr) && int.TryParse(countStr, out var count) && count > 0)
                {
                    int occIndex = (dayDiff / interval) + 1;
                    if (occIndex > count) return false;
                }

                return true;
            }

            case "WEEKLY":
            {
                var allowedDays = new HashSet<DayOfWeek>();
                if (parts.TryGetValue("BYDAY", out var byDayStr))
                {
                    foreach (var token in byDayStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        var d = ParseDayOfWeek(token.Length >= 2 ? token[^2..] : token);
                        if (d.HasValue) allowedDays.Add(d.Value);
                    }
                }
                else
                {
                    allowedDays.Add(dtStart.DayOfWeek);
                }

                if (!allowedDays.Contains(targetDate.DayOfWeek)) return false;

                var wStart = GetWeekStart(dtStart.Date, wkst);
                var wTarget = GetWeekStart(targetDate.Date, wkst);
                int weekDiff = (int)Math.Round((wTarget - wStart).TotalDays / 7.0);

                if (weekDiff < 0 || weekDiff % interval != 0) return false;

                if (parts.TryGetValue("COUNT", out var countStr) && int.TryParse(countStr, out var count) && count > 0)
                {
                    int occurrences = 0;
                    for (DateTime cur = dtStart.Date; cur <= targetDate.Date; cur = cur.AddDays(1))
                    {
                        if (allowedDays.Contains(cur.DayOfWeek))
                        {
                            var wCur = GetWeekStart(cur, wkst);
                            int curWeekDiff = (int)Math.Round((wCur - wStart).TotalDays / 7.0);
                            if (curWeekDiff % interval == 0)
                            {
                                occurrences++;
                                if (occurrences > count) return false;
                            }
                        }
                    }
                }

                return true;
            }

            case "MONTHLY":
            {
                int monthDiff = (targetDate.Year - dtStart.Year) * 12 + (targetDate.Month - dtStart.Month);
                if (monthDiff < 0 || monthDiff % interval != 0) return false;

                if (parts.TryGetValue("BYDAY", out var byDayStr))
                {
                    bool dayMatched = false;
                    foreach (var token in byDayStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (token.Length < 2) continue;
                        string dayPart = token[^2..];
                        var targetDow = ParseDayOfWeek(dayPart);
                        if (!targetDow.HasValue || targetDate.DayOfWeek != targetDow.Value) continue;

                        string prefix = token[..^2];
                        if (string.IsNullOrEmpty(prefix))
                        {
                            dayMatched = true;
                            break;
                        }

                        if (int.TryParse(prefix, out int nth))
                        {
                            if (nth > 0)
                            {
                                int occIndex = (targetDate.Day - 1) / 7 + 1;
                                if (occIndex == nth)
                                {
                                    dayMatched = true;
                                    break;
                                }
                            }
                            else if (nth == -1)
                            {
                                if (targetDate.AddDays(7).Month != targetDate.Month)
                                {
                                    dayMatched = true;
                                    break;
                                }
                            }
                            else if (nth == -2)
                            {
                                if (targetDate.AddDays(7).Month == targetDate.Month && targetDate.AddDays(14).Month != targetDate.Month)
                                {
                                    dayMatched = true;
                                    break;
                                }
                            }
                        }
                    }

                    if (!dayMatched) return false;
                }
                else if (parts.TryGetValue("BYMONTHDAY", out var byMonthDayStr))
                {
                    var days = byMonthDayStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    bool dayFound = false;
                    int daysInMonth = DateTime.DaysInMonth(targetDate.Year, targetDate.Month);

                    foreach (var dStr in days)
                    {
                        if (int.TryParse(dStr, out int day))
                        {
                            if (day > 0 && targetDate.Day == day)
                            {
                                dayFound = true;
                                break;
                            }
                            if (day < 0 && targetDate.Day == (daysInMonth + 1 + day))
                            {
                                dayFound = true;
                                break;
                            }
                        }
                    }

                    if (!dayFound) return false;
                }
                else
                {
                    if (targetDate.Day != dtStart.Day) return false;
                }

                return true;
            }

            case "YEARLY":
            {
                int yearDiff = targetDate.Year - dtStart.Year;
                if (yearDiff < 0 || yearDiff % interval != 0) return false;

                return targetDate.Month == dtStart.Month && targetDate.Day == dtStart.Day;
            }

            default:
                return false;
        }
    }

    private static DayOfWeek? ParseDayOfWeek(string dayStr)
    {
        return dayStr.ToUpperInvariant() switch
        {
            "SU" => DayOfWeek.Sunday,
            "MO" => DayOfWeek.Monday,
            "TU" => DayOfWeek.Tuesday,
            "WE" => DayOfWeek.Wednesday,
            "TH" => DayOfWeek.Thursday,
            "FR" => DayOfWeek.Friday,
            "SA" => DayOfWeek.Saturday,
            _ => null
        };
    }

    private static DateTime GetWeekStart(DateTime date, DayOfWeek wkst)
    {
        int diff = (7 + (date.DayOfWeek - wkst)) % 7;
        return date.Date.AddDays(-diff);
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
