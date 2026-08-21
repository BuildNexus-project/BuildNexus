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
