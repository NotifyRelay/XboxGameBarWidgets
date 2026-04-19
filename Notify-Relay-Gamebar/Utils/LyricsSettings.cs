using System;
using Windows.Storage;

namespace NotifyRelayGamebar.Utils
{
    public static class LyricsSettings
    {
        private const string KeyOnlineEnabled = "LyricsOnlineEnabled";
        private const string KeyDelayMs = "LyricsDelayMs";
        private const string KeySourcePriority = "LyricsSourcePriority";

        public static event EventHandler SettingsChanged;

        public static bool OnlineLyricsEnabled
        {
            get => ReadBool(KeyOnlineEnabled, true);
            set
            {
                if (value == OnlineLyricsEnabled)
                {
                    return;
                }

                WriteBool(KeyOnlineEnabled, value);
                RaiseSettingsChanged();
            }
        }

        public static int LyricsDelayMs
        {
            get => ReadInt(KeyDelayMs, 0);
            set
            {
                if (value == LyricsDelayMs)
                {
                    return;
                }

                WriteInt(KeyDelayMs, value);
                RaiseSettingsChanged();
            }
        }

        public static string LyricsSourcePriority
        {
            get => ReadString(KeySourcePriority, "163");
            set
            {
                var normalized = string.IsNullOrWhiteSpace(value) ? "163" : value;
                if (string.Equals(normalized, LyricsSourcePriority, StringComparison.Ordinal))
                {
                    return;
                }

                WriteString(KeySourcePriority, normalized);
                RaiseSettingsChanged();
            }
        }

        private static void RaiseSettingsChanged()
        {
            SettingsChanged?.Invoke(null, EventArgs.Empty);
        }

        private static ApplicationDataContainer LocalSettings => ApplicationData.Current.LocalSettings;

        private static bool ReadBool(string key, bool defaultValue)
        {
            if (LocalSettings.Values.TryGetValue(key, out var value) && value is bool boolValue)
            {
                return boolValue;
            }

            return defaultValue;
        }

        private static int ReadInt(string key, int defaultValue)
        {
            if (LocalSettings.Values.TryGetValue(key, out var value))
            {
                if (value is int intValue)
                {
                    return intValue;
                }

                if (value is long longValue)
                {
                    return (int)longValue;
                }

                if (value is string stringValue && int.TryParse(stringValue, out var parsed))
                {
                    return parsed;
                }
            }

            return defaultValue;
        }

        private static string ReadString(string key, string defaultValue)
        {
            if (LocalSettings.Values.TryGetValue(key, out var value) && value is string stringValue)
            {
                return stringValue;
            }

            return defaultValue;
        }

        private static void WriteBool(string key, bool value)
        {
            LocalSettings.Values[key] = value;
        }

        private static void WriteInt(string key, int value)
        {
            LocalSettings.Values[key] = value;
        }

        private static void WriteString(string key, string value)
        {
            LocalSettings.Values[key] = value;
        }
    }
}
