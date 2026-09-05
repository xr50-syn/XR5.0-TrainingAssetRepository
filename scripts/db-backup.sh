#!/bin/bash
# Dumps the base database and every xr50_tenant_* schema from the stack's MariaDB container into
# one timestamped SQL file. Run it before every upgrade: MySQL DDL is not transactional, so this
# dump is the rollback path for a migration that stops halfway.
#
# usage: scripts/db-backup.sh [output-dir]        (default: ./backups, gitignored)
#
# Reads XR50_REPO_DB_PASSWORD (and XR50_REPO_DB_NAME) from the environment or from .env; the
# password is passed to the container through MYSQL_PWD and never printed.
set -euo pipefail
cd "$(dirname "$0")/.."

if [ -z "${XR50_REPO_DB_PASSWORD:-}" ] && [ -f .env ]; then
    set -a; . ./.env; set +a
fi
: "${XR50_REPO_DB_PASSWORD:?XR50_REPO_DB_PASSWORD is not set (export it or provide .env)}"

CONTAINER="${MARIADB_CONTAINER:-mariadb}"
BASE_DB="${XR50_REPO_DB_NAME:-magical_library}"
OUT_DIR="${1:-backups}"
mkdir -p "$OUT_DIR"
OUT="$OUT_DIR/xr50-db-$(date +%Y%m%d-%H%M%S).sql"

M() { docker exec -i -e MYSQL_PWD="$XR50_REPO_DB_PASSWORD" "$CONTAINER" "$@"; }

mapfile -t TENANT_DBS < <(M mysql -uroot -N -B \
    -e "SELECT SCHEMA_NAME FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME LIKE 'xr50\\_tenant\\_%' ORDER BY SCHEMA_NAME")

echo "Dumping $BASE_DB and ${#TENANT_DBS[@]} tenant database(s) from container $CONTAINER to $OUT"
M mysqldump -uroot --single-transaction --routines --triggers --databases "$BASE_DB" "${TENANT_DBS[@]}" > "$OUT"

echo "Wrote $(du -h "$OUT" | cut -f1): $OUT"
echo "Restore with: docker exec -i -e MYSQL_PWD=... $CONTAINER mysql -uroot < $OUT"
