<#
.SYNOPSIS
  编译 + 部署 + 打包纯净发布版（给其他玩家用的 ZIP）。

.DESCRIPTION
  生成 dist/AIChronicle_v<版本>.zip，解压后把里面的 AIChronicle 文件夹整个放进
  游戏目录 Modules\ 下即可使用。

  打包内容（纯净版，不含源码/战役数据）：
    - _Module\ 下的 SubModule.xml / GUI / Prompts（排除 Prompts\Campaigns\ 运行期数据）
    - 新编译的 AIChronicle.dll（Win64_Shipping_Client）
    - 面向玩家的文档：README_MOD.md、安装说明.txt、LICENSE

  用法：
    powershell -ExecutionPolicy Bypass -File scripts\package.ps1        # 全流程：编译+部署+打包
    powershell -ExecutionPolicy Bypass -File scripts\package.ps1 -SkipBuild  # 跳过编译，仅打包（需已编译）
#>
param(
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not $SkipBuild) {
    if (-not $env:BANNERLORD_GAME_DIR) {
        $env:BANNERLORD_GAME_DIR = "D:\steam\steamapps\common\Mount & Blade II Bannerlord"
    }
    $env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")
    Write-Host "[1/3] 编译 + 部署..." -ForegroundColor Cyan
    dotnet build -c Release
    if ($LASTEXITCODE -ne 0) { throw "编译失败" }
} else {
    Write-Host "[1/3] 跳过编译（-SkipBuild）" -ForegroundColor Yellow
}

Write-Host "[2/3] 组装纯净包..." -ForegroundColor Cyan
$version = (Select-String -Path "_Module\SubModule.xml" -Pattern '<Version value="([^"]+)"').Matches[0].Groups[1].Value.TrimStart('v')
$dist = Join-Path $root "dist"
$staging = Join-Path $dist "AIChronicle"
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $staging "bin\Win64_Shipping_Client") | Out-Null

# 模块本体（SubModule.xml / GUI / Prompts，排除 Campaigns 运行期数据）
Copy-Item "_Module\SubModule.xml" $staging
Copy-Item "_Module\GUI" (Join-Path $staging "GUI") -Recurse
Copy-Item "_Module\Prompts" (Join-Path $staging "Prompts") -Recurse
$campaigns = Join-Path $staging "Prompts\Campaigns"
if (Test-Path $campaigns) { Remove-Item $campaigns -Recurse -Force }

# 新编译的 DLL
$dll = "bin\Release\net472\AIChronicle.dll"
if (-not (Test-Path $dll)) { throw "未找到编译产物 $dll，请先编译" }
Copy-Item $dll (Join-Path $staging "bin\Win64_Shipping_Client\AIChronicle.dll")

# 面向玩家的文档
Copy-Item "README_MOD.md" $staging
Copy-Item "LICENSE" $staging
$installDoc = Get-ChildItem -Path $root -Filter "*.txt" | Where-Object { $_.Name -like "*安装*" -or $_.Name -like "*readme*" } | Select-Object -First 1
if ($installDoc) { Copy-Item $installDoc.FullName $staging }

Write-Host "[3/3] 压缩..." -ForegroundColor Cyan
$zipPath = Join-Path $dist "AIChronicle_v$version.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path $staging -DestinationPath $zipPath -CompressionLevel Optimal
Remove-Item $staging -Recurse -Force

$size = [math]::Round((Get-Item $zipPath).Length / 1KB, 1)
Write-Host "完成：$zipPath（$size KB）" -ForegroundColor Green
