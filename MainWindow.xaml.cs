using Microsoft.UI.Xaml;
using CommunityToolkit.Mvvm.Input;

namespace WorkActivityPanel;

/// <summary>
/// The application window. This hosts a Frame that displays pages.
/// </summary>
public sealed partial class MainWindow : Window
{
    private bool _isExplicitExit;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (System.IO.File.Exists(iconPath))
        {
            AppWindow.SetIcon(iconPath);
        }

        // Hide to tray on window closing unless explicitly exiting
        AppWindow.Closing += (sender, args) =>
        {
            if (!_isExplicitExit)
            {
                args.Cancel = true;
                AppWindow.Hide();
            }
        };

        // Left click on tray icon toggles window visibility
        TrayIcon.LeftClickCommand = new RelayCommand(ToggleWindowVisibility);

        // Navigate the root frame to the main page on startup.
        RootFrame.Navigate(typeof(MainPage));
    }

    private void ToggleWindowVisibility()
    {
        if (AppWindow.IsVisible)
        {
            AppWindow.Hide();
        }
        else
        {
            AppWindow.Show();
            AppWindow.MoveInZOrderAtTop();
        }
    }

    private void ShowPanel_Click(object sender, RoutedEventArgs e)
    {
        AppWindow.Show();
        AppWindow.MoveInZOrderAtTop();
    }

    private void HidePanel_Click(object sender, RoutedEventArgs e)
    {
        AppWindow.Hide();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        _isExplicitExit = true;
        TrayIcon.Dispose();
        Application.Current.Exit();
        System.Environment.Exit(0);
    }
}

