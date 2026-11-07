@echo off
chcp 936 >nul
setlocal
REM 托管 Python 开了 UTF-8 模式：固定住，避免控制台 936 与管道输出不一致
set "PYTHONUTF8=1"
set "PYTHONIOENCODING=utf-8"
title disk-guardian 执行安全清理
set "PY=C:\Users\36230\.workbuddy\binaries\python\versions\3.13.12\python.exe"
set "APP=D:\4rchive\Code\disk-guardian\guardian.py"
if not exist "%PY%" set "PY=python"
if not exist "%APP%" goto :NOAPP
echo.
echo 只处理 level 1 纯缓存。先列清单，再要求输入 DELETE 才会动手。
echo 动作是移入回收站，可以撤销，不会永久删除。
echo 要真正腾出空间，请另跑 3-清空回收站.bat。
echo.
"%PY%" "%APP%" --apply --level 1 --json-report
echo.
pause
exit /b 0
:NOAPP
echo 找不到主程序：%APP%
echo 请确认项目还在 D:\4rchive\Code\disk-guardian\
pause
exit /b 1
