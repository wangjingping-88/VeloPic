using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using VeloPic.Core;

namespace VeloPic.App;

public sealed partial class MainWindow : Window
{
    private readonly WallpaperService _wallpaperService = new();

    private async void ImageGridView_OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is MediaTileViewModel item)
        {
            _selectedImage = item;
            var selectedCount = ImageGridView.SelectedItems.Count;
            CategoryHintText.Text = item.IsVideo
                ? selectedCount > 1
                    ? $"已选中 {selectedCount} 个视频，可双击进入沉浸播放"
                    : $"已选中“{item.FileName}”，可在右侧预览或双击播放"
                : selectedCount > 1
                    ? $"已选中 {selectedCount} 张图片，使用“标记”可批量归类"
                    : $"已选中“{item.FileName}”，使用“标记”归类";
            await UpdateSelectionDetails(item);
        }
    }

    private void ImageGridView_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ImageGridView.SelectedItems.Count > 0)
        {
            return;
        }

        _selectedImage = null;
        _ = UpdateSelectionDetails(null);
    }

    private async void ImageGridView_OnRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var container = FindGridViewItem(e.OriginalSource as DependencyObject);
        if (container?.Content is not MediaTileViewModel item)
        {
            return;
        }

        if (!ImageGridView.SelectedItems.Contains(item))
        {
            ImageGridView.SelectedItems.Clear();
            ImageGridView.SelectedItem = item;
        }

        _selectedImage = item;
        var selectedCount = ImageGridView.SelectedItems.Count;
        CategoryHintText.Text = item.IsVideo
            ? $"已选中“{item.FileName}”，可查看或打开文件位置"
            : selectedCount > 1
                ? $"已选中 {selectedCount} 张图片，右键操作将应用到所选图片"
                : $"已选中“{item.FileName}”，可使用右键菜单操作";
        await UpdateSelectionDetails(item);

        var menu = item.IsVideo ? CreateVideoContextMenu() : CreateImageContextMenu();
        menu.ShowAt(container, e.GetPosition(container));
        e.Handled = true;
    }

    private MenuFlyout CreateImageContextMenu()
    {
        var records = GetSelectedRecords();
        var allFavorited = records.Count > 0 && records.All(record => _collections.Favorites.Contains(record.FullPath));
        var allWallpaperFavorited = records.Count > 0 && records.All(record => _collections.WallpaperFavorites.Contains(record.FullPath));
        var menu = new MenuFlyout();

        menu.Items.Add(CreateImageContextMenuItem("查看", "\uE890", (_, _) => OpenViewer()));
        menu.Items.Add(CreateImageContextMenuItem(
            allFavorited ? "取消收藏" : "收藏",
            "\uE734",
            FavoriteButton_OnClick));

        var categoryMenu = new MenuFlyoutSubItem
        {
            Text = "标记分类",
            Icon = new FontIcon { Glyph = "\uE8EC" }
        };
        foreach (var category in BuiltInCategories
                     .Concat(_settings.CustomCategories)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var categoryItem = new MenuFlyoutItem
            {
                Text = category,
                Tag = category
            };
            categoryItem.Click += ManualCategoryMenuItem_OnClick;
            categoryMenu.Items.Add(categoryItem);
        }

        menu.Items.Add(categoryMenu);
        var removableCategories = records
            .SelectMany(record => _settings.Categories.TryGetValue(record.FullPath, out var categories)
                ? categories
                : [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(category => category, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var removeCategoryMenu = new MenuFlyoutSubItem
        {
            Text = "移除标签",
            Icon = new FontIcon { Glyph = "\uE711" },
            IsEnabled = removableCategories.Count > 0
        };
        foreach (var category in removableCategories)
        {
            var categoryItem = new MenuFlyoutItem
            {
                Text = category,
                Tag = category
            };
            categoryItem.Click += RemoveCategoryMenuItem_OnClick;
            removeCategoryMenu.Items.Add(categoryItem);
        }

        menu.Items.Add(removeCategoryMenu);
        menu.Items.Add(CreateImageContextMenuItem(
            IsViewingAlbum() ? "移出当前相册" : "加入相册...",
            "\uEB9F",
            AddToAlbumButton_OnClick));
        menu.Items.Add(CreateImageContextMenuItem(
            allWallpaperFavorited ? "移出壁纸收藏" : "壁纸收藏",
            "\uE7F4",
            WallpaperFavoriteButton_OnClick));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(CreateImageContextMenuItem("打开文件位置", "\uE8B7", OpenLocationButton_OnClick));
        menu.Items.Add(CreateImageContextMenuItem("删除到回收站", "\uE74D", DeleteButton_OnClick));
        return menu;
    }

    private MenuFlyout CreateVideoContextMenu()
    {
        var menu = new MenuFlyout();
        menu.Items.Add(CreateImageContextMenuItem("查看", "\uE890", (_, _) => OpenViewer()));
        menu.Items.Add(CreateImageContextMenuItem("打开文件位置", "\uE8B7", OpenLocationButton_OnClick));
        return menu;
    }

    private void MoreActionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement anchor && _selectedImage is not null)
        {
            (_selectedImage.IsVideo ? CreateVideoContextMenu() : CreateImageContextMenu()).ShowAt(anchor);
        }
    }

    private static MenuFlyoutItem CreateImageContextMenuItem(
        string text,
        string glyph,
        RoutedEventHandler clickHandler)
    {
        var item = new MenuFlyoutItem
        {
            Text = text,
            Icon = new FontIcon { Glyph = glyph }
        };
        item.Click += clickHandler;
        return item;
    }

    private static GridViewItem? FindGridViewItem(DependencyObject? source)
    {
        while (source is not null && source is not GridViewItem)
        {
            source = VisualTreeHelper.GetParent(source);
        }

        return source as GridViewItem;
    }

    private void ImageGridView_OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        OpenViewer();
    }

    private void ViewButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenViewer();
    }

    private void FavoriteButton_OnClick(object sender, RoutedEventArgs e)
    {
        var records = GetSelectedRecords();
        if (records.Count == 0)
        {
            StatusText.Text = "请先选择一张或多张图片";
            return;
        }

        var shouldAdd = records.Any(record => !_collections.Favorites.Contains(record.FullPath));
        _collections.SetFavorite(records.Select(record => record.FullPath), shouldAdd);
        SettingsStore.Save(null, _settings);
        var isViewingFavorites = string.Equals(
            _activeNavigationFilter,
            "favorites",
            StringComparison.OrdinalIgnoreCase);
        if (isViewingFavorites)
        {
            ApplyFilterAndSort();
            ClearCurrentSelection();
        }
        else
        {
            UpdateVisibleFavoriteMarks(records);
            UpdateCollectionActionLabels();
        }

        StatusText.Text = shouldAdd
            ? $"已收藏 {records.Count:N0} 张图片"
            : $"已取消收藏 {records.Count:N0} 张图片";
    }

    private void UpdateVisibleFavoriteMarks(IReadOnlyCollection<ImageRecord> records)
    {
        var changedPaths = records
            .Select(record => record.FullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var tile in Images.Where(tile => changedPaths.Contains(tile.FullPath)))
        {
            tile.SetFavorite(_collections.Favorites.Contains(tile.FullPath));
        }
    }

    private List<ImageRecord> GetSelectedRecords()
    {
        var selected = ImageGridView.SelectedItems
            .OfType<MediaTileViewModel>()
            .Where(item => item.Record.Kind == MediaKind.Image)
            .Select(item => item.Record.AsImageRecord())
            .ToList();
        if (selected.Count == 0 && _selectedImage is { Record.Kind: MediaKind.Image })
        {
            selected.Add(_selectedImage.Record.AsImageRecord());
        }

        return selected
            .DistinctBy(item => item.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private bool IsViewingAlbum()
    {
        return string.Equals(_activeNavigationFilter, "albums", StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(_activeAlbum);
    }

    private void UpdateCollectionActionLabels()
    {
        var selectedPath = _selectedImage?.FullPath;
        FavoriteButtonLabel.Text = selectedPath is not null && _collections.Favorites.Contains(selectedPath)
            ? "取消收藏"
            : "收藏";
        WallpaperFavoriteButtonLabel.Text = selectedPath is not null && _collections.WallpaperFavorites.Contains(selectedPath)
            ? "移出收藏"
            : "壁纸收藏";
        AlbumActionButtonLabel.Text = IsViewingAlbum() ? "移出相册" : "加入相册";
    }

    private void ClearCurrentSelection()
    {
        ImageGridView.SelectedItems.Clear();
        _selectedImage = null;
        _ = UpdateSelectionDetails(null);
    }

    private void RemoveFromActiveAlbum(IReadOnlyCollection<ImageRecord> records)
    {
        if (_activeAlbum is null)
        {
            return;
        }

        var albumName = _activeAlbum;
        var result = _collections.RemoveFromAlbum(albumName, records.Select(record => record.FullPath));
        if (result.RemovedCount == 0)
        {
            StatusText.Text = "选中的图片不在当前相册中";
            return;
        }

        if (result.AlbumDeleted)
        {
            _activeAlbum = null;
            _activeNavigationFilter = null;
            _activeNavigationSection = "folder";
            _activeCategory = null;
            SetNavigationSelection("folder");
            SetCategorySelection(null, showAll: true);
        }

        SettingsStore.Save(null, _settings);
        RebuildAlbumNavigation();
        ApplyFilterAndSort();
        ClearCurrentSelection();
        StatusText.Text = result.AlbumDeleted
            ? $"已移出 {result.RemovedCount:N0} 张图片，空相册“{albumName}”已删除"
            : $"已从相册“{albumName}”移出 {result.RemovedCount:N0} 张图片";
    }

    private async void AddToAlbumButton_OnClick(object sender, RoutedEventArgs e)
    {
        var records = GetSelectedRecords();
        if (records.Count == 0)
        {
            await ShowMessageAsync(IsViewingAlbum() ? "移出相册" : "加入相册", "请先选择一张或多张图片。");
            return;
        }

        if (IsViewingAlbum())
        {
            RemoveFromActiveAlbum(records);
            return;
        }

        var albumBox = new ComboBox
        {
            Height = 36,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = _settings.Albums.Keys.OrderBy(name => name, StringComparer.CurrentCulture).ToList(),
            PlaceholderText = "选择已有相册"
        };
        var newAlbumBox = new TextBox
        {
            PlaceholderText = "或输入新相册名称",
            Height = 36,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Style = Application.Current.Resources["SingleLineTextBoxStyle"] as Style
        };
        var content = new StackPanel { Spacing = 8, MinWidth = 340 };
        content.Children.Add(new TextBlock { Text = $"将 {records.Count:N0} 张图片加入相册" });
        content.Children.Add(albumBox);
        content.Children.Add(newAlbumBox);

        var dialog = new ContentDialog
        {
            Title = "加入相册",
            Content = content,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var albumName = newAlbumBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(albumName))
        {
            albumName = albumBox.SelectedItem as string ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(albumName))
        {
            await ShowMessageAsync("加入相册", "请选择已有相册或输入新相册名称。");
            return;
        }

        _collections.AddToAlbum(albumName, records.Select(record => record.FullPath));

        SettingsStore.Save(null, _settings);
        RebuildAlbumNavigation();
        StatusText.Text = $"已将 {records.Count:N0} 张图片加入相册：{albumName}";
    }

    private void WallpaperFavoriteButton_OnClick(object sender, RoutedEventArgs e)
    {
        var records = GetSelectedRecords();
        if (records.Count == 0)
        {
            StatusText.Text = "请先选择一张或多张图片";
            return;
        }

        var shouldAdd = records.Any(record => !_collections.WallpaperFavorites.Contains(record.FullPath));
        _collections.SetWallpaperFavorite(records.Select(record => record.FullPath), shouldAdd);
        SettingsStore.Save(null, _settings);
        var isViewingWallpaperFavorites = string.Equals(
            _activeNavigationFilter,
            "wallpaper",
            StringComparison.OrdinalIgnoreCase);
        if (isViewingWallpaperFavorites)
        {
            ApplyFilterAndSort();
            ClearCurrentSelection();
        }
        else
        {
            UpdateCollectionActionLabels();
        }

        StatusText.Text = shouldAdd
            ? $"已将 {records.Count:N0} 张图片加入壁纸收藏夹"
            : $"已从壁纸收藏夹移除 {records.Count:N0} 张图片";
    }

    private void WallpaperFitButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: string fitMode })
        {
            return;
        }

        _settings.Appearance.WallpaperFit = fitMode;
        SettingsStore.Save(null, _settings);
        UpdateWallpaperFitButtons();
    }

    private void UpdateWallpaperFitButtons()
    {
        foreach (var button in new[]
                 {
                     WallpaperFillButton,
                     WallpaperFitButton,
                     WallpaperCenterButton,
                     WallpaperTileButton,
                     WallpaperStretchButton
                 })
        {
            var isSelected = button.Tag is string fitMode &&
                             string.Equals(fitMode, _settings.Appearance.WallpaperFit, StringComparison.OrdinalIgnoreCase);
            button.IsChecked = isSelected;
            ApplyContentForeground(button, isSelected, pointerOver: false, RootGrid.ActualTheme);
            var label = button.Tag switch
            {
                "fit" => "适中",
                "center" => "居中",
                "tile" => "平铺",
                "stretch" => "拉伸",
                _ => "填充"
            };
            AutomationProperties.SetName(button, $"壁纸显示：{label}{(isSelected ? "，当前已选" : string.Empty)}");
        }
    }

    private async void WallpaperButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedImage is null)
        {
            return;
        }

        try
        {
            _wallpaperService.SetWallpaper(_selectedImage.FullPath, _settings.Appearance.WallpaperFit);
            StatusText.Text = $"已设为壁纸：{_selectedImage.FileName}";
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("设置壁纸失败", ex.Message);
        }
    }

    private void OpenLocationButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedImage is null)
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{_selectedImage.FullPath}\"",
            UseShellExecute = true
        });
    }

    private async void DeleteButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_selectedImage is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "删除到回收站？",
            Content = _selectedImage.FullPath,
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            XamlRoot = RootGrid.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                _selectedImage.FullPath,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);

            var deletedPath = _selectedImage.FullPath;
            _collections.RemovePath(deletedPath);
            SettingsStore.Save(null, _settings);
            _allImages.RemoveAll(x => string.Equals(x.FullPath, _selectedImage.FullPath, StringComparison.OrdinalIgnoreCase));
            _allMedia.RemoveAll(x => string.Equals(x.FullPath, _selectedImage.FullPath, StringComparison.OrdinalIgnoreCase));
            _selectedImage = null;
            await UpdateSelectionDetails(null);
            ApplyFilterAndSort();
            StatusText.Text = "已删除到回收站";
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("删除失败", ex.Message);
        }
    }

}
