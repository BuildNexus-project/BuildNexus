# BuildNexus Frontend

React single-page app for BuildNexus.

## Stack
- React 19 + TypeScript, built with Vite
- Tailwind CSS v4 (`@tailwindcss/vite`, CSS-first config in `src/index.css`)
- shadcn/ui components in `src/components/ui` (owned by us — edit them freely)
- react-hook-form + zod for form state and validation
- React Router for client-side routing

## Run

```bash
npm install
npm run dev
```

Serves on `http://localhost:5173`. Requests to `/api/*` are proxied to the User
Service on `http://localhost:5001`, so the browser makes same-origin calls and
no CORS configuration is needed in development. When the YARP gateway lands,
change that one proxy target in `vite.config.ts`.

The User Service must be running — see `infra/README.md`.

## Routes

| Route          | Access                                             |
|----------------|----------------------------------------------------|
| `/register`    | Anyone — Client, Architect or Project Manager only  |
| `/login`       | Anyone                                             |
| `/forgot-password` | Anyone                                         |
| `/reset-password`  | Anyone — needs a `?token=` from the reset email |
| `/`            | Anyone — public landing page; signed-in users go to `/home` |
| `/home`        | Signed-in users, each shown their own role's dashboard; others are redirected to `/login` |
| `/profile`     | Signed-in users                                    |
| `/projects/new` | Client                                            |
| `/directory`   | Architect, Project Manager                         |
| `/admin/users` | Admin                                              |

The access token is kept in `localStorage` under `buildnexus.accessToken` and
read back on load, so a refresh keeps the session. It is discarded if it is
malformed or has expired, and the app signs out on its own the moment it does.

`ProtectedRoute` is a convenience, not a security boundary — the User Service
rejects any request without a valid token regardless of what the UI shows.

`RoleRoute` is the same idea for the role-gated routes: it wraps `ProtectedRoute`
and adds a role check, and the role lists it takes (`CLIENT_ROLES`,
`PROJECT_STAFF_ROLES`, `ADMIN_ROLES` in `src/lib/roles.ts`) mirror the service's own. A signed-in user
reaching a route their role may not open stays where they are and is shown who
the page is for, rather than being redirected somewhere that hides what
happened. The home page offers each link only to the roles allowed to open it.

None of that is the boundary either. Each page also handles the `403` the
service returns and shows the reason it gives, so bypassing the guard changes
nothing about what a user can actually read.

## Landing page

`/` is a public page — the BuildNexus name, a one-line description, and Log in /
Register buttons — so a first-time visitor arrives at something that explains
the product rather than a bare login form. A signed-in visitor has no use for
it: `LandingPage` forwards them to `/home`, the authenticated landing page,
which stays behind `ProtectedRoute` for anyone who reaches it without a session.
`/register`, `/login` and the `ProtectedRoute` redirect are unchanged.

## Role dashboards (US-21)

`/home` shows each signed-in user the dashboard for their own role, under the
welcome banner — never one generic dashboard for everyone to interpret for
themselves. `HomePage` switches on the role and renders one of four components
in `src/components/dashboard/`.

**Why it is several requests.** A dashboard needs data from all five services,
and each service's database is its own: nothing may join across two of them, and
the gateway makes no decisions and holds no data. So there is no single
"dashboard" endpoint. Every service exposes the slice it owns, under its own
gateway prefix and gated to one role, and the page joins the slices on the
project id. The fetchers and their types are all in `src/lib/dashboard-api.ts`.

| Role           | Shows (acceptance criteria)                                      | Slices it loads |
|----------------|------------------------------------------------------------------|-----------------|
| Client         | Active projects, design status, build progress, payments due     | `/api/projects/dashboard/client`, `/api/designs/dashboard/client`, `/api/construction/dashboard/client`, `/api/payments/dashboard/client` |
| Architect      | Assigned projects, pending revisions                             | `/api/projects/dashboard/architect`, `/api/designs/dashboard/architect` |
| Project Manager | Active construction, milestones due                              | `/api/construction/dashboard/project-manager`, and `/api/projects` for names |
| Admin          | System-wide counts: users, projects, and the reports             | `/api/users/dashboard/admin`, `/api/projects/dashboard/admin` |

A role is only ever asked for its own slices, and `HomePage.test.tsx` pins that
for all four. Each service refuses the wrong role anyway; this keeps the page
from causing a `403` in the console on every visit.

**Slices load and fail separately.** `useDashboardSlice` loads one slice and
reports `loading`, `ready` or `error`, so a slow or failed service leaves a dash
and an alert beside everything that did load, not a blank dashboard. A failed
slice shows `—`, never `0`: a failure is not "nothing to report". The alert names
the slice, followed by the reason the service gave when it gave one — the Design
Service answers `502` with a reason when it cannot reach the Project Service, and
that reaches the person.

**The joins and sums are pure functions** in `src/lib/dashboard-view.ts`, tested
without rendering anything: overall build progress is weighted by milestones (an
average of percentages would let a 2-milestone project count as much as a
10-milestone one), what is owed per project is summed in whole cents so a total
cannot drift, and a project with no known name falls back to its short id rather
than a blank.

**Things a reader should know**

- **"Milestones due" means outstanding.** Milestones have no due date — only
  `NotStarted`, `InProgress` and `Completed` — so the figure is what is still to
  finish, not what is late, and the tile says so.
- **The Project Manager's view is portfolio-wide**, like the Build & payment
  report: the Construction Service records who owns a project but not which
  Project Manager runs it. Names come from `GET /api/projects`, which lists only
  the projects a Project Manager is assigned to, so a build they are not assigned
  to shows a short id and is not linked (the Project Service would refuse the
  page). That name lookup is best-effort and raises no error if it fails.
- **Reports have links, not a count.** Nothing stores a report — each is generated
  on demand — so there is no number to read. The Admin dashboard links the three
  report pages, taken from the Admin's own navigation.
- **Design status is derived from each document's latest version**, in the order
  most in need of the Client first: awaiting their review, then a revision they are
  waiting on, then approved. A project nothing has been uploaded for is `No design
  yet`, not blank.

## New project (US-05)

`/projects/new` is where a Client submits a construction project: name,
location, land size in perches, budget, floors, bedrooms, bathrooms, garage
spaces, and free-text other requirements. Everything but the last is required.

Client only, mirroring the Project Service's own gate on `POST /api/projects`.
Staff roles do not submit work on a customer's behalf, so the link is offered
on the home page to Clients alone and the endpoint refuses anyone else with a
`403` whatever the router does.

Bedrooms, bathrooms and garage spaces accept `0` — not every build is a house,
and a garage is a count rather than a checkbox so that "none" and "two cars"
are the same question. Floors must be at least 1.

The numeric inputs are registered with `valueAsNumber`, which hands back `NaN`
for an empty box. `src/lib/project-schemas.ts` writes those messages for what
that actually means to the person reading them — "Number of bedrooms is
required", not a type error.

On success the page confirms the project is `Pending` and offers to submit
another, resetting to a blank form rather than leaving the previous answers in
place. The status is the service's, shown as it was returned.

## Password reset (US-04)

`/forgot-password` asks the service to email a link and then shows the sentence
the service returned, unchanged. That sentence is deliberately the same whether
or not the address has an account, so rewording it here would risk saying more
than the service means to.

`/reset-password` reads its token from the query string of the emailed link.
Two situations replace the form rather than annotating it: no token in the URL,
and a `400` naming no field — which is how the service reports a link that is
unknown, expired or already used. Neither can be fixed by editing the form, so
both offer a fresh link instead. A `400` that *does* name a field is an ordinary
validation failure and stays on the form.

To follow the flow locally, the reset email is caught by **Mailpit** rather than
delivered: open the inbox at <http://localhost:8025>, ask for a reset, and the
message appears there within a second with its link. See `infra/README.md`.

(A native `dotnet run` of the User Service logs the email to its console instead,
unless it is pointed at Mailpit — `services/user-service/README.md` covers that.)

## Scripts
| Command           | What it does                        |
|-------------------|-------------------------------------|
| `npm run dev`     | Dev server with hot reload          |
| `npm run build`   | Type-check and produce `dist/`      |
| `npm run preview` | Serve the production build          |
| `npm run lint`    | oxlint                              |
| `npm test`        | Vitest, once                        |
| `npm run test:watch` | Vitest in watch mode             |

## Adding shadcn components

```bash
npx shadcn@latest add <component>
```

Note this shadcn version is built on **Base UI**, not Radix, and no longer ships
the react-hook-form `<Form>` wrapper — `field.tsx` is its replacement, wired to
react-hook-form by hand.
