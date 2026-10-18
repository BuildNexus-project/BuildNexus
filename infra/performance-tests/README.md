# Performance testing (SCRUM-45 / US-32)

The JMeter plan exercises authenticated project creation, design upload,
Client workflows and construction reporting against a running BuildNexus
system. It records response times, throughput and request failures, and
produces an HTML dashboard.

The current runner executes on demand; it has no automated performance
threshold gate and is not connected to `ci.yml` yet. A successful JMeter
process alone does not prove that its HTTP requests passed. Inspect the JTL
results and dashboard as described below.

[RESULTS.md](RESULTS.md) and [AZURE-RESULTS.md](AZURE-RESULTS.md) contain
historical local and Azure measurements. They do not cover the current
cross-service workloads; record a fresh run before comparing performance.

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
- Install Python 3.10+ for `prepare-fixtures.py`; it uses only the Python
  standard library. The runner finds `python3` or `python`; set `PYTHON_CMD`
  to the executable if needed. Use Bash (Git Bash on Windows) for the runner.
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
  -Jproject_threads=2 -Jproject_rampup=1 -Jproject_loops=2 \
  -Jdesign_threads=1 -Jdesign_rampup=1 -Jdesign_loops=1 \
  -Jjourney_threads=2 -Jjourney_rampup=1 -Jjourney_loops=2 \
  -Jreport_threads=1 -Jreport_rampup=1 -Jreport_loops=2 \
  -Jjourney_think_ms=100 -Jreport_think_ms=100
```

The runner prints the new run directory beneath `results/validation/`.
Open its `report/index.html` and check that **every** sampler has 0% errors.
Check its `results.jtl` for `success=true` on every row and the following
sample counts:

| Sampler(s) | Expected count |
|---|---|
| `Create Project` | 4 |
| `Upload Design Document` | 1 |
| Each of the seven Client journey labels above | 4 each (28 total) |
| `Admin Login For Reports` | 1 |
| `Construction Reports` | 2 |
| All samples, including setup for project/design groups | **48** |

`Create Project For Design Upload` is a separate setup sampler and does not
count toward the four measured creates. Missing samples indicate an
incomplete workflow even if JMeter itself exits successfully.

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

## Default workload and outputs

From `infra/performance-tests`:

```bash
./run-performance-tests.sh
```

The existing project/design workloads retain their defaults: 20 Client
threads create 10 projects each, and 10 Architect threads upload 5 documents
each, with a 10-second ramp-up per group. The Client journey adds 2 concurrent
users with 5 loops each and a 2-second ramp-up. Construction reporting adds
1 Admin thread with 5 loops and a 1-second ramp-up. These are configurable
finite workloads; named smoke/load/stress profiles and pass/fail performance
thresholds are not implemented yet. By default, a Client journey pauses
500 ms before each of its seven requests, and the reporting group pauses
1,000 ms before each measured report request. These pauses are configurable.

Each invocation gets a new directory beneath `results/<env_label>/`, named
with a UTC timestamp and unique suffix. Without `-Jenv_label`, this is
`results/local/<run-id>/`, containing:

- `results.jtl`: raw samples.
- `report/index.html`: JMeter HTML dashboard.
- `jmeter.log`: JMeter execution log.

These outputs are ignored by Git. Runs with the same environment label retain
separate evidence instead of replacing earlier reports. The runner prints
the exact paths after completion. Use a simple environment label such as
`validation`, `local` or `azure`.

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
| `env_label` | `local` | Parent directory: `results/<env_label>/`; every run gets its own timestamped subdirectory. |
| `gateway_protocol` / `gateway_host` / `gateway_port` | `http` / `localhost` / `5000` | Fixture setup, Client journey and reporting target. |
| `auth_protocol` / `auth_host` / `auth_port` | Gateway settings | Project/design setup: authentication and user-role updates. |
| `project_protocol` / `project_host` / `project_port` | Gateway settings | Project/design setup: project creation and staff assignment. |
| `design_protocol` / `design_host` / `design_port` | Gateway settings | Design-upload target. |
| `admin_email` / `admin_password` | `ADMIN_EMAIL` / `ADMIN_PASSWORD`, falling back to local seeded Admin | Admin account for fixture preparation, promotion, assignment and reporting. |
| `project_threads` / `project_rampup` / `project_loops` | `20` / `10` seconds / `10` | Concurrent Clients, ramp-up and projects per user. |
| `design_threads` / `design_rampup` / `design_loops` | `10` / `10` seconds / `5` | Concurrent Architects, ramp-up and uploads per user. |
| `journey_threads` / `journey_rampup` / `journey_loops` | `2` / `2` seconds / `5` | Concurrent Clients, ramp-up and seven-request journeys per user. |
| `report_threads` / `report_rampup` / `report_loops` | `1` / `1` second / `5` | Concurrent Admin users, ramp-up and report requests per user. |
| `journey_think_ms` | `500` | Pause before each of the seven Client journey requests, in milliseconds. |
| `report_think_ms` | `1000` | Pause before each measured report request, in milliseconds; excludes Admin login. |
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
