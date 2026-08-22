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

| Route       | Access                                              |
|-------------|-----------------------------------------------------|
| `/register` | Anyone — Client, Architect or Project Manager only   |
| `/login`    | Anyone                                              |
| `/`         | Signed-in users; others are redirected to `/login`   |

The access token is kept in `localStorage` under `buildnexus.accessToken` and
read back on load, so a refresh keeps the session. It is discarded if it is
malformed or has expired, and the app signs out on its own the moment it does.

`ProtectedRoute` is a convenience, not a security boundary — the User Service
rejects any request without a valid token regardless of what the UI shows.

## Scripts
| Command           | What it does                        |
|-------------------|-------------------------------------|
| `npm run dev`     | Dev server with hot reload          |
| `npm run build`   | Type-check and produce `dist/`      |
| `npm run preview` | Serve the production build          |
| `npm run lint`    | oxlint                              |

## Adding shadcn components

```bash
npx shadcn@latest add <component>
```

Note this shadcn version is built on **Base UI**, not Radix, and no longer ships
the react-hook-form `<Form>` wrapper — `field.tsx` is its replacement, wired to
react-hook-form by hand.
