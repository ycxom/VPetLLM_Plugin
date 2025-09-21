# AppLauncherPlugin - 应用启动器插件

## 功能介绍

AppLauncherPlugin 是一个强大的应用启动器插件，允许AI助手启动各种应用程序。该插件支持多种应用类型的识别和启动。

## 主要特性

### 1. 多种应用支持
- **自定义应用**: 用户可以添加自己的应用程序，指定名称、路径和启动参数
- **系统应用**: 自动识别常见的Windows系统应用程序
- **Steam游戏**: 自动扫描Steam游戏库，支持通过Steam协议启动游戏
- **开始菜单应用**: 自动扫描Windows开始菜单中的应用程序

### 2. 智能识别
- 自动扫描 `C:\Users\ycxom\AppData\Roaming\Microsoft\Windows\Start Menu\Programs` 目录
- 支持公共开始菜单应用识别
- 内置常见系统应用程序列表
- 自动检测Steam安装路径并扫描游戏库
- 支持常见Steam游戏的快速启动（CS2、Dota2、TF2等）

### 3. 灵活配置
- 可开启/关闭开始菜单扫描功能
- 可开启/关闭系统应用功能
- 可开启/关闭Steam游戏支持
- 支持启动日志记录
- 完整的设置界面

## 使用方法

### 基本语法
```
[:plugin(AppLauncher(应用名称))]
[:plugin(AppLauncher(action(setting)))]
```

### 使用示例
```
# 启动系统应用
[:plugin(AppLauncher(notepad))]           # 启动记事本
[:plugin(AppLauncher(calculator))]        # 启动计算器

# 启动自定义应用
[:plugin(AppLauncher(chrome))]            # 启动Chrome浏览器（如果已配置）

# 启动Steam游戏
[:plugin(AppLauncher(cs2))]               # 启动Counter-Strike 2
[:plugin(AppLauncher(dota2))]             # 启动Dota 2
[:plugin(AppLauncher(steam))]             # 打开Steam客户端

# 打开设置界面
[:plugin(AppLauncher(action(setting)))]   # 打开设置界面
[:plugin(AppLauncher(setting))]           # 打开设置界面（向后兼容）
```

## 内置系统应用

插件内置了以下常见系统应用的支持：

| 应用名称 | 描述 | 命令 |
|---------|------|------|
| notepad | 记事本 | notepad.exe |
| calculator | 计算器 | calc.exe |
| paint | 画图 | mspaint.exe |
| cmd | 命令提示符 | cmd.exe |
| powershell | PowerShell | powershell.exe |
| explorer | 文件资源管理器 | explorer.exe |
| taskmgr | 任务管理器 | taskmgr.exe |
| regedit | 注册表编辑器 | regedit.exe |
| msconfig | 系统配置 | msconfig.exe |
| control | 控制面板 | control.exe |
| winver | Windows版本信息 | winver.exe |
| charmap | 字符映射表 | charmap.exe |
| magnify | 放大镜 | magnify.exe |
| osk | 屏幕键盘 | osk.exe |
| snip | 截图工具 | ms-screenclip: |
| settings | Windows设置 | ms-settings: |

## Steam游戏支持

插件支持自动检测和启动Steam游戏：

### 自动检测功能
- 自动查找Steam安装路径
- 扫描Steam游戏库中已安装的游戏
- 从ACF文件中提取游戏ID信息

### 内置常见游戏
| 游戏名称 | 别名 | Steam ID |
|---------|------|----------|
| Counter-Strike 2 | cs2 | 730 |
| Dota 2 | dota2 | 570 |
| Team Fortress 2 | tf2 | 440 |
| Left 4 Dead 2 | l4d2 | 550 |
| Portal 2 | - | 620 |
| Half-Life 2 | - | 220 |
| Garry's Mod | gmod | 4000 |
| Rust | - | 252490 |
| Grand Theft Auto V | gtav, gta5 | 271590 |
| Cyberpunk 2077 | - | 1091500 |
| The Witcher 3 | witcher3 | 292030 |
| Steam客户端 | steam | - |

### Steam游戏启动方式
1. **Steam协议启动**: 使用 `steam://rungameid/游戏ID` 协议
2. **直接启动**: 如果找到游戏安装路径，直接启动游戏可执行文件
3. **Steam客户端**: 使用 `steam://open/main` 打开Steam主界面

## 配置管理

### 打开设置界面
```
[:plugin(AppLauncher(action(setting)))]
```
或者使用向后兼容的方式：
```
[:plugin(AppLauncher(setting))]
```

### 设置界面功能
1. **基本设置**
   - 启用/禁用开始菜单应用扫描
   - 启用/禁用系统应用
   - 启用/禁用启动日志记录

2. **自定义应用管理**
   - 添加新的自定义应用
   - 编辑现有应用配置
   - 删除不需要的应用
   - 测试应用启动

3. **应用列表查看**
   - 查看所有可用的应用程序
   - 按类型分类显示（自定义、系统、开始菜单）

### 添加自定义应用
1. 在设置界面中填写应用信息：
   - **应用名称**: 用于调用的名称（如：chrome）
   - **应用路径**: 可执行文件的完整路径
   - **启动参数**: 可选的命令行参数

2. 点击"添加应用"保存配置

3. 使用 `[:plugin(AppLauncher(应用名称))]` 启动

## 配置文件

插件的配置保存在 `AppLauncherPlugin.json` 文件中，包含：
- 自定义应用列表
- 功能开关设置
- 日志记录选项

## 安全说明

- 插件会验证应用路径的有效性
- 支持相对路径和绝对路径
- 使用系统默认的安全启动方式
- 记录所有启动操作（可选）

## 故障排除

### 常见问题
1. **应用启动失败**
   - 检查应用路径是否正确
   - 确认应用文件是否存在
   - 检查是否有足够的权限

2. **找不到应用**
   - 确认应用名称拼写正确
   - 检查相关功能开关是否启用
   - 尝试刷新应用列表

3. **开始菜单应用不显示**
   - 确认"启用开始菜单应用扫描"已开启
   - 点击"刷新应用列表"重新扫描

## 技术特性

- 基于 .NET 8.0 开发
- 支持 WPF 图形界面
- 异步应用启动
- 完整的错误处理
- 多语言支持（中文、英文、日文）

## 版本信息

- 版本: 1.0.0
- 作者: ycxom
- 兼容性: VPetLLM 插件系统

---

通过这个插件，AI助手可以帮助用户快速启动各种应用程序，大大提升了交互体验和工作效率。