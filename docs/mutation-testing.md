# Mutation testing (SE3112-04)

Line coverage tells you which lines the tests **ran**. It does not tell you whether
the tests would **notice** if those lines were wrong. A test that calls a method and
asserts nothing gives 100% coverage and catches no bugs.

Mutation testing measures that second thing. The tool plants small bugs in the code
on purpose, one at a time, and runs the tests against each one. If a test fails, the
bug was caught. If every test still passes, the tests have a gap.

BuildNexus uses [Stryker.NET](https://stryker-mutator.io/docs/stryker-net/introduction/)
on the Payment Service and the User Service.

## How Stryker works

1. **Build and baseline.** Stryker builds the test project and runs the unit tests
   once, unchanged. They must all pass, or there is nothing to compare against.
2. **Create mutants.** It reads the service's C# source with the Roslyn compiler and
   makes many small changes, called *mutants*. Each mutant is one change, for example:

   | Mutator | Original | Mutant |
   |---|---|---|
   | Equality | `iterations <= 0` | `iterations < 0` |
   | Logical | `a \|\| b` | `a && b` |
   | Negate expression | `if (recorded)` | `if (!recorded)` |
   | Boolean | `IsActive = true` | `IsActive = false` |
   | String | `"Not your invoice."` | `""` |
   | Statement | `consumer.Commit(result);` | `;` (the line is removed) |
   | Block removal | `{ ... }` | `{}` |

3. **Compile them all at once.** Building the project again for every mutant would be
   far too slow, so Stryker compiles **every** mutant into one assembly, each one
   wrapped in an `if` that checks which mutant is "active". It then switches between
   them at run time.
4. **Coverage analysis.** One instrumented test run records which tests reach which
   mutant. A mutant that no test reaches is marked *NoCoverage* straight away, and
   each other mutant is run only against the tests that reach it.
5. **Run the tests per mutant.** For each mutant, the tests that cover it are run
   with that mutant switched on, and the result is recorded (statuses below).
6. **Report.** An HTML report shows every file with each mutant inline, coloured by
   status. The *progress* reporter prints the totals in the terminal.

## What each status means

| Status | Meaning | Good or bad |
|---|---|---|
| **Killed** | At least one test failed with the mutant active. The tests caught the bug. | Good |
| **Survived** | The tests ran the mutated code and all still passed. The bug went unnoticed. | Bad, a gap in the assertions |
| **Timeout** | The mutant made the tests run far longer than the baseline, usually an infinite loop (`i++` became `i--`). It counts as detected, since a hanging test is a failure a developer would see. | Good |
| **NoCoverage** | No test reaches this code at all, so the mutant cannot be killed. | Bad, a gap in what is tested |
| CompileError | The mutant does not compile (for example, it uses a variable before it is assigned). Not a real program, so not counted. | Ignored |
| Ignored | Excluded on purpose by the configuration (a file outside `mutate`, or a method in `ignore-methods`). | Ignored |

## The score formula

```
                      Killed + Timeout
mutation score = ------------------------------------------  x 100
                 Killed + Timeout + Survived + NoCoverage
```

CompileError and Ignored mutants are left out. They were never valid tests of the
test suite.

Example, the Payment Service before new tests: 138 killed, 0 timeout, 45 survived,
46 no coverage gives 138 / (138 + 45 + 46) = 138 / 229 = **60.26%**.

## Configuration

Each service has its own `stryker-config.json` in its test project:
[payment-service-tests/stryker-config.json](../services/payment-service-tests/stryker-config.json) and
[user-service-tests/stryker-config.json](../services/user-service-tests/stryker-config.json).

| Setting | Value | Why |
|---|---|---|
| `project` / `test-projects` | the service and its test project | Which code to mutate and which tests to run. |
| `test-case-filter` | `Category!=Integration` | Unit tests only, the same filter the CI unit test job uses. Integration tests need MySQL and Kafka, and would be run once for every mutant. |
| `mutate` | `**/*.cs` minus the exclusions | Business logic only (Controllers, Messaging, Models, Configuration, Authorization, Services). |
| excluded | `Program.cs`, `Migrations/`, `Contracts/`, `obj/` | Startup wiring, SQL scripts, request/response DTOs and compiler-generated code. |
| excluded | `ApiDocs/` | Swagger setup, not behaviour. |
| excluded | `Data/` | The SQL repositories. Their tests are integration tests against a real MySQL database, which this run filters out, so every mutant there would show as NoCoverage and measure nothing. |
| `ignore-methods` | `*Log*` | Mutating the text of a log message (`_logger.LogInformation("...")`) cannot change behaviour, and no test should assert on log wording. |
| `reporters` | `html`, `progress` | The HTML report to read, and live progress in the terminal. |
| `thresholds` | high 80, low 60, break 50 | 80% or more is shown green, 60 to 80 amber, under 60 red. **Under 50 the run exits with an error**, which fails the CI job. |

## Running it

The tool is pinned in [.config/dotnet-tools.json](../.config/dotnet-tools.json), so
everybody runs the same version:

```bash
dotnet tool restore                      # once, installs dotnet-stryker 5.0.0
cd services/payment-service-tests
dotnet stryker                           # reads stryker-config.json in this folder
# report: StrykerOutput/<date-time>/reports/mutation-report.html (git-ignored)
```

On a slow service, mutate only the files changed since a branch:

```bash
dotnet stryker --since:main
```

Both services run in full well within reason (the Payment Service in about 75
seconds, the User Service in about 25), so `--since` was not needed.

In CI the `mutation-tests` job in [.github/workflows/ci.yml](../.github/workflows/ci.yml)
runs both services as a matrix. It runs only on a manual run with the **Run mutation
testing** box ticked, or on the weekly schedule (Monday 02:00 UTC), never on a push.
Each service's HTML report is uploaded as the artifact `mutation-report-<service>`.

## Results: Payment Service

### Before and after

| | Before | After |
|---|---:|---:|
| Unit tests | 252 | 271 (+19) |
| Mutants tested (not ignored, compiled) | 229 | 229 |
| Killed | 138 | 169 |
| Survived | 45 | 31 |
| Timeout | 0 | 0 |
| No coverage | 46 | 29 |
| **Mutation score** | **60.26%** | **73.80%** |
| Run time | 1 min 19 s | 1 min 12 s |

16 surviving mutants and 15 no-coverage mutants were killed (31 in total).
Two no-coverage mutants are now covered but survive (the "carried no payload object"
message text, explained below).

### The weakest classes in the first run

`AuthorizationProblemEvents` scored 0% with **no** surviving mutants: all 13 of its
mutants were *NoCoverage*, because no unit test called it at all. It is listed
separately because it had nothing to survive. The three weakest classes that did
have surviving mutants were:

**1. `Messaging/ProjectEventsConsumer.cs`: 48.15%** (13 killed, 7 survived, 7 no coverage)

| Line | Mutation | Code | Now |
|---|---|---|---|
| 167 | Statement removed | `throw new JsonException("The message body was empty.");` | Killed |
| 167 | String emptied | same line, message text | Survived |
| 171 | String emptied | `?? throw new JsonException("...deserialised to null.")` | Survived |
| 184 | Statement removed | `throw new JsonException("A ProjectCreated event carried no payload object.");` | Killed |
| 184 | String emptied | same line, message text | Survived |
| 192 | String emptied | `"...missing its projectId or clientId."` | Killed |
| 203 | Negate | `if (recorded)` → `if (!recorded)` | Survived |

**2. `Messaging/ConstructionEventsConsumer.cs`: 53.49%** (23 killed, 8 survived, 12 no coverage)

| Line | Mutation | Code | Now |
|---|---|---|---|
| 180 | Statement removed | `throw new JsonException("The message body was empty.");` | Killed |
| 180 | String emptied | same line, message text | Survived |
| 184 | String emptied | `"The message body deserialised to null."` | Survived |
| 189 | Statement removed | `return;` after handling ConstructionCompleted | Survived |
| 210 | String emptied | `"...missing its projectId or startedBy."` | Killed |
| 218 | String emptied | `"...carried no eventId."` | Killed |
| 289 | String emptied | `"...missing its projectId."` | Killed |
| 301 | Negate | `if (announced)` → `if (!announced)` | Survived |

**3. `Controllers/ClientPaymentsController.cs`: 56.00%** (14 killed, 9 survived, 2 no coverage)

| Line | Mutation | Code | Now |
|---|---|---|---|
| 89 | String emptied | 403 title `"Not your invoice."` | Killed |
| 90 | String emptied | 403 detail `"You can only record payments..."` | Killed |
| 106 | String emptied | same 403 title, invoice deleted mid-request | Killed |
| 107 | String emptied | same 403 detail, invoice deleted mid-request | Killed |
| 124 | String emptied | `controllerName: "ClientInvoices"` in the 201 Location | Killed |
| 168 | String emptied | 409 title `"Payment exceeds the outstanding amount."` | Killed |
| 170 | String emptied | 409 detail `"Enter that amount or less."` | Killed |
| 173 | String emptied | 409 title `"This invoice is already paid."` | Killed |
| 174 | String emptied | 409 detail `"Nothing is outstanding..."` | Killed |

### What the new tests are

All are new files. No existing test was changed.

| File | Tests | What it closes |
|---|---:|---|
| `AuthorizationProblemEventsTests.cs` | 5 | The 401 and 403 bodies. An expired token gets `error="invalid_token"` and "Your session has expired", any other failure gets plain `Bearer`. 13 mutants went from no coverage to killed. |
| `MalformedEventMessageTests.cs` | 9 | A **null** message body (a Kafka "tombstone") and an envelope with **no `payload` key**. The old tests used `""` and `"payload": null`, which still ended in a `JsonException` a line later even with the `throw` deleted. With these inputs the deleted `throw` lets an `ArgumentNullException` or `InvalidOperationException` escape instead. That matters: the consumer only skips a message on a `JsonException`, and retries anything else forever, which blocks the partition. Also checks that the error names the missing field, since it is all an operator sees in the log. |
| `ClientPaymentsControllerResponseTests.cs` | 5 | The exact titles and details of the 403 and 409 answers, which the Client's screen displays, and that the 201's Location points at the Client invoice listing. |

### What still survives, and why that is acceptable

* **Equivalent mutants.** These change the code but not its behaviour, so no test can
  kill them. Removing the `return;` at `ConstructionEventsConsumer.cs:189` is one: the
  next `if` returns anyway for a ConstructionCompleted event. The
  `"The ConstructionStarted payload deserialised to null."` line (206) cannot be reached
  at all: a payload that would deserialise to null is a JSON `null`, and the "no payload
  object" check on line 200 has already refused it. That is why it stays NoCoverage.
* **Log-only branches.** `if (recorded)` and `if (announced)` only choose which log line
  is written. Asserting on log wording would make the tests brittle for no gain.
* **Exception message text** for a body that is empty or has no payload. The type of
  the exception is what decides the behaviour, and that is now tested.
* **NoCoverage in `ExecuteAsync` / `ProcessAsync`.** The Kafka consume loop needs a
  real broker. It is exercised by `PaymentKafkaIntegrationTests`, which this run
  filters out.

## Results: User Service

The second service, chosen because its unit tests run in under a second, so a full
run takes about 25 seconds.

| | Before | After |
|---|---:|---:|
| Unit tests | 192 | 205 (+13) |
| Mutants tested | 198 | 198 |
| Killed | 77 | 103 |
| Survived | 32 | 30 |
| Timeout | 0 | 0 |
| No coverage | 89 | 65 |
| **Mutation score** | **38.89%** | **52.02%** |
| Run time | 24 s | 23 s |

The first run was **under the break threshold of 50%**, so Stryker exited with an
error, and the CI job would have failed. Most of the gap is NoCoverage in
`AuthController` and `UsersController`, which are tested mainly by integration tests
through a real web host (`UserServiceFactory`), and this run excludes those.

New test files:

| File | Tests | What it closes |
|---|---:|---|
| `AuthorizationProblemEventsTests.cs` | 5 | Same as the Payment Service. 13 mutants killed. |
| `InternalServiceAuthenticationHandlerTests.cs` | 5 | The API key check on the internal endpoints: missing header, the header sent twice, a wrong key of the right length (only the byte comparison can refuse it), and a key that only starts with the right one (only the length check can refuse it). 10 mutants killed. A security check that previously had no unit test at all. |
| `Pbkdf2PasswordHasherEdgeCaseTests.cs` | 3 | Inputs that are wrong in exactly **one** way: an empty password against a real hash of `""`, a genuine hash relabelled `bcrypt`, and a hash claiming 0 iterations. Each one would sign the caller in, or crash the login, if one `\|\|` became `&&` or one `<=` became `<`. 3 survivors killed. |

The User Service is still under the "low" threshold of 60%. Most of what remains is
NoCoverage in `AuthController` (registration and login), `SmtpEmailSender` and
`AdminSeeder`, which are covered by integration tests or need an SMTP server. Moving
the controller logic into services that can be unit tested would be the next step.
