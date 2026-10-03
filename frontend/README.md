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
| `/admin/oversight` | Admin — every project, who is on it, which have stalled, and links to the three reports |

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

## Platform oversight (US-38)

`/admin/oversight` is the Admin's single entry point for platform health, and the
Admin's first button on `/home`. It has two parts:

- **Reports** — links to the project status, construction & payment and design
  approval reports. They are taken from the Admin's own navigation, so a report
  added there appears here too and one the Admin cannot open is never offered.
  Linked rather than embedded: each already exists as a full page with its own
  filters and export, and an embed would duplicate them.
- **All projects** — one table from `GET /api/projects/oversight`: status,
  Architect, Project Manager, last-updated date. Cancelled projects are listed.
  A person who is assigned but whose name could not be looked up reads
  "Assigned (name unavailable)" rather than looking like an empty slot.

Stalled projects get a tinted row and a "Stalled · N days" badge — in words as
well as colour. The service decides which are stalled and says how many days that
means; the page only shows it. "Show only stalled projects" narrows the list.

The list and the reports load independently of each other: if the projects cannot
be loaded the reports are still offered.

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
| Project Manager | Active construction, milestones due                              | `/api/construction/dashboard/project-manager` |
| Admin          | System-wide counts: users, projects, and the reports available   | `/api/users/dashboard/admin`, `/api/projects/dashboard/admin` |

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

- **"Milestones due" means outstanding, and overdue where there is a date.** A Project
  Manager may give a milestone a due date — optionally, when they add it, or afterwards in
  the Due column of the project page's milestones table, which saves as soon as the date
  is changed and clears when it is emptied. The dashboard counts every milestone not yet
  completed, says how many of those are overdue, and lists the dated ones first, soonest
  first. A milestone with no date is outstanding and never late; a completed one is never
  late however old its date. Dates are handled as `yyyy-MM-dd` strings
  (`src/lib/milestone-dates.ts`), never as `Date` objects, so no timezone can shift the day
  — `new Date('2026-10-05')` is the 4th anywhere west of Greenwich. The Client sees the
  same date beside each milestone on the Progress page, and is told "Was due …" when it has
  passed without the milestone being done.
- **The Project Manager's dashboard is their own projects.** The Construction Service does
  not know which Project Manager runs a project, so it asks the Project Service, with the
  caller's own token, which ones are theirs, and answers for those. That answer also names
  each project, so the dashboard is one request with no separate lookup, and every build
  is a link the Project Manager may open. If the Project Service cannot be reached the
  dashboard shows the reason and a dash — it does not say "nothing under way".
- **Reports are counted as the pages available.** Nothing stores a report — each is
  generated on demand — so there is no number of "reports made". The Admin dashboard's
  tile counts the report pages the Admin can open, says so ("Generated on demand, not
  stored"), and is taken from the Admin's own navigation so it cannot drift from the links
  in the panel beneath it.
- **Design status is derived from each document's latest version**, in the order
  most in need of the Client first: awaiting their review, then a revision they are
  waiting on, then approved. A project nothing has been uploaded for is `No design
  yet`, not blank.
- **A long figure shrinks to fit its tile.** An amount such as `LKR 4,810,000.00` is set
  smaller than a count, and the non-breaking space the currency formatter puts after
  `LKR` is made an ordinary one so the amount wraps at the space instead of through the
  digits. A phone's two-column grid otherwise made the tile wider than the screen. The
  tests cannot see this; a real browser can.

## Notifications (US-26)

A Client or Architect sees what has happened on their projects — a design
approved, a milestone completed, a payment received — in a **Notifications**
panel between the welcome banner and their dashboard on `/home`. That page is
where a login lands, so a notification stored earlier is visible on the next
login or dashboard load; nothing is pushed.

- `src/components/dashboard/NotificationsPanel.tsx` is rendered by `HomePage` for
  the roles in `NOTIFICATION_ROLES` only. The Project Manager and Admin are never
  sent any, so they are neither shown the panel nor asked for the list. It sits in
  the page rather than inside the role dashboards, whose tests pin the exact
  slices each one requests.
- `src/lib/notifications-api.ts` has the three calls, all under
  `/api/projects/notifications`: the list, mark one read, mark all read. The type
  is `UserNotification`, not `Notification`, which is the browser's own global.
- The "N new" badge is the service's whole unread count, not the length of the
  page it showed.
- Marking read asks the service and then reads the list again, so what is on
  screen is the service's answer rather than a local guess. It has its own small
  loading effect instead of `useDashboardSlice`, which reads once on mount.
- Like a dashboard slice, a failed load names what failed and shows no figure —
  never a reassuring zero — and leaves the rest of the dashboard working.

**The bell.** `NotificationBell` sits in the header of a Client or Architect on
every page, with the unread count (capped at "99+") and a link to the history. It
shows no badge for zero, and none before the first answer or if there never is
one — no badge is never a claim that nothing is new. A failed later read keeps the
last count rather than guessing. The Project Manager and Admin get no bell and are
never asked for a count. It is not one of the `NAV_ITEMS`: it is not a page in a
role's own workspace list, so the nav tests' guard map is unchanged.

**The history.** `/notifications` (`NotificationsPage`, guarded by
`NOTIFICATION_ROLES`) lists every notification newest first, 20 at a time, with
"Load more", Mark as read and Mark all as read. It is reached from the bell and
from "View all" on the panel. Unlike the panel it updates the rows on screen
instead of re-reading: a re-read of the first page would throw away the pages
already loaded. A notification that arrives while paging pushes the rest down
one, so the next page can begin with a row already shown — those are skipped by id.
The panel and the page share `NotificationRow`, so one notification looks the same
in both.

**Staying current.** Nothing is pushed — there is no push transport, for the
reason on `useAutoRefresh` — so the bell and the panel re-read on that same
polling: every 30 seconds while the tab is visible, and straight away when it
becomes visible again. `useUnreadNotificationCount` asks for a page of one, since
the unread figure in the answer is the whole count whatever the page size. When the
panel or the history page marks something read it calls
`announceNotificationsChanged()` (`notifications-sync.ts`), a window event the bell
listens for, so the badge does not lag a poll behind. The history page itself does
not poll: it is a record you opened, and re-reading it would reshuffle the pages
under the reader.

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
