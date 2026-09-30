# Performance test results — Azure (SCRUM-45 / US-32)

Captured from a real run of `project-performance-test-plan.jmx` against the
services deployed to Azure — see [README.md](README.md) for what the plan
does and how to reproduce this, and [RESULTS.md](RESULTS.md) for the
equivalent run against the local `docker compose` stack. The two are kept as
separate files, each transcribed only from its own `results/<env_label>/`
directory, so a local run and an Azure run never overwrite each other's
numbers.

Only User Service, Project Service and Design Service are deployed to Azure
so far (see `infra/terraform/`) — Construction Service, Payment Service and
the API Gateway are not, which is why the plan targets the three deployed
services directly with `auth_*`/`project_*`/`design_*` host overrides instead
of one Gateway host.

## Run details

| | |
|---|---|
| Date | 2026-09-29 |
| Target | Azure App Services — `auth_host`=`buildnexus-user-service-2026.azurewebsites.net`, `project_host`=`buildnexus-project-service-2026.azurewebsites.net`, `design_host`=`buildnexus-design-service-2026.azurewebsites.net`, all over HTTPS (443). No Gateway: it isn't deployed yet. |
| App Service plan | B1 (Basic), shared by all three deployed App Services — see `infra/terraform/main.tf` |
| MySQL | Azure Database for MySQL Flexible Server, Burstable B1ms — see `infra/terraform/main.tf` |
| JMeter | 5.6.3, non-GUI mode, with `-Jhttps.sessioncontext.shared=false` (needed once the plan talks to three different HTTPS hostnames in the same run — see [capacity note](#note-b1-cannot-take-the-plans-default-load) below) |
| Load | Reduced from the plan's defaults: 5 Client threads × 5 loops (25 `Create Project` requests), 3 Architect threads × 3 loops (9 `Upload Design Document` requests). The default load (20×10 / 10×5) overwhelmed User Service on this tier — see the note below. |
| Total samples | 65 (setup calls + the two measured endpoints) |
| Total errors | 1 (1.5%) — one `500` on `Upload Design Document`, see [below](#one-500-on-upload-design-document) |

## Project creation — `POST /api/projects`

25 requests, 5 concurrent Client users, 0 errors.

| Metric | Value |
|---|---|
| Throughput | 2.46 req/s |
| Mean response time | 692 ms |
| Median | 423 ms |
| Min / Max | 148 ms / 3,135 ms |
| p90 | 1,732 ms |
| p95 | 1,748 ms |
| p99 | 3,135 ms |

## Design upload — `POST /api/designs/projects/{id}/documents`

9 requests, 3 concurrent Architect users, 1 error (11%).

| Metric | Value |
|---|---|
| Throughput | 0.90 req/s |
| Mean response time | 3,159 ms |
| Median | 702 ms |
| Min / Max | 357 ms / 8,572 ms |
| p90 | 8,572 ms |
| p95 | 8,572 ms |
| p99 | 8,572 ms |

n=9 is too small for the p90/p95/p99 columns to mean much on their own (they
all land on the same worst sample) — read this table as "8 fast, 1 very
slow", not as a real tail distribution. A larger Design Upload run would need
more Architect threads/loops, which is exactly what pushed User Service over
on this tier at the plan's defaults; see the note below.

### One `500` on Upload Design Document

The single failure was a real `500 Internal Server Error` from Design
Service, on a request that took 8.2 seconds before failing — thread
`Design Upload Load 2-3`, project `674e0ac9-021e-4d33-b9b9-211feb31854b`.
No Application Insights is wired into Design Service yet (only User Service
has it, per SCRUM-47's proof of concept in `infra/terraform/main.tf`), so
there's no trace to pull for this specific request after the fact. This is
consistent in shape with the deadlock finding in
[RESULTS.md](RESULTS.md#note-the-deadlock-finding-from-earlier-runs-is-not-reproducing)
— a slow failure rather than a fast one — but n=1 isn't enough to call it
the same root cause; worth watching for a repeat on a larger Azure run once
the capacity issue below is addressed.

## Setup-call overhead (for context, not part of the two target endpoints)

Register/login calls run once per thread against User Service to obtain a
real JWT; not what this story measures, but included because they're the
whole reason this run's load had to be turned down — see the note below.

| Sampler | Requests | Mean | p95 |
|---|---|---|---|
| Register Client | 8 | 6,342 ms | 8,865 ms |
| Login Client | 8 | 4,176 ms | 6,826 ms |
| Register Architect | 3 | 3,548 ms | 4,857 ms |
| Login Architect | 3 | 2,287 ms | 2,878 ms |
| Admin Login | 3 | 1,809 ms | 2,395 ms |
| Assign Architect To Project | 3 | 1,283 ms | 2,854 ms |

Seconds, not milliseconds — two to three orders of magnitude slower than the
same calls locally (see [RESULTS.md](RESULTS.md)'s setup-call table, all
under 120 ms). This is B1's single small instance under real network
latency and TLS overhead, not a bug; see the comparison note below.

### Note: B1 cannot take the plan's default load

A first attempt at this run used the plan's defaults (20 Client threads × 10
loops, 10 Architect threads × 5 loops — 30 concurrent threads total, all
opening with a Register+Login against User Service). That run came back with
a 70% error rate: `Register Client`/`Login Client` timing out or failing
under the concurrency, which cascaded into `401`s on `Create Project` (no
valid Client token), which cascaded into `Assign Architect To Project`
hitting a malformed path (empty `projectId`) and getting back a `405`, which
cascaded into `Upload Design Document` 404ing against a project that was
never created. None of that was a bug in the plan or in Project/Design
Service — it was one bottleneck (User Service auth under load on a B1 plan)
producing several believable-looking secondary symptoms everywhere
downstream. Dropping to 5/3 concurrent threads, as used for the run recorded
above, cleared all of it except the one `500` noted above.

This is itself a real finding, not just a workaround: **the current Azure
deployment cannot handle the load profile RESULTS.md documents comfortably
locally.** B1 is a single small instance with no autoscale, shared across
all three App Services, versus a 16-CPU Docker host locally. Worth a
follow-up story if this deployment is meant to demonstrate realistic
concurrent load — either a higher App Service tier, or documenting a lower
supported concurrency as the deployment's actual capacity.

## Comparing against the local baseline

Every request against Azure crosses the public internet to a B1 App Service
plan rather than a warm container on the same Docker host, so higher latency
here than in [RESULTS.md](RESULTS.md) is expected on its own. What's notable
here isn't just that Azure is slower — it's that the *default test load*,
which the local stack handles at 0 errors and single-digit-millisecond
latency, isn't sustainable against this deployment at all. The 5/3-thread
numbers above are the closest like-for-like comparison this tier currently
supports.

## Reproducing this run

```bash
cd infra/performance-tests
./run-performance-tests.sh -Jenv_label=azure -Jhttps.sessioncontext.shared=false \
  -Jauth_protocol=https    -Jauth_host=buildnexus-user-service-2026.azurewebsites.net    -Jauth_port=443 \
  -Jproject_protocol=https -Jproject_host=buildnexus-project-service-2026.azurewebsites.net -Jproject_port=443 \
  -Jdesign_protocol=https  -Jdesign_host=buildnexus-design-service-2026.azurewebsites.net  -Jdesign_port=443 \
  -Jadmin_email=<a real Admin account's email on this deployment> -Jadmin_password=<its password> \
  -Jproject_threads=5 -Jproject_loops=5 -Jdesign_threads=3 -Jdesign_loops=3
```

App Service names come from `terraform output user_service_app_name` /
`project_service_app_name` / `design_service_app_name` (see
`infra/terraform/outputs.tf`). The Admin account has to already exist and
hold the `Admin` role — Azure's `AdminSeeder` only runs under
`IsDevelopment()`, so unlike the local stack nothing seeds one automatically;
see `infra/RUNBOOK.md`'s "Creating the first Admin" section.

Before running this: confirm both User Service and Project Service App
Services are actually in the `Running` state (`az webapp show --name
<app-name> --resource-group buildnexus-rg --query state`) — on this tier
they get stopped between sessions to save cost, and a stopped app answers
every request with a `403 Site Disabled` page rather than a real error.

The full HTML dashboard is regenerated at `results/azure/report/index.html`
on every run — not committed here since it's derived from
`results/azure/results.jtl`, which any re-run reproduces.
