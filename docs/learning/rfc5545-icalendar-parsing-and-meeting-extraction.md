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

iCal date-time properties come in three forms (RFC 5545 §3.3.5):

| Form | Example | Semantics |
|---|---|---|
| UTC | `20260910T160000Z` | Always UTC; `Z` suffix |
| TZID-qualified | `DTSTART;TZID=America/New_York:20260910T110000` | Local time in the named zone |
| Floating | `DTSTART:20260910T110000` | No timezone; treated as machine local |

#### Old approach (UTC-only) — bug
The original `ParseDateTime()` used `DateTimeStyles.AssumeLocal` for any value without a `Z` suffix. This silently ignored the `TZID` parameter, so an event at `11:00 America/New_York` (UTC-4 = 15:00 UTC) would display as `11:00` in the local machine timezone instead of the correctly converted `12:00 UTC-3`.

#### New pipeline — TZID-aware

```
keyPart: "DTSTART;TZID=America/New_York"
              │
              ▼ ExtractTzid(keyPart)
         "America/New_York"
              │
valPart: "20260910T110000"
              │
              ▼ ParseDateTimeWithTzid(valPart, tzid)
         1. EndsWith('Z')? → ParseDateTime (UTC path, no change)
         2. tzid == null?  → floating local (RFC 5545 §3.3.5)
         3. TimeZoneInfo.FindSystemTimeZoneById(tzid)
              └─ TimeZoneNotFoundException → TryResolveIanaTimeZone(tzid)
                   (IANA→Windows fallback table: ~50 common zones)
         4. TimeZoneInfo.ConvertTimeToUtc(parsedDt, tz).ToLocalTime()
              │
              ▼
         DateTime in local machine time ✅
```

Key properties:
- **UTC values always bypass TZID**: a `Z` suffix takes priority regardless of what `TZID` is present.
- **Graceful fallback**: unknown or unsupported TZID IDs (rare, exotic zones) fall back to floating-local — the same behaviour as before the fix — preventing crashes.
- **Windows compatibility**: .NET 6+ on Windows can resolve IANA IDs via ICU; `TryResolveIanaTimeZone` provides an explicit IANA→Windows dictionary as a reliable secondary fallback for the most common 50 zones.
- **All TZID-bearing properties handled**: `DTSTART`, `DTEND`, `RECURRENCE-ID`, and `EXDATE` all go through `ParseDateTimeWithTzid`.

### 4. Recurrence Rule Evaluation (`RRULE`, `EXDATE`, and `RECURRENCE-ID`)
Google Calendar exports recurring events by defining a master `VEVENT` with `DTSTART` (the series inception date) and an `RRULE` property (e.g., `FREQ=WEEKLY;WKST=SU;BYDAY=MO,TH`), rather than emitting daily duplicate entries. Without recurring rule evaluation, any recurring event whose series started on a previous date is ignored when filtering for today's date.

Instead of generating an unbounded stream of future dates (which consumes substantial memory and CPU), `ICalParser` evaluates occurrences targeting specifically the requested date (`targetDate`):
1. **Pass 1 - Exclusion & Override Indexing**:
   - Collect all `EXDATE` entries to exclude cancelled dates in a recurring series.
   - Collect all `RECURRENCE-ID` entries. If an instance was cancelled (`STATUS:CANCELLED`), it suppresses the master series for that date. If rescheduled or modified, the override takes precedence over the master rule.
2. **Pass 2 - Target Date Evaluation**:
   - **Single Events**: Validates whether `DTSTART` matches `targetDate` (or spans it for all-day events).
   - **Overrides (`RECURRENCE-ID`)**: Directly renders the overridden instance if active on `targetDate`.
   - **Recurring Masters (`RRULE`)**: Evaluates frequency patterns:
     - `WEEKLY`: Validates day of week against `BYDAY` (e.g. `MO,TH`) and calculates week index relative to `WKST` and `INTERVAL`.
     - `DAILY`: Validates `dayDiff % INTERVAL == 0` and optional weekday constraints.
     - `MONTHLY`: Validates either exact month day (`BYMONTHDAY`) or ordinal weekday (`BYDAY=1MO`, `2FR`, `-1FR`).
     - `YEARLY`: Validates matching month and day across `INTERVAL` years.
     - Enforces `UNTIL` and `COUNT` termination criteria.

```csharp
if (MatchesRecurrenceRule(ev.RRule, ev.DtStart.Value, targetDate))
{
    var start = ev.IsAllDay ? targetDate.Date : targetDate.Date + ev.DtStart.Value.TimeOfDay;
    var duration = (ev.DtEnd ?? ...) - ev.DtStart.Value;
    var end = start + duration;
    // Map event for today preserving meeting links and title
}
```

## Key Takeaway
A specialized, lightweight RFC 5545 parser with native targeted `RRULE` expansion avoids heavy external dependencies while delivering sub-millisecond execution and deterministic meeting extraction for desktop productivity tools.
