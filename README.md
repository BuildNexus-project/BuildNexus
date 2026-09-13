# BuildNexus
This is a microservices-based construction project management system with 5 services (User/Identity, Project, Design, Construction, Payment), React frontend, Kafka event bus, ADO.NET with direct SQL (no ORM), built by a 4-person team.

## Testing

Two frameworks, standardized across the whole codebase (US-29):

- **Backend — xUnit.** Every .NET service that has any code has a matching `*-service-tests` project beside it (`services/user-service-tests`, `services/project-service-tests`, `services/design-service-tests`, `services/construction-service-tests`) plus `api-gateway-tests`. Run one with `dotnet test services/<name>/<Project>.csproj`; CI runs all of them on every push.
- **Frontend — Vitest + React Testing Library.** Tests live beside the code they cover, as `*.test.ts` / `*.test.tsx`. Run with `npm test` from `frontend/`; CI runs the same command.

Conventions used across both:

- **Fakes over mocks** for anything a unit test stands in for — an `IProjectRepository`, an `IUserDirectoryClient`, an `ApiError` — so a test reads as "given this data" rather than "expect this call".
- **Real MySQL for SQL-heavy repositories.** A repository whose whole job is a query — `ProjectRepositoryDatabaseTests`, `DesignDocumentRepositoryDatabaseTests`, `MilestoneSetupRepositoryDatabaseTests` — runs the production migrations and the real ADO.NET against a live database (a `*DatabaseFixture` + `*DatabaseCollection` pair per service), because what is under test is the SQL, and a stub cannot show it holds under the real engine. Everything else needs nothing running.
- **Two reflection-based guards, present in every backend service that has controllers:** `EndpointRoleDeclarationTests` (every endpoint either names its allowed roles or is explicitly anonymous — US-03) and `MigrationScriptTests` (every event type a service can publish is allowed by its own outbox schema).
- **Frontend validation is unit-tested at the schema, not just through a page.** The Zod schemas in `frontend/src/lib/*-schemas.ts` mirror a backend service's own validation rules, and are tested directly for the boundary cases — empty, over-length, malformed — that a page-level render test would not exercise one by one.

### Known gap

`payment-service` has no code yet — it is a placeholder for a future story. There is nothing there to unit test, so "payment calculations" has no tests under this story: they will be added alongside whichever story first builds that service's logic.
