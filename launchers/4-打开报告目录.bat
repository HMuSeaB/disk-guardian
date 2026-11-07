@echo off
chcp 936 >nul
setlocal
REM 托管 Python 开了 UTF-8 模式：固定住，避免控制台 936 与管道输出不一致
set "PYTHONUTF8=1"
set "PYTHONIOENCODING=utf-8"
title disk-guardian 打开报告目录
set "OUT=D:\4rchive\Code\disk-guardian\out"
if not exist "%OUT%" goto :NOOUT
explorer "%OUT%"
exit /b 0
:NOOUT
echo 还没有输出目录，先跑 1-预览可清理.bat
pause
exit /b 1
