using System;
using NotifyRelayGamebar.Utils;
using Windows.UI.Xaml;

namespace NotifyRelayGamebar.Models
{
    public class LyricsSettingsViewModel : DependencyObject
    {
        public LyricsSettingsViewModel()
        {
            IsOnlineLyricsEnabled = LyricsSettings.OnlineLyricsEnabled;
            LyricsDelayMs = LyricsSettings.LyricsDelayMs;
            LyricsSourcePriority = LyricsSettings.LyricsSourcePriority;
        }

        public bool IsOnlineLyricsEnabled
        {
            get => (bool)GetValue(IsOnlineLyricsEnabledProperty);
            set => SetValue(IsOnlineLyricsEnabledProperty, value);
        }

        public static readonly DependencyProperty IsOnlineLyricsEnabledProperty =
            DependencyProperty.Register(
                nameof(IsOnlineLyricsEnabled),
                typeof(bool),
                typeof(LyricsSettingsViewModel),
                new PropertyMetadata(true, OnIsOnlineLyricsEnabledChanged));

        public double LyricsDelayMs
        {
            get => (double)GetValue(LyricsDelayMsProperty);
            set => SetValue(LyricsDelayMsProperty, value);
        }

        public static readonly DependencyProperty LyricsDelayMsProperty =
            DependencyProperty.Register(
                nameof(LyricsDelayMs),
                typeof(double),
                typeof(LyricsSettingsViewModel),
                new PropertyMetadata(0d, OnLyricsDelayMsChanged));

        public string LyricsSourcePriority
        {
            get => (string)GetValue(LyricsSourcePriorityProperty);
            set => SetValue(LyricsSourcePriorityProperty, value);
        }

        public static readonly DependencyProperty LyricsSourcePriorityProperty =
            DependencyProperty.Register(
                nameof(LyricsSourcePriority),
                typeof(string),
                typeof(LyricsSettingsViewModel),
                new PropertyMetadata("163", OnLyricsSourcePriorityChanged));

        private static void OnIsOnlineLyricsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is bool value)
            {
                LyricsSettings.OnlineLyricsEnabled = value;
            }
        }

        private static void OnLyricsDelayMsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is double value)
            {
                LyricsSettings.LyricsDelayMs = (int)Math.Round(value);
            }
        }

        private static void OnLyricsSourcePriorityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is string value)
            {
                LyricsSettings.LyricsSourcePriority = value;
            }
        }
    }
}
