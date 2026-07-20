using System.ComponentModel;
using Microsoft.UI.Xaml.Media.Imaging;
using VeloPic.Core;

namespace VeloPic.App;

public sealed class ImageTileViewModel : INotifyPropertyChanged
{
    private int _thumbnailWidth;
    private int _thumbnailHeight;

    public ImageTileViewModel(ImageRecord record, int thumbnailSize, bool isFavorite)
    {
        Record = record;
        FullPath = record.FullPath;
        FileName = record.FileName;
        DirectoryPath = record.DirectoryPath;
        SizeText = FormatSize(record.SizeBytes);
        ModifiedText = record.ModifiedAt.LocalDateTime.ToString("yyyy/MM/dd HH:mm:ss");
        _thumbnailWidth = thumbnailSize;
        _thumbnailHeight = (int)(thumbnailSize * 0.72);
        FavoriteMark = isFavorite ? "★" : string.Empty;
        Thumbnail = new BitmapImage
        {
            DecodePixelWidth = thumbnailSize,
            UriSource = new Uri(record.FullPath)
        };
    }

    public ImageRecord Record { get; }

    public string FullPath { get; }

    public string FileName { get; }

    public string DirectoryPath { get; }

    public string SizeText { get; }

    public string ModifiedText { get; }

    public int ThumbnailWidth => _thumbnailWidth;

    public int ThumbnailHeight => _thumbnailHeight;

    public string FavoriteMark { get; }

    public BitmapImage Thumbnail { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void UpdateThumbnailSize(int thumbnailSize)
    {
        var thumbnailHeight = (int)(thumbnailSize * 0.72);
        if (_thumbnailWidth == thumbnailSize && _thumbnailHeight == thumbnailHeight)
        {
            return;
        }

        _thumbnailWidth = thumbnailSize;
        _thumbnailHeight = thumbnailHeight;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThumbnailWidth)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThumbnailHeight)));
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1024L * 1024L * 1024L)
        {
            return $"{bytes / 1024d / 1024d / 1024d:N2} GB";
        }

        if (bytes >= 1024L * 1024L)
        {
            return $"{bytes / 1024d / 1024d:N1} MB";
        }

        return $"{bytes / 1024d:N0} KB";
    }
}
