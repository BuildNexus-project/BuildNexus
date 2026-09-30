#!/usr/bin/env bash
# Runs project-performance-test-plan.jmx (SCRUM-45 / US-32) against a real
# running BuildNexus environment and writes a raw results file plus an HTML
# dashboard report. Not part of ci.yml on purpose — see infra/performance-tests/README.md
# for why performance tests run on demand rather than on every push.
#
# Usage:
#   ./run-performance-tests.sh                                   # local docker-compose stack, defaults
#   ./run-performance-tests.sh -Jproject_host=my-project-service.example.com -Jproject_threads=50
#   ./run-performance-tests.sh -Jenv_label=azure -Jauth_protocol=https -Jauth_host=... [...]
#
# There is no single target host/port/protocol: the Gateway and frontend
# aren't deployed yet, so this plan points at User, Project and Design
# Service directly, each with its own auth_*/project_*/design_* protocol,
# host and port triple - see the property table in README.md.
#
# -Jenv_label (default "local") picks results/<env_label>/ as the output
# directory, so a run against Azure writes to results/azure/ instead of
# overwriting the local baseline in results/local/ - see AZURE-RESULTS.md
# and RESULTS.md, which are transcribed from those two directories
# respectively and never from each other's.
#
# JMeter itself is not vendored in this repository (its binary distribution is
# ~80 MB) - install it and put it on PATH, or point JMETER_CMD at the jmeter
# executable, before running this script:
#   https://jmeter.apache.org/download_jmeter.cgi
#
# JMETER_CMD, not JMETER_BIN: jmeter.bat sets its own JMETER_BIN internally
# from its own location, and inherits (rather than overwrites) one already
# present in the environment - naming this variable JMETER_BIN would leak in
# and point the launcher at the wrong jar.

set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")"

JMETER_CMD="${JMETER_CMD:-jmeter}"

# Checked by actually invoking it, not command -v: on Windows, jmeter.bat is
# runnable without carrying a Unix executable bit, which command -v requires.
if ! "$JMETER_CMD" --version >/dev/null 2>&1; then
  echo "error: could not run '$JMETER_CMD'." >&2
  echo "Install JMeter (https://jmeter.apache.org/download_jmeter.cgi) or set JMETER_CMD to its full path (jmeter.bat on Windows)." >&2
  exit 1
fi

# Parsed out of "$@" rather than left to JMeter alone: this decides where the
# *shell script* writes results, and JMeter has no way to hand that back out
# to the script that invoked it. Still passed through to jmeter unchanged
# below, so it's also available inside the plan as ${__P(env_label,local)} if
# a later story wants it (e.g. tagged into a sampler label).
ENV_LABEL="local"
for arg in "$@"; do
  case "$arg" in
    -Jenv_label=*)
      ENV_LABEL="${arg#-Jenv_label=}"
      ;;
  esac
done

RESULTS_DIR="results/$ENV_LABEL"
REPORT_DIR="$RESULTS_DIR/report"
RESULTS_FILE="$RESULTS_DIR/results.jtl"

# jmeter -e -o refuses to write into a report directory that already exists,
# and -l refuses to write into a results file that already has data in it -
# both from a previous run with this same env_label.
rm -rf "$REPORT_DIR"
rm -f "$RESULTS_FILE"
mkdir -p "$RESULTS_DIR"

echo "Running JMeter test plan for env_label=$ENV_LABEL (auth/project/design targets default to http://localhost:5000 unless -Jauth_host/-Jproject_host/-Jdesign_host, etc. override them)..."

"$JMETER_CMD" -n -t project-performance-test-plan.jmx \
  -l "$RESULTS_FILE" \
  -e -o "$REPORT_DIR" \
  "$@"

echo
echo "Raw results:    $RESULTS_DIR/results.jtl"
echo "HTML dashboard: $REPORT_DIR/index.html"
