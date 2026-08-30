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
| `/`            | Signed-in users; others are redirected to `/login`  |
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
