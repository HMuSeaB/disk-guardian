@echo off
chcp 936 >nul
setlocal
REM 托管 Python 开了 UTF-8 模式：固定住，避免控制台 936 与管道输出不一致
set "PYTHONUTF8=1"
set "PYTHONIOENCODING=utf-8"
title disk-guardian C# 核心自检
set "ROOT=D:\4rchive\Code\disk-guardian"
cd /d "%ROOT%"
dotnet run --project src\DiskGuardian.Cli\DiskGuardian.Cli.csproj -- --check-rules
echo.
pause
exit /b %errorlevel%
