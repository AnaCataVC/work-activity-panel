using System;
using System.Collections.Generic;
using WorkActivityPanel.Models;
using Xunit;

namespace WorkActivityPanel.Tests;

public class WorkScheduleTests
{
    [Fact]
    public void IsWorkTime_ReturnsTrue_DuringWorkHoursOnWorkDay()
    {
        // Arrange: Monday at 10:30 AM (Work hours: 09:00 - 18:00)
        var schedule = new WorkSchedule
        {
            StartTime = new TimeSpan(9, 0, 0),
            EndTime = new TimeSpan(18, 0, 0),
            WorkDays = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday },
            IsVacationMode = false
        };

        var mondayTenAm = new DateTime(2026, 8, 17, 10, 30, 0); // Monday

        // Act
        var result = schedule.IsWorkTime(mondayTenAm);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsWorkTime_ReturnsFalse_BeforeWorkHours()
    {
        // Arrange: Monday at 08:00 AM (Work hours: 09:00 - 18:00)
        var schedule = new WorkSchedule
        {
            StartTime = new TimeSpan(9, 0, 0),
            EndTime = new TimeSpan(18, 0, 0),
            WorkDays = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday },
            IsVacationMode = false
        };

        var mondayEightAm = new DateTime(2026, 8, 17, 8, 0, 0); // Monday

        // Act
        var result = schedule.IsWorkTime(mondayEightAm);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsWorkTime_ReturnsFalse_AfterWorkHours()
    {
        // Arrange: Monday at 19:00 (Work hours: 09:00 - 18:00)
        var schedule = new WorkSchedule
        {
            StartTime = new TimeSpan(9, 0, 0),
            EndTime = new TimeSpan(18, 0, 0),
            WorkDays = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday },
            IsVacationMode = false
        };

        var mondaySevenPm = new DateTime(2026, 8, 17, 19, 0, 0); // Monday

        // Act
        var result = schedule.IsWorkTime(mondaySevenPm);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsWorkTime_ReturnsFalse_OnWeekend()
    {
        // Arrange: Saturday at 11:00 AM
        var schedule = new WorkSchedule
        {
            StartTime = new TimeSpan(9, 0, 0),
            EndTime = new TimeSpan(18, 0, 0),
            WorkDays = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday },
            IsVacationMode = false
        };

        var saturdayElevenAm = new DateTime(2026, 8, 15, 11, 0, 0); // Saturday

        // Act
        var result = schedule.IsWorkTime(saturdayElevenAm);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsWorkTime_ReturnsFalse_WhenVacationModeActive()
    {
        // Arrange: Monday at 10:30 AM but vacation mode is ON
        var schedule = new WorkSchedule
        {
            StartTime = new TimeSpan(9, 0, 0),
            EndTime = new TimeSpan(18, 0, 0),
            WorkDays = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday },
            IsVacationMode = true
        };

        var mondayTenAm = new DateTime(2026, 8, 17, 10, 30, 0); // Monday

        // Act
        var result = schedule.IsWorkTime(mondayTenAm);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void GetTimeUntilWorkStart_CalculatesSameDayDelay_WhenBeforeWorkHours()
    {
        // Arrange: Monday at 07:00 AM, StartTime is 09:00 AM
        var schedule = new WorkSchedule
        {
            StartTime = new TimeSpan(9, 0, 0),
            EndTime = new TimeSpan(18, 0, 0),
            WorkDays = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday },
            IsVacationMode = false
        };

        var mondaySevenAm = new DateTime(2026, 8, 17, 7, 0, 0);

        // Act
        var delay = schedule.GetTimeUntilWorkStart(mondaySevenAm);

        // Assert: Expected 2 hours
        Assert.Equal(TimeSpan.FromHours(2), delay);
    }

    [Fact]
    public void GetTimeUntilWorkStart_CalculatesNextWorkDay_WhenOnFridayAfterHours()
    {
        // Arrange: Friday at 19:00 (7 PM), Next work day is Monday at 09:00 AM
        var schedule = new WorkSchedule
        {
            StartTime = new TimeSpan(9, 0, 0),
            EndTime = new TimeSpan(18, 0, 0),
            WorkDays = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday },
            IsVacationMode = false
        };

        var fridaySevenPm = new DateTime(2026, 8, 21, 19, 0, 0); // Friday 19:00
        var expectedNextStart = new DateTime(2026, 8, 24, 9, 0, 0); // Monday 09:00
        var expectedDelay = expectedNextStart - fridaySevenPm;

        // Act
        var delay = schedule.GetTimeUntilWorkStart(fridaySevenPm);

        // Assert
        Assert.Equal(expectedDelay, delay);
    }

    [Fact]
    public void GetTimeUntilWorkStart_ReturnsInfinite_WhenNoWorkDaysConfigured()
    {
        var schedule = new WorkSchedule
        {
            WorkDays = new List<DayOfWeek>()
        };

        var delay = schedule.GetTimeUntilWorkStart();
        Assert.Equal(System.Threading.Timeout.InfiniteTimeSpan, delay);
    }
}
