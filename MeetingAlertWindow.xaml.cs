using System;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WorkActivityPanel.Models;
using WorkActivityPanel.Services.Interfaces;

namespace WorkActivityPanel;

/// <summary>
/// Full-screen centered alert popup that appears when a meeting with a video conference link is starting.
/// Positioned in the center of the primary display, always-on-top, with a 2-minute auto-close countdown.
/// </summary>
public sealed partial class MeetingAlertWindow : Window
{
    private const int AutoCloseTotalSeconds = 120;

    private readonly CalendarEvent _meeting;
    private readonly IAppLauncherService? _launcher;
    private int _remainingSeconds = AutoCloseTotalSeconds;
    private DispatcherTimer? _countdownTimer;

    public MeetingAlertWindow(CalendarEvent meeting)
    {
        _meeting = meeting;
        _launcher = App.GetService<IAppLauncherService>();

        InitializeComponent();

        ConfigureWindow();
        PopulateContent();
        StartCountdown();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Window setup
    // ──────────────────────────────────────────────────────────────────────────

    private void ConfigureWindow()
    {
        // Remove standard title bar chrome
        ExtendsContentIntoTitleBar = true;

        // Set icon so the taskbar entry is recognizable
        var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (System.IO.File.Exists(iconPath))
        {
            AppWindow.SetIcon(iconPath);
        }

        AppWindow.Title = "Reunión por comenzar";

        // Always-on-top overlapped presenter
        var presenter = OverlappedPresenter.Create();
        presenter.IsAlwaysOnTop = true;
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        AppWindow.SetPresenter(presenter);

        // Size and center on the primary display
        const int windowWidth = 500;
        const int windowHeight = 340;

        var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;

        int left = workArea.X + (workArea.Width - windowWidth) / 2;
        int top = workArea.Y + (workArea.Height - windowHeight) / 2;

        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(left, top, windowWidth, windowHeight));
        AppWindow.Show();
        AppWindow.MoveInZOrderAtTop();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Content
    // ──────────────────────────────────────────────────────────────────────────

    private void PopulateContent()
    {
        MeetingTitleText.Text = _meeting.Title;
        MeetingTimeText.Text = $"{_meeting.FormattedStartTime} – {_meeting.FormattedEndTime}";
        UpdateCountdownLabel();
    }

    private void UpdateCountdownLabel()
    {
        var minutes = _remainingSeconds / 60;
        var seconds = _remainingSeconds % 60;
        CountdownText.Text = minutes > 0
            ? $"Esta ventana se cerrará en {minutes}:{seconds:D2} min"
            : $"Esta ventana se cerrará en {seconds} seg";
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Auto-close countdown
    // ──────────────────────────────────────────────────────────────────────────

    private void StartCountdown()
    {
        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += OnCountdownTick;
        _countdownTimer.Start();
    }

    private void OnCountdownTick(object? sender, object e)
    {
        _remainingSeconds--;

        if (_remainingSeconds <= 0)
        {
            CloseWindow();
            return;
        }

        UpdateCountdownLabel();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Button handlers
    // ──────────────────────────────────────────────────────────────────────────

    private void OnJoinClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_meeting.MeetingLink))
        {
            _launcher?.OpenUrl(_meeting.MeetingLink);
        }
        CloseWindow();
    }

    private void OnDismissClick(object sender, RoutedEventArgs e)
    {
        CloseWindow();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Cleanup
    // ──────────────────────────────────────────────────────────────────────────

    private void CloseWindow()
    {
        _countdownTimer?.Stop();
        _countdownTimer = null;
        Close();
    }
}
