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
