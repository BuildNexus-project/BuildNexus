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

| Method | Route            | Allowed roles |
|--------|------------------|---------------|
| POST   | `/api/projects` | Client        |
| GET    | `/health`       | Anonymous     |

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

**Every test in this project runs without MySQL and without Kafka.** The
repository and the event publisher are stood in for, so nothing has to be
started first — which is also why the CI job runs this project without bringing
up a `project-db` container the tests would never connect to.

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
