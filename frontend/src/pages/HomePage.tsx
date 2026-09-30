import { ArrowRight, ArrowUpRight, CircleUser } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'

import { useAuth } from '@/auth/auth-context'
import { ArchitectDashboard } from '@/components/dashboard/ArchitectDashboard'
import { ClientDashboard } from '@/components/dashboard/ClientDashboard'
import { Button } from '@/components/ui/button'
import { fetchProjects, type ProjectSummary as ProjectSummaryRow } from '@/lib/project-api'
import { NAV_ITEMS, WORKSPACE_LABELS, type NavItem } from '@/lib/nav'
import type { Role } from '@/lib/roles'
import { cn } from '@/lib/utils'

/** One line under the greeting, in the voice of what this role is here to do. */
const ROLE_INTROS: Record<Role, string> = {
  Client:
    'Follow your build from first brief to final handover — and start something new whenever you are ready.',
  Architect: 'Your assigned projects, and the design work that moves each one forward.',
  ProjectManager: 'Keep milestones, costs and delivery moving across the projects you run.',
  Admin: 'Look after the people, the projects and the reporting across the whole platform.',
}

/** The one thing each role most often comes here to do, offered first and loudest. */
const PRIMARY_ACTION: Record<Role, string> = {
  Client: '/projects/new',
  Architect: '/projects',
  ProjectManager: '/projects',
  Admin: '/admin/users',
}

/** What the summary tiles are called, since "your" is only true for some roles. */
const SUMMARY_HEADINGS: Record<Role, string> = {
  Client: 'Your projects at a glance',
  Architect: 'Your assigned projects at a glance',
  ProjectManager: 'Your assigned projects at a glance',
  Admin: 'Every project at a glance',
}

/**
 * The dashboard for a role that has its own — the Client's and the Architect's read
 * several services and put them together (US-21). A role without one falls through to
 * the project summary below.
 */
function RoleDashboard({ role }: { role: Role }) {
  switch (role) {
    case 'Client':
      return <ClientDashboard />
    case 'Architect':
      return <ArchitectDashboard />
    default:
      return <ProjectSummary role={role} />
  }
}

/** Where a project is, in three groups a reader can take in at once. */
function summarise(projects: readonly ProjectSummaryRow[]) {
  return {
    total: projects.length,
    design: projects.filter((p) => ['Pending', 'Designing', 'DesignApproved'].includes(p.status))
      .length,
    construction: projects.filter((p) => p.status === 'Construction').length,
    completed: projects.filter((p) => p.status === 'Completed').length,
  }
}

/**
 * Where a role without a dedicated dashboard sees its projects stand, in four tiles.
 *
 * A convenience and never the point of the page. It is read from the same list the
 * Projects page shows, so it cannot disagree with it, and if that request fails the
 * section is left out rather than shown wrong or showing an error on a page that
 * otherwise works.
 */
function ProjectSummary({ role }: { role: Role }) {
  const { authFetch } = useAuth()
  const [projects, setProjects] = useState<ProjectSummaryRow[] | null>(null)

  useEffect(() => {
    let cancelled = false

    fetchProjects(authFetch)
      .then((loaded) => {
        if (!cancelled) {
          setProjects(loaded)
        }
      })
      // Deliberately quiet — see the component's note.
      .catch(() => undefined)

    // The effect can outlive the page if the user navigates away mid-request.
    return () => {
      cancelled = true
    }
  }, [authFetch])

  if (!projects) {
    return null
  }

  const summary = summarise(projects)

  return (
    <section aria-labelledby="summary-heading" className="flex flex-col gap-4">
      <h2 id="summary-heading" className="font-heading text-lg font-semibold tracking-tight">
        {SUMMARY_HEADINGS[role]}
      </h2>

      <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
        <Stat label="Active projects" value={summary.total} />
        <Stat label="In design" value={summary.design} dot="bg-sky-500" />
        <Stat label="Under construction" value={summary.construction} dot="bg-orange-500" />
        <Stat label="Completed" value={summary.completed} dot="bg-emerald-500" />
      </div>
    </section>
  )
}

/**
 * The signed-in user's dashboard: a welcome in the BuildNexus black, the summary of
 * their own role's work, and a card for everything their role can do — the same places
 * as the header, described.
 */
export function HomePage() {
  const { user } = useAuth()

  if (!user) {
    return null
  }

  const firstName = user.fullName.trim().split(/\s+/)[0]
  const items = NAV_ITEMS[user.role]
  const primary = items.find((item) => item.to === PRIMARY_ACTION[user.role]) ?? items[0]

  return (
    <main className="mx-auto flex w-full max-w-7xl flex-col gap-10 px-4 py-8 sm:px-6 sm:py-10">
      <section className="relative overflow-hidden rounded-2xl bg-brand px-6 py-10 text-brand-foreground sm:px-12 sm:py-14">
        {/* Decorative: the mark, large and faint, bleeding off the corner. */}
        <img
          aria-hidden
          src="/logo-mark.png"
          alt=""
          className="pointer-events-none absolute -top-12 -right-12 h-80 w-auto opacity-10 brightness-0 invert sm:-right-6 sm:h-96"
        />

        <div className="relative max-w-2xl">
          <span className="inline-flex items-center rounded-full border border-white/20 px-3 py-1 text-xs font-medium tracking-wide uppercase">
            {WORKSPACE_LABELS[user.role]}
          </span>

          <h1 className="mt-5 font-heading text-3xl font-semibold tracking-tight text-balance sm:text-5xl">
            Welcome back, {firstName}.
          </h1>
          <p className="mt-4 max-w-xl text-base text-pretty text-brand-foreground/70 sm:text-lg">
            {ROLE_INTROS[user.role]}
          </p>

          <div className="mt-8 flex flex-col gap-3 sm:flex-row">
            <Button
              render={<Link to={primary.to} />}
              className="h-11 bg-brand-foreground px-5 text-brand hover:bg-brand-foreground/90"
            >
              {primary.label}
              <ArrowRight />
            </Button>
            <Button
              render={<Link to="/profile" />}
              variant="outline"
              className="h-11 border-white/25 bg-transparent px-5 text-brand-foreground hover:bg-white/10 hover:text-brand-foreground"
            >
              Manage your profile
            </Button>
          </div>
        </div>
      </section>

      <RoleDashboard role={user.role} />

      <section aria-labelledby="go-heading" className="flex flex-col gap-4">
        <h2 id="go-heading" className="font-heading text-lg font-semibold tracking-tight">
          Where would you like to go?
        </h2>

        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {items.map((item) => (
            <DestinationCard key={item.to} item={item} />
          ))}
          <DestinationCard
            item={{
              to: '/profile',
              label: 'Your profile',
              description: 'Keep your name and contact details up to date.',
              icon: CircleUser,
            }}
          />
        </div>
      </section>
    </main>
  )
}

function Stat({ label, value, dot }: { label: string; value: number; dot?: string }) {
  return (
    <Link
      to="/projects"
      className="rounded-xl bg-card p-5 ring-1 ring-foreground/10 transition-all hover:-translate-y-0.5 hover:ring-foreground/20"
    >
      <span className="flex items-center gap-2 text-sm text-muted-foreground">
        {dot && <span aria-hidden className={cn('size-2 rounded-full', dot)} />}
        {label}
      </span>
      <span className="mt-2 block font-heading text-4xl font-semibold tracking-tight">{value}</span>
    </Link>
  )
}

function DestinationCard({ item }: { item: NavItem }) {
  const Icon = item.icon

  return (
    <Link
      to={item.to}
      className="group relative rounded-xl bg-card p-5 ring-1 ring-foreground/10 transition-all outline-none hover:-translate-y-0.5 hover:ring-foreground/20 focus-visible:ring-2 focus-visible:ring-ring"
    >
      <span className="grid size-10 place-items-center rounded-lg bg-muted text-foreground">
        <Icon className="size-5" aria-hidden />
      </span>
      <h3 className="mt-4 font-heading text-sm font-semibold">{item.label}</h3>
      <p className="mt-1.5 text-sm text-pretty text-muted-foreground">{item.description}</p>
      <ArrowUpRight
        aria-hidden
        className="absolute top-5 right-5 size-4 text-muted-foreground opacity-0 transition-opacity group-hover:opacity-100 group-focus-visible:opacity-100"
      />
    </Link>
  )
}
