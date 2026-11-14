#!/bin/sh
# Nightly PostgreSQL backup (custom format, restorable with pg_restore), keeping BACKUP_KEEP_DAYS days.
# Runs in the `backup` container; the first backup is taken at start-up.
set -eu
KEEP_DAYS="${BACKUP_KEEP_DAYS:-7}"

while true; do
  file="/backups/treasurykit-$(date -u +%Y%m%dT%H%M%SZ).dump"
  if pg_dump --format=custom --file="$file.partial"; then
    mv "$file.partial" "$file"
    echo "$(date -u +%FT%TZ) backup written: $file ($(du -h "$file" | cut -f1))"
  else
    rm -f "$file.partial"
    echo "$(date -u +%FT%TZ) backup FAILED" >&2
  fi
  find /backups -name 'treasurykit-*.dump' -mtime +"$KEEP_DAYS" -delete
  sleep 86400
done
