# Project Service

Owner: [name]

Construction projects for BuildNexus: a Client submits their requirements here,
and the project moves through its lifecycle from this service.

Publishes business events to Kafka on the `project-events` topic.

## Stack
- ASP.NET Core 10 Web API
- ADO.NET over MySQL (MySqlConnector) — direct SQL only, no ORM
- JWT bearer authentication (validation only — the User Service issues tokens)

## Database
Owns `buildnexus_project_db`. No other service may query it or hold a foreign
key into it, and this service holds none into theirs — `projects.client_id`
records the Client from the token's `sub` claim as a plain column, not a key
into the User Service's `users` table.

The schema lives in `Migrations/` as numbered `.sql` files, embedded in the
assembly and applied by [DbUp](https://dbup.readthedocs.io) when the service
starts — set up the same way as the User Service. DbUp records each script it
has run in a `schemaversions` table and applies only the ones missing, so
starting against a database that is empty, one a few stories behind, or already
current all do the right thing.

The service does **not** create `buildnexus_project_db` itself — it must already
exist, the way `MYSQL_DATABASE` creates it when the Docker container starts. The
`buildnexus` account is granted access to only that one database, and checking
for a database's existence needs a connection to MySQL's own `mysql` schema,
which that account cannot reach. That scoping is deliberate, so the service
works within it rather than asking for broader access.

DbUp runs the same hand-written SQL we would otherwise apply by hand. It is not
an ORM and takes no part in queries: the data access is still ADO.NET with
direct SQL.

To add a schema change, drop the next numbered script into `Migrations/` and
restart the service:

```
Migrations/002_whatever_changed.sql
```

Never edit a script that has already run somewhere — DbUp has recorded it as
done and will not run it again. Add the next number instead.

## Run locally
Through the local stack (recommended — brings up MySQL too):

```bash
cd ../../infra && docker compose up -d
```

Or natively, which is faster to iterate on (start the database with
`docker compose up -d project-db` first). One-time setup per machine, so the
signing key stays out of the repository:

```bash
dotnet user-secrets init
dotnet user-secrets set "Jwt:SigningKey" "<the Jwt__SigningKey value from infra/.env.example>"
dotnet run
```

User Secrets are loaded automatically in Development only. Without them the
service refuses to start — that is the intended behaviour, not a bug.

Either way the service listens on `http://localhost:5002`, with Swagger UI at
`/swagger` in Development. Its database is published on **3307**, not 3306 —
`user-db` already has that port, and one service per schema means one container
per schema.

## Endpoints

| Method | Route                                        | Allowed roles                            |
|--------|----------------------------------------------|------------------------------------------|
| POST   | `/api/projects`                              | Client                                   |
| GET    | `/api/projects`                              | Client, Architect, ProjectManager, Admin |
| GET    | `/api/projects/{id}`                         | Client, Architect, ProjectManager, Admin |
| PATCH  | `/api/projects/{id}/status`                  | Architect, ProjectManager, Admin         |
| POST   | `/api/projects/{id}/cancellation`            | Client, Admin                            |
| PUT    | `/api/projects/{id}/architect`               | Admin                                    |
| PUT    | `/api/projects/{id}/project-manager`         | Admin                                    |
| GET    | `/api/projects/{id}/events`                  | Admin                                    |
| GET    | `/api/projects/oversight`                    | Admin                                    |
| GET    | `/api/projects/reports/status`               | Admin                                    |
| GET    | `/api/projects/reports/status/export`        | Admin                                    |
| GET    | `/api/projects/dashboard/client`             | Client                                   |
| GET    | `/api/projects/dashboard/architect`          | Architect                                |
| GET    | `/api/projects/dashboard/admin`              | Admin                                    |
| GET    | `/api/projects/notifications`                | Client, Architect                        |
| POST   | `/api/projects/notifications/{id}/read`      | Client, Architect                        |
| POST   | `/api/projects/notifications/read-all`       | Client, Architect                        |
| GET    | `/health`                                    | Anonymous                                |

The report, oversight, dashboard and notification endpoints are described in
their own sections below, and the live list with request and response bodies is
the service's Swagger UI at `/swagger`.

`POST /api/projects` is US-05: a Client describes the building they want and
the project is created with status `Pending`, waiting on the company to pick it
up. Client only, deliberately — an Architect, Project Manager or Admin holding a
perfectly valid token is refused with `403`, because submitting work on a
customer's behalf is not what this endpoint is for.

Two things are decided by the service rather than taken from the payload:

- **Who it belongs to.** `client_id` comes from the `sub` claim of the caller's
  own token, so a project cannot be submitted for somebody else by sending a
  different id.
- **The status.** It is `Pending` on creation and the caller has no say in it.

The requirements captured are the name, location, land size (perches), budget,
floors, bedrooms, bathrooms and garage spaces, plus free-text other
requirements. Everything but the last is required. Bedrooms, bathrooms and
garage spaces accept `0` — not every build is a house — while floors must be at
least 1. The numeric fields are modelled as nullable so a missing value is
refused by name rather than binding silently as zero, which for those three
would be a legitimate answer the Client never gave.

Sending `""` for other requirements stores `NULL`, which is also what a project
submitted without them has.

Validation failures come back as `400` with an RFC 7807 `errors` map keyed by
field name, which is what the React form reads to put each message under the
input that caused it.

The frontend never calls this service directly: every `/api/projects` call goes
through the API Gateway on `http://localhost:5000`, which validates the token
and proxies it here unchanged. This service re-validates it and enforces its own
role checks — the gateway makes no authorization decisions.

## Project status report (US-18)

An Admin-only view of the whole pipeline: every project grouped by its current
status, with the count and budget total of each group.

| Method | Route                                  | Allowed roles |
|--------|----------------------------------------|---------------|
| GET    | `/api/projects/reports/status`         | Admin         |
| GET    | `/api/projects/reports/status/export`  | Admin         |

Both take the same optional filters, which combine:

| Query parameter | Meaning |
|-----------------|---------|
| `status`        | Only these statuses. Repeat it (`?status=Pending&status=Designing`) or comma-separate (`?status=Pending,Designing`). Names are not case-sensitive; a number is not a status. Omit it for every status. |
| `from`, `to`    | First and last day to include, as `yyyy-MM-dd`. Both are inclusive whole days in UTC, so `from=to` is one day. Either may be left off. |

The date range is on `created_at` — the day the Client submitted the project.
`updated_at` moves with every status change, so a range over it would answer
"what was touched" rather than "what came in".

An unknown status, a date that is not a date, or a `from` after `to` is a `400`
with the reason under `errors.filter`, never a quietly widened report: an Admin
who misspells a status must not be handed the whole pipeline as if it were the
slice they asked for.

`GET /api/projects/reports/status` answers:

```json
{
  "generatedAt": "2026-09-30T08:00:00Z",
  "totalProjects": 3,
  "totalBudget": 147500000.00,
  "groups": [
    {
      "status": "Pending",
      "count": 1,
      "totalBudget": 18500000.00,
      "projects": [
        { "id": "…", "name": "Beachfront villa", "location": "Galle",
          "status": "Pending", "budget": 18500000.00,
          "createdAt": "2026-09-02T09:00:00", "updatedAt": "2026-09-02T09:00:00" }
      ]
    }
  ]
}
```

`groups` has one entry per status in scope, in lifecycle order (`Pending`,
`Designing`, `DesignApproved`, `Construction`, `Completed`, `Cancelled`) — and a
status with nothing in it is still there with `count: 0`, so the report keeps its
shape from one week to the next. A `status` filter limits which groups appear.
Projects inside a group are newest first, with `id` breaking a tie so two runs
over unchanged data read the same.

`GET /api/projects/reports/status/export` is the same report as a CSV download,
`project-status-report-<yyyy-MM-dd>.csv`: one row per project, grouped in
lifecycle order, with the columns `Status, Project ID, Name, Location, Budget,
Created (UTC), Last updated (UTC)`. Three details are deliberate:

- **Quoting.** A project name is whatever a Client typed, commas and quotes
  included, so a field containing one is quoted with inner quotes doubled
  (RFC 4180).
- **Formulas.** An Admin opens the file in Excel, so text starting with `=`, `+`,
  `-` or `@` is prefixed with an apostrophe and reads as plain text rather than
  running.
- **Encoding.** The file starts with a UTF-8 byte-order mark, so a spreadsheet
  reads non-ASCII names correctly instead of guessing.

Filtering is done by MySQL, with every value — including the status list — as a
bound parameter; grouping and ordering are done in code
(`ProjectStatusReportBuilder`), where they can be tested without a database. The
query lives in its own `IProjectReportRepository`, apart from the one a request
that writes goes through.

The React screen is `/admin/reports/project-status`. It exports the filter that
is *applied*, not one that has been ticked and not yet applied, so the file is
always the report on screen. No gateway change is needed: the existing
`/api/projects/{**catch-all}` route already covers these paths.

## Platform oversight (US-38)

An Admin-only list of every project on the platform, for the one screen that
also links to the reports.

| Method | Route                     | Allowed roles |
|--------|---------------------------|---------------|
| GET    | `/api/projects/oversight` | Admin         |

Each row carries the project's status, its assigned Architect and Project
Manager (id and name), its `updatedAt` — the "last updated" date — and an
`isStalled` flag. The envelope adds `totalProjects`, `stalledCount`,
`generatedAt` and `stalledAfterDays`, so the screen quotes the service's
threshold rather than keeping its own copy.

- **Cancelled projects are included**, unlike `GET /api/projects`: this is the
  whole-platform view, and the status says which are closed out.
- **Names are looked up, never stored**, through the same `IUserNameResolver` the
  project detail uses. A name that cannot be found is `null` with the id still
  present, so a User Service outage never stops the list.
- **Stalled** (`ProjectStallPolicy`): a project that is not `Completed` or
  `Cancelled` and whose `updated_at` is 14 or more days old. A `Pending` project
  nobody has picked up counts — that is the neglect the screen is for. The rule
  lives in one class and every row is judged at one instant, so the flags and
  the count cannot disagree. The 14 days is a constant, not configuration; the
  BA has not been asked yet whether it should be.
- It reuses the existing `ListAllAsync` query (no new SQL) and lives in its own
  `OversightController`, like `ReportsController`, because it reads across every
  project. No gateway change: `/api/projects/{**catch-all}` already covers it.

The React screen is `/admin/oversight`.

## Role dashboards (US-21)

The Project Service's slice of each role's dashboard — the part only it knows:
which projects a Client submitted, which an Architect is assigned to, and how
many projects the system holds in each status. The other slices of the same
dashboards come from the other services; the page joins them on the project id
(see `frontend/README.md`, *Role dashboards*).

| Method | Route                               | Allowed roles |
|--------|-------------------------------------|---------------|
| GET    | `/api/projects/dashboard/client`    | Client        |
| GET    | `/api/projects/dashboard/architect` | Architect     |
| GET    | `/api/projects/dashboard/admin`     | Admin         |

One endpoint per role rather than one that switches on the caller's role, so
each response shape is declared, gated and documented on its own. Each is gated
to exactly its own role, and `EndpointRoleDeclarationTests` pins that.

- **Client** — `{ activeCount, projects[] }`: the projects the caller submitted.
- **Architect** — `{ assignedCount, projects[] }`: the projects they are assigned to.
- **Admin** — `{ totalCount, groups[] }`: every project, and one `{ status, count }`
  entry per status in lifecycle order, **including statuses nothing is in**, so
  the shape does not change with the data.

**Active** means neither `Completed` nor `Cancelled`. The two per-person lists
are active projects only, most recently moved first (`updated_at`). The Admin
total counts every project, whatever its status.

None of these takes an id. Who is asking decides what comes back, and it is
always the token's own `sub` — a Client cannot ask for another Client's
dashboard because there is nowhere to say whose. A Client with nothing under
way gets an empty list and a count of `0`, which is the truthful answer rather
than a refusal.

All three are ADO.NET reads in `ProjectDashboardRepository`, kept apart from
`IProjectRepository` the way the report query is. The Admin counts are a single
`GROUP BY status`, so the cost does not grow with the pipeline.

## In-app notifications (US-26)

A Client or Architect is told, on their next visit, when a design is approved, a
milestone is completed or a payment is received on one of their projects.
Nothing is pushed: a consumer stores a row per person when the event happens, and
the dashboard reads those rows the next time it loads.

| Method | Route                                     | Allowed roles       |
|--------|-------------------------------------------|---------------------|
| GET    | `/api/projects/notifications`             | Client, Architect   |
| POST   | `/api/projects/notifications/{id}/read`   | Client, Architect   |
| POST   | `/api/projects/notifications/read-all`    | Client, Architect   |

- **List** — `{ unreadCount, hasMore, notifications[] }`, newest first, paged by
  `?skip=` (default 0) and `?take=` (default 20, 1 to 50). `hasMore` says whether
  there are older ones beyond this page: ask again with `skip` advanced by the
  length of `notifications`. `unreadCount` is every unread notification the
  caller has, not the length of the page, so "12 new" stays true when only 20 are
  shown. A `skip` or `take` out of range is a `400` naming it — never quietly
  clamped, since a caller that asked for 500 and was handed 50 would believe it
  had reached the end. Each notification is
  `{ id, projectId, eventType, message, occurredAt, isRead }`; `occurredAt` is
  UTC with a `Z`. Pages follow on with no overlap or gap because the order is
  `occurred_at` newest first with the id as the tie-break, so it is the same on
  every read.
- **Mark read** — `204`, and repeating it is harmless (it keeps the time it was
  first read). A notification that is not the caller's answers `404`, exactly as
  one that does not exist does, so a guessed id confirms nothing.
- **Mark all read** — `204`, even when nothing was unread.

None takes a user id: whose notifications these are is always the token's own
`sub`. Admin and Project Manager get `403` — nothing is ever stored for them.
No gateway change is needed; `/api/projects/{**catch-all}` already covers it.

### What is stored, and for whom

`NotificationEventsConsumer` reads three topics owned by other services and turns
three event types into notifications. Anything else on those topics is ignored.

| Topic                 | `eventType`          | Message                                                  |
|-----------------------|----------------------|----------------------------------------------------------|
| `design-events`       | `DesignApproved`     | `Design "<name>" (version <n>) was approved on "<project>".` |
| `construction-events` | `MilestoneCompleted` | `Milestone "<name>" was completed on "<project>".`       |
| `payment-events`      | `PaymentReceived`    | `A payment was received on "<project>".`                 |

An event names a project, never the people, and the people are in this service's
own `projects` table — which is why the notifications live here and not in a
service of their own. Each event notifies the project's **Client** and its
**assigned Architect** (just the Client while no Architect is assigned). The
Project Manager and Admin are not told. The person who caused the event is told
too: a Client who pays gets a receipt.

**The payment amount is deliberately not in the message.** The Architect is a
recipient and the Payment Service refuses them every one of its endpoints, so a
figure here would show them what they may not otherwise see.

`NotificationMapper` holds those rules and is pure, so they are unit-tested with
no database or broker; the consumer is the plumbing around it. Resilience follows
the other consumers here: an unreadable message is logged and committed past, an
unknown project is logged and skipped, and a database failure leaves the offset
uncommitted so the message is retried.

**Redelivery is safe.** `notifications` has a unique key on `(event_id, user_id)`
— the id is the envelope's `eventId` — so Kafka delivering an event twice stores
it once. The insert is `ON DUPLICATE KEY UPDATE`, not `INSERT IGNORE`: a missing
project still fails loudly instead of being swallowed as a warning.

**It reads under its own consumer group**, `<Kafka:ConsumerGroupId>-notifications`
(`project-service-notifications` by default). Two readers sharing a group would
split a topic's partitions between them, and each would see only some of the
messages — and this service already reads `construction-events` and
`payment-events` under groups of their own. A brand-new group reads each topic
from the start, so the first run also notifies for older events; they are stamped
with when the event happened, not when it was read.

**Who is told is decided when the event is read, not when it happened.** An event
names a project and never the people, so the Client and Architect are whoever the
project has *now*. In steady state that is the same thing — events are read within
moments — but a replay, or a reader that was down for a while, tells the Architect
the project has today. An Architect assigned after a design was approved would be
told of that approval on a replay, and one reassigned away would not. Nothing on
any topic says who the Architect was at the time, so it cannot be done better from
here.

**Every Kafka client here connects to the broker the same way.** The producer and
all three consumers build their librdkafka config through `KafkaBrokerSettings`,
so the optional Azure Event Hubs login (`Kafka:SecurityProtocol`, `SaslMechanism`,
`SaslUsername`, `SaslPassword`) and connection tuning reach every one of them. The
consumers once did not receive them at all: against Event Hubs the service could
publish and could not read. Unset, as locally, none of those keys is passed and a
client is configured exactly as before. `request.timeout.ms` stays with the
publisher — it is a producer-only setting.

Schema: `Migrations/008_create_notifications_table.sql`. `user_id` is not a
foreign key (the account lives in the User Service's database); `project_id` is.

## Events published (Kafka)

This service publishes to one topic, `project-events` — one topic per publishing
service, not one per event type. A consumer subscribes to everything the Project
Service has to say and filters on the envelope's `eventType`; a topic per event
type would multiply partitions for no gain and lose the ordering between two
events about the same project.

Messages are keyed by project id, so every event about one project lands on the
same partition and reaches consumers in the order it happened.

| Event            | Published when                       |
|------------------|--------------------------------------|
| `ProjectCreated` | A Client submits a project (US-05)   |

Every event uses the envelope shared across all four publishing services:

```json
{
  "eventType": "ProjectCreated",
  "eventId": "3f1c8e5a-9d42-4f7b-8c11-2a6e0b7d4f93",
  "occurredAt": "2026-08-30T09:15:00+00:00",
  "payload": {
    "projectId": "b2d4...",
    "clientId": "7a91...",
    "name": "Beachfront villa",
    "location": "Galle",
    "landSizePerches": 25.5,
    "budget": 18500000.00,
    "floors": 2,
    "bedrooms": 4,
    "bathrooms": 3,
    "garageSpaces": 2,
    "otherRequirements": "Solar hot water",
    "status": "Pending",
    "createdAt": "2026-08-30T09:15:00"
  }
}
```

`eventId` identifies the publication, not the project — that is what lets a
consumer recognise a message the broker has redelivered.

The payload carries the whole project rather than just its id. A consumer in
another service cannot query `buildnexus_project_db` to fill in the rest — one
schema per service — so an id-only event would force a REST call back here for
every message, which is the coupling the bus exists to avoid.

The producer publishes with `acks=all` and idempotence on, so an event is not
acknowledged until every in-sync replica has it, and an internal retry cannot
put it on the topic twice.

### When the broker is down

A failed publish **does not fail the request**. The project row is already
committed by the time the event goes out, so a 500 would tell the Client their
submission was lost when it was not, and a retry would create a second project.
The failure is logged at error level with the project id, which is enough to
republish by hand.

That leaves a real gap, stated here rather than hidden: a project created while
Kafka is unreachable is never announced. Closing it properly means writing the
event into this service's own database in the same transaction as the row and
draining it with a background worker — the transactional outbox pattern — which
is its own story, not something to smuggle into US-05.

`Kafka:MessageTimeoutMs` is 5 seconds, far below librdkafka's five-minute
default, because the publish is awaited inside the HTTP request that caused it.
Left at the default, a Client submitting a project while the broker was down
would watch a spinner for five minutes.

### Running Kafka locally

`docker compose up -d` brings up a single-node broker in KRaft mode — no
ZooKeeper. Containers reach it at `kafka:9092`; a native `dotnet run` reaches
the published host listener at `localhost:29092`, which is what
`appsettings.json` defaults to. Topics are auto-created on first publish, so
there is nothing to set up by hand.

To watch events arrive while testing the form:

```bash
docker exec -it buildnexus-kafka /opt/kafka/bin/kafka-console-consumer.sh \
  --bootstrap-server localhost:9092 --topic project-events --from-beginning
```

## Tests

```bash
cd ../project-service-tests && dotnet test
```

**Most tests in this project run without MySQL and without Kafka.** The
repository and the event publisher are stood in for, so nothing has to be
started first. The exceptions are tagged `[Trait("Category", "Integration")]`
and need the real thing: `ProjectRepositoryDatabaseTests`,
`ProjectPaymentStatusDatabaseTests`, `ProjectReportRepositoryDatabaseTests`,
`ProjectDashboardRepositoryDatabaseTests` and
`NotificationRepositoryDatabaseTests` run SQL against `project-db`, and
`ProjectKafkaIntegrationTests` does a real round trip through the broker. Start
them with `cd infra && docker compose up -d --wait project-db kafka`.

```bash
dotnet test --filter "Category!=Integration"   # what needs nothing running
dotnet test --filter "Category=Integration"    # what needs project-db and kafka
```

CI runs the two groups as separate stages: the unit stage with no containers,
then the integration stage once its databases and broker are healthy.

`CreateProjectRequestTests` covers the validation rules: the required fields,
the column bounds mirrored from `001_create_projects_table.sql`, and the
distinction the nullable numeric fields exist for — that a missing bedroom count
is refused while an answer of `0` is accepted.

`ProjectsControllerTests` walks the US-05 acceptance criteria over stand-in
collaborators: every requirement from the form is stored, the status is
`Pending`, the client id comes from the token rather than the payload, and
`ProjectCreated` is published once the project is stored. It also pins the two
failure cases that matter — a project that could not be stored is never
announced, and a project that could not be announced still answers `201`.

`ProjectCreatedEventTests` pins the wire contract: the four envelope properties
in order, the event type, an ISO-8601 `occurredAt`, an `eventId` that differs
between two events about the same project, and the full payload. Nothing checks
this at compile time — a consumer reads JSON off a topic, so a renamed property
would fail at runtime in somebody else's service rather than here.

`EndpointRoleDeclarationTests` walks the controllers by reflection and fails if
any endpoint neither declares its roles nor is explicitly `[AllowAnonymous]`, or
names a role the platform does not have. An endpoint added in a later story is
held to that rule without anyone having to remember this file.

The US-18 report is covered in layers. `ProjectStatusReportBuilderTests` and
`ProjectReportFilterTests` check the grouping, the ordering and the filter rules
(what is accepted, what is refused, where the date range's edges fall) with no
database. `ProjectStatusReportCsvTests` pins the CSV — columns, quoting, the
formula guard, culture-independent numbers. `ProjectStatusReportEndpointTests`
walks the acceptance criteria over a stand-in query: grouped by status, the
filters reach the query, a bad filter is a `400` before anything is read, and the
export carries the same report. `EndpointRoleDeclarationTests` pins both
endpoints to Admin only. The SQL itself — the status list and the inclusive date
boundaries — is only provable against the real engine, so
`ProjectReportRepositoryDatabaseTests` runs against `project-db` (the same
integration category as `ProjectRepositoryDatabaseTests`): start it first with
`cd infra && docker compose up -d --wait project-db`.

The US-38 oversight list is covered by `ProjectStallPolicyTests` (every open
status, the exact 14-day edge, finished projects never stalled) and
`ProjectOversightEndpointTests` (every project listed, cancelled kept, staff by
id and name, a name that cannot be found, one lookup per person, an empty
platform, the stalled flags and count) over a stand-in repository, with no
database. `EndpointRoleDeclarationTests` pins it to Admin only.

The US-21 dashboards are covered the same way. `ProjectDashboardEndpointTests`
walks each role's dashboard over a stand-in query: what comes back, that an empty
result is an empty dashboard and not an error, that the query is for the caller
named in the token and not anyone else, and that a token with no usable `sub` is
refused before anything is read. `ProjectDashboardRepositoryDatabaseTests` proves
the SQL against `project-db` — ownership scoping, that completed and cancelled
projects are left out, the newest-moved-first order, and the per-status count
(asserted as a difference before and after, since the development database holds
other projects).

The US-26 notifications are covered in layers too. `NotificationMapperTests`
pins which events count, who is told and what they are told — including that no
amount reaches the message and that an unreadable event is refused — with no
database or broker. `NotificationEventsConsumerTests` drives
`HandleAsync` over stand-ins: each of the three events stores for the Client and
the Architect, a redelivery stores nothing new, other events and unknown
projects are left alone, and the commit-or-retry exception contract holds.
`NotificationsEndpointTests` walks the acceptance criterion that a stored
notification is visible on the person's next request, and that it is only theirs;
it also pins the paging — pages that follow on, `hasMore`, the edges, and the
`400` for a `skip` or `take` out of range. `KafkaConsumerConfigTests` pins what
every consumer is built with, for the local broker and for Event Hubs, without a
broker.
`NotificationRepositoryDatabaseTests` proves the SQL against `project-db`: the
unique key, the all-or-nothing batch, the foreign key and CHECK refusals, and
that a second "mark read" keeps the first time. Each person in it is a fresh
random id, so it shares the development database without reading anyone's rows.

`MigrationScriptTests` checks the scripts are embedded (DbUp silently skips one
that is not), sort into the order they must run in, do not switch database, and
hold no foreign key into another service's schema.

## Configuration
- `ConnectionStrings:ProjectDb` — MySQL connection string
- `Kafka:BootstrapServers` — the broker list, supplied as
  `Kafka__BootstrapServers`. The shared name every publishing service uses. The
  service refuses to start without it: one that cannot say where Kafka is would
  publish nothing, and finding that out from a log line after the first project
  was submitted is too late.
- `Kafka:MessageTimeoutMs` — how long a publish may spend reaching the broker.
  Defaults to 5000; see [When the broker is down](#when-the-broker-is-down).
- `Kafka:SecurityProtocol`, `Kafka:SaslMechanism`, `Kafka:SaslUsername`,
  `Kafka:SaslPassword` — how the producer and every consumer authenticate to the broker. **All
  four are unset locally**, and then each client speaks plaintext, which is what
  the compose broker's `kafka:9092` listener expects — exactly as before these
  settings existed. Azure Event Hubs accepts only SASL over TLS, so the deployed
  service is given all four: `SaslSsl`, `Plain`, the literal username
  `$ConnectionString`, and the Event Hubs namespace's connection string as the
  password, with `Kafka:BootstrapServers` set to
  `<namespace>.servicebus.windows.net:9093`. The password is a secret and is
  only ever supplied as `Kafka__SaslPassword`. They are all-or-nothing: the
  service refuses to start with a SASL protocol missing any of the other three,
  or with any of those three and no SASL protocol, because either half would
  otherwise show up only as every publish failing to connect.
- `Kafka:RequestTimeoutMs`, `Kafka:SocketKeepaliveEnable`,
  `Kafka:MetadataMaxAgeMs` — connection tuning for Azure Event Hubs. **All three
  are unset locally**, and librdkafka keeps its own defaults, exactly as before
  these settings existed. The deployed service is given the values Event Hubs
  documents for librdkafka clients: `60000` (librdkafka's five-second default is
  too low — Event Hubs enforces a twenty-second minimum), `true` (Azure closes a
  connection left idle for 240 seconds), and `180000` (below that same limit;
  librdkafka's default is fifteen minutes). Each is independent of the others,
  and the service refuses to start only on a timeout or age of zero or less.
- `Jwt:Issuer`, `Jwt:Audience`
- `Jwt:SigningKey` — **deliberately empty in `appsettings.json`.** It is supplied
  per environment: `Jwt__SigningKey` from `infra/.env` in Docker, or User Secrets
  for a native run. The service refuses to start if it is missing or shorter than
  32 bytes, so there is no weak default to fall back on.

`Jwt:Issuer` (`BuildNexusAuth`) and `Jwt:Audience` (`BuildNexusServices`) are a
shared convention: this service, the API Gateway and every other
token-validating service must use these exact values and the same signing key,
or every token the User Service issues is refused here. There is no
`Jwt:AccessTokenLifetimeMinutes` — that is User Service only, baked into each
token's `exp` claim at issue time, and a validator just checks whether `exp` has
passed.

Note the environment-variable names use a double underscore (`Jwt__SigningKey`),
which is how ASP.NET Core maps onto the `Jwt:SigningKey` configuration path. A
single underscore does not bind.

Local overrides go in `appsettings.Development.json`, which is git-ignored.
