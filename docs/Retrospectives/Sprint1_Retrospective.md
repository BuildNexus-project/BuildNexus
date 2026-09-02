# Sprint 1 Retrospective — BuildNexus

**Sprint dates:** 2026-08-20 to 2026-09-01 (per actual Jira completion timestamps)
**Delivered:** 10 of 10 planned stories, 42 of 42 planned points (100%)
**Stories:** US-01, US-02, US-03, US-04, US-05, US-06, US-22, US-25, US-28, US-37

---

## What Went Well

**Verification discipline, once adopted, kept paying for itself.** The turning point was US-01: a MySqlConnector-CHAR(36)-to-Guid mismatch shipped past automated tests because those tests ran against a fake repository, not real MySQL. From that point on, "verified against real infrastructure, not a mock" became a standing rule — and it kept finding real bugs the rest of the sprint: the status-history ordering bug in US-06 (a whole-second `DATETIME` plus a random GUID tie-break gave a stable but non-chronological order), a MySqlConnector timezone bug in US-22 (`DateTimeOffset` misreading an `Unspecified`-kind timestamp), and the outbox pattern in US-22 was proven against an actually-stopped-and-restarted Kafka broker, not just asserted to work. This wasn't a one-time lesson — it was a discipline that kept catching things a full sprint later.

**Shared conventions, decided once, held up under reuse.** JWT signing/claims convention, the Kafka topic/envelope convention, target framework (net10.0), and the YARP decision for the Gateway were all settled during Sprint 1 and then correctly inherited by every story that needed them afterward, without needing to be re-derived or drifting between services. That's the intended payoff of deciding these early, and it worked.

**Real architectural tradeoffs got made deliberately, not by default.** The outbox-vs-fire-and-forget decision on US-22 involved genuine debate — a first proposal, a follow-up question about whether it matched what was actually agreed, and a final decision made with the tradeoff (head-of-line blocking) explicitly understood and documented, not glossed over.

**Gaps found after implementation got fixed without destabilizing the finalized backlog.** US-37 alone picked up two real additions post-implementation (edit capability, pagination) plus a related security fix (the Admin self-registration hole) — all tracked on the live Jira ticket rather than either being lost or forcing a rewrite of the graded backlog document.

---

## What Didn't Go Well

**The same environment problem happened twice.** A native MySQL install held port 3306 during the very first setup (US-01), and the identical issue recurred during US-37 — a second team member's machine, same root cause, blocking 18 integration tests. The fix (stop the conflicting service, set it to Manual) was known after the first occurrence but wasn't turned into something the team checked proactively before it cost time a second time.

**The migration approach started wrong and had to be corrected mid-sprint.** Local database schema was initially handled via `docker-entrypoint-initdb.d` scripts, which only run once, on an empty volume — meaning a second schema change would have silently failed to apply on any database that already existed. This was caught and replaced with DbUp (proper migration tracking via a `schemaversions` table) before it caused real damage, but it was scope that had to be redone, not scope that was avoided.

**Role rotation didn't happen — only Dev and DevOps ran this sprint.** This was a deliberate, reasonable simplification made early on to keep Sprint 1 moving. The cost: two of four team members have no individually-attributable Sprint 1 contribution yet, against a rubric that grades role-specific contribution per person. This needs an explicit decision for Sprint 2, not another default extension of the same simplification.

**Several AC gaps were only caught after a story was already being implemented, not during backlog grooming.** The Admin self-registration hole, US-37's missing edit/pagination coverage, and the CI/CD pipeline's originally-unscoped five-stage AC were all found reactively, mid-build. With no BA role active this sprint, nothing was reviewing acceptance criteria for completeness before implementation started — a direct consequence of the role-rotation gap above, not a separate issue.

**A real compliance question sat unaddressed for the whole sprint.** Whether this project's extensive use of Claude Code to write actual code is compatible with the assignment's AI usage policy was only surfaced near the end of Sprint 1, even though it's been true since the first commit on Day 1. The longer it goes unresolved, the more code exists that may need to be reconsidered.

---

## Action Items for Sprint 2

| # | Action | Owner | Tracked |
|---|---|---|---|
| 1 | Raise the AI usage policy question with the module leader | Team | Not yet actioned — highest priority item carried over from the submission checklist |
| 2 | Decide Sprint 2's actual role assignments — at minimum get a QA pass running again; if BA/QA stay deferred again, that needs to be a stated decision, not a silent repeat | Team | This document |
| 3 | Before starting each new service, proactively check for and stop conflicting local services (port 3306, etc.) rather than discovering it mid-story | Whoever starts Design Service next | Worth adding to the shared Dev prompt's new-service checklist |
| 4 | Every new service uses DbUp migrations from first commit — not raw init scripts | Whoever starts Design Service next | Already in the shared Dev prompt's item 6 |
| 5 | Keep "verify against real infrastructure before calling a story done" as a hard rule, not a one-time lesson | Everyone | Already standing practice; this just confirms it stays that way |
| 6 | Double-check MySqlConnector's actual returned CLR type for DATETIME/CHAR columns against what the code assumes, rather than trusting the obvious guess — two independent bugs from this exact pattern in one sprint | Whoever writes DB-facing code next | Worth a line in the shared Dev prompt if a third instance shows up |

---

## Velocity Note for Sprint 2 Planning

Sprint 1 delivered 100% of its planned 42 points. Sprint 2 is currently planned at 40 points — a comparable load, on paper. Worth watching closely given the added, non-trivial complexity in the plan: the Design Service introduces file/document handling for the first time, and any Azure deployment work pulled forward into this sprint (User Service, Project Service) is genuinely new scope with no direct Sprint 1 precedent to estimate against.
