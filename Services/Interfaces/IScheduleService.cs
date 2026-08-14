using WorkActivityPanel.Models;

namespace WorkActivityPanel.Services.Interfaces;

/// <summary>
/// Service for managing and reacting to the work schedule.
/// </summary>
public interface IScheduleService
{
    /// <summary>
    /// Gets the current work schedule.
    /// </summary>
    WorkSchedule CurrentSchedule { get; }

    /// <summary>
    /// Gets a value indicating whether it is currently work time.
    /// </summary>
    bool IsWorkTime { get; }

    /// <summary>
    /// Gets or sets the work start time.
    /// </summary>
    TimeSpan WorkStartTime { get; set; }

    /// <summary>
    /// Gets or sets the work end time.
    /// </summary>
    TimeSpan WorkEndTime { get; set; }

    /// <summary>
    /// Gets or sets the list of work days.
    /// </summary>
    List<DayOfWeek> WorkDays { get; set; }

    /// <summary>
    /// Gets a value indicating whether vacation mode is enabled.
    /// </summary>
    bool IsVacationMode { get; }

    /// <summary>
    /// Occurs when work hours begin.
    /// </summary>
    event EventHandler? WorkStarted;

    /// <summary>
    /// Occurs when work hours end.
    /// </summary>
    event EventHandler? WorkEnded;

    /// <summary>
    /// Updates the current schedule and restarts timers.
    /// </summary>
    void UpdateSchedule(WorkSchedule schedule);

    /// <summary>
    /// Sets vacation mode and updates timers accordingly.
    /// </summary>
    void SetVacationMode(bool enabled);

    /// <summary>
    /// Starts the schedule timers.
    /// </summary>
    void Start();

    /// <summary>
    /// Stops the schedule timers.
    /// </summary>
    void Stop();
}
