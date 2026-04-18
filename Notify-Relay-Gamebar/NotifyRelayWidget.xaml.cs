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
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace NotifyRelayGamebar
{
    public sealed partial class NotifyRelayWidget : Page
    {
        private sealed class SpectrumBindingToken
        {
            public Models.MediaSessionViewModel ViewModel { get; }
            public long Token { get; }

            public SpectrumBindingToken(Models.MediaSessionViewModel viewModel, long token)
            {
                ViewModel = viewModel;
                Token = token;
            }
        }

        private XboxGameBarWidget widget;
        private PlayerViewModel PlayerViewModel { get; set; }
        private NotificationViewModel NotificationViewModel { get; set; }
        
        // 模块实例
        private ThemeManager _themeManager;
        private ExampleNotificationManager _exampleNotificationManager;
        private NotificationManager _notificationManager;
        private MediaPlaybackManager _mediaPlaybackManager;
        
        // 记录展开状态的字典
        private Dictionary<string, bool> _expandedStates = new Dictionary<string, bool>();

        public NotifyRelayWidget()
        {
            this.InitializeComponent();

            PlayerViewModel = new PlayerViewModel();
            NotificationViewModel = new NotificationViewModel();
        }

        private void PlayIndicator_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
        {
            UnregisterPlayIndicatorCallback(sender);

            if (sender.DataContext is Models.MediaSessionViewModel viewModel)
            {
                var token = viewModel.RegisterPropertyChangedCallback(
                    Models.MediaSessionViewModel.IsPlayingProperty,
                    (d, p) =>
                    {
                        var _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                        {
                            UpdateSpectrumState(sender, viewModel.IsPlaying);
                        });
                    });

                sender.Tag = new SpectrumBindingToken(viewModel, token);
                UpdateSpectrumState(sender, viewModel.IsPlaying);
            }
            else
            {
                StopSpectrum(sender);
            }
        }

        private void PlayIndicator_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                if (element.DataContext is Models.MediaSessionViewModel viewModel)
                {
                    UpdateSpectrumState(element, viewModel.IsPlaying);
                }
                else
                {
                    StopSpectrum(element);
                }
            }
        }

        private void PlayIndicator_Unloaded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                StopSpectrum(element);
                UnregisterPlayIndicatorCallback(element);
            }
        }

        private static void UpdateSpectrumState(FrameworkElement element, bool isPlaying)
        {
            if (isPlaying)
            {
                StartSpectrum(element);
            }
            else
            {
                StopSpectrum(element);
            }
        }

        private static void StartSpectrum(FrameworkElement element)
        {
            if (element.Resources["SpectrumStoryboard"] is Storyboard storyboard)
            {
                storyboard.Begin();
            }
        }

        private static void StopSpectrum(FrameworkElement element)
        {
            if (element.Resources["SpectrumStoryboard"] is Storyboard storyboard)
            {
                storyboard.Stop();
            }
        }

        private static void UnregisterPlayIndicatorCallback(FrameworkElement element)
        {
            if (element.Tag is SpectrumBindingToken token)
            {
                token.ViewModel.UnregisterPropertyChangedCallback(
                    Models.MediaSessionViewModel.IsPlayingProperty,
                    token.Token);
                element.Tag = null;
            }
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
                null, // PlaybackControlsPanel 已移除，现在每个媒体块都有自己的控制按钮
                ToastStack,
                BackgroundGrid);
            
            // 初始化通知管理器
            _notificationManager = new NotificationManager(NotificationViewModel, ToastStack);
            _notificationManager.Initialize();
            
            // 初始化示例通知管理器
            _exampleNotificationManager = new ExampleNotificationManager(
                widget,
                ToastStack,
                () => _mediaPlaybackManager?.GetAllSessions());
            
            // 初始化媒体播放管理器
            _mediaPlaybackManager = new MediaPlaybackManager(
                widget,
                PlayerWidgetView,
                null, // PlaybackControlsPanel 已移除，现在每个媒体块都有自己的控制按钮
                Dispatcher,
                PlayerViewModel,
                NotificationViewModel,
                () => _exampleNotificationManager.UpdateExampleNotifications(),
                () => _themeManager.IsInPinnedAndClosedState());
                
            // Link services
            if (_notificationManager.NotificationService != null)
            {
                _notificationManager.NotificationService.MediaManager = _mediaPlaybackManager;
                _mediaPlaybackManager.NotificationService = _notificationManager.NotificationService;
            }
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
            string deviceId = GetDeviceIdFromSender(sender);
            _mediaPlaybackManager.PreviousButton_Click(sender, e, deviceId);
        }

        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            string deviceId = GetDeviceIdFromSender(sender);
            _mediaPlaybackManager.PlayPauseButton_Click(sender, e, deviceId);
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            string deviceId = GetDeviceIdFromSender(sender);
            _mediaPlaybackManager.NextButton_Click(sender, e, deviceId);
        }

        // 从 sender 中获取设备 ID
        private string GetDeviceIdFromSender(object sender)
        {
            if (sender is Button button && button.CommandParameter != null)
            {
                return button.CommandParameter.ToString();
            }
            return string.Empty;
        }

        // 处理媒体项点击事件
        private async void MediaItem_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            var border = sender as Windows.UI.Xaml.Controls.Border;
            if (border == null) return;

            // 找到展开面板
            var grid = border.Child as Windows.UI.Xaml.Controls.Grid;
            if (grid == null) return;

            var expandedPanel = grid.FindName("ExpandedPanel") as Windows.UI.Xaml.Controls.Grid;
            if (expandedPanel == null) return;

            // 获取设备ID（从DataContext）
            var mediaSession = border.DataContext as Models.MediaSessionViewModel;
            if (mediaSession == null) return;

            // 切换展开状态
            bool isExpanded;
            if (_expandedStates.TryGetValue(mediaSession.DeviceId, out isExpanded))
            {
                isExpanded = !isExpanded;
            }
            else
            {
                isExpanded = true;
            }

            _expandedStates[mediaSession.DeviceId] = isExpanded;

            // 获取封面相关元素
            var coverBorder = grid.FindName("CoverBorder") as Windows.UI.Xaml.Controls.Border;
            var coverImage = grid.FindName("CoverImage") as Windows.UI.Xaml.Controls.Image;
            var titleText = grid.FindName("TitleText") as Windows.UI.Xaml.Controls.TextBlock;
            var playIndicator = grid.FindName("PlayIndicator") as Windows.UI.Xaml.Controls.Grid;

            // 获取展开态元素
            var expandedTitle = grid.FindName("ExpandedTitle") as Windows.UI.Xaml.Controls.TextBlock;
            var expandedArtist = grid.FindName("ExpandedArtist") as Windows.UI.Xaml.Controls.TextBlock;
            var expandedDevice = grid.FindName("ExpandedDevice") as Windows.UI.Xaml.Controls.TextBlock;
            var expandedControls = grid.FindName("ExpandedControls") as Windows.UI.Xaml.Controls.StackPanel;

            if (coverBorder != null && coverImage != null && titleText != null && playIndicator != null)
            {
                if (isExpanded)
                {
                    // 展开动画
                    expandedPanel.Visibility = Windows.UI.Xaml.Visibility.Visible;
                    
                    // 计算动画参数
                    var startWidth = coverBorder.Width;
                    var startHeight = coverBorder.Height;
                    var startCornerRadius = coverBorder.CornerRadius.TopLeft;
                    
                    var targetWidth = 72.0;
                    var targetHeight = 72.0;
                    var targetCornerRadius = 14.0;
                    
                    // 执行动画
                    var duration = 250; // 动画持续时间（毫秒）
                    var startTime = DateTime.Now;
                    
                    while ((DateTime.Now - startTime).TotalMilliseconds < duration)
                    {
                        var progress = (DateTime.Now - startTime).TotalMilliseconds / duration;
                        // 使用缓动函数（改为更平滑的EaseOutQuad）
                        var easedProgress = 1 - Math.Pow(1 - progress, 3);
                        
                        // 更新封面大小
                        coverBorder.Width = startWidth + (targetWidth - startWidth) * easedProgress;
                        coverBorder.Height = startHeight + (targetHeight - startHeight) * easedProgress;
                        coverBorder.CornerRadius = new Windows.UI.Xaml.CornerRadius(
                            startCornerRadius + (targetCornerRadius - startCornerRadius) * easedProgress
                        );
                        
                        // 更新图片大小
                        coverImage.Width = coverBorder.Width;
                        coverImage.Height = coverBorder.Height;
                        
                        // 淡出收起态的其他元素
                        titleText.Opacity = 1.0 - progress;
                        playIndicator.Opacity = 1.0 - progress;
                        
                        // 淡入展开态的元素
                        if (expandedTitle != null) expandedTitle.Opacity = progress;
                        if (expandedArtist != null) expandedArtist.Opacity = progress;
                        if (expandedDevice != null) expandedDevice.Opacity = progress;
                        if (expandedControls != null) expandedControls.Opacity = progress;
                        
                        // 让出UI线程
                        await Task.Delay(16);
                    }
                    
                    // 确保最终状态
                    coverBorder.Width = targetWidth;
                    coverBorder.Height = targetHeight;
                    coverBorder.CornerRadius = new Windows.UI.Xaml.CornerRadius(targetCornerRadius);
                    coverImage.Width = targetWidth;
                    coverImage.Height = targetHeight;
                    titleText.Opacity = 0;
                    playIndicator.Opacity = 0;
                    
                    if (expandedTitle != null) expandedTitle.Opacity = 1;
                    if (expandedArtist != null) expandedArtist.Opacity = 1;
                    if (expandedDevice != null) expandedDevice.Opacity = 1;
                    if (expandedControls != null) expandedControls.Opacity = 1;
                    
                    // 重置封面位置到展开态的正确位置
                    // 计算从收起态到展开态的位置偏移
                    Windows.UI.Xaml.Media.GeneralTransform transform = coverBorder.TransformToVisual(expandedPanel);
                    var position = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
                    // 定位封面到展开态的第一行左侧
                    coverBorder.Margin = new Windows.UI.Xaml.Thickness(-position.X, -position.Y, 0, 0);
                }
                else
                {
                    // 收起动画
                    var startWidth = coverBorder.Width;
                    var startHeight = coverBorder.Height;
                    var startCornerRadius = coverBorder.CornerRadius.TopLeft;
                    
                    var targetWidth = 18.0;
                    var targetHeight = 18.0;
                    var targetCornerRadius = 5.0;
                    
                    // 执行动画
                    var duration = 250;
                    var startTime = DateTime.Now;
                    
                    while ((DateTime.Now - startTime).TotalMilliseconds < duration)
                    {
                        var progress = (DateTime.Now - startTime).TotalMilliseconds / duration;
                        // 使用缓动函数（改为更平滑的EaseOutQuad）
                        var easedProgress = 1 - Math.Pow(1 - progress, 3);
                        
                        // 更新封面大小
                        coverBorder.Width = startWidth - (startWidth - targetWidth) * easedProgress;
                        coverBorder.Height = startHeight - (startHeight - targetHeight) * easedProgress;
                        coverBorder.CornerRadius = new Windows.UI.Xaml.CornerRadius(
                            startCornerRadius - (startCornerRadius - targetCornerRadius) * easedProgress
                        );
                        
                        // 更新图片大小
                        coverImage.Width = coverBorder.Width;
                        coverImage.Height = coverBorder.Height;
                        
                        // 淡入收起态的其他元素
                        titleText.Opacity = progress;
                        playIndicator.Opacity = progress;
                        
                        // 淡出展开态的元素
                        if (expandedTitle != null) expandedTitle.Opacity = 1.0 - progress;
                        if (expandedArtist != null) expandedArtist.Opacity = 1.0 - progress;
                        if (expandedDevice != null) expandedDevice.Opacity = 1.0 - progress;
                        if (expandedControls != null) expandedControls.Opacity = 1.0 - progress;
                        
                        await Task.Delay(16);
                    }
                    
                    // 确保最终状态
                    coverBorder.Width = targetWidth;
                    coverBorder.Height = targetHeight;
                    coverBorder.CornerRadius = new Windows.UI.Xaml.CornerRadius(targetCornerRadius);
                    coverImage.Width = targetWidth;
                    coverImage.Height = targetHeight;
                    titleText.Opacity = 1;
                    playIndicator.Opacity = 1;
                    
                    if (expandedTitle != null) expandedTitle.Opacity = 0;
                    if (expandedArtist != null) expandedArtist.Opacity = 0;
                    if (expandedDevice != null) expandedDevice.Opacity = 0;
                    if (expandedControls != null) expandedControls.Opacity = 0;
                    
                    // 重置封面位置
                    coverBorder.Margin = new Windows.UI.Xaml.Thickness(0);
                    
                    expandedPanel.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
                }
            }
        }

        // 缓动函数
        private double EaseOutBack(double t)
        {
            double c1 = 1.70158;
            double c3 = c1 + 1;
            return 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2);
        }
    }
}