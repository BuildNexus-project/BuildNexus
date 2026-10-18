# Performance testing (SCRUM-45 / US-32)

This JMeter plan exercises authenticated project creation and design-document
upload against a running BuildNexus environment. It records response times,
throughput and request failures, and generates an HTML dashboard.

The current runner executes on demand; it has no automated performance
threshold gate and is not connected to `ci.yml` yet. A successful JMeter
process alone does not prove that its HTTP requests passed. Inspect the JTL
results and dashboard as described below.

[RESULTS.md](RESULTS.md) and [AZURE-RESULTS.md](AZURE-RESULTS.md) contain
historical local and Azure measurements. They are not a new baseline for the
repaired authentication flow; record a fresh run before comparing performance.

## What the plan covers

`project-performance-test-plan.jmx` has two concurrent thread groups:

| Thread group | Endpoint under load | What each thread does |
|---|---|---|
| Project Creation Load | `POST /api/projects` | Registers and logs in as a Client once, then repeatedly creates projects. |
| Design Upload Load | `POST /api/designs/projects/{id}/documents` | Registers and logs in as a Client, registers a future Architect, creates a project as Client, logs in as Admin, promotes the future Architect, logs in as that Architect, assigns its Architect as Admin, then repeatedly uploads a design. |

Registration requires `fullName`, `email`, `phoneNumber` and `password`.
The plan supplies a valid test phone number. Every registration creates a
**Client** account; supplying a `role` in the signup payload cannot create an
Architect. The design setup therefore calls `PUT /api/users/{id}` with the
Admin token and the account's `fullName`, `email` and `role: "Architect"`.
It logs in as Architect **after** promotion so the new token carries that role.
The Admin then assigns the Architect through
`PUT /api/projects/{projectId}/architect` before any uploads.

Every test account obtains a real JWT through `/api/auth/login`. The Client
token creates projects, the Admin token changes roles and assigns staff, and
the assigned Architect token uploads documents. Both thread groups stop the
affected thread when an assertion fails (`stopthread`), which avoids cascading
requests with missing tokens or project IDs. Other threads can finish, so
missing workload samples must still be treated as an incomplete run.

[assertions/validate-response.groovy](assertions/validate-response.groovy)
checks that returned fixture IDs are valid, token and ID extraction succeeds,
registration returns the default Client role, and login and promotion return
the expected role. HTTP status assertions check each endpoint's response code.

Use the `Create Project` and `Upload Design Document` sampler rows for workload
measurements. Registration, login, promotion and assignment are setup samples;
their failures still invalidate the run, but their latency should be explained
separately from the two measured endpoints.

`fixtures/sample-design.pdf` is a small synthetic PDF with a `%PDF-` header.
It exercises the upload path with a file below the 10 MB limit. Its results do
not establish performance for large real-world drawings.

## Prerequisites

- Prepare `infra/.env` using the instructions in [../README.md](../README.md),
  then start the local stack from the repository root:

  ```bash
  docker compose -f infra/docker-compose.yml up -d --build --wait
  ```

  The default target is the API Gateway at `http://localhost:5000`; it forwards
  authentication, project and design requests to their respective services.
  Starting containers does not prove application readiness: the short
  validation run below verifies the authenticated paths used by the workload.
- Install [Apache JMeter](https://jmeter.apache.org/download_jmeter.cgi) 5.6+
  and a compatible Java runtime. Put JMeter's `bin/` directory on `PATH` or set
  `JMETER_CMD` to its executable. JMeter is not vendored in the repository.
- Use Bash (Git Bash on Windows) to run `run-performance-tests.sh`.
- The design scenario needs an active Admin account. The default properties
  target the local stack's seeded development Admin; override `admin_email`
  and `admin_password` for environments with different credentials.

## Short validation run

Run this before collecting a performance baseline:

```bash
cd infra/performance-tests
./run-performance-tests.sh -Jenv_label=validation \
  -Jproject_threads=2 -Jproject_rampup=1 -Jproject_loops=2 \
  -Jdesign_threads=1 -Jdesign_rampup=1 -Jdesign_loops=1
```

Open `results/validation/report/index.html` and check that **every** sampler
has 0% errors. Check `results/validation/results.jtl` for `success=true` on
every row, **17 total samples**, exactly **4 `Create Project` samples** and
**1 `Upload Design Document` sample**. `Create Project For Design Upload` is a separate setup
sampler and does not count toward those four measured creates. Missing samples
indicate an incomplete workflow even if JMeter itself exits successfully.

This small run verifies authentication, role assignment, correlation and file
upload. It is not evidence of capacity or a statistically useful p95 latency.

### On Windows

`JMETER_CMD`, rather than `JMETER_BIN`, is deliberate: `jmeter.bat` uses
`JMETER_BIN` internally. Pointing that variable at an executable can break its
launcher. For the JMeter installation in this workspace's Windows environment,
run from Git Bash:

```bash
export JMETER_CMD="/c/Users/malit/Downloads/apache-jmeter-5.6.3/apache-jmeter-5.6.3/bin/jmeter.bat"
./run-performance-tests.sh -Jenv_label=validation \
  -Jproject_threads=2 -Jproject_rampup=1 -Jproject_loops=2 \
  -Jdesign_threads=1 -Jdesign_rampup=1 -Jdesign_loops=1
```

Other machines should substitute their own installation path.

## Default workload and outputs

From `infra/performance-tests`:

```bash
./run-performance-tests.sh
```

The default workload uses 20 Client threads creating 10 projects each and 10
Architect threads uploading 5 documents each. Both groups ramp up over 10
seconds. These are configurable finite workloads, not named smoke/load/stress
profiles. The current plan has no think time or timed steady-state period;
requests run as fast as each thread can complete them.

Without `-Jenv_label`, the runner writes:

- `results/local/results.jtl`: raw samples.
- `results/local/report/index.html`: JMeter HTML dashboard.

Both are ignored by Git. The runner replaces the previous output for the same
environment label, so copy results elsewhere before rerunning if they must be
retained. Use a simple label such as `validation`, `local` or `azure`.

## Targets and workload settings

The three protocol/host/port settings all default to the same local gateway.
Separate overrides remain available for older deployments that expose User,
Project and Design services directly. Authentication, Admin login and user-role
updates use `auth_*`; project creation and staff assignment use `project_*`;
uploads use `design_*`.

For a gateway on another host, set all three service targets to that gateway.
For direct-service testing, set each target to the corresponding service.
These measure different request paths; record the chosen path with results.

```bash
# Direct-service settings for an existing Azure deployment; confirm hosts first.
./run-performance-tests.sh -Jenv_label=azure \
  -Jauth_protocol=https -Jauth_host=buildnexus-user-service-2026.azurewebsites.net -Jauth_port=443 \
  -Jproject_protocol=https -Jproject_host=buildnexus-project-service-2026.azurewebsites.net -Jproject_port=443 \
  -Jdesign_protocol=https -Jdesign_host=buildnexus-design-service-2026.azurewebsites.net -Jdesign_port=443

# A larger finite workload, after the short validation run passes.
./run-performance-tests.sh -Jproject_threads=50 -Jproject_loops=20 -Jdesign_threads=25 -Jdesign_loops=10
```

| Property | Default | Meaning |
|---|---|---|
| `env_label` | `local` | Output directory: `results/<env_label>/`. |
| `auth_protocol` / `auth_host` / `auth_port` | `http` / `localhost` / `5000` | Authentication and user-role update target. |
| `project_protocol` / `project_host` / `project_port` | `http` / `localhost` / `5000` | Project creation and architect-assignment target. |
| `design_protocol` / `design_host` / `design_port` | `http` / `localhost` / `5000` | Design-upload target. |
| `admin_email` / `admin_password` | Local seeded development Admin | Admin account for Architect promotion and assignment. |
| `project_threads` / `project_rampup` / `project_loops` | `20` / `10` seconds / `10` | Concurrent Client users, ramp-up and projects per user. |
| `design_threads` / `design_rampup` / `design_loops` | `10` / `10` seconds / `5` | Concurrent Architects, ramp-up and uploads per user. |

## Test data and repeated runs

Each thread registers test users with unique timestamped emails
(`jmeter.*.<run-id>@loadtest.invalid`) and creates its own projects. Repeated
runs add accounts, projects and documents to the target database; the plan
does not delete them afterward. Use a dedicated development/test environment
and record its data state when comparing measurements.
