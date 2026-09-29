# Performance test results (SCRUM-45 / US-32)

Captured from a real run of `project-performance-test-plan.jmx` — see
[README.md](README.md) for what the plan does and how to reproduce this.

## Run details

| | |
|---|---|
| Date | 2026-09-27 |
| Target | Local `docker compose` stack (`infra/docker-compose.yml`), via the API Gateway at `http://localhost:5000` — all 5 services, all 5 MySQL containers, and Kafka running, containers already warm (up 35 hours / 3 days, not a cold start) |
| Docker host | 16 CPUs, ~7.6 GB RAM allocated to Docker Desktop |
| JMeter | 5.6.3, non-GUI mode |
| Load | Defaults: 20 Client threads × 10 loops (200 `Create Project` requests, 10s ramp-up); 10 Architect threads × 5 loops (50 `Upload Design Document` requests, 10s ramp-up) |
| Total samples | 360 (setup calls + the two measured endpoints) |
| Total errors | 2 (0.56%) — both on `Upload Design Document`, see [Finding](#finding-deadlocks-under-concurrent-design-uploads) below |

## Project creation — `POST /api/projects`

200 requests, 20 concurrent Client users, 0 errors.

| Metric | Value |
|---|---|
| Throughput | 20.9 req/s |
| Mean response time | 28 ms |
| Median | 26 ms |
| Min / Max | 18 ms / 58 ms |
| p90 | 39 ms |
| p95 | 42 ms |
| p99 | 50 ms |

Fast and stable under this load — no errors, and even the slowest 1% of
requests stayed under 50 ms.

## Design upload — `POST /api/designs/projects/{id}/documents`

50 requests, 10 concurrent Architect users, 2 errors (4%).

| Metric | Value |
|---|---|
| Throughput | 1.68 req/s |
| Mean response time | 4,201 ms |
| Median | 4,157 ms |
| Min / Max | 1,866 ms / 5,009 ms |
| p90 | 4,748 ms |
| p95 | 4,866 ms |
| p99 | 5,009 ms |

Two orders of magnitude slower than project creation under concurrent load,
and not just for the requests that failed — every sample in this batch,
successful or not, took at least 1.9 seconds. See the finding below.

### Finding: deadlocks under concurrent design uploads

Both failures came back `500 Internal Server Error`. The Design Service's
own logs show why:

```
MySqlConnector.MySqlException (0x80004005): Deadlock found when trying to get lock; try restarting transaction
   at ... DesignDocumentRepository.InsertDocumentAsync(...) in .../Data/DesignDocumentRepository.cs:line 261
   at ... DesignDocumentRepository.InsertDocumentAsync(...) in .../Data/DesignDocumentRepository.cs:line 263
   at ... DesignDocumentRepository.TryAddVersionAsync(...) in .../Data/DesignDocumentRepository.cs:line 71
   at ... DesignsController.Upload(...) in .../Controllers/DesignsController.cs:line 110
```

The repeated `InsertDocumentAsync` frame suggests the repository already
retries once on a deadlock; here both the original attempt and the retry lost
the race. This reproduced with only 10 concurrent Architects, each uploading
to their **own** project, so the contention is not two threads fighting over
the same document row — something shared (plausibly the outbox insert that
rides along in the same transaction, per US-23) is serializing writes across
unrelated uploads.

This is an application-code finding, not an infrastructure one, so it has not
been touched here — this role's scope is `/infra` and `/.github/workflows`.
Worth a follow-up story: the 4-second-plus response times on file uploads
that didn't even error suggest lock contention is costing every concurrent
upload, not only the 4% that surfaced as a hard failure.

## Setup-call overhead (for context, not part of the two target endpoints)

Register/login calls run once per thread to obtain a real JWT; not what this
story measures, but included so the two tables above aren't read in
isolation. All well under 150 ms even at these concurrency levels.

| Sampler | Requests | Mean | p95 |
|---|---|---|---|
| Register Client | 30 | 92 ms | 117 ms |
| Login Client | 30 | 71 ms | 79 ms |
| Register Architect | 10 | 84 ms | 103 ms |
| Login Architect | 10 | 70 ms | 78 ms |
| Admin Login | 10 | 73 ms | 79 ms |
| Assign Architect To Project | 10 | 31 ms | 57 ms |

## Reproducing this run

```bash
cd infra/performance-tests
docker compose -f ../docker-compose.yml up -d   # if the stack isn't already running
./run-performance-tests.sh
```

The full HTML dashboard (per-endpoint response-time distribution graphs,
response codes over time) is regenerated at `results/report/index.html` on
every run — not committed here since it's derived from `results/results.jtl`,
which any re-run reproduces.
