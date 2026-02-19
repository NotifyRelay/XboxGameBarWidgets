using Microsoft.Gaming.XboxGameBar;
using NotifyRelayGamebar.Models;
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
        private UIElement _playerWidgetView;
        private UIElement _playbackControlsPanel;
        private CoreDispatcher _dispatcher;
        private Action _updateExampleNotifications;
        private Func<bool> _isInPinnedAndClosedState;
        
        private PlayerViewModel _playerViewModel;
        private NotificationViewModel _notificationViewModel;
        // Circular dependency issue? NotificationService will call MediaPlaybackManager
        // But MediaPlaybackManager needs to call NotificationService to send commands.
        // I will set NotificationService later via a property or method to avoid constructor loop.
        public NotificationService NotificationService { get; set; }

        private NowPlayingSessionManager _npsManager;
        // private IList<NowPlayingSession> MediaSessions { get; private set; } // Removed in favor of _allSessions
        // public NowPlayingSession MediaSession { get; private set; } // Changed to generic object
        
        private List<object> _allSessions = new List<object>();
        private object _currentSession; // Can be NowPlayingSession or RemoteMediaSession
        private List<RemoteMediaSession> _remoteSessions = new List<RemoteMediaSession>();
        
        private MediaPlaybackDataSource _mediaPlaybackSource;
        private int _sessionIndex = 0;

        public MediaPlaybackManager(
            XboxGameBarWidget widget,
            UIElement playerWidgetView,
            UIElement playbackControlsPanel,
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

        public IList<NowPlayingSession> GetAllSessions()
        {
             // For ExampleNotificationManager compatibility, just return local sessions or null
             // Since ExampleNotificationManager expects IList<NowPlayingSession>
             return _npsManager?.GetSessions();
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
            var localSessions = sessionManager?.GetSessions() ?? new NowPlayingSession[0];
            
            _allSessions.Clear();
            
            // Add all remote sessions first if available
            foreach (var remoteSession in _remoteSessions)
            {
                _allSessions.Add(remoteSession);
            }
            
            foreach (var s in localSessions)
            {
                _allSessions.Add(s);
            }

            _sessionIndex = FindIndexOfCurrentSession(_currentSession ?? sessionManager.CurrentSession);
            
            // Ensure index is valid
            if (_sessionIndex >= _allSessions.Count) _sessionIndex = 0;
            if (_allSessions.Count == 0) _sessionIndex = -1;

            await _dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
            {
                var mediaSessionsCount = _allSessions.Count;

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

                _playerViewModel.SessionsAvailable = mediaSessionsCount > 0;
                await _notificationViewModel.SetMediaSessionStatus(mediaSessionsCount > 0);
                
                // 更新 PlayerViewModel 中的媒体会话集合
                UpdateMediaSessionsViewModel();
                
                UpdateMediaVisibility();
                _updateExampleNotifications?.Invoke();
            });

            await LoadSession();
        }

        // 更新 PlayerViewModel 中的媒体会话视图模型集合
        private async void UpdateMediaSessionsViewModel()
        {
            // 确保在主线程上更新 UI
            await _dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
            {
                // 获取本地媒体会话
                var localSessions = _npsManager?.GetSessions() ?? new NowPlayingSession[0];
                
                // 获取当前会话 ID 集合
                var currentSessionIds = _playerViewModel.MediaSessions.Select(s => s.SessionId).ToList();
                var remoteSessionIds = _remoteSessions.Select(s => s.DeviceId).ToList();
                var localSessionIds = localSessions.Select(s => s.SourceAppId).ToList();
                
                // 合并所有会话 ID
                var allSessionIds = new List<string>();
                allSessionIds.AddRange(remoteSessionIds);
                allSessionIds.AddRange(localSessionIds);

                // 移除不再存在的会话
                var sessionsToRemove = _playerViewModel.MediaSessions.Where(s => !allSessionIds.Contains(s.SessionId)).ToList();
                foreach (var sessionToRemove in sessionsToRemove)
                {
                    _playerViewModel.MediaSessions.Remove(sessionToRemove);
                }

                // 更新或添加远程会话
                foreach (var remoteSession in _remoteSessions)
                {
                    var existingSession = _playerViewModel.MediaSessions.FirstOrDefault(s => s.SessionId == remoteSession.DeviceId);
                    
                    if (existingSession != null)
                    {
                        // 更新现有会话的属性
                        existingSession.Title = remoteSession.Title;
                        existingSession.Artist = remoteSession.Artist;
                        existingSession.IsPlaying = remoteSession.IsPlaying;

                        // 只有当封面 URL 发生变化时才更新封面
                        if (!string.IsNullOrEmpty(remoteSession.CoverUrl))
                        {
                            await existingSession.UpdateThumbnailFromUrl(remoteSession.CoverUrl);
                        }
                    }
                    else
                    {
                        // 添加新会话
                        var sessionViewModel = new NotifyRelayGamebar.Models.MediaSessionViewModel
                        {
                            SessionId = remoteSession.DeviceId,
                            DeviceId = remoteSession.DeviceId,
                            Title = remoteSession.Title,
                            Artist = remoteSession.Artist,
                            IsPlaying = remoteSession.IsPlaying,
                            IsPlayPauseEnabled = true,
                            IsPreviousEnabled = true,
                            IsNextEnabled = true,
                            IsRemoteSession = true
                        };

                        // 更新封面
                        if (!string.IsNullOrEmpty(remoteSession.CoverUrl))
                        {
                            await sessionViewModel.UpdateThumbnailFromUrl(remoteSession.CoverUrl);
                        }

                        _playerViewModel.MediaSessions.Add(sessionViewModel);
                    }
                }

                // 更新或添加本地会话
                foreach (var localSession in localSessions)
                {
                    var existingSession = _playerViewModel.MediaSessions.FirstOrDefault(s => s.SessionId == localSession.SourceAppId);
                    
                    if (existingSession != null)
                    {
                        // 更新现有会话的属性
                        try
                        {
                            var mediaSource = localSession.ActivateMediaPlaybackDataSource();
                            var mediaInfo = mediaSource.GetMediaObjectInfo();
                            var playbackInfo = mediaSource.GetMediaPlaybackInfo();
                            
                            existingSession.Title = mediaInfo.Title;
                            existingSession.Artist = mediaInfo.Artist;
                            existingSession.Album = mediaInfo.AlbumTitle;
                            existingSession.IsPlaying = (playbackInfo.PropsValid.HasFlag(MediaPlaybackProps.State) ? playbackInfo.PlaybackState : MediaPlaybackState.Unknown) == MediaPlaybackState.Playing;
                            
                            // 更新封面
                            var thumbnailStream = mediaSource.GetThumbnailStream();
                            if (thumbnailStream != null)
                            {
                                await existingSession.UpdateThumbnail(thumbnailStream);
                            }
                        }
                        catch (Exception ex)
                        {
                            Timber.Log(LoggerLevel.Error, ex, "Error updating local session");
                        }
                    }
                    else
                    {
                        // 添加新会话
                        try
                        {
                            var mediaSource = localSession.ActivateMediaPlaybackDataSource();
                            var mediaInfo = mediaSource.GetMediaObjectInfo();
                            var playbackInfo = mediaSource.GetMediaPlaybackInfo();
                            var playerCapabilities = playbackInfo.PlaybackCaps;
                            
                            var sessionViewModel = new NotifyRelayGamebar.Models.MediaSessionViewModel
                            {
                                SessionId = localSession.SourceAppId,
                                DeviceId = localSession.SourceAppId,
                                Title = mediaInfo.Title,
                                Artist = mediaInfo.Artist,
                                Album = mediaInfo.AlbumTitle,
                                IsPlaying = (playbackInfo.PropsValid.HasFlag(MediaPlaybackProps.State) ? playbackInfo.PlaybackState : MediaPlaybackState.Unknown) == MediaPlaybackState.Playing,
                                IsPlayPauseEnabled = playerCapabilities.HasFlag(MediaPlaybackCapabilities.PlayPauseToggle),
                                IsPreviousEnabled = playerCapabilities.HasFlag(MediaPlaybackCapabilities.Previous),
                                IsNextEnabled = playerCapabilities.HasFlag(MediaPlaybackCapabilities.Next),
                                IsRemoteSession = false
                            };

                            // 更新封面
                            var thumbnailStream = mediaSource.GetThumbnailStream();
                            if (thumbnailStream != null)
                            {
                                await sessionViewModel.UpdateThumbnail(thumbnailStream);
                            }

                            _playerViewModel.MediaSessions.Add(sessionViewModel);
                        }
                        catch (Exception ex)
                        {
                            Timber.Log(LoggerLevel.Error, ex, "Error adding local session");
                        }
                    }
                }
            });
        }

        public void UpdateMediaVisibility()
        {
            bool hasSessions = _allSessions.Count > 0;
            try
            {
                if (_isInPinnedAndClosedState?.Invoke() == true)
                {
                    // 固定且关闭状态下隐藏媒体控制按钮；如果没有会话，则隐藏整个媒体块
                    if (_playbackControlsPanel != null)
                    {
                        _playbackControlsPanel.Visibility = Visibility.Collapsed;
                    }
                    if (_playerWidgetView != null)
                    {
                        _playerWidgetView.Visibility = hasSessions ? Visibility.Visible : Visibility.Collapsed;
                    }
                }
                else
                {
                    // 非固定且关闭状态下根据媒体会话数量显示/隐藏
                    var visibility = hasSessions ? Visibility.Visible : Visibility.Collapsed;
                    if (_playbackControlsPanel != null)
                    {
                        _playbackControlsPanel.Visibility = visibility;
                    }
                    if (_playerWidgetView != null)
                    {
                        _playerWidgetView.Visibility = visibility;
                    }
                }
            }
            catch
            {
                // 如果访问 Pinned 属性失败，根据媒体会话数量显示/隐藏
                var visibility = hasSessions ? Visibility.Visible : Visibility.Collapsed;
                if (_playbackControlsPanel != null)
                {
                    _playbackControlsPanel.Visibility = visibility;
                }
                if (_playerWidgetView != null)
                {
                    _playerWidgetView.Visibility = visibility;
                }
            }
        }

        private int FindIndexOfCurrentSession(object currentSession)
        {
            if (currentSession == null) return 0;
            
            int i = 0;
            foreach (var session in _allSessions)
            {
                if (session is NowPlayingSession nps && currentSession is NowPlayingSession currentNps)
                {
                    if (Equals(currentNps.SourceAppId, nps.SourceAppId)) return i;
                }
                else if (session is RemoteMediaSession rms && currentSession is RemoteMediaSession currentRms)
                {
                    if (currentRms.DeviceId == rms.DeviceId) return i;
                }
                i++;
            }
            return 0;
        }

        private async Task LoadSession()
        {
            UnloadSession();

            if (_sessionIndex >= 0 && _sessionIndex < _allSessions.Count)
            {
                _currentSession = _allSessions[_sessionIndex];
            }
            else
            {
                _currentSession = null;
            }

            if (_currentSession != null)
            {
                if (_currentSession is NowPlayingSession nps)
                {
                    _mediaPlaybackSource = nps.ActivateMediaPlaybackDataSource();
                    _mediaPlaybackSource.MediaPlaybackDataChanged += MediaPlaybackSource_MediaPlaybackDataChanged;
                    await UpdatePlayer(_mediaPlaybackSource);
                }
                else if (_currentSession is RemoteMediaSession rms)
                {
                    await UpdatePlayer(rms);
                }
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
            _allSessions.Clear();
            _remoteSessions.Clear();
            _currentSession = null;
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
            // _currentSession = null; // Don't null it here, wait for next assignment
        }

        private async Task UpdatePlayer(RemoteMediaSession session)
        {
            await _dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
            {
                _playerViewModel.Title = session.Title;
                _playerViewModel.Artist = session.Artist;
                _playerViewModel.Album = ""; // 远程信息可能没有专辑信息
                
                // 远程会话支持所有控制
                _playerViewModel.IsPlaying = session.IsPlaying;
                _playerViewModel.IsPlayPauseEnabled = true;
                _playerViewModel.IsPreviousEnabled = true;
                _playerViewModel.IsNextEnabled = true;
                _playerViewModel.IsShuffleEnabled = false;
                _playerViewModel.IsRepeatEnabled = false;
                _playerViewModel.IsShuffleActive = false;
                _playerViewModel.AutoRepeatMode = MediaPlaybackRepeatMode.None;

                if (!string.IsNullOrEmpty(session.CoverUrl))
                {
                    await _playerViewModel.UpdateThumbnailFromUrl(session.CoverUrl);
                }
                else
                {
                    _playerViewModel.ThumbnailImageSource = null;
                }
            });
        }

        public void UpdateRemoteMediaSession(RemoteMediaSession session)
        {
            if (session == null || string.IsNullOrEmpty(session.DeviceId))
            {
                // 清空所有远程会话
                _remoteSessions.Clear();
            }
            else
            {
                // 查找是否已存在该设备的会话
                var existingSession = _remoteSessions.FirstOrDefault(s => s.DeviceId == session.DeviceId);
                
                if (existingSession == null)
                {
                    // 添加新会话
                    _remoteSessions.Add(session);
                }
                else
                {
                    // 更新现有会话
                    existingSession.Update(session.Title, session.Artist, session.CoverUrl, session.IsPlaying);
                }
            }
            
            // 触发重新加载以更新列表
            ReloadSessions(_npsManager);
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
        public void PreviousButton_Click(object sender, RoutedEventArgs e, string deviceId = "")
        {
            if (!string.IsNullOrEmpty(deviceId))
            {
                // 检查是否是远程会话
                var remoteSession = _remoteSessions.FirstOrDefault(s => s.DeviceId == deviceId);
                if (remoteSession != null)
                {
                    // 使用从按钮传递的设备 ID 控制远程会话
                    NotificationService?.SendMediaControlCommandAsync(deviceId, "previous");
                }
                else
                {
                    // 尝试控制本地会话
                    ControlLocalSession(deviceId, MediaPlaybackCommands.Previous);
                }
            }
            else if (_currentSession is RemoteMediaSession rms)
            {
                // 回退到当前会话的设备 ID
                NotificationService?.SendMediaControlCommandAsync(rms.DeviceId, "previous");
            }
            else
            {
                _mediaPlaybackSource?.SendMediaPlaybackCommand(MediaPlaybackCommands.Previous);
            }
        }

        public void PlayPauseButton_Click(object sender, RoutedEventArgs e, string deviceId = "")
        {
            if (!string.IsNullOrEmpty(deviceId))
            {
                // 检查是否是远程会话
                var remoteSession = _remoteSessions.FirstOrDefault(s => s.DeviceId == deviceId);
                if (remoteSession != null)
                {
                    // 使用从按钮传递的设备 ID 控制远程会话
                    NotificationService?.SendMediaControlCommandAsync(deviceId, "playPause");
                }
                else
                {
                    // 尝试控制本地会话
                    var playbackCommand = _playerViewModel.MediaSessions.FirstOrDefault(s => s.SessionId == deviceId)?.IsPlaying == true 
                        ? MediaPlaybackCommands.Pause 
                        : MediaPlaybackCommands.Play;
                    ControlLocalSession(deviceId, playbackCommand);
                }
            }
            else if (_currentSession is RemoteMediaSession rms)
            {
                // 回退到当前会话的设备 ID
                NotificationService?.SendMediaControlCommandAsync(rms.DeviceId, "playPause");
            }
            else
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
        }

        public void NextButton_Click(object sender, RoutedEventArgs e, string deviceId = "")
        {
            if (!string.IsNullOrEmpty(deviceId))
            {
                // 检查是否是远程会话
                var remoteSession = _remoteSessions.FirstOrDefault(s => s.DeviceId == deviceId);
                if (remoteSession != null)
                {
                    // 使用从按钮传递的设备 ID 控制远程会话
                    NotificationService?.SendMediaControlCommandAsync(deviceId, "next");
                }
                else
                {
                    // 尝试控制本地会话
                    ControlLocalSession(deviceId, MediaPlaybackCommands.Next);
                }
            }
            else if (_currentSession is RemoteMediaSession rms)
            {
                // 回退到当前会话的设备 ID
                NotificationService?.SendMediaControlCommandAsync(rms.DeviceId, "next");
            }
            else
            {
                _mediaPlaybackSource?.SendMediaPlaybackCommand(MediaPlaybackCommands.Next);
            }
        }

        // 控制本地会话
        private void ControlLocalSession(string sourceAppId, MediaPlaybackCommands command)
        {
            try
            {
                var localSessions = _npsManager?.GetSessions() ?? new NowPlayingSession[0];
                var targetSession = localSessions.FirstOrDefault(s => s.SourceAppId == sourceAppId);
                if (targetSession != null)
                {
                    var mediaSource = targetSession.ActivateMediaPlaybackDataSource();
                    mediaSource.SendMediaPlaybackCommand(command);
                }
            }
            catch (Exception ex)
            {
                Timber.Log(LoggerLevel.Error, ex, "Error controlling local session");
            }
        }
    }
}