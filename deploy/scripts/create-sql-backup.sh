#!/usr/bin/env bash
set -Eeuo pipefail

log() {
    printf '[%s] %s\n' "$(date -u +'%Y-%m-%dT%H:%M:%SZ')" "$*"
}

fail() {
    log "ERROR: $*"
    exit 1
}

require_command() {
    command -v "$1" >/dev/null 2>&1 || fail "Required command '$1' is not available."
}

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEPLOY_DIR="${DEPLOY_DIR:-$(cd "${SCRIPT_DIR}/../.." && pwd)}"
COMPOSE_FILE="${COMPOSE_FILE:-${DEPLOY_DIR}/deploy/docker-compose.production.yml}"
ENV_FILE="${ENV_FILE:-${DEPLOY_DIR}/deploy/.env}"
BACKUP_HOST_DIR="${BACKUP_HOST_DIR:-${DEPLOY_DIR}/deploy/backups/sqlserver}"
SQLSERVER_SERVICE_NAME="${SQLSERVER_SERVICE_NAME:-sqlserver}"
CONTAINER_BACKUP_DIR="/var/opt/mssql/backup"

require_command docker
require_command date
require_command mkdir

[[ -f "$COMPOSE_FILE" ]] || fail "Compose file '$COMPOSE_FILE' was not found."
[[ -f "$ENV_FILE" ]] || fail "Environment file '$ENV_FILE' was not found."

mkdir -p "$BACKUP_HOST_DIR"

set -a
# shellcheck disable=SC1090
source "$ENV_FILE"
set +a

: "${DB_NAME:?DB_NAME must be set in $ENV_FILE}"
: "${MSSQL_SA_PASSWORD:?MSSQL_SA_PASSWORD must be set in $ENV_FILE}"

compose=(docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE")

database_identifier="${DB_NAME//]/]]}"
safe_database_name="${DB_NAME//[^A-Za-z0-9_.-]/_}"
timestamp="$(date -u +'%Y%m%d_%H%M%S')"
backup_filename="${safe_database_name}_${timestamp}.bak"
container_backup_path="${CONTAINER_BACKUP_DIR}/${backup_filename}"
host_backup_path="${BACKUP_HOST_DIR}/${backup_filename}"

run_sql() {
    local sql="$1"

    "${compose[@]}" exec -T "$SQLSERVER_SERVICE_NAME" bash -lc '
        set -euo pipefail
        if [ -x /opt/mssql-tools18/bin/sqlcmd ]; then
            SQLCMD=/opt/mssql-tools18/bin/sqlcmd
        else
            SQLCMD=/opt/mssql-tools/bin/sqlcmd
        fi

        "$SQLCMD" \
            -S 127.0.0.1 \
            -U sa \
            -P "$MSSQL_SA_PASSWORD" \
            -C \
            -b \
            -Q "$1"
    ' bash "$sql"
}

log "Ensuring SQL Server backup directory is writable."
"${compose[@]}" exec -T "$SQLSERVER_SERVICE_NAME" bash -lc '
    set -euo pipefail
    mkdir -p /var/opt/mssql/backup
    test -w /var/opt/mssql/backup
'

backup_sql="BACKUP DATABASE [${database_identifier}] TO DISK = N'${container_backup_path}' WITH COPY_ONLY, INIT, CHECKSUM, STATS = 10;"
verify_sql="RESTORE VERIFYONLY FROM DISK = N'${container_backup_path}' WITH CHECKSUM;"

log "Creating SQL Server backup '${backup_filename}'."
run_sql "$backup_sql"

[[ -s "$host_backup_path" ]] || fail "Backup file '$host_backup_path' was not created or is empty."

log "Running RESTORE VERIFYONLY for '${backup_filename}'."
run_sql "$verify_sql"

log "SQL Server backup verified successfully: $host_backup_path"
printf '%s\n' "$host_backup_path"
