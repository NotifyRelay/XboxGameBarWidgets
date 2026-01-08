using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NotifyRelayGamebar
{
    public static class BindingExtensions
    {
        public static bool IsFalse(bool? enabled)
        {
            return enabled.HasValue && !enabled.Value;
        }

        public static bool IsTrue(bool? enabled)
        {
            return enabled.HasValue && enabled.Value;
        }

        public static bool IsNull(bool? value)
        {
            return !value.HasValue;
        }
    }
}
