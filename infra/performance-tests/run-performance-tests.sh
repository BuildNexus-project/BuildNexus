#!/usr/bin/env bash
# Runs project-performance-test-plan.jmx (SCRUM-45 / US-32) against a real
# running BuildNexus environment and writes a raw results file plus an HTML
# dashboard report. Not part of ci.yml on purpose — see infra/performance-tests/README.md
# for why performance tests run on demand rather than on every push.
#
# Usage:
#   ./run-performance-tests.sh                                   # local docker-compose stack, defaults
#   ./run-performance-tests.sh -Jhost=my-gateway.example.com -Jproject_threads=50
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

RESULTS_DIR="results"
REPORT_DIR="$RESULTS_DIR/report"
RESULTS_FILE="$RESULTS_DIR/results.jtl"

# jmeter -e -o refuses to write into a report directory that already exists.
rm -rf "$REPORT_DIR"
mkdir -p "$RESULTS_DIR"

echo "Running JMeter test plan (target defaults to http://localhost:5000 unless -Jhost/-Jport/-Jprotocol override it)..."

"$JMETER_CMD" -n -t project-performance-test-plan.jmx \
  -l "$RESULTS_FILE" \
  -e -o "$REPORT_DIR" \
  "$@"

echo
echo "Raw results:    $RESULTS_DIR/results.jtl"
echo "HTML dashboard: $REPORT_DIR/index.html"
