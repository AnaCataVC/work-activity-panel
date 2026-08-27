using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
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

    public App()
    {
        InitializeComponent();
        
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
    }

    public static T GetService<T>() where T : class
    {
        return _host!.Services.GetRequiredService<T>();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        Window = new MainWindow();
        
        // Start the schedule service
        var scheduleService = GetService<IScheduleService>();
        scheduleService.Start();
        
        Window.Activate();
    }
}
