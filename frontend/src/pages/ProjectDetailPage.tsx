import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'

import { useAuth } from '@/auth/auth-context'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Separator } from '@/components/ui/separator'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { apiErrorMessage } from '@/lib/api'
import {
  fetchProject,
  fetchProjectEvents,
  updateProjectStatus,
  type ProjectDetail,
  type ProjectEvent,
  type ProjectStatusChange,
} from '@/lib/project-api'
import { deliveryOf, needsAttention } from '@/lib/project-events'
import { PROJECT_STATUS_LABELS, type ProjectStatus } from '@/lib/project-status'
import { ADMIN_ROLES, ROLE_LABELS, STATUS_CHANGE_ROLES } from '@/lib/roles'

/** A date and time the service sent, as a reader would write it. */
function formatMoment(iso: string): string {
  return new Date(iso).toLocaleString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  })
}

/** Grouped digits with the currency named, rather than a bare number. */
function formatMoney(amount: number): string {
  return `LKR ${new Intl.NumberFormat(undefined, { maximumFractionDigits: 2 }).format(amount)}`
}

/**
 * How one history entry reads. The opening entry has no previous status — the
 * project did not come from anywhere, it was created.
 */
function describeChange(change: ProjectStatusChange): string {
  const to = PROJECT_STATUS_LABELS[change.toStatus]

  return change.fromStatus === null
    ? `Created as ${to}`
    : `${PROJECT_STATUS_LABELS[change.fromStatus]} → ${to}`
}

/** Shown when the delivery log itself cannot be read. */
const EVENTS_LOAD_FAILED = 'Could not load this project’s events.'

/**
 * How one event's delivery reads.
 *
 * A delivered event that took several attempts is shown as delivered, with the
 * count in the small print: that is the outbox having worked, and badging it as
 * a problem would teach people to ignore the badge.
 */
function Delivery({ event }: { event: ProjectEvent }) {
  const delivery = deliveryOf(event)

  if (delivery === 'delivered') {
    return (
      <div className="flex flex-col gap-1">
        <Badge variant="secondary" className="w-fit">
          Delivered
        </Badge>
        <span className="text-muted-foreground text-xs">
          {formatMoment(event.publishedAt as string)}
          {event.attemptCount > 1 && ` · after ${event.attemptCount} attempts`}
        </span>
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-1">
      <Badge variant={delivery === 'retrying' ? 'destructive' : 'outline'} className="w-fit">
        {delivery === 'retrying' ? 'Not delivered' : 'Waiting'}
      </Badge>
      <span className="text-muted-foreground text-xs">
        {delivery === 'retrying'
          ? `${event.attemptCount} ${event.attemptCount === 1 ? 'attempt' : 'attempts'}${
              event.lastError ? ` · ${event.lastError}` : ''
            }`
          : 'Not yet sent'}
      </span>
    </div>
  )
}

/** One labelled fact about the project. */
function Detail({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex flex-col gap-1">
      <span className="text-muted-foreground text-xs uppercase tracking-wide">{label}</span>
      <span className="text-sm">{children}</span>
    </div>
  )
}

/**
 * One project in full, with its status history, and the controls to move it
 * along (US-06).
 *
 * Open to every role: which projects a caller may actually open is a
 * per-project question the service answers — the owning client, the assigned
 * staff, or an Admin — and a role alone cannot decide it. Somebody who is not
 * on the project gets a 403 with a reason, which is what is shown here.
 */
export function ProjectDetailPage() {
  const { projectId } = useParams<{ projectId: string }>()
  const { authFetch, user } = useAuth()

  const [project, setProject] = useState<ProjectDetail | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [statusError, setStatusError] = useState<string | null>(null)
  const [changingTo, setChangingTo] = useState<ProjectStatus | null>(null)
  const [events, setEvents] = useState<ProjectEvent[] | null>(null)
  const [eventsError, setEventsError] = useState<string | null>(null)

  // The events view is the integration answering for itself, and the service
  // refuses it to everybody else with a 403. Asking anyway would show every
  // Client an error for something that is not theirs to see.
  const isAdmin = user !== null && ADMIN_ROLES.includes(user.role)

  useEffect(() => {
    if (!projectId) {
      return
    }

    let cancelled = false

    fetchProject(authFetch, projectId)
      .then((loaded) => {
        if (!cancelled) {
          setProject(loaded)
        }
      })
      .catch((error: unknown) => {
        if (!cancelled) {
          // A 403 or 404 is a real answer with a reason in it — show that
          // reason rather than flattening it into "something went wrong".
          setLoadError(apiErrorMessage(error, 'Could not load this project. Please try again.'))
        }
      })

    return () => {
      cancelled = true
    }
  }, [authFetch, projectId])

  /**
   * Loads the delivery log, separately from the project itself so a failure
   * here cannot take the page down with it — an administrator who cannot reach
   * the events should still see the project.
   */
  useEffect(() => {
    if (!projectId || !isAdmin) {
      return
    }

    let cancelled = false

    fetchProjectEvents(authFetch, projectId)
      .then((loaded) => {
        if (!cancelled) {
          setEvents(loaded)
          setEventsError(null)
        }
      })
      .catch((error: unknown) => {
        if (!cancelled) {
          setEventsError(apiErrorMessage(error, EVENTS_LOAD_FAILED))
        }
      })

    return () => {
      cancelled = true
    }
  }, [authFetch, isAdmin, projectId])

  /**
   * Re-reads the delivery log after something has changed it.
   *
   * Not cancellation-guarded like the load above, because this only runs from a
   * button the user just pressed on a page that is still mounted.
   */
  async function refreshEvents(id: string) {
    try {
      setEvents(await fetchProjectEvents(authFetch, id))
      setEventsError(null)
    } catch (error) {
      // The move itself succeeded; only the log failed to refresh. Saying so
      // beats leaving a stale list looking current.
      setEventsError(apiErrorMessage(error, EVENTS_LOAD_FAILED))
    }
  }

  /**
   * Moves the project on. The service answers with the project as it now
   * stands, history included, so the page updates from its reply rather than
   * refetching or guessing at the new state.
   */
  async function moveTo(status: ProjectStatus) {
    if (!projectId) {
      return
    }

    setStatusError(null)
    setChangingTo(status)

    try {
      setProject(await updateProjectStatus(authFetch, projectId, status))

      // The move just raised events of its own, so the log below is already out
      // of date. The reply to the PATCH does not carry them — they are written
      // by the same transaction but sent afterwards — so it is re-read.
      if (isAdmin) {
        await refreshEvents(projectId)
      }
    } catch (error) {
      setStatusError(
        apiErrorMessage(error, 'Could not update the status. Please try again.'),
      )
    } finally {
      setChangingTo(null)
    }
  }

  if (loadError) {
    return (
      <main className="mx-auto flex min-h-svh w-full max-w-2xl flex-col justify-center gap-4 p-6">
        <Card>
          <CardHeader>
            <CardTitle>This project is not available to you</CardTitle>
            <CardDescription role="alert">{loadError}</CardDescription>
          </CardHeader>

          <CardContent>
            <Button render={<Link to="/projects" />} variant="outline" className="w-full">
              Back to projects
            </Button>
          </CardContent>
        </Card>
      </main>
    )
  }

  if (!project) {
    return (
      <main className="mx-auto flex min-h-svh w-full max-w-2xl flex-col justify-center gap-4 p-6">
        <p className="text-muted-foreground text-sm">Loading this project…</p>
      </main>
    )
  }

  // The service decides what the project may move to; this only decides whether
  // to offer it. Staff who are not on this project still get a 403 when they
  // try, which is the answer that counts.
  const canChangeStatus = user !== null && STATUS_CHANGE_ROLES.includes(user.role)
  const nextStatuses = canChangeStatus ? project.allowedNextStatuses : []

  return (
    <main className="mx-auto flex min-h-svh w-full max-w-3xl flex-col justify-center gap-4 p-6">
      <Card>
        <CardHeader>
          <CardTitle className="flex flex-wrap items-center gap-3">
            {project.name}
            <Badge variant="secondary">{PROJECT_STATUS_LABELS[project.status]}</Badge>
          </CardTitle>
          <CardDescription>
            {project.location} · submitted {formatDate(project.createdAt)}
          </CardDescription>
        </CardHeader>

        <CardContent className="flex flex-col gap-6">
          <section className="flex flex-col gap-3">
            <h2 className="text-sm font-medium">Requirements</h2>

            <div className="grid gap-4 sm:grid-cols-3">
              <Detail label="Land size">{project.landSizePerches} perches</Detail>
              <Detail label="Budget">{formatMoney(project.budget)}</Detail>
              <Detail label="Floors">{project.floors}</Detail>
              <Detail label="Bedrooms">{project.bedrooms}</Detail>
              <Detail label="Bathrooms">{project.bathrooms}</Detail>
              <Detail label="Garage spaces">{project.garageSpaces}</Detail>
            </div>

            <Detail label="Other requirements">
              {project.otherRequirements ?? (
                <span className="text-muted-foreground">Nothing further was asked for.</span>
              )}
            </Detail>
          </section>

          <Separator />

          <section className="flex flex-col gap-3">
            <h2 className="text-sm font-medium">Team</h2>

            {/* Ids rather than names: the accounts live in the User Service's
                own database, and the Project Service holds no copy of them. */}
            <div className="grid gap-4 sm:grid-cols-2">
              <Detail label="Architect">
                {project.assignedArchitectId ?? (
                  <span className="text-muted-foreground">Not yet assigned</span>
                )}
              </Detail>
              <Detail label="Project manager">
                {project.assignedProjectManagerId ?? (
                  <span className="text-muted-foreground">Not yet assigned</span>
                )}
              </Detail>
            </div>
          </section>

          <Separator />

          <section className="flex flex-col gap-3">
            <h2 className="text-sm font-medium">Status history</h2>

            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Change</TableHead>
                  <TableHead>By</TableHead>
                  <TableHead>When</TableHead>
                </TableRow>
              </TableHeader>

              <TableBody>
                {/* Oldest first, exactly as the service sent it — nothing here
                    re-sorts, so the page cannot disagree with the record. */}
                {project.statusHistory.map((change) => (
                  <TableRow key={change.id}>
                    <TableCell className="font-medium">{describeChange(change)}</TableCell>
                    <TableCell>
                      <span className="block">{ROLE_LABELS[change.changedByRole]}</span>
                      <span className="text-muted-foreground block text-xs">
                        {change.changedByUserId}
                      </span>
                    </TableCell>
                    <TableCell>{formatMoment(change.changedAt)}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </section>

          {canChangeStatus && (
            <>
              <Separator />

              <section className="flex flex-col gap-3">
                <h2 className="text-sm font-medium">Move this project on</h2>

                {nextStatuses.length === 0 ? (
                  <p className="text-muted-foreground text-sm">
                    This project is {PROJECT_STATUS_LABELS[project.status]} and cannot move any
                    further.
                  </p>
                ) : (
                  <div className="flex flex-wrap gap-3">
                    {nextStatuses.map((status) => (
                      <Button
                        key={status}
                        onClick={() => moveTo(status)}
                        disabled={changingTo !== null}
                      >
                        {changingTo === status
                          ? 'Updating…'
                          : `Move to ${PROJECT_STATUS_LABELS[status]}`}
                      </Button>
                    ))}
                  </div>
                )}

                {statusError && (
                  <p role="alert" className="text-destructive text-sm">
                    {statusError}
                  </p>
                )}
              </section>
            </>
          )}

          {isAdmin && (
            <>
              <Separator />

              <section className="flex flex-col gap-3">
                <div className="flex flex-wrap items-center gap-3">
                  <h2 className="text-sm font-medium">Integration events</h2>
                  {events !== null && needsAttention(events) && (
                    <Badge variant="destructive">Needs attention</Badge>
                  )}
                </div>

                <p className="text-muted-foreground text-sm">
                  What this project announced to the other services. An event is recorded in the
                  same transaction as the change it describes and sent afterwards, so one that has
                  not gone yet is delayed rather than lost.
                </p>

                {eventsError ? (
                  <p role="alert" className="text-destructive text-sm">
                    {eventsError}
                  </p>
                ) : events === null ? (
                  <p className="text-muted-foreground text-sm">Loading events…</p>
                ) : events.length === 0 ? (
                  <p className="text-muted-foreground text-sm">
                    This project has not announced anything yet.
                  </p>
                ) : (
                  <Table>
                    <TableHeader>
                      <TableRow>
                        <TableHead>Event</TableHead>
                        <TableHead>Raised</TableHead>
                        <TableHead>Delivery</TableHead>
                      </TableRow>
                    </TableHeader>

                    <TableBody>
                      {/* Oldest first, exactly as the service sent it — which is
                          also the order the events go onto the topic. */}
                      {events.map((event) => (
                        <TableRow key={event.id}>
                          <TableCell>
                            {/* The wire value, not a friendly name: it is what a
                                consumer subscribes to, so it is what an
                                administrator needs to match against. */}
                            <span className="font-mono text-xs font-medium">{event.eventType}</span>
                            <span className="text-muted-foreground block text-xs">{event.id}</span>
                          </TableCell>
                          <TableCell>{formatMoment(event.occurredAt)}</TableCell>
                          <TableCell>
                            <Delivery event={event} />
                          </TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                )}
              </section>
            </>
          )}

          <Button render={<Link to={`/projects/${project.id}/designs`} />} variant="outline" className="w-full">
            Design documents
          </Button>

          <p className="text-muted-foreground text-center text-sm">
            <Link to="/projects" className="text-foreground underline underline-offset-4">
              Back to projects
            </Link>
          </p>
        </CardContent>
      </Card>
    </main>
  )
}
