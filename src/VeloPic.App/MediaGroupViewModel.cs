using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using VeloPic.Core;

namespace VeloPic.App;

public sealed class MediaGroupViewModel : INotifyPropertyChanged
{
    private bool _isExpanded;

    public MediaGroupViewModel(
        string directoryPath,
        string title,
        IEnumerable<MediaTileViewModel> items,
        bool isExpanded)
    {
        DirectoryPath = directoryPath;
        Title = title;
        AllItems = items.ToList();
        _isExpanded = isExpanded;
        VisibleItems = new ObservableCollection<MediaTileViewModel>(isExpanded ? AllItems : []);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string DirectoryPath { get; }

    public string Title { get; }

    public IReadOnlyList<MediaTileViewModel> AllItems { get; }

    public ObservableCollection<MediaTileViewModel> VisibleItems { get; }

    public int Count => AllItems.Count;

    public string CountText => Count.ToString("N0");

    public bool IsExpanded
    {
        get => _isExpanded;
        private set
        {
            if (_isExpanded == value)
            {
                return;
            }

            _isExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ChevronGlyph));
        }
    }

    public string ChevronGlyph => IsExpanded ? "\uE70D" : "\uE76C";

    public bool Toggle()
    {
        return SetExpanded(!IsExpanded);
    }

    public bool SetExpanded(bool isExpanded)
    {
        if (IsExpanded == isExpanded)
        {
            return false;
        }

        IsExpanded = isExpanded;
        VisibleItems.Clear();
        if (IsExpanded)
        {
            foreach (var item in AllItems)
            {
                VisibleItems.Add(item);
            }
        }

        return true;
    }

    public void UpdateThumbnailSize(int thumbnailSize)
    {
        foreach (var item in AllItems)
        {
            item.UpdateThumbnailSize(thumbnailSize);
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
