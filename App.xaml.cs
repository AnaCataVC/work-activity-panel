using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using WorkActivityPanel.Models;
using WorkActivityPanel.Services;
using WorkActivityPanel.Services.Interfaces;
using WorkActivityPanel.ViewModels;

namespace WorkActivityPanel;

public partial class App : Application
{
    public static Window Window { get; private set; } = null!;
    public static Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; private set; } = null!;
    public static nint WindowHandle => WinRT.Interop.WindowNative.GetWindowHandle(Window);
    
    private static IHost? _host;

    // Tracks the currently visible meeting alert popup to avoid duplicates
    private static WeakReference<MeetingAlertWindow>? _activeMeetingAlert;
    private static string? _activeMeetingAlertId;

    public App()
    {
        this.UnhandledException += (s, e) =>
        {
            LogCrash("XAML_UnhandledException", e.Exception, e.Message);
        };
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            LogCrash("AppDomain_UnhandledException", e.ExceptionObject as System.Exception, e.ExceptionObject?.ToString());
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            LogCrash("TaskScheduler_UnobservedTaskException", e.Exception, e.Exception?.ToString());
        };

        LogTrace("App() constructor started");

        try
        {
            InitializeComponent();
            LogTrace("InitializeComponent() completed");
            
            _host = Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    // Core Services
                    services.AddSingleton<IScheduleService, ScheduleService>();
                    services.AddSingleton<IAppLauncherService, AppLauncherService>();
                    services.AddSingleton<IGoogleCalendarService, GoogleCalendarService>();
                    services.AddSingleton<IDriveSyncService, DriveSyncService>();
                    services.AddSingleton<IGitHubAuthService, GitHubAuthService>();
                    services.AddSingleton<IUpdateService, UpdateService>();
                    
                    // ViewModels
                    services.AddSingleton<DashboardViewModel>();
                    services.AddTransient<SettingsViewModel>();
                })
                .Build();
            LogTrace("Host built successfully");
        }
        catch (System.Exception ex)
        {
            LogCrash("App_Constructor", ex, ex.Message);
            throw;
        }
    }

    public static T GetService<T>() where T : class
    {
        return _host!.Services.GetRequiredService<T>();
    }

    /// <summary>
    /// Shows the meeting alert popup window on the UI thread.
    /// If a popup for the same meeting is already visible, the call is ignored.
    /// </summary>
    public static void ShowMeetingAlert(CalendarEvent meeting)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            // Prevent duplicate popup for the same meeting
            if (_activeMeetingAlertId == meeting.Id &&
                _activeMeetingAlert != null &&
                _activeMeetingAlert.TryGetTarget(out var existing))
            {
                existing.Activate();
                return;
            }

            var alertWindow = new MeetingAlertWindow(meeting);

            // Clear reference when the window closes
            alertWindow.Closed += (_, _) =>
            {
                if (_activeMeetingAlertId == meeting.Id)
                {
                    _activeMeetingAlertId = null;
                    _activeMeetingAlert = null;
                }
            };

            _activeMeetingAlert = new WeakReference<MeetingAlertWindow>(alertWindow);
            _activeMeetingAlertId = meeting.Id;
        });
    }


    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        LogTrace("OnLaunched() started");
        try
        {
            var cmdArgs = Environment.GetCommandLineArgs();
            bool isAutostart = cmdArgs.Any(a => string.Equals(a, Helpers.AutostartHelper.AutostartArgument, StringComparison.OrdinalIgnoreCase));
            LogTrace($"Launch arguments evaluated. IsAutostart: {isAutostart}");

            var scheduleService = GetService<IScheduleService>();

            if (isAutostart && scheduleService.AutoCloseOnNonWorkDays)
            {
                var today = DateTime.Today;
                bool isWorkDay = scheduleService.CurrentSchedule.WorkDays.Contains(today.DayOfWeek);
                bool isVacation = scheduleService.CurrentSchedule.IsVacationActive(today);

                if (!isWorkDay || isVacation)
                {
                    LogTrace($"Autostart ignored: today is non-work day (IsWorkDay={isWorkDay}, IsVacation={isVacation}). Exiting silently.");
                    Exit();
                    return;
                }
            }

            DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            LogTrace("DispatcherQueue obtained");

            Window = new MainWindow();
            LogTrace("MainWindow instantiated");
            
            // Start the schedule service
            scheduleService.Start();
            LogTrace("ScheduleService started");
            
            Window.Activate();
            LogTrace("Window.Activate() executed");
        }
        catch (System.Exception ex)
        {
            LogCrash("OnLaunched", ex, ex.Message);
            throw;
        }
    }

    public static void LogTrace(string step)
    {
        try
        {
            var logDir = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "WorkActivityPanel", "Logs");
            System.IO.Directory.CreateDirectory(logDir);
            System.IO.File.AppendAllText(System.IO.Path.Combine(logDir, "startup_diagnostic.log"), $"[{System.DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {step}\n");
        }
        catch { }
    }

    public static void LogCrash(string source, System.Exception? ex, string? message)
    {
        try
        {
            var logDir = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "WorkActivityPanel", "Logs");
            System.IO.Directory.CreateDirectory(logDir);
            var content = $"[{System.DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] CRASH in {source}: {message}\nException: {ex}\nStackTrace: {ex?.StackTrace}\nInnerException: {ex?.InnerException}\n\n";
            System.IO.File.AppendAllText(System.IO.Path.Combine(logDir, "startup_diagnostic.log"), content);
            System.IO.File.AppendAllText(System.IO.Path.Combine(logDir, "crash.log"), content);
        }
        catch { }
    }
}
