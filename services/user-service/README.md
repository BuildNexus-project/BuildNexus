# User Service

Owner: [name]

Identity and access management for BuildNexus: registration, login, and JWT issuing
for the four platform roles (`Client`, `Architect`, `ProjectManager`, `Admin`).

REST-only — this service does not publish or consume Kafka events.

## Stack
- ASP.NET Core 8 Web API
- ADO.NET over MySQL (MySqlConnector) — direct SQL only, no ORM
- JWT bearer authentication

## Database
Owns `buildnexus_user_db`. The schema lives with the rest of the deployment
config in `infra/db/user-service/` and is applied automatically when the stack
starts.

## Run locally
Through the local stack (recommended — brings up MySQL too):

```bash
cd ../../infra && docker compose up -d
```

Or directly against a MySQL you manage yourself:

```bash
dotnet run
```

Either way the service listens on `http://localhost:5001`, with Swagger UI at
`/swagger` in Development.

## Endpoints

| Method | Route                 | Allowed roles                            |
|--------|-----------------------|------------------------------------------|
| POST   | `/api/auth/register`  | Anonymous                                |
| POST   | `/api/auth/login`     | Anonymous                                |
| GET    | `/api/users/me`       | Client, Architect, ProjectManager, Admin |
| GET    | `/api/users/{id}`     | Admin                                    |
| GET    | `/health`             | Anonymous                                |

Protected routes expect `Authorization: Bearer <token>`. Tokens are validated on
issuer, audience, signature and lifetime with no clock skew, so an expired or
malformed token is rejected with `401`.

## Configuration
- `ConnectionStrings:UserDb` — MySQL connection string
- `Jwt:Issuer`, `Jwt:Audience`, `Jwt:AccessTokenLifetimeMinutes`
- `Jwt:SigningKey` — **deliberately empty in `appsettings.json`.** The key is
  supplied per environment as `Jwt__SigningKey`, which `infra/docker-compose.yml`
  reads from `infra/.env`. The service refuses to start if it is missing or
  shorter than 32 bytes, so there is no weak default to fall back on. The API
  Gateway must be given the same key, issuer and audience.

Local overrides go in `appsettings.Development.json`, which is git-ignored.
