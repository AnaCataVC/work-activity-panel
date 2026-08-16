# Learning: WinUI 3 InfoBar Layout, Visibility & ActionButton Constraints

## Context
The WinUI 3 `InfoBar` control provides in-app notifications with severity states (`Informational`, `Success`, `Warning`, `Error`). In **Work Activity Panel**, `InfoBar` elements notify users about upcoming calendar meetings, vacation mode status, and available application updates.

## Problems & Gotchas

### 1. `InfoBar.ActionButton` Type Constraint (XAML Compilation Error WMC0015)
Attempting to group multiple action controls (e.g., `<StackPanel Orientation="Horizontal">` with a primary button and a secondary link) inside `<InfoBar.ActionButton>` causes a fatal compile-time XAML error:
```
XamlCompiler error WMC0015: Cannot assign 'StackPanel' into property 'ActionButton', type must be assignable to 'ButtonBase'
```
* **Root Cause**: `InfoBar.ActionButton` is strictly typed to `ButtonBase` (accepting `Button`, `HyperlinkButton`, etc.), rather than a general `UIElement` or container.
* **Solution**: Place the single primary action button in `<InfoBar.ActionButton>` and put any secondary links, supplementary descriptions, or progress texts in `<InfoBar.Content>`.

```xml
<InfoBar Title="¡Nueva versión disponible!"
         Message="{x:Bind ViewModel.UpdateBannerMessage, Mode=OneWay}"
         IsOpen="{x:Bind ViewModel.ShowUpdateBanner, Mode=OneWay}"
         Severity="Informational"
         IsClosable="True">
    <!-- Primary Action (ButtonBase) -->
    <InfoBar.ActionButton>
        <Button Content="Actualizar Ahora" 
                Command="{x:Bind ViewModel.DownloadAndInstallUpdateCommand}" 
                Style="{StaticResource AccentButtonStyle}"/>
    </InfoBar.ActionButton>
    <!-- Secondary Content & Supplementary Controls -->
    <InfoBar.Content>
        <StackPanel Orientation="Horizontal" Spacing="12" Margin="0,4,0,0">
            <HyperlinkButton Content="Ver notas de la versión en GitHub"
                             Command="{x:Bind ViewModel.OpenReleaseNotesCommand}" 
                             Padding="0" />
            <TextBlock Text="{x:Bind ViewModel.UpdateDownloadStatusText, Mode=OneWay}"
                       Visibility="{x:Bind ViewModel.IsDownloadingUpdate, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}"
                       Style="{StaticResource CaptionTextBlockStyle}"
                       VerticalAlignment="Center" />
        </StackPanel>
    </InfoBar.Content>
</InfoBar>
```

### 2. Layout Space Preservation in Vertical StackPanels
In WinUI 3, when an `InfoBar` is nested within a `StackPanel` and `IsOpen` is bound to `false`, the control animates closed. However, in certain container hierarchies, collapsed margins or default sizing can leave residual spacing.
* **Best Practice**: Bind both `IsOpen` and `Visibility` (using `BoolToVisibilityConverter`) to ensure the control is completely removed from the layout pass when closed:
```xml
<InfoBar IsOpen="{x:Bind ViewModel.ShowUpdateBanner, Mode=OneWay}"
         Visibility="{x:Bind ViewModel.ShowUpdateBanner, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}" />
```

## Key Takeaway
When building rich notifications with WinUI 3 `InfoBar`:
* Always assign a single `ButtonBase` to `InfoBar.ActionButton` and place multiple or custom controls in `InfoBar.Content`.
* Pair `IsOpen` with `Visibility` using a `BoolToVisibilityConverter` to prevent unwanted whitespace in layout stacks.
