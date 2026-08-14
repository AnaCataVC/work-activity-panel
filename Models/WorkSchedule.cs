namespace WorkActivityPanel.Models;

/// <summary>
/// Represents the configurable work schedule.
/// </summary>
public class WorkSchedule
{
    /// <summary>
    /// Gets or sets the work start time (default 9:00 AM).
    /// </summary>
    public TimeSpan StartTime { get; set; } = new TimeSpan(9, 0, 0);

    /// <summary>
    /// Gets or sets the work end time (default 6:00 PM).
    /// </summary>
    public TimeSpan EndTime { get; set; } = new TimeSpan(18, 0, 0);

    /// <summary>
    /// Gets or sets which days are work days (default Mon-Fri).
    /// </summary>
    public List<DayOfWeek> WorkDays { get; set; } = new List<DayOfWeek>
    {
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday
    };

    /// <summary>
    /// Gets or sets a value indicating whether vacation mode is enabled.
    /// </summary>
    public bool IsVacationMode { get; set; }

    /// <summary>
    /// Checks if the given time (or current time) is within work hours, on a work day, and vacation mode is off.
    /// </summary>
    public bool IsWorkTime(DateTime? currentTime = null)
    {
        if (IsVacationMode) return false;

        var time = currentTime ?? DateTime.Now;
        if (!WorkDays.Contains(time.DayOfWeek)) return false;

        var timeOfDay = time.TimeOfDay;
        return timeOfDay >= StartTime && timeOfDay <= EndTime;
    }

    /// <summary>
    /// Returns the TimeSpan until the next work start time.
    /// </summary>
    public TimeSpan GetTimeUntilWorkStart(DateTime? currentTime = null)
    {
        if (WorkDays == null || WorkDays.Count == 0)
        {
            return Timeout.InfiniteTimeSpan;
        }

        var time = currentTime ?? DateTime.Now;
        var nextWorkStart = time.Date + StartTime;

        if (time.TimeOfDay >= StartTime)
        {
            nextWorkStart = nextWorkStart.AddDays(1);
        }

        int maxDaysToCheck = 8;
        while (!WorkDays.Contains(nextWorkStart.DayOfWeek) && maxDaysToCheck-- > 0)
        {
            nextWorkStart = nextWorkStart.AddDays(1);
        }

        return nextWorkStart - time;
    }
}
