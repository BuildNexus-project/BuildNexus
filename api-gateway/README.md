# API Gateway

The single entry point for the BuildNexus frontend. Every `/api` call the React
app makes arrives here; the five services behind it are not called directly.

Built on [YARP](https://microsoft.github.io/reverse-proxy/) inside an ordinary
ASP.NET Core project, so it builds, containerises and deploys exactly like the
five services do. There are no controllers — the whole gateway is the routing
table in `appsettings.json` plus the token validation in `Program.cs`.

## What it does

1. **Routes** a request to the service that owns its path prefix.
2. **Validates the JWT** before proxying. A request with a missing, expired,
   forged or wrongly-signed token is answered here and never reaches a service.
3. **Forwards the token onward** untouched, so each service still validates it
   itself rather than trusting that something upstream did.
4. **Answers CORS** for the frontend origin — configured once here, never
   repeated in the five services.

## Routing table

| Path prefix          | Cluster        | Auth          |
|----------------------|----------------|---------------|
| `/api/auth/*`        | `user`         | anonymous     |
| `/api/users/*`       | `user`         | authenticated |
| `/api/projects/*`    | `project`      | authenticated |
| `/api/designs/*`     | `design`       | authenticated |
| `/api/construction/*`| `construction` | authenticated |
| `/api/payments/*`    | `payment`      | authenticated |
| `/health`            | —              | anonymous     |

`/api/auth` is the only anonymous route: a caller cannot present a token before
it has been issued one. The User Service still decides for itself what it will
serve without authentication.

A path matching no route is answered by the gateway — 404 for an authenticated
caller, 401 for an anonymous one, since the deny-by-default fallback policy also
covers requests that match no route. Nothing unrouted reaches a service.

Only the User Service exists so far. The other four routes are configured and
correct, and will answer 502 until the stories that build those services land.

## Configuration

Nothing secret lives in `appsettings.json`. The addresses in it are the local
`dotnet run` ports; everything else arrives as environment variables.

| Variable | Purpose |
|----------|---------|
| `Jwt__Issuer`, `Jwt__Audience`, `Jwt__SigningKey` | The shared convention. Must be identical to the values the User Service signs with, or every token is refused here. |
| `Cors__AllowedOrigins__0` | The origin the React app is served from. |
| `ReverseProxy__Clusters__<id>__Destinations__primary__Address` | Where a cluster points. Cluster ids (`user`, `project`, `design`, `construction`, `payment`) are free of hyphens so this reads as a plain variable name. |

The gateway refuses to start if the signing key is missing or shorter than 32
bytes, rather than turning every proxied request into a 401 at runtime.

## Tests

```bash
dotnet test api-gateway-tests/ApiGateway.Tests.csproj
```

They need nothing running — no database, no stack, none of the five services.
`api-gateway-tests` boots the real gateway host with a stub standing in for each
cluster, so the routing table is asserted for what it claims (this prefix
reaches this service, and the path arrives unchanged) and every refusal is
asserted twice: the caller gets a 401, and the stub behind the route received
nothing. A status code alone would not tell "refused at the gateway" apart from
"proxied, and the service refused it", which is the distinction US-25 is about.

## Running it

In the stack, with everything wired for you:

```bash
cd infra && docker compose up -d
```

The gateway is then on <http://localhost:5000> and the React dev server proxies
`/api` to it.

On its own, the signing key has to come from somewhere — user secrets are the
local equivalent of the environment variable:

```bash
dotnet user-secrets set "Jwt:SigningKey" "<the same key the User Service uses>"
dotnet run
```
