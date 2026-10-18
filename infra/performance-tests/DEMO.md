# Load testing demo and VIVA notes

This is Person 4's SE3112 contribution on
`feature/SE3112-05-load-testing`. The selected individual area is **Load &
Performance Testing**. See [README.md](README.md) for installation and
[RESULTS.md](RESULTS.md) for measured findings. The group's other three
areas are unit/mocking, mutation testing, and code quality/metrics.

## Prepare before your slot

1. Start Docker Desktop and the backend stack using the README. Confirm
   `http://localhost:5000/health` through `http://localhost:5005/health` return
   success. Keep the stack running for the demonstration.
2. Use Git Bash, set `JMETER_CMD` to your JMeter executable and `PYTHON_CMD`
   if Python is not found. Run the smoke command below once to check the
   actual machine and Admin credentials. Do not download tools or rebuild
   containers during the two-minute individual slot.
3. Open `project-performance-test-plan.jmx` in your editor at the four
   thread groups, `profiles/smoke.properties`, and `check-performance.py`
   at `evaluate`. Have the retained load/stress dashboards open in browser
   tabs, clearly labelled as earlier measurements.
4. Open the group's CI run showing `Load tests (JMeter smoke)`, `Test report`,
   and its artifact. The workflow must already have been committed, pushed,
   and dispatched with `run_load` enabled; local validation alone does not
   demonstrate a successful GitHub run.
5. Rehearse with a timer. A smoke failure is evidence to explain using its
   real reason; do not replace it with earlier output and call it live.

JMeter 5.6.3 can print package-scanning deprecation notices at startup.
These tool notices are separate from the HTTP/assertion errors shown in
the request table; explain the actual gate result if asked.

## Two-minute individual demonstration

Run from the repository root:

```bash
bash infra/performance-tests/run-performance-tests.sh -Jprofile=smoke -Jenv_label=live-demo
```

| Time | Show and explain |
|---|---|
| 0:00–0:15 | Start the command. “This uses real HTTP requests through the gateway. Four thread groups cover project creation, design upload, a Client journey and construction reports.” |
| 0:15–0:40 | While fixtures prepare, show the smoke properties: six users, finite loops, one-second ramp-up, 100 ms think time and zero allowed errors. Show the load/stress properties briefly: assumed 20-user baseline and bounded 20/40/80/160-user steps. |
| 0:40–1:05 | Show response assertions and CSV correlation: each Client reads its own project, invoice, payment and 50% construction progress. Explain that fixture setup is outside measurement; authentication/setup inside JMeter is counted but separated from workload latency. |
| 1:05–1:35 | Show the completed console table and the newly printed `summary.md` path. Explain expected/observed counts, endpoint p95 versus its budget, error percentage, throughput, and the exit status. Open the new HTML dashboard if time permits. |
| 1:35–2:00 | Show the measured load/stress finding from `RESULTS.md` and the retained report. State the tested limit or failure accurately. Hand over: “The same runner returns a failing status to CI and its metrics appear in our combined report.” |

If fixture or JVM startup is slower on the presentation machine, use the
waiting time to explain code and budget assumptions. Finish the live run
before claiming it passed. Retained stress evidence supports discussion
without running a multi-minute stress experiment inside the slot.

For the recorded experiment, your closing explanation can be: “The 20-user
load passed all 906 requests. Stress passed at 80 users, then at 160 a design
upload hit a database deadlock and returned 500. That thread stopped, leaving
nine missing uploads, and four endpoint latency budgets failed. The overall
error rate alone was only 0.0138%, so latency and completeness checks caught
problems that an overall error threshold would miss.” This describes the
tested steps; it does not establish 80 users as a production capacity limit.

## Optional failure demonstration

Run this during rehearsal or only if your allotted time allows another run:

```bash
bash infra/performance-tests/run-performance-tests.sh -Jprofile=smoke -Jenv_label=gate-demo -Jp95_login_ms=1
echo "Runner exit status: $?"
```

A response slower than 1 ms breaches the deliberately strict login budget,
even when its HTTP status and content are correct. This demonstrates the
gate using actual timings; the budget change does not create a service
fault. Read the reason in `summary.md`. Normal smoke/load/stress measurements
use the versioned budgets, without this override.

## Questions to practise

| Question | Points to explain in your own words |
|---|---|
| Why JMeter? | The project already has a JMeter HTTP plan. Properties control reproducible workloads, non-GUI execution works in CI, and JTL samples support independent gates and an HTML dashboard. |
| Load versus stress versus smoke? | Smoke checks the paths cheaply. Load applies the assumed 20-user assessment workload. Stress increases the configured concurrency in bounded steps and stops at the first failed gate. None represents observed production traffic. |
| What does “20 users” mean? | Five project threads, three design threads, ten Client-journey threads and two report threads. They have finite loops, ramp-up and think time; their active lifetimes differ, so 20 is a configured upper concurrency rather than a sustained plateau. |
| Why not use status assertions alone? | A 200 response can contain empty or incorrect data. Groovy checks tokens, roles, IDs, payment totals and milestone progress. An assertion failure marks the JTL sample unsuccessful. |
| How do users obtain tokens? | Real registration and login. Staff accounts are promoted by a real Admin and log in again; registration itself creates Clients. Each protected request uses the appropriate bearer token. |
| How is Kafka handled? | Fixture setup uses bounded polling for the projections needed by later services. A timeout aborts the run. The HTTP workload does not directly measure Kafka consumer lag or end-to-end event latency. |
| How is p95 calculated? | Sort endpoint elapsed times and choose rank `ceil(0.95 × count)`. It is not the mean or the average of group p95 values. With only one or four smoke samples it is weak statistical evidence. |
| How are latency and errors gated? | Each measured endpoint has its own p95 budget; its error percentage must be at or below 0% for smoke or 1% for load/stress. Any setup error or unexpected sample count fails the whole run. |
| Why exact sample counts? | `stopthread` prevents later calls using missing IDs/tokens. Counting every sampler catches those missing requests so an incomplete, artificially fast run cannot pass. |
| What does requests/second mean here? | Per-endpoint samples divided by the time from its first start to its last finish, including gaps. Endpoint rates overlap in time and cannot be added to obtain whole-run throughput. |
| Why does JMeter exit zero despite failures? | A completed JMeter process can contain unsuccessful samples. The Python checker reads the JTL, checks counts and budgets, and determines the runner's exit status. A process error or missing dashboard also fails. |
| What can the experiment establish? | Performance of this workload on this warm local stack and data state. Single-run variability, accumulated fixtures, the small PDF, finite loops and a shared generator/server machine limit generalisation. |
| How would you investigate slow login? | Correlate its latency with User Service CPU and logs, then profile the real password verification path and repeat a controlled experiment. Do not weaken password hashing to improve benchmark numbers. |
| Why can a failed CI job have passing metrics? | Failures can occur outside the measured requests, such as report upload or cleanup. The combined report preserves the failed job status and never lets a passing metric file turn it green. |

## Contribution and submission checklist

Person 4 owns the JMeter workloads/assertions, API fixtures, runner,
profiles, performance gate, tests, CI load job/report integration, results
and this demonstration guide. The five commits document those changes.
Record the student's actual name in the group's existing contribution table.

For the group package, include source/tests/configuration and the setup
README. Use the existing architecture diagram in the group intro and devote
the load-testing portion of a slide to the workload, measured result and
one qualified finding. Keep the whole deck at the assignment's five-slide
limit. The group intro/automation demonstration is two minutes, the four
individual slots are two minutes each, and VIVA is two minutes total.

Keep the generated JTL/dashboard folders available locally for the live
walkthrough. CI retains its allowlisted report artifact for 14 days; download
it before it expires if it will be used at VIVA.
