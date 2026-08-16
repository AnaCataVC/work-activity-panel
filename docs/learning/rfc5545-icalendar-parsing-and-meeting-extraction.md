# Learning: Lightweight RFC 5545 iCalendar Parsing & Meeting Link Extraction

## Context
**Work Activity Panel** monitors private Google Calendar iCal feeds (`.ics`) to provide an instant overview of daily meetings, automate companion tool launching (Granola 5 minutes before meetings), and provide 1-click meeting join links.

## Problem & Challenge
Bringing in full third-party iCalendar libraries often adds dozens of dependencies, thousands of lines of reflection code, and heavy memory overhead. We needed a lightweight, robust, and zero-dependency parser tailored for daily meeting agendas that could:
1. Correctly handle RFC 5545 line unfolding (where long lines continue on lines starting with a space or tab).
2. Normalize date formats across UTC (`YYYYMMDDTHHMMSSZ`), local time (`TZID=...:YYYYMMDDTHHMMSS`), and all-day events (`VALUE=DATE:YYYYMMDD`).
3. Accurately extract video conference URLs (Google Meet, Zoom, Microsoft Teams, Webex) from summaries, descriptions, and locations.
4. Filter out cancelled events (`STATUS:CANCELLED`) and deduplicate updated recurring occurrences (`RECURRENCE-ID` / `UID`).

## Solution Architecture: `ICalParser` Engine

```
┌────────────────────────────────────────────────────────┐
│                   Raw .ics Feed Data                   │
└───────────────────────────┬────────────────────────────┘
                            │ 1. Line Unfolding (CRLF + whitespace)
                            ▼
┌────────────────────────────────────────────────────────┐
│                 Unfolded Line Stream                   │
└───────────────────────────┬────────────────────────────┘
                            │ 2. VEVENT Block Splitting
                            ▼
┌────────────────────────────────────────────────────────┐
│                   VEVENT Properties                    │
│  - STATUS: CANCELLED filtering                         │
│  - DTSTART / DTEND parsing & UTC/Local normalization   │
│  - Multi-source Meeting Link Regex matching            │
│  - UID / Sequence deduplication                        │
└───────────────────────────┬────────────────────────────┘
                            │ 3. Target Date Filtering
                            ▼
┌────────────────────────────────────────────────────────┐
│           List<CalendarEvent> (Sorted by Start)        │
└────────────────────────────────────────────────────────┘
```

### 1. Robust RFC 5545 Line Unfolding
In RFC 5545, any line starting with a space (`0x20`) or horizontal tab (`0x09`) is a continuation of the previous line:
```csharp
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
```

### 2. Multi-Platform Video Conference Link Extraction
Using source-generated regular expressions (`[GeneratedRegex]`) to scan `LOCATION`, `DESCRIPTION`, and `SUMMARY` fields:
```csharp
[GeneratedRegex(@"https?:\/\/(?:[a-zA-Z0-9\-]+\.)?(?:meet\.google\.com|zoom\.us|teams\.microsoft\.com|teams\.live\.com|webex\.com)\/[^\s<""'>]+", RegexOptions.IgnoreCase)]
private static partial Regex MeetingLinkRegex();
```

### 3. Timezone Normalization
Parses both UTC markers (`Z` suffix) and local dates using invariant culture ISO formats:
```csharp
if (val.EndsWith("Z", StringComparison.OrdinalIgnoreCase))
{
    if (DateTime.TryParseExact(val, "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var utcDt))
    {
        return utcDt.ToLocalTime();
    }
}
```

## Key Takeaway
A specialized, lightweight RFC 5545 parser avoids heavy external dependencies while delivering sub-millisecond execution and deterministic meeting extraction for desktop productivity tools.
