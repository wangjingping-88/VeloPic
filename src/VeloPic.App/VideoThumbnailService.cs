using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace VeloPic.App;

public sealed class VideoThumbnailService
{
    private readonly SemaphoreSlim _concurrency = new(4, 4);

    public async Task<BitmapImage?> LoadAsync(
        string path,
        uint requestedSize,
        CancellationToken cancellationToken)
    {
        await _concurrency.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var thumbnail = await file.GetThumbnailAsync(
                ThumbnailMode.VideosView,
                Math.Max(160u, requestedSize),
                ThumbnailOptions.UseCurrentScale);
            cancellationToken.ThrowIfCancellationRequested();
            if (thumbnail is null || thumbnail.Size == 0)
            {
                return null;
            }

            var bitmap = new BitmapImage
            {
                DecodePixelWidth = (int)Math.Max(320u, requestedSize * 2)
            };
            await bitmap.SetSourceAsync(thumbnail);
            cancellationToken.ThrowIfCancellationRequested();
            return bitmap;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
        finally
        {
            _concurrency.Release();
        }
    }
}
