using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading.Tasks;
using Windows.Data.Json;

namespace NotifyRelayGamebar.Utils
{
    public sealed class LyricLine
    {
        public long TimeMs { get; }
        public string Text { get; }

        public LyricLine(long timeMs, string text)
        {
            TimeMs = timeMs;
            Text = text ?? string.Empty;
        }
    }

    public static class LyricsService
    {
        private const string NetEaseUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36";
        private const string LrcLibUserAgent = "NotifyRelayGamebar/1.0";
        private static readonly HttpClient HttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        private static readonly Dictionary<string, IReadOnlyList<LyricLine>> Cache = new Dictionary<string, IReadOnlyList<LyricLine>>(StringComparer.Ordinal);
        private static readonly object CacheLock = new object();

        public const string DefaultSource = "163";

        public static string BuildCacheKey(string title, string artist, int durationSeconds)
        {
            var safeTitle = (title ?? string.Empty).Trim();
            var safeArtist = (artist ?? string.Empty).Trim();
            return string.Format(CultureInfo.InvariantCulture, "{0}\u001F{1}\u001F{2}", safeTitle, safeArtist, durationSeconds);
        }

        public static async Task<IReadOnlyList<LyricLine>> FetchLyricsAsync(
            string title,
            string artist,
            int durationSeconds,
            string source,
            bool fallback)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            var key = BuildCacheKey(title, artist, durationSeconds);
            lock (CacheLock)
            {
                if (Cache.TryGetValue(key, out var cached))
                {
                    return cached;
                }
            }

            IReadOnlyList<LyricLine> result = null;
            if (string.Equals(source, "lrclib", StringComparison.OrdinalIgnoreCase))
            {
                result = await FetchLyricsFromLrcLibAsync(title, artist, durationSeconds).ConfigureAwait(false);
                if (result == null && fallback)
                {
                    result = await FetchLyricsFromNetEaseAsync(title, artist).ConfigureAwait(false);
                }
            }
            else
            {
                result = await FetchLyricsFromNetEaseAsync(title, artist).ConfigureAwait(false);
                if (result == null && fallback)
                {
                    result = await FetchLyricsFromLrcLibAsync(title, artist, durationSeconds).ConfigureAwait(false);
                }
            }

            lock (CacheLock)
            {
                Cache[key] = result;
            }

            return result;
        }

        public static string GetCurrentLine(IReadOnlyList<LyricLine> lines, long positionMs, int delayMs)
        {
            if (lines == null || lines.Count == 0)
            {
                return null;
            }

            var current = positionMs + delayMs;
            if (current < 0)
            {
                current = 0;
            }

            int low = 0;
            int high = lines.Count - 1;
            int match = -1;
            while (low <= high)
            {
                int mid = low + ((high - low) / 2);
                var time = lines[mid].TimeMs;
                if (time <= current)
                {
                    match = mid;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }

            return match >= 0 ? lines[match].Text : null;
        }

        private static async Task<IReadOnlyList<LyricLine>> FetchLyricsFromNetEaseAsync(string title, string artist)
        {
            var result = await FetchLyricsFromNetEaseInnerAsync(title, artist).ConfigureAwait(false);
            if (result != null)
            {
                return result;
            }

            return await FetchLyricsFromNetEaseInnerAsync(title, string.Empty).ConfigureAwait(false);
        }

        private static async Task<IReadOnlyList<LyricLine>> FetchLyricsFromNetEaseInnerAsync(string title, string artist)
        {
            var query = string.IsNullOrWhiteSpace(artist) ? title : $"{title} {artist}";
            var url = $"https://music.163.com/api/search/get/web?s={UrlEncode(query)}&type=1&offset=0&total=true&limit=10";

            var searchJson = await GetJsonObjectAsync(url, NetEaseUserAgent).ConfigureAwait(false);
            if (searchJson == null)
            {
                return null;
            }

            var resultObject = GetObject(searchJson, "result");
            var songsArray = GetArray(resultObject, "songs");
            if (songsArray == null || songsArray.Count == 0)
            {
                return null;
            }

            var artistLower = (artist ?? string.Empty).Trim().ToLowerInvariant();
            long songId = 0;

            if (!string.IsNullOrEmpty(artistLower))
            {
                foreach (var songValue in songsArray)
                {
                    if (songValue.ValueType != JsonValueType.Object) continue;
                    var song = songValue.GetObject();
                    var artistsArray = GetArray(song, "artists");
                    if (artistsArray == null) continue;

                    foreach (var artistValue in artistsArray)
                    {
                        if (artistValue.ValueType != JsonValueType.Object) continue;
                        var artistObject = artistValue.GetObject();
                        var name = GetString(artistObject, "name");
                        if (string.Equals(name, artistLower, StringComparison.OrdinalIgnoreCase))
                        {
                            songId = (long)GetNumber(song, "id");
                            break;
                        }
                    }

                    if (songId != 0)
                    {
                        break;
                    }
                }
            }

            if (songId == 0)
            {
                var firstSong = songsArray[0].GetObject();
                songId = (long)GetNumber(firstSong, "id");
            }

            if (songId == 0)
            {
                return null;
            }

            var lyricUrl = $"https://music.163.com/api/song/lyric?id={songId}&lv=1&kv=1&tv=-1";
            var lyricJson = await GetJsonObjectAsync(lyricUrl, NetEaseUserAgent).ConfigureAwait(false);
            if (lyricJson == null)
            {
                return null;
            }

            var lrcObject = GetObject(lyricJson, "lrc");
            var tlrcObject = GetObject(lyricJson, "tlyric");
            var lrc = GetString(lrcObject, "lyric") ?? string.Empty;
            var tlrc = GetString(tlrcObject, "lyric") ?? string.Empty;

            var lines = ParseLyrics(lrc, tlrc);
            return lines.Count == 0 ? null : lines;
        }

        private static async Task<IReadOnlyList<LyricLine>> FetchLyricsFromLrcLibAsync(string title, string artist, int durationSeconds)
        {
            var result = await FetchLyricsFromLrcLibInnerAsync(title, artist, durationSeconds).ConfigureAwait(false);
            if (result != null)
            {
                return result;
            }

            return await FetchLyricsFromLrcLibSearchAsync(title, artist).ConfigureAwait(false);
        }

        private static async Task<IReadOnlyList<LyricLine>> FetchLyricsFromLrcLibInnerAsync(string title, string artist, int durationSeconds)
        {
            var url = $"https://lrclib.net/api/get?track_name={UrlEncode(title)}&artist_name={UrlEncode(artist)}&duration={durationSeconds}";
            var json = await GetJsonObjectAsync(url, LrcLibUserAgent).ConfigureAwait(false);
            if (json == null)
            {
                return null;
            }

            var syncedLyrics = GetString(json, "syncedLyrics");
            if (string.IsNullOrWhiteSpace(syncedLyrics))
            {
                return null;
            }

            var lines = ParseLyrics(syncedLyrics, string.Empty);
            return lines.Count == 0 ? null : lines;
        }

        private static async Task<IReadOnlyList<LyricLine>> FetchLyricsFromLrcLibSearchAsync(string title, string artist)
        {
            var query = string.IsNullOrWhiteSpace(artist) ? title : $"{title} {artist}";
            var url = $"https://lrclib.net/api/search?q={UrlEncode(query)}";
            var json = await GetJsonArrayAsync(url, LrcLibUserAgent).ConfigureAwait(false);
            if (json == null)
            {
                return null;
            }

            foreach (var item in json)
            {
                if (item.ValueType != JsonValueType.Object) continue;
                var obj = item.GetObject();
                var syncedLyrics = GetString(obj, "syncedLyrics");
                if (string.IsNullOrWhiteSpace(syncedLyrics))
                {
                    continue;
                }

                var lines = ParseLyrics(syncedLyrics, string.Empty);
                if (lines.Count > 0)
                {
                    return lines;
                }
            }

            return null;
        }

        private static List<LyricLine> ParseLyrics(string lrc, string tlrc)
        {
            var map = new SortedDictionary<long, string>();

            void ProcessContent(string content, bool isMain)
            {
                if (string.IsNullOrEmpty(content))
                {
                    return;
                }

                foreach (var rawLine in content.Split('\n'))
                {
                    var line = rawLine.Trim();
                    if (!line.StartsWith("[", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var parts = line.Split(']');
                    if (parts.Length < 2)
                    {
                        continue;
                    }

                    var text = parts[parts.Length - 1].Trim();
                    if (string.IsNullOrEmpty(text) && !isMain)
                    {
                        continue;
                    }

                    for (int i = 0; i < parts.Length - 1; i++)
                    {
                        var timePart = parts[i].TrimStart('[');
                        var ms = ParseTime(timePart);
                        if (!ms.HasValue)
                        {
                            continue;
                        }

                        if (map.TryGetValue(ms.Value, out var existing))
                        {
                            if (string.IsNullOrEmpty(existing) && !string.IsNullOrEmpty(text))
                            {
                                map[ms.Value] = text;
                            }
                        }
                        else
                        {
                            map[ms.Value] = text;
                        }
                    }
                }
            }

            ProcessContent(lrc, true);
            ProcessContent(tlrc, false);

            var lines = new List<LyricLine>();
            foreach (var item in map)
            {
                lines.Add(new LyricLine(item.Key, item.Value));
            }

            return lines;
        }

        private static long? ParseTime(string timeString)
        {
            if (string.IsNullOrWhiteSpace(timeString))
            {
                return null;
            }

            var parts = timeString.Split(':');
            if (parts.Length < 2)
            {
                return null;
            }

            if (!long.TryParse(parts[0], out var mins))
            {
                return null;
            }

            var rest = parts[1];
            string secsString = rest;
            string msString = null;

            var dotIndex = rest.IndexOf('.');
            if (dotIndex >= 0)
            {
                secsString = rest.Substring(0, dotIndex);
                msString = rest.Substring(dotIndex + 1);
            }
            else
            {
                var colonIndex = rest.IndexOf(':');
                if (colonIndex >= 0)
                {
                    secsString = rest.Substring(0, colonIndex);
                    msString = rest.Substring(colonIndex + 1);
                }
                else if (parts.Length > 2)
                {
                    secsString = parts[1];
                    msString = parts[2];
                }
            }

            if (!long.TryParse(secsString, out var secs))
            {
                return null;
            }

            long ms = 0;
            if (!string.IsNullOrEmpty(msString))
            {
                var digits = StripNonDigits(msString);
                if (digits.Length > 0 && long.TryParse(digits, out var rawMs))
                {
                    ms = rawMs;
                    if (digits.Length == 2) ms *= 10;
                    else if (digits.Length == 1) ms *= 100;
                    else if (digits.Length > 3) ms /= (long)Math.Pow(10, digits.Length - 3);
                }
            }

            return (mins * 60000) + (secs * 1000) + ms;
        }

        private static string StripNonDigits(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            var chars = new char[input.Length];
            var count = 0;
            foreach (var ch in input)
            {
                if (ch >= '0' && ch <= '9')
                {
                    chars[count++] = ch;
                }
            }

            return count == 0 ? string.Empty : new string(chars, 0, count);
        }

        private static async Task<JsonObject> GetJsonObjectAsync(string url, string userAgent)
        {
            var json = await GetStringAsync(url, userAgent).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                return JsonObject.Parse(json);
            }
            catch
            {
                return null;
            }
        }

        private static async Task<JsonArray> GetJsonArrayAsync(string url, string userAgent)
        {
            var json = await GetStringAsync(url, userAgent).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                return JsonArray.Parse(json);
            }
            catch
            {
                return null;
            }
        }

        private static async Task<string> GetStringAsync(string url, string userAgent)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                if (!string.IsNullOrWhiteSpace(userAgent))
                {
                    request.Headers.UserAgent.ParseAdd(userAgent);
                }

                using (var response = await HttpClient.SendAsync(request).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        return null;
                    }

                    return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
            }
        }

        private static JsonObject GetObject(JsonObject obj, string name)
        {
            if (obj == null)
            {
                return null;
            }

            if (obj.TryGetValue(name, out var value) && value.ValueType == JsonValueType.Object)
            {
                return value.GetObject();
            }

            return null;
        }

        private static JsonArray GetArray(JsonObject obj, string name)
        {
            if (obj == null)
            {
                return null;
            }

            if (obj.TryGetValue(name, out var value) && value.ValueType == JsonValueType.Array)
            {
                return value.GetArray();
            }

            return null;
        }

        private static string GetString(JsonObject obj, string name)
        {
            if (obj == null)
            {
                return null;
            }

            if (obj.TryGetValue(name, out var value) && value.ValueType == JsonValueType.String)
            {
                return value.GetString();
            }

            return null;
        }

        private static double GetNumber(JsonObject obj, string name)
        {
            if (obj == null)
            {
                return 0;
            }

            if (obj.TryGetValue(name, out var value) && value.ValueType == JsonValueType.Number)
            {
                return value.GetNumber();
            }

            return 0;
        }

        private static string UrlEncode(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            var output = new System.Text.StringBuilder();
            foreach (var b in System.Text.Encoding.UTF8.GetBytes(input))
            {
                if ((b >= '0' && b <= '9') || (b >= 'a' && b <= 'z') || (b >= 'A' && b <= 'Z') || b == '-' || b == '_' || b == '.' || b == '~')
                {
                    output.Append((char)b);
                }
                else if (b == ' ')
                {
                    output.Append('+');
                }
                else
                {
                    output.Append('%');
                    output.Append(b.ToString("X2", CultureInfo.InvariantCulture));
                }
            }

            return output.ToString();
        }
    }
}
