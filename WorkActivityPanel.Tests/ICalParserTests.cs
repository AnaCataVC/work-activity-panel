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
}
