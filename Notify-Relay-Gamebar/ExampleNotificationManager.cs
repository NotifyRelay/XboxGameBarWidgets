using Microsoft.Gaming.XboxGameBar;
using NPSMLib;
using System;
using System.Collections.Generic;
using TimberLog;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace NotifyRelayGamebar
{
    public class ExampleNotificationManager
    {
        private XboxGameBarWidget _widget;
        private StackPanel _toastStack;
        private Func<IList<NowPlayingSession>> _getMediaSessions;
        
        // 示例通知管理
        private bool _showingExampleNotifications = false;
        private StackPanel _mediaBlockExample;
        private StackPanel _notificationExample1;
        private StackPanel _notificationExample2;

        public ExampleNotificationManager(
            XboxGameBarWidget widget,
            StackPanel toastStack,
            Func<IList<NowPlayingSession>> getMediaSessions)
        {
            _widget = widget;
            _toastStack = toastStack;
            _getMediaSessions = getMediaSessions;
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
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(1, 1, 0, 0),
                Foreground = new SolidColorBrush(Windows.UI.Colors.Black)
            };
            var titleBlock = new TextBlock()
            {
                Text = "媒体控制示例",
                FontWeight = FontWeights.Bold,
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
        private StackPanel CreateExampleNotification(string title, string content, string appName, string deviceName = null)
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
            var sourceText = string.IsNullOrWhiteSpace(deviceName)
                ? "来自" + appName
                : "来自" + deviceName + "的" + appName;
            var sourceLine = new TextBlock()
            {
                Text = sourceText,
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
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(1, 1, 0, 0),
                Foreground = new SolidColorBrush(Windows.UI.Colors.Black)
            };
            var titleBlock = new TextBlock()
            {
                Text = title,
                FontWeight = FontWeights.Bold,
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
        public void ShowExampleNotifications()
        {
            if (_showingExampleNotifications || _toastStack == null)
                return;

            try
            {
                // 根据GameBar显示模式决定显示哪些示例
                bool isGameBarOpen = _widget != null && _widget.GameBarDisplayMode == XboxGameBarDisplayMode.Foreground;
                bool hasNoMediaSession = (_getMediaSessions?.Invoke()?.Count ?? 0) == 0;

                // 显示媒体块示例（当没有实际媒体数据且处于展示模式下显示）
                if (hasNoMediaSession && isGameBarOpen)
                {
                    _mediaBlockExample = CreateMediaBlockExample();
                    _toastStack.Children.Insert(0, _mediaBlockExample);
                }

                // 显示通知示例：与媒体块显示条件保持一致（除了是否有媒体数据）
                // 即：当处于展示模式下显示
                bool shouldShowNotifications = isGameBarOpen;
                if (shouldShowNotifications)
                {
                    _notificationExample1 = CreateExampleNotification("通知示例1", "这是第一条示例通知，用于展示通知的外观和样式。", "示例应用", "示例设备");
                    _notificationExample2 = CreateExampleNotification("通知示例2", "这是第二条示例通知，演示了多行文本的显示效果。", "示例应用", "示例设备");
                    
                    _toastStack.Children.Add(_notificationExample1);
                    _toastStack.Children.Add(_notificationExample2);
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
        public void HideExampleNotifications()
        {
            if (!_showingExampleNotifications || _toastStack == null)
                return;

            try
            {
                // 移除媒体块示例
                if (_mediaBlockExample != null && _toastStack.Children.Contains(_mediaBlockExample))
                {
                    _toastStack.Children.Remove(_mediaBlockExample);
                    _mediaBlockExample = null;
                }

                // 移除通知示例
                if (_notificationExample1 != null && _toastStack.Children.Contains(_notificationExample1))
                {
                    _toastStack.Children.Remove(_notificationExample1);
                    _notificationExample1 = null;
                }

                if (_notificationExample2 != null && _toastStack.Children.Contains(_notificationExample2))
                {
                    _toastStack.Children.Remove(_notificationExample2);
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
        public void UpdateExampleNotifications()
        {
            if (_widget == null)
                return;

            bool isGameBarOpen = _widget.GameBarDisplayMode == XboxGameBarDisplayMode.Foreground;
            bool isPinned = _widget.Pinned;
            bool hasNoMediaSession = (_getMediaSessions?.Invoke()?.Count ?? 0) == 0;

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
                
                _toastStack.Children.Add(_notificationExample1);
                _toastStack.Children.Add(_notificationExample2);
                _showingExampleNotifications = true;
                return;
            }

            // 焦点且非固定（GameBar打开且未固定）：显示媒体块示例（当没有实际媒体数据时）
            if (hasNoMediaSession)
            {
                _mediaBlockExample = CreateMediaBlockExample();
                _toastStack.Children.Insert(0, _mediaBlockExample);
                _showingExampleNotifications = true;
            }
            // 焦点且非固定：不显示通知示例
        }
    }
}