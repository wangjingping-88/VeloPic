using System.Buffers.Binary;
using System.Text;
using VeloPic.Core;

var tests = new List<(string Name, Action Run)>
{
    ("识别支持的图片扩展名", ImageFileScannerTests.SupportsExpectedExtensions),
    ("递归扫描只返回图片文件", ImageFileScannerTests.EnumeratesImagesRecursively),
    ("非递归扫描只返回根目录图片文件", ImageFileScannerTests.EnumeratesImagesWithoutRecursion),
    ("主题解析符合跟随系统和手动模式", ThemeResolverTests.ResolvesThemeModes),
    ("常用分辨率筛选兼容横图和竖图", ImageResolutionFilterTests.MatchesCommonResolutionLevels),
    ("画面方向筛选区分横屏竖屏和方形", ImageOrientationFilterTests.MatchesLandscapeAndPortrait),
    ("设置文件可以往返保存", SettingsStoreTests.RoundTripsSettings),
    ("旧版单标签设置可以迁移", SettingsStoreTests.LoadsLegacySingleCategory),
    ("旧设置默认使用平铺浏览模式", SettingsStoreTests.UsesFlatBrowseModeForLegacySettings),
    ("收藏和相册删除结果可以持久化", SettingsStoreTests.RoundTripsCollectionRemoval),
    ("设置保存使用原子替换", SettingsStoreTests.ReplacesSettingsAtomically),
    ("图片扫描支持取消", ImageFileScannerTests.HonorsCancellation),
    ("图库查询组合筛选和排序正确", ImageLibraryQueryServiceTests.FiltersAndSorts),
    ("同一图片可出现在多个标签筛选中", ImageLibraryQueryServiceTests.SupportsMultipleCategories),
    ("集合服务同步收藏和相册设置", ImageCollectionServiceTests.SynchronizesSettings),
    ("集合服务支持批量移除单个标签", ImageCollectionServiceTests.RemovesCategoryFromMultipleImages),
    ("查看器导航支持首尾循环", ImageViewerNavigatorTests.WrapsAround),
    ("媒体扫描识别图片和常见视频格式", MediaFileScannerTests.SupportsAndScansMedia),
    ("媒体查询按类型筛选并在目录内排序", MediaLibraryQueryServiceTests.FiltersByKindAndSorts),
    ("媒体查询支持横竖屏筛选", MediaLibraryQueryServiceTests.FiltersByOrientation),
    ("媒体查询支持图片多标签", MediaLibraryQueryServiceTests.SupportsMultipleCategories),
    ("目录分组生成完整相对路径标题", MediaDirectoryGroupingTests.BuildsRelativeDirectoryTitles),
    ("媒体查看导航支持首尾循环", MediaViewerNavigatorTests.WrapsAround),
    ("分片 MP4 可以读取真实视频时长", Mp4DurationReaderTests.ReadsFragmentTimelineDuration)
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

static class ImageOrientationFilterTests
{
    public static void MatchesLandscapeAndPortrait()
    {
        AssertEx.True(ImageOrientationFilter.Matches(1920, 1080, ImageOrientation.Landscape), "宽图应匹配横屏");
        AssertEx.True(!ImageOrientationFilter.Matches(1080, 1920, ImageOrientation.Landscape), "竖图不应匹配横屏");
        AssertEx.True(ImageOrientationFilter.Matches(1080, 1920, ImageOrientation.Portrait), "高图应匹配竖屏");
        AssertEx.True(!ImageOrientationFilter.Matches(1920, 1080, ImageOrientation.Portrait), "横图不应匹配竖屏");
        AssertEx.True(!ImageOrientationFilter.Matches(1024, 1024, ImageOrientation.Landscape), "方形图片不应归为横屏");
        AssertEx.True(!ImageOrientationFilter.Matches(1024, 1024, ImageOrientation.Portrait), "方形图片不应归为竖屏");
        AssertEx.True(ImageOrientationFilter.Matches(1024, 1024, ImageOrientation.All), "全部方向应保留方形图片");
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
                WallpaperFit = "fit",
                BrowseMode = MediaBrowseMode.DirectoryGrouped
            },
            Sorting = new SortingSettings
            {
                Field = ImageSortField.FileSize,
                Direction = ImageSortDirection.Ascending
            },
            Favorites = ["a.jpg", "b.png"],
            CustomCategories = ["建筑", "夜景"]
        };
        settings.Categories["a.jpg"] = ["风景", "旅行"];

        SettingsStore.Save(path, settings);
        var loaded = SettingsStore.Load(path);

        AssertEx.Equal(root.Path, loaded.LastFolder, "应保存上次目录");
        AssertEx.Equal(VeloPicThemeMode.Dark, loaded.Appearance.Theme, "应保存主题");
        AssertEx.Equal(220, loaded.Appearance.ThumbnailSize, "应保存缩略图大小");
        AssertEx.Equal("fit", loaded.Appearance.WallpaperFit, "应保存壁纸显示模式");
        AssertEx.Equal(MediaBrowseMode.DirectoryGrouped, loaded.Appearance.BrowseMode, "应保存媒体浏览模式");
        AssertEx.Equal(ImageSortField.FileSize, loaded.Sorting.Field, "应保存排序字段");
        AssertEx.Equal(ImageSortDirection.Ascending, loaded.Sorting.Direction, "应保存排序方向");
        AssertEx.Equal(2, loaded.Favorites.Count, "应保存收藏列表");
        AssertEx.Equal(2, loaded.CustomCategories.Count, "应保存自定义标签");
        AssertEx.Equal("建筑", loaded.CustomCategories[0], "应保留自定义标签名称");
        AssertEx.Equal(2, loaded.Categories["a.jpg"].Count, "应保存多个手动标签");
        AssertEx.True(loaded.Categories["a.jpg"].Contains("风景"), "应保存第一个标签");
        AssertEx.True(loaded.Categories["a.jpg"].Contains("旅行"), "应保存第二个标签");
    }

    public static void LoadsLegacySingleCategory()
    {
        using var root = TempDirectory.Create();
        var path = Path.Combine(root.Path, "settings.json");
        File.WriteAllText(path, """
        {
          "categories": {
            "a.jpg": "风景"
          }
        }
        """);

        var loaded = SettingsStore.Load(path);

        AssertEx.Equal(1, loaded.Categories["a.jpg"].Count, "旧版字符串标签应迁移为单元素列表");
        AssertEx.Equal("风景", loaded.Categories["a.jpg"][0], "迁移后应保留标签名称");
    }

    public static void UsesFlatBrowseModeForLegacySettings()
    {
        using var root = TempDirectory.Create();
        var path = Path.Combine(root.Path, "settings.json");
        File.WriteAllText(path, """
        {
          "appearance": {
            "theme": "system",
            "thumbnailSize": 190,
            "wallpaperFit": "fill"
          }
        }
        """);

        var loaded = SettingsStore.Load(path);

        AssertEx.Equal(MediaBrowseMode.Flat, loaded.Appearance.BrowseMode, "旧设置缺少浏览模式时应使用平铺模式");
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
            Categories = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                [first.FullPath] = ["风景", "旅行"],
                [second.FullPath] = ["城市"]
            },
            SortField = ImageSortField.FileSize,
            SortDirection = ImageSortDirection.Ascending
        });

        AssertEx.Equal(1, result.Count, "组合筛选应只保留匹配图片");
        AssertEx.Equal(first.FullPath, result[0].FullPath, "筛选结果应为风景 JPG");
    }

    public static void SupportsMultipleCategories()
    {
        var image = new ImageRecord("C:\\images\\trip.jpg", "trip.jpg", "C:\\images", 100, DateTimeOffset.Now);
        var categories = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [image.FullPath] = ["风景", "旅行"]
        };
        var service = new ImageLibraryQueryService();

        var landscapes = service.Query([image], new ImageQueryOptions
        {
            Category = "风景",
            Categories = categories
        });
        var trips = service.Query([image], new ImageQueryOptions
        {
            Category = "旅行",
            Categories = categories
        });

        AssertEx.Equal(1, landscapes.Count, "图片应出现在第一个标签页");
        AssertEx.Equal(1, trips.Count, "图片应同时出现在第二个标签页");
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

    public static void RemovesCategoryFromMultipleImages()
    {
        var settings = new AppSettings
        {
            Categories = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["a.jpg"] = ["风景", "旅行"],
                ["b.jpg"] = ["风景"],
                ["c.jpg"] = ["人物"]
            }
        };
        var service = new ImageCollectionService(settings);

        var removedCount = service.RemoveCategory(["a.jpg", "b.jpg", "c.jpg"], "风景");

        AssertEx.Equal(2, removedCount, "应从两张带目标标签的图片中移除标签");
        AssertEx.Equal("旅行", settings.Categories["a.jpg"][0], "应保留图片的其他标签");
        AssertEx.True(!settings.Categories.ContainsKey("b.jpg"), "移除最后一个标签后应删除空标签记录");
        AssertEx.Equal("人物", settings.Categories["c.jpg"][0], "不含目标标签的图片不应变化");
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

static class MediaFileScannerTests
{
    public static void SupportsAndScansMedia()
    {
        foreach (var extension in new[]
                 {
                     ".mp4", ".m4v", ".mov", ".avi", ".wmv", ".mkv", ".webm",
                     ".mpeg", ".mpg", ".3gp", ".mts", ".m2ts"
                 })
        {
            AssertEx.True(MediaFileScanner.IsSupportedVideo($"clip{extension}"), $"应识别 {extension} 视频");
        }

        AssertEx.True(MediaFileScanner.IsSupportedVideo("clip.MP4"), "视频扩展名识别应忽略大小写");
        AssertEx.True(!MediaFileScanner.IsSupportedVideo("audio.mp3"), "不应把音频识别为视频");

        using var root = TempDirectory.Create();
        Directory.CreateDirectory(Path.Combine(root.Path, "nested"));
        File.WriteAllText(Path.Combine(root.Path, "cover.jpg"), "fake");
        File.WriteAllText(Path.Combine(root.Path, "nested", "clip.webm"), "fake");
        File.WriteAllText(Path.Combine(root.Path, "notes.txt"), "fake");

        var records = new MediaFileScanner()
            .EnumerateMedia(new ScanOptions(root.Path, Recursive: true))
            .ToList();

        AssertEx.Equal(2, records.Count, "扫描应同时返回图片和视频");
        AssertEx.Equal(1, records.Count(record => record.Kind == MediaKind.Image), "应返回一张图片");
        AssertEx.Equal(1, records.Count(record => record.Kind == MediaKind.Video), "应返回一个视频");
    }
}

static class MediaLibraryQueryServiceTests
{
    public static void FiltersByKindAndSorts()
    {
        var image = new MediaRecord("C:\\library\\cover.jpg", "cover.jpg", "C:\\library", 50, DateTimeOffset.MinValue, MediaKind.Image);
        var largeVideo = new MediaRecord("C:\\library\\b.mp4", "b.mp4", "C:\\library", 300, DateTimeOffset.MinValue, MediaKind.Video);
        var smallVideo = new MediaRecord("C:\\library\\a.mp4", "a.mp4", "C:\\library", 100, DateTimeOffset.MinValue, MediaKind.Video);

        var result = new MediaLibraryQueryService().Query([image, largeVideo, smallVideo], new MediaQueryOptions
        {
            Kind = MediaKind.Video,
            FileExtension = ".mp4",
            SortField = ImageSortField.FileSize,
            SortDirection = ImageSortDirection.Ascending
        });

        AssertEx.Equal(2, result.Count, "视频标签应只返回视频");
        AssertEx.Equal(smallVideo, result[0], "组内应按文件大小升序排列");
        AssertEx.Equal(largeVideo, result[1], "较大视频应排在后面");
    }

    public static void FiltersByOrientation()
    {
        var landscape = new MediaRecord("C:\\library\\landscape.jpg", "landscape.jpg", "C:\\library", 50, DateTimeOffset.MinValue, MediaKind.Image);
        var portrait = new MediaRecord("C:\\library\\portrait.jpg", "portrait.jpg", "C:\\library", 50, DateTimeOffset.MinValue, MediaKind.Image);
        var square = new MediaRecord("C:\\library\\square.jpg", "square.jpg", "C:\\library", 50, DateTimeOffset.MinValue, MediaKind.Image);
        var dimensions = new Dictionary<string, ImageDimensions>(StringComparer.OrdinalIgnoreCase)
        {
            [landscape.FullPath] = new ImageDimensions(1920, 1080),
            [portrait.FullPath] = new ImageDimensions(1080, 1920),
            [square.FullPath] = new ImageDimensions(1024, 1024)
        };

        var result = new MediaLibraryQueryService().Query([landscape, portrait, square], new MediaQueryOptions
        {
            Kind = MediaKind.Image,
            Orientation = ImageOrientation.Portrait,
            Dimensions = dimensions
        });

        AssertEx.Equal(1, result.Count, "竖屏筛选应只保留高度大于宽度的图片");
        AssertEx.Equal(portrait, result[0], "竖屏筛选结果不正确");
    }

    public static void SupportsMultipleCategories()
    {
        var image = new MediaRecord("C:\\library\\cover.jpg", "cover.jpg", "C:\\library", 50, DateTimeOffset.MinValue, MediaKind.Image);
        var categories = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [image.FullPath] = ["风景", "旅行"]
        };
        var service = new MediaLibraryQueryService();

        var landscape = service.Query([image], new MediaQueryOptions
        {
            Kind = MediaKind.Image,
            Category = "风景",
            Categories = categories
        });
        var travel = service.Query([image], new MediaQueryOptions
        {
            Kind = MediaKind.Image,
            Category = "旅行",
            Categories = categories
        });

        AssertEx.Equal(1, landscape.Count, "图片应出现在第一个标签页面");
        AssertEx.Equal(1, travel.Count, "图片应同时出现在第二个标签页面");
    }
}

static class MediaDirectoryGroupingTests
{
    public static void BuildsRelativeDirectoryTitles()
    {
        using var root = TempDirectory.Create();
        var nested = Path.Combine(root.Path, "人物", "动漫人物");
        Directory.CreateDirectory(nested);
        var rootRecord = new MediaRecord(Path.Combine(root.Path, "root.jpg"), "root.jpg", root.Path, 1, DateTimeOffset.MinValue, MediaKind.Image);
        var nestedRecord = new MediaRecord(Path.Combine(nested, "nested.jpg"), "nested.jpg", nested, 1, DateTimeOffset.MinValue, MediaKind.Image);

        var groups = MediaDirectoryGrouping.Create([nestedRecord, rootRecord], root.Path);
        var rootName = Path.GetFileName(root.Path);

        AssertEx.Equal(2, groups.Count, "根目录和深层目录应分别成组");
        AssertEx.True(groups.Any(group => group.Title == rootName), "根目录文件应使用根目录名称作为标题");
        AssertEx.True(groups.Any(group => group.Title == Path.Combine(rootName, "人物", "动漫人物")), "深层目录标题应保留完整相对层级");
    }
}

static class MediaViewerNavigatorTests
{
    public static void WrapsAround()
    {
        var first = new MediaRecord("a.mp4", "a.mp4", string.Empty, 1, DateTimeOffset.MinValue, MediaKind.Video);
        var second = new MediaRecord("b.mp4", "b.mp4", string.Empty, 1, DateTimeOffset.MinValue, MediaKind.Video);
        var navigator = new MediaViewerNavigator();

        AssertEx.Equal(second, navigator.Open([first, second], second.FullPath), "应从选中视频打开");
        AssertEx.Equal(first, navigator.Next(), "末个视频的下一个应回到首个");
        AssertEx.Equal(second, navigator.Previous(), "首个视频的上一个应回到末个");
    }
}

static class Mp4DurationReaderTests
{
    public static void ReadsFragmentTimelineDuration()
    {
        using var root = TempDirectory.Create();
        var path = Path.Combine(root.Path, "fragmented.mp4");
        File.WriteAllBytes(path, CreateFragmentedMp4());

        var duration = Mp4DurationReader.TryRead(path);

        AssertEx.True(duration.HasValue, "应从分片时间线读取视频时长");
        AssertEx.True(Math.Abs(duration.GetValueOrDefault().TotalSeconds - 20.02) < 0.001, "分片视频时长应为 20.02 秒");
    }

    private static byte[] CreateFragmentedMp4()
    {
        var trackHeader = Box("tkhd", Concat(UInt32(0), UInt32(0), UInt32(0), UInt32(1)));
        var mediaHeader = Box("mdhd", Concat(UInt32(0), UInt32(0), UInt32(0), UInt32(30000), UInt32(0)));
        var handler = Box("hdlr", Concat(UInt32(0), UInt32(0), Encoding.ASCII.GetBytes("vide")));
        var media = Box("mdia", Concat(mediaHeader, handler));
        var track = Box("trak", Concat(trackHeader, media));

        var trackExtends = Box("trex", Concat(UInt32(0), UInt32(1), UInt32(1), UInt32(1001)));
        var movieExtends = Box("mvex", trackExtends);
        var movie = Box("moov", Concat(track, movieExtends));

        var fragmentHeader = Box("tfhd", Concat(UInt32(0x000008), UInt32(1), UInt32(1001)));
        var decodeTime = Box("tfdt", Concat(UInt32(0x01000000), UInt64(500500)));
        var trackRun = Box("trun", Concat(UInt32(0), UInt32(100)));
        var trackFragment = Box("traf", Concat(fragmentHeader, decodeTime, trackRun));
        var movieFragment = Box("moof", trackFragment);

        return Concat(movie, movieFragment);
    }

    private static byte[] Box(string type, byte[] payload)
    {
        return Concat(UInt32((uint)(8 + payload.Length)), Encoding.ASCII.GetBytes(type), payload);
    }

    private static byte[] UInt32(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] UInt64(ulong value)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(part => part.Length)];
        var offset = 0;
        foreach (var part in parts)
        {
            Buffer.BlockCopy(part, 0, result, offset, part.Length);
            offset += part.Length;
        }

        return result;
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
