"""Exercise gate decisions using synthetic JTLs, not a running service."""

import contextlib
import csv
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest


SCRIPT = Path(__file__).resolve().parents[1] / "check-performance.py"
SPEC = importlib.util.spec_from_file_location("performance_gate", SCRIPT)
gate = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(gate)


class PerformanceGateTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.properties = {
            "profile": "smoke", "fixture_timeout": "60",
            "connect_timeout_ms": "3000", "response_timeout_ms": "10000",
            "max_error_rate_pct": "0", "gateway_host": "localhost",
            "gateway_port": "5000", "gateway_protocol": "http", "env_label": "local",
        }
        for group in gate.GROUPS:
            for suffix, value in (("threads", "1"), ("loops", "2"), ("rampup", "0"), ("think_ms", "0")):
                self.properties[f"{group}_{suffix}"] = value
        for key in gate.LIMITS.values():
            self.properties[key] = "100"

    def config(self, **updates):
        properties = {**self.properties, **updates}
        path = self.root / "effective.properties"
        path.write_text("\n".join(f"{key}={value}" for key, value in properties.items()), encoding="utf-8")
        return gate.read_config(path)

    def samples(self, config):
        return [[1000 + index * 100, 50, label, "true"]
                for label, count in gate.expected_counts(config).items() for index in range(count)]

    def write_jtl(self, rows, name="results.jtl"):
        path = self.root / name
        with path.open("w", newline="", encoding="utf-8") as stream:
            writer = csv.writer(stream)
            writer.writerow(["timeStamp", "elapsed", "label", "success"])
            writer.writerows(rows)
        return path

    def metric(self, summary, label):
        return next(metric for metric in summary["metrics"] if metric["label"] == label)

    def test_complete_cross_service_run_passes_and_preserves_typed_settings(self):
        config = self.config()
        summary = gate.evaluate(config, self.write_jtl(self.samples(config)), 0)
        self.assertEqual(summary["verdict"], "PASS")
        self.assertEqual(summary["total_samples"], sum(gate.expected_counts(config).values()))
        self.assertEqual(summary["virtual_users"], 4)
        self.assertEqual(summary["settings"]["gateway_port"], 5000)
        self.assertEqual(summary["settings"]["stress_multipliers"], [1, 2, 4, 8])
        self.assertEqual(self.metric(summary, "Construction Reports")["expected_samples"], 2)

    def test_configuration_rejects_missing_unknown_duplicate_and_secret_settings(self):
        for key, value in (("admin_password", "never-print-this"), ("typo_threads", "1")):
            with self.subTest(key=key), self.assertRaises(ValueError) as error:
                self.config(**{key: value})
            self.assertNotIn("never-print-this", str(error.exception))
        path = self.root / "effective.properties"
        self.config()
        path.write_text(path.read_text() + "\nprofile=load\n")
        with self.assertRaises(ValueError):
            gate.read_config(path)
        self.properties.pop("p95_login_ms")
        with self.assertRaises(ValueError):
            self.config()

    def test_configuration_numeric_and_public_metadata_boundaries(self):
        bad_settings = [
            {"project_threads": "01"}, {"project_threads": "10000"},
            {"project_threads": "-1"}, {"journey_loops": "0"},
            {"report_think_ms": "1000000001"}, {"project_rampup": "1.5"},
            {"fixture_timeout": "nan"}, {"p95_login_ms": "inf"},
            {"max_error_rate_pct": "100.1"}, {"response_timeout_ms": "0"},
            {"gateway_host": "user:password@host"}, {"gateway_port": "65536"},
            {"gateway_protocol": "ftp"}, {"env_label": "../local"},
            {"stress_multipliers": "2,4"}, {"stress_multipliers": "1,2,2"},
            {"stress_multipliers": "1,65"}, {"stress_multipliers": "1,2,3,4,5,6,7,8,9"},
            {f"{group}_threads": "0" for group in gate.GROUPS},
        ]
        for updates in bad_settings:
            with self.subTest(updates=updates), self.assertRaises(ValueError):
                self.config(**updates)
        config = self.config(project_threads="9999", max_error_rate_pct="100", fixture_timeout="0.5")
        self.assertEqual(config["project_threads"], 9999)
        self.assertEqual(config["fixture_timeout"], 0.5)

    def test_setup_errors_cannot_be_hidden_by_relaxed_workload_error_limit(self):
        config = self.config(max_error_rate_pct="100")
        rows = self.samples(config)
        rows[0][3] = "false"
        summary = gate.evaluate(config, self.write_jtl(rows), 0)
        self.assertEqual(summary["verdict"], "FAIL")
        self.assertIn("Setup request failed", self.metric(summary, "Register Client")["reasons"])

    def test_setup_latency_is_not_subject_to_workload_response_time_limit(self):
        config = self.config()
        rows = self.samples(config)
        rows[0][1] = 100000
        summary = gate.evaluate(config, self.write_jtl(rows), 0)
        self.assertEqual(summary["verdict"], "PASS")
        self.assertEqual(self.metric(summary, "Register Client")["limits"], {})

    def test_nearest_rank_p95_and_boundary_are_measured_per_endpoint(self):
        config = self.config(project_loops="20", p95_project_create_ms="19")
        rows = self.samples(config)
        project_rows = [row for row in rows if row[2] == "Create Project"]
        for duration, row in enumerate(project_rows, 1):
            row[1] = duration
        summary = gate.evaluate(config, self.write_jtl(rows), 0)
        metric = self.metric(summary, "Create Project")
        self.assertEqual(summary["verdict"], "PASS")
        self.assertEqual(metric["p95_ms"], 19)
        self.assertEqual(metric["avg_ms"], 10.5)
        self.assertAlmostEqual(metric["throughput_rps"], 20 / 1.92)
        project_rows[-2][1] = 20
        summary = gate.evaluate(config, self.write_jtl(rows), 0)
        self.assertEqual(summary["verdict"], "FAIL")
        self.assertIn("p95 response time exceeds limit", self.metric(summary, "Create Project")["reasons"])

    def test_error_rate_limit_inclusive_and_applies_to_each_endpoint(self):
        config = self.config(max_error_rate_pct="50")
        rows = self.samples(config)
        next(row for row in rows if row[2] == "Payment Dashboard")[3] = "false"
        self.assertEqual(gate.evaluate(config, self.write_jtl(rows), 0)["verdict"], "PASS")
        config["max_error_rate_pct"] = 49.99
        summary = gate.evaluate(config, self.write_jtl(rows), 0)
        self.assertEqual(summary["verdict"], "FAIL")
        self.assertEqual(self.metric(summary, "Payment Dashboard")["error_rate_pct"], 50)

    def test_missing_extra_unknown_and_disabled_samples_fail(self):
        config = self.config(report_threads="0")
        complete = self.samples(config)
        for rows in (complete[:-1], complete + [complete[-1]],
                     complete + [[1000, 10, "Unexpected endpoint", "true"]],
                     complete + [[1000, 10, "Construction Reports", "true"]]):
            with self.subTest(rows=len(rows)):
                summary = gate.evaluate(config, self.write_jtl(rows), 0)
                self.assertEqual(summary["verdict"], "FAIL")

    def test_malformed_records_fail_even_if_other_samples_are_complete(self):
        config = self.config()
        malformed = [["NaN", 5, "Create Project", "true"],
                     [0, "Infinity", "Create Project", "true"],
                     [-1, 5, "Create Project", "true"],
                     [0, -5, "Create Project", "true"],
                     [0, 5, "Create Project", "yes"],
                     [0, 5, "", "true"], [0, 5, "Create Project"], []]
        for bad_row in malformed:
            with self.subTest(row=bad_row):
                summary = gate.evaluate(config, self.write_jtl(self.samples(config) + [bad_row]), 0)
                self.assertEqual(summary["verdict"], "FAIL")
                self.assertTrue(any("Malformed JTL" in failure for failure in summary["failures"]))

    def test_invalid_header_empty_missing_and_broken_csv_fail(self):
        config = self.config()
        path = self.root / "invalid.jtl"
        for content in ("", "timeStamp,elapsed,label,success\n", "timeStamp,elapsed,label\n",
                        "timeStamp,elapsed,label,success,success\n",
                        'timeStamp,elapsed,label,success\n0,10,"broken,true\n'):
            with self.subTest(content=content):
                path.write_text(content)
                self.assertEqual(gate.evaluate(config, path, 0)["verdict"], "FAIL")
        self.assertEqual(gate.evaluate(config, self.root / "missing.jtl", 0)["verdict"], "FAIL")

    def test_jmeter_exit_status_fails_complete_results(self):
        config = self.config()
        summary = gate.evaluate(config, self.write_jtl(self.samples(config)), 9)
        self.assertEqual(summary["verdict"], "FAIL")
        self.assertIn("JMeter exited with code 9", summary["failures"])

    def test_cli_emits_both_reports_for_missing_results_and_rejects_bad_config(self):
        self.config()
        output = self.root / "report"
        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
            status = gate.main(["--config", str(self.root / "effective.properties"),
                                "--results", str(self.root / "missing.jtl"), "--output-dir", str(output)])
            self.assertEqual(status, 1)
            self.assertEqual(gate.main(["--validate-config", str(self.root / "effective.properties")]), 0)
            (self.root / "effective.properties").write_text("profile=smoke")
            self.assertEqual(gate.main(["--validate-config", str(self.root / "effective.properties")]), 2)
        self.assertEqual(json.loads((output / "summary.json").read_text())["verdict"], "FAIL")
        self.assertIn("Performance gate: FAIL", (output / "summary.md").read_text())

    def prepare_stress(self, failing_multiplier=None, fail_setup=False, multipliers=(1, 2)):
        config = self.config(profile="stress", stress_multipliers="1,2")
        for multiplier in multipliers:
            settings = {**config, "multiplier": multiplier,
                        **{f"{group}_threads": config[f"{group}_threads"] * multiplier for group in gate.GROUPS}}
            rows = self.samples(settings)
            if multiplier == failing_multiplier:
                if fail_setup:
                    rows[0][3] = "false"
                else:
                    for row in rows:
                        if row[2] == "Payment Dashboard":
                            row[1] = 101
            summary = gate.evaluate(settings, self.write_jtl(rows), 0)
            directory = self.root / f"step-{multiplier:02d}-x{multiplier}"
            gate.write_reports(directory, "summary", summary, gate.markdown(summary))

    def test_optional_dashboard_missing_or_directory_fails_and_existing_file_passes(self):
        config = self.config()
        results = self.write_jtl(self.samples(config))
        dashboard = self.root / "dashboard" / "index.html"
        output = self.root / "reports"
        arguments = ["--config", str(self.root / "effective.properties"),
                     "--results", str(results), "--output-dir", str(output),
                     "--dashboard", str(dashboard), "--jmeter-exit-code", "0"]
        with contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(gate.main(arguments), 1)
            summary = json.loads((output / "summary.json").read_text())
            self.assertEqual(summary["failures"], ["JMeter HTML dashboard was not generated"])
            dashboard.mkdir(parents=True)
            self.assertEqual(gate.main(arguments), 1)
            dashboard.rmdir()
            dashboard.write_text("<!doctype html><title>JMeter report</title>")
            self.assertEqual(gate.main(arguments), 0)
            summary = json.loads((output / "summary.json").read_text())
            self.assertEqual(summary["verdict"], "PASS")
            self.assertEqual(summary["failures"], [])

    def test_dashboard_option_is_rejected_in_config_validation_and_stress_modes(self):
        for option in ("--validate-config", "--stress-summary"):
            with self.subTest(option=option), contextlib.redirect_stderr(io.StringIO()):
                with self.assertRaises(SystemExit) as error:
                    gate.main([option, str(self.root), "--dashboard", str(self.root / "index.html")])
                self.assertEqual(error.exception.code, 2)

    def test_stress_all_pass_reports_limit_not_found(self):
        self.prepare_stress()
        summary = gate.stress_summary(self.root)
        self.assertEqual(summary["verdict"], "PASS")
        self.assertFalse(summary["capacity_limit_found"])
        self.assertIsNone(summary["first_breach_users"])
        self.assertEqual(summary["last_completed_users"], 8)
        self.assertIn("does not prove maximum capacity", summary["conclusion"])
        self.assertTrue((self.root / "stress-summary.json").is_file())
        self.assertTrue((self.root / "stress-summary.md").is_file())

    def test_stress_first_threshold_breach_reports_users_and_multiplier(self):
        self.prepare_stress(failing_multiplier=2)
        summary = gate.stress_summary(self.root)
        self.assertEqual(summary["verdict"], "FAIL")
        self.assertTrue(summary["capacity_limit_found"])
        self.assertEqual(summary["first_breach_users"], 8)
        self.assertEqual(summary["first_breach_multiplier"], 2)

    def test_stress_setup_failure_and_incomplete_run_do_not_establish_capacity(self):
        self.prepare_stress(failing_multiplier=1, fail_setup=True, multipliers=(1,))
        summary = gate.stress_summary(self.root)
        self.assertEqual(summary["verdict"], "FAIL")
        self.assertFalse(summary["capacity_limit_found"])
        self.assertTrue(any("complete planned" in failure for failure in summary["failures"]))

    def test_stress_rejects_corrupt_summary_and_mismatched_settings(self):
        self.prepare_stress()
        path = self.root / "step-02-x2" / "summary.json"
        summary = json.loads(path.read_text())
        summary["settings"]["journey_loops"] = 999
        path.write_text(json.dumps(summary))
        self.assertEqual(gate.stress_summary(self.root)["verdict"], "FAIL")
        path.write_text("{not json}")
        self.assertEqual(gate.stress_summary(self.root)["verdict"], "FAIL")

    def test_stress_rejects_claimed_pass_with_missing_metrics_or_inconsistent_totals(self):
        self.prepare_stress()
        path = self.root / "step-02-x2" / "summary.json"
        original = json.loads(path.read_text())
        for updates in ({"metrics": []}, {"total_samples": 0}, {"failures": ["JMeter failed"]}):
            with self.subTest(updates=updates):
                path.write_text(json.dumps({**original, **updates}))
                self.assertEqual(gate.stress_summary(self.root)["verdict"], "FAIL")


if __name__ == "__main__":
    unittest.main()
