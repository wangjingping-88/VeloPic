namespace VeloPic.Core;

public sealed class ImageViewerNavigator
{
    private IReadOnlyList<ImageRecord> _images = [];

    public int Index { get; private set; }

    public ImageRecord? Open(IReadOnlyList<ImageRecord> images, string selectedPath)
    {
        _images = images;
        var selectedIndex = images
            .Select((image, index) => new { image.FullPath, Index = index })
            .FirstOrDefault(item => string.Equals(item.FullPath, selectedPath, StringComparison.OrdinalIgnoreCase))
            ?.Index ?? 0;
        return ShowAt(selectedIndex);
    }

    public ImageRecord? ShowAt(int index)
    {
        if (_images.Count == 0)
        {
            return null;
        }

        Index = (index % _images.Count + _images.Count) % _images.Count;
        return _images[Index];
    }

    public ImageRecord? Previous() => ShowAt(Index - 1);

    public ImageRecord? Next() => ShowAt(Index + 1);

    public void Close()
    {
        _images = [];
        Index = 0;
    }
}
