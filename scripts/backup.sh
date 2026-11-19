#!/usr/bin/env bash
# Back up the shop database to a compressed SQL dump (keeps the last 7).
set -euo pipefail
cd "$(dirname "$0")/.."

if [ -f .env ]; then set -a; . ./.env; set +a; fi

POSTGRES_USER="${POSTGRES_USER:-washtag}"
POSTGRES_DB="${POSTGRES_DB:-washtagdb}"
mkdir -p backups

STAMP="$(date +%Y%m%d_%H%M%S)"
FILE="backups/washtag_${STAMP}.sql.gz"

echo "Backing up database '${POSTGRES_DB}' as '${POSTGRES_USER}'..."
TMP="${FILE}.tmp"
docker compose exec -T postgres pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" \
  --no-owner --clean --if-exists | gzip > "$TMP"
# Never leave a truncated dump that retention would treat as a valid backup.
gzip -t "$TMP" || { rm -f "$TMP"; echo "Backup verification failed" >&2; exit 1; }
mv -f "$TMP" "$FILE"
echo "Saved $FILE"

ls -1t backups/*.sql.gz 2>/dev/null | tail -n +8 | xargs -r rm --
echo "Retention: keeping the newest 7 backups."
