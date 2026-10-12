param(
    [Parameter(Mandatory)][uri]$Url,
    [Parameter(Mandatory)][string]$Destination
)

$ErrorActionPreference = 'Stop'
# 某些 Wowhead 页面拒绝 urllib 的 HTTP 客户端，使用 Windows 的标准请求客户端读取公开页面。
$taskPage = Invoke-WebRequest -Uri $Url -TimeoutSec 45
[System.IO.File]::WriteAllText($Destination, $taskPage.Content, [System.Text.UTF8Encoding]::new($false))
