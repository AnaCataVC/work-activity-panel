using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WorkActivityPanel.Models;
using WorkActivityPanel.Services.Interfaces;

namespace WorkActivityPanel.ViewModels;

/// <summary>
/// Main view model for the dashboard page.
/// </summary>
public partial class DashboardViewModel : ObservableObject
{
    private static readonly CultureInfo SpanishCulture = new("es-ES");

    private readonly IScheduleService _scheduleService;
    private readonly IAppLauncherService _appLauncherService;
    private readonly IGoogleCalendarService _googleCalendarService;
    private readonly DispatcherTimer _timer;

    [ObservableProperty]
    private bool _isWorkTime;

    [ObservableProperty]
    private string _workStatus = string.Empty;

    [ObservableProperty]
    private string _workStatusIcon = string.Empty;

    [ObservableProperty]
    private bool _isSlackRunning;

    [ObservableProperty]
    private bool _isGranolaRunning;

    [ObservableProperty]
    private string _slackStatusText = "Cerrado";

    [ObservableProperty]
    private string _granolaStatusText = "Cerrado";

    [ObservableProperty]
    private Brush _slackStatusColor = new SolidColorBrush(Microsoft.UI.Colors.IndianRed);

    [ObservableProperty]
    private Brush _granolaStatusColor = new SolidColorBrush(Microsoft.UI.Colors.IndianRed);

    [ObservableProperty]
    private bool _isVacationMode;

    [ObservableProperty]
    private string _currentTime = string.Empty;

    [ObservableProperty]
    private string _currentDate = string.Empty;

    [ObservableProperty]
    private string _workScheduleDisplay = string.Empty;

    // Google Workspace Properties
    [ObservableProperty]
    private bool _isGoogleConnected;

    [ObservableProperty]
    private bool _hasMeetings;

    [ObservableProperty]
    private bool _showUpcomingMeetingBanner;

    [ObservableProperty]
    private string _upcomingMeetingTitle = string.Empty;

    public ObservableCollection<CalendarEvent> TodayMeetings { get; } = new();

    public DashboardViewModel(
        IScheduleService scheduleService,
        IAppLauncherService appLauncherService,
        IGoogleCalendarService googleCalendarService)
    {
        _scheduleService = scheduleService;
        _appLauncherService = appLauncherService;
        _googleCalendarService = googleCalendarService;

        _scheduleService.WorkStarted += OnWorkStarted;
        _scheduleService.WorkEnded += OnWorkEnded;
        _googleCalendarService.UpcomingMeetingDetected += OnUpcomingMeetingDetected;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _timer.Tick += (s, e) => UpdateTime();
        _timer.Start();

        Initialize();
    }

    private async void Initialize()
    {
        UpdateTime();
        UpdateWorkScheduleDisplay();

        IsVacationMode = _scheduleService.CurrentSchedule.IsVacationMode;
        IsWorkTime = _scheduleService.IsWorkTime;

        UpdateStatusDisplay();
        await RefreshStatus();
        await RefreshGoogleDataAsync();
    }

    private void UpdateTime()
    {
        CurrentTime = DateTime.Now.ToString("HH:mm");
        var rawDate = DateTime.Now.ToString("dddd, d 'de' MMMM", SpanishCulture);
        CurrentDate = char.ToUpper(rawDate[0]) + rawDate[1..];
    }

    private void UpdateWorkScheduleDisplay()
    {
        var start = _scheduleService.CurrentSchedule.StartTime;
        var end = _scheduleService.CurrentSchedule.EndTime;
        WorkScheduleDisplay = $"{DateTime.Today.Add(start):hh\\:mm} - {DateTime.Today.Add(end):hh\\:mm}";
    }

    private void UpdateStatusDisplay()
    {
        if (IsVacationMode)
        {
            WorkStatus = "Modo Vacaciones";
            WorkStatusIcon = "\uE709"; // sun
        }
        else if (IsWorkTime)
        {
            WorkStatus = "Horario Laboral Activo";
            WorkStatusIcon = "\uE8BE"; // clock
        }
        else
        {
            WorkStatus = "Fuera de Horario Laboral";
            WorkStatusIcon = "\uE708"; // moon
        }
    }

    partial void OnIsVacationModeChanged(bool value)
    {
        _scheduleService.SetVacationMode(value);
        UpdateStatusDisplay();
    }

    private void OnWorkStarted(object? sender, EventArgs e)
    {
        App.DispatcherQueue.TryEnqueue(async () =>
        {
            IsWorkTime = true;
            UpdateStatusDisplay();
            _appLauncherService.EnsureSlackRunning();
            await RefreshStatus();
            await RefreshGoogleDataAsync();
        });
    }

    private void OnWorkEnded(object? sender, EventArgs e)
    {
        App.DispatcherQueue.TryEnqueue(() =>
        {
            IsWorkTime = false;
            UpdateStatusDisplay();
        });
    }

    private void OnUpcomingMeetingDetected(object? sender, CalendarEvent meeting)
    {
        App.DispatcherQueue.TryEnqueue(() =>
        {
            UpcomingMeetingTitle = $"{meeting.Title} ({meeting.FormattedStartTime})";
            ShowUpcomingMeetingBanner = true;
        });
    }

    [RelayCommand]
    private void DismissUpcomingMeetingBanner()
    {
        ShowUpcomingMeetingBanner = false;
    }

    [RelayCommand]
    private async Task OpenSlack()
    {
        _appLauncherService.LaunchSlack();
        await Task.Delay(1000);
        await RefreshStatus();
    }

    [RelayCommand]
    private async Task OpenGranola()
    {
        _appLauncherService.LaunchGranola();
        await Task.Delay(1000);
        await RefreshStatus();
    }

    [RelayCommand]
    private async Task RefreshStatus()
    {
        IsSlackRunning = _appLauncherService.IsSlackRunning();
        IsGranolaRunning = _appLauncherService.IsGranolaRunning();

        SlackStatusText = IsSlackRunning ? "En ejecución" : "Cerrado";
        GranolaStatusText = IsGranolaRunning ? "En ejecución" : "Cerrado";

        SlackStatusColor = IsSlackRunning 
            ? new SolidColorBrush(Microsoft.UI.Colors.ForestGreen) 
            : new SolidColorBrush(Microsoft.UI.Colors.IndianRed);
        GranolaStatusColor = IsGranolaRunning 
            ? new SolidColorBrush(Microsoft.UI.Colors.ForestGreen) 
            : new SolidColorBrush(Microsoft.UI.Colors.IndianRed);

        await RefreshGoogleDataAsync();
    }

    private async Task RefreshGoogleDataAsync()
    {
        try
        {
            IsGoogleConnected = _googleCalendarService.IsConfigured;

            if (_googleCalendarService.IsConfigured)
            {
                // Fetch Calendar Events via iCal feed
                var events = await _googleCalendarService.GetTodayEventsAsync();
                TodayMeetings.Clear();
                foreach (var ev in events)
                {
                    TodayMeetings.Add(ev);
                }
                HasMeetings = TodayMeetings.Count > 0;
            }
            else
            {
                TodayMeetings.Clear();
                HasMeetings = false;
            }
        }
        catch
        {
            // Ignore background sync errors
        }
    }

    [RelayCommand]
    private void OpenMeetingLink(CalendarEvent? meeting)
    {
        if (meeting != null && !string.IsNullOrWhiteSpace(meeting.MeetingLink))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = meeting.MeetingLink,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }

    [RelayCommand]
    private void ToggleVacationMode()
    {
        IsVacationMode = !IsVacationMode;
    }
}
