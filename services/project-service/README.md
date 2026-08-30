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

| Method | Route       | Allowed roles |
|--------|-------------|---------------|
| GET    | `/health`   | Anonymous     |

The frontend never calls this service directly: every `/api/projects` call goes
through the API Gateway on `http://localhost:5000`, which validates the token
and proxies it here unchanged. This service re-validates it and enforces its own
role checks — the gateway makes no authorization decisions.

## Configuration
- `ConnectionStrings:ProjectDb` — MySQL connection string
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
