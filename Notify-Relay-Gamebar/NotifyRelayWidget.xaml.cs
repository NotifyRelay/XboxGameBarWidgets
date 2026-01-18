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
        
        // 示例通知管理
        private bool _showingExampleNotifications = false;
        private StackPanel _mediaBlockExample;
        private StackPanel _notificationExample1;
        private StackPanel _notificationExample2;

        public NotifyRelayWidget()
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
                // 订阅GameBar显示模式变化事件
                widget.GameBarDisplayModeChanged += Widget_GameBarDisplayModeChanged;
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
                widget.GameBarDisplayModeChanged -= Widget_GameBarDisplayModeChanged;
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
                UpdateExampleNotifications();
            });
        }

        /// <summary>
        /// 设置背景透明度和媒体控制按钮可见性
        /// </summary>
        private void SetBackgroundOpacity()
        {
            // 调试日志
            Timber.Log(LoggerLevel.Info, "SetBackgroundOpacity called, widget: {0}, PlayerWidgetView: {1}", widget != null ? "NotNull" : "Null", PlayerWidgetView != null ? "NotNull" : "Null");

            // 当小部件被固定且GameBar关闭时，强制完全透明并调整媒体/通知背景画笔
            try
            {
                if (widget != null)
                {
                    bool isPinnedAndClosed = IsInPinnedAndClosedState();

                    if (isPinnedAndClosed)
                    {
                        Timber.Log(LoggerLevel.Info, "Setting fixed and closed state: Background opacity 0.0");

                        try
                        {
                            PlaybackControlsPanel.Visibility = Visibility.Collapsed;
                        }
                        catch (Exception ex)
                        {
                            Timber.Log(LoggerLevel.Error, ex, "Error setting controls visibility when pinned and closed");
                        }

                        // 在固定且关闭时，仅更改背景画笔（不影响文本和边框）；如果没有会话，隐藏整个媒体块
                        bool hasSessionsPinned = (MediaSessions?.Count ?? 0) > 0;
                        PlayerWidgetView.Visibility = hasSessionsPinned ? Visibility.Visible : Visibility.Collapsed;

                        // 在固定且关闭时，保持PlayerWidgetView背景透明，不设置背景
                        if (PlayerWidgetView != null)
                        {
                            PlayerWidgetView.Background = new SolidColorBrush(Windows.UI.Colors.Transparent);
                        }

                        // ToastStack 始终保持透明，不设置背景
                        if (ToastStack != null)
                        {
                            ToastStack.Background = new SolidColorBrush(Windows.UI.Colors.Transparent);
                        }

                        UpdateExampleNotifications();
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
        /// 处理GameBar显示模式变化事件
        /// </summary>
        private async void Widget_GameBarDisplayModeChanged(object sender, object e)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                Timber.Log(LoggerLevel.Info, "GameBarDisplayModeChanged event triggered, new mode: {0}", widget.GameBarDisplayMode);
                SetBackgroundOpacity();
                UpdateExampleNotifications();
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

        /// <summary>
        /// 检查是否处于固定且关闭状态
        /// </summary>
        /// <returns>如果处于固定且关闭状态返回true，否则返回false</returns>
        private bool IsInPinnedAndClosedState()
        {
            if (widget == null)
                return false;

            bool isPinned = widget.Pinned;
            bool isGameBarClosed = widget.GameBarDisplayMode == XboxGameBarDisplayMode.PinnedOnly;
            
            Timber.Log(LoggerLevel.Info, "IsInPinnedAndClosedState: Pinned={0}, GameBarDisplayMode={1}, Result={2}", 
                isPinned, widget.GameBarDisplayMode, isPinned && isGameBarClosed);
            
            return isPinned && isGameBarClosed;
        }

        /// <summary>
        /// 创建媒体块示例
        /// </summary>
        private StackPanel CreateMediaBlockExample()
        {
            var mediaBlockExample = new StackPanel()
            {
                Margin = new Thickness(6, 4, 6, 4),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(Windows.UI.Colors.Transparent)
            };

            // 阴影层
            var shadowBorder = new Border()
            {
                Margin = new Thickness(2, 2, 0, 0),
                CornerRadius = new CornerRadius(8, 8, 8, 8),
                Background = new SolidColorBrush(Windows.UI.Colors.Black),
                Opacity = 0.5
            };
            mediaBlockExample.Children.Add(shadowBorder);

            // 主边框
            var container = new Border()
            {
                Padding = new Thickness(8),
                CornerRadius = new CornerRadius(8, 8, 8, 8),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                BorderThickness = new Thickness(2),
                BorderBrush = new SolidColorBrush(Windows.UI.Colors.White),
                Background = new SolidColorBrush(Windows.UI.Colors.Transparent)
            };

            // 内层边框
            var innerBorder = new Border()
            {
                CornerRadius = new CornerRadius(6, 6, 6, 6),
                Padding = new Thickness(8),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(200, 38, 38, 38))
            };

            // 内容
            var contentStack = new StackPanel()
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            // 标题
            var titleGrid = new Grid();
            var titleStroke = new TextBlock()
            {
                Text = "媒体控制示例",
                FontWeight = Windows.UI.Text.FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(1, 1, 0, 0),
                Foreground = new SolidColorBrush(Windows.UI.Colors.Black)
            };
            var titleBlock = new TextBlock()
            {
                Text = "媒体控制示例",
                FontWeight = Windows.UI.Text.FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Foreground = new SolidColorBrush(Windows.UI.Colors.White)
            };
            titleGrid.Children.Add(titleStroke);
            titleGrid.Children.Add(titleBlock);

            // 描述
            var descGrid = new Grid();
            var descStroke = new TextBlock()
            {
                Text = "这是一个媒体控制示例，当没有实际媒体数据且处于展示模式下显示",
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(1, 1, 0, 0),
                Foreground = new SolidColorBrush(Windows.UI.Colors.Black)
            };
            var descBlock = new TextBlock()
            {
                Text = "这是一个媒体控制示例，当没有实际媒体数据且处于展示模式下显示",
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Foreground = new SolidColorBrush(Windows.UI.Colors.White)
            };
            descGrid.Children.Add(descStroke);
            descGrid.Children.Add(descBlock);

            contentStack.Children.Add(titleGrid);
            contentStack.Children.Add(descGrid);

            innerBorder.Child = contentStack;
            container.Child = innerBorder;
            mediaBlockExample.Children.Add(container);

            return mediaBlockExample;
        }

        /// <summary>
        /// 创建示例通知
        /// </summary>
        private StackPanel CreateExampleNotification(string title, string content, string appName)
        {
            var notificationExample = new StackPanel()
            {
                Margin = new Thickness(6, 4, 6, 4),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(Windows.UI.Colors.Transparent)
            };

            // 阴影层
            var shadowBorder = new Border()
            {
                Margin = new Thickness(2, 2, 0, 0),
                CornerRadius = new CornerRadius(8, 8, 8, 8),
                Background = new SolidColorBrush(Windows.UI.Colors.Black),
                Opacity = 0.5
            };
            notificationExample.Children.Add(shadowBorder);

            // 主边框
            var container = new Border()
            {
                Padding = new Thickness(8),
                CornerRadius = new CornerRadius(8, 8, 8, 8),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                BorderThickness = new Thickness(2),
                BorderBrush = new SolidColorBrush(Windows.UI.Colors.White),
                Background = new SolidColorBrush(Windows.UI.Colors.Transparent)
            };

            // 内层边框
            var innerBorder = new Border()
            {
                CornerRadius = new CornerRadius(6, 6, 6, 6),
                Padding = new Thickness(8),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(200, 38, 38, 38))
            };

            // 内容
            var vertical = new StackPanel()
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            // 来源行
            var sourceLine = new TextBlock()
            {
                Text = "来自 " + appName,
                FontSize = 12,
                Foreground = new SolidColorBrush(Windows.UI.Colors.Gray),
                TextWrapping = TextWrapping.NoWrap
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(0, GridUnitType.Auto) });
            grid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition());

            // 图标
            var img = new Image()
            {
                Width = 48,
                Height = 48,
                Margin = new Thickness(0, 0, 8, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Source = new BitmapImage(new Uri("ms-appx:///Assets/StoreLogo.png"))
            };

            // 标题
            var titleGrid = new Grid();
            var titleStroke = new TextBlock()
            {
                Text = title,
                FontWeight = Windows.UI.Text.FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(1, 1, 0, 0),
                Foreground = new SolidColorBrush(Windows.UI.Colors.Black)
            };
            var titleBlock = new TextBlock()
            {
                Text = title,
                FontWeight = Windows.UI.Text.FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Foreground = new SolidColorBrush(Windows.UI.Colors.White)
            };
            titleGrid.Children.Add(titleStroke);
            titleGrid.Children.Add(titleBlock);

            // 内容
            var contentGrid = new Grid();
            var contentStroke = new TextBlock()
            {
                Text = content,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(1, 1, 0, 0),
                Foreground = new SolidColorBrush(Windows.UI.Colors.Black)
            };
            var contentBlock = new TextBlock()
            {
                Text = content,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Foreground = new SolidColorBrush(Windows.UI.Colors.White)
            };
            contentGrid.Children.Add(contentStroke);
            contentGrid.Children.Add(contentBlock);

            Grid.SetRow(img, 0);
            Grid.SetColumn(img, 0);
            Grid.SetRowSpan(img, 2);
            Grid.SetRow(titleGrid, 0);
            Grid.SetColumn(titleGrid, 1);
            Grid.SetRow(contentGrid, 1);
            Grid.SetColumn(contentGrid, 1);

            grid.Children.Add(img);
            grid.Children.Add(titleGrid);
            grid.Children.Add(contentGrid);

            vertical.Children.Add(sourceLine);
            vertical.Children.Add(grid);

            innerBorder.Child = vertical;
            container.Child = innerBorder;
            notificationExample.Children.Add(container);

            return notificationExample;
        }

        /// <summary>
        /// 显示示例通知
        /// </summary>
        private void ShowExampleNotifications()
        {
            if (_showingExampleNotifications || ToastStack == null)
                return;

            try
            {
                // 根据GameBar显示模式决定显示哪些示例
                bool isGameBarOpen = widget != null && widget.GameBarDisplayMode == XboxGameBarDisplayMode.Foreground;
                bool hasNoMediaSession = (MediaSessions?.Count ?? 0) == 0;

                // 显示媒体块示例（当没有实际媒体数据且处于展示模式下显示）
                if (hasNoMediaSession && isGameBarOpen)
                {
                    _mediaBlockExample = CreateMediaBlockExample();
                    ToastStack.Children.Insert(0, _mediaBlockExample);
                }

                // 显示通知示例：与媒体块显示条件保持一致（除了是否有媒体数据）
                // 即：当处于展示模式下显示
                bool shouldShowNotifications = isGameBarOpen;
                if (shouldShowNotifications)
                {
                    _notificationExample1 = CreateExampleNotification("通知示例1", "这是第一条示例通知，用于展示通知的外观和样式。", "示例应用");
                    _notificationExample2 = CreateExampleNotification("通知示例2", "这是第二条示例通知，演示了多行文本的显示效果。", "示例应用");
                    
                    ToastStack.Children.Add(_notificationExample1);
                    ToastStack.Children.Add(_notificationExample2);
                }

                _showingExampleNotifications = true;
                Timber.Log(LoggerLevel.Info, "Example notifications shown");
            }
            catch (Exception ex)
            {
                Timber.Log(LoggerLevel.Error, ex, "Error showing example notifications");
            }
        }

        /// <summary>
        /// 隐藏示例通知
        /// </summary>
        private void HideExampleNotifications()
        {
            if (!_showingExampleNotifications || ToastStack == null)
                return;

            try
            {
                // 移除媒体块示例
                if (_mediaBlockExample != null && ToastStack.Children.Contains(_mediaBlockExample))
                {
                    ToastStack.Children.Remove(_mediaBlockExample);
                    _mediaBlockExample = null;
                }

                // 移除通知示例
                if (_notificationExample1 != null && ToastStack.Children.Contains(_notificationExample1))
                {
                    ToastStack.Children.Remove(_notificationExample1);
                    _notificationExample1 = null;
                }

                if (_notificationExample2 != null && ToastStack.Children.Contains(_notificationExample2))
                {
                    ToastStack.Children.Remove(_notificationExample2);
                    _notificationExample2 = null;
                }

                _showingExampleNotifications = false;
                Timber.Log(LoggerLevel.Info, "Example notifications hidden");
            }
            catch (Exception ex)
            {
                Timber.Log(LoggerLevel.Error, ex, "Error hiding example notifications");
            }
        }

        /// <summary>
        /// 更新示例通知显示状态
        /// </summary>
        private void UpdateExampleNotifications()
        {
            if (widget == null)
                return;

            bool isGameBarOpen = widget.GameBarDisplayMode == XboxGameBarDisplayMode.Foreground;
            bool isPinned = widget.Pinned;
            bool hasNoMediaSession = (MediaSessions?.Count ?? 0) == 0;

            Timber.Log(LoggerLevel.Info, "Updating example notifications: GameBarOpen={0}, Pinned={1}, HasNoMediaSession={2}", 
                isGameBarOpen, isPinned, hasNoMediaSession);

            // 隐藏所有示例
            HideExampleNotifications();

            // 非焦点（GameBar关闭）：不显示通知示例
            if (!isGameBarOpen)
            {
                return;
            }

            // 焦点且固定（GameBar打开且固定）：显示通知示例
            if (isPinned)
            {
                // 只显示通知示例，不显示媒体块示例
                _notificationExample1 = CreateExampleNotification("通知示例1", "这是第一条示例通知，用于展示通知的外观和样式。", "示例应用");
                _notificationExample2 = CreateExampleNotification("通知示例2", "这是第二条示例通知，演示了多行文本的显示效果。", "示例应用");
                
                ToastStack.Children.Add(_notificationExample1);
                ToastStack.Children.Add(_notificationExample2);
                _showingExampleNotifications = true;
                return;
            }

            // 焦点且非固定（GameBar打开且未固定）：显示媒体块示例（当没有实际媒体数据时）
            if (hasNoMediaSession)
            {
                _mediaBlockExample = CreateMediaBlockExample();
                ToastStack.Children.Insert(0, _mediaBlockExample);
                _showingExampleNotifications = true;
            }
            // 焦点且非固定：不显示通知示例
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
                    if (widget != null && IsInPinnedAndClosedState())
                    {
                        // 固定且关闭状态下隐藏媒体控制按钮；如果没有会话，则隐藏整个媒体块
                        PlaybackControlsPanel.Visibility = Visibility.Collapsed;
                        bool hasSessionsPinned = (MediaSessions?.Count ?? 0) > 0;
                        PlayerWidgetView.Visibility = hasSessionsPinned ? Visibility.Visible : Visibility.Collapsed;
                    }
                    else
                    {
                        // 非固定且关闭状态下根据媒体会话数量显示/隐藏
                        var visibility = hasSessions ? Visibility.Visible : Visibility.Collapsed;
                        PlaybackControlsPanel.Visibility = visibility;
                        PlayerWidgetView.Visibility = visibility;
                    }
                    
                    // 更新示例通知显示状态
                    UpdateExampleNotifications();
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