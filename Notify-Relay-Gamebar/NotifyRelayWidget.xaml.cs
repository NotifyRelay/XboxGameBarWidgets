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
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace NotifyRelayGamebar
{
    public sealed partial class NotifyRelayWidget : Page
    {
        private XboxGameBarWidget widget;
        private PlayerViewModel PlayerViewModel { get; set; }
        private NotificationViewModel NotificationViewModel { get; set; }
        
        // 模块实例
        private ThemeManager _themeManager;
        private ExampleNotificationManager _exampleNotificationManager;
        private NotificationManager _notificationManager;
        private MediaPlaybackManager _mediaPlaybackManager;

        public NotifyRelayWidget()
        {
            this.InitializeComponent();

            PlayerViewModel = new PlayerViewModel();
            NotificationViewModel = new NotificationViewModel();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            // 添加日志来调试
            Timber.Log(LoggerLevel.Info, "OnNavigatedTo called, e.Parameter type: {0}", e.Parameter != null ? e.Parameter.GetType().FullName : "Null");
            
            widget = e.Parameter as XboxGameBarWidget;

            // 添加日志来调试
            Timber.Log(LoggerLevel.Info, "widget after casting: {0}", widget != null ? "NotNull" : "Null");

            // 初始化模块（无论widget是否为null都需要初始化）
            InitializeModules();
            
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
                // 订阅GameBar显示模式变化事件
                widget.GameBarDisplayModeChanged += Widget_GameBarDisplayModeChanged;
                
                // 设置初始背景透明度和颜色
                _themeManager.SetBackgroundOpacity();
                _themeManager.SetBackgroundColor();
                
                // 调试日志：初始固定状态
                Timber.Log(LoggerLevel.Info, "Initial widget state: Pinned = {0}, RequestedTheme = {1}", widget.Pinned, widget.RequestedTheme);
            }
            else
            {
                Timber.Log(LoggerLevel.Info, "widget is null in OnNavigatedTo, running in normal window mode");
            }
            
            // 启动服务（无论widget是否为null都需要启动服务）
            _mediaPlaybackManager.StartService();
            _notificationManager.StartNotificationService();
        }

        /// <summary>
        /// 初始化各个模块
        /// </summary>
        private void InitializeModules()
        {
            // 初始化主题管理器
            _themeManager = new ThemeManager(
                widget,
                PlayerWidgetView,
                PlaybackControlsPanel,
                ToastStack,
                BackgroundGrid);
            
            // 初始化通知管理器
            _notificationManager = new NotificationManager(NotificationViewModel, ToastStack);
            _notificationManager.Initialize();
            
            // 初始化示例通知管理器
            _exampleNotificationManager = new ExampleNotificationManager(
                widget,
                ToastStack,
                () => _mediaPlaybackManager?.MediaSessions);
            
            // 初始化媒体播放管理器
            _mediaPlaybackManager = new MediaPlaybackManager(
                widget,
                PlayerWidgetView,
                PlaybackControlsPanel,
                Dispatcher,
                PlayerViewModel,
                NotificationViewModel,
                () => _exampleNotificationManager.UpdateExampleNotifications(),
                () => _themeManager.IsInPinnedAndClosedState());
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            
            // 取消订阅事件
            if (widget != null)
            {
                widget.PinnedChanged -= Widget_PinnedChanged;
                widget.RequestedThemeChanged -= Widget_RequestedThemeChanged;
                widget.GameBarDisplayModeChanged -= Widget_GameBarDisplayModeChanged;
            }
            
            // 停止服务
            if (_mediaPlaybackManager != null && _notificationManager != null)
            {
                _mediaPlaybackManager.StopService();
                _notificationManager.StopNotificationService();
            }
        }

        /// <summary>
        /// 处理小部件固定状态变化事件
        /// </summary>
        private void Widget_PinnedChanged(object sender, object e)
        {
            // 添加日志来调试事件是否触发
            Timber.Log(LoggerLevel.Info, "Widget_PinnedChanged event triggered, Pinned: {0}", widget?.Pinned.ToString());
            
            // 直接在UI线程上更新
            var _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                _themeManager.SetBackgroundOpacity();
                _mediaPlaybackManager.UpdateMediaVisibility();
                _exampleNotificationManager.UpdateExampleNotifications();
            });
        }

        /// <summary>
        /// 处理小部件主题变化事件
        /// </summary>
        private async void Widget_RequestedThemeChanged(object sender, object e)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                _themeManager.SetBackgroundColor();
            });
        }

        /// <summary>
        /// 处理GameBar显示模式变化事件
        /// </summary>
        private async void Widget_GameBarDisplayModeChanged(object sender, object e)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                Timber.Log(LoggerLevel.Info, "GameBarDisplayModeChanged event triggered, new mode: {0}", widget.GameBarDisplayMode);
                _themeManager.SetBackgroundOpacity();
                _mediaPlaybackManager.UpdateMediaVisibility();
                _exampleNotificationManager.UpdateExampleNotifications();
            });
        }

        // 媒体控制按钮事件处理
        private void PreviousButton_Click(object sender, RoutedEventArgs e)
        {
            _mediaPlaybackManager.PreviousButton_Click(sender, e);
        }

        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            _mediaPlaybackManager.PlayPauseButton_Click(sender, e);
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            _mediaPlaybackManager.NextButton_Click(sender, e);
        }
    }
}