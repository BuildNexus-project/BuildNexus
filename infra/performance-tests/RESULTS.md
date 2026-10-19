# SE3112 performance results — 2026-10-18

The 20-user load profile passed. Stress passed at 20, 40 and 80 configured
users, then failed at 160: a design-upload database deadlock returned HTTP
500, nine later upload samples were missing, and four endpoint p95 budgets
were breached. A fresh smoke run after stress passed. The failure is retained
as an application finding; no service code or acceptance budget was changed
to make the experiment pass.

These are real local runs of the current four-group plan, measured from
source commit `8b9c90c`. Compact samples, public settings, summaries and
resource snapshots are retained in [evidence/2026-10-18](evidence/2026-10-18/).
[LEGACY-RESULTS.md](LEGACY-RESULTS.md) and
[AZURE-RESULTS.md](AZURE-RESULTS.md) preserve the earlier two-workload
experiments; their figures are not comparable baselines for this plan.

## Environment and workload

| Item | Recorded setting |
|---|---|
| Target | Local Docker Compose, all requests through `http://localhost:5000` (YARP Gateway); all five services, five MySQL databases, Kafka and Mailpit running |
| Host | Windows, Intel Core i5-12500H, 16 logical processors, 16,849,948,672 bytes RAM (15.69 GiB) |
| Docker allocation | 16 CPUs, 8,162,308,096 bytes RAM (7.60 GiB), Docker Engine 29.6.2 |
| Generator | JMeter 5.6.3 non-GUI, Temurin Java 17.0.20, heap `-Xms256m -Xmx1024m`; Python 3.13; Git Bash |
| State | Warm applications, started around 12:49 UTC; existing development data/volumes retained; tests run sequentially on the same machine |
| Fixtures | Real registration, Admin promotion/assignment, approved designs, two milestones (50% progress), invoice 1,000 and partial payment 250; prepared before each profile's JMeter measurement |
| Upload | Synthetic 209-byte PDF; this is not a large-drawing benchmark |
| Runtime integrity | No increase in container restart counts and no OOM-killed state across the recorded runs |

Profile settings are versioned in `profiles/`. Smoke configures 2/1/2/1
project/design/journey/report threads, loops 2/1/2/2, one-second ramp-up and
100 ms think time. Load configures 5/3/10/2 threads, loops 20/10/10/20,
ten-second ramp-up and 250/500/250/500 ms think time. Stress multiplies only
those thread counts by 1, 2, 4 and 8, with a separate JMeter process per step.
The 20-user baseline is an assessment assumption, not observed production
traffic. Finite loops mean different group lifetimes; observed sampled peak
threads are reported below rather than implying a sustained plateau.

Smoke permits zero errors. Load/stress permit at most 1% per measured
endpoint. Every setup error or unexpected sample count fails the run. Each
endpoint also has its own p95 budget, shown below.

## Overall observations

All-request figures include JMeter setup calls; fixture API preparation is
outside the JTL. These combined averages/p95 values describe the mix and are
not used in place of endpoint gates. “Peak” is the maximum `allThreads` seen
in recorded samples, not a continuously observed concurrency measurement.

| Run | Configured users / sampled peak | Samples / expected | Errors | Error % | Requests/s | Mean ms | p95 ms | Gate |
|---|---:|---:|---:|---:|---:|---:|---:|---|
| Smoke | 6 / 6 | 48 / 48 | 0 | 0.0000 | 10.12 | 101.58 | 423.00 | PASS |
| Load | 20 / 19 | 906 / 906 | 0 | 0.0000 | 28.33 | 100.75 | 197.00 | PASS |
| Stress x1 | 20 / 20 | 906 / 906 | 0 | 0.0000 | 30.79 | 66.00 | 211.00 | PASS |
| Stress x2 | 40 / 40 | 1812 / 1812 | 0 | 0.0000 | 51.50 | 127.70 | 282.00 | PASS |
| Stress x4 | 80 / 80 | 3624 / 3624 | 0 | 0.0000 | 104.11 | 199.86 | 623.00 | PASS |
| Stress x8 | 160 / 160 | 7239 / 7248 | 1 | 0.0138 | 133.48 | 513.72 | 1779.00 | FAIL |
| Smoke after stress | 6 / 6 | 48 / 48 | 0 | 0.0000 | 6.22 | 127.92 | 634.00 | PASS |

Timings below separate the measured HTTP window from total command wall time.

| Profile invocation | Started UTC | Wall time s | Runner exit |
|---|---|---:|---:|
| smoke | 2026-10-18T14:54:26 | 42.22 | 0 |
| load | 2026-10-18T14:55:34 | 106.05 | 0 |
| stress | 2026-10-18T14:58:06 | 647.44 | 1 |
| smoke-recovery | 2026-10-18T15:10:14 | 46.34 | 0 |

Stress wall time includes preparing 80 Client fixtures before its four
steps. That setup, JVM startup, dashboard generation and sparse resource
sampling account for the difference from the HTTP windows:
Smoke 4.741 s, Load 31.982 s, Stress x1 29.423 s, Stress x2 35.187 s, Stress x4 34.808 s, Stress x8 54.233 s, Smoke after stress 7.716 s.

## Smoke: measured endpoints

[Exact settings and machine summary](evidence/2026-10-18/smoke/summary.json).

| Operation | Samples | Requests/s | Mean ms | p95 ms | p95 budget ms | Errors % | Gate |
|---|---:|---:|---:|---:|---:|---:|---|
| Create Project | 4 | 18.96 | 45.50 | 52.00 | 1500.00 | 0.00 | PASS |
| Upload Design Document | 1 | 1.38 | 726.00 | 726.00 | 3000.00 | 0.00 | PASS |
| Login Under Load | 4 | 1.03 | 176.50 | 341.00 | 2000.00 | 0.00 | PASS |
| List Projects | 4 | 2.66 | 24.25 | 27.00 | 1000.00 | 0.00 | PASS |
| View Project | 4 | 2.66 | 36.75 | 44.00 | 1000.00 | 0.00 | PASS |
| Client Invoices | 4 | 2.65 | 24.00 | 32.00 | 1000.00 | 0.00 | PASS |
| Payment History | 4 | 2.64 | 29.25 | 37.00 | 1000.00 | 0.00 | PASS |
| Payment Dashboard | 4 | 2.61 | 25.75 | 31.00 | 1000.00 | 0.00 | PASS |
| Construction Progress | 4 | 2.59 | 236.50 | 448.00 | 1000.00 | 0.00 | PASS |
| Construction Reports | 2 | 9.66 | 37.00 | 53.00 | 2000.00 | 0.00 | PASS |

## Load: measured endpoints

[Exact settings and machine summary](evidence/2026-10-18/load/summary.json).

| Operation | Samples | Requests/s | Mean ms | p95 ms | p95 budget ms | Errors % | Gate |
|---|---:|---:|---:|---:|---:|---:|---|
| Create Project | 100 | 8.94 | 41.90 | 65.00 | 1500.00 | 0.00 | PASS |
| Upload Design Document | 30 | 1.06 | 1760.90 | 2151.00 | 3000.00 | 0.00 | PASS |
| Login Under Load | 100 | 3.60 | 132.11 | 210.00 | 2000.00 | 0.00 | PASS |
| List Projects | 100 | 3.93 | 23.07 | 38.00 | 1000.00 | 0.00 | PASS |
| View Project | 100 | 3.94 | 28.55 | 41.00 | 1000.00 | 0.00 | PASS |
| Client Invoices | 100 | 3.95 | 27.84 | 46.00 | 1000.00 | 0.00 | PASS |
| Payment History | 100 | 3.95 | 24.77 | 40.00 | 1000.00 | 0.00 | PASS |
| Payment Dashboard | 100 | 3.95 | 21.39 | 29.00 | 1000.00 | 0.00 | PASS |
| Construction Progress | 100 | 3.95 | 26.01 | 39.00 | 1000.00 | 0.00 | PASS |
| Construction Reports | 40 | 3.18 | 28.38 | 65.00 | 2000.00 | 0.00 | PASS |

## Stress: endpoint latency as concurrency increases

Each cell is p95 milliseconds. The per-step JSON files also retain each
endpoint’s mean, requests/second, sample count and error rate, including
setup metrics. The overview above records all four metrics for every step.

| Operation | Budget ms | 20 users | 40 users | 80 users | 160 users |
|---|---:|---:|---:|---:|---:|
| Create Project | 1500.00 | 80.00 | 138.00 | 623.00 | 1287.00 |
| Upload Design Document | 3000.00 | 716.00 | 2186.00 | 2657.00 | 3829.00 |
| Login Under Load | 2000.00 | 224.00 | 368.00 | 662.00 | 2491.00 |
| List Projects | 1000.00 | 41.00 | 59.00 | 199.00 | 1087.00 |
| View Project | 1000.00 | 56.00 | 67.00 | 351.00 | 1634.00 |
| Client Invoices | 1000.00 | 40.00 | 53.00 | 224.00 | 492.00 |
| Payment History | 1000.00 | 51.00 | 136.00 | 232.00 | 518.00 |
| Payment Dashboard | 1000.00 | 41.00 | 69.00 | 166.00 | 385.00 |
| Construction Progress | 1000.00 | 48.00 | 62.00 | 232.00 | 634.00 |
| Construction Reports | 2000.00 | 52.00 | 77.00 | 271.00 | 1162.00 |

Per-step source summaries: [20 users](evidence/2026-10-18/stress/step-01-x1/summary.json), [40 users](evidence/2026-10-18/stress/step-02-x2/summary.json), [80 users](evidence/2026-10-18/stress/step-03-x4/summary.json), [160 users](evidence/2026-10-18/stress/step-04-x8/summary.json).

The 160-user step produced 7,239/7,248 samples. `Upload Design Document`
produced 231/240, including one HTTP 500 (2,395 ms); `stopthread` prevented
its remaining nine uploads. Its observed error rate was 0.43%, below the 1%
budget, but the incomplete workload and 3,829 ms p95 still fail the gate.
Login (2,491 ms), project listing (1,087 ms) and project detail (1,634 ms)
also exceeded their own budgets. The runner returned 1 and stopped after
that step; all four HTML dashboards were generated.

**80 configured users was the highest passing step in this experiment.**
160 was the first failed tested step. Because the final workload is
incomplete and the profiles are finite, the combined stress summary
correctly leaves `capacity_limit_found=false`. This is not a measurement of
an exact maximum or a safe production concurrency limit.

## Supporting resource observations

Docker statistics were sampled about every five seconds while the runner
was active. The table uses only snapshots timestamped within each JTL's
request window. Values are peak sampled CPU percentages; they can exceed
100% across multiple CPUs and are not percentages of the whole 16-CPU host.
The short initial smoke window has no matching snapshot.

| Container | Load 20 | Stress 20 | Stress 40 | Stress 80 | Stress 160 |
|---|---:|---:|---:|---:|---:|
| user-service | 132.20 | 141.85 | 306.97 | 676.86 | 1118.42 |
| design-service | 37.80 | 4.60 | 10.35 | 20.37 | 67.49 |
| design-db | 12.68 | 6.56 | 16.81 | 46.62 | 78.11 |
| kafka | 202.33 | 175.23 | 182.33 | 188.76 | 133.52 |
| api-gateway | 16.17 | 17.06 | 26.86 | 53.25 | 82.02 |

The load/stress windows have 7/6/7/6/8 snapshots respectively. Resource files
also retain memory, I/O and process counts; these samples cannot resolve
short peaks or SQL lock waits. Windows JMeter resource use was not sampled.

## Findings and follow-up

1. **The design write path has a confirmed failure under this stress.**
   [The selected server exception](evidence/2026-10-18/stress/design-deadlock.txt)
   reports `MySqlConnector.MySqlException: Deadlock found when trying to get
   lock`, with `DesignDocumentRepository.InsertDocumentAsync` on the stack.
   This reopens the historical deadlock concern with fresh evidence. The
   exact lock cycle, index interaction or outbox contribution is not proven.
   A follow-up should capture the InnoDB deadlock graph and transaction
   timings, check index/lock ordering and transaction boundaries, then assess
   a bounded whole-transaction retry for transient deadlocks with duplicate
   event/idempotency checks. Re-run the unchanged workload after any fix.
2. **Authentication is a likely CPU pressure point.** User Service peak
   sampled CPU rose from 141.85% at stress 20 to 1,118.42% at stress 160,
   while login p95 rose past 2,000 ms. The real
   [password verifier](../../services/user-service/Services/Pbkdf2PasswordHasher.cs)
   uses PBKDF2-SHA256 with 210,000 iterations for newly created users.
   That code and the correlated CPU/latency make authentication a candidate
   hot path; profiling is still needed to attribute the time. Preserve the
   password security settings while investigating concurrency and capacity.
3. **A combined percentile would hide endpoint problems.** Load overall
   p95 is 197 ms, versus upload p95 2,151 ms (only 30 of 906 samples).
   At stress 160, overall errors are only 0.0138%, yet latency and workload
   completeness fail. Per-endpoint gates and exact counts are essential.
4. **Throughput growth slows at the last step.** Doubling configured users
   from 80 to 160 increased observed whole-run throughput from 104.11 to
   133.48 requests/s (about 28%), with worse latency and an incomplete upload
   workload. This is a degradation observation, not an exact saturation
   point. The same finite-loop closed workload slows its own request rate
   when responses slow, and all components share one physical machine.

The separate load run and stress x1 use the same 20-user workload settings
but upload p95 differs (2,151 versus 716 ms). Their fixture pools, data state,
warmth and scheduling differ. Single runs do not separate those effects;
repeat controlled runs before attributing all latency changes to concurrency.
The small PDF, repeated reads of fixed fixtures, accumulating write data,
short steps and absence of a soak test limit generalisation. Kafka projection
readiness is verified before measurement; consumer lag is not measured.

## Reproduce and inspect

Prepare the stack and tools as described in [README.md](README.md). From the
repository root in Bash, run one profile at a time:

```bash
bash infra/performance-tests/run-performance-tests.sh -Jprofile=smoke -Jenv_label=assessment-smoke
bash infra/performance-tests/run-performance-tests.sh -Jprofile=load -Jenv_label=assessment-load
bash infra/performance-tests/run-performance-tests.sh -Jprofile=stress -Jenv_label=assessment-stress
echo "Stress runner exit status: $?"
bash infra/performance-tests/run-performance-tests.sh -Jprofile=smoke -Jenv_label=assessment-smoke-recovery
```

Set `HEAP='-Xms256m -Xmx1024m'` in the environment to match the recorded
generator heap. Use the installation paths for `JMETER_CMD`/`PYTHON_CMD`
shown in the README when running on this Windows machine. The wrapper does
not reset the target databases. Re-runs create new directories and real
test data; the recorded timings and deadlock need not recur identically.

For supporting resource observations, run this in another terminal during
the experiment and record capture times:

```bash
docker stats --no-stream --format '{{json .}}'
docker logs --since <UTC-start-time> buildnexus-design-service
```

The machine-readable environment manifests identify each exact original
run directory. Open its `report/index.html` and `summary.md` under the
ignored `results/` tree. Retained compact CSVs reproduce every stored gate
summary without needing the running stack; see [evidence/README.md](evidence/README.md).
The temporary credential CSV was removed after each invocation and is not
part of the retained evidence.

The CI smoke job is implemented and locally linted/tested. These measurements
were made locally; a successful remote GitHub execution is not claimed here.
After pushing, dispatch `CI/CD` with `run_load` enabled and verify the load
artifact and combined report before the group demonstration. Use
[DEMO.md](DEMO.md) for the live walkthrough and VIVA preparation.
