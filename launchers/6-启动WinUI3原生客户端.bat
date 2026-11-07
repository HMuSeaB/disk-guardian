@echo off
chcp 936 >nul
setlocal
REM 托管 Python 开了 UTF-8 模式：固定住，避免控制台 936 与管道输出不一致
set "PYTHONUTF8=1"
set "PYTHONIOENCODING=utf-8"
title disk-guardian WinUI 3 原生客户端
set "ROOT=D:\4rchive\Code\disk-guardian"
cd /d "%ROOT%"
echo 正在启动 disk-guardian WinUI 3 原生 Fluent 桌面客户端...
dotnet run --project src\DiskGuardian.UI\DiskGuardian.UI.csproj
if errorlevel 1 pause
exit /b %errorlevel%
