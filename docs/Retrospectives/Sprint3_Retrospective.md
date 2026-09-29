# Sprint 3 Retrospective — BuildNexus

**Sprint dates:** 2026-09-20 to 2026-09-29

**Stories:** SCRUM-26 (US-13), SCRUM-27 (US-14), SCRUM-28 (US-15), SCRUM-29 (US-16), SCRUM-30 (US-17), SCRUM-32 (US-19), SCRUM-43 (Integration Testing)

**Delivered:** 7 of 7 planned stories. Points not reportable — see *What Didn't Go Well*.

---

## What Went Well

**Payment Service went from an empty folder to a complete service in one sprint, by following the template rather than inventing one.** Sprint 2's closing note called `payment-service` "the natural next candidate now that the Design→Construction handoff pattern exists as a template to follow rather than invent again." That is exactly how it played out: US-15 scaffolded the service using the Construction Service's own shape — DbUp migrations, ADO.NET with no ORM, JWT under the shared convention, deny-by-default authorization, Swagger, `/health` — and then US-16 and US-17 built on it without a single structural decision needing to be re-argued. Four stories, one new service, no new patterns. The transactional outbox, the Kafka envelope, and the locally-replicated-ownership pattern were all lifted from existing services and worked first time in their new home.

**Running the actual application found a class of bug the test suite structurally cannot see.** Sprint 1 established "verify against real infrastructure, not a mock" and Sprint 2 kept it. Sprint 3 pushed it one step further — running the whole stack, not just the tests — and it earned its keep three times. Payment Service had shipped with no `.dockerignore`, so `docker build` failed outright with `NETSDK1064`; CI had been green throughout because CI runs `dotnet build`, never `docker build`. The quotation that "didn't show in the Client's UI" turned out to be a missing ownership replica row, not a broken feature. And handover was only ever demonstrable because of a message somebody had typed onto a Kafka topic by hand. None of these were findable from the test suite, and all three were found by starting the app and using it.

**A long-standing cross-service defect was diagnosed forensically and then fixed the harder, correct way.** `FinalPaymentSettled` — the event US-14's handover gate had waited on since it shipped — was traced to a single hand-placed message on the topic, identified by three independent marks: `json.dumps`-style whitespace no .NET serializer emits, `Z`-suffixed timestamps where `DateTimeOffset` produces `+00:00`, and a date on which the outbox CHECK constraint would have rejected the event type outright. The obvious fix — announce when the balance reaches zero — would have been *worse than the gap*, letting a project be handed over while still largely unbilled. The rule adopted instead waits on two facts, build-complete and balance-zero, raised by whichever lands last. That is more work for a correctness reason, chosen deliberately.

**The team's own new conventions were adopted mid-flight rather than fought.** SCRUM-43 split CI into separate unit and integration jobs while US-17 was in progress, requiring every MySQL- or Kafka-touching test class to carry `[Trait("Category", "Integration")]`. The in-flight branch broke, and the fix was to adopt the convention — merge `develop` in and tag the class — rather than to argue with it or work around the infrastructure. A generic diagnosis of that same failure recommended adding a MySQL service container to the workflow; it was correctly rejected, because the workflow already starts MySQL through compose and the tests were simply running in the wrong job.

---

## What Didn't Go Well

**A known defect was flagged across three consecutive stories before anyone acted on it — and it still is not merged.** `FinalPaymentSettled` was raised as a gap during US-15, again during US-16, and again during US-17. Each time it was correctly identified as out of that story's scope, and each time the conclusion was "it needs its own ticket". No ticket was ever raised. It was eventually fixed as an unplanned side piece of work on a branch that, at sprint close, is **still unmerged** — so handover remains refused for every project in `develop`. Repeatedly noticing a problem is not the same as tracking it, and this sprint proved the difference costs real time.

**A test stand-in was allowed to become a silent production dependency.** US-14 shipped in Sprint 3 with AC-4 unverifiable — Payment Service did not exist, so the settlement event was faked with `kafka-console-producer` as an acknowledged stand-in. That was a reasonable call at the time. What went wrong is that the fake was never marked as temporary anywhere outside a standup note, and for the rest of the sprint it remained the *only* evidence handover worked. Anyone reviewing the system would have concluded the feature was finished. A fake that outlives the day it was created needs a ticket attached to it, not a mention.

**The same shell mistake destroyed the environment configuration twice in one day.** `infra/.env` was overwritten with a single line by a redirect using `>` instead of `>>`, wiping all 20 variables and stopping the entire stack from parsing. It was restored from `.env.example` — and then truncated again, by the same mistake, within minutes of being warned about it. No data was lost, because the template's development passwords happen to match what the database volumes were created with, but that was luck rather than design: had they differed, every volume would have needed recreating.

**CI's blind spots keep being discovered reactively, one at a time.** Sprint 2's action item #1 was to write down the rule the first time a CI failure of a given class was hit. That partly held — but the *class* of problem recurred in a new form. CI does not build Docker images (that stage is scheduled for Sprint 4), which is why a missing `.dockerignore` stayed invisible through two stories. And the unit/integration split, itself a CI improvement, silently broke an in-flight branch because nothing prompts a story to re-check CI conventions merged after its branch was cut.

**Sprint 2's action item on story points was not done, so Sprint 3 again cannot report a delivered-points percentage.** `docs/product-backlog.md` is still the three-line placeholder it was at the end of Sprint 2 — "Add your backlog here." Sprint 1 reported 100% of 32 points; Sprint 2 could not; Sprint 3 now cannot either. This is the second sprint in a row that this specific action item has been carried and dropped, which makes it the one most worth either doing or formally abandoning.

---

## Action Items for Sprint 4

| # | Action | Owner |
|---|---|---|
| 1 | Merge `feature/final-payment-settled` — handover is refused for every project until it lands, and it needs its test class tagged `[Trait("Category", "Integration")]` first | Dev |
| 2 | When a story flags a gap as "needs its own ticket", raise the ticket that day — three stories flagged the same defect and none of them created one | Everyone |
| 3 | Any manual stand-in — a hand-placed Kafka message, a hand-inserted database row — gets a ticket attached the moment it is created, and the story it unblocks is not called done until the ticket is closed | Everyone |
| 4 | Add a `docker build` smoke step to CI so a service that cannot be containerised fails the build rather than staying green for two stories | Dev |
| 5 | After any service database reset, reset that service's Kafka consumer group offsets too — `AutoOffsetReset.Earliest` only applies to a brand-new group, so replicated state (`project_owners`, `payment_settlements`, `construction_completions`) silently never rebuilds | Dev |
| 6 | Fill in `docs/product-backlog.md` with real story-point estimates, or formally drop points reporting — carried from Sprint 2 and not done | Everyone |
| 7 | Never redirect into `infra/.env` with `>`; it is gitignored, so there is no version-control safety net | Everyone |

---

## Velocity Note for Sprint 4 Planning

Sprint 3 delivered the full Payment Service — four stories across a service that did not exist at sprint start — plus the construction lifecycle stories that preceded it and the combined report. The template-following approach paid off: the second, third and fourth stories in that service moved noticeably faster than the first, because scaffolding, conventions and test patterns were already settled.

Two things should shape Sprint 4's plan. First, **unplanned work took real time this sprint** — the `FinalPaymentSettled` fix, the `.dockerignore` fix, the CI trait fix and two environment restorations were none of them planned, and together they were a meaningful share of the sprint. Some of that is the cost of finally clearing debt carried from Sprint 2, but it should be expected rather than absorbed silently.

Second, **the remaining known gaps are mostly integration-shaped, not feature-shaped**: merging the settlement branch, getting Docker builds into CI, and the deploy stages still outstanding from Sprint 2's Azure work. These are lower-visibility than a new screen but they are what stands between the system demonstrably working and actually working — the hand-placed Kafka message is the clearest illustration of that distinction this project has produced.
