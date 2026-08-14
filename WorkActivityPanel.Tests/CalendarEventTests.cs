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
}
