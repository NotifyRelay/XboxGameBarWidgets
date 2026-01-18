using Microsoft.Gaming.XboxGameBar;
using NPSMLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TimberLog;
using Windows.ApplicationModel.Core;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace NotifyRelayGamebar
{
    public sealed partial class PlayerWidget : Page
    {
        private XboxGameBarWidget widget;
        private PlayerViewModel PlayerViewModel { get; set; }
        private NotificationViewModel NotificationViewModel { get; set; }
        private NotificationService NotificationService { get; set; }

        private NowPlayingSessionManager NPSManager;
        private IList<NowPlayingSession> MediaSessions;

        private NowPlayingSession MediaSession;
        private MediaPlaybackDataSource MediaPlaybackSource;
        private int SessionIndex = 0;
        
        // 主题画笔
        private Windows.UI.Xaml.Media.SolidColorBrush widgetDarkThemeBrush;
        private Windows.UI.Xaml.Media.SolidColorBrush widgetLightThemeBrush;
        // 保存原始背景画笔以便恢复
        private Windows.UI.Xaml.Media.Brush originalPlayerBackgroundBrush;
        private Windows.UI.Xaml.Media.Brush originalToastBackgroundBrush;

        public PlayerWidget()
        {
            this.InitializeComponent();

            PlayerViewModel = new PlayerViewModel();

            // 初始化主题画笔
            widgetDarkThemeBrush = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 38, 38, 38));
            widgetLightThemeBrush = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 219, 219, 219));

            NotificationViewModel = new NotificationViewModel();
            NotificationService = new NotificationService(NotificationViewModel);
            NotificationService.ErrorOccurred += NotificationService_ErrorOccurred;

            // 保存控件的原始背景画笔（InitializeComponent 后可用）
            try
            {
                originalPlayerBackgroundBrush = PlayerWidgetView?.Background;
            }
            catch { originalPlayerBackgroundBrush = null; }

            try
            {
                originalToastBackgroundBrush = ToastStack?.Background;
            }
            catch { originalToastBackgroundBrush = null; }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            // 添加日志来调试
            Timber.Log(LoggerLevel.Info, "OnNavigatedTo called, e.Parameter type: {0}", e.Parameter != null ? e.Parameter.GetType().FullName : "Null");
            
            widget = e.Parameter as XboxGameBarWidget;

            // 添加日志来调试
            Timber.Log(LoggerLevel.Info, "widget after casting: {0}", widget != null ? "NotNull" : "Null");

            // 将小部件宽度锁定为通知浮窗的宽度
            if (widget != null)
            {
                try
                {
                    float overlayWidth = 320.0f;
                    try
                    {
                        // 尝试获取ToastStack的宽度
                        overlayWidth = (float)ToastStack.Width;
                    }
                    catch { }

                    // 设置小部件的最小和最大宽度
                    Windows.Foundation.Size minSize = new Windows.Foundation.Size();
                    minSize.Height = 0.0f;
                    minSize.Width = overlayWidth;

                    Windows.Foundation.Size maxSize = widget.MaxWindowSize;
                    maxSize.Width = overlayWidth;

                    widget.MinWindowSize = minSize;
                    widget.MaxWindowSize = maxSize;
                }
                catch
                {
                    // 忽略在不支持或失败情况下的错误
                }

                // 订阅固定状态变化事件
                Timber.Log(LoggerLevel.Info, "Subscribing to PinnedChanged event");
                widget.PinnedChanged += Widget_PinnedChanged;
                // 订阅主题变化事件
                widget.RequestedThemeChanged += Widget_RequestedThemeChanged;
                // 设置初始背景透明度和颜色
                SetBackgroundOpacity();
                SetBackgroundColor();
                
                // 调试日志：初始固定状态
                Timber.Log(LoggerLevel.Info, "Initial widget state: Pinned = {0}, RequestedTheme = {1}", widget.Pinned, widget.RequestedTheme);
            }
            else
            {
                Timber.Log(LoggerLevel.Error, "widget is null in OnNavigatedTo");
            }

            // 将ToastStack传递给NotificationViewModel
            NotificationViewModel.ToastStack = ToastStack;
            
            StartService();
            StartNotificationService();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            
            // 取消订阅事件
            if (widget != null)
            {
                widget.PinnedChanged -= Widget_PinnedChanged;
                widget.RequestedThemeChanged -= Widget_RequestedThemeChanged;
            }
            
            StopService();
            StopNotificationService();
        }

        /// <summary>
        /// 处理小部件固定状态变化事件
        /// </summary>
        private void Widget_PinnedChanged(object sender, object e)
        {
            // 添加日志来调试事件是否触发
            Timber.Log(LoggerLevel.Info, "Widget_PinnedChanged event triggered, Pinned: {0}", widget?.Pinned.ToString());
            
            // 直接在UI线程上更新
            Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                SetBackgroundOpacity();
            });
        }

        /// <summary>
        /// 设置背景透明度和媒体控制按钮可见性
        /// </summary>
        private void SetBackgroundOpacity()
        {
            // 调试日志
            Timber.Log(LoggerLevel.Info, "SetBackgroundOpacity called, widget: {0}, PlayerWidgetView: {1}", widget != null ? "NotNull" : "Null", PlayerWidgetView != null ? "NotNull" : "Null");

            // 当小部件被固定时，强制完全透明并调整媒体/通知背景画笔
            try
            {
                if (widget != null)
                {
                    bool isPinned = widget.Pinned;
                    Timber.Log(LoggerLevel.Info, "Widget Pinned: {0}", isPinned);

                    if (isPinned)
                    {
                        Timber.Log(LoggerLevel.Info, "Setting fixed state: Background opacity 0.0");

                        try
                        {
                            PlaybackControlsPanel.Visibility = Visibility.Collapsed;
                        }
                        catch (Exception ex)
                        {
                            Timber.Log(LoggerLevel.Error, ex, "Error setting controls visibility when pinned");
                        }

                        // 在固定时，仅更改背景画笔（不影响文本和边框）；如果没有会话，隐藏整个媒体块
                        bool hasSessionsPinned = (MediaSessions?.Count ?? 0) > 0;
                        PlayerWidgetView.Visibility = hasSessionsPinned ? Visibility.Visible : Visibility.Collapsed;

                        // 在固定时，保持PlayerWidgetView背景透明，不设置背景
                        if (PlayerWidgetView != null)
                        {
                            PlayerWidgetView.Background = new SolidColorBrush(Windows.UI.Colors.Transparent);
                        }

                        // ToastStack 始终保持透明，不设置背景
                        if (ToastStack != null)
                        {
                            ToastStack.Background = new SolidColorBrush(Windows.UI.Colors.Transparent);
                        }

                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                // 如果访问 Pinned 属性失败，记录错误并继续使用回退逻辑
                Timber.Log(LoggerLevel.Error, ex, "Error accessing widget.Pinned property");
            }

            // 未固定时，根据是否有媒体会话显示媒体控件
            Timber.Log(LoggerLevel.Info, "Setting non-fixed state");

            try
            {
                bool hasSessions = (MediaSessions?.Count ?? 0) > 0;
                var visibility = hasSessions ? Visibility.Visible : Visibility.Collapsed;
                PlaybackControlsPanel.Visibility = visibility;

                // 未固定时，保持PlayerWidgetView背景透明，不设置背景
                if (PlayerWidgetView != null)
                {
                    PlayerWidgetView.Background = new SolidColorBrush(Windows.UI.Colors.Transparent);
                }

                // ToastStack 始终保持透明，不恢复背景
                if (ToastStack != null)
                {
                    ToastStack.Background = new SolidColorBrush(Windows.UI.Colors.Transparent);
                }
            }
            catch (Exception ex)
            {
                Timber.Log(LoggerLevel.Error, ex, "Error setting controls visibility in non-fixed state");
            }
        }

        private Windows.UI.Xaml.Media.Brush CreateSemiTransparentBrush(Windows.UI.Xaml.Media.Brush source, byte alpha)
        {
            try
            {
                if (source is Windows.UI.Xaml.Media.SolidColorBrush scb)
                {
                    var c = scb.Color;
                    c.A = alpha;
                    return new Windows.UI.Xaml.Media.SolidColorBrush(c);
                }

                // 回退到应用主题背景色（如果可用）并调整透明度
                if (Application.Current?.Resources != null && Application.Current.Resources.ContainsKey("ApplicationPageBackgroundThemeBrush"))
                {
                    var def = Application.Current.Resources["ApplicationPageBackgroundThemeBrush"] as Windows.UI.Xaml.Media.SolidColorBrush;
                    if (def != null)
                    {
                        var c = def.Color;
                        c.A = alpha;
                        return new Windows.UI.Xaml.Media.SolidColorBrush(c);
                    }
                }

                return source;
            }
            catch
            {
                return source;
            }
        }

        /// <summary>
        /// 处理小部件主题变化事件
        /// </summary>
        private async void Widget_RequestedThemeChanged(object sender, object e)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                SetBackgroundColor();
            });
        }

        /// <summary>
        /// 设置背景颜色
        /// </summary>
        private void SetBackgroundColor()
        {
            if (widget == null)
                return;

            var requestedTheme = widget.RequestedTheme;
            
            // 设置页面主题
            RequestedTheme = requestedTheme;
            
            // 保持BackgroundGrid透明，避免显示方形背景
            BackgroundGrid.Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.Transparent);
        }

        private void StartService()
        {
            if (NPSManager != null)
            {
                StopService();
            }

            try
            {
                NPSManager = new NowPlayingSessionManager();
                NPSManager.SessionListChanged += NPSManager_SessionsChanged;
                ReloadSessions(NPSManager);
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
                ReloadSessions(sender as NowPlayingSessionManager ?? NPSManager);
            }
        }

        private async void ReloadSessions(NowPlayingSessionManager sessionManager)
        {
            MediaSessions = sessionManager?.GetSessions();
            SessionIndex = FindIndexOfCurrentSession(MediaSession ?? sessionManager.CurrentSession);

            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                var mediaSessionsCount = (MediaSessions?.Count ?? 1);

                if (mediaSessionsCount > 1)
                {
                    PlayerViewModel.ShowNextSession = SessionIndex + 1 < mediaSessionsCount;
                    PlayerViewModel.ShowPreviousSession = SessionIndex - 1 >= 0;
                }
                else
                {
                    PlayerViewModel.ShowNextSession = false;
                    PlayerViewModel.ShowPreviousSession = false;
                }

                PlayerViewModel.SessionsAvailable = (MediaSessions?.Count ?? 0) > 0;
                NotificationViewModel.SetMediaSessionStatus((MediaSessions?.Count ?? 0) > 0);
                
                // 更新媒体控制按钮可见性，但要考虑小部件固定状态
                bool hasSessions = (MediaSessions?.Count ?? 0) > 0;
                try
                {
                    if (widget != null && widget.Pinned)
                    {
                        // 固定状态下隐藏媒体控制按钮；如果没有会话，则隐藏整个媒体块
                        PlaybackControlsPanel.Visibility = Visibility.Collapsed;
                        bool hasSessionsPinned = (MediaSessions?.Count ?? 0) > 0;
                        PlayerWidgetView.Visibility = hasSessionsPinned ? Visibility.Visible : Visibility.Collapsed;
                    }
                    else
                    {
                        // 非固定状态下根据媒体会话数量显示/隐藏
                        var visibility = hasSessions ? Visibility.Visible : Visibility.Collapsed;
                        PlaybackControlsPanel.Visibility = visibility;
                        PlayerWidgetView.Visibility = visibility;
                    }
                }
                catch
                {
                    // 如果访问 Pinned 属性失败，根据媒体会话数量显示/隐藏
                    var visibility = hasSessions ? Visibility.Visible : Visibility.Collapsed;
                    PlaybackControlsPanel.Visibility = visibility;
                    PlayerWidgetView.Visibility = visibility;
                }
            });

            await LoadSession();
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

            MediaSession = MediaSessions.ElementAtOrDefault(SessionIndex);

            if (MediaSession != null)
            {
                MediaPlaybackSource = MediaSession.ActivateMediaPlaybackDataSource();
                MediaPlaybackSource.MediaPlaybackDataChanged += MediaPlaybackSource_MediaPlaybackDataChanged;

                await UpdatePlayer(MediaPlaybackSource);
            }
        }

        private void StopService()
        {
            // Unregister events
            if (NPSManager != null)
            {
                try
                {
                    NPSManager.SessionListChanged -= NPSManager_SessionsChanged;
                }
                catch (Exception ex)
                {
                    Timber.Log(LoggerLevel.Error, ex);
                }
            }

            NPSManager = null;
            MediaSessions = null;
            NotificationViewModel.SetMediaSessionStatus(false);
        }

        private void UnloadSession()
        {
            if (MediaPlaybackSource != null)
            {
                try
                {
                    MediaPlaybackSource.MediaPlaybackDataChanged -= MediaPlaybackSource_MediaPlaybackDataChanged;
                }
                catch (Exception ex)
                {
                    Timber.Log(LoggerLevel.Error, ex);
                }
            }
            MediaPlaybackSource = null;
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

            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
            {
                PlayerViewModel.Title = mediaObjectInfo.Title;
                PlayerViewModel.Artist = mediaObjectInfo.Artist;
                PlayerViewModel.Album = mediaObjectInfo.AlbumTitle;

                if (string.IsNullOrWhiteSpace(PlayerViewModel.Artist) && !string.IsNullOrWhiteSpace(mediaObjectInfo.AlbumArtist))
                {
                    PlayerViewModel.Artist = mediaObjectInfo.AlbumArtist;
                }

                await PlayerViewModel.UpdateThumbnail(thumbnailStream);
            });
        }

        private async Task UpdatePlaybackInfo(MediaPlaybackDataSource source)
        {
            var playbackInfo = source.GetMediaPlaybackInfo();

            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                var playerCapabilities = playbackInfo.PlaybackCaps;
                var playerValidProps = playbackInfo.PropsValid;

                PlayerViewModel.IsShuffleActive = playerValidProps.HasFlag(MediaPlaybackProps.ShuffleEnabled) ? playbackInfo.ShuffleEnabled : false;
                PlayerViewModel.AutoRepeatMode = playerValidProps.HasFlag(MediaPlaybackProps.AutoRepeatMode) ? playbackInfo.RepeatMode : MediaPlaybackRepeatMode.Unknown;
                PlayerViewModel.IsPlaying = (playerValidProps.HasFlag(MediaPlaybackProps.State) ? playbackInfo.PlaybackState : MediaPlaybackState.Unknown) switch
                {
                    MediaPlaybackState.Playing => true,
                    _ => false,
                };

                PlayerViewModel.IsShuffleEnabled = playerCapabilities.HasFlag(MediaPlaybackCapabilities.Shuffle);
                PlayerViewModel.IsRepeatEnabled = playerCapabilities.HasFlag(MediaPlaybackCapabilities.Repeat);
                PlayerViewModel.IsPreviousEnabled = playerCapabilities.HasFlag(MediaPlaybackCapabilities.Previous);
                PlayerViewModel.IsNextEnabled = playerCapabilities.HasFlag(MediaPlaybackCapabilities.Next);
                PlayerViewModel.IsPlayPauseEnabled = playerCapabilities.HasFlag(MediaPlaybackCapabilities.PlayPauseToggle);
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

        // 会话切换按钮已移除，相关事件处理方法已删除

        private void PreviousButton_Click(object sender, RoutedEventArgs e)
        {
            MediaPlaybackSource?.SendMediaPlaybackCommand(MediaPlaybackCommands.Previous);
        }

        // Shuffle/Repeat 按钮已移除，相关事件处理方法已删除

        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (PlayerViewModel.IsPlaying)
            {
                MediaPlaybackSource?.SendMediaPlaybackCommand(MediaPlaybackCommands.Pause);
            }
            else
            {
                MediaPlaybackSource?.SendMediaPlaybackCommand(MediaPlaybackCommands.Play);
            }
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            MediaPlaybackSource?.SendMediaPlaybackCommand(MediaPlaybackCommands.Next);
        }

        

        #region Notification Service

        private async void StartNotificationService()
        {
            try
            {
                await NotificationService.StartAsync();
                Timber.Log(LoggerLevel.Info, "Notification service started");
            }
            catch (Exception ex)
            {
                Timber.Log(LoggerLevel.Error, ex, "Failed to start notification service");
            }
        }

        private void StopNotificationService()
        {
            try
            {
                NotificationService.Stop();
                Timber.Log(LoggerLevel.Info, "Notification service stopped");
            }
            catch (Exception ex)
            {
                Timber.Log(LoggerLevel.Error, ex, "Failed to stop notification service");
            }
        }

        private void NotificationService_ErrorOccurred(object sender, string e)
        {
            Timber.Log(LoggerLevel.Error, "Notification service error: {0}", e);
        }

        #endregion
    }
}