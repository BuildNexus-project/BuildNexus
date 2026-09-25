# BuildNexus
This is a microservices-based construction project management system with 5 services (User/Identity, Project, Design, Construction, Payment), React frontend, Kafka event bus, ADO.NET with direct SQL (no ORM), built by a 4-person team.

## Testing

Two frameworks, standardized across the whole codebase (US-29):

- **Backend — xUnit.** Every .NET service that has any code has a matching `*-service-tests` project beside it (`services/user-service-tests`, `services/project-service-tests`, `services/design-service-tests`, `services/construction-service-tests`) plus `api-gateway-tests`. Run one with `dotnet test services/<name>/<Project>.csproj`; CI runs all of them on every push.
- **Frontend — Vitest + React Testing Library.** Tests live beside the code they cover, as `*.test.ts` / `*.test.tsx`. Run with `npm test` from `frontend/`; CI runs the same command.

  **Use the Node version in `.nvmrc` (`nvm use` from anywhere in the repo).** Node 25
  ships an experimental Web Storage API whose `localStorage` replaces jsdom's on the
  test global, and every frontend test then fails in `src/test/setup.ts` with
  `localStorage.clear is not a function` — a local-only breakage, since CI reads the
  same `.nvmrc`.

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

### Known gap

`payment-service` has no code yet — it is a placeholder for a future story. There is nothing there to unit test, so "payment calculations" has no tests under this story, and no coverage number either: both will exist once a future story builds that service's logic.
