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

    private readonly MediaFileScanner _scanner = new();
    private readonly MediaLibraryQueryService _libraryQuery = new();
    private readonly VideoThumbnailService _videoThumbnailService = new();
    private readonly SystemMetricsMonitor _metricsMonitor = new();
    private readonly AppSettings _settings;
    private readonly ImageCollectionService _collections;
    private readonly List<MediaRecord> _allMedia = [];
    private readonly List<MediaRecord> _filteredMedia = [];
    private readonly List<ImageRecord> _allImages = [];
    private readonly List<ImageRecord> _filteredImages = [];
    private readonly Dictionary<string, bool> _groupExpandedStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<FrameworkElement> _mediaGroupHeaders = [];
    private readonly ConcurrentDictionary<string, ImageDimensions> _imageDimensions = new(StringComparer.OrdinalIgnoreCase);
    private string _currentFolder = string.Empty;
    private bool _isInitializing = true;
    private readonly UiDispatcherQueueTimer _metricsTimer;
    private readonly UiDispatcherQueueTimer _thumbnailLayoutTimer;
    private readonly UiDispatcherQueueTimer _thumbnailSettingsTimer;
    private readonly UiDispatcherQueueTimer _filterApplyTimer;
    private readonly UiDispatcherQueueTimer _previewControlsHideTimer;
    private readonly UiDispatcherQueueTimer _previewProgressTimer;
    private readonly UiDispatcherQueueTimer _viewerControlsHideTimer;
    private readonly UiDispatcherQueueTimer _viewerProgressTimer;
    private int _renderedThumbnailSize;
    private bool _suppressFilterChanges;
    private bool _isResolutionMetadataReady;
    private CancellationTokenSource? _resolutionMetadataCancellation;
    private CancellationTokenSource? _scanCancellation;
    private CancellationTokenSource? _videoThumbnailCancellation;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private MediaKind _activeMediaKind = MediaKind.Image;

    public ObservableCollection<MediaTileViewModel> Images { get; } = [];

    public ObservableCollection<MediaGroupViewModel> MediaGroups { get; } = [];

    public MainWindow()
    {
        _settings = SettingsStore.Load();
        _settings.Sorting ??= new SortingSettings();
        _settings.CustomCategories ??= [];
        _collections = new ImageCollectionService(_settings);
        _currentFolder = _settings.LastFolder;

        InitializeComponent();
        InitializeSelectionButtonInteractions();
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
        _previewControlsHideTimer = DispatcherQueue.CreateTimer();
        _previewControlsHideTimer.Interval = TimeSpan.FromMilliseconds(2500);
        _previewControlsHideTimer.IsRepeating = false;
        _previewControlsHideTimer.Tick += PreviewControlsHideTimer_OnTick;
        _previewProgressTimer = DispatcherQueue.CreateTimer();
        _previewProgressTimer.Interval = TimeSpan.FromMilliseconds(250);
        _previewProgressTimer.Tick += PreviewProgressTimer_OnTick;
        _viewerControlsHideTimer = DispatcherQueue.CreateTimer();
        _viewerControlsHideTimer.Interval = TimeSpan.FromMilliseconds(2500);
        _viewerControlsHideTimer.IsRepeating = false;
        _viewerControlsHideTimer.Tick += ViewerControlsHideTimer_OnTick;
        _viewerProgressTimer = DispatcherQueue.CreateTimer();
        _viewerProgressTimer.Interval = TimeSpan.FromMilliseconds(250);
        _viewerProgressTimer.Tick += ViewerProgressTimer_OnTick;
        Closed += (_, _) =>
        {
            _metricsTimer.Stop();
            _thumbnailLayoutTimer.Stop();
            _thumbnailSettingsTimer.Stop();
            _filterApplyTimer.Stop();
            _previewControlsHideTimer.Stop();
            _previewProgressTimer.Stop();
            _viewerControlsHideTimer.Stop();
            _viewerProgressTimer.Stop();
            _lifetimeCancellation.Cancel();
            _scanCancellation?.Cancel();
            _scanCancellation?.Dispose();
            _resolutionMetadataCancellation?.Cancel();
            _resolutionMetadataCancellation?.Dispose();
            _videoThumbnailCancellation?.Cancel();
            _videoThumbnailCancellation?.Dispose();
            StopAndReleaseVideoPlayers();
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
        var folderPath = NativeFolderPicker.PickFolder(WindowNative.GetWindowHandle(this), "选择要导入 VeloPic 的媒体文件夹");
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
                _scanner.EnumerateMedia(new ScanOptions(folderPath, Recursive: true), cancellation.Token)
                    .OrderByDescending(x => x.ModifiedAt)
                    .ToList(), cancellation.Token);

            cancellation.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_scanCancellation, cancellation))
            {
                return;
            }

            var isNewRoot = !string.Equals(_currentFolder, folderPath, StringComparison.OrdinalIgnoreCase);
            if (isNewRoot)
            {
                _groupExpandedStates.Clear();
            }

            _allMedia.Clear();
            _allMedia.AddRange(records);
            _allImages.Clear();
            _allImages.AddRange(records.Where(record => record.Kind == MediaKind.Image).Select(record => record.AsImageRecord()));
            _currentFolder = folderPath;
            _settings.LastFolder = folderPath;
            SettingsStore.Save(null, _settings);

            UpdateFileTypeFilterOptions();
            StartResolutionMetadataLoad(_allImages);
            ApplyFilterAndSort();
            UpdateSmartClassificationButtonState();
            var videoCount = records.Count(record => record.Kind == MediaKind.Video);
            StatusText.Text = $"扫描完成：{_allImages.Count:N0} 张图片，{videoCount:N0} 个视频";
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

    private void UpdateFileTypeFilterOptions()
    {
        var records = _allMedia.Where(record => record.Kind == _activeMediaKind).ToList();
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
        OrientationFilterBox.IsEnabled = false;
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
            OrientationFilterBox.IsEnabled = _imageDimensions.Count > 0;
            FilterMetadataStatusText.Text = $"分辨率已读取：{_imageDimensions.Count:N0} / {records.Count:N0}";
            if (GetSelectedResolutionLevel() != ImageResolutionLevel.All ||
                GetSelectedOrientation() != ImageOrientation.All)
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
        OrientationFilterBox.SelectedIndex = 0;
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

        if (OrientationFilterBox.SelectedIndex > 0)
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
        foreach (var group in MediaGroups)
        {
            group.UpdateThumbnailSize(thumbnailSize);
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
        var orientation = GetSelectedOrientation();
        var fileType = GetSelectedFilterTag(FileTypeFilterBox);
        var collectionFilter = _activeMediaKind == MediaKind.Image ? _activeNavigationFilter switch
        {
            "favorites" => ImageCollectionFilter.Favorites,
            "albums" => ImageCollectionFilter.Album,
            "wallpaper" => ImageCollectionFilter.WallpaperFavorites,
            _ => ImageCollectionFilter.All
        } : ImageCollectionFilter.All;
        var albumPaths = _activeAlbum is not null && _settings.Albums.TryGetValue(_activeAlbum, out var paths)
            ? paths
            : [];

        _filteredMedia.Clear();
        _filteredMedia.AddRange(_libraryQuery.Query(_allMedia, new MediaQueryOptions
        {
            Kind = _activeMediaKind,
            FileName = FilterNameBox?.Text ?? string.Empty,
            Resolution = _activeMediaKind == MediaKind.Image && _isResolutionMetadataReady
                ? resolutionLevel
                : ImageResolutionLevel.All,
            Orientation = _activeMediaKind == MediaKind.Image && _isResolutionMetadataReady
                ? orientation
                : ImageOrientation.All,
            FileExtension = fileType,
            Category = _activeMediaKind == MediaKind.Image ? _activeCategory : null,
            Collection = collectionFilter,
            Favorites = _collections.Favorites,
            WallpaperFavorites = _collections.WallpaperFavorites,
            AlbumPaths = albumPaths,
            Categories = _settings.Categories,
            Dimensions = _imageDimensions,
            SortField = _settings.Sorting.Field,
            SortDirection = _settings.Sorting.Direction
        }));

        _filteredImages.Clear();
        if (_activeMediaKind == MediaKind.Image)
        {
            _filteredImages.AddRange(_filteredMedia.Select(record => record.AsImageRecord()));
        }

        _videoThumbnailCancellation?.Cancel();
        _videoThumbnailCancellation?.Dispose();
        _videoThumbnailCancellation = new CancellationTokenSource();
        Images.Clear();
        MediaGroups.Clear();
        var thumbnailSize = CalculateAdaptiveThumbnailSize();
        _renderedThumbnailSize = thumbnailSize;
        var limitedRecords = _filteredMedia.Take(MaxInitialTiles).ToList();
        var itemsByPath = new Dictionary<string, MediaTileViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in limitedRecords)
        {
            var item = new MediaTileViewModel(
                record,
                thumbnailSize,
                _collections.Favorites.Contains(record.FullPath));
            Images.Add(item);
            itemsByPath[record.FullPath] = item;
        }

        var groupedRecords = string.IsNullOrWhiteSpace(_currentFolder)
            ? []
            : MediaDirectoryGrouping.Create(limitedRecords, _currentFolder);
        foreach (var group in groupedRecords)
        {
            var items = group.Items
                .Select(record => itemsByPath[record.FullPath])
                .ToList();

            var stateKey = GetGroupStateKey(_activeMediaKind, group.DirectoryPath);
            var isExpanded = !_groupExpandedStates.TryGetValue(stateKey, out var expanded) || expanded;
            MediaGroups.Add(new MediaGroupViewModel(group.DirectoryPath, group.Title, items, isExpanded));
        }

        GroupedMediaSource.Source = MediaGroups;
        ApplyBrowseModeSource();
        if (_activeMediaKind == MediaKind.Video)
        {
            _ = LoadVideoThumbnailsAsync(Images, thumbnailSize, _videoThumbnailCancellation);
        }

        EmptyText.Visibility = Images.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateStatus();
    }

    private static string GetGroupStateKey(MediaKind kind, string directoryPath) => $"{kind}|{directoryPath}";

    private async Task LoadVideoThumbnailsAsync(
        IReadOnlyCollection<MediaTileViewModel> items,
        int thumbnailSize,
        CancellationTokenSource cancellation)
    {
        try
        {
            await Task.WhenAll(items
                .Where(item => item.IsVideo)
                .Select(async item =>
                {
                    var thumbnail = await _videoThumbnailService.LoadAsync(
                        item.FullPath,
                        (uint)Math.Max(160, thumbnailSize),
                        cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    if (thumbnail is not null && ReferenceEquals(_videoThumbnailCancellation, cancellation))
                    {
                        item.SetThumbnail(thumbnail);
                    }
                }));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void MediaGroupHeader_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement header)
        {
            _mediaGroupHeaders.Add(header);
            UpdateMediaGroupHeaderWidth(header);
        }
    }

    private void MediaGroupHeader_OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement header)
        {
            _mediaGroupHeaders.Remove(header);
        }
    }

    private void ImageGridView_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateMediaGroupHeaderWidths();
    }

    private void UpdateMediaGroupHeaderWidths()
    {
        foreach (var header in _mediaGroupHeaders.ToList())
        {
            UpdateMediaGroupHeaderWidth(header);
        }
    }

    private void UpdateMediaGroupHeaderWidth(FrameworkElement header)
    {
        // 为 GridView 内边距、项目容器和垂直滚动条预留空间，避免数量被遮挡。
        header.Width = Math.Max(280, ImageGridView.ActualWidth - 76);
    }

    private void MediaGroupHeader_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: MediaGroupViewModel group })
        {
            return;
        }

        if (!group.Toggle())
        {
            return;
        }

        if (!group.IsExpanded &&
            _selectedImage is not null &&
            group.AllItems.Contains(_selectedImage))
        {
            ClearCurrentSelection();
        }

        _groupExpandedStates[GetGroupStateKey(_activeMediaKind, group.DirectoryPath)] = group.IsExpanded;
        NotifyMediaGroupChanged(group);
        UpdateDirectoryGroupActionState();
        RefreshGroupedMediaLayout();
    }

    private void CollapseAllGroupsButton_OnClick(object sender, RoutedEventArgs e)
    {
        SetAllMediaGroupsExpanded(false);
    }

    private void ExpandAllGroupsButton_OnClick(object sender, RoutedEventArgs e)
    {
        SetAllMediaGroupsExpanded(true);
    }

    private void SetAllMediaGroupsExpanded(bool isExpanded)
    {
        if (_settings.Appearance.BrowseMode != MediaBrowseMode.DirectoryGrouped)
        {
            return;
        }

        if (!isExpanded && _selectedImage is not null)
        {
            ClearCurrentSelection();
        }

        var changed = false;
        for (var index = 0; index < MediaGroups.Count; index++)
        {
            var group = MediaGroups[index];
            if (group.SetExpanded(isExpanded))
            {
                // CollectionViewSource 对组内集合变更的虚拟化刷新并不稳定。
                // 顶层 Replace 通知会让它立即重新读取 VisibleItems，且不会重建媒体项。
                MediaGroups[index] = group;
                changed = true;
            }

            _groupExpandedStates[GetGroupStateKey(_activeMediaKind, group.DirectoryPath)] = isExpanded;
        }

        UpdateDirectoryGroupActionState();
        if (changed)
        {
            RefreshGroupedMediaLayout();
        }
    }

    private void NotifyMediaGroupChanged(MediaGroupViewModel group)
    {
        var index = MediaGroups.IndexOf(group);
        if (index >= 0)
        {
            MediaGroups[index] = group;
        }
    }

    private void RefreshGroupedMediaLayout()
    {
        if (_settings.Appearance.BrowseMode != MediaBrowseMode.DirectoryGrouped)
        {
            return;
        }

        UpdateMediaGroupHeaderWidths();
        ImageGridView.InvalidateMeasure();
        ImageGridView.InvalidateArrange();
        DispatcherQueue.TryEnqueue(() =>
        {
            UpdateMediaGroupHeaderWidths();
            ImageGridView.InvalidateMeasure();
            ImageGridView.InvalidateArrange();
            ImageGridView.UpdateLayout();
        });
    }

    private void BrowseModeButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: string modeTag } ||
            !Enum.TryParse<MediaBrowseMode>(modeTag, out var mode))
        {
            UpdateBrowseModeButtons();
            return;
        }

        if (_settings.Appearance.BrowseMode == mode)
        {
            UpdateBrowseModeButtons();
            return;
        }

        var selectedPath = _selectedImage?.FullPath;
        ImageGridView.SelectedItems.Clear();
        _selectedImage = null;
        _settings.Appearance.BrowseMode = mode;
        SettingsStore.Save(null, _settings);
        ApplyBrowseModeSource();
        RestoreSelectionAfterBrowseModeChange(selectedPath);
        StatusText.Text = mode == MediaBrowseMode.Flat ? "已切换到平铺浏览" : "已切换到按目录浏览";
    }

    private void ApplyBrowseModeSource()
    {
        var isGrouped = _settings.Appearance.BrowseMode == MediaBrowseMode.DirectoryGrouped;
        ImageGridView.ItemsSource = isGrouped ? GroupedMediaSource.View : Images;
        UpdateBrowseModeButtons();
        if (isGrouped)
        {
            RefreshGroupedMediaLayout();
        }
    }

    private void RestoreSelectionAfterBrowseModeChange(string? selectedPath)
    {
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            _ = UpdateSelectionDetails(null);
            return;
        }

        var selectedItem = Images.FirstOrDefault(item =>
            string.Equals(item.FullPath, selectedPath, StringComparison.OrdinalIgnoreCase));
        if (selectedItem is null ||
            _settings.Appearance.BrowseMode == MediaBrowseMode.DirectoryGrouped &&
            !MediaGroups.Any(group => group.IsExpanded && group.VisibleItems.Contains(selectedItem)))
        {
            _ = UpdateSelectionDetails(null);
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            ImageGridView.SelectedItem = selectedItem;
            _selectedImage = selectedItem;
            _ = UpdateSelectionDetails(selectedItem);
            ImageGridView.ScrollIntoView(selectedItem);
        });
    }

    private void UpdateBrowseModeButtons()
    {
        var isGrouped = _settings.Appearance.BrowseMode == MediaBrowseMode.DirectoryGrouped;
        FlatBrowseModeButton.IsChecked = !isGrouped;
        DirectoryBrowseModeButton.IsChecked = isGrouped;
        DirectoryGroupActions.Visibility = isGrouped ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(FlatBrowseModeButton, $"平铺浏览{(!isGrouped ? "，当前已选" : string.Empty)}");
        AutomationProperties.SetName(DirectoryBrowseModeButton, $"按目录浏览{(isGrouped ? "，当前已选" : string.Empty)}");
        UpdateDirectoryGroupActionState();
    }

    private void UpdateDirectoryGroupActionState()
    {
        var isGrouped = _settings.Appearance.BrowseMode == MediaBrowseMode.DirectoryGrouped;
        CollapseAllGroupsButton.IsEnabled = isGrouped && MediaGroups.Any(group => group.IsExpanded);
        ExpandAllGroupsButton.IsEnabled = isGrouped && MediaGroups.Any(group => !group.IsExpanded);
    }

    private void MediaKindButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: string kindTag } ||
            !Enum.TryParse<MediaKind>(kindTag, out var kind) ||
            kind == _activeMediaKind)
        {
            UpdateMediaKindButtons();
            return;
        }

        StopAndReleaseVideoPlayers();
        ClearCurrentSelection();
        _activeMediaKind = kind;
        if (kind == MediaKind.Video && _activeNavigationFilter is not null)
        {
            _activeNavigationFilter = null;
            _activeNavigationSection = "folder";
            _activeAlbum = null;
            _activeCategory = null;
            SetNavigationSelection("folder");
            SetCategorySelection(null, showAll: true);
        }

        _suppressFilterChanges = true;
        ResolutionFilterBox.SelectedIndex = 0;
        OrientationFilterBox.SelectedIndex = 0;
        _suppressFilterChanges = false;
        ResolutionFilterBox.IsEnabled = kind == MediaKind.Image && _isResolutionMetadataReady && _imageDimensions.Count > 0;
        OrientationFilterBox.IsEnabled = kind == MediaKind.Image && _isResolutionMetadataReady && _imageDimensions.Count > 0;
        UpdateImageOnlyNavigationState();
        UpdateSelectionActionState(false, false);
        _ = UpdateSelectionDetails(null);
        UpdateMediaKindButtons();
        UpdateFileTypeFilterOptions();
        UpdateFilterButtonState();
        ApplyFilterAndSort();
    }

    private void UpdateMediaKindButtons()
    {
        ImageKindButton.IsChecked = _activeMediaKind == MediaKind.Image;
        VideoKindButton.IsChecked = _activeMediaKind == MediaKind.Video;
        FilterPanelTitle.Text = _activeMediaKind == MediaKind.Image ? "筛选图片" : "筛选视频";
        EmptyMessageText.Text = _activeMediaKind == MediaKind.Image
            ? "选择一个图片文件夹开始浏览"
            : "选择一个包含视频的文件夹开始浏览";
        ResolutionFilterSection.Visibility = _activeMediaKind == MediaKind.Image
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void UpdateImageOnlyNavigationState()
    {
        var isImage = _activeMediaKind == MediaKind.Image;
        foreach (var element in new UIElement[]
                 {
                     FavoritesNavigationItem,
                     AlbumsNavigationItem,
                     WallpaperNavigationItem,
                     ManualClassificationButton,
                     SmartClassificationButton,
                     AllPhotosCategoryButton,
                     CategoryListPanel
                 })
        {
            element.IsHitTestVisible = isImage;
            element.Opacity = isImage ? 1 : 0.45;
        }
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

    private ImageOrientation GetSelectedOrientation()
    {
        return GetSelectedFilterTag(OrientationFilterBox) switch
        {
            "landscape" => ImageOrientation.Landscape,
            "portrait" => ImageOrientation.Portrait,
            _ => ImageOrientation.All
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
        foreach (var group in MediaGroups)
        {
            group.UpdateThumbnailSize(thumbnailSize);
        }
    }

    private void UpdateStatus()
    {
        CurrentFolderText.Text = string.IsNullOrWhiteSpace(_currentFolder) ? "未选择目录" : _currentFolder;
        HeaderPathText.Text = string.IsNullOrWhiteSpace(_currentFolder) ? "尚未选择媒体文件夹" : _currentFolder;
        var totalCount = _allMedia.Count(record => record.Kind == _activeMediaKind);
        var unit = _activeMediaKind == MediaKind.Image ? "张图片" : "个视频";
        FolderStatsText.Text = $"{totalCount:N0} {unit}";
        ScanProgressText.Text = $"显示：{Images.Count:N0} / {_filteredMedia.Count:N0}";
        CacheStatusText.Text = _filteredMedia.Count > MaxInitialTiles
            ? $"已虚拟化加载前 {MaxInitialTiles:N0} 个，筛选可缩小范围"
            : "缩略图按需加载";
        UpdateCategoryCounts();
        UpdateCollectionActionLabels();
    }

    private string GetNavigationTitle()
    {
        return _activeNavigationFilter switch
        {
            "favorites" => "收藏",
            "recent" => "最近",
            "albums" => string.IsNullOrWhiteSpace(_activeAlbum) ? "相册" : _activeAlbum,
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

        CpuStatusText.Text = $"CPU {metrics.CpuUsagePercent:0}%";
        MemoryStatusText.Text =
            $"内存 {metrics.UsedMemoryGiB:0.0}/{metrics.TotalMemoryGiB:0.0} GB ({metrics.MemoryUsagePercent:0}%)";
        GpuStatusText.Text = $"GPU {gpuText}";
    }

}
