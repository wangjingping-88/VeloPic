namespace VeloPic.Core;

public sealed record MediaQueryOptions
{
    public MediaKind Kind { get; init; } = MediaKind.Image;
    public string FileName { get; init; } = string.Empty;
    public ImageResolutionLevel Resolution { get; init; } = ImageResolutionLevel.All;
    public string FileExtension { get; init; } = "all";
    public string? Category { get; init; }
    public ImageCollectionFilter Collection { get; init; }
    public IReadOnlySet<string> Favorites { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlySet<string> WallpaperFavorites { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyCollection<string> AlbumPaths { get; init; } = [];
    public IReadOnlyDictionary<string, string> Categories { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, ImageDimensions> Dimensions { get; init; } = new Dictionary<string, ImageDimensions>();
    public ImageSortField SortField { get; init; } = ImageSortField.ModifiedTime;
    public ImageSortDirection SortDirection { get; init; } = ImageSortDirection.Descending;
}

public sealed class MediaLibraryQueryService
{
    public IReadOnlyList<MediaRecord> Query(IEnumerable<MediaRecord> source, MediaQueryOptions options)
    {
        IEnumerable<MediaRecord> records = source.Where(record => record.Kind == options.Kind);
        var fileName = options.FileName.Trim();
        if (fileName.Length > 0)
        {
            records = records.Where(record => record.FileName.Contains(fileName, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.Equals(options.FileExtension, "all", StringComparison.OrdinalIgnoreCase))
        {
            records = records.Where(record => string.Equals(
                Path.GetExtension(record.FullPath),
                options.FileExtension,
                StringComparison.OrdinalIgnoreCase));
        }

        if (options.Kind == MediaKind.Image)
        {
            records = ApplyImageFilters(records, options);
        }

        return ApplySort(records, options.SortField, options.SortDirection).ToList();
    }

    private static IEnumerable<MediaRecord> ApplyImageFilters(
        IEnumerable<MediaRecord> records,
        MediaQueryOptions options)
    {
        if (options.Resolution != ImageResolutionLevel.All)
        {
            records = records.Where(record =>
                options.Dimensions.TryGetValue(record.FullPath, out var dimensions) &&
                ImageResolutionFilter.Matches(dimensions.Width, dimensions.Height, options.Resolution));
        }

        if (!string.IsNullOrWhiteSpace(options.Category))
        {
            records = records.Where(record =>
                options.Categories.TryGetValue(record.FullPath, out var category) &&
                string.Equals(category, options.Category, StringComparison.OrdinalIgnoreCase));
        }

        return options.Collection switch
        {
            ImageCollectionFilter.Favorites => records.Where(record => options.Favorites.Contains(record.FullPath)),
            ImageCollectionFilter.Tagged => records.Where(record => options.Categories.ContainsKey(record.FullPath)),
            ImageCollectionFilter.Album => FilterAlbum(records, options.AlbumPaths),
            ImageCollectionFilter.WallpaperFavorites => records.Where(record => options.WallpaperFavorites.Contains(record.FullPath)),
            _ => records
        };
    }

    private static IEnumerable<MediaRecord> ApplySort(
        IEnumerable<MediaRecord> records,
        ImageSortField field,
        ImageSortDirection direction)
    {
        return (field, direction) switch
        {
            (ImageSortField.FileName, ImageSortDirection.Ascending) =>
                records.OrderBy(record => record.FileName, StringComparer.OrdinalIgnoreCase),
            (ImageSortField.FileName, ImageSortDirection.Descending) =>
                records.OrderByDescending(record => record.FileName, StringComparer.OrdinalIgnoreCase),
            (ImageSortField.FileSize, ImageSortDirection.Ascending) =>
                records.OrderBy(record => record.SizeBytes).ThenBy(record => record.FileName, StringComparer.OrdinalIgnoreCase),
            (ImageSortField.FileSize, ImageSortDirection.Descending) =>
                records.OrderByDescending(record => record.SizeBytes).ThenBy(record => record.FileName, StringComparer.OrdinalIgnoreCase),
            (ImageSortField.ModifiedTime, ImageSortDirection.Ascending) =>
                records.OrderBy(record => record.ModifiedAt).ThenBy(record => record.FileName, StringComparer.OrdinalIgnoreCase),
            _ => records.OrderByDescending(record => record.ModifiedAt).ThenBy(record => record.FileName, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static IEnumerable<MediaRecord> FilterAlbum(
        IEnumerable<MediaRecord> records,
        IReadOnlyCollection<string> albumPaths)
    {
        var paths = albumPaths as IReadOnlySet<string> ?? new HashSet<string>(albumPaths, StringComparer.OrdinalIgnoreCase);
        return records.Where(record => paths.Contains(record.FullPath));
    }
}
