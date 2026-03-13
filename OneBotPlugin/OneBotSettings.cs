using Newtonsoft.Json;

namespace OneBotPlugin
{
    public class OneBotSettings
    {
        public bool EnableForwardWS { get; set; } = false;
        public string ForwardWSUrl { get; set; } = "ws://127.0.0.1:3001";
        public string AccessToken { get; set; } = "";

        public bool EnableReverseWS { get; set; } = false;
        public string ReverseWSHost { get; set; } = "127.0.0.1";
        public int ReverseWSPort { get; set; } = 8080;

        public bool EnableHttpApi { get; set; } = false;
        public string HttpApiUrl { get; set; } = "http://127.0.0.1:3000";

        public HashSet<long> AllowedUsers { get; set; } = new();
        public HashSet<long> AllowedGroups { get; set; } = new();
        public bool AllowAllPrivate { get; set; } = false;
        public bool AllowAllGroup { get; set; } = false;

        public bool ReplyWithAt { get; set; } = true;
        public bool AtTriggerOnly { get; set; } = true;
        public string BotQQ { get; set; } = "";
        public string MasterQQ { get; set; } = "";
        public int MaxMessageLength { get; set; } = 4000;
        public int MessageQueueInterval { get; set; } = 1000;
    }
}
