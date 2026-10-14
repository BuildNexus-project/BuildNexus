#!/usr/bin/env bash
# Runs every automated test stage in the same order CI does, then prints one
# PASS/FAIL table with timings. Exits non-zero if any stage failed.
#
#   scripts/run-all-tests.sh                 all stages
#   scripts/run-all-tests.sh unit frontend   only the named stages
#
# Stages: unit, integration, frontend (frontend = lint + tests)
#
# Needs: the .NET 10 SDK, Node 22 (.nvmrc), Docker Desktop (integration stage
# only) and infra/.env (copy infra/.env.example).

set -u
cd "$(dirname "$0")/.."

LOG_DIR="$(mktemp -d)"

# The six backend test projects. A project added later must be added here, or it
# is never run locally (the same rule ci.yml has).
UNIT_PROJECTS=(
  services/user-service-tests/UserService.Tests.csproj
  services/project-service-tests/ProjectService.Tests.csproj
  services/design-service-tests/DesignService.Tests.csproj
  services/construction-service-tests/ConstructionService.Tests.csproj
  services/payment-service-tests/PaymentService.Tests.csproj
  api-gateway-tests/ApiGateway.Tests.csproj
)

# The api-gateway tests have no integration category, so it is not in this list.
INTEGRATION_PROJECTS=(
  services/user-service-tests/UserService.Tests.csproj
  services/project-service-tests/ProjectService.Tests.csproj
  services/design-service-tests/DesignService.Tests.csproj
  services/construction-service-tests/ConstructionService.Tests.csproj
  services/payment-service-tests/PaymentService.Tests.csproj
)

# The results table: one entry per stage, filled in as each finishes.
NAMES=()
RESULTS=()
TIMES=()
COUNTS=()
ANY_FAILED=0

# record <name> <exit-code> <seconds> <test-count>
record() {
  NAMES+=("$1")
  TIMES+=("$3s")
  COUNTS+=("$4")
  if [ "$2" -eq 0 ]; then RESULTS+=("PASS"); else RESULTS+=("FAIL"); ANY_FAILED=1; fi
}

# Adds up every "Total: N" line dotnet test prints, one per test project.
count_dotnet() { grep -aoE 'Total: +[0-9]+' "$1" | awk '{ s += $2 } END { print s + 0 }'; }

# Reads vitest's "Tests  12 passed (12)" line and returns the number in brackets.
count_vitest() { grep -aE '^ *Tests ' "$1" | grep -oE '\([0-9]+\)' | tr -d '()' | tail -1; }

# run_dotnet_projects <category-filter> <log> <project>...
# Runs each project even if an earlier one failed, so the table shows every result.
run_dotnet_projects() {
  local filter="$1" log="$2" status=0
  shift 2
  for project in "$@"; do
    echo "--- dotnet test $project ($filter)"
    dotnet test "$project" -c Release --filter "$filter" 2>&1 | tee -a "$log"
    [ "${PIPESTATUS[0]}" -eq 0 ] || status=1
  done
  return $status
}

stage_unit() {
  local log="$LOG_DIR/unit.log" start=$SECONDS status
  : > "$log"
  run_dotnet_projects "Category!=Integration" "$log" "${UNIT_PROJECTS[@]}"
  status=$?
  record "Backend unit tests" "$status" $((SECONDS - start)) "$(count_dotnet "$log")"
}

# Reads infra/.env into the environment without executing it as shell code.
load_env_file() {
  local line key value
  while IFS= read -r line || [ -n "$line" ]; do
    case "$line" in ''|'#'*) continue ;; esac
    key="${line%%=*}"
    value="${line#*=}"
    export "$key=$value"
  done < infra/.env
}

stage_integration() {
  local log="$LOG_DIR/integration.log" start=$SECONDS status
  : > "$log"

  if [ ! -f infra/.env ]; then
    echo "infra/.env is missing. Copy infra/.env.example to infra/.env first." | tee -a "$log"
    record "Backend integration tests" 1 $((SECONDS - start)) 0
    return
  fi
  load_env_file

  # The tests find Kafka here. The database ports are the compose file's own.
  export Kafka__BootstrapServers=localhost:29092

  local compose=(docker compose -f infra/docker-compose.yml)
  # A MySQL already running on this machine holds port 3306, which user-db also
  # wants. The override file moves user-db to 3308, and the user-service tests are
  # told where it went. CI has no clash and uses neither.
  if (echo > /dev/tcp/127.0.0.1/3306) 2>/dev/null; then
    echo "Port 3306 is in use here: moving user-db to 3308 with the override file."
    compose+=(-f infra/docker-compose.override.yml)
    export USER_DB_PORT=3308
  fi

  local infra=(user-db project-db design-db construction-db payment-db kafka)
  echo "--- starting ${infra[*]}"
  if "${compose[@]}" up -d --wait --wait-timeout 180 "${infra[@]}" 2>&1 | tee -a "$log" \
      && [ "${PIPESTATUS[0]}" -eq 0 ]; then
    run_dotnet_projects "Category=Integration" "$log" "${INTEGRATION_PROJECTS[@]}"
    status=$?
  else
    status=1
  fi

  # Stopped, not removed, so the databases keep their data between runs.
  "${compose[@]}" stop "${infra[@]}" > /dev/null 2>&1
  record "Backend integration tests" "$status" $((SECONDS - start)) "$(count_dotnet "$log")"
}

stage_frontend() {
  local lint_log="$LOG_DIR/lint.log" test_log="$LOG_DIR/frontend.log" start status
  : > "$lint_log"; : > "$test_log"

  # The frontend tests only start on the Node version in .nvmrc. On another one,
  # every test file dies before it runs, which looks like hundreds of failures.
  local want have
  want="$(tr -d 'v 
' < .nvmrc | cut -d. -f1)"
  have="$(node --version | tr -d 'v' | cut -d. -f1)"
  if [ "$want" != "$have" ]; then
    echo "Frontend needs Node $want (.nvmrc) but this shell has Node $have. Run 'nvm use' and try again." | tee -a "$lint_log"
    record "Frontend lint" 1 0 "-"
    record "Frontend tests" 1 0 "-"
    return
  fi

  if [ ! -d frontend/node_modules ]; then
    echo "--- npm ci"
    (cd frontend && npm ci --ignore-scripts) 2>&1 | tee -a "$lint_log"
  fi

  start=$SECONDS
  echo "--- npm run lint"
  (cd frontend && npm run lint) 2>&1 | tee -a "$lint_log"
  status=${PIPESTATUS[0]}
  record "Frontend lint" "$status" $((SECONDS - start)) "-"

  start=$SECONDS
  echo "--- npm test"
  (cd frontend && npm test) 2>&1 | tee -a "$test_log"
  status=${PIPESTATUS[0]}
  record "Frontend tests" "$status" $((SECONDS - start)) "$(count_vitest "$test_log")"
}

print_summary() {
  echo
  echo "================ TEST SUMMARY ================"
  printf '%-28s %-6s %-8s %s\n' "Stage" "Result" "Time" "Tests"
  printf '%-28s %-6s %-8s %s\n' "----------------------------" "------" "--------" "-----"
  local i
  for i in "${!NAMES[@]}"; do
    printf '%-28s %-6s %-8s %s\n' "${NAMES[$i]}" "${RESULTS[$i]}" "${TIMES[$i]}" "${COUNTS[$i]}"
  done
  echo "=============================================="
  echo "Full logs: $LOG_DIR"
  if [ "$ANY_FAILED" -eq 0 ]; then echo "OVERALL: PASS"; else echo "OVERALL: FAIL"; fi
}

# ---- main ---------------------------------------------------------------

STAGES=("$@")
[ "${#STAGES[@]}" -eq 0 ] && STAGES=(unit integration frontend)

for stage in "${STAGES[@]}"; do
  case "$stage" in
    unit|integration|frontend) ;;
    *) echo "Unknown stage '$stage'. Use: unit integration frontend"; exit 2 ;;
  esac
done

for stage in "${STAGES[@]}"; do
  "stage_$stage"
done

print_summary
exit "$ANY_FAILED"
