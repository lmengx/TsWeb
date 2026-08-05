@echo off
chcp 65001 >nul
echo === 1/4 安装前端依赖 ===
cd /d "%~dp0frontend"
call pnpm install

echo === 2/4 构建前端 ===
call pnpm vite build

echo === 3/4 复制产物到后端 ===
if exist "%~dp0backend\dist" rmdir /s /q "%~dp0backend\dist"
xcopy /e /i /q "%~dp0frontend\dist" "%~dp0backend\dist"

echo === 4/4 启动后端服务 ===
cd /d "%~dp0backend"
call pnpm install
node server.js