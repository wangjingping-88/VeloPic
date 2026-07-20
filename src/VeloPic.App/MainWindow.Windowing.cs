using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VeloPic.Core;
using Windows.Graphics;
using WinRT.Interop;

namespace VeloPic.App;

public sealed partial class MainWindow : Window
{
    private const double CompactWidth = 1040;
    private const double NarrowWidth = 780;
    private const int DefaultWindowWidth = 2000;
    private const int DefaultWindowHeight = 1125;
    private const int MinimumWindowWidth = 1180;
    private const int MinimumWindowHeight = 900;
    private const double BaseLeftPaneWidth = 232;
    private const double BaseRightPaneWidth = 340;
    private const int GwlWndProc = -4;
    private const uint WmGetMinMaxInfo = 0x0024;
    private WindowProc? _windowProc;
    private IntPtr _previousWindowProc;
    private int _minimumWindowWidthPixels;
    private int _minimumWindowHeightPixels;

    private void ConfigureWindow()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "VeloPic.ico");
        if (File.Exists(iconPath))
        {
            appWindow.SetIcon(iconPath);
        }
        appWindow.Closing += AppWindow_OnClosing;
        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;
        var dpi = Math.Max(GetDpiForWindow(hwnd), 96u);
        var rasterizationScale = dpi / 96d;
        _minimumWindowWidthPixels = Math.Min(ToPhysicalPixels(MinimumWindowWidth, rasterizationScale), workArea.Width);
        _minimumWindowHeightPixels = Math.Min(ToPhysicalPixels(MinimumWindowHeight, rasterizationScale), workArea.Height);
        var initialWidth = Math.Clamp(DefaultWindowWidth, _minimumWindowWidthPixels, workArea.Width);
        var initialHeight = Math.Clamp(DefaultWindowHeight, _minimumWindowHeightPixels, workArea.Height);

        var x = workArea.X + (workArea.Width - initialWidth) / 2;
        var y = workArea.Y + (workArea.Height - initialHeight) / 2;
        appWindow.MoveAndResize(new RectInt32(x, y, initialWidth, initialHeight));
        InstallMinimumWindowSize(hwnd, _minimumWindowWidthPixels, _minimumWindowHeightPixels);
        UpdateTitleBarColors(ElementTheme.Dark);
    }

    private void AppWindow_OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!IsViewerOpen())
        {
            return;
        }

        args.Cancel = true;
        CloseViewer();
    }

    private void RootGrid_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateAdaptiveLayout(e.NewSize.Width);
        if (Images.Count > 0)
        {
            _thumbnailLayoutTimer.Stop();
            _thumbnailLayoutTimer.Start();
        }
    }

    private void UpdateAdaptiveLayout(double width)
    {
        if (LeftSidebar is null)
        {
            return;
        }

        var hideDetails = width < CompactWidth;
        var hideSidebar = width < NarrowWidth;

        LeftSidebar.Visibility = hideSidebar ? Visibility.Collapsed : Visibility.Visible;
        DetailPane.Visibility = hideDetails ? Visibility.Collapsed : Visibility.Visible;

        var layoutScale = Math.Clamp(width / 1440d, 0.92d, 1.12d);
        var leftPaneWidth = Math.Clamp(BaseLeftPaneWidth * layoutScale, 220d, 260d);
        var rightPaneWidth = Math.Clamp(BaseRightPaneWidth * layoutScale, 320d, 372d);

        LeftPaneColumn.Width = hideSidebar ? new GridLength(0) : new GridLength(leftPaneWidth);
        RightPaneColumn.Width = hideDetails ? new GridLength(0) : new GridLength(rightPaneWidth);
        HeaderBrandColumn.Width = new GridLength(hideSidebar ? 196d : Math.Max(196d, leftPaneWidth - 22d));
        var headerExpansion = Math.Clamp((width - MinimumWindowWidth) / (1440d - MinimumWindowWidth), 0d, 1d);
        SortModeGrid.Width = 294d + 32d * headerExpansion;
        ThemeModeGrid.Width = 286d + 32d * headerExpansion;
        HeaderPathBar.Visibility = width >= 1600d ? Visibility.Visible : Visibility.Collapsed;

        MainContent.Margin = hideSidebar
            ? new Thickness(8, 8, 8, 8)
            : hideDetails
                ? new Thickness(0, 8, 8, 8)
                : new Thickness(0, 8, 6, 8);
    }

    private void InitializeControlsFromSettings()
    {
        ThumbnailSlider.Value = _settings.Appearance.ThumbnailSize;
        UpdateSortDirectionButton();
        UpdateWallpaperFitButtons();
    }

    private static int ToPhysicalPixels(double effectivePixels, double rasterizationScale)
    {
        return (int)Math.Ceiling(effectivePixels * rasterizationScale);
    }

    // 使用原生窗口最小跟踪尺寸，阻止用户把布局缩小到不可用状态。
    private void InstallMinimumWindowSize(IntPtr hwnd, int minimumWidthPixels, int minimumHeightPixels)
    {
        _minimumWindowWidthPixels = minimumWidthPixels;
        _minimumWindowHeightPixels = minimumHeightPixels;
        _windowProc = WindowProcHandler;
        var callback = Marshal.GetFunctionPointerForDelegate(_windowProc);
        _previousWindowProc = SetWindowLongPtr(hwnd, GwlWndProc, callback);
    }

    private IntPtr WindowProcHandler(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WmGetMinMaxInfo && lParam != IntPtr.Zero)
        {
            var minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            minMaxInfo.MinTrackSize = new NativePoint
            {
                X = _minimumWindowWidthPixels,
                Y = _minimumWindowHeightPixels
            };
            Marshal.StructureToPtr(minMaxInfo, lParam, false);
            return IntPtr.Zero;
        }

        return CallWindowProc(_previousWindowProc, hwnd, message, wParam, lParam);
    }

    private void ApplyTheme(VeloPicThemeMode mode)
    {
        var actualTheme = mode switch
        {
            VeloPicThemeMode.Dark => ElementTheme.Dark,
            VeloPicThemeMode.Light => ElementTheme.Light,
            _ => GetWindowsAppTheme()
        };

        RootGrid.RequestedTheme = actualTheme;
        SetNavigationSelection(_activeNavigationSection, actualTheme);
        SetCategorySelection(
            _activeCategory,
            showAll: _activeNavigationSection == "folder" && _activeNavigationFilter is null && _activeCategory is null,
            themeOverride: actualTheme);
        UpdateWallpaperFitButtons();
        UpdateTitleBarColors(actualTheme);
        UpdateThemeModeButtons(mode, actualTheme);
        UpdateSortFieldButtons(_settings.Sorting.Field, actualTheme);
    }

    private static ElementTheme GetWindowsAppTheme()
    {
        const string personalizePath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        var value = Registry.CurrentUser.OpenSubKey(personalizePath)?.GetValue("AppsUseLightTheme");
        return value is int intValue && intValue == 0 ? ElementTheme.Dark : ElementTheme.Light;
    }

    private void UpdateTitleBarColors(ElementTheme actualTheme)
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        if (actualTheme == ElementTheme.Light)
        {
            appWindow.TitleBar.BackgroundColor = Windows.UI.Color.FromArgb(255, 247, 250, 253);
            appWindow.TitleBar.ForegroundColor = Windows.UI.Color.FromArgb(255, 24, 34, 51);
            appWindow.TitleBar.InactiveBackgroundColor = Windows.UI.Color.FromArgb(255, 247, 250, 253);
            appWindow.TitleBar.InactiveForegroundColor = Windows.UI.Color.FromArgb(255, 83, 97, 118);
            appWindow.TitleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(255, 247, 250, 253);
            appWindow.TitleBar.ButtonForegroundColor = Windows.UI.Color.FromArgb(255, 24, 34, 51);
            appWindow.TitleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(255, 230, 236, 243);
            appWindow.TitleBar.ButtonHoverForegroundColor = Windows.UI.Color.FromArgb(255, 24, 34, 51);
            appWindow.TitleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(255, 206, 217, 230);
            appWindow.TitleBar.ButtonPressedForegroundColor = Windows.UI.Color.FromArgb(255, 24, 34, 51);
            appWindow.TitleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(255, 247, 250, 253);
            appWindow.TitleBar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(255, 83, 97, 118);
            return;
        }

        appWindow.TitleBar.BackgroundColor = Windows.UI.Color.FromArgb(255, 18, 26, 38);
        appWindow.TitleBar.ForegroundColor = Windows.UI.Color.FromArgb(255, 238, 243, 250);
        appWindow.TitleBar.InactiveBackgroundColor = Windows.UI.Color.FromArgb(255, 18, 26, 38);
        appWindow.TitleBar.InactiveForegroundColor = Windows.UI.Color.FromArgb(255, 168, 179, 195);
        appWindow.TitleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(255, 18, 26, 38);
        appWindow.TitleBar.ButtonForegroundColor = Windows.UI.Color.FromArgb(255, 238, 243, 250);
        appWindow.TitleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(255, 38, 49, 65);
        appWindow.TitleBar.ButtonHoverForegroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255);
        appWindow.TitleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(255, 31, 41, 55);
        appWindow.TitleBar.ButtonPressedForegroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255);
        appWindow.TitleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(255, 18, 26, 38);
        appWindow.TitleBar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(255, 168, 179, 195);
    }

    private void UpdateThemeModeButtons(VeloPicThemeMode mode, ElementTheme actualTheme)
    {
        foreach (var button in new[] { SystemThemeButton, DarkThemeButton, LightThemeButton })
        {
            var selected = string.Equals(button.Tag?.ToString(), mode.ToString(), StringComparison.OrdinalIgnoreCase);
            button.IsChecked = selected;
            AutomationProperties.SetName(button, $"{button.Content}主题{(selected ? "，当前已选" : string.Empty)}");
        }
    }

    private void DetailScrollViewer_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DetailContentGrid is null || SelectedImagePreviewFrame is null)
        {
            return;
        }

        if (Math.Abs(DetailContentGrid.MinHeight - e.NewSize.Height) > 0.5d)
        {
            DetailContentGrid.MinHeight = e.NewSize.Height;
        }

        var expandableHeight = Math.Max(0d, e.NewSize.Height - 680d);
        SetElementHeightIfChanged(
            SelectedImagePreviewFrame,
            Math.Clamp(154d + expandableHeight * 0.18d, 154d, 190d));
    }

    private static void SetElementHeightIfChanged(FrameworkElement element, double height)
    {
        if (double.IsNaN(element.Height) || Math.Abs(element.Height - height) > 0.5d)
        {
            element.Height = height;
        }
    }

    private void UpdateSortFieldButtons(ImageSortField sortField, ElementTheme actualTheme)
    {
        var selectedBackground = actualTheme == ElementTheme.Dark
            ? new SolidColorBrush(ColorHelper.FromArgb(255, 25, 63, 125))
            : new SolidColorBrush(ColorHelper.FromArgb(255, 220, 234, 255));
        var normalBackground = actualTheme == ElementTheme.Dark
            ? new SolidColorBrush(ColorHelper.FromArgb(255, 27, 36, 49))
            : new SolidColorBrush(ColorHelper.FromArgb(255, 255, 255, 255));
        var selectedBorder = actualTheme == ElementTheme.Dark
            ? new SolidColorBrush(ColorHelper.FromArgb(255, 60, 141, 255))
            : new SolidColorBrush(ColorHelper.FromArgb(255, 36, 111, 229));
        var normalBorder = actualTheme == ElementTheme.Dark
            ? new SolidColorBrush(ColorHelper.FromArgb(255, 53, 66, 85))
            : new SolidColorBrush(ColorHelper.FromArgb(255, 198, 210, 224));

        foreach (var button in new[] { ModifiedTimeSortButton, FileNameSortButton, FileSizeSortButton })
        {
            var selected = string.Equals(button.Tag?.ToString(), sortField.ToString(), StringComparison.OrdinalIgnoreCase);
            button.Background = selected ? selectedBackground : normalBackground;
            button.BorderBrush = selected ? selectedBorder : normalBorder;
            button.BorderThickness = new Thickness(1);
            AutomationProperties.SetName(button, $"按{button.Content}排序{(selected ? "，当前已选" : string.Empty)}");
        }
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var content = new StackPanel
        {
            Spacing = 20,
            MinWidth = 340
        };
        content.Children.Add(new TextBlock
        {
            Text = message,
            MaxWidth = 380,
            TextWrapping = TextWrapping.Wrap
        });
        var acknowledgeButton = new Button
        {
            Content = "知道了",
            Width = 196,
            HorizontalAlignment = HorizontalAlignment.Center,
            Style = Application.Current.Resources["CompactButtonStyle"] as Style
        };
        content.Children.Add(acknowledgeButton);

        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            XamlRoot = RootGrid.XamlRoot
        };
        acknowledgeButton.Click += (_, _) => dialog.Hide();
        await dialog.ShowAsync();
    }

    private async Task<bool> ConfirmAsync(string title, string message, string primaryButtonText)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock
            {
                Text = message,
                MaxWidth = 380,
                TextWrapping = TextWrapping.Wrap
            },
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr newProc);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern IntPtr CallWindowProc(IntPtr previousProc, IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

}
