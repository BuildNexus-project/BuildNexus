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

This service is **REST-only**. It does not publish or consume Kafka events — no
story has named one for it yet.

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
| GET    | `/health`                                         | Anonymous                           |

Every `/api/designs` call goes through the API Gateway on
`http://localhost:5000`, which validates the token and proxies it here
unchanged. This service re-validates it and enforces its own role checks — the
gateway makes no authorization decisions.

**Whether a caller may touch a given project** is not something the role alone
decides, and this service does not own that fact. It asks the Project Service
(`GET /api/projects/{projectId}`) with the caller's own token: a `200` means the
caller is party to the project, a `403` or `404` is relayed as-is.

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

## Tests

```bash
cd ../design-service-tests && dotnet test
```
