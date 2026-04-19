using Microsoft.Gaming.XboxGameBar;
using NotifyRelayGamebar.Models;
using NotifyRelayGamebar.Utils;
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
        private bool _mediaPlaybackSourceSubscribed;
        private int _sessionIndex = 0;

        private DispatcherTimer _pollTimer;
        private DateTime _lastManagerRefresh = DateTime.MinValue;
        private DateTime _lastFullSessionRefresh = DateTime.MinValue;

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

            LyricsSettings.SettingsChanged += LyricsSettings_SettingsChanged;
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

                _pollTimer = new DispatcherTimer();
                _pollTimer.Interval = TimeSpan.FromMilliseconds(300);
                _pollTimer.Tick += PollTimer_Tick;
                _pollTimer.Start();
                _lastManagerRefresh = DateTime.Now;
                _lastFullSessionRefresh = DateTime.Now;
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
                    
                    // 检查是否有有效信息（参考WinIsland逻辑）
                    bool hasValidInfo = !string.IsNullOrWhiteSpace(remoteSession.Title) || !string.IsNullOrWhiteSpace(remoteSession.Artist);
                    bool shouldPulse = (DateTime.Now - remoteSession.LastTitleUpdateTime).TotalSeconds <= 5;
                    
                    if (existingSession != null)
                    {
                        if (hasValidInfo)
                        {
                            // 更新现有会话的属性
                            existingSession.Title = remoteSession.Title;
                            existingSession.OriginalTitle = remoteSession.Title ?? string.Empty;
                            existingSession.Artist = remoteSession.Artist;
                            existingSession.IsPlaying = remoteSession.IsPlaying || shouldPulse;
                            existingSession.LyricLines = null;
                            existingSession.CurrentLyricLine = string.Empty;
                            existingSession.LyricsKey = string.Empty;
                            existingSession.LyricsRequestId = 0;
                            existingSession.DurationSeconds = 0;

                            // 只有当封面 URL 发生变化时才更新封面
                            if (!string.IsNullOrEmpty(remoteSession.CoverUrl))
                            {
                                await existingSession.UpdateThumbnailFromUrl(remoteSession.CoverUrl);
                            }
                        }
                        else
                        {
                            // 没有有效信息，移除会话
                            _playerViewModel.MediaSessions.Remove(existingSession);
                        }
                    }
                    else if (hasValidInfo)
                    {
                        // 添加新会话（只有当有有效信息时）
                        var sessionViewModel = new NotifyRelayGamebar.Models.MediaSessionViewModel
                        {
                            SessionId = remoteSession.DeviceId,
                            DeviceId = remoteSession.DeviceId,
                            DeviceName = remoteSession.DeviceName,
                            Title = remoteSession.Title,
                            OriginalTitle = remoteSession.Title ?? string.Empty,
                            Artist = remoteSession.Artist,
                            IsPlaying = remoteSession.IsPlaying || shouldPulse,
                            IsPlayPauseEnabled = true,
                            IsPreviousEnabled = true,
                            IsNextEnabled = true,
                            IsRemoteSession = true,
                            LyricLines = null,
                            CurrentLyricLine = string.Empty,
                            LyricsKey = string.Empty,
                            LyricsRequestId = 0,
                            DurationSeconds = 0
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
                    
                    try
                    {
                        var mediaSource = localSession.ActivateMediaPlaybackDataSource();
                        var mediaInfo = mediaSource.GetMediaObjectInfo();
                        var playbackInfo = mediaSource.GetMediaPlaybackInfo();

                        string title = mediaInfo.Title ?? string.Empty;
                        string artist = mediaInfo.Artist ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(artist) && !string.IsNullOrWhiteSpace(mediaInfo.AlbumArtist))
                        {
                            artist = mediaInfo.AlbumArtist;
                        }

                        var durationSeconds = 0;
                        var hasTimeline = TryGetTimelineFromSource(mediaSource, playbackInfo, out var position, out var duration);
                        if (hasTimeline)
                        {
                            if (duration > TimeSpan.Zero)
                            {
                                durationSeconds = (int)Math.Round(duration.TotalSeconds);
                            }
                        }
                        
                        // 检查是否有有效信息（参考WinIsland逻辑）
                        bool hasValidInfo = !string.IsNullOrWhiteSpace(title) || !string.IsNullOrWhiteSpace(artist);
                        
                        if (existingSession != null)
                        {
                            if (hasValidInfo)
                            {
                                // 更新现有会话的属性
                                existingSession.OriginalTitle = title;
                                existingSession.Artist = artist;
                                existingSession.Album = mediaInfo.AlbumTitle;
                                existingSession.DurationSeconds = durationSeconds;
                                existingSession.IsPlaying = (playbackInfo.PropsValid.HasFlag(MediaPlaybackProps.State) ? playbackInfo.PlaybackState : MediaPlaybackState.Unknown) == MediaPlaybackState.Playing;

                                if (existingSession.LyricLines == null || existingSession.LyricLines.Count == 0)
                                {
                                    existingSession.Title = title;
                                }

                                EnsureLyricsForSessionAsync(existingSession, title, artist, durationSeconds);

                                if (hasTimeline)
                                {
                                    UpdateLyricLineForSession(existingSession, position);
                                }
                                
                                // 更新封面
                                var thumbnailStream = mediaSource.GetThumbnailStream();
                                if (thumbnailStream != null)
                                {
                                    await existingSession.UpdateThumbnail(thumbnailStream);
                                }
                            }
                            else
                            {
                                // 没有有效信息，移除会话
                                _playerViewModel.MediaSessions.Remove(existingSession);
                            }
                        }
                        else if (hasValidInfo)
                        {
                            // 添加新会话（只有当有有效信息时）
                            var playerCapabilities = playbackInfo.PlaybackCaps;
                            
                            var sessionViewModel = new NotifyRelayGamebar.Models.MediaSessionViewModel
                            {
                                SessionId = localSession.SourceAppId,
                                DeviceId = localSession.SourceAppId,
                                Title = title,
                                OriginalTitle = title,
                                Artist = artist,
                                Album = mediaInfo.AlbumTitle,
                                IsPlaying = (playbackInfo.PropsValid.HasFlag(MediaPlaybackProps.State) ? playbackInfo.PlaybackState : MediaPlaybackState.Unknown) == MediaPlaybackState.Playing,
                                IsPlayPauseEnabled = playerCapabilities.HasFlag(MediaPlaybackCapabilities.PlayPauseToggle),
                                IsPreviousEnabled = playerCapabilities.HasFlag(MediaPlaybackCapabilities.Previous),
                                IsNextEnabled = playerCapabilities.HasFlag(MediaPlaybackCapabilities.Next),
                                IsRemoteSession = false,
                                DurationSeconds = durationSeconds
                            };

                            EnsureLyricsForSessionAsync(sessionViewModel, title, artist, durationSeconds);

                            if (hasTimeline)
                            {
                                UpdateLyricLineForSession(sessionViewModel, position);
                            }

                            // 更新封面
                            var thumbnailStream = mediaSource.GetThumbnailStream();
                            if (thumbnailStream != null)
                            {
                                await sessionViewModel.UpdateThumbnail(thumbnailStream);
                            }

                            _playerViewModel.MediaSessions.Add(sessionViewModel);
                        }
                    }
                    catch (Exception ex)
                    {
                        Timber.Log(LoggerLevel.Error, ex, "Error processing local session");
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
                    _mediaPlaybackSourceSubscribed = true;
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
            _pollTimer?.Stop();
            _pollTimer = null;

            LyricsSettings.SettingsChanged -= LyricsSettings_SettingsChanged;

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
            if (_mediaPlaybackSource != null && _mediaPlaybackSourceSubscribed)
            {
                try
                {
                    _mediaPlaybackSource.MediaPlaybackDataChanged -= MediaPlaybackSource_MediaPlaybackDataChanged;
                }
                catch (Exception ex)
                {
                    Timber.Log(LoggerLevel.Warn, ex, "Failed to unsubscribe MediaPlaybackDataChanged");
                }
                finally
                {
                    _mediaPlaybackSourceSubscribed = false;
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
                    session.LastTitleUpdateTime = DateTime.Now;
                    session.LastUpdateTime = DateTime.Now;
                    // 添加新会话
                    _remoteSessions.Add(session);
                    ScheduleRemotePulse(session.DeviceId, session.LastTitleUpdateTime);
                }
                else
                {
                    bool titleChanged = !string.Equals(existingSession.Title, session.Title, StringComparison.Ordinal);
                    if (titleChanged)
                    {
                        existingSession.LastTitleUpdateTime = DateTime.Now;
                    }
                    // 更新现有会话
                    existingSession.Update(session.Title, session.Artist, session.CoverUrl, session.IsPlaying);

                    if (titleChanged)
                    {
                        ScheduleRemotePulse(existingSession.DeviceId, existingSession.LastTitleUpdateTime);
                    }
                }
            }
            
            // 触发重新加载以更新列表
            ReloadSessions(_npsManager);
        }

        private void ScheduleRemotePulse(string deviceId, DateTime pulseStart)
        {
            var _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                var current = _remoteSessions.FirstOrDefault(s => s.DeviceId == deviceId);
                if (current != null && current.LastTitleUpdateTime == pulseStart)
                {
                    UpdateMediaSessionsViewModel();
                }
            });
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
            
            // 同时更新 MediaSessions 集合中的对应项
            UpdateMediaSessionsViewModel();
        }

        private void PollTimer_Tick(object sender, object e)
        {
            if (_npsManager == null) return;

            try
            {
                if ((DateTime.Now - _lastManagerRefresh).TotalSeconds > 30)
                {
                    RefreshSessionManager();
                    _lastManagerRefresh = DateTime.Now;
                    return;
                }

                PollCurrentSessionFresh();

                if ((DateTime.Now - _lastFullSessionRefresh).TotalSeconds > 3)
                {
                    PollAllSessionsFresh();
                    _lastFullSessionRefresh = DateTime.Now;
                }
            }
            catch (Exception ex)
            {
                Timber.Log(LoggerLevel.Error, ex, "Error in poll timer");
            }
        }

        private void PollCurrentSessionFresh()
        {
            try
            {
                var managerCurrent = _npsManager?.CurrentSession;

                if (managerCurrent == null)
                {
                    if (_currentSession != null)
                    {
                        ReloadSessions(_npsManager);
                    }
                    return;
                }

                if (_currentSession is NowPlayingSession ourSession)
                {
                    if (ourSession.SourceAppId != managerCurrent.SourceAppId)
                    {
                        ReloadSessions(_npsManager);
                        return;
                    }
                }
                else if (_currentSession == null)
                {
                    ReloadSessions(_npsManager);
                    return;
                }

                var freshSource = managerCurrent.ActivateMediaPlaybackDataSource();
                var playbackInfo = freshSource.GetMediaPlaybackInfo();
                var mediaInfo = freshSource.GetMediaObjectInfo();

                var currentVm = _playerViewModel.MediaSessions
                    .FirstOrDefault(s => !s.IsRemoteSession && s.SessionId == managerCurrent.SourceAppId);

                if (currentVm != null && TryGetTimelineFromSource(freshSource, playbackInfo, out var position, out var duration))
                {
                    if (duration > TimeSpan.Zero)
                    {
                        currentVm.DurationSeconds = (int)Math.Round(duration.TotalSeconds);
                    }

                    UpdateLyricLineForSession(currentVm, position);
                }

                bool isPlaying = (playbackInfo.PropsValid.HasFlag(MediaPlaybackProps.State)
                    ? playbackInfo.PlaybackState
                    : MediaPlaybackState.Unknown) == MediaPlaybackState.Playing;

                if (_playerViewModel.IsPlaying != isPlaying)
                {
                    _playerViewModel.IsPlaying = isPlaying;
                }

                var caps = playbackInfo.PlaybackCaps;
                var validProps = playbackInfo.PropsValid;

                bool shuffleActive = validProps.HasFlag(MediaPlaybackProps.ShuffleEnabled) && playbackInfo.ShuffleEnabled;
                var repeatMode = validProps.HasFlag(MediaPlaybackProps.AutoRepeatMode) ? playbackInfo.RepeatMode : MediaPlaybackRepeatMode.Unknown;

                if (_playerViewModel.IsShuffleActive != shuffleActive)
                    _playerViewModel.IsShuffleActive = shuffleActive;
                if (_playerViewModel.AutoRepeatMode != repeatMode)
                    _playerViewModel.AutoRepeatMode = repeatMode;

                string title = mediaInfo.Title ?? "";
                string artist = mediaInfo.Artist ?? "";
                if (string.IsNullOrWhiteSpace(artist) && !string.IsNullOrWhiteSpace(mediaInfo.AlbumArtist))
                    artist = mediaInfo.AlbumArtist;

                if (title != _playerViewModel.Title || artist != _playerViewModel.Artist)
                {
                    _playerViewModel.Title = title;
                    _playerViewModel.Artist = artist;
                    _playerViewModel.Album = mediaInfo.AlbumTitle ?? "";

                    var thumbnailStream = freshSource.GetThumbnailStream();
                    if (thumbnailStream != null)
                    {
                        var _ = _playerViewModel.UpdateThumbnail(thumbnailStream);
                    }
                    else
                    {
                        _playerViewModel.ThumbnailImageSource = null;
                    }

                    UpdateMediaSessionsViewModel();
                }
            }
            catch (Exception ex)
            {
                Timber.Log(LoggerLevel.Error, ex, "Error polling current session fresh");
            }
        }

        private void PollAllSessionsFresh()
        {
            try
            {
                var localSessions = _npsManager?.GetSessions();
                if (localSessions == null) return;

                var currentSessionIds = new HashSet<string>(localSessions.Select(s => s.SourceAppId));
                var viewModelIds = new HashSet<string>(_playerViewModel.MediaSessions
                    .Where(s => !s.IsRemoteSession).Select(s => s.SessionId));

                if (!currentSessionIds.SetEquals(viewModelIds))
                {
                    ReloadSessions(_npsManager);
                    return;
                }

                foreach (var localSession in localSessions)
                {
                    try
                    {
                        var existingVm = _playerViewModel.MediaSessions
                            .FirstOrDefault(s => s.SessionId == localSession.SourceAppId && !s.IsRemoteSession);
                        if (existingVm == null) continue;

                        var freshSource = localSession.ActivateMediaPlaybackDataSource();
                        var playbackInfo = freshSource.GetMediaPlaybackInfo();
                        var mediaInfo = freshSource.GetMediaObjectInfo();

                        bool isPlaying = (playbackInfo.PropsValid.HasFlag(MediaPlaybackProps.State)
                            ? playbackInfo.PlaybackState
                            : MediaPlaybackState.Unknown) == MediaPlaybackState.Playing;

                        if (existingVm.IsPlaying != isPlaying)
                            existingVm.IsPlaying = isPlaying;

                        string title = mediaInfo.Title ?? "";
                        string artist = mediaInfo.Artist ?? "";
                        if (string.IsNullOrWhiteSpace(artist) && !string.IsNullOrWhiteSpace(mediaInfo.AlbumArtist))
                        {
                            artist = mediaInfo.AlbumArtist;
                        }

                        if (TryGetTimelineFromSource(freshSource, playbackInfo, out var position, out var duration))
                        {
                            if (duration > TimeSpan.Zero)
                            {
                                existingVm.DurationSeconds = (int)Math.Round(duration.TotalSeconds);
                            }

                            if (existingVm.LyricLines != null && existingVm.LyricLines.Count > 0)
                            {
                                UpdateLyricLineForSession(existingVm, position);
                            }
                        }

                        if (existingVm.OriginalTitle != title || existingVm.Artist != artist)
                        {
                            existingVm.OriginalTitle = title;
                            if (existingVm.LyricLines == null || existingVm.LyricLines.Count == 0)
                            {
                                existingVm.Title = title;
                            }
                            existingVm.Artist = artist;
                            existingVm.Album = mediaInfo.AlbumTitle ?? "";

                            EnsureLyricsForSessionAsync(existingVm, title, artist, existingVm.DurationSeconds);

                            var thumbnailStream = freshSource.GetThumbnailStream();
                            if (thumbnailStream != null)
                            {
                                var _ = existingVm.UpdateThumbnail(thumbnailStream);
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void RefreshSessionManager()
        {
            try
            {
                if (_npsManager != null)
                {
                    _npsManager.SessionListChanged -= NPSManager_SessionsChanged;
                }

                _npsManager = new NowPlayingSessionManager();
                _npsManager.SessionListChanged += NPSManager_SessionsChanged;
                ReloadSessions(_npsManager);
            }
            catch (Exception ex)
            {
                Timber.Log(LoggerLevel.Error, ex, "Error refreshing session manager");
            }
        }

        private async void LyricsSettings_SettingsChanged(object sender, EventArgs e)
        {
            if (_dispatcher == null)
            {
                return;
            }

            await _dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                foreach (var session in _playerViewModel.MediaSessions.Where(s => !s.IsRemoteSession))
                {
                    if (!LyricsSettings.OnlineLyricsEnabled)
                    {
                        session.LyricLines = null;
                        session.CurrentLyricLine = string.Empty;
                        session.Title = session.OriginalTitle;
                        session.LyricsKey = string.Empty;
                        session.LyricsRequestId = 0;
                        continue;
                    }

                    EnsureLyricsForSessionAsync(session, session.OriginalTitle, session.Artist, session.DurationSeconds);
                }
            });
        }

        private void EnsureLyricsForSessionAsync(MediaSessionViewModel session, string title, string artist, int durationSeconds)
        {
            if (session == null || session.IsRemoteSession)
            {
                return;
            }

            if (!LyricsSettings.OnlineLyricsEnabled)
            {
                session.OriginalTitle = title ?? string.Empty;
                session.Title = session.OriginalTitle;
                session.LyricLines = null;
                session.CurrentLyricLine = string.Empty;
                session.LyricsKey = string.Empty;
                session.LyricsRequestId = 0;
                return;
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                session.OriginalTitle = title ?? string.Empty;
                session.Title = session.OriginalTitle;
                session.LyricLines = null;
                session.CurrentLyricLine = string.Empty;
                session.LyricsKey = string.Empty;
                session.LyricsRequestId = 0;
                return;
            }

            var key = LyricsService.BuildCacheKey(title, artist, durationSeconds);
            if (string.Equals(session.LyricsKey, key, StringComparison.Ordinal) && (session.LyricLines != null || session.LyricsRequestId > 0))
            {
                return;
            }

            session.OriginalTitle = title ?? string.Empty;
            session.LyricsKey = key;
            session.LyricLines = null;
            session.CurrentLyricLine = string.Empty;
            session.Title = session.OriginalTitle;

            var requestId = ++session.LyricsRequestId;
            _ = FetchLyricsForSessionAsync(session, title, artist, durationSeconds, key, requestId);
        }

        private async Task FetchLyricsForSessionAsync(MediaSessionViewModel session, string title, string artist, int durationSeconds, string key, int requestId)
        {
            var source = LyricsSettings.LyricsSourcePriority;
            var lines = await LyricsService.FetchLyricsAsync(title, artist, durationSeconds, source, true).ConfigureAwait(false);
            await _dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (session == null || session.LyricsRequestId != requestId || !string.Equals(session.LyricsKey, key, StringComparison.Ordinal))
                {
                    return;
                }

                session.LyricLines = lines ?? Array.Empty<LyricLine>();
                if (session.LyricLines.Count == 0)
                {
                    session.CurrentLyricLine = string.Empty;
                    session.Title = session.OriginalTitle;
                }
            });
        }

        private void UpdateLyricLineForSession(MediaSessionViewModel session, TimeSpan position)
        {
            if (session == null || session.IsRemoteSession)
            {
                return;
            }

            if (!LyricsSettings.OnlineLyricsEnabled)
            {
                session.CurrentLyricLine = string.Empty;
                session.Title = session.OriginalTitle;
                return;
            }

            if (session.LyricLines == null || session.LyricLines.Count == 0)
            {
                session.CurrentLyricLine = string.Empty;
                session.Title = session.OriginalTitle;
                return;
            }

            var line = LyricsService.GetCurrentLine(session.LyricLines, (long)position.TotalMilliseconds, LyricsSettings.LyricsDelayMs);
            if (line == null)
            {
                session.CurrentLyricLine = string.Empty;
                session.Title = session.OriginalTitle;
                return;
            }

            session.CurrentLyricLine = line;
            session.Title = line;
        }

        private static bool TryGetTimelineFromSource(MediaPlaybackDataSource source, MediaPlaybackInfo playbackInfo, out TimeSpan position, out TimeSpan duration)
        {
            position = TimeSpan.Zero;
            duration = TimeSpan.Zero;

            if (source != null)
            {
                var sourceType = source.GetType();
                var method = sourceType.GetMethod("GetMediaTimelineProperties") ?? sourceType.GetMethod("GetTimelineProperties");
                if (method != null && method.GetParameters().Length == 0)
                {
                    var timeline = method.Invoke(source, null);
                    if (timeline != null && TryReadTimelineValues(timeline, out position, out duration))
                    {
                        return true;
                    }
                }
            }

            if (TryReadTimelineValues(playbackInfo, out position, out duration))
            {
                return true;
            }

            return position > TimeSpan.Zero || duration > TimeSpan.Zero;
        }

        private static bool TryReadTimelineValues(object timelineObject, out TimeSpan position, out TimeSpan duration)
        {
            position = TimeSpan.Zero;
            duration = TimeSpan.Zero;

            if (timelineObject == null)
            {
                return false;
            }

            var type = timelineObject.GetType();
            var hasPosition = TryGetTimeSpanValue(type, timelineObject, new[] { "PlaybackPosition", "Position", "CurrentPosition", "PlaybackPositionMs", "PositionMs", "PlaybackPositionTicks", "PositionTicks" }, out position);
            var hasDuration = TryGetTimeSpanValue(type, timelineObject, new[] { "PlaybackDuration", "Duration", "EndTime", "PlaybackLength", "TotalDuration", "PlaybackDurationMs", "DurationMs", "PlaybackDurationTicks", "DurationTicks" }, out duration);
            return hasPosition || hasDuration;
        }

        private static bool TryGetTimeSpanValue(Type type, object instance, string[] names, out TimeSpan value)
        {
            foreach (var name in names)
            {
                var prop = type.GetProperty(name);
                if (prop == null)
                {
                    continue;
                }

                var raw = prop.GetValue(instance);
                if (TryConvertToTimeSpan(raw, out value))
                {
                    return true;
                }
            }

            value = TimeSpan.Zero;
            return false;
        }

        private static bool TryConvertToTimeSpan(object raw, out TimeSpan value)
        {
            if (raw == null)
            {
                value = TimeSpan.Zero;
                return false;
            }

            if (raw is TimeSpan timeSpan)
            {
                value = timeSpan;
                return true;
            }

            if (raw is long longValue)
            {
                value = ConvertNumberToTimeSpan(longValue);
                return true;
            }

            if (raw is int intValue)
            {
                value = ConvertNumberToTimeSpan(intValue);
                return true;
            }

            if (raw is double doubleValue)
            {
                value = ConvertNumberToTimeSpan(doubleValue);
                return true;
            }

            if (raw is ulong ulongValue)
            {
                value = ConvertNumberToTimeSpan(ulongValue);
                return true;
            }

            value = TimeSpan.Zero;
            return false;
        }

        private static TimeSpan ConvertNumberToTimeSpan(double value)
        {
            if (value <= 0)
            {
                return TimeSpan.Zero;
            }

            if (value > TimeSpan.FromDays(1).TotalMilliseconds)
            {
                var ticks = (long)Math.Round(value);
                return TimeSpan.FromTicks(ticks);
            }

            return TimeSpan.FromMilliseconds(value);
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
                    // 对于本地会话，我们需要根据实际状态发送正确的命令
                    // 首先获取本地会话的当前状态
                    var sessionViewModel = _playerViewModel.MediaSessions.FirstOrDefault(s => s.SessionId == deviceId);
                    if (sessionViewModel != null)
                    {
                        // 根据当前播放状态发送相反的命令
                        var playbackCommand = sessionViewModel.IsPlaying 
                            ? MediaPlaybackCommands.Pause 
                            : MediaPlaybackCommands.Play;
                        ControlLocalSession(deviceId, playbackCommand);
                        
                        // 立即更新会话状态，以确保 UI 反映正确的状态
                        sessionViewModel.IsPlaying = !sessionViewModel.IsPlaying;
                    }
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