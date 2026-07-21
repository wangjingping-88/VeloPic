namespace VeloPic.Core;

public sealed class MediaViewerNavigator
{
    private IReadOnlyList<MediaRecord> _media = [];

    public int Index { get; private set; }

    public MediaRecord? Open(IReadOnlyList<MediaRecord> media, string selectedPath)
    {
        _media = media;
        var selectedIndex = media
            .Select((record, index) => new { record.FullPath, Index = index })
            .FirstOrDefault(item => string.Equals(item.FullPath, selectedPath, StringComparison.OrdinalIgnoreCase))
            ?.Index ?? 0;
        return ShowAt(selectedIndex);
    }

    public MediaRecord? ShowAt(int index)
    {
        if (_media.Count == 0)
        {
            return null;
        }

        Index = (index % _media.Count + _media.Count) % _media.Count;
        return _media[Index];
    }

    public MediaRecord? Previous() => ShowAt(Index - 1);

    public MediaRecord? Next() => ShowAt(Index + 1);

    public void Close()
    {
        _media = [];
        Index = 0;
    }
}
