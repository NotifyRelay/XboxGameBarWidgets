using NotifyRelayGamebar.Models;
using NotifyRelayGamebar.Utils;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace NotifyRelayGamebar
{
    public sealed partial class WidgetSettings : Page
    {
        public LyricsSettingsViewModel LyricsSettingsViewModel { get; } = new LyricsSettingsViewModel();

        public WidgetSettings()
        {
            InitializeComponent();
            Loaded += WidgetSettings_Loaded;
            TimberLog.Timber.Log(TimberLog.LoggerLevel.Info, "WidgetSettings: ctor");
        }

        private void WidgetSettings_Loaded(object sender, RoutedEventArgs e)
        {
            TimberLog.Timber.Log(TimberLog.LoggerLevel.Info, "WidgetSettings: Loaded");
            LyricsSettingsViewModel.IsOnlineLyricsEnabled = LyricsSettings.OnlineLyricsEnabled;
            LyricsSettingsViewModel.LyricsDelayMs = LyricsSettings.LyricsDelayMs;
            LyricsSettingsViewModel.LyricsSourcePriority = LyricsSettings.LyricsSourcePriority;
        }
    }
}
