using System;
using Windows.Data.Json;

namespace NotifyRelayGamebar.Models
{
    public sealed class SuperIslandParsedInfo
    {
        public string Title { get; set; }
        public string SubTitle { get; set; }
        public string Extra { get; set; }
        public SuperIslandTimerInfo TimerInfo { get; set; }
        public int? ProgressPercent { get; set; }
        public string ProgressTitle { get; set; }
    }

    public static class SuperIslandParamV2Parser
    {
        public static SuperIslandParsedInfo Parse(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            if (!JsonObject.TryParse(raw, out var root))
            {
                return null;
            }

            var info = new SuperIslandParsedInfo();
            var highlight = GetObject(root, "highlightInfo");
            var chat = GetObject(root, "chatInfo");
            var baseInfo = GetObject(root, "baseInfo");
            var hintInfo = GetObject(root, "hintInfo");
            var progressInfo = GetObject(root, "progressInfo");
            var multiProgressInfo = GetObject(root, "multiProgressInfo");

            if (highlight != null)
            {
                info.Title = GetString(highlight, "title");
                info.SubTitle = GetString(highlight, "content") ?? GetString(highlight, "subContent");
                info.TimerInfo = ParseTimerInfo(highlight);
            }

            if (chat != null)
            {
                info.Title ??= GetString(chat, "title");
                info.SubTitle ??= GetString(chat, "content");
                info.TimerInfo ??= ParseTimerInfo(chat);
            }

            if (baseInfo != null)
            {
                info.Title ??= GetString(baseInfo, "title") ?? GetString(baseInfo, "subTitle");
                info.SubTitle ??= GetString(baseInfo, "content") ?? GetString(baseInfo, "subContent");
                info.Extra ??= GetString(baseInfo, "extraTitle") ?? GetString(baseInfo, "specialTitle");
            }

            if (hintInfo != null)
            {
                info.TimerInfo ??= ParseTimerInfo(hintInfo);
            }

            if (multiProgressInfo != null)
            {
                var progress = GetInt(multiProgressInfo, "progress");
                if (progress.HasValue && progress.Value >= 0 && progress.Value <= 100)
                {
                    info.ProgressPercent = progress.Value;
                    info.ProgressTitle = GetString(multiProgressInfo, "title");
                }
            }

            if (info.ProgressPercent == null && progressInfo != null)
            {
                var progress = GetInt(progressInfo, "progress");
                if (progress.HasValue && progress.Value >= 0 && progress.Value <= 100)
                {
                    info.ProgressPercent = progress.Value;
                }
            }

            return info;
        }

        private static JsonObject GetObject(JsonObject root, string key)
        {
            if (root.TryGetValue(key, out var value) && value.ValueType == JsonValueType.Object)
            {
                return value.GetObject();
            }
            return null;
        }

        private static string GetString(JsonObject root, string key)
        {
            if (root.TryGetValue(key, out var value) && value.ValueType == JsonValueType.String)
            {
                return value.GetString();
            }
            return null;
        }

        private static int? GetInt(JsonObject root, string key)
        {
            if (root.TryGetValue(key, out var value) && value.ValueType == JsonValueType.Number)
            {
                return (int)value.GetNumber();
            }
            return null;
        }

        private static SuperIslandTimerInfo ParseTimerInfo(JsonObject root)
        {
            var timerObj = GetObject(root, "timerInfo");
            if (timerObj == null)
            {
                return null;
            }

            if (!timerObj.TryGetValue("timerType", out var typeValue) || typeValue.ValueType != JsonValueType.Number)
            {
                return null;
            }

            return new SuperIslandTimerInfo
            {
                TimerType = (int)typeValue.GetNumber(),
                TimerWhen = GetLong(timerObj, "timerWhen"),
                TimerTotal = GetLong(timerObj, "timerTotal"),
                TimerSystemCurrent = GetLong(timerObj, "timerSystemCurrent")
            };
        }

        private static long GetLong(JsonObject root, string key)
        {
            if (root.TryGetValue(key, out var value) && value.ValueType == JsonValueType.Number)
            {
                return (long)value.GetNumber();
            }
            return 0;
        }
    }
}
