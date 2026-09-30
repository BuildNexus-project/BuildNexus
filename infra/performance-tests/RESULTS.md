# Performance test results (SCRUM-45 / US-32)

Captured from a real run of `project-performance-test-plan.jmx` — see
[README.md](README.md) for what the plan does and how to reproduce this.

## Run details

| | |
|---|---|
| Date | 2026-09-29 |
| Target | Local `docker compose` stack (`infra/docker-compose.yml`) — after the SCRUM-45 host-split refactor, User/Project/Design Service are hit directly (`auth_*`/`project_*`/`design_*`, all defaulting to `http://localhost:5000`, same as the Gateway's local port) rather than through one shared Gateway host. All 5 services, all 5 MySQL containers, and Kafka running; containers up between 10 hours and 5 days, not a cold start. |
| Docker host | 16 CPUs, ~7.6 GB RAM allocated to Docker Desktop |
| JMeter | 5.6.3, non-GUI mode |
| Load | Defaults: 20 Client threads × 10 loops (200 `Create Project` requests, 10s ramp-up); 10 Architect threads × 5 loops (50 `Upload Design Document` requests, 10s ramp-up) |
| Total samples | 360 (setup calls + the two measured endpoints) |
| Total errors | 0 |

## Project creation — `POST /api/projects`

200 requests, 20 concurrent Client users, 0 errors.

| Metric | Value |
|---|---|
| Throughput | 21.0 req/s |
| Mean response time | 17 ms |
| Median | 15 ms |
| Min / Max | 10 ms / 53 ms |
| p90 | 24 ms |
| p95 | 28 ms |
| p99 | 39 ms |

Fast and stable under this load — no errors, and even the slowest 1% of
requests stayed under 40 ms.

## Design upload — `POST /api/designs/projects/{id}/documents`

50 requests, 10 concurrent Architect users, 0 errors.

| Metric | Value |
|---|---|
| Throughput | 5.6 req/s |
| Mean response time | 24 ms |
| Median | 21 ms |
| Min / Max | 14 ms / 39 ms |
| p90 | 34 ms |
| p95 | 35 ms |
| p99 | 39 ms |

In the same ballpark as project creation this time — no errors, and nothing
close to the multi-second stalls seen previously. See the note below.

### Note: the deadlock finding from earlier runs is not reproducing

An earlier run captured here (2026-09-27, superseded by this one) hit 2
`500 Internal Server Error` deadlocks out of 50 `Upload Design Document`
requests, with every sample in that batch — successful or not — taking at
least 1.9 seconds. The Design Service's logs at the time pointed at
`DesignDocumentRepository.InsertDocumentAsync`/`TryAddVersionAsync`
(`MySqlConnector.MySqlException: Deadlock found when trying to get lock; try
restarting transaction`), with contention plausibly from the outbox insert
riding along in the same transaction (US-23), serializing writes across
unrelated uploads even though each Architect uploaded to their own project.

Two consecutive local runs on 2026-09-29 (this one and one immediately
before it, same load, same stack) both came back with 0 errors and
sub-40ms uploads — no sign of the deadlock. Nothing was changed here to fix
it; this role's scope is `/infra` and `/.github/workflows`, not the
repository code. Worth carrying forward rather than treating as closed: the
condition may be data- or timing-dependent (accumulated table state, a code
fix that landed elsewhere, warmer connections) rather than gone for good, so
a load test before a release should still watch for it recurring, and the
1.9-second-floor-even-on-success symptom from the original run is worth
keeping in mind if it does.

## Setup-call overhead (for context, not part of the two target endpoints)

Register/login calls run once per thread to obtain a real JWT; not what this
story measures, but included so the two tables above aren't read in
isolation. All well under 120 ms even at these concurrency levels.

| Sampler | Requests | Mean | p95 |
|---|---|---|---|
| Register Client | 30 | 79 ms | 112 ms |
| Login Client | 30 | 63 ms | 86 ms |
| Register Architect | 10 | 77 ms | 99 ms |
| Login Architect | 10 | 64 ms | 70 ms |
| Admin Login | 10 | 62 ms | 75 ms |
| Assign Architect To Project | 10 | 23 ms | 40 ms |

## Reproducing this run

```bash
cd infra/performance-tests
docker compose -f ../docker-compose.yml up -d   # if the stack isn't already running
./run-performance-tests.sh
```

No `-Jenv_label` needed: this run's numbers came from the default, `local`,
which is also where a bare `./run-performance-tests.sh` writes. The full HTML
dashboard (per-endpoint response-time distribution graphs, response codes
over time) is regenerated at `results/local/report/index.html` on every run —
not committed here since it's derived from `results/local/results.jtl`, which
any re-run reproduces. See [AZURE-RESULTS.md](AZURE-RESULTS.md) for the
equivalent run against Azure, kept in its own file so neither overwrites the
other.
