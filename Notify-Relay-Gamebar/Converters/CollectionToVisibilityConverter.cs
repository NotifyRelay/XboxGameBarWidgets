using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Markup;

namespace NotifyRelayGamebar.Converters
{
    public class CollectionToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool isEmpty = true;

            if (value is IEnumerable enumerable)
            {
                isEmpty = !enumerable.GetEnumerator().MoveNext();
            } 
            else
            {
                isEmpty = value == null;
            }

            if (bool.TryParse(parameter?.ToString(), out bool result) && result)
            {
                isEmpty = !isEmpty;
            }

            return isEmpty ? Visibility.Collapsed : Visibility.Visible;
        }

        internal static object Convert(object value, Type targetType)
        {
            if (targetType.IsInstanceOfType(value))
            {
                return value;
            }
            else
            {
                return XamlBindingHelper.ConvertValue(targetType, value);
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
