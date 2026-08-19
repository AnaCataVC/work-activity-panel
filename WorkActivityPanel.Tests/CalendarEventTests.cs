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
}
