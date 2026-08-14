using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkActivityPanel.Helpers;
using WorkActivityPanel.Services.Interfaces;

namespace WorkActivityPanel.ViewModels;

/// <summary>
/// View model for the settings page.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly IScheduleService _scheduleService;
    private readonly IGoogleCalendarService _calendarService;

    [ObservableProperty]
    private TimeSpan _workStartTime;

    [ObservableProperty]
    private TimeSpan _workEndTime;

    [ObservableProperty]
    private bool _isMonday;

    [ObservableProperty]
    private bool _isTuesday;

    [ObservableProperty]
    private bool _isWednesday;

    [ObservableProperty]
    private bool _isThursday;

    [ObservableProperty]
    private bool _isFriday;

    [ObservableProperty]
    private bool _isSaturday;

    [ObservableProperty]
    private bool _isSunday;

    [ObservableProperty]
    private bool _isAutostartEnabled;

    [ObservableProperty]
    private bool _isVacationMode;

    // Calendar iCal Configuration
    [ObservableProperty]
    private string _calendarICalUrl = string.Empty;

    [ObservableProperty]
    private bool _isCalendarConnected;

    [ObservableProperty]
    private string _calendarConnectionStatus = "No configurado";

    [ObservableProperty]
    private bool _showSaveConfirmation;

    [ObservableProperty]
    private string _saveConfirmationMessage = string.Empty;

    public SettingsViewModel(IScheduleService scheduleService, IGoogleCalendarService calendarService)
    {
        _scheduleService = scheduleService;
        _calendarService = calendarService;
        LoadSettings();
    }

    private void LoadSettings()
    {
        var schedule = _scheduleService.CurrentSchedule;
        WorkStartTime = schedule.StartTime;
        WorkEndTime = schedule.EndTime;
        IsVacationMode = schedule.IsVacationMode;

        var workDays = schedule.WorkDays ?? Enumerable.Empty<DayOfWeek>();
        IsMonday = workDays.Contains(DayOfWeek.Monday);
        IsTuesday = workDays.Contains(DayOfWeek.Tuesday);
        IsWednesday = workDays.Contains(DayOfWeek.Wednesday);
        IsThursday = workDays.Contains(DayOfWeek.Thursday);
        IsFriday = workDays.Contains(DayOfWeek.Friday);
        IsSaturday = workDays.Contains(DayOfWeek.Saturday);
        IsSunday = workDays.Contains(DayOfWeek.Sunday);

        IsAutostartEnabled = AutostartHelper.IsAutostartEnabled();

        // Load saved iCal URL
        CalendarICalUrl = _calendarService.ICalUrl ?? string.Empty;
        UpdateCalendarStatus();
    }

    private void UpdateCalendarStatus()
    {
        IsCalendarConnected = _calendarService.IsConfigured;
        CalendarConnectionStatus = IsCalendarConnected
            ? "Conectado y sincronizado"
            : "No configurado";
    }

    [RelayCommand]
    private async Task SaveCalendarUrl()
    {
        if (string.IsNullOrWhiteSpace(CalendarICalUrl))
        {
            CalendarConnectionStatus = "Por favor ingresa la URL iCal secreta.";
            return;
        }

        CalendarConnectionStatus = "Verificando enlace de calendario...";
        var success = await _calendarService.SetICalCredentialsAsync(CalendarICalUrl.Trim());

        if (success)
        {
            var events = await _calendarService.GetTodayEventsAsync();
            IsCalendarConnected = true;
            CalendarConnectionStatus = $"Sincronizado correctamente ({events.Count} eventos hoy)";
        }
        else
        {
            IsCalendarConnected = false;
            CalendarConnectionStatus = "Error: no se pudo leer el calendario. Verifica que el enlace sea el secreto (.ics).";
        }
    }

    [RelayCommand]
    private async Task ClearCalendarUrl()
    {
        await _calendarService.ClearICalCredentialsAsync();
        CalendarICalUrl = string.Empty;
        UpdateCalendarStatus();
    }

    [RelayCommand]
    private void SaveSettings()
    {
        var workDays = new List<DayOfWeek>();
        if (IsMonday) workDays.Add(DayOfWeek.Monday);
        if (IsTuesday) workDays.Add(DayOfWeek.Tuesday);
        if (IsWednesday) workDays.Add(DayOfWeek.Wednesday);
        if (IsThursday) workDays.Add(DayOfWeek.Thursday);
        if (IsFriday) workDays.Add(DayOfWeek.Friday);
        if (IsSaturday) workDays.Add(DayOfWeek.Saturday);
        if (IsSunday) workDays.Add(DayOfWeek.Sunday);

        var schedule = _scheduleService.CurrentSchedule;
        schedule.StartTime = WorkStartTime;
        schedule.EndTime = WorkEndTime;
        schedule.WorkDays = workDays;
        schedule.IsVacationMode = IsVacationMode;

        _scheduleService.UpdateSchedule(schedule);
        _scheduleService.SetVacationMode(IsVacationMode);

        AutostartHelper.SetAutostart(IsAutostartEnabled);

        SaveConfirmationMessage = "¡Horario y preferencias guardados correctamente!";
        ShowSaveConfirmation = true;
    }
}
