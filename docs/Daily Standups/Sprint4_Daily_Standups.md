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
- Chamath(IT24101842): US-21 (Role-Based Dashboard Statistics, SCRUM-34) completed — branch `feature/SCRUM-34-Role-Based-Dashboard-Statistics-US-21` ready to merge into `develop`. A vertical slice across all five services. Backend: nine read-only dashboard endpoints, each an ADO.NET query owned by the service whose data it reads and gated to exactly one role — Project (client, architect, admin), User (admin), Design (client, architect), Construction (client, project-manager), Payment (client). Frontend: a typed client for all nine (`dashboard-api.ts`), a `useDashboardSlice` hook, pure join and sum helpers (`dashboard-view.ts`), shared tile / progress bar / panel components, and four role dashboards on `/home` that replace the old generic project summary. All four acceptance scenarios covered: Client (active projects, design status, progress, payments due), Architect (assigned projects, pending revisions), Project Manager (active construction, milestones due), Admin (users and projects counts, reports).
- Chamath(IT24101842): US-21 follow-up — closed the gaps the first pass had flagged. Milestones now take an optional due date (migration 010, `PUT /api/construction/milestones/{id}/due-date`, a date field on the project page's milestone form and table, shown to the Client on the Progress page), so "milestones due" can say what is overdue. The Project Manager dashboard is now scoped to the projects they are assigned to, by asking the Project Service with the caller's own token, and is one request. The Admin dashboard has a "reports available" count. The progress percentage is one shared helper instead of three copies. The user-service database tests find `user-db` on any port. Construction, Design and Payment READMEs brought up to date. All four dashboards were then opened in real Chrome against demo data seeded through the API. Unit tests on both sides plus database tests against real MySQL for every new query.

### Challenges
- The story needs data from all five services and each service's database is its own, so no single query can answer a dashboard, and two services' data cannot be joined in SQL. Resolved by giving each service the slice it owns, under its existing gateway prefix (no gateway change), and joining on the project id in the page. That meant editing four services beyond Project, so it was raised before any code was written rather than decided alone.
- Milestones had no due date — only `NotStarted`, `InProgress` and `Completed` — so "milestones due" could not mean "late". First treated as outstanding and flagged; the follow-up added an optional, nullable due date rather than changing how milestones work, so a milestone with no date behaves exactly as US-12 defined it. Whether the BA wants dates required, or a rule such as "not in the past", is still theirs to say.
- The Admin AC asks for a count of "reports", but nothing stores a report — each is generated on demand. First shown as links only; now counted as the report pages available and labelled "generated on demand, not stored", taken from the Admin's own navigation so it cannot drift from the links. The wording still needs a BA answer.
- The Design Service has no record of whose a project is. It asks the Project Service for the caller's projects, forwarding the caller's own token, and answers `502` with a reason when that service is down — an outage must not read as "you have no projects". The Construction Service now does the same for the Project Manager, because it records who owns a project but not who runs it; a build started on a project assigned to a different Project Manager belongs to that one's dashboard.
- New Construction database tests failed with `DuplicateMilestoneNameException` and a leftover row: the fixture's run id is shared by every test class in the collection, and the project-id suffixes chosen (`d01`…) were already taken by another class. Renamed to a unique prefix and re-ran the whole suite.
- A test caught a real fault in the first version of the frontend hook: it showed the service's problem `title` (for example "Internal Server Error") in place of naming which part of the dashboard had failed. It now always names the slice and appends the service's `detail` only when there is one.
- Looking at the finished dashboards in a real browser found what the tests could not: on a phone, the Client's "LKR 4,810,000.00" payments tile ran out of its card and stretched the whole page to 512px on a 390px screen. The grid item would not shrink below its content, and the currency formatter's non-breaking space left nowhere to break but inside the digits. Fixed in the tile (it may shrink, long figures are set smaller, the space is made ordinary), confirmed at both widths, and pinned with tests. A same-day revision also read "waiting 0 days"; it now says it was asked for today.
- The User Service integration tests connect to MySQL on port 3306, which a native MySQL holds on this machine — `infra/docker-compose.override.yml`, git-ignored, moves the container to 3308 — so all of them failed at login here. CI was never affected. The port now comes from `USER_DB_PORT` (default 3306), set per machine in a git-ignored `local.runsettings` that `dotnet test` picks up; all 210 tests pass locally with no manual step.
- A running stack holds a lot of earlier test data, so seeding demo data for the visual check and removing it afterwards had to be done by recorded id rather than by pattern, across five databases, to leave the rest untouched.

### Decisions
- One endpoint per role rather than one that switches on the caller's role, each gated to exactly one role, and none takes an id: who is asking decides what comes back, always from the token's `sub`, so nobody can widen their own dashboard by asking differently
- "Active" means neither `Completed` nor `Cancelled` for projects; for the Construction Service a build is "under way" when it has been started and not handed over — narrower than the report's "active", which also lists planned builds nobody has started. A project whose status is `Completed` is still read for the Project Manager, because its build may be awaiting handover
- A project's design status is read from each document's latest version only, ranked awaiting the Client's review, then a revision they are waiting on, then approved; a project nothing has been uploaded for is `NoDesign`, not blank, and a revision the Architect has answered with a newer upload is no longer pending
- Payments due are the outstanding amount (invoice amount minus payments) on `Pending` invoices — the same definition US-16 and US-17 use — so a figure on the dashboard is one the pay endpoint will accept
- Each slice loads and fails on its own: a failed slice shows a dash and an alert beside everything that did load, never a zero, and a Project Service outage is a `502`, never an empty list
- Overall build progress is weighted by milestones, not an average of percentages, and per-project amounts owed are summed in whole cents so a total cannot drift
- A due date is a calendar day (`DATE` column, `yyyy-MM-dd` string end to end), never a timestamp, so no timezone can move it; overdue means dated, before today and not completed, judged against an injected clock so the count and each milestone's flag agree and tests can fix the day
- Dated milestones lead the list, soonest first, then undated ones; the overdue count is never capped by the list
- Flagged, not done: the `Base UI nativeButton` dev-only console warning on link-styled buttons is older than this story and was left alone after the last attempt to silence it broke eight tests

---

## 2026-10-02

### Present
Chamath(IT24101842)

### Progress
- Chamath(IT24101842): US-26 (In-App Notifications from Events, SCRUM-39) completed — branch `feature/SCRUM-39-In-App-Notifications-from-Events-US-26` ready to merge into `develop`. A vertical slice in the Project Service and the frontend. Backend: a `notifications` table (migration 008) holding one row per person per event; a Kafka consumer that reads `design-events`, `construction-events` and `payment-events` and stores a notification for the project's Client and assigned Architect whenever a `DesignApproved`, `MilestoneCompleted` or `PaymentReceived` arrives; and three endpoints under `/api/projects/notifications` (list, mark one read, mark all read), open to Client and Architect only. Frontend: a typed client (`notifications-api.ts`) and a Notifications panel on `/home` with a "N new" badge, a link to each project and mark-read controls, shown to Clients and Architects. Both acceptance scenarios covered: the consumer stores per-user records, and a stored notification is visible the next time the person logs in or loads their dashboard. Unit tests for the event-to-notification mapping, consumer and endpoint tests over stand-ins, and database tests against real MySQL for every query.
- Chamath(IT24101842): checked end to end against the running stack — created a project, assigned the Architect, published one event of each kind straight to Kafka and read the results back through the gateway as each role (200 for Client and Architect, 403 for Project Manager and Admin, 401 with no token, 404 for another person's notification) — then opened the page in real Chrome at desktop and phone width.

### Challenges
- The story says to notify "whoever is concerned", but an event names a project and never the people. The Client's id is on the project events; the Architect's id is on none of them. The only place both are recorded is the Project Service's own `projects` table, so the notifications live there rather than in a new service — a separate one could not have worked out who to tell without a change to the Project Service anyway, and would have meant a sixth service, database, pipeline and deployment. No other service was edited and the gateway needed no change.
- The Project Service already reads `construction-events` and `payment-events` under consumer groups of its own. Joining one of them would have split a topic's partitions between two readers, each seeing only some messages, so the new consumer reads all three topics under a group of its own (`project-service-notifications`).
- Kafka delivers at least once, so a restart replays events. The table's unique key on (event id, person) makes a replay store nothing new; the insert is `ON DUPLICATE KEY UPDATE` and not `INSERT IGNORE`, because `IGNORE` would also turn a foreign-key failure into a warning and a notification for a missing project would silently never exist.
- The Payment Service refuses an Architect every one of its endpoints, so a payment amount in the notification text would show them a figure they may not otherwise see. The text says only that a payment was received.
- The first run of the consumer replayed the whole history of the three topics and stored nothing: every old event was about a project earlier test runs had deleted. Those are logged and skipped rather than treated as errors, since a retry cannot create the project.
- The existing dashboard tests assert the exact requests each dashboard makes, so a notifications request inside them would have broken those assertions. The panel is rendered by the home page instead, and the home page's own request assertions were updated for the Client and Architect.
- The first version of the panel re-read its list by changing the identity of a callback, which made the linter warn about an unnecessary dependency. Rather than add a warning, or change the shared `useDashboardSlice` hook for a story-local reason, the panel has its own small loading effect that re-runs after a change.
- Flagged, not fixed: the existing Construction, Payment and Project consumers never apply the Azure Event Hubs login settings that the publishers do, so they would fail to connect in Azure. The new consumer applies them; the others belong to other stories.
- Looking at the finished page and the demo data it needed, the demo project and its notifications were removed by recorded id afterwards, leaving the development database as it was.

### Decisions
- The notifications live in the Project Service, the one service that knows a project's Client and Architect, and are written by a consumer and read through the service's own endpoints — nothing new is published and no cross-service lookup is made
- Each event notifies the project's Client and its assigned Architect, the two roles the story names; the Project Manager and Admin are not told and are refused by the endpoints. The person who caused the event is told too — a Client who pays gets a receipt — because leaving them out would leave the Client with nothing for two of the three events
- A notification is stored as finished text, stamped with when the event happened rather than when it was read, so it reads the same later and sorts correctly even for an event replayed after downtime
- The list is capped at 20 and the unread count is the whole figure, so "12 new" stays true when 20 are shown
- Marking read is explicit; showing the panel does not mark anything read. Marking one twice is a success that keeps the first time, and a notification that is not the caller's is a `404`, the same as one that does not exist
- Every endpoint takes the person only from the token's `sub` — none takes an id — so nobody can read or dismiss another person's notification
- A message too long for its column is cut to fit, because a row the database refuses would be retried forever and hold the partition
- A new consumer group reads each topic from the start, so the first run also notifies for older events; accepted, since they are stamped with their real time and the list is capped
- Open for the BA: should the person who caused an event be told about it (today they are), and should the Project Manager or Admin ever be told? Both are a one-line change in the mapper

---
