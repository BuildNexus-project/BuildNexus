import { Link } from 'react-router-dom'

import { DashboardPanel } from '@/components/dashboard/DashboardPanel'
import { SliceProblems } from '@/components/dashboard/SliceProblems'
import { StatTile } from '@/components/dashboard/StatTile'
import { StatusBadge } from '@/components/StatusBadge'
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
  fetchArchitectDesignDashboard,
  fetchArchitectProjectsDashboard,
  type ArchitectDesignDashboard,
  type ArchitectProjectsDashboard,
} from '@/lib/dashboard-api'
import {
  daysSince,
  figureOf,
  formatDate,
  projectNamer,
  revisionCountsByProject,
} from '@/lib/dashboard-view'
import { useDashboardSlice, type SliceState } from '@/lib/use-dashboard-slice'

/**
 * The Architect's dashboard (US-21 AC-2): the projects they are assigned to, and the
 * revisions Clients have sent back that they have not yet answered.
 *
 * Two requests to two services — which projects are theirs is the Project Service's
 * data, which revisions are pending is the Design Service's — joined here on the project
 * id. They load and fail separately, so a Design Service that is down leaves the
 * assigned projects exactly as useful as they were.
 */
export function ArchitectDashboard() {
  const projects = useDashboardSlice(
    fetchArchitectProjectsDashboard,
    'Your assigned projects could not be loaded right now.',
  )
  const design = useDashboardSlice(
    fetchArchitectDesignDashboard,
    'Pending revisions could not be loaded right now.',
  )

  return (
    <div className="flex flex-col gap-10">
      <section aria-labelledby="summary-heading" className="flex flex-col gap-4">
        <h2 id="summary-heading" className="font-heading text-lg font-semibold tracking-tight">
          Your assigned projects at a glance
        </h2>

        <SliceProblems slices={[projects, design]} />

        <div className="grid grid-cols-2 gap-4">
          <StatTile
            label="Assigned projects"
            value={figureOf(projects, (data) => data.assignedCount)}
            to="/projects"
          />
          <StatTile
            label="Pending revisions"
            value={figureOf(design, (data) => data.pendingRevisionCount)}
            hint={
              design.status === 'ready'
                ? design.data.pendingRevisionCount === 0
                  ? 'Nothing sent back'
                  : 'Sent back by clients'
                : undefined
            }
            dot="bg-amber-500"
          />
        </div>
      </section>

      <section aria-labelledby="revisions-heading" className="flex flex-col gap-4">
        <h2 id="revisions-heading" className="font-heading text-lg font-semibold tracking-tight">
          Revisions to make
        </h2>

        <PendingRevisions design={design} projects={projects} />
      </section>

      <section aria-labelledby="assigned-heading" className="flex flex-col gap-4">
        <h2 id="assigned-heading" className="font-heading text-lg font-semibold tracking-tight">
          Your assigned projects
        </h2>

        <AssignedProjects projects={projects} design={design} />
      </section>
    </div>
  )
}

/** How long a revision has been waiting, as a person would say it: "today", "1 day", "5 days". */
function waiting(days: number): string {
  if (days === 0) {
    return 'asked for today'
  }

  return `waiting ${days} ${days === 1 ? 'day' : 'days'}`
}

function PendingRevisions({
  design,
  projects,
}: {
  design: SliceState<ArchitectDesignDashboard>
  projects: SliceState<ArchitectProjectsDashboard>
}) {
  if (design.status === 'loading') {
    return <p className="text-muted-foreground text-sm">Loading your revisions…</p>
  }

  // The reason is already stated once, above, by the problems list.
  if (design.status === 'error') {
    return null
  }

  if (design.data.revisions.length === 0) {
    return (
      <DashboardPanel title="Nothing to redo">
        <p className="text-muted-foreground text-sm">
          No revisions are waiting. When a Client sends a design back with changes, it appears here
          until you upload the next version.
        </p>
      </DashboardPanel>
    )
  }

  // Names come from the Project Service, which the revisions did not: the Design Service
  // holds project ids and nothing else. One it cannot name falls back to a short id.
  const nameOf = projectNamer(projects.status === 'ready' ? projects.data.projects : [])

  return (
    <ul className="flex flex-col gap-3">
      {design.data.revisions.map((revision) => (
        <li key={revision.documentId}>
          <DashboardPanel
            title={`${revision.documentName} · version ${revision.versionNumber}`}
            description={nameOf(revision.projectId)}
          >
            {revision.reviewComment && (
              <p className="text-sm">
                <span className="text-muted-foreground">The client asked: </span>“{revision.reviewComment}”
              </p>
            )}
            <div className="flex flex-wrap items-center justify-between gap-2">
              <span className="text-muted-foreground text-xs">
                Requested {formatDate(revision.requestedAt)} · {waiting(daysSince(revision.requestedAt))}
              </span>
              <Link
                to={`/projects/${revision.projectId}/designs`}
                className="text-sm underline underline-offset-4"
              >
                Upload the next version
              </Link>
            </div>
          </DashboardPanel>
        </li>
      ))}
    </ul>
  )
}

function AssignedProjects({
  projects,
  design,
}: {
  projects: SliceState<ArchitectProjectsDashboard>
  design: SliceState<ArchitectDesignDashboard>
}) {
  if (projects.status === 'loading') {
    return <p className="text-muted-foreground text-sm">Loading your projects…</p>
  }

  if (projects.status === 'error') {
    return null
  }

  if (projects.data.projects.length === 0) {
    return (
      <DashboardPanel title="Nothing assigned yet">
        <p className="text-muted-foreground text-sm">
          No project has been assigned to you yet. An administrator puts an architect on a project.
        </p>
      </DashboardPanel>
    )
  }

  const revisionCounts = design.status === 'ready' ? revisionCountsByProject(design.data.revisions) : null

  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>Project</TableHead>
          <TableHead>Status</TableHead>
          <TableHead>Revisions</TableHead>
          <TableHead className="text-right">Last moved</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {projects.data.projects.map((project) => {
          const waiting = revisionCounts?.get(project.id) ?? 0

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
                {revisionCounts === null ? (
                  <span
                    className="text-muted-foreground text-sm"
                    aria-label={design.status === 'loading' ? 'Loading' : 'Unavailable'}
                  >
                    {design.status === 'loading' ? '…' : '—'}
                  </span>
                ) : waiting > 0 ? (
                  <Badge variant="default">
                    {waiting} to make
                  </Badge>
                ) : (
                  <span className="text-muted-foreground text-sm">None</span>
                )}
              </TableCell>
              <TableCell className="text-right text-sm tabular-nums">{formatDate(project.updatedAt)}</TableCell>
            </TableRow>
          )
        })}
      </TableBody>
    </Table>
  )
}
