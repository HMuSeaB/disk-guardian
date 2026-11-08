$ErrorActionPreference = "Stop"

Write-Host "=== DiskGuardian 本地一键构建与极简打包脚本 ===" -ForegroundColor Cyan

$distDir = "dist"
if (Test-Path $distDir) {
    Remove-Item -Recurse -Force $distDir
}

# 1. 核心安全自测
Write-Host "`n[1/4] 正在运行 21 项核心安全与防误杀自测..." -ForegroundColor Yellow
dotnet run --project src/DiskGuardian.Cli/DiskGuardian.Cli.csproj -c Release -p:Platform=x64 -r win-x64 -- --check-rules
if ($LASTEXITCODE -ne 0) {
    Write-Host "核心自测未通过，终止构建！" -ForegroundColor Red
    exit 1
}

# 2. 构建 WinUI 3 应用与依赖至 app/ 目录
Write-Host "`n[2/4] 正在编译 WinUI 3 桌面端核心与收纳依赖..." -ForegroundColor Yellow
$winuiPkg = "$distDir/DiskGuardian-WinUI3"
dotnet publish src/DiskGuardian.UI/DiskGuardian.UI.csproj -c Release -r win-x64 --self-contained false -p:Platform=x64 -p:WindowsPackageType=None -p:AppxPackage=false -p:WindowsAppSDKSelfContained=true -o "$winuiPkg/app"

# 3. 构建根目录极简启动器
Write-Host "`n[3/4] 正在编译根目录极简启动器 (DiskGuardian.exe)..." -ForegroundColor Yellow
$launcherOut = "$distDir/launcher-temp"
dotnet publish src/DiskGuardian.Launcher/DiskGuardian.Launcher.csproj -c Release -r win-x64 --self-contained false -p:Platform=x64 -o $launcherOut
Copy-Item "$launcherOut/DiskGuardian.exe" $winuiPkg/ -Force
Remove-Item -Recurse -Force $launcherOut

# 复制配置文件与生成说明
Copy-Item rules.json $winuiPkg/ -Force
Copy-Item rules.json "$winuiPkg/app/" -Force

@"
========================================================================
  DiskGuardian (磁盘守护者) - Windows 原生系统与空间守护工具
========================================================================

【使用指南】:
  1. 双击根目录的【DiskGuardian.exe】直接启动应用（WinUI 3 原生界面）。
  2. 依赖项及运行时已统一收纳在 app/ 目录中，请勿删除 app 目录。
  3. 核心清理规则位于 rules.json，支持自由自定义。

【核心特性】:
  - 核心安全硬禁区代码化保护：锁定事件日志 (winevt)、转储文件 (dmp) 与系统组件。
  - 极速双清理模式：支持【直接永久清理】（零回收站卡顿）与【移入回收站】（安全防误杀）。
  - Windows 幽灵代理诊断与安全修复 (ProxyDoctor)：免重启广播刷新，绝不杀 explorer.exe。
========================================================================
"@ | Out-File -FilePath "$winuiPkg/使用说明.txt" -Encoding utf8

# 清理 pdb
Get-ChildItem -Path $winuiPkg -Recurse -Filter *.pdb | Remove-Item -Force

# 4. 构建 CLI 运维端
Write-Host "`n[4/4] 正在编译 CLI 极速运维工具..." -ForegroundColor Yellow
$cliPkg = "$distDir/DiskGuardian-Cli"
dotnet publish src/DiskGuardian.Cli/DiskGuardian.Cli.csproj -c Release -r win-x64 --self-contained false -p:Platform=x64 -o $cliPkg
Copy-Item rules.json $cliPkg/ -Force
Get-ChildItem -Path $cliPkg -Recurse -Filter *.pdb | Remove-Item -Force

# 5. 打包为 ZIP
Write-Host "`n正在压缩生成发布包..." -ForegroundColor Yellow
Compress-Archive -Path "$winuiPkg/*" -DestinationPath "$distDir/disk-guardian-winui3-win-x64.zip" -Force
Compress-Archive -Path "$cliPkg/*" -DestinationPath "$distDir/disk-guardian-cli-win-x64.zip" -Force

Write-Host "`n========================================================" -ForegroundColor Green
Write-Host "构建成功！发布产物位于 dist 目录：" -ForegroundColor Green
Write-Host "  - 桌面端极简包: dist/disk-guardian-winui3-win-x64.zip" -ForegroundColor White
Write-Host "  - CLI 运维端包: dist/disk-guardian-cli-win-x64.zip" -ForegroundColor White
Write-Host "解压桌面端后，根目录仅保留 DiskGuardian.exe 与必要文件，双击秒开！" -ForegroundColor Green
Write-Host "========================================================`n" -ForegroundColor Green
