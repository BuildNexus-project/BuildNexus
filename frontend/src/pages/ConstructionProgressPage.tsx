import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'

import { useAuth } from '@/auth/auth-context'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { ApiError, apiErrorMessage } from '@/lib/api'
import {
  CONSTRUCTION_PHASE_STATUS_LABELS,
  fetchProjectProgressSummary,
  MILESTONE_STATUS_LABELS,
  type ConstructionPhaseSummary,
  type MilestoneStatus,
  type ProjectProgressSummary,
} from '@/lib/construction-api'
import { fetchProjects, type ProjectSummary } from '@/lib/project-api'
import { PROJECT_STATUS_LABELS, type ProjectStatus } from '@/lib/project-status'
import { useAutoRefresh } from '@/lib/use-auto-refresh'

/**
 * The statuses a project can have construction progress under.
 *
 * The same three the Project Manager's milestone section uses: the Construction
 * Service only answers for a project whose `milestone_setups` row exists, and
 * that row is planted when the design is approved. Asking for a `Pending` or
 * `Designing` project would earn a 404 that means "not yet" rather than
 * anything wrong, so those are shown as waiting instead of being requested.
 * `Cancelled` is left out: a closed-out project has no build to watch.
 */
const BUILDABLE_STATUSES: readonly ProjectStatus[] = ['DesignApproved', 'Construction', 'Completed']

/**
 * What the dashboard knows about one project's build.
 *
 * `summary` is `null` for a project the service has no plan for yet — a 404,
 * which is a real state (the design was approved moments ago and the
 * `DesignApproved` event has not been consumed) rather than a failure. `error`
 * carries anything else that went wrong for that one project, so a single
 * failing project does not blank the whole page.
 */
type ProjectProgressEntry = {
  project: ProjectSummary
  summary: ProjectProgressSummary | null
  error: string | null
}

/** The badge tone for each milestone status — completed reads as done, not as neutral. */
const MILESTONE_BADGE_VARIANT: Record<MilestoneStatus, 'default' | 'secondary' | 'outline'> = {
  NotStarted: 'outline',
  InProgress: 'secondary',
  Completed: 'default',
}

/** A timestamp the service sent, as a reader would write it. */
function formatTime(iso: string): string {
  return new Date(iso).toLocaleString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
  })
}

/**
 * One project's build: the overall rollup as a percentage and a bar, and every
 * milestone with where it stands (US-13 AC-1).
 */
/**
 * Where the Client's build stands, and — once it gets there — that the project has
 * been handed over to them (US-14 AC-4).
 *
 * Rendered above the milestone rollup because it is the headline: a client whose
 * project is finished and delivered should read that first, not infer it from a bar
 * at 100%. A build merely under way says so quietly and lets the milestones speak.
 *
 * Only the dates the service sends are shown. There is deliberately no "handed over
 * by" line — the Client's summary does not carry a staff id, and this page could not
 * render one if it wanted to.
 */
function BuildPhaseSummary({ phase }: { phase: ConstructionPhaseSummary }) {
  const handedOver = phase.status === 'HandedOver'

  return (
    <div
      data-testid="build-phase"
      className={
        handedOver
          ? 'flex flex-col gap-1 rounded-md border p-3'
          : 'flex flex-col gap-1 text-muted-foreground text-xs'
      }
    >
      <span className="flex flex-wrap items-center gap-2">
        <Badge variant={handedOver ? 'default' : 'secondary'}>
          {CONSTRUCTION_PHASE_STATUS_LABELS[phase.status]}
        </Badge>
        {handedOver && phase.handedOverAtUtc && (
          <span className="text-sm font-medium">
            Handed over to you on {formatTime(phase.handedOverAtUtc)}
          </span>
        )}
      </span>

      <span className="text-muted-foreground text-xs">
        Construction started {formatTime(phase.startedAtUtc)}
        {phase.completedAtUtc && ` · completed ${formatTime(phase.completedAtUtc)}`}
      </span>
    </div>
  )
}

function ProjectProgressCard({ entry }: { entry: ProjectProgressEntry }) {
  const { project, summary, error } = entry

  return (
    <Card data-testid={`progress-${project.id}`}>
      <CardHeader>
        <CardTitle className="flex flex-wrap items-center justify-between gap-2">
          <Link to={`/projects/${project.id}`} className="underline underline-offset-4">
            {project.name}
          </Link>
          <Badge variant="secondary">{PROJECT_STATUS_LABELS[project.status]}</Badge>
        </CardTitle>
        <CardDescription>{project.location}</CardDescription>
      </CardHeader>

      <CardContent className="flex flex-col gap-4">
        {error && (
          <p role="alert" className="text-destructive text-sm">
            {error}
          </p>
        )}

        {!error && summary === null && (
          <p className="text-muted-foreground text-sm">
            Construction has not been planned yet. The milestones appear here once your project
            manager has set them out.
          </p>
        )}

        {summary?.phase && <BuildPhaseSummary phase={summary.phase} />}

        {summary && (
          <>
            {/* The rollup. tabular-nums so the two decimals do not shift the
                layout as the value moves under the refresh. */}
            <div className="flex flex-col gap-2">
              <div className="flex items-baseline justify-between gap-2">
                <span className="text-2xl font-semibold tabular-nums">
                  {summary.progressPercent.toFixed(2)}%
                </span>
                <span className="text-muted-foreground text-xs">
                  {summary.completedMilestones} of {summary.totalMilestones} milestones complete
                </span>
              </div>
              <div
                role="progressbar"
                aria-valuenow={Math.round(summary.progressPercent)}
                aria-valuemin={0}
                aria-valuemax={100}
                aria-label={`${project.name} construction progress`}
                className="bg-muted h-2 w-full overflow-hidden rounded-full"
              >
                <div
                  className="bg-primary h-full transition-[width] duration-500 ease-out"
                  style={{ width: `${summary.progressPercent}%` }}
                />
              </div>
            </div>

            {summary.milestones.length === 0 ? (
              <p className="text-muted-foreground text-sm">
                Your project manager has not set out the milestones yet.
              </p>
            ) : (
              <ul className="flex flex-col gap-2">
                {summary.milestones.map((milestone) => (
                  <li
                    key={milestone.id}
                    className="flex flex-wrap items-center justify-between gap-2 border-b pb-2 last:border-b-0 last:pb-0"
                  >
                    <span className="text-sm font-medium">{milestone.name}</span>
                    <span className="flex items-center gap-2">
                      <span className="text-muted-foreground text-xs">
                        Updated {formatTime(milestone.updatedAtUtc)}
                      </span>
                      <Badge variant={MILESTONE_BADGE_VARIANT[milestone.status]}>
                        {MILESTONE_STATUS_LABELS[milestone.status]}
                      </Badge>
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </>
        )}
      </CardContent>
    </Card>
  )
}

/**
 * The Client's construction dashboard (US-13): how each of their projects is
 * advancing, with per-milestone status and an overall progress bar.
 *
 * Client-only, matching the Construction Service's own gate — and the service
 * additionally refuses a project the caller does not own, so a Client cannot
 * reach another's build by editing a URL.
 *
 * The data re-reads itself (AC-2): once when the page opens, again whenever the
 * tab becomes visible, and on a timer while it is on screen. Nothing on this
 * page asks the Client to refresh, and there is no refresh control to press —
 * see {@link useAutoRefresh} for why it polls rather than pushing.
 */
export function ConstructionProgressPage() {
  const { authFetch } = useAuth()

  const [entries, setEntries] = useState<ProjectProgressEntry[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)

  /**
   * One full read: the Client's projects, then the build behind each one that
   * has reached a status where construction exists.
   *
   * Written to replace the rendered state wholesale rather than merge into it,
   * so a milestone the PM renamed or removed cannot linger. The projects are
   * re-read too, not just the progress: a project that reaches
   * `DesignApproved` between polls has to appear on its own.
   */
  const load = useCallback(async () => {
    try {
      const projects = await fetchProjects(authFetch)
      const buildable = projects.filter((project) => BUILDABLE_STATUSES.includes(project.status))

      // Concurrent, and each failure is contained to its own card: one project
      // the service is unhappy about must not blank the rest of the dashboard.
      const loaded = await Promise.all(
        buildable.map(async (project): Promise<ProjectProgressEntry> => {
          try {
            return { project, summary: await fetchProjectProgressSummary(authFetch, project.id), error: null }
          } catch (error) {
            if (error instanceof ApiError && error.status === 404) {
              // No construction plan yet — the design was approved but the
              // DesignApproved event has not been consumed. A real state.
              return { project, summary: null, error: null }
            }

            return {
              project,
              summary: null,
              error: apiErrorMessage(error, 'Could not load progress for this project.'),
            }
          }
        }),
      )

      setEntries(loaded)
      setLoadError(null)
    } catch (error) {
      // The projects call itself failed, so there is nothing to show. Any
      // entries already on screen are left alone: stale progress with an error
      // beside it beats blanking a dashboard the Client was reading because one
      // poll did not land.
      setLoadError(apiErrorMessage(error, 'Could not load your projects. Please try again.'))
    }
  }, [authFetch])

  useEffect(() => {
    void load()
  }, [load])

  // AC-2: the Client never has to refresh to see the latest status.
  useAutoRefresh(useCallback(() => void load(), [load]))

  return (
    <main className="mx-auto flex min-h-svh w-full max-w-3xl flex-col gap-4 p-6">
      <Card>
        <CardHeader>
          <CardTitle>Construction progress</CardTitle>
          <CardDescription>
            How your projects are advancing. This updates on its own as your project manager moves
            each milestone — there is nothing to refresh.
          </CardDescription>
        </CardHeader>

        <CardContent className="flex flex-col gap-4">
          {loadError && (
            <p role="alert" className="text-destructive text-sm">
              {loadError}
            </p>
          )}

          {!loadError && !entries && (
            <p className="text-muted-foreground text-sm">Loading your projects…</p>
          )}

          {entries && entries.length === 0 && (
            <p className="text-muted-foreground text-sm">
              None of your projects have reached construction yet. Progress appears here once a
              project's design has been approved.
            </p>
          )}

          <p className="text-muted-foreground text-center text-sm">
            <Link to="/projects" className="text-foreground underline underline-offset-4">
              All your projects
            </Link>
          </p>
        </CardContent>
      </Card>

      {entries?.map((entry) => <ProjectProgressCard key={entry.project.id} entry={entry} />)}
    </main>
  )
}
