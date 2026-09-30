import { Link } from 'react-router-dom'

import { DashboardPanel } from '@/components/dashboard/DashboardPanel'
import { ProgressBar } from '@/components/dashboard/ProgressBar'
import { SliceProblems } from '@/components/dashboard/SliceProblems'
import { StatTile } from '@/components/dashboard/StatTile'
import { Badge } from '@/components/ui/badge'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import {
  CONSTRUCTION_PHASE_STATUS_LABELS,
  MILESTONE_STATUS_LABELS,
} from '@/lib/construction-api'
import {
  fetchProjectManagerConstructionDashboard,
  type ProjectManagerConstructionDashboard,
} from '@/lib/dashboard-api'
import { figureOf, shortId } from '@/lib/dashboard-view'
import { formatDay } from '@/lib/milestone-dates'
import { useDashboardSlice, type SliceState } from '@/lib/use-dashboard-slice'
import { cn } from '@/lib/utils'

/** Where the Project Manager reads build progress and billing across their projects. */
const BUILD_REPORT = '/reports/construction-payment'

/** A project's name as the service gave it, or its short id in the unlikely case it gave none. */
function nameOf(projectId: string, projectName: string | null): string {
  return projectName ?? shortId(projectId)
}

/**
 * The Project Manager's dashboard (US-21 AC-3): the builds under way, and the milestones
 * still to finish on them — on the projects they are assigned to.
 *
 * One request. The Construction Service does not know which Project Manager runs a project,
 * so it asks the Project Service, with the caller's own token, which projects are theirs and
 * reads only those; the same answer names them, so every build and milestone here says which
 * project it is without the page asking a second service.
 *
 * "Due" means outstanding: every milestone not yet completed. Where the Project Manager gave
 * a milestone a due date it can also be overdue, and the dated ones lead the list, most
 * overdue first. A milestone with no date is outstanding and never late.
 */
export function ProjectManagerDashboard() {
  const construction = useDashboardSlice(
    fetchProjectManagerConstructionDashboard,
    'Active construction could not be loaded right now.',
  )

  return (
    <div className="flex flex-col gap-10">
      <section aria-labelledby="summary-heading" className="flex flex-col gap-4">
        <h2 id="summary-heading" className="font-heading text-lg font-semibold tracking-tight">
          Construction at a glance
        </h2>

        <SliceProblems slices={[construction]} />

        <div className="grid grid-cols-2 gap-4">
          <StatTile
            label="Active builds"
            value={figureOf(construction, (data) => data.activeBuildCount)}
            hint="Started, and not yet handed over"
            dot="bg-orange-500"
            to={BUILD_REPORT}
          />
          <StatTile
            label="Milestones due"
            value={figureOf(construction, (data) => data.milestonesDue.totalCount)}
            hint={
              <>
                Not yet completed, on active builds
                {construction.status === 'ready' && construction.data.milestonesDue.overdueCount > 0 && (
                  <span className="text-destructive font-medium">
                    {' · '}
                    {construction.data.milestonesDue.overdueCount} overdue
                  </span>
                )}
              </>
            }
            dot="bg-amber-500"
            to={BUILD_REPORT}
          />
        </div>
      </section>

      <section aria-labelledby="builds-heading" className="flex flex-col gap-4">
        <h2 id="builds-heading" className="font-heading text-lg font-semibold tracking-tight">
          Active construction
        </h2>

        <ActiveBuilds construction={construction} />
      </section>

      <section aria-labelledby="due-heading" className="flex flex-col gap-4">
        <h2 id="due-heading" className="font-heading text-lg font-semibold tracking-tight">
          Milestones due
        </h2>

        <MilestonesDue construction={construction} />
      </section>
    </div>
  )
}

function ActiveBuilds({ construction }: { construction: SliceState<ProjectManagerConstructionDashboard> }) {
  if (construction.status === 'loading') {
    return <p className="text-muted-foreground text-sm">Loading active construction…</p>
  }

  // The reason is already stated once, above, by the problems list.
  if (construction.status === 'error') {
    return null
  }

  if (construction.data.activeBuilds.length === 0) {
    return (
      <DashboardPanel title="No builds under way">
        <p className="text-muted-foreground text-sm">
          Nothing has been started on the projects assigned to you. A build appears here once
          construction is started on one of them, and leaves it when the project is handed over.
        </p>
      </DashboardPanel>
    )
  }

  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>Project</TableHead>
          <TableHead>Phase</TableHead>
          <TableHead>Progress</TableHead>
          <TableHead className="text-right">Milestones left</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {construction.data.activeBuilds.map((build) => (
          <TableRow key={build.projectId}>
            <TableCell className="font-medium">
              <Link to={`/projects/${build.projectId}`} className="underline underline-offset-4">
                {nameOf(build.projectId, build.projectName)}
              </Link>
            </TableCell>
            <TableCell>
              {build.phaseStatus ? (
                <Badge variant="secondary">{CONSTRUCTION_PHASE_STATUS_LABELS[build.phaseStatus]}</Badge>
              ) : (
                <span className="text-muted-foreground text-sm">—</span>
              )}
            </TableCell>
            <TableCell>
              <div className="flex flex-col gap-1">
                <ProgressBar
                  percent={build.progressPercent}
                  label={`${nameOf(build.projectId, build.projectName)} construction progress`}
                />
                <span className="text-muted-foreground text-xs">
                  {build.completedMilestones} of {build.totalMilestones} milestones
                </span>
              </div>
            </TableCell>
            <TableCell className="text-right tabular-nums">{build.outstandingMilestones}</TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  )
}

function MilestonesDue({ construction }: { construction: SliceState<ProjectManagerConstructionDashboard> }) {
  if (construction.status === 'loading') {
    return <p className="text-muted-foreground text-sm">Loading milestones…</p>
  }

  if (construction.status === 'error') {
    return null
  }

  const { totalCount, overdueCount, milestones } = construction.data.milestonesDue

  if (milestones.length === 0) {
    return (
      <DashboardPanel title="Nothing outstanding">
        <p className="text-muted-foreground text-sm">
          Every milestone on the builds under way is complete.
        </p>
      </DashboardPanel>
    )
  }

  const order = 'Dated ones first, soonest first, then the rest — those in progress ahead of those not started.'

  return (
    <DashboardPanel
      title="Still to finish"
      description={totalCount > milestones.length ? `Showing ${milestones.length} of ${totalCount}. ${order}` : order}
    >
      {overdueCount > 0 && (
        <p className="text-destructive text-sm font-medium">
          {overdueCount} overdue {overdueCount === 1 ? 'milestone' : 'milestones'}
        </p>
      )}
      <ul className="flex flex-col divide-y">
        {milestones.map((milestone) => (
          <li key={milestone.id} className="flex flex-wrap items-center justify-between gap-2 py-2 first:pt-0 last:pb-0">
            <div className="flex flex-col">
              <span className="text-sm font-medium">{milestone.name}</span>
              <span className="text-muted-foreground text-xs">
                {nameOf(milestone.projectId, milestone.projectName)}
              </span>
              {milestone.dueDate && (
                <span className={cn('text-xs', milestone.isOverdue ? 'text-destructive font-medium' : 'text-muted-foreground')}>
                  {milestone.isOverdue ? 'Overdue — was due' : 'Due'} {formatDay(milestone.dueDate)}
                </span>
              )}
            </div>
            <Badge variant={milestone.status === 'InProgress' ? 'default' : 'secondary'}>
              {MILESTONE_STATUS_LABELS[milestone.status]}
            </Badge>
          </li>
        ))}
      </ul>
    </DashboardPanel>
  )
}
