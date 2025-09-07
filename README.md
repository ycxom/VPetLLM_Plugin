# VPetLLM 插件开发指南

## 至普通用户

- 在 VPetLLM_Plugin\ExamplePlugin\plugin\ExamplePlugin.dll 内及是插件文件，将其下载即可
- 以此类推，进入目录下载其他插件即可


欢迎来到 VPetLLM 的插件开发！通过插件，您可以扩展 VPetLLM 的功能，让您的桌宠更加智能和强大。

## 目录
1.  [插件简介](#插件简介)
2.  [开发环境设置](#开发环境设置)
3.  [核心接口详解](#核心接口详解)
    *   [IVPetLLMPlugin](#ivpetllmplugin)
    *   [IActionPlugin](#iactionplugin)
4.  [插件示例](#插件示例)
5.  [构建与部署](#构建与部署)
6.  [AI 如何调用插件](#ai-如何调用插件)

---

## 插件简介

VPetLLM 插件是一个实现了特定接口的 `.dll` 文件，它允许您：
*   **添加新的功能**：例如，获取天气信息、查询股价、控制智能家居等。
*   **与外部服务交互**：通过调用 API，将外部数据集成到与桌宠的互动中。

目前，VPetLLM 支持两种类型的插件：
*   **通用插件 (`IVPetLLMPlugin`)**：提供了插件的基本框架，是所有插件都必须实现的接口。
*   **动作插件 (`IActionPlugin`)**：继承自 `IVPetLLMPlugin`，主要用于定义可以被 AI 主动调用的、具有明确功能的操作。

---

## 开发环境设置

1.  **IDE**: 推荐使用 Visual Studio。
2.  **项目类型**: 创建一个 `.NET` 类库项目。
3.  **添加引用**: 在您的项目中，需要添加对 `VPet-Simulator.Windows.Interface.dll` 和 `VPetLLM.dll` 的引用。

---

## 核心接口详解

### IVPetLLMPlugin
这是所有插件都必须实现的基础接口。

```csharp
public interface IVPetLLMPlugin
{
    // 插件的名称，AI 将通过这个名称来调用你的插件。
    // 名称应该清晰、简洁，并使用下划线(_)代替空格。
    string Name { get; }

    // 插件的详细描述，这将帮助 AI 理解插件的功能。
    string Description { get; }
    
    // 插件接受的参数说明，采用 JSON Schema 格式。
    // 这对于 AI 理解如何正确构造函数参数至关重要。
    string Parameters { get; }

    // 控制插件是否启用
    bool Enabled { get; set; }

    // 插件文件的路径
    string FilePath { get; set; }

    // 插件的核心功能实现。
    // 当 AI 调用此插件时，这个异步方法将被执行。
    // 'arguments' 参数是 AI 根据 'Parameters' 定义生成的 JSON 字符串。
    Task<string> Function(string arguments);

    // 初始化方法，在插件被加载时调用。
    // 你可以在这里进行一些初始化操作，例如保存 VPetLLM 的实例。
    void Initialize(VPetLLM plugin);

    // 卸载方法，在插件被卸载时调用。
    void Unload();

    // 日志记录方法，方便调试。
    void Log(string message);
}
```

### IActionPlugin
这是一个标记接口，继承自 `IVPetLLMPlugin`。它表示该插件是一个可以被 AI 调用的“动作”。

```csharp
public interface IActionPlugin : IVPetLLMPlugin
{
    // 当插件被作为动作调用时，此方法将被执行。
    // 通常，你可以在这里触发一些即时的、无返回值的操作。
    void Invoke();
}
```

---

## 插件示例

### SystemInfoPlugin
下面是一个获取当前系统信息的简单插件示例 (`SystemInfoPlugin`)：

```csharp
using System;
using System.Threading.Tasks;
using VPetLLM;
using VPetLLM.Core;

public class SystemInfoPlugin : IActionPlugin
{
    public string Name => "system_info";
    public string Description => "获取当前操作系统的版本信息。";
    public string Parameters => "{}"; // 这个插件不需要任何参数
    public bool Enabled { get; set; } = true;
    public string FilePath { get; set; } = "";

    private VPetLLM.VPetLLM? _vpetLLM;

    public void Initialize(VPetLLM.VPetLLM plugin)
    {
        _vpetLLM = plugin;
        FilePath = plugin.PluginPath;
        VPetLLM.Utils.Logger.Log("System Info Plugin Initialized!");
    }

    public Task<string> Function(string arguments)
    {
        var osInfo = Environment.OSVersion.VersionString;
        if (_vpetLLM == null) return Task.FromResult("VPetLLM instance is not initialized.");
        _vpetLLM.Log($"SystemInfoPlugin: Function called. Returning OSVersion: {osInfo}");
        return Task.FromResult(osInfo);
    }

    public void Invoke()
    {
        // 这个方法现在可以被保留，或者在未来用于无参数的、手动的调用
    }

    public void Unload()
    {
        VPetLLM.Utils.Logger.Log("System Info Plugin Unloaded!");
    }

    public void Log(string message)
    {
        if (_vpetLLM == null) return;
        _vpetLLM.Log(message);
    }
}
```

### ReminderPlugin
下面是一个定时提醒的插件示例 (`ReminderPlugin`)：

```csharp
using System;
using System.Threading.Tasks;
using VPetLLM;
using VPetLLM.Core;

public class ReminderPlugin : IActionPlugin
{
    public string Name => "reminder";
    public string Description => "设置一个定时提醒。";
    public string Parameters => "{\"time_in_minutes\": \"提醒的分钟数\", \"message\": \"提醒的内容\"}";
    public bool Enabled { get; set; } = true;
    public string FilePath { get; set; } = "";

    private VPetLLM.VPetLLM? _vpetLLM;

    public void Initialize(VPetLLM.VPetLLM plugin)
    {
        _vpetLLM = plugin;
        FilePath = plugin.PluginPath;
        VPetLLM.Utils.Logger.Log("Reminder Plugin Initialized!");
    }

    public Task<string> Function(string arguments)
    {
        try
        {
            var args = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(arguments);
            if (args == null)
            {
                return Task.FromResult("创建提醒失败：无效的参数。");
            }
            int minutes = args.time_in_minutes;
            string message = args.message;

            // 启动一个后台任务来处理延迟和提醒，不要 await 它
            _ = ReminderTask(minutes, message);

            return Task.FromResult($"好的，我会在 {minutes} 分钟后提醒你: {message}");
        }
        catch (Exception e)
        {
            return Task.FromResult($"创建提醒失败，请检查参数: {e.Message}");
        }
    }

    private async Task ReminderTask(int minutes, string message)
    {
        if (_vpetLLM == null) return;
        await Task.Delay(TimeSpan.FromMinutes(minutes));

        // 使用Dispatcher在UI线程上显示提醒
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            _vpetLLM.MW.Main.Say($"叮咚！提醒时间到啦：{message}");
        });
    }

    public void Invoke()
    {
    }

    public void Unload()
    {
        VPetLLM.Utils.Logger.Log("Reminder Plugin Unloaded!");
    }

    public void Log(string message)
    {
        if (_vpetLLM == null) return;
        _vpetLLM.Log(message);
    }
}
```

---

## 构建与部署

1.  **构建**: 在 Visual Studio 中，将你的项目构建为 `.dll` 文件。
2.  **部署**: 将生成的 `.dll` 文件复制到 `我的文档/VPetLLM/Plugin` 目录下。
3.  **重启 VPet**: 重启 VPet-Simulator，插件将会被自动加载。

---

## AI 如何调用插件

为了让 AI 能够调用你的插件，你需要在给 AI 的 `system prompt` (角色设定) 中，明确告知它有哪些插件可用，以及如何调用它们。

### 通过 UI 管理插件

你可以在 VPetLLM 的设置窗口中，通过“插件”选项卡来管理插件：
*   **导入**: 点击“导入插件”按钮，选择你的 `.dll` 文件。
*   **启用/禁用**: 通过勾选插件列表中的复选框来启用或禁用插件。
*   **卸载**: 选择一个插件，然后点击“卸载插件”按钮来删除它。

### 通过指令管理插件

你也可以通过在聊天中发送指令来管理插件：
*   **启用插件**: `[:plugin(enable:插件名称)]`
*   **禁用插件**: `[:plugin(disable:插件名称)]`
*   **删除插件**: `[:plugin(delete:插件名称)]`

### 调用插件

调用插件的格式为：`[:plugin(插件名称(参数))]`

**示例 `system prompt`**:

> 你是一只桌宠...
>
> **可用插件**:
> *   `system_info`: 获取操作系统信息。调用方法: `[:plugin(system_info())]`
> *   `reminder(time_in_minutes: int, message: string)`: 设置一个定时提醒。调用方法: `[:plugin(reminder(time_in_minutes: 3, message: "开发plugin"))]`

通过这样的设定，AI 在需要时，就会在它的回复中包含特定的插件调用指令，从而触发你的插件功能。