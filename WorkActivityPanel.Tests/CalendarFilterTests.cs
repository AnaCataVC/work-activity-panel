using System;
using WorkActivityPanel.Models;
using Xunit;

namespace WorkActivityPanel.Tests;

public class CalendarFilterTests
{
    [Theory]
    [InlineData("[Personal] Cita médica")]
    [InlineData("[personal] Tramite bancario")]
    [InlineData("[PERSONAL] Almuerzo")]
    [InlineData("Reunión [Personal]")]
    [InlineData("[Privado] Llamada familiar")]
    [InlineData("Out of office")]
    [InlineData("Out of Office: Doctor")]
    [InlineData("Fuera de la oficina")]
    [InlineData("OOO - Vacation")]
    [InlineData("Vacaciones")]
    [InlineData("Focus time")]
    public void ShouldOpenGranola_DefaultExcludedKeywords_ShouldReturnFalse(string title)
    {
        var settings = new CalendarFilterSettings();
        var evt = new CalendarEvent
        {
            Title = title,
            StartTime = DateTime.Now.AddHours(1),
            EndTime = DateTime.Now.AddHours(2),
            MeetingLink = "https://meet.google.com/abc-defg-hij"
        };

        var shouldOpen = settings.ShouldOpenGranola(evt);

        Assert.False(shouldOpen);
    }

    [Theory]
    [InlineData("Sprint Planning")]
    [InlineData("Daily Standup")]
    [InlineData("1:1 Sync with Manager")]
    [InlineData("Client Architecture Review")]
    public void ShouldOpenGranola_RegularMeetings_ShouldReturnTrue(string title)
    {
        var settings = new CalendarFilterSettings();
        var evt = new CalendarEvent
        {
            Title = title,
            StartTime = DateTime.Now.AddHours(1),
            EndTime = DateTime.Now.AddHours(2),
            MeetingLink = "https://meet.google.com/abc-defg-hij"
        };

        var shouldOpen = settings.ShouldOpenGranola(evt);

        Assert.True(shouldOpen);
    }

    [Fact]
    public void ShouldOpenGranola_AllDayEvent_WhenIgnoreAllDayIsTrue_ShouldReturnFalse()
    {
        var settings = new CalendarFilterSettings { IgnoreAllDayEvents = true };
        var evt = new CalendarEvent
        {
            Title = "Company Holiday",
            IsAllDay = true,
            StartTime = DateTime.Today,
            EndTime = DateTime.Today.AddDays(1)
        };

        Assert.False(settings.ShouldOpenGranola(evt));
    }

    [Fact]
    public void ShouldOpenGranola_AllDayEvent_WhenIgnoreAllDayIsFalse_ShouldReturnTrueForRegularTitle()
    {
        var settings = new CalendarFilterSettings { IgnoreAllDayEvents = false };
        var evt = new CalendarEvent
        {
            Title = "Hackathon Day 1",
            IsAllDay = true,
            StartTime = DateTime.Today,
            EndTime = DateTime.Today.AddDays(1)
        };

        Assert.True(settings.ShouldOpenGranola(evt));
    }

    [Fact]
    public void ShouldOpenGranola_RequireMeetingLink_WhenMeetingLinkPresent_ShouldReturnTrue()
    {
        var settings = new CalendarFilterSettings { RequireMeetingLink = true };
        var evt = new CalendarEvent
        {
            Title = "Sprint Demo",
            MeetingLink = "https://meet.google.com/abc-defg-hij",
            StartTime = DateTime.Now.AddHours(1),
            EndTime = DateTime.Now.AddHours(2)
        };

        Assert.True(settings.ShouldOpenGranola(evt));
    }

    [Fact]
    public void ShouldOpenGranola_RequireMeetingLink_WhenMeetingLinkMissing_ShouldReturnFalse()
    {
        var settings = new CalendarFilterSettings { RequireMeetingLink = true };
        var evt = new CalendarEvent
        {
            Title = "In-person Whiteboard Session",
            MeetingLink = null,
            StartTime = DateTime.Now.AddHours(1),
            EndTime = DateTime.Now.AddHours(2)
        };

        Assert.False(settings.ShouldOpenGranola(evt));
    }

    [Fact]
    public void ShouldOpenGranola_CustomExcludedKeywords_ShouldHonorCustomList()
    {
        var settings = new CalendarFilterSettings
        {
            ExcludedKeywords = "[NoGranola], InternalOnly, DoNotRecord"
        };

        var evtExcluded = new CalendarEvent
        {
            Title = "Project Sync [NoGranola]",
            StartTime = DateTime.Now.AddHours(1),
            EndTime = DateTime.Now.AddHours(2)
        };

        var evtAllowed = new CalendarEvent
        {
            Title = "Regular Sync",
            StartTime = DateTime.Now.AddHours(1),
            EndTime = DateTime.Now.AddHours(2)
        };

        Assert.False(settings.ShouldOpenGranola(evtExcluded));
        Assert.True(settings.ShouldOpenGranola(evtAllowed));
    }
}
