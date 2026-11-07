@echo off
chcp 936 >nul
setlocal
REM 托管 Python 开了 UTF-8 模式：固定住，避免控制台 936 与管道输出不一致
set "PYTHONUTF8=1"
set "PYTHONIOENCODING=utf-8"
title disk-guardian 图形界面
set "PYW=C:\Users\36230\.workbuddy\binaries\python\versions\3.13.12\pythonw.exe"
set "SRV=D:\4rchive\Code\disk-guardian\ui\server.py"
if not exist "%PYW%" set "PYW=pythonw"
if not exist "%SRV%" goto :NOSRV
echo 正在启动 disk-guardian 图形界面，浏览器窗口稍后自动出现。
echo 关闭请在界面右上角点「退出」；不要直接关服务进程。
start "" "%PYW%" "%SRV%"
timeout /t 2 >nul
exit /b 0
:NOSRV
echo 找不到界面服务：%SRV%
echo 请确认项目还在 D:\4rchive\Code\disk-guardian\
pause
exit /b 1
