using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace NotifyRelayGamebar.Models
{
    public class SuperIslandViewModel : DependencyObject
    {
        private SuperIslandTimerInfo _timerInfo;
        private int? _fixedProgress;
        private string _lastImageUrl;

        public string SourceId
        {
            get { return (string)GetValue(SourceIdProperty); }
            set { SetValue(SourceIdProperty, value); }
        }

        public static readonly DependencyProperty SourceIdProperty =
            DependencyProperty.Register("SourceId", typeof(string), typeof(SuperIslandViewModel), new PropertyMetadata(string.Empty));

        public string DeviceId
        {
            get { return (string)GetValue(DeviceIdProperty); }
            set { SetValue(DeviceIdProperty, value); }
        }

        public static readonly DependencyProperty DeviceIdProperty =
            DependencyProperty.Register("DeviceId", typeof(string), typeof(SuperIslandViewModel), new PropertyMetadata(string.Empty));

        public string DeviceName
        {
            get { return (string)GetValue(DeviceNameProperty); }
            set { SetValue(DeviceNameProperty, value); }
        }

        public static readonly DependencyProperty DeviceNameProperty =
            DependencyProperty.Register("DeviceName", typeof(string), typeof(SuperIslandViewModel), new PropertyMetadata(string.Empty));

        public string Title
        {
            get { return (string)GetValue(TitleProperty); }
            set { SetValue(TitleProperty, value); }
        }

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register("Title", typeof(string), typeof(SuperIslandViewModel), new PropertyMetadata(string.Empty));

        public string SubTitle
        {
            get { return (string)GetValue(SubTitleProperty); }
            set { SetValue(SubTitleProperty, value); }
        }

        public static readonly DependencyProperty SubTitleProperty =
            DependencyProperty.Register("SubTitle", typeof(string), typeof(SuperIslandViewModel), new PropertyMetadata(string.Empty));

        public string Extra
        {
            get { return (string)GetValue(ExtraProperty); }
            set { SetValue(ExtraProperty, value); }
        }

        public static readonly DependencyProperty ExtraProperty =
            DependencyProperty.Register("Extra", typeof(string), typeof(SuperIslandViewModel), new PropertyMetadata(string.Empty));

        public bool HasExtra
        {
            get { return (bool)GetValue(HasExtraProperty); }
            set { SetValue(HasExtraProperty, value); }
        }

        public static readonly DependencyProperty HasExtraProperty =
            DependencyProperty.Register("HasExtra", typeof(bool), typeof(SuperIslandViewModel), new PropertyMetadata(false));

        public string TimerText
        {
            get { return (string)GetValue(TimerTextProperty); }
            set { SetValue(TimerTextProperty, value); }
        }

        public static readonly DependencyProperty TimerTextProperty =
            DependencyProperty.Register("TimerText", typeof(string), typeof(SuperIslandViewModel), new PropertyMetadata(string.Empty));

        public bool HasTimer
        {
            get { return (bool)GetValue(HasTimerProperty); }
            set { SetValue(HasTimerProperty, value); }
        }

        public static readonly DependencyProperty HasTimerProperty =
            DependencyProperty.Register("HasTimer", typeof(bool), typeof(SuperIslandViewModel), new PropertyMetadata(false));

        public double ProgressPercent
        {
            get { return (double)GetValue(ProgressPercentProperty); }
            set { SetValue(ProgressPercentProperty, value); }
        }

        public static readonly DependencyProperty ProgressPercentProperty =
            DependencyProperty.Register("ProgressPercent", typeof(double), typeof(SuperIslandViewModel), new PropertyMetadata(0.0));

        public bool HasProgress
        {
            get { return (bool)GetValue(HasProgressProperty); }
            set { SetValue(HasProgressProperty, value); }
        }

        public static readonly DependencyProperty HasProgressProperty =
            DependencyProperty.Register("HasProgress", typeof(bool), typeof(SuperIslandViewModel), new PropertyMetadata(false));

        public ImageSource ImageSource
        {
            get { return (ImageSource)GetValue(ImageSourceProperty); }
            set { SetValue(ImageSourceProperty, value); }
        }

        public static readonly DependencyProperty ImageSourceProperty =
            DependencyProperty.Register("ImageSource", typeof(ImageSource), typeof(SuperIslandViewModel), new PropertyMetadata(null));

        public bool HasImage
        {
            get { return (bool)GetValue(HasImageProperty); }
            set { SetValue(HasImageProperty, value); }
        }

        public static readonly DependencyProperty HasImageProperty =
            DependencyProperty.Register("HasImage", typeof(bool), typeof(SuperIslandViewModel), new PropertyMetadata(false));

        public bool IsExpanded
        {
            get { return (bool)GetValue(IsExpandedProperty); }
            set { SetValue(IsExpandedProperty, value); }
        }

        public static readonly DependencyProperty IsExpandedProperty =
            DependencyProperty.Register("IsExpanded", typeof(bool), typeof(SuperIslandViewModel), new PropertyMetadata(false));

        public bool NeedsTimerUpdate => _timerInfo != null;

        public void UpdateFromState(SuperIslandState state)
        {
            var parsed = SuperIslandParamV2Parser.Parse(state?.ParamV2Raw);
            Title = FirstNotEmpty(parsed?.Title, state?.Title) ?? string.Empty;
            SubTitle = FirstNotEmpty(parsed?.SubTitle, state?.Text) ?? string.Empty;
            Extra = parsed?.Extra ?? string.Empty;
            HasExtra = !string.IsNullOrWhiteSpace(Extra);

            _timerInfo = parsed?.TimerInfo;
            _fixedProgress = parsed?.ProgressPercent;

            if (_fixedProgress.HasValue)
            {
                ProgressPercent = _fixedProgress.Value;
                HasProgress = true;
            }
            else
            {
                HasProgress = false;
            }

            UpdateTimer(DateTimeOffset.Now);
        }

        public void UpdateTimer(DateTimeOffset now)
        {
            if (_timerInfo == null)
            {
                HasTimer = false;
                TimerText = string.Empty;
                if (!_fixedProgress.HasValue)
                {
                    HasProgress = false;
                }
                return;
            }

            var nowMs = now.ToUnixTimeMilliseconds();
            var paused = _timerInfo.TimerType == -2 || _timerInfo.TimerType == 2;
            var currentValue = _timerInfo.TimerWhen;
            if (!paused)
            {
                var delta = Math.Max(0, nowMs - _timerInfo.TimerSystemCurrent);
                currentValue += delta;
            }

            var isCountdown = _timerInfo.TimerType < 0;
            var displayMs = isCountdown
                ? Math.Max(_timerInfo.TimerTotal - currentValue, 0)
                : Math.Max(currentValue, 0);

            TimerText = FormatTime(displayMs);
            HasTimer = true;

            if (!_fixedProgress.HasValue && _timerInfo.TimerTotal > 0)
            {
                var progress = isCountdown
                    ? (double)displayMs / _timerInfo.TimerTotal
                    : Math.Min(currentValue, _timerInfo.TimerTotal) / (double)_timerInfo.TimerTotal;

                ProgressPercent = Math.Max(0, Math.Min(100, progress * 100.0));
                HasProgress = true;
            }
        }

        public async Task UpdateImageAsync(Dictionary<string, string> pics)
        {
            var url = ResolveImageUrl(pics);
            if (string.IsNullOrWhiteSpace(url))
            {
                ImageSource = null;
                HasImage = false;
                _lastImageUrl = null;
                return;
            }

            if (string.Equals(_lastImageUrl, url, StringComparison.Ordinal))
            {
                HasImage = ImageSource != null;
                return;
            }

            _lastImageUrl = url;
            var image = await LoadImageAsync(url);
            ImageSource = image;
            HasImage = image != null;
        }

        private static string ResolveImageUrl(Dictionary<string, string> pics)
        {
            if (pics == null || pics.Count == 0)
            {
                return null;
            }

            foreach (var kvp in pics)
            {
                if (!string.IsNullOrWhiteSpace(kvp.Value))
                {
                    return kvp.Value;
                }
            }

            return null;
        }

        private static async Task<ImageSource> LoadImageAsync(string url)
        {
            try
            {
                if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    var commaIndex = url.IndexOf(',');
                    if (commaIndex <= 0)
                    {
                        return null;
                    }

                    var base64Data = url.Substring(commaIndex + 1);
                    var bytes = Convert.FromBase64String(base64Data);
                    using (var stream = new InMemoryRandomAccessStream())
                    {
                        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
                        {
                            writer.WriteBytes(bytes);
                            await writer.StoreAsync();
                        }
                        stream.Seek(0);
                        var bmp = new BitmapImage();
                        await bmp.SetSourceAsync(stream);
                        return bmp;
                    }
                }

                return new BitmapImage(new Uri(url));
            }
            catch
            {
                return null;
            }
        }

        private static string FormatTime(long milliseconds)
        {
            var totalSeconds = (int)Math.Max(0, milliseconds / 1000);
            var hours = totalSeconds / 3600;
            var minutes = (totalSeconds % 3600) / 60;
            var seconds = totalSeconds % 60;
            if (hours > 0)
            {
                return string.Format("{0}:{1:00}:{2:00}", hours, minutes, seconds);
            }

            return string.Format("{0:00}:{1:00}", minutes, seconds);
        }

        private static string FirstNotEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }
    }
}
