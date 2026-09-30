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
import { figureOf, projectNamer } from '@/lib/dashboard-view'
import { fetchProjects } from '@/lib/project-api'
import { useDashboardSlice, type SliceState } from '@/lib/use-dashboard-slice'

/** Where the Project Manager reads build progress and billing across the portfolio. */
const BUILD_REPORT = '/reports/construction-payment'

/**
 * The Project Manager's dashboard (US-21 AC-3): the builds under way, and the milestones
 * still to finish on them.
 *
 * The figures come from the Construction Service, and they are portfolio-wide: that
 * service records who owns a project but not which Project Manager runs it, so it cannot
 * narrow to "your" builds — the same scope the Build & payment report has.
 *
 * "Due" means outstanding. Milestones carry no due date, only NotStarted, InProgress and
 * Completed, so there is nothing to be late against; this is what is still to finish.
 *
 * Project names are a second, separate request to the Project Service, and best-effort:
 * a name belongs to that service, which lists only the projects this Project Manager is
 * assigned to. A build it cannot name is shown by its short id rather than hidden, and a
 * failed name lookup is not a reason to put an error on a dashboard whose figures loaded.
 */
export function ProjectManagerDashboard() {
  const construction = useDashboardSlice(
    fetchProjectManagerConstructionDashboard,
    'Active construction could not be loaded right now.',
  )
  const projects = useDashboardSlice(fetchProjects)

  const nameOf = projectNamer(projects.status === 'ready' ? projects.data : [])
  // Only a project the Project Service listed is one this person may open.
  const openable = new Set(projects.status === 'ready' ? projects.data.map((project) => project.id) : [])

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
            hint="Not yet completed, on active builds"
            dot="bg-amber-500"
            to={BUILD_REPORT}
          />
        </div>
      </section>

      <section aria-labelledby="builds-heading" className="flex flex-col gap-4">
        <h2 id="builds-heading" className="font-heading text-lg font-semibold tracking-tight">
          Active construction
        </h2>

        <ActiveBuilds construction={construction} nameOf={nameOf} openable={openable} />
      </section>

      <section aria-labelledby="due-heading" className="flex flex-col gap-4">
        <h2 id="due-heading" className="font-heading text-lg font-semibold tracking-tight">
          Milestones due
        </h2>

        <MilestonesDue construction={construction} nameOf={nameOf} />
      </section>
    </div>
  )
}

function ActiveBuilds({
  construction,
  nameOf,
  openable,
}: {
  construction: SliceState<ProjectManagerConstructionDashboard>
  nameOf: (projectId: string) => string
  openable: ReadonlySet<string>
}) {
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
          Nothing has been started. A build appears here once construction is started on a project,
          and leaves it when the project is handed over.
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
              <ProjectLabel projectId={build.projectId} nameOf={nameOf} openable={openable} />
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
                  label={`${nameOf(build.projectId)} construction progress`}
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

function MilestonesDue({
  construction,
  nameOf,
}: {
  construction: SliceState<ProjectManagerConstructionDashboard>
  nameOf: (projectId: string) => string
}) {
  if (construction.status === 'loading') {
    return <p className="text-muted-foreground text-sm">Loading milestones…</p>
  }

  if (construction.status === 'error') {
    return null
  }

  const { totalCount, milestones } = construction.data.milestonesDue

  if (milestones.length === 0) {
    return (
      <DashboardPanel title="Nothing outstanding">
        <p className="text-muted-foreground text-sm">
          Every milestone on the builds under way is complete.
        </p>
      </DashboardPanel>
    )
  }

  return (
    <DashboardPanel
      title="Still to finish"
      description={
        totalCount > milestones.length
          ? `Showing ${milestones.length} of ${totalCount} — those in progress first, then the next in line.`
          : 'Those in progress first, then the next in line.'
      }
    >
      <ul className="flex flex-col divide-y">
        {milestones.map((milestone) => (
          <li key={milestone.id} className="flex flex-wrap items-center justify-between gap-2 py-2 first:pt-0 last:pb-0">
            <div className="flex flex-col">
              <span className="text-sm font-medium">{milestone.name}</span>
              <span className="text-muted-foreground text-xs">{nameOf(milestone.projectId)}</span>
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

/** A project's name, as a link when the Project Manager may open it and as plain text when not. */
function ProjectLabel({
  projectId,
  nameOf,
  openable,
}: {
  projectId: string
  nameOf: (projectId: string) => string
  openable: ReadonlySet<string>
}) {
  if (!openable.has(projectId)) {
    return <span>{nameOf(projectId)}</span>
  }

  return (
    <Link to={`/projects/${projectId}`} className="underline underline-offset-4">
      {nameOf(projectId)}
    </Link>
  )
}
