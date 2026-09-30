import { Link } from 'react-router-dom'

import { DashboardPanel } from '@/components/dashboard/DashboardPanel'
import { SliceProblems } from '@/components/dashboard/SliceProblems'
import { StatTile } from '@/components/dashboard/StatTile'
import { StatusBadge } from '@/components/StatusBadge'
import {
  fetchAdminProjectsDashboard,
  fetchAdminUsersDashboard,
  type AdminProjectsDashboard,
  type AdminUsersDashboard,
} from '@/lib/dashboard-api'
import { figureOf } from '@/lib/dashboard-view'
import { NAV_ITEMS } from '@/lib/nav'
import { ROLE_LABELS } from '@/lib/roles'
import { useDashboardSlice, type SliceState } from '@/lib/use-dashboard-slice'

/** A project in either of these is done with; everything else is still being worked on. */
const FINISHED_STATUSES = ['Completed', 'Cancelled']

/**
 * The reports the Admin can open. Taken from the Admin's own navigation, so a report added
 * there appears here without this page being told, and one the Admin cannot open is never
 * offered.
 */
const REPORT_LINKS = NAV_ITEMS.Admin.filter((item) => item.to.includes('reports'))

/**
 * The Admin's dashboard (US-21 AC-4): how many users and projects the system holds, and
 * the reports to open.
 *
 * Two requests to two services — users are the User Service's data, projects the Project
 * Service's — and they load and fail separately, so an outage in one leaves the other's
 * counts on screen.
 *
 * Reports get links and no number. Nothing stores a report: each of the three is
 * generated on demand from other data, so there is no count of them to read, and inventing
 * one would be a figure that answers to nothing.
 */
export function AdminDashboard() {
  const users = useDashboardSlice(fetchAdminUsersDashboard, 'User counts could not be loaded right now.')
  const projects = useDashboardSlice(
    fetchAdminProjectsDashboard,
    'Project counts could not be loaded right now.',
  )

  return (
    <div className="flex flex-col gap-10">
      <section aria-labelledby="summary-heading" className="flex flex-col gap-4">
        <h2 id="summary-heading" className="font-heading text-lg font-semibold tracking-tight">
          The whole platform at a glance
        </h2>

        <SliceProblems slices={[users, projects]} />

        <div className="grid grid-cols-2 gap-4">
          <StatTile
            label="Users"
            value={figureOf(users, (data) => data.totalUsers)}
            hint={
              users.status === 'ready'
                ? `${users.data.activeUsers} active · ${users.data.inactiveUsers} deactivated`
                : undefined
            }
            dot="bg-sky-500"
            to="/admin/users"
          />
          <StatTile
            label="Projects"
            value={figureOf(projects, (data) => data.totalCount)}
            hint={projects.status === 'ready' ? `${activeProjects(projects.data)} still active` : undefined}
            dot="bg-orange-500"
            to="/admin/reports/project-status"
          />
        </div>
      </section>

      <section aria-labelledby="breakdown-heading" className="flex flex-col gap-4">
        <h2 id="breakdown-heading" className="font-heading text-lg font-semibold tracking-tight">
          System-wide counts
        </h2>

        <div className="grid gap-4 lg:grid-cols-3">
          <UsersByRole users={users} />
          <ProjectsByStatus projects={projects} />
          <Reports />
        </div>
      </section>
    </div>
  )
}

/** Projects that are neither completed nor cancelled. */
function activeProjects(projects: AdminProjectsDashboard): number {
  return projects.groups
    .filter((group) => !FINISHED_STATUSES.includes(group.status))
    .reduce((sum, group) => sum + group.count, 0)
}

function UsersByRole({ users }: { users: SliceState<AdminUsersDashboard> }) {
  return (
    <DashboardPanel title="Users by role">
      {users.status === 'loading' && <p className="text-muted-foreground text-sm">Loading…</p>}
      {users.status === 'error' && <p className="text-muted-foreground text-sm">Not available.</p>}
      {users.status === 'ready' && (
        <dl className="flex flex-col gap-2">
          {users.data.roles.map((entry) => (
            <div key={entry.role} className="flex items-center justify-between text-sm">
              <dt>{ROLE_LABELS[entry.role]}</dt>
              <dd className="tabular-nums">{entry.count}</dd>
            </div>
          ))}
        </dl>
      )}
    </DashboardPanel>
  )
}

function ProjectsByStatus({ projects }: { projects: SliceState<AdminProjectsDashboard> }) {
  return (
    <DashboardPanel title="Projects by status">
      {projects.status === 'loading' && <p className="text-muted-foreground text-sm">Loading…</p>}
      {projects.status === 'error' && <p className="text-muted-foreground text-sm">Not available.</p>}
      {projects.status === 'ready' && (
        <dl className="flex flex-col gap-2">
          {projects.data.groups.map((group) => (
            <div key={group.status} className="flex items-center justify-between text-sm">
              <dt>
                <StatusBadge status={group.status} />
              </dt>
              <dd className="tabular-nums">{group.count}</dd>
            </div>
          ))}
        </dl>
      )}
    </DashboardPanel>
  )
}

function Reports() {
  return (
    <DashboardPanel title="Reports" description="Generated on demand from the data above.">
      <ul className="flex flex-col gap-3">
        {REPORT_LINKS.map((report) => (
          <li key={report.to}>
            <Link to={report.to} className="text-sm font-medium underline underline-offset-4">
              {report.label}
            </Link>
            <p className="text-muted-foreground text-xs">{report.description}</p>
          </li>
        ))}
      </ul>
    </DashboardPanel>
  )
}
