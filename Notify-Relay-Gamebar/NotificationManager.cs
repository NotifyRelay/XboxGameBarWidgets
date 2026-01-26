using System;
using TimberLog;
using Windows.UI.Xaml.Controls;

namespace NotifyRelayGamebar
{
    public class NotificationManager
    {
        private NotificationService _notificationService;
        public NotificationService NotificationService => _notificationService;
        private NotificationViewModel _notificationViewModel;
        private StackPanel _toastStack;

        public NotificationManager(NotificationViewModel notificationViewModel, StackPanel toastStack)
        {
            _notificationViewModel = notificationViewModel;
            _toastStack = toastStack;
        }

        public void Initialize()
        {
            _notificationService = new NotificationService(_notificationViewModel);
            _notificationService.ErrorOccurred += NotificationService_ErrorOccurred;
            
            // 将ToastStack传递给NotificationViewModel
            _notificationViewModel.ToastStack = _toastStack;
        }

        public async void StartNotificationService()
        {
            try
            {
                await _notificationService.StartAsync();
                Timber.Log(LoggerLevel.Info, "Notification service started");
            }
            catch (Exception ex)
            {
                Timber.Log(LoggerLevel.Error, ex, "Failed to start notification service");
            }
        }

        public void StopNotificationService()
        {
            try
            {
                _notificationService.Stop();
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
    }
}