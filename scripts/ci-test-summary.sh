#!/usr/bin/env bash
# Prints the pipeline's combined test summary as Markdown. The `test-report` job in
# .github/workflows/ci.yml appends this to $GITHUB_STEP_SUMMARY.
#
# Environment (set by the job):
#   UNIT_RESULT INTEGRATION_RESULT FRONTEND_RESULT DOCKER_RESULT STATIC_RESULT
#       needs.<job>.result: success, failure, cancelled or skipped
#   STATIC_RAN  "true" if the SonarCloud analysis really ran, "false" if it was skipped
#               because the SONAR_TOKEN secret is not set
#   RUN_URL     link to this workflow run, where the artifacts are listed
# Arguments:
#   $1  folder the artifacts were downloaded into (default: reports)
#         reports/unit/*.trx          backend unit test results
#         reports/integration/*.trx   backend integration test results
#         reports/frontend/test-results.json   vitest results
#         reports/coverage/Summary.json        ReportGenerator's per-service coverage

set -u
REPORTS="${1:-reports}"

# success -> "PASS", failure -> "FAIL", anything else is shown as it came.
verdict() {
  case "${1:-skipped}" in
    success) echo "✅ PASS" ;;
    failure) echo "❌ FAIL" ;;
    *)       echo "⏭️ ${1:-skipped}" ;;
  esac
}

# Sums the <Counters total= passed= failed=> line of every .trx file under a folder,
# and prints "total passed failed", or "-" when there are none.
backend_counts() {
  local files
  files=$(find "$1" -name '*.trx' 2>/dev/null)
  if [ -z "$files" ]; then echo "- - -"; return; fi
  # shellcheck disable=SC2086
  grep -ho '<Counters [^>]*>' $files | awk '
    {
      for (i = 1; i <= NF; i++) {
        split($i, kv, "=")
        gsub(/"/, "", kv[2])
        if (kv[1] == "total")  total  += kv[2]
        if (kv[1] == "passed") passed += kv[2]
        if (kv[1] == "failed") failed += kv[2]
      }
    }
    END { printf "%d %d %d\n", total, passed, failed }'
}

# vitest writes numTotalTests, numPassedTests and numFailedTests to its JSON report.
frontend_counts() {
  local file="$1/test-results.json"
  if [ ! -f "$file" ]; then echo "- - -"; return; fi
  node -e "
    const r = require(require('path').resolve(process.argv[1]));
    console.log(r.numTotalTests + ' ' + r.numPassedTests + ' ' + r.numFailedTests);
  " "$file"
}

# Prints one table row per backend service (ReportGenerator's "assemblies"): line and
# branch coverage from unit and integration tests together. Prints nothing if the
# report is missing.
coverage_table() {
  local file="$1/Summary.json"
  [ -f "$file" ] || return 0
  node -e "
    const assemblies = require(require('path').resolve(process.argv[1])).coverage.assemblies;
    for (const a of assemblies) {
      const name = a.name.replace('BuildNexus.', '');
      const lines = a.coverage + '% (' + a.coveredlines + '/' + a.coverablelines + ')';
      const branches = a.branchcoverage + '% (' + a.coveredbranches + '/' + a.totalbranches + ')';
      console.log('| ' + name + ' | ' + lines + ' | ' + branches + ' |');
    }
  " "$file"
}

row() { # <stage> <result> <total> <passed> <failed>
  echo "| $1 | $(verdict "$2") | $3 | $4 | $5 |"
}

read -r ut up uf <<< "$(backend_counts "$REPORTS/unit")"
read -r it ip if_ <<< "$(backend_counts "$REPORTS/integration")"
read -r ft fp ff <<< "$(frontend_counts "$REPORTS/frontend")"

echo "# Test pipeline summary"
echo
echo "| Stage | Result | Tests | Passed | Failed |"
echo "|---|---|---|---|---|"
row "Backend unit tests"        "${UNIT_RESULT:-}"        "$ut" "$up" "$uf"
row "Backend integration tests" "${INTEGRATION_RESULT:-}"  "$it" "$ip" "$if_"
row "Frontend lint + tests"     "${FRONTEND_RESULT:-}"     "$ft" "$fp" "$ff"
row "Docker build (6 images)"   "${DOCKER_RESULT:-}"       "-"   "-"   "-"
if [ "${STATIC_RAN:-}" = "false" ]; then
  echo "| Static analysis (SonarCloud) | ⏭️ skipped, no SONAR_TOKEN | - | - | - |"
else
  row "Static analysis (SonarCloud)" "${STATIC_RESULT:-}" "-" "-" "-"
fi
echo

per_service="$(coverage_table "$REPORTS/coverage")"
if [ -n "$per_service" ]; then
  echo "## Backend coverage per service"
  echo
  echo "Unit and integration tests together."
  echo
  echo "| Service | Line coverage | Branch coverage |"
  echo "|---|---|---|"
  echo "$per_service"
  echo
fi

echo "## Coverage reports and other artifacts"
echo
echo "Download them from the **Artifacts** section at the bottom of [this run](${RUN_URL:-#}):"
echo
echo "- \`backend-coverage-report\`: unit tests only, HTML"
echo "- \`backend-combined-coverage-report\`: unit + integration tests, HTML"
echo "- \`frontend-coverage-report\`: frontend coverage, HTML"

# Overall verdict: the job fails if any stage did.
for result in "${UNIT_RESULT:-}" "${INTEGRATION_RESULT:-}" "${FRONTEND_RESULT:-}" "${DOCKER_RESULT:-}" "${STATIC_RESULT:-}"; do
  if [ "$result" != "success" ]; then
    exit 1
  fi
done
