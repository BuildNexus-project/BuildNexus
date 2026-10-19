# Retained assessment evidence

`2026-10-18/` contains real local HTTP observations from the current plan,
not synthetic unit-test input. See [../RESULTS.md](../RESULTS.md) for the
environment, conclusions and commands used to collect them.

Each smoke/load run and stress step contains:

- `samples.csv`: the original JTL rows restricted to timestamp, elapsed
  time, sampler label, success, response code, thread name, latency,
  connect time and group/all-thread counts. URLs, fixture identifiers,
  bodies and credentials are excluded.
- `effective.properties`: resolved public settings used for that run.
- `summary.json`: the original machine-readable performance gate result.
- `resources.jsonl`: Docker resource snapshots whose timestamps fall in
  that run's JTL window. The initial smoke has no matching snapshot; an
  empty file means no measurements, not zero CPU.

`environment.json` records wall-clock timing (including fixture setup, JVM
startup and dashboard generation), runner exit status, Docker CPU/RAM,
container image IDs/start/restart state, and SHA-256 fingerprints for the
original JTL and retained samples. The original JTL/dashboard remain in
ignored `results/` directories identified by that file. The recorded source
commit precedes this documentation/evidence commit; the measured runner,
profiles and service source are unchanged by the final commit.

Stress also contains its root configuration and combined stress summary.
The stress summary's step paths are relative to the stress directory.

To independently recalculate one retained run from the repository root:

```bash
python3 infra/performance-tests/check-performance.py \
  --config infra/performance-tests/evidence/2026-10-18/load/effective.properties \
  --results infra/performance-tests/evidence/2026-10-18/load/samples.csv \
  --output-dir infra/performance-tests/results/evidence-recheck/load
```

Substitute `smoke` or a directory such as `stress/step-01-x1` for another
measurement. The expected exit status is nonzero for a retained failed
gate. This rechecks HTTP observations and thresholds; it does not rerun
HTTP traffic, regenerate a dashboard or infer the original JMeter process
status. The live runner also checks process status and dashboard existence.

Resource snapshots are supporting observations at roughly five-second
intervals, not a profiler. They exclude the Windows JMeter process. Values
can miss short peaks and do not establish a particular SQL statement or
code path as the cause of a delay. Docker percentages can exceed 100% on
this multi-core host; do not read them as a percentage of all 16 CPUs.
