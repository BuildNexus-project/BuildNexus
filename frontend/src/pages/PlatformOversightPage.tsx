import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'

import { useAuth } from '@/auth/auth-context'
import { StatusBadge } from '@/components/StatusBadge'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { apiErrorMessage } from '@/lib/api'
import { NAV_ITEMS } from '@/lib/nav'
import {
  daysSinceUpdate,
  fetchProjectOversight,
  staffLabel,
  type OversightProject,
  type ProjectOversight,
} from '@/lib/oversight-api'

const LOAD_FAILED = 'Could not load the projects. Please try again.'

/**
 * The reports the Admin can open. Taken from the Admin's own navigation, so a report added
 * there appears here without this page being told, and one the Admin cannot open is never
 * offered.
 */
const REPORT_LINKS = NAV_ITEMS.Admin.filter((item) => item.to.includes('reports'))

/** A date the service sent, as a reader would write it. */
function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  })
}

/**
 * Platform oversight (US-38): every project on the platform with its status, who is on it
 * and when it last moved, and one place to open the full reporting suite from.
 *
 * Admin only — the route guards it, and the endpoint behind it answers 403 to anyone else.
 *
 * Stalled projects (an open project that has not moved for a while) are highlighted. The
 * service decides which are stalled and says how long "a while" is; this page only shows it,
 * and says "Stalled" in words as well as colour so it never depends on seeing the colour.
 */
export function PlatformOversightPage() {
  const { authFetch } = useAuth()

  const [oversight, setOversight] = useState<ProjectOversight | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [stalledOnly, setStalledOnly] = useState(false)

  useEffect(() => {
    let cancelled = false

    fetchProjectOversight(authFetch)
      .then((loaded) => {
        if (!cancelled) {
          setOversight(loaded)
        }
      })
      .catch((error: unknown) => {
        if (!cancelled) {
          setLoadError(apiErrorMessage(error, LOAD_FAILED))
        }
      })

    return () => {
      cancelled = true
    }
  }, [authFetch])

  const shown = oversight?.projects.filter((project) => !stalledOnly || project.isStalled) ?? []

  return (
    <main className="mx-auto flex w-full max-w-7xl flex-col gap-6 px-4 py-8 sm:px-6 sm:py-10">
      <Card>
        <CardHeader>
          <CardTitle role="heading" aria-level={2}>
            Reports
          </CardTitle>
          <CardDescription>
            The full reporting suite, from one place. Each report is generated on demand.
          </CardDescription>
        </CardHeader>

        <CardContent>
          <ul className="grid gap-3 md:grid-cols-3">
            {REPORT_LINKS.map((report) => {
              const Icon = report.icon

              return (
                <li key={report.to}>
                  <Link
                    to={report.to}
                    className="hover:bg-muted/50 focus-visible:ring-ring flex h-full flex-col gap-1 rounded-lg border p-4 outline-none focus-visible:ring-2"
                  >
                    <span className="flex items-center gap-2 text-sm font-medium">
                      <Icon aria-hidden className="size-4" />
                      {report.label}
                    </span>
                    <span className="text-muted-foreground text-xs">{report.description}</span>
                  </Link>
                </li>
              )
            })}
          </ul>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle role="heading" aria-level={2}>
            All projects
          </CardTitle>
          <CardDescription>
            Every project on the platform, whoever submitted it. Open one to see its details and
            history.
          </CardDescription>
        </CardHeader>

        <CardContent className="flex flex-col gap-4">
          {loadError && (
            <p role="alert" className="text-destructive text-sm">
              {loadError}
            </p>
          )}

          {!loadError && !oversight && (
            <p role="status" className="text-muted-foreground text-sm">
              Loading the projects…
            </p>
          )}

          {oversight && (
            <>
              <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                <p data-testid="oversight-summary" className="text-sm">
                  <span className="font-medium">
                    {oversight.totalProjects}{' '}
                    {oversight.totalProjects === 1 ? 'project' : 'projects'}
                  </span>
                  <span className="text-muted-foreground">
                    {' '}
                    · {oversight.stalledCount} stalled (open, with no update for{' '}
                    {oversight.stalledAfterDays} days or more)
                  </span>
                </p>

                <label className="text-muted-foreground flex w-fit items-center gap-2 text-sm">
                  <input
                    type="checkbox"
                    checked={stalledOnly}
                    onChange={(event) => setStalledOnly(event.target.checked)}
                    className="border-input size-4 rounded"
                  />
                  Show only stalled projects
                </label>
              </div>

              {oversight.totalProjects === 0 && (
                <p className="text-muted-foreground text-sm">There are no projects yet.</p>
              )}

              {oversight.totalProjects > 0 && shown.length === 0 && (
                <p className="text-muted-foreground text-sm">No projects are stalled.</p>
              )}

              {shown.length > 0 && (
                <div className="overflow-x-auto">
                  <Table>
                    <TableHeader>
                      <TableRow>
                        <TableHead>Project</TableHead>
                        <TableHead>Status</TableHead>
                        <TableHead>Architect</TableHead>
                        <TableHead>Project manager</TableHead>
                        <TableHead>Last updated</TableHead>
                      </TableRow>
                    </TableHeader>

                    <TableBody>
                      {shown.map((project) => (
                        <OversightRow key={project.id} project={project} />
                      ))}
                    </TableBody>
                  </Table>
                </div>
              )}
            </>
          )}

          <p className="text-muted-foreground text-center text-sm">
            <Link to="/home" className="text-foreground underline underline-offset-4">
              Back to home
            </Link>
          </p>
        </CardContent>
      </Card>
    </main>
  )
}

function OversightRow({ project }: { project: OversightProject }) {
  return (
    <TableRow
      data-testid={`project-${project.id}`}
      data-stalled={project.isStalled ? 'true' : undefined}
      className={project.isStalled ? 'bg-amber-500/10' : undefined}
    >
      <TableCell className="font-medium">
        <Link to={`/projects/${project.id}`} className="underline underline-offset-4">
          {project.name}
        </Link>
        <span className="text-muted-foreground block text-xs font-normal">{project.location}</span>
      </TableCell>
      <TableCell>
        <StatusBadge status={project.status} />
      </TableCell>
      <StaffCell name={project.assignedArchitectName} id={project.assignedArchitectId} />
      <StaffCell name={project.assignedProjectManagerName} id={project.assignedProjectManagerId} />
      <TableCell className="tabular-nums">
        {formatDate(project.updatedAt)}
        {project.isStalled && (
          <Badge variant="outline" className="ml-2 border-amber-500 text-amber-700 dark:text-amber-400">
            Stalled · {daysSinceUpdate(project.updatedAt)} days
          </Badge>
        )}
      </TableCell>
    </TableRow>
  )
}

function StaffCell({ name, id }: { name: string | null; id: string | null }) {
  return (
    <TableCell className={id === null ? 'text-muted-foreground' : undefined}>
      {staffLabel(name, id)}
    </TableCell>
  )
}
