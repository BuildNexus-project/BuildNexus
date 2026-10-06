# Test documentation

One page that pulls together what is tested, how much of the code the tests
reach, how the system performs under load, and what end-to-end testing exists.
Every number below was measured on this branch or transcribed from a committed
results file, and says which. Where something does not exist yet, it says so.

For how the system is put together, see [architecture.md](architecture.md).

## At a glance

| | Result | Measured |
|---|---|---|
| Backend tests | **1,552 passing, 0 failing** (1,217 unit + 335 integration) | 2026-10-04, this branch |
| Backend coverage (unit + integration) | **82.1% lines, 79.5% branches** | 2026-10-04, this branch |
| Backend coverage (unit tests only) | 47.1% lines, 37.1% branches | 2026-10-04, this branch |
| Frontend tests | **796 passing, 0 failing** | 2026-10-04, this branch |
| Frontend coverage | **90.2% lines, 83.0% branches, 91.5% functions** | 2026-10-04, this branch |
| Performance (JMeter), local stack | 0 errors; project create p95 28 ms, design upload p95 35 ms | 2026-09-29 ([RESULTS.md](../infra/performance-tests/RESULTS.md)) |
| Performance (JMeter), Azure | Reduced load only; 1 error in 65 samples; p95 1.7 s on project create | 2026-09-29 ([AZURE-RESULTS.md](../infra/performance-tests/AZURE-RESULTS.md)) |
| End-to-end (Selenium) | **Not part of this story; pending its own story** | see [below](#end-to-end-tests-selenium) |

## How to reproduce

Backend (needs Docker for the databases and Kafka: `cd infra && docker compose up -d`):

```bash
# one project at a time, as CI does; add the coverage flag to measure it
dotnet test services/user-service-tests/UserService.Tests.csproj \
  --collect:"XPlat Code Coverage" --results-directory ./coverage/user-service

# unit tests only (nothing needs to be running) / integration tests only
dotnet test <project> --filter "Category!=Integration"
dotnet test <project> --filter "Category=Integration"
```

Merging the Cobertura files into one report is `reportgenerator` (version 5.5.11, as in
`.github/workflows/ci.yml`). CI does this on every run and publishes
`backend-coverage-report` (unit only) and `backend-combined-coverage-report` (unit +
integration) as build artifacts, plus the percentages in the job summary.

Frontend (use the Node version in `.nvmrc`):

```bash
cd frontend
npm test                 # run the tests
npm run test:coverage    # run them and measure coverage (frontend/coverage/)
```

## Unit and integration tests (backend)

Every service has its own xUnit project beside it, plus one for the gateway.
A test class that needs MySQL or Kafka carries `[Trait("Category", "Integration")]`;
everything else is a unit test and needs nothing running. CI runs the two groups as
separate jobs.

| Service | Unit tests | Integration tests | Line coverage, unit only | Line coverage, unit + integration | Branch coverage, unit + integration |
|---|---|---|---|---|---|
| User Service | 183 | 33 | 40.4% | **83.8%** | 74.3% |
| Project Service | 545 | 68 | 62.6% | **84.0%** | 82.5% |
| Design Service | 126 | 31 | 44.6% | **76.2%** | 74.7% |
| Construction Service | 173 | 103 | 39.2% | **82.2%** | 84.3% |
| Payment Service | 142 | 100 | 38.5% | **82.7%** | 77.0% |
| API Gateway | 48 | none | 100% | **100%** | 91.6% |
| **Total** | **1,217** | **335** | **47.1%** | **82.1%** | **79.5%** |

### Reading the coverage figures

- **The combined column is the one that describes the system.** The unit-only column
  is low on purpose: a service's data layer is hand-written ADO.NET SQL, and a stub
  cannot show that SQL works, so those repositories are tested against a real MySQL
  and that coverage only shows up in the integration run. Kafka publishers, consumers
  and the outbox are the same.
- **What is counted:** each service's own assembly, including `Program.cs` and the new
  Swagger configuration. Test projects are not counted. Coverlet measures lines that
  were executed, not that the assertions around them are good.
- **Team target: roughly 70%**, agreed as a goal, not a gate. CI reports the number on
  every run and does not fail the build on it. Every service clears 70% on the combined
  figure.
- **Where the lowest numbers are:** Design Service (76.2%) on the combined figure and, on
  unit tests alone, Payment, Construction and User (38% to 40%). Neither is a failure of the target, but they
  are where a new test would be most worth writing.

### Guards that run in every service

- `EndpointRoleDeclarationTests`: every endpoint either names its allowed roles or is
  explicitly anonymous, so a new endpoint cannot ship unprotected by accident.
- `MigrationScriptTests`: every event a service can publish is allowed by its own outbox
  schema.
- `SwaggerDocumentTests` (US-36): the generated OpenAPI document defines the JWT bearer
  scheme, marks every protected endpoint as needing it (and anonymous ones as open), and
  names the roles each one allows.

## Frontend tests

Vitest and React Testing Library; tests sit beside the code they cover. 47 test files,
796 tests, all passing.

| Metric | Covered / total | Percent |
|---|---|---|
| Lines | 1,447 / 1,605 | **90.2%** |
| Statements | 1,499 / 1,659 | 90.4% |
| Branches | 1,068 / 1,286 | 83.0% |
| Functions | 537 / 587 | 91.5% |

The zod schemas that mirror each service's own validation rules are tested directly at
the boundaries (empty, over-length, malformed), not only through a rendered page.

**One timing-sensitive test.** `NotificationsPage > loading more > adds the next page
below the first` hit vitest's 5-second limit once during this measurement, when the
frontend run overlapped with the backend coverage run on the same machine (5.9 s). On a
quiet re-run all 796 passed. It is a slow test rather than a wrong one, but on a loaded
CI runner it could fail intermittently.

## Performance tests (JMeter)

Plan: [`infra/performance-tests/project-performance-test-plan.jmx`](../infra/performance-tests/project-performance-test-plan.jmx),
run by `run-performance-tests.sh` (JMeter 5.6.3, non-GUI). Two thread groups hit the two
write-heavy endpoints with real JWTs minted through the real register and login calls.

### Local stack (`docker compose`, 16 CPUs), 2026-09-29: 360 samples, 0 errors

| Endpoint | Load | Throughput | Mean | Median | p90 | p95 | p99 | Max | Errors |
|---|---|---|---|---|---|---|---|---|---|
| `POST /api/projects` | 200 requests, 20 concurrent Clients | 21.0 req/s | 17 ms | 15 ms | 24 ms | 28 ms | 39 ms | 53 ms | 0 |
| `POST /api/designs/projects/{id}/documents` | 50 requests, 10 concurrent Architects | 5.6 req/s | 24 ms | 21 ms | 34 ms | 35 ms | 39 ms | 39 ms | 0 |

### Azure (App Service B1, shared by three services), 2026-09-29: 65 samples, 1 error

The plan's default load overwhelmed the User Service on this tier (70% errors), so this
run uses a reduced load.

| Endpoint | Load | Throughput | Mean | Median | p95 | Max | Errors |
|---|---|---|---|---|---|---|---|
| `POST /api/projects` | 25 requests, 5 concurrent Clients | 2.46 req/s | 692 ms | 423 ms | 1,748 ms | 3,135 ms | 0 |
| `POST /api/designs/projects/{id}/documents` | 9 requests, 3 concurrent Architects | 0.90 req/s | 3,159 ms | 702 ms | 8,572 ms | 8,572 ms | 1 (`500`) |

### What the results say

- Locally both endpoints are fast and stable at the default load.
- The current Azure deployment cannot sustain that load: one small shared instance with
  no autoscale. That is a capacity finding, not a code defect, and is documented rather
  than hidden.
- An earlier local run (2026-09-27) saw 2 deadlocks in 50 design uploads. It did not
  reproduce in two later runs and nothing was changed to fix it, so it is carried forward
  as something to watch, not closed.
- The Azure design-upload `500` (n=1, 8.2 s before failing) is the same shape as that
  deadlock but cannot be confirmed as the same cause: Application Insights is only wired
  into the User Service.
- With n = 9 the Azure design-upload percentiles all land on the worst sample; read it as
  "8 fast, 1 very slow".
- Both runs hit the services directly. They predate the gateway's deployment to Azure,
  so the gateway hop is not in these numbers.

Full tables, setup-call timings and the reproduction commands are in the two results files.

## End-to-end tests (Selenium)

**Not in this story.** Selenium browser tests are delivered by a separate story, and
there is no Selenium code in the repository yet, so there is nothing to report here and no
results have been invented. This section is the place to fill in once that story merges.

| Scenario | Roles | Result |
|---|---|---|
| _to be filled in from the Selenium story_ | | |

What covers the full flow today, short of a browser: the integration tests exercise each
service's real HTTP endpoints, database and Kafka; the gateway tests boot the real gateway
against stub clusters and assert both routing and refusals; and the frontend tests render
the pages against stubbed services.

## Known gaps

1. **No end-to-end browser tests yet.** Belongs to the Selenium story; this document needs
   its scenarios added then.
2. **Coverage is a reported number, not an enforced one.** CI does not fail below 70%.
3. **Performance is measured on two endpoints only**, not on Construction, Payment, the
   dashboards or the gateway, and the Azure run is at reduced load.
4. **Construction Service and Payment Service are not deployed to Azure**, so they have
   local test and performance coverage only.
5. **One frontend test is close to its time limit** (see above).
