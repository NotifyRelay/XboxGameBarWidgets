using System.Collections.Generic;

namespace NotifyRelayGamebar.Models
{
    public sealed class SuperIslandState
    {
        public string Title { get; set; }
        public string Text { get; set; }
        public string ParamV2Raw { get; set; }
        public Dictionary<string, string> Pics { get; set; }
    }
}
