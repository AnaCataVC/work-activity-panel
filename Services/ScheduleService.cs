using System.Text.Json;
using WorkActivityPanel.Helpers;
using WorkActivityPanel.Models;
using WorkActivityPanel.Services.Interfaces;

namespace WorkActivityPanel.Services;

/// <summary>
/// Implementation of IScheduleService using System.Threading.Timer.
/// </summary>
public class ScheduleService : IScheduleService, IDisposable
{
    private const string ScheduleSettingsKey = "WorkScheduleSettings";
    private Timer? _timer;
    private WorkSchedule _currentSchedule;

    /// <inheritdoc />
    public event EventHandler? WorkStarted;

    /// <inheritdoc />
    public event EventHandler? WorkEnded;

    /// <inheritdoc />
    public event EventHandler<bool>? VacationModeChanged;

    /// <inheritdoc />
    public event EventHandler? ScheduleChanged;

    public ScheduleService()
    {
        _currentSchedule = LoadSchedule();
    }

    /// <inheritdoc />
    public WorkSchedule CurrentSchedule => _currentSchedule;

    /// <inheritdoc />
    public bool IsWorkTime => _currentSchedule.IsWorkTime();

    /// <inheritdoc />
    public TimeSpan WorkStartTime
    {
        get => _currentSchedule.StartTime;
        set
        {
            _currentSchedule.StartTime = value;
            SaveSchedule(_currentSchedule);
            Start();
            ScheduleChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc />
    public TimeSpan WorkEndTime
    {
        get => _currentSchedule.EndTime;
        set
        {
            _currentSchedule.EndTime = value;
            SaveSchedule(_currentSchedule);
            Start();
            ScheduleChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc />
    public List<DayOfWeek> WorkDays
    {
        get => _currentSchedule.WorkDays;
        set
        {
            _currentSchedule.WorkDays = value;
            SaveSchedule(_currentSchedule);
            Start();
            ScheduleChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc />
    public bool IsVacationMode => _currentSchedule.IsVacationMode;

    /// <inheritdoc />
    public void UpdateSchedule(WorkSchedule schedule)
    {
        var vacationChanged = _currentSchedule.IsVacationMode != schedule.IsVacationMode;
        _currentSchedule = schedule;
        SaveSchedule(_currentSchedule);
        Start(); // Restart with new schedule
        
        if (vacationChanged)
        {
            VacationModeChanged?.Invoke(this, schedule.IsVacationMode);
        }
        ScheduleChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void SetVacationMode(bool enabled)
    {
        var vacationChanged = _currentSchedule.IsVacationMode != enabled;
        _currentSchedule.IsVacationMode = enabled;
        SaveSchedule(_currentSchedule);
        
        if (enabled)
        {
            Stop();
        }
        else
        {
            Start();
        }

        if (vacationChanged)
        {
            VacationModeChanged?.Invoke(this, enabled);
        }
        ScheduleChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void Start()
    {
        Stop();

        if (_currentSchedule.IsVacationMode) return;

        if (IsWorkTime)
        {
            WorkStarted?.Invoke(this, EventArgs.Empty);
            ScheduleWorkEnd();
        }
        else
        {
            ScheduleWorkStart();
        }
    }

    /// <inheritdoc />
    public void Stop()
    {
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        _timer?.Dispose();
        _timer = null;
    }

    private void ScheduleWorkStart()
    {
        var delay = _currentSchedule.GetTimeUntilWorkStart();
        _timer = new Timer(OnWorkStart, null, delay, Timeout.InfiniteTimeSpan);
    }

    private void OnWorkStart(object? state)
    {
        WorkStarted?.Invoke(this, EventArgs.Empty);
        ScheduleWorkEnd();
    }

    private void ScheduleWorkEnd()
    {
        var now = DateTime.Now;
        var endToday = now.Date + _currentSchedule.EndTime;
        var delay = endToday - now;
        
        if (delay.TotalMilliseconds <= 0)
        {
            // If already past end time for some reason, just schedule next start
            ScheduleWorkStart();
            return;
        }

        _timer = new Timer(OnWorkEnd, null, delay, Timeout.InfiniteTimeSpan);
    }

    private void OnWorkEnd(object? state)
    {
        WorkEnded?.Invoke(this, EventArgs.Empty);
        ScheduleWorkStart();
    }

    private WorkSchedule LoadSchedule()
    {
        try
        {
            var jsonString = LocalSettingsHelper.Get(ScheduleSettingsKey);
            if (!string.IsNullOrEmpty(jsonString))
            {
                var schedule = JsonSerializer.Deserialize<WorkSchedule>(jsonString);
                if (schedule != null)
                {
                    return schedule;
                }
            }
        }
        catch
        {
            // Fallback to default
        }
        return new WorkSchedule();
    }

    private void SaveSchedule(WorkSchedule schedule)
    {
        try
        {
            var jsonString = JsonSerializer.Serialize(schedule);
            LocalSettingsHelper.Set(ScheduleSettingsKey, jsonString);
        }
        catch
        {
            // Ignore saving errors
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
