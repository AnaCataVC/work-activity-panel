# Learning: Handling H.NotifyIcon System Tray Context Menus in WinUI 3

## Context
In **Work Activity Panel**, the application runs as a background tray utility with an interactive Windows System Tray icon using the `H.NotifyIcon.WinUI` library. The tray icon provides quick access via a context flyout with options such as "Show Panel", "Minimize to Tray", and "Exit".

## Problem & Challenge
When setting up `MenuFlyoutItem` elements inside `tb:TaskbarIcon.ContextFlyout`, assigning standard XAML event handlers (`Click="Exit_Click"`, `Click="ShowPanel_Click"`, `Click="HidePanel_Click"`) failed to trigger when clicking the context menu items in the system tray. The menu appeared correctly, but clicking any item had no effect.

## Root Cause
By default, `H.NotifyIcon` renders context menus using the native Win32 `PopupMenu` mode (`ContextMenuMode="PopupMenu"`). Because native Win32 menus exist outside the WinUI 3 XAML visual tree:
1. Windows OS handles the menu messages at the Win32 message loop level and does not route input events back into WinUI's XAML event bubbling pipeline.
2. WinUI XAML `Click` events are never dispatched for these items.
3. `H.NotifyIcon` translates clicks on Win32 menu items strictly into `ICommand.Execute(...)` calls on the `MenuFlyoutItem.Command` property. If `Command` is `null` (because only `Click` was defined), the action is silently ignored.

## Solution & Pattern

### 1. Define Strongly-Typed `IRelayCommand` Properties
In the window code-behind or ViewModel, declare and instantiate `IRelayCommand` properties:

```csharp
public IRelayCommand ShowPanelCommand { get; }
public IRelayCommand HidePanelCommand { get; }
public IRelayCommand ExitCommand { get; }

public MainWindow()
{
    InitializeComponent();

    ShowPanelCommand = new RelayCommand(ShowPanel);
    HidePanelCommand = new RelayCommand(HidePanel);
    ExitCommand = new RelayCommand(ExitApplication);
    
    // ...
}
```

### 2. Bind Commands Using `x:Bind` in XAML
Replace `Click="*_Click"` attributes with `Command="{x:Bind ...}"` bindings:

```xml
<tb:TaskbarIcon
    x:Name="TrayIcon"
    IconSource="Assets/AppIcon.ico"
    ToolTipText="Work Activity Panel">
    <tb:TaskbarIcon.ContextFlyout>
        <MenuFlyout>
            <MenuFlyoutItem Text="Mostrar Panel" Command="{x:Bind ShowPanelCommand}">
                <MenuFlyoutItem.Icon>
                    <FontIcon Glyph="&#xE737;" />
                </MenuFlyoutItem.Icon>
            </MenuFlyoutItem>
            <MenuFlyoutItem Text="Minimizar a la bandeja" Command="{x:Bind HidePanelCommand}">
                <MenuFlyoutItem.Icon>
                    <FontIcon Glyph="&#xE738;" />
                </MenuFlyoutItem.Icon>
            </MenuFlyoutItem>
            <MenuFlyoutSeparator />
            <MenuFlyoutItem Text="Salir" Command="{x:Bind ExitCommand}">
                <MenuFlyoutItem.Icon>
                    <FontIcon Glyph="&#xE7E8;" />
                </MenuFlyoutItem.Icon>
            </MenuFlyoutItem>
        </MenuFlyout>
    </tb:TaskbarIcon.ContextFlyout>
</tb:TaskbarIcon>
```

### 3. Clean Tray Icon Disposal on Application Exit
When exiting a tray application in WinUI 3, ensure `TrayIcon.Dispose()` is called prior to `Application.Current.Exit()` and `Environment.Exit(0)` to prevent "ghost" icons in the Windows notification area:

```csharp
private void ExitApplication()
{
    _isExplicitExit = true;
    TrayIcon.Dispose();
    Application.Current.Exit();
    System.Environment.Exit(0);
}
```

## Key Takeaways
- Always use `ICommand` / `RelayCommand` with `Command="{x:Bind ...}"` for `H.NotifyIcon` context menus in WinUI 3 applications.
- Win32 native menus bypass the XAML event pipeline, rendering `Click` event handlers ineffective.
- Explicitly dispose `TaskbarIcon` during application termination routines to ensure clean Windows notification area cleanup.
