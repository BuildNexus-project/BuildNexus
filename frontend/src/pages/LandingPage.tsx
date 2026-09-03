import {
  ArrowRight,
  Building2,
  CreditCard,
  DraftingCompass,
  ListChecks,
  ShieldCheck,
  Users,
} from 'lucide-react'
import { Link, Navigate } from 'react-router-dom'

import { useAuth } from '@/auth/auth-context'
import { Button } from '@/components/ui/button'

/**
 * The public front door, shown on `/` to a visitor with no session: the
 * BuildNexus name, what the product is, and the two ways in. Arriving at the
 * site lands here rather than on a bare login form.
 *
 * A signed-in visitor has no use for it and is sent straight to the
 * authenticated home at `/home`; `App` keeps that route behind `ProtectedRoute`
 * for anyone who reaches it without a session.
 */
export function LandingPage() {
  const { isAuthenticated } = useAuth()

  if (isAuthenticated) {
    return <Navigate to="/home" replace />
  }

  return (
    <div className="relative min-h-svh overflow-hidden bg-background text-foreground">
      {/* Decorative only: a soft glow and a masked grid behind the hero. */}
      <div aria-hidden className="pointer-events-none absolute inset-0">
        <div className="absolute inset-x-0 top-0 h-[520px] bg-[radial-gradient(80%_100%_at_50%_0%,color-mix(in_oklch,var(--color-foreground)_7%,transparent),transparent_70%)]" />
        <div className="absolute inset-x-0 top-0 h-[520px] [background-image:linear-gradient(color-mix(in_oklch,var(--color-foreground)_6%,transparent)_1px,transparent_1px),linear-gradient(90deg,color-mix(in_oklch,var(--color-foreground)_6%,transparent)_1px,transparent_1px)] [background-size:64px_64px] [mask-image:radial-gradient(70%_80%_at_50%_0%,black,transparent_75%)]" />
      </div>

      <div className="relative">
        <header className="sticky top-0 z-20 border-b border-border/60 bg-background/80 backdrop-blur">
          <div className="mx-auto flex h-16 max-w-7xl items-center justify-between px-6">
            <span className="flex items-center gap-2 font-heading text-base font-semibold tracking-tight">
              <img src="/logo-mark.png" alt="" className="h-8 w-auto shrink-0" />
              BuildNexus
            </span>
            <nav className="flex items-center gap-2">
              <Button render={<Link to="/login" />} variant="ghost" size="sm">
                Log in
              </Button>
              <Button render={<Link to="/register" />} size="sm">
                Register
              </Button>
            </nav>
          </div>
        </header>

        <main>
          {/* Full-bleed banner: breaks out of the centered column so the black
              runs edge to edge, the logo nearly filling it. */}
          <div className="flex items-center justify-center bg-primary px-6 py-6 sm:py-8">
            <img
              src="/logo.png"
              alt="BuildNexus"
              className="h-62 w-auto brightness-0 invert sm:h-75"
            />
          </div>

          <section className="mx-auto max-w-7xl px-6 pt-12 pb-16 text-center sm:pt-16">
            <h1 className="mx-auto max-w-3xl font-heading text-4xl font-semibold tracking-tight text-balance sm:text-5xl md:text-6xl">
              Build smarter, from first brief to final handover.
            </h1>
            <p className="mx-auto mt-6 max-w-2xl text-base text-pretty text-muted-foreground sm:text-lg">
              BuildNexus brings clients, architects, and project managers into one place to plan,
              track, and deliver construction projects — design, build, and payment included.
            </p>
            <div className="mt-9 flex flex-col items-center justify-center gap-3 sm:flex-row">
              <Button render={<Link to="/register" />} className="h-11 w-full px-6 sm:w-auto">
                Get started
                <ArrowRight />
              </Button>
              <Button
                render={<Link to="/login" />}
                variant="outline"
                className="h-11 w-full px-6 sm:w-auto"
              >
                Log in to your account
              </Button>
            </div>
          </section>

          <section className="mx-auto max-w-7xl px-6 py-16">
            <div className="mx-auto max-w-2xl text-center">
              <h2 className="font-heading text-2xl font-semibold tracking-tight sm:text-3xl">
                Everything a project needs, in one system
              </h2>
              <p className="mt-3 text-muted-foreground">
                Five services behind one login — identity, projects, design, construction, and
                payment — working from the same record.
              </p>
            </div>
            <div className="mt-12 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
              {FEATURES.map(({ icon: Icon, title, body }) => (
                <div
                  key={title}
                  className="rounded-xl bg-card p-5 ring-1 ring-foreground/10 transition-all hover:-translate-y-0.5 hover:ring-foreground/20"
                >
                  <span className="grid size-9 place-items-center rounded-lg bg-muted text-foreground">
                    <Icon className="size-4" />
                  </span>
                  <h3 className="mt-4 font-heading text-sm font-semibold">{title}</h3>
                  <p className="mt-1.5 text-sm text-muted-foreground">{body}</p>
                </div>
              ))}
            </div>
          </section>

          <section className="mx-auto max-w-7xl px-6 pt-8 pb-24">
            <div className="rounded-2xl bg-primary px-8 py-12 text-center text-primary-foreground sm:px-12">
              {/* black line-art logo forced to white for the dark panel */}
              <img
                src="/logo-mark.png"
                alt=""
                className="mx-auto mb-6 h-12 w-auto brightness-0 invert"
              />
              <h2 className="font-heading text-2xl font-semibold tracking-tight sm:text-3xl">
                Ready to see your projects in one place?
              </h2>
              <p className="mx-auto mt-3 max-w-xl text-sm text-primary-foreground/80">
                Create an account as a client, architect, or project manager and start from your
                first brief.
              </p>
            </div>
          </section>
        </main>

        <footer className="border-t border-border/60">
          <div className="mx-auto flex max-w-7xl flex-col items-center justify-between gap-3 px-6 py-8 text-sm text-muted-foreground sm:flex-row">
            <span className="flex items-center gap-2">
              <img src="/logo-mark.png" alt="" className="h-5 w-auto shrink-0" />
              BuildNexus
            </span>
            <span>Construction project management, end to end.</span>
          </div>
        </footer>
      </div>
    </div>
  )
}

const FEATURES = [
  {
    icon: Building2,
    title: 'Projects, end to end',
    body: 'A client submits the brief — location, budget, floors, rooms — and follows it from pending through to completed without chasing anyone.',
  },
  {
    icon: DraftingCompass,
    title: 'Design & construction',
    body: 'Architects and project managers pick up approved work and move each project one stage at a time, the way the lifecycle allows.',
  },
  {
    icon: CreditCard,
    title: 'Payments in step',
    body: 'Billing tracks progress, so invoicing follows the build instead of running ahead of it or lagging behind.',
  },
  {
    icon: ShieldCheck,
    title: 'Roles that mean something',
    body: 'Client, Architect, Project Manager, Admin. Every screen and action is scoped to the role that owns it — in the UI and in the services.',
  },
  {
    icon: Users,
    title: 'Shared team directory',
    body: 'Project staff find each other in one place. Clients stay focused on their own build and nothing else.',
  },
  {
    icon: ListChecks,
    title: 'A recorded history',
    body: 'Every status change and approval is written down and published across the platform, so the record reads the same everywhere.',
  },
] as const
