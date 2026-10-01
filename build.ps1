$ErrorActionPreference = 'Stop'
$version = '2.5.2'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) { throw '找不到 .NET Framework C# 编译器。' }
$buildDir = Join-Path $PSScriptRoot 'build'
$distDir = Join-Path $PSScriptRoot 'dist'
$packageDir = Join-Path $buildDir 'package'
New-Item -ItemType Directory -Path $buildDir, $distDir, $packageDir -Force | Out-Null
$sources = @((Join-Path $PSScriptRoot 'src\CodexQuotaRings.cs'), (Join-Path $PSScriptRoot 'src\TaskbarView.cs'))
$refs = @('/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Web.Extensions.dll')
$refs += '/win32manifest:' + (Join-Path $PSScriptRoot 'src\app.manifest')
$app = Join-Path $packageDir 'CodexQuotaRings.exe'
& $compiler /nologo /target:winexe /optimize+ $refs "/out:$app" $sources
if ($LASTEXITCODE -ne 0) { throw 'GUI 构建失败。' }
$check = Join-Path $buildDir 'QuotaCheck.exe'
& $compiler /nologo /target:exe $refs "/out:$check" $sources
if ($LASTEXITCODE -ne 0) { throw '自检程序构建失败。' }
& $check --self-test
if ($LASTEXITCODE -ne 0) { throw '额度解析自检失败。' }
$hoverCheck = Join-Path $buildDir 'HoverCheck.exe'
& $compiler /nologo /target:exe /main:HoverCheck $refs "/out:$hoverCheck" $sources (Join-Path $PSScriptRoot 'tests\HoverCheck.cs')
if ($LASTEXITCODE -ne 0) { throw '悬停自检构建失败。' }
& $hoverCheck
if ($LASTEXITCODE -ne 0) { throw '悬停交互自检失败。' }
$fakeCli = Join-Path $buildDir 'FakeCodex.exe'
$placementCheck = Join-Path $buildDir 'TaskbarPlacementCheck.exe'
& $compiler /nologo /target:exe /main:TaskbarPlacementCheck $refs "/out:$placementCheck" $sources (Join-Path $PSScriptRoot 'tests\TaskbarPlacementCheck.cs')
if ($LASTEXITCODE -ne 0) { throw 'Taskbar placement test build failed.' }
& $placementCheck
if ($LASTEXITCODE -ne 0) { throw 'Taskbar placement test failed.' }
& $compiler /nologo /target:exe /r:System.Web.Extensions.dll "/out:$fakeCli" (Join-Path $PSScriptRoot 'tests\FakeCodex.cs')
if ($LASTEXITCODE -ne 0) { throw '独立查询模拟 CLI 构建失败。' }
$standaloneCheck = Join-Path $buildDir 'StandaloneCheck.exe'
& $compiler /nologo /target:exe /main:StandaloneCheck $refs "/out:$standaloneCheck" $sources (Join-Path $PSScriptRoot 'tests\StandaloneCheck.cs')
if ($LASTEXITCODE -ne 0) { throw '独立查询自检构建失败。' }
& $standaloneCheck $fakeCli
if ($LASTEXITCODE -ne 0) { throw '独立查询与缓存自检失败。' }
foreach ($name in @('README.md', 'LICENSE', 'install.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $packageDir -Force
}
New-Item -ItemType Directory -Path (Join-Path $packageDir 'docs') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs\preview.png') -Destination (Join-Path $packageDir 'docs\preview.png') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs\hover-card.png') -Destination (Join-Path $packageDir 'docs\hover-card.png') -Force
$zip = Join-Path $distDir "CodexQuotaRings-v$version-windows.zip"
$packageFiles = @($app, (Join-Path $packageDir 'README.md'), (Join-Path $packageDir 'LICENSE'), (Join-Path $packageDir 'install.ps1'), (Join-Path $packageDir 'docs'))
Compress-Archive -LiteralPath $packageFiles -DestinationPath $zip -Force
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath (Join-Path $distDir 'SHA256SUMS.txt') -Value ($hash + '  ' + [IO.Path]::GetFileName($zip)) -Encoding ASCII
Write-Host "已构建：$zip"

