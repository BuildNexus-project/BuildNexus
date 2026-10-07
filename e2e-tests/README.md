# End-to-end tests (Selenium)

Browser tests for the whole system, driven the way a person uses it: a real Chrome window
clicking through the React app, which calls the API gateway, which calls the services, which
use their databases and Kafka. Nothing is stubbed and nothing calls an API directly. This is
the third layer of the testing story, after unit tests (US-29) and integration tests (US-30).

| | |
|---|---|
| Framework | xUnit + Selenium.WebDriver (C#, net10.0), the same as every other test project here |
| Browser | Chrome, headless by default. Selenium Manager downloads the matching chromedriver on first run |
| Tag | `Category=E2E`, so the unit (`Category!=Integration`) and integration (`Category=Integration`) CI jobs never start a browser |

## What it covers

Two flows, each one test, across four roles and all five services.

### 1. `FullProjectWorkflowTests`: from sign-up to payment

1. **Register**: a Client, an Architect and a Project Manager sign up through `/register`.
2. **Create project**: the Client submits a project through `/projects/new`.
3. **Assign staff**: the seeded Admin assigns the Architect and the Project Manager on the project page.
4. **Upload design**: the Architect uploads a PNG as a new design document.
5. **Approve**: the Client approves the design, then sees a "design approved" notification. That notification exists only if the Design Service's event reached the Project Service through Kafka.
6. **Quote and invoice**: the Project Manager generates a quotation and raises an invoice.
7. **Record payment**: the Client pays the invoice in full. The invoice reads Paid, the outstanding balance reads `LKR 0.00`, and a "payment received" notification arrives, again over Kafka (Payment Service to Project Service).

### 2. `ConstructionHandoverWorkflowTests`: from approved design to handover

Steps 1 to 5 above (shared, in `WorkflowSetup`), then:

6. **Move on**: the Architect moves the project to Design Approved. Approving the document does not do it; the status is the company's word.
7. **Quote, build, start**: the Project Manager quotes the project, creates the standard milestones (which only appear once the Construction Service has heard the design was approved over Kafka) and starts construction. The project reads Construction.
8. **Pay the automatic invoice**: nobody raises an invoice, yet the Client has one for the quoted total. The Payment Service made it from the `ConstructionStarted` event. The Client pays it in full.
9. **Finish and hand over**: the Project Manager sets all seven milestones to Completed, marks construction complete, and hands the project over. Handover is refused until the Payment Service's settlement event reaches the Construction Service, so the test retries until it is accepted. The project then reads Completed.

Each flow is a single test on purpose. Every step depends on the one before, so splitting a flow
would make later tests fail for an earlier test's reason.

## Run it

You need Docker Desktop, Node 22, the .NET 10 SDK and Chrome.

```bash
# 1. the backend: five databases, Kafka, Mailpit, five services, the gateway
cd infra
cp .env.example .env        # once; the values are working local-development ones
docker compose up -d --build

# 2. the frontend (not part of compose)
cd ../frontend
npm install
npm run dev                 # http://localhost:5173

# 3. the tests, from the repository root
dotnet test e2e-tests/EndToEnd.Tests.csproj
```

If the frontend is not answering, the test stops at once and says so, rather than waiting out a
timeout. Both flows together take under a minute (46 and 57 seconds in the recorded runs).

To watch the browser instead of running headless: `E2E_HEADLESS=false dotnet test e2e-tests/EndToEnd.Tests.csproj`.

### Settings

All optional, read from environment variables. The defaults are the local stack.

| Variable | Default | Meaning |
|---|---|---|
| `E2E_BASE_URL` | `http://localhost:5173` | Where the React app is served. Its `/api` calls must reach the gateway (Vite proxies them locally) |
| `E2E_ADMIN_EMAIL` | `admin@buildnexus.local` | An existing Admin. Assigning staff is Admin-only and an Admin cannot register |
| `E2E_ADMIN_PASSWORD` | `ChangeMe123!` | That Admin's password |
| `E2E_HEADLESS` | `true` | `false` opens a visible window |
| `E2E_WAIT_SECONDS` | `30` | The longest any one wait may take, including a wait for a Kafka event to arrive |

The Development Admin exists only where the User Service runs with `ASPNETCORE_ENVIRONMENT=Development`,
which Docker Compose sets. Against a deployed environment, set the Admin variables to a real Admin.

## Re-running

Every run registers new accounts and a new project under a random suffix (for example
`e2e.client.1a2b3c4d@buildnexus.test`), so it can be repeated against the same databases. The
data is left behind. `docker compose down -v` wipes it.

## How the tests are written

- **`BuildNexusApp`** has one method per thing a person does (`Register`, `SignIn`, `CreateProject`, `AssignStaff`, `UploadDesign`, `ApproveDesign`, `QuoteAndInvoice`, `PayInvoice`), so the test reads as the workflow.
- **`BrowserSession`** holds the Chrome window and the waiting. There is no `Thread.Sleep` and no implicit wait: every step waits for the thing it needs to be on screen, and a timeout names that thing and prints the page's visible text.
- **Kafka effects are waited for, not slept for.** Some pages read once on load and never poll, so `ReloadUntilVisible` reloads until the event's result appears.
- **Selectors follow what the user sees**: field ids, button text, headings and the existing `aria-label`s. No `data-testid` attributes were added to the app for the tests.

## When it fails

The failure message names the step, the URL and the page text. The test also saves a screenshot to
`e2e-tests/bin/<Configuration>/net10.0/screenshots/failure-<run>.png`. Common causes:

- **"The frontend did not answer"**: `npm run dev` is not running, or `E2E_BASE_URL` is wrong.
- **A timeout on the sign-in or registration step**: the backend is not up. Check `docker compose ps`.
- **A timeout waiting for a notification**: a Kafka consumer is behind or down. Check the Project Service logs (`docker logs buildnexus-project-service`).
- **Admin sign-in fails**: the User Service is not running in Development, or the Admin's password was changed.

## In CI

The `backend-e2e-tests` job in `.github/workflows/ci.yml` builds and starts the whole stack and runs
this suite. It runs only when started by hand (Actions tab, CI, Run workflow). The project is
compiled on every push by the backend build job.
