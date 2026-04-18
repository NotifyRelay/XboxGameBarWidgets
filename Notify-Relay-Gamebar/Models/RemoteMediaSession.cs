using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage.Streams;

namespace NotifyRelayGamebar.Models
{
    public class RemoteMediaSession
    {
        public string DeviceId { get; set; }
        public string DeviceName { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public string CoverUrl { get; set; }
        public bool IsPlaying { get; set; }
        public DateTime LastUpdateTime { get; set; }
        public DateTime LastTitleUpdateTime { get; set; }

        public void Update(string title, string artist, string coverUrl, bool isPlaying)
        {
            Title = title;
            Artist = artist;
            CoverUrl = coverUrl;
            IsPlaying = isPlaying;
            LastUpdateTime = DateTime.Now;
        }
    }
}