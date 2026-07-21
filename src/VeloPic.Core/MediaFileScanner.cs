namespace VeloPic.Core;

public sealed class MediaFileScanner
{
    public static readonly IReadOnlySet<string> SupportedImageExtensions = ImageFileScanner.SupportedExtensions;

    public static readonly IReadOnlySet<string> SupportedVideoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4",
        ".m4v",
        ".mov",
        ".avi",
        ".wmv",
        ".mkv",
        ".webm",
        ".mpeg",
        ".mpg",
        ".3gp",
        ".mts",
        ".m2ts"
    };

    public static bool IsSupportedImage(string path) => ImageFileScanner.IsSupportedImage(path);

    public static bool IsSupportedVideo(string path) => HasExtension(path, SupportedVideoExtensions);

    public static bool IsSupportedMedia(string path) => IsSupportedImage(path) || IsSupportedVideo(path);

    public IEnumerable<MediaRecord> EnumerateMedia(ScanOptions options, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(options.RootPath))
        {
            throw new DirectoryNotFoundException($"目录不存在：{options.RootPath}");
        }

        foreach (var file in EnumerateFilesSafe(options.RootPath, options.Recursive, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryCreateRecord(file, options, out var record))
            {
                continue;
            }

            yield return record;
        }
    }

    private static bool TryCreateRecord(FileInfo file, ScanOptions options, out MediaRecord record)
    {
        record = default!;
        try
        {
            var kind = IsSupportedImage(file.FullName)
                ? MediaKind.Image
                : IsSupportedVideo(file.FullName)
                    ? MediaKind.Video
                    : (MediaKind?)null;
            if (kind is null || ShouldSkip(file, options))
            {
                return false;
            }

            record = new MediaRecord(
                file.FullName,
                file.Name,
                file.DirectoryName ?? string.Empty,
                file.Length,
                file.LastWriteTime,
                kind.Value);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool HasExtension(string path, IReadOnlySet<string> extensions)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var extension = Path.GetExtension(path);
        return !string.IsNullOrWhiteSpace(extension) && extensions.Contains(extension);
    }

    private static bool ShouldSkip(FileInfo file, ScanOptions options)
    {
        return (!options.IncludeHidden && file.Attributes.HasFlag(FileAttributes.Hidden)) ||
               (!options.IncludeSystem && file.Attributes.HasFlag(FileAttributes.System));
    }

    private static IEnumerable<FileInfo> EnumerateFilesSafe(
        string rootPath,
        bool recursive,
        CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(rootPath);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var path in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileInfo file;
                try
                {
                    file = new FileInfo(path);
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }
                catch (IOException)
                {
                    continue;
                }

                yield return file;
            }

            if (!recursive)
            {
                continue;
            }

            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(directory);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var child in children)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (File.GetAttributes(child).HasFlag(FileAttributes.ReparsePoint))
                    {
                        continue;
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }
                catch (IOException)
                {
                    continue;
                }

                pending.Push(child);
            }
        }
    }
}
