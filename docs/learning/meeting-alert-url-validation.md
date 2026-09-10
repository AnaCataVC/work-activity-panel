# Meeting Alert Popup — URL Validation & Adversarial Design

## Overview

The **Meeting Alert Popup** (`MeetingAlertWindow`) is a centered, always-on-top WinUI 3 window that fires a configurable number of minutes before a calendar event that has a video conference link. It is driven by `GoogleCalendarService.ScheduleMeetingAlerts`, which creates one-shot `System.Threading.Timer` instances per eligible meeting.

---

## Events

| Event | Raised when |
|-------|-------------|
| `MeetingStartingNow` | Meeting link is a **well-formed absolute URI** (`Uri.IsWellFormedUriString(link, UriKind.Absolute)`) |
| `MeetingAlertInvalidUrl` | Meeting has a non-empty link that **fails URI validation** |
| *(neither)* | Meeting has no link (`null` or empty string), or the meeting is fully in the past |

Both events carry the originating `CalendarEvent` as the event argument, and both are declared in `IGoogleCalendarService` and implemented in `GoogleCalendarService`.

---

## URL Validation — Design Rationale

### Why validate before raising the popup event?

If `MeetingAlertWindow.OnJoinClick` receives a malformed link and passes it directly to `IAppLauncherService.OpenUrl`, the shell `ShellExecuteEx` call silently fails or opens a broken browser tab. This was identified during adversarial review as a silent failure mode.

The validation gate in the timer callback (`Uri.IsWellFormedUriString`) ensures:
1. No popup appears for meetings with broken URLs (avoids user confusion).
2. The error is surfaced as a separate `MeetingAlertInvalidUrl` event that consumers can observe independently.
3. `MeetingAlertWindow` is only ever opened when the link is safe to pass to the OS shell.

### Why not validate inside `MeetingAlertWindow`?

Validating inside the UI layer would be too late — the window would already be visible. The service layer is the canonical source of truth for event routing.

---

## Two-Path Firing Model

`ScheduleMeetingAlerts` creates alerts via two branches:

```
popupAlertTime = meeting.StartTime − offsetMinutes
popupDelay     = popupAlertTime − now

if (popupDelay > TimeSpan.Zero)          → schedule a Timer (fires later)
else if (now >= popupAlertTime
      && now < meeting.StartTime)         → fire immediately (app was launched mid-window)
```

URL validation is applied identically in **both** branches to prevent the immediate-fire path from bypassing the guard.

---

## `DashboardViewModel` Integration

```csharp
// Constructor subscriptions
_googleCalendarService.MeetingStartingNow     += OnMeetingStartingNow;
_googleCalendarService.MeetingAlertInvalidUrl += OnMeetingAlertInvalidUrl;
```

```csharp
private void OnMeetingStartingNow(object? sender, CalendarEvent meeting)
{
    if (IsVacationMode) return;
    App.ShowMeetingAlert(meeting);  // dispatches to UI thread; deduplicates by meeting.Id
}

private void OnMeetingAlertInvalidUrl(object? sender, CalendarEvent meeting)
{
    // Silent log only — no popup is shown for broken links.
    Debug.WriteLine($"[MeetingAlert] Skipped popup for '{meeting.Title}' – invalid URL: '{meeting.MeetingLink}'");
}
```

---

## Unit Tests — `MeetingAlertTests.cs`

Tests live in `WorkActivityPanel.Tests/MeetingAlertTests.cs` and cover all validation branches:

| Test | Branch | Assertion |
|------|--------|-----------|
| `ScheduleMeetingAlerts_ValidUrl_RaisesMeetingStartingNow` | Immediate | `MeetingStartingNow` fires; `MeetingAlertInvalidUrl` does NOT |
| `ScheduleMeetingAlerts_InvalidUrl_RaisesMeetingAlertInvalidUrl` | Immediate | `MeetingAlertInvalidUrl` fires; `MeetingStartingNow` does NOT |
| `ScheduleMeetingAlerts_NoLink_NeitherEventFires` | Immediate | Neither event fires |
| `ScheduleMeetingAlerts_PastMeeting_NeitherEventFires` | — | Neither event fires for expired meetings |
| `ScheduleMeetingAlerts_FutureMeetingValidUrl_TimerFiresMeetingStartingNow` | Timer | `MeetingStartingNow` fires asynchronously (~200 ms) |
| `ScheduleMeetingAlerts_FutureMeetingInvalidUrl_TimerFiresMeetingAlertInvalidUrl` | Timer | `MeetingAlertInvalidUrl` fires asynchronously (~200 ms) |

### How the immediate-branch tests work

The tests use a **5-minute alert offset** with a meeting **2 minutes from now**. This means:

```
popupAlertTime = now + 2min − 5min = now − 3min
popupDelay     = −3 min  → NOT > TimeSpan.Zero

Immediate branch condition: now >= (now − 3min) && now < (now + 2min)  ✓
```

The event fires synchronously within `ScheduleMeetingAlerts`, with no real timer involved.

### Running the tests

```powershell
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" test WorkActivityPanel.Tests\WorkActivityPanel.Tests.csproj
```

---

## Key Lessons Learned

1. **Validate at the service layer, not the UI layer.** URL validation in the timer callback prevents the popup from ever appearing with a broken link, rather than failing silently inside the window.
2. **Both fire paths must be guarded.** The immediate-fire branch and the timer-callback branch are distinct code paths. Forgetting to guard one lets invalid URLs bypass the check when the app launches mid-window.
3. **Immediate-branch test design requires arithmetic.** To trigger the immediate path synchronously in tests, the meeting's `StartTime` and the configured offset must be chosen so that `popupDelay ≤ 0` and `now < StartTime`. Using offset=5 and `StartTime = now + 2min` reliably satisfies both conditions without any `Thread.Sleep`.
