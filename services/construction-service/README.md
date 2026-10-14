# Construction Service

Owns the build itself: the milestones a Project Manager plans for an approved
project, how far through them the build is, and the start, completion and
handover of construction (US-12 to US-14). It also serves the construction half
of the combined report (US-19) and of the role dashboards (US-21).

## Stack
- ASP.NET Core 10 Web API
- ADO.NET over MySQL (MySqlConnector) — direct SQL only, no ORM
- JWT bearer authentication (validation only — the User Service issues tokens)
- Confluent.Kafka, through a transactional outbox

Publishes to the `construction-events` topic — `ConstructionStarted`,
`ConstructionCompleted` and `MilestoneCompleted` (US-14, US-24) — drained from an
outbox written in the same transaction as the change it announces. It consumes
three topics, each to keep a fact it needs locally instead of asking another
service at request time:

| Topic            | Event               | What this service records                                        |
|------------------|---------------------|------------------------------------------------------------------|
| `design-events`  | `DesignApproved`    | The project's design is approved, so milestones may be planned (US-23) |
| `project-events` | `ProjectCreated`    | Which Client owns the project, so their reads are scoped (US-13) |
| `payment-events` | `FinalPaymentSettled` | The final payment is in, which the handover gate waits for (US-14) |

## Database
Owns `buildnexus_construction_db`. No other service may query it or hold a
foreign key into it, and this service holds none into theirs — every
`project_id` is a plain column copied off an event or a request.

| Table                      | Holds                                                              |
|----------------------------|--------------------------------------------------------------------|
| `milestone_setups`         | One row per project whose design is approved — the gate for milestones |
| `construction_milestones`  | The milestones: name, status, optional due date (US-12, US-21)     |
| `construction_phases`      | One row per project whose build has started: `Started`, `Completed`, `HandedOver` |
| `construction_outbox_events` | Events waiting to be published                                   |
| `project_owners`           | Which Client owns each project                                     |
| `payment_settlements`      | Which projects have had their final payment settled                |

The schema lives in `Migrations/` as numbered `.sql` files, embedded in the
assembly and applied by [DbUp](https://dbup.readthedocs.io) when the service
starts, exactly as in the other services. Never edit a script that has already
run — add the next number instead. The service does **not** create the database
itself; it must already exist, the way `MYSQL_DATABASE` creates it when the
Docker container starts.

## Run locally
Through the local stack (recommended — brings up MySQL and Kafka too):

```bash
cd ../../infra && docker compose up -d
```

Or natively, with `docker compose up -d construction-db kafka` first, and the
Project Service running if you want the Project Manager dashboard. One-time setup
per machine, so the signing key stays out of the repository:

```bash
dotnet user-secrets set "Jwt:SigningKey" "<the Jwt__SigningKey value from infra/.env.example>"
dotnet run
```

The service listens on `http://localhost:5004`, with Swagger UI at `/swagger` in
Development. Its database is published on **3310**.

## Endpoints

Every call goes through the API Gateway on `http://localhost:5000`, which
validates the token and proxies it here unchanged. This service re-validates it
and enforces its own role checks — the gateway makes no authorization decisions.

| Method | Route                                                      | Allowed roles         |
|--------|------------------------------------------------------------|-----------------------|
| POST   | `/api/construction/projects/{projectId}/milestones`         | ProjectManager        |
| POST   | `/api/construction/projects/{projectId}/milestones/from-template` | ProjectManager  |
| GET    | `/api/construction/projects/{projectId}/milestones`         | ProjectManager        |
| PATCH  | `/api/construction/milestones/{id}/status`                  | ProjectManager        |
| PUT    | `/api/construction/milestones/{id}/due-date`                | ProjectManager        |
| GET    | `/api/construction/projects/{projectId}/progress`           | ProjectManager        |
| POST   | `/api/construction/projects/{projectId}/start`              | ProjectManager        |
| POST   | `/api/construction/projects/{projectId}/complete`           | ProjectManager        |
| POST   | `/api/construction/projects/{projectId}/handover`           | ProjectManager        |
| GET    | `/api/construction/projects/{projectId}/phase`              | ProjectManager        |
| GET    | `/api/construction/projects/{projectId}/progress-summary`   | Client (own project)  |
| GET    | `/api/construction/milestone-setups`                        | Admin                 |
| GET    | `/api/construction/reports/progress`                        | Admin, ProjectManager |
| GET    | `/api/construction/dashboard/client`                        | Client                |
| GET    | `/api/construction/dashboard/project-manager`               | ProjectManager        |
| GET    | `/health`                                                   | Anonymous             |

Milestones open once a project's design is approved: creating one for a project
with no `milestone_setups` row is refused with `400`. A milestone moves between
`NotStarted`, `InProgress` and `Completed`; the project's progress is milestones
completed over milestones planned, recomputed on every read and never stored.
Starting construction needs an approved design and at least one milestone,
completing it needs every milestone `Completed`, and handover needs the build
completed and the final payment settled — each checked inside the transaction
that writes, and refused with a `409` and a `reason` saying which.

## Milestone due dates (US-21)

A milestone may have a due date: a calendar day, optional, `yyyy-MM-dd` on the
wire and a `DATE` column in the table — no time of day and no timezone, so "due
on the 5th" is the 5th wherever it is read.

- Set it when creating a milestone (`dueDate` on `POST .../milestones`), or set,
  change and clear it afterwards with `PUT /api/construction/milestones/{id}/due-date`
  (`{ "dueDate": "2026-10-05" }`, or `{ "dueDate": null }` to clear). The template
  milestones are planted without one and get theirs this way.
- It is additive. A milestone with no date is created, moved and counted exactly as
  US-12 defined it — outstanding until completed, and never late. Every milestone
  that existed before migration 010 has none.
- A milestone is **overdue** when it has a date, the date is before today, and it is
  not completed. Completed milestones are never overdue however old their date, and
  one due today is not late yet.
- A completed milestone may still be given a date; it changes nothing about whether it
  is outstanding.

## Role dashboards (US-21)

The construction slice of the Client's and the Project Manager's dashboards. The
other slices come from the other services; the page joins them on the project id
(see `frontend/README.md`, *Role dashboards*).

- **Client** — `{ projects[] }`: build progress for each project the caller owns that
  has milestones planned and has not been handed over, each
  `{ projectId, phaseStatus, totalMilestones, completedMilestones, progressPercent }`.
  Ownership is this service's own `project_owners` record, so no other service is
  asked, and the endpoint takes no id. A project that is planned but not started is
  listed with a `null` `phaseStatus` and `0` progress; one with no milestones is
  left out, since a zero would read as a stalled build.
- **Project Manager** — `{ activeBuildCount, activeBuilds[], milestonesDue }`, for the
  projects **they are assigned to**. This service records who owns a project but not
  which Project Manager runs it, so it asks the Project Service
  (`GET /api/projects`, forwarding the caller's own token — the same arrangement the
  Design Service uses) and reads only those projects; nothing in the request says
  whose dashboard it is. That same answer names each project, so every build and
  milestone carries its `projectName`.
  - `activeBuilds` are the builds that have been started and not handed over
    (`Started` or `Completed`), each with its progress and `outstandingMilestones`.
    A project whose status is `Completed` is still read, because its build may be
    awaiting handover.
  - `milestonesDue` is `{ totalCount, overdueCount, milestones[] }`. **Due means
    outstanding** — every milestone not yet `Completed` on those builds — and
    `overdueCount` is how many of them have a due date that has passed. The list is
    the first ten: dated milestones first, soonest (so most overdue) first, then
    undated ones; within a date, those `InProgress` ahead of those `NotStarted`.
    Neither count is capped. Each item carries its `dueDate` and an `isOverdue` flag.
  - A Project Manager with no project assigned gets an empty dashboard. If the Project
    Service cannot be reached the endpoint answers **`502`** with a reason, never an
    empty dashboard: an outage must not read as "you have nothing under way".
  - A build a different Project Manager started on a project assigned to you is yours
    to see; one you started on a project assigned to someone else is theirs.

"Overdue" is judged against today's UTC date, from an injected clock, and the same
day is used for the count and for each milestone's flag so they cannot disagree. The
progress percentage comes from one shared definition (`ProgressPercentage`) used by the
per-project screen, the report and the dashboards, so a project's figure is the same
number wherever it is shown.

## Configuration
- `ConnectionStrings:ConstructionDb` — MySQL connection string
- `Jwt:Issuer`, `Jwt:Audience` — the shared convention (`BuildNexusAuth` /
  `BuildNexusServices`)
- `Jwt:SigningKey` — **deliberately empty in `appsettings.json`.** Supplied per
  environment: `Jwt__SigningKey` from `infra/.env` in Docker, or User Secrets for a
  native run. The service refuses to start if it is missing or shorter than 32 bytes.
- `Kafka:BootstrapServers` — the broker, supplied as `Kafka__BootstrapServers`:
  `kafka:9092` in the stack, `localhost:29092` for a native run.
- `Services:ProjectService:BaseUrl` — where to reach the Project Service for the
  Project Manager dashboard. `http://project-service:8080/` in the stack (set by
  `infra/docker-compose.yml`, which also starts this service after it),
  `http://localhost:5002/` for a native run. Validated at startup.

## Tests

```bash
cd ../construction-service-tests && dotnet test
```

**Most tests run without MySQL and without Kafka.** The repositories and the Project
Service client are stood in for. The exceptions are tagged
`[Trait("Category", "Integration")]` and need the real thing: the
`*DatabaseTests` classes run SQL against `construction-db`, and
`ConstructionKafkaIntegrationTests` does a real round trip through the broker. Start
them with `cd infra && docker compose up -d --wait construction-db kafka`.

```bash
dotnet test --filter "Category!=Integration"   # what needs nothing running
dotnet test --filter "Category=Integration"    # what needs construction-db and kafka
```

`EndpointRoleDeclarationTests` walks the controllers by reflection and fails if any
endpoint neither declares its roles nor is explicitly `[AllowAnonymous]`, and pins each
endpoint's roles. The US-21 dashboards are covered in layers:
`ConstructionDashboardEndpointTests` over fakes (caller scoping, token forwarding, the
`502` and `401` paths, the fixed clock, the list cap), `HttpProjectDirectoryClientTests`
(what is sent, and that any answer other than a list is "unavailable", never empty),
`ConstructionDashboardRepositoryDatabaseTests` and `MilestoneDueDateDatabaseTests`
against the real engine (ownership and project scoping, the "under way" gate, ordering,
the overdue boundaries, and that a day round-trips through the `DATE` column unshifted),
and `ProgressPercentageTests` for the shared percentage.
