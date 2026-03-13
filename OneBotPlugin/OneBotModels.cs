using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OneBotPlugin
{
    #region OneBot Events (received from OneBot implementation)

    public class OneBotEvent
    {
        [JsonProperty("time")]
        public long Time { get; set; }

        [JsonProperty("self_id")]
        public long SelfId { get; set; }

        [JsonProperty("post_type")]
        public string PostType { get; set; } = "";

        [JsonProperty("message_type")]
        public string? MessageType { get; set; }

        [JsonProperty("sub_type")]
        public string? SubType { get; set; }

        [JsonProperty("message_id")]
        public long MessageId { get; set; }

        [JsonProperty("user_id")]
        public long UserId { get; set; }

        [JsonProperty("group_id")]
        public long? GroupId { get; set; }

        [JsonProperty("message")]
        public JToken? Message { get; set; }

        [JsonProperty("raw_message")]
        public string? RawMessage { get; set; }

        [JsonProperty("sender")]
        public OneBotSender? Sender { get; set; }

        [JsonProperty("meta_event_type")]
        public string? MetaEventType { get; set; }

        [JsonProperty("notice_type")]
        public string? NoticeType { get; set; }
    }

    public class OneBotSender
    {
        [JsonProperty("user_id")]
        public long UserId { get; set; }

        [JsonProperty("nickname")]
        public string Nickname { get; set; } = "";

        [JsonProperty("card")]
        public string? Card { get; set; }

        [JsonProperty("role")]
        public string? Role { get; set; }
    }

    public class OneBotMessageSegment
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "";

        [JsonProperty("data")]
        public JObject? Data { get; set; }
    }

    #endregion

    #region OneBot Actions (sent to OneBot implementation)

    public class OneBotActionRequest
    {
        [JsonProperty("action")]
        public string Action { get; set; } = "";

        [JsonProperty("params")]
        public object? Params { get; set; }

        [JsonProperty("echo")]
        public string? Echo { get; set; }
    }

    public class OneBotActionResponse
    {
        [JsonProperty("status")]
        public string Status { get; set; } = "";

        [JsonProperty("retcode")]
        public int RetCode { get; set; }

        [JsonProperty("data")]
        public JToken? Data { get; set; }

        [JsonProperty("echo")]
        public string? Echo { get; set; }

        public bool IsOk => Status == "ok" || RetCode == 0;
    }

    public class SendMsgParams
    {
        [JsonProperty("message_type")]
        public string? MessageType { get; set; }

        [JsonProperty("user_id")]
        public long? UserId { get; set; }

        [JsonProperty("group_id")]
        public long? GroupId { get; set; }

        [JsonProperty("message")]
        public object Message { get; set; } = "";

        [JsonProperty("auto_escape")]
        public bool AutoEscape { get; set; } = false;
    }

    public class GetLoginInfoResult
    {
        [JsonProperty("user_id")]
        public long UserId { get; set; }

        [JsonProperty("nickname")]
        public string Nickname { get; set; } = "";
    }

    #endregion

    #region Plugin Action Arguments (from LLM)

    public class OneBotSendArgs
    {
        [JsonProperty("user_id")]
        public long? UserId { get; set; }

        [JsonProperty("group_id")]
        public long? GroupId { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; } = "";

        [JsonProperty("image")]
        public string? Image { get; set; }
    }

    #endregion

    #region Internal Message Context

    public class ForwardTarget
    {
        public long? GroupId { get; set; }
        public long? UserId { get; set; }
    }

    public class OneBotMessageContext
    {
        public long UserId { get; set; }
        public long? GroupId { get; set; }
        public string SenderName { get; set; } = "";
        public string Text { get; set; } = "";
        public long MessageId { get; set; }
        public bool IsGroup => GroupId.HasValue && GroupId.Value > 0;
    }

    #endregion
}
