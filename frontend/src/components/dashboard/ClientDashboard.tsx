import { Link } from 'react-router-dom'

import { DashboardPanel } from '@/components/dashboard/DashboardPanel'
import { ProgressBar } from '@/components/dashboard/ProgressBar'
import { SliceProblems } from '@/components/dashboard/SliceProblems'
import { StatTile } from '@/components/dashboard/StatTile'
import { StatusBadge } from '@/components/StatusBadge'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import {
  DESIGN_STATE_LABELS,
  fetchClientConstructionDashboard,
  fetchClientDesignDashboard,
  fetchClientPaymentsDashboard,
  fetchClientProjectsDashboard,
  type ClientConstructionDashboard,
  type ClientDesignDashboard,
  type ClientPaymentsDashboard,
  type ClientProjectsDashboard,
  type DesignState,
} from '@/lib/dashboard-api'
import {
  designStateCounts,
  figureOf,
  formatMoney,
  indexBy,
  outstandingByProject,
  overallProgress,
} from '@/lib/dashboard-view'
import { useDashboardSlice, type SliceState } from '@/lib/use-dashboard-slice'
import { cn } from '@/lib/utils'

/** How a design state is drawn: the one that needs the Client loudest, the rest quieter. */
const DESIGN_BADGE: Record<DesignState, { variant: 'default' | 'secondary' | 'outline'; className?: string }> = {
  AwaitingReview: { variant: 'default' },
  RevisionRequested: { variant: 'secondary' },
  Approved: { variant: 'outline' },
  NoDesign: { variant: 'outline', className: 'text-muted-foreground' },
}

/**
 * The Client's dashboard (US-21 AC-1): their active projects, where the design stands on
 * each, how far the build has got, and what they still owe.
 *
 * Four requests to four services — the projects, the design status, the build progress
 * and the payments due are each a different service's data — joined here on the project
 * id. They load and fail separately: a slow Payment Service leaves a dash in the
 * payments tile and the rest of the dashboard exactly as useful as it was.
 */
export function ClientDashboard() {
  const projects = useDashboardSlice(
    fetchClientProjectsDashboard,
    'Your projects could not be loaded right now.',
  )
  const design = useDashboardSlice(
    fetchClientDesignDashboard,
    'Design status could not be loaded right now.',
  )
  const construction = useDashboardSlice(
    fetchClientConstructionDashboard,
    'Build progress could not be loaded right now.',
  )
  const payments = useDashboardSlice(
    fetchClientPaymentsDashboard,
    'Payments due could not be loaded right now.',
  )

  return (
    <div className="flex flex-col gap-10">
      <section aria-labelledby="summary-heading" className="flex flex-col gap-4">
        <h2 id="summary-heading" className="font-heading text-lg font-semibold tracking-tight">
          Your projects at a glance
        </h2>

        <SliceProblems slices={[projects, design, construction, payments]} />

        <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
          <StatTile
            label="Active projects"
            value={figureOf(projects, (data) => data.activeCount)}
            to="/projects"
          />
          <StatTile
            label="Designs awaiting your review"
            value={figureOf(design, (data) => designStateCounts(data.projects).AwaitingReview)}
            hint={
              design.status === 'ready'
                ? `${designStateCounts(design.data.projects).Approved} of ${design.data.projects.length} approved`
                : undefined
            }
            dot="bg-sky-500"
            to="/projects"
          />
          <StatTile
            label="Build progress"
            value={figureOf(construction, (data) => {
              const progress = overallProgress(data.projects)

              return progress ? `${progress.percent}%` : '—'
            })}
            hint={
              construction.status === 'ready'
                ? buildProgressHint(overallProgress(construction.data.projects))
                : undefined
            }
            dot="bg-orange-500"
            to="/progress"
          />
          <StatTile
            label="Payments due"
            value={figureOf(payments, (data) => formatMoney(data.totalDue))}
            hint={
              payments.status === 'ready'
                ? payments.data.invoiceCount === 0
                  ? 'Nothing owing'
                  : `${payments.data.invoiceCount} unpaid invoice${payments.data.invoiceCount === 1 ? '' : 's'}`
                : undefined
            }
            dot="bg-emerald-500"
            to="/my-costs"
          />
        </div>
      </section>

      <section aria-labelledby="active-projects-heading" className="flex flex-col gap-4">
        <h2 id="active-projects-heading" className="font-heading text-lg font-semibold tracking-tight">
          Your active projects
        </h2>

        <ActiveProjects
          projects={projects}
          design={design}
          construction={construction}
          payments={payments}
        />
      </section>
    </div>
  )
}

/** The line under the build-progress figure: what it is made of, or why there is no figure. */
function buildProgressHint(progress: ReturnType<typeof overallProgress>): string {
  return progress
    ? `${progress.completed} of ${progress.total} milestones done`
    : 'No build planned yet'
}

function ActiveProjects({
  projects,
  design,
  construction,
  payments,
}: {
  projects: SliceState<ClientProjectsDashboard>
  design: SliceState<ClientDesignDashboard>
  construction: SliceState<ClientConstructionDashboard>
  payments: SliceState<ClientPaymentsDashboard>
}) {
  if (projects.status === 'loading') {
    return <p className="text-muted-foreground text-sm">Loading your projects…</p>
  }

  // The reason is already stated once, above, by the problems list — repeating it here
  // would be the same sentence twice on one screen.
  if (projects.status === 'error') {
    return null
  }

  if (projects.data.projects.length === 0) {
    return (
      <DashboardPanel title="Nothing under way">
        <p className="text-muted-foreground text-sm">
          You have no active projects. Describe the building you want and the company will pick it
          up.
        </p>
        <div>
          <Button render={<Link to="/projects/new" />}>Start a project</Button>
        </div>
      </DashboardPanel>
    )
  }

  const designByProject = design.status === 'ready' ? indexBy(design.data.projects, (d) => d.projectId) : null
  const progressByProject =
    construction.status === 'ready' ? indexBy(construction.data.projects, (p) => p.projectId) : null
  const dueByProject = payments.status === 'ready' ? outstandingByProject(payments.data.invoices) : null

  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>Project</TableHead>
          <TableHead>Status</TableHead>
          <TableHead>Design</TableHead>
          <TableHead>Build progress</TableHead>
          <TableHead className="text-right">Payment due</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {projects.data.projects.map((project) => {
          const designEntry = designByProject?.get(project.id)
          const build = progressByProject?.get(project.id)
          const due = dueByProject?.get(project.id)

          return (
            <TableRow key={project.id}>
              <TableCell className="font-medium">
                <Link to={`/projects/${project.id}`} className="underline underline-offset-4">
                  {project.name}
                </Link>
                <span className="text-muted-foreground block text-xs font-normal">{project.location}</span>
              </TableCell>
              <TableCell>
                <StatusBadge status={project.status} />
              </TableCell>
              <TableCell>
                {designEntry ? (
                  <Badge
                    variant={DESIGN_BADGE[designEntry.state].variant}
                    className={cn(DESIGN_BADGE[designEntry.state].className)}
                  >
                    {DESIGN_STATE_LABELS[designEntry.state]}
                  </Badge>
                ) : (
                  <Unavailable slice={design} />
                )}
              </TableCell>
              <TableCell>
                {build ? (
                  <div className="flex flex-col gap-1">
                    <ProgressBar percent={build.progressPercent} label={`${project.name} build progress`} />
                    <span className="text-muted-foreground text-xs">
                      {build.completedMilestones} of {build.totalMilestones} milestones
                    </span>
                  </div>
                ) : construction.status === 'ready' ? (
                  <span className="text-muted-foreground text-sm">Not planned yet</span>
                ) : (
                  <Unavailable slice={construction} />
                )}
              </TableCell>
              <TableCell className="text-right tabular-nums">
                {payments.status !== 'ready' ? (
                  <Unavailable slice={payments} />
                ) : due ? (
                  formatMoney(due)
                ) : (
                  <span className="text-muted-foreground">Nothing due</span>
                )}
              </TableCell>
            </TableRow>
          )
        })}
      </TableBody>
    </Table>
  )
}

/** A cell whose slice has not arrived, or never will: an ellipsis while it is coming, a dash if it is not. */
function Unavailable({ slice }: { slice: SliceState<unknown> }) {
  return (
    <span className="text-muted-foreground text-sm" aria-label={slice.status === 'loading' ? 'Loading' : 'Unavailable'}>
      {slice.status === 'loading' ? '…' : '—'}
    </span>
  )
}
