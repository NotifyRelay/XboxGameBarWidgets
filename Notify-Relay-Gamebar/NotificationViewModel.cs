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
            }
        }
    }

    public async Task AddNotification(NotificationModel notification)
    {
        await Windows.ApplicationModel.Core.CoreApplication.MainView.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
        {
            Notifications.Insert(0, notification);
            
            // 限制通知数量，只保留最近的10条
            if (Notifications.Count > 10)
            {
                Notifications.RemoveAt(Notifications.Count - 1);
            }
            
            // 直接在UI上显示通知
            if (_toastStack != null)
            {
                ShowInternalToast(notification);
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
            // 使用保存的UI线程调度器执行UI操作
            await _uiDispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                try
                {
                    // create a border container for a single toast
                    Border container = new Border();
                    // 明确设置四个角的圆角，确保四角均为圆角
                    container.CornerRadius = new CornerRadius(6, 6, 6, 6);
                    container.Margin = new Thickness(6, 4, 6, 4);
                    container.Padding = new Thickness(8);
                    // 使通知容器横向填满可用空间
                    container.HorizontalAlignment = HorizontalAlignment.Stretch;
                    // 选择背景色（使用系统主题色）
                    container.Background = Application.Current.Resources["ApplicationPageBackgroundThemeBrush"] as SolidColorBrush;
                    
                    // Vertical layout: first line shows source, then a grid with icon on left and title/body on right
                    StackPanel vertical = new StackPanel();
                    vertical.Orientation = Orientation.Vertical;
                    vertical.HorizontalAlignment = HorizontalAlignment.Stretch;
                    
                    // first line: 来源
                    TextBlock sourceLine = new TextBlock();
                    // 使用传入的应用名
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
                    
                    // grid with icon column and text column (two rows)
                    Grid grid = new Grid();
                    // columns
                    ColumnDefinition col0 = new ColumnDefinition();
                    ColumnDefinition col1 = new ColumnDefinition();
                    // first column = Auto (fixed to icon), second = Star (take remaining space)
                    col0.Width = new GridLength(0, GridUnitType.Auto);
                    col1.Width = new GridLength(1, GridUnitType.Star);
                    grid.ColumnDefinitions.Add(col0);
                    grid.ColumnDefinitions.Add(col1);
                    grid.HorizontalAlignment = HorizontalAlignment.Stretch;
                    // rows
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
                    
                    // 设置图标
                    if (notification.IconImage != null)
                    {
                        img.Source = notification.IconImage;
                    }
                    
                    // title (row 0, col1)
                    TextBlock titleBlock = new TextBlock();
                    titleBlock.Text = notification.Title;
                    titleBlock.FontWeight = Windows.UI.Text.FontWeights.Bold;
                    titleBlock.TextWrapping = TextWrapping.Wrap;
                    titleBlock.HorizontalAlignment = HorizontalAlignment.Stretch;
                    // body (row1, col1)
                    TextBlock bodyBlock = new TextBlock();
                    bodyBlock.Text = notification.Body;
                    bodyBlock.TextWrapping = TextWrapping.Wrap;
                    bodyBlock.HorizontalAlignment = HorizontalAlignment.Stretch;
                    
                    // place elements in grid
                    Grid.SetRow(img, 0);
                    Grid.SetColumn(img, 0);
                    Grid.SetRowSpan(img, 2);
                    Grid.SetRow(titleBlock, 0);
                    Grid.SetColumn(titleBlock, 1);
                    Grid.SetRow(bodyBlock, 1);
                    Grid.SetColumn(bodyBlock, 1);
                    
                    grid.Children.Add(img);
                    grid.Children.Add(titleBlock);
                    grid.Children.Add(bodyBlock);
                    
                    vertical.Children.Add(sourceLine);
                    vertical.Children.Add(grid);
                    
                    container.Child = vertical;
                    
                    // append to ToastStack (later notifications appear below)
                    _toastStack.Children.Add(container);
                    
                    // Play notification sound
                    try
                    {
                        // 使用PlaySound播放通知声音
                        // 异步播放系统默认的通知声音，避免干扰其他音频播放
                        NativeMethods.PlaySound("Notification.Default", IntPtr.Zero, (uint)(SoundFlags.SND_ALIAS | SoundFlags.SND_ASYNC | SoundFlags.SND_NODEFAULT));
                    }
                    catch (Exception ex)
                    {
                        Timber.Log(LoggerLevel.Error, ex, "Failed to play notification sound");
                    }
                    
                    // 获取当前的UI调度器，用于自动隐藏操作
                    var currentDispatcher = _uiDispatcher;
                    var currentToastStack = _toastStack;
                    
                    // auto-hide after 4 seconds and then remove the specific child
                    Task.Run(async () =>
                    {
                        await Task.Delay(4000);
                        // 使用当前的UI调度器执行移除操作
                        await currentDispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                        {
                            try
                            {
                                currentToastStack.Children.Remove(container);
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
                    Timber.Log(LoggerLevel.Error, ex, "Error creating internal toast UI");
                }
            });
        }
        catch (Exception ex)
        {
            Timber.Log(LoggerLevel.Error, ex, "Error showing internal toast");
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