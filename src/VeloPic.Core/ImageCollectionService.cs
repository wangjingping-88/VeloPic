namespace VeloPic.Core;

public readonly record struct AlbumRemovalResult(int RemovedCount, bool AlbumDeleted);

public sealed class ImageCollectionService
{
    private readonly AppSettings _settings;
    private readonly HashSet<string> _favorites;
    private readonly HashSet<string> _wallpaperFavorites;

    public ImageCollectionService(AppSettings settings)
    {
        _settings = settings;
        _favorites = new HashSet<string>(settings.Favorites, StringComparer.OrdinalIgnoreCase);
        _wallpaperFavorites = new HashSet<string>(settings.WallpaperFavorites, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlySet<string> Favorites => _favorites;

    public IReadOnlySet<string> WallpaperFavorites => _wallpaperFavorites;

    public void SetFavorite(IEnumerable<string> paths, bool isFavorite)
    {
        UpdateSet(_favorites, paths, isFavorite);
        _settings.Favorites = _favorites.ToList();
    }

    public void SetWallpaperFavorite(IEnumerable<string> paths, bool isFavorite)
    {
        UpdateSet(_wallpaperFavorites, paths, isFavorite);
        _settings.WallpaperFavorites = _wallpaperFavorites.ToList();
    }

    public void AddToAlbum(string albumName, IEnumerable<string> paths)
    {
        if (!_settings.Albums.TryGetValue(albumName, out var albumPaths))
        {
            albumPaths = [];
            _settings.Albums[albumName] = albumPaths;
        }

        var existing = new HashSet<string>(albumPaths, StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (existing.Add(path))
            {
                albumPaths.Add(path);
            }
        }
    }

    public AlbumRemovalResult RemoveFromAlbum(string albumName, IEnumerable<string> paths)
    {
        if (!_settings.Albums.TryGetValue(albumName, out var albumPaths))
        {
            return default;
        }

        var removed = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        var previousCount = albumPaths.Count;
        albumPaths.RemoveAll(removed.Contains);
        var albumDeleted = albumPaths.Count == 0;
        if (albumDeleted)
        {
            _settings.Albums.Remove(albumName);
        }

        return new AlbumRemovalResult(previousCount - albumPaths.Count, albumDeleted);
    }

    public int RemoveCategory(IEnumerable<string> paths, string category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return 0;
        }

        var removedCount = 0;
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!_settings.Categories.TryGetValue(path, out var categories))
            {
                continue;
            }

            var removed = categories.RemoveAll(item =>
                string.Equals(item, category, StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
            {
                continue;
            }

            removedCount++;
            if (categories.Count == 0)
            {
                _settings.Categories.Remove(path);
            }
        }

        return removedCount;
    }

    public void RemovePath(string path)
    {
        _favorites.Remove(path);
        _wallpaperFavorites.Remove(path);
        _settings.Favorites = _favorites.ToList();
        _settings.WallpaperFavorites = _wallpaperFavorites.ToList();
        _settings.Categories.Remove(path);
        foreach (var albumName in _settings.Albums.Keys.ToList())
        {
            var album = _settings.Albums[albumName];
            album.RemoveAll(item => string.Equals(item, path, StringComparison.OrdinalIgnoreCase));
            if (album.Count == 0)
            {
                _settings.Albums.Remove(albumName);
            }
        }
    }

    private static void UpdateSet(HashSet<string> target, IEnumerable<string> paths, bool shouldContain)
    {
        foreach (var path in paths)
        {
            if (shouldContain)
            {
                target.Add(path);
            }
            else
            {
                target.Remove(path);
            }
        }
    }
}
