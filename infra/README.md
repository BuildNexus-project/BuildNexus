# Infrastructure

Local development stack for BuildNexus.

## Start

```bash
cp .env.example .env    # then fill in the values
docker compose up -d
```

| Service        | Container                | Host address            |
|----------------|--------------------------|-------------------------|
| User Service   | buildnexus-user-service  | http://localhost:5001   |
| User database  | buildnexus-user-db       | localhost:3307 (MySQL)  |

Port 3307 is deliberate — 3306 is usually taken by a locally installed MySQL.

## Database schema

`db/<service>/` holds the schema for one service, applied in filename order the
first time that service's data volume is created. Each service owns its own
database; nothing here may join across two of them.

To re-apply a schema from scratch, drop the volume and start again:

```bash
docker compose down -v && docker compose up -d
```

## Secrets

Real values live in `infra/.env`, which is git-ignored. `.env.example` lists
every variable the stack needs.

`JWT_ISSUER`, `JWT_AUDIENCE` and `JWT_SIGNING_KEY` are shared: the User Service
signs tokens with them and every service that validates a token — starting with
the API Gateway — must be given the same three values. They are set here rather
than in any one service's `appsettings.json` so the two sides cannot drift apart.
