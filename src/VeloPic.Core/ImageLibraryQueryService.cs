namespace VeloPic.Core;

public readonly record struct ImageDimensions(int Width, int Height);

public enum ImageCollectionFilter
{
    All,
    Favorites,
    Tagged,
    Album,
    WallpaperFavorites
}

public sealed record ImageQueryOptions
{
    public string FileName { get; init; } = string.Empty;

    public ImageResolutionLevel Resolution { get; init; } = ImageResolutionLevel.All;

    public ImageOrientation Orientation { get; init; } = ImageOrientation.All;

    public string FileExtension { get; init; } = "all";

    public string? Category { get; init; }

    public ImageCollectionFilter Collection { get; init; }

    public IReadOnlySet<string> Favorites { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> WallpaperFavorites { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> AlbumPaths { get; init; } = [];

    public IReadOnlyDictionary<string, List<string>> Categories { get; init; } =
        new Dictionary<string, List<string>>();

    public IReadOnlyDictionary<string, ImageDimensions> Dimensions { get; init; } = new Dictionary<string, ImageDimensions>();

    public ImageSortField SortField { get; init; } = ImageSortField.ModifiedTime;

    public ImageSortDirection SortDirection { get; init; } = ImageSortDirection.Descending;
}

public sealed class ImageLibraryQueryService
{
    public IReadOnlyList<ImageRecord> Query(IEnumerable<ImageRecord> source, ImageQueryOptions options)
    {
        IEnumerable<ImageRecord> records = source;
        var fileName = options.FileName.Trim();
        if (fileName.Length > 0)
        {
            records = records.Where(record => record.FileName.Contains(fileName, StringComparison.OrdinalIgnoreCase));
        }

        if (options.Resolution != ImageResolutionLevel.All)
        {
            records = records.Where(record =>
                options.Dimensions.TryGetValue(record.FullPath, out var dimensions) &&
                ImageResolutionFilter.Matches(dimensions.Width, dimensions.Height, options.Resolution));
        }

        if (options.Orientation != ImageOrientation.All)
        {
            records = records.Where(record =>
                options.Dimensions.TryGetValue(record.FullPath, out var dimensions) &&
                ImageOrientationFilter.Matches(dimensions.Width, dimensions.Height, options.Orientation));
        }

        if (!string.Equals(options.FileExtension, "all", StringComparison.OrdinalIgnoreCase))
        {
            records = records.Where(record => string.Equals(
                Path.GetExtension(record.FullPath),
                options.FileExtension,
                StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(options.Category))
        {
            records = records.Where(record =>
                options.Categories.TryGetValue(record.FullPath, out var categories) &&
                categories.Contains(options.Category, StringComparer.OrdinalIgnoreCase));
        }

        records = ApplyCollectionFilter(records, options);
        records = (options.SortField, options.SortDirection) switch
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

        return records.ToList();
    }

    private static IEnumerable<ImageRecord> ApplyCollectionFilter(
        IEnumerable<ImageRecord> records,
        ImageQueryOptions options)
    {
        return options.Collection switch
        {
            ImageCollectionFilter.Favorites => records.Where(record => options.Favorites.Contains(record.FullPath)),
            ImageCollectionFilter.Tagged => records.Where(record =>
                options.Categories.TryGetValue(record.FullPath, out var categories) && categories.Count > 0),
            ImageCollectionFilter.Album => FilterAlbum(records, options.AlbumPaths),
            ImageCollectionFilter.WallpaperFavorites => records.Where(record => options.WallpaperFavorites.Contains(record.FullPath)),
            _ => records
        };
    }

    private static IEnumerable<ImageRecord> FilterAlbum(
        IEnumerable<ImageRecord> records,
        IReadOnlyCollection<string> albumPaths)
    {
        var paths = albumPaths as IReadOnlySet<string> ?? new HashSet<string>(albumPaths, StringComparer.OrdinalIgnoreCase);
        return records.Where(record => paths.Contains(record.FullPath));
    }
}
