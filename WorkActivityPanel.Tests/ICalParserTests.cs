using System;
using WorkActivityPanel.Helpers;
using Xunit;

namespace WorkActivityPanel.Tests;

public class ICalParserTests
{
    [Fact]
    public void UnfoldLines_ShouldCombineWrappedLines()
    {
        string raw = "SUMMARY:This is a long meeting \r\n title that was wrapped\r\nLOCATION:Room A";
        var lines = ICalParser.UnfoldLines(raw);

        Assert.Equal(2, lines.Count);
        Assert.Equal("SUMMARY:This is a long meeting title that was wrapped", lines[0]);
        Assert.Equal("LOCATION:Room A", lines[1]);
    }

    [Fact]
    public void ParseEventsForDate_ShouldExtractEventWithMeetLink()
    {
        var today = DateTime.Today;
        string todayStr = today.ToString("yyyyMMdd");

        string ics = $@"BEGIN:VCALENDAR
VERSION:2.0
PRODID:-//Google Inc//Google Calendar//EN
BEGIN:VEVENT
UID:test-123@google.com
DTSTART:{todayStr}T140000Z
DTEND:{todayStr}T143000Z
SUMMARY:Sprint Review
LOCATION:https://meet.google.com/abc-defg-hij
DESCRIPTION:Join meeting at https://meet.google.com/abc-defg-hij
END:VEVENT
BEGIN:VEVENT
UID:test-456@google.com
DTSTART:20250101T100000Z
DTEND:20250101T110000Z
SUMMARY:Old Meeting
LOCATION:Office
END:VEVENT
END:VCALENDAR";

        var events = ICalParser.ParseEventsForDate(ics, today);

        Assert.Single(events);
        Assert.Equal("test-123@google.com", events[0].Id);
        Assert.Equal("Sprint Review", events[0].Title);
        Assert.Equal("https://meet.google.com/abc-defg-hij", events[0].MeetingLink);
    }

    [Fact]
    public void ParseEventsForDate_ShouldFilterCancelledEventsAndDeduplicate()
    {
        var today = DateTime.Today;
        string todayStr = today.ToString("yyyyMMdd");

        string ics = $@"BEGIN:VCALENDAR
VERSION:2.0
BEGIN:VEVENT
UID:recurring-event-1@google.com
DTSTART:{todayStr}T150000Z
DTEND:{todayStr}T160000Z
SUMMARY:Daily Standup
STATUS:CANCELLED
END:VEVENT
BEGIN:VEVENT
UID:duplicate-event-2@google.com
DTSTART:{todayStr}T160000Z
DTEND:{todayStr}T170000Z
SUMMARY:1-on-1 Sync
END:VEVENT
BEGIN:VEVENT
UID:duplicate-event-2@google.com
DTSTART:{todayStr}T160000Z
DTEND:{todayStr}T170000Z
SUMMARY:1-on-1 Sync
END:VEVENT
END:VCALENDAR";

        var events = ICalParser.ParseEventsForDate(ics, today);

        Assert.Single(events);
        Assert.Equal("duplicate-event-2@google.com", events[0].Id);
        Assert.Equal("1-on-1 Sync", events[0].Title);
    }

    [Fact]
    public void ExtractMeetingLink_ShouldDetectZoomAndTeams()
    {
        string zoomText = "Please join Zoom meeting: https://us02web.zoom.us/j/123456789";
        string teamsText = "Microsoft Teams Meeting: https://teams.microsoft.com/l/meetup-join/abc";

        var zoomLink = ICalParser.ExtractMeetingLink("", zoomText);
        var teamsLink = ICalParser.ExtractMeetingLink(teamsText, "");

        Assert.Equal("https://us02web.zoom.us/j/123456789", zoomLink);
        Assert.Equal("https://teams.microsoft.com/l/meetup-join/abc", teamsLink);
    }

    [Fact]
    public void ParseEventsForDate_ShouldRecognizeAllDayEvents()
    {
        var today = DateTime.Today;
        string todayStr = today.ToString("yyyyMMdd");
        string tomorrowStr = today.AddDays(1).ToString("yyyyMMdd");

        string ics = $@"BEGIN:VCALENDAR
VERSION:2.0
BEGIN:VEVENT
UID:allday-ooo-1@google.com
DTSTART;VALUE=DATE:{todayStr}
DTEND;VALUE=DATE:{tomorrowStr}
SUMMARY:Out of office
END:VEVENT
END:VCALENDAR";

        var events = ICalParser.ParseEventsForDate(ics, today);

        Assert.Single(events);
        Assert.Equal("allday-ooo-1@google.com", events[0].Id);
        Assert.Equal("Out of office", events[0].Title);
        Assert.True(events[0].IsAllDay);
        Assert.Equal("Todo el día", events[0].FormattedStartTime);
    }

    [Fact]
    public void ParseEventsForDate_ShouldExpandWeeklyRecurringEventOnMatchingDay()
    {
        // Series started 2 weeks ago on Monday at 11:30 AM, repeats weekly on MO and TH
        var seriesStart = new DateTime(2026, 8, 24, 11, 30, 0);
        var targetMonday = new DateTime(2026, 9, 7); // Monday
        var targetTuesday = new DateTime(2026, 9, 8); // Tuesday
        var targetThursday = new DateTime(2026, 9, 10); // Thursday

        string ics = @"BEGIN:VCALENDAR
VERSION:2.0
BEGIN:VEVENT
UID:standup-algoritmo@google.com
DTSTART:20260824T113000
DTEND:20260824T120000
RRULE:FREQ=WEEKLY;WKST=SU;BYDAY=MO,TH
SUMMARY:Standup Algoritmo
LOCATION:https://meet.google.com/vep-hzrs-xex
DESCRIPTION:Join at https://meet.google.com/vep-hzrs-xex
STATUS:CONFIRMED
END:VEVENT
END:VCALENDAR";

        var mondayEvents = ICalParser.ParseEventsForDate(ics, targetMonday);
        var tuesdayEvents = ICalParser.ParseEventsForDate(ics, targetTuesday);
        var thursdayEvents = ICalParser.ParseEventsForDate(ics, targetThursday);

        // Monday should match
        Assert.Single(mondayEvents);
        Assert.Equal("Standup Algoritmo", mondayEvents[0].Title);
        Assert.Equal(new DateTime(2026, 9, 7, 11, 30, 0), mondayEvents[0].StartTime);
        Assert.Equal(new DateTime(2026, 9, 7, 12, 0, 0), mondayEvents[0].EndTime);
        Assert.Equal("https://meet.google.com/vep-hzrs-xex", mondayEvents[0].MeetingLink);

        // Tuesday should NOT match
        Assert.Empty(tuesdayEvents);

        // Thursday should match
        Assert.Single(thursdayEvents);
        Assert.Equal("Standup Algoritmo", thursdayEvents[0].Title);
        Assert.Equal(new DateTime(2026, 9, 10, 11, 30, 0), thursdayEvents[0].StartTime);
    }

    [Fact]
    public void ParseEventsForDate_ShouldRespectUntilInRecurringEvent()
    {
        // Series until Sept 1, 2026
        var targetDate = new DateTime(2026, 9, 7);

        string ics = @"BEGIN:VCALENDAR
VERSION:2.0
BEGIN:VEVENT
UID:expired-standup@google.com
DTSTART:20260801T100000
DTEND:20260801T110000
RRULE:FREQ=WEEKLY;UNTIL=20260901T000000Z;BYDAY=MO
SUMMARY:Old Standup
END:VEVENT
END:VCALENDAR";

        var events = ICalParser.ParseEventsForDate(ics, targetDate);
        Assert.Empty(events);
    }

    [Fact]
    public void ParseEventsForDate_ShouldRespectExDateInRecurringEvent()
    {
        var targetMonday = new DateTime(2026, 9, 7);

        string ics = @"BEGIN:VCALENDAR
VERSION:2.0
BEGIN:VEVENT
UID:standup-with-holiday@google.com
DTSTART:20260824T100000
DTEND:20260824T110000
RRULE:FREQ=WEEKLY;BYDAY=MO
EXDATE:20260907T100000
SUMMARY:Standup
END:VEVENT
END:VCALENDAR";

        var events = ICalParser.ParseEventsForDate(ics, targetMonday);
        Assert.Empty(events);
    }

    [Fact]
    public void ParseEventsForDate_ShouldRespectRecurrenceIdOverride()
    {
        var targetMonday = new DateTime(2026, 9, 7);

        // Master series is at 10:00 AM, but Sept 7 occurrence was rescheduled to 14:00 PM with new title
        string ics = @"BEGIN:VCALENDAR
VERSION:2.0
BEGIN:VEVENT
UID:standup-rescheduled@google.com
DTSTART:20260824T100000
DTEND:20260824T110000
RRULE:FREQ=WEEKLY;BYDAY=MO
SUMMARY:Regular Standup
END:VEVENT
BEGIN:VEVENT
UID:standup-rescheduled@google.com
RECURRENCE-ID:20260907T100000
DTSTART:20260907T140000
DTEND:20260907T150000
SUMMARY:Rescheduled Standup
LOCATION:https://meet.google.com/new-link
END:VEVENT
END:VCALENDAR";

        var events = ICalParser.ParseEventsForDate(ics, targetMonday);

        Assert.Single(events);
        Assert.Equal("Rescheduled Standup", events[0].Title);
        Assert.Equal(new DateTime(2026, 9, 7, 14, 0, 0), events[0].StartTime);
        Assert.Equal("https://meet.google.com/new-link", events[0].MeetingLink);
    }

    [Fact]
    public void ParseEventsForDate_ShouldRespectRecurrenceIdCancelled()
    {
        var targetMonday = new DateTime(2026, 9, 7);

        // Master series on MO, but Sept 7 was cancelled
        string ics = @"BEGIN:VCALENDAR
VERSION:2.0
BEGIN:VEVENT
UID:standup-cancelled-day@google.com
DTSTART:20260824T100000
DTEND:20260824T110000
RRULE:FREQ=WEEKLY;BYDAY=MO
SUMMARY:Regular Standup
END:VEVENT
BEGIN:VEVENT
UID:standup-cancelled-day@google.com
RECURRENCE-ID:20260907T100000
STATUS:CANCELLED
SUMMARY:Regular Standup
END:VEVENT
END:VCALENDAR";

        var events = ICalParser.ParseEventsForDate(ics, targetMonday);
        Assert.Empty(events);
    }

    [Fact]
    public void ParseEventsForDate_ShouldSupportBiweeklyInterval()
    {
        // Series starts Aug 24 (Week 0)
        // Week 1 (Aug 31): should not match
        // Week 2 (Sept 7): should match
        var targetWeek1 = new DateTime(2026, 8, 31);
        var targetWeek2 = new DateTime(2026, 9, 7);

        string ics = @"BEGIN:VCALENDAR
VERSION:2.0
BEGIN:VEVENT
UID:biweekly-review@google.com
DTSTART:20260824T150000
DTEND:20260824T160000
RRULE:FREQ=WEEKLY;INTERVAL=2;BYDAY=MO
SUMMARY:Bi-weekly Sprint Review
END:VEVENT
END:VCALENDAR";

        var eventsWeek1 = ICalParser.ParseEventsForDate(ics, targetWeek1);
        var eventsWeek2 = ICalParser.ParseEventsForDate(ics, targetWeek2);

        Assert.Empty(eventsWeek1);
        Assert.Single(eventsWeek2);
        Assert.Equal("Bi-weekly Sprint Review", eventsWeek2[0].Title);
    }
}
