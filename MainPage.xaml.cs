using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WorkActivityPanel.ViewModels;

namespace WorkActivityPanel;

public sealed partial class MainPage : Page
{
    public DashboardViewModel ViewModel { get; }

    public MainPage()
    {
        ViewModel = App.GetService<DashboardViewModel>();
        InitializeComponent();
    }
    
    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.RefreshScheduleAndStatus();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(SettingsPage));
    }

    private void JoinMeeting_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string url && !string.IsNullOrWhiteSpace(url))
        {
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
    }

    private void InfoBar_CloseButtonClick(InfoBar sender, object args)
    {
        ViewModel.ShowUpcomingMeetingBanner = false;
    }

    private void UpdateInfoBar_CloseButtonClick(InfoBar sender, object args)
    {
        ViewModel.ShowUpdateBanner = false;
    }
}

