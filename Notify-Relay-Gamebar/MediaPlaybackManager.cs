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

        private List<object> _allSessions = new List<object>();
        private object _currentSession; // Can be RemoteMediaSession
        private List<RemoteMediaSession> _remoteSessions = new List<RemoteMediaSession>();
        
        private int _sessionIndex = 0;

        private DispatcherTimer _pollTimer;
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
        }

        public void StartService()
        {
            // 始终创建计时器
            try
            {
                _pollTimer = new DispatcherTimer();
                _pollTimer.Interval = TimeSpan.FromMilliseconds(300);
                _pollTimer.Tick += PollTimer_Tick;
                _pollTimer.Start();
                _lastFullSessionRefresh = DateTime.Now;
            }
            catch (Exception ex)
            {
                Timber.Log(LoggerLevel.Error, ex, "Failed to create poll timer");
            }
        }

        private async void ReloadSessions()
        {
            _allSessions.Clear();
            
            // 只添加远程会话
            foreach (var remoteSession in _remoteSessions)
            {
                _allSessions.Add(remoteSession);
            }

            _sessionIndex = FindIndexOfCurrentSession(_currentSession);
            
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
                // 获取远程会话 ID 集合
                var remoteSessionIds = _remoteSessions.Select(s => s.DeviceId).ToList();

                // 移除不再存在的会话
                var sessionsToRemove = _playerViewModel.MediaSessions.Where(s => !remoteSessionIds.Contains(s.SessionId)).ToList();
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
                if (session is RemoteMediaSession rms && currentSession is RemoteMediaSession currentRms)
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

            if (_currentSession is RemoteMediaSession rms)
            {
                await UpdatePlayer(rms);
            }
        }

        public void StopService()
        {
            _pollTimer?.Stop();
            _pollTimer = null;

            _allSessions.Clear();
            _remoteSessions.Clear();
            _currentSession = null;
            var _ = _notificationViewModel.SetMediaSessionStatus(false);
        }

        private void UnloadSession()
        {
            // Session cleanup is handled in LoadSession
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
            ReloadSessions();
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

        private void PollTimer_Tick(object sender, object e)
        {
            // 定时刷新远程会话的脉冲状态
            if (_lastFullSessionRefresh == DateTime.MinValue) return;
            if ((DateTime.Now - _lastFullSessionRefresh).TotalSeconds > 3)
            {
                UpdateMediaSessionsViewModel();
                _lastFullSessionRefresh = DateTime.Now;
            }
        }

        // 媒体控制按钮事件处理
        public void PreviousButton_Click(object sender, RoutedEventArgs e, string deviceId = "")
        {
            if (!string.IsNullOrEmpty(deviceId))
            {
                NotificationService?.SendMediaControlCommandAsync(deviceId, "previous");
            }
            else if (_currentSession is RemoteMediaSession rms)
            {
                NotificationService?.SendMediaControlCommandAsync(rms.DeviceId, "previous");
            }
        }

        public void PlayPauseButton_Click(object sender, RoutedEventArgs e, string deviceId = "")
        {
            if (!string.IsNullOrEmpty(deviceId))
            {
                NotificationService?.SendMediaControlCommandAsync(deviceId, "playPause");
            }
            else if (_currentSession is RemoteMediaSession rms)
            {
                NotificationService?.SendMediaControlCommandAsync(rms.DeviceId, "playPause");
            }
        }

        public void NextButton_Click(object sender, RoutedEventArgs e, string deviceId = "")
        {
            if (!string.IsNullOrEmpty(deviceId))
            {
                NotificationService?.SendMediaControlCommandAsync(deviceId, "next");
            }
            else if (_currentSession is RemoteMediaSession rms)
            {
                NotificationService?.SendMediaControlCommandAsync(rms.DeviceId, "next");
            }
        }
    }
}
