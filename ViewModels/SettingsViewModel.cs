using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkActivityPanel.Helpers;
using WorkActivityPanel.Models;
using WorkActivityPanel.Services.Interfaces;

namespace WorkActivityPanel.ViewModels;

/// <summary>
/// View model for the settings page.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly IScheduleService _scheduleService;
    private readonly IGoogleCalendarService _calendarService;
    private readonly IDriveSyncService _driveSyncService;
    private readonly IGitHubAuthService _gitHubAuthService;
    private readonly IUpdateService _updateService;

    // About & Update Configuration
    public string AppVersionText => $"Versión {_updateService.CurrentAppVersion}";

    [ObservableProperty]
    private string _updateStatusText = "Presiona 'Buscar Actualizaciones' para verificar.";

    [ObservableProperty]
    private bool _isCheckingUpdates;

    [ObservableProperty]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    private bool _isDownloadingUpdate;

    [ObservableProperty]
    private double _downloadProgress;

    private UpdateInfo? _latestUpdateInfo;

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

    // GitHub CLI & Accounts Configuration
    [ObservableProperty]
    private string _gitHubWorkAccount = string.Empty;

    [ObservableProperty]
    private string _gitHubPersonalAccount = string.Empty;

    [ObservableProperty]
    private bool _gitHubAutoSwitchOnWorkStart = true;

    [ObservableProperty]
    private bool _gitHubAutoSwitchOnWorkEnd = true;

    [ObservableProperty]
    private bool _isGitHubInstalled;

    [ObservableProperty]
    private string _gitHubSettingsStatus = string.Empty;

    public ObservableCollection<string> AvailableGitHubAccounts { get; } = new();

    // Calendar iCal Configuration
    [ObservableProperty]
    private string _calendarICalUrl = string.Empty;

    [ObservableProperty]
    private string _calendarExcludedKeywords = CalendarFilterSettings.DefaultExcludedKeywords;

    [ObservableProperty]
    private bool _calendarIgnoreAllDayEvents = true;

    [ObservableProperty]
    private bool _calendarRequireMeetingLink;

    [ObservableProperty]
    private bool _isCalendarConnected;

    [ObservableProperty]
    private string _calendarConnectionStatus = "No configurado";

    // Google Drive Sync Configuration
    [ObservableProperty]
    private string _driveWebAppUrl = string.Empty;

    [ObservableProperty]
    private string _driveAuthToken = string.Empty;

    [ObservableProperty]
    private string _driveFolderUrl = string.Empty;

    [ObservableProperty]
    private string _driveIncludedExtensions = string.Empty;

    [ObservableProperty]
    private string _driveExcludedExtensions = ".tmp, .log, .exe, .bak, .zip";

    [ObservableProperty]
    private string _driveExcludedFolders = "node_modules, .git, bin, obj, .vs, temp";

    [ObservableProperty]
    private long _driveMaxFileSizeMb = 50;

    [ObservableProperty]
    private bool _driveOnlyModifiedOrNew = true;

    [ObservableProperty]
    private bool _driveAutoSyncOnWorkEnd = true;

    public ObservableCollection<SyncSource> DriveSyncSources { get; } = new();

    [ObservableProperty]
    private bool _isDriveConnected;

    [ObservableProperty]
    private string _driveConnectionStatus = "No configurado";

    [ObservableProperty]
    private bool _isDriveTesting;

    // Save Confirmation
    [ObservableProperty]
    private bool _showSaveConfirmation;

    [ObservableProperty]
    private string _saveConfirmationMessage = string.Empty;

    public SettingsViewModel(
        IScheduleService scheduleService,
        IGoogleCalendarService calendarService,
        IDriveSyncService driveSyncService,
        IGitHubAuthService gitHubAuthService,
        IUpdateService updateService)
    {
        _scheduleService = scheduleService;
        _calendarService = calendarService;
        _driveSyncService = driveSyncService;
        _gitHubAuthService = gitHubAuthService;
        _updateService = updateService;

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

        // Load GitHub Account Settings
        var ghSettings = _gitHubAuthService.Settings;
        GitHubWorkAccount = ghSettings.WorkAccount ?? string.Empty;
        GitHubPersonalAccount = ghSettings.PersonalAccount ?? string.Empty;
        GitHubAutoSwitchOnWorkStart = ghSettings.AutoSwitchOnWorkStart;
        GitHubAutoSwitchOnWorkEnd = ghSettings.AutoSwitchOnWorkEnd;
        _ = LoadGitHubAccountsAsync();

        // Load saved iCal URL & Filter settings
        CalendarICalUrl = _calendarService.ICalUrl ?? string.Empty;
        var filterSettings = _calendarService.FilterSettings;
        CalendarExcludedKeywords = filterSettings.ExcludedKeywords;
        CalendarIgnoreAllDayEvents = filterSettings.IgnoreAllDayEvents;
        CalendarRequireMeetingLink = filterSettings.RequireMeetingLink;
        UpdateCalendarStatus();

        // Load Drive Sync settings
        var driveSettings = _driveSyncService.Settings;
        DriveWebAppUrl = driveSettings.WebAppUrl ?? string.Empty;
        DriveAuthToken = driveSettings.AuthToken ?? string.Empty;
        DriveFolderUrl = driveSettings.DriveFolderUrl ?? string.Empty;
        DriveIncludedExtensions = driveSettings.IncludedExtensions ?? string.Empty;
        DriveExcludedExtensions = !string.IsNullOrEmpty(driveSettings.ExcludedExtensions) ? driveSettings.ExcludedExtensions : ".tmp, .log, .exe, .bak, .zip";
        DriveExcludedFolders = !string.IsNullOrEmpty(driveSettings.ExcludedFolders) ? driveSettings.ExcludedFolders : "node_modules, .git, bin, obj, .vs, temp";
        DriveMaxFileSizeMb = driveSettings.MaxFileSizeMb > 0 ? driveSettings.MaxFileSizeMb : 20;
        DriveOnlyModifiedOrNew = driveSettings.OnlyModifiedOrNew;
        DriveAutoSyncOnWorkEnd = driveSettings.AutoSyncOnWorkEnd;

        DriveSyncSources.Clear();
        if (driveSettings.Sources != null)
        {
            foreach (var s in driveSettings.Sources)
            {
                DriveSyncSources.Add(s);
            }
        }

        UpdateDriveStatus();
    }

    private void UpdateCalendarStatus()
    {
        IsCalendarConnected = _calendarService.IsConfigured;
        CalendarConnectionStatus = IsCalendarConnected
            ? "Conectado y sincronizado"
            : "No configurado";
    }

    private void UpdateDriveStatus()
    {
        IsDriveConnected = _driveSyncService.IsConfigured;
        DriveConnectionStatus = IsDriveConnected
            ? "Configurado y listo"
            : "Falta configurar la URL o agregar carpetas";
    }

    [RelayCommand]
    public void AddDriveSyncSource(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return;
        DriveSyncSources.Add(new SyncSource
        {
            LocalFolderPath = folderPath,
            DestinationPrefix = new DirectoryInfo(folderPath).Name
        });
    }

    [RelayCommand]
    public void RemoveDriveSyncSource(SyncSource? source)
    {
        if (source != null && DriveSyncSources.Contains(source))
        {
            DriveSyncSources.Remove(source);
        }
    }

    [RelayCommand]
    private async Task SaveCalendarUrl()
    {
        // Update and save calendar filter settings
        var filter = new CalendarFilterSettings
        {
            ExcludedKeywords = CalendarExcludedKeywords?.Trim() ?? CalendarFilterSettings.DefaultExcludedKeywords,
            IgnoreAllDayEvents = CalendarIgnoreAllDayEvents,
            RequireMeetingLink = CalendarRequireMeetingLink
        };
        _calendarService.UpdateFilterSettings(filter);

        if (string.IsNullOrWhiteSpace(CalendarICalUrl))
        {
            CalendarConnectionStatus = "Filtros guardados. Ingresa la URL iCal secreta para sincronizar.";
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
    private async Task TestDriveConnection()
    {
        if (string.IsNullOrWhiteSpace(DriveWebAppUrl))
        {
            DriveConnectionStatus = "Por favor ingresa la URL del Web App de Google Apps Script.";
            return;
        }

        IsDriveTesting = true;
        DriveConnectionStatus = "Enviando archivo de prueba a Google Drive...";

        try
        {
            var fileId = await _driveSyncService.TestConnectionAsync(DriveWebAppUrl.Trim());
            DriveConnectionStatus = string.IsNullOrEmpty(fileId)
                ? "Conexión recibida pero sin ID de archivo."
                : $"¡Conexión exitosa! Archivo de prueba creado en Google Drive (ID: {fileId[..Math.Min(8, fileId.Length)]}...)";
            IsDriveConnected = true;
        }
        catch (Exception ex)
        {
            DriveConnectionStatus = $"Error de conexión: {ex.Message}";
        }
        finally
        {
            IsDriveTesting = false;
        }
    }

    private async Task LoadGitHubAccountsAsync()
    {
        try
        {
            var info = await _gitHubAuthService.GetAccountsStatusAsync();
            IsGitHubInstalled = info.IsGhInstalled;
            AvailableGitHubAccounts.Clear();
            foreach (var account in info.AvailableAccounts)
            {
                AvailableGitHubAccounts.Add(account);
            }

            if (string.IsNullOrEmpty(GitHubWorkAccount) && !string.IsNullOrEmpty(info.WorkAccount))
            {
                GitHubWorkAccount = info.WorkAccount;
            }
            if (string.IsNullOrEmpty(GitHubPersonalAccount) && !string.IsNullOrEmpty(info.PersonalAccount))
            {
                GitHubPersonalAccount = info.PersonalAccount;
            }

            GitHubSettingsStatus = info.IsGhInstalled
                ? $"{info.AvailableAccounts.Count} cuentas detectadas en GitHub CLI"
                : "GitHub CLI no detectado en el sistema";
        }
        catch (Exception ex)
        {
            GitHubSettingsStatus = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void SaveGitHubSettings()
    {
        var settings = _gitHubAuthService.Settings;
        settings.WorkAccount = !string.IsNullOrWhiteSpace(GitHubWorkAccount) ? GitHubWorkAccount.Trim() : null;
        settings.PersonalAccount = !string.IsNullOrWhiteSpace(GitHubPersonalAccount) ? GitHubPersonalAccount.Trim() : null;
        settings.AutoSwitchOnWorkStart = GitHubAutoSwitchOnWorkStart;
        settings.AutoSwitchOnWorkEnd = GitHubAutoSwitchOnWorkEnd;

        _gitHubAuthService.UpdateSettings(settings);

        SaveConfirmationMessage = "¡Ajustes de cuenta laboral de GitHub guardados!";
        ShowSaveConfirmation = true;
    }

    [RelayCommand]
    private void SaveDriveSettings()
    {
        var settings = _driveSyncService.Settings;
        settings.WebAppUrl = DriveWebAppUrl?.Trim() ?? string.Empty;
        settings.AuthToken = DriveAuthToken?.Trim() ?? string.Empty;
        settings.DriveFolderUrl = DriveFolderUrl?.Trim() ?? string.Empty;
        settings.IncludedExtensions = DriveIncludedExtensions?.Trim() ?? string.Empty;
        settings.ExcludedExtensions = DriveExcludedExtensions?.Trim() ?? ".tmp, .log, .exe, .bak, .zip";
        settings.ExcludedFolders = DriveExcludedFolders?.Trim() ?? "node_modules, .git, bin, obj, .vs, temp";
        settings.MaxFileSizeMb = DriveMaxFileSizeMb > 0 ? DriveMaxFileSizeMb : 20;
        settings.OnlyModifiedOrNew = DriveOnlyModifiedOrNew;
        settings.AutoSyncOnWorkEnd = DriveAutoSyncOnWorkEnd;
        settings.Sources = DriveSyncSources.ToList();

        _driveSyncService.UpdateSettings(settings);
        UpdateDriveStatus();

        SaveConfirmationMessage = "¡Ajustes de Google Drive guardados correctamente!";
        ShowSaveConfirmation = true;
    }

    [RelayCommand]
    private void ClearHashIndex()
    {
        _driveSyncService.ClearHashIndex();
        SaveConfirmationMessage = "¡Caché de respaldo restablecida! La próxima sincronización re-evaluará todos los archivos.";
        ShowSaveConfirmation = true;
    }

    [RelayCommand]
    private async Task SaveSettings()
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

        // Also save GitHub settings
        SaveGitHubSettings();

        // Also save drive settings
        SaveDriveSettings();

        // Also save calendar filter settings
        var filter = new CalendarFilterSettings
        {
            ExcludedKeywords = CalendarExcludedKeywords?.Trim() ?? CalendarFilterSettings.DefaultExcludedKeywords,
            IgnoreAllDayEvents = CalendarIgnoreAllDayEvents,
            RequireMeetingLink = CalendarRequireMeetingLink
        };
        _calendarService.UpdateFilterSettings(filter);

        // Also persist Google Calendar settings
        if (!string.IsNullOrWhiteSpace(CalendarICalUrl))
        {
            if (!string.Equals(_calendarService.ICalUrl, CalendarICalUrl.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                await _calendarService.SetICalCredentialsAsync(CalendarICalUrl.Trim());
            }
            else if (_calendarService.IsConfigured)
            {
                await _calendarService.GetTodayEventsAsync();
            }
            UpdateCalendarStatus();
        }
        else if (_calendarService.IsConfigured && string.IsNullOrWhiteSpace(CalendarICalUrl))
        {
            await _calendarService.ClearICalCredentialsAsync();
            UpdateCalendarStatus();
        }

        SaveConfirmationMessage = "¡Toda la configuración y preferencias han sido guardadas!";
        ShowSaveConfirmation = true;
    }

    [RelayCommand]
    private async Task CheckForUpdates()
    {
        IsCheckingUpdates = true;
        UpdateStatusText = "Buscando nuevas versiones en GitHub...";

        try
        {
            var update = await _updateService.CheckForUpdatesAsync();
            _latestUpdateInfo = update;

            if (!update.IsSuccess)
            {
                UpdateStatusText = update.ErrorMessage ?? "Error al consultar actualizaciones.";
                IsUpdateAvailable = false;
            }
            else if (update.IsUpdateAvailable)
            {
                UpdateStatusText = $"¡Nueva versión v{update.LatestVersion} disponible! (Instalada: v{update.CurrentVersion})";
                IsUpdateAvailable = true;
            }
            else
            {
                UpdateStatusText = $"Tienes la versión más reciente instalada (v{update.CurrentVersion}).";
                IsUpdateAvailable = false;
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText = $"Error: {ex.Message}";
            IsUpdateAvailable = false;
        }
        finally
        {
            IsCheckingUpdates = false;
        }
    }

    [RelayCommand]
    private async Task DownloadAndInstallUpdate()
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
        DownloadProgress = 0;
        UpdateStatusText = "Descargando actualización...";

        try
        {
            var progress = new Progress<double>(p =>
            {
                App.DispatcherQueue.TryEnqueue(() =>
                {
                    DownloadProgress = p;
                    UpdateStatusText = $"Descargando ({p:F0}%)...";
                });
            });

            var installerPath = await _updateService.DownloadUpdateAsync(
                _latestUpdateInfo.DownloadUrl,
                _latestUpdateInfo.InstallerFileName,
                progress);

            UpdateStatusText = "Ejecutando instalador...";
            _updateService.LaunchInstaller(installerPath);
        }
        catch (Exception ex)
        {
            UpdateStatusText = $"Error al descargar: {ex.Message}";
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
}


