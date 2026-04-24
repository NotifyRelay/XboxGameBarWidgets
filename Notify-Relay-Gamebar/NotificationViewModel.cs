using NotifyRelayGamebar.Models;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using TimberLog;
using Windows.Foundation;
using Windows.ApplicationModel.Core;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;

namespace NotifyRelayGamebar
{
    public class NotificationViewModel : INotifyPropertyChanged
    {
        private static readonly TimeSpan SuperIslandTimeout = TimeSpan.FromSeconds(10);
        private ObservableCollection<NotificationModel> _notifications;
        public ObservableCollection<NotificationModel> Notifications
        {
            get { return _notifications; }
            set
            {
                _notifications = value;
                OnPropertyChanged(nameof(Notifications));
            }
        }

        private bool _hasMediaSession;
        public bool HasMediaSession
        {
            get { return _hasMediaSession; }
            set
            {
                _hasMediaSession = value;
                OnPropertyChanged(nameof(HasMediaSession));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private ObservableCollection<SuperIslandViewModel> _superIslands;
        public ObservableCollection<SuperIslandViewModel> SuperIslands
        {
            get { return _superIslands; }
            set
            {
                _superIslands = value;
                OnPropertyChanged(nameof(SuperIslands));
            }
        }

        private bool _hasSuperIslands;
        public bool HasSuperIslands
        {
            get { return _hasSuperIslands; }
            set
            {
                _hasSuperIslands = value;
                OnPropertyChanged(nameof(HasSuperIslands));
            }
        }

        public NotificationViewModel()
        {
            Notifications = new ObservableCollection<NotificationModel>();
            SuperIslands = new ObservableCollection<SuperIslandViewModel>();
            SuperIslands.CollectionChanged += SuperIslands_CollectionChanged;
            UpdateHasSuperIslands();
        }

        private StackPanel _toastStack;
        private Windows.UI.Core.CoreDispatcher _uiDispatcher;
        private StackPanel _expandedToastContainer;
        private Grid _minimizedIconContainer;
        private readonly List<InternalToastEntry> _internalToasts = new List<InternalToastEntry>();
        private bool _notificationHostInitialized;

        private const int ExpandedToastDurationMs = 4000;
        private const int MinimizedIconDurationMs = 30000;
        private const double MinimizedIconSlotWidth = 26;
        private const double MinimizedIconSlotHeight = 26;
        private const int MaxExpandedToasts = 2;
        private const double BodyScrollSpeedPxPerSec = 15.0;
        private const int BodyScrollPauseBeforeMs = 1500;
        private const int BodyScrollReadBufferMs = 1000;
        private const double BodyLineHeightFactor = 1.33;
        private readonly Queue<InternalToastEntry> _pendingExpandQueue = new Queue<InternalToastEntry>();

        private sealed class InternalToastEntry
        {
            public Grid Card { get; set; }
            public Button IconButton { get; set; }
            public Image IconImage { get; set; }
            public bool IsExpanded { get; set; }
            public bool IsRemoved { get; set; }
            public int ExpandedLifetimeToken { get; set; }
            public int IconLifetimeToken { get; set; }
            public FrameworkElement BodyScrollContainer { get; set; }
            public Grid BodyGrid { get; set; }
            public Storyboard BodyScrollStoryboard { get; set; }
            public bool PendingExpand { get; set; }
            public double ScrollDurationMs { get; set; }
        }
    
        public StackPanel ToastStack
        {
            set 
            {
                if (_toastStack != null)
                {
                    _toastStack.SizeChanged -= ToastStack_SizeChanged;
                }

                _toastStack = value;
                _notificationHostInitialized = false;
                _expandedToastContainer = null;
                _minimizedIconContainer = null;
                // 获取并保存UI线程的调度器
                if (value != null)
                {
                    _uiDispatcher = value.Dispatcher;
                    Timber.Log(LoggerLevel.Info, "ToastStack set, Dispatcher: {0}", _uiDispatcher != null ? "NotNull" : "Null");
                    EnsureNotificationHost();
                }
                else
                {
                    Timber.Log(LoggerLevel.Info, "ToastStack set to null");
                }
            }
        }

        public async Task AddNotification(NotificationModel notification)
        {
            Timber.Log(LoggerLevel.Info, "AddNotification called, ToastStack: {0}", _toastStack != null ? "NotNull" : "Null");
            // 优先使用ToastStack自身的Dispatcher（来自小部件窗口），若为空则回退到MainView的Dispatcher
            var dispatcherToUse = _uiDispatcher ?? Windows.ApplicationModel.Core.CoreApplication.MainView.Dispatcher;
            await dispatcherToUse.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                Notifications.Insert(0, notification);

                // 限制通知数量，只保留最近的10条
                if (Notifications.Count > 10)
                {
                    Notifications.RemoveAt(Notifications.Count - 1);
                }

                // 直接在UI上显示通知
                Timber.Log(LoggerLevel.Info, "In AddNotification UI thread, ToastStack: {0}", _toastStack != null ? "NotNull" : "Null");
                if (_toastStack != null)
                {
                    ShowInternalToast(notification);
                }
                else
                {
                    Timber.Log(LoggerLevel.Warn, "_toastStack is null in AddNotification");
                }
            });
        }

        public async Task AddOrUpdateSuperIsland(string deviceId, string deviceName, string sourceId, bool isEnd, Windows.Data.Json.JsonObject payload)
        {
            var dispatcherToUse = _uiDispatcher ?? CoreApplication.MainView.Dispatcher;
            await dispatcherToUse.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                var now = DateTimeOffset.Now;
                if (isEnd)
                {
                    RemoveSuperIsland(sourceId);
                    SuperIslandStore.RemoveExact(sourceId);
                    return;
                }

                if (payload == null)
                {
                    return;
                }

                var merged = SuperIslandStore.ApplyIncoming(sourceId, payload);
                if (merged == null)
                {
                    RemoveSuperIsland(sourceId);
                    return;
                }

                if (!_superIslandMap.TryGetValue(sourceId, out var viewModel))
                {
                    viewModel = new SuperIslandViewModel
                    {
                        SourceId = sourceId,
                        DeviceId = deviceId,
                        DeviceName = deviceName
                    };
                    _superIslandMap[sourceId] = viewModel;
                    SuperIslands.Insert(0, viewModel);
                }
                else
                {
                    viewModel.DeviceId = deviceId;
                    viewModel.DeviceName = deviceName;
                }

                viewModel.UpdateFromState(merged);
                _ = viewModel.UpdateImageAsync(merged.Pics);
                _superIslandLastSeen[sourceId] = now;
                EnsureSuperIslandTimer();
                UpdateHasSuperIslands();
            });
        }
    
        private async void ShowInternalToast(NotificationModel notification)
        {
            // 确保使用正确的UI线程调度器
            if (_uiDispatcher == null || _toastStack == null)
            {
                Timber.Log(LoggerLevel.Error, "Error showing internal toast: UI dispatcher or toast stack is null");
                return;
            }

            try
            {
                // 将所有对 XAML 的创建与修改都封装到 ToastStack 对应的 Dispatcher 上，避免跨窗口/跨线程访问
                await _uiDispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    try
                    {
                        EnsureNotificationHost();

                        // 创建通知UI元素
                        // 创建带阴影的容器，使用Grid和多层边框实现
                        Grid shadowGrid = new Grid();
                        shadowGrid.Margin = new Thickness(6, 4, 6, 4);
                        shadowGrid.HorizontalAlignment = HorizontalAlignment.Stretch;
                        
                        // 阴影层
                        Border shadowBorder = new Border();
                        shadowBorder.CornerRadius = new CornerRadius(8, 8, 8, 8);
                        shadowBorder.Margin = new Thickness(2, 2, 0, 0);
                        shadowBorder.Background = new SolidColorBrush(Windows.UI.Colors.Black);
                        shadowBorder.Opacity = 0.5;
                        
                        // 主边框
                        Border container = new Border();
                        container.CornerRadius = new CornerRadius(8, 8, 8, 8);
                        container.Padding = new Thickness(0);
                        container.HorizontalAlignment = HorizontalAlignment.Stretch;
                        // 添加白色描边
                        container.BorderThickness = new Thickness(2);
                        container.BorderBrush = new SolidColorBrush(Windows.UI.Colors.White);
                        container.Background = new SolidColorBrush(Windows.UI.Colors.Transparent);
                        
                        // 内层容器，设置背景色和圆角，Border会自动按圆角裁剪背景
                        Border innerBorder = new Border();
                        innerBorder.CornerRadius = new CornerRadius(6, 6, 6, 6);
                        innerBorder.Padding = new Thickness(8);
                        if (Application.Current.RequestedTheme == ApplicationTheme.Dark)
                        {
                            innerBorder.Background = new SolidColorBrush(Color.FromArgb(200, 38, 38, 38));
                        }
                        else
                        {
                            innerBorder.Background = new SolidColorBrush(Color.FromArgb(200, 219, 219, 219));
                        }
                        
                        // 构建层次结构
                        container.Child = innerBorder;
                        shadowGrid.Children.Add(shadowBorder);
                        shadowGrid.Children.Add(container);

                        StackPanel vertical = new StackPanel();
                        vertical.Orientation = Orientation.Vertical;
                        vertical.HorizontalAlignment = HorizontalAlignment.Stretch;

                        TextBlock sourceLine = new TextBlock();
                        var appName = string.IsNullOrWhiteSpace(notification.AppName) ? "未知应用" : notification.AppName;
                        if (!string.IsNullOrWhiteSpace(notification.DeviceName))
                        {
                            sourceLine.Text = "来自" + notification.DeviceName + "的" + appName;
                        }
                        else
                        {
                            sourceLine.Text = "来自" + appName;
                        }
                        sourceLine.FontSize = 12;
                        sourceLine.Foreground = new SolidColorBrush(Windows.UI.Colors.Gray);
                        sourceLine.TextWrapping = TextWrapping.NoWrap;

                        Grid grid = new Grid();
                        ColumnDefinition col0 = new ColumnDefinition();
                        ColumnDefinition col1 = new ColumnDefinition();
                        col0.Width = new GridLength(0, GridUnitType.Auto);
                        col1.Width = new GridLength(1, GridUnitType.Star);
                        grid.ColumnDefinitions.Add(col0);
                        grid.ColumnDefinitions.Add(col1);
                        grid.HorizontalAlignment = HorizontalAlignment.Stretch;
                        RowDefinition row0 = new RowDefinition();
                        RowDefinition row1 = new RowDefinition();
                        grid.RowDefinitions.Add(row0);
                        grid.RowDefinitions.Add(row1);

                        Image img = new Image();
                        img.Width = 48;
                        img.Height = 48;
                        img.Margin = new Thickness(0, 0, 8, 0);
                        img.HorizontalAlignment = HorizontalAlignment.Left;
                        img.VerticalAlignment = VerticalAlignment.Top;

                        // 使用Grid和两个重叠的TextBlock实现描边效果
                        Grid titleGrid = new Grid();
                        titleGrid.HorizontalAlignment = HorizontalAlignment.Stretch;
                        
                        // 底层描边文本
                        TextBlock titleStroke = new TextBlock();
                        titleStroke.Text = notification.Title;
                        titleStroke.FontWeight = Windows.UI.Text.FontWeights.Bold;
                        titleStroke.TextWrapping = TextWrapping.Wrap;
                        titleStroke.HorizontalAlignment = HorizontalAlignment.Stretch;
                        titleStroke.Margin = new Thickness(1, 1, 0, 0);
                        titleStroke.Foreground = Application.Current.RequestedTheme == ApplicationTheme.Dark ? 
                            new SolidColorBrush(Windows.UI.Colors.Black) : 
                            new SolidColorBrush(Windows.UI.Colors.White);
                        
                        // 上层主文本
                        TextBlock titleBlock = new TextBlock();
                        titleBlock.Text = notification.Title;
                        titleBlock.FontWeight = Windows.UI.Text.FontWeights.Bold;
                        titleBlock.TextWrapping = TextWrapping.Wrap;
                        titleBlock.HorizontalAlignment = HorizontalAlignment.Stretch;
                        titleBlock.Foreground = Application.Current.RequestedTheme == ApplicationTheme.Dark ? 
                            new SolidColorBrush(Windows.UI.Colors.White) : 
                            new SolidColorBrush(Windows.UI.Colors.Black);
                        
                        titleGrid.Children.Add(titleStroke);
                        titleGrid.Children.Add(titleBlock);

                        Grid bodyGrid = new Grid();
                        bodyGrid.HorizontalAlignment = HorizontalAlignment.Stretch;

                        // 底层描边文本
                        TextBlock bodyStroke = new TextBlock();
                        bodyStroke.Text = notification.Body;
                        bodyStroke.FontSize = 14;
                        bodyStroke.TextWrapping = TextWrapping.Wrap;
                        bodyStroke.HorizontalAlignment = HorizontalAlignment.Stretch;
                        bodyStroke.Margin = new Thickness(1, 1, 0, 0);
                        bodyStroke.Foreground = Application.Current.RequestedTheme == ApplicationTheme.Dark ?
                            new SolidColorBrush(Windows.UI.Colors.Black) :
                            new SolidColorBrush(Windows.UI.Colors.White);

                        // 上层主文本
                        TextBlock bodyBlock = new TextBlock();
                        bodyBlock.Text = notification.Body;
                        bodyBlock.FontSize = 14;
                        bodyBlock.TextWrapping = TextWrapping.Wrap;
                        bodyBlock.HorizontalAlignment = HorizontalAlignment.Stretch;
                        bodyBlock.Foreground = Application.Current.RequestedTheme == ApplicationTheme.Dark ?
                            new SolidColorBrush(Windows.UI.Colors.White) :
                            new SolidColorBrush(Windows.UI.Colors.Black);

                        bodyGrid.Children.Add(bodyStroke);
                        bodyGrid.Children.Add(bodyBlock);

                        var threeLineHeight = 14 * BodyLineHeightFactor * 3;

                        // bodyScrollContainer: Canvas 不约束子元素布局，
                        // bodyGrid 在里面可以自由撑开到文本实际高度，
                        // 而 Canvas 的固定 Height 使卡片本身不撑大
                        Canvas bodyScrollContainer = new Canvas
                        {
                            Height = threeLineHeight,
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            Clip = new RectangleGeometry { Rect = new Rect(0, 0, 2000, threeLineHeight) }
                        };
                        bodyGrid.RenderTransform = new TranslateTransform { Y = 0 };
                        Canvas.SetLeft(bodyGrid, 0);
                        Canvas.SetTop(bodyGrid, 0);
                        bodyScrollContainer.Children.Add(bodyGrid);
                        // 设置 bodyGrid 宽度与父级一致，使 TextBlock 能正确换行
                        bodyScrollContainer.SizeChanged += (sender, args) =>
                        {
                            var w = bodyScrollContainer.ActualWidth;
                            if (w > 0) bodyGrid.Width = w;
                        };

                        Grid.SetRow(img, 0);
                        Grid.SetColumn(img, 0);
                        Grid.SetRowSpan(img, 2);
                        Grid.SetRow(titleGrid, 0);
                        Grid.SetColumn(titleGrid, 1);
                        Grid.SetRow(bodyScrollContainer, 1);
                        Grid.SetColumn(bodyScrollContainer, 1);

                        grid.Children.Add(img);
                        grid.Children.Add(titleGrid);
                        grid.Children.Add(bodyScrollContainer);

                        vertical.Children.Add(sourceLine);
                        vertical.Children.Add(grid);

                        // 将内容添加到内层边框，再将内层边框添加到外层边框
                        innerBorder.Child = vertical;
                        container.Child = innerBorder;

                        var iconButton = CreateMinimizedIconButton(out var iconImage);
                        var toastEntry = new InternalToastEntry
                        {
                            Card = shadowGrid,
                            IconButton = iconButton,
                            IconImage = iconImage,
                            IsExpanded = true,
                            BodyScrollContainer = bodyScrollContainer,
                            BodyGrid = bodyGrid
                        };

                        bodyScrollContainer.SizeChanged += (sender, args) =>
                        {
                            if (toastEntry.IsRemoved) return;
                            var availableWidth = bodyScrollContainer.ActualWidth;
                            if (availableWidth <= 0) return;

                            // bodyGrid 在 Canvas 内不受高度约束，Measure TextBlock 获得实际文本高度
                            bodyBlock.Measure(new Windows.Foundation.Size(availableWidth, double.PositiveInfinity));
                            var contentHeight = bodyBlock.DesiredSize.Height;

                            if (contentHeight > threeLineHeight + 2)
                            {
                                var overflow = contentHeight - threeLineHeight;
                                toastEntry.ScrollDurationMs = overflow / BodyScrollSpeedPxPerSec * 1000.0;
                                if (toastEntry.ScrollDurationMs < 500) toastEntry.ScrollDurationMs = 500;
                                bodyScrollContainer.Clip = new RectangleGeometry { Rect = new Rect(0, 0, availableWidth, threeLineHeight) };
                                StartBodyScrollAnimation(toastEntry);
                            }

                            ScheduleAutoCollapse(toastEntry);
                        };

                        shadowGrid.Tapped += (sender, args) =>
                        {
                            CollapseToastToIcon(toastEntry);
                        };

                        iconButton.Click += (sender, args) =>
                        {
                            ExpandToastFromIcon(toastEntry);
                        };

                        _internalToasts.Add(toastEntry);

                        // 在UI线程上触发图标加载（LoadIconImageAsync 内部会再次使用 _uiDispatcher）
                        if (!string.IsNullOrEmpty(notification.IconUrl))
                        {
                            LoadIconImageAsync(img, notification.IconUrl);
                            LoadIconImageAsync(iconImage, notification.IconUrl);
                        }

                        // 播放通知声音（非 UI 操作，可以在这里调用）
                        try
                        {
                            NativeMethods.PlaySound("Notification.Default", IntPtr.Zero, (uint)(SoundFlags.SND_ALIAS | SoundFlags.SND_ASYNC | SoundFlags.SND_NODEFAULT));
                        }
                        catch (Exception ex)
                        {
                            Timber.Log(LoggerLevel.Error, ex, "Failed to play notification sound");
                        }

                        var expandedCount = _internalToasts.Count(e => e.IsExpanded && !e.IsRemoved);
                        if (expandedCount > MaxExpandedToasts)
                        {
                            toastEntry.IsExpanded = false;
                            toastEntry.PendingExpand = true;
                            _pendingExpandQueue.Enqueue(toastEntry);
                            RelayoutMinimizedIcons();
                            return;
                        }

                        _expandedToastContainer.Children.Insert(0, shadowGrid);

                        // 准备"气泡弹出"入场动画：先小后大并轻微上浮，同时淡入
                        StartBubblePopAnimation(shadowGrid);

                        // 不在这里立即调用 ScheduleAutoCollapse，
                        // 而是等待 SizeChanged 事件触发后，在那里面设置 ScrollDurationMs 并启动滚动动画和计时器
                        // 这样可以确保 auto-collapse 的延迟时间正确计算
                    }
                    catch (Exception ex)
                    {
                        Timber.Log(LoggerLevel.Error, ex, "Error creating internal toast UI (inside dispatcher)");
                    }
                });
            }
            catch (Exception ex)
            {
                Timber.Log(LoggerLevel.Error, ex, "Error creating internal toast UI");
            }
        }

        private void EnsureNotificationHost()
        {
            if (_notificationHostInitialized || _toastStack == null)
            {
                return;
            }

            _expandedToastContainer = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 4)
            };

            _minimizedIconContainer = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(6, 0, 6, 8),
                Visibility = Visibility.Collapsed
            };

            _toastStack.Children.Add(_expandedToastContainer);
            _toastStack.Children.Add(_minimizedIconContainer);
            _toastStack.SizeChanged += ToastStack_SizeChanged;

            _notificationHostInitialized = true;
            RelayoutMinimizedIcons();
        }

        private void ToastStack_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RelayoutMinimizedIcons();
        }

        private Button CreateMinimizedIconButton(out Image iconImage)
        {
            iconImage = new Image
            {
                Width = 20,
                Height = 20,
                Stretch = Stretch.UniformToFill
            };

            var button = new Button
            {
                Width = 20,
                Height = 20,
                Padding = new Thickness(0),
                Margin = new Thickness(1),
                BorderThickness = new Thickness(0),
                Background = new SolidColorBrush(Colors.Transparent),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Content = iconImage
            };

            return button;
        }

        private void StartBubblePopAnimation(UIElement element)
        {
            if (element == null)
            {
                return;
            }

            element.Opacity = 0;
            element.RenderTransformOrigin = new Point(0.5, 0.5);
            element.RenderTransform = new CompositeTransform
            {
                ScaleX = 0.78,
                ScaleY = 0.78,
                TranslateY = 12
            };

            var popupStoryboard = new Storyboard();

            var fadeInAnimation = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(210),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(fadeInAnimation, element);
            Storyboard.SetTargetProperty(fadeInAnimation, "Opacity");

            var scaleXAnimation = new DoubleAnimation
            {
                From = 0.78,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(260),
                EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(scaleXAnimation, element);
            Storyboard.SetTargetProperty(scaleXAnimation, "(UIElement.RenderTransform).(CompositeTransform.ScaleX)");

            var scaleYAnimation = new DoubleAnimation
            {
                From = 0.78,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(260),
                EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(scaleYAnimation, element);
            Storyboard.SetTargetProperty(scaleYAnimation, "(UIElement.RenderTransform).(CompositeTransform.ScaleY)");

            var translateYAnimation = new DoubleAnimation
            {
                From = 12,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(260),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(translateYAnimation, element);
            Storyboard.SetTargetProperty(translateYAnimation, "(UIElement.RenderTransform).(CompositeTransform.TranslateY)");

            popupStoryboard.Children.Add(fadeInAnimation);
            popupStoryboard.Children.Add(scaleXAnimation);
            popupStoryboard.Children.Add(scaleYAnimation);
            popupStoryboard.Children.Add(translateYAnimation);
            popupStoryboard.Begin();
        }

        private void StartBodyScrollAnimation(InternalToastEntry toastEntry)
        {
            if (toastEntry == null || toastEntry.IsRemoved || toastEntry.BodyGrid == null)
            {
                return;
            }

            StopBodyScrollAnimation(toastEntry);

            var bodyGrid = toastEntry.BodyGrid;
            var scrollDurationMs = toastEntry.ScrollDurationMs;
            if (scrollDurationMs <= 0) return;

            var containerHeight = toastEntry.BodyScrollContainer?.Height ?? 0;

            var scrollStoryboard = new Storyboard();

            var scrollAnimation = new DoubleAnimation
            {
                From = 0,
                To = -(toastEntry.ScrollDurationMs / 1000.0 * BodyScrollSpeedPxPerSec),
                Duration = TimeSpan.FromMilliseconds(scrollDurationMs),
                BeginTime = TimeSpan.FromMilliseconds(BodyScrollPauseBeforeMs)
            };
            Storyboard.SetTarget(scrollAnimation, bodyGrid);
            Storyboard.SetTargetProperty(scrollAnimation, "(UIElement.RenderTransform).(TranslateTransform.Y)");

            scrollStoryboard.Children.Add(scrollAnimation);
            toastEntry.BodyScrollStoryboard = scrollStoryboard;
            scrollStoryboard.Begin();
        }

        private void StopBodyScrollAnimation(InternalToastEntry toastEntry)
        {
            if (toastEntry?.BodyScrollStoryboard != null)
            {
                toastEntry.BodyScrollStoryboard.Stop();
                toastEntry.BodyScrollStoryboard = null;
            }
        }

        private int GetAutoCollapseDelayMs(InternalToastEntry toastEntry)
        {
            if (toastEntry == null) return ExpandedToastDurationMs;
            if (toastEntry.ScrollDurationMs > 0)
            {
                return BodyScrollPauseBeforeMs + (int)toastEntry.ScrollDurationMs + BodyScrollReadBufferMs;
            }
            return ExpandedToastDurationMs;
        }

        private void CollapseToastToIcon(InternalToastEntry toastEntry)
        {
            if (toastEntry == null || toastEntry.IsRemoved || !toastEntry.IsExpanded)
            {
                return;
            }

            toastEntry.IsExpanded = false;
            toastEntry.ExpandedLifetimeToken++;
            StopBodyScrollAnimation(toastEntry);

            toastEntry.Card.IsHitTestVisible = false;
            var shrinkStoryboard = new Storyboard();

            var fadeAnimation = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(170),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            Storyboard.SetTarget(fadeAnimation, toastEntry.Card);
            Storyboard.SetTargetProperty(fadeAnimation, "Opacity");

            var scaleXAnimation = new DoubleAnimation
            {
                From = 1,
                To = 0.82,
                Duration = TimeSpan.FromMilliseconds(170),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            Storyboard.SetTarget(scaleXAnimation, toastEntry.Card);
            Storyboard.SetTargetProperty(scaleXAnimation, "(UIElement.RenderTransform).(CompositeTransform.ScaleX)");

            var scaleYAnimation = new DoubleAnimation
            {
                From = 1,
                To = 0.82,
                Duration = TimeSpan.FromMilliseconds(170),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            Storyboard.SetTarget(scaleYAnimation, toastEntry.Card);
            Storyboard.SetTargetProperty(scaleYAnimation, "(UIElement.RenderTransform).(CompositeTransform.ScaleY)");

            shrinkStoryboard.Children.Add(fadeAnimation);
            shrinkStoryboard.Children.Add(scaleXAnimation);
            shrinkStoryboard.Children.Add(scaleYAnimation);

            shrinkStoryboard.Completed += (sender, args) =>
            {
                if (toastEntry.IsRemoved)
                {
                    return;
                }

                if (_expandedToastContainer?.Children.Contains(toastEntry.Card) == true)
                {
                    _expandedToastContainer.Children.Remove(toastEntry.Card);
                }

                toastEntry.Card.IsHitTestVisible = true;
                toastEntry.Card.Opacity = 1;
                toastEntry.Card.RenderTransform = new CompositeTransform
                {
                    ScaleX = 1,
                    ScaleY = 1,
                    TranslateY = 0
                };
                toastEntry.Card.RenderTransformOrigin = new Point(0.5, 0.5);

                RelayoutMinimizedIcons();
                ScheduleMinimizedIconRemoval(toastEntry);

                while (_pendingExpandQueue.Count > 0)
                {
                    var nextEntry = _pendingExpandQueue.Dequeue();
                    if (nextEntry.IsRemoved) continue;
                    nextEntry.PendingExpand = false;
                    ExpandToastFromIcon(nextEntry);
                    break;
                }
            };

            shrinkStoryboard.Begin();
        }

        private void ExpandToastFromIcon(InternalToastEntry toastEntry)
        {
            if (toastEntry == null || toastEntry.IsRemoved || toastEntry.IsExpanded)
            {
                return;
            }

            if (toastEntry.PendingExpand)
            {
                RemoveFromPendingQueue(toastEntry);
            }

            var expandedCount = _internalToasts.Count(e => e.IsExpanded && !e.IsRemoved);
            if (expandedCount >= MaxExpandedToasts)
            {
                toastEntry.PendingExpand = true;
                _pendingExpandQueue.Enqueue(toastEntry);
                return;
            }

            toastEntry.IsExpanded = true;
            toastEntry.IconLifetimeToken++;

            _expandedToastContainer?.Children.Insert(0, toastEntry.Card);
            StartBubblePopAnimation(toastEntry.Card);
            RelayoutMinimizedIcons();
            ScheduleAutoCollapse(toastEntry);
        }

        private void RemoveFromPendingQueue(InternalToastEntry entry)
        {
            if (entry == null || !entry.PendingExpand) return;
            var remaining = new Queue<InternalToastEntry>();
            while (_pendingExpandQueue.Count > 0)
            {
                var item = _pendingExpandQueue.Dequeue();
                if (item != entry) remaining.Enqueue(item);
            }
            while (remaining.Count > 0)
            {
                _pendingExpandQueue.Enqueue(remaining.Dequeue());
            }
            entry.PendingExpand = false;
        }

        private void RelayoutMinimizedIcons()
        {
            if (_minimizedIconContainer == null)
            {
                return;
            }

            _minimizedIconContainer.Children.Clear();
            _minimizedIconContainer.RowDefinitions.Clear();
            _minimizedIconContainer.ColumnDefinitions.Clear();

            var minimizedEntries = _internalToasts.Where(entry => !entry.IsRemoved && !entry.IsExpanded).ToList();
            if (minimizedEntries.Count == 0)
            {
                _minimizedIconContainer.Visibility = Visibility.Collapsed;
                return;
            }

            var availableWidth = _minimizedIconContainer.ActualWidth;
            if (availableWidth <= 0 && _toastStack != null)
            {
                availableWidth = Math.Max(0, _toastStack.ActualWidth - 20);
            }

            var columns = availableWidth > 0
                ? Math.Max(1, (int)Math.Floor(availableWidth / MinimizedIconSlotWidth))
                : 1;

            for (var c = 0; c < columns; c++)
            {
                _minimizedIconContainer.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(MinimizedIconSlotWidth, GridUnitType.Pixel)
                });
            }

            var rows = (int)Math.Ceiling((double)minimizedEntries.Count / columns);
            for (var r = 0; r < rows; r++)
            {
                _minimizedIconContainer.RowDefinitions.Add(new RowDefinition
                {
                    Height = new GridLength(MinimizedIconSlotHeight, GridUnitType.Pixel)
                });
            }

            for (var i = 0; i < minimizedEntries.Count; i++)
            {
                var row = i / columns;
                var col = i % columns;
                var iconButton = minimizedEntries[i].IconButton;
                iconButton.Opacity = 1;
                Grid.SetRow(iconButton, row);
                Grid.SetColumn(iconButton, col);
                _minimizedIconContainer.Children.Add(iconButton);
            }

            _minimizedIconContainer.Visibility = Visibility.Visible;
        }

        private void ScheduleAutoCollapse(InternalToastEntry toastEntry)
        {
            if (toastEntry == null || toastEntry.IsRemoved)
            {
                return;
            }

            var delayMs = GetAutoCollapseDelayMs(toastEntry);
            var token = ++toastEntry.ExpandedLifetimeToken;
            Task.Run(async () =>
            {
                await Task.Delay(delayMs);
                await _uiDispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    if (toastEntry.IsRemoved || !toastEntry.IsExpanded || toastEntry.ExpandedLifetimeToken != token)
                    {
                        return;
                    }

                    CollapseToastToIcon(toastEntry);
                });
            });
        }

        private void ScheduleMinimizedIconRemoval(InternalToastEntry toastEntry)
        {
            if (toastEntry == null || toastEntry.IsRemoved)
            {
                return;
            }

            var token = ++toastEntry.IconLifetimeToken;
            Task.Run(async () =>
            {
                await Task.Delay(MinimizedIconDurationMs);
                await _uiDispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    if (toastEntry.IsRemoved || toastEntry.IsExpanded || toastEntry.IconLifetimeToken != token)
                    {
                        return;
                    }

                    RemoveToastEntry(toastEntry);
                });
            });
        }

        private void RemoveToastEntry(InternalToastEntry toastEntry)
        {
            if (toastEntry == null || toastEntry.IsRemoved)
            {
                return;
            }

            toastEntry.IsRemoved = true;
            toastEntry.IconLifetimeToken++;
            toastEntry.ExpandedLifetimeToken++;
            StopBodyScrollAnimation(toastEntry);

            if (toastEntry.PendingExpand)
            {
                RemoveFromPendingQueue(toastEntry);
            }

            if (toastEntry.Card?.Parent is Panel cardParent)
            {
                cardParent.Children.Remove(toastEntry.Card);
            }

            if (toastEntry.IconButton?.Parent is Panel iconParent)
            {
                iconParent.Children.Remove(toastEntry.IconButton);
            }

            _internalToasts.Remove(toastEntry);
            RelayoutMinimizedIcons();

            while (_pendingExpandQueue.Count > 0)
            {
                var nextEntry = _pendingExpandQueue.Dequeue();
                if (nextEntry.IsRemoved) continue;
                nextEntry.PendingExpand = false;
                ExpandToastFromIcon(nextEntry);
                break;
            }
        }

        private readonly Dictionary<string, SuperIslandViewModel> _superIslandMap = new Dictionary<string, SuperIslandViewModel>();
        private readonly Dictionary<string, DateTimeOffset> _superIslandLastSeen = new Dictionary<string, DateTimeOffset>();
        private DispatcherTimer _superIslandTimer;

        private void RemoveSuperIsland(string sourceId)
        {
            if (_superIslandMap.TryGetValue(sourceId, out var viewModel))
            {
                SuperIslands.Remove(viewModel);
                _superIslandMap.Remove(sourceId);
            }

            _superIslandLastSeen.Remove(sourceId);

            EnsureSuperIslandTimer();
            UpdateHasSuperIslands();
        }

        private void SuperIslands_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateHasSuperIslands();
        }

        private void UpdateHasSuperIslands()
        {
            HasSuperIslands = SuperIslands != null && SuperIslands.Count > 0;
        }

        private void EnsureSuperIslandTimer()
        {
            var hasItems = SuperIslands.Count > 0;
            if (!hasItems)
            {
                if (_superIslandTimer != null)
                {
                    _superIslandTimer.Stop();
                    _superIslandTimer.Tick -= SuperIslandTimer_Tick;
                    _superIslandTimer = null;
                }
                return;
            }

            if (_superIslandTimer == null)
            {
                _superIslandTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                _superIslandTimer.Tick += SuperIslandTimer_Tick;
                _superIslandTimer.Start();
            }
        }

        private void SuperIslandTimer_Tick(object sender, object e)
        {
            var now = DateTimeOffset.Now;
            foreach (var item in SuperIslands)
            {
                item.UpdateTimer(now);
            }

            var expired = _superIslandLastSeen
                .Where(entry => now - entry.Value > SuperIslandTimeout)
                .Select(entry => entry.Key)
                .ToList();

            foreach (var sourceId in expired)
            {
                RemoveSuperIsland(sourceId);
                SuperIslandStore.RemoveExact(sourceId);
            }
        }
        
        /// <summary>
        /// 在UI线程上加载图标
        /// </summary>
        private async void LoadIconImageAsync(Image image, string iconUrl)
        {
            if (string.IsNullOrEmpty(iconUrl) || _uiDispatcher == null)
            {
                return;
            }
            
            try
            {
                await _uiDispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
                {
                    try
                    {
                        if (iconUrl.StartsWith("data:"))
                        {
                            // 处理Base64编码的图标
                            await LoadIconFromBase64Async(image, iconUrl);
                        }
                        else
                        {
                            // 处理普通URI
                            await LoadIconFromUriAsync(image, new Uri(iconUrl));
                        }
                    }
                    catch (Exception ex)
                    {
                        Timber.Log(LoggerLevel.Error, ex, "Error loading icon image on UI thread");
                        image.Source = null;
                    }
                });
            }
            catch (Exception ex)
            {
                Timber.Log(LoggerLevel.Error, ex, "Error dispatching icon load");
            }
        }
        
        /// <summary>
        /// 从Base64 URL加载图标
        /// </summary>
        private async Task LoadIconFromBase64Async(Image image, string base64Url)
        {
            try
            {
                // 从data: URL中提取Base64数据
                var commaIndex = base64Url.IndexOf(',');
                if (commaIndex == -1)
                {
                    Timber.Log(LoggerLevel.Warn, "Base64 URL does not contain comma separator");
                    return;
                }
                
                var base64Data = base64Url.Substring(commaIndex + 1);
                var bytes = Convert.FromBase64String(base64Data);
                
                // 创建IBuffer
                var buffer = Windows.Security.Cryptography.CryptographicBuffer.CreateFromByteArray(bytes);
                
                // 在UI线程上创建并设置BitmapImage
                using (var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream())
                {
                    await stream.WriteAsync(buffer);
                    await stream.FlushAsync();
                    stream.Seek(0);
                    
                    var bitmap = new BitmapImage();
                    await bitmap.SetSourceAsync(stream);
                    image.Source = bitmap;
                }
            }
            catch (Exception ex)
            {
                Timber.Log(LoggerLevel.Error, ex, "Error loading icon from Base64");
                image.Source = null;
            }
        }
        
        /// <summary>
        /// 从URI加载图标
        /// </summary>
        private async Task LoadIconFromUriAsync(Image image, Uri uri)
        {
            try
            {
                var bitmap = new BitmapImage();
                await bitmap.SetSourceAsync(await Windows.Storage.Streams.RandomAccessStreamReference.CreateFromUri(uri).OpenReadAsync());
                image.Source = bitmap;
            }
            catch (Exception ex)
            {
                Timber.Log(LoggerLevel.Error, ex, "Error loading icon from URI");
                image.Source = null;
            }
        }
    
        // Native methods for playing sound
        private static class NativeMethods
        {
            [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
            public static extern bool PlaySound(string pszSound, IntPtr hmod, uint fdwSound);
        }
        
        [Flags]
        private enum SoundFlags : uint
        {
            SND_ALIAS = 0x00010000,
            SND_ASYNC = 0x0001,
            SND_NODEFAULT = 0x0002
        }

        public async Task RemoveNotification(NotificationModel notification)
        {
            await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                Notifications.Remove(notification);
            });
        }

        public async Task ClearNotifications()
        {
            await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                Notifications.Clear();

                foreach (var toastEntry in _internalToasts.ToList())
                {
                    RemoveToastEntry(toastEntry);
                }

                _pendingExpandQueue.Clear();
            });
        }

        public async Task SetMediaSessionStatus(bool hasSession)
        {
            try
            {
                // 使用Window.Current.Dispatcher而不是CoreApplication.MainView.CoreWindow.Dispatcher，避免跨ASTA线程调用问题
                await Windows.ApplicationModel.Core.CoreApplication.MainView.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    HasMediaSession = hasSession;
                });
            }
            catch (Exception ex)
            {
                Timber.Log(LoggerLevel.Error, ex, "Error setting media session status");
            }
        }

        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}