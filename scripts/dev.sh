#!/usr/bin/env bash
# Development helper: make sure the SQL Server container is running, then start
# Emplyo. SQL Server is an external dependency; this script only starts an
# existing container, it never creates or removes one.
#
# Usage:
#   scripts/dev.sh                      # ensure DB, then dotnet run the web app
#   scripts/dev.sh migrate              # ensure DB, then run a maintenance command
#   scripts/dev.sh --ensure-db          # ensure DB only (used by the VS Code task)
#
# Configuration (environment variables):
#   EMPLYO_SQL_CONTAINER  container name (default: mssql)
#   EMPLYO_SQL_PORT       host port      (default: 1433)
#   EMPLYO_SKIP_DB_CHECK  set to 1 when using a SQL Server not managed by Docker
set -eu

container="${EMPLYO_SQL_CONTAINER:-mssql}"
port="${EMPLYO_SQL_PORT:-1433}"
timeout_seconds=60

ensure_only=false
if [[ "${1:-}" == "--ensure-db" ]]; then
  ensure_only=true
  shift
fi

if [[ "${EMPLYO_SKIP_DB_CHECK:-0}" != "1" ]]; then
  if ! command -v docker >/dev/null 2>&1; then
    echo "error: docker is required to manage the '$container' SQL Server container." >&2
    echo "       Set EMPLYO_SKIP_DB_CHECK=1 if you use a SQL Server outside Docker." >&2
    exit 1
  fi

  if ! docker inspect "$container" >/dev/null 2>&1; then
    echo "error: Docker container '$container' does not exist." >&2
    echo "       See README.md (SQL Server setup) to create your instance," >&2
    echo "       or set EMPLYO_SKIP_DB_CHECK=1 to skip this check." >&2
    exit 1
  fi

  if [[ "$(docker inspect -f '{{.State.Running}}' "$container")" != "true" ]]; then
    echo "Starting SQL Server container '$container'..."
    docker start "$container" >/dev/null
  fi

  started_at="$(docker inspect -f '{{.State.StartedAt}}' "$container")"

  # Docker's port forwarder accepts TCP connections before SQL Server is ready
  # to serve logins, so for SQL Server images also wait for its own readiness
  # log line (newer than the current container start).
  wait_for_readiness_log=false
  if [[ "$(docker inspect -f '{{.Config.Image}}' "$container")" == *sql* ]]; then
    wait_for_readiness_log=true
  fi

  ready=false
  printf "Waiting for SQL Server on localhost:%s" "$port"
  for _ in $(seq 1 "$timeout_seconds"); do
    if (echo > "/dev/tcp/127.0.0.1/$port") 2>/dev/null; then
      if [[ "$wait_for_readiness_log" == "false" ]] \
        || docker logs --since "$started_at" "$container" 2>&1 | grep -q "ready for client connections"; then
        ready=true
        echo " ready"
        break
      fi
    fi
    printf "."
    sleep 1
  done

  if [[ "$ready" != "true" ]]; then
    echo
    echo "error: SQL Server did not become reachable on localhost:$port within ${timeout_seconds}s." >&2
    exit 1
  fi
fi

if [[ "$ensure_only" == "true" ]]; then
  exit 0
fi

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

exec dotnet run --project src/Emplyo.Web -- "$@"
