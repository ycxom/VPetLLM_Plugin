namespace RemoteChatPlugin;

public sealed class RemoteChatSettings
{
    /// <summary>true = 使用 VPetLLM 官方聊天服务器；false = 使用自部署地址。首次安装默认官方。</summary>
    public bool UseOfficialServer { get; set; } = true;

    /// <summary>自部署模式下用户填写的中继地址（官方模式忽略）。</summary>
    public string ServerUrl { get; set; } = "";

    /// <summary>自部署模式下用户填写的网页地址（官方模式忽略）。</summary>
    public string WebUrl { get; set; } = "";
    public string RoomId { get; set; } = "";
    public string ProtectedSecret { get; set; } = "";
    public int MaxMessageLength { get; set; } = 4000;

    /// <summary>是否允许远端浏览器回看本地近期聊天历史（隐私项，默认关闭）。</summary>
    public bool ShareHistory { get; set; } = false;

    /// <summary>历史同步时回传的最大条数。</summary>
    public int HistoryMaxMessages { get; set; } = 40;
}
