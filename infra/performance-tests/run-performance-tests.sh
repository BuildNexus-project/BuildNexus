#!/usr/bin/env bash
# Resolve a named profile, prepare real API data, then enforce the JTL quality gate.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"

if (( BASH_VERSINFO[0] < 4 )); then
  echo 'error: Bash 4+ is required (Git Bash on Windows is supported).' >&2
  exit 2
fi

PROFILE=smoke
for arg in "$@"; do
  case "$arg" in
    --help|-h)
      cat <<'USAGE'
Usage: ./run-performance-tests.sh [-Jprofile=smoke|load|stress] [-Jproperty=value ...]

Default: smoke (6 users, 48 expected samples). Load assumes 20 users.
Stress scales load users by 1,2,4,8 and stops at the first failed quality gate.
Override -Jstress_multipliers=1,2 to run fewer steps.

Use -Jgateway_host/-Jgateway_port/-Jgateway_protocol to select the gateway.
Use -Jenv_label=local to choose the results parent; each run is preserved.
Profile settings can be overridden with -Jproject_threads, -Jjourney_loops,
-Jp95_login_ms, -Jmax_error_rate_pct, and the properties listed in README.md.
Set JMETER_CMD and PYTHON_CMD if JMeter/Python 3.10+ are not on PATH.
ADMIN_EMAIL/ADMIN_PASSWORD or -Jadmin_email/-Jadmin_password select the Admin.
Exit: 0 PASS, 1 execution/gate failure, 2 invalid configuration.
USAGE
      exit 0 ;;
    -Jprofile=*) PROFILE="${arg#-Jprofile=}" ;;
  esac
done
case "$PROFILE" in smoke|load|stress) ;; *) echo 'error: profile must be smoke, load or stress.' >&2; exit 2 ;; esac

# Only public, supported settings may enter the retained properties file.
is_setting_key() {
  case "$1" in
    profile|stress_multipliers|env_label|fixture_timeout|connect_timeout_ms|response_timeout_ms|max_error_rate_pct) return 0 ;;
    gateway_protocol|gateway_host|gateway_port|auth_protocol|auth_host|auth_port|project_protocol|project_host|project_port|design_protocol|design_host|design_port) return 0 ;;
  esac
  [[ "$1" =~ ^(project|design|journey|report)_(threads|loops|rampup|think_ms)$ ]] \
    || [[ "$1" =~ ^p95_(project_create|design_upload|login|project_list|project_detail|invoices|history|dashboard|progress|reports)_ms$ ]]
}

declare -A PERF_CONFIG=()
while IFS= read -r line || [[ -n "$line" ]]; do
  line="${line%$'\r'}"
  [[ -z "$line" || "$line" == \#* ]] && continue
  key="${line%%=*}"
  value="${line#*=}"
  if [[ "$line" != *=* || "$value" =~ ^[[:space:]] || "$value" =~ [[:space:]]$ ]] \
      || ! is_setting_key "$key"; then
    echo 'error: invalid profile property; see profiles/.' >&2
    exit 2
  fi
  PERF_CONFIG["$key"]="$value"
done < "profiles/$PROFILE.properties"
PERF_CONFIG[profile]="$PROFILE"
PERF_CONFIG[multiplier]=1
PERF_CONFIG[env_label]=local
PERF_CONFIG[gateway_protocol]=http
PERF_CONFIG[gateway_host]=localhost
PERF_CONFIG[gateway_port]=5000
ADMIN_EMAIL_VALUE="${ADMIN_EMAIL:-admin@buildnexus.local}"
ADMIN_PASSWORD_VALUE="${ADMIN_PASSWORD:-ChangeMe123!}"

for arg in "$@"; do
  case "$arg" in
    -Jadmin_email=*) ADMIN_EMAIL_VALUE="${arg#-Jadmin_email=}"; continue ;;
    -Jadmin_password=*) ADMIN_PASSWORD_VALUE="${arg#-Jadmin_password=}"; continue ;;
    -J*=*) key="${arg#-J}"; key="${key%%=*}"; value="${arg#*=}" ;;
    *) echo 'error: use -Jproperty=value; see --help.' >&2; exit 2 ;;
  esac
  if ! is_setting_key "$key" || [[ "$value" == *$'\n'* || "$value" == *$'\r'* \
      || "$value" =~ ^[[:space:]] || "$value" =~ [[:space:]]$ ]]; then
    echo 'error: unsupported property or surrounding/multiline whitespace; see --help.' >&2
    exit 2
  fi
  PERF_CONFIG["$key"]="$value"
done
for target in auth project design; do
  for field in protocol host port; do
    key="${target}_${field}"
    gateway_key="gateway_${field}"
    if [[ -z "${PERF_CONFIG[$key]+present}" ]]; then
      PERF_CONFIG["$key"]="${PERF_CONFIG[$gateway_key]}"
    fi
  done
done
ENV_LABEL="${PERF_CONFIG[env_label]}"
if [[ ! "$ENV_LABEL" =~ ^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$ ]]; then
  echo 'error: env_label must be 1-64 letters, digits, hyphens or underscores.' >&2
  exit 2
fi

if [[ -z "${PYTHON_CMD:-}" ]]; then
  if python3 -c 'import sys; sys.exit(sys.version_info < (3, 10))' >/dev/null 2>&1; then
    PYTHON_CMD=python3
  else
    PYTHON_CMD=python
  fi
fi
if ! "$PYTHON_CMD" -c 'import sys; sys.exit(sys.version_info < (3, 10))' >/dev/null 2>&1; then
  echo 'error: Python 3.10+ is required; set PYTHON_CMD.' >&2
  exit 1
fi

mkdir -p "results/$ENV_LABEL"
RESULTS_DIR=$(mktemp -d "results/$ENV_LABEL/$(date -u +%Y%m%dT%H%M%SZ)-XXXXXX")
FIXTURE_FILE="$RESULTS_DIR/fixtures.csv"
cleanup() { rm -f -- "$FIXTURE_FILE"; }
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

# Every step saves the exact public settings used for both JMeter and its gate.
write_config() {
  local output="$1" factor="$2" setting resolved
  for setting in "${!PERF_CONFIG[@]}"; do
    resolved="${PERF_CONFIG[$setting]}"
    case "$setting" in
      project_threads|design_threads|journey_threads|report_threads)
        if [[ "$factor" != 1 ]]; then resolved=$(( 10#$resolved * factor )); fi ;;
      multiplier) resolved="$factor" ;;
    esac
    printf '%s=%s\n' "$setting" "$resolved"
  done | LC_ALL=C sort > "$output"
}
write_config "$RESULTS_DIR/effective.properties" 1
"$PYTHON_CMD" check-performance.py --validate-config "$RESULTS_DIR/effective.properties" >/dev/null

SCALE_FACTORS=(1)
if [[ "$PROFILE" == stress ]]; then
  IFS=',' read -r -a SCALE_FACTORS <<< "${PERF_CONFIG[stress_multipliers]}"
fi
LAST_FACTOR="${SCALE_FACTORS[${#SCALE_FACTORS[@]}-1]}"
for group in project design journey report; do
  key="${group}_threads"
  if (( 10#${PERF_CONFIG[$key]} * LAST_FACTOR > 9999 )); then
    echo 'error: a stress step exceeds the supported maximum of 9999 threads per group.' >&2
    exit 2
  fi
done
FIXTURE_USERS=$(( 10#${PERF_CONFIG[journey_threads]} * LAST_FACTOR ))
REPORT_FIXTURES=$(( 10#${PERF_CONFIG[report_threads]} * LAST_FACTOR ))
if (( REPORT_FIXTURES > FIXTURE_USERS )); then FIXTURE_USERS=$REPORT_FIXTURES; fi

JMETER_CMD="${JMETER_CMD:-jmeter}"
if ! "$JMETER_CMD" --version >/dev/null 2>&1; then
  echo "error: could not run '$JMETER_CMD'; install JMeter or set JMETER_CMD." >&2
  exit 1
fi
BASE_URL="${PERF_CONFIG[gateway_protocol]}://${PERF_CONFIG[gateway_host]}:${PERF_CONFIG[gateway_port]}"
echo "Profile: $PROFILE; run directory: $RESULTS_DIR"
if (( FIXTURE_USERS > 0 )); then
  echo "Preparing $FIXTURE_USERS Client/project fixtures through $BASE_URL..."
  ADMIN_EMAIL="$ADMIN_EMAIL_VALUE" ADMIN_PASSWORD="$ADMIN_PASSWORD_VALUE" \
    "$PYTHON_CMD" prepare-fixtures.py --base-url "$BASE_URL" --users "$FIXTURE_USERS" \
    --output "$FIXTURE_FILE" --poll-timeout "${PERF_CONFIG[fixture_timeout]}"
fi

LAST_GATE_STATUS=0
run_step() {
  local step_dir="$1" factor="$2" jmeter_status=0
  mkdir -p "$step_dir"
  write_config "$step_dir/effective.properties" "$factor"
  echo "Running $PROFILE at concurrency multiplier $factor..."
  # -q loads the resolved profile. Managed CSV output settings make gate input
  # deterministic. Credentials stay in the child environment, outside logs.
  ADMIN_EMAIL="$ADMIN_EMAIL_VALUE" ADMIN_PASSWORD="$ADMIN_PASSWORD_VALUE" \
    "$JMETER_CMD" -n -t project-performance-test-plan.jmx \
    -q "$step_dir/effective.properties" \
    -l "$step_dir/results.jtl" -j "$step_dir/jmeter.log" -e -o "$step_dir/report" \
    "-Jfixture_csv=$FIXTURE_FILE" \
    -Jjmeter.save.saveservice.output_format=csv \
    -Jjmeter.save.saveservice.print_field_names=true \
    -Jjmeter.save.saveservice.timestamp_format=ms \
    -Jjmeter.save.saveservice.time=true -Jjmeter.save.saveservice.label=true \
    -Jjmeter.save.saveservice.successful=true \
    -Jjmeter.save.saveservice.assertion_results_failure_message=true \
    || jmeter_status=$?
  if "$PYTHON_CMD" check-performance.py --config "$step_dir/effective.properties" \
      --results "$step_dir/results.jtl" --output-dir "$step_dir" \
      --jmeter-exit-code "$jmeter_status" --dashboard "$step_dir/report/index.html"; then
    LAST_GATE_STATUS=0
  else
    LAST_GATE_STATUS=$?
  fi
  echo "Summary: $step_dir/summary.md"
  echo "HTML dashboard: $step_dir/report/index.html"
}

if [[ "$PROFILE" != stress ]]; then
  run_step "$RESULTS_DIR" 1
  exit "$LAST_GATE_STATUS"
fi

step_index=0
for factor in "${SCALE_FACTORS[@]}"; do
  step_index=$(( step_index + 1 ))
  printf -v step_name 'step-%02d-x%s' "$step_index" "$factor"
  run_step "$RESULTS_DIR/$step_name" "$factor"
  if (( LAST_GATE_STATUS != 0 )); then
    echo 'Stopping stress after the first failed step; inspect its summary for the cause.'
    break
  fi
done
"$PYTHON_CMD" check-performance.py --stress-summary "$RESULTS_DIR"
