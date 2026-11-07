@echo off
chcp 936 >nul
setlocal
REM 托管 Python 开了 UTF-8 模式：固定住，避免控制台 936 与管道输出不一致
set "PYTHONUTF8=1"
set "PYTHONIOENCODING=utf-8"
title disk-guardian 预览
set "PY=C:\Users\36230\.workbuddy\binaries\python\versions\3.13.12\python.exe"
set "APP=D:\4rchive\Code\disk-guardian\guardian.py"
if not exist "%PY%" set "PY=python"
if not exist "%APP%" goto :NOAPP
"%PY%" "%APP%" --json-report
set "RC=%errorlevel%"
echo.
echo 以上是只读预览，没有删除任何东西。
echo 报告在 D:\4rchive\Code\disk-guardian\out\last_report.json
echo.
pause
exit /b %RC%
:NOAPP
echo 找不到主程序：%APP%
echo 请确认项目还在 D:\4rchive\Code\disk-guardian\
pause
exit /b 1
