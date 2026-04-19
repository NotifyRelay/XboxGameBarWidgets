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
using Windows.ApplicationModel.Core;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
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
    
        public StackPanel ToastStack
        {
            set 
            {
                _toastStack = value;
                // 获取并保存UI线程的调度器
                if (value != null)
                {
                    _uiDispatcher = value.Dispatcher;
                    Timber.Log(LoggerLevel.Info, "ToastStack set, Dispatcher: {0}", _uiDispatcher != null ? "NotNull" : "Null");
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

                        // 使用Grid和两个重叠的TextBlock实现描边效果
                        Grid bodyGrid = new Grid();
                        bodyGrid.HorizontalAlignment = HorizontalAlignment.Stretch;
                        
                        // 底层描边文本
                        TextBlock bodyStroke = new TextBlock();
                        bodyStroke.Text = notification.Body;
                        bodyStroke.TextWrapping = TextWrapping.Wrap;
                        bodyStroke.HorizontalAlignment = HorizontalAlignment.Stretch;
                        bodyStroke.Margin = new Thickness(1, 1, 0, 0);
                        bodyStroke.Foreground = Application.Current.RequestedTheme == ApplicationTheme.Dark ? 
                            new SolidColorBrush(Windows.UI.Colors.Black) : 
                            new SolidColorBrush(Windows.UI.Colors.White);
                        
                        // 上层主文本
                        TextBlock bodyBlock = new TextBlock();
                        bodyBlock.Text = notification.Body;
                        bodyBlock.TextWrapping = TextWrapping.Wrap;
                        bodyBlock.HorizontalAlignment = HorizontalAlignment.Stretch;
                        bodyBlock.Foreground = Application.Current.RequestedTheme == ApplicationTheme.Dark ? 
                            new SolidColorBrush(Windows.UI.Colors.White) : 
                            new SolidColorBrush(Windows.UI.Colors.Black);
                        
                        bodyGrid.Children.Add(bodyStroke);
                        bodyGrid.Children.Add(bodyBlock);

                        Grid.SetRow(img, 0);
                        Grid.SetColumn(img, 0);
                        Grid.SetRowSpan(img, 2);
                        Grid.SetRow(titleGrid, 0);
                        Grid.SetColumn(titleGrid, 1);
                        Grid.SetRow(bodyGrid, 1);
                        Grid.SetColumn(bodyGrid, 1);

                        grid.Children.Add(img);
                        grid.Children.Add(titleGrid);
                        grid.Children.Add(bodyGrid);

                        vertical.Children.Add(sourceLine);
                        vertical.Children.Add(grid);

                        // 将内容添加到内层边框，再将内层边框添加到外层边框
                        innerBorder.Child = vertical;
                        container.Child = innerBorder;

                        // append to ToastStack (later notifications appear below)
                        _toastStack.Children.Add(shadowGrid);

                        // 在UI线程上触发图标加载（LoadIconImageAsync 内部会再次使用 _uiDispatcher）
                        if (!string.IsNullOrEmpty(notification.IconUrl))
                        {
                            LoadIconImageAsync(img, notification.IconUrl);
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

                        // 启动自动隐藏计时（计时后通过 dispatcher 移除）
                        var currentDispatcher = _uiDispatcher;
                        var currentToastStack = _toastStack;
                        var currentShadowGrid = shadowGrid;

                        Task.Run(async () =>
                        {
                            await Task.Delay(4000);
                            await currentDispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                            {
                                try
                                {
                                    currentToastStack.Children.Remove(currentShadowGrid);
                                }
                                catch (Exception ex)
                                {
                                    Timber.Log(LoggerLevel.Error, ex, "Error removing internal toast");
                                }
                            });
                        });
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