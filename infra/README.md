# Infrastructure

Local development stack for BuildNexus.

## Start

```bash
cp .env.example .env    # then fill in the values
docker compose up -d
```

| Service        | Container                | Host address            |
|----------------|--------------------------|-------------------------|
| API Gateway    | buildnexus-api-gateway   | http://localhost:5000   |
| User Service   | buildnexus-user-service  | http://localhost:5001   |
| User database  | buildnexus-user-db       | localhost:3306 (MySQL)  |

The gateway is the entry point: the frontend calls <http://localhost:5000> and
nothing else, and the Vite dev server proxies `/api` there. The service port
above it is published for debugging only — the app does not use it, and a
service does not need a published port for the stack to work.

The database uses the standard port 3306, matching the default connection
string the User Service ships with, so `dotnet run` needs no extra configuration.

If a MySQL server is already installed on your machine it will hold that port and
the container will fail to start with "port is already allocated". Stop it, and
set it to start manually so it does not reclaim the port after a reboot:

```powershell
Stop-Service MySQL80
Set-Service MySQL80 -StartupType Manual
```

## The gateway

`FRONTEND_ORIGIN` is the origin the React app is served from. CORS is configured
once, at the gateway, so this is the only place an origin is declared for the
whole system — none of the five services repeats it.

Which path prefix reaches which service is the gateway's own routing table; see
`api-gateway/README.md`. The addresses in that file are the local `dotnet run`
ports, and `docker-compose.yml` overrides each one to a compose service name.
Only the User Service exists so far, so the other four routes answer 502 until
the stories that build them land.

## Database schema

Schema lives with the service that owns it, in `services/<service>/Migrations/`,
and is applied by that service at startup — nothing here creates tables. Each
service owns its own database; nothing may join across two of them.

The migrations are numbered `.sql` files run in filename order by DbUp, which
records what it has already applied in a `schemaversions` table and runs only
the rest. Starting the stack is therefore safe against an empty database, one a
few stories behind, or one already current, and adding a column no longer means
throwing the volume away. Add the next number rather than editing a script that
has already shipped.

Dropping the volume is now only for deliberately starting from nothing:

```bash
docker compose down -v && docker compose up -d
```

## Configuration

`cp .env.example .env` gives every teammate the same working local setup — the
values in `.env.example` are filled in, not placeholders. `.env` itself stays
git-ignored so per-machine edits never get committed.

`Jwt__Issuer`, `Jwt__Audience` and `Jwt__SigningKey` are the shared convention:
the User Service signs tokens with them and every service that validates a token
— starting with the API Gateway — must be given the same three values. The
gateway checks the signature before it proxies anything, so a mismatch here does
not fail quietly: every request through the gateway comes back 401. They live
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
docker run --rm mcr.microsoft.com/dotnet/sdk:10.0   sh -c 'curl -s -o /dev/null -w "%{http_code}
" http://example.com;          curl -s -o /dev/null -w "%{http_code}
" https://example.com'
```

`200` then `000` confirms it. Exempt Docker Desktop from your antivirus's HTTPS
scanning; only if that fails does the build image need the interceptor's root CA.
Meanwhile `docker compose up -d user-db` starts the database on its own.
