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
        public string Business { get; set; }
        public string PicFunction { get; set; }
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
            
            info.Business = GetString(root, "business");

            JsonObject highlight = null;
            JsonObject chat = null;
            JsonObject baseInfo = null;
            JsonObject hintInfo = null;
            JsonObject progressInfo = null;
            JsonObject multiProgressInfo = null;
            JsonObject animTextInfo = null;
            JsonObject picInfo = null;

            try { highlight = GetObject(root, "highlightInfo"); } catch { }
            try { chat = GetObject(root, "chatInfo"); } catch { }
            try { baseInfo = GetObject(root, "baseInfo"); } catch { }
            try { hintInfo = GetObject(root, "hintInfo"); } catch { }
            try { progressInfo = GetObject(root, "progressInfo"); } catch { }
            try { multiProgressInfo = GetObject(root, "multiProgressInfo"); } catch { }
            try { animTextInfo = GetObject(root, "animTextInfo"); } catch { }
            try { picInfo = GetObject(root, "picInfo"); } catch { }

            if (highlight == null)
            {
                highlight = ParseHighlightFromIconText(root);
            }

            if (highlight != null)
            {
                info.Title = GetString(highlight, "title");
                info.SubTitle = GetString(highlight, "content") ?? GetString(highlight, "subContent");
                info.TimerInfo = ParseTimerInfo(highlight);
                info.PicFunction = GetString(highlight, "picFunction");
            }

            if (animTextInfo != null)
            {
                info.Title ??= GetString(animTextInfo, "title");
                info.SubTitle ??= GetString(animTextInfo, "content");
                info.TimerInfo ??= ParseTimerInfo(animTextInfo);
            }

            if (picInfo != null)
            {
                info.Title ??= GetString(picInfo, "title");
                info.SubTitle ??= GetString(picInfo, "content") ?? GetString(picInfo, "subContent");
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
                info.Title ??= GetString(hintInfo, "title") ?? GetString(hintInfo, "subTitle");
                info.SubTitle ??= GetString(hintInfo, "content") ?? GetString(hintInfo, "subContent");
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

                if (info.ProgressPercent == null && !string.IsNullOrWhiteSpace(info.ProgressTitle))
                {
                    var title = GetString(baseInfo, "title");
                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        info.ProgressTitle = title;
                    }
                }
            }

            return info;
        }

        private static JsonObject ParseHighlightFromIconText(JsonObject root)
        {
            var iconText = GetObject(root, "iconTextInfo");
            if (iconText == null)
            {
                return null;
            }

            var title = GetString(iconText, "title");
            var content = GetString(iconText, "content");
            string subContent = null;
            
            foreach (var key in new[] { "subTitle", "tip", "desc", "description" })
            {
                subContent = GetString(iconText, key);
                if (!string.IsNullOrWhiteSpace(subContent))
                {
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(content) && string.IsNullOrWhiteSpace(subContent))
            {
                return null;
            }

            var highlight = new JsonObject();
            if (!string.IsNullOrWhiteSpace(title))
            {
                highlight.Add("title", JsonValue.CreateStringValue(title));
            }
            if (!string.IsNullOrWhiteSpace(content))
            {
                highlight.Add("content", JsonValue.CreateStringValue(content));
            }
            if (!string.IsNullOrWhiteSpace(subContent))
            {
                highlight.Add("subContent", JsonValue.CreateStringValue(subContent));
            }

            var animIcon = GetObject(iconText, "animIconInfo");
            if (animIcon != null)
            {
                var iconKey = GetString(animIcon, "src");
                if (!string.IsNullOrWhiteSpace(iconKey))
                {
                    highlight.Add("picFunction", JsonValue.CreateStringValue(iconKey));
                }
            }

            return highlight;
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
