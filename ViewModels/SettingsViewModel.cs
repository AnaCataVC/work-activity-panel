using System;
using System.Collections.Generic;
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

    // Calendar iCal Configuration
    [ObservableProperty]
    private string _calendarICalUrl = string.Empty;

    [ObservableProperty]
    private bool _isCalendarConnected;

    [ObservableProperty]
    private string _calendarConnectionStatus = "No configurado";

    // Google Drive Sync Configuration
    [ObservableProperty]
    private string _driveWebAppUrl = string.Empty;

    [ObservableProperty]
    private string _driveLocalFolderPath = string.Empty;

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
        IUpdateService updateService)
    {
        _scheduleService = scheduleService;
        _calendarService = calendarService;
        _driveSyncService = driveSyncService;
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

        // Load saved iCal URL
        CalendarICalUrl = _calendarService.ICalUrl ?? string.Empty;
        UpdateCalendarStatus();

        // Load Drive Sync settings
        var driveSettings = _driveSyncService.Settings;
        DriveWebAppUrl = driveSettings.WebAppUrl ?? string.Empty;
        DriveLocalFolderPath = driveSettings.LocalFolderPath ?? string.Empty;
        DriveIncludedExtensions = driveSettings.IncludedExtensions ?? string.Empty;
        DriveExcludedExtensions = !string.IsNullOrEmpty(driveSettings.ExcludedExtensions) ? driveSettings.ExcludedExtensions : ".tmp, .log, .exe, .bak, .zip";
        DriveExcludedFolders = !string.IsNullOrEmpty(driveSettings.ExcludedFolders) ? driveSettings.ExcludedFolders : "node_modules, .git, bin, obj, .vs, temp";
        DriveMaxFileSizeMb = driveSettings.MaxFileSizeMb > 0 ? driveSettings.MaxFileSizeMb : 50;
        DriveOnlyModifiedOrNew = driveSettings.OnlyModifiedOrNew;
        DriveAutoSyncOnWorkEnd = driveSettings.AutoSyncOnWorkEnd;
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
            : "Falta configurar URL o carpeta local";
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

    [RelayCommand]
    private void SaveDriveSettings()
    {
        var settings = _driveSyncService.Settings;
        settings.WebAppUrl = DriveWebAppUrl?.Trim() ?? string.Empty;
        settings.LocalFolderPath = DriveLocalFolderPath?.Trim() ?? string.Empty;
        settings.IncludedExtensions = DriveIncludedExtensions?.Trim() ?? string.Empty;
        settings.ExcludedExtensions = DriveExcludedExtensions?.Trim() ?? ".tmp, .log, .exe, .bak, .zip";
        settings.ExcludedFolders = DriveExcludedFolders?.Trim() ?? "node_modules, .git, bin, obj, .vs, temp";
        settings.MaxFileSizeMb = DriveMaxFileSizeMb > 0 ? DriveMaxFileSizeMb : 50;
        settings.OnlyModifiedOrNew = DriveOnlyModifiedOrNew;
        settings.AutoSyncOnWorkEnd = DriveAutoSyncOnWorkEnd;

        _driveSyncService.UpdateSettings(settings);
        UpdateDriveStatus();

        SaveConfirmationMessage = "¡Ajustes de Google Drive guardados correctamente!";
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

        // Also save drive settings
        SaveDriveSettings();

        // Also persist Google Calendar settings
        if (!string.IsNullOrWhiteSpace(CalendarICalUrl))
        {
            if (!string.Equals(_calendarService.ICalUrl, CalendarICalUrl.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                await _calendarService.SetICalCredentialsAsync(CalendarICalUrl.Trim());
                UpdateCalendarStatus();
            }
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


