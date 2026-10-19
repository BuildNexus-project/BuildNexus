"""Keep the combined report honest when jobs or retained artifacts fail."""

import copy
import csv
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("ci_summary", ROOT / "ci-summary.py")
report = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(report)


class CiSummaryTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.reports = self.root / "reports"
        self.load = self.reports / "load"
        self.config = report.gate.read_config(ROOT / "profiles" / "smoke.properties")

    def measured_summary(self, failed=False, partial=False):
        rows = [[1000 + index * 100, 50, label, "true"]
                for label, count in report.gate.expected_counts(self.config).items()
                for index in range(count)]
        if failed:
            next(row for row in rows if row[2] == "Login Under Load")[3] = "false"
        if partial:
            rows = rows[:3]
        path = self.root / "results.jtl"
        with path.open("w", newline="", encoding="utf-8") as stream:
            writer = csv.writer(stream)
            writer.writerow(["timeStamp", "elapsed", "label", "success"])
            writer.writerows(rows)
        return report.gate.evaluate(self.config, path, 0)

    def save(self, summary, run="run-01"):
        path = self.load / run / "summary.json"
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(summary), encoding="utf-8")
        return path

    def test_success_reports_measured_workloads_separately_from_setup(self):
        self.save(self.measured_summary())
        result = report.inspect_report(self.load, "success")
        text = report.details(result)
        self.assertTrue(result["ok"])
        self.assertIn("48 / 48 requests", text)
        self.assertIn("6 virtual users", text)
        self.assertIn("Login Under Load", text)
        self.assertIn("Construction Reports", text)
        self.assertNotIn("Register Client |", text)

    def test_skipped_job_does_not_infer_metrics_from_stale_artifact(self):
        self.save(self.measured_summary())
        result = report.inspect_report(self.load, "skipped")
        self.assertTrue(result["ok"])
        self.assertIsNone(result["summary"])
        self.assertIn("No performance measurements", report.details(result))

    def test_missing_ambiguous_and_malformed_reports_fail_without_metrics(self):
        self.assertFalse(report.inspect_report(self.load, "success")["ok"])
        path = self.save(self.measured_summary())
        path.write_text("{incomplete", encoding="utf-8")
        self.assertIsNone(report.inspect_report(self.load, "success")["summary"])
        self.save(self.measured_summary())
        self.save(self.measured_summary(), run="run-02")
        self.assertIsNone(report.inspect_report(self.load, "success")["summary"])

    def test_failure_or_cancellation_cannot_be_overridden_by_passing_metrics(self):
        self.save(self.measured_summary())
        for status in ("failure", "cancelled"):
            with self.subTest(status=status):
                result = report.inspect_report(self.load, status)
                self.assertFalse(result["ok"])
                self.assertIsNotNone(result["summary"])
                self.assertIn("cannot override", report.details(result))

    def test_failed_and_partial_gates_retain_available_measurements(self):
        for options in ({"failed": True}, {"partial": True}):
            with self.subTest(options=options):
                self.save(self.measured_summary(**options))
                result = report.inspect_report(self.load, "success")
                self.assertFalse(result["ok"])
                self.assertEqual(result["summary"]["verdict"], "FAIL")
                self.assertIn("performance gate", result["row"])

    def test_inconsistent_totals_profile_limits_or_nonfinite_numbers_are_rejected(self):
        original = self.measured_summary()
        mutations = [
            lambda s: s.update(total_samples=47),
            lambda s: s.update(profile="load"),
            lambda s: s.update(virtual_users=True),
            lambda s: s.update(multiplier=True),
            lambda s: s.update(p95_ms=float("nan")),
            lambda s: s.update(error_rate_pct=0.1),
            lambda s: s["settings"].update(p95_login_ms=999999),
            lambda s: s["settings"].update(admin_password="must-not-appear"),
            lambda s: s["metrics"][0].update(label="Unknown Label"),
            lambda s: s["metrics"][-1]["limits"].update(p95_ms=999999),
        ]
        for index, mutate in enumerate(mutations):
            with self.subTest(index=index):
                summary = copy.deepcopy(original)
                mutate(summary)
                self.save(summary)
                result = report.inspect_report(self.load, "success")
                self.assertFalse(result["ok"])
                self.assertIsNone(result["summary"])
                self.assertNotIn("must-not-appear", report.details(result))

    def test_forged_pass_cannot_hide_endpoint_error_or_latency_breach(self):
        for failed in (True, False):
            with self.subTest(failed=failed):
                summary = self.measured_summary(failed=failed)
                metric = next(m for m in summary["metrics"] if m["label"] == "Login Under Load")
                if not failed:
                    metric["p95_ms"] = metric["limits"]["p95_ms"] + 1
                metric.update(verdict="PASS", reasons=[])
                summary.update(verdict="PASS", failures=[])
                self.save(summary)
                self.assertIsNone(report.inspect_report(self.load, "success")["summary"])

    def test_cli_exit_status_matches_the_report(self):
        self.save(self.measured_summary())
        for status, expected in (("success", 0), ("skipped", 0), ("failure", 1)):
            with self.subTest(status=status):
                process = subprocess.run(
                    [sys.executable, "-B", str(ROOT / "ci-summary.py"), "--reports-dir", str(self.load),
                     "--job-result", status, "--part", "row"],
                    env={**os.environ, "PYTHONIOENCODING": "utf-8"},
                    capture_output=True, encoding="utf-8", timeout=15)
                self.assertEqual(process.returncode, expected, process.stderr)
                self.assertIn("Load tests (JMeter smoke)", process.stdout)

    def test_combined_bash_summary_preserves_other_stages_and_propagates_load_failure(self):
        bash = (Path("C:/Program Files/Git/bin/bash.exe") if os.name == "nt"
                else shutil.which("bash"))
        if not bash or not Path(bash).is_file():
            self.skipTest("Bash is unavailable")
        environment = {**os.environ, "PYTHON_CMD": sys.executable.replace("\\", "/"),
                       "PYTHONIOENCODING": "utf-8", "STATIC_RAN": "false",
                       "MUTATION_RESULT": "skipped"}
        for key in ("UNIT", "INTEGRATION", "FRONTEND", "DOCKER", "STATIC"):
            environment[f"{key}_RESULT"] = "success"
        self.save(self.measured_summary())
        for status, expected in (("skipped", 0), ("success", 0), ("failure", 1)):
            with self.subTest(status=status):
                process = subprocess.run(
                    [str(bash), "scripts/ci-test-summary.sh", self.reports.as_posix()],
                    cwd=ROOT.parents[1], env={**environment, "LOAD_RESULT": status},
                    capture_output=True, encoding="utf-8", timeout=30)
                self.assertEqual(process.returncode, expected, process.stderr)
                self.assertIn("Backend unit tests", process.stdout)
                self.assertIn("Load tests (JMeter smoke)", process.stdout)
                self.assertEqual("48 / 48 requests" in process.stdout, status != "skipped")
        (self.load / "run-01" / "summary.json").unlink()
        process = subprocess.run(
            [str(bash), "scripts/ci-test-summary.sh", self.reports.as_posix()],
            cwd=ROOT.parents[1], env={**environment, "LOAD_RESULT": "success"},
            capture_output=True, encoding="utf-8", timeout=30)
        self.assertEqual(process.returncode, 1, process.stderr)
        self.assertIn("summary unavailable", process.stdout)


if __name__ == "__main__":
    unittest.main()
