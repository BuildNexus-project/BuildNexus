# Sprint 2 Daily Standups — BuildNexus

---

## 2026-09-03

### Present
it24100312

### Progress
- it24100312: US-09 (Upload & Version Design Documents) — the whole vertical slice in one day: scaffolded the Design Service from scratch (its own schema, DbUp migrations, JWT validation and deny-by-default authorization, Swagger, `/health`, mirroring the Project Service's setup), `DesignDocumentRepository` over ADO.NET (`AddVersionAsync` finds-or-creates the document and appends `MAX(version_number) + 1` in one transaction, retrying on the unique constraint so a racing upload never skips or reuses a number), file type/size validation, the per-project access check against the Project Service, the upload and listing endpoints, the frontend API client and schema, and the design documents page — then covered all of it with tests and wired the new project into CI

### Challenges
- The `${DESIGN_DB_*:?...}` compose-interpolation trap: `docker compose up user-db project-db` parses the *whole* compose file before starting anything, so the new `design-db`/`design-service` blocks' required variables had to be set in the CI job's env even though the job doesn't start either container. Diagnosed and fixed same day.

### Decisions
- New service, new database, same shape as Project Service — no ORM, DbUp migrations, JWT/deny-by-default, Swagger — kept identical on purpose rather than an opportunity to vary the pattern

---

## 2026-09-06

### Present
it24100312

### Progress
- it24100312: US-07 (Assign Architect and Project Manager) started — indexed the project staff columns, added the cross-service call to read a user's role from the User Service (so an assignment can be refused if the account isn't actually an Architect/ProjectManager), `AssignStaffAsync` through ADO.NET, and the assign-architect / assign-project-manager endpoints

### Challenges
- The new repository methods needed implementing on every test double before the suite would build again — routine, but touched more files than the endpoint work itself

### Decisions
- Assigning staff is Admin-only, checked against the User Service rather than trusted from the request — the same "don't trust the caller, verify against the source of truth" pattern as the project-access check in US-09

---

## 2026-09-07

### Present
it24100312

### Progress
- it24100312: US-07 completed and merged — endpoint tests, the frontend API client calls, letting an Admin assign staff from the project detail page, and the UI tests for the new controls (plus updating the project-page tests to account for the new staff-list calls the page now makes)

### Challenges
- None logged

### Decisions
- None logged beyond the day before

---

## 2026-09-08

### Present
it24100312

### Progress
- it24100312: US-08 (Cancel/Reject Project) — full vertical slice, completed and merged same day: the `Cancelled` status and its history note, recording a cancellation reason through ADO.NET, the cancel-project endpoint, the frontend API client call, letting a Client or Admin cancel from the UI, keeping cancelled projects out of the default project list, and tests end to end (backend and the frontend list-toggle/control)

### Challenges
- None logged

### Decisions
- Cancellation is Client-or-Admin, not staff — and only reachable before `Construction` starts (enforced by the same `ProjectStatusTransitions` cancellable-from rule from Sprint 1's US-06), so work already on the ground can't be cancelled out from under it

---

## 2026-09-09

### Present
it24100312

### Progress
- it24100312: US-10 (View Design Documents and History) — full vertical slice, completed and merged same day: widened `design_document_versions`' status check constraint (migration `002`, additive — `001` was left untouched) to allow `UnderReview` and `Approved` alongside `Submitted`, the "current version" rule (the highest-numbered version that is `UnderReview` or `Approved` — not simply the latest upload), matched the frontend contract to the widened schema, highlighted the current version in the documents view, and tested the current-version rule end to end

### Challenges
- The original `001` migration's status constraint only allowed `Submitted` — had to be widened via a new migration rather than the original, the DbUp discipline from Sprint 1 (never edit a migration that's already shipped) paying off immediately

### Decisions
- "Current" is a computed rule (highest-numbered `UnderReview`/`Approved` version), not a stored flag — so it can never drift out of sync with the version rows themselves

---

## 2026-09-10

### Present
it24100312

### Progress
- **US-11 (Review Design — Approve or Request Revision), completed and merged:** an internal user-lookup endpoint so other services can check an account's role, migration `003` (adds `RevisionRequested` and the `reviewed_by`/`reviewed_at`/`review_comment` columns), `RecordReviewDecisionAsync` — one transaction with the document row locked for its length, so two decisions on the same document can't race, refusing a version that isn't `Submitted` or a document that already has an Approved version — the Design Service's first Kafka outbox and its first producer, the approve and request-revision endpoints, emailing the Architect on a revision request (via Mailpit locally), and the frontend UI and API client
- **US-20 (Design Approval Report), completed and merged:** the aggregate report query (per-project document/version counts and average time-to-approval), its response shape, the Admin-only `GET /api/designs/reports/approval` endpoint, and the frontend report page
- **US-23 (Design Approval Event Integration), completed and merged:** widened the outbox to three event types (`DesignSubmitted`, `DesignRevisionRequested`, `DesignApproved`) and raised the first two alongside the existing `DesignApproved`; scaffolded the **Construction Service** — the platform's fourth backend service, its own database — and gave it the codebase's **first Kafka consumer**: `DesignEventsConsumer` creates a milestone-setup placeholder from `DesignApproved`, absorbs a redelivered/duplicate event via a unique-index `INSERT IGNORE` rather than erroring, commits a message it can't parse rather than blocking the partition on it, and never touches the publisher on any failure

### Challenges
- **Real listing bug, caught by functional testing, not unit tests:** `ListForProjectAsync`'s own `SELECT` never picked up `reviewed_by`/`reviewed_at`/`review_comment` — only the write path and the single-version read did — so the documents listing kept showing a decision as blank even after it was recorded. Invisible to the existing controller tests because the in-memory fake repository mutates the same object a later read returns; only a real end-to-end run against MySQL surfaced it. Fixed same day, with a regression test (`ListForProjectAsync_reflects_a_recorded_review_decision`) that runs against real MySQL in CI.
- The `${InternalService__ApiKey:?...}` compose-interpolation trap — the second time this exact class of CI failure happened (see 2026-09-04). Fixed the same way, but it was re-diagnosed from scratch rather than recognized on sight.
- The `${CONSTRUCTION_DB_*:?...}` compose-interpolation trap — the *third* occurrence of the same pattern, on the new Construction Service. This time named explicitly as a recurring class of failure rather than a one-off.
- Verifying the first Kafka consumer properly meant publishing real events to the broker and watching the resilience behaviour happen, not asserting it: a redelivered `DesignApproved` was absorbed (no second row), a malformed message was logged and skipped, an unrelated event type on the same topic was silently ignored, and the consumer stayed up through all of it.

### Decisions
- Confirmed with the team before building anything (three separate forks, not guessed): one milestone-setup placeholder per **project**, not per document; **backend-only** for this story, no frontend for the Construction Service yet; and publish **all three** event types even though only `DesignApproved` has a consumer today, since the other two carry the same project/document context a future consumer would otherwise have to call back for
- `EndpointRoleDeclarationTests` widened to recognise a non-default `AuthenticationSchemes` value as a third valid way an endpoint can be gated, alongside a declared role or explicit `[AllowAnonymous]` — needed once the internal user-lookup endpoint used its own scheme

---

## 2026-09-13

### Present
it24100312, IT24101495

### Progress
- it24100312: US-29 (Unit Testing Coverage) completed and merged — documented xUnit (backend) and Vitest + React Testing Library (frontend) as the standardized frameworks (both already the codebase's actual practice, never written down before); audited existing coverage before adding anything, and added tests only for the real gaps that turned up — `Pbkdf2PasswordHasher` (hash/verify roundtrip, salting, tampered/malformed values refused) and `PlatformRoleAttribute` on the backend; the five frontend `lib/` validation/token/form-error modules that had never been unit-tested directly, only through whichever page used them
- IT24101495: SCRUM-52 (Deploy User Service to Azure) started — Terraform provider and remote state backend, the shared Azure resources every service deployment will reuse, the User Service's App Service and database, a path-filtered CI/CD deploy job (so a change to one service doesn't redeploy all of them), and the deployment runbook

### Challenges
- IT24101495: needed to move the Azure resource stack from `centralindia` to `southeastasia`, and separately documented how to recover a Terraform `apply` that failed partway — both written down as runbook material rather than left as tribal knowledge

### Decisions
- `payment-service` has no code at all — confirmed with the team up front that "payment calculations" has no tests under US-29 by design, rather than building throwaway calculation logic just to have something to test. Logged explicitly as a known gap in the README instead of silently dropped.

---

## 2026-09-14

### Present
it24100312, IT24101495

### Progress
- it24100312: US-33 (Code Coverage Reporting) completed and merged — activated `coverlet.collector` in CI (every backend `*-tests` project already referenced it from earlier stories; it had just never been invoked), merged the five services' Cobertura files into one HTML report plus a summary written straight into the CI job's own summary page, added `@vitest/coverage-v8` on the frontend with the same job-summary treatment, and documented both plus the agreed target
- IT24101495: SCRUM-53 (Deploy Project Service to Azure) started — the Project Service's own database and scoped MySQL user, and the shared Event Hubs namespace plus the `project-events` hub it will publish to — in progress

### Challenges
- None logged for coverage — reportgenerator and `@vitest/coverage-v8` verified locally before touching CI, so the CI change itself was mechanical

### Decisions
- Confirmed with the team before building: a rough **70%** target, and **informational only** — CI reports the number every run but does not fail the build on it. Nobody had looked at real coverage numbers yet, so gating now risked breaking CI the moment coverage was actually measured; real frontend coverage turned out to already be ~90% lines, backend suites already thorough after US-29
