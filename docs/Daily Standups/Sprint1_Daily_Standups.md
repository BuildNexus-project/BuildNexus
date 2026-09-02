# Sprint 1 Daily Standups — BuildNexus

---

## 2026-08-20

### Present
Malith(IT24101495)

### Progress
- Malith(IT24101495): US-01 (User Registration & Login) — backend foundation: scaffolded User Service project and database schema, built the ADO.NET data access layer, added password hashing (PBKDF2-HMAC-SHA256), JWT issuance, and request validation for auth

### Challenges
- None logged — first day of implementation

### Decisions
- Password hashing: PBKDF2-HMAC-SHA256
- Data access confirmed as raw ADO.NET from the very first commit, no ORM at any point

---

## 2026-08-21

### Present
Malith(IT24101495)

### Progress
- Malith(IT24101495): US-01 continued — built registration and login endpoints, added bearer token validation and protected user endpoints, and stood up the local infrastructure stack (Docker Compose, shared JWT config)

### Challenges
- MySqlConnector maps a CHAR(36) column to `System.Guid`, not `string` — the original `GetString("id")` would have thrown `InvalidCastException` against real MySQL. Caught and fixed same day, before it reached a later story.
- Project had defaulted to `net8.0`; retargeted to `net10.0` (current LTS) same day rather than letting it stand

### Decisions
- Every service targets net10.0, not net8.0, going forward — locked into the shared prompts after this

---

## 2026-08-22

### Present
Malith(IT24101495)

### Progress
- Malith(IT24101495): US-01 completed and merged — scaffolded the React frontend with Tailwind and shadcn/ui, built the auth context with token persistence, the registration page (role-restricted dropdown), the login page, and protected routing with an authenticated landing page

### Challenges
- Registration endpoint originally accepted `role: "Admin"` with no restriction — anyone could self-register as Admin. Closed same day: registration restricted to Client/Architect/ProjectManager, one bootstrap Admin seeded separately.
- Local MySQL default port didn't match `docker-compose`'s published port — fixed so the default connection string works unmodified rather than needing manual override

### Decisions
- Frontend scaffolded with shadcn/ui + Tailwind from the first commit — not added later
- Verification going forward: every story checked against real MySQL, not just automated tests, following directly from the Guid bug caught the day before

---

## 2026-08-23

### Present
Malith(IT24101495)

### Progress
- Malith(IT24101495): US-02 (Manage User Profile) — core feature work: view/update profile endpoint and page

### Challenges
- None logged for this half of the story

### Decisions
- Email/role changes: disallowed via self-service, matching the AC's own either/or — admin-initiated changes covered separately by US-37

---

## 2026-08-24

### Present
Malith(IT24101495)

### Progress
- Malith(IT24101495): US-02 completed and merged

### Challenges
- Docker build blocked by TLS interception (`NU1301`, `PartialChain` certificate error) — diagnosed as antivirus HTTPS scanning, not a Dockerfile defect
- JWT signing key and MySQL connection string not yet consistently externalized

### Decisions
- Local dev secrets: git-ignored `.env` with a committed `.env.example` holding real working values (safe — local-dev-only)
- JWT convention locked in for every future service: issuer `BuildNexusAuth`, audience `BuildNexusServices`, shared signing key via `Jwt__SigningKey`, role claim name `role`

---

## 2026-08-26

### Present
Malith(IT24101495)

### Progress
- Malith(IT24101495): US-03 (Enforce Role-Based Access Control) completed and merged — RBAC enforcement across protected endpoints, plus frontend `RoleRoute` component

### Challenges
- Frontend had no test framework at all until this point — Vitest + Testing Library added as part of finishing this story

### Decisions
- Frontend testing framework: Vitest + Testing Library, not Jest — matches the Vite-based frontend tooling

---

## 2026-08-27

### Present
Siyara(IT24103193)

### Progress
- Siyara(IT24103193): US-25 (API Gateway Routing & Integration) completed and merged — Gateway built using YARP, including a dedicated `api-gateway-tests` project

### Challenges
- Gateway had no way to be booted by a test harness — missing `public partial class Program { }`, same pattern as User Service
- A guessed `/api/documents` route prefix from an earlier lost build was removed rather than kept

### Decisions
- API Gateway implemented with YARP, not Ocelot/Nginx; validates the token and forwards it unchanged, doesn't make authorization decisions itself

---

## 2026-08-28

### Present
Malith(IT24101495), Siyara(IT24103193)

### Progress
- Malith(IT24101495): US-04 (Password Reset) completed and merged
- Siyara(IT24103193): US-28 (Configure CI/CD Pipeline) completed and merged — GitHub Actions workflow for Build + Unit Test stages

### Challenges
- Siyara: original AC described the full five-stage pipeline as if achievable immediately — clarified Sprint 1 scope as Build + Unit Test only; no `.sln` file, so CI needed explicit project paths

### Decisions
- Password reset uses a local-dev mail catcher (Mailpit) for demoable, reliable testing rather than a live Gmail SMTP dependency during demos
- CI/CD scope note added directly to the Jira ticket

---

## 2026-08-30

### Present
Malith(IT24101495)

### Progress
- Malith(IT24101495): US-05 (Create Construction Project) completed and merged — Project Service stood up from scratch, including DbUp migrations and the first real Kafka producer in the project
- Malith(IT24101495): US-06 (View & Update Project Status) started — core feature work: project detail/history view, status-update endpoint with valid-transition enforcement

### Challenges
- Two schema judgment calls US-05's AC didn't specify — `garage_spaces` as an integer count, land size in perches

### Decisions
- Kafka convention locked in for every future publishing service: Confluent.Kafka client, one topic per service, shared envelope `{ eventType, eventId, occurredAt, payload }`
- Status only moves through valid transitions (`Pending → Designing → Design Approved → Construction → Completed`), each change writing a history record

---

## 2026-08-31

### Present
Malith(IT24101495)

### Progress
- Malith(IT24101495): US-06 completed and merged
- Malith(IT24101495): US-22 (Project Event Integration) started — adding `ProjectUpdated`/`ProjectApproved` events on top of the `ProjectCreated` publisher from US-05

### Challenges
- QA caught a real ordering bug in US-06: status history sorted by a whole-second `DATETIME` plus a random GUID tie-break gave a stable but not chronological order. Fixed with a proper `AUTO_INCREMENT` sequence column, numbering existing rows by true order via `ROW_NUMBER()` before attaching `AUTO_INCREMENT`.

### Decisions
- Confirmed the "assigned staff" branch of US-06's access rule stays unreachable until US-07 (Sprint 2) — expected sequencing, not a defect

---

## 2026-09-01

### Present
Malith(IT24101495)

### Progress
- Malith(IT24101495): US-22 completed and merged
- Malith(IT24101495): US-37 (Admin: Manage Users & Roles) completed and merged — plus two additions recorded directly on the Jira ticket (edit capability, pagination) and a partial mitigation for deactivated users' tokens (access token lifetime shortened from 60 to 20 minutes)

### Challenges
- MySqlConnector timezone bug in US-22: timestamps read back come out `Kind=Unspecified`, which `DateTimeOffset` would otherwise interpret using the server's local offset rather than UTC
- Chose a transactional outbox over fire-and-forget for US-22, since silent event loss on broker downtime didn't meet the AC's word "reliably"
- A native MySQL install on the dev machine again held port 3306 during US-37, blocking 18 new integration tests — same root cause as the very first environment setup
- Confirmed a real, accepted limitation in US-37: deactivating a user doesn't revoke their already-issued JWT — same tradeoff already accepted for password reset in US-04

### Decisions
- Outbox pattern accepted for Project Service, verified against a real stopped/restarted broker; Design/Construction/Payment Service will use simple fire-and-forget instead, avoiding the added complexity three more times for marginal benefit
- Full token revocation remains out of scope; shortened token lifetime is the proportionate partial mitigation
- **Outstanding, not yet resolved:** whether this project's extensive use of Claude Code to write actual code is compatible with the assignment's AI usage policy — flagged for the team to raise with the module leader before final submission
