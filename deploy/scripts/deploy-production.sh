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

TARGET_SHA="${1:-}"
[[ -n "$TARGET_SHA" ]] || fail "Usage: $0 <git-commit-sha>"
[[ "$TARGET_SHA" =~ ^[0-9a-fA-F]{7,40}$ ]] || fail "The requested commit SHA '$TARGET_SHA' is invalid."

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEPLOY_DIR="${DEPLOY_DIR:-/opt/Neftyanik}"
PRODUCTION_BRANCH="${PRODUCTION_BRANCH:-master}"
REMOTE_NAME="${REMOTE_NAME:-origin}"
ENV_FILE="${ENV_FILE:-${DEPLOY_DIR}/deploy/.env}"
COMPOSE_FILE="${COMPOSE_FILE:-${DEPLOY_DIR}/deploy/docker-compose.production.yml}"
LOCK_FILE="${LOCK_FILE:-${DEPLOY_DIR}/deploy/.production-deploy.lock}"
BACKUP_SCRIPT_PATH="${BACKUP_SCRIPT_PATH:-${SCRIPT_DIR}/create-sql-backup.sh}"
TARGET_SHA_LOWER="$(printf '%s' "$TARGET_SHA" | tr '[:upper:]' '[:lower:]')"

require_command curl
require_command docker
require_command flock
require_command git
require_command awk

mkdir -p "$(dirname "$LOCK_FILE")"
exec 9>"$LOCK_FILE"
flock -n 9 || fail "Another deployment is already running."

WEB_WAS_STOPPED=0
COMPOSE_READY=0
HEALTH_URL=""
COMPOSE_ARGS=()

cleanup() {
    local exit_code=$?
    trap - EXIT

    if (( exit_code != 0 )); then
        log "Deployment failed."

        if (( WEB_WAS_STOPPED == 1 )) && (( COMPOSE_READY == 1 )); then
            log "Attempting to start the web service after failure."
            if "${COMPOSE_ARGS[@]}" up -d web; then
                log "Web service restart attempt completed."
            else
                log "WARNING: Web service restart attempt failed. Manual intervention may be required."
            fi
        fi
    fi

    exit "$exit_code"
}
trap cleanup EXIT

wait_for_service_health() {
    local service_name="$1"
    local attempts="${2:-30}"
    local sleep_seconds="${3:-5}"
    local container_id
    local status

    for ((attempt = 1; attempt <= attempts; attempt++)); do
        container_id="$(${COMPOSE_ARGS[@]} ps -q "$service_name")"

        if [[ -n "$container_id" ]]; then
            status="$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' "$container_id" 2>/dev/null || true)"
            if [[ "$status" == "healthy" ]]; then
                return 0
            fi
        fi

        log "Waiting for service '$service_name' to become healthy (attempt ${attempt}/${attempts})."
        sleep "$sleep_seconds"
    done

    fail "Service '$service_name' did not become healthy in time."
}

wait_for_public_health() {
    local url="$1"
    local attempts="${2:-12}"
    local sleep_seconds="${3:-5}"

    for ((attempt = 1; attempt <= attempts; attempt++)); do
        if curl --fail --silent --show-error --location --max-time 30 "$url" >/dev/null; then
            return 0
        fi

        log "Waiting for public health check to succeed (attempt ${attempt}/${attempts})."
        sleep "$sleep_seconds"
    done

    fail "Public health check failed for '$url'."
}

get_health_url() {
    local site_address="$1"
    local primary_entry

    primary_entry="$(printf '%s' "$site_address" | tr ',' ' ' | awk '{print $1}')"
    primary_entry="${primary_entry%/}"

    if [[ -z "$primary_entry" ]]; then
        fail "SITE_ADDRESS is empty in '$ENV_FILE'."
    fi

    if [[ "$primary_entry" =~ ^https?:// ]]; then
        printf '%s/health\n' "${primary_entry%/}"
    else
        printf 'https://%s/health\n' "$primary_entry"
    fi
}

cd "$DEPLOY_DIR"

[[ -f "$ENV_FILE" ]] || fail "Production environment file '$ENV_FILE' was not found."
[[ -f "$COMPOSE_FILE" ]] || fail "Compose file '$COMPOSE_FILE' was not found."
[[ -f "$BACKUP_SCRIPT_PATH" ]] || fail "Backup script '$BACKUP_SCRIPT_PATH' was not found."

git rev-parse --is-inside-work-tree >/dev/null 2>&1 || fail "'$DEPLOY_DIR' is not a Git repository."

git remote get-url "$REMOTE_NAME" >/dev/null 2>&1 || fail "Git remote '$REMOTE_NAME' is not configured."
current_branch="$(git branch --show-current)"
[[ "$current_branch" == "$PRODUCTION_BRANCH" ]] || fail "Expected checked out branch '$PRODUCTION_BRANCH', but found '$current_branch'."

if ! git diff --quiet --ignore-submodules HEAD -- || ! git diff --cached --quiet --ignore-submodules --; then
    fail "Tracked local Git changes were found on the VPS. Resolve them before deploying."
fi

set -a
# shellcheck disable=SC1090
source "$ENV_FILE"
set +a

: "${SITE_ADDRESS:?SITE_ADDRESS must be set in $ENV_FILE}"
HEALTH_URL="$(get_health_url "$SITE_ADDRESS")"

COMPOSE_ARGS=(docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE")
COMPOSE_READY=1

previous_commit="$(git rev-parse HEAD)"
previous_commit_short="$(git rev-parse --short HEAD)"

log "Fetching branch '$PRODUCTION_BRANCH' from '$REMOTE_NAME'."
git fetch --prune "$REMOTE_NAME" "$PRODUCTION_BRANCH"

if ! git cat-file -e "${TARGET_SHA_LOWER}^{commit}" 2>/dev/null; then
    log "Requested commit '$TARGET_SHA_LOWER' was not included in the branch fetch. Attempting a direct fetch."
    git fetch --no-tags "$REMOTE_NAME" "$TARGET_SHA_LOWER" || true
fi

if ! git cat-file -e "${TARGET_SHA_LOWER}^{commit}" 2>/dev/null; then
    fail "Requested commit '$TARGET_SHA_LOWER' is not available from '$REMOTE_NAME'."
fi

if ! git merge-base --is-ancestor HEAD "$TARGET_SHA_LOWER"; then
    fail "Requested commit '$TARGET_SHA_LOWER' is not a fast-forward from the current production commit '$previous_commit_short'."
fi

if ! git merge-base --is-ancestor "$TARGET_SHA_LOWER" "$REMOTE_NAME/$PRODUCTION_BRANCH"; then
    fail "Requested commit '$TARGET_SHA_LOWER' is not contained in '$REMOTE_NAME/$PRODUCTION_BRANCH'."
fi

log "Ensuring SQL Server service is running."
"${COMPOSE_ARGS[@]}" up -d sqlserver
wait_for_service_health sqlserver 30 5

log "Creating a verified SQL Server backup before deployment."
DEPLOY_DIR="$DEPLOY_DIR" ENV_FILE="$ENV_FILE" COMPOSE_FILE="$COMPOSE_FILE" bash "$BACKUP_SCRIPT_PATH" >/dev/null

log "Updating branch '$PRODUCTION_BRANCH' to tested commit '$TARGET_SHA_LOWER'."
if [[ "$previous_commit" != "$TARGET_SHA_LOWER" ]]; then
    git merge --ff-only "$TARGET_SHA_LOWER"
else
    log "Production repository is already at the requested commit."
fi

log "Building updated production web image."
"${COMPOSE_ARGS[@]}" build web

log "Stopping web service for the migration window."
"${COMPOSE_ARGS[@]}" stop web
WEB_WAS_STOPPED=1

log "Applying pending EF Core migrations."
"${COMPOSE_ARGS[@]}" --profile migration run --rm migrate

log "Starting updated web service."
"${COMPOSE_ARGS[@]}" up -d web
wait_for_service_health web 30 5
WEB_WAS_STOPPED=0

log "Ensuring Caddy is running with the current configuration."
"${COMPOSE_ARGS[@]}" up -d caddy

log "Performing public health check through Caddy: $HEALTH_URL"
wait_for_public_health "$HEALTH_URL" 12 5

log "Deployed Git commit: $(git rev-parse HEAD)"
"${COMPOSE_ARGS[@]}" ps
