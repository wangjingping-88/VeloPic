using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using VeloPic.Core;
using Windows.Graphics.Imaging;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.System;
using UiDispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace VeloPic.App;

public sealed partial class MainWindow : Window
{
    private readonly MediaViewerNavigator _viewerNavigator = new();
    private MediaTileViewModel? _selectedImage;
    private bool _viewerDragging;
    private bool _viewerPointerMoved;
    private Windows.Foundation.Point _viewerLastPoint;
    private Windows.Foundation.Point _viewerPressPoint;
    private ViewerMouseButton _viewerPressedButton = ViewerMouseButton.None;
    private int _selectionVersion;
    private string? _selectedPreviewPath;
    private bool _videoFailureHandlersAttached;
    private MediaSource? _previewMediaSource;
    private MediaSource? _viewerMediaSource;
    private bool _isUpdatingPreviewProgress;
    private bool _isUpdatingViewerProgress;
    private TimeSpan? _previewKnownDuration;
    private TimeSpan? _viewerKnownDuration;
    private string? _previewVideoPath;
    private string? _viewerVideoPath;
    private int _previewMediaVersion;
    private int _viewerMediaVersion;

    private async Task UpdateSelectionDetails(MediaTileViewModel? item)
    {
        var selectionVersion = ++_selectionVersion;
        if (item is null)
        {
            UpdateSelectionActionState(false, false);
            UpdateAdaptiveLayout(RootGrid.ActualWidth);
            UpdateDetailPreviewFrameHeight(DetailScrollViewer.ActualHeight);
            StopPreviewVideo();
            if (_selectedPreviewPath is not null || PreviewImage.Source is not null)
            {
                _selectedPreviewPath = null;
                PreviewImage.Source = null;
            }
            DetailFileName.Text = _activeMediaKind == MediaKind.Image ? "未选择图片" : "未选择视频";
            DetailResolution.Text = "-";
            DetailSize.Text = "-";
            DetailTime.Text = "-";
            DetailPath.Text = "-";
            UpdateSelectedCategoryChip(null);
            UpdateCollectionActionLabels();
            CategoryHintText.Text = _activeMediaKind == MediaKind.Image
                ? "点击分类可筛选；选中图片后用“标记”归类"
                : "视频支持预览与沉浸播放；图片整理功能在图片标签中使用";
            return;
        }

        var isImage = item.Record.Kind == MediaKind.Image;
        UpdateSelectionActionState(true, isImage);
        UpdateAdaptiveLayout(RootGrid.ActualWidth);
        UpdateDetailPreviewFrameHeight(DetailScrollViewer.ActualHeight);
        if (isImage && !string.Equals(_selectedPreviewPath, item.FullPath, StringComparison.OrdinalIgnoreCase))
        {
            StopPreviewVideo();
            _selectedPreviewPath = item.FullPath;
            PreviewImage.Visibility = Visibility.Visible;
            PreviewImage.Source = new BitmapImage
            {
                DecodePixelWidth = 420,
                UriSource = new Uri(item.FullPath)
            };
        }
        else if (!isImage && !string.Equals(_selectedPreviewPath, item.FullPath, StringComparison.OrdinalIgnoreCase))
        {
            _selectedPreviewPath = item.FullPath;
            PreviewImage.Source = null;
            PreviewImage.Visibility = Visibility.Collapsed;
            ShowPreviewVideo(item.FullPath);
        }

        UpdateSelectedCategoryChip(isImage ? item.FullPath : null);
        DetailFileName.Text = item.FileName;
        var resolution = isImage
            ? await ReadResolutionAsync(item.FullPath)
            : await ReadVideoResolutionAsync(item.FullPath);
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

    private void UpdateSelectionActionState(bool hasSelection, bool isImage)
    {
        ViewButton.IsEnabled = hasSelection;
        FavoriteButton.IsEnabled = hasSelection && isImage;
        OpenLocationButton.IsEnabled = hasSelection;
        DeleteButton.IsEnabled = hasSelection;
        AddToAlbumButton.IsEnabled = hasSelection && isImage;
        WallpaperFavoriteButton.IsEnabled = hasSelection && isImage;
        WallpaperButton.IsEnabled = hasSelection && isImage;
        WallpaperQuickButton.IsEnabled = hasSelection && isImage;
        MoreActionButton.IsEnabled = hasSelection;
        DetailTagButton.IsEnabled = hasSelection && isImage;
        WallpaperFillButton.IsEnabled = hasSelection && isImage;
        WallpaperFitButton.IsEnabled = hasSelection && isImage;
        WallpaperCenterButton.IsEnabled = hasSelection && isImage;
        WallpaperTileButton.IsEnabled = hasSelection && isImage;
        WallpaperStretchButton.IsEnabled = hasSelection && isImage;
        WallpaperDisplayBox.IsEnabled = hasSelection && isImage;
        SmartClassificationButton.IsEnabled = _activeMediaKind == MediaKind.Image && _allImages.Count > 0;
    }

    private async Task<string> ReadVideoResolutionAsync(string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            var properties = await file.Properties.GetVideoPropertiesAsync();
            return properties.Width > 0 && properties.Height > 0
                ? $"{properties.Width} x {properties.Height}"
                : "-";
        }
        catch
        {
            return "-";
        }
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

        ShowViewerRecord(_viewerNavigator.Open(_filteredMedia, _selectedImage.FullPath));
        ViewerOverlay.Visibility = Visibility.Visible;
        ViewerOverlay.Focus(FocusState.Programmatic);
        DispatcherQueue.TryEnqueue(() => ViewerOverlay.Focus(FocusState.Programmatic));
    }

    private void ShowViewerRecord(MediaRecord? record)
    {
        if (record is null)
        {
            return;
        }
        StopViewerVideo();
        ViewerTransform.ScaleX = 1;
        ViewerTransform.ScaleY = 1;
        ViewerTransform.TranslateX = 0;
        ViewerTransform.TranslateY = 0;
        ViewerImage.Width = double.NaN;
        ViewerImage.Height = double.NaN;
        ViewerImage.Stretch = Stretch.Uniform;
        if (record.Kind == MediaKind.Video)
        {
            ViewerImage.Source = null;
            ViewerImage.Visibility = Visibility.Collapsed;
            ViewerVideoPlayer.Visibility = Visibility.Visible;
            ViewerVideoControls.Visibility = Visibility.Visible;
            ViewerVideoErrorText.Visibility = Visibility.Collapsed;
            ResetViewerProgress();
            EnsureVideoFailureHandlers();
            _viewerVideoPath = record.FullPath;
            var mediaVersion = ++_viewerMediaVersion;
            _ = ResolveVideoDurationAsync(record.FullPath, mediaVersion, isViewer: true);
            _viewerMediaSource = MediaSource.CreateFromUri(new Uri(record.FullPath));
            ViewerVideoPlayer.Source = _viewerMediaSource;
            ViewerVideoPlayer.MediaPlayer.Play();
        }
        else
        {
            ViewerImage.Visibility = Visibility.Visible;
            ViewerImage.Source = new BitmapImage
            {
                DecodePixelWidth = 2400,
                UriSource = new Uri(record.FullPath)
            };
        }
        ViewerCaption.Text = record.FileName;
    }

    private void CloseViewer()
    {
        ViewerOverlay.Visibility = Visibility.Collapsed;
        StopViewerVideo();
        ViewerImage.Source = null;
        ViewerImage.Width = double.NaN;
        ViewerImage.Height = double.NaN;
        ViewerImage.Stretch = Stretch.Uniform;
        _viewerNavigator.Close();
        RestoreGalleryFocus();
        DispatcherQueue.TryEnqueue(RestoreGalleryFocus);
    }

    private void ShowPreviewVideo(string path)
    {
        StopPreviewVideo();
        EnsureVideoFailureHandlers();
        PreviewVideoErrorText.Visibility = Visibility.Collapsed;
        PreviewVideoPlayer.Visibility = Visibility.Visible;
        PreviewVideoControls.Visibility = Visibility.Visible;
        ResetPreviewProgress();
        _previewVideoPath = path;
        var mediaVersion = ++_previewMediaVersion;
        _ = ResolveVideoDurationAsync(path, mediaVersion, isViewer: false);
        _previewMediaSource = MediaSource.CreateFromUri(new Uri(path));
        PreviewVideoPlayer.Source = _previewMediaSource;
    }

    private void PreviewVideoSurface_OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (PreviewVideoPlayer.Visibility == Visibility.Visible)
        {
            ShowPreviewVideoControls();
        }
    }

    private void PreviewPlayPauseButton_OnClick(object sender, RoutedEventArgs e)
    {
        var player = PreviewVideoPlayer.MediaPlayer;
        if (player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
        {
            player.Pause();
        }
        else
        {
            player.Play();
        }

        ShowPreviewVideoControls();
    }

    private void PreviewMuteButton_OnClick(object sender, RoutedEventArgs e)
    {
        var player = PreviewVideoPlayer.MediaPlayer;
        player.IsMuted = !player.IsMuted;
        UpdatePreviewMuteButton();
        ShowPreviewVideoControls();
    }

    private void PreviewProgressSlider_OnValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isUpdatingPreviewProgress || _previewMediaSource is null)
        {
            return;
        }

        try
        {
            var session = PreviewVideoPlayer.MediaPlayer.PlaybackSession;
            if (session.NaturalDuration > TimeSpan.Zero &&
                Math.Abs(session.Position.TotalSeconds - e.NewValue) > 0.5d)
            {
                session.Position = TimeSpan.FromSeconds(e.NewValue);
            }
        }
        catch
        {
            // Some system codecs expose a non-seekable playback session.
        }

        ShowPreviewVideoControls();
    }

    private void ViewerVideoControls_OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;
        ShowViewerVideoControls();
    }

    private void ViewerVideoControls_OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;
    }

    private void ViewerPlayPauseButton_OnClick(object sender, RoutedEventArgs e)
    {
        var player = ViewerVideoPlayer.MediaPlayer;
        if (player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
        {
            player.Pause();
        }
        else
        {
            player.Play();
        }

        ShowViewerVideoControls();
    }

    private void ViewerMuteButton_OnClick(object sender, RoutedEventArgs e)
    {
        var player = ViewerVideoPlayer.MediaPlayer;
        player.IsMuted = !player.IsMuted;
        UpdateViewerMuteButton();
        ShowViewerVideoControls();
    }

    private void ViewerProgressSlider_OnValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isUpdatingViewerProgress || _viewerMediaSource is null)
        {
            return;
        }

        try
        {
            var session = ViewerVideoPlayer.MediaPlayer.PlaybackSession;
            var duration = GetEffectiveDuration(session.NaturalDuration, _viewerKnownDuration);
            if (duration > TimeSpan.Zero &&
                Math.Abs(session.Position.TotalSeconds - e.NewValue) > 0.5d)
            {
                session.Position = TimeSpan.FromSeconds(e.NewValue);
            }
        }
        catch
        {
            // Some system codecs expose a non-seekable playback session.
        }

        ShowViewerVideoControls();
    }

    private void ShowPreviewVideoControls()
    {
        PreviewVideoControls.Visibility = Visibility.Visible;
        RestartPreviewControlsHideTimerIfPlaying();
    }

    private void PreviewControlsHideTimer_OnTick(UiDispatcherQueueTimer sender, object args)
    {
        if (PreviewVideoPlayer.Visibility == Visibility.Visible &&
            PreviewVideoPlayer.MediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
        {
            PreviewVideoControls.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowViewerVideoControls()
    {
        ViewerVideoControls.Visibility = Visibility.Visible;
        RestartViewerControlsHideTimerIfPlaying();
    }

    private void ViewerControlsHideTimer_OnTick(UiDispatcherQueueTimer sender, object args)
    {
        if (ViewerVideoPlayer.Visibility == Visibility.Visible &&
            ViewerVideoPlayer.MediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
        {
            ViewerVideoControls.Visibility = Visibility.Collapsed;
        }
    }

    private void PreviewProgressTimer_OnTick(UiDispatcherQueueTimer sender, object args)
    {
        UpdatePreviewProgress();
    }

    private void ViewerProgressTimer_OnTick(UiDispatcherQueueTimer sender, object args)
    {
        UpdateViewerProgress();
    }

    private void UpdatePreviewProgress()
    {
        if (_previewMediaSource is null)
        {
            return;
        }

        var session = PreviewVideoPlayer.MediaPlayer.PlaybackSession;
        var duration = GetEffectiveDuration(session.NaturalDuration, _previewKnownDuration);
        var position = session.Position;
        var hasDuration = duration > TimeSpan.Zero;
        var maximum = hasDuration ? duration.TotalSeconds : Math.Max(1d, position.TotalSeconds + 1d);

        _isUpdatingPreviewProgress = true;
        try
        {
            PreviewProgressSlider.IsEnabled = hasDuration;
            PreviewProgressSlider.Maximum = maximum;
            PreviewProgressSlider.Value = Math.Clamp(position.TotalSeconds, 0d, maximum);
            PreviewVideoTimeText.Text = hasDuration
                ? $"{FormatMediaTime(position)} / {FormatMediaTime(duration)}"
                : $"{FormatMediaTime(position)} / --:--";
        }
        finally
        {
            _isUpdatingPreviewProgress = false;
        }
    }

    private void UpdateViewerProgress()
    {
        if (_viewerMediaSource is null)
        {
            return;
        }

        var session = ViewerVideoPlayer.MediaPlayer.PlaybackSession;
        var duration = GetEffectiveDuration(session.NaturalDuration, _viewerKnownDuration);
        var position = session.Position;
        var hasDuration = duration > TimeSpan.Zero;
        var maximum = hasDuration ? duration.TotalSeconds : Math.Max(1d, position.TotalSeconds + 1d);

        _isUpdatingViewerProgress = true;
        try
        {
            ViewerProgressSlider.IsEnabled = hasDuration;
            ViewerProgressSlider.Maximum = maximum;
            ViewerProgressSlider.Value = Math.Clamp(position.TotalSeconds, 0d, maximum);
            ViewerVideoTimeText.Text = hasDuration
                ? $"{FormatMediaTime(position)} / {FormatMediaTime(duration)}"
                : $"{FormatMediaTime(position)} / --:--";
        }
        finally
        {
            _isUpdatingViewerProgress = false;
        }
    }

    private void ResetPreviewProgress()
    {
        _isUpdatingPreviewProgress = true;
        try
        {
            PreviewProgressSlider.IsEnabled = false;
            PreviewProgressSlider.Maximum = 1d;
            PreviewProgressSlider.Value = 0d;
            PreviewVideoTimeText.Text = "0:00 / 0:00";
        }
        finally
        {
            _isUpdatingPreviewProgress = false;
        }
    }

    private void ResetViewerProgress()
    {
        _isUpdatingViewerProgress = true;
        try
        {
            ViewerProgressSlider.IsEnabled = false;
            ViewerProgressSlider.Maximum = 1d;
            ViewerProgressSlider.Value = 0d;
            ViewerVideoTimeText.Text = "0:00 / 0:00";
        }
        finally
        {
            _isUpdatingViewerProgress = false;
        }
    }

    private static TimeSpan GetEffectiveDuration(TimeSpan naturalDuration, TimeSpan? knownDuration)
    {
        if (IsUsableDuration(naturalDuration))
        {
            return naturalDuration;
        }

        return knownDuration is TimeSpan duration && IsUsableDuration(duration)
            ? duration
            : TimeSpan.Zero;
    }

    private static bool IsUsableDuration(TimeSpan duration)
    {
        return duration > TimeSpan.Zero && duration < TimeSpan.FromDays(365);
    }

    private static string FormatMediaTime(TimeSpan value)
    {
        return value.TotalHours >= 1d
            ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{(int)value.TotalMinutes}:{value.Seconds:00}";
    }

    private void UpdatePreviewPlayPauseButton(MediaPlaybackState playbackState)
    {
        var isPlaying = playbackState == MediaPlaybackState.Playing;
        PreviewPlayPauseIcon.Glyph = isPlaying ? "\uE769" : "\uE768";
        AutomationProperties.SetName(PreviewPlayPauseButton, isPlaying ? "暂停" : "播放");
    }

    private void UpdateViewerPlayPauseButton(MediaPlaybackState playbackState)
    {
        var isPlaying = playbackState == MediaPlaybackState.Playing;
        ViewerPlayPauseIcon.Glyph = isPlaying ? "\uE769" : "\uE768";
        AutomationProperties.SetName(ViewerPlayPauseButton, isPlaying ? "暂停" : "播放");
    }

    private void UpdatePreviewMuteButton()
    {
        var isMuted = PreviewVideoPlayer.MediaPlayer.IsMuted;
        PreviewMuteIcon.Glyph = isMuted ? "\uE74F" : "\uE767";
        AutomationProperties.SetName(PreviewMuteButton, isMuted ? "恢复声音" : "静音");
    }

    private void UpdateViewerMuteButton()
    {
        var isMuted = ViewerVideoPlayer.MediaPlayer.IsMuted;
        ViewerMuteIcon.Glyph = isMuted ? "\uE74F" : "\uE767";
        AutomationProperties.SetName(ViewerMuteButton, isMuted ? "恢复声音" : "静音");
    }

    private void RestartPreviewControlsHideTimerIfPlaying()
    {
        _previewControlsHideTimer.Stop();
        if (PreviewVideoPlayer.Visibility == Visibility.Visible &&
            PreviewVideoPlayer.MediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
        {
            _previewControlsHideTimer.Start();
        }
    }

    private void RestartViewerControlsHideTimerIfPlaying()
    {
        _viewerControlsHideTimer.Stop();
        if (ViewerVideoPlayer.Visibility == Visibility.Visible &&
            ViewerVideoPlayer.MediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
        {
            _viewerControlsHideTimer.Start();
        }
    }

    private async Task ResolveVideoDurationAsync(string path, int mediaVersion, bool isViewer)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            var properties = await file.Properties.GetVideoPropertiesAsync();
            var duration = properties.Duration;
            if (!IsUsableDuration(duration))
            {
                duration = await Task.Run(() => Mp4DurationReader.TryRead(path) ?? TimeSpan.Zero);
                if (!IsUsableDuration(duration))
                {
                    return;
                }
            }

            if (isViewer)
            {
                if (mediaVersion != _viewerMediaVersion ||
                    !string.Equals(path, _viewerVideoPath, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                _viewerKnownDuration = duration;
                UpdateViewerProgress();
                return;
            }

            if (mediaVersion != _previewMediaVersion ||
                !string.Equals(path, _previewVideoPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _previewKnownDuration = duration;
            UpdatePreviewProgress();
        }
        catch
        {
            // Duration stays unknown when metadata cannot be read.
        }
    }

    private void EnsureVideoFailureHandlers()
    {
        if (_videoFailureHandlersAttached)
        {
            return;
        }

        _videoFailureHandlersAttached = true;
        PreviewVideoPlayer.MediaPlayer.MediaOpened += (_, _) =>
            DispatcherQueue.TryEnqueue(UpdatePreviewProgress);
        PreviewVideoPlayer.MediaPlayer.PlaybackSession.PlaybackStateChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(() =>
            {
                var playbackState = PreviewVideoPlayer.MediaPlayer.PlaybackSession.PlaybackState;
                UpdatePreviewPlayPauseButton(playbackState);
                if (playbackState == MediaPlaybackState.Playing)
                {
                    _previewProgressTimer.Start();
                    RestartPreviewControlsHideTimerIfPlaying();
                }
                else if (playbackState is MediaPlaybackState.Paused or MediaPlaybackState.None)
                {
                    _previewControlsHideTimer.Stop();
                    _previewProgressTimer.Stop();
                    UpdatePreviewProgress();
                    PreviewVideoControls.Visibility = Visibility.Visible;
                }
            });
        PreviewVideoPlayer.MediaPlayer.MediaFailed += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            _previewControlsHideTimer.Stop();
            _previewProgressTimer.Stop();
            PreviewVideoControls.Visibility = Visibility.Collapsed;
            PreviewVideoPlayer.Visibility = Visibility.Collapsed;
            PreviewVideoErrorText.Visibility = Visibility.Visible;
        });
        ViewerVideoPlayer.MediaPlayer.MediaOpened += (_, _) =>
            DispatcherQueue.TryEnqueue(UpdateViewerProgress);
        ViewerVideoPlayer.MediaPlayer.PlaybackSession.PlaybackStateChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(() =>
            {
                var playbackState = ViewerVideoPlayer.MediaPlayer.PlaybackSession.PlaybackState;
                UpdateViewerPlayPauseButton(playbackState);
                if (playbackState == MediaPlaybackState.Playing)
                {
                    _viewerProgressTimer.Start();
                    RestartViewerControlsHideTimerIfPlaying();
                }
                else if (playbackState is MediaPlaybackState.Paused or MediaPlaybackState.None)
                {
                    _viewerControlsHideTimer.Stop();
                    _viewerProgressTimer.Stop();
                    UpdateViewerProgress();
                    ViewerVideoControls.Visibility = Visibility.Visible;
                }
            });
        ViewerVideoPlayer.MediaPlayer.MediaFailed += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            _viewerControlsHideTimer.Stop();
            _viewerProgressTimer.Stop();
            ViewerVideoControls.Visibility = Visibility.Collapsed;
            ViewerVideoPlayer.Visibility = Visibility.Collapsed;
            ViewerVideoErrorText.Visibility = Visibility.Visible;
        });
    }

    private void StopPreviewVideo()
    {
        _previewControlsHideTimer.Stop();
        _previewProgressTimer.Stop();
        ++_previewMediaVersion;
        try
        {
            PreviewVideoPlayer.MediaPlayer.Pause();
        }
        catch
        {
        }

        PreviewVideoPlayer.Source = null;
        _previewMediaSource?.Dispose();
        _previewMediaSource = null;
        _previewVideoPath = null;
        _previewKnownDuration = null;
        ResetPreviewProgress();
        PreviewVideoControls.Visibility = Visibility.Collapsed;
        PreviewVideoPlayer.Visibility = Visibility.Collapsed;
        PreviewVideoErrorText.Visibility = Visibility.Collapsed;
    }

    private void StopViewerVideo()
    {
        _viewerControlsHideTimer.Stop();
        _viewerProgressTimer.Stop();
        ++_viewerMediaVersion;
        try
        {
            ViewerVideoPlayer.MediaPlayer.Pause();
        }
        catch
        {
        }

        ViewerVideoPlayer.Source = null;
        _viewerMediaSource?.Dispose();
        _viewerMediaSource = null;
        _viewerVideoPath = null;
        _viewerKnownDuration = null;
        ResetViewerProgress();
        ViewerVideoControls.Visibility = Visibility.Collapsed;
        ViewerVideoPlayer.Visibility = Visibility.Collapsed;
        ViewerVideoErrorText.Visibility = Visibility.Collapsed;
    }

    private void StopAndReleaseVideoPlayers()
    {
        StopPreviewVideo();
        StopViewerVideo();
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
