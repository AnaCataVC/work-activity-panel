using System;
using WorkActivityPanel.Models;
using Xunit;

namespace WorkActivityPanel.Tests;

public class CalendarEventTests
{
    [Fact]
    public void CalendarEvent_CanBeConstructedWithProperties()
    {
        // Arrange & Act
        var now = DateTime.Now;
        var evt = new CalendarEvent
        {
            Id = "evt-123",
            Title = "Sprint Planning",
            StartTime = now,
            EndTime = now.AddHours(1),
            MeetingLink = "https://meet.google.com/abc-defg-hij"
        };

        // Assert
        Assert.Equal("evt-123", evt.Id);
        Assert.Equal("Sprint Planning", evt.Title);
        Assert.Equal(now, evt.StartTime);
        Assert.Equal(now.AddHours(1), evt.EndTime);
        Assert.Equal("https://meet.google.com/abc-defg-hij", evt.MeetingLink);
    }

    [Fact]
    public void CalendarEvent_PastEvent_ShouldIdentifyAsPast()
    {
        var evt = new CalendarEvent
        {
            Title = "Past Meeting",
            StartTime = DateTime.Now.AddHours(-3),
            EndTime = DateTime.Now.AddHours(-2)
        };

        Assert.True(evt.IsPast);
        Assert.False(evt.IsInProgress);
        Assert.False(evt.IsUpcoming);
        Assert.Equal("Finalizada", evt.StatusText);
    }

    [Fact]
    public void CalendarEvent_InProgressEvent_ShouldIdentifyAsInProgress()
    {
        var evt = new CalendarEvent
        {
            Title = "Active Meeting",
            StartTime = DateTime.Now.AddMinutes(-15),
            EndTime = DateTime.Now.AddMinutes(45)
        };

        Assert.False(evt.IsPast);
        Assert.True(evt.IsInProgress);
        Assert.False(evt.IsUpcoming);
        Assert.Equal("En curso", evt.StatusText);
    }

    [Fact]
    public void CalendarEvent_UpcomingEvent_ShouldIdentifyAsUpcoming()
    {
        var evt = new CalendarEvent
        {
            Title = "Future Meeting",
            StartTime = DateTime.Now.AddHours(2),
            EndTime = DateTime.Now.AddHours(3),
            OpensGranola = true
        };

        Assert.False(evt.IsPast);
        Assert.False(evt.IsInProgress);
        Assert.True(evt.IsUpcoming);
        Assert.Equal("Granola se abrirá 5 min antes", evt.StatusText);
    }

    [Fact]
    public void CalendarEvent_UpcomingEvent_WithOpensGranolaFalse_ShouldShowSoloEnCalendario()
    {
        var evt = new CalendarEvent
        {
            Title = "[Personal] Cita con el dentista",
            StartTime = DateTime.Now.AddHours(2),
            EndTime = DateTime.Now.AddHours(3),
            OpensGranola = false
        };

        Assert.True(evt.IsUpcoming);
        Assert.Equal("Solo en calendario", evt.StatusText);
    }

    [Fact]
    public void CalendarEvent_AllDayEvent_FormattedTimesShouldShowTodoElDia()
    {
        var evt = new CalendarEvent
        {
            Title = "Out of office - Vacaciones",
            StartTime = DateTime.Today,
            EndTime = DateTime.Today.AddDays(1),
            IsAllDay = true
        };

        Assert.Equal("Todo el día", evt.FormattedStartTime);
        Assert.Equal("Todo el día", evt.FormattedEndTime);
    }

    [Theory]
    [InlineData(6, false)]  // 6 min before: outside 5-min window
    [InlineData(5, true)]   // Exactly 5 min before: inside window
    [InlineData(2, true)]   // 2 min before: inside window
    [InlineData(0, false)]  // 0 min (meeting starts): outside pre-meeting window (now in progress)
    [InlineData(-5, false)] // 5 min after start: meeting in progress/past
    public void CalendarEvent_PreMeetingWindow_ShouldEvaluateCorrectly(int minutesBeforeStart, bool expectedInPreWindow)
    {
        var meeting = new CalendarEvent
        {
            Id = "test-window",
            Title = "Sync Meeting",
            StartTime = DateTime.Now.AddMinutes(minutesBeforeStart),
            EndTime = DateTime.Now.AddMinutes(minutesBeforeStart + 30),
            OpensGranola = true
        };

        var now = DateTime.Now;
        bool inPreWindow = meeting.OpensGranola && !meeting.IsAllDay && now >= meeting.StartTime.AddMinutes(-5) && now < meeting.StartTime;

        Assert.Equal(expectedInPreWindow, inPreWindow);
    }

    [Fact]
    public void CalendarEvent_Matches_IdenticalProperties_ShouldReturnTrue()
    {
        var start = DateTime.Today.AddHours(10);
        var end = start.AddMinutes(30);

        var event1 = new CalendarEvent
        {
            Id = "meeting-1",
            Title = "Sync Cata / Jose",
            StartTime = start,
            EndTime = end,
            MeetingLink = "https://meet.google.com/abc",
            OpensGranola = true,
            IsAllDay = false
        };

        var event2 = new CalendarEvent
        {
            Id = "meeting-1",
            Title = "Sync Cata / Jose",
            StartTime = start,
            EndTime = end,
            MeetingLink = "https://meet.google.com/abc",
            OpensGranola = true,
            IsAllDay = false
        };

        Assert.True(event1.Matches(event2));
        Assert.True(event2.Matches(event1));
    }

    [Fact]
    public void CalendarEvent_Matches_SameIdDifferentTime_ShouldReturnFalse()
    {
        var baseDate = DateTime.Today;

        var oldEvent = new CalendarEvent
        {
            Id = "sync-cata-jose",
            Title = "Sync Cata / Jose",
            StartTime = baseDate.AddHours(11).AddMinutes(30),
            EndTime = baseDate.AddHours(12),
            MeetingLink = "https://meet.google.com/abc",
            OpensGranola = true,
            IsAllDay = false
        };

        var rescheduledEvent = new CalendarEvent
        {
            Id = "sync-cata-jose",
            Title = "Sync Cata / Jose",
            StartTime = baseDate.AddHours(12).AddMinutes(30), // Rescheduled from 11:30 to 12:30
            EndTime = baseDate.AddHours(13),
            MeetingLink = "https://meet.google.com/abc",
            OpensGranola = true,
            IsAllDay = false
        };

        Assert.False(oldEvent.Matches(rescheduledEvent));
        Assert.False(rescheduledEvent.Matches(oldEvent));
    }

    [Fact]
    public void CalendarEvent_Matches_DifferentPropertiesOrNull_ShouldReturnFalse()
    {
        var evt = new CalendarEvent
        {
            Id = "event-test",
            Title = "Status Update",
            StartTime = DateTime.Today.AddHours(9),
            EndTime = DateTime.Today.AddHours(10),
            MeetingLink = "https://meet.google.com/abc",
            OpensGranola = true,
            IsAllDay = false
        };

        Assert.False(evt.Matches(null));

        var diffId = new CalendarEvent { Id = "other-id", Title = evt.Title, StartTime = evt.StartTime, EndTime = evt.EndTime, MeetingLink = evt.MeetingLink, OpensGranola = evt.OpensGranola, IsAllDay = evt.IsAllDay };
        Assert.False(evt.Matches(diffId));

        var diffTitle = new CalendarEvent { Id = evt.Id, Title = "New Title", StartTime = evt.StartTime, EndTime = evt.EndTime, MeetingLink = evt.MeetingLink, OpensGranola = evt.OpensGranola, IsAllDay = evt.IsAllDay };
        Assert.False(evt.Matches(diffTitle));

        var diffLink = new CalendarEvent { Id = evt.Id, Title = evt.Title, StartTime = evt.StartTime, EndTime = evt.EndTime, MeetingLink = "https://meet.google.com/xyz", OpensGranola = evt.OpensGranola, IsAllDay = evt.IsAllDay };
        Assert.False(evt.Matches(diffLink));
    }
}
