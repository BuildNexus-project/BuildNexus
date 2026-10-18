#!/usr/bin/env python3
"""Fail-closed JMeter gates and retained reports; Python standard library only."""

import argparse
import csv
import json
import math
from pathlib import Path
import re
import sys


GROUPS = ("project", "design", "journey", "report")
LIMITS = {
    "Create Project": "p95_project_create_ms",
    "Upload Design Document": "p95_design_upload_ms",
    "Login Under Load": "p95_login_ms",
    "List Projects": "p95_project_list_ms",
    "View Project": "p95_project_detail_ms",
    "Client Invoices": "p95_invoices_ms",
    "Payment History": "p95_history_ms",
    "Payment Dashboard": "p95_dashboard_ms",
    "Construction Progress": "p95_progress_ms",
    "Construction Reports": "p95_reports_ms",
}
METADATA = {"env_label"} | {
    f"{service}_{part}"
    for service in ("gateway", "auth", "project", "design")
    for part in ("protocol", "host", "port")
}


def integer(value, name, minimum=0, maximum=None):
    if not re.fullmatch(r"0|[1-9][0-9]*", value):
        raise ValueError(f"{name} must be an integer")
    result = int(value)
    if result < minimum or (maximum is not None and result > maximum):
        raise ValueError(f"{name} is outside its permitted range")
    return result


def number(value, name, minimum=0, inclusive=False, maximum=None):
    try:
        result = float(value)
    except ValueError as exc:
        raise ValueError(f"{name} must be numeric") from exc
    if (not math.isfinite(result) or result < minimum
            or (not inclusive and result == minimum)
            or (maximum is not None and result > maximum)):
        raise ValueError(f"{name} is outside its permitted range")
    return result


def read_config(path):
    """A small public-property whitelist keeps credentials out of reports."""
    raw = {}
    for line_number, line in enumerate(Path(path).read_text(encoding="utf-8-sig").splitlines(), 1):
        line = line.strip()
        if not line or line.startswith(("#", "!")):
            continue
        key, separator, value = line.partition("=")
        key, value = key.strip(), value.strip()
        if not separator or key in raw or not value:
            raise ValueError(f"Invalid or duplicate property at line {line_number}")
        raw[key] = value
    required = {"profile", "fixture_timeout", "connect_timeout_ms", "response_timeout_ms",
                "max_error_rate_pct"} | set(LIMITS.values()) | {
                    f"{group}_{suffix}" for group in GROUPS
                    for suffix in ("threads", "loops", "rampup", "think_ms")}
    unknown = raw.keys() - required - METADATA - {"multiplier", "stress_multipliers"}
    if unknown:
        raise ValueError("Unknown property; only documented public settings are allowed")
    missing = required - raw.keys()
    if missing:
        raise ValueError("Missing properties: " + ", ".join(sorted(missing)))
    config = {}
    if raw["profile"] not in ("smoke", "load", "stress"):
        raise ValueError("profile must be smoke, load or stress")
    config["profile"] = raw["profile"]
    config["multiplier"] = integer(raw.get("multiplier", "1"), "multiplier", 1, 64)
    for group in GROUPS:
        for suffix in ("threads", "loops", "rampup", "think_ms"):
            key = f"{group}_{suffix}"
            config[key] = integer(raw[key], key, 1 if suffix == "loops" else 0,
                                  9999 if suffix == "threads" else 1_000_000_000)
    if not any(config[f"{group}_threads"] for group in GROUPS):
        raise ValueError("At least one workload must have a nonzero thread count")
    for key in ("connect_timeout_ms", "response_timeout_ms"):
        config[key] = integer(raw[key], key, 1)
    for key in ("fixture_timeout", *LIMITS.values()):
        config[key] = number(raw[key], key)
    config["max_error_rate_pct"] = number(raw["max_error_rate_pct"], "max_error_rate_pct",
                                          inclusive=True, maximum=100)
    multipliers = [integer(item.strip(), "stress_multipliers", 1, 64)
                   for item in raw.get("stress_multipliers", "1,2,4,8").split(",")]
    if len(multipliers) > 8 or multipliers[0] != 1 or any(
            left >= right for left, right in zip(multipliers, multipliers[1:])):
        raise ValueError("stress_multipliers must start at 1 and strictly increase (up to 8 steps)")
    config["stress_multipliers"] = multipliers
    for key in sorted(raw.keys() & METADATA):
        value = raw[key]
        if key.endswith("_port"):
            config[key] = integer(value, key, 1, 65535)
        elif key.endswith("_protocol"):
            if value not in ("http", "https"):
                raise ValueError(f"{key} must be http or https")
            config[key] = value
        else:
            pattern = r"[A-Za-z0-9][A-Za-z0-9_-]{0,63}" if key == "env_label" else r"[A-Za-z0-9][A-Za-z0-9.-]*"
            if not re.fullmatch(pattern, value):
                raise ValueError(f"Invalid {key}")
            config[key] = value
    return config


def expected_counts(config):
    p, d, j, r = (config[f"{group}_threads"] for group in GROUPS)
    counts = {
        "Register Client": p + d, "Login Client": p + d,
        "Create Project": p * config["project_loops"],
        "Register Architect": d, "Create Project For Design Upload": d,
        "Admin Login": d, "Promote Fixture To Architect": d,
        "Login Architect": d, "Assign Architect To Project": d,
        "Upload Design Document": d * config["design_loops"],
    }
    for label in ("Login Under Load", "List Projects", "View Project", "Client Invoices",
                  "Payment History", "Payment Dashboard", "Construction Progress"):
        counts[label] = j * config["journey_loops"]
    counts["Admin Login For Reports"] = r
    counts["Construction Reports"] = r * config["report_loops"]
    return counts


def read_samples(path):
    """Retain valid rows for diagnostics but never ignore malformed input."""
    samples, failures = [], []
    try:
        with Path(path).open(encoding="utf-8-sig", newline="") as stream:
            rows = csv.reader(stream, strict=True)
            header = next(rows, [])
            names = [item.strip().casefold() for item in header]
            required = {"timestamp", "elapsed", "label", "success"}
            if len(names) != len(set(names)) or not required.issubset(names):
                return [], ["JTL must have unique columns including timeStamp, elapsed, label and success"]
            columns = {name: names.index(name) for name in required}
            for row in rows:
                try:
                    if len(row) != len(header):
                        raise ValueError("column count does not match header")
                    fields = {name: row[index].strip() for name, index in columns.items()}
                    start = number(fields["timestamp"], "timeStamp", inclusive=True)
                    elapsed = number(fields["elapsed"], "elapsed", inclusive=True)
                    if not fields["label"] or fields["success"].casefold() not in ("true", "false"):
                        raise ValueError("label must be nonempty and success must be true or false")
                    if not math.isfinite(start + elapsed):
                        raise ValueError("sample end time is not finite")
                    samples.append({"label": fields["label"], "start": start, "elapsed": elapsed,
                                    "success": fields["success"].casefold() == "true"})
                except ValueError as exc:
                    failures.append(f"Malformed JTL record at line {rows.line_num}: {exc}")
    except (OSError, UnicodeError, csv.Error) as exc:
        failures.append(f"Cannot read complete JTL: {type(exc).__name__}")
    if not samples:
        failures.append("JTL contains no valid samples")
    return samples, failures


def statistics(samples):
    durations = sorted(sample["elapsed"] for sample in samples)
    count = len(durations)
    errors = sum(not sample["success"] for sample in samples)
    window = ((max(sample["start"] + sample["elapsed"] for sample in samples)
               - min(sample["start"] for sample in samples)) / 1000) if count else 0
    return {"samples": count, "errors": errors,
            "error_rate_pct": 100 * errors / count if count else 0.0,
            "avg_ms": sum(value / count for value in durations) if count else None,
            "p95_ms": durations[math.ceil(.95 * count) - 1] if count else None,
            "window_seconds": window,
            "throughput_rps": count / window if window > 0 else None}


def evaluate(config, results, jmeter_exit_code):
    samples, failures = read_samples(results)
    if jmeter_exit_code != 0:
        failures.append(f"JMeter exited with code {jmeter_exit_code}")
    expected = expected_counts(config)
    metrics = []
    for label in list(expected) + sorted({sample["label"] for sample in samples} - expected.keys()):
        values = statistics([sample for sample in samples if sample["label"] == label])
        kind = "workload" if label in LIMITS else "setup" if label in expected else "unknown"
        reasons = []
        if label not in expected:
            reasons.append("Unknown sampler label")
        wanted = expected.get(label, 0)
        if values["samples"] != wanted:
            reasons.append(f"Expected {wanted} samples; observed {values['samples']}")
        limits = {}
        if kind == "setup" and values["errors"]:
            reasons.append("Setup request failed")
        if kind == "workload":
            limits = {"p95_ms": config[LIMITS[label]], "error_rate_pct": config["max_error_rate_pct"]}
            if values["p95_ms"] is not None and values["p95_ms"] > limits["p95_ms"]:
                reasons.append("p95 response time exceeds limit")
            if values["error_rate_pct"] > limits["error_rate_pct"]:
                reasons.append("Error rate exceeds limit")
        metrics.append({"label": label, "kind": kind, "expected_samples": wanted,
                        **values, "limits": limits, "reasons": reasons,
                        "verdict": "FAIL" if reasons else "PASS"})
        failures.extend(f"{label}: {reason}" for reason in reasons)
    totals = statistics(samples)
    return {"schema_version": 1, "profile": config["profile"], "multiplier": config["multiplier"],
            "verdict": "FAIL" if failures else "PASS", "settings": config,
            "virtual_users": sum(config[f"{group}_threads"] for group in GROUPS),
            "expected_samples": sum(expected.values()), "total_samples": totals.pop("samples"),
            **totals, "metrics": metrics, "failures": failures}


def display(value):
    return "-" if value is None else f"{value:.2f}" if isinstance(value, float) else str(value)


def markdown(summary):
    lines = [f"# Performance gate: {summary['verdict']}", "",
             f"Profile: **{summary['profile']}**; multiplier: **{summary['multiplier']}**; "
             f"virtual users: **{summary['virtual_users']}**.", "",
             f"Samples: {summary['total_samples']} / {summary['expected_samples']} expected; "
             f"errors: {summary['errors']} ({summary['error_rate_pct']:.2f}%).", "",
             "Setup requests must succeed; their latency is excluded from workload limits. "
             "p95 uses the nearest-rank method. Throughput uses each sampler's first start to last end.", "",
             "| Sampler | Kind | Samples / expected | Errors % | p95 ms / limit | Requests/s | Verdict |",
             "| --- | --- | ---: | ---: | ---: | ---: | --- |"]
    for metric in summary["metrics"]:
        label = metric["label"].replace("|", r"\|").replace("\n", " ").replace("\r", " ")
        lines.append(f"| {label} | {metric['kind']} | {metric['samples']} / {metric['expected_samples']} "
                     f"| {metric['error_rate_pct']:.2f} | {display(metric['p95_ms'])} / "
                     f"{display(metric['limits'].get('p95_ms'))} | {display(metric['throughput_rps'])} "
                     f"| {metric['verdict']} |")
    lines += ["", "Failures:"] + ([f"- {failure}" for failure in summary["failures"]] or ["- None."])
    return "\n".join(lines) + "\n"


def write_reports(directory, stem, summary, text):
    directory = Path(directory)
    directory.mkdir(parents=True, exist_ok=True)
    (directory / f"{stem}.json").write_text(json.dumps(summary, indent=2, allow_nan=False) + "\n", encoding="utf-8")
    (directory / f"{stem}.md").write_text(text, encoding="utf-8")


def console_summary(summary):
    """Keep the live demo readable while full diagnostics remain in reports."""
    def clean(value):
        return "".join(character if 32 <= ord(character) < 127 else " "
                       for character in str(value))

    print(f"Performance gate: {summary['verdict']}")
    if "executed_steps" in summary:
        print(clean(summary["conclusion"]))
        print(f"{'Multiplier':>10} {'Users':>7} {'Samples':>9} {'Errors':>7} Verdict")
        for step in summary["executed_steps"]:
            print(f"{step['multiplier']:>10} {step['virtual_users']:>7} "
                  f"{step['total_samples']:>9} {step['errors']:>7} {step['verdict']}")
    else:
        print(f"Max error rate per endpoint: {summary['settings']['max_error_rate_pct']:.2f}%")
        print(f"{'Sampler':<24} {'Count':>9} {'Avg ms':>9} {'p95 ms / limit':>19} "
              f"{'Errors %':>9} {'Req/s':>9} Verdict")
        for metric in summary["metrics"]:
            if metric["kind"] != "workload" or not metric["expected_samples"]:
                continue
            label = clean(metric["label"])[:24]
            count = f"{metric['samples']}/{metric['expected_samples']}"
            percentile = f"{display(metric['p95_ms'])} / {display(metric['limits']['p95_ms'])}"
            print(f"{label:<24} {count:>9} {display(metric['avg_ms']):>9} {percentile:>19} "
                  f"{metric['error_rate_pct']:>9.2f} {display(metric['throughput_rps']):>9} "
                  f"{metric['verdict']}")
    for failure in summary["failures"][:5]:
        print("FAIL: " + clean(failure)[:160])
    remaining = len(summary["failures"]) - 5
    if remaining > 0:
        print(f"... {remaining} more failures; see the JSON/Markdown report.")


def stress_summary(directory):
    root = Path(directory)
    config = read_config(root / "effective.properties")
    if config["profile"] != "stress" or config["multiplier"] != 1:
        raise ValueError("Stress root requires profile=stress and multiplier=1")
    planned = config["stress_multipliers"]
    steps, failures, breaches = [], [], []
    for path in sorted(root.glob("step-*/summary.json")):
        try:
            item = json.loads(path.read_text(encoding="utf-8"))
            multiplier = item["multiplier"]
            if (type(multiplier) is not int or multiplier not in planned
                    or item["schema_version"] != 1 or item["profile"] != "stress"
                    or item["verdict"] not in ("PASS", "FAIL")):
                raise ValueError("invalid step metadata")
            settings = item["settings"]
            if settings != {**config, "multiplier": multiplier,
                            **{f"{group}_threads": config[f"{group}_threads"] * multiplier for group in GROUPS}}:
                raise ValueError("step settings differ from planned settings")
            users = sum(settings[f"{group}_threads"] for group in GROUPS)
            if item["virtual_users"] != users or not isinstance(item["metrics"], list):
                raise ValueError("invalid step workload")
            expected = expected_counts(settings)
            labels = [metric["label"] for metric in item["metrics"]]
            if len(labels) != len(set(labels)) or not expected.keys() <= set(labels):
                raise ValueError("missing or duplicate sampler metrics")
            for metric in item["metrics"]:
                if (type(metric["samples"]) is not int or type(metric["errors"]) is not int
                        or not 0 <= metric["errors"] <= metric["samples"]
                        or metric["expected_samples"] != expected.get(metric["label"], 0)
                        or metric["verdict"] not in ("PASS", "FAIL")):
                    raise ValueError("invalid sampler metrics")
            if (item["expected_samples"] != sum(expected.values())
                    or item["total_samples"] != sum(metric["samples"] for metric in item["metrics"])
                    or item["errors"] != sum(metric["errors"] for metric in item["metrics"])
                    or not isinstance(item["failures"], list)):
                raise ValueError("inconsistent step totals")
            failed_metrics = [metric for metric in item["metrics"] if metric["verdict"] == "FAIL"]
            if item["verdict"] == "PASS" and (failed_metrics or item["failures"]
                    or item["total_samples"] != item["expected_samples"] or not item["total_samples"]):
                raise ValueError("PASS step contains failures or incomplete workload")
            performance_breach = (item["verdict"] == "FAIL" and bool(failed_metrics)
                                  and all(metric["kind"] == "workload" and metric["samples"] == metric["expected_samples"]
                                          for metric in failed_metrics)
                                  and bool(item["failures"])
                                  and all(isinstance(reason, str) and "exceeds limit" in reason for reason in item["failures"]))
            if performance_breach:
                breaches.append((multiplier, users))
            steps.append({"multiplier": multiplier, "virtual_users": users, "verdict": item["verdict"],
                          "total_samples": item["total_samples"], "errors": item["errors"],
                          "summary": path.relative_to(root).as_posix()})
            if item["verdict"] == "FAIL":
                failures.append(f"Stress multiplier {multiplier} failed; inspect its summary")
        except (OSError, UnicodeError, json.JSONDecodeError, KeyError, TypeError, ValueError) as exc:
            failures.append(f"Invalid step summary {path.parent.name}: {type(exc).__name__}")
    steps.sort(key=lambda item: item["multiplier"])
    if [item["multiplier"] for item in steps] != planned:
        failures.append("Executed steps do not match the complete planned multiplier sequence")
    first = min(breaches) if breaches else (None, None)
    conclusion = (f"First observed threshold breach: {first[1]} virtual users (multiplier {first[0]})."
                  if breaches else "No capacity limit was found; passing tested loads does not prove maximum capacity.")
    if failures and not breaches:
        conclusion = "Execution was incomplete or failed without an established capacity limit. " + conclusion
    summary = {"schema_version": 1, "profile": "stress", "verdict": "FAIL" if failures else "PASS",
               "settings": config, "planned_multipliers": planned, "executed_steps": steps,
               "first_breach_multiplier": first[0], "first_breach_users": first[1],
               "last_completed_multiplier": steps[-1]["multiplier"] if steps else None,
               "last_completed_users": steps[-1]["virtual_users"] if steps else None,
               "capacity_limit_found": bool(breaches), "conclusion": conclusion, "failures": failures}
    lines = [f"# Stress test: {summary['verdict']}", "", conclusion, "",
             "| Multiplier | Virtual users | Samples | Errors | Verdict |",
             "| ---: | ---: | ---: | ---: | --- |"]
    lines += [f"| {step['multiplier']} | {step['virtual_users']} | {step['total_samples']} | "
              f"{step['errors']} | {step['verdict']} |" for step in steps]
    lines += ["", "Failures:"] + ([f"- {failure}" for failure in failures] or ["- None."])
    write_reports(root, "stress-summary", summary, "\n".join(lines) + "\n")
    return summary


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--validate-config", metavar="PROPERTIES")
    mode.add_argument("--stress-summary", metavar="DIRECTORY")
    parser.add_argument("--config")
    parser.add_argument("--results")
    parser.add_argument("--output-dir")
    parser.add_argument("--dashboard", help="Optional expected JMeter HTML dashboard index.html")
    parser.add_argument("--jmeter-exit-code", type=int, default=0)
    args = parser.parse_args(argv)
    if (args.validate_config or args.stress_summary) and any((args.config, args.results, args.output_dir, args.dashboard)):
        parser.error("Config validation and stress aggregation cannot be combined with result options")
    try:
        if args.validate_config:
            read_config(args.validate_config)
            print("Configuration valid")
            return 0
        if args.stress_summary:
            summary = stress_summary(args.stress_summary)
        else:
            if not all((args.config, args.results, args.output_dir)):
                parser.error("--config, --results and --output-dir are required")
            config = read_config(args.config)
            summary = evaluate(config, args.results, args.jmeter_exit_code)
            if args.dashboard and not Path(args.dashboard).is_file():
                summary["failures"].append("JMeter HTML dashboard was not generated")
                summary["verdict"] = "FAIL"
            write_reports(args.output_dir, "summary", summary, markdown(summary))
        console_summary(summary)
        return 0 if summary["verdict"] == "PASS" else 1
    except (OSError, UnicodeError, ValueError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
