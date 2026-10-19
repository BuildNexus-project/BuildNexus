#!/usr/bin/env python3
"""Validate the retained CI smoke report before adding metrics to the job summary."""

import argparse
import importlib.util
import json
import math
from pathlib import Path
import sys


ROOT = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("performance_gate", ROOT / "check-performance.py")
gate = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(gate)


def require(condition, message="summary schema or values are invalid"):
    if not condition:
        raise ValueError(message)


def count(value):
    require(type(value) is int and value >= 0)
    return value


def number(value):
    require(type(value) in (int, float) and math.isfinite(value) and value >= 0)
    return value


def messages(value):
    require(isinstance(value, list) and all(isinstance(item, str) and item.strip() for item in value))
    return value


def statistics(values, sample_key="samples"):
    samples, errors = count(values[sample_key]), count(values["errors"])
    require(errors <= samples)
    rate = number(values["error_rate_pct"])
    require(rate <= 100 and math.isclose(rate, 100 * errors / samples if samples else 0,
                                       rel_tol=1e-9, abs_tol=1e-9))
    for key in ("avg_ms", "p95_ms"):
        if samples:
            number(values[key])
        else:
            require(values[key] is None)
    window = number(values["window_seconds"])
    if window > 0:
        require(math.isclose(number(values["throughput_rps"]), samples / window,
                             rel_tol=1e-9, abs_tol=1e-9))
    else:
        require(values["throughput_rps"] is None)
    return samples, errors


def validate_summary(summary):
    """Accept the default CI smoke profile, including truthful failed/partial runs."""
    config = gate.read_config(ROOT / "profiles" / "smoke.properties")
    expected = gate.expected_counts(config)
    require(type(summary["schema_version"]) is int and summary["schema_version"] == 1)
    require(summary["profile"] == "smoke" and count(summary["multiplier"]) == 1)
    require(count(summary["virtual_users"]) == sum(config[f"{group}_threads"] for group in gate.GROUPS))
    settings = summary["settings"]
    require(isinstance(settings, dict) and all(settings.get(key) == value for key, value in config.items()),
            "summary settings do not match the default CI smoke profile")
    require(not (settings.keys() - config.keys() - gate.METADATA))
    require(count(summary["expected_samples"]) == sum(expected.values()))
    total, errors = statistics(summary, "total_samples")
    metrics = summary["metrics"]
    require(isinstance(metrics, list) and len(metrics) == len(expected))
    seen, observed, observed_errors, metric_failed = set(), 0, 0, False
    for metric in metrics:
        label = metric["label"]
        require(isinstance(label, str) and label in expected and label not in seen)
        seen.add(label)
        require(count(metric["expected_samples"]) == expected[label])
        samples, request_errors = statistics(metric)
        observed += samples
        observed_errors += request_errors
        workload = label in gate.LIMITS
        require(metric["kind"] == ("workload" if workload else "setup"))
        limits = ({"p95_ms": config[gate.LIMITS[label]],
                   "error_rate_pct": config["max_error_rate_pct"]} if workload else {})
        require(metric["limits"] == limits)
        reasons = messages(metric["reasons"])
        require(metric["verdict"] in ("PASS", "FAIL"))
        require((metric["verdict"] == "FAIL") == bool(reasons))
        breached = samples != expected[label] or (not workload and request_errors > 0)
        if workload:
            breached |= (metric["p95_ms"] is not None and metric["p95_ms"] > limits["p95_ms"])
            breached |= metric["error_rate_pct"] > limits["error_rate_pct"]
        require(not breached or metric["verdict"] == "FAIL", "summary claims PASS for a failed gate")
        metric_failed |= metric["verdict"] == "FAIL"
    require(observed == total and observed_errors == errors)
    failures = messages(summary["failures"])
    require(summary["verdict"] in ("PASS", "FAIL"))
    require((summary["verdict"] == "FAIL") == bool(failures))
    require(not metric_failed or summary["verdict"] == "FAIL")
    return summary


def inspect_report(directory, job_result):
    if job_result == "skipped":
        return {"ok": True, "row": "⏭️ SKIPPED, manual only", "summary": None,
                "note": "Load testing was skipped. No performance measurements were produced."}
    try:
        require(job_result in ("success", "failure", "cancelled"), "load job result is invalid")
        paths = list(Path(directory).glob("*/summary.json"))
        require(len(paths) == 1, "expected exactly one smoke summary at load/<run-id>/summary.json")
        summary = validate_summary(json.loads(paths[0].read_text(encoding="utf-8-sig")))
    except (OSError, UnicodeError, json.JSONDecodeError, KeyError, TypeError, ValueError, OverflowError):
        return {"ok": False, "row": "❌ FAIL, summary unavailable", "summary": None,
                "note": f"Job: **{job_result.upper() if job_result in ('success', 'failure', 'cancelled') else 'INVALID'}**. "
                        "Performance summary unavailable: the artifact is missing, ambiguous, or invalid. "
                        "Inspect the load job logs; no measurements are inferred."}
    ok = job_result == "success" and summary["verdict"] == "PASS"
    row = "✅ PASS" if ok else (f"❌ FAIL, job {job_result}" if job_result != "success"
                                else "❌ FAIL, performance gate")
    note = f"Job: **{job_result.upper()}**. Performance gate: **{summary['verdict']}**."
    if job_result != "success":
        note += " Passing metrics cannot override a failed or cancelled job."
    return {"ok": ok, "row": row, "summary": summary, "note": note}


def details(result):
    lines = ["## Load testing", "", result["note"], ""]
    summary = result["summary"]
    if summary:
        lines += [f"Smoke: **{summary['virtual_users']} virtual users**; "
                  f"**{summary['total_samples']} / {summary['expected_samples']} requests**; "
                  f"**{summary['errors']} errors ({summary['error_rate_pct']:.2f}%)**.", "",
                  "Request totals include setup. Endpoint metrics below cover measured workloads; "
                  "the dashboard and full setup checks are in the artifact.", "",
                  "| Endpoint / operation | Samples / expected | p95 ms / limit | Errors % | Requests/s | Gate |",
                  "|---|---:|---:|---:|---:|---|"]
        for metric in summary["metrics"]:
            if metric["kind"] != "workload":
                continue
            p95 = gate.display(metric["p95_ms"])
            rate = gate.display(metric["throughput_rps"])
            lines.append(f"| {metric['label']} | {metric['samples']} / {metric['expected_samples']} "
                         f"| {p95} / {gate.display(metric['limits']['p95_ms'])} "
                         f"| {metric['error_rate_pct']:.2f} | {rate} | {metric['verdict']} |")
        lines.append("")
    return "\n".join(lines)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--reports-dir", required=True)
    parser.add_argument("--job-result", default="skipped")
    parser.add_argument("--part", choices=("row", "details"), required=True)
    args = parser.parse_args(argv)
    result = inspect_report(args.reports_dir, args.job_result)
    print(f"| Load tests (JMeter smoke) | {result['row']} | - | - | - |"
          if args.part == "row" else details(result))
    return 0 if result["ok"] else 1


if __name__ == "__main__":
    sys.exit(main())
