#!/usr/bin/env bash
# 建立範例監控項目（可重複執行；slug 已存在時會略過）。
# 用法：./scripts/seed.sh [API_BASE_URL]
#   API_KEY 預設為 appsettings.Development.json 中的開發用金鑰，可用環境變數覆寫。
set -euo pipefail

BASE_URL="${1:-http://localhost:5080}"
API_KEY="${API_KEY:-dev-api-key-change-me-1234}"
RESPONSE_FILE="$(mktemp)"
trap 'rm -f "$RESPONSE_FILE"' EXIT

create() {
  local slug="$1" body="$2" status
  status=$(curl -sS -o "$RESPONSE_FILE" -w '%{http_code}' \
    -H "X-Api-Key: ${API_KEY}" -H 'Content-Type: application/json' \
    -d "$body" "${BASE_URL}/api/monitors")
  case "$status" in
    201) echo "✔ created ${slug}: $(grep -o '"pingUrl":"[^"]*"' "$RESPONSE_FILE" | cut -d'"' -f4)" ;;
    409) echo "• ${slug} already exists, skipped" ;;
    *)   echo "✘ ${slug} failed with HTTP ${status}:"; cat "$RESPONSE_FILE"; echo; exit 1 ;;
  esac
}

# 1. 台北夜間 ETL：每天 02:00（Asia/Taipei），寬限 60 分鐘
create nightly-etl '{
  "name": "Nightly ETL (Taipei)", "slug": "nightly-etl",
  "scheduleType": "Cron", "cronExpression": "0 2 * * *",
  "graceMinutes": 60, "timeZone": "Asia/Taipei"
}'

# 2. 柏林海外備份主機：當地時間每天 02:30（Europe/Berlin，會跟著夏令時間移動），寬限 45 分鐘
create berlin-backup '{
  "name": "Offsite backup (Berlin)", "slug": "berlin-backup",
  "scheduleType": "Cron", "cronExpression": "30 2 * * *",
  "graceMinutes": 45, "timeZone": "Europe/Berlin"
}'

# 3. 紐約週報：週一到週五 06:15（America/New_York），通知送到會回 500 的端點，用來示範重試
create ny-weekday-report '{
  "name": "Weekday report (New York)", "slug": "ny-weekday-report",
  "scheduleType": "Cron", "cronExpression": "15 6 * * 1-5",
  "graceMinutes": 30, "timeZone": "America/New_York",
  "webhookUrl": "http://localhost:5099/webhook/fail"
}'

# 4. 每小時同步：固定間隔 60 分鐘（UTC），寬限 10 分鐘
create hourly-sync '{
  "name": "Hourly CRM sync", "slug": "hourly-sync",
  "scheduleType": "Interval", "intervalMinutes": 60,
  "graceMinutes": 10, "timeZone": "UTC"
}'

# 5. 示範用：每 1 分鐘、寬限 1 分鐘 —— ping 一次後約 2~2.5 分鐘就會看到 Late → Down
create demo-quick '{
  "name": "Demo: one-minute job", "slug": "demo-quick",
  "scheduleType": "Interval", "intervalMinutes": 1,
  "graceMinutes": 1, "timeZone": "Asia/Taipei"
}'

echo
echo "List: curl -H 'X-Api-Key: ${API_KEY}' ${BASE_URL}/api/monitors"
