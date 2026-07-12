# 蓝牙音频接收器 —— 一键安装脚本
# 作用：把随包的自签名证书导入“受信任人/受信任根”，再安装 MSIX。
# 用法：右键此文件 -> “使用 PowerShell 运行”；若被拦截，见下方说明。

$ErrorActionPreference = "Stop"

# 需要管理员权限（导入证书到 LocalMachine 需要）
$isAdmin = ([Security.Principal.WindowsPrincipal] `
    [Security.Principal.WindowsIdentity]::GetCurrent()
).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Host "需要管理员权限，正在尝试以管理员身份重新启动……" -ForegroundColor Yellow
    Start-Process powershell -Verb RunAs -ArgumentList `
        "-ExecutionPolicy Bypass -File `"$PSCommandPath`""
    exit
}

$here = Split-Path -Parent $MyInvocation.MyCommand.Definition
$cer  = Join-Path $here "BtAudioReceiver.cer"
$msix = Join-Path $here "BtAudioReceiver.msix"

if (-not (Test-Path $cer))  { throw "找不到证书文件 BtAudioReceiver.cer（应与本脚本同目录）" }
if (-not (Test-Path $msix)) { throw "找不到安装包 BtAudioReceiver.msix（应与本脚本同目录）" }

Write-Host "1/2 导入并信任证书……" -ForegroundColor Cyan
# 导入到“受信任根”和“受信任人”，让系统信任这个自签名包
Import-Certificate -FilePath $cer -CertStoreLocation "Cert:\LocalMachine\Root"          | Out-Null
Import-Certificate -FilePath $cer -CertStoreLocation "Cert:\LocalMachine\TrustedPeople" | Out-Null
Write-Host "    证书已信任。" -ForegroundColor Green

Write-Host "2/2 安装应用……" -ForegroundColor Cyan
Add-AppxPackage -Path $msix
Write-Host "    安装完成！可在开始菜单搜索“蓝牙音频接收器”启动。" -ForegroundColor Green

Write-Host ""
Write-Host "如需卸载：设置 > 应用 > 找到“蓝牙音频接收器” > 卸载。" -ForegroundColor DarkGray
