#!/bin/bash
# The database backup (PLANNING Faz 6, E8). Runs in the "backup" service, from the same image
# as the database, so pg_dump always matches the server's version.
#
#   (no argument)   every day at BACKUP_TIME (TZ), keep BACKUP_KEEP_DAYS days of dumps
#   now             one dump at once
#   list            the dumps there are
#   restore FILE    replace the database with a dump; stop api and web first:
#
#     docker compose -f docker-compose.yml stop web api
#     docker compose -f docker-compose.yml run --rm backup restore btu-asistan-2026-11-20-0300.dump
#     docker compose -f docker-compose.yml up -d
#
# Dumps hold the students' questions (KVKK): they are written readable by their owner only,
# and copying them off the server is the school's IT's job.
set -euo pipefail

BACKUP_DIR=/backups
BACKUP_TIME="${BACKUP_TIME:-03:00}"
BACKUP_KEEP_DAYS="${BACKUP_KEEP_DAYS:-14}"

# pg_dump and pg_restore read the server and account from PGHOST, PGUSER, PGPASSWORD and
# PGDATABASE (compose).
umask 077

log() {
    echo "$(date '+%Y-%m-%d %H:%M:%S') $*"
}

backup_once() {
    local file="$BACKUP_DIR/btu-asistan-$(date +%Y-%m-%d-%H%M).dump"

    # Written under another name first: a dump cut short (disk full, restart) never looks
    # like a good one.
    pg_dump --format=custom --file="$file.partial"
    mv "$file.partial" "$file"
    log "backup written: $(basename "$file") ($(du -h "$file" | cut -f1))"

    find "$BACKUP_DIR" -name 'btu-asistan-*.dump' -mtime +"$BACKUP_KEEP_DAYS" -print -delete \
        | while read -r old; do log "old backup deleted: $(basename "$old")"; done
    rm -f "$BACKUP_DIR"/*.partial
}

restore() {
    local file="$1"
    [[ "$file" == /* ]] || file="$BACKUP_DIR/$file"
    [[ -f "$file" ]] || { log "no such backup: $file"; exit 1; }

    log "restoring $(basename "$file") into $PGDATABASE"
    # --clean drops what the dump recreates; one transaction, so a failure leaves the
    # database as it was.
    pg_restore --dbname="$PGDATABASE" --clean --if-exists --single-transaction --exit-on-error "$file"
    log "restore finished; start the services again with: docker compose -f docker-compose.yml up -d"
}

case "${1:-}" in
    now)
        backup_once
        ;;
    list)
        ls -lh "$BACKUP_DIR"/btu-asistan-*.dump 2>/dev/null || log "no backups yet"
        ;;
    restore)
        restore "${2:?usage: restore FILE}"
        ;;
    "")
        log "backups every day at $BACKUP_TIME ($TZ), kept for $BACKUP_KEEP_DAYS days, in $BACKUP_DIR"
        while true; do
            now=$(date +%s)
            next=$(date -d "today $BACKUP_TIME" +%s)
            (( next > now )) || next=$(date -d "tomorrow $BACKUP_TIME" +%s)
            sleep $(( next - now ))

            # A failed night is logged and tried again the next night, not retried in a loop.
            backup_once || log "backup FAILED"
        done
        ;;
    *)
        echo "usage: backup.sh [now | list | restore FILE]" >&2
        exit 2
        ;;
esac
