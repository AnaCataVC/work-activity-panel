using System;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WorkActivityPanel.ViewModels;

namespace WorkActivityPanel;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        ViewModel = App.GetService<SettingsViewModel>();
        InitializeComponent();
    }
    
    private void BackButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (Frame.CanGoBack) Frame.GoBack();
    }

    private async void BrowseFolderButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        try
        {
            var folderPicker = new FolderPicker();
            folderPicker.FileTypeFilter.Add("*");
            var hwnd = App.WindowHandle;
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);
            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder != null)
            {
                ViewModel.DriveLocalFolderPath = folder.Path;
            }
        }
        catch
        {
            // Ignore picker cancellation or exception
        }
    }

    private async void AddSourceFolderButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        try
        {
            var folderPicker = new FolderPicker();
            folderPicker.FileTypeFilter.Add("*");
            var hwnd = App.WindowHandle;
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);
            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder != null)
            {
                ViewModel.AddDriveSyncSource(folder.Path);
            }
        }
        catch
        {
            // Ignore picker cancellation or exception
        }
    }

    private void DeleteSourceButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Button { Tag: Models.SyncSource source })
        {
            ViewModel.RemoveDriveSyncSource(source);
        }
    }
}
