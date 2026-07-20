namespace VeloPic.Core;

public sealed class ImageFileScanner
{
    public static readonly IReadOnlySet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp",
        ".bmp",
        ".gif",
        ".tif",
        ".tiff",
        ".heic",
        ".heif",
        ".avif"
    };

    public static bool IsSupportedImage(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var extension = Path.GetExtension(path);
        return !string.IsNullOrWhiteSpace(extension) && SupportedExtensions.Contains(extension);
    }

    public IEnumerable<ImageRecord> EnumerateImages(ScanOptions options, CancellationToken cancellationToken = default)
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

    private static bool TryCreateRecord(FileInfo file, ScanOptions options, out ImageRecord record)
    {
        record = default!;
        try
        {
            if (!IsSupportedImage(file.FullName) || ShouldSkip(file, options))
            {
                return false;
            }

            record = new ImageRecord(
                file.FullName,
                file.Name,
                file.DirectoryName ?? string.Empty,
                file.Length,
                file.LastWriteTime);
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

    private static bool ShouldSkip(FileInfo file, ScanOptions options)
    {
        if (!options.IncludeHidden && file.Attributes.HasFlag(FileAttributes.Hidden))
        {
            return true;
        }

        if (!options.IncludeSystem && file.Attributes.HasFlag(FileAttributes.System))
        {
            return true;
        }

        return false;
    }

    private static IEnumerable<FileInfo> EnumerateFilesSafe(string rootPath, bool recursive, CancellationToken cancellationToken)
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

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileInfo info;
                try
                {
                    info = new FileInfo(file);
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                yield return info;
            }

            if (!recursive)
            {
                continue;
            }

            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(directory);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var child in directories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var attributes = File.GetAttributes(child);
                    if (attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        continue;
                    }
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                pending.Push(child);
            }
        }
    }
}
