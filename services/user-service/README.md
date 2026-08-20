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
Owns `buildnexus_user_db`. Apply the schema with:

```bash
mysql -u root -p < db/01_schema.sql
```

## Run locally
```bash
dotnet run
```
Listens on `http://localhost:5001`; Swagger UI at `/swagger` in Development.

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
- `Jwt:SigningKey` — **the value in `appsettings.json` is a development
  placeholder.** Every real environment must override it with a secret of at
  least 32 bytes, supplied as the environment variable `Jwt__SigningKey`. The
  service refuses to start if the key is missing or too short.

Local overrides go in `appsettings.Development.json`, which is git-ignored.
