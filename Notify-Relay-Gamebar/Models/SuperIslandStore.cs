using System.Collections.Generic;
using Windows.Data.Json;

namespace NotifyRelayGamebar.Models
{
    public static class SuperIslandStore
    {
        private const string TerminateValue = "__END__";
        private static Dictionary<string, SuperIslandState> Store = new Dictionary<string, SuperIslandState>();
        private static readonly object StoreLock = new object();

        public static SuperIslandState ApplyIncoming(string sourceId, JsonObject payload)
        {
            if (payload == null)
            {
                return null;
            }

            sourceId = sourceId?.Trim();
            if (string.IsNullOrWhiteSpace(sourceId))
            {
                return null;
            }

            lock (StoreLock)
            {
                var store = EnsureStore();

                var terminateValue = GetString(payload, "terminateValue");
                if (terminateValue == TerminateValue)
                {
                    store.Remove(sourceId);
                    return null;
                }

                if (payload.TryGetValue("changes", out var changesValue) && changesValue.ValueType == JsonValueType.Object)
                {
                    var oldState = store.TryGetValue(sourceId, out var existing) ? existing : null;
                    var merged = ApplyDelta(oldState, changesValue.GetObject());
                    if (merged != null)
                    {
                        store[sourceId] = merged;
                    }
                    return merged;
                }

                var state = ParseStateFromFull(payload);
                if (state == null)
                {
                    return null;
                }

                store[sourceId] = state;
                return state;
            }
        }

        public static bool RemoveExact(string sourceId)
        {
            lock (StoreLock)
            {
                return EnsureStore().Remove(sourceId);
            }
        }

        private static Dictionary<string, SuperIslandState> EnsureStore()
        {
            if (Store == null)
            {
                Store = new Dictionary<string, SuperIslandState>();
            }
            return Store;
        }

        private static SuperIslandState ParseStateFromFull(JsonObject payload)
        {
            return new SuperIslandState
            {
                Title = GetString(payload, "title"),
                Text = GetString(payload, "text"),
                ParamV2Raw = GetString(payload, "param_v2_raw"),
                Pics = ParsePics(payload)
            };
        }

        private static SuperIslandState ApplyDelta(SuperIslandState oldState, JsonObject changes)
        {
            var state = oldState ?? new SuperIslandState
            {
                Pics = new Dictionary<string, string>()
            };

            if (changes.TryGetValue("title", out var titleValue))
            {
                state.Title = titleValue.ValueType == JsonValueType.String ? titleValue.GetString() : state.Title;
            }

            if (changes.TryGetValue("text", out var textValue))
            {
                state.Text = textValue.ValueType == JsonValueType.String ? textValue.GetString() : state.Text;
            }

            if (changes.TryGetValue("param_v2_raw", out var paramValue))
            {
                state.ParamV2Raw = paramValue.ValueType == JsonValueType.String ? paramValue.GetString() : state.ParamV2Raw;
            }

            if (state.Pics == null)
            {
                state.Pics = new Dictionary<string, string>();
            }

            if (changes.TryGetValue("pics", out var picsValue) && picsValue.ValueType == JsonValueType.Object)
            {
                foreach (var kvp in picsValue.GetObject())
                {
                    if (kvp.Value.ValueType == JsonValueType.String)
                    {
                        var url = kvp.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(url))
                        {
                            state.Pics[kvp.Key] = url;
                        }
                    }
                }
            }

            if (changes.TryGetValue("pics_removed", out var removedValue) && removedValue.ValueType == JsonValueType.Array)
            {
                foreach (var item in removedValue.GetArray())
                {
                    if (item.ValueType == JsonValueType.String)
                    {
                        var key = item.GetString();
                        if (!string.IsNullOrWhiteSpace(key))
                        {
                            state.Pics.Remove(key);
                        }
                    }
                }
            }

            return state;
        }

        private static Dictionary<string, string> ParsePics(JsonObject payload)
        {
            if (!payload.TryGetValue("pics", out var picsValue) || picsValue.ValueType != JsonValueType.Object)
            {
                return null;
            }

            var pics = new Dictionary<string, string>();
            foreach (var kvp in picsValue.GetObject())
            {
                if (kvp.Value.ValueType == JsonValueType.String)
                {
                    var url = kvp.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        pics[kvp.Key] = url;
                    }
                }
            }

            return pics.Count > 0 ? pics : null;
        }

        private static string GetString(JsonObject payload, string key)
        {
            if (payload.TryGetValue(key, out var value) && value.ValueType == JsonValueType.String)
            {
                return value.GetString();
            }
            return null;
        }
    }
}
