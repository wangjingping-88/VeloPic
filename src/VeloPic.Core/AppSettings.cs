namespace VeloPic.Core;

public sealed class AppSettings
{
    public AppearanceSettings Appearance { get; set; } = new();

    public SortingSettings Sorting { get; set; } = new();

    public string LastFolder { get; set; } = string.Empty;

    public List<string> Favorites { get; set; } = [];

    public List<string> WallpaperFavorites { get; set; } = [];

    public Dictionary<string, List<string>> Albums { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> Categories { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> CustomCategories { get; set; } = [];
}

public sealed class AppearanceSettings
{
    public VeloPicThemeMode Theme { get; set; } = VeloPicThemeMode.System;

    public int ThumbnailSize { get; set; } = 190;

    public string WallpaperFit { get; set; } = "fill";

    public MediaBrowseMode BrowseMode { get; set; } = MediaBrowseMode.Flat;
}

public sealed class SortingSettings
{
    public ImageSortField Field { get; set; } = ImageSortField.ModifiedTime;

    public ImageSortDirection Direction { get; set; } = ImageSortDirection.Descending;
}

public enum ImageSortField
{
    ModifiedTime,
    FileName,
    FileSize
}

public enum ImageSortDirection
{
    Ascending,
    Descending
}

public enum MediaBrowseMode
{
    Flat,
    DirectoryGrouped
}

public enum VeloPicThemeMode
{
    System,
    Dark,
    Light
}

public enum ResolvedTheme
{
    Dark,
    Light
}
