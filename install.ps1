$ErrorActionPreference = 'Stop'

$sourceExe = Join-Path $PSScriptRoot 'CodexQuotaRings.exe'
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\CodexQuotaRings'
$targetExe = Join-Path $installDir 'CodexQuotaRings.exe'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

if (-not (Test-Path -LiteralPath $sourceExe -PathType Leaf)) {
    throw '安装包里找不到 CodexQuotaRings.exe。'
}

$running = Get-Process -Name CodexQuotaRings -ErrorAction SilentlyContinue
if ($running) {
    throw '请先右键托盘圆环选择“退出”，再重新运行安装脚本。'
}

New-Item -ItemType Directory -Path $installDir -Force | Out-Null
Copy-Item -LiteralPath $sourceExe -Destination $targetExe -Force
New-Item -Path $runKey -Force | Out-Null
Set-ItemProperty -Path $runKey -Name CodexQuotaRings -Value ('"' + $targetExe + '" --background')
Start-Process -FilePath $targetExe -ArgumentList '--background' -WindowStyle Hidden
Write-Host "已安装并启动：$targetExe"
Write-Host '透明双圆环会直接显示在任务栏右侧；拖动圆环可以调整横向位置。'
