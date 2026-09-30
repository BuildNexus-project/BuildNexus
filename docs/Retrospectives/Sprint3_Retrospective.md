# Sprint 3 Retrospective — BuildNexus

**Sprint dates:** 2026-09-20 to 2026-09-29

**Stories:** SCRUM-25 (US-12), SCRUM-26 (US-13), SCRUM-27 (US-14), SCRUM-28 (US-15), SCRUM-29 (US-16), SCRUM-30 (US-17), SCRUM-32 (US-19), SCRUM-37 (US-24), SCRUM-45 (US-32), SCRUM-54 (US-35c), SCRUM-43 (Integration Testing)

**Delivered:** 11 of 11 planned stories. Points not reportable — see *What Didn't Go Well*.

---

## What Went Well

**Payment Service went from empty folder to complete service in one sprint, by following the existing template rather than inventing one.** US-15 scaffolded it using Construction Service's own shape (DbUp, ADO.NET, JWT convention, deny-by-default, Swagger, /health); US-16 and US-17 then built on it with zero structural decisions re-argued. Four stories, one new service, no new patterns.

**Running the actual application — not just the tests — found bugs the test suite structurally couldn't see, three separate times** A missing .dockerignore broke docker build while CI (which only runs dotnet build) stayed green; a "missing" quotation turned out to be a lost ownership-replica row; and handover only ever worked because of a message someone had typed onto a Kafka topic by hand. None were findable from the test suite alone.

**A long-standing cross-service defect (FinalPaymentSettled never actually firing) was diagnosed forensically and fixed the correct, harder way, not the easy wrong way.** The obvious fix — announce handover-ready at zero balance — would have let projects be handed over while still largely unbilled. The adopted rule waits on two facts (build-complete AND balance-zero), correctly rejecting the tempting shortcut.

**New team conventions were adopted mid-flight rather than fought.** When CI's unit/integration test split broke an in-flight branch, the fix was to adopt the new convention (tag the test class), not work around it — and a generic "add a MySQL container" suggestion for the same failure was correctly rejected since the real cause was a misfiled test.

---

## What Didn't Go Well

**A known defect was flagged three separate times before anyone actually tracked it — and it's still unmerged.** FinalPaymentSettled was raised as a gap during US-15, US-16, and US-17, each time correctly scoped out with "needs its own ticket" — and no ticket was ever raised. It was eventually fixed as unplanned side-work, on a branch still unmerged at sprint close. Handover remains refused for every project in develop.

**A test stand-in quietly became a silent production dependency.** US-14's settlement event was faked with a manual Kafka message as an acknowledged stand-in — reasonable at the time, but it was never marked temporary anywhere beyond a standup note, and remained the only evidence handover worked for the rest of the sprint.

**The same shell mistake destroyed infra/.env twice in one day** (> instead of >>), truncating it again within minutes of being warned. No data was lost, but only by luck — the template's dev passwords happened to match the existing database volumes.

**CI's blind spots keep surfacing reactively, one at a time.** CI still doesn't build Docker images (scheduled Sprint 4), which is exactly why the missing .dockerignore stayed invisible for two stories; and the new unit/integration split broke an in-flight branch with no mechanism prompting a story to re-check conventions merged after its branch was cut.


---

## Action Items for Sprint 4

| # | Action | Owner |
|---|---|---|
| 1 | Merge feature/final-payment-settled — handover is refused for every project until it lands | Dev |
| 2 | Raise a ticket the same day a gap is flagged as "needs its own ticket" — don't just note it and move on | Everyone |
| 3 | Any manual stand-in (hand-placed Kafka message, hand-inserted row) gets a ticket the moment it's created; the story it unblocks isn't "done" until that ticket closes | Everyone |
| 4 | Add a `docker build` smoke step to CI so an uncontainerizable service fails the build  | Dev |
| 5 | After any service database reset, also reset that service's Kafka consumer group offsets | Dev |
| 6 | Never redirect into `infra/.env` with `>`; it is gitignored, so there is no safety net | Everyone |

---

## Velocity Note for Sprint 4 Planning

Sprint 3 delivered a full new service (Payment) in four stories, with each successive story moving faster once the template was set. But a meaningful share of the sprint was unplanned debt-clearing work (FinalPaymentSettled, .dockerignore, the CI trait fix, two .env restorations) — expect this pattern rather than assume it away.

The remaining known gaps are mostly integration-shaped, not feature-shaped: merging the settlement branch, getting Docker builds into CI, and outstanding Sprint 2 deploy work. Lower visibility than a new screen, but this is what separates the system looking like it works from actually working.
