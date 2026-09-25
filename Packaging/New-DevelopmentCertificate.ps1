param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\certificate'),
    [string]$Password = 'Shigure-Development-Only'
)

$ErrorActionPreference = 'Stop'
$resolvedOutput = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null

$certificate = New-SelfSignedCertificate `
    -Subject 'CN=Arasaka Corporation' `
    -Type CodeSigningCert `
    -KeyAlgorithm RSA `
    -KeyLength 2048 `
    -HashAlgorithm SHA256 `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -NotAfter (Get-Date).AddYears(3)

$securePassword = ConvertTo-SecureString $Password -AsPlainText -Force
$pfxPath = Join-Path $resolvedOutput 'Shigure.Development.pfx'
$cerPath = Join-Path $resolvedOutput 'Shigure.Development.cer'
Export-PfxCertificate -Cert $certificate -FilePath $pfxPath -Password $securePassword | Out-Null
Export-Certificate -Cert $certificate -FilePath $cerPath | Out-Null

# 只信任刚生成的开发证书；生产发布应改用正式代码签名证书。
Import-Certificate -FilePath $cerPath -CertStoreLocation 'Cert:\CurrentUser\TrustedPeople' | Out-Null

Write-Host "开发证书已生成并信任：$pfxPath"
Write-Host "开发证书密码：$Password"
