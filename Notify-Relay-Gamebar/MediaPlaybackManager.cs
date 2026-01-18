using Microsoft.Gaming.XboxGameBar;
using NPSMLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TimberLog;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace NotifyRelayGamebar
{
    public class MediaPlaybackManager
    {
        private XboxGameBarWidget _widget;
        private Panel _playerWidgetView;
        private Panel _playbackControlsPanel;
        private CoreDispatcher _dispatcher;
        private Action _updateExampleNotifications;
        private Func<bool> _isInPinnedAndClosedState;
        
        private PlayerViewModel _playerViewModel;
        private NotificationViewModel _notificationViewModel;

        private NowPlayingSessionManager _npsManager;
        public IList<NowPlayingSession> MediaSessions { get; private set; }
        public NowPlayingSession MediaSession { get; private set; }
        private MediaPlaybackDataSource _mediaPlaybackSource;
        private int _sessionIndex = 0;

        public MediaPlaybackManager(
            XboxGameBarWidget widget,
            Panel playerWidgetView,
            Panel playbackControlsPanel,
            CoreDispatcher dispatcher,
            PlayerViewModel playerViewModel,
            NotificationViewModel notificationViewModel,
            Action updateExampleNotifications,
            Func<bool> isInPinnedAndClosedState)
        {
            _widget = widget;
            _playerWidgetView = playerWidgetView;
            _playbackControlsPanel = playbackControlsPanel;
            _dispatcher = dispatcher;
            _playerViewModel = playerViewModel;
            _notificationViewModel = notificationViewModel;
            _updateExampleNotifications = updateExampleNotifications;
            _isInPinnedAndClosedState = isInPinnedAndClosedState;
        }

        public void StartService()
        {
            if (_npsManager != null)
            {
                StopService();
            }

            try
            {
                _npsManager = new NowPlayingSessionManager();
                _npsManager.SessionListChanged += NPSManager_SessionsChanged;
                ReloadSessions(_npsManager);
            }
            catch (Exception ex)
            {
                Timber.Log(LoggerLevel.Error, ex);
            }
        }

        private void NPSManager_SessionsChanged(object sender, NowPlayingSessionManagerEventArgs args)
        {
            if (args.NotificationType != NowPlayingSessionManagerNotificationType.CurrentSessionChanged)
            {
                ReloadSessions(sender as NowPlayingSessionManager ?? _npsManager);
            }
        }

        private async void ReloadSessions(NowPlayingSessionManager sessionManager)
        {
            MediaSessions = sessionManager?.GetSessions();
            _sessionIndex = FindIndexOfCurrentSession(MediaSession ?? sessionManager.CurrentSession);

            await _dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
            {
                var mediaSessionsCount = (MediaSessions?.Count ?? 1);

                if (mediaSessionsCount > 1)
                {
                    _playerViewModel.ShowNextSession = _sessionIndex + 1 < mediaSessionsCount;
                    _playerViewModel.ShowPreviousSession = _sessionIndex - 1 >= 0;
                }
                else
                {
                    _playerViewModel.ShowNextSession = false;
                    _playerViewModel.ShowPreviousSession = false;
                }

                _playerViewModel.SessionsAvailable = (MediaSessions?.Count ?? 0) > 0;
                await _notificationViewModel.SetMediaSessionStatus((MediaSessions?.Count ?? 0) > 0);
                
                UpdateMediaVisibility();
                _updateExampleNotifications?.Invoke();
            });

            await LoadSession();
        }

        public void UpdateMediaVisibility()
        {
            bool hasSessions = (MediaSessions?.Count ?? 0) > 0;
            try
            {
                if (_isInPinnedAndClosedState?.Invoke() == true)
                {
                    // 固定且关闭状态下隐藏媒体控制按钮；如果没有会话，则隐藏整个媒体块
                    _playbackControlsPanel.Visibility = Visibility.Collapsed;
                    bool hasSessionsPinned = (MediaSessions?.Count ?? 0) > 0;
                    _playerWidgetView.Visibility = hasSessionsPinned ? Visibility.Visible : Visibility.Collapsed;
                }
                else
                {
                    // 非固定且关闭状态下根据媒体会话数量显示/隐藏
                    var visibility = hasSessions ? Visibility.Visible : Visibility.Collapsed;
                    _playbackControlsPanel.Visibility = visibility;
                    _playerWidgetView.Visibility = visibility;
                }
            }
            catch
            {
                // 如果访问 Pinned 属性失败，根据媒体会话数量显示/隐藏
                var visibility = hasSessions ? Visibility.Visible : Visibility.Collapsed;
                _playbackControlsPanel.Visibility = visibility;
                _playerWidgetView.Visibility = visibility;
            }
        }

        private int FindIndexOfCurrentSession(NowPlayingSession currentSession)
        {
            int i = 0;

            foreach (var session in MediaSessions)
            {
                if (Equals(currentSession.SourceAppId, session.SourceAppId))
                {
                    return i;
                }

                i++;
            }

            return 0;
        }

        private async Task LoadSession()
        {
            UnloadSession();

            MediaSession = MediaSessions.ElementAtOrDefault(_sessionIndex);

            if (MediaSession != null)
            {
                _mediaPlaybackSource = MediaSession.ActivateMediaPlaybackDataSource();
                _mediaPlaybackSource.MediaPlaybackDataChanged += MediaPlaybackSource_MediaPlaybackDataChanged;

                await UpdatePlayer(_mediaPlaybackSource);
            }
        }

        public void StopService()
        {
            // Unregister events
            if (_npsManager != null)
            {
                try
                {
                    _npsManager.SessionListChanged -= NPSManager_SessionsChanged;
                }
                catch (Exception ex)
                {
                    Timber.Log(LoggerLevel.Error, ex);
                }
            }

            _npsManager = null;
            MediaSessions = null;
            var _ = _notificationViewModel.SetMediaSessionStatus(false);
        }

        private void UnloadSession()
        {
            if (_mediaPlaybackSource != null)
            {
                try
                {
                    _mediaPlaybackSource.MediaPlaybackDataChanged -= MediaPlaybackSource_MediaPlaybackDataChanged;
                }
                catch (Exception ex)
                {
                    Timber.Log(LoggerLevel.Error, ex);
                }
            }
            _mediaPlaybackSource = null;
            MediaSession = null;
        }

        private async Task UpdatePlayer(MediaPlaybackDataSource source)
        {
            await UpdateMediaProperties(source);
            await UpdatePlaybackInfo(source);
        }

        private async Task UpdateMediaProperties(MediaPlaybackDataSource source)
        {
            var mediaObjectInfo = source.GetMediaObjectInfo();
            var thumbnailStream = source.GetThumbnailStream();

            await _dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
            {
                _playerViewModel.Title = mediaObjectInfo.Title;
                _playerViewModel.Artist = mediaObjectInfo.Artist;
                _playerViewModel.Album = mediaObjectInfo.AlbumTitle;

                if (string.IsNullOrWhiteSpace(_playerViewModel.Artist) && !string.IsNullOrWhiteSpace(mediaObjectInfo.AlbumArtist))
                {
                    _playerViewModel.Artist = mediaObjectInfo.AlbumArtist;
                }

                await _playerViewModel.UpdateThumbnail(thumbnailStream);
            });
        }

        private async Task UpdatePlaybackInfo(MediaPlaybackDataSource source)
        {
            var playbackInfo = source.GetMediaPlaybackInfo();

            await _dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                var playerCapabilities = playbackInfo.PlaybackCaps;
                var playerValidProps = playbackInfo.PropsValid;

                _playerViewModel.IsShuffleActive = playerValidProps.HasFlag(MediaPlaybackProps.ShuffleEnabled) ? playbackInfo.ShuffleEnabled : false;
                _playerViewModel.AutoRepeatMode = playerValidProps.HasFlag(MediaPlaybackProps.AutoRepeatMode) ? playbackInfo.RepeatMode : MediaPlaybackRepeatMode.Unknown;
                _playerViewModel.IsPlaying = (playerValidProps.HasFlag(MediaPlaybackProps.State) ? playbackInfo.PlaybackState : MediaPlaybackState.Unknown) switch
                {
                    MediaPlaybackState.Playing => true,
                    _ => false,
                };

                _playerViewModel.IsShuffleEnabled = playerCapabilities.HasFlag(MediaPlaybackCapabilities.Shuffle);
                _playerViewModel.IsRepeatEnabled = playerCapabilities.HasFlag(MediaPlaybackCapabilities.Repeat);
                _playerViewModel.IsPreviousEnabled = playerCapabilities.HasFlag(MediaPlaybackCapabilities.Previous);
                _playerViewModel.IsNextEnabled = playerCapabilities.HasFlag(MediaPlaybackCapabilities.Next);
                _playerViewModel.IsPlayPauseEnabled = playerCapabilities.HasFlag(MediaPlaybackCapabilities.PlayPauseToggle);
            });
        }

        private async void MediaPlaybackSource_MediaPlaybackDataChanged(object sender, MediaPlaybackDataChangedArgs e)
        {
            switch (e.DataChangedEvent)
            {
                case MediaPlaybackDataChangedEvent.PlaybackInfoChanged:
                    await UpdatePlaybackInfo(e.MediaPlaybackDataSource);
                    break;
                case MediaPlaybackDataChangedEvent.MediaInfoChanged:
                    await UpdateMediaProperties(e.MediaPlaybackDataSource);
                    break;
            }
        }

        // 媒体控制按钮事件处理
        public void PreviousButton_Click(object sender, RoutedEventArgs e)
        {
            _mediaPlaybackSource?.SendMediaPlaybackCommand(MediaPlaybackCommands.Previous);
        }

        public void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_playerViewModel.IsPlaying)
            {
                _mediaPlaybackSource?.SendMediaPlaybackCommand(MediaPlaybackCommands.Pause);
            }
            else
            {
                _mediaPlaybackSource?.SendMediaPlaybackCommand(MediaPlaybackCommands.Play);
            }
        }

        public void NextButton_Click(object sender, RoutedEventArgs e)
        {
            _mediaPlaybackSource?.SendMediaPlaybackCommand(MediaPlaybackCommands.Next);
        }
    }
}