using NPSMLib;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace NotifyRelayGamebar
{
    public class RepeatButtonTemplateSelector : DataTemplateSelector
    {
        protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
        {
            if (item is MediaPlaybackRepeatMode value)
            {
                return value switch
                {
                    MediaPlaybackRepeatMode.Track => Application.Current.Resources["RepeatTrackTemplate"],
                    MediaPlaybackRepeatMode.List => Application.Current.Resources["RepeatListTemplate"],
                    _ => Application.Current.Resources["RepeatNoneTemplate"],
                } as DataTemplate;
            }

            return Application.Current.Resources["RepeatNoneTemplate"] as DataTemplate;
        }
    }
}
