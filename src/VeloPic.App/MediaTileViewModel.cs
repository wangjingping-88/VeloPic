using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using VeloPic.Core;

namespace VeloPic.App;

public sealed class MediaTileViewModel : INotifyPropertyChanged
{
    private double _thumbnailWidth;
    private double _thumbnailHeight;
    private ImageSource? _thumbnail;
    private string _favoriteMark;

    public MediaTileViewModel(MediaRecord record, int thumbnailSize, bool isFavorite)
    {
        Record = record;
        FileName = record.FileName;
        FullPath = record.FullPath;
        _favoriteMark = GetFavoriteMark(record, isFavorite);
        if (record.Kind == MediaKind.Image)
        {
            _thumbnail = new BitmapImage
            {
                DecodePixelWidth = Math.Max(240, thumbnailSize * 2),
                UriSource = new Uri(record.FullPath)
            };
        }

        UpdateThumbnailSize(thumbnailSize);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MediaRecord Record { get; }

    public string FileName { get; }

    public string FullPath { get; }

    public string FavoriteMark => _favoriteMark;

    public string SizeText => Record.SizeBytes switch
    {
        >= 1024L * 1024L * 1024L => $"{Record.SizeBytes / (1024d * 1024d * 1024d):0.##} GB",
        >= 1024L * 1024L => $"{Record.SizeBytes / (1024d * 1024d):0.##} MB",
        _ => $"{Math.Max(1, Record.SizeBytes / 1024d):0} KB"
    };

    public string ModifiedText => Record.ModifiedAt.LocalDateTime.ToString("yyyy/MM/dd HH:mm:ss");

    public bool IsVideo => Record.Kind == MediaKind.Video;

    public Visibility VideoFallbackVisibility => IsVideo && Thumbnail is null
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility VideoPlayBadgeVisibility => IsVideo
        ? Visibility.Visible
        : Visibility.Collapsed;

    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        private set
        {
            if (ReferenceEquals(_thumbnail, value))
            {
                return;
            }

            _thumbnail = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VideoFallbackVisibility));
        }
    }

    public double ThumbnailWidth
    {
        get => _thumbnailWidth;
        private set
        {
            if (Math.Abs(_thumbnailWidth - value) < 0.1)
            {
                return;
            }

            _thumbnailWidth = value;
            OnPropertyChanged();
        }
    }

    public double ThumbnailHeight
    {
        get => _thumbnailHeight;
        private set
        {
            if (Math.Abs(_thumbnailHeight - value) < 0.1)
            {
                return;
            }

            _thumbnailHeight = value;
            OnPropertyChanged();
        }
    }

    public void UpdateThumbnailSize(int thumbnailSize)
    {
        ThumbnailWidth = thumbnailSize;
        ThumbnailHeight = Math.Max(96, Math.Round(thumbnailSize * 0.72));
    }

    public void SetThumbnail(ImageSource? thumbnail) => Thumbnail = thumbnail;

    public void SetFavorite(bool isFavorite)
    {
        var favoriteMark = GetFavoriteMark(Record, isFavorite);
        if (string.Equals(_favoriteMark, favoriteMark, StringComparison.Ordinal))
        {
            return;
        }

        _favoriteMark = favoriteMark;
        OnPropertyChanged(nameof(FavoriteMark));
    }

    private static string GetFavoriteMark(MediaRecord record, bool isFavorite) =>
        isFavorite && record.Kind == MediaKind.Image ? "\u2605" : string.Empty;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
