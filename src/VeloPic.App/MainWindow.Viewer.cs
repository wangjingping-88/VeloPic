using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using VeloPic.Core;
using Windows.Graphics.Imaging;
using Windows.System;

namespace VeloPic.App;

public sealed partial class MainWindow : Window
{
    private readonly ImageViewerNavigator _viewerNavigator = new();
    private ImageTileViewModel? _selectedImage;
    private bool _viewerDragging;
    private bool _viewerPointerMoved;
    private Windows.Foundation.Point _viewerLastPoint;
    private Windows.Foundation.Point _viewerPressPoint;
    private ViewerMouseButton _viewerPressedButton = ViewerMouseButton.None;
    private int _selectionVersion;
    private string? _selectedPreviewPath;

    private async Task UpdateSelectionDetails(ImageTileViewModel? item)
    {
        var selectionVersion = ++_selectionVersion;
        if (item is null)
        {
            UpdateSelectionActionState(false);
            UpdateAdaptiveLayout(RootGrid.ActualWidth);
            if (_selectedPreviewPath is not null || PreviewImage.Source is not null)
            {
                _selectedPreviewPath = null;
                PreviewImage.Source = null;
            }
            DetailFileName.Text = "未选择图片";
            DetailResolution.Text = "-";
            DetailSize.Text = "-";
            DetailTime.Text = "-";
            DetailPath.Text = "-";
            UpdateSelectedCategoryChip(null);
            UpdateCollectionActionLabels();
            CategoryHintText.Text = "点击分类可筛选；选中图片后用“标记”归类";
            return;
        }

        UpdateSelectionActionState(true);
        UpdateAdaptiveLayout(RootGrid.ActualWidth);
        if (!string.Equals(_selectedPreviewPath, item.FullPath, StringComparison.OrdinalIgnoreCase))
        {
            _selectedPreviewPath = item.FullPath;
            PreviewImage.Source = new BitmapImage
            {
                DecodePixelWidth = 420,
                UriSource = new Uri(item.FullPath)
            };
        }

        UpdateSelectedCategoryChip(item.FullPath);
        DetailFileName.Text = item.FileName;
        var resolution = await ReadResolutionAsync(item.FullPath);
        if (selectionVersion != _selectionVersion ||
            !string.Equals(_selectedImage?.FullPath, item.FullPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        DetailResolution.Text = resolution;
        DetailSize.Text = item.SizeText;
        DetailTime.Text = item.ModifiedText;
        DetailPath.Text = item.FullPath;
        ToolTipService.SetToolTip(DetailPath, item.FullPath);
        UpdateCollectionActionLabels();
    }

    private void UpdateSelectionActionState(bool hasSelection)
    {
        ViewButton.IsEnabled = hasSelection;
        FavoriteButton.IsEnabled = hasSelection;
        OpenLocationButton.IsEnabled = hasSelection;
        DeleteButton.IsEnabled = hasSelection;
        AddToAlbumButton.IsEnabled = hasSelection;
        WallpaperFavoriteButton.IsEnabled = hasSelection;
        WallpaperButton.IsEnabled = hasSelection;
        WallpaperQuickButton.IsEnabled = hasSelection;
        MoreActionButton.IsEnabled = hasSelection;
        DetailTagButton.IsEnabled = hasSelection;
        WallpaperFillButton.IsEnabled = hasSelection;
        WallpaperFitButton.IsEnabled = hasSelection;
        WallpaperCenterButton.IsEnabled = hasSelection;
        WallpaperTileButton.IsEnabled = hasSelection;
        WallpaperStretchButton.IsEnabled = hasSelection;
        WallpaperDisplayBox.IsEnabled = hasSelection;
    }

    private async Task<string> ReadResolutionAsync(string path)
    {
        if (!_imageDimensions.TryGetValue(path, out var dimensions))
        {
            var loaded = await ReadImageDimensionsAsync(path, CancellationToken.None);
            if (loaded is not ImageDimensions value)
            {
                return "-";
            }

            dimensions = value;
            _imageDimensions[path] = value;
        }

        return $"{dimensions.Width} x {dimensions.Height}";
    }

    private static async Task<ImageDimensions?> ReadImageDimensionsAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 4096,
                useAsync: true);
            using var randomAccessStream = stream.AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
            cancellationToken.ThrowIfCancellationRequested();
            return new ImageDimensions((int)decoder.PixelWidth, (int)decoder.PixelHeight);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private void OpenViewer()
    {
        if (_selectedImage is null)
        {
            return;
        }

        ShowViewerRecord(_viewerNavigator.Open(_filteredImages, _selectedImage.FullPath));
        ViewerOverlay.Visibility = Visibility.Visible;
        ViewerOverlay.Focus(FocusState.Programmatic);
        DispatcherQueue.TryEnqueue(() => ViewerOverlay.Focus(FocusState.Programmatic));
    }

    private void ShowViewerRecord(ImageRecord? record)
    {
        if (record is null)
        {
            return;
        }
        ViewerTransform.ScaleX = 1;
        ViewerTransform.ScaleY = 1;
        ViewerTransform.TranslateX = 0;
        ViewerTransform.TranslateY = 0;
        ViewerImage.Width = double.NaN;
        ViewerImage.Height = double.NaN;
        ViewerImage.Stretch = Stretch.Uniform;
        ViewerImage.Source = new BitmapImage
        {
            DecodePixelWidth = 2400,
            UriSource = new Uri(record.FullPath)
        };
        ViewerCaption.Text = record.FileName;
    }

    private void CloseViewer()
    {
        ViewerOverlay.Visibility = Visibility.Collapsed;
        ViewerImage.Source = null;
        ViewerImage.Width = double.NaN;
        ViewerImage.Height = double.NaN;
        ViewerImage.Stretch = Stretch.Uniform;
        _viewerNavigator.Close();
        RestoreGalleryFocus();
        DispatcherQueue.TryEnqueue(RestoreGalleryFocus);
    }

    private void RestoreGalleryFocus()
    {
        if (_selectedImage is not null &&
            ImageGridView.ContainerFromItem(_selectedImage) is Control selectedContainer &&
            selectedContainer.Focus(FocusState.Programmatic))
        {
            return;
        }

        ImageGridView.Focus(FocusState.Programmatic);
    }

    private void ShowPreviousImage()
    {
        ShowViewerRecord(_viewerNavigator.Previous());
    }

    private void ShowNextImage()
    {
        ShowViewerRecord(_viewerNavigator.Next());
    }

    private void ViewerOverlay_OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Escape:
                CloseViewer();
                e.Handled = true;
                break;
            case VirtualKey.Left:
                ShowPreviousImage();
                e.Handled = true;
                break;
            case VirtualKey.Right:
                ShowNextImage();
                e.Handled = true;
                break;
        }
    }

    private void ViewerEscapeAccelerator_OnInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!IsViewerOpen())
        {
            return;
        }

        CloseViewer();
        args.Handled = true;
    }

    private void ViewerPreviousAccelerator_OnInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!IsViewerOpen())
        {
            return;
        }

        ShowPreviousImage();
        args.Handled = true;
    }

    private void ViewerNextAccelerator_OnInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!IsViewerOpen())
        {
            return;
        }

        ShowNextImage();
        args.Handled = true;
    }

    private bool IsViewerOpen()
    {
        return ViewerOverlay.Visibility == Visibility.Visible;
    }

    private void ViewerOverlay_OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(ViewerOverlay).Properties.MouseWheelDelta;
        var factor = delta > 0 ? 1.12 : 0.88;
        ViewerTransform.ScaleX = Math.Clamp(ViewerTransform.ScaleX * factor, 0.2, 8);
        ViewerTransform.ScaleY = ViewerTransform.ScaleX;
    }

    private void ViewerOverlay_OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(ViewerOverlay);
        var properties = point.Properties;
        _viewerPressedButton = properties.IsRightButtonPressed
            ? ViewerMouseButton.Right
            : properties.IsLeftButtonPressed
                ? ViewerMouseButton.Left
                : ViewerMouseButton.None;
        _viewerDragging = _viewerPressedButton == ViewerMouseButton.Left && ViewerTransform.ScaleX > 1.01;
        _viewerPointerMoved = false;
        _viewerPressPoint = point.Position;
        _viewerLastPoint = point.Position;
        ViewerOverlay.CapturePointer(e.Pointer);
    }

    private void ViewerOverlay_OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_viewerDragging)
        {
            return;
        }

        var point = e.GetCurrentPoint(ViewerOverlay).Position;
        if (Math.Abs(point.X - _viewerPressPoint.X) > 6 || Math.Abs(point.Y - _viewerPressPoint.Y) > 6)
        {
            _viewerPointerMoved = true;
        }

        ViewerTransform.TranslateX += point.X - _viewerLastPoint.X;
        ViewerTransform.TranslateY += point.Y - _viewerLastPoint.Y;
        _viewerLastPoint = point;
    }

    private void ViewerOverlay_OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_viewerPointerMoved)
        {
            if (_viewerPressedButton == ViewerMouseButton.Left)
            {
                ShowPreviousImage();
            }
            else if (_viewerPressedButton == ViewerMouseButton.Right)
            {
                ShowNextImage();
            }
        }

        _viewerDragging = false;
        _viewerPressedButton = ViewerMouseButton.None;
        ViewerOverlay.ReleasePointerCapture(e.Pointer);
    }

    private enum ViewerMouseButton
    {
        None,
        Left,
        Right
    }
}
