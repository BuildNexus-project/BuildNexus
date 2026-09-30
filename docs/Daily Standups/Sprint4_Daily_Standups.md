# Sprint 4 Daily Standups — BuildNexus

---

## 2026-09-30

### Present
Chamath(IT24101842)

### Progress
- Chamath(IT24101842): US-18 (Project Report, SCRUM-31) completed — branch `feature/SCRUM-31-Project-Report-US18` ready to merge into `develop`. Full vertical slice: a read-only report query in the Project Service (`ProjectReportRepository`, ADO.NET with the status list and date bounds as bound parameters), pure grouping logic (`ProjectStatusReportBuilder`) and filter parsing (`ProjectReportFilterParser`), the Admin-only `GET /api/projects/reports/status` and `/status/export` endpoints, a CSV writer, the frontend API client and zod filter schema, and the `/admin/reports/project-status` page (status checkboxes, submitted-from/to dates, grouped view with counts and budget totals, Export CSV) with its Admin-only route and nav entry. Covered by unit tests on both sides plus database tests against real MySQL; README updated.

### Challenges
- A fix made while checking the page — a `nativeButton={false}` override in the shared `Button` to silence a dev-only Base UI console warning — was committed with the story and broke 8 frontend tests in CI: Base UI then puts `role="button"` on the rendered anchor, so every link-role query stopped matching. Reverted the same day. The change was outside the story's scope and was never run against the full frontend suite first — same lesson as the Sprint 3 build-before-push note.
- CI run #314 showed as cancelled, not failed: the docs commit was pushed while it was running and the workflow's concurrency rule cancelled it; #315, which covers the whole branch, was green. Push a story's commits together rather than one at a time.
- Opening the finished page in a real browser found what the tests couldn't: the status checkboxes ran together with no spacing (the dev server had not picked up the new file's Tailwind classes — the production build was correct, a restart fixed it), and the local database's ~790 QA-test projects put every row in one page. Each status group now lists its newest 50 with a "Show all" button; counts, totals and the CSV still cover every project.
- The Project Service README still said every test runs without MySQL and Kafka, which stopped being true when the `Integration`-tagged database tests were added — corrected.

### Decisions
- The date range filters on `created_at` (the day the Client submitted the project) as inclusive whole UTC days, not on `updated_at`, which moves with every status change and would answer "what was touched" rather than "what came in"
- A bad filter — unknown status, impossible date, `from` after `to` — is a `400` with the reason, never silently widened to the whole pipeline
- Every status in scope is always reported, in lifecycle order, even at zero projects, so the report keeps the same shape from week to week
- Filtering is done in SQL, grouping and ordering in code — so the grouping rules are unit-tested without a database, and only the SQL needs real MySQL
- The CSV is built server-side, and quotes RFC 4180-style, prefixes cells starting `=`, `+`, `-` or `@` with an apostrophe (an Admin opens it in Excel and a project name is whatever a Client typed), and starts with a UTF-8 byte-order mark; the export always uses the applied filter, so the file matches the screen
- Large groups are capped on the client rather than paged by the server, because the export needs every row anyway
- Flagged, not done: the report shows project name, location and budget but not the Client's name — that needs a User Service lookup, outside this story; no currency symbol, matching the other report pages

---

## 2026-09-30

### Present
Chamath(IT24101842)

### Progress
- Chamath(IT24101842): US-21 (Role-Based Dashboard Statistics, SCRUM-34) completed — branch `feature/SCRUM-34-Role-Based-Dashboard-Statistics-US-21` ready to merge into `develop`. A vertical slice across all five services, in 13 commits. Backend: nine read-only dashboard endpoints, each an ADO.NET query owned by the service whose data it reads and gated to exactly one role — Project (client, architect, admin), User (admin), Design (client, architect), Construction (client, project-manager), Payment (client). Frontend: a typed client for all nine (`dashboard-api.ts`), a `useDashboardSlice` hook, pure join and sum helpers (`dashboard-view.ts`), shared tile / progress bar / panel components, and four role dashboards on `/home` that replace the old generic project summary. All four acceptance scenarios covered: Client (active projects, design status, progress, payments due), Architect (assigned projects, pending revisions), Project Manager (active construction, milestones due), Admin (users and projects counts, reports). Unit tests on both sides plus database tests against real MySQL for every new query; READMEs updated; verified end to end through the gateway with real Client, Architect, Project Manager and Admin tokens.

### Challenges
- The story needs data from all five services and each service's database is its own, so no single query can answer a dashboard, and two services' data cannot be joined in SQL. Resolved by giving each service the slice it owns, under its existing gateway prefix (no gateway change), and joining on the project id in the page. That meant editing four services beyond Project, so it was raised before any code was written rather than decided alone.
- Milestones have no due date — only `NotStarted`, `InProgress` and `Completed` — so the Project Manager's "milestones due" cannot mean "late". Treated as outstanding and worded that way on screen; adding a due date would change US-12's create-milestone contract, so it is a gap for the BA, not invented here.
- The Admin AC asks for a count of "reports", but nothing stores a report — each is generated on demand — so there is no number to read. The Admin dashboard links the three report pages instead; the wording needs a BA answer.
- The Design Service has no record of who owns a project. It now asks the Project Service for the caller's projects, forwarding the caller's own token, and answers `502` with a reason when that service is down — an outage must not read as "you have no projects".
- The Construction Service stores who owns a project but not which Project Manager runs it, so the Project Manager's dashboard is portfolio-wide like the existing Build & payment report, not "your own" builds. Project names come from a separate best-effort request, so a build they are not assigned to shows a short id and is not linked.
- New Construction database tests failed with `DuplicateMilestoneNameException` and a leftover row: the fixture's run id is shared by every test class in the collection, and the project-id suffixes chosen (`d01`…) were already taken by another class. Renamed to a unique prefix and re-ran the whole suite.
- A test caught a real fault in the first version of the frontend hook: it showed the service's problem `title` (for example "Internal Server Error") in place of naming which part of the dashboard had failed. It now always names the slice and appends the service's `detail` only when there is one.
- The User Service integration tests connect to MySQL on port 3306, which a native MySQL holds on this machine — `infra/docker-compose.override.yml` moves the container to 3308 — so they fail at login here: 30 existing tests plus the 3 new ones. The new SQL was verified by pointing the tests at 3308 for one run and restoring the file; the underlying port mismatch is unchanged.
- Not yet looked at in a browser: the session had no browser tool, so the layout is verified by component tests, the production build and live calls through the gateway, not by eye — worth a look at `/home` for each role before merging.

### Decisions
- One endpoint per role rather than one that switches on the caller's role, each gated to exactly one role, and none takes an id: who is asking decides what comes back, always from the token's `sub`, so nobody can widen their own dashboard by asking differently
- "Active" means neither `Completed` nor `Cancelled` for projects; for the Construction Service a build is "under way" when it has been started and not handed over — narrower than the report's "active", which also lists planned builds nobody has started
- A project's design status is read from each document's latest version only, ranked awaiting the Client's review, then a revision they are waiting on, then approved; a project nothing has been uploaded for is `NoDesign`, not blank, and a revision the Architect has answered with a newer upload is no longer pending
- Payments due are the outstanding amount (invoice amount minus payments) on `Pending` invoices — the same definition US-16 and US-17 use — so a figure on the dashboard is one the pay endpoint will accept
- Each slice loads and fails on its own: a failed slice shows a dash and an alert beside everything that did load, never a zero, and a Project Service outage is a `502`, never an empty list
- Overall build progress is weighted by milestones, not an average of percentages, and per-project amounts owed are summed in whole cents so a total cannot drift
- The Project Manager's dashboard keeps the report's portfolio-wide scope and the Admin's reports are links with no count — both stated on screen and in the READMEs rather than presented as something they are not
- Flagged, not done: `CalculatePercent` now exists in three repositories and could be shared; the Design Service README's endpoint table still omits approve, request-revision and the report; the Construction Service README was a one-line placeholder, so only the dashboard section was added

---
