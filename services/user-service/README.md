# User Service

Owner: [name]

Identity and access management for BuildNexus: registration, login, and JWT issuing
for the four platform roles (`Client`, `Architect`, `ProjectManager`, `Admin`).

REST-only — this service does not publish or consume Kafka events.

## Stack
- ASP.NET Core 10 Web API
- ADO.NET over MySQL (MySqlConnector) — direct SQL only, no ORM
- JWT bearer authentication

## Database
Owns `buildnexus_user_db`. No other service may query it or hold a foreign key
into it.

The schema lives in `Migrations/` as numbered `.sql` files, embedded in the
assembly and applied by [DbUp](https://dbup.readthedocs.io) when the service
starts. DbUp records each script it has run in a `schemaversions` table and
applies only the ones missing, so starting against a database that is empty,
one a few stories behind, or already current all do the right thing.

The service does **not** create `buildnexus_user_db` itself — it must already
exist, the way `MYSQL_DATABASE` creates it when the Docker container starts.
The `buildnexus` account is granted access to only that one database, and
checking for a database's existence needs a connection to MySQL's own `mysql`
schema, which that account cannot reach. That scoping is deliberate, so the
service works within it rather than asking for broader access.

DbUp runs the same hand-written SQL we would otherwise apply by hand. It is not
an ORM and takes no part in queries: the data access is still ADO.NET with
direct SQL.

To add a schema change, drop the next numbered script into `Migrations/` and
restart the service:

```
Migrations/003_whatever_changed.sql
```

Never edit a script that has already run somewhere — DbUp has recorded it as
done and will not run it again. Add the next number instead.

## Run locally
Through the local stack (recommended — brings up MySQL too):

```bash
cd ../../infra && docker compose up -d
```

Or natively, which is faster to iterate on (start the database with
`docker compose up -d user-db` first). One-time setup per machine, so the
signing key stays out of the repository:

```bash
dotnet user-secrets init
dotnet user-secrets set "Jwt:SigningKey" "<the Jwt__SigningKey value from infra/.env.example>"
dotnet run
```

User Secrets are loaded automatically in Development only. Without them the
service refuses to start — that is the intended behaviour, not a bug.

Either way the service listens on `http://localhost:5001`, with Swagger UI at
`/swagger` in Development.

## Endpoints

| Method | Route                 | Allowed roles                            |
|--------|-----------------------|------------------------------------------|
| POST   | `/api/auth/register`  | Anonymous (Client, Architect, PM only)   |
| POST   | `/api/auth/login`     | Anonymous                                |
| GET    | `/api/users/me`       | Client, Architect, ProjectManager, Admin |
| PUT    | `/api/users/me`       | Client, Architect, ProjectManager, Admin |
| GET    | `/api/users/{id}`     | Admin                                    |
| GET    | `/health`             | Anonymous                                |

`PUT /api/users/me` edits the caller's own full name, phone number and contact
address, and nothing else. Email and role are **not** self-editable — that is the
team's decision for US-02, taken because the email is the login identity and the
role is the authorisation boundary. A payload carrying either field is refused
with `400` naming it, rather than being silently ignored, and the `UPDATE`
statement behind the endpoint does not list those columns at all. Changing them
is an administrator's job.

Sending `""` for a phone number or address clears it: the value is stored as
`NULL`, which is also what a brand-new account has.

Self-service registration cannot create an `Admin`: the handler rejects that role
with `400` before hashing anything. Role names must be sent in their exact
canonical form — `ProjectManager`, not `projectmanager`.

Because of that restriction, a bootstrap Admin is seeded at startup **in
Development only** when no Admin exists:

| Email                    | Password       |
|--------------------------|----------------|
| `admin@buildnexus.local` | `ChangeMe123!` |

The password is hashed at runtime by the same hasher registration uses, so it
cannot go stale if the hashing changes. Real environments must not use this path
— their first Admin comes from a secret or manual creation after deploy (US-35).

## Tests

```bash
cd ../user-service-tests && dotnet test
```

The unit tests cover the profile validation rules, the profile update action
over a stand-in repository, and the migration scripts being embedded and in
order. They need no database.

`RegistrationRoleTests` is the exception: it boots the real host and needs the
development database running (`cd ../../infra && docker compose up -d user-db`).
It cleans up the accounts it creates.

Protected routes expect `Authorization: Bearer <token>`. Tokens are validated on
issuer, audience, signature and lifetime with no clock skew, so an expired or
malformed token is rejected with `401`.

## Configuration
- `ConnectionStrings:UserDb` — MySQL connection string
- `Jwt:Issuer`, `Jwt:Audience`, `Jwt:AccessTokenLifetimeMinutes`
- `Jwt:SigningKey` — **deliberately empty in `appsettings.json`.** It is supplied
  per environment: `Jwt__SigningKey` from `infra/.env` in Docker, or User Secrets
  for a native run. The service refuses to start if it is missing or shorter than
  32 bytes, so there is no weak default to fall back on.

`Jwt:Issuer` (`BuildNexusAuth`) and `Jwt:Audience` (`BuildNexusServices`) are a
shared convention: the API Gateway and every other token-validating service must
use these exact values and the same signing key. `Jwt:AccessTokenLifetimeMinutes`
is User Service only — it is baked into each token's `exp` claim at issue time,
and validators just check whether `exp` has passed.

Note the environment-variable names use a double underscore (`Jwt__SigningKey`),
which is how ASP.NET Core maps onto the `Jwt:SigningKey` configuration path. A
single underscore does not bind.

Local overrides go in `appsettings.Development.json`, which is git-ignored.
