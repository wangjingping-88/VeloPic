using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using VeloPic.Core;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.System;
using WinRT.Interop;
using UiDispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace VeloPic.App;

public sealed partial class MainWindow : Window
{
    private const int MaxInitialTiles = 2000;

    private readonly ImageFileScanner _scanner = new();
    private readonly ImageLibraryQueryService _libraryQuery = new();
    private readonly SystemMetricsMonitor _metricsMonitor = new();
    private readonly AppSettings _settings;
    private readonly ImageCollectionService _collections;
    private readonly List<ImageRecord> _allImages = [];
    private readonly List<ImageRecord> _filteredImages = [];
    private readonly ConcurrentDictionary<string, ImageDimensions> _imageDimensions = new(StringComparer.OrdinalIgnoreCase);
    private string _currentFolder = string.Empty;
    private bool _isInitializing = true;
    private readonly UiDispatcherQueueTimer _metricsTimer;
    private readonly UiDispatcherQueueTimer _thumbnailLayoutTimer;
    private readonly UiDispatcherQueueTimer _thumbnailSettingsTimer;
    private readonly UiDispatcherQueueTimer _filterApplyTimer;
    private int _renderedThumbnailSize;
    private bool _suppressFilterChanges;
    private bool _isResolutionMetadataReady;
    private CancellationTokenSource? _resolutionMetadataCancellation;
    private CancellationTokenSource? _scanCancellation;
    private readonly CancellationTokenSource _lifetimeCancellation = new();

    public ObservableCollection<ImageTileViewModel> Images { get; } = [];

    public MainWindow()
    {
        _settings = SettingsStore.Load();
        _settings.Sorting ??= new SortingSettings();
        _settings.CustomCategories ??= [];
        _collections = new ImageCollectionService(_settings);
        _currentFolder = _settings.LastFolder;

        InitializeComponent();
        RebuildCategoryControls();
        RebuildAlbumNavigation();
        _metricsTimer = DispatcherQueue.CreateTimer();
        _metricsTimer.Interval = TimeSpan.FromSeconds(1);
        _metricsTimer.Tick += MetricsTimer_OnTick;
        _thumbnailLayoutTimer = DispatcherQueue.CreateTimer();
        _thumbnailLayoutTimer.Interval = TimeSpan.FromMilliseconds(160);
        _thumbnailLayoutTimer.IsRepeating = false;
        _thumbnailLayoutTimer.Tick += ThumbnailLayoutTimer_OnTick;
        _thumbnailSettingsTimer = DispatcherQueue.CreateTimer();
        _thumbnailSettingsTimer.Interval = TimeSpan.FromMilliseconds(240);
        _thumbnailSettingsTimer.IsRepeating = false;
        _thumbnailSettingsTimer.Tick += ThumbnailSettingsTimer_OnTick;
        _filterApplyTimer = DispatcherQueue.CreateTimer();
        _filterApplyTimer.Interval = TimeSpan.FromMilliseconds(180);
        _filterApplyTimer.IsRepeating = false;
        _filterApplyTimer.Tick += FilterApplyTimer_OnTick;
        Closed += (_, _) =>
        {
            _metricsTimer.Stop();
            _thumbnailLayoutTimer.Stop();
            _thumbnailSettingsTimer.Stop();
            _filterApplyTimer.Stop();
            _lifetimeCancellation.Cancel();
            _scanCancellation?.Cancel();
            _scanCancellation?.Dispose();
            _resolutionMetadataCancellation?.Cancel();
            _resolutionMetadataCancellation?.Dispose();
            SettingsStore.Save(null, _settings);
            _metricsMonitor.Dispose();
            if (!_isSmartClassifying)
            {
                _smartClassificationService?.Dispose();
            }
        };
        ConfigureWindow();
        InitializeControlsFromSettings();
        ApplyTheme(_settings.Appearance.Theme);
        _isInitializing = false;
        _ = UpdateSelectionDetails(null);
        UpdateStatus();
        UpdatePerformanceStatus();
        _metricsTimer.Start();

        if (!string.IsNullOrWhiteSpace(_currentFolder) && Directory.Exists(_currentFolder))
        {
            _ = LoadFolderAsync(_currentFolder);
        }
    }

    private async void ChooseFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        await ChooseFolderAsync();
    }

    private async Task ChooseFolderAsync()
    {
        var folderPath = NativeFolderPicker.PickFolder(WindowNative.GetWindowHandle(this), "选择要导入 VeloPic 的图片文件夹");
        if (!string.IsNullOrWhiteSpace(folderPath))
        {
            await LoadFolderAsync(folderPath);
        }
    }

    private async void RescanButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentFolder) || !Directory.Exists(_currentFolder))
        {
            await ChooseFolderAsync();
            return;
        }

        await LoadFolderAsync(_currentFolder);
    }

    private async Task LoadFolderAsync(string folderPath)
    {
        _scanCancellation?.Cancel();
        _scanCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _scanCancellation = cancellation;

        StatusText.Text = $"正在扫描：{folderPath}";
        ScanProgressText.Text = "扫描中...";
        ScanStateText.Text = "后台递归扫描中...";
        ScanActivityIndicator.Visibility = Visibility.Visible;
        ChooseFolderButton.IsEnabled = false;
        RescanButton.IsEnabled = false;

        try
        {
            var records = await Task.Run(() =>
                _scanner.EnumerateImages(new ScanOptions(folderPath, Recursive: true), cancellation.Token)
                    .OrderByDescending(x => x.ModifiedAt)
                    .ToList(), cancellation.Token);

            cancellation.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_scanCancellation, cancellation))
            {
                return;
            }

            _allImages.Clear();
            _allImages.AddRange(records);
            _currentFolder = folderPath;
            _settings.LastFolder = folderPath;
            SettingsStore.Save(null, _settings);

            UpdateFileTypeFilterOptions(records);
            StartResolutionMetadataLoad(records);
            ApplyFilterAndSort();
            StatusText.Text = $"扫描完成：{_allImages.Count:N0} 张图片";
            ScanStateText.Text = "扫描已完成";
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(_scanCancellation, cancellation))
            {
                StatusText.Text = "扫描已取消";
                ScanStateText.Text = "扫描已取消";
            }
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("扫描失败", ex.Message);
            StatusText.Text = "扫描失败";
            ScanStateText.Text = "扫描失败";
        }
        finally
        {
            if (ReferenceEquals(_scanCancellation, cancellation))
            {
                _scanCancellation.Dispose();
                _scanCancellation = null;
                ChooseFolderButton.IsEnabled = true;
                RescanButton.IsEnabled = true;
                ScanActivityIndicator.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void UpdateFileTypeFilterOptions(IReadOnlyList<ImageRecord> records)
    {
        var selectedType = GetSelectedFilterTag(FileTypeFilterBox);
        var fileTypes = records
            .Select(record => Path.GetExtension(record.FullPath).ToLowerInvariant())
            .Where(extension => !string.IsNullOrWhiteSpace(extension))
            .GroupBy(extension => extension, StringComparer.OrdinalIgnoreCase)
            .Select(group => new { Extension = group.Key, Count = group.Count() })
            .OrderBy(item => item.Extension, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _suppressFilterChanges = true;
        FileTypeFilterBox.Items.Clear();
        FileTypeFilterBox.Items.Add(new ComboBoxItem
        {
            Content = $"全部类型（{records.Count:N0}）",
            Tag = "all"
        });

        foreach (var fileType in fileTypes)
        {
            FileTypeFilterBox.Items.Add(new ComboBoxItem
            {
                Content = $"{fileType.Extension.TrimStart('.').ToUpperInvariant()}（{fileType.Count:N0}）",
                Tag = fileType.Extension
            });
        }

        var selectedIndex = FileTypeFilterBox.Items
            .OfType<ComboBoxItem>()
            .Select((item, index) => new { Item = item, Index = index })
            .FirstOrDefault(entry => string.Equals(
                entry.Item.Tag?.ToString(),
                selectedType,
                StringComparison.OrdinalIgnoreCase))
            ?.Index ?? 0;
        FileTypeFilterBox.SelectedIndex = selectedIndex;
        _suppressFilterChanges = false;
        UpdateFilterButtonState();
    }

    private void StartResolutionMetadataLoad(IReadOnlyList<ImageRecord> records)
    {
        _resolutionMetadataCancellation?.Cancel();
        _resolutionMetadataCancellation?.Dispose();
        _resolutionMetadataCancellation = new CancellationTokenSource();
        _imageDimensions.Clear();
        _isResolutionMetadataReady = records.Count == 0;
        ResolutionFilterBox.IsEnabled = false;
        FilterMetadataStatusText.Text = records.Count == 0
            ? "当前没有可读取的图片"
            : $"正在读取分辨率：0 / {records.Count:N0}";

        if (records.Count > 0)
        {
            _ = LoadResolutionMetadataAsync(records, _resolutionMetadataCancellation);
        }
    }

    private async Task LoadResolutionMetadataAsync(
        IReadOnlyList<ImageRecord> records,
        CancellationTokenSource cancellation)
    {
        var completed = 0;
        try
        {
            await Parallel.ForEachAsync(
                records,
                new ParallelOptions
                {
                    CancellationToken = cancellation.Token,
                    MaxDegreeOfParallelism = 4
                },
                async (record, token) =>
                {
                    var dimensions = await ReadImageDimensionsAsync(record.FullPath, token);
                    token.ThrowIfCancellationRequested();
                    if (dimensions is ImageDimensions value)
                    {
                        _imageDimensions[record.FullPath] = value;
                    }

                    var current = Interlocked.Increment(ref completed);
                    if (current % 25 == 0 || current == records.Count)
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            if (ReferenceEquals(_resolutionMetadataCancellation, cancellation))
                            {
                                FilterMetadataStatusText.Text = $"正在读取分辨率：{current:N0} / {records.Count:N0}";
                            }
                        });
                    }
                });
        }
        catch (OperationCanceledException)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            if (!ReferenceEquals(_resolutionMetadataCancellation, cancellation))
            {
                return;
            }

            _isResolutionMetadataReady = true;
            ResolutionFilterBox.IsEnabled = _imageDimensions.Count > 0;
            FilterMetadataStatusText.Text = $"分辨率已读取：{_imageDimensions.Count:N0} / {records.Count:N0}";
            if (GetSelectedResolutionLevel() != ImageResolutionLevel.All)
            {
                ApplyFilterAndSort();
            }
        });
    }

    private void FilterNameBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing || _suppressFilterChanges)
        {
            return;
        }

        UpdateFilterButtonState();
        _filterApplyTimer.Stop();
        _filterApplyTimer.Start();
    }

    private void FilterApplyTimer_OnTick(UiDispatcherQueueTimer sender, object args)
    {
        ApplyFilterAndSort();
    }

    private void FilterBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing || _suppressFilterChanges)
        {
            return;
        }

        UpdateFilterButtonState();
        ApplyFilterAndSort();
    }

    private void ResetFiltersButton_OnClick(object sender, RoutedEventArgs e)
    {
        _filterApplyTimer.Stop();
        _suppressFilterChanges = true;
        FilterNameBox.Text = string.Empty;
        ResolutionFilterBox.SelectedIndex = 0;
        FileTypeFilterBox.SelectedIndex = 0;
        _suppressFilterChanges = false;
        UpdateFilterButtonState();
        ApplyFilterAndSort();
        FilterFlyout.Hide();
    }

    private void UpdateFilterButtonState()
    {
        var activeCount = 0;
        if (!string.IsNullOrWhiteSpace(FilterNameBox.Text))
        {
            activeCount++;
        }

        if (ResolutionFilterBox.SelectedIndex > 0)
        {
            activeCount++;
        }

        if (FileTypeFilterBox.SelectedIndex > 0)
        {
            activeCount++;
        }

        FilterButtonLabel.Text = activeCount == 0 ? "筛选" : $"筛选 {activeCount}";
        AutomationProperties.SetName(FilterButton, activeCount == 0 ? "筛选图片" : $"筛选图片，已启用 {activeCount} 项条件");
    }

    private void SortFieldButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isInitializing || sender is not Button { Tag: string sortFieldTag })
        {
            return;
        }

        if (!Enum.TryParse<ImageSortField>(sortFieldTag, out var sortField))
        {
            return;
        }

        _settings.Sorting.Field = sortField;
        SettingsStore.Save(null, _settings);
        UpdateSortFieldButtons(_settings.Sorting.Field, RootGrid.ActualTheme);
        ApplyFilterAndSort();
    }

    private void SortDirectionButton_OnClick(object sender, RoutedEventArgs e)
    {
        _settings.Sorting.Direction = _settings.Sorting.Direction == ImageSortDirection.Ascending
            ? ImageSortDirection.Descending
            : ImageSortDirection.Ascending;
        SettingsStore.Save(null, _settings);
        UpdateSortDirectionButton();
        ApplyFilterAndSort();
    }

    private void UpdateSortDirectionButton()
    {
        if (_settings.Sorting.Direction == ImageSortDirection.Ascending)
        {
            SortDirectionIcon.Glyph = "\uE70E";
            SortDirectionLabel.Text = "升序";
            ToolTipService.SetToolTip(SortDirectionButton, "当前为升序，点击切换为降序");
            AutomationProperties.SetName(SortDirectionButton, "排序方向：升序");
            return;
        }

        SortDirectionIcon.Glyph = "\uE70D";
        SortDirectionLabel.Text = "降序";
        ToolTipService.SetToolTip(SortDirectionButton, "当前为降序，点击切换为升序");
        AutomationProperties.SetName(SortDirectionButton, "排序方向：降序");
    }

    private void ThumbnailSlider_OnValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_isInitializing)
        {
            return;
        }

        if (_settings is null)
        {
            return;
        }

        _settings.Appearance.ThumbnailSize = (int)e.NewValue;
        var thumbnailSize = CalculateAdaptiveThumbnailSize();
        _renderedThumbnailSize = thumbnailSize;
        foreach (var item in Images)
        {
            item.UpdateThumbnailSize(thumbnailSize);
        }

        _thumbnailSettingsTimer.Stop();
        _thumbnailSettingsTimer.Start();
    }

    private void ThumbnailSettingsTimer_OnTick(UiDispatcherQueueTimer sender, object args)
    {
        SettingsStore.Save(null, _settings);
        ApplyFilterAndSort();
    }

    private void ThumbnailSlider_OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(ThumbnailSlider).Properties.MouseWheelDelta;
        var step = delta > 0 ? 10 : -10;
        ThumbnailSlider.Value = Math.Clamp(ThumbnailSlider.Value + step, ThumbnailSlider.Minimum, ThumbnailSlider.Maximum);
        e.Handled = true;
    }

    private void ThemeModeButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isInitializing || sender is not ToggleButton { Tag: string themeTag } toggleButton)
        {
            return;
        }

        if (Enum.TryParse<VeloPicThemeMode>(themeTag, out var theme))
        {
            toggleButton.IsChecked = true;
            _settings.Appearance.Theme = theme;
            ApplyTheme(theme);
            SettingsStore.Save(null, _settings);
        }
    }

    private void ApplyFilterAndSort()
    {
        if (Images is null)
        {
            return;
        }

        var resolutionLevel = GetSelectedResolutionLevel();
        var fileType = GetSelectedFilterTag(FileTypeFilterBox);
        var collectionFilter = _activeNavigationFilter switch
        {
            "favorites" => ImageCollectionFilter.Favorites,
            "tags" => ImageCollectionFilter.Tagged,
            "albums" => ImageCollectionFilter.Album,
            "wallpaper" => ImageCollectionFilter.WallpaperFavorites,
            _ => ImageCollectionFilter.All
        };
        var albumPaths = _activeAlbum is not null && _settings.Albums.TryGetValue(_activeAlbum, out var paths)
            ? paths
            : [];

        _filteredImages.Clear();
        _filteredImages.AddRange(_libraryQuery.Query(_allImages, new ImageQueryOptions
        {
            FileName = FilterNameBox?.Text ?? string.Empty,
            Resolution = _isResolutionMetadataReady ? resolutionLevel : ImageResolutionLevel.All,
            FileExtension = fileType,
            Category = _activeCategory,
            Collection = collectionFilter,
            Favorites = _collections.Favorites,
            WallpaperFavorites = _collections.WallpaperFavorites,
            AlbumPaths = albumPaths,
            Categories = _settings.Categories,
            Dimensions = _imageDimensions,
            SortField = _settings.Sorting.Field,
            SortDirection = _settings.Sorting.Direction
        }));

        Images.Clear();
        var thumbnailSize = CalculateAdaptiveThumbnailSize();
        _renderedThumbnailSize = thumbnailSize;
        foreach (var record in _filteredImages.Take(MaxInitialTiles))
        {
            Images.Add(new ImageTileViewModel(record, thumbnailSize, _collections.Favorites.Contains(record.FullPath)));
        }

        EmptyText.Visibility = Images.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateStatus();
    }

    private ImageResolutionLevel GetSelectedResolutionLevel()
    {
        return GetSelectedFilterTag(ResolutionFilterBox) switch
        {
            "4k" => ImageResolutionLevel.FourK,
            "2k" => ImageResolutionLevel.TwoK,
            "1k" => ImageResolutionLevel.OneK,
            _ => ImageResolutionLevel.All
        };
    }

    private static string GetSelectedFilterTag(ComboBox comboBox)
    {
        return comboBox.SelectedItem is ComboBoxItem { Tag: string tag } ? tag : "all";
    }

    private int CalculateAdaptiveThumbnailSize()
    {
        var targetSize = Math.Clamp(_settings.Appearance.ThumbnailSize, 120, 260);
        var availableWidth = ImageGridView?.ActualWidth ?? 0;
        if (availableWidth < targetSize + 48)
        {
            return targetSize;
        }

        // 预留列表内边距与滚动条；12 是卡片外边距和项目容器的平均占用。
        var usableWidth = Math.Max(targetSize + 12, availableWidth - 30);
        var columns = Math.Max(1, (int)Math.Round(usableWidth / (targetSize + 12), MidpointRounding.AwayFromZero));
        var thumbnailSize = (int)Math.Floor(usableWidth / columns - 12);
        var minimumAdaptiveSize = (int)Math.Floor(targetSize * 0.9);
        var maximumAdaptiveSize = (int)Math.Ceiling(targetSize * 1.15);

        if (thumbnailSize < minimumAdaptiveSize && columns > 1)
        {
            columns--;
            thumbnailSize = (int)Math.Floor(usableWidth / columns - 12);
        }
        else if (thumbnailSize > maximumAdaptiveSize)
        {
            columns++;
            thumbnailSize = (int)Math.Floor(usableWidth / columns - 12);
        }

        return Math.Max(96, thumbnailSize);
    }

    private void ThumbnailLayoutTimer_OnTick(UiDispatcherQueueTimer sender, object args)
    {
        var thumbnailSize = CalculateAdaptiveThumbnailSize();
        if (thumbnailSize == _renderedThumbnailSize)
        {
            return;
        }

        _renderedThumbnailSize = thumbnailSize;
        foreach (var item in Images)
        {
            item.UpdateThumbnailSize(thumbnailSize);
        }
    }

    private void UpdateStatus()
    {
        CurrentFolderText.Text = string.IsNullOrWhiteSpace(_currentFolder) ? "未选择目录" : _currentFolder;
        HeaderPathText.Text = string.IsNullOrWhiteSpace(_currentFolder) ? "尚未选择图片文件夹" : _currentFolder;
        FolderStatsText.Text = $"{_allImages.Count:N0} 张图片";
        UpdateStorageStatus();
        GalleryTitle.Text = string.IsNullOrWhiteSpace(_activeCategory)
            ? $"{GetNavigationTitle()}  {_filteredImages.Count:N0} 张图片"
            : $"{_activeCategory}  {_filteredImages.Count:N0} 张图片";
        ScanProgressText.Text = $"显示：{Images.Count:N0} / {_filteredImages.Count:N0}";
        CacheStatusText.Text = _filteredImages.Count > MaxInitialTiles
            ? $"已虚拟化加载前 {MaxInitialTiles:N0} 张，搜索可缩小范围"
            : "缩略图按需加载";
        UpdateCategoryCounts();
        UpdateCollectionActionLabels();
    }

    private void UpdateStorageStatus()
    {
        if (string.IsNullOrWhiteSpace(_currentFolder))
        {
            StorageDriveText.Text = "等待选择目录";
            StorageStatsText.Text = "选择文件夹后显示磁盘容量";
            StorageUsageBar.Value = 0;
            return;
        }

        try
        {
            var root = Path.GetPathRoot(_currentFolder);
            if (string.IsNullOrWhiteSpace(root))
            {
                return;
            }

            var drive = new DriveInfo(root);
            var usedBytes = Math.Max(0L, drive.TotalSize - drive.AvailableFreeSpace);
            var usagePercent = drive.TotalSize > 0 ? usedBytes * 100d / drive.TotalSize : 0d;
            StorageDriveText.Text = $"{drive.Name.TrimEnd('\\')}  {drive.VolumeLabel}".Trim();
            StorageStatsText.Text = $"可用 {FormatStorageSize(drive.AvailableFreeSpace)}，共 {FormatStorageSize(drive.TotalSize)}";
            StorageUsageBar.Value = Math.Clamp(usagePercent, 0d, 100d);
        }
        catch (Exception)
        {
            StorageDriveText.Text = Path.GetPathRoot(_currentFolder) ?? "当前磁盘";
            StorageStatsText.Text = "暂时无法读取磁盘容量";
            StorageUsageBar.Value = 0;
        }
    }

    private static string FormatStorageSize(long bytes)
    {
        const double gib = 1024d * 1024d * 1024d;
        const double tib = gib * 1024d;
        return bytes >= tib ? $"{bytes / tib:0.##} TB" : $"{bytes / gib:0.#} GB";
    }

    private string GetNavigationTitle()
    {
        return _activeNavigationFilter switch
        {
            "favorites" => "收藏",
            "recent" => "最近",
            "albums" => string.IsNullOrWhiteSpace(_activeAlbum) ? "相册" : _activeAlbum,
            "tags" => "标签",
            "wallpaper" => "壁纸收藏夹",
            _ => "当前目录"
        };
    }

    private void MetricsTimer_OnTick(UiDispatcherQueueTimer sender, object args)
    {
        UpdatePerformanceStatus();
    }

    private void UpdatePerformanceStatus()
    {
        var metrics = _metricsMonitor.Read();
        var gpuText = metrics.GpuUsagePercent is double gpuUsage
            ? $"{gpuUsage:0}%"
            : "--";

        PerformanceStatusText.Text =
            $"CPU {metrics.CpuUsagePercent:0}%  |  内存 {metrics.UsedMemoryGiB:0.0}/{metrics.TotalMemoryGiB:0.0} GB ({metrics.MemoryUsagePercent:0}%)  |  GPU {gpuText}";
    }

}
