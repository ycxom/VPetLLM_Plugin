# WeatherPlugin - VPetLLM 天气插件

查询中国地区天气信息的 VPetLLM 插件。

## ⚠️ 重要提示

**该接口仅支持中国大陆地区数据，不支持其他国家和地区的天气查询。**

## 功能特性

- 🌤️ 查询实时天气（温度、湿度、风向、天气状况）
- 📅 查询未来4天天气预报
- 🔍 城市名称模糊匹配（支持简称、拼音错误容错）
- ⚙️ 可配置默认城市
- 🌐 支持多语言描述

## 使用方法

### AI 调用格式

```
<|plugin_Weather_begin|> city(北京), type(current) <|plugin_Weather_end|>
<|plugin_Weather_begin|> city(上海), type(forecast) <|plugin_Weather_end|>
```

### 参数说明

| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| city | string | 否 | 城市名称，默认使用设置中的默认城市 |
| type | string | 否 | 查询类型：`current`(实时) 或 `forecast`(预报)，默认 `current` |
| action | string | 否 | 设置为 `setting` 打开设置窗口 |

### 示例

- 查询北京当前天气：`city(北京), type(current)`
- 查询上海天气预报：`city(上海), type(forecast)`
- 打开设置窗口：`action(setting)`

## 安装

将 `WeatherPlugin.dll` 复制到 `我的文档/VPetLLM/Plugin` 目录下。

## 设置

通过设置窗口可以配置：

- **默认城市**：未指定城市时使用的默认城市
- **请求超时**：API 请求超时时间（秒）
- **API 密钥**：可选，用于自定义 API 提供商

## 技术实现

- 使用 `weather.exlb.net` API 获取天气数据
- 使用 Levenshtein 编辑距离算法进行城市名称模糊匹配
- 内置中国大陆主要城市数据库

## 开发

```bash
# 构建
dotnet build WeatherPlugin/WeatherPlugin.csproj

# 输出位置
WeatherPlugin/plugin/WeatherPlugin.dll
```
