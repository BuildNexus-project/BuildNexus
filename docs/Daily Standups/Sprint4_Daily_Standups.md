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
- Chamath(IT24101842): US-26 (In-App Notifications from Events, SCRUM-39) completed — branch `feature/SCRUM-39-In-App-Notifications-from-Events-US-26` ready to merge into `develop`. A vertical slice in the Project Service and the frontend. Backend: a `notifications` table (migration 008) holding one row per person per event; a Kafka consumer that reads `design-events`, `construction-events` and `payment-events` and stores a notification for the project's Client and assigned Architect whenever a `DesignApproved`, `MilestoneCompleted` or `PaymentReceived` arrives; and three endpoints under `/api/projects/notifications` (a paged list, mark one read, mark all read), open to Client and Architect only. Frontend: a typed client (`notifications-api.ts`), a Notifications panel on `/home` with a "N new" badge, a link to each project and mark-read controls, a bell with the unread count in the header, and a full history page at `/notifications` with "Load more". Both acceptance scenarios covered: the consumer stores per-user records, and a stored notification is visible the next time the person logs in or loads their dashboard. Unit tests for the event-to-notification mapping, consumer and endpoint tests over stand-ins, and database tests against real MySQL for every query.
- Chamath(IT24101842): US-26 follow-up — closed the gaps the first pass had flagged. The bell, the history page and live refresh were added, so a notification that arrives while the dashboard is open shows up without a reload (the app's existing polling plus a change announcement, so marking something read updates the bell at once), and the list can be paged past the latest 20 (`skip`/`take`, `hasMore`, a `400` for a bad value). The Project Service's three consumers now receive the Azure Event Hubs login settings through one shared `KafkaBrokerSettings`, as its publisher already did.
- Chamath(IT24101842): checked end to end against the running stack — created a project, assigned the Architect, published events of each kind straight to Kafka and read the results back through the gateway as each role (200 for Client and Architect, 403 for Project Manager and Admin, 401 with no token, 404 for another person's notification, 400 for a bad page) — then opened the pages in real Chrome at desktop and phone width, including leaving the dashboard open and watching the bell go from 26 to 27 on its own.

### Challenges
- The story says to notify "whoever is concerned", but an event names a project and never the people. The Client's id is on the project events; the Architect's id is on none of them. The only place both are recorded is the Project Service's own `projects` table, so the notifications live there rather than in a new service — a separate one could not have worked out who to tell without a change to the Project Service anyway, and would have meant a sixth service, database, pipeline and deployment. No other service was edited and the gateway needed no change.
- The Project Service already reads `construction-events` and `payment-events` under consumer groups of its own. Joining one of them would have split a topic's partitions between two readers, each seeing only some messages, so the new consumer reads all three topics under a group of its own (`project-service-notifications`).
- Kafka delivers at least once, so a restart replays events. The table's unique key on (event id, person) makes a replay store nothing new; the insert is `ON DUPLICATE KEY UPDATE` and not `INSERT IGNORE`, because `IGNORE` would also turn a foreign-key failure into a warning and a notification for a missing project would silently never exist.
- The Payment Service refuses an Architect every one of its endpoints, so a payment amount in the notification text would show them a figure they may not otherwise see. The text says only that a payment was received.
- The first run of the consumer replayed the whole history of the three topics and stored nothing: every old event was about a project earlier test runs had deleted. Those are logged and skipped rather than treated as errors, since a retry cannot create the project.
- The existing dashboard tests assert the exact requests each dashboard makes, so a notifications request inside them would have broken those assertions. The panel is rendered by the home page instead, and the home page's own request assertions were updated for the Client and Architect. For the same reason the header's new count request had to be allowed for in two older tests that render the whole shell (the app-shell tests and the project status report page's "never asks for the report" check) — what they prove is unchanged.
- The first version of the panel re-read its list by changing the identity of a callback, which made the linter warn about an unnecessary dependency. Rather than add a warning, or change the shared `useDashboardSlice` hook for a story-local reason, the panel has its own small loading effect that re-runs after a change and on the polling timer.
- Only the Project Service's publisher passed the Azure Event Hubs login settings; its three consumers did not, so against Event Hubs it could publish but not read. Fixed in this service by building every client's config in one place and pinning it with tests. First thought to be a small gap in the Construction and Payment services' consumers too, but a check showed those two services have no Event Hubs support at all — not in their options, their publishers or the Terraform — and are not deployed to Azure yet. That is part of deploying them, not a defect today, so it was left for the story that does that rather than tucked into this one.
- Who is told is decided when the event is read, not when it happened, because nothing on any topic says who the Architect was at the time. Fine in steady state; on a replay or after downtime an Architect assigned later is told about an earlier approval, and one reassigned away is not. Documented, not fixable from here.
- Looking at the finished pages and the demo data they needed, both demo projects and their notifications were removed by recorded id from the Project, Construction and Payment databases afterwards, leaving the development database as it was (788 projects, no notifications).

### Decisions
- The notifications live in the Project Service, the one service that knows a project's Client and Architect, and are written by a consumer and read through the service's own endpoints — nothing new is published and no cross-service lookup is made
- Each event notifies the project's Client and its assigned Architect, the two roles the story names; the Project Manager and Admin are not told and are refused by the endpoints. The person who caused the event is told too — a Client who pays gets a receipt — because leaving them out would leave the Client with nothing for two of the three events
- A notification is stored as finished text, stamped with when the event happened rather than when it was read, so it reads the same later and sorts correctly even for an event replayed after downtime
- The list is paged (default 20, at most 50) and the unread count is the whole figure, so "12 new" stays true however many are shown; a bad `skip` or `take` is a `400`, never quietly clamped
- Marking read is explicit; showing the panel does not mark anything read. Marking one twice is a success that keeps the first time, and a notification that is not the caller's is a `404`, the same as one that does not exist
- Every endpoint takes the person only from the token's `sub` — none takes an id — so nobody can read or dismiss another person's notification
- Live refresh is the app's existing polling, not a push channel: there is no push transport and none was added. The bell and panel poll; the history page does not, since re-reading it would reshuffle the pages under the reader
- The bell is shown only to Client and Architect, and shows no badge for zero or before a count is known, so a missing badge never claims nothing is new
- A message too long for its column is cut to fit, because a row the database refuses would be retried forever and hold the partition
- A new consumer group reads each topic from the start, so the first run also notifies for older events; accepted, since they are stamped with their real time
- Flagged, not done: the Construction and Payment services' Azure Event Hubs support belongs to the story that deploys them
- Open for the BA: should the person who caused an event be told about it (today they are), should the Project Manager or Admin ever be told, and is notifying for older events on a first deploy acceptable? The first two are a one-line change in the mapper

---

## 2026-10-03

### Present
Karunathilaka R.C.D

### Progress
- Karunathilaka R.C.D: US-38 (Admin: Platform Oversight & Reports, SCRUM-51) completed — branch `feature/SCRUM-51-Admin-Platform-Oversight-and-Reports-US-38` ready to merge into `develop`. Backend (Project Service only): an Admin-only `GET /api/projects/oversight` returning every project with its status, assigned Architect and Project Manager (id and name), last-updated date and a stalled flag, plus the total, the stalled count and the stalled threshold; the stalled rule is its own small class (`ProjectStallPolicy`). Frontend: a typed client (`oversight-api.ts`) and an Admin-only page at `/admin/oversight` with a Reports section linking the Project, Construction & Payment and Design Approval reports and an All projects table with stalled rows highlighted. It has an "Oversight" entry in the Admin's header, footer and dashboard cards, a breadcrumb, and is now the Admin's first button on `/home`. All three acceptance scenarios covered, including the optional stalled highlighting. Unit tests on both sides: the stall rule, the endpoint over a stand-in repository, the role gate, the client and the page.

### Challenges
- The three reports and the Admin's project list already existed, so the story is mostly a new view over them. Reused `ListAllAsync` and the existing name lookup rather than adding SQL or a new service; no other service and no gateway route was touched, since `/api/projects/{**catch-all}` already covers the new path.
- The existing project list (`GET /api/projects`) leaves out cancelled projects and carries no staff, so it could not be reused as-is for "all projects with assigned staff". A separate Admin-only endpoint was cleaner than adding flags to a list every role uses.
- "Links to or embeds" the reports: linked, because each report is already a full page with its own filters and CSV export and embedding would have duplicated them. The links come from the Admin's navigation list, so the page and the dashboard cannot disagree about which reports exist.
- Adding "Oversight" to the Admin navigation changed the expected list in existing tests (header, footer, dashboard cards, breadcrumbs, route guards); updated them to the new order. Changing the Admin's first button on `/home` from Users to Oversight was a one-line change in the home page that goes with the story.
- The first version of the page test failed because the shared card title is not a heading by default; the page's two card titles are now real headings, as the dashboard panels already do.

### Decisions
- The oversight list is its own Admin-only endpoint in its own controller, like the report controller, because it reads across every project; the role is enforced by the service independently of the gateway
- Cancelled projects are included, because this is the whole-platform view and the status says which are closed out
- A name that cannot be looked up is `null` with the id kept, and the screen says "Assigned (name unavailable)" — a User Service outage never blocks the list and never makes an assigned slot look empty
- Stalled means not `Completed`/`Cancelled` and not updated for 14 days or more; a `Pending` project nobody has picked up counts, because that is exactly the neglect the screen exists to surface. The service decides and sends the threshold, so the screen keeps no second copy of the rule
- Stalled is shown with a badge in words as well as a tinted row, so it never depends on seeing the colour
- Flagged, not done: the 14-day threshold is a constant, not configuration; "last updated" moves on status changes and staff assignment but not on design uploads or milestone progress, which live in other services' databases
- Open for the BA: is 14 days the right threshold, and should "stalled" take design or construction activity into account (that would need the other services to report it)

---

## 2026-10-04

### Present
Chamath(IT24101842)

### Progress
- Chamath(IT24101842): US-36 (Project & API Documentation, SCRUM-49) completed — branch `feature/SCRUM-49-Project-and-API-Documentation-US-36` ready to merge into `develop`, apart from the Selenium section noted below. Backend: every service's Swagger now marks only the endpoints that need a token (login, register and reset no longer show a padlock), states each endpoint's allowed roles and its `401`/`403` responses, and shows the controllers' own `///` descriptions; the User Service also documents its `/api/internal` API-key scheme. The setup moved out of each `Program.cs` into an `ApiDocs` folder (`SwaggerSetup`, `AuthorizationOperationFilter`) in all five services, with a database-free `SwaggerDocumentTests` per service. Documentation: the root `README` now covers full-stack setup, ports, the seeded Admin, Swagger links for all five services and how to authorize in Swagger; `docs/architecture.md` has a diagram of the local stack and a separate one of what is deployed on Azure, plus the request path and a topic-by-topic event table; `docs/test-documentation.md` consolidates test counts, coverage, JMeter results and E2E status. Stale statements in the infra, gateway and frontend READMEs, the JMeter notes and the runbook were corrected. All three acceptance scenarios covered, with one gap: Scenario 3 mentions Selenium E2E scenarios, which are delivered by a separate story, so that section of the test documentation is a marked placeholder.

### Challenges
- Swagger was already wired into every service, so the real work was making it meet the acceptance criteria. The one global Bearer requirement marked every endpoint as locked, including login and register, and the controllers' XML comments were never loaded, so most written descriptions never reached the page. Replaced the global requirement with a filter that reads the same `[AllowAnonymous]`/`[Authorize]` metadata the authorization middleware reads.
- The first test run showed the roles line missing from every description: Swashbuckle's XML-comments step overwrites the operation description, and it ran after the filter. Registering the comments before the filter, so the filter appends, fixed it. Worth knowing for anyone adding another filter.
- Turning on XML documentation made four services warn about partly documented parameters (CS1573) and one about a stale `<see cref>` in the Payment Service (copied from the Construction Service, naming a class Payment does not have). Silenced the first, as the goal is richer API docs and not a comment on every parameter; fixed the second, since it was wrong.
- Swashbuckle is configured per service and the services share no code, so the filter and setup are copied into each rather than shared. The User Service's copy is the only one that uses the internal-key branch.
- Measured real coverage rather than quoting CI: unit tests alone reach 47.1% of backend lines but unit plus integration reach 82.1%, because the data layer is SQL that is only exercised against real MySQL. Both figures are in the test documentation with the reason, so the lower one is not read as a failure.
- The frontend coverage run had one timeout (a 5-second limit hit at 5.9 s) when it overlapped with the backend run on the same machine; a quiet re-run passed 796 of 796. Documented as a slow test, not fixed here.
- The existing docs said "only the User Service exists" in two places and the performance notes said the gateway was not deployed. All three predate later stories; corrected, with dated notes on the recorded results rather than rewriting them.

### Decisions
- Swagger stays Development-only. The runbook turns it off in Azure on purpose, and the README says where to see it (the local stack); changing that is a deployment decision, not a documentation one
- The gateway has no Swagger page, because it has no endpoints of its own; the README says so rather than adding a page that would document nothing
- The architecture is drawn twice, local and Azure, because they differ (no Construction or Payment Service on Azure, Event Hubs instead of Kafka, one shared MySQL server) and a single diagram would be wrong for one of them
- The test documentation uses the combined coverage figure as the headline and shows unit-only beside it, with the reason, instead of picking whichever number looks better
- The JMeter results are transcribed from the committed results files with their dates, not re-run; they predate the gateway's deployment and the document says so
- Selenium E2E is left as a marked placeholder with an empty scenarios table, not filled with invented results
- Flagged, not done: the per-service READMEs' endpoint tables are out of date (Swagger is now the reference and the README links to it); the Application Insights gap that stops the Azure design-upload `500` being traced; the near-limit frontend test
- Open for the BA: should Swagger be switched on in Azure for a marker, and where should the Selenium results go in the test documentation when that story merges

---
