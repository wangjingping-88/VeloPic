using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using VeloPic.Core;

namespace VeloPic.App;

public sealed partial class MainWindow : Window
{
    private static readonly string[] BuiltInCategories = ["风景", "城市", "人物", "自然"];
    private readonly Dictionary<string, Button> _categoryButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextBlock> _categoryCountTexts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Button> _albumButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Button> _selectedButtons = [];
    private SmartClassificationService? _smartClassificationService;
    private string? _activeCategory;
    private string? _activeNavigationFilter;
    private string? _activeAlbum;
    private string _activeNavigationSection = "folder";
    private bool _albumsExpanded;
    private bool _isSmartClassifying;

    private void UpdateCategoryCounts()
    {
        AllPhotosCountText.Text = _allImages.Count.ToString("N0");
        foreach (var (category, countText) in _categoryCountTexts)
        {
            countText.Text = CountCategory(category).ToString("N0");
        }
    }

    private void RebuildCategoryControls()
    {
        foreach (var button in _categoryButtons.Values)
        {
            UnregisterSelectionButton(button);
        }

        CategoryListPanel.Children.Clear();
        _categoryButtons.Clear();
        _categoryCountTexts.Clear();

        var categories = BuiltInCategories
            .Concat(_settings.CustomCategories)
            .Where(category => !string.IsNullOrWhiteSpace(category))
            .Select(category => category.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _settings.CustomCategories = categories
            .Where(category => !BuiltInCategories.Contains(category, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var menuFlyout = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight };
        foreach (var category in categories)
        {
            var categoryButton = CreateCategoryButton(category, out var countText);
            CategoryListPanel.Children.Add(categoryButton);
            _categoryButtons[category] = categoryButton;
            _categoryCountTexts[category] = countText;

            var menuItem = new MenuFlyoutItem
            {
                Text = $"标记为{category}",
                Tag = category
            };
            menuItem.Click += ManualCategoryMenuItem_OnClick;
            menuFlyout.Items.Add(menuItem);
        }

        ManualClassificationButton.Flyout = menuFlyout;
        UpdateCategoryCounts();
    }

    private Button CreateCategoryButton(string category, out TextBlock countText)
    {
        var grid = new Grid { Padding = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock
        {
            Text = category,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        countText = new TextBlock
        {
            Text = "0",
            FontSize = 11,
            MinWidth = 34,
            Margin = new Thickness(8, 0, 4, 0),
            TextAlignment = TextAlignment.Right,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Style = Application.Current.Resources["SecondaryTextStyle"] as Style
        };
        Grid.SetColumn(countText, 1);
        grid.Children.Add(label);
        grid.Children.Add(countText);

        var button = new Button
        {
            Tag = category,
            Style = Application.Current.Resources["CategoryItemButtonStyle"] as Style,
            Content = grid
        };
        RegisterSelectionButton(button);
        button.Click += CategoryButton_OnClick;
        return button;
    }

    private async void AddCustomCategoryButton_OnClick(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox
        {
            PlaceholderText = "输入标签名称",
            MaxLength = 20,
            Height = 36,
            MinWidth = 320,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Style = Application.Current.Resources["SingleLineTextBoxStyle"] as Style
        };
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(nameBox);
        content.Children.Add(new TextBlock
        {
            Text = "自定义标签可手动标记和筛选；智能分类目前只识别风景、城市、人物、自然。",
            Style = Application.Current.Resources["SecondaryTextStyle"] as Style,
            TextWrapping = TextWrapping.Wrap
        });

        var dialog = new ContentDialog
        {
            Title = "新增标签",
            Content = content,
            PrimaryButtonText = "新增",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var category = nameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(category))
        {
            await ShowMessageAsync("新增标签", "标签名称不能为空。");
            return;
        }

        var allCategories = BuiltInCategories.Concat(_settings.CustomCategories);
        if (allCategories.Contains(category, StringComparer.OrdinalIgnoreCase))
        {
            await ShowMessageAsync("新增标签", $"标签“{category}”已经存在。");
            return;
        }

        _settings.CustomCategories.Add(category);
        SettingsStore.Save(null, _settings);
        RebuildCategoryControls();
        SetCategorySelection(_activeCategory, showAll: _activeCategory is null);
        CategoryHintText.Text = $"已新增“{category}”；请选择图片后使用“标记”归类";
        StatusText.Text = $"已新增自定义标签：{category}";
    }

    private int CountCategory(string category)
    {
        return _allImages.Count(x =>
            _settings.Categories.TryGetValue(x.FullPath, out var savedCategories) &&
            savedCategories.Contains(category, StringComparer.OrdinalIgnoreCase));
    }

    private void AllPhotosCategoryButton_OnClick(object sender, RoutedEventArgs e)
    {
        _activeCategory = null;
        _activeNavigationFilter = null;
        _activeAlbum = null;
        _activeNavigationSection = "folder";
        SetNavigationSelection("folder");
        SetCategorySelection(null, showAll: true);
        ApplyFilterAndSort();
        StatusText.Text = "已显示全部照片";
    }

    private void CategoryButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string category })
        {
            return;
        }

        _activeCategory = category;
        _activeNavigationFilter = null;
        _activeAlbum = null;
        _activeNavigationSection = "folder";
        SetNavigationSelection("folder");
        SetCategorySelection(category, showAll: false);
        ApplyFilterAndSort();
        StatusText.Text = $"正在查看分类：{category}";
    }

    private async void ManualCategoryMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: string category })
        {
            return;
        }

        var selectedRecords = GetSelectedRecords();
        if (selectedRecords.Count == 0)
        {
            await ShowMessageAsync("手动分类", "请先选择一张或多张图片，再使用“标记”选择分类。");
            return;
        }

        foreach (var record in selectedRecords)
        {
            if (!_settings.Categories.TryGetValue(record.FullPath, out var categories))
            {
                categories = [];
                _settings.Categories[record.FullPath] = categories;
            }

            if (!categories.Contains(category, StringComparer.OrdinalIgnoreCase))
            {
                categories.Add(category);
            }
        }

        SettingsStore.Save(null, _settings);
        if (!string.IsNullOrWhiteSpace(_activeCategory))
        {
            ApplyFilterAndSort();
        }
        else
        {
            UpdateCategoryCounts();
        }

        StatusText.Text = $"已将 {selectedRecords.Count:N0} 张图片标记为：{category}";
        CategoryHintText.Text = $"已标记 {selectedRecords.Count:N0} 张图片为“{category}”";
        UpdateSelectedCategoryChip(_selectedImage?.FullPath);
    }

    private void SelectedCategoryRemoveButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string removedCategory })
        {
            return;
        }

        RemoveCategoryFromSelectedRecords(removedCategory);
    }

    private void RemoveCategoryMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: string removedCategory })
        {
            return;
        }

        RemoveCategoryFromSelectedRecords(removedCategory);
    }

    private void RemoveCategoryFromSelectedRecords(string removedCategory)
    {
        var selectedRecords = GetSelectedRecords();
        var removedCount = _collections.RemoveCategory(
            selectedRecords.Select(record => record.FullPath),
            removedCategory);
        if (removedCount == 0)
        {
            StatusText.Text = $"所选图片没有“{removedCategory}”标签";
            return;
        }

        SettingsStore.Save(null, _settings);
        UpdateCategoryCounts();
        UpdateSelectedCategoryChip(_selectedImage?.FullPath);
        StatusText.Text = $"已从 {removedCount:N0} 张图片移除“{removedCategory}”标签";
        CategoryHintText.Text = $"已从 {removedCount:N0} 张图片移除“{removedCategory}”标签";

        var removedFromCurrentView = string.Equals(
            _activeCategory,
            removedCategory,
            StringComparison.OrdinalIgnoreCase);
        if (removedFromCurrentView)
        {
            ApplyFilterAndSort();
            ClearCurrentSelection();
        }
    }

    private void UpdateSelectedCategoryChip(string? imagePath)
    {
        if (!string.IsNullOrWhiteSpace(imagePath) &&
            _settings.Categories.TryGetValue(imagePath, out var categories) &&
            categories.Count > 0)
        {
            SelectedCategoriesItems.ItemsSource = categories.ToList();
            SelectedCategoriesItems.Visibility = Visibility.Visible;
            return;
        }

        SelectedCategoriesItems.ItemsSource = null;
        SelectedCategoriesItems.Visibility = Visibility.Collapsed;
    }

    private async void SmartClassificationButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isSmartClassifying)
        {
            return;
        }

        var selectedItems = ImageGridView.SelectedItems
            .OfType<MediaTileViewModel>()
            .Where(item => item.Record.Kind == MediaKind.Image)
            .Select(item => item.Record.AsImageRecord())
            .ToList();
        var targets = selectedItems.Count > 0 ? selectedItems : _filteredImages.ToList();
        if (targets.Count == 0)
        {
            await ShowMessageAsync("智能分类", "当前没有可分析的图片。请先选择图片文件夹。");
            return;
        }

        try
        {
            _smartClassificationService ??= new SmartClassificationService();
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("智能分类不可用", $"本地模型加载失败：{ex.Message}");
            return;
        }

        var unclassifiedTargets = targets
            .Where(item => !_settings.Categories.TryGetValue(item.FullPath, out var categories) || categories.Count == 0)
            .DistinctBy(item => item.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var skippedCount = targets.Count - unclassifiedTargets.Count;
        if (unclassifiedTargets.Count == 0)
        {
            StatusText.Text = $"智能分类已完成，跳过已有分类的 {skippedCount:N0} 张图片";
            CategoryHintText.Text = "当前图片都已有分类，未覆盖手动分类结果";
            return;
        }

        _isSmartClassifying = true;
        UpdateSmartClassificationButtonState();
        var classified = new List<(ImageRecord Image, SmartClassificationResult Result)>();

        try
        {
            StatusText.Text = $"智能分类 0/{targets.Count:N0}（跳过 {skippedCount:N0}）";
            CategoryHintText.Text = "本地视觉模型分析中，不会上传图片";

            await Task.Run(async () =>
            {
                for (var index = 0; index < unclassifiedTargets.Count; index++)
                {
                    var image = unclassifiedTargets[index];
                    var result = await _smartClassificationService!.ClassifyAsync(image, _lifetimeCancellation.Token);
                    if (result is not null)
                    {
                        classified.Add((image, result));
                    }

                    if ((index + 1) % 5 == 0 || index == unclassifiedTargets.Count - 1)
                    {
                        var completed = skippedCount + index + 1;
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            StatusText.Text = $"智能分类 {completed:N0}/{targets.Count:N0}";
                        });
                    }
                }
            });

            _lifetimeCancellation.Token.ThrowIfCancellationRequested();

            foreach (var (image, result) in classified)
            {
                _settings.Categories[image.FullPath] = [result.Category];
            }

            SettingsStore.Save(null, _settings);
            ApplyFilterAndSort();
            var categorySummary = classified
                .GroupBy(item => item.Result.Category)
                .OrderByDescending(group => group.Count())
                .Select(group => $"{group.Key} {group.Count():N0}");
            var summary = string.Join("、", categorySummary);
            StatusText.Text = $"智能分类完成：已归类 {classified.Count:N0} 张，跳过 {skippedCount:N0} 张";
            CategoryHintText.Text = string.IsNullOrWhiteSpace(summary)
                ? "未识别出符合当前分类的图片"
                : $"本次结果：{summary}";
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("智能分类失败", ex.Message);
            StatusText.Text = "智能分类失败";
        }
        finally
        {
            _isSmartClassifying = false;
            if (!_lifetimeCancellation.IsCancellationRequested)
            {
                UpdateSmartClassificationButtonState();
            }
        }
    }

    private void UpdateSmartClassificationButtonState()
    {
        SmartClassificationButton.IsEnabled = !_isSmartClassifying &&
                                              _activeMediaKind == MediaKind.Image &&
                                              _allImages.Count > 0;
    }

    private void NavigationItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag })
        {
            return;
        }

        if (string.Equals(tag, "albums", StringComparison.OrdinalIgnoreCase))
        {
            SetAlbumNavigationExpanded(!_albumsExpanded);
            return;
        }

        _activeNavigationSection = tag;
        SetNavigationSelection(tag);
        SetCategorySelection(null, showAll: false);
        switch (tag)
        {
            case "folder":
                _activeCategory = null;
                _activeNavigationFilter = null;
                _activeAlbum = null;
                SetCategorySelection(null, showAll: true);
                ApplyFilterAndSort();
                StatusText.Text = _allImages.Count > 0
                    ? "正在查看当前文件夹"
                    : "请先通过顶部“导入文件夹”选择图片目录";
                break;
            case "favorites":
                _activeCategory = null;
                _activeNavigationFilter = "favorites";
                _activeAlbum = null;
                ApplyFilterAndSort();
                StatusText.Text = "正在查看收藏";
                break;
            case "recent":
                _activeCategory = null;
                _activeNavigationFilter = "recent";
                _activeAlbum = null;
                ApplyFilterAndSort();
                StatusText.Text = "正在查看最近图片";
                break;
            case "wallpaper":
                _activeCategory = null;
                _activeNavigationFilter = "wallpaper";
                _activeAlbum = null;
                ApplyFilterAndSort();
                StatusText.Text = _collections.WallpaperFavorites.Count > 0
                    ? "正在查看壁纸收藏夹"
                    : "壁纸收藏夹暂无内容，请先加入图片";
                break;
        }
    }

    private void SetNavigationSelection(string tag, ElementTheme? themeOverride = null)
    {
        ApplyButtonSelection(FolderNavigationItem, tag == "folder", themeOverride);
        ApplyButtonSelection(FavoritesNavigationItem, tag == "favorites", themeOverride);
        ApplyButtonSelection(
            AlbumsNavigationItem,
            tag == "albums" && string.IsNullOrWhiteSpace(_activeAlbum),
            themeOverride);
        ApplyButtonSelection(RecentNavigationItem, tag == "recent", themeOverride);
        ApplyButtonSelection(WallpaperNavigationItem, tag == "wallpaper", themeOverride);
        SetAlbumSelection(tag == "albums" ? _activeAlbum : null, themeOverride);
    }

    private void RebuildAlbumNavigation()
    {
        foreach (var button in _albumButtons.Values)
        {
            UnregisterSelectionButton(button);
        }

        AlbumListPanel.Children.Clear();
        _albumButtons.Clear();

        var albumNames = _settings.Albums.Keys
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.CurrentCulture)
            .ToList();
        if (albumNames.Count == 0)
        {
            AlbumListPanel.Children.Add(new TextBlock
            {
                Text = "暂无相册",
                Margin = new Thickness(8, 5, 8, 5),
                Style = Application.Current.Resources["SecondaryTextStyle"] as Style
            });
            return;
        }

        foreach (var albumName in albumNames)
        {
            var content = new Grid { Padding = new Thickness(0, 3, 0, 3) };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new TextBlock
            {
                Text = albumName,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var count = new TextBlock
            {
                Text = _settings.Albums[albumName].Count.ToString("N0"),
                MinWidth = 34,
                Margin = new Thickness(8, 0, 4, 0),
                TextAlignment = TextAlignment.Right,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Style = Application.Current.Resources["SecondaryTextStyle"] as Style
            };
            Grid.SetColumn(count, 1);
            content.Children.Add(label);
            content.Children.Add(count);

            var button = new Button
            {
                Tag = albumName,
                Style = Application.Current.Resources["CategoryItemButtonStyle"] as Style,
                Content = content
            };
            RegisterSelectionButton(button);
            button.Click += AlbumNavigationButton_OnClick;
            ToolTipService.SetToolTip(button, albumName);
            AutomationProperties.SetName(button, $"相册 {albumName}，{_settings.Albums[albumName].Count:N0} 张图片");

            var deleteItem = new MenuFlyoutItem
            {
                Text = "删除相册",
                Tag = albumName,
                Icon = new FontIcon { Glyph = "\uE74D" }
            };
            deleteItem.Click += DeleteAlbumNavigationItem_OnClick;
            var menu = new MenuFlyout();
            menu.Items.Add(deleteItem);
            button.ContextFlyout = menu;

            AlbumListPanel.Children.Add(button);
            _albumButtons[albumName] = button;
        }

        SetAlbumSelection(_activeAlbum);
    }

    private void SetAlbumNavigationExpanded(bool expanded)
    {
        _albumsExpanded = expanded;
        AlbumListContainer.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        AlbumsChevronIcon.Glyph = expanded ? "\uE70D" : "\uE76C";
        AutomationProperties.SetName(AlbumsNavigationItem, expanded ? "折叠相册列表" : "展开相册列表");
    }

    private void AlbumNavigationButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string albumName })
        {
            return;
        }

        _activeCategory = null;
        _activeNavigationSection = "albums";
        _activeNavigationFilter = "albums";
        _activeAlbum = albumName;
        SetAlbumNavigationExpanded(true);
        SetNavigationSelection("albums");
        SetCategorySelection(null, showAll: false);
        ApplyFilterAndSort();
        ClearCurrentSelection();
        StatusText.Text = $"正在查看相册：{albumName}";
    }

    private async void DeleteAlbumNavigationItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: string albumName } ||
            !_settings.Albums.ContainsKey(albumName))
        {
            return;
        }

        if (!await ConfirmAsync("删除相册", $"确定删除相册“{albumName}”吗？相册中的原始图片不会被删除。", "删除"))
        {
            return;
        }

        var wasActive = string.Equals(_activeAlbum, albumName, StringComparison.OrdinalIgnoreCase);
        _settings.Albums.Remove(albumName);
        if (wasActive)
        {
            _activeAlbum = null;
            _activeNavigationFilter = null;
            _activeNavigationSection = "folder";
            _activeCategory = null;
            SetNavigationSelection("folder");
            SetCategorySelection(null, showAll: true);
            ApplyFilterAndSort();
            ClearCurrentSelection();
        }

        SettingsStore.Save(null, _settings);
        RebuildAlbumNavigation();
        StatusText.Text = $"已删除相册：{albumName}";
    }

    private void SetAlbumSelection(string? albumName, ElementTheme? themeOverride = null)
    {
        foreach (var (name, button) in _albumButtons)
        {
            ApplyButtonSelection(
                button,
                string.Equals(name, albumName, StringComparison.OrdinalIgnoreCase),
                themeOverride);
        }
    }

    private void SetCategorySelection(string? category, bool showAll, ElementTheme? themeOverride = null)
    {
        ApplyButtonSelection(
            AllPhotosCategoryButton,
            showAll && string.IsNullOrWhiteSpace(category),
            themeOverride);
        foreach (var (categoryName, button) in _categoryButtons)
        {
            ApplyButtonSelection(
                button,
                string.Equals(categoryName, category, StringComparison.OrdinalIgnoreCase),
                themeOverride);
        }
    }

    private void ApplyButtonSelection(Button button, bool selected, ElementTheme? themeOverride = null)
    {
        var actualTheme = themeOverride ?? RootGrid.ActualTheme;
        if (selected)
        {
            _selectedButtons.Add(button);
        }
        else
        {
            _selectedButtons.Remove(button);
        }

        ApplyButtonVisualState(button, selected, pointerOver: false, actualTheme);
    }

    private void InitializeSelectionButtonInteractions()
    {
        foreach (var button in new[]
                 {
                     FolderNavigationItem,
                     FavoritesNavigationItem,
                     AlbumsNavigationItem,
                     RecentNavigationItem,
                     WallpaperNavigationItem,
                     AllPhotosCategoryButton
                 })
        {
            RegisterSelectionButton(button);
        }
    }

    private void RegisterSelectionButton(Button button)
    {
        button.PointerEntered -= SelectionButton_OnPointerEntered;
        button.PointerExited -= SelectionButton_OnPointerExited;
        button.PointerEntered += SelectionButton_OnPointerEntered;
        button.PointerExited += SelectionButton_OnPointerExited;
    }

    private void UnregisterSelectionButton(Button button)
    {
        button.PointerEntered -= SelectionButton_OnPointerEntered;
        button.PointerExited -= SelectionButton_OnPointerExited;
        _selectedButtons.Remove(button);
    }

    private void SelectionButton_OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button button)
        {
            ApplyButtonVisualState(
                button,
                _selectedButtons.Contains(button),
                pointerOver: true,
                RootGrid.ActualTheme);
        }
    }

    private void SelectionButton_OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button button)
        {
            ApplyButtonVisualState(
                button,
                _selectedButtons.Contains(button),
                pointerOver: false,
                RootGrid.ActualTheme);
        }
    }

    private static void ApplyButtonVisualState(
        Button button,
        bool selected,
        bool pointerOver,
        ElementTheme actualTheme)
    {
        if (selected)
        {
            button.Background = pointerOver
                ? CreateSelectionHoverBackground(actualTheme)
                : CreateSelectionBackground(actualTheme);
        }
        else if (pointerOver)
        {
            button.Background = CreatePointerOverBackground(actualTheme);
        }
        else
        {
            button.ClearValue(Control.BackgroundProperty);
        }

        ApplyContentForeground(button, selected, pointerOver, actualTheme);
    }

    private static SolidColorBrush CreateSelectionBackground(ElementTheme actualTheme)
    {
        return actualTheme == ElementTheme.Dark
            ? new SolidColorBrush(ColorHelper.FromArgb(255, 54, 139, 255))
            : new SolidColorBrush(ColorHelper.FromArgb(255, 36, 111, 229));
    }

    private static SolidColorBrush CreateSelectionHoverBackground(ElementTheme actualTheme)
    {
        return actualTheme == ElementTheme.Dark
            ? new SolidColorBrush(ColorHelper.FromArgb(255, 60, 141, 255))
            : new SolidColorBrush(ColorHelper.FromArgb(255, 21, 95, 204));
    }

    private static SolidColorBrush CreatePointerOverBackground(ElementTheme actualTheme)
    {
        return actualTheme == ElementTheme.Dark
            ? new SolidColorBrush(ColorHelper.FromArgb(255, 48, 60, 79))
            : new SolidColorBrush(ColorHelper.FromArgb(255, 220, 234, 255));
    }

    private static void ApplyContentForeground(
        ContentControl control,
        bool selected,
        bool pointerOver,
        ElementTheme actualTheme)
    {
        if (selected)
        {
            control.Foreground = new SolidColorBrush(Colors.White);
        }
        else if (pointerOver)
        {
            control.Foreground = CreatePointerOverForeground(actualTheme);
        }
        else
        {
            control.ClearValue(Control.ForegroundProperty);
        }

        if (control.Content is DependencyObject content)
        {
            ApplyDescendantTextForeground(content, selected, pointerOver, actualTheme);
        }
    }

    private static SolidColorBrush CreatePointerOverForeground(ElementTheme actualTheme)
    {
        return actualTheme == ElementTheme.Dark
            ? new SolidColorBrush(ColorHelper.FromArgb(255, 238, 243, 250))
            : new SolidColorBrush(ColorHelper.FromArgb(255, 24, 34, 51));
    }

    private static void ApplyDescendantTextForeground(
        DependencyObject element,
        bool selected,
        bool pointerOver,
        ElementTheme actualTheme)
    {
        if (element is TextBlock textBlock)
        {
            if (selected)
            {
                textBlock.Foreground = textBlock.Tag as string == "SelectionCountBadge" &&
                                       actualTheme != ElementTheme.Dark
                    ? CreateSelectionBackground(actualTheme)
                    : new SolidColorBrush(Colors.White);
            }
            else if (pointerOver)
            {
                textBlock.Foreground = CreatePointerOverForeground(actualTheme);
            }
            else
            {
                textBlock.ClearValue(TextBlock.ForegroundProperty);
            }

            return;
        }

        if (element is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                ApplyDescendantTextForeground(child, selected, pointerOver, actualTheme);
            }

            return;
        }

        if (element is Border { Child: DependencyObject borderChild })
        {
            ApplyDescendantTextForeground(borderChild, selected, pointerOver, actualTheme);
            return;
        }

        if (element is ContentControl { Content: DependencyObject nestedContent })
        {
            ApplyDescendantTextForeground(nestedContent, selected, pointerOver, actualTheme);
        }
    }

}
