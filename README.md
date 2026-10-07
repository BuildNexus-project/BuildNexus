# BuildNexus

A microservices-based construction project management system. A Client describes the building they want, the company assigns an Architect and a Project Manager, designs are uploaded and approved, construction is tracked milestone by milestone, and payments are invoiced and recorded. Five .NET services (User/Identity, Project, Design, Construction, Payment), a React frontend, a YARP API gateway, and Kafka carrying events between services. ADO.NET with direct SQL (no ORM). Built by a 4-person team.

- **Architecture diagrams** (local stack and what is deployed on Azure), request path and event flow: [docs/architecture.md](docs/architecture.md)
- **Azure deployment**: [infra/RUNBOOK.md](infra/RUNBOOK.md)
- **Local stack details**: [infra/README.md](infra/README.md)

## Stack

| Layer | Technology |
|---|---|
| Frontend | React 19 + TypeScript, Vite, Tailwind CSS v4, shadcn/ui, react-hook-form + zod |
| API Gateway | ASP.NET Core 10 + YARP; validates the JWT, answers CORS |
| Services | ASP.NET Core 10 Web API, ADO.NET + MySQL (MySqlConnector), DbUp migrations |
| Database | MySQL 8, one independent schema per service |
| Messaging | Apache Kafka (Confluent.Kafka); Azure Event Hubs when deployed |
| Auth | JWT bearer, four roles: `Client`, `Architect`, `ProjectManager`, `Admin` |
| Infra | Docker Compose locally; Terraform + GitHub Actions to Azure |

## Repository layout

| Folder | What is in it |
|---|---|
| `services/user-service`, `project-service`, `design-service`, `construction-service`, `payment-service` | The five services, each with its own `Migrations/` and `README.md` |
| `services/*-tests`, `api-gateway-tests` | xUnit test projects, one per service and one for the gateway |
| `e2e-tests` | Selenium end-to-end tests that drive the real UI against the running stack (see [e2e-tests/README.md](e2e-tests/README.md)) |
| `api-gateway` | The YARP gateway |
| `frontend` | The React app |
| `infra` | `docker-compose.yml`, Terraform, JMeter performance plan and results |
| `docs` | Architecture, sprint standups, retrospectives, QA reports |

## Run the whole stack locally

You need [Docker Desktop](https://www.docker.com/products/docker-desktop/) and Node 22 (`.nvmrc`; `nvm use` picks it). The .NET 10 SDK is only needed to run tests or run a service outside Docker.

**1. Start the backend** — five databases, Kafka, Mailpit, the five services and the gateway:

```bash
cd infra
cp .env.example .env      # the values are working local-development ones, no editing needed
docker compose up -d
```

The first run builds every image and takes a few minutes. Each service applies its own database migrations on startup. Check everything is up with `docker compose ps`; every service should show `healthy` or `running`.

If a MySQL server is already installed on your machine it holds port 3306 and `user-db` will not start. See the note in [infra/README.md](infra/README.md).

**2. Start the frontend** (it is not part of compose):

```bash
cd frontend
npm install
npm run dev
```

Open <http://localhost:5173>. The dev server proxies every `/api` call to the gateway on port 5000.

**3. Sign in.** In Development a bootstrap Admin is seeded:

| Email | Password |
|---|---|
| `admin@buildnexus.local` | `ChangeMe123!` |

The Admin cannot be created by self-service registration. Register Client, Architect and Project Manager accounts from the app's `/register` page. Password-reset emails land in Mailpit at <http://localhost:8025>.

To stop: `docker compose down` (keep data) or `docker compose down -v` (wipe all databases).

### Ports

| What | Address |
|---|---|
| Frontend (Vite) | <http://localhost:5173> |
| API Gateway | <http://localhost:5000> |
| User / Project / Design / Construction / Payment Service | <http://localhost:5001> / `5002` / `5003` / `5004` / `5005` |
| MySQL: user / project / design / construction / payment | `3306` / `3307` / `3309` / `3310` / `3311` |
| Kafka (from the host) | `localhost:29092` |
| Mailpit inbox | <http://localhost:8025> |

The frontend only ever calls the gateway. The service ports are published for debugging and for Swagger UI below.

## API documentation (Swagger UI)

Each service serves interactive OpenAPI docs at `/swagger`. With the stack running:

| Service | Swagger UI | OpenAPI JSON |
|---|---|---|
| User Service | <http://localhost:5001/swagger> | <http://localhost:5001/swagger/v1/swagger.json> |
| Project Service | <http://localhost:5002/swagger> | <http://localhost:5002/swagger/v1/swagger.json> |
| Design Service | <http://localhost:5003/swagger> | <http://localhost:5003/swagger/v1/swagger.json> |
| Construction Service | <http://localhost:5004/swagger> | <http://localhost:5004/swagger/v1/swagger.json> |
| Payment Service | <http://localhost:5005/swagger> | <http://localhost:5005/swagger/v1/swagger.json> |

The API Gateway has no Swagger page: it has no endpoints of its own, only a routing table (see [api-gateway/README.md](api-gateway/README.md)).

Swagger is on only when a service runs in the `Development` environment, which is what Docker Compose sets. The Azure deployment runs `Production` and has it switched off.

**Trying a protected endpoint:**

1. Open the User Service's Swagger UI and call `POST /api/auth/login` with the Admin account above (or any account you registered).
2. Copy the `accessToken` from the response.
3. Click **Authorize** (top right of any service's Swagger UI), paste the token and confirm. Do not type `Bearer `; the UI adds it. One token works on every service, because they share the signing key.
4. Call any endpoint with **Try it out**.

How to read a Swagger page:

- An endpoint with a **padlock** needs a bearer token. An endpoint without one is open (login, register, password reset).
- Each protected endpoint states the role it needs in its **Access** line and lists `401` (no valid token) and `403` (wrong role) among its responses.
- Request and response bodies show example values built from the real contract types.
- `/api/internal/*` on the User Service is for other services and takes an `X-Internal-Api-Key` header instead of a token.

## Roles

| Role | Does |
|---|---|
| `Client` | Submits projects, reviews and approves designs, tracks construction, receives and pays invoices |
| `Architect` | Uploads and revises design documents for the projects they are assigned to |
| `ProjectManager` | Runs the construction phase and milestones, sees the project reports |
| `Admin` | Manages users, assigns staff, sees platform oversight and every report |

Every endpoint declares the roles that may call it. The gateway checks only that the token is genuine and unexpired; each service re-checks the token and enforces its own roles.

## Testing

The consolidated summary — test counts, coverage per service, JMeter results and the state of end-to-end testing — is in [docs/test-documentation.md](docs/test-documentation.md). What follows is how the tests are set up.

Two frameworks, standardized across the whole codebase (US-29):

- **Backend — xUnit.** Every .NET service that has any code has a matching `*-service-tests` project beside it (`services/user-service-tests`, `services/project-service-tests`, `services/design-service-tests`, `services/construction-service-tests`, `services/payment-service-tests`) plus `api-gateway-tests`. Run one with `dotnet test services/<name>/<Project>.csproj`; CI runs all of them on every push.
- **Frontend — Vitest + React Testing Library.** Tests live beside the code they cover, as `*.test.ts` / `*.test.tsx`. Run with `npm test` from `frontend/`; CI runs the same command.

  **Use the Node version in `.nvmrc` (`nvm use` from anywhere in the repo).** Node 25
  ships an experimental Web Storage API whose `localStorage` replaces jsdom's on the
  test global, and every frontend test then fails in `src/test/setup.ts` with
  `localStorage.clear is not a function` — a local-only breakage, since CI reads the
  same `.nvmrc`.

A third layer sits on top of those two (US-31):

- **End-to-end — Selenium (xUnit, Chrome).** `e2e-tests/` drives the real React app against the running stack — gateway, all five services, databases and Kafka — through two full workflows: register, create a project, assign staff, upload and approve a design, then invoice and pay; and from an approved design through construction, the automatic invoice, payment and handover. Start the stack as above, then run `dotnet test e2e-tests/EndToEnd.Tests.csproj`. Setup, settings and troubleshooting are in [e2e-tests/README.md](e2e-tests/README.md). CI compiles it on every push and runs it only when triggered by hand.

Conventions used across both:

- **Fakes over mocks** for anything a unit test stands in for — an `IProjectRepository`, an `IUserDirectoryClient`, an `ApiError` — so a test reads as "given this data" rather than "expect this call".
- **Real MySQL for SQL-heavy repositories.** A repository whose whole job is a query — `ProjectRepositoryDatabaseTests`, `DesignDocumentRepositoryDatabaseTests`, `MilestoneSetupRepositoryDatabaseTests` — runs the production migrations and the real ADO.NET against a live database (a `*DatabaseFixture` + `*DatabaseCollection` pair per service), because what is under test is the SQL, and a stub cannot show it holds under the real engine. Everything else needs nothing running.
- **Two reflection-based guards, present in every backend service that has controllers:** `EndpointRoleDeclarationTests` (every endpoint either names its allowed roles or is explicitly anonymous — US-03) and `MigrationScriptTests` (every event type a service can publish is allowed by its own outbox schema).
- **Frontend validation is unit-tested at the schema, not just through a page.** The Zod schemas in `frontend/src/lib/*-schemas.ts` mirror a backend service's own validation rules, and are tested directly for the boundary cases — empty, over-length, malformed — that a page-level render test would not exercise one by one.

### Coverage

Every test run also measures coverage (US-33):

- **Backend — Coverlet**, via `coverlet.collector` (already referenced by every `*-tests.csproj`, now actually invoked). CI runs each service's tests with `--collect:"XPlat Code Coverage"`, then merges the five Cobertura files with [reportgenerator](https://reportgenerator.io/) into one HTML report and a summary on the job's own GitHub Actions page. Run it yourself: `dotnet test <project> --collect:"XPlat Code Coverage" --results-directory ./coverage/<name>`.
- **Frontend — `@vitest/coverage-v8`**, V8's own instrumentation of the real test run. `npm run test:coverage` from `frontend/`; CI runs the same and writes the four overall percentages into its job summary too.

Both publish their full report as a CI build artifact (`backend-coverage-report`, `frontend-coverage-report`) on every run, pass or fail — not only living in the job log.

**Team target: a rough 70%**, agreed as a goal rather than a gate — CI reports the number on every run but does not fail the build on it. Backend suites are already fairly thorough after US-29; the frontend currently measures at roughly 90% lines. Revisit turning this into an enforced minimum once a few sprints of real numbers exist to set one sensibly, rather than picking a gate before anyone has seen the data.
