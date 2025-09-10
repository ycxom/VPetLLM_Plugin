# VPetLLM 插件开发指南

## 至普通用户

- 在 VPetLLM_Plugin\ExamplePlugin\plugin\ExamplePlugin.dll 内及是插件文件，将其下载即可
- 以此类推，进入目录下载其他插件即可

欢迎来到 VPetLLM 的插件开发！通过插件，您可以扩展 VPetLLM 的功能，让您的桌宠更加智能和强大。

## 目录

1. [插件简介](#插件简介)
2. [开发环境设置](#开发环境设置)
3. [核心接口详解](#核心接口详解)
    - [IVPetLLMPlugin](#ivpetllmplugin)
    - [IActionPlugin](#iactionplugin)
4. [构建与部署](#构建与部署)
5. [AI 如何调用插件](#ai-如何调用插件)

---

## 插件简介

VPetLLM 插件是一个实现了特定接口的 `.dll` 文件，它允许您：
- **添加新的功能**：例如，获取天气信息、查询股价、控制智能家居等。
- **与外部服务交互**：通过调用 API，将外部数据集成到与桌宠的互动中。

目前，VPetLLM 支持两种类型的插件：
- **通用插件 (`IVPetLLMPlugin`)**：提供了插件的基本框架，是所有插件都必须实现的接口。
- **动作插件 (`IActionPlugin`)**：继承自 `IVPetLLMPlugin`，主要用于定义可以被 AI 主动调用的、具有明确功能的操作。

---

## 开发环境设置

1. **IDE**: 推荐使用 Visual Studio。
2. **项目类型**: 创建一个 `.NET` 类库项目。
3. **添加引用**: 在您的项目中，需要添加对 `VPet-Simulator.Windows.Interface.dll` 和 `VPetLLM.dll` 的引用。

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

    // 插件的详细描述，这将帮助 AI 理解插件的功能。             注意：ai 依靠 此信息来生成调用插件的指令。
    string Description { get; }
    
    // 插件接受的参数说明，例如 "time(int), unit(string), event(string)"。  注意：ai 依靠 此信息来生成调用插件的指令。
    string Parameters { get; }

    // 插件的调用示例。                                     注意：ai 依靠 此信息来生成调用插件的指令。
    string Examples { get; }

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
    // 这是一个标记接口，表明该插件可以被AI作为动作调用。
    // 它没有额外的方法。
}
```

---

## 构建与部署

1. **构建**: 在 Visual Studio 中，将你的项目构建为 `.dll` 文件。
2. **部署**: 将生成的 `.dll` 文件复制到 `我的文档/VPetLLM/Plugin` 目录下。

---

### 通过 UI 管理插件

你可以在 VPetLLM 的设置窗口中，通过“插件”选项卡来管理插件：
- **导入**: 点击“导入插件”按钮，选择你的 `.dll` 文件。
- **启用/禁用**: 通过勾选插件列表中的复选框来启用或禁用插件。
- **卸载**: 选择一个插件，然后点击“卸载插件”按钮来删除它。

### 编译插件

```csproj
  <ItemGroup>
    <Reference Include="VPetLLM">
      <HintPath>D:\CodeDesk\VPetLLM\VPetLLM\3000_VPetLLM\plugin\VPetLLM.dll</HintPath>  <!-- VPetLLM.dll路径 -->
      <Private>false</Private>
    </Reference>
  </ItemGroup>
```
修改路径，确保 `VPetLLM.dll` 能被读取。
