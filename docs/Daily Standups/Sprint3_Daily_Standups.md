# Sprint 3 Daily Standups — BuildNexus

---

## 2026-09-20

### Present
- Siyara(IT24103193)

### Progress
- Siyara(IT24103193): US-13 (View Construction Progress) completed and merged (PR #32) — Client-only progress endpoint and dashboard, with project ownership replicated locally from project-events rather than querying Project Service directly

### Challenges
- Frontend CI caught a type error only npm run build surfaces (Vitest/oxlint don't type-check) — always run a full build before pushing, not just tests

### Decisions
- Ownership replicated from ProjectCreated, not queried live — no cross-service call on a read path
- Live updates handled by polling (on mount, on tab visibility, every 30s), not push — no push transport exists yet

---

## 2026-09-22

### Present
- Siyara(IT24103193)

### Progress
- Siyara(IT24103193): US-14 (Start, Complete & Hand Over Construction) completed and merged (PR #36) — all three gated transitions, Construction Service's first Kafka producer, and a ConstructionEventsConsumer added to Project Service so it reflects ConstructionStarted

### Challenges
- AC-4 (handover gated on payment) couldn't be tested end to end — Payment Service didn't exist yet, so the settlement event was faked via kafka-console-producer as a stand-in

### Decisions
- Handover fails closed: no settlement marker means refused, by design
- Handover itself publishes no event — the two named events already cover the transition

---

## 2026-09-25

### Present
- Siyara(IT24103193)

### Progress
- Siyara(IT24103193): US-15 (Generate Quotation & Invoice) completed and merged (PR #34) — Payment Service scaffolded from scratch, quotations/invoices, automatic invoicing on ConstructionStarted
- US-14 follow-up fixes merged (PR #35)
- US-16 (Record Payment) started

### Challenges
- Payment Service was missing a .dockerignore, breaking docker build — invisible to CI, which only runs dotnet build
- Two Kafka consumers in one service sharing a consumer group ID were silently stealing each other's partitions — fixed by suffixing the group per topic

### Decisions
- Automatic invoice triggers once on ConstructionStarted, billing the current quotation total — a project with no quotation is skipped, not guessed at

---

## 2026-09-26

### Present
- Siyara(IT24103193)

### Progress
- Siyara(IT24103193): US-16 (Record Payment) completed and merged (PR #36) — payment endpoint with ownership check, Payment Service's first Kafka producer
- US-17 (View Payment History) started
- Found and fixed the reason handover had never actually worked: Payment Service now publishes FinalPaymentSettled, the event US-14's handover gate was always waiting on

### Challenges
- Handover had only ever "worked" once, because someone had manually placed a fake event on the Kafka topic by hand weeks earlier — nothing in the codebase actually produced it

### Decisions
- FinalPaymentSettled fires when both construction is complete and the balance is zero, whichever happens last
- A never-billed project is announced settled on completion, so handover isn't blocked forever

--- 

## 2026-09-27

### Present
- Siyara(IT24103193), Rayan(IT24100312)

### Progress
- Siyara(IT24103193): US-17 (View Payment History & Outstanding Balance) completed and merged (PR #38)
- US-19 (Construction and Payment Report) started

### Challenges
- CI briefly failed on US-17 because a new test class predated a just-merged convention (tagging DB-touching tests as Integration) — fixed by tagging it correctly, not by changing infrastructure

### Decisions
- Outstanding balance is always derived live, never stored, so it can never disagree with what the payment endpoint will actually accept

---

## 2026-09-28

### Present
- Siyara(IT24103193), Rayan(IT24100312)

### Progress
- Siyara(IT24103193): US-19 completed and merged (PR #40) — combined construction/payment report as two tabs of one page
- it24100312: role-aware app shell, dashboard, and report nav entry merged (PR #39)

### Challenges
- infra/.env got wiped by an accidental > overwrite instead of >> — restored from .env.example, no data lost
- An older project's quotation was invisible to its Client because Payment Service's local ownership replica never got rebuilt after a database reset (Kafka offsets weren't reset too)

### Decisions
- New rule: any service replicating another's data needs "reset the consumer group after a DB reset" documented in its runbook — logged as a known characteristic of the pattern, not a bug


--- 

## 2026-09-29

### Present
- Siyara(IT24103193)

### Progress
- Siyara(IT24103193): US-19 follow-up — report now shows project names instead of raw ids, and states its own definition of "active" on the page

### Decisions
- "Active" means: design approved, milestones planned, not yet handed over — pulled directly from the query, not restated by hand
- Flagged (not fixed): this reflects construction-phase status, not the project's own status — a project marked Completed still shows until handover
