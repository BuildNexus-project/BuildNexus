import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'

import { useAuth } from '@/auth/auth-context'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldLabel } from '@/components/ui/field'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { Separator } from '@/components/ui/separator'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { ApiError, apiErrorMessage } from '@/lib/api'
import { fetchAllUsers, type AdminUserSummary } from '@/lib/auth-api'
import {
  assignArchitect,
  assignProjectManager,
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

/** Shown when the list of assignable staff cannot be read. */
const STAFF_LOAD_FAILED = 'Could not load the list of staff to assign.'

/**
 * The message for a failed assignment. A 400 from the service names the field
 * that was wrong — an unknown id, or an account that is not the right role —
 * and that specific message beats "one or more validation errors occurred".
 */
function describeAssignFailure(error: unknown, fallback: string): string {
  if (error instanceof ApiError) {
    const fieldMessages = Object.values(error.fieldErrors).flat()

    if (fieldMessages.length > 0) {
      return fieldMessages.join(' ')
    }
  }

  return apiErrorMessage(error, fallback)
}

/**
 * One assignment control: a dropdown of the staff who may fill a slot, and a
 * button to commit the choice. Only ever rendered for an Admin.
 */
function AssignRow({
  label,
  placeholder,
  staff,
  value,
  onValueChange,
  onAssign,
  busy,
}: {
  label: string
  placeholder: string
  /** `null` while the list is still loading. */
  staff: AdminUserSummary[] | null
  value: string | null
  onValueChange: (value: string | null) => void
  onAssign: () => void
  busy: boolean
}) {
  const nameFor = (id: string | null) =>
    staff?.find((person) => person.id === id)?.fullName ?? placeholder

  return (
    <div className="flex flex-col gap-2 sm:flex-row sm:items-end">
      <Field className="flex-1">
        <FieldLabel htmlFor={label}>{label}</FieldLabel>
        <Select value={value} onValueChange={onValueChange} disabled={staff === null || busy}>
          <SelectTrigger id={label} className="w-full">
            <SelectValue placeholder={placeholder}>{(id: string | null) => nameFor(id)}</SelectValue>
          </SelectTrigger>
          <SelectContent>
            {(staff ?? []).map((person) => (
              <SelectItem key={person.id} value={person.id}>
                {person.fullName}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </Field>

      <Button onClick={onAssign} disabled={!value || busy}>
        {busy ? 'Assigning…' : 'Assign'}
      </Button>
    </div>
  )
}

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

  const [architects, setArchitects] = useState<AdminUserSummary[] | null>(null)
  const [projectManagers, setProjectManagers] = useState<AdminUserSummary[] | null>(null)
  const [architectChoice, setArchitectChoice] = useState<string | null>(null)
  const [pmChoice, setPmChoice] = useState<string | null>(null)
  const [assigning, setAssigning] = useState<'architect' | 'projectManager' | null>(null)
  const [assignError, setAssignError] = useState<string | null>(null)

  // The events view is the integration answering for itself, and the service
  // refuses it to everybody else with a 403. Asking anyway would show every
  // Client an error for something that is not theirs to see. Staff assignment
  // is Admin-only for the same reason.
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
   * Loads the staff an Admin can assign — active Architects and Project
   * Managers only. The role filter is the service's (`GET /api/users?role=`),
   * so the dropdowns cannot offer somebody the assign endpoint would refuse;
   * deactivated accounts are dropped here.
   */
  useEffect(() => {
    if (!isAdmin) {
      return
    }

    let cancelled = false

    Promise.all([
      fetchAllUsers(authFetch, { role: 'Architect', pageSize: 100 }),
      fetchAllUsers(authFetch, { role: 'ProjectManager', pageSize: 100 }),
    ])
      .then(([architectPage, projectManagerPage]) => {
        if (!cancelled) {
          setArchitects(architectPage.items.filter((person) => person.isActive))
          setProjectManagers(projectManagerPage.items.filter((person) => person.isActive))
        }
      })
      .catch((error: unknown) => {
        if (!cancelled) {
          setAssignError(apiErrorMessage(error, STAFF_LOAD_FAILED))
        }
      })

    return () => {
      cancelled = true
    }
  }, [authFetch, isAdmin])

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

  /**
   * Puts the chosen person in a slot. The service answers with the project as
   * it now stands — an Architect assignment on a Pending project comes back
   * Designing, history included — so the page updates from the reply.
   */
  async function assign(slot: 'architect' | 'projectManager') {
    if (!projectId) {
      return
    }

    const staffId = slot === 'architect' ? architectChoice : pmChoice

    if (!staffId) {
      return
    }

    setAssignError(null)
    setAssigning(slot)

    try {
      const updated =
        slot === 'architect'
          ? await assignArchitect(authFetch, projectId, staffId)
          : await assignProjectManager(authFetch, projectId, staffId)

      setProject(updated)

      if (slot === 'architect') {
        setArchitectChoice(null)
        // Assigning an Architect to a Pending project moves it to Designing,
        // which raises an event — so the log below is out of date.
        if (isAdmin) {
          await refreshEvents(projectId)
        }
      } else {
        setPmChoice(null)
      }
    } catch (error) {
      setAssignError(
        describeAssignFailure(error, 'Could not assign this person. Please try again.'),
      )
    } finally {
      setAssigning(null)
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

            {isAdmin && (
              <div className="flex flex-col gap-4">
                <AssignRow
                  label="Assign architect"
                  placeholder={architects === null ? 'Loading…' : 'Choose an architect'}
                  staff={architects}
                  value={architectChoice}
                  onValueChange={setArchitectChoice}
                  onAssign={() => assign('architect')}
                  busy={assigning === 'architect'}
                />
                <AssignRow
                  label="Assign project manager"
                  placeholder={projectManagers === null ? 'Loading…' : 'Choose a project manager'}
                  staff={projectManagers}
                  value={pmChoice}
                  onValueChange={setPmChoice}
                  onAssign={() => assign('projectManager')}
                  busy={assigning === 'projectManager'}
                />

                <p className="text-muted-foreground text-sm">
                  Assigning an architect while this project is still Pending moves it to Designing.
                </p>

                {assignError && (
                  <p role="alert" className="text-destructive text-sm">
                    {assignError}
                  </p>
                )}
              </div>
            )}
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
