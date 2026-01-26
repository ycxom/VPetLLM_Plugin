# VPetLLM 插件开发指南

## 📦 至普通用户

直接下载对应插件目录下的 `.dll` 文件即可使用：

| 插件名称 | 功能描述 | 下载路径 |
|---------|---------|---------|
| **MoneyManagerPlugin** | 节日里给萝莉斯包红包！ | `MoneyManagerPlugin/plugin/MoneyManagerPlugin.dll` |
| **ReminderPlugin** | 设置定时提醒 | `ReminderPlugin/plugin/ReminderPlugin.dll` |
| **SystemInfoPlugin** | 获取当前操作系统的版本信息 | `SystemInfoPlugin/plugin/SystemInfoPlugin.dll` |
| **ForegroundAppPlugin** | 监视前台应用程序并将其名称提供给 AI | `ForegroundAppPlugin/plugin/ForegroundAppPlugin.dll` |
| **AppLauncherPlugin** | 允许 AI 启动应用程序 | `AppLauncherPlugin/plugin/AppLauncherPlugin.dll` |
| **MinecraftVersionPlugin** | 监听并识别我的世界版本 | `MinecraftVersionPlugin/plugin/MinecraftVersionPlugin.dll` |
| **MarkdownViewerPlugin** | 在独立窗口中渲染 Markdown 文档 | `MarkdownViewerPlugin/plugin/MarkdownViewerPlugin.dll` |
| **WebSearchPlugin** | 搜索互联网内容或获取网页内容 | `WebSearchPlugin/plugin/WebSearchPlugin.dll` |
| **ExamplePlugin** | 示例插件（开发参考） | `ExamplePlugin/plugin/ExamplePlugin.dll` |

---

## 🛠️ 开发者指南

欢迎来到 VPetLLM 的插件开发！通过插件，您可以扩展 VPetLLM 的功能，让您的桌宠更加智能和强大。

## 目录

1. [插件简介](#插件简介)
2. [开发环境设置](#开发环境设置)
3. [核心接口详解](#核心接口详解)
4. [完整示例](#完整示例)
5. [高级接口](#高级接口)
6. [构建与部署](#构建与部署)
7. [AI 如何调用插件](#ai-如何调用插件)

---

## 插件简介

VPetLLM 插件是一个实现了特定接口的 `.dll` 文件，它允许您：
- **添加新的功能**：例如，获取天气信息、查询股价、控制智能家居等。
- **与外部服务交互**：通过调用 API，将外部数据集成到与桌宠的互动中。

目前，VPetLLM 支持以下类型的插件接口：

| 接口 | 说明 |
|-----|------|
| `IVPetLLMPlugin` | 基础接口，所有插件必须实现 |
| `IActionPlugin` | 标记接口，表示可被 AI 主动调用的动作插件 |
| `IPluginWithData` | 提供插件数据目录支持 |
| `IDynamicInfoPlugin` | 提供动态信息给 AI 的插件 |

---

## 开发环境设置

1. **IDE**: 推荐使用 Visual Studio
2. **项目类型**: 创建 `.NET 8.0` 类库项目（启用 WPF）
3. **添加引用**: 
   - `VPet-Simulator.Windows.Interface.dll`（通过 NuGet）
   - `VPetLLM.dll`（本地引用）

### 项目配置 (.csproj)

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <OutputType>Library</OutputType>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <UseWPF>true</UseWPF>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
    <PackageReference Include="VPet-Simulator.Windows.Interface" Version="1.1.0.50" />
  </ItemGroup>

  <ItemGroup>
    <Reference Include="VPetLLM">
      <HintPath>你的VPetLLM.dll路径</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>

  <!-- 可选：自动复制到 plugin 目录 -->
  <Target Name="CopyPlugin" AfterTargets="Build">
    <ItemGroup>
      <PluginFiles Include="$(OutDir)$(TargetName).dll" />
    </ItemGroup>
    <Copy SourceFiles="@(PluginFiles)" DestinationFolder="plugin" />
  </Target>

</Project>
```

---

## 核心接口详解

### IVPetLLMPlugin

这是所有插件都必须实现的基础接口。

```csharp
public interface IVPetLLMPlugin
{
    // 插件的名称，AI 将通过这个名称来调用你的插件
    // 名称应该清晰、简洁，并使用下划线(_)代替空格
    string Name { get; }

    // 插件的详细描述，帮助 AI 理解插件的功能
    // 注意：AI 依靠此信息来生成调用插件的指令
    string Description { get; }
    
    // 插件接受的参数说明，例如 "time(int), unit(string), event(string)"
    // 注意：AI 依靠此信息来生成调用插件的指令
    string Parameters { get; }

    // 插件的调用示例
    // 注意：AI 依靠此信息来生成调用插件的指令
    string Examples { get; }

    // 控制插件是否启用
    bool Enabled { get; set; }

    // 插件文件的路径
    string FilePath { get; set; }

    // 插件的核心功能实现
    // 当 AI 调用此插件时，这个异步方法将被执行
    // 'arguments' 参数是 AI 根据 'Parameters' 定义生成的字符串
    Task<string> Function(string arguments);

    // 初始化方法，在插件被加载时调用
    // 你可以在这里进行一些初始化操作，例如保存 VPetLLM 的实例
    void Initialize(VPetLLM plugin);

    // 卸载方法，在插件被卸载时调用
    void Unload();

    // 日志记录方法，方便调试
    void Log(string message);
}
```

### IActionPlugin

这是一个标记接口，继承自 `IVPetLLMPlugin`。它表示该插件是一个可以被 AI 调用的"动作"。

```csharp
public interface IActionPlugin : IVPetLLMPlugin
{
    // 这是一个标记接口，表明该插件可以被 AI 作为动作调用
    // 它没有额外的方法
}
```

---

## 完整示例

### 示例 1：简单插件（ExamplePlugin）

最简单的插件实现，返回固定问候语：

```csharp
using System.Threading.Tasks;
using VPetLLM;

public class ExamplePlugin : IActionPlugin
{
    public string Name => "example_plugin";
    public string Author => "ycxom";
    public string Description
    {
        get
        {
            if (_vpetLLM is null) return "一个简单的示例插件。";
            switch (_vpetLLM.Settings.Language)
            {
                case "ja": return "呼び出されると固定の挨拶を返す簡単なサンプルプラグインです。";
                case "zh-hans": return "一个简单的示例插件，在调用时返回固定的问候语。";
                case "zh-hant": return "一個簡單的範例插件，在呼叫時返回固定的問候語。";
                case "en":
                default: return "A simple example plugin that returns a fixed greeting when called.";
            }
        }
    }
    public string Parameters => ""; // 此插件不需要任何参数
    public string Examples => "Example: `<|plugin_example_plugin_begin|> <|plugin_example_plugin_end|>`";
    public bool Enabled { get; set; } = true;
    public string FilePath { get; set; } = "";

    private VPetLLM.VPetLLM? _vpetLLM;

    public void Initialize(VPetLLM.VPetLLM plugin)
    {
        _vpetLLM = plugin;
        FilePath = plugin.PluginPath;
        VPetLLM.Utils.Logger.Log("Example Plugin Initialized!");
    }

    public Task<string> Function(string arguments)
    {
        if (_vpetLLM is null) return Task.FromResult("VPetLLM instance is not initialized.");
        var result = "Hello, I am an example plugin!";
        _vpetLLM.Log($"ExamplePlugin: Function called. Returning: {result}");
        return Task.FromResult(result);
    }

    public void Unload()
    {
        VPetLLM.Utils.Logger.Log("Example Plugin Unloaded!");
    }

    public void Log(string message)
    {
        _vpetLLM?.Log(message);
    }
}
```

### 示例 2：带参数的插件（ReminderPlugin）

支持参数解析的定时提醒插件：

```csharp
using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using VPetLLM;

public class ReminderPlugin : IActionPlugin
{
    public string Name => "reminder";
    public string Author => "ycxom";
    public string Description
    {
        get
        {
            if (_vpetLLM is null) return "设置一个定时提醒。";
            switch (_vpetLLM.Settings.Language)
            {
                case "ja": return "タイマーリマインダーを設定します。";
                case "zh-hans": return "设置一个定时提醒。";
                case "zh-hant": return "設置一個定時提醒。";
                case "en":
                default: return "Set a timed reminder.";
            }
        }
    }
    
    // 定义参数格式，AI 会根据此生成调用指令
    public string Parameters => "time(int), unit(string, optional: seconds/minutes), event(string)";
    
    // 提供调用示例，帮助 AI 理解如何调用
    public string Examples => "Example: `<|plugin_reminder_begin|> time(10), unit(minutes), event(\"study\") <|plugin_reminder_end|>`";
    
    public bool Enabled { get; set; } = true;
    public string FilePath { get; set; } = "";

    private VPetLLM.VPetLLM? _vpetLLM;

    public void Initialize(VPetLLM.VPetLLM plugin)
    {
        _vpetLLM = plugin;
        VPetLLM.Utils.Logger.Log("Reminder Plugin Initialized!");
    }

    public Task<string> Function(string arguments)
    {
        try
        {
            // 使用正则表达式解析参数
            var timeMatch = new Regex(@"time\((\d+)\)").Match(arguments);
            var unitMatch = new Regex(@"unit\((\w+)\)").Match(arguments);
            var eventMatch = new Regex(@"event\(""(.*?)""\)").Match(arguments);

            if (!timeMatch.Success || !eventMatch.Success)
            {
                return Task.FromResult("创建提醒失败：缺少 'time' 或 'event' 参数。");
            }

            var timeValue = int.Parse(timeMatch.Groups[1].Value);
            var unit = unitMatch.Success ? unitMatch.Groups[1].Value.ToLower() : "seconds";
            var message = eventMatch.Groups[1].Value;

            TimeSpan delay;
            switch (unit)
            {
                case "minute":
                case "minutes":
                    delay = TimeSpan.FromMinutes(timeValue);
                    break;
                case "second":
                case "seconds":
                default:
                    delay = TimeSpan.FromSeconds(timeValue);
                    break;
            }

            // 启动异步提醒任务
            _ = ReminderTask(delay, message);

            return Task.FromResult($"好的，我会在 {timeValue} {unit} 后提醒你 '{message}'");
        }
        catch (Exception e)
        {
            return Task.FromResult($"创建提醒失败，请检查参数: {e.Message}");
        }
    }

    private async Task ReminderTask(TimeSpan delay, string message)
    {
        if (_vpetLLM is null) return;
        await Task.Delay(delay);

        var aiName = _vpetLLM.Settings.AiName;
        var notificationTitle = $"{aiName} 提醒你";
        var notificationMessage = $"该 "{message}" 了";

        // 使用 Dispatcher 在 UI 线程上执行 UI 操作
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var mainWindow = System.Windows.Application.Current.MainWindow;
            if (mainWindow is not null)
            {
                mainWindow.Activate();
                mainWindow.Topmost = true;
                _ = Task.Run(async () =>
                {
                    await Task.Delay(3000);
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => mainWindow.Topmost = false);
                });
            }
            
            System.Windows.MessageBox.Show(notificationMessage, notificationTitle, 
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        });

        // 让桌宠说话
        var response = $"reminder_finished, Task: \"{message}\"";
        await _vpetLLM.ChatCore.Chat(response, true);
    }

    public void Unload()
    {
        VPetLLM.Utils.Logger.Log("Reminder Plugin Unloaded!");
    }

    public void Log(string message)
    {
        _vpetLLM?.Log(message);
    }
}
```

---

## 高级接口

### IPluginWithData

如果你的插件需要保存配置或数据，可以实现此接口：

```csharp
public interface IPluginWithData
{
    // 插件数据目录路径，由 VPetLLM 自动设置
    string PluginDataDir { get; set; }
}
```

### IDynamicInfoPlugin

如果你的插件需要向 AI 提供动态信息（如监控数据），可以实现此接口：

```csharp
public interface IDynamicInfoPlugin
{
    // 返回动态信息供 AI 参考
    // 此方法会被定期调用
}
```

### 带设置窗口的插件

参考 `AppLauncherPlugin` 或 `WebSearchPlugin`，可以通过 `action(setting)` 参数打开设置窗口：

```csharp
public Task<string> Function(string arguments)
{
    var actionMatch = new Regex(@"action\((\w+)\)").Match(arguments);
    if (actionMatch.Success)
    {
        var action = actionMatch.Groups[1].Value.ToLower();
        if (action == "setting")
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var settingWindow = new YourSettingWindow();
                settingWindow.Show();
            });
            return Task.FromResult("设置窗口已打开。");
        }
    }
    // ... 其他逻辑
}
```

---

## 构建与部署

1. **构建**: 在 Visual Studio 中，将你的项目构建为 `.dll` 文件
2. **部署**: 将生成的 `.dll` 文件复制到 `我的文档/VPetLLM/Plugin` 目录下

### 通过 UI 管理插件

你可以在 VPetLLM 的设置窗口中，通过"插件"选项卡来管理插件：
- **导入**: 点击"导入插件"按钮，选择你的 `.dll` 文件
- **启用/禁用**: 通过勾选插件列表中的复选框来启用或禁用插件
- **卸载**: 选择一个插件，然后点击"卸载插件"按钮来删除它

---

## AI 如何调用插件

### 调用格式

AI 通过特定格式的标记来调用插件：

```
<|plugin_插件名称_begin|> 参数内容 <|plugin_插件名称_end|>
```

**注意**：旧格式 `[:plugin]pluginName(arguments)` 已被弃用，不再支持。

### 调用流程

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         VPetLLM 插件调用流程                              │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  1. AI 生成响应                                                          │
│     ↓                                                                   │
│  2. CommandFormatParser 解析响应文本                                      │
│     - 使用正则表达式匹配 <|plugin_xxx_begin|>...<|plugin_xxx_end|>        │
│     - 提取插件名称和参数                                                  │
│     ↓                                                                   │
│  3. ActionProcessor 处理命令                                             │
│     - 识别 plugin_xxx 格式，提取插件名称                                   │
│     - 查找对应的 PluginHandler                                           │
│     ↓                                                                   │
│  4. PluginHandler 执行插件                                               │
│     - 根据插件名称查找已注册的插件实例                                      │
│     - 调用插件的 Function(arguments) 方法                                 │
│     ↓                                                                   │
│  5. 插件返回结果                                                         │
│     - 结果格式化为 [Plugin Result: pluginName] result                    │
│     - 通过 ResultAggregator 聚合后回传给 AI                               │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

### 调用示例

| 插件 | 调用格式 |
|-----|---------|
| reminder | `<\|plugin_reminder_begin\|> time(10), unit(minutes), event("study") <\|plugin_reminder_end\|>` |
| WebSearch | `<\|plugin_WebSearch_begin\|> search\|AMD 9950HX <\|plugin_WebSearch_end\|>` |
| AppLauncher | `<\|plugin_AppLauncher_begin\|> notepad <\|plugin_AppLauncher_end\|>` |
| example_plugin | `<\|plugin_example_plugin_begin\|> <\|plugin_example_plugin_end\|>` |

### 插件注册机制

插件在 VPetLLM 启动时通过 `PluginManager` 自动加载：

1. **扫描目录**：扫描 `我的文档/VPetLLM/Plugin` 目录下的所有 `.dll` 文件
2. **影子拷贝**：将 DLL 复制到临时目录，避免文件锁定问题
3. **加载程序集**：使用 `AssemblyLoadContext` 加载程序集（支持热卸载）
4. **实例化插件**：查找实现 `IVPetLLMPlugin` 接口的类型并创建实例
5. **初始化**：调用插件的 `Initialize()` 方法
6. **注册到 ChatCore**：将插件添加到聊天核心，使 AI 可以调用

### 插件信息注入

启用的插件信息会被注入到 AI 的系统提示（System Message）中：

```
Available Plugins:
plugin_name: 插件描述 Example: `<|plugin_xxx_begin|> ... <|plugin_xxx_end|>`
```

AI 根据这些信息理解如何调用插件。因此，`Description`、`Parameters` 和 `Examples` 属性非常重要。

### 结果回传

插件执行完成后，结果会通过 `ResultAggregator` 聚合（2秒窗口），然后统一回传给 AI：

```csharp
// 插件返回结果后，PluginHandler 会格式化并聚合
var formattedResult = $"[Plugin Result: {pluginName}] {result}";
ResultAggregator.Enqueue(formattedResult);
```

这样 AI 可以根据插件返回的结果继续对话或执行后续操作。

---

## 🔧 高级功能

### IPluginTakeover - 插件接管接口

如果你的插件需要完全接管消息处理流程（如流式处理），可以实现此接口：

```csharp
public interface IPluginTakeover : IVPetLLMPlugin
{
    // 是否支持接管模式
    bool SupportsTakeover { get; }

    // 开始接管处理
    Task<bool> BeginTakeoverAsync(string initialContent);

    // 处理接管期间的内容片段
    Task<bool> ProcessTakeoverContentAsync(string content);

    // 结束接管处理
    Task<string> EndTakeoverAsync();

    // 检查是否应该结束接管
    bool ShouldEndTakeover(string content);
}
```

### 限流机制

VPetLLM 内置了插件调用限流机制，防止 AI 过度调用插件：

- **默认配置**：5次/2分钟（跨消息调用）
- **同一消息内**：不受限流影响（允许插件联合调用）

### 主动发送消息

插件可以主动向 AI 发送消息：

```csharp
// 方法 1：通过 ChatCore 发送
await _vpetLLM.ChatCore.Chat("消息内容", true);

// 方法 2：通过 PluginHandler 发送格式化消息
VPetLLM.Handlers.PluginHandler.SendPluginMessage("plugin_name", "消息内容");
```

### 访问 VPet 状态

通过 `_vpetLLM` 实例可以访问桌宠的各种状态：

```csharp
// 获取设置
var settings = _vpetLLM.Settings;
var aiName = settings.AiName;
var userName = settings.UserName;
var language = settings.Language;

// 获取聊天历史
var history = _vpetLLM.GetChatHistory();

// 播放 TTS
await _vpetLLM.PlayTTSAsync("要说的话");

// 获取可用动画列表
var animations = _vpetLLM.GetAvailableAnimations();
```

### 🚀 直接调用 LLM

插件可以通过 `LLMEntry` 接口直接调用 LLM 服务：

```csharp
// 在插件的 Function 方法中
var response = await _vpetLLM.LLMEntry.CallAsync("你的消息");
```

**特性：**
- 简单易用，一行代码即可调用
- 自动记录调用日志（调用者、消息、响应、耗时）
- 不影响主对话历史
- 支持所有 LLM 提供商（OpenAI、Ollama、Gemini、Free）

**使用示例：**

```csharp
public async Task<string> Function(string arguments)
{
    if (_vpetLLM?.LLMEntry == null)
        return "LLM service not available";
    
    // 让 LLM 分析用户输入
    var analysis = await _vpetLLM.LLMEntry.CallAsync($"分析：{arguments}");
    return analysis;
}
```

**日志输出：**
```
[LLM Call] Plugin:YourPluginName calling LLM
[LLM Call] Message: 分析：...
[LLM Call] Plugin:YourPluginName - Response in 2.34s
[LLM Call] Response: ...
```

**注意：** 外部应用调用时，日志会显示为 `ExternalProgram:` 前缀，以区分插件调用。

---

## 📝 开发建议

### 基本规范

1. **多语言支持**: 在 `Description` 属性中根据 `_vpetLLM.Settings.Language` 返回不同语言的描述
2. **错误处理**: 在 `Function` 方法中妥善处理异常，返回友好的错误信息
3. **日志记录**: 使用 `VPetLLM.Utils.Logger.Log()` 或 `_vpetLLM.Log()` 记录调试信息
4. **UI 线程**: 如需操作 UI，使用 `Application.Current.Dispatcher.Invoke()` 或 `InvokeAsync()`
5. **资源清理**: 在 `Unload()` 方法中释放资源（如 HttpClient、定时器等）

### 命名规范

- **插件名称**：使用小写字母和下划线，如 `my_plugin`、`web_search`
- **参数格式**：使用 `参数名(类型)` 格式，如 `time(int)`, `query(string)`
- **示例格式**：提供完整的调用示例，帮助 AI 理解

### 性能建议

1. **异步操作**：耗时操作使用 `async/await`，避免阻塞
2. **缓存**：对于频繁访问的数据，考虑使用缓存
3. **超时处理**：网络请求设置合理的超时时间
4. **资源复用**：如 `HttpClient`，应在插件生命周期内复用
