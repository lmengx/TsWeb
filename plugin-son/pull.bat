@echo off

:repeat

chcp 65001 >nul
setlocal enabledelayedexpansion

:: 获取脚本所在目录并切换
cd /d "%~dp0"
cd TsWeb
echo === 当前分支 ===
for /f "delims=" %%b in ('git branch --show-current') do set "cur_branch=%%b"
echo !cur_branch!

echo.
echo === 拉取远端更新 ===
git pull origin !cur_branch!

echo.
echo === 最近 5 条提交 ===
git log --oneline -5

echo.
echo === 完成 ===
pause
goto repeat