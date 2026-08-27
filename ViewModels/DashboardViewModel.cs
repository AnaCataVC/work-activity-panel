using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
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
    private readonly IDriveSyncService _driveSyncService;
    private readonly IGitHubAuthService _gitHubAuthService;
    private readonly IUpdateService _updateService;
    private readonly DispatcherTimer _timer;

    // Application Update Properties
    [ObservableProperty]
    private bool _showUpdateBanner;

    [ObservableProperty]
    private string _updateBannerMessage = string.Empty;

    [ObservableProperty]
    private bool _isDownloadingUpdate;

    [ObservableProperty]
    private double _updateDownloadProgress;

    [ObservableProperty]
    private string _updateDownloadStatusText = string.Empty;

    private UpdateInfo? _latestUpdateInfo;

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
    private string _noMeetingsMessage = "No tienes reuniones programadas para hoy.";

    [ObservableProperty]
    private bool _showUpcomingMeetingBanner;

    [ObservableProperty]
    private string _upcomingMeetingTitle = string.Empty;

    private string? _currentAlertMeetingId;
    private string? _userDismissedMeetingId;

    public ObservableCollection<CalendarEvent> TodayMeetings { get; } = new();
    private readonly List<CalendarEvent> _allTodayEvents = new();

    // Google Drive Backup / Sync Properties
    [ObservableProperty]
    private bool _isDriveSyncConfigured;

    [ObservableProperty]
    private bool _isDriveSyncing;

    [ObservableProperty]
    private string _driveSyncStatusText = "No configurado";

    [ObservableProperty]
    private string _driveSyncDetailText = string.Empty;

    [ObservableProperty]
    private double _driveSyncProgress;

    [ObservableProperty]
    private string _driveSyncFoldersDisplay = string.Empty;

    [ObservableProperty]
    private string _driveSyncLastSyncText = "Nunca";

    [ObservableProperty]
    private Brush _driveSyncStatusColor = new SolidColorBrush(Microsoft.UI.Colors.SlateGray);

    [ObservableProperty]
    private bool _hasSyncErrors;

    [ObservableProperty]
    private int _syncErrorsCount;

    [ObservableProperty]
    private string _syncErrorsButtonText = string.Empty;

    public ObservableCollection<SyncErrorItem> SyncErrorsList { get; } = new();


    // GitHub CLI & Account Switcher Properties
    [ObservableProperty]
    private bool _isGitHubInstalled;

    [ObservableProperty]
    private bool _isGitHubAuthenticated;

    [ObservableProperty]
    private string _activeGitHubAccount = string.Empty;

    [ObservableProperty]
    private string _selectedGitHubAccount = string.Empty;

    [ObservableProperty]
    private string _alternativeGitHubAccount = string.Empty;

    [ObservableProperty]
    private string _quickSwitchTargetRole = string.Empty;

    [ObservableProperty]
    private bool _hasMultipleGitHubAccounts;

    [ObservableProperty]
    private bool _isSwitchingGitHubAccount;

    [ObservableProperty]
    private bool _isActiveAccountWorkAccount;

    [ObservableProperty]
    private bool _isActiveAccountPersonalAccount;

    [ObservableProperty]
    private bool _isWorkAccountConfigured;

    [ObservableProperty]
    private string _gitHubAccountRoleBadge = "Activa";

    [ObservableProperty]
    private string _gitHubStatusText = "Consultando...";

    [ObservableProperty]
    private Brush _gitHubStatusColor = new SolidColorBrush(Microsoft.UI.Colors.SlateGray);

    public ObservableCollection<string> AvailableGitHubAccounts { get; } = new();

    public DashboardViewModel(
        IScheduleService scheduleService,
        IAppLauncherService appLauncherService,
        IGoogleCalendarService googleCalendarService,
        IDriveSyncService driveSyncService,
        IGitHubAuthService gitHubAuthService,
        IUpdateService updateService)
    {
        _scheduleService = scheduleService;
        _appLauncherService = appLauncherService;
        _googleCalendarService = googleCalendarService;
        _driveSyncService = driveSyncService;
        _gitHubAuthService = gitHubAuthService;
        _updateService = updateService;

        _scheduleService.WorkStarted += OnWorkStarted;
        _scheduleService.WorkEnded += OnWorkEnded;
        _scheduleService.VacationModeChanged += OnVacationModeChanged;
        _scheduleService.ScheduleChanged += OnScheduleChanged;
        _googleCalendarService.UpcomingMeetingDetected += OnUpcomingMeetingDetected;

        _driveSyncService.SyncProgressChanged += OnDriveSyncProgressChanged;
        _driveSyncService.SyncCompleted += OnDriveSyncCompleted;
        _driveSyncService.SettingsChanged += OnDriveSettingsChanged;
        _driveSyncService.SyncErrorsChanged += OnDriveSyncErrorsChanged;

        _gitHubAuthService.ActiveAccountChanged += OnGitHubActiveAccountChanged;
        _gitHubAuthService.SettingsChanged += OnGitHubSettingsChanged;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _timer.Tick += (s, e) => UpdateTime();
        _timer.Start();

        Initialize();
    }

    private async void Initialize()
    {
        UpdateTime();
        RefreshAllStatus();
        if (IsWorkTime && !IsVacationMode)
        {
            await EnsureGitHubAccountForScheduleAsync(isWorkStart: true);
        }
        await RefreshStatus();
        _ = CheckForUpdatesInBackgroundAsync();
    }

    private void OnGitHubSettingsChanged(object? sender, EventArgs e)
    {
        App.DispatcherQueue.TryEnqueue(async () =>
        {
            await RefreshGitHubStatusAsync();
        });
    }

    private void OnDriveSettingsChanged(object? sender, EventArgs e)
    {
        App.DispatcherQueue.TryEnqueue(RefreshDriveSyncStatus);
    }

    public void RefreshAllStatus()
    {
        RefreshScheduleAndStatus();
        RefreshDriveSyncStatus();
        _ = RefreshGoogleDataAsync();
        _ = RefreshGitHubStatusAsync();
    }

    public void RefreshScheduleAndStatus()
    {
        IsVacationMode = _scheduleService.IsVacationMode;
        IsWorkTime = _scheduleService.IsWorkTime;
        UpdateWorkScheduleDisplay();
        UpdateStatusDisplay();
    }

    private void UpdateTime()
    {
        CurrentTime = DateTime.Now.ToString("HH:mm");
        var rawDate = DateTime.Now.ToString("dddd, d 'de' MMMM", SpanishCulture);
        CurrentDate = char.ToUpper(rawDate[0]) + rawDate[1..];
        UpdateTodayMeetingsDisplay();
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
        if (_scheduleService.IsVacationMode != value)
        {
            _scheduleService.SetVacationMode(value);
        }
        UpdateStatusDisplay();
    }

    private void OnVacationModeChanged(object? sender, bool enabled)
    {
        App.DispatcherQueue.TryEnqueue(() =>
        {
            IsVacationMode = enabled;
            UpdateStatusDisplay();
        });
    }

    private void OnScheduleChanged(object? sender, EventArgs e)
    {
        App.DispatcherQueue.TryEnqueue(() =>
        {
            RefreshScheduleAndStatus();
        });
    }

    private void OnWorkStarted(object? sender, EventArgs e)
    {
        App.DispatcherQueue.TryEnqueue(async () =>
        {
            IsWorkTime = true;
            UpdateStatusDisplay();
            _appLauncherService.EnsureSlackRunning();
            await EnsureGitHubAccountForScheduleAsync(isWorkStart: true);
            await RefreshStatus();
            await RefreshGoogleDataAsync();
        });
    }

    private void OnWorkEnded(object? sender, EventArgs e)
    {
        App.DispatcherQueue.TryEnqueue(async () =>
        {
            IsWorkTime = false;
            UpdateStatusDisplay();
            await EnsureGitHubAccountForScheduleAsync(isWorkStart: false);
            await RefreshStatus();
        });
    }

    private void OnUpcomingMeetingDetected(object? sender, CalendarEvent meeting)
    {
        if (IsVacationMode) return;

        App.DispatcherQueue.TryEnqueue(() =>
        {
            if (_userDismissedMeetingId == meeting.Id)
            {
                return;
            }

            _currentAlertMeetingId = meeting.Id;
            UpcomingMeetingTitle = $"{meeting.Title} ({meeting.FormattedStartTime})";
            ShowUpcomingMeetingBanner = true;
        });
    }

    private void OnDriveSyncProgressChanged(object? sender, SyncProgressReport report)
    {
        App.DispatcherQueue.TryEnqueue(() =>
        {
            IsDriveSyncing = true;
            DriveSyncProgress = report.Percentage;
            DriveSyncDetailText = report.StatusMessage;
        });
    }

    private void OnDriveSyncCompleted(object? sender, SyncResultSummary summary)
    {
        App.DispatcherQueue.TryEnqueue(() =>
        {
            IsDriveSyncing = false;
            RefreshDriveSyncStatus();
            DriveSyncDetailText = summary.Message;
        });
    }

    private void OnDriveSyncErrorsChanged(object? sender, IReadOnlyList<SyncErrorItem> errors)
    {
        App.DispatcherQueue.TryEnqueue(() =>
        {
            UpdateSyncErrorsDisplay(errors);
        });
    }

    private void UpdateSyncErrorsDisplay(IReadOnlyList<SyncErrorItem> errors)
    {
        SyncErrorsList.Clear();
        foreach (var err in errors)
        {
            SyncErrorsList.Add(err);
        }
        SyncErrorsCount = errors.Count;
        HasSyncErrors = errors.Count > 0;
        SyncErrorsButtonText = $"Ver archivos no sincronizados ({errors.Count})";
    }

    [RelayCommand]
    private void DismissUpcomingMeetingBanner()
    {
        ShowUpcomingMeetingBanner = false;
        _userDismissedMeetingId = _currentAlertMeetingId;
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
        RefreshDriveSyncStatus();
        await RefreshGitHubStatusAsync();
    }

    public void RefreshDriveSyncStatus()
    {
        IsDriveSyncConfigured = _driveSyncService.IsConfigured;
        IsDriveSyncing = _driveSyncService.IsSyncing;
        UpdateSyncErrorsDisplay(_driveSyncService.LastSyncErrors);

        var settings = _driveSyncService.Settings;
        var driveFolders = settings.Sources
            .Where(s => !string.IsNullOrWhiteSpace(s.LocalFolderPath))
            .Select(s => s.EffectiveDestinationPrefix)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        DriveSyncFoldersDisplay = driveFolders.Count == 0
            ? "Sin carpetas configuradas"
            : string.Join(" · ", driveFolders);

        DriveSyncLastSyncText = settings.LastSyncTime.HasValue
            ? settings.LastSyncTime.Value.ToString("dd/MM HH:mm")
            : "Nunca";

        if (!IsDriveSyncConfigured)
        {
            DriveSyncStatusText = "Sin configurar";
            DriveSyncStatusColor = new SolidColorBrush(Microsoft.UI.Colors.IndianRed);
        }
        else if (IsDriveSyncing)
        {
            DriveSyncStatusText = "Sincronizando...";
            DriveSyncStatusColor = new SolidColorBrush(Microsoft.UI.Colors.DarkOrange);
        }
        else if (HasSyncErrors)
        {
            DriveSyncStatusText = $"Con errores ({SyncErrorsCount})";
            DriveSyncStatusColor = new SolidColorBrush(Microsoft.UI.Colors.IndianRed);
        }
        else
        {
            DriveSyncStatusText = "Al día";
            DriveSyncStatusColor = new SolidColorBrush(Microsoft.UI.Colors.ForestGreen);
        }
    }

    [RelayCommand]
    private async Task SyncDriveNow()
    {
        if (!_driveSyncService.IsConfigured)
        {
            DriveSyncDetailText = "Configura la URL y la carpeta en Ajustes antes de sincronizar.";
            return;
        }

        IsDriveSyncing = true;
        DriveSyncProgress = 0;
        DriveSyncDetailText = "Iniciando sincronización...";
        RefreshDriveSyncStatus();

        await _driveSyncService.RunSyncAsync();
    }

    [RelayCommand]
    private async Task ForceSyncDrive()
    {
        if (!_driveSyncService.IsConfigured)
        {
            DriveSyncDetailText = "Configura la URL y la carpeta en Ajustes antes de sincronizar.";
            return;
        }

        IsDriveSyncing = true;
        DriveSyncProgress = 0;
        DriveSyncDetailText = "Iniciando sincronización completa forzada (sin omitir archivos)...";
        RefreshDriveSyncStatus();

        await _driveSyncService.RunSyncAsync(forceFullSync: true);
    }

    [RelayCommand]
    private async Task RetrySyncErrors()
    {
        if (!_driveSyncService.IsConfigured) return;

        IsDriveSyncing = true;
        DriveSyncProgress = 0;
        DriveSyncDetailText = "Iniciando reintento de archivos no sincronizados...";
        RefreshDriveSyncStatus();

        await _driveSyncService.RetryFailedFilesAsync();
    }

    [RelayCommand]
    private void ClearAllSyncErrors()
    {
        _driveSyncService.ClearSyncErrors();
        RefreshDriveSyncStatus();
    }

    [RelayCommand]
    private void CopySyncErrorsReport()
    {
        if (SyncErrorsList.Count == 0) return;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Reporte de Errores de Sincronización - Work Activity Panel");
        sb.AppendLine($"Fecha: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Total de archivos no sincronizados: {SyncErrorsList.Count}");
        sb.AppendLine(new string('-', 60));

        foreach (var err in SyncErrorsList)
        {
            sb.AppendLine($"• Archivo: {err.FileName}");
            sb.AppendLine($"  Ruta local: {err.FilePath}");
            sb.AppendLine($"  Destino: {err.RelativePath}");
            sb.AppendLine($"  Categoría: {err.ErrorCategory}");
            sb.AppendLine($"  Detalle: {err.ErrorMessage}");
            sb.AppendLine($"  Hora: {err.Timestamp:HH:mm:ss}");
            sb.AppendLine();
        }

        try
        {
            var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.SetText(sb.ToString());
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
            DriveSyncDetailText = "Reporte de errores copiado al portapapeles.";
        }
        catch { }
    }

    [RelayCommand]
    private void CancelDriveSync()
    {
        _driveSyncService.CancelSync();
        DriveSyncDetailText = "Cancelando...";
    }

    [RelayCommand]
    private void OpenDriveFolder()
    {
        var url = _driveSyncService.Settings.DriveFolderUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            url = "https://drive.google.com/drive/my-drive";
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch { }
    }


    private void UpdateTodayMeetingsDisplay()
    {
        if (!_googleCalendarService.IsConfigured || IsVacationMode)
        {
            TodayMeetings.Clear();
            HasMeetings = false;
            NoMeetingsMessage = IsVacationMode
                ? "El modo vacaciones está activo."
                : "Google Calendar no está configurado en Ajustes.";
            ShowUpcomingMeetingBanner = false;
            _currentAlertMeetingId = null;
            return;
        }

        var now = DateTime.Now;
        // Filter out past events (keep active/in-progress and upcoming events)
        var activeAndUpcoming = _allTodayEvents.Where(ev => ev.EndTime > now).ToList();

        bool changed = TodayMeetings.Count != activeAndUpcoming.Count;
        if (!changed)
        {
            for (int i = 0; i < activeAndUpcoming.Count; i++)
            {
                if (TodayMeetings[i].Id != activeAndUpcoming[i].Id)
                {
                    changed = true;
                    break;
                }
            }
        }

        if (changed)
        {
            TodayMeetings.Clear();
            foreach (var ev in activeAndUpcoming)
            {
                TodayMeetings.Add(ev);
            }
        }

        HasMeetings = TodayMeetings.Count > 0;

        if (!HasMeetings)
        {
            NoMeetingsMessage = _allTodayEvents.Count > 0
                ? "No tienes más reuniones pendientes por hoy."
                : "No tienes reuniones programadas para hoy.";
        }

        // Auto-dismiss the 5-minute pre-meeting banner if the meeting has started or concluded,
        // or if no qualifying meeting is currently in the pre-start window.
        if (ShowUpcomingMeetingBanner)
        {
            if (!string.IsNullOrEmpty(_currentAlertMeetingId))
            {
                var alertMeeting = _allTodayEvents.FirstOrDefault(ev => ev.Id == _currentAlertMeetingId);
                // If meeting not found, or now >= StartTime (meeting started/past), or now < StartTime - 5min
                if (alertMeeting == null || now >= alertMeeting.StartTime || now < alertMeeting.StartTime.AddMinutes(-5))
                {
                    ShowUpcomingMeetingBanner = false;
                    _currentAlertMeetingId = null;
                }
            }
            else
            {
                var anyInWindow = _allTodayEvents.Any(ev =>
                    ev.OpensGranola &&
                    !ev.IsAllDay &&
                    now >= ev.StartTime.AddMinutes(-5) &&
                    now < ev.StartTime);

                if (!anyInWindow)
                {
                    ShowUpcomingMeetingBanner = false;
                }
            }
        }
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
                _allTodayEvents.Clear();
                _allTodayEvents.AddRange(events);
                UpdateTodayMeetingsDisplay();
            }
            else
            {
                _allTodayEvents.Clear();
                UpdateTodayMeetingsDisplay();
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

    private void OnGitHubActiveAccountChanged(object? sender, string? newAccount)
    {
        App.DispatcherQueue.TryEnqueue(async () =>
        {
            await RefreshGitHubStatusAsync();
        });
    }

    [RelayCommand]
    public async Task RefreshGitHubStatusAsync()
    {
        try
        {
            var info = await _gitHubAuthService.GetAccountsStatusAsync();

            IsGitHubInstalled = info.IsGhInstalled;
            IsGitHubAuthenticated = info.IsAuthenticated;
            ActiveGitHubAccount = info.ActiveAccount ?? string.Empty;
            HasMultipleGitHubAccounts = info.HasMultipleAccounts;

            IsActiveAccountWorkAccount = info.IsActiveAccountWorkAccount;
            IsActiveAccountPersonalAccount = info.IsActiveAccountPersonalAccount;
            IsWorkAccountConfigured = !string.IsNullOrEmpty(info.WorkAccount);

            if (IsActiveAccountWorkAccount)
            {
                GitHubAccountRoleBadge = "Cuenta Laboral";
            }
            else if (IsActiveAccountPersonalAccount)
            {
                GitHubAccountRoleBadge = "Cuenta Personal";
            }
            else
            {
                GitHubAccountRoleBadge = "Activa";
            }

            AvailableGitHubAccounts.Clear();
            foreach (var account in info.AvailableAccounts)
            {
                AvailableGitHubAccounts.Add(account);
            }

            if (!string.IsNullOrEmpty(ActiveGitHubAccount) && AvailableGitHubAccounts.Contains(ActiveGitHubAccount))
            {
                SelectedGitHubAccount = ActiveGitHubAccount;
            }
            else if (AvailableGitHubAccounts.Count > 0)
            {
                SelectedGitHubAccount = AvailableGitHubAccounts[0];
            }

            // Determine alternative account if there are 2 accounts
            if (AvailableGitHubAccounts.Count == 2 && !string.IsNullOrEmpty(ActiveGitHubAccount))
            {
                AlternativeGitHubAccount = AvailableGitHubAccounts.FirstOrDefault(a => !string.Equals(a, ActiveGitHubAccount, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
                if (!string.IsNullOrEmpty(AlternativeGitHubAccount))
                {
                    if (string.Equals(AlternativeGitHubAccount, info.WorkAccount, StringComparison.OrdinalIgnoreCase))
                    {
                        QuickSwitchTargetRole = " (Laboral)";
                    }
                    else if (string.Equals(AlternativeGitHubAccount, info.PersonalAccount, StringComparison.OrdinalIgnoreCase))
                    {
                        QuickSwitchTargetRole = " (Personal)";
                    }
                    else
                    {
                        QuickSwitchTargetRole = string.Empty;
                    }
                }
            }
            else
            {
                AlternativeGitHubAccount = string.Empty;
                QuickSwitchTargetRole = string.Empty;
            }

            if (!IsGitHubInstalled)
            {
                GitHubStatusText = "GitHub CLI no detectado";
                GitHubStatusColor = new SolidColorBrush(Microsoft.UI.Colors.IndianRed);
            }
            else if (!IsGitHubAuthenticated)
            {
                GitHubStatusText = "Sin cuentas autenticadas";
                GitHubStatusColor = new SolidColorBrush(Microsoft.UI.Colors.IndianRed);
            }
            else
            {
                string roleSuffix = IsActiveAccountWorkAccount ? " (Laboral)" : (IsActiveAccountPersonalAccount ? " (Personal)" : string.Empty);
                GitHubStatusText = $"Cuenta activa: {ActiveGitHubAccount}{roleSuffix}";
                GitHubStatusColor = new SolidColorBrush(Microsoft.UI.Colors.ForestGreen);
            }
        }
        catch (Exception ex)
        {
            GitHubStatusText = $"Error: {ex.Message}";
            GitHubStatusColor = new SolidColorBrush(Microsoft.UI.Colors.IndianRed);
        }
    }

    private async Task EnsureGitHubAccountForScheduleAsync(bool isWorkStart)
    {
        if (IsVacationMode) return;

        var settings = _gitHubAuthService.Settings;
        if (isWorkStart && settings.AutoSwitchOnWorkStart && !string.IsNullOrWhiteSpace(settings.WorkAccount))
        {
            var info = await _gitHubAuthService.GetAccountsStatusAsync();
            if (info.IsGhInstalled && !string.Equals(info.ActiveAccount, settings.WorkAccount.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                await SwitchGitHubAccount(settings.WorkAccount.Trim());
            }
        }
        else if (!isWorkStart && settings.AutoSwitchOnWorkEnd && !string.IsNullOrWhiteSpace(settings.PersonalAccount))
        {
            var info = await _gitHubAuthService.GetAccountsStatusAsync();
            if (info.IsGhInstalled && !string.Equals(info.ActiveAccount, settings.PersonalAccount.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                await SwitchGitHubAccount(settings.PersonalAccount.Trim());
            }
        }
    }

    [RelayCommand]
    private async Task SwitchGitHubAccount(string? targetAccount = null)
    {
        string target = !string.IsNullOrWhiteSpace(targetAccount)
            ? targetAccount.Trim()
            : SelectedGitHubAccount;

        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        if (string.Equals(target, ActiveGitHubAccount, StringComparison.OrdinalIgnoreCase))
        {
            GitHubStatusText = $"{target} ya es la cuenta activa.";
            return;
        }

        IsSwitchingGitHubAccount = true;
        GitHubStatusText = $"Cambiando cuenta a {target}...";
        GitHubStatusColor = new SolidColorBrush(Microsoft.UI.Colors.DarkOrange);

        try
        {
            var result = await _gitHubAuthService.SwitchAccountAsync(target);
            if (result.Success)
            {
                await RefreshGitHubStatusAsync();
                GitHubStatusText = $"✓ Cuenta cambiada a {target}";
                GitHubStatusColor = new SolidColorBrush(Microsoft.UI.Colors.ForestGreen);
            }
            else
            {
                GitHubStatusText = result.Message;
                GitHubStatusColor = new SolidColorBrush(Microsoft.UI.Colors.IndianRed);
            }
        }
        finally
        {
            IsSwitchingGitHubAccount = false;
        }
    }

    [RelayCommand]
    private async Task QuickSwitchGitHubAccount()
    {
        if (!string.IsNullOrEmpty(AlternativeGitHubAccount))
        {
            await SwitchGitHubAccount(AlternativeGitHubAccount);
        }
    }

    [RelayCommand]
    private void OpenGitHubProfile()
    {
        string url = !string.IsNullOrEmpty(ActiveGitHubAccount)
            ? $"https://github.com/{ActiveGitHubAccount}"
            : "https://github.com";

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch { }
    }

    private async Task CheckForUpdatesInBackgroundAsync()
    {
        try
        {
            var update = await _updateService.CheckForUpdatesAsync();
            if (update.IsUpdateAvailable)
            {
                _latestUpdateInfo = update;
                App.DispatcherQueue.TryEnqueue(() =>
                {
                    UpdateBannerMessage = $"Work Activity Panel v{update.LatestVersion} está disponible (versión actual v{update.CurrentVersion}).";
                    ShowUpdateBanner = true;
                });
            }
        }
        catch { }
    }

    [RelayCommand]
    private async Task DownloadAndInstallUpdateAsync()
    {
        if (_latestUpdateInfo?.DownloadUrl == null)
        {
            if (!string.IsNullOrEmpty(_latestUpdateInfo?.ReleaseHtmlUrl))
            {
                OpenReleaseNotes();
            }
            return;
        }

        IsDownloadingUpdate = true;
        UpdateDownloadProgress = 0;
        UpdateDownloadStatusText = "Descargando actualización...";

        try
        {
            var progress = new Progress<double>(p =>
            {
                App.DispatcherQueue.TryEnqueue(() =>
                {
                    UpdateDownloadProgress = p;
                    UpdateDownloadStatusText = $"Descargando actualización ({p:F0}%)...";
                });
            });

            var installerPath = await _updateService.DownloadUpdateAsync(
                _latestUpdateInfo.DownloadUrl,
                _latestUpdateInfo.InstallerFileName,
                progress);

            UpdateDownloadStatusText = "Iniciando instalador...";
            _updateService.LaunchInstaller(installerPath);
        }
        catch (Exception ex)
        {
            UpdateDownloadStatusText = $"Error al descargar: {ex.Message}";
        }
        finally
        {
            IsDownloadingUpdate = false;
        }
    }

    [RelayCommand]
    private void OpenReleaseNotes()
    {
        if (!string.IsNullOrEmpty(_latestUpdateInfo?.ReleaseHtmlUrl))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _latestUpdateInfo.ReleaseHtmlUrl,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }

    [RelayCommand]
    private void DismissUpdateBanner()
    {
        ShowUpdateBanner = false;
    }
}

