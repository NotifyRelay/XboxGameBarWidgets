using NPSMLib;
using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace NotifyRelayGamebar.Models
{
    public class MediaSessionViewModel : DependencyObject
    {
        public string SessionId
        {
            get { return (string)GetValue(SessionIdProperty); }
            set { SetValue(SessionIdProperty, value); }
        }

        public static readonly DependencyProperty SessionIdProperty =
            DependencyProperty.Register("SessionId", typeof(string), typeof(MediaSessionViewModel), new PropertyMetadata(string.Empty));

        public string DeviceId
        {
            get { return (string)GetValue(DeviceIdProperty); }
            set { SetValue(DeviceIdProperty, value); }
        }

        public static readonly DependencyProperty DeviceIdProperty =
            DependencyProperty.Register("DeviceId", typeof(string), typeof(MediaSessionViewModel), new PropertyMetadata(string.Empty));

        public string DeviceName
        {
            get { return (string)GetValue(DeviceNameProperty); }
            set { SetValue(DeviceNameProperty, value); }
        }

        public static readonly DependencyProperty DeviceNameProperty =
            DependencyProperty.Register("DeviceName", typeof(string), typeof(MediaSessionViewModel), new PropertyMetadata(string.Empty));

        public string Title
        {
            get { return (string)GetValue(TitleProperty); }
            set { SetValue(TitleProperty, value); }
        }

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register("Title", typeof(string), typeof(MediaSessionViewModel), new PropertyMetadata(string.Empty));

        public string Artist
        {
            get { return (string)GetValue(ArtistProperty); }
            set { SetValue(ArtistProperty, value); }
        }

        public static readonly DependencyProperty ArtistProperty =
            DependencyProperty.Register("Artist", typeof(string), typeof(MediaSessionViewModel), new PropertyMetadata(string.Empty));

        public string Album
        {
            get { return (string)GetValue(AlbumProperty); }
            set { SetValue(AlbumProperty, value); }
        }

        public static readonly DependencyProperty AlbumProperty =
            DependencyProperty.Register("Album", typeof(string), typeof(MediaSessionViewModel), new PropertyMetadata(string.Empty));

        public ImageSource ThumbnailImageSource
        {
            get { return (ImageSource)GetValue(ThumbnailImageSourceProperty); }
            set { SetValue(ThumbnailImageSourceProperty, value); }
        }

        public static readonly DependencyProperty ThumbnailImageSourceProperty =
            DependencyProperty.Register("ThumbnailImageSource", typeof(ImageSource), typeof(MediaSessionViewModel), new PropertyMetadata(null));

        public bool IsShuffleActive
        {
            get { return (bool)GetValue(IsShuffleActiveProperty); }
            set { SetValue(IsShuffleActiveProperty, value); }
        }

        public static readonly DependencyProperty IsShuffleActiveProperty =
            DependencyProperty.Register("IsShuffleActive", typeof(bool), typeof(MediaSessionViewModel), new PropertyMetadata(false));

        public MediaPlaybackRepeatMode AutoRepeatMode
        {
            get { return (MediaPlaybackRepeatMode)GetValue(AutoRepeatModeProperty); }
            set { SetValue(AutoRepeatModeProperty, value); }
        }

        public static readonly DependencyProperty AutoRepeatModeProperty =
            DependencyProperty.Register("AutoRepeatMode", typeof(MediaPlaybackRepeatMode), typeof(MediaSessionViewModel), new PropertyMetadata(MediaPlaybackRepeatMode.None));

        public bool IsPlaying
        {
            get { return (bool)GetValue(IsPlayingProperty); }
            set { SetValue(IsPlayingProperty, value); }
        }

        public static readonly DependencyProperty IsPlayingProperty =
            DependencyProperty.Register("IsPlaying", typeof(bool), typeof(MediaSessionViewModel), new PropertyMetadata(false));

        public bool IsShuffleEnabled
        {
            get { return (bool)GetValue(IsShuffleEnabledProperty); }
            set { SetValue(IsShuffleEnabledProperty, value); }
        }

        public static readonly DependencyProperty IsShuffleEnabledProperty =
            DependencyProperty.Register("IsShuffleEnabled", typeof(bool), typeof(MediaSessionViewModel), new PropertyMetadata(false));

        public bool IsRepeatEnabled
        {
            get { return (bool)GetValue(IsRepeatEnabledProperty); }
            set { SetValue(IsRepeatEnabledProperty, value); }
        }

        public static readonly DependencyProperty IsRepeatEnabledProperty =
            DependencyProperty.Register("IsRepeatEnabled", typeof(bool), typeof(MediaSessionViewModel), new PropertyMetadata(false));

        public bool IsPreviousEnabled
        {
            get { return (bool)GetValue(IsPreviousEnabledProperty); }
            set { SetValue(IsPreviousEnabledProperty, value); }
        }

        public static readonly DependencyProperty IsPreviousEnabledProperty =
            DependencyProperty.Register("IsPreviousEnabled", typeof(bool), typeof(MediaSessionViewModel), new PropertyMetadata(false));

        public bool IsNextEnabled
        {
            get { return (bool)GetValue(IsNextEnabledProperty); }
            set { SetValue(IsNextEnabledProperty, value); }
        }

        public static readonly DependencyProperty IsNextEnabledProperty =
            DependencyProperty.Register("IsNextEnabled", typeof(bool), typeof(MediaSessionViewModel), new PropertyMetadata(false));

        public bool IsPlayPauseEnabled
        {
            get { return (bool)GetValue(IsPlayPauseEnabledProperty); }
            set { SetValue(IsPlayPauseEnabledProperty, value); }
        }

        public static readonly DependencyProperty IsPlayPauseEnabledProperty =
            DependencyProperty.Register("IsPlayPauseEnabled", typeof(bool), typeof(MediaSessionViewModel), new PropertyMetadata(false));

        public bool IsRemoteSession
        {
            get { return (bool)GetValue(IsRemoteSessionProperty); }
            set { SetValue(IsRemoteSessionProperty, value); }
        }

        public static readonly DependencyProperty IsRemoteSessionProperty =
            DependencyProperty.Register("IsRemoteSession", typeof(bool), typeof(MediaSessionViewModel), new PropertyMetadata(false));

        public async Task UpdateThumbnailFromUrl(string url)
        {
            if (!string.IsNullOrEmpty(url))
            {
                try
                {
                    // Check if it is a data URL
                    if (url.StartsWith("data:image"))
                    {
                        // Handle data URL (base64)
                        var commaIndex = url.IndexOf(',');
                        if (commaIndex != -1)
                        {
                            var base64Data = url.Substring(commaIndex + 1);
                            var bytes = Convert.FromBase64String(base64Data);
                            using (var stream = new InMemoryRandomAccessStream())
                            {
                                using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
                                {
                                    writer.WriteBytes(bytes);
                                    await writer.StoreAsync();
                                }
                                await UpdateThumbnail(stream.AsStream());
                            }
                        }
                    }
                    else
                    {
                        // Handle regular URL
                        BitmapImage bmpImage = new BitmapImage(new Uri(url));
                        ThumbnailImageSource = bmpImage;
                    }
                }
                catch
                {
                    ThumbnailImageSource = null;
                }
            }
            else
            {
                ThumbnailImageSource = null;
            }
        }

        public async Task UpdateThumbnail(Stream thumbnailStream)
        {
            if (thumbnailStream != null)
            {
                try
                {
                    BitmapImage bmpImage = new()
                    {
                        DecodePixelWidth = 75,
                        CreateOptions = BitmapCreateOptions.None,
                    };
                    await bmpImage.SetSourceAsync(thumbnailStream.AsRandomAccessStream());
                    ThumbnailImageSource = bmpImage;
                }
                catch
                {
                    ThumbnailImageSource = null;
                }
            } else
            {
                ThumbnailImageSource = null;
            }
        }

        public void UpdateFromRemoteSession(RemoteMediaSession session)
        {
            if (session != null)
            {
                DeviceId = session.DeviceId;
                Title = session.Title;
                Artist = session.Artist;
                Album = "";
                IsPlaying = session.IsPlaying;
                IsPlayPauseEnabled = true;
                IsPreviousEnabled = true;
                IsNextEnabled = true;
                IsShuffleEnabled = false;
                IsRepeatEnabled = false;
                IsShuffleActive = false;
                AutoRepeatMode = MediaPlaybackRepeatMode.None;
                IsRemoteSession = true;
            }
        }
    }
}
