using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace NotifyRelayGamebar
{
    public class ShuffleButtonTemplateSelector : DataTemplateSelector
    {
        protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
        {
            if (item is bool value && value)
            {
                return Application.Current.Resources["ShuffleOnTemplate"] as DataTemplate;
            }
            else
            {
                return Application.Current.Resources["ShuffleOffTemplate"] as DataTemplate;
            }
        }
    }
}
