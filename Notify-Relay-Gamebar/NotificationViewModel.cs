using NotifyRelayGamebar.Models;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.UI.Core;
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

        public async Task AddNotification(NotificationModel notification)
        {
            await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                Notifications.Insert(0, notification);
                
                // 限制通知数量，只保留最近的10条
                if (Notifications.Count > 10)
                {
                    Notifications.RemoveAt(Notifications.Count - 1);
                }
            });
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
            await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                HasMediaSession = hasSession;
            });
        }

        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}