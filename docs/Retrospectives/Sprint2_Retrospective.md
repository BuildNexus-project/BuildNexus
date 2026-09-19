# Sprint 2 Retrospective — BuildNexus

**Sprint dates:** 2026-09-03 to 2026-09-14
 
**Stories:** SCRUM-20 (US-07), SCRUM-21 (US-08), SCRUM-22 (US-09), SCRUM-23 (US-10), SCRUM-24 (US-11), SCRUM-33 (US-20), SCRUM-36 (US-23), SCRUM-42 (US-29), SCRUM-46 (US-33), SCRUM-52 (US-35), SCRUM-36 (US-23), SCRUM-53 (US-35)

**Delivered:** 12 of 12 planned stories, 30 of 30 planned points (100%)

---

## What Went Well

**Full vertical slices, same-day, became the norm rather than the exception.** US-07, US-08, US-09, US-10, US-11, US-20 and US-23 each went DB/migration → repository → business logic → controller → (Kafka, where the story needed it) → frontend API client → frontend UI → tests, in the commit order the team settled on, without leaving a layer for "later." Several — US-08, US-09, US-10 — landed complete within a single calendar day; 2026-09-10 alone completed three stories (US-11, US-20, and US-23, the last of which stood up an entirely new backend service).

**Functional testing kept catching what unit tests structurally cannot — the exact lesson Sprint 1 drew from the Guid bug, and it held up a sprint later.** US-11's `ListForProjectAsync` was silently leaving `reviewed_by`/`reviewed_at`/`review_comment` out of its own `SELECT`, so the documents listing kept showing a decision as blank even after it had been recorded — a real, waiting-to-ship bug that every controller unit test missed, because the in-memory fake repository mutates the same object a later read returns and so can never expose a real SQL projection defect. Only a genuine end-to-end run against MySQL surfaced it. Found and fixed the same day, with a regression test that runs against real MySQL in CI.

**Genuine product-scope questions got surfaced and decided explicitly, not guessed past.** Three real forks came up this sprint — US-23's placeholder granularity (per-project vs per-document), its frontend scope (backend-only), and which events to publish; US-29's discovery that `payment-service` has no code and therefore nothing to unit-test; US-33's coverage target and whether to enforce it as a CI gate — and every one of them was put to the team and confirmed before a line of code was written, rather than assumed.

**The Construction Service — the platform's fourth backend service, and the codebase's first Kafka *consumer* — was verified against a real, running broker, not asserted to work.** Real `DesignApproved` events were published and watched: a placeholder got created, a redelivered duplicate was absorbed without a second row, a malformed message was logged and skipped rather than blocking the partition, an unrelated event type on the same topic was silently ignored, and the consumer stayed up through all of it.

**US-29 resisted padding.** Before adding a single test, the existing suites were audited rather than assumed thin. Validation and status-transition logic turned out to already be well covered from earlier stories' test-first habit, so the story added tests only where a real, previously-uncovered gap existed — password hashing, the role-validation attribute, five frontend `lib/` modules — instead of duplicating coverage that already existed to hit a number.

**Migration discipline held under real pressure to cut corners.** design-service picked up three additive migrations this sprint (`002` for review statuses, `003` for the review-decision columns, `005` for the widened outbox) and not one of them touched an already-shipped script — directly following through on Sprint 1's own action item on this exact point.

## What Didn't Go Well

**The compose-whole-file-interpolation CI trap recurred three times before it was named as a pattern instead of re-diagnosed from scratch each time.** `docker compose up <named services>` parses the *entire* file before starting anything, so a required `${VAR:?...}` on *any* service block — even one a given CI job never starts — has to be set in that job's environment. It bit US-09 (`DESIGN_DB_*`), then US-11 (`InternalService__ApiKey`), then US-23 (`CONSTRUCTION_DB_*`) — the same class of "the same problem happened twice [in this case, three times]" Sprint 1's retrospective already flagged about port 3306.

**A schema gap was found mid-story rather than during grooming, again.** US-10 needed `UnderReview`/`Approved` on `design_document_versions` for its "current version" rule, but the original `001` migration only allowed `Submitted` — caught and corrected with an additive migration before anything shipped, but it's the same "AC implies a future value the schema doesn't have yet" pattern Sprint 1 named as a recurring risk, this time on a different service.

**Local environment friction cost real time again, this time Docker Desktop rather than a port conflict.** Verification runs against the containerized stack repeatedly needed Docker Desktop restarted and polled ready before a test container would even start — not a code defect, but the same shape of "known fix, rediscovered each time" as Sprint 1's MySQL-on-3306 issue.

## Action Items for Sprint 3

| # | Action | Owner |
|---|---|---|
| 1 | The first time a CI `${VAR:?...}` compose-interpolation failure is hit, write the rule down immediately — a comment in `ci.yml` alone didn't stop it recurring twice more this sprint | Dev |
| 2 | Before starting a verification pass, confirm Docker Desktop is actually running rather than discovering it from the first failed container command | Dev |
| 3 | When a story's AC implies a status/enum value the schema doesn't have yet (a "current version" rule, a new decision outcome), check the schema against every value the story will need before writing the first migration for it | Dev |
| 4 | Fill in `docs/product-backlog.md` with real story-point estimates so a future retrospective can report a delivered-points percentage the way Sprint 1's did | Everyone |
| 5 | Keep confirming genuine product-scope forks with the team before building rather than guessing — this didn't fail once this sprint; worth keeping deliberately rather than by luck | Everyone |

## Note for Sprint 3 Planning

Sprint 2 added a fourth backend service (Construction) and the codebase's first Kafka consumer, closed out the Design Service's full review lifecycle (upload → list → approve/request-revision → report → downstream event), and opened a new, mostly-independent stream of work — deploying services to Azure (SCRUM-52 merged for User Service; SCRUM-53 for Project Service in progress). `payment-service` remains entirely unbuilt: no code, no tests, no coverage number, and is the natural next candidate now that the Design→Construction handoff pattern (Kafka consumer, idempotent placeholder, decoupled failure handling) exists as a template to follow rather than invent again.
