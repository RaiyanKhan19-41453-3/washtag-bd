#!/usr/bin/env bash
# Restore a compressed dump created by backup.sh. DESTRUCTIVE.
set -euo pipefail
cd "$(dirname "$0")/.."

if [ "$#" -ne 1 ]; then
  echo "Usage: ./scripts/restore.sh backups/washtag_YYYYMMDD_HHMMSS.sql.gz"
  exit 1
fi
FILE="$1"
if [ ! -f "$FILE" ]; then
  echo "Backup file not found: $FILE"
  exit 1
fi

if [ -f .env ]; then set -a; . ./.env; set +a; fi
POSTGRES_USER="${POSTGRES_USER:-washtag}"
POSTGRES_DB="${POSTGRES_DB:-washtagdb}"

echo "WARNING: this replaces ALL data in database '${POSTGRES_DB}'."
read -r -p "Type RESTORE to continue: " confirmation
if [ "$confirmation" != "RESTORE" ]; then
  echo "Cancelled."
  exit 1
fi

echo "Stopping the API so no writes race the restore..."
docker compose stop api
if gzip -dc "$FILE" | docker compose exec -T postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1; then
  docker compose start api
  echo "Restore complete. The API has been restarted."
else
  docker compose start api
  echo "Restore FAILED. The API has been restarted; check the database before use."
  exit 1
fi
