using NotifyRelayGamebar.Models;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
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

        public NotificationViewModel()
        {
            Notifications = new ObservableCollection<NotificationModel>();
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
                        Border container = new Border();
                        container.CornerRadius = new CornerRadius(6, 6, 6, 6);
                        container.Margin = new Thickness(6, 4, 6, 4);
                        container.Padding = new Thickness(8);
                        container.HorizontalAlignment = HorizontalAlignment.Stretch;
                        // 添加白色描边，使其与背景有明显区分
                        container.BorderThickness = new Thickness(2);
                        container.BorderBrush = new SolidColorBrush(Windows.UI.Colors.White);
                        if (Application.Current.RequestedTheme == ApplicationTheme.Dark)
                        {
                            container.Background = new SolidColorBrush(Color.FromArgb(255, 38, 38, 38));
                        }
                        else
                        {
                            container.Background = new SolidColorBrush(Color.FromArgb(255, 219, 219, 219));
                        }

                        StackPanel vertical = new StackPanel();
                        vertical.Orientation = Orientation.Vertical;
                        vertical.HorizontalAlignment = HorizontalAlignment.Stretch;

                        TextBlock sourceLine = new TextBlock();
                        if (!string.IsNullOrEmpty(notification.AppName))
                        {
                            sourceLine.Text = "来自 " + notification.AppName;
                        }
                        else
                        {
                            sourceLine.Text = "来自 未知应用";
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

                        container.Child = vertical;

                        // append to ToastStack (later notifications appear below)
                        _toastStack.Children.Add(container);

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
                        var currentContainer = container;

                        Task.Run(async () =>
                        {
                            await Task.Delay(4000);
                            await currentDispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                            {
                                try
                                {
                                    currentToastStack.Children.Remove(currentContainer);
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