using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace OneBotPlugin
{
    public enum OneBotNodeType
    {
        ForwardWS,
        ReverseWS,
        HttpApi
    }

    public class OneBotNodeSetting
    {
        public string Name { get; set; } = "OneBot节点";
        public bool Enabled { get; set; } = true;
        public OneBotNodeType Type { get; set; } = OneBotNodeType.ForwardWS;

        public string? Url { get; set; }
        public string? Host { get; set; }
        public int? Port { get; set; }
        public string? AccessToken { get; set; }

        [JsonIgnore]
        public string? DisplayUrlOrHost => Type == OneBotNodeType.ReverseWS
            ? $"{Host ?? "127.0.0.1"}:{Port ?? 8080}"
            : Url;

        [JsonIgnore]
        public string TypeDisplay => Type switch
        {
            OneBotNodeType.ForwardWS => "正向 WS",
            OneBotNodeType.ReverseWS => "反向 WS",
            OneBotNodeType.HttpApi => "HTTP API",
            _ => Type.ToString()
        };
    }

    public class OneBotSettings
    {
        public List<OneBotNodeSetting> Nodes { get; set; } = new List<OneBotNodeSetting>();

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

        public List<OneBotNodeSetting> GetEnabledNodes()
        {
            return Nodes.Where(n => n.Enabled).ToList();
        }
    }
}
