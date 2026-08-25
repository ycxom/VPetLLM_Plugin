using VPetLLM.Core.Abstractions.Interfaces.Plugin;
using VPetLLM.Core.Abstractions.Models;

/// <summary>系统信息插件的调用契约。无参数。本插件没有命名空间，与主文件保持一致。</summary>
public partial class SystemInfoPlugin : IToolSchemaPlugin
{
    public ToolSchema? GetToolSchema()
    {
        var lang = _vpetLLM?.Settings.Language ?? "en";

        var (summary, form) = lang switch
        {
            "zh-hans" => ("读取本机的硬件与系统信息", "获取 CPU、内存、系统版本等信息，不需要参数"),
            "zh-hant" => ("讀取本機的硬體與系統資訊", "獲取 CPU、記憶體、系統版本等資訊，不需要參數"),
            "ja" => ("このPCのハードウェアとシステム情報を読む", "CPU・メモリ・OS バージョンなどを取得する（引数不要）"),
            _ => ("Read this machine's hardware and system information",
                  "Get CPU, memory, OS version and similar details; takes no arguments")
        };

        return new ToolSchema
        {
            Summary = summary,
            Forms = new[] { new ToolCallForm { Summary = form } }
        };
    }
}
