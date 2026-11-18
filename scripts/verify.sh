#!/usr/bin/env bash
# scripts/verify.sh — Bản Linux của harness kiểm chứng (tham chiếu).
# Bản chính cho Windows là scripts/verify.ps1. File này chỉ gọi .ps1 qua pwsh nếu có, hoặc thông báo.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if command -v pwsh >/dev/null 2>&1; then
  pwsh -NoProfile -File "$ROOT/scripts/verify.ps1" "$@"
else
  echo "Chạy trên Linux nhưng không có pwsh. Hãy dùng scripts/verify.ps1 trên Windows PowerShell." >&2
  exit 1
fi
