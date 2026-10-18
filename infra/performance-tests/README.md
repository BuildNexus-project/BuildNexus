# Performance testing (SCRUM-45 / US-32)

The JMeter plan exercises authenticated project creation, design upload,
Client workflows and construction reporting against a running BuildNexus
system. It records response times, throughput and request failures, and
produces an HTML dashboard and a machine-readable pass/fail summary.

The runner provides smoke, load and bounded stress profiles. It fails when
JMeter fails, requests exceed their latency/error budgets, setup fails or the
expected workload is incomplete. The same smoke runner is integrated into
the shared GitHub Actions workflow and its combined test report.

[RESULTS.md](RESULTS.md) records the current SE3112 smoke/load/stress
measurements. [DEMO.md](DEMO.md) provides the two-minute walkthrough,
contribution scope and VIVA questions. Earlier two-workload measurements
remain in [LEGACY-RESULTS.md](LEGACY-RESULTS.md) and
[AZURE-RESULTS.md](AZURE-RESULTS.md); their defaults and paths are historical.

## What the plan covers

`project-performance-test-plan.jmx` has four concurrent thread groups:

| Thread group | Requests under load | What each thread does |
|---|---|---|
| Project Creation Load | `POST /api/projects` | Registers and logs in as a Client once, then repeatedly creates projects. |
| Design Upload Load | `POST /api/designs/projects/{id}/documents` | Creates Client, Architect and project fixtures once, assigns the Architect, then repeatedly uploads a design. |
| Client Journey Load | Login, project list/detail, invoices, payment history/dashboard and construction progress | Uses a prepared Client and its populated project; repeats the seven-request workflow below. |
| Construction Reports Load | `GET /api/construction/reports/progress` | Logs in as Admin once, then repeatedly reads construction reports. |

The Client journey sends these requests in order on every loop:

| Sampler label | Endpoint |
|---|---|
| `Login Under Load` | `POST /api/auth/login` |
| `List Projects` | `GET /api/projects` |
| `View Project` | `GET /api/projects/{projectId}` |
| `Client Invoices` | `GET /api/payments/my-projects/{projectId}/invoices` |
| `Payment History` | `GET /api/payments/my-projects/{projectId}/history` |
| `Payment Dashboard` | `GET /api/payments/dashboard/client` |
| `Construction Progress` | `GET /api/construction/projects/{projectId}/progress-summary` |

The journey crosses User, Project, Payment and Construction services through
the Gateway. Its project, approved design, invoice, partial payment and
milestones are prepared through real APIs before JMeter starts, so the reads
exercise populated data. Bounded polling waits for Kafka-driven state to
become available. Fixture failure stops the runner before measured requests.

Registration requires `fullName`, `email`, `phoneNumber` and `password`.
Every registration creates a **Client** account; a signup `role` cannot create
an Architect or ProjectManager. Setup promotes staff through
`PUT /api/users/{id}` using an Admin token, then logs in again to obtain a JWT
with the assigned role. Project staff assignment precedes restricted actions.
The design-upload group follows this flow inside JMeter; the Client journey
uses fixtures prepared before JMeter.

[assertions/validate-response.groovy](assertions/validate-response.groovy)
checks response data as well as successful HTTP statuses, including tokens,
roles and correlated fixture IDs. Thread groups stop an affected thread when
an assertion fails (`stopthread`), avoiding requests with missing tokens or
project IDs. Other threads can finish, so missing workload samples still
invalidate a run.

For latency analysis, use `Create Project`, `Upload Design Document`, the
seven Client journey labels, and `Construction Reports`. Registration,
promotion, assignment and `Admin Login For Reports` are setup samples; their
failures invalidate the run, but explain their latency separately. Fixture
preparation happens outside JMeter and does not appear in its JTL results.

`fixtures/sample-design.pdf` is a small synthetic PDF with a `%PDF-` header.
It exercises the upload path with a file below the 10 MB limit. Its results
do not establish performance for large real-world drawings.

## Prerequisites

- Prepare `infra/.env` using the instructions in [../README.md](../README.md),
  then start the local stack from the repository root:

  ```bash
  docker compose -f infra/docker-compose.yml up -d --build --wait
  ```

  The default target is the API Gateway at `http://localhost:5000`. The
  cross-service workloads require the User, Project, Design, Payment,
  Construction and Kafka services. Container startup alone does not prove
  application readiness; fixture preparation verifies the required paths.
- Install [Apache JMeter](https://jmeter.apache.org/download_jmeter.cgi) 5.6+
  and a compatible Java runtime. Put JMeter's `bin/` directory on `PATH` or set
  `JMETER_CMD` to its executable. JMeter is not vendored in the repository.
- Install Python 3.10+ for fixture preparation and result checking; both use
  only the Python standard library. The runner finds `python3` or `python`;
  set `PYTHON_CMD` to the executable if needed. Use Bash 4+ (Git Bash on
  Windows) for the runner.
- Use an active Admin account. Defaults target the local stack's seeded
  development Admin. Set `ADMIN_EMAIL` and `ADMIN_PASSWORD`, or pass
  `-Jadmin_email=...` and `-Jadmin_password=...`; explicit properties take
  precedence over environment variables. The runner passes credentials to
  both tools through the process environment, keeping them out of JMeter's
  logged properties. [preprocessors/admin-login.groovy](preprocessors/admin-login.groovy)
  JSON-encodes the Admin login payload, including custom password characters.

## Short validation run

Run this before collecting a performance baseline:

```bash
cd infra/performance-tests
./run-performance-tests.sh -Jenv_label=validation \
  -Jprofile=smoke
```

The runner prints the new run directory beneath `results/validation/`.
Read `summary.md` for the verdict and per-endpoint metrics, then open
`report/index.html` for graphs. The checker requires **every** sampler to
succeed in smoke and verifies these sample counts:

| Sampler(s) | Expected count |
|---|---|
| `Create Project` | 4 |
| `Upload Design Document` | 1 |
| Each of the seven Client journey labels above | 4 each (28 total) |
| `Admin Login For Reports` | 1 |
| `Construction Reports` | 2 |
| All samples, including setup for project/design groups | **48** |

`Create Project For Design Upload` is a separate setup sampler and does not
count toward the four measured creates. Missing or extra samples fail the
completeness check even if JMeter itself exits successfully.

This small run verifies authentication, staff assignment, fixture correlation,
file upload and populated reads across services. It is not evidence of
capacity or a statistically useful p95 latency.

### On Windows

`JMETER_CMD`, rather than `JMETER_BIN`, is deliberate: `jmeter.bat` uses
`JMETER_BIN` internally. Pointing that variable at an executable can break its
launcher. For the JMeter installation in this workspace's Windows environment,
run from Git Bash:

```bash
export JMETER_CMD="/c/Users/malit/Downloads/apache-jmeter-5.6.3/apache-jmeter-5.6.3/bin/jmeter.bat"
```

Then run the short validation command above. Other machines should substitute
their own installation path.

## Profiles and performance gates

From `infra/performance-tests`, choose a versioned profile. Running without
arguments uses `smoke`:

```bash
./run-performance-tests.sh -Jprofile=smoke
./run-performance-tests.sh -Jprofile=load
./run-performance-tests.sh -Jprofile=stress
```

| Setting | Smoke | Load | Stress |
|---|---|---|---|
| Concurrent users: project / design / journey / report | 2 / 1 / 2 / 1 (6 total) | 5 / 3 / 10 / 2 (20 total) | Load baseline multiplied by 1, 2, 4 and 8 (20 / 40 / 80 / 160 total) |
| Loops per user: project / design / journey / report | 2 / 1 / 2 / 2 | 20 / 10 / 10 / 20 | Same as load for every step |
| Ramp-up per group | 1 second | 10 seconds | 10 seconds per step |
| Think time: project / design / journey / report | 100 / 100 / 100 / 100 ms | 250 / 500 / 250 / 500 ms | Same as load |
| Allowed errors for each measured endpoint | 0% | 1% | 1% |
| Total JTL samples including setup | 48 | 906 | 906 multiplied by the step multiplier |

The 20-user load profile is an **assumed team demonstration baseline**, not
a measured production traffic model. Users run finite loops; actual duration
depends on response times, ramp-up and think time. Think time applies before
each measured request; a Client journey has seven such pauses per loop.
Record the target environment and measured results before drawing conclusions.

Stress runs are separate bounded steps, each with fresh project/design setup.
They increase concurrency and stop at the first failing step: a threshold
breach, failed JMeter process, setup error or incomplete workload. The runner
prepares the maximum Client journey fixture pool once (80 fixtures for the
default steps) and reuses it across steps. You can shorten the sequence:

```bash
./run-performance-tests.sh -Jprofile=stress -Jstress_multipliers=1,2
```

If every step passes, the configured limit was not reached. This is not proof
of production capacity. Compare per-step latency, throughput and errors, and
use service logs or resource measurements to investigate any degradation.

The checker applies these p95 limits individually to the measured endpoints
in every profile:

| Sampler label | Property override | p95 limit |
|---|---|---|
| `Create Project` | `p95_project_create_ms` | 1,500 ms |
| `Upload Design Document` | `p95_design_upload_ms` | 3,000 ms |
| `Login Under Load` | `p95_login_ms` | 2,000 ms |
| `List Projects` | `p95_project_list_ms` | 1,000 ms |
| `View Project` | `p95_project_detail_ms` | 1,000 ms |
| `Client Invoices` | `p95_invoices_ms` | 1,000 ms |
| `Payment History` | `p95_history_ms` | 1,000 ms |
| `Payment Dashboard` | `p95_dashboard_ms` | 1,000 ms |
| `Construction Progress` | `p95_progress_ms` | 1,000 ms |
| `Construction Reports` | `p95_reports_ms` | 2,000 ms |

These are explicit assessment budgets, not established service-level
agreements. Each endpoint must satisfy its own latency and error budget, so
a fast high-volume endpoint cannot hide a slow low-volume one. Any failed
setup sample fails the run, regardless of the error budget. The checker also
requires exact expected counts per sampler, catching threads stopped by
response assertions. Setup latency is reported separately and is not subject
to these endpoint p95 limits. API fixture preparation remains outside the JTL.
The runner also requires the generated HTML dashboard to exist before a run
can pass.

For each endpoint, p95 uses the nearest-rank method: sort elapsed times and
select rank `ceil(0.95 * sample_count)`. Endpoint throughput is its sample count
divided by the time from its first request start to its last request end,
including gaps between requests. A tiny smoke sample has limited statistical
value even when its gate passes.

To demonstrate a threshold failure without changing service code, deliberately
set a 1 ms login budget:

```bash
./run-performance-tests.sh -Jprofile=smoke -Jenv_label=gate-demo -Jp95_login_ms=1
```

The checker evaluates the actual recorded responses against the stricter
budget and returns a failing exit status when they exceed it. This changes
the acceptance criterion; it does not inject faults or alter measurements.
Use the normal budgets for the baseline run.

## Outputs and exit statuses

Each invocation gets a new directory beneath `results/<env_label>/`, named
with a UTC timestamp and unique suffix. Without `-Jenv_label`, this is
`results/local/<run-id>/`. A smoke/load run contains:

- `effective.properties`: the resolved public configuration, including CLI
  overrides; Admin credentials are excluded.
- `results.jtl`: raw samples.
- `report/index.html`: JMeter HTML dashboard.
- `jmeter.log`: JMeter execution log.
- `summary.json` and `summary.md`: overall verdict, failures and per-endpoint
  sample counts, errors, p95 and throughput.

A stress run has a root `effective.properties` and
`stress-summary.json` / `stress-summary.md`. Each step has its own directory
such as `step-01-x1/` or `step-02-x2/`, containing the same artifacts as a
smoke/load run, including that step's effective configuration. Read the root
summary to see where the bounded sequence stopped.

The runner returns `0` only when its required checks pass, `1` for execution
or quality-gate failure, and `2` for invalid configuration. A successful JMeter
exit alone is insufficient. Inspect the summary's reason when a run fails;
an early fixture failure may occur before JMeter artifacts exist.

To recheck retained JTL evidence independently:

```bash
python3 check-performance.py --config results/local/<run-id>/effective.properties \
  --results results/local/<run-id>/results.jtl \
  --output-dir results/local/<run-id>
```

Replace `<run-id>` with the actual run directory, and include a step directory
when checking stress evidence. When investigating a failed JMeter process,
also pass its recorded exit code with `--jmeter-exit-code`; the standalone
checker defaults to `0` because it cannot infer the old process status from
the JTL. The runner supplies the actual status automatically.

These outputs are ignored by Git. Runs with the same environment label retain
separate evidence instead of replacing earlier reports. The runner prints
the exact paths after completion. Use a simple environment label such as
`validation`, `local` or `azure`.

The quality-gate checker has isolated tests for threshold boundaries, failed
setup, incomplete results and result parsing:

```bash
python3 -m unittest discover -s tests
```

## Run in GitHub Actions

After pushing the committed branch, open **Actions → CI → Run workflow**,
select the branch, enable **Run load testing (JMeter)** and start the workflow.
For a load-focused demonstration, disable the optional Selenium and mutation
checks. The existing build, unit, integration, Docker and static-analysis
jobs retain their own trigger rules.

The `load-tests` job runs only for an explicit manual dispatch with `run_load`
enabled. Pushes, pull requests and the weekly schedule skip it. It installs
Python 3.12, Java 17 and checksum-verified JMeter 5.6.3, runs the gate/report
tests, then builds and starts the Compose stack on the isolated runner.
Database credentials are disposable test values; JWT and internal service
keys are randomly generated for that job. No deployment secrets are needed.

The job waits for all five service health endpoints and the Gateway, then
calls the same local command:

```bash
bash infra/performance-tests/run-performance-tests.sh -Jprofile=smoke -Jenv_label=ci-load
```

The default profile requires 48 samples across six concurrent users, zero
request errors, complete setup and passing endpoint latency gates. An early
fixture failure stops execution; a failed gate or JMeter process fails the
job. CI smoke verifies the pipeline on its runner; collect load/stress
measurements on a documented target environment for performance conclusions.

The load job publishes its Markdown summary and retains a
`load-testing-report` artifact for 14 days, including available
`<run-id>/summary.json`, `summary.md`, `effective.properties`, `results.jtl`
and `report/`. The upload allowlist excludes the temporary credential CSV.
On failure, container status and recent logs appear in the job console.
Cleanup always removes the disposable runner's stack and volumes.

The shared **Test report** job downloads the artifact and adds endpoint p95,
throughput and error metrics separately from unit-test counts.
`ci-summary.py` checks the default smoke configuration and report consistency
before displaying measurements. A failed/cancelled load job stays failed
even with passing measurements. A requested run with missing or invalid
metrics fails the combined report and says measurements are unavailable;
a skipped run is explicitly marked skipped. Reports are never fabricated
from missing artifacts.

To preview reporting locally after copying a retained smoke summary into
`reports/load/<run-id>/summary.json`:

```bash
python3 infra/performance-tests/ci-summary.py --reports-dir reports/load \
  --job-result success --part details
```

## Targets and workload settings

The Gateway target defaults to `http://localhost:5000`. Set `gateway_protocol`,
`gateway_host` and `gateway_port` to run the cross-service workloads elsewhere.
The fixture preparer uses that same Gateway. The older `auth_*`, `project_*`
and `design_*` settings default to the Gateway settings and remain available
for deployments that expose those services directly. They affect the
project/design groups; the Client journey and reports use `gateway_*`.

```bash
# Run all workloads through another Gateway.
./run-performance-tests.sh -Jenv_label=test \
  -Jgateway_protocol=https -Jgateway_host=gateway.example.com -Jgateway_port=443

# Older direct-service Azure deployment: run project/design workloads only.
# Confirm service hosts before using these historical deployment addresses.
./run-performance-tests.sh -Jenv_label=azure \
  -Jjourney_threads=0 -Jreport_threads=0 \
  -Jauth_protocol=https -Jauth_host=buildnexus-user-service-2026.azurewebsites.net -Jauth_port=443 \
  -Jproject_protocol=https -Jproject_host=buildnexus-project-service-2026.azurewebsites.net -Jproject_port=443 \
  -Jdesign_protocol=https -Jdesign_host=buildnexus-design-service-2026.azurewebsites.net -Jdesign_port=443
```

| Property | Default | Meaning |
|---|---|---|
| `profile` | `smoke` | Load settings from `profiles/smoke.properties`, `profiles/load.properties` or `profiles/stress.properties`, then apply `-Jproperty=value` overrides. |
| `env_label` | `local` | Parent directory: `results/<env_label>/`; every run gets its own timestamped subdirectory. |
| `gateway_protocol` / `gateway_host` / `gateway_port` | `http` / `localhost` / `5000` | Fixture setup, Client journey and reporting target. |
| `auth_protocol` / `auth_host` / `auth_port` | Gateway settings | Project/design setup: authentication and user-role updates. |
| `project_protocol` / `project_host` / `project_port` | Gateway settings | Project/design setup: project creation and staff assignment. |
| `design_protocol` / `design_host` / `design_port` | Gateway settings | Design-upload target. |
| `admin_email` / `admin_password` | `ADMIN_EMAIL` / `ADMIN_PASSWORD`, falling back to local seeded Admin | Admin account for fixture preparation, promotion, assignment and reporting. |
| `project_threads` / `project_rampup` / `project_loops` | `2` / `1` second / `2` (smoke) | Concurrent Clients, ramp-up and projects per user. |
| `design_threads` / `design_rampup` / `design_loops` | `1` / `1` second / `1` (smoke) | Concurrent Architects, ramp-up and uploads per user. |
| `journey_threads` / `journey_rampup` / `journey_loops` | `2` / `1` second / `2` (smoke) | Concurrent Clients, ramp-up and seven-request journeys per user. |
| `report_threads` / `report_rampup` / `report_loops` | `1` / `1` second / `2` (smoke) | Concurrent Admin users, ramp-up and report requests per user. |
| `project_think_ms` / `design_think_ms` | `100` / `100` (smoke) | Pause in milliseconds before each measured create/upload request; excludes setup. |
| `journey_think_ms` / `report_think_ms` | `100` / `100` (smoke) | Pause in milliseconds before each measured journey/report request; excludes setup. |
| `stress_multipliers` | `1,2,4,8` | Bounded ascending concurrency multipliers for the stress profile. |
| `max_error_rate_pct` | `0` (smoke), `1` (load/stress) | Maximum failed-sample percentage for each measured endpoint. |
| `p95_*_ms` | See endpoint table above | Maximum endpoint p95 elapsed time in milliseconds. |
| `connect_timeout_ms` / `response_timeout_ms` | `5000` / `10000` ms | JMeter HTTP connection and response timeouts. |
| `fixture_timeout` | `60` seconds | Maximum wait for each Kafka-backed ownership or construction-plan projection during setup. |

Set `journey_threads=0` and `report_threads=0` to run only the older
project/design groups without the cross-service fixture setup. Gateway and
direct-service runs measure different request paths; record the chosen path
with the results.

## Test data and repeated runs

Each project/design thread registers users with unique timestamped emails
(`jmeter.*.<run-id>@loadtest.invalid`) and creates its own projects. Before
JMeter, `prepare-fixtures.py` creates a populated fixture for each Client
journey user using the APIs: an assigned project, an approved design, two
milestones (one Completed and one NotStarted, giving 50% progress), a 1,000
invoice and a 250 partial payment (750 still due). Fixtures include promoted
staff accounts required for those operations. Setup waits for the state
needed by downstream services and stops on a timeout or invalid response.

The preparer writes fixture identifiers and test credentials to a temporary
`fixtures.csv` in the ignored run directory. JMeter consumes its eight columns:
`email,password,client_id,project_id,invoice_id,payment_id,architect_id,milestone_id`.
The runner removes the CSV on exit, including failure. This only removes the
local credential file; it does not delete test data from the services.

Each thread group reads the CSV independently, assigning one row to each
thread. The thread group's outer loop runs once; the inner loop repeats the
session or report requests using that same row. This keeps each Client's
identity and project consistent across repeated sessions.

To inspect fixture setup separately with a local seeded Admin:

```bash
python3 prepare-fixtures.py --base-url http://localhost:5000 \
  --users 1 --output results/manual-fixtures.csv
```

The standalone command leaves the CSV for inspection. It contains test
credentials; remove it after use. Set `ADMIN_EMAIL` and `ADMIN_PASSWORD` for a
different Admin account.

Repeated runs add accounts, projects, designs, milestones, invoices and
payments to the target databases. Use a dedicated development/test environment
and record its data state when comparing measurements.
