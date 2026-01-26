using NPSMLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace NotifyRelayGamebar
{
    public class PlayerViewModel : DependencyObject
    {
        public bool SessionsAvailable
        {
            get { return (bool)GetValue(SessionsAvailableProperty); }
            set { SetValue(SessionsAvailableProperty, value); }
        }

        // Using a DependencyProperty as the backing store for SessionsAvailable.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty SessionsAvailableProperty =
            DependencyProperty.Register("SessionsAvailable", typeof(bool), typeof(PlayerViewModel), new PropertyMetadata(false));

        public bool ShowPreviousSession
        {
            get { return (bool)GetValue(ShowPreviousSessionProperty); }
            set { SetValue(ShowPreviousSessionProperty, value); }
        }

        // Using a DependencyProperty as the backing store for ShowPreviousSession.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty ShowPreviousSessionProperty =
            DependencyProperty.Register("ShowPreviousSession", typeof(bool), typeof(PlayerViewModel), new PropertyMetadata(false));

        public bool ShowNextSession
        {
            get { return (bool)GetValue(ShowNextSessionProperty); }
            set { SetValue(ShowNextSessionProperty, value); }
        }

        // Using a DependencyProperty as the backing store for ShowNextSession.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty ShowNextSessionProperty =
            DependencyProperty.Register("ShowNextSession", typeof(bool), typeof(PlayerViewModel), new PropertyMetadata(false));

        public string Title
        {
            get { return (string)GetValue(TitleProperty); }
            set { SetValue(TitleProperty, value); }
        }

        // Using a DependencyProperty as the backing store for Title.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register("Title", typeof(string), typeof(PlayerViewModel), new PropertyMetadata(""));

        public string Artist
        {
            get { return (string)GetValue(ArtistProperty); }
            set { SetValue(ArtistProperty, value); }
        }

        // Using a DependencyProperty as the backing store for Artist.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty ArtistProperty =
            DependencyProperty.Register("Artist", typeof(string), typeof(PlayerViewModel), new PropertyMetadata(""));

        public string Album
        {
            get { return (string)GetValue(AlbumProperty); }
            set { SetValue(AlbumProperty, value); }
        }

        // Using a DependencyProperty as the backing store for Album.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty AlbumProperty =
            DependencyProperty.Register("Album", typeof(string), typeof(PlayerViewModel), new PropertyMetadata(""));

        public ImageSource ThumbnailImageSource
        {
            get { return (ImageSource)GetValue(ThumbnailImageSourceProperty); }
            set { SetValue(ThumbnailImageSourceProperty, value); }
        }

        // Using a DependencyProperty as the backing store for ThumbnailImageSource.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty ThumbnailImageSourceProperty =
            DependencyProperty.Register("ThumbnailImageSource", typeof(ImageSource), typeof(PlayerViewModel), new PropertyMetadata(null));

        public bool IsShuffleActive
        {
            get { return (bool)GetValue(IsShuffleActiveProperty); }
            set { SetValue(IsShuffleActiveProperty, value); }
        }

        // Using a DependencyProperty as the backing store for IsShuffleActive.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty IsShuffleActiveProperty =
            DependencyProperty.Register("IsShuffleActive", typeof(bool), typeof(PlayerViewModel), new PropertyMetadata(false));

        public MediaPlaybackRepeatMode AutoRepeatMode
        {
            get { return (MediaPlaybackRepeatMode)GetValue(AutoRepeatModeProperty); }
            set { SetValue(AutoRepeatModeProperty, value); }
        }

        // Using a DependencyProperty as the backing store for AutoRepeatMode.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty AutoRepeatModeProperty =
            DependencyProperty.Register("AutoRepeatMode", typeof(MediaPlaybackRepeatMode), typeof(PlayerViewModel), new PropertyMetadata(MediaPlaybackRepeatMode.None));

        public bool IsPlaying
        {
            get { return (bool)GetValue(IsPlayingProperty); }
            set { SetValue(IsPlayingProperty, value); }
        }

        // Using a DependencyProperty as the backing store for IsPlaying.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty IsPlayingProperty =
            DependencyProperty.Register("IsPlaying", typeof(bool), typeof(PlayerViewModel), new PropertyMetadata(false));

        public bool IsShuffleEnabled
        {
            get { return (bool)GetValue(IsShuffleEnabledProperty); }
            set { SetValue(IsShuffleEnabledProperty, value); }
        }

        // Using a DependencyProperty as the backing store for IsShuffleEnabled.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty IsShuffleEnabledProperty =
            DependencyProperty.Register("IsShuffleEnabled", typeof(bool), typeof(PlayerViewModel), new PropertyMetadata(false));

        public bool IsRepeatEnabled
        {
            get { return (bool)GetValue(IsRepeatEnabledProperty); }
            set { SetValue(IsRepeatEnabledProperty, value); }
        }

        // Using a DependencyProperty as the backing store for IsRepeatEnabled.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty IsRepeatEnabledProperty =
            DependencyProperty.Register("IsRepeatEnabled", typeof(bool), typeof(PlayerViewModel), new PropertyMetadata(false));

        public bool IsPreviousEnabled
        {
            get { return (bool)GetValue(IsPreviousEnabledProperty); }
            set { SetValue(IsPreviousEnabledProperty, value); }
        }

        // Using a DependencyProperty as the backing store for IsPreviousEnabled.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty IsPreviousEnabledProperty =
            DependencyProperty.Register("IsPreviousEnabled", typeof(bool), typeof(PlayerViewModel), new PropertyMetadata(false));

        public bool IsNextEnabled
        {
            get { return (bool)GetValue(IsNextEnabledProperty); }
            set { SetValue(IsNextEnabledProperty, value); }
        }

        // Using a DependencyProperty as the backing store for IsNextEnabled.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty IsNextEnabledProperty =
            DependencyProperty.Register("IsNextEnabled", typeof(bool), typeof(PlayerViewModel), new PropertyMetadata(false));

        public bool IsPlayPauseEnabled
        {
            get { return (bool)GetValue(IsPlayPauseEnabledProperty); }
            set { SetValue(IsPlayPauseEnabledProperty, value); }
        }

        // Using a DependencyProperty as the backing store for IsPlayPauseEnabled.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty IsPlayPauseEnabledProperty =
            DependencyProperty.Register("IsPlayPauseEnabled", typeof(bool), typeof(PlayerViewModel), new PropertyMetadata(false));

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
    }
}
