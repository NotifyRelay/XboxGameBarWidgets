using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.UI.Xaml.Data;

namespace NotifyRelayGamebar
{
    public class SliderDurationTooltipConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is double totalMillis)
            {
                var timeSpan = TimeSpan.FromMilliseconds(totalMillis);

                var sb = new StringBuilder();

                if (timeSpan.TotalHours >= 1)
                {
                    sb.Append(timeSpan.ToString("hh"));
                }
                if (sb.Length > 0)
                {
                    sb.Append(":");
                }
                sb.Append(timeSpan.ToString("mm':'ss"));

                return sb.ToString();
            }

            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
