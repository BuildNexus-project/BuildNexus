import { ArrowRight, ArrowUpRight, CircleUser } from 'lucide-react'
import { Link } from 'react-router-dom'

import { useAuth } from '@/auth/auth-context'
import { AdminDashboard } from '@/components/dashboard/AdminDashboard'
import { ArchitectDashboard } from '@/components/dashboard/ArchitectDashboard'
import { ClientDashboard } from '@/components/dashboard/ClientDashboard'
import { NotificationsPanel } from '@/components/dashboard/NotificationsPanel'
import { ProjectManagerDashboard } from '@/components/dashboard/ProjectManagerDashboard'
import { Button } from '@/components/ui/button'
import { NAV_ITEMS, WORKSPACE_LABELS, type NavItem } from '@/lib/nav'
import { NOTIFICATION_ROLES, type Role } from '@/lib/roles'

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

/**
 * The dashboard for a role (US-21). Each one reads from the several services that hold
 * that role's data and puts them together, showing only what the role's own work is made
 * of — never one generic dashboard for everyone to interpret for themselves.
 */
function RoleDashboard({ role }: { role: Role }) {
  switch (role) {
    case 'Client':
      return <ClientDashboard />
    case 'Architect':
      return <ArchitectDashboard />
    case 'ProjectManager':
      return <ProjectManagerDashboard />
    case 'Admin':
      return <AdminDashboard />
  }
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

      {NOTIFICATION_ROLES.includes(user.role) && <NotificationsPanel />}

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
