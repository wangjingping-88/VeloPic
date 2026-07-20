using VeloPic.Core;

var tests = new List<(string Name, Action Run)>
{
    ("识别支持的图片扩展名", ImageFileScannerTests.SupportsExpectedExtensions),
    ("递归扫描只返回图片文件", ImageFileScannerTests.EnumeratesImagesRecursively),
    ("非递归扫描只返回根目录图片文件", ImageFileScannerTests.EnumeratesImagesWithoutRecursion),
    ("主题解析符合跟随系统和手动模式", ThemeResolverTests.ResolvesThemeModes),
    ("常用分辨率筛选兼容横图和竖图", ImageResolutionFilterTests.MatchesCommonResolutionLevels),
    ("设置文件可以往返保存", SettingsStoreTests.RoundTripsSettings),
    ("收藏和相册删除结果可以持久化", SettingsStoreTests.RoundTripsCollectionRemoval),
    ("设置保存使用原子替换", SettingsStoreTests.ReplacesSettingsAtomically),
    ("图片扫描支持取消", ImageFileScannerTests.HonorsCancellation),
    ("图库查询组合筛选和排序正确", ImageLibraryQueryServiceTests.FiltersAndSorts),
    ("集合服务同步收藏和相册设置", ImageCollectionServiceTests.SynchronizesSettings),
    ("查看器导航支持首尾循环", ImageViewerNavigatorTests.WrapsAround)
};

var failed = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {test.Name}: {ex.Message}");
    }
}

if (failed > 0)
{
    Console.Error.WriteLine($"FAILED: {failed}/{tests.Count}");
    return 1;
}

Console.WriteLine($"PASSED: {tests.Count}/{tests.Count}");
return 0;

static class AssertEx
{
    public static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message}。期望：{expected}，实际：{actual}");
        }
    }
}

static class ImageFileScannerTests
{
    public static void SupportsExpectedExtensions()
    {
        AssertEx.True(ImageFileScanner.IsSupportedImage("A.JPG"), "应识别大写 JPG");
        AssertEx.True(ImageFileScanner.IsSupportedImage("wallpaper.avif"), "应识别 AVIF");
        AssertEx.True(!ImageFileScanner.IsSupportedImage("readme.txt"), "不应识别 txt");
    }

    public static void EnumeratesImagesRecursively()
    {
        using var root = TempDirectory.Create();
        Directory.CreateDirectory(Path.Combine(root.Path, "nested"));
        File.WriteAllText(Path.Combine(root.Path, "a.jpg"), "fake");
        File.WriteAllText(Path.Combine(root.Path, "b.txt"), "fake");
        File.WriteAllText(Path.Combine(root.Path, "nested", "c.PNG"), "fake");

        var scanner = new ImageFileScanner();
        var records = scanner.EnumerateImages(new ScanOptions(root.Path, Recursive: true)).ToList();

        AssertEx.Equal(2, records.Count, "递归扫描应返回两张图片");
        AssertEx.True(records.All(x => x.FullPath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || x.FullPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)), "扫描结果只能包含图片");
    }

    public static void EnumeratesImagesWithoutRecursion()
    {
        using var root = TempDirectory.Create();
        Directory.CreateDirectory(Path.Combine(root.Path, "nested"));
        File.WriteAllText(Path.Combine(root.Path, "a.jpg"), "fake");
        File.WriteAllText(Path.Combine(root.Path, "nested", "c.PNG"), "fake");

        var scanner = new ImageFileScanner();
        var records = scanner.EnumerateImages(new ScanOptions(root.Path, Recursive: false)).ToList();

        AssertEx.Equal(1, records.Count, "非递归扫描只返回根目录图片");
        AssertEx.Equal("a.jpg", records[0].FileName, "根目录图片文件名应正确");
    }

    public static void HonorsCancellation()
    {
        using var root = TempDirectory.Create();
        File.WriteAllText(Path.Combine(root.Path, "a.jpg"), "fake");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var scanner = new ImageFileScanner();
        var canceled = false;
        try
        {
            _ = scanner.EnumerateImages(
                    new ScanOptions(root.Path, Recursive: true),
                    cancellation.Token)
                .ToList();
        }
        catch (OperationCanceledException)
        {
            canceled = true;
        }

        AssertEx.True(canceled, "扫描应响应取消请求");
    }
}

static class ThemeResolverTests
{
    public static void ResolvesThemeModes()
    {
        AssertEx.Equal(ResolvedTheme.Dark, ThemeResolver.Resolve(VeloPicThemeMode.Dark, ResolvedTheme.Light), "手动深色应忽略系统主题");
        AssertEx.Equal(ResolvedTheme.Light, ThemeResolver.Resolve(VeloPicThemeMode.System, ResolvedTheme.Light), "跟随系统应返回系统浅色");
        AssertEx.Equal(ResolvedTheme.Dark, ThemeResolver.Resolve(VeloPicThemeMode.System, ResolvedTheme.Dark), "跟随系统应返回系统深色");
    }
}

static class ImageResolutionFilterTests
{
    public static void MatchesCommonResolutionLevels()
    {
        AssertEx.True(ImageResolutionFilter.Matches(3840, 2160, ImageResolutionLevel.FourK), "应识别横向 4K");
        AssertEx.True(ImageResolutionFilter.Matches(2160, 3840, ImageResolutionLevel.FourK), "应识别竖向 4K");
        AssertEx.True(ImageResolutionFilter.Matches(2560, 1440, ImageResolutionLevel.TwoK), "应识别 2K");
        AssertEx.True(ImageResolutionFilter.Matches(1920, 1080, ImageResolutionLevel.OneK), "应识别 1K");
        AssertEx.True(!ImageResolutionFilter.Matches(1920, 1080, ImageResolutionLevel.TwoK), "1K 不应匹配 2K");
    }
}

static class SettingsStoreTests
{
    public static void RoundTripsSettings()
    {
        using var root = TempDirectory.Create();
        var path = Path.Combine(root.Path, "settings.json");
        var settings = new AppSettings
        {
            LastFolder = root.Path,
            Appearance = new AppearanceSettings
            {
                Theme = VeloPicThemeMode.Dark,
                ThumbnailSize = 220,
                WallpaperFit = "fit"
            },
            Sorting = new SortingSettings
            {
                Field = ImageSortField.FileSize,
                Direction = ImageSortDirection.Ascending
            },
            Favorites = ["a.jpg", "b.png"],
            CustomCategories = ["建筑", "夜景"]
        };
        settings.Categories["a.jpg"] = "风景";

        SettingsStore.Save(path, settings);
        var loaded = SettingsStore.Load(path);

        AssertEx.Equal(root.Path, loaded.LastFolder, "应保存上次目录");
        AssertEx.Equal(VeloPicThemeMode.Dark, loaded.Appearance.Theme, "应保存主题");
        AssertEx.Equal(220, loaded.Appearance.ThumbnailSize, "应保存缩略图大小");
        AssertEx.Equal("fit", loaded.Appearance.WallpaperFit, "应保存壁纸显示模式");
        AssertEx.Equal(ImageSortField.FileSize, loaded.Sorting.Field, "应保存排序字段");
        AssertEx.Equal(ImageSortDirection.Ascending, loaded.Sorting.Direction, "应保存排序方向");
        AssertEx.Equal(2, loaded.Favorites.Count, "应保存收藏列表");
        AssertEx.Equal(2, loaded.CustomCategories.Count, "应保存自定义标签");
        AssertEx.Equal("建筑", loaded.CustomCategories[0], "应保留自定义标签名称");
        AssertEx.Equal("风景", loaded.Categories["a.jpg"], "应保存手动分类");
    }

    public static void RoundTripsCollectionRemoval()
    {
        using var root = TempDirectory.Create();
        var path = Path.Combine(root.Path, "settings.json");
        var settings = new AppSettings
        {
            Favorites = ["favorite.jpg"],
            WallpaperFavorites = ["wallpaper.jpg"],
            Albums = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["旅行"] = ["trip.jpg"]
            }
        };

        settings.Favorites.Remove("favorite.jpg");
        settings.WallpaperFavorites.Remove("wallpaper.jpg");
        settings.Albums.Remove("旅行");
        SettingsStore.Save(path, settings);
        var loaded = SettingsStore.Load(path);

        AssertEx.Equal(0, loaded.Favorites.Count, "取消收藏应持久化");
        AssertEx.Equal(0, loaded.WallpaperFavorites.Count, "移出壁纸收藏夹应持久化");
        AssertEx.Equal(0, loaded.Albums.Count, "删除相册应持久化");
    }

    public static void ReplacesSettingsAtomically()
    {
        using var root = TempDirectory.Create();
        var path = Path.Combine(root.Path, "settings.json");
        SettingsStore.Save(path, new AppSettings { LastFolder = "first" });
        SettingsStore.Save(path, new AppSettings { LastFolder = "second" });

        var loaded = SettingsStore.Load(path);
        AssertEx.Equal("second", loaded.LastFolder, "第二次保存应完整替换旧设置");
        AssertEx.True(!File.Exists(path + ".tmp"), "成功保存后不应遗留临时文件");
    }
}

static class ImageLibraryQueryServiceTests
{
    public static void FiltersAndSorts()
    {
        var first = new ImageRecord("C:\\images\\mountain.jpg", "mountain.jpg", "C:\\images", 300, DateTimeOffset.Parse("2026-01-01"));
        var second = new ImageRecord("C:\\images\\city.png", "city.png", "C:\\images", 100, DateTimeOffset.Parse("2026-01-02"));
        var service = new ImageLibraryQueryService();
        var result = service.Query([first, second], new ImageQueryOptions
        {
            FileExtension = ".jpg",
            Category = "风景",
            Categories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [first.FullPath] = "风景",
                [second.FullPath] = "城市"
            },
            SortField = ImageSortField.FileSize,
            SortDirection = ImageSortDirection.Ascending
        });

        AssertEx.Equal(1, result.Count, "组合筛选应只保留匹配图片");
        AssertEx.Equal(first.FullPath, result[0].FullPath, "筛选结果应为风景 JPG");
    }
}

static class ImageCollectionServiceTests
{
    public static void SynchronizesSettings()
    {
        var settings = new AppSettings();
        var service = new ImageCollectionService(settings);
        service.SetFavorite(["a.jpg"], true);
        service.SetWallpaperFavorite(["a.jpg"], true);
        service.AddToAlbum("旅行", ["a.jpg", "b.jpg"]);
        var removal = service.RemoveFromAlbum("旅行", ["a.jpg"]);

        AssertEx.True(service.Favorites.Contains("a.jpg"), "收藏集合应同步更新");
        AssertEx.True(settings.WallpaperFavorites.Contains("a.jpg"), "壁纸收藏应写回设置");
        AssertEx.Equal(1, removal.RemovedCount, "应从相册移除一张图片");
        AssertEx.Equal("b.jpg", settings.Albums["旅行"][0], "相册应保留未移除图片");
    }
}

static class ImageViewerNavigatorTests
{
    public static void WrapsAround()
    {
        var first = new ImageRecord("a.jpg", "a.jpg", string.Empty, 1, DateTimeOffset.MinValue);
        var second = new ImageRecord("b.jpg", "b.jpg", string.Empty, 1, DateTimeOffset.MinValue);
        var navigator = new ImageViewerNavigator();

        AssertEx.Equal(second, navigator.Open([first, second], second.FullPath), "应从选中图片打开");
        AssertEx.Equal(first, navigator.Next(), "末张的下一张应回到首张");
        AssertEx.Equal(second, navigator.Previous(), "首张的上一张应回到末张");
    }
}

sealed class TempDirectory : IDisposable
{
    public string Path { get; }

    private TempDirectory(string path)
    {
        Path = path;
    }

    public static TempDirectory Create()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "VeloPicTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return new TempDirectory(path);
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
