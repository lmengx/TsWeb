#!/bin/bash
# 从远端拉取最新 git 变动
# 用法: bash pull.sh

set -e

cd "$(dirname "$0")"

echo "=== 当前分支 ==="
git branch --show-current

echo ""
echo "=== 拉取远端更新 ==="
git pull origin "$(git branch --show-current)"

echo ""
echo "=== 最近 5 条提交 ==="
git log --oneline -5

echo ""
echo "=== 完成 ==="