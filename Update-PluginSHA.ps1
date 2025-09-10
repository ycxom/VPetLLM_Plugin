param (
    [string]$PluginId,
    [string]$DllPath
)

# 强制PowerShell使用UTF-8 with BOM
$PSDefaultParameterValues['Out-File:Encoding'] = 'utf8bom'
$PSDefaultParameterValues['Set-Content:Encoding'] = 'utf8bom'

# 检查参数
if (-not $PluginId -or -not $DllPath) {
    Write-Error "Usage: .\Update-PluginSHA.ps1 -PluginId <ID> -DllPath <DLL>"
    exit 1
}

# 检查 DLL 文件
if (-not (Test-Path $DllPath)) {
    Write-Error "DLL file not found: $DllPath"
    exit 1
}

# 寻找 Newtonsoft.Json.dll
# VPetLLM.dll的路径是固定的，用它来定位依赖
$vpetLLMDllPath = "D:\CodeDesk\VPetLLM\VPetLLM\3000_VPetLLM\plugin\VPetLLM.dll"
$pluginBaseDir = (Get-Item $vpetLLMDllPath).Directory.FullName
$newtonsoftPath = Join-Path $pluginBaseDir "Newtonsoft.Json.dll"

if (-not (Test-Path $newtonsoftPath)) {
    Write-Error "Newtonsoft.Json.dll not found at expected location: $newtonsoftPath"
    exit 1
}
Add-Type -Path $newtonsoftPath

# 计算 SHA256
$sha256 = (Get-FileHash -Path $DllPath -Algorithm SHA256).Hash.ToLower()

# 定位 PluginList.json
$pluginListPath = Join-Path $PSScriptRoot "PluginList.json"

if (-not (Test-Path $pluginListPath)) {
    Write-Error "PluginList.json not found at: $pluginListPath"
    exit 1
}

# 使用 [System.IO.File] 和 UTF8 with BOM 编码读取
$jsonString = [System.IO.File]::ReadAllText($pluginListPath, [System.Text.Encoding]::UTF8)
$jsonObj = [Newtonsoft.Json.Linq.JObject]::Parse($jsonString)

if ($jsonObj.ContainsKey($PluginId)) {
    $oldHash = $jsonObj[$PluginId]["SHA256"].ToString()
    $jsonObj[$PluginId]["SHA256"] = $sha256
    
    # 使用 Newtonsoft.Json 序列化以保留格式和特殊字符
    $newJsonString = $jsonObj.ToString([Newtonsoft.Json.Formatting]::Indented)
    
    # 使用 [System.IO.File] 和 UTF8 with BOM 编码写回
    [System.IO.File]::WriteAllText($pluginListPath, $newJsonString, [System.Text.Encoding]::UTF8)
    
    Write-Host "Successfully updated SHA256 for $PluginId from $oldHash to $sha256"
} else {
    Write-Error "PluginId '$PluginId' not found in PluginList.json"
    exit 1
}