# Design Service

Owner: [name]

Design & document management for BuildNexus. An Architect uploads design
documents for a project here, and every revision is kept as its own version so
the client can review the latest work while the full history is preserved
(US-09).

## Stack
- ASP.NET Core 10 Web API
- ADO.NET over MySQL (MySqlConnector) — direct SQL only, no ORM
- JWT bearer authentication (validation only — the User Service issues tokens)

Publishes business events to Kafka on the `design-events` topic, drained from a
transactional outbox (US-23). It consumes nothing.

## Database
Owns `buildnexus_design_db`. No other service may query it or hold a foreign key
into it, and this service holds none into theirs — `design_documents.project_id`
records the project from the route as a plain column, not a key into the Project
Service's `projects` table.

The schema lives in `Migrations/` as numbered `.sql` files, embedded in the
assembly and applied by [DbUp](https://dbup.readthedocs.io) when the service
starts — set up the same way as the User Service and the Project Service. DbUp
records each script it has run in a `schemaversions` table and applies only the
ones missing.

The service does **not** create `buildnexus_design_db` itself — it must already
exist, the way `MYSQL_DATABASE` creates it when the Docker container starts.

To add a schema change, drop the next numbered script into `Migrations/` and
restart. Never edit a script that has already run — add the next number instead.

## Run locally
Through the local stack (recommended — brings up MySQL too):

```bash
cd ../../infra && docker compose up -d
```

Or natively (start the database with `docker compose up -d design-db` first).
One-time setup per machine, so the signing key stays out of the repository:

```bash
dotnet user-secrets set "Jwt:SigningKey" "<the Jwt__SigningKey value from infra/.env.example>"
dotnet run
```

The service listens on `http://localhost:5003`, with Swagger UI at `/swagger` in
Development. Its database is published on **3309** — `user-db` has 3306 (3308 via
the local override), `project-db` has 3307, and one service per schema means one
container per schema.

## Endpoints

| Method | Route                                             | Allowed roles                       |
|--------|---------------------------------------------------|-------------------------------------|
| POST   | `/api/designs/projects/{projectId}/documents`     | Architect                           |
| GET    | `/api/designs/projects/{projectId}/documents`     | Client, Architect, ProjectManager, Admin |
| GET    | `/api/designs/versions/{versionId}/file`          | Client, Architect, ProjectManager, Admin |
| GET    | `/api/designs/dashboard/client`                   | Client                              |
| GET    | `/api/designs/dashboard/architect`                | Architect                           |
| GET    | `/health`                                         | Anonymous                           |

Every `/api/designs` call goes through the API Gateway on
`http://localhost:5000`, which validates the token and proxies it here
unchanged. This service re-validates it and enforces its own role checks — the
gateway makes no authorization decisions.

**Whether a caller may touch a given project** is not something the role alone
decides, and this service does not own that fact. It asks the Project Service
(`GET /api/projects/{projectId}`) with the caller's own token: a `200` means the
caller is party to the project, a `403` or `404` is relayed as-is.

## Role dashboards (US-21)

The design slice of the Client's and the Architect's dashboards. The other
slices come from the other services; the page joins them on the project id (see
`frontend/README.md`, *Role dashboards*).

- **Client** — `{ projects[] }`: one entry per *active* project the Client has,
  each `{ projectId, state, documentCount, awaitingReviewCount,
  revisionRequestedCount, approvedCount }`. `state` is `NoDesign`,
  `AwaitingReview`, `RevisionRequested` or `Approved`, and a project nothing has
  been uploaded for is reported as `NoDesign` rather than left out.
- **Architect** — `{ pendingRevisionCount, revisions[] }`: the revisions a Client
  has asked for that the Architect has not yet answered, longest waiting first,
  each with the document, version, the Client's comment and when it was asked.

Both are read from each document's **latest** version only. A revision the
Architect has since answered with a newer upload is no longer pending, whatever
became of the newer version. A project is `AwaitingReview` (something is waiting
on the Client) before `RevisionRequested` (the Client is waiting on the
Architect) before `Approved` (every document is signed off).

**This service does not know whose a project is**, and must not grow a copy of
it. So each action first asks the Project Service which projects the caller may
see (`GET /api/projects`, forwarding the caller's own token — the same
arrangement as the per-project access check above) and looks only at those,
excluding `Completed` and `Cancelled`. Neither endpoint takes a project id, so a
caller cannot widen their own dashboard by asking differently.

If the Project Service cannot be reached, both answer **`502`** with a reason,
never an empty dashboard: an outage must not read as "you have no projects".

## Configuration
- `ConnectionStrings:DesignDb` — MySQL connection string
- `Jwt:Issuer`, `Jwt:Audience` — the shared convention (`BuildNexusAuth` /
  `BuildNexusServices`)
- `Jwt:SigningKey` — **deliberately empty in `appsettings.json`.** Supplied per
  environment: `Jwt__SigningKey` from `infra/.env` in Docker, or User Secrets
  for a native run. The service refuses to start if it is missing or shorter
  than 32 bytes.
- `Services:ProjectService:BaseUrl` — where to reach the Project Service for the
  per-project access check. `http://project-service:8080/` in the stack,
  `http://localhost:5002/` for a native run.
- `Kafka:BootstrapServers` — the broker, supplied as `Kafka__BootstrapServers`:
  `kafka:9092` in the stack, the Event Hubs namespace's
  `<namespace>.servicebus.windows.net:9093` when deployed.
- `Kafka:SecurityProtocol`, `Kafka:SaslMechanism`, `Kafka:SaslUsername`,
  `Kafka:SaslPassword` — how the producer authenticates to the broker. **All
  four are unset locally**, and the producer speaks plaintext to the compose
  broker. Azure Event Hubs accepts only SASL over TLS, so the deployed service is
  given all four: `SaslSsl`, `Plain`, the literal username `$ConnectionString`,
  and the namespace's connection string as the password — a secret, only ever
  supplied as `Kafka__SaslPassword`. All-or-nothing: the service refuses to start
  on a partial set. Same settings as the Project Service.
- `Kafka:RequestTimeoutMs`, `Kafka:SocketKeepaliveEnable`,
  `Kafka:MetadataMaxAgeMs` — Event Hubs connection tuning. **Unset locally**, so
  librdkafka keeps its defaults; deployed as `60000`, `true` and `180000`, the
  values Event Hubs documents for librdkafka clients.

## Tests

```bash
cd ../design-service-tests && dotnet test
```
