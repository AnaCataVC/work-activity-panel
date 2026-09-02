# Learning: Calendar Event Mutation Detection & In-Place WinUI 3 Collection Reconciliation

## Context
**Work Activity Panel** displays today's meetings and work routines fetched via private Google Calendar iCalendar feeds (`.ics`). It dynamically reflects start/end times, active meeting states, and pre-meeting companion launches.

## Problem & Root Cause
When an existing calendar event was rescheduled in Google Calendar (e.g., moved from 11:30 AM to 12:30 PM) without changing the total count of daily meetings or their relative order, the update was never reflected in the dashboard UI upon refreshing.

### Root Cause Analysis
1. **Persistent UID in RFC 5545:** In Google Calendar and the iCalendar format, rescheduling a meeting preserves its original `UID` (`CalendarEvent.Id`).
2. **ID-Only Comparison Trap:** The view model's display updater (`UpdateTodayMeetingsDisplay`) previously checked for changes using only collection counts and item IDs:
   ```csharp
   // Flawed check: ignored property mutations
   bool changed = TodayMeetings.Count != activeAndUpcoming.Count;
   if (!changed)
   {
       for (int i = 0; i < activeAndUpcoming.Count; i++)
       {
           if (TodayMeetings[i].Id != activeAndUpcoming[i].Id)
           {
               changed = true;
               break;
           }
       }
   }
   ```
   Because `TodayMeetings[i].Id == activeAndUpcoming[i].Id` remained true across all slots, `changed` stayed `false`. The UI collection was never updated, retaining the old `CalendarEvent` instances in memory.
3. **HTTP Client Caching:** Without explicit cache invalidation headers, local HTTP stacks or intermediate web proxies could serve stale responses for the `.ics` feed.

---

## Architectural Solution & Patterns

```
┌────────────────────────────────────────────────────────┐
│             Google Calendar Feed (.ics)                │
└───────────────────────────┬────────────────────────────┘
                            │ 1. HTTP Fetch (Cache-Control: no-cache, no-store)
                            ▼
┌────────────────────────────────────────────────────────┐
│                  Parsed Calendar Events                │
│             (New instances with updated times)         │
└───────────────────────────┬────────────────────────────┘
                            │ 2. Filter Active & Upcoming (EndTime > DateTime.Now)
                            ▼
┌────────────────────────────────────────────────────────┐
│         In-Place Reconciliation Algorithm              │
│  - Count & ID Order Match:                             │
│      For each index i:                                 │
│        if (!TodayMeetings[i].Matches(active[i]))       │
│           TodayMeetings[i] = active[i]; // Replace     │
│  - Reordered or Count Mismatch:                        │
│      TodayMeetings.Clear() -> AddRange                 │
└───────────────────────────┬────────────────────────────┘
                            │ 3. WinUI 3 ObservableCollection
                            ▼
┌────────────────────────────────────────────────────────┐
│          Flicker-Free, Targeted UI Update              │
└────────────────────────────────────────────────────────┘
```

### 1. Value-Based Structural Matching (`Matches`)
We introduced an explicit equality method in `CalendarEvent` checking all display-critical properties rather than relying solely on `Id`:
```csharp
public bool Matches(CalendarEvent? other)
{
    if (other is null) return false;
    if (ReferenceEquals(this, other)) return true;

    return Id == other.Id &&
           StartTime == other.StartTime &&
           EndTime == other.EndTime &&
           Title == other.Title &&
           MeetingLink == other.MeetingLink &&
           OpensGranola == other.OpensGranola &&
           IsAllDay == other.IsAllDay;
}
```

### 2. In-Place Collection Replacement vs. Full Collection Reset
Rather than clearing and rebuilding the entire collection (`TodayMeetings.Clear(); foreach (var ev in ...) TodayMeetings.Add(ev);`), which triggers full visual DOM/control destruction and noticeable UI flicker in WinUI 3:
- When the count and ID sequence match, we perform **targeted index replacement**:
  ```csharp
  if (orderPreserved)
  {
      for (int i = 0; i < activeAndUpcoming.Count; i++)
      {
          if (!TodayMeetings[i].Matches(activeAndUpcoming[i]))
          {
              TodayMeetings[i] = activeAndUpcoming[i];
          }
      }
  }
  ```
- `ObservableCollection<T>[i] = value` triggers `NotifyCollectionChangedAction.Replace` specifically for the modified index, preserving scroll positions, focus, and visual smoothness.
- If items were reordered (e.g. an afternoon meeting moved ahead of a morning meeting) or added/removed, the collection falls back to a clean re-sync.

### 3. Reentrancy Debouncing & Anti-Cache Headers
- Configured HTTP client request headers to mandate fresh retrieval from Google servers:
  ```csharp
  client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue
  {
      NoCache = true,
      NoStore = true,
      MustRevalidate = true
  };
  client.DefaultRequestHeaders.Pragma.Add(new NameValueHeaderValue("no-cache"));
  ```
- Protected asynchronous refresh invocations with `_isRefreshingCalendar` to prevent overlapping concurrent operations triggered by rapid manual user clicks.

---

## Key Takeaways
1. **Never Assume Immutability from Persistent IDs:** An entity's identity (`Id`/`UID`) does not represent its state. When comparing collections for UI synchronization, compare state attributes (`StartTime`, `EndTime`, `Title`) or implement structured equivalence.
2. **Prefer In-Place Index Replacement over Full Clear in WinUI 3:** Replacing single elements in an `ObservableCollection` avoids visual flickering and costly XAML element re-inflation.
3. **HTTP Client Anti-Cache Discipline:** Always configure `Cache-Control: no-cache, no-store` on polling/refresh HTTP clients interacting with external calendar and status feeds.
