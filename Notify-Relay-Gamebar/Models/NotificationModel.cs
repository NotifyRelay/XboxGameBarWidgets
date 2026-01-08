using System;
using Windows.UI.Xaml.Media.Imaging;

namespace NotifyRelayGamebar.Models
{
    public class NotificationModel
    {
        public string AppName { get; set; }
        public string Title { get; set; }
        public string Body { get; set; }
        public Uri IconUri { get; set; }
        public BitmapImage IconImage { get; set; }
        public DateTime ReceivedTime { get; set; }
        public bool IsMediaNotification { get; set; }
    }
}