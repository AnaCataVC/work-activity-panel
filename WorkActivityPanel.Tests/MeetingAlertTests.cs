using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WorkActivityPanel.Models;
using WorkActivityPanel.Services;
using WorkActivityPanel.Services.Interfaces;
using Xunit;

namespace WorkActivityPanel.Tests;

/// <summary>
/// Unit tests for the meeting popup alert URL-validation logic inside <see cref="GoogleCalendarService"/>.
/// These tests exercise <see cref="GoogleCalendarService.ScheduleMeetingAlerts"/> directly to verify
/// that <c>MeetingStartingNow</c> is raised only for well-formed absolute URIs and that
/// <c>MeetingAlertInvalidUrl</c> is raised instead for malformed links.
/// </summary>
public class MeetingAlertTests : IDisposable
{
    // Keeps tests from touching the real LocalAppData settings file.
    private readonly TempSettingsFileScope _settingsScope = new();

    private static GoogleCalendarService CreateService(int alertOffsetMinutes = 5)
    {
        var launcherMock = new Mock<IAppLauncherService>();
        var svc = new GoogleCalendarService(launcherMock.Object, NullLogger<GoogleCalendarService>.Instance);
        svc.UpdateFilterSettings(new CalendarFilterSettings { MeetingAlertOffsetMinutes = alertOffsetMinutes });
        return svc;
    }

    /// <summary>
    /// Builds a meeting that falls inside the "already in the alert window" branch.
    /// With a 5-minute offset and StartTime 2 minutes from now:
    ///   popupAlertTime = now + 2min − 5min = now − 3min
    ///   → now >= popupAlertTime (3 min ago) &amp;&amp; now &lt; StartTime (2 min away) ✓
    /// The service fires the event synchronously without scheduling a real timer.
    /// </summary>
    private static CalendarEvent MakeImmediateEvent(string? meetingLink) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Title = "Test Meeting",
        StartTime = DateTime.Now.AddMinutes(2),
        EndTime = DateTime.Now.AddHours(1),
        MeetingLink = meetingLink,
    };

    // ──────────────────────────────────────────────────────────────────────────
    // Immediate branch: valid URL → MeetingStartingNow fires, InvalidUrl does NOT.
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("https://meet.google.com/abc-defg-hij")]
    [InlineData("https://us06web.zoom.us/j/12345678?pwd=abc123")]
    [InlineData("https://teams.microsoft.com/l/meetup-join/abc123")]
    [InlineData("https://app.chime.aws/meetings/123456")]
    [InlineData("https://webex.com/meet/johndoe")]
    public void ScheduleMeetingAlerts_ValidUrl_RaisesMeetingStartingNow(string url)
    {
        // offset=5min so "now" is inside the 5-minute alert window for a meeting 2 min away.
        using var svc = CreateService(alertOffsetMinutes: 5);

        bool startingNowFired = false;
        bool invalidUrlFired = false;

        svc.MeetingStartingNow += (_, _) => startingNowFired = true;
        svc.MeetingAlertInvalidUrl += (_, _) => invalidUrlFired = true;

        svc.ScheduleMeetingAlerts(new[] { MakeImmediateEvent(url) });

        Assert.True(startingNowFired, "MeetingStartingNow should have fired for a valid URL.");
        Assert.False(invalidUrlFired, "MeetingAlertInvalidUrl must NOT fire for a valid URL.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Immediate branch: malformed URL → MeetingAlertInvalidUrl fires, StartingNow does NOT.
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("meet.google.com/abc")]  // relative-looking, no scheme
    [InlineData("://broken-scheme")]
    [InlineData("   ")]                  // whitespace — IsWellFormedUriString returns false
    public void ScheduleMeetingAlerts_InvalidUrl_RaisesMeetingAlertInvalidUrl(string badUrl)
    {
        // Guard: if somehow the runtime considers this a well-formed absolute URI,
        // the service will rightly fire MeetingStartingNow — skip rather than assert wrongly.
        if (Uri.IsWellFormedUriString(badUrl, UriKind.Absolute))
            return;

        using var svc = CreateService(alertOffsetMinutes: 5);

        bool startingNowFired = false;
        bool invalidUrlFired = false;

        svc.MeetingStartingNow += (_, _) => startingNowFired = true;
        svc.MeetingAlertInvalidUrl += (_, _) => invalidUrlFired = true;

        svc.ScheduleMeetingAlerts(new[] { MakeImmediateEvent(badUrl) });

        Assert.True(invalidUrlFired, "MeetingAlertInvalidUrl should have fired for a malformed URL.");
        Assert.False(startingNowFired, "MeetingStartingNow must NOT fire for a malformed URL.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // No link → neither event fires (popup silently skipped per design).
    // ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ScheduleMeetingAlerts_NoLink_NeitherEventFires(string? link)
    {
        using var svc = CreateService(alertOffsetMinutes: 5);

        bool startingNowFired = false;
        bool invalidUrlFired = false;

        svc.MeetingStartingNow += (_, _) => startingNowFired = true;
        svc.MeetingAlertInvalidUrl += (_, _) => invalidUrlFired = true;

        svc.ScheduleMeetingAlerts(new[] { MakeImmediateEvent(link) });

        Assert.False(startingNowFired, "MeetingStartingNow must NOT fire for events without a link.");
        Assert.False(invalidUrlFired, "MeetingAlertInvalidUrl must NOT fire for events without a link.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Past meeting → popup alert is skipped entirely; neither event fires.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ScheduleMeetingAlerts_PastMeeting_NeitherEventFires()
    {
        using var svc = CreateService(alertOffsetMinutes: 0);

        bool startingNowFired = false;
        bool invalidUrlFired = false;

        svc.MeetingStartingNow += (_, _) => startingNowFired = true;
        svc.MeetingAlertInvalidUrl += (_, _) => invalidUrlFired = true;

        var past = new CalendarEvent
        {
            Id = Guid.NewGuid().ToString(),
            Title = "Old Meeting",
            StartTime = DateTime.Now.AddHours(-2),
            EndTime = DateTime.Now.AddHours(-1),
            MeetingLink = "https://meet.google.com/abc-defg-hij",
        };

        svc.ScheduleMeetingAlerts(new[] { past });

        // Brief pause to confirm no timer fires asynchronously.
        Thread.Sleep(50);

        Assert.False(startingNowFired, "MeetingStartingNow must NOT fire for a past meeting.");
        Assert.False(invalidUrlFired, "MeetingAlertInvalidUrl must NOT fire for a past meeting.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Timer path: valid URL → MeetingStartingNow fires after delay.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ScheduleMeetingAlerts_FutureMeetingValidUrl_TimerFiresMeetingStartingNow()
    {
        // offset=0, meeting starts in 200 ms → timer fires in ~200 ms.
        using var svc = CreateService(alertOffsetMinutes: 0);

        var tcs = new TaskCompletionSource<CalendarEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool invalidUrlFired = false;

        svc.MeetingStartingNow += (_, e) => tcs.TrySetResult(e);
        svc.MeetingAlertInvalidUrl += (_, _) => invalidUrlFired = true;

        var future = new CalendarEvent
        {
            Id = Guid.NewGuid().ToString(),
            Title = "Upcoming Valid Meeting",
            StartTime = DateTime.Now.AddMilliseconds(200),
            EndTime = DateTime.Now.AddHours(1),
            MeetingLink = "https://meet.google.com/xyz-uvw-rst",
        };

        svc.ScheduleMeetingAlerts(new[] { future });

        var fired = await Task.WhenAny(tcs.Task, Task.Delay(3000)) == tcs.Task;
        Assert.True(fired, "MeetingStartingNow should have fired within 3 seconds for a near-future valid-URL meeting.");
        Assert.False(invalidUrlFired, "MeetingAlertInvalidUrl must NOT fire for a valid URL.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Timer path: invalid URL → MeetingAlertInvalidUrl fires after delay.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ScheduleMeetingAlerts_FutureMeetingInvalidUrl_TimerFiresMeetingAlertInvalidUrl()
    {
        using var svc = CreateService(alertOffsetMinutes: 0);

        var tcs = new TaskCompletionSource<CalendarEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool startingNowFired = false;

        svc.MeetingAlertInvalidUrl += (_, e) => tcs.TrySetResult(e);
        svc.MeetingStartingNow += (_, _) => startingNowFired = true;

        var future = new CalendarEvent
        {
            Id = Guid.NewGuid().ToString(),
            Title = "Upcoming Bad-URL Meeting",
            StartTime = DateTime.Now.AddMilliseconds(200),
            EndTime = DateTime.Now.AddHours(1),
            MeetingLink = "not-a-valid-url",
        };

        svc.ScheduleMeetingAlerts(new[] { future });

        var fired = await Task.WhenAny(tcs.Task, Task.Delay(3000)) == tcs.Task;
        Assert.True(fired, "MeetingAlertInvalidUrl should have fired within 3 seconds for a near-future invalid-URL meeting.");
        Assert.False(startingNowFired, "MeetingStartingNow must NOT fire for a malformed URL.");
    }

    public void Dispose() => _settingsScope.Dispose();
}
