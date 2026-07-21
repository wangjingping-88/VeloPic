namespace VeloPic.Core;

public sealed record MediaDirectoryGroup(string DirectoryPath, string Title, IReadOnlyList<MediaRecord> Items);

public static class MediaDirectoryGrouping
{
    public static IReadOnlyList<MediaDirectoryGroup> Create(
        IEnumerable<MediaRecord> records,
        string rootPath)
    {
        var normalizedRoot = Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var rootName = Path.GetFileName(normalizedRoot);
        if (string.IsNullOrWhiteSpace(rootName))
        {
            rootName = normalizedRoot;
        }

        return records
            .GroupBy(record => Path.GetFullPath(record.DirectoryPath), StringComparer.OrdinalIgnoreCase)
            .Select(group => new MediaDirectoryGroup(
                group.Key,
                BuildTitle(group.Key, normalizedRoot, rootName),
                group.ToList()))
            .OrderBy(group => group.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildTitle(string directoryPath, string rootPath, string rootName)
    {
        var relativePath = Path.GetRelativePath(rootPath, directoryPath);
        return string.Equals(relativePath, ".", StringComparison.Ordinal)
            ? rootName
            : Path.Combine(rootName, relativePath);
    }
}
