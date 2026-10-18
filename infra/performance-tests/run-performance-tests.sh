#!/usr/bin/env bash
# Prepare real API data, then measure the JMeter workloads through the gateway.
# Setup requests are outside the JTL; each run keeps its own logs and dashboard.
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")"

ENV_LABEL=local
GATEWAY_PROTOCOL=http
GATEWAY_HOST=localhost
GATEWAY_PORT=5000
JOURNEY_THREADS=2
REPORT_THREADS=1
FIXTURE_TIMEOUT=60
ADMIN_EMAIL_VALUE="${ADMIN_EMAIL:-admin@buildnexus.local}"
ADMIN_PASSWORD_VALUE="${ADMIN_PASSWORD:-ChangeMe123!}"
JMETER_PROPERTIES=()

for arg in "$@"; do
  case "$arg" in
    --help|-h)
      cat <<'USAGE'
Usage: ./run-performance-tests.sh [-Jproperty=value ...]

Gateway: gateway_protocol=http gateway_host=localhost gateway_port=5000
Workloads: project_threads=20 design_threads=10 journey_threads=2 report_threads=1
Output: env_label=local (each run gets a separate timestamped directory)
Setup: fixture_timeout=60 (seconds per Kafka projection)

Set JMETER_CMD to the JMeter executable and PYTHON_CMD to Python 3 if needed.
ADMIN_EMAIL/ADMIN_PASSWORD or -Jadmin_email/-Jadmin_password select the Admin.
Use -Jjourney_threads=0 -Jreport_threads=0 to run the two legacy workloads only.
See README.md for loop, ramp-up and think-time properties and short validation.
USAGE
      exit 0 ;;
    -Jenv_label=*) ENV_LABEL="${arg#-Jenv_label=}" ;;
    -Jgateway_protocol=*) GATEWAY_PROTOCOL="${arg#-Jgateway_protocol=}" ;;
    -Jgateway_host=*) GATEWAY_HOST="${arg#-Jgateway_host=}" ;;
    -Jgateway_port=*) GATEWAY_PORT="${arg#-Jgateway_port=}" ;;
    -Jjourney_threads=*) JOURNEY_THREADS="${arg#-Jjourney_threads=}" ;;
    -Jreport_threads=*) REPORT_THREADS="${arg#-Jreport_threads=}" ;;
    -Jfixture_timeout=*) FIXTURE_TIMEOUT="${arg#-Jfixture_timeout=}" ;;
    -Jadmin_email=*) ADMIN_EMAIL_VALUE="${arg#-Jadmin_email=}" ;;
    -Jadmin_password=*) ADMIN_PASSWORD_VALUE="${arg#-Jadmin_password=}" ;;
    -Jauth_protocol=*) AUTH_PROTOCOL_VALUE="${arg#-Jauth_protocol=}" ;;
    -Jauth_host=*) AUTH_HOST_VALUE="${arg#-Jauth_host=}" ;;
    -Jauth_port=*) AUTH_PORT_VALUE="${arg#-Jauth_port=}" ;;
    -Jproject_protocol=*) PROJECT_PROTOCOL_VALUE="${arg#-Jproject_protocol=}" ;;
    -Jproject_host=*) PROJECT_HOST_VALUE="${arg#-Jproject_host=}" ;;
    -Jproject_port=*) PROJECT_PORT_VALUE="${arg#-Jproject_port=}" ;;
    -Jdesign_protocol=*) DESIGN_PROTOCOL_VALUE="${arg#-Jdesign_protocol=}" ;;
    -Jdesign_host=*) DESIGN_HOST_VALUE="${arg#-Jdesign_host=}" ;;
    -Jdesign_port=*) DESIGN_PORT_VALUE="${arg#-Jdesign_port=}" ;;
    -Jfixture_csv=*) echo 'error: fixture_csv is managed by this runner.' >&2; exit 2 ;;
    -J*=*) ;; # Other JMeter properties, including the existing service overrides.
    *) echo 'error: use -Jproperty=value; see --help.' >&2; exit 2 ;;
  esac
  # JMeter logs every -J value at INFO. Credentials travel in the child process
  # environment, and are JSON-encoded by the Admin login preprocessor.
  case "$arg" in
    -Jadmin_email=*|-Jadmin_password=*) ;;
    *) JMETER_PROPERTIES+=("$arg") ;;
  esac
done

if [[ ! "$ENV_LABEL" =~ ^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$ ]]; then
  echo 'error: env_label must be 1-64 letters, digits, hyphens or underscores.' >&2
  exit 2
fi
if [[ "$GATEWAY_PROTOCOL" != http && "$GATEWAY_PROTOCOL" != https ]] \
    || [[ ! "$GATEWAY_HOST" =~ ^[A-Za-z0-9][A-Za-z0-9.-]*$ ]] \
    || [[ ! "$GATEWAY_PORT" =~ ^[1-9][0-9]{0,4}$ ]] || (( GATEWAY_PORT > 65535 )); then
  echo 'error: supply an http/https gateway, a hostname and a port from 1 to 65535.' >&2
  exit 2
fi
for count in "$JOURNEY_THREADS" "$REPORT_THREADS"; do
  if [[ ! "$count" =~ ^(0|[1-9][0-9]{0,3})$ ]]; then
    echo 'error: journey_threads and report_threads must be integers from 0 to 9999.' >&2
    exit 2
  fi
done

JMETER_CMD="${JMETER_CMD:-jmeter}"
# Invoke the launcher: command -v is unreliable for Windows .bat executables.
if ! "$JMETER_CMD" --version >/dev/null 2>&1; then
  echo "error: could not run '$JMETER_CMD'; install JMeter or set JMETER_CMD." >&2
  exit 1
fi

FIXTURE_USERS=$JOURNEY_THREADS
if (( REPORT_THREADS > FIXTURE_USERS )); then FIXTURE_USERS=$REPORT_THREADS; fi
if (( FIXTURE_USERS > 0 )); then
  if [[ -z "${PYTHON_CMD:-}" ]]; then
    if python3 -c 'import sys; sys.exit(sys.version_info < (3, 10))' >/dev/null 2>&1; then
      PYTHON_CMD=python3
    else
      PYTHON_CMD=python
    fi
  fi
  if ! "$PYTHON_CMD" -c 'import sys; sys.exit(sys.version_info < (3, 10))' >/dev/null 2>&1; then
    echo 'error: Python 3.10+ is required to prepare cross-service fixtures; set PYTHON_CMD.' >&2
    exit 1
  fi
fi

mkdir -p "results/$ENV_LABEL"
RESULTS_DIR=$(mktemp -d "results/$ENV_LABEL/$(date -u +%Y%m%dT%H%M%SZ)-XXXXXX")
REPORT_DIR="$RESULTS_DIR/report"
RESULTS_FILE="$RESULTS_DIR/results.jtl"
FIXTURE_FILE="$RESULTS_DIR/fixtures.csv"
# The CSV contains generated test passwords. Keep it out of retained artifacts,
# including when fixture preparation or JMeter fails. Never delete prior runs.
cleanup() { rm -f -- "$FIXTURE_FILE"; }
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

BASE_URL="$GATEWAY_PROTOCOL://$GATEWAY_HOST:$GATEWAY_PORT"
echo "Run directory: $RESULTS_DIR"
if (( FIXTURE_USERS > 0 )); then
  echo "Preparing $FIXTURE_USERS Client/project fixtures through $BASE_URL..."
  ADMIN_EMAIL="$ADMIN_EMAIL_VALUE" ADMIN_PASSWORD="$ADMIN_PASSWORD_VALUE" \
    "$PYTHON_CMD" prepare-fixtures.py --base-url "$BASE_URL" \
    --users "$FIXTURE_USERS" --output "$FIXTURE_FILE" --poll-timeout "$FIXTURE_TIMEOUT"
fi

echo "Running JMeter through $BASE_URL (individual service overrides remain available)..."
ADMIN_EMAIL="$ADMIN_EMAIL_VALUE" ADMIN_PASSWORD="$ADMIN_PASSWORD_VALUE" \
  "$JMETER_CMD" -n -t project-performance-test-plan.jmx \
  -l "$RESULTS_FILE" -j "$RESULTS_DIR/jmeter.log" -e -o "$REPORT_DIR" \
  "${JMETER_PROPERTIES[@]}" \
  "-Jfixture_csv=$FIXTURE_FILE" \
  "-Jauth_protocol=${AUTH_PROTOCOL_VALUE:-$GATEWAY_PROTOCOL}" \
  "-Jauth_host=${AUTH_HOST_VALUE:-$GATEWAY_HOST}" "-Jauth_port=${AUTH_PORT_VALUE:-$GATEWAY_PORT}" \
  "-Jproject_protocol=${PROJECT_PROTOCOL_VALUE:-$GATEWAY_PROTOCOL}" \
  "-Jproject_host=${PROJECT_HOST_VALUE:-$GATEWAY_HOST}" "-Jproject_port=${PROJECT_PORT_VALUE:-$GATEWAY_PORT}" \
  "-Jdesign_protocol=${DESIGN_PROTOCOL_VALUE:-$GATEWAY_PROTOCOL}" \
  "-Jdesign_host=${DESIGN_HOST_VALUE:-$GATEWAY_HOST}" "-Jdesign_port=${DESIGN_PORT_VALUE:-$GATEWAY_PORT}"

echo
echo "Raw results:    $RESULTS_FILE"
echo "JMeter log:     $RESULTS_DIR/jmeter.log"
echo "HTML dashboard: $REPORT_DIR/index.html"
