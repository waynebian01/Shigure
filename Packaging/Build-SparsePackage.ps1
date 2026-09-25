param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$CertificatePath,
    [string]$CertificatePassword,
    [switch]$Register
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts\sparse-package'))
$layoutDirectory = Join-Path $artifactRoot 'layout'
$publishDirectory = Join-Path $artifactRoot 'publish'
$packagePath = Join-Path $artifactRoot 'Shigure.Identity.msix'

if (-not $artifactRoot.StartsWith($repositoryRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw '打包输出目录不在仓库内，已停止。'
}

if (Test-Path -LiteralPath $artifactRoot) {
    Remove-Item -LiteralPath $artifactRoot -Recurse -Force
}

New-Item -ItemType Directory -Path (Join-Path $layoutDirectory 'Assets') -Force | Out-Null
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

dotnet publish (Join-Path $repositoryRoot 'Shigure.csproj') `
    -c $Configuration `
    -o $publishDirectory
if ($LASTEXITCODE -ne 0) {
    throw 'dotnet publish 失败。'
}

[xml]$project = Get-Content -LiteralPath (Join-Path $repositoryRoot 'Shigure.csproj')
$version = [string](
    $project.Project.PropertyGroup |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_.Version) } |
        Select-Object -First 1).Version
[xml]$packageManifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'AppxManifest.xml')
$packageManifest.Package.Identity.Version = $version
$manifestPath = Join-Path $layoutDirectory 'AppxManifest.xml'
$packageManifest.Save($manifestPath)

Add-Type -AssemblyName System.Drawing
$sourceImagePath = Join-Path $repositoryRoot 'Assets\arasaka-icon-transparent.png'
$sourceImage = [Drawing.Image]::FromFile($sourceImagePath)
try {
    foreach ($asset in @(
        @{ Name = 'StoreLogo.png'; Size = 50 },
        @{ Name = 'Square44x44Logo.png'; Size = 44 },
        @{ Name = 'Square150x150Logo.png'; Size = 150 }
    )) {
        $bitmap = New-Object Drawing.Bitmap $asset.Size, $asset.Size
        try {
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([Drawing.Color]::Transparent)
                $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.DrawImage($sourceImage, 0, 0, $asset.Size, $asset.Size)
            }
            finally {
                $graphics.Dispose()
            }

            $bitmap.Save(
                (Join-Path $layoutDirectory "Assets\$($asset.Name)"),
                [Drawing.Imaging.ImageFormat]::Png)
        }
        finally {
            $bitmap.Dispose()
        }
    }
}
finally {
    $sourceImage.Dispose()
}

# 稀疏包的可视资源由外部位置解析，因此发布目录也必须包含同样的 Assets 路径。
Copy-Item -LiteralPath (Join-Path $layoutDirectory 'Assets') `
    -Destination (Join-Path $publishDirectory 'Assets') `
    -Recurse `
    -Force

function Find-WindowsSdkTool([string]$Name) {
    $sdkBin = 'C:\Program Files (x86)\Windows Kits\10\bin'
    $tool = Get-ChildItem -LiteralPath $sdkBin -Recurse -Filter $Name -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if ($null -eq $tool) {
        throw "未找到 $Name；请安装 Windows SDK 的 MSIX Packaging Tools。"
    }

    return $tool.FullName
}

$makeAppx = Find-WindowsSdkTool 'makeappx.exe'
& $makeAppx pack /d $layoutDirectory /p $packagePath /nv /o
if ($LASTEXITCODE -ne 0) {
    throw 'MakeAppx 打包失败。'
}

if (-not [string]::IsNullOrWhiteSpace($CertificatePath)) {
    $resolvedCertificate = [IO.Path]::GetFullPath($CertificatePath)
    $certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new(
        $resolvedCertificate,
        $CertificatePassword)
    if ($certificate.Subject -ne 'CN=Arasaka Corporation') {
        throw "证书主题必须为 CN=Arasaka Corporation，实际为 $($certificate.Subject)。"
    }

    $signTool = Find-WindowsSdkTool 'signtool.exe'
    & $signTool sign /fd SHA256 /f $resolvedCertificate /p $CertificatePassword $packagePath
    if ($LASTEXITCODE -ne 0) {
        throw 'SignTool 签名失败。'
    }
}
elseif ($Register) {
    throw '注册身份包前必须通过 -CertificatePath 提供签名证书。'
}

if ($Register) {
    Add-AppxPackage -Path $packagePath -ExternalLocation $publishDirectory -ForceApplicationShutdown
    Write-Host '身份包已注册。请从 publish 目录重新启动 Shigure.exe，并在系统弹窗中允许无边框捕获。'
}

Write-Host "发布目录：$publishDirectory"
Write-Host "身份包：$packagePath"
