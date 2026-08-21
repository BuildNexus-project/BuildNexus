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

## Configuration

`cp .env.example .env` gives every teammate the same working local setup — the
values in `.env.example` are filled in, not placeholders. `.env` itself stays
git-ignored so per-machine edits never get committed.

`Jwt__Issuer`, `Jwt__Audience` and `Jwt__SigningKey` are the shared convention:
the User Service signs tokens with them and every service that validates a token
— starting with the API Gateway — must be given the same three values. They live
here rather than in any one service's `appsettings.json` so the two sides cannot
drift apart. The double underscore is what ASP.NET Core maps onto `Jwt:SigningKey`;
a single underscore will not bind.

The signing key in `.env.example` is a **local-development value only**. The
production key is delivered through GitHub Secrets (US-35) and never appears in
any file in this repository.

## Known issue: build fails behind TLS interception

If `docker compose up -d` fails with `NU1301: Unable to load the service index
for https://api.nuget.org/v3/index.json`, your machine is intercepting TLS from
the Docker VM — antivirus HTTPS scanning is the usual cause. Confirm with:

```bash
docker run --rm mcr.microsoft.com/dotnet/sdk:8.0   sh -c 'curl -s -o /dev/null -w "%{http_code}
" http://example.com;          curl -s -o /dev/null -w "%{http_code}
" https://example.com'
```

`200` then `000` confirms it. Exempt Docker Desktop from your antivirus's HTTPS
scanning; only if that fails does the build image need the interceptor's root CA.
Meanwhile `docker compose up -d user-db` starts the database on its own.
