using System;
using System.Diagnostics;
using System.Globalization;
using TimberLog;

namespace NotifyRelayGamebar.Utils
{
    public class VsOutputLoggingTree : TimberLog.Timber.Tree
    {
        private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

        protected override void Log(LoggerLevel loggerLevel, string message, Exception exception)
        {
            var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", Culture);
            var level = loggerLevel.ToString().ToUpper(Culture);

            if (exception != null)
            {
                Debug.WriteLine($"{timestamp}|{level}|{message} Exception={exception}");
            }
            else
            {
                Debug.WriteLine($"{timestamp}|{level}|{message}");
            }
        }
    }
}
