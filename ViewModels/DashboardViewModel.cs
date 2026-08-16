using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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
    private bool _showUpcomingMeetingBanner;

    [ObservableProperty]
    private string _upcomingMeetingTitle = string.Empty;

    public ObservableCollection<CalendarEvent> TodayMeetings { get; } = new();

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
    private string _driveSyncFolderDisplay = string.Empty;

    [ObservableProperty]
    private string _driveSyncLastSyncText = "Nunca";

    [ObservableProperty]
    private Brush _driveSyncStatusColor = new SolidColorBrush(Microsoft.UI.Colors.SlateGray);

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
    private bool _hasMultipleGitHubAccounts;

    [ObservableProperty]
    private bool _isSwitchingGitHubAccount;

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

        _gitHubAuthService.ActiveAccountChanged += OnGitHubActiveAccountChanged;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _timer.Tick += (s, e) => UpdateTime();
        _timer.Start();

        Initialize();
    }

    private async void Initialize()
    {
        UpdateTime();
        RefreshAllStatus();
        await RefreshStatus();
        _ = CheckForUpdatesInBackgroundAsync();
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
        if (IsVacationMode) return;

        App.DispatcherQueue.TryEnqueue(() =>
        {
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
        RefreshDriveSyncStatus();
        await RefreshGitHubStatusAsync();
    }

    public void RefreshDriveSyncStatus()
    {
        IsDriveSyncConfigured = _driveSyncService.IsConfigured;
        IsDriveSyncing = _driveSyncService.IsSyncing;

        var settings = _driveSyncService.Settings;
        DriveSyncFolderDisplay = string.IsNullOrWhiteSpace(settings.LocalFolderPath)
            ? "No se ha seleccionado carpeta"
            : settings.LocalFolderPath;

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
    private void CancelDriveSync()
    {
        _driveSyncService.CancelSync();
        DriveSyncDetailText = "Cancelando...";
    }

    [RelayCommand]
    private void OpenDriveFolder()
    {
        var path = _driveSyncService.Settings.LocalFolderPath;
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch { }
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
            }
            else
            {
                AlternativeGitHubAccount = string.Empty;
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
                GitHubStatusText = $"Cuenta activa: {ActiveGitHubAccount}";
                GitHubStatusColor = new SolidColorBrush(Microsoft.UI.Colors.ForestGreen);
            }
        }
        catch (Exception ex)
        {
            GitHubStatusText = $"Error: {ex.Message}";
            GitHubStatusColor = new SolidColorBrush(Microsoft.UI.Colors.IndianRed);
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

