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
        App.LogTrace("MainPage constructor started");
        try
        {
            ViewModel = App.GetService<DashboardViewModel>();
            App.LogTrace("MainPage DashboardViewModel resolved");
            InitializeComponent();
            App.LogTrace("MainPage InitializeComponent completed");
        }
        catch (System.Exception ex)
        {
            App.LogCrash("MainPage_Constructor", ex, ex.Message);
            throw;
        }
    }
    
    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        App.LogTrace("MainPage OnNavigatedTo started");
        base.OnNavigatedTo(e);
        ViewModel.RefreshAllStatus();
        App.LogTrace("MainPage OnNavigatedTo finished");
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
        ViewModel.DismissUpcomingMeetingBannerCommand.Execute(null);
    }

    private void UpdateInfoBar_CloseButtonClick(InfoBar sender, object args)
    {
        ViewModel.ShowUpdateBanner = false;
    }

    private async void ShowSyncErrorsDialog_Click(object sender, RoutedEventArgs e)
    {
        var errors = ViewModel.SyncErrorsList;
        if (errors.Count == 0) return;

        var scrollViewer = new ScrollViewer
        {
            MaxHeight = 380,
            HorizontalScrollMode = ScrollMode.Disabled,
            VerticalScrollMode = ScrollMode.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var listStack = new StackPanel { Spacing = 8, Margin = new Thickness(0, 4, 0, 4) };

        foreach (var err in errors)
        {
            var itemBorder = new Border
            {
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 8)
            };

            var itemGrid = new Grid();
            itemGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            itemGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            itemGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Row 0: File Name + Category Badge
            var topGrid = new Grid();
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            titleStack.Children.Add(new FontIcon { Glyph = "\uE8A5", FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
            titleStack.Children.Add(new TextBlock
            {
                Text = err.FileName,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 260
            });
            Grid.SetColumn(titleStack, 0);
            topGrid.Children.Add(titleStack);

            var badgeBorder = new Border
            {
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBackgroundBrush"],
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = err.ErrorCategory,
                    FontSize = 10,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                }
            };
            Grid.SetColumn(badgeBorder, 1);
            topGrid.Children.Add(badgeBorder);


            Grid.SetRow(topGrid, 0);
            itemGrid.Children.Add(topGrid);

            // Row 1: Reason / Error message
            var errorText = new TextBlock
            {
                Text = err.ErrorMessage,
                FontSize = 11,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                Margin = new Thickness(20, 4, 0, 2),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(errorText, 1);
            itemGrid.Children.Add(errorText);

            // Row 2: File Path
            var pathText = new TextBlock
            {
                Text = err.FilePath,
                FontSize = 10,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
                Margin = new Thickness(20, 0, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetRow(pathText, 2);
            itemGrid.Children.Add(pathText);

            itemBorder.Child = itemGrid;
            listStack.Children.Add(itemBorder);
        }

        scrollViewer.Content = listStack;

        var dialog = new ContentDialog
        {
            Title = $"Archivos no sincronizados ({errors.Count})",
            Content = scrollViewer,
            PrimaryButtonText = "Reintentar estos archivos",
            SecondaryButtonText = "Copiar reporte",
            CloseButtonText = "Cerrar",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            await ViewModel.RetrySyncErrorsCommand.ExecuteAsync(null);
        }
        else if (result == ContentDialogResult.Secondary)
        {
            ViewModel.CopySyncErrorsReportCommand.Execute(null);
        }
    }
}


