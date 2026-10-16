# Unit testing and mocking

How the Payment and Construction services are unit tested with **Moq**, why each kind of test double was chosen, which tests were added, and a 2-minute script for showing them live. Part of the SE3112 testing pipeline (see "Test automation pipeline" in the root [README](../README.md)).

## In one paragraph

A unit test checks one piece of logic on its own. The controllers, the event consumers and the outbox dispatcher all talk to something outside themselves: a repository (the database) or the message broker (Kafka). In a unit test those outside things are replaced by **test doubles**, so the test is fast, needs no Docker, and fails only when the logic is wrong. Before this work the doubles were hand-written classes (`Fake*.cs`). Moq adds a second kind: doubles we can **check afterwards** ("was this called once, with these values, or never?"). Both kinds are kept.

## The three kinds of double

| Double | What it is | In this repo | Use it when |
|---|---|---|---|
| **Stub** | A stand-in that gives a fixed answer. We do not check how it was used. | `Setup(...).ReturnsAsync(...)` in Moq | The code under test needs an answer to carry on: "this project has a quotation of 900,000". |
| **Mock** | A stand-in we **check afterwards**: was it called, how many times, with which values. | `Verify(..., Times.Once)` or `Times.Never` in Moq | The thing that matters is the **call itself**: "the payment was recorded once, for this client" or "the repository was never touched". |
| **Fake** | A hand-written stand-in that really works, in a small way (like a tiny in-memory table). | The existing `Fake*.cs` files, for example `FakeInvoiceRepository` | The thing that matters is the **state afterwards**: "after two payments the balance is 600". |

A Moq `Mock<T>` plays both of the first two roles. The `Setup` line makes it a stub, and the `Verify` line makes it a mock. One object, two jobs.

### Why each one was chosen

| Code under test | Double used | Why |
|---|---|---|
| **Controllers** (payment, invoice, quotation, construction phase, milestone) | Repositories as **stub + mock** | The controller's job is to decide what to ask the repository, and whether to ask at all. A stub gives the answer, and `Verify` proves the call was made once with the right ids. `Times.Never` proves nothing was stored when the request was invalid, forbidden or had no subject. A fake cannot easily prove a call did *not* happen. |
| **Outbox dispatcher** | Outbox and broker as **stub + mock** | The rule is about order and consequences: mark a row published only *after* the broker accepted it, mark it failed *after* it refused, and stop at the first failure. Stubbing the broker to throw (broker down) and verifying which `Mark...` call followed is the direct way to test that. |
| **Event consumers** (Construction events, Project events, Payment events, Design events) | Repositories as **stub + mock** | A consumer reads one message and makes **one** decision. A good message must cause exactly one call with the ids from the message. A bad, duplicate or unrelated message must cause no call. `Verify` states each of those in one line. |
| **Request validation** (amount rules, milestone name rule) | **No double** | These are attributes (`[Range]`, `[Required]`). There is nothing outside to replace, so the test runs the real attributes through the same `Validator` ASP.NET uses. |
| **State that builds up** (balance after several payments) | **Fake** (existing tests, left as they were) | A fake remembers. A mock does not. The old tests already cover this well, so the new ones do not repeat it. |

### What is never mocked

- The class being tested (`OutboxDispatcher`, the consumers and the controllers are the real classes).
- The SQL itself. The repositories are replaced, so the SQL is not run here. The database tests tagged `Category=Integration` cover that, and they are excluded from the unit run.

## What was added

Moq `4.20.72` was added to **`PaymentService.Tests`** and **`ConstructionService.Tests`** only. No production project changed, and no existing test or fake was modified.

| Service | New tests | Total unit tests (was, now) | `[Theory]` methods |
|---|---|---|---|
| Payment | 103 | 149, then 252 | 17 |
| Construction | 83 | 180, then 263 | 14 |
| **Both** | **186** | | **31** |

The brief asked for at least 5 theories using both `[InlineData]` and `[MemberData]`. There are 31 theory methods, and both attributes are used in both services.

The new tests live in files named `*MockTests.cs`, plus two `*ValidationTests.cs` files. The names follow the existing style: a plain sentence with underscores, such as `A_client_who_does_not_own_the_project_never_reaches_the_payment_repository`.

### Edge cases covered

| Edge case | Where |
|---|---|
| **Zero, negative and tiny amounts** (0, -0.01, -1, 0.001) | `RequestAmountValidationTests` |
| **Boundaries** (0.01 accepted, 0.00 refused, 9,999,999,999,999.99 accepted, 9,999,999,999,999.995 refused; milestone name of 0, 1, 150 and 151 characters) | `RequestAmountValidationTests`, `CreateMilestoneRequestValidationTests` |
| **Largest and smallest amounts pass through unchanged** | `The_amount_reaches_the_repository_exactly_as...` (payment, invoice, quotation), `The_invoice_amount_is_always_the_quotation_total` |
| **Duplicate events** (repository says "already done", and the same message twice) | `...is_absorbed_not_an_error`, `The_same_message_delivered_twice_...` in all four consumer test classes |
| **Invalid status transitions** (start twice, complete before starting, hand over unpaid, hand over twice, pay an already-paid invoice, overpay) | `A_transition_the_project_is_not_ready_for_is_a_409_naming_the_reason` (10 cases), `A_payment_the_invoice_cannot_take_is_a_409_naming_the_reason` |
| **Failure paths where a call must never happen** | Every `Times.Never` and `VerifyNoOtherCalls()` below |
| **A broker that is down, and a database that is down** | Outbox dispatcher tests, `A_failing_..._write_surfaces_as_something_other_than_a_json_failure` in the consumer tests |

## How to read one test

From `ConstructionEventsConsumerMockTests`:

```csharp
StubQuotation(1_100_000m);          // STUB: the project's current quotation is 1,100,000
StubInvoiceRaised();                // STUB: the repository will create an invoice

await HandleAsync(ConstructionStarted());   // run the real consumer on one message

_invoices.Verify(                   // MOCK: check the repository call
    i => i.CreateFromEventIfAbsentAsync(
        ProjectId, 1_100_000m, StartedBy, EventId, It.IsAny<CancellationToken>()),
    Times.Once);                    // exactly once, with these exact values
```

- `Setup` or the `Stub...` helper: "when asked, answer this".
- The `await` line: run the **real** code under test.
- `Verify(..., Times.Once)`: "it must have been called exactly once with these values".
- `Verify(..., Times.Never)`: "it must **not** have been called".
- `VerifyNoOtherCalls()`: "nothing else was touched at all".
- `It.IsAny<CancellationToken>()`: "I do not care which cancellation token".

### Theory tests

A `[Theory]` is one test method run many times with different inputs.

- `[InlineData(...)]` puts the inputs right on the method. Use it for small, simple values such as strings, enums and ints.
- `[MemberData(nameof(...))]` takes the inputs from a property. Use it when the values do not fit in an attribute. For example, a `decimal` such as `9_999_999_999_999.99m` cannot be written in `[InlineData]`.

## The new tests

Counts in brackets are the number of cases a theory runs. "Stub + mock" means the repository is stubbed for its answer and verified for the calls.

### Payment Service

#### `ClientPaymentsControllerMockTests` (13)

| Test | Double | What it proves |
|---|---|---|
| `A_payment_on_an_owned_invoice_is_recorded_once_for_the_caller` | Stub + mock | Payment recorded exactly once, for the caller, against the route invoice; 201 with new balance |
| `The_amount_reaches_the_repository_exactly_as_the_client_sent_it` (5, MemberData) | Stub + mock | 0.01, 1, 999.99, 250,000.75 and the 9,999,999,999,999.99 maximum are passed on unchanged |
| `A_client_who_does_not_own_the_project_never_reaches_the_payment_repository` | Stub + mock | 403, and `RecordPaymentAsync` is `Times.Never` |
| `An_unknown_invoice_is_refused_without_asking_who_owns_it_or_paying_it` | Stub + mock | 403 identical to "not yours"; no ownership check and no payment |
| `An_invalid_amount_is_refused_before_any_repository_is_touched` | Mock | Bad input stops at the door; no repository is called |
| `A_token_without_a_usable_subject_is_unauthorized_and_nothing_is_looked_up` | Mock | 401 and no repository is called |
| `An_invoice_deleted_between_the_two_reads_is_refused_like_one_that_is_not_theirs` | Stub | A "not found" from the repository becomes the same 403 |
| `A_payment_the_invoice_cannot_take_is_a_409_naming_the_reason` (2, InlineData) | Stub | Overpaying a pending invoice and paying a paid one both give 409 with the right `reason` and outstanding amount |

#### `InvoicesControllerMockTests` (9)

| Test | Double | What it proves |
|---|---|---|
| `A_generated_invoice_is_created_once_for_the_route_project_and_the_caller` | Stub + mock | `CreateAsync` once with project, amount, caller; response is Pending |
| `The_amount_reaches_the_repository_exactly_as_it_was_typed` (5, MemberData) | Stub + mock | Boundary amounts pass through unchanged |
| `An_invalid_amount_is_refused_and_nothing_is_stored` | Mock | `CreateAsync` is `Times.Never` |
| `A_token_without_a_usable_subject_cannot_raise_an_invoice` | Mock | 401; nothing called |
| `A_project_that_has_not_been_billed_lists_as_an_empty_list_not_an_error` | Stub + mock | Empty list gives 200, not an error |

#### `QuotationsControllerMockTests` (10)

| Test | Double | What it proves |
|---|---|---|
| `A_generated_quotation_is_created_once_for_the_route_project_and_the_caller` | Stub + mock | `CreateAsync` once with project, total, caller |
| `The_estimated_total_reaches_the_repository_exactly_as_it_was_typed` (5, MemberData) | Stub + mock | Boundary totals pass through unchanged |
| `Re_quoting_a_project_adds_a_second_quotation_and_never_changes_the_first` | Stub + mock | Two quotes mean exactly 2 `CreateAsync` calls and **no other call** (nothing updated or deleted) |
| `An_invalid_total_is_refused_and_nothing_is_stored` | Mock | Repository not touched |
| `A_token_without_a_usable_subject_cannot_raise_a_quotation` | Mock | 401; repository not touched |
| `A_project_that_was_never_quoted_lists_as_an_empty_list_not_an_error` | Stub | Empty list gives 200 |

#### `RequestAmountValidationTests` (14, no doubles)

| Test | What it proves |
|---|---|
| `A_zero_negative_or_oversized_amount_is_refused_on_every_money_request` (7, MemberData) | 0, -0.01, -1, -250,000, 0.001, 9,999,999,999,999.995 and 10,000,000,000,000 are refused on the invoice, quotation and payment requests |
| `A_positive_amount_up_to_the_column_limit_is_accepted_on_every_money_request` (5, MemberData) | 0.01, 0.02, 1, 250,000.75 and the maximum are accepted on all three |
| `The_smallest_amount_allowed_is_one_cent` (2, InlineData) | The lower boundary: 0.01 in, 0.00 out |

#### `OutboxDispatcherMockTests` (14)

| Test | Double | What it proves |
|---|---|---|
| `A_pending_event_is_sent_once_and_then_marked_published_once` | Stub + mock | Published once; `MarkPublishedAsync` once with a UTC time; `MarkFailedAsync` never |
| `An_event_the_broker_refuses_is_marked_failed_and_never_marked_published` | Stub + mock | A broker that throws leads to `MarkFailedAsync` with the message, and `MarkPublishedAsync` is `Times.Never` |
| `The_failure_recorded_includes_the_inner_message_when_there_is_one` (2, InlineData) | Stub + mock | The recorded error is `outer` or `outer (inner)` |
| `The_batch_stops_at_the_first_refused_event_and_never_reaches_the_one_behind_it` | Stub + mock | Event 1 sent, event 2 fails, event 3 is never published or marked |
| `Every_pending_event_is_sent_once_and_marked_once_and_an_empty_outbox_sends_nothing` (4, MemberData) | Stub + mock | 0, 1, 3 and 7 pending events give exactly that many sends and marks |
| `The_outbox_is_asked_for_exactly_the_configured_batch_size` (4, InlineData) | Stub + mock | Batch sizes 1, 4, 50 and 500 reach `ListPendingAsync` unchanged |
| `A_database_failure_on_one_pass_does_not_stop_the_dispatcher` | Stub + mock | The first read throws, and the next pass still delivers the event |

#### `ConstructionEventsConsumerMockTests` (25)

| Test | Double | What it proves |
|---|---|---|
| `A_ConstructionStarted_bills_the_current_quotation_once_with_the_causing_event_id` | Stub + mock | One invoice call with project, total, StartedBy and the event id |
| `The_invoice_amount_is_always_the_quotation_total` (5, MemberData) | Stub + mock | Totals from 0.01 to the maximum are billed exactly |
| `A_project_with_no_quotation_is_skipped_without_failing_and_nothing_is_billed` | Stub + mock | No exception; the invoice repository is not touched |
| `A_ConstructionStarted_the_repository_has_already_billed_is_absorbed_not_an_error` | Stub + mock | Duplicate event: the repository answers "nothing new", and no error |
| `The_same_message_delivered_twice_reaches_the_repository_twice_with_the_same_event_id` | Stub + mock | The consumer passes the same event id both times, so the unique index can spot the repeat |
| `A_ConstructionStarted_missing_or_zeroing_an_id_is_a_json_failure_and_touches_nothing` (5, MemberData) | Mock | Missing or all-zero ids give a JSON failure and no repository call |
| `An_envelope_with_no_event_id_is_refused_before_any_repository_is_asked` | Mock | No event id means no billing |
| `A_failing_invoice_write_surfaces_as_something_other_than_a_json_failure` | Stub | A database error is retried, not skipped |
| `Any_other_event_type_is_ignored_and_touches_nothing` (4, InlineData) | Mock | Other types, and the right name in the wrong case, change nothing |
| `A_ConstructionCompleted_records_the_completion_once_and_never_raises_an_invoice` | Stub + mock | Completion recorded once with the UTC time; no invoice, no quotation read |
| `A_ConstructionCompleted_is_handled_the_same_whether_or_not_it_announced_the_settlement` (2, InlineData) | Stub + mock | Announced true or false: neither is an error |
| `A_ConstructionCompleted_with_a_zero_project_id_is_a_json_failure_and_records_nothing` | Mock | Bad completion is rejected and nothing is written |
| `A_failing_completion_write_surfaces_as_something_other_than_a_json_failure` | Stub | A database error is retried |

#### `ProjectEventsConsumerMockTests` (18)

| Test | Double | What it proves |
|---|---|---|
| `A_ProjectCreated_records_the_owner_once_with_the_ids_from_the_message` | Stub + mock | `RecordOwnerIfAbsentAsync` once with project and client |
| `A_ProjectCreated_for_a_project_that_already_has_an_owner_is_absorbed_not_an_error` | Stub + mock | Duplicate event: no error |
| `The_same_message_delivered_twice_asks_the_repository_twice_with_the_same_ids` | Stub + mock | Same ids passed both times |
| `Any_other_event_type_is_ignored_and_the_repository_is_never_called` (4, InlineData) | Mock | Other types, and `projectcreated` in the wrong case, are ignored |
| `A_ProjectCreated_missing_or_zeroing_an_id_is_a_json_failure_and_records_nothing` (5, MemberData) | Mock | Missing or zero ids: JSON failure, nothing recorded |
| `A_body_that_will_never_parse_is_a_json_failure_and_records_nothing` (4, InlineData) | Mock | Empty, blank, garbage and `null` bodies: JSON failure |
| `A_ProjectCreated_whose_payload_is_not_an_object_is_a_json_failure_and_records_nothing` | Mock | A text payload is rejected |
| `A_failing_owner_write_surfaces_as_something_other_than_a_json_failure` | Stub | A database error is retried |

### Construction Service

#### `ConstructionPhaseControllerMockTests` (18)

| Test | Double | What it proves |
|---|---|---|
| `A_transition_that_succeeds_is_a_200_and_asks_the_repository_once_for_the_caller` (3, InlineData) | Stub + mock | Start, complete and hand over each ask the repository once, for the caller, and give 200 |
| `A_transition_the_project_is_not_ready_for_is_a_409_naming_the_reason` (10, InlineData) | Stub + mock | **Every invalid transition**: not approved, no milestones, already started, not started, milestones incomplete, already completed, not completed, final payment not settled, already handed over. Each gives 409 with its `reason`, one repository call, nothing else |
| `A_token_without_a_usable_subject_is_unauthorized_and_the_repository_is_never_asked` (3, InlineData) | Mock | 401 for each transition; repository untouched |
| `An_outcome_with_no_message_defined_fails_loudly_instead_of_answering_with_an_empty_problem` | Stub | An outcome added later without a message throws instead of returning a blank error |
| `A_project_whose_build_has_not_started_has_no_phase_to_read_and_is_a_404` | Stub + mock | Reading a project with no phase gives 404 |

#### `MilestonesControllerMockTests` (21)

| Test | Double | What it proves |
|---|---|---|
| `A_milestone_is_created_once_with_its_name_trimmed` (4, InlineData) | Stub + mock | Spaces around a name are removed before it is stored; inner spaces are kept |
| `A_milestone_for_a_project_whose_design_is_not_approved_is_a_400` | Stub | A `null` from the repository gives a specific 400 |
| `A_milestone_name_already_used_on_the_project_is_a_409_naming_it` | Stub | The repository's duplicate exception gives 409 that names the milestone |
| `An_invalid_milestone_is_refused_before_the_repository_is_touched` | Mock | Repository untouched |
| `The_template_creates_the_canonical_milestones_once_for_the_route_project` / `The_template_for_a_project_whose_design_is_not_approved_is_a_400` | Stub + mock | Template is passed once; a missing design gives 400 |
| `Each_status_reaches_the_repository_exactly_as_requested` (3, InlineData) | Stub + mock | NotStarted, InProgress and Completed each reach the repository unchanged |
| `A_status_change_on_an_unknown_milestone_is_a_404` | Stub | Unknown milestone gives 404 |
| `A_status_change_with_no_status_is_refused_and_nothing_is_demoted` | Mock | No status means `UpdateStatusAsync` is `Times.Never`, so a Completed milestone cannot be quietly reset |
| `A_due_date_reaches_the_repository_as_given_and_null_clears_it` (4, MemberData) | Stub + mock | `null`, a normal date, `DateOnly.MinValue` and `DateOnly.MaxValue` pass through |
| `A_due_date_on_an_unknown_milestone_is_a_404` | Stub | Unknown milestone gives 404 |
| `Progress_for_a_project_with_no_milestone_plan_is_a_404_not_zero_percent` / `Progress_is_reported_as_the_repository_computed_it` | Stub | No plan gives 404 and not "0%"; a plan gives the figures as computed |

#### `CreateMilestoneRequestValidationTests` (11, no doubles)

| Test | What it proves |
|---|---|
| `A_name_is_accepted_from_one_to_150_characters_and_refused_outside_that` (7, InlineData) | Lengths 0 and 151 refused; 1, 2, 149 and 150 accepted; 1000 refused |
| `A_name_made_only_of_spaces_is_refused` (4, InlineData) | Space, spaces, tab and newline are refused, so trimming can never produce an empty name |

#### `PaymentEventsConsumerMockTests` (15)

| Test | Double | What it proves |
|---|---|---|
| `A_FinalPaymentSettled_records_the_settlement_once_with_the_ids_and_time_from_the_message` | Stub + mock | One settlement call with project, event id and UTC time |
| `A_FinalPaymentSettled_for_a_project_already_settled_is_absorbed_not_an_error` | Stub + mock | Duplicate event: no error |
| `The_same_message_delivered_twice_asks_the_repository_twice_with_the_same_event_id` | Stub + mock | Same event id both times |
| `Anything_that_is_not_the_final_payment_is_ignored_and_never_marks_a_project_settled` (4, InlineData) | Mock | A part payment (`PaymentSettled`), `PaymentReceived`, `InvoiceGenerated` and a wrong-case name never mark a project settled, so a project cannot be handed over half paid |
| `A_FinalPaymentSettled_missing_its_project_is_a_json_failure_and_records_nothing` (3, MemberData) | Mock | Missing or zero project id: rejected |
| `A_body_that_will_never_parse_is_a_json_failure_and_records_nothing` (4, InlineData) | Mock | Unparseable bodies: rejected |
| `A_failing_settlement_write_surfaces_as_something_other_than_a_json_failure` | Stub | A database error is retried |

#### `DesignEventsConsumerMockTests` (18)

| Test | Double | What it proves |
|---|---|---|
| `A_DesignApproved_creates_the_placeholder_once_with_the_ids_and_time_from_the_message` | Stub + mock | One placeholder call with project, document, event id and UTC time |
| `A_DesignApproved_for_a_project_that_already_has_a_placeholder_is_absorbed_not_an_error` | Stub + mock | Duplicate event: no error |
| `The_same_message_delivered_twice_asks_the_repository_twice_with_the_same_event_id` | Stub + mock | Same event id both times |
| `Any_other_event_type_is_ignored_and_the_repository_is_never_called` (4, InlineData) | Mock | `DesignSubmitted`, `DesignRevisionRequested`, `ProjectCreated` and wrong-case names are ignored |
| `A_DesignApproved_missing_or_zeroing_an_id_is_a_json_failure_and_creates_nothing` (5, MemberData) | Mock | Missing or zero project or document id: rejected |
| `A_body_that_will_never_parse_is_a_json_failure_and_creates_nothing` (4, InlineData) | Mock | Unparseable bodies: rejected |
| `A_DesignApproved_whose_payload_is_not_an_object_is_a_json_failure_and_creates_nothing` | Mock | A text payload is rejected |
| `A_failing_placeholder_write_surfaces_as_something_other_than_a_json_failure` | Stub | A database error is retried |

The Construction outbox dispatcher is not tested a second time. Its code is identical to the Payment one apart from namespaces, so the Payment tests cover the same logic.

## Running them

Run from the repository root.

```bash
# The command from the brief: every Payment unit test (252 pass)
dotnet test services/payment-service-tests/PaymentService.Tests.csproj -c Release --filter "Category!=Integration"

# Only the new Moq tests in Payment (89)
dotnet test services/payment-service-tests/PaymentService.Tests.csproj -c Release --filter "FullyQualifiedName~MockTests"

# Construction: every unit test (263 pass), and only its Moq tests (72)
dotnet test services/construction-service-tests/ConstructionService.Tests.csproj -c Release --filter "Category!=Integration"
dotnet test services/construction-service-tests/ConstructionService.Tests.csproj -c Release --filter "FullyQualifiedName~MockTests"

# One theory, showing each of its cases by name
dotnet test services/payment-service-tests/PaymentService.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~The_invoice_amount_is_always_the_quotation_total" --logger "console;verbosity=normal"
```

The same unit run happens in CI (`.github/workflows/ci.yml`, the `backend-unit-tests` job), so these tests also run on every push.

## Two-minute demo script

**Before the demo (not counted in the two minutes):**

1. In the repo root, run `dotnet build services/payment-service-tests/PaymentService.Tests.csproj -c Release` once. After that, the live runs below take only a few seconds, because `--no-build` skips the compile.
2. Open `services/payment-service-tests/ConstructionEventsConsumerMockTests.cs` in the editor. Keep a terminal open at the repo root.

| Time | Do | Say |
|---|---|---|
| **0:00 to 0:20** | Say it, with nothing on screen yet. | "A unit test checks logic on its own, so the database and Kafka are replaced by test doubles. We had hand-written fakes. We added Moq, which gives **stubs** that return an answer and **mocks** that let us check the calls afterwards. 186 new tests across Payment and Construction." |
| **0:20 to 0:45** | Run: `dotnet test services/payment-service-tests/PaymentService.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~MockTests"` | "These are the Moq tests for Payment: controllers, the outbox dispatcher and the event consumers. 89 pass in about two seconds, with no database and no Kafka running." |
| **0:45 to 1:15** | In the editor, go to `A_ConstructionStarted_bills_the_current_quotation_once_with_the_causing_event_id`. Point at the stub lines, the `HandleAsync` line, then the `Verify`. | "The two `Stub` lines tell the fake repositories what to answer: this project has a quotation of 1.1 million. Then we run the real consumer on one Kafka message. `Verify` with `Times.Once` checks that it asked the repository to create exactly one invoice, with this project, this amount, this person and this event id. If the code billed twice, billed a different amount or never billed, this fails." |
| **1:15 to 1:40** | Run the one-theory command (last one in "Running them"). Then scroll up to `QuotationTotals` at the top of the file. | "This is a `[Theory]`: one test, five inputs, from one cent up to the 13-digit maximum, taken from a `[MemberData]` property. Each is a separate pass. The rule is that the invoice is always the quotation total, and the edges are where bugs hide." |
| **1:40 to 2:00** | Open `ClientPaymentsControllerMockTests.cs`, find `A_client_who_does_not_own_the_project_never_reaches_the_payment_repository`. Then run the full command from the brief. | "This is the failure path. A client who does not own the project gets a 403, and `Times.Never` proves the payment repository was never called. A hand-written fake would not show that easily. And this is the full Payment suite from the brief: **252 passed, 0 failed**." |

**If you have spare time (about 40 seconds): break the code on purpose.** In `services/payment-service/Messaging/ConstructionEventsConsumer.cs`, change `quotation.EstimatedTotal,` to `quotation.EstimatedTotal + 1m,` and run `dotnet test services/payment-service-tests/PaymentService.Tests.csproj -c Release --filter "FullyQualifiedName~ConstructionEventsConsumerMockTests"`. Several tests fail, each saying it expected an invoice call with the quotation amount and the call never happened. Then undo it with `git checkout -- services/payment-service/Messaging/ConstructionEventsConsumer.cs`. Say: "This shows the tests catch a real bug." It rebuilds, so it takes longer than the other runs.

**If it goes wrong:** every command above is safe to re-run. If a run says it cannot find the build, drop `--no-build`.

## Questions you may get

| Question | Short answer |
|---|---|
| What is the difference between a stub and a mock? | A stub gives an answer so the code can carry on. A mock is checked afterwards to see how it was called. In Moq, `Setup` makes a stub and `Verify` makes a mock. |
| Why not use the fakes you already had? | A fake is good at "what is the state afterwards". It is awkward for "was this called exactly once with these values" and for "was this **never** called". Both kinds are kept, each used where it fits. |
| What does `Times.Never` give you? | It proves nothing was written on a failure path. A bug that stores a bad invoice anyway is caught. |
| What is `VerifyNoOtherCalls()`? | After verifying the calls we expect, it fails if the code made any other call on that mock. |
| What do the consumer tests say about duplicate events? | The consumer does not decide what is a duplicate. The database unique key does. The consumer's job is to pass the **same event id** each time, and a test proves it does. A repository that answers "already done" is treated as normal, not an error. |
| Do these tests prove the SQL is right? | No. The repository is replaced, so no SQL runs. The `Category=Integration` tests, which need MySQL, cover the SQL. |
| How do you know the tests are not empty? | I broke the production code on purpose (wrong invoice amount, no zero-id check, batch not stopping on a refused event, a part payment treated as final, and others). The new tests failed each time (14 failures in Payment, 20 in Construction), and the code was then restored. |
| Why are the outbox tests asynchronous? | The dispatcher is a background loop. The tests wait for a signal from a mock callback, not a fixed sleep, with a 10-second limit so a broken run fails instead of hanging. The database-failure test takes about one second because the dispatcher waits its one-second poll interval before the retry. |

## Things to know

- **Culture:** the `[Range]` limits are text such as `"0.01"`, parsed using the machine's culture. The amount-validation tests assume a culture that writes decimals with a dot. They pass here. A machine set to a comma-decimal culture would fail them. That is how the production attribute works, not something the tests introduced.
- **Existing tests unchanged:** the old `*Tests.cs` files and all `Fake*.cs` files are as they were. The new classes sit beside them with a `MockTests` suffix.
- **Shared helper:** `CallerContext.cs` in `payment-service-tests` builds a signed-in caller for the new Payment controller tests. The Construction phase test builds its own caller inside the class.
