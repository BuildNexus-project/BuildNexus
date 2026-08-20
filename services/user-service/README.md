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

## Configuration
`ConnectionStrings:UserDb` in `appsettings.json` (override locally via
`appsettings.Development.json`, which is git-ignored).
